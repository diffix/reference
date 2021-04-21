# Initial design notes of tight DB integration

This document contains portions of the initial design notes from [issue #15](https://github.com/diffix/reference/issues/15).

Other portions were moved to [led.md](./led.md), [multiple-aid.md](./multiple-aid.md), and [inequalities.md](./inequalities.md).

These notes will be moved to individual documents as we go.


----------
# LIKE regex

I'm cautiously optimistic that we'll be able to remove all of our LIKE restrictions. There are two issues to consider, 1) using wildcards to launch a difference attack, and 2) using wildcards to average away noise (de-noise). Both issues require that we gather certain information *during regex processing*. My uncertainty comes from the fact that I don't understand how regex processing happens in any detail.

The difference attack exploits cases where a victim can be included with or excluded from a set of other AID values with wildcards. An old example of this is where there are many 'paul' but only one 'paula'. Then `LIKE 'paul'` and `LIKE 'paul_'` differ by one user.

The de-noise attack of course depends on how we make noise, but if we assume that we seed noise based on column values, then we have the following attack. Suppose that the attacker wants to de-noise one noise layer for the condition where all rows match `LIKE '%LIDL%'`. Suppose the column values have a variety of strings before and after 'LIDL'. The attacker could then do the [split averaging attack](https://demo.aircloak.com/docs/attacks.html#split-averaging-attack), which generates pairs of queries where each pair when, summed together, has all rows with `%LIDL%`. Since each answer will have different rows, then the static noise for each answer will differ so long as there are enough different substrings before or after 'LIDL'.

**Difference Attack**

The basic idea behind defending against this difference attack is to observe the substrings that match against each wildcard, and to identify cases where removing the wildcard, or replacing it with a substring or another wildcard, effects only a LE number of AID values. This represents a potential difference attack, so we silently drop the corresponding rows.

I'm assuming that if the LIKE result is true, then the portions of the column string associated with each symbol of the regex are known. What I mean by this for example is that if the regex string is '%abc_xyz_%', and the column value is 'zzabc.xyzZZ', then the regex algorithm could return the following meta-data:

| Position | Wildcard | Matching substring |
| -------- | -------- | ------------------ |
| 1        | %        | zz                 |
| 5        | _        | .                  |
| 9        | _        | Z                  |
| 10       | %        | Z                  |


After query engine processing, if there are N matching rows then for each wildcard there will be a corresponding N matching substrings.

An analyst can change any wildcard to any other wildcard or any substring (including NULL substring) in some different query. Therefore, for any given wildcard, we need to detect if changing it to another wildcard or substring would result in the rows associated with an LE number of AID values being dropped. If yes, we drop the corresponding rows silently.

There are two cases that may result in dropping rows:

1. All of the wildcard matches are for the same substring except for a small number of substrings associated with an LE number of AID values. In this case an attacker could replace the wildcard with that substring, and the LE rows would be excluded. This can be the case for both '%' and '_' wildcards.
2. All of the wildcard matches are for some length substring N1 or longer except for a small number of shorter substrings of length N associated with an LE number of AID values. In this case an attacker could replace the N-or-more wildcard with an N1-or-more wildcard, and the LE rows would be excluded.

For example, suppose that in some query, 10 users have card_type 'gold card', and potentially 1 user has card_type 'platinum card'. A analyst wants to attack the platinum card holder using a pair of queries using `WHERE card_type LIKE '%card'` and `WHERE card_type LIKE '%d card'`. The wild card of the first query would have the substring 'gold ' associated with 10 AID values, and the substring 'platinum ' associated with a single AID value. These latter rows are LE and would be silently dropped. As a result, the two queries would have the same underlying count whether or not the victim was included in the first query.

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

We've always disallowed full OUTER JOIN in essence because the joined table has two AID columns and we really didn't know how to deal with it. We could end up with a single AID value passing LCF because it is joined with all the other AID values.

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

Consider the case of an extreme outlier AID value that represents 50% of all rows. This would have a very noticeable impact on the cost estimates that are returned, particularly compared with the actual values we return post anonymization

