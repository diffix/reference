# Issues with LED

This documents the need for LED, and motivates a number of design decisions. In working through this, we developed the idea of LE noise layers (see [Solution](#solution)), which defends against several new and appears to replace the need for dynamic noise layers altogether.

- [Noise layer notation](#noise-layer-notation)
- [Background (difference attacks)](#background-difference-attacks)
  - [Simple difference attack, static noise](#simple-difference-attack,-static-noise)
  - [First derivative difference attack, static noise](#first-derivative-difference-attack-static-noise)
  - [First derivative difference attack, static and dynamic noise](#first-derivative-difference-attack-static-and-dynamic-noise)
  - [Chaff attack](#chaff-attack)
- [Ideal solution](#ideal-solution)
  - [Definition of threshold for Low Effect (LE)](#definition-of-threshold-for-low-effect-le)
  - [Higher noise levels for only LE conditions](#higher-noise-levels-for-only-le-conditions)
- [Solution space](#solution-space)
  - [Must sometimes override query execution plan to understand LE conditions](#must-sometimes-override-query-execution-plan-to-understand-le-conditions)
  - [Which LE conditions do we need to detect?](#which-LE-conditions-do-we-need-to-detect)
  - [Is dynamic noise without column value adjustment enough?](#is-dynamic-noise-without-column-value-adjustment-enough)
- [New attacks](#new-attacks)
  - [Multi-histogram (first derivative difference attack)](#multi-histogram-first-derivative-difference-attack)
  - [Multi-sample difference attack using JOIN](#multi-sample-difference-attack-using-join)
- [Solution](#solution)
  - [Multi-sample JOIN attack](#multi-sample-join-attack)
  - [Multi-histogram attack](#multi-histogram-attack)
  - [LE noise layer with AIDVs (not as good)](#le-noise-layer-with-aidvs-not-as-good)
  - [Seeding LE noise layers properly](#seeding-le-noise-layers-properly)
- [Adjusting when LE = 1 only](#adjusting-when-le--1-only)
- [Do we still need dynamic noise layers?](#do-we-still-need-dynamic-noise-layers)
  - [First derivative difference attack, revisited](#first-derivative-difference-attack-revisited)
  - [Chaff attack, revisited](#chaff-attack-revisited)

## Noise layer notation

Throughout this doc there are examples of the noise layers associated with conditions (including implicit conditions defined by `SELECT ... GROUP BY`. These noise layers have a certain compact notation, described here.

The first letter of a noise layer is S, D, or L (for Static, Dynamic, and LE noise). Static and dynamic noise is what we have used in the past. LE noise is something new.

When followed by another letter or letters, that letter or letters denotes the condition that the noise layer is associated with. Our shorthand for conditions is one upper-case letter, optionally followed by a number or a lower-case letter:

* A: The condition 'A' (where the condition is something like `column = value`, or `SELECT column ... GROUP BY`).
* X1: The condition X1 (normally the first of a sequence of conditions of a certain type, like a chaff condition).
* Xn: The condition Xn (normally the last of a sequence of n conditions of a certain type).

Examples are:

* SA: Static noise layer for condition A
* DXn: Dynamic noise layer for condition Xn
* LC: LE noise layer for condition C

Dynamic noise layers depend on the AID Value Sets (AIDVSs) for seeding material. As a result, different dynamic noise layers for the same condition but in different buckets will have different noise values. This is denoted with a digit after the D:

* D1B and D2B: Two dynamic noise layers associated with condition B, but with different AIDVS in the seed (AIDVS1 and AIDVS2, effectively)

Furthermore, sometimes we want to distinguish between AIDVS's that contain or do not contain the victim AIDV. We do this with the letter 'v' after the number:

* D1vB and D1B: Two dynamic noise layers associated with condition B, the first of which has the victim in the AIDVS, and the second of which has the same AIDVS except without the victim.

The longest possible noise layer example would be D2vX1, which would be the dynamic noise layer seeded by AIDVS2 which includes the victim, for condition X1.

Phew.

## Background (difference attacks)

As background, the primary reason we need LED is to deal with difference attacks. These are attacks where an analyst has a pair of queries (by convention I call them 'left' and 'right') that potentially differs by one 'victim' user (or possibly by N users, but where  N-1 of them are known and one of them is the victim), and then tries to detect if the left and right buckets differ or not.

In this document we refer to 'static' and 'dynamic' noise layers (where 'dynamic' is what we sometimes called UID layers). Anyway, static layer seeds depend only one the semantics of the SQL, while dynamic layer seeds depend also on the specific users in the bucket. (We also introduce a new kind of noise layer in [Solution](#solution), but that isn't part of the background.)

### Simple difference attack, static noise

Static noise by itself defends against a simple difference attack where there is only a single left and right bucket.

| L query     | R query | L answer      | R answer ex | R answer in  |
|-------------|---------|---------------|-------------|--------------|
| A and not I | A       | cnt + SA + SI | cnt + SA    | cnt + 1 + SA |

In the above `A` and `I` represent conditions, like `age = 10`. By convention, conditions that isolate or help isolate the user are I, J, and K. So in the above, I could be `ssn = '123-45-6789'`. Or if we need two conditions to isolate a user, we could have `(I and J)`, where I is `dob = '1957-12-14'` and J is `zip = 12345`.

'L' and 'R' in the headings row mean 'left' and 'right'.

`cnt` is the true count of the answer excluding the victim. As mentioned [before](#noise-layer-notation) `SX` is the static noise layer associated with condition `X`.

In the above, the left query excludes the victim, while the right query includes the victim if the victim has attribute A (is included by condition A), and excludes the victim otherwise. These two possibilities are expressed with the columns 'R answer ex' (excludes victim) and 'R answer in' (includes victim).

The goal of the attacker is to determine which of the right answers is the correct one. In this case, the static noise layer `SI` prevents the attacker from knowing because the left answer could be bigger or smaller than the right answer regardless of whether the victim is included or not. The larger the noise standard deviation, the less confident the attacker is.

### First derivative difference attack, static noise

The problem comes with the first derivative difference attack, and is the reason why we introduced dynamic noise. In the first derivative different attack, the attacker generates a histogram of left and right buckets:

| L query     | R query | L answer      | R answer ex | R answer in  | diff   |
|-------------|---------|---------------|-------------|--------------|--------|
| A and not I | A       | cnt + SA + SI |             | cnt + 1 + SA | SI - 1 |
| B and not I | B       | cnt + SB + SI | cnt + SB    |              | SI     |
| C and not I | C       | cnt + SC + SI | cnt + SC    |              | SI     |

Here, A, B, and C represent the different values of the bucket. For instance, the left query might have been:

```sql
SELECT age
FROM table
WHERE ssn <> '123-45-6789'
GROUP BY 1
```

Then A in the above table might be `age = 10`, B might be `age = 11`, and C might be `age = 12`. All the buckets in the answers might have been generated with the above query, or each bucket could have been generated with a separate query (i.e. `WHERE age = 10 and ssn <> '123-45-6789'`).

As a histogram, the victim can be in only one bucket, and the attacker of course knows it. In this case, the victim is in the first row. The column `diff` gives the difference of the left and right buckets. In all cases, cnt and SA cancel out, leaving only SI and the victim (or not). As a result, the difference will be the same in all buckets except that of the victim, thus revealing the victim's attribute.

Note by the way that we need static noise to defend against some averaging attacks, not discussed here.

### First derivative difference attack, static and dynamic noise

Now let's see what happens when we add dynamic noise layers. We can suppose for the sake of this discussion that the dynamic noise layers are seeded in part by the AIDVSs in the bucket.

| L query     | R query | L answer                  | R answer ex    | R answer in         | diff                      |
|-------------|---------|---------------------------|----------------|---------------------|---------------------------|
| A and not I | A       | cnt + SA + SI + D1A + D1I |                | cnt + 1 + SA + D1vA | SI + D1A + D1I - 1 - D1vA |
| B and not I | B       | cnt + SB + SI + D2B + D2I | cnt + SB + D2B |                     | SI + D2I                  |
| C and not I | C       | cnt + SC + SI + D3C + D3I | cnt + SC + D3C |                     | SI + D3I                  |

In the above table, the dynamic noise layers are denoted `D1A`, where `A` implies seeding material from `A`, and `1` implies seeding material from some AIDVS. Therefore, D1I is a different noise value from D2I. 

Now looking at the difference, we see that each of them has a different dynamic noise sample (D1I, D2I, D3I, etc.). This is because each row has a different set of users (those with attribute A, those with attribute B, etc.). This noise masks the presence of the victim.

Note that the difference for the victim's bucket has two additional noise terms, D1A and D1vA. The `1v` in D1vA represents seed material from the AIDVS `1` plus the victim's AIDV. The dynamic noise layer associated with A is seeded differently left and right because the AIDVS's are different (left excludes the victim, right includes the victim). 

An important take-away here is that it is only the dynamic noise layer associated with I that defeats the attack ... the other dynamic noise layers are in fact canceled out.

### Chaff attack

In a chaff attack, the attacker adds a bunch of conditions that definitely have no effect on the answer. An example would be `age <> 1000`. These are denoted by X1, X2, ..., Xn etc.

The following table shows the result. (Here all of the right answers are in the same column. The victim is in the first row. The static noise layers are not shown because in any event they are canceled out by the left/right difference.)

| L query                               | R query                     | L answer                            | R answer                              | diff                                                           |
|---------------------------------------|-----------------------------|-------------------------------------|---------------------------------------|----------------------------------------------------------------|
| A and not I and not X1 ... and not Xn | A and not X1 ... and not Xn | cnt + D1a + D1I + D1X1 + ... + D1Xn | cnt + 1 + D1va  + D1vX1 + ... + D1vXn | D1a + D1I + D1X1 + ... + D1Xn - 1 - D1va - D1vX1 - ... - D1vXn |
| B and not I and not X1 ... and not Xn | B and not X1 ... and not Xn | cnt + D2b + D2I + D2X1 + ... + D2Xn | cnt + D2b + D2X1 + ... + D2Xn         | D2I                                                            |
| C and not I and not X1 ... and not Xn | C and not X1 ... and not Xn | cnt + D3c + D3I + D3X1 + ... + D3Xn | cnt + D3c + D3X1 + ... + D3Xn         | D3I                                                            |

Because the AIDVS in the left and right buckets of the first row differ (because of the victim), none of the dynamic noise layers in the first row get canceled out, whereas all except D#I get canceled out in the other rows. While all of the differences will be different, the sheer amount of noise in the victim's difference reveal which is the victim. In essence the magnitude of the difference reveals the victim's attribute.

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

A very expensive way to do this would be generate many queries, each differing from the original by some combination of dropped conditions, and then see for each bucket if any of the modified queries differ from the original by some small number of AIDVs (i.e. are "low effect"). For these buckets, substitute the answer of the modified query for the original.

If several modified queries are low effect, then we would determine which rows are "flipped" in the modified answer relative to the original answer. By flipped, I mean included in the modified answer but excluded in the original, or vice versa. The answer given to the analyst is then the original answer, but with all the flipped rows flipped.

A less expensive (but still potentially expensive) way to do this would be to measure the role that every True/False combination of conditions plays in the output. If none of the combinations has a low effect, then none of the conditions can have a low effect, since removal of any condition would change at least one of the T/F combinations. If any combinations does have a low effect, then we can check and see if dropping any condition or combination would only effect that combination, and if so we can flip the rows associated with the combination.

### Definition of threshold for Low Effect (LE)

A difficulty with this ideal solution is that we need to decide when a condition or combination is low effect (LE). Clearly a condition that affects zero or one AIDV is LE. But what about a condition with two AIDV's?

Suppose for the sake of argument that we set the LE threshold to be 1 AIDV, so that a condition with 2 AIDV's is not LE (NLE), but a condition with 1 AIDV is LE.  This now opens a difference attack on the threshold itself. The idea here is that the attacker looks for a difference between 1 and 2 users (rather than 0 or 1).

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

## Solution space

There are two broad approaches we can take (one with three sub-approaches):

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

Consider a first derivative difference attack where the attacker wants to learn the value for column `col_attribute` for a victim that can be isolated with I. `col_attribute` has values A, B, C, ...  Assume that the victim has value A, and that none of the values are low count.

The set of left-side queries would be:

```
SELECT count(*)
FROM tab
WHERE col_isolate <> X      -- not I
      AND col_attribute = X
```

For X=A, X=B, X=C, etc.

And the set of right side queries as:

```
SELECT count(*)
FROM tab
WHERE col_attribute = X
```

The attacker is looking to construct the following histogram:

| col | left  | right | diff      |
|-----|-------|-------|-----------|
| A   | not I | has I | different |
| B   | not I | not I | same      |
| C   | not I | not I | same      |

where none of the left buckets include the victim, and one of the right buckets contains the victim and therefore produces a different difference.

What we want to do here is make some kind of fix for the A bucket, but not fix for the other buckets.

Assuming that the query plan is in the order [not I,`col_attribute`], the truth table for the left A query is this:

| I | not I | A | out | status |
|---|-------|---|-----|--------|
| 1 | 0     | - | 0   | LE     |
| 0 | 1     | 1 | 1   | NLE    |
| 0 | 1     | 0 | 0   | NLE    |

and the truth table for left B query (and subsequent C, D, ... queries) looks like this:

| I | not I | B | out | status |
|---|-------|---|-----|--------|
| 1 | 0     | - | 0   | LE     |
| 0 | 1     | 1 | 1   | NLE    |
| 0 | 1     | 0 | 0   | NLE    |

The reason for the `-` in the first row is because the second WHERE clause is never evaluated: Once `not I` is found to be false, then the entire expression is false and the evaluation ends.

For both A and B-etc., one cannot tell from these truth tables alone whether or not a fix is required. In both cases, the value for A/B is unknown, and so it is unknown whether or not the outcome would be different if the `not I` condition were dropped.

In these cases, it is necessary to force the evaluation of A/B. This can be done by saving the rows associated with the LE combination, and separately evaluating the A/B condition after the query engine completes. Alternatively, we could manipulate the plan so that all combinations execute fully at least once.

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
| A and not I | A       | cnt + SA + SI + D1A + D1I |                | cnt + 1 + SA + D1vA | SI + D1A + D1I - 1 - D1vA |
| B and not I | B       | cnt + SB + SI + D2B + D2I | cnt + SB + D2B |                     | SI + D2I                  |
| C and not I | C       | cnt + SC + SI + D3C + D3I | cnt + SC + D3C |                     | SI + D3I                  |

What we want to do is to seed dynamic noise layers so that there is no difference in the individual layers left and right. Specifically what this means is that, for the first row, D1A = D1vAa. In other words, the noise layer for the bucket that includes the victim in the right query is seeded identically to the corresponding noise layer in the left query. To do this we would have to drop the victim's AIDV from the seeding of D1vA.

In that case, the resulting table would be this:


| L query     | R query | L answer                  | R answer ex    | R answer in         | diff                      |
|-------------|---------|---------------------------|----------------|---------------------|---------------------------|
| A and not I | A       | cnt + SA + SI + D1A + D1I |                | cnt + 1 + SA + D1vA | SI + D1I - 1              |
| B and not I | B       | cnt + SB + SI + D2B + D2I | cnt + SB + D2B |                     | SI + D2I                  |
| C and not I | C       | cnt + SC + SI + D3C + D3I | cnt + SC + D3C |                     | SI + D3I                  |

As required, the left and right buckets for any given row always differ (because of both the static and dynamic noise layers), and the difference between rows always differs because of the dynamic noise layers. In addition, the *amount* of noise difference is the same. This means that the chaff attack won't work.

## New attacks

In this section, we describe some new attacks that we recently thought of.

### Multi-histogram (first derivative difference attack)

Now, suppose that the attacker knows a number of attributes of the victim, K1, K2, K3, etc. The attacker can now make a *set of* histograms, one for each known attribute. 

The left side outputs are obtained with a sequence of queries like this:

```
SELECT count(*) FROM tab WHERE Kn and A and not I
SELECT count(*) FROM tab WHERE Kn and B and not I
SELECT count(*) FROM tab WHERE Kn and C and not I
...
```

The right side answers for one histogram would be obtained with the query:

```
SELECT col, count(*)
FROM tab
WHERE Kn
GROUP BY 1
```

The above sets of histograms would be repeated for n=1, n=2 etc.

The table showing how the answers are composed is then:

| L query            | R query  | L answer                                  | R answer ex                   | R answer in                         | diff                                   |
|--------------------|----------|-------------------------------------------|-------------------------------|-------------------------------------|----------------------------------------|
| Kn and A and not I | Kn and A | cnt + SKn + SA + SI + D1nKn + D1nA + D1nI |                               | cnt + 1 + SKn + SA + D1nvKn + D1nvA | SI + D1nKn - D1nvKn + D1nA - D1nvA - 1 |
| Kn and B and not I | Kn and B | cnt + SKn + SB + SI + D2nKn + D2nB + D2nI | cnt + SKn + SB + D2nKn + D2nB |                                     | SI + D2nI                              |
| Kn and C and not I | Kn and C | cnt + SKn + SC + SI + D3nKn + D3nC + D3nI | cnt + SKn + SC + D3nKn + D3nC |                                     | SI + D3nI                              |

Here D1nKn, D2nKn, ..., are dynamic noise layers for queries with condition Kn. Since each condition Kn for n=1,2,3..., the set of AIDs is different, so D1nKn, D2nKn, etc. are different for n=1,2,3...

The above looks a bit of a mess, but effectively all of the dynamic noise layers in the difference column differ from each other. They also all have zero mean and so when summed are still zero mean, and so can be replaced with the phrase '0-mean-noise'.

With that in mind, if we sum the difference from the Kn for each bucket A, B, C etc., we get:

| Bucket | Sum                                                                                        |
|--------|--------------------------------------------------------------------------------------------|
| A      | (Si + zero-mean-noise - 1) + (Si + zero-mean-noise - 1) + (Si + zero-mean-noise - 1) + ... |
| B      | (Si + zero-mean-noise) + (Si + zero-mean-noise) + (Si + zero-mean-noise) + ...             |
| C      | (Si + zero-mean-noise) + (Si + zero-mean-noise) + (Si + zero-mean-noise) + ...             |

The noise values Si are the same for all the sums, so they cancel out, so to speak. The dynamic noise values all have zero mean, and the combined standard deviation grows as the log of the number of noise values, so the -1 contributions from the A bucket probably dominates.

In other words, if the attacker knows enough things about K1, K2 etc. about the victim (probably 5 or 6 such things), then the victim attribute can be learned.

Let's call this the multi-histogram first derivative difference attack (or just multi-histogram for short).

### Multi-sample difference attack using JOIN

There is an attack whereby a JOIN is used to replicate the victim across many different buckets. This works when the ON condition allows the victim in (say) the left selectable to match with multiple entities in the right selectable. Following is an example:

```
SELECT left.unknown_col, right.replicate_col, count(*)
FROM (
    SELECT * FROM table1
    WHERE isolating_col <> 'victim'
    ) left
JOIN (
    SELECT * from table2
    ) right
ON left.join_col = right.join_col
```

(Unfortunately the nomenclature gets confusing now, because I've been using 'left' and 'right' to refer to the two queries in a difference attack, and now I'm going to also use them also to refer to the JOIN selectables.) The above query would be the right query in an attack, while the left query would not have the WHERE clause.

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

The solution is to add a new type of noise layer that is used whenever a condition is determined to be LE. We'll call it the LE noise layer. The LE noise layer is applied to every non-LE condition.

The LE noise layer is seeded as follows:

`[static_condition_materials, LE_static_condition_materials]`

Where:

* The `static_condition_materials` are the same seed materials used for normal static conditions
* The `LE_static_condition_materials` are the static seed materials for the LE condition itself

### Multi-sample JOIN attack

Now let's see how this protects against the multi-sample attack from above.

Working with the query example in the multi-sample attack from above, a single bucket has:

* Static: `SU1, SR, SJ`
* Dynamic: `D1U1, D1R, D1J`

From the right query with the victim (same bucket):

* Static: `SU1, SR, SJ, SI`
* Dynamic: `D1vU1, D1vR, D1vJ, D1vI`
* LE: `LIU1, LIR, LIJ`

And from the right query without the victim:

* Static: `SU2, SR, SJ, SI`
* Dynamic: `D2U2, D2R, D2J, D2I`
* LE: `LIU2, LIR, LIJ`

(In the above U1, U2 etc. refer to the group of buckets with `unknown_col` values u1, u2 etc. The 'R' values may differ, but within an `unknown_col` group they are the same, so we don't subscript them here.)

The nomenclature for the LE noise layer is LXY. The X represents the seed material from the isolating (LE) condition, while the Y represent the seed material from the non-LE condition. Therefore, LIR is the LE layer for isolating condition I and non-LE condition R.

All the dynamic layers are zero-mean, and average away. All the static layers except SI are removed with the difference of the left and right queries. Note that the SJ static layers will be seeded with a lot of different values for a given bucket, but are likely to be the same right and left. All of the LE layers are removed with the difference.

So if we sum the counts across all the buckets for each `unknown_col` value and take the left/right difference, then for buckets with the victim we get:

`cnt, SI, (zero-mean noise), victim x samples, LIU1, LIR, LIJ`

and for buckets without the victim:

```
cnt, SI, (zero-mean noise), LIU2, LIR, LIJ
cnt, SI, (zero-mean noise), LIU3, LIR, LIJ
etc.
```

The LIUx hides the victim because they differ with each `unknown_col` group.

### Multi-histogram attack

The following table is a repeat of the example from the Multi-histogram attack above, but with the LE noise layers added.

| L query            | R query  | L answer                                             | R answer ex (first row has victim)  | diff                                              |
|--------------------|----------|------------------------------------------------------|-------------------------------------|---------------------------------------------------|
| Kn and A and not I | Kn and A | cnt + SKn + LIKn + SA + LIA + SI + D1nKn + D1nA + D1nI | cnt + 1 + SKn + SA + D1nvKn + D1nvA | SI + LIKn + LIA + D1nKn - D1nvKn + D1nA - D1nvA - 1 |
| Kn and B and not I | Kn and B | cnt + SKn + LIKn + SB + LIB + SI + D2nKn + D2nB + D2nI | cnt + SKn + SB + D2nKn + D2nB       | SI + LIKn + LIB + D2nI                              |
| Kn and C and not I | Kn and C | cnt + SKn + LIKn + SC + LIC + SI + D3nKn + D3nC + D3nI | cnt + SKn + SC + D3nKn + D3nC       | SI + LIKn + LIC + D3nI                              |


The following is the result of summing the left and right query difference for each attribute A, B, C, etc.:

| Bucket | Sum                                                                                                                         |
|--------|-----------------------------------------------------------------------------------------------------------------------------|
| A      | (Si + LIKn + LIA + zero-mean-noise - 1) + (Si + LIKn + LIA + zero-mean-noise - 1) + (Si + LIKn + LIA + zero-mean-noise - 1) + ... |
| B      | (Si + LIKn + LIB + zero-mean-noise) + (Si + LIKn + LIB + zero-mean-noise) + (Si + LIKn + LIB + zero-mean-noise) + ...             |
| C      | (Si + LIKn + LIC + zero-mean-noise) + (Si + LIKn + LIC + zero-mean-noise) + (Si + LIKn + LIC + zero-mean-noise) + ...             |

The `LIA`, `LIB`, etc. noise layers in each row effectively hide which bucket the victim is in.

### LE noise layer with AIDVs (not as good)

Now, assume that the LE noise layer is applied to each non-LE condition, and is seeded as follows:

`[non-LE_static_condition_materials, LE_static_condition_materials, LE_AIDVs]`

Where:

* The `non-LE_static_condition_materials` are the same seed materials used for normal static conditions
* The `LE_static_condition_materials` are the static seed materials for the LE condition itself
* The `LE_AIDVs` are the AID Values that are affected by the LE condition.

For the query above, the seed materials for the condition `unknown_col=u1` in a bucket where the victim is included would look be:

`['unknown_col','=','u1','aid_col','<>','victim',('victim')]`

The first three (`'unknown_col','=','u1'`) are the static materials for the condition, the second three (`'aid_col','<>','victim'`) are the static materials for the LE condition, and the final `victim` is the AIDV of the victim.

The seed materials for the the condition `unknown_col=u2` in a bucket where the victim is excluded would be:

`['unknown_col','=','u2','aid_col','<>','victim',()]`

A single bucket from the left query has:

* Static: `SU1, SR, SJ`
* Dynamic: `D1U1, D1R, D1J`

From the right query with the victim (same bucket):

* Static: `SU1, SR, SJ, SI`
* Dynamic: `D1vU1, D1vR, D1vJ, D1vI`
* LIE: `LI1vU1, LI1vR, LI1vJ`

And from the right query without the victim:

* Static: `SU2, SR, SJ, SI`
* Dynamic: `D2U2, D2R, D2J, D2I`
* LIE: `LIU2, LIR, LIJ`


All the dynamic layers are zero-mean, and average away. All the static layers except SI are removed with the difference of the left and right queries. Note that the SJ static layers will be seeded with a lot of different values for a given bucket, but are likely to be the same right and left.

So if we sum the counts across all the buckets for each `unknown_col` value and take the left/right difference, then for buckets with the victim we get:

`SI, (zero-mean noise), victim x samples, LI1vU1, LI1vR, LI1vJ` 

and for buckets without the victim:

```
cnt, SI, (zero-mean noise), LIU2, LIR, LIJ
cnt, SI, (zero-mean noise), LIU3, LIR, LIJ
etc.
```

Across a set of buckets with the same Ux, the `LIUx` or `LIvUx` buckets are static, and so hide the `victim x samples` difference. However, all of the other LE layers are the same for every other condition, and so there might be a way to detect that the victim's `unknown_col` group is different from the others.

### Seeding LE noise layers properly

Note in the above examples that the LE noise layer is the same for LE conditions with one entity or zero entities. This is a bit tricky, because the seed material for the LE layer includes the isolating condition. An example would be `ssn <> '123-45-6789'`. Even when the condition matches nothing, we would like to be able to seed with the appropriate value (i.e. '123-45-6789').

To do this, we need to over-ride the optimization of the query execution plan so that we learn the value being excluded. For instance, if the WHERE clause is:

`WHERE age = 40 and ssn <> '123-45-6789'`

Suppose the victim is age 41. When examining rows with `age=40`, the first condition returns True, and the second condition is evaluated. However, since the victim is age 41, the second condition never matches and we don't learn how to seed `ssn`. Likewise when examining rows with `age<>40`, the first condition returns False, and the second condition is never evaluated. Therefore we never learn how to seed `ssn <> '123-45-6789`.

One way around this would be to over-ride the execution plan until we hit a row that returns False for `ssn <> '123-45-6789'`. This certainly raises the cost of the query.

Another way would be to run a separate query with `ssn = '123-45-6789'` just to learn for which value the condition would return False. Also costly.

The problem with both of the above approaches is that they fail with chaff conditions, since in those cases there is *never* a hit. The problem with this is that it leads to an attack whereby the attacker can detect whether seeding succeeded or not. 

Another approach might be static analysis of the condition. Problem is that I'm not sure how to do this for complex string matching (`LIKE` for instance).

> TODO: Need to think more about static analysis for seeding. Though my current thinking is that indeed `=` and `<>` need to be done with static analysis, and we can deal with `LIKE` and `substring()` separately.

In the following, we give some strawman examples of how LE noise layers could be seeded, and the problem with each approach.

By way of notation, *LE0* is an LE condition that matches nothing (i.e. a chaff condition). *LE+* is an LE condition that matches at least one AID. Note that an LE-noise layer is seeded by at least as follows:

```
[NLE_column_name, operator, NLE_column_value, LE_column_name, operator, LE_column_value]
```

By and large the issue here is what we assign to the `LE_column_value`, especially for LE0. In the following, we refer to the `LE_column_value` as simply the value.


**Design 1: Use NULL for column value for LE0 conditions**

In this design, LE+ conditions are assigned the observed (`LE_column_value`) value, and LE0 conditions are assigned `NULL`. With this design, *every* LE0 condition is given the same noise value. This leads to a *membership inference attack* where we determine if a given user is in the database or not. The attack has a series of queries with the following WHERE clauses:

```
WHERE not X1
WHERE not X2
...
WHERE not Xn
WHERE not I
```

The Xn are chaff conditions. All the queries with Xn have the same noise layer, so literally all of the answers for those queries will be identical. If I is not in the database, then the last (`WHERE not I`) query will also produce the identical answer. If I is in the database, the last query will produce a different answer, thus revealing I's membership.

**Design 2: LE0 conditions get a value from naive static analysis of the SQL:**

The idea here is that LE+ conditions are assigned a value from observation, and LE0 conditions are assigned a value from a naive static analysis (i.e. merely use the right-hand-side (RHS) string). The reason that LE+ conditions are assigned a value from observation is to prevent a *seed averaging* attack, whereby the same conditions produces different seeds, and the noise is averaged out.

The Design 1 attack no longer works because each query produces a different noise value. Instead, what the attack can do is to generate a series of conditions that all identify the same entity. For instance:

```
WHERE aid_column <> 12345                    -- not I
WHERE aid_column + 1 <> 12346                -- not I
WHERE aid_column + 2 <> 12347                -- not I
...
```

If the victim is in the database, then all of these conditions will generate the same noise. If the victim is not in the database, then they will generate different noise.

**Design 3: Like design 2, but give LE+ conditions an additional naive static analysis layer:**

The idea here is that we give LE+ conditions two noise layers, the one seeded from observed value, and another one seeded from a naive static analysis. In this case, the conditions from design 2 will all produce a different noise value, and so from casual observation behave just like LE0 conditions.

Instead, the attack can take the average of the queries from design 2, and compare them with a query that has o condition. If the victim is in the table, then the answers to the queries are:

```
cnt + 1 + LI + L1I
cnt + 1 + LI + L2I
...
```

Where LI is the LE layer from observation, and L1I, L2I etc. are the layers from naive static analysis.

If the victim is not in the table, then we get:


```
cnt + L1I
cnt + L2I
...
```

Finally, the answer for the query without the condition is:

```
cnt + baseNoise
```

Unless LI is very close to zero, the average of the set of answers with the victim will be much different from the answer without the condition. By contrast, the average of the set of answers without the victim will be relatively close to the answer without the condition. (Note here that the above is not including all the noise layers, but the basic principle still holds.)

**Design 4: Smarter static evaluation:**

Perhaps the right answer is to do smarter static evaluation, or more to the point to limit ourselves to clean conditions (at least for now) while we work on smarter static evaluation. I'm convinced from work a couple years ago that we can do good static evaluation for math operations. For string operations we'll have to do more work, or in the worst case disallow string operations for all LE conditions.


## Adjusting when LE = 1 only

In an [earlier section](#definition-of-threshold-for-low-effect-le), we pointed out that using adjustment of LE rows leads to too much distortion.

One thing we could do, however, is adjust in only the case where a single user is affected. Even though doing so doesn't appear necessary at this point, I have a general concern that if an attacker runs many different difference attacks on the same user, that the attacker will be able to detect a signal through the noise.

## Do we still need dynamic noise layers?

Keeping in mind that the purpose of dynamic noise layers is to defend against difference attacks, the question arises as to whether we need dynamic noise layers if we have LE noise layers. Let's revisit the earlier difference attacks.

### First derivative difference attack, revisited

In [First derivative difference attack, static and dynamic noise](#first-derivative-difference-attack-static-and-dynamic-noise), we had this table:

| L query     | R query | L answer                  | R answer ex    | R answer in         | diff                      |
|-------------|---------|---------------------------|----------------|---------------------|---------------------------|
| A and not I | A       | cnt + SA + SI + D1A + D1I |                | cnt + 1 + SA + D1vA | SI + D1A + D1I - 1 - D1vA |
| B and not I | B       | cnt + SB + SI + D2B + D2I | cnt + SB + D2B |                     | SI + D2I                  |
| C and not I | C       | cnt + SC + SI + D3C + D3I | cnt + SC + D3C |                     | SI + D3I                  |

With LE noise layers instead of dynamic noise layers, we would get this (where the victim is in the first bucket):

| L query     | R query | L answer           | R answer     | diff        |
|-------------|---------|--------------------|--------------|-------------|
| A and not I | A       | cnt + SA + SI + LIA | cnt + 1 + SA | SI + LIA - 1 |
| B and not I | B       | cnt + SB + SI + LIB | cnt + SB     | SI + LIB     |
| C and not I | C       | cnt + SC + SI + LIC | cnt + SC     | SI + LIC     |

If we take any given row in the above table, we see that the presence or absence of the victim is hidden by both the static (SI) and LE (LIA, LIB, etc.) noise layers. One can imagine using some set of queries to deduce the value of SI, since it is repeated so often in these queries, but each LE layer is different because it is mixed with a different condition. The LE layers are themselves enough to hide the presence of absence of the victim.

In the first derivative attack, the attacker is looking for the one bucket whose difference is different from that of the other buckets. The LE layers prevent that because "vertically", each LE layer is differe (LIA, LIB, LIC, etc.). So it appears that we don't need dynamic layers.

### Chaff attack, revisited

The [Chaff attack](#chaff-attack) with dynamic noise layers produced the following table:

| L query                               | R query                     | L answer                            | R answer                              | diff                                                           |
|---------------------------------------|-----------------------------|-------------------------------------|---------------------------------------|----------------------------------------------------------------|
| A and not I and not X1 ... and not Xn | A and not X1 ... and not Xn | cnt + D1a + D1I + D1X1 + ... + D1Xn | cnt + 1 + D1va  + D1vX1 + ... + D1vXn | D1a + D1I + D1X1 + ... + D1Xn - 1 - D1va - D1vX1 - ... - D1vXn |
| B and not I and not X1 ... and not Xn | B and not X1 ... and not Xn | cnt + D2b + D2I + D2X1 + ... + D2Xn | cnt + D2b + D2X1 + ... + D2Xn         | D2I                                                            |
| C and not I and not X1 ... and not Xn | C and not X1 ... and not Xn | cnt + D3c + D3I + D3X1 + ... + D3Xn | cnt + D3c + D3X1 + ... + D3Xn         | D3I                                                            |

With LE layers instead of dynamic layers, we get this instead (leaving out static layers except in the difference):

| L query                               | R query                     | L answer                      | R answer                    | diff         |
|---------------------------------------|-----------------------------|-------------------------------|-----------------------------|--------------|
| A and not I and not X1 ... and not Xn | A and not X1 ... and not Xn | cnt + LIA + LX1A + ... + LXnA | cnt + 1 + LX1A + ... + LXnA | SI + LIA - 1 |
| B and not I and not X1 ... and not Xn | B and not X1 ... and not Xn | cnt + LIB + LX1B + ... + LXnB | cnt + 1 + LX1B + ... + LXnB | SI + LIB     |
| C and not I and not X1 ... and not Xn | C and not X1 ... and not Xn | cnt + LIC + LX1C + ... + LXnC | cnt + 1 + LX1C + ... + LXnC | SI + LIC     |

As expected, the chaff attack doesn't work at all, because the chaff attack relies entirely on the effect of different AIDVS's, which with LE layers no longer affect the answer. In fact, this attack boils down to the same thing as the first derivative difference attack from above.
