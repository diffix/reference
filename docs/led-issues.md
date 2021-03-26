# Why LED is so hard

This documents the various designs we've looked at for LED, and why they fail.

## Glossary

* Query Execution Plan (or just plan): This is the sequence of events like condition evaluations that Postgres decides. Critically it includes optimizations (e.g. if a condition evaluates to True, then end).

## Background (difference attacks)

As background, the primary reason we need LED is to deal with difference attacks. These are attacks where an analyst has a pair of queries (by convention I call them 'left' and 'right') that potentially differs by one 'victim' user (or possible by N users, but where  N-1 of them are known and one of them is the victim), and then tries to detect if the left and right buckets differ or not.

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

The goal of the attacker is to determine which of the right answers is the correct one. In this case, the static noise layer `Si` prevents the attacker from knowing because the left answer could be bigger or smaller than the right answer regardless of whether the victim is included or not.

### First derivative difference attack, static noise

The problem comes with the first derivative difference, and is the reason why we introduced dynamic noise. In the first derivative different attack, the attacker generates a histogram of left and right buckets:

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
| A and not I | A       | cnt + Sa + Si + Da1 + Di1 |                | cnt + 1 + Sa + Da1v | Si + Da1 + Di1 - 1 - Dav1 |
| B and not I | B       | cnt + Sb + Si + Db2 + Di2 | cnt + Sb + Db2 |                     | Si + Di2                  |
| C and not I | C       | cnt + Sc + Si + Dc3 + Di3 | cnt + Sc + Dc3 |                     | Si + Di3                  |

In the above table, the dynamic noise layers are denoted `Da1`, where `a` implies seeding material from `A`, and `1` implies seeding material from some set of AIDs. Therefore, Di1 is a different noise value from Di2. 

Now looking at the difference, we see that each of them has a different dynamic noise sample (Di1, Di2, Di3, etc.). This is because each row has a different set of users (those with attribute A, those with attribute B, etc.). This noise masks the presence of the victim.

Note that the difference for the victim's bucket has two additional noise terms, Da1 and Dav1. The `v1` in Dav1 represents seed material from the set of AIDs `1` plus the victim's AID. The dynamic noise layer associated with A is seeded differently left and right because the AID sets are different (left excludes the victim, right includes the victim). 

An important take-away here is that it is the dynamic noise layer associated with I that defeats the attack ... the other dynamic noise layers are in fact canceled out.

### Chaff attack

In a chaff attack, the attacker adds a bunch of conditions that definitely have no effect on the answer. An example would be `age <> 1000`. These are denoted by X1, X2, ..., Xn etc.

The following table shows the result. (Here all of the right answers are in the same column. The victim is in the first row.)

| L query                               | R query | L answer                            | R answer                              | diff                 |
|---------------------------------------|---------|-------------------------------------|---------------------------------------|----------------------|
| A and not I and not X1 ... and not Xn | A       | cnt + Da1 + Di1 + Dx11 + ... + Dxn1 | cnt + 1 + Da1v  + Dx1v1 + ... + Dxnv1 | Da1 + Di1 + Dx11 + ... + Dxn1 - 1 - Dav1 - Dx1v1 - ... - Dxnv1 |
| B and not I and not X1 ... and not Xn | B       | cnt + Db2 + Di2 + Dx12 + ... + Dxn2 | cnt + Db2 + Dx12 + ... + Dxn2         | Di2                  |
| C and not I and not X1 ... and not Xn | C       | cnt + Dc3 + Di3 + Dx13 + ... + Dxn3 | cnt + Dc3 + Dx13 + ... + Dxn3        | Di3                  |

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

### Definition of Low Effect (LE)

A difficulty with this ideal solution is that we need to decide when a condition or combination is low effect (LE). Clearly a condition that affects zero of one AID is LE. 

Suppose for the sake of argument that we set the LE threshold to be 1 AID. This now opens an attack on the threshold itself. For instance, suppose that the attacker knows that one user with AID1 has attribute A, and wants to determine if some other user (the victim) also has attribute A. If the attacker can compose a condition that matches both AID1 and the victim, then the first derivative difference attack works using this 2-AID condition as the isolating condition in the left query.

If the victim has attribute A, then the isolating condition affects two AIDs, is not considered LE, and no adjustment is made. In this case, the left and right buckets differ by two users. If the victim does not have attribute A, then the isolating condition affects only one AID, is considered LE, and an adjustment is made. In this case, the left and right buckets have no difference. As a result, the attacker learns whether the victim has attribute A or not.

To defend against this, we would need a noisy LE threshold (same as how we have a noisy LCF threshold). This, however, leads to a utility problem. Say we have a noisy threshold with an average of 3. This could lead to a lot of conditions being classified as LE, with the corresponding adjustments. This could be quite bad for relatively small buckets (which are common).

## Solution space

There are two broad approaches we can take (one with two sub-approaches):

1. Continue to use dynamic noise layers, but somehow manage them so that the chaff attack goes away
  a. Eliminate the effect of low-effect conditions on seeding so that the same dynamic noise layers are produced for left and right.
  b. Modify seeding so that left and right dynamic noise layers are always different (but then we need to defend against averaging attacks).
  c. Eliminate the use of dynamic layers with chaff conditions
2. Don't use dynamic noise layers, and instead adjust answers by eliminating the effect of isolated users

For solutions 1a, 1b, and 2, we need to make active changes to what is computed by the query engine, either changing the seed material of dynamic layers, or changing the aggregate answer itself, or both. Regardless of which it is, we refer to any such change as a *fix*.

This is all complicated by a number of factors:

1. The isolating condition can be composed of multiple conditions (`A or (I and J)`, where `(I and J)` isolates the victim, but either `I` or `J` alone does not).
2. The isolating condition might be in different places in the execution plan (`A or (I and J)` versus `(I and J) or A`. If the victim matches condition A, then in the first case the LE combination is not observed, whereas in the second case it is.
3. The isolating conditions might not be together (`A or (I and J)` is equivalent to `(A or I) and (A or J)`.

### Which LE conditions do we need to detect?

As it so happens, we don't necessarily need to detect all LE conditions. We only need to detect them when a fix is required.

For instance, consider a first derivative diff attack where the attacker wants to learn the value for column `col` for a victim that can be isolated with I. `col` has values A, B, C, ...  Assume that the victim has value A, and that none of the values are low count.

The right side answers can be obtained with a single query:

```
SELECT col, count(*)
FROM tab
GROUP BY 1
```

The left side outputs are obtained with a sequence of queries like this:

```
SELECT count(*) FROM tab WHERE A and not I
SELECT count(*) FROM tab WHERE B and not I
SELECT count(*) FROM tab WHERE C and not I
...
```

The attacker is looking for the following histogram:

| col | left  | right | diff      |
|-----|-------|-------|-----------|
| A   | not I | has I | different |
| B   | not I | not I | same      |
| C   | not I | not I | same      |

where none of the left buckets include the victim, and one of the right buckets contains the victim and therefore produces a different difference.

What we want to do here is make some kind of fix for the A bucket, but not fix for the other buckets.

Assuming the query execution plan [col,not I], the truth table for A looks like this:

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

Note that in the case of B etc., no LE condition is detected, even though there is one.  In this case it may be ok not to detect the LE condition, because at least for solutions 1a and 2, no fix is needed.

> TODO: Determine if this is always the case

In the case of A, one can see that it is necessary to do some kind of fix, because from inspection one can see that if the `not I` condition were dropped, the output would change from 0 to 1.


### Must sometimes override query execution plan to understand LE conditions

Suppose that the plan is reversed to be [not I,col]. Now the truth table for A is this:

| I | not I | A | out | status |
|---|-------|---|-----|--------|
| 1 | 0     | - | 0   | LE     |
| 0 | 1     | 1 | 1   | NLE    |
| 0 | 1     | 0 | 0   | NLE    |

and the truth table for B etc. looks like this:

| I | not I | B | out | status |
|---|-------|---|-----|--------|
| 1 | 0     | - | 0   | LE     |
| 0 | 1     | 1 | 1   | NLE    |
| 0 | 1     | 0 | 0   | NLE    |

For B etc., the LE combination is now discovered.

For both A and B etc., one cannot tell from these truth tables alone whether or not a fix is required. In both cases, the value for A/B is unknown, and so it is unknown whether or not the outcome would be different if the `not I` condition were dropped.

In these cases, it is necessary to force the evaluation of A/B. This can be done by saving the rows associated with the LE combination, and evaluating the A/B condition.

