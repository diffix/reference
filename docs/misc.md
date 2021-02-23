# Initial design notes of tight DB integration

This document contains portions of the initial design notes from [issue #15](https://github.com/diffix/reference/issues/15).

Other portions were moved to [led.md](./led.md) and [multiple-aid.md](./multiple-aid.md).

These notes will be moved to individual documents as we go.

----------
# Histograms

For histograms (i.e. bucket functions) I think we should still enforce snapping like we currently do, with the exception that we can allow finer granularity and more options (powers of 2, money style, etc). This way we can avoid the inaccuracies inherent in the solution for inequalities given above.

----------
# Inequalities

I believe we can do away with the range and snapping requirements, at least from the point of view of the analyst. Under the hood we'll still have snapping. For the sake of the following discussion, let's assume that we snap ranges and offsets by powers of 2 (1/4, 1/2, 1, 2, 4...), and that we allow shifting of an offset by 1/2 or the range. Note that the choice of snapping increments is hidden from the analyst, so these don't have to be user friendly per se.

Basic idea is this. When an inequality is specified, we want to find a snapped range inside the inequality that includes a noisy-threshold number of distinct users (say between 2 and 4 distinct users). The box can align with the edge of the inequality (i.e. the box for `col <= 10` can have 10 as it's right edge). The reason the box is inside the inequality is because the rows inside the inequality will be included in the query execution, and therefore are more likely to be accessible to our logic.

The drawing here illustrates that. Here the analyst has specified a `<=` condition (the red line, which we'll call the edge). The x's represent the data points, and for the sake of simplicity we'll assume one data point per distinct AID. The drawing shows two scenarios, one where the data points are relatively more densely populated, and one where they are relatively more sparsely populated.

![](https://paper-attachments.dropbox.com/s_832D3C952962442CDA33E4F625E4143AA62099E867229D6068A42F99D3011C9E_1599545192241_image.png)


The blue boxes represent snapped ranges within the edge. The box for the dense scenario has 5 distinct AIDs, and the box for the sparse scenario has 3 distinct AIDs. Data points inside the boxes are included in the answer, but data points between the boxes and the edge are excluded (marked in red). The noise layers would be seeded by the size and position of the box.

A critical point is that if the edge is moved left or right slightly, this would not change the chosen boxes. This makes it hard to do an averaging attack on the condition's noise layer.

As the edge is moved further, it would get to the point where a new box would be chosen, possibly bigger or smaller than the previous box, probably but not necessarily including or excluding different AIDs, but in any event the choice of the new box would not be triggered by crossing a given data point, but rather by moving far enough that a new box is a better fit. This is not to say that the box isn't data driven, it is. However, typically the choice of box is based on the contributions of multiple AIDs, not a single AID.

If the edge matches a snapped value, then it may well be the case that no data points are excluded. If the analyst specifies a range, and the range itself matches a snapped range that is allowed for histograms, then we can use the traditional seeding approach which would in turn match the seeds of histograms.

For instance, if the range is `WHERE age BETWEEN 10 and 20`, then we can seed the same as a corresponding bucket generated with `SELECT bucket(age BY 10)`.

There are still some details to be worked out regarding which box to use when there are several choices. Probably we want to minimize the number of dropped data points. This in turn can (normally) best be done when the box itself is small, because this allows for finer-grained offsets. 

Sebastian mentions this case:


    SELECT age, count(*)
    FROM users
    WHERE age > 10

which could in principle display a value for `age = 10`. Sebastian points out that this is probably a non-issue since the `age=10` bucket would almost certainly be suppressed. Nevertheless we might need an explicit check to prevent it.

For instance we could perhaps ensure the boxes are always strictly greater or equal to the range. I.e. `age > 10` means the lower bound of the range has to be greater than 10 too... In that case the value for `age = 10` would not be part of the result set at all. If the dataset allows it, we might get values from 11 onwards (assuming the range `[11,11]` passes the low count filter).

In the case where there are no values to the right (left) of a less-than (greater-than) condition, we don't need the box to encompass the value. Rather we can treat it as though the analyst edge were identical to the right-most (left-most) data point. This prevents an averaging attack where the analyst just selects edges further and further to the right (left) of the max (min) value.

For instance, suppose the condition is `WHERE age < 1001`. Suppose the highest age in the system is 102. Then the bounding box might well be `[98,100]` (if there are enough users in that box).

No doubt other missing details here, but this is the basic idea and it looks solid to me.

> TODO: Need to code a simulation of this approach and try a variety of attacks

## Unclean inequalities

It occurs to me that the above discussion implicitly assumes that inequalities are clean (i.e. we know the value of the edge. If not clean, though, this begs the question of how the DB knows what to access from an index. They must have some way of knowing what in the index is included and excluded, and if they know this, then we could leverage it to allow unclean inequalities.

For instance, if the condition is `WHERE sqrt(age) < 5`, we would somehow know that this really means `age < 25` (based on knowledge we borrow from Postgres) and proceed accordingly.

> TODO: We need to think about cases where changing an edge from one query to the next can isolate a single user. To the extent that such cases exist, we may need to detect them.

## Time-based attacks ('now()' as inequality)

One of the attacks that we've never addressed is that of repeating a query and detecting the difference between the two queries. If it is somehow known that only one user changed in the query context between the two queries then this can be detected.

If the DB records a timestamp for when rows are added, we can leverage this by implicitly treating each query as having `WHERE ts <= now()` attached to it, and treating this inequality as described above.

From Sebastian:

This is mostly a problem for datasets that continuously evolve. In those instances, we need some way of knowing when a row was inserted or changed. A very hairy solution would be to implement our own "index type" (or other ability to record metadata) which is such that inserts/writes to the database triggers an update. This way we can record metadata when rows are inserted/changed and use that information as a way of determining what data to include and what to exclude.

For datasets that have a known update frequency, we could do something akin to what I described in [this issue](https://github.com/diffix/strategy/issues/7):

Namely, we could:
- have a "dataset version id" and an accompanying noise layer. Hence each new version of a dataset (whether or not the data has changed), will produce a different result
- use a noise layer based on a pre-determined update frequency. I.e. if the dataset is updating once per month, then we could add a noise layer seeded by `bucket(current time by update frequency)`. Of course this value must align with the update frequency! Otherwise, you would get a new noise value both when data has changed and when the update interval changes...


----------
# LIKE regex

I'm cautiously optimistic that we'll be able to remove all of our LIKE restrictions. There are two issues to consider, 1) using wildcards to launch a difference attack, and 2) using wildcards to average away noise (de-noise). Both issues require that we gather certain information *during regex processing*. My uncertainty comes from the fact that I don't understand how regex processing happens in any detail.

The difference attack exploits cases where a victim can be included with or excluded from a set of other AIDs with wildcards. An old example of this is where there are many 'paul' but only one 'paula'. Then `LIKE 'paul'` and `LIKE 'paul_'` differ by one user.

The de-noise attack of course depends on how we make noise, but if we assume that we seed noise based on column values, then we have the following attack. Suppose that the attacker wants to de-noise one noise layer for the condition where all rows match `LIKE '%LIDL%'`. Suppose the column values have a variety of strings before and after 'LIDL'. The attacker could then do the [split averaging attack](https://demo.aircloak.com/docs/attacks.html#split-averaging-attack), which generates pairs of queries where each pair when, summed together, has all rows with `%LIDL%`. Since each answer will have different rows, then the static noise for each answer will differ so long as there are enough different substrings before or after 'LIDL'. 

**Difference Attack**

The basic idea behind defending against this difference attack is to observe the substrings that match against each wildcard, and to identify cases where removing the wildcard, or replacing it with a substring or another wildcard, effects only a LE number of AIDs. This represents a potential difference attack, so we silently drop the corresponding rows.

I'm assuming that if the LIKE result is true, then the portions of the column string associated with each symbol of the regex are known. What I mean by this for example is that if the regex string is '%abc_xyz_%', and the column value is 'zzabc.xyzZZ', then the regex algorithm could return the following meta-data:

| Position | Wildcard | Matching substring |
| -------- | -------- | ------------------ |
| 1        | %        | zz                 |
| 5        | _        | .                  |
| 9        | _        | Z                  |
| 10       | %        | Z                  |


After query engine processing, if there are N matching rows then for each wildcard there will be a corresponding N matching substrings.

An analyst can change any wildcard to any other wildcard or any substring (including NULL substring) in some different query. Therefore, for any given wildcard, we need to detect if changing it to another wildcard or substring would result in the rows associated with an LE number of AIDs being dropped. If yes, we drop the corresponding rows silently.

There are two cases that may result in dropping rows:

1. All of the wildcard matches are for the same substring except for a small number of substrings associated with an LE number of AIDs. In this case an attacker could replace the wildcard with that substring, and the LE rows would be excluded. This can be the case for both '%' and '_' wildcards.
2. All of the wildcard matches are for some length substring N1 or longer except for a small number of shorter substrings of length N associated with an LE number of AIDs. In this case an attacker could replace the N-or-more wildcard with an N1-or-more wildcard, and the LE rows would be excluded.

For example, suppose that in some query, 10 users have card_type 'gold card', and potentially 1 user has card_type 'platinum card'. A analyst wants to attack the platinum card holder using a pair of queries using `WHERE card_type LIKE '%card'` and `WHERE card_type LIKE '%d card'`. The wild card of the first query would have the substring 'gold ' associated with 10 AIDs, and the substring 'platinum ' associated with a single AID. These latter rows are LE and would be silently dropped. As a result, the two queries would have the same underlying count whether or not the victim was included in the first query.

As a second example, suppose that in some query, there are 10 'bronze card' holders, 10 'silver card' holders, 10 'platinum card' holder, and potentially 1 'gold card' holder. The attacker makes two queries, one with `WHERE card_type LIKE '%____ card'%` and `WHERE card_type LIKE '%----- card'`. The first condition includes the single gold card holding victim, and the second condition excludes the victim. The following table shows the metadata for each symbol:

| position | wildcard | matching substring 1st query            |
| -------- | -------- | --------------------------------------- |
| 1        | %        | 'br' 10x, 'si' 10x, 'plat' 10x, null 1x |
| 2        | _        | 'o' 10x, 'l' 10x, 'i' 10x               |
| 3        | _        | 'n' 10x, 'v' 10x, 'n' 10x               |
| 4        | -        | 'z' 10x, 'e' 10x, 'u' 10x               |
| 5        | -        | 'e' 10x, 'r' 10x, 'm' 10x               |

We see that none of the '_' wildcards lead to any dropped rows, because there are no LE characters. By contrast, the '%' wildcard has 30 substrings with length 2 or more, and only one substring with a smaller length (zero). We would therefore drop the 'gold card' rows.

**De-noise attack**
If a LIKE condition is `'%LIDL%'`, then one approach to defending against the split-averaging version of the de-noise attack would be to seed the noise layer with the substring 'LIDL' (in addition to the seed for the complete string). A strawman design would be to add a noise layer for every string constant in the regex expression. For instance, the expression '%foo%bar' would have a noise layer for 'foo' and another noise layer for 'bar'.

The problem here is that it leads to another form of de-noise attack, where low-effect (or zero-effect) wildcards are used to generate different noise layers. So for instance, `'%L_DL%'`, `'%IDL%'`, `'%LI_L%'`, and so on. As long as the same rows are included, the analyst can get different noise layers.

The solution approach would be to recognize when wildcards can be replaced with substring constants, and then do so before determining the seed for the noise layers. In essence, we identify LE wildcard conditions using the mechanism above, and if after dropping rows we find that the wildcard can be replaced with a substring constant (including zero-length substring), then we do so for the purpose of determining the seed material.

Lot's of details to be worked out, but this is the basic idea.



# Attacks on JOINs
## JOIN with non-personal table

From https://github.com/Aircloak/aircloak/issues/898 we had an attack on joining with non-personal tables, whose fix led to a lot of extra configuration (all of the valid JOIN conditions had to be explicitly configured). Would be very nice to avoid this.

The attack involved a series of queries like this:


    SELECT ... FROM users WHERE age = 1
    SELECT ... JOIN ... ON users.age = safe_table.id WHERE safe_table.value = 'carrot'
    SELECT ... JOIN ... ON users.age = safe_table2.id WHERE safe_table2.value = 'orange'
    SELECT ... JOIN ... ON users.age = safe_table3.id WHERE safe_table3.value = 'salami'
    etc.

which were semantically equivalent but led to different noise samples. 

One solution that comes immediately to mind is to have a new noise layer which is seeded solely by the set of distinct UIDs (no layers). Even where every other noise layer changes, this one noise layer, in an attack like the above, would stay the same. (Note that for Diffix Level 1, we need this noise layer anyway.)

Another solution would be to have two noise layers associated with any condition that has two columns involved. So in the above example, the condition `users.age = safe_table.id` would have two noise layers, one seeded with `users.age` and one seeded with `safe_table.id`. Ultimately the only value of that condition would be `1`, so the layer associated with `users.age` would prevent new samples. This feels to me like a cleaner solution.


## Full OUTER JOINs

We've always disallowed full OUTER JOIN in essence because the joined table has two AID columns and we really didn't know how to deal with it. We could end up with a single AID passing LCF because it is joined with all the other AIDs.

In principle we could fix this with support for multiple AIDs. We would have to recognize that each row has multiple AIDs associated with it because of the JOIN, and act accordingly. I'm not sure to what extent we need full OUTER JOIN, so we may not need to do anything other than continue to restrict it.

Sebastian mentions this case from https://github.com/Aircloak/aircloak/issues/1240:

```sql
SELECT count(*) FROM (
   SELECT distinct uid FROM t WHERE beverage = 'tea'
) tea FULL OUTER JOIN (
   SELECT distinct uid FROM t WHERE beverage = 'coffee'
) coffee ON tea.uid = coffee.uid
```

as being equivalent to counting people who have had tea OR coffee.

> TODO: Look more at full `OUTER JOIN` and see what we need to do to apply LED to it (just as we would for an OR statement).


# TODOs

Look into whether there is leakage from EXPLAIN.

From Sebastian:

There is likely to be leakage through EXPLAIN. The EXPLAIN results have cost estimates. The cost estimates are based on the number of rows a query processes, or how many rows a condition is likely to exclude etc.

Consider the case of an extreme outlier AID that represents 50% of all rows. This would have a very noticeable impact on the cost estimates that are returned, particularly compared with the actual values we return post anonymization

