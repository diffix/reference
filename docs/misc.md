# Initial design notes of tight DB integration

This document contains the initial design notes from issue #15.

These notes will be moved to individual documents as we go.


# Dynamic noise layers

In previous designs, the dynamic noise layers are based on AIDs (UIDs). The purpose of the dynamic noise layer is to defend against the first derivative difference attack. This is needed because the static noise layers constitute a predictable difference between the "left" and "right" queries in the attack pair. The dynamic layer is not predictable, because each bucket in the attack has a different set of AIDs, and therefore a different dynamic noise layer.

One tricky point about the dynamic layer is that it must not be possible to generate many different dynamic noise layers for what is semantically the same query, thus averaging out the dynamic noise. To avoid this, we use the AIDs themselves in part to seed the dynamic noise layer. Queries with the same semantics will always produce the same set of AIDs.

One of the difficulties with using AIDs with proxy Diffix is the cost of recording them. In the stats-based noise approach, we were not able to collect all of the AIDs, and so used the work-around of recording the min, max, and count. With DB Diffix, it should be reasonable to collect per-AID data, but it may be costly to recompute the set of distinct AIDs when adjusting for Low Effect (LE). 

With DB Diffix it appears that we can use the conditions themselves as the basis of dynamic noise layers instead of AIDs. There are two reasons. First, if we are doing Low Effect Detection (LED), then an attacker would not be able to use chaff conditions to average away dynamic noise layers. Second, with tight DB integration, we can do a good job of determining the semantics of each condition, and therefore prevent conditions with different syntax but same semantics.

This opens the possibility of using the seed material from the non-LE (NLE) conditions as the basis for the dynamic noise layers rather than the AIDs. Essentially we can used the combined seed material of all NLE conditions to replace the set of AIDs that we formerly used. This seed material is then added to each individual conditions seed material to compose the per-conditions dynamic noise layers (note both LE and NLE conditions still have dynamic noise layers, though the seed material itself comes only from NLE conditions).

(Note that we still need per-condition dynamic noise layers. This is because in a difference attack, the condition that isolates the victim will always end up being LE, and so is ignored for the purpose of seeding dynamic noise layers. This would lead to the dynamic noise layer being identical on the left and right.)


----------
# LED

See [led.md](./led.md).

# Computing Per-AID Contributions

With an integrated cloak, we can go back to the original style of computing noise, where we have information about individual AIDs. This is needed to compute noise about the various aggregates, including future aggregates and even user-defined aggregates. There are at least two types of aggregates:

1. Combined: An aggregate that is derived from multiple values. `sum()` is an example.
2. Individual: An aggregate that is derived from a single value. `median()` is an example.


> TODO: there may be other types. Maybe should have a look at a bunch of aggregates and see.


## Multiple AIDs

A table may have one or more AID columns (columns labeled as being AIDs). When there is more than one AID in a query (either because there are multiple AIDs in a table or tables have been joined), by default, Diffix treats them as distinct. In other words, as though they refer to different entities. In so doing, Diffix handles AIDs of different types seamlessly, and also is robust in cases where `JOIN` operations incorrectly mix AIDs together (i.e. a row for user1 is joined with a row for user2).

We use the following nomenclature. AIDX.Y refers to an AID for entity Y from column AIDX. For example, `AID1 = send_email` and `AID1.1 = sue1@gmail.com` and `AID1.2 = bob6@yahoo.com`.

When computing LCF, the number of distinct AIDs in the bucket is taken to be the minimum of all AID columns. For example, suppose the tables used in a query have two AID columns, AID1 and AI2. Suppose a bucket from that query has two distinct AIDs from AID1 (AID1.1 and AID1.2), and three distinct AIDs from AID2 (AID2.1, AID2.2, and AID2.3). Then the bucket is treated as having the minimum of these, two distinct AIDs.

When computing noise, the noise amount is computed separately for each AID column, and the largest such noise value is used. (Note that for `sum()`, for instance, the noise value is taken from the average contribution of all AIDs, or 1/2 the average of the top group. This is how AID affects noise amount.)

When computing flattening (for `sum()`), a flattening value for each distinct AID column is computed separately, and the output is adjusted (flattened) by whichever value is greater. Note that it is possible for the AID columns used for noise and for flattening to be different.

For each new aggregate function, we'll need to take into account multiple AID columns when determining how to perturb the output.

Note that if two AID columns are identical (have the same values and refer to the same entities), then even if they are treated as distinct the right thing happens from an anonymity point of view. For instance, regarding LCF, a bucket might have AID1.1, AID1.2, AID 2.1, and AID 2.2. It so happens that `AID1.1=AID2.1` and `AID1.2=AID2.2`, but the mechanism doesn't care. The number of distinct AIDs to be used for LCF is two.

The same is the case for noise or flattening. If two AID columns are identical, then the same rows will be used for the top group for both AIDs or for computing the contribution of the AIDs.

Of course, if the AID columns are identical and they are treated as distinct, then the execution is less efficient because Diffix is in essence doing the same work twice. (Optimizations that deal with this are discussed later.)

In any event, the fact that the mechanism doesn't care if two AIDs are identical or different means that in general it isn't necessary for an analyst to JOIN tables simply for the purpose of ensuring that there is an AID. It also means that we can be much more relaxed about what constitutes an AID. In proxy Diffix, not only was there one AID, but that AID had to come from the same identifier space. This caused real difficulties in cases where more than one type of identifier was "AID-like" (i.e. an account number and a customer number). With a more flexible approach, an analyst can label what the "best" columns are to protect each kind of AID, and the mechanism will do the right thing in each case.

Let's look at some examples of this. Suppose we have three tables, `users`, `accounts`, and `atm`.

`users` has columns: `ssn`, `age`, `gender`, `zip`, where `ssn` is the social security number.

`accounts` has columns: `ssn`, `account`.

`atm` has columns `amount`, `account`, where `amount` is the amount withdrawn from the atm.

Some accounts have two users, and some users have multiple accounts. `accounts` has one row per user per account.

Suppose that the administrator tags the following as being AID columns: `atm.account`, `accounts.ssn`, and `users.ssn`. (Strictly speaking the admin should tag `accounts.account` as well, but let's suppose that he or she doesn't.)

If the analyst queries just the `atm` table, then `atm.account` is protected. In some theoretical sense user is not protected, but it would be very hard for an attacker to exploit this. It might be that one user has four accounts, and that user has very high withdrawls in all those accounts, and so that user isn't adequately flattened, but this is a corner case that I wouldn't worry about in practice.

In any event, if the attacker wants to somehow exploit information about that user, then he has to JOIN with `users` and `accounts`, at which point `ssn` is also brought in as an AID, and so now the user is protected.

Suppose now that the attacker does a query with `atm JOIN users on amount = age`. Such a query would totally mix up `ssn` and `account` in that individual rows would contain `ssn` and `account` that have nothing to do with each other. Nevertheless, both `ssn` and `account` would be protected by virtue of the two AIDs. 

### Problem Case: Proxy AIDs

A difficult case is where a column that is not protected (and cannot be tagged as AID) has a high correlation with something that is protected. An example is this:

Three tables, `paychecks`, `users`, and `companies`. We want to protect both users and companies.

`paychecks`: `user_id`, `amount`, `date`
`users`: `user_id`, `age`, `zip`
`companies`: `user_id`, `company`

Since `company` only appears in one place, it is tagged there. However, suppose that there are some zips with only one company (but other zips that have multiple companies).

In this case, an attack on a company could be like

```
SELECT sum(p.amount)
FROM user u JOIN paychecks p 
ON u.user_id = p.user_id
WHERE u.zip = 12345
```

Since table `companies` is not in the query, `company` is not protected and `zip` acts as a proxy for `company`. I don't think there is a simple solution to this problem. Rather, I think one needs to engineer `company` into the table `users`. One way might be to build a view like:

```
CREATE VIEW `users2` AS
SELECT u.user_id AS user_id, ... c.company
FROM users u JOIN companies c
ON u.user_id = c.user_id
```

The problem with this is that if there are multiple companies per user, then we end up with multiple rows per user in the `users2` table, which might not be what we want.

I'm not sure how big of a problem this will be in practice. Even this example is somewhat contrived, because if a user belongs to multiple companies, then presumably the `paychecks` table would have a column indicating which company the paycheck came from.

### Pseudo-AIDs (or multi-column AIDs)

The clinic database had a problem whereby a person would have multiple `patient_id`. It appeared just to be sloppy registration practice. This would for instance allow someone to view individual names in the database. One way to deal with this might be to allow some kind of multi-column AID, where several columns serve as a pseudo-identifier and Diffix concats them into a single AID. This is risky though, because if the admin uses too many columns, it could have the effect of making one person look like multiple people in the system and then they become attackable in other ways. 

Perhaps the better alternative is just to continue to have non-selectable columns.

### Configuration

From the admin point of view, I think the steps to configuration is something like this:

1. Decide what entities will be protected (individual humans, devices, companies, etc.)
2. For each entity, try to find the single best column in each table that identifies that entity.
3. Take care to note that multiple different people can appear in different columns (sender/receiver).
4. Consider whether the identified columns are 100% effective in identifying the entity, or rather have some flaws (sometimes the same person has multiple AID values, sometime the same AID value has multiple persons). In these cases it may be necessary to use multiple columns for the same entity.
5. Take care not to tag redundant columns as AID (i.e. two columns that effectively identify the same entities). Doing so just slows down execution time.
6. Tag each column as AID (note not necessary to configure what entity is being protected, or how AIDs relate to each other).



## sum()

If the aggregate is `sum()`, we need to determine which AIDs contribute the most, both for flattening and for determining the amount of noise. I'm assuming that, in the DB, aggregates are often computed on the fly. So for instance, `sum()` is computed by adding the column value to the aggregate as rows are determined to be included.

Since we don't know how much each AID contributes until after query execution, we need to keep a data structure for all AIDs, and compute the contribution for each AID on the fly. (We may also want to keep a data structure that efficiently retains the top contributors, but in principle we could determine this after we have computed all AID contributions.)

I'm envisioning a hash structure for maintaining AID contributions. There must be good support for this in the DB. For `sum()`, we can add the column value in each row to the AID entry in the hash structure.

If LED requires that we remove a row that was previously added, we can subtract the column value from the hashed AID entry.

| goal             | heavy contributors (top 4-5 AIDs)    |
| ---------------- | ------------------------------------ |
| data structure 1 | hash indexed by AID                  |
| data structure 2 | top few contributing AIDs            |
| insert           | add column value to AID index        |
| delete           | subtract column value from AID index |

## median()

With median, we want to be able to efficiently find the values (and corresponding AIDs) above and below the true median. The idea here is that we don't want to report the true median unless there are enough other AIDs with the same value. If there are not, then we will need to generate a median from some composite (more or less as we did in an earlier version of Aircloak).

One way to compute median efficiently is with a pair of heaps, where the left heap and the right heap have the same number of entries, and the values in the left heap are smaller than the values in the right heap.

| goal             | AIDs above and below true median                                   |
| ---------------- | ------------------------------------------------------------------ |
| data structure 1 | pair of heaps? (see what DB does?)                                 |
| data structure 2 | AIDs above and below                                               |
| insert           | add to heap                                                        |
| delete           | delete from heap (note not needed by native DB median computation) |

## max(), min()

For `max()`, we need to know several AIDs of top values (and bottom values for `min()`).

## 

zzzz


----------
# Inequalities

I believe we can do away with the range and snapping requirements, at least from the point of view of the analyst. Under the hood we'll still have snapping. For the sake of the following discussion, let's assume that we snap ranges and offsets by powers of 2 (1/4, 1/2, 1, 2, 4...), and that we allow shifting of an offset by 1/2 or the range. Note that the choice of snapping increments is hidden from the analyst, so these don't have to be user friendly per se.

Basic idea is this. When an inequality is specified, we want to find a snapped range inside the inequality that includes a noisy-threshold number of distinct users (say between 2 and 4 distinct users). 

The drawing here illustrates that. Here the analyst has specified a `<=` condition (the red line, which we'll call the edge). The x's represent the data points, and for the sake of simplicity we'll assume one data point per distinct AID. The drawing shows two scenarios, one where the data points are relatively more densely populated, and one where they are relatively more sparsely populated.

![](https://paper-attachments.dropbox.com/s_832D3C952962442CDA33E4F625E4143AA62099E867229D6068A42F99D3011C9E_1599545192241_image.png)


The blue boxes represent snapped ranges within the edge. The box for the dense scenario has 5 distinct AIDs, and the box for the sparse scenario has 3 distinct AIDs. Data points inside the boxes are included in the answer, but data points between the boxes and the edge are excluded (marked in red). The noise layers would be seeded by the size and position of the box.

A critical point is that if the edge is moved left or right slightly, this would not change the chosen boxes. This makes it hard to do an averaging attack on the condition's noise layer.

As the edge is moved further, it would get to the point where a new box would be chosen, possibly bigger or smaller than the previous box, probably but not necessarily including or excluding different AIDs, but in any event the choice of the new box would not be triggered by crossing a given data point, but rather by moving far enough that a new box is a better fit. This is not to say that the box isn't data driven, it is. However, typically the choice of box is based on the contributions of multiple AIDs, not a single AID.

If the edge matches a snapped value, then it may well be the case that no data points are excluded. If the analyst specifies a range, and the range itself matches a snapped range, then we can use the traditional seeding approach which would in turn match the seeds of histograms.

There are still some details to be worked out regarding which box to use when there are several choices. Probably we want to minimize the number of dropped data points. This in turn can (normally) best be done when the box itself is small, because this allows for finer-grained offsets. 

Sebastian mentions this case:


    SELECT age, count(*)
    FROM users
    WHERE age > 10

which could in principle display a value for `age = 10`. Sebastian points out that this is probably a non-issue since the `age=10` bucket would almost certainly be suppressed. Nevertheless we might need an explicit check to prevent it.

In the case where there are no values to the right (left) of a less-than (greater-than) condition, we don't need the box to encompass the value. Rather we can treat it as though the analyst edge were identical to the right-most (left-most) data point. This prevents an averaging attack where the analyst just selects edges further and further to the right (left) of the max (min) value.

No doubt other missing details here, but this is the basic idea and it looks solid to me.

## Unclean inequalities

It occurs to me that the above discussion implicitly assumes that inequalities are clean (i.e. we know the value of the edge. If not clean, though, this begs the question of how the DB knows what to access from an index. They must have some way of knowing what in the index is included and excluded, and if they know this, then we could leverage it to allow unclean inequalities.


> TODO: We need to think about cases where changing an edge from one query to the next can isolate a single user. To the extent that such cases exist, we may need to detect them.
## Time-based attacks ('now()' as inequality)

One of the attacks that we've never addressed is that of repeating a query and detecting the difference between the two queries. If it is somehow known that only one user changed in the query context between the two queries then this can be detected.

If the DB records a timestamp for when rows are added, we can leverage this by implicitly treating each query as having `WHERE ts <= now()` attached to it, and treating this inequality as described above.

----------
# Histograms

For histograms I think we should still enforce snapping like we currently do, with the exception that we can allow finer granularity and more options (powers of 2, money style, etc). This way we can avoid the inaccuracies inherent in the solution for inequalities given above.


----------
# LIKE regex

I'm cautiously optimistic that we'll be able to remove all of our LIKE restrictions. There are two issues to consider, 1) using wildcards to launch a difference attack, and 2) using wildcards to average away noise (de-noise). Both issues require that we gather certain information *during regex processing*. My uncertainty comes from the fact that I don't understand how regex processing happens in any detail.

The difference attack exploits cases where a victim can be included with or excluded from a set of other AIDs with wildcards. An old example of this is where there are many 'paul' but only one 'paula'. Then `LIKE` `'``paul``'` and `LIKE` `'``paul_``'` differ by one user.

The de-noise attack of course depends on how we make noise, but if we assume that we seed noise based on column values, then we have the following attack. Suppose that the attacker wants to de-noise one noise layer for the condition where all rows match `LIKE` `'``%LIDL%``'`. Suppose the column values have a variety of strings before and after 'LIDL'. The attacker could then do the [split averaging attack](https://demo.aircloak.com/docs/attacks.html#split-averaging-attack), which generates pairs of queries where each pair when, summed together, has all rows with `%LIDL%`. Since each answer will have different rows, then the static noise for each answer will differ so long as there are enough different substrings before or after 'LIDL'. 

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

For example, suppose that in some query, 10 users have card_type 'gold card', and potentially 1 user has card_type 'platinum card'. A analyst wants to attack the platinum card holder using a pair of queries using `WHERE card_type LIKE` `'``%card``'` and `WHERE card_type LIKE` `'``%d card``'`. The wild card of the first query would have the substring 'gold ' associated with 10 AIDs, and the substring 'platinum ' associated with a single AID. These latter rows are LE and would be silently dropped. As a result, the two queries would have the same underlying count whether or not the victim was included in the first query.

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
If a LIKE condition is `'``%LIDL%``'`, then one approach to defending against the split-averaging version of the de-noise attack would be to seed the noise layer with the substring 'LIDL' (in addition to the seed for the complete string). A strawman design would be to add a noise layer for every string constant in the regex expression. For instance, the expression '%foo%bar' would have a noise layer for 'foo' and another noise layer for 'bar'.

The problem here is that it leads to another form of de-noise attack, where low-effect (or zero-effect) wildcards are used to generate different noise layers. So for instance, `'``%L_DL%``'`, `'``%IDL%``'`, `'``%LI_L%``'`, and so on. As long as the same rows are included, the analyst can get different noise layers.

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


# TODOs

Look into whether there is leakage from EXPLAIN.


