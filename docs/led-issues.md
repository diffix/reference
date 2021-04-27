# Why LED is so hard

This documents the various designs we've looked at for LED, and why they fail.

## Glossary

* Query Execution Plan (or just plan): This is the sequence of events like condition evaluations that Postgres decides. Critically it includes optimizations (e.g. if a condition evaluates to True, then end).

## Background (difference attacks)

As background, the primary reason we need LED is to deal with difference attacks. These are attacks where an analyst has a pair of queries (by convention I call them 'left' and 'right') that potentially differs by one 'victim' user (or possibly by N users, but where  N-1 of them are known and one of them is the victim), and then tries to detect if the left and right buckets differ or not.

In this document we refer to 'static' and 'dynamic' noise layers (where 'dynamic' is what we sometimes called UID layers). Anyway, static layer seeds depend only one the semantics of the SQL, while dynamic layer seeds depend also on the specific users in the bucket (although there are variants on how to do this, as will be seen).

### Simple difference attack, static noise

Static noise by itself defends against a simple difference attack where there is only a single left and right bucket.

| L query     | R query | L answer      | R answer ex | R answer in  |
|-------------|---------|---------------|-------------|--------------|
| A and not I | A       | cnt + Sa + Si | cnt + Sa    | cnt + 1 + Sa |

In the above `A` and `I` represent conditions, like `age = 10`. By convention, conditions that isolate or help isolate the user are I, J, and K. So in the above, I could be `ssn = '123-45-6789'`. Or if we need two conditions to isolate a user, we could have `(I and J)`, where I is `dob = '1957-12-14'` and J is `zip = 12345`.

'L' and 'R' in the headings row mean 'left' and 'right'.

`cnt` is the true count of the answer excluding the victim. `Sx` is the static noise layer associated with condition `X`.

In the above, the left query excludes the victim, while the right query includes the victim if the victim has attribute A (is included by condition A), and excludes the victim otherwise. These two possibilities are expressed with the columns 'R answer ex' (excludes victim) and 'R answer in' (includes victim).

The goal of the attacker is to determine which of the right answers is the correct one. In this case, the static noise layer `Si` prevents the attacker from knowing because the left answer could be bigger or smaller than the right answer regardless of whether the victim is included or not. The larger the noise standard deviation, the less confident the attacker is.

### First derivative difference attack, static noise

The problem comes with the first derivative difference attack, and is the reason why we introduced dynamic noise. In the first derivative different attack, the attacker generates a histogram of left and right buckets:

| L query     | R query | L answer      | R answer ex | R answer in  | diff   |
|-------------|---------|---------------|-------------|--------------|--------|
| A and not I | A       | cnt + Sa + Si |             | cnt + 1 + Sa | Si - 1 |
| B and not I | B       | cnt + Sb + Si | cnt + Sb    |              | Si     |
| C and not I | C       | cnt + Sc + Si | cnt + Sc    |              | Si     |

Here, A, B, and C represent the different values of the bucket. For instance, the left query might have been:

```sql
SELECT age
FROM table
WHERE ssn <> '123-45-6789'
GROUP BY 1
```

Then A in the above table might be `age = 10`, B might be `age = 11`, and C might be `age = 12`. All the buckets in the answers might have been generated with the above query, or each bucket could have been generated with a separate query (i.e. `WHERE age = 10 and ssn <> '123-45-6789'`).

As a histogram, the victim can be in only one bucket, and the attacker of course knows it. In this case, the victim is in the first row. The column `diff` gives the difference of the left and right buckets. In all cases, cnt and Sa cancel out, leaving only Si and the victim (or not). As a result, the difference will be the same in all buckets except that of the victim, thus revealing the victim's attribute.

Note by the way that we need static noise to defend against some averaging attacks, not discussed here.

### First derivative difference attack, static and dynamic noise

Now let's see what happens when we add dynamic noise layers. We can suppose for the sake of this discussion that the dynamic noise layers are seeded in part by the set of AIDs in the bucket.

| L query     | R query | L answer                  | R answer ex    | R answer in         | diff                      |
|-------------|---------|---------------------------|----------------|---------------------|---------------------------|
| A and not I | A       | cnt + Sa + Si + Da1 + Di1 |                | cnt + 1 + Sa + Da1v | Si + Da1 + Di1 - 1 - Da1v |
| B and not I | B       | cnt + Sb + Si + Db2 + Di2 | cnt + Sb + Db2 |                     | Si + Di2                  |
| C and not I | C       | cnt + Sc + Si + Dc3 + Di3 | cnt + Sc + Dc3 |                     | Si + Di3                  |

In the above table, the dynamic noise layers are denoted `Da1`, where `a` implies seeding material from `A`, and `1` implies seeding material from some set of AIDs. Therefore, Di1 is a different noise value from Di2. 

Now looking at the difference, we see that each of them has a different dynamic noise sample (Di1, Di2, Di3, etc.). This is because each row has a different set of users (those with attribute A, those with attribute B, etc.). This noise masks the presence of the victim.

Note that the difference for the victim's bucket has two additional noise terms, Da1 and Da1v. The `v1` in Da1v represents seed material from the set of AIDs `1` plus the victim's AID. The dynamic noise layer associated with A is seeded differently left and right because the AID sets are different (left excludes the victim, right includes the victim). 

An important take-away here is that it is the dynamic noise layer associated with I that defeats the attack ... the other dynamic noise layers are in fact canceled out.

### Chaff attack

In a chaff attack, the attacker adds a bunch of conditions that definitely have no effect on the answer. An example would be `age <> 1000`. These are denoted by X1, X2, ..., Xn etc.

The following table shows the result. (Here all of the right answers are in the same column. The victim is in the first row.)

| L query                               | R query                     | L answer                            | R answer                              | diff                                                           |
|---------------------------------------|-----------------------------|-------------------------------------|---------------------------------------|----------------------------------------------------------------|
| A and not I and not X1 ... and not Xn | A and not X1 ... and not Xn | cnt + Da1 + Di1 + Dx11 + ... + Dxn1 | cnt + 1 + Da1v  + Dx1v1 + ... + Dxnv1 | Da1 + Di1 + Dx11 + ... + Dxn1 - 1 - Da1v - Dx1v1 - ... - Dxnv1 |
| B and not I and not X1 ... and not Xn | B and not X1 ... and not Xn | cnt + Db2 + Di2 + Dx12 + ... + Dxn2 | cnt + Db2 + Dx12 + ... + Dxn2         | Di2                                                            |
| C and not I and not X1 ... and not Xn | C and not X1 ... and not Xn | cnt + Dc3 + Di3 + Dx13 + ... + Dxn3 | cnt + Dc3 + Dx13 + ... + Dxn3         | Di3                                                            |

Because the set of AIDs in the left and right buckets of the first row differ (because of the victim), none of the dynamic noise layers in the first row get canceled out, whereas all except Di# get canceled out in the other rows. While all of the differences will be different, the sheer amount of noise in the victim's difference reveal which is the victim. In essence the magnitude of the difference reveals the victim's attribute.

Note that it not necessary to put all of the chaff conditions in a single query. Rather, there could be a set of queries, each with a different chaff condition:

```
Q1:  A and not I and not X1
Q2:  A and not I and not X2
...
Qn:  A and not I and not Xn
```

The difference for each query for each bucket is taken, and then the sum of differences for all queries for each bucket is computed. The increased noise can be detected that way.

The way we deal with this in Insights is to recognize when negative conditions are indeed chaff by pre-computing the common column values, and disallowing them.

The conundrum here is that on one hand we need dynamic noise to defeat the first derivative difference attack, but on the other hand, the dynamic (AID-based) nature of the noise itself can be exploited in a chaff attack.

## Ideal solution

The perfect solution is to have the left query mimic the behavior of the right query so that there is no difference between right and left.

A very expensive way to do this would be generate many queries, each differing from the original by some combination of dropped conditions, and then see for each bucket if any of the modified queries differ from the original by some small number of AIDs (i.e. are "low effect"). For these buckets, substitute the answer of the modified query for the original.

If several modified queries are low effect, then we would determine which rows are "flipped" in the modified answer relative to the original answer. By flipped, I mean included in the modified answer but excluded in the original, or vice versa. The answer given to the analyst is then the original answer, but with all the flipped rows flipped.

A less expensive (but still potentially expensive) way to do this would be to measure the role that every True/False combination of conditions plays in the output. If none of the combinations has a low effect, then none of the conditions can have a low effect, since removal of any condition would change at least one of the T/F combinations. If any combinations does have a low effect, then we can check and see if dropping any condition or combination would only effect that combination, and if so we can flip the rows associated with the combination.

### Definition of threshold for Low Effect (LE)

A difficulty with this ideal solution is that we need to decide when a condition or combination is low effect (LE). Clearly a condition that affects zero or one AID is LE. But what about a condition with two AIDs?

Suppose for the sake of argument that we set the LE threshold to be 1 AID, so that a condition with 2 AIDs is not LE (NLE), but a condition with 1 AID is LE.  This now opens a difference attack on the threshold itself. The idea here is that the attacker looks for a difference between 1 and 2 users (rather than 0 or 1).

For instance, suppose that the attacker is able to isolate two users. This may not be that hard to do. For instance, if the two users are the only two users that are both women and are both age 35 in the CS dept, then the isolating condition would be:

```
WHERE ... OR (gender = 'W' AND age = 35 AND dept = 'CS')
```

Suppose the attacker wants to learn some unknown attribute value for the two users. If both users have different values, then the two buckets where each user appears are LE, because the isolating condition affects one user. In all other buckets the isolating condition affects two users, and so would not be LE. The difference then between the users' buckets and the other buckets is one. This difference is easily covered by the noise.

If however both users have the same value, then in that one bucket the left/right difference will be zero, while in all other buckets it is two. In other words, the noise has a greater difference to hide.

To defend against this, we would need a noisy LE threshold (same as how we have a noisy LCF threshold). Ideally the range of the noisy threshold is somewhat wide (say between 2 and 6 or even more, like we do with LCF), so that the attacker is unsure if N versus N-1 users is going to hit the threshold.

This, however, leads to a utility problem. Say we have a noisy threshold with an average of 3. This could lead to a lot of conditions being classified as LE, with the corresponding adjustments. This could be quite bad for relatively small buckets (which are common).

On the other hand, in this particular example there are 3x2=6 noise layers associated with the three conditions that comprise the isolating condition, so this masks the bucket well enough. More generally, it would be rare to be able to isolate a pair of users without having several conditions involved. In the worst case, we could increase the noise levels to deal with this.

### Higher noise levels for only LE conditions

Along the lines of increasing noise, another idea would be to increase the amount of noise for the noise layers associated with LE conditions.

> TODO: run experiments to see how much noise is needed to deal with N/N+1 difference attacks

## Solution space

There are two broad approaches we can take (one with two sub-approaches):

1. Continue to use dynamic noise layers, but somehow manage them so that the chaff attack goes away
  a. Eliminate the effect of low-effect conditions on seeding so that the same dynamic noise layers are produced for left and right.
  b. Modify seeding so that left and right dynamic noise layers are *always* different (but then we need to defend against averaging attacks).
  c. Eliminate the use of dynamic layers with chaff conditions
2. Don't use dynamic noise layers, and instead adjust answers by eliminating the effect of isolated users

Note that 1 and 2 are not mutually exclusive ... they could be combined.

For solutions 1a, 1b, and 2, we need to make active changes to what is computed by the query engine, either changing the seed material of dynamic layers, or changing the aggregate answer itself, or both. Regardless of which it is, we refer to any such change as a *fix*.

This is all complicated by a number of factors:

1. The isolating condition can be composed of multiple conditions (`A or (I and J)`, where `(I and J)` isolates the victim, but either `I` or `J` alone does not).
2. The isolating condition might be in different places in the execution plan (`A or (I and J)` versus `(I and J) or A`. If the victim matches condition A, then in the first case the LE combination is not observed, whereas in the second case it is.
3. The isolating conditions might not be together (`A or (I and J)` is equivalent to `(A or I) and (A or J)`.

### Must sometimes override query execution plan to understand LE conditions

Consider a first derivative diff attack where the attacker wants to learn the value for column `col` for a victim that can be isolated with I. `col` has values A, B, C, ...  Assume that the victim has value A, and that none of the values are low count.

The left-side query would be:

```
SELECT col_attribute,       -- A, B, C, ...
       count(*)
FROM tab
WHERE col_isolate <> X      -- not I
GROUP BY 1
```

And the right side as:

```
SELECT col_attribute,      -- A, B, C
       count(*)
FROM tab
GROUP BY 1
```

The attacker is looking for the following histogram:

| col | left  | right | diff      |
|-----|-------|-------|-----------|
| A   | not I | has I | different |
| B   | not I | not I | same      |
| C   | not I | not I | same      |

where none of the left buckets include the victim, and one of the right buckets contains the victim and therefore produces a different difference.

What we want to do here is make some kind of fix for the A bucket, but not fix for the other buckets.

Assuming that the query plan is in the order [not I,`col_attribute`], the truth table for A is this:

| I | not I | A | out | status |
|---|-------|---|-----|--------|
| 1 | 0     | - | 0   | LE     |
| 0 | 1     | 1 | 1   | NLE    |
| 0 | 1     | 0 | 0   | NLE    |

and the truth table for B-etc. looks like this:

| I | not I | B | out | status |
|---|-------|---|-----|--------|
| 1 | 0     | - | 0   | LE     |
| 0 | 1     | 1 | 1   | NLE    |
| 0 | 1     | 0 | 0   | NLE    |

For both A and B-etc., one cannot tell from these truth tables alone whether or not a fix is required. In both cases, the value for A/B is unknown, and so it is unknown whether or not the outcome would be different if the `not I` condition were dropped.

In these cases, it is necessary to force the evaluation of A/B. This can be done by saving the rows associated with the LE combination, and evaluating the A/B condition. Alternatively, we could manipulate the plan so that all combinations execute fully at least once.

### Which LE conditions do we need to detect?

The following is an example of a case where we don't need to necessarily always detect an LE condition (though from the conclusion above that we need to detect all LE combinations, I'm not sure that this observation helps us).

For instance, from the above example assume that the query execution plan is [`col_attribute`,not I]. This can be done with individual queries to produce the left side, like this:

```
SELECT count(*) FROM tab WHERE A and not I
SELECT count(*) FROM tab WHERE B and not I
SELECT count(*) FROM tab WHERE C and not I
...
```

The truth table for A now looks like this:

| A | I | not I | out | status |
|---|---|-------|-----|--------|
| 1 | 1 | 0     | 0   | LE     |
| 1 | 0 | 1     | 1   | NLE    |
| 0 | - | -     | 0   | NLE    |

And for B etc. looks like this:

| B | I | not I | out | status |
|---|---|-------|-----|--------|
| 1 | 0 | 1     | 1   | NLE    |
| 0 | - | -     | 0   | NLE    |

Note that in the case of B etc., no LE condition is detected, even though there is one. In this particular case it may be ok not to detect the LE condition, because at least for solutions 1a and 2, no fix is needed.

In the case of A, one can see that it is necessary to do some kind of fix, because from inspection one can see that if the `not I` condition were dropped, the output would change from 0 to 1.

### Is dynamic noise without column value adjustment enough?

Suppose we had a perfect implementation of solution 1a above (repeated here):

1. Continue to use dynamic noise layers, but somehow manage them so that the chaff attack goes away
  a. Eliminate the effect of low-effect conditions on seeding so that the same dynamic noise layers are produced for left and right.

Let's revisit the first derivative attack with this idea.

The table from that example is this:

| L query     | R query | L answer                  | R answer ex    | R answer in         | diff                      |
|-------------|---------|---------------------------|----------------|---------------------|---------------------------|
| A and not I | A       | cnt + Sa + Si + Da1 + Di1 |                | cnt + 1 + Sa + Da1v | Si + Da1 + Di1 - 1 - Da1v |
| B and not I | B       | cnt + Sb + Si + Db2 + Di2 | cnt + Sb + Db2 |                     | Si + Di2                  |
| C and not I | C       | cnt + Sc + Si + Dc3 + Di3 | cnt + Sc + Dc3 |                     | Si + Di3                  |

What we want to do is to seed dynamic noise layers so that there is no difference in the individual layers left and right. Specifically what this means is that, for the first row, Da1 = Da1v. In other words, the noise layer for the bucket that includes the victim in the right query is seeded identically to the corresponding noise layer in the left query. To do this we would have to drop the victim's AID from the seeding of Da1v.

In that case, the resulting table would be this:


| L query     | R query | L answer                  | R answer ex    | R answer in         | diff                      |
|-------------|---------|---------------------------|----------------|---------------------|---------------------------|
| A and not I | A       | cnt + Sa + Si + Da1 + Di1 |                | cnt + 1 + Sa + Da1v | Si + Di1 - 1              |
| B and not I | B       | cnt + Sb + Si + Db2 + Di2 | cnt + Sb + Db2 |                     | Si + Di2                  |
| C and not I | C       | cnt + Sc + Si + Dc3 + Di3 | cnt + Sc + Dc3 |                     | Si + Di3                  |

As required, the left and right buckets for any given row always differ (because of both the static and dynamic noise layers), and the difference between rows always differs because of the dynamic noise layers. In addition, the *amount* of noise difference is the same. This means that the chaff attack won't work.

## Multi-histogram (first derivative difference attack)

Now, suppose that the attacker knows a number of attributes of the victim, K1, K2, K3, etc. The attacker can now make a *set of* histograms, one for each known attribute. 

The right side answers for one histogram would be obtained with the query:

```
SELECT col, count(*)
FROM tab
WHERE Kn
GROUP BY 1
```

The left side outputs are obtained with a sequence of queries like this:

```
SELECT count(*) FROM tab WHERE Kn and A and not I
SELECT count(*) FROM tab WHERE Kn and B and not I
SELECT count(*) FROM tab WHERE Kn and C and not I
...
```

The above sets of histograms would be repeated for n=1, n=2 etc.

The table showing how the answers are composed is then:

| L query            | R query  | L answer                          | R answer ex           | R answer in               | diff          |
|--------------------|----------|-----------------------------------|-----------------------|---------------------------|---------------|
| Kn and A and not I | Kn and A | cnt + Skn + Sa + Si + Da1n + Di1n |                       | cnt + 1 + Skn + Sa + Da1n | Si + Di1n - 1 |
| Kn and B and not I | Kn and B | cnt + Skn + Sb + Si + Db2n + Di2n | cnt + Skn + Sb + Db2n |                           | Si + Di2n     |
| Kn and C and not I | Kn and C | cnt + Skn + Sc + Si + Dc3n + Di3n | cnt + Skn + Sc + Dc3n |                           | Si + Di3n     |

Here Di1n is a dynamic noise layer for a query with condition Kn. Since each condition Kn for n=1,2,3..., the set of AIDs is different, so Di1n is different for n=1,2,3...

Now, if we sum the difference from the Kn for each bucket A, B, C etc., we get:

| Bucket | Sum                                                       |
|--------|-----------------------------------------------------------|
| A      | (Si + Di11 - 1) + (Si + Di12 - 1) + (Si + Di13 - 1) + ... |
| B      | (Si + Di21) + (Si + Di22) + (Si + Di23) + ...             |
| C      | (Si + Di31) + (Si + Di32) + (Si + Di33) + ...             |

The noise values Si are the same for all the sums, so they cancel out, so to speak. The noise values DiMN all have zero mean, and the combined standard deviation grows as the log of the number of noise values, so the -1 contributions from the A bucket probably dominate.

In other words, if the attacker knows enough things about K1, K2 etc. about the victim (probably 5 or 6 such things), then the victim attribute can be learned.

Let's call this the multi-histogram first derivative difference attack (or just multi-histogram for short).


## Multi-sample difference attack using JOIN

There is an attack whereby a JOIN is used to replicate the victim across many different buckets. This works when the ON condition allows the victim in (say) the left selectable to match with multiple entities in the right selectable. Following is an example:

```
SELECT left.unknown_col, right.replicate_col, count(*)
FROM (
    SELECT * FROM table1
    WHERE aid_col <> 'victim'
    ) left
JOIN (
    SELECT * from table2
    ) right
ON left.join_col = right.join_col
```

(Unfortunately the nomenclature gets confusing now, because I've been using 'left' and 'right' to refer to the two queries in a difference attack, and now I'm going to use them also to refer to the JOIN selectables.) The above query would be the right query in an attack, while the left query would not have the WHERE clause.

In the above query, the columns are:

1. `unknown_col`: This is the column for which we want to learn the victim's value.
2. `join_col`: This is a column on which the JOIN takes place. Each value in `join_col` should be shared among a largish number of entities, so that the victim is paired with multiple entities in the right selectable.
2. `replicate_col`: This is a column that is selected in the outer selectable that will give us multiple buckets, each of which the victim will appear.

The query will produce a table like this:

| unknown_col | replicate_col | count |
|-------------|---------------|-------|
| u1          | r1            | 22    |
| u1          | r2            | 31    |
| u1          | r3            | 19    |
| ...         | ...           | ...   |
| u1          | rN            | 31    |
| u2          | r1            | 41    |
| u2          | r2            | 16    |
| u2          | r3            | 22    |
| ...         | ...           | ...   |
| u2          | rN            | 29    |

Regarding the left query (which does not exclude the victim), if the victim has `unknown_col` value `u1`, then the victim will appear in *all* of the buckets with `unknown_col=u1`, and in none of the other buckets.

If we were to take the difference of the left and right queries for each of the buckets individually, then there is enough noise to hide the presence or absence of the victim. If, however, we sum the counts for all the buckets with `u1`, and again for all the buckets with `u2`, then we effectively get multiple samples on the victim, and difference in the count is no longer hidden by the noise.

There are examples of this attack at [https://github.com/diffix/experiments/tree/master/join-lcf-noise](https://github.com/diffix/experiments/tree/master/join-lcf-noise).

## Solution

The solution is to add a new type of noise layer that is used whenever a condition is determined to be LE. We'll call it the LE noise layer. The LE noise layer is applied to every condition.

### LE noise layers without AIDVs (fail)

Assume that the LE noise layer is seeded as follows:

`[static_condition_materials, LE_static_condition_materials]`

Where:

* The `static_condition_materials` are the same seed materials used for normal static conditions
* The `LE_static_condition_materials` are the static seed materials for the LE condition itself

Working with the query in the multi-sample attack from above, a single bucket from the left query has:

* Static: SNu1, SNr1, SNj1
* Dynamic: DNu1, DNr1, DNj1
* LE: LENu1, LENr1, LENj1

From the right query with the victim:

* Static: SNu1, SNr1, SNj1, SNa
* Dynamic: DNu1v, DNr1v, DNj1v, DNa1v
* LE: LENu1, LENr1, LENj1, LENa1

And from the right query without the victim:

* Static: SNu2, SNr1, SNj1, SNa
* Dynamic: DNu2, DNr1, DNj1, DNa1
* LE: LENu2, LENr1, LENj1, LENa1

All the dynamic layers are zero-mean, and average away. All the static layers except SNa are removed with the difference of the left and right queries. Note that the SNj static layers will be seeded with a lot of different values for a given bucket, but are likely to be the same right and left. All of the LEN layers are removed with the difference.

So if we sum the counts across all the buckets for each `unknown_col` value and take the left/right difference, then for buckets with the victim we get:

`SNa, (zero-mean noise), victim x samples`

and for buckets without the victim:

`SNa, (zero-mean noise)`

This doesn't work, obviously.

### LE noise layer with AIDVs (fail)

Assume that the LE noise layer is seeded as follows:

`[static_condition_materials, LE_static_condition_materials, LE_AIDVs]`

Where:

* The `static_condition_materials` are the same seed materials used for normal static conditions
* The `LE_static_condition_materials` are the static seed materials for the LE condition itself
* The `LE_AIDVs` are the AID Values that are affected by the LE condition.

For the query above, the seed materials for the condition `unknown_col=u1` in a bucket where the victim is included would look be:

`['unknown_col','=','u1','aid_col','<>','victim',('victim')]`

The first three (`'unknown_col','=','u1'`) are the static materials for the condition, the second three (`'aid_col','<>','victim'`) are the static materials for the LE condition, and the final `victim` is the AIDV of the victim.

The seed materials for the the condition `unknown_col=u2` in a bucket where the victim is excluded would be:

`['unknown_col','=','u2','aid_col','<>','victim',()]`

Oops, doesn't this create though a histogram attack?

left           right

u1 with V      u1 w/o V
u2 w/o  V      u2 w/o V
u3 w/o  V      u3 w/o V
etc.

Well, the left and right noise is the same except for the u1 bucket. That doesn't look good.

A single bucket from the left query has:

* Static: SNu1, SNr1, SNj1
* Dynamic: DNu1, DNr1, DNj1
* LE: LENu1, LENr1, LENj1

From the right query with the victim:

* Static: SNu1, SNr1, SNj1, SNa
* Dynamic: DNu1v, DNr1v, DNj1v, DNa1v
* LE: LENu1v, LENr1v, LENj1v, LENa1v

And from the right query without the victim:

* Static: SNu2, SNr1, SNj1, SNa
* Dynamic: DNu2, DNr1, DNj1, DNa1
* LE: LENu2, LENr1, LENj1, LENa1

All the dynamic layers are zero-mean, and average away. All the static layers except SNa are removed with the difference of the left and right queries. Note that the SNj static layers will be seeded with a lot of different values for a given bucket, but are likely to be the same right and left.

So if we sum the counts across all the buckets for each `unknown_col` value and take the left/right difference, then for buckets with the victim we get:

`SNa, (zero-mean noise), (all LEN layers), victim x samples`

and for buckets without the victim:

`SNa, (zero-mean noise)`

This looks like a problem, because the summed-buckets bucket with the victim (`unknown_col=u1`) will have a lot more noise than the summed-buckets buckets without the victim (all other `unknown_col` values). This is a form of noise exploitation.



-------

zzzz

This implies that it isn't enough to simply make left and right dynamic noise the same. We must also adjust aggregate outputs (i.e. add 1 to each of the right-hand answers for attribute A).


### The multi-histogram attack is hard if the attacker has to distinguish between N and N-1 users (versus 0 and 1 user)

It was pointed out earlier that we need to have a noisy threshold for LE, and in fact the range should be fairly wide (not just between 1 and 2 users, but rather between 2 and 5 or 6 users), but on the other hand doing so would lead to poor utility if we adjust aggregates in all cases.  One thing we can try instead is to adjust the aggregate when there is one LE user only, but use the wider range for adjusting seeds for dynamic layers only.

> TODO: Validate that the basic first derivative difference attack with 1/2 users doesn't work well because of the number of noise layers that are needed practically speaking.

The idea for the attacker is that he has a known set of attributes for the victim, K1, K2, .... For each such attribute, the attacker needs to come up with an expression like:

```
WHERE Kn and A and not (isolates two users that share Kn)
```

where one of the two users is the victim, and the other we'll call a 'plant' (because the other user is planted in the query alongside the victim).

In order to discover if the victim has attribute A, the plant must have attribute A. So in total, the requirements for a successful plant are:

1. The plant must have the unknown attribute A
2. The plant must have the known per-histogram attribute Kn
3. The plant must share enough other attributes with the victim to isolate the plant and the victim together
4. The bucket needs to pass LCF

My guess is that meeting all of these requirements to produce enough histograms to effectively attack a given victim will be exceedingly rare.

> TODO: Validate that meeting these requirements would indeed be rare.

zzzz
