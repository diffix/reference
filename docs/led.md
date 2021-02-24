# Dynamic noise layers

In previous designs, the dynamic noise layers were based on AIDs (UIDs). The purpose of the dynamic noise layer is to defend against the first derivative difference attack. This is needed because the static noise layers constitute a predictable difference between the "left" and "right" queries in the attack pair. The dynamic layer is not predictable, because each bucket in the attack has a different set of AIDs, and therefore a different dynamic noise layer.

One tricky point about the dynamic layer is that it must not be possible to generate many different dynamic noise layers for what is semantically the same query, thus averaging out the dynamic noise. To avoid this, we use the AIDs themselves in part to seed the dynamic noise layer. Queries with the same semantics will always produce the same set of AIDs.

One of the difficulties with using AIDs with proxy Diffix was the cost of recording them. In the stats-based noise approach, we were not able to collect all of the AIDs, and so used the work-around of recording the min, max, and count. With DB Diffix, it should be reasonable to collect per-AID data, but it may be costly to recompute the set of distinct AIDs when adjusting for Low Effect (LE).

With DB Diffix it appears that we can use the conditions themselves as the basis of dynamic noise layers instead of AIDs. There are two reasons. First, if we are doing Low Effect Detection (LED), then an attacker would not be able to use chaff conditions to average away dynamic noise layers. Second, with tight DB integration, we can do a good job of determining the semantics of each condition, and therefore prevent conditions with different syntax but same semantics.

This opens the possibility of using the seed material from the non-LE (NLE) conditions as the basis for the dynamic noise layers rather than the AIDs. Essentially we can used the combined seed material of all NLE conditions to replace the set of AIDs that we formerly used. This seed material is then added to each individual conditions seed material to compose the per-conditions dynamic noise layers (note both LE and NLE conditions still have dynamic noise layers, though the seed material itself comes only from NLE conditions).

(Note that we still need per-condition dynamic noise layers. This is because in a difference attack, the condition that isolates the victim will always end up being LE, and so is ignored for the purpose of seeding dynamic noise layers. This would lead to the dynamic noise layer being identical on the left and right.)

----------

# Glossary

In the following discussion of low effect detection we will be using the following terms:

- **Bucket**: a single aggregate row in the output (or multiple rows in the case of implicit aggregation). [The definition can be found here](https://github.com/diffix/strategy/issues/6).
- **Combination**: is a set of truth values associated with both conditions and a bucket. Condition `A` has two combinations: `A = true` and `A = false`. Either or both might exist for a bucket.
- **Low effect**: the number of distinct AIDs associated with a bucket or combination is below a threshold.


# LED

Low-Effect Detection is where we find conditions or groups of conditions that have very little effect on the AIDs that comprise the result of a query.

In principle, LED can be used to defend against difference attacks by removing the effect of LE conditions on the answer. This would have the same effect as dropping the condition from the query. There are however two difficulties here. First, one can't entirely drop the condition from a query, because a given condition or condition combination can be LE for some answer buckets, and not LE (NLE) for others. This suggests that the mechanism for LED can't be dropping conditions per se, but rather adjusting answers to nullify the condition effect.

Second, it seems unlikely to me that we'll be able to perfectly eliminate the effect of a condition in all cases. Counts we can probably do alright, but there might be small errors do to machine precision or something for non-integer aggregates. Therefore we'll probably still need some kind of dynamic noise to protect against first derivative difference attacks. There may also be cases where we can't adjust aggregate outputs at all.

> TODO: Note: Sebastian doesn't necessarily agree. We'll find out as we try things out.

Given this, the basic idea now is to detect LE conditions using a noisy threshold, build dynamic layers as described above (use condition semantics of NLE conditions), and adjust aggregate outputs based on LE conditions.

> TODO: Determine whether adjusting the aggregate output allows us to eliminate the need for the AID noise layers, or reduce noise. This might be a good noise reduction technique for common cases.


## Identifying LE conditions using combinations

It is assumed that the identification of LE conditions takes place in the query engine itself. Furthermore it is assumed that the query engine processes conditions in some order that the analyst cannot influence, and stops when a given row is determined to be true (include in the answer) or false. In other words, not all conditions are necessarily examined, in fact some conditions might never be examined.

The basic idea to identifying LE combinations is to build a truth table for each bucket, where by *bucket* I mean the output rows of the query. Each condition is labeled as true (1), false (0) or unknown (-), and the outcome of each combination is labeled as true or false (true means the associated rows will be included in the bucket, and false that they will be excluded). Each row in the truth table is a combination. For each combination encountered by the query engine, we keep track of the number of distinct AIDs for each AID column so long as the number of distinct AIDs is below the LE threshold.

Here is an example of such a truth table with the four combinations (C1, C2, C3, and C4) corresponding to the logic `A and (B or C)` evaluated in the order `A-->B-->C` (where `A` represents a condition like `age <> 0`):

|    | A     | B          | C          | outcome | AID1 | AID2 |
| -- | ----- | ---------- | ---------- | ------- | ---- | ---- |
| C1 | false | ---------- | ---------- | false   | NLE  | NLE  |
| C2 | true  | true       | ---------- | true    | NLE  | NLE  |
| C3 | true  | false      | true       | true    | LE   | NLE  |
| C4 | true  | false      | false      | false   | NLE  | NLE  |

At the end of query engine processing, we know if any combinations are LE for the given bucket. For instance, in the above example, the combination C3 is LE for AID1.

> TODO: Note: Seb points out that we might know earlier, for instance after the sub-query in a `JOIN`, and there may be advantages to removing the effect earlier in the query. Something to keep in mind.

If any combination is LE, then it may be that we need to adjust the aggregate output. We only want to do that, however, if the analyst could in fact generate an attack pair by removing one or more conditions. This is the case when dropping or negating the condition 1) changes the logical outcome of the LE combination, and 2) does not change the logical outcome for NLE combinations.

> Note: The reason this assumes adjusting the aggregate output (versus adding or removing rows prior to aggregate computation) is that I'm presuming that the query engine may compute aggregates on the fly as it processes rows. For instance, the query engine may add a given row to a `count(*)` aggregate when it encounters the row, and only later might we decide that the row should be removed due to LE. In this case, we'd need to adjust the (already computed) aggregate rather than somehow compute the aggregate all over again.

> Edon says this:

> We can do whatever we want on the transition ("on the fly") phase and final phase. Also the data we store as intermediate state can be anything.

> One idea would be to store a per-combination aggregation. Will we always include or exclude complete "combination groups"? For example:

combination_id | outcome | count
-|-|-
1 | true | 10
2 | true | 2
3 | false | 7
4 | false | 1

> We can hold the partially aggregated count for each combination, and calculate the final at the last step of an aggregate after we know what is flipped and what is not. Say we want to include combination 4, the count would be `10 + 2 + 1 = 13`.

In the above example, C3 is LE. Suppose the analyst were to drop or negate A. This would effectively result in A being set to true, which would (probably) change the outcome of many combination C1 rows to true, causing those rows to be added to the answer. In other words, the change would have a large effect and so can't be used by an attacker in a difference attack or a chaff attack. Therefore we can safely leave the C1 rows as is and keep the dynamic seeding material from A.

If however the analyst were to drop C, then the only logic effect this would have would be to change the rows associated with C3 from true (included in the answer) to false (excluded from the answer). Since therefore a difference attack is possible, we want to adjust for the C3 rows by 1) adjust the aggregate outputs to what they would be if the rows are excluded, and 2) remove C from the seed materials for dynamic noise layers.

Here is another example of a truth table, this time with five combinations:

|    | A     | B          | C          | D          | outcome | AID |
| -- | ----- | ---------- | ---------- | ---------- | ------- | --- |
| C1 | true  | ---------- | ---------- | ---------- | true    | NLE |
| C2 | false | false      | ---------- | ---------- | false   | NLE |
| C3 | false | true       | false      | ---------- | false   | NLE |
| C4 | false | true       | true       | false      | false   | NLE |
| C5 | false | true       | true       | true       | true    | LE  |

This corresponds to the logic `A or (B and C and D)`, evaluated in order `A-->B-->C-->D`. This might be a setup for a difference attack where `(B and C and D)` comprise a pseudo-identifier for the victim. The paired query in the difference attack would exclude the pseudo-identifier. Combination C5 is the only LE combination.

Suppose we consider dropping condition D. That would cause D to be always true, which would change all of the C4 rows to true. Since C4 is NLE (has more than the threshold number of distinct AIDs) the attack would not work. The same holds for B and C (taken individually). Dropping A would cause A to stay false, and so would not change the C5 outcome (and as well would probably change many C1 rows from true to false).


> TODO: Note that these truth tables are incomplete, in that database logic is ternary (True, False, NULL). I'm not sure that this has any practical impact on the mechanisms described here. Ultimately we want to know if dropping a condition will flip rows or not (i.e. cause excluded rows to be excluded and vice versa). Something to keep in mind.

By dropping B, C, and D (or in other words, dropping the expression `(B and C and D)`, however, only the C5 rows change. This is because the expression essentially becomes false (because it is a por), which doesn't change the outcome of any other combinations. Therefore the aggregates would be adjusted to account for excluding the C5 rows, and B, C, and D would not be used for the dynamic noise seed material. (Though note that there would still be dynamic noise layers associated with B, C, and D.)


> TODO: work out the details of the algorithm for detecting LE condition expressions.

A basic approach to LED, then is:

1. Determine if any combinations are LE. If not, then done.
2. If so, determine if dropping one or more conditions leads to a situation where only an LE number of rows are affected. If not, then done.
3. If so, emulate "dropping" those conditions by changing OR'd conditions or expressions to false, and changing AND'd conditions or expressions to true.
4. Re-evaluate the LE rows under emulated dropping. If any rows are flipped (changed from included to excluded or vice versa), then adjust aggregate values to reflect that.
5. Remove dropped conditions from dynamic seeding material.

Note that not only does this description assume that the query engine optimization doesn't evaluate conditions unnecessarily, it depends on it. If the query engine doesn't do this, then the LE logic has to be sophisticated enough to recognize when evaluated conditions in fact have no effect.

> TODO: Reverse noise exploitation attack needs more thought.
> TODO: Think how to seed noise layer when a condition has zero effect.

## Adjusting aggregate values

We call changing a row from included to excluded, or vice versa, as *flipping* the row. A flipped row can lead to an adjustment in the query's aggregate outputs.

> TODO: Work through the details of this. Not straightforward in many cases. May not even be possible in some cases?

> TODO: Seb suggests we might be able to compute per-combination aggregates, and then compute the final aggregate based on which combinations remain.


## Seeding materials

The purpose of seeding is to make noise layers sticky. The core concept is that the noise associated with a given condition is based on the semantics of that condition. If a condition is a pand (positive and) or a por (positive or), then the semantics of that condition are defined by the rows for which the condition evaluates to true. If a condition is a nand (negative and) or a nor (negative or), then the semantics of that condition are defined by the rows for which the condition evaluates to false.

Note that it is not necessary for the final outcome of a row to match the condition's outcome. If a pand's condition evaluates to true even though the row evaluates to false, the value of the column associated with the condition is still used as seeding material.

As with earlier versions of Diffix, we have both static and dynamic noise layers. Unlike earlier versions, all conditions have both static and dynamic noise layers. (In earlier versions, we left off the dynamic noise layer in some cases because of the chaff attack.)

Recall from earlier versions of Diffix that the static seeding material for a given condition consists primarily of:

1. The column name
2. The operator
3. The values from the column that "match" the condition (where a match if the value of included rows for pands and pors, or excluded rows for nands and nors)

We want to keep this approach, but with DB integration we can monitor which column values match during query execution. This in turn means that negative conditions no longer need to be clear. (Though we may have other reasons to limit math.)

Dynamic noise layers have the above seeding material, plus the seeding material taken from the conditions that are not droppable. This seeding material takes the place of the AID information used in prior Diffix versions. The additional seeding material for all dynamic noise layers comprises all of the static seeding material from all non-droppable conditions. Note that it is strictly speaking possible that all conditions are excluded from the dynamic seeding material. In this case, some default value is used (which has the effect of still making the dynamic layers different from the static layers).

Unfortunately it can happen that a condition is never evaluated by the query engine, and yet we want to make noise layers and so need seeding material. I think in this case it should be safe to use some default value (i.e. `NULL`) in place of the column value.

It probably makes sense to monitor the column values using a bloom filter, and then using the bloom filter directly as seeding material. Besides being compact and efficient, an advantage of the bloom filter is that it can be combined with other bloom filters, thus working in a distributed implementation. An important point is that the bloom filter doesn't have to be perfectly accurate when there are many different column values. The primary purpose of static noise layers is to prevent an attacker from learning an exact count for some attribute, and if the attribute has many different values, this is not useful for the attacker. Since the bloom filter doesn't have to be perfectly accurate, the size of the filter can be relatively small, and the hash function can favor efficiency over randomness.

As an efficiency measure, we can still determine the column value of a clear condition from SQL inspection only. In this case, however, to generate the same seeding material that would have come from building a bloom filter, we can simply insert the inspected value into a bloom filter.

Strictly speaking, we would want to remove column values from the bloom filters when rows are flipped (from included to excluded, or from excluded to included). To avoid having to use counting bloom filters for this, perhaps what we can do is hold off on inserting column values into bloom filters until it is determined that a given condition won't be dropped.

> TODO: flesh this out with example ... Unlike the case with prior Diffix versions, we can now always have layers seeded by the column values, including for instance for ranges. This will allow us for instance to automatically prevent extra noise samples the case where a range is equivalent to an equality, i.e. `col BETWEEN 1 and 2` where `col` is an integer type. Note however that if we were to do this, then in the process we'd want to recognize that the operator is effective `=` and not `BETWEEN`, and generate the seed materials accordingly. Or perhaps not even use operator as part of the seed material.

**Seeding materials implementation**

With DB integration it is no longer necessary to float values per se. Rather column values can be recorded as they occur in the query engine and stored in some data structure "on the side". We might have to do specialized things when dealing with DB indexes, for instance if using an index means that we no longer process every row.


> TODO: Learn more about DB indexes.


## Abstract Logic Examples

These examples cover how the LED logic works, and assume that each condition is independent (i.e. the outcome of one condition does not change the outcome of another condition).

**Example 1**: `A and B`, Evaluation order A-->B

|       | A    | B          | out    | case 1     | case 2 | case 3 | case 4 | case 5 |
| ----- | ---- | ---------- | ------ | ---------- | ------ | ------ | ------ | ------ |
| C1    | 0    | ---------- | 0      | NLE        | NLE    | NLE    | LE     | LE     |
| C2    | 1    | 1          | 1      | NLE        | NLE    | NLE    | NLE    | NLE    |
| C3    | 1    | 0          | 0      | NLE        | LE     | 0      | LE     | 0      |
| drop- | able | cond-      | itions | ---------- | B      | B      | A, B   | A, B   |

Case 1:

- No LE combinations, so no flipped rows. All seed material for A and B come from C2 rows.

Case 2:

- (This represents attack where A is `dept='CS'` and B is `gender='M'` and there is one woman in the CS dept.)
- C3 is LE. Dropping B (setting it to true) only changes C3, so the C3 rows can be included (flipped), and B is dropped from dynamic seed material.

Case 3:

- (This represents the case where conditions A and B are semantically identical. An attacker might try this for instance to amplify the noise component and try to deduce what the noise is.)
- Same outcome as case 2, but here there are no rows to flip.
- Note that this example, among others, presumes that condition evaluation by the query engine always stops as soon as C1 is determined to be false. If this is not the case, then  most likely C1 would be split into two combinations, both NLE with `A=false`, and one with `B=true` and one with `B=false`. In this case, the LE logic would need to be smart enough to recognize that setting B to true would not change the outcome of combinations with `A=false`.

Case 4:

- (This represents a case where B and A mostly overlap, but neither is a subset of the other. Note this is extremely rare.)
- Both C1 and C3 are LE. Dropping A would only change C1, and dropping B would only change C3. An attacker could make an attack with either. Note in particular that flipping the rows associated with C1 or C3 would cause those rows to fall under C2. Since C2 is already NLE, it would remain so after flipping, so we flip the rows for both C1 and C3.
    - Note here that effectively we are evaluating what would happen if we drop A but not B, and separately evaluating what would happen if we drop B but not A. In other words, we don't evaluate what would happen if we were to drop both A and B together, which would in fact only produce the logic (`true AND true)`.
- We drop both A and B from dynamic seeding. In this case, the dynamic seeding material is some default value.

Case 5:

- (This represents the case where the rows matching B are a subset of the rows matching A. In other words, whenever A is true, B is also true.)
- This similar to Case 4, with the exception that there is nothing to flip for C3. This means we include (flip) the rows associated with `C1` and consider neither `A` nor `B` for dynamic noise layer seeding.


**Example 2**: `A or B`, evaluation order A-->B

|       | A    | B          | out   | case 1     | case 2 | case 3 | case 4 | case 5 |
| ----- | ---- | ---------- | ----- | ---------- | ------ | ------ | ------ | ------ |
| C1    | 1    | ---------- | 1     | NLE        | NLE    | NLE    | LE     | NLE    |
| C2    | 0    | 0          | 0     | NLE        | NLE    | NLE    | NLE    | LE     |
| C3    | 0    | 1          | 1     | NLE        | LE     | 0      | LE     | 0      |
| drop- | able | cond-      | tions | ---------- | B      | B      | A, B   | B      |

Case 1:

- No LE conditions, so nothing is done.

Case 2:

- C3 is LE. Dropping B (setting it to false) only changes C3, so the C3 rows can be flipped, and B is dropped from dynamic seed material.

Case 3:

- (This represents the case where conditions A and B are semantically identical. An attacker might try this for instance to amplify the noise component and try to deduce what the noise is.)
- Dropping B only effect the C3 rows (of which there are none), so drop B from dynamic seeding.

Case 4:

- (This represents a case where B and A mostly overlap, but neither is a subset of the other. Note that it is possible that the combined rows from C1 and C3 are enough to pass LCF, even though each combination individually is LE.)
- Both A and B are droppable. Note that by flipping (excluding) their rows, the bucket in any event becomes LCF and is suppressed.

Case 5:

- (This represents the case where B is a subset of A. In other words, whenever A is true, B is also true.)
- Dropping B (set to false) only affects C3, so can be used as an attack. Dropping A affects C1, which is NLE, so can't be used in an attack. Note that the fact that C2 is LE doesn't lead to an attack.

**Example 3:** `A and (B or C)`, evaluation `A-->B-->C`

|    | A | B          | C          | out  | case 1 | case 2 | case 3 | case 4 |
| -- | - | ---------- | ---------- | ---- | ------ | ------ | ------ | ------ |
| C1 | 0 | ---------- | ---------- | 0    | NLE    | LE     | LE     | NLE    |
| C2 | 1 | 1          | ---------- | 1    | NLE    | NLE    | LE     | LE     |
| C3 | 1 | 0          | 1          | 1    | NLE    | NLE    | NLE    | LE     |
| C4 | 1 | 0          | 0          | 0    | NLE    | NLE    | NLE    | NLE    |
|    |   |            |            | DROP |        | A      | A, B   | B, C   |

Case 1:

- No LE combinations, so do nothing.

Case 2:

- C1 is LE. Dropping A (change to true) only changes C1. Flip C1 rows, remove A from dynamic seeding.

Case 3:

- Here both C1 and C2 are LE, but one with an outcome of false, and one with an outcome of true. Let's suppose that A is `gender = 'M'`, and that there is one female which is why C1 is LE. Let's suppose that B is `race = 'Eskimo'`, and that there is only one Eskimo, which is why C2 is LE. Suppose that C is `party = 'Dem'`. The attacker could drop either A or B in an attack. Note that the woman cannot also be the Eskimo, because then condition C2 would not have occurred at all.
- Suppose that the attacker drops A. Then the woman's row flips to included, and contributes to either C3 or C4, depending on whether the woman is democrat or not. Either way, the row won't go to C2 (because the woman isn't an Eskimo), so there is no chance that dropping A causes C2 to become NLE.
- Suppose that the attacker drops B. Then B is set to false, in which case the row will either match C3 (and still be included) or C4 (and be excluded). Either way, the row will not change C1, and so there is no way that C1 can go from LE to NLE.
- Therefore both A and B are dropped, the corresponding rows are re-evaluated accordingly, and neither A nor B are included in the dynamic noise seeding.

Case 4:

- Dropping A affects C1 (NLE), so can't be used in an attack.
- Dropping B only affects C2, so it is dropped. Note that this would cause the victim to match either C3 or C4, depending on the political party. If the victim is democrat, this could cause C3 to go from LE to NLE.
- Dropping C only affects C3, so it is dropped. Note that the fact that dropping B could have caused C3 to become NLE, we nevertheless must drop C because the attacker might plan to drop C and not B.

**Example 4:** `A and (B or C)`, evaluation `B-->C-->A` (Note same logic as example 3, but different evaluation order.)

|    | A          | B | C          | out  | case 1 | case 2 | case 3 | case 4 |
| -- | ---------- | - | ---------- | ---- | ------ | ------ | ------ | ------ |
| C1 | 0          | 1 | ---------- | 0    | NLE    | LE     | LE     | NLE    |
| C2 | 1          | 1 | ---------- | 1    | NLE    | NLE    | LE     | LE     |
| C3 | ---------- | 0 | 0          | 0    | LE     | LE     | LE     | LE     |
| C4 | 0          | 0 | 1          | 0    | LE     | LE     | LE     | NLE    |
| C5 | 1          | 0 | 1          | 1    | NLE    | NLE    | NLE    | LE     |
|    |            |   |            | DROP |        | (A)    | (A), B | B, C   |

`A and (B or C)`, evaluation `B-->C-->A`

Case 1:

- A can't be used in an attack because it would change the NLE C1 rows. B can't be used in an attack because it would change C1 or C2 rows (probably both). C can't be used in an attack because it would change the NLE C5 rows.

Case 2:

- If A dropped (changed to true), then outputs of C1 and C4 change. Both are LE, but if taken together (combined distinct AIDs) they are not LE, then A cannot be used in an attack. Otherwise it can.
- If B dropped (changed to false), then the outcome of C2 (NLE) changes, so cannot be used in an attack.
- If C dropped (changed to false), then the outcome of C5 (NLE) changes, so cannot be used in an attack.

Case 3:

- Many male (A), non-Eskimo (B) democrats (C). Not much of anything else.
- If A dropped (set to true), then the outcomes of both C4 and C1 go to true. Although both of these are LE, taken together they might not be LE. If the latter, then A cannot used as an attack. Otherwise it can.
- If B dropped (set to false), then the outcome of C2 may change. Therefore it can be used as an attack.
- If C is dropped (set to false), then the outcome of C5 changes (NLE), so C cannot be used in an attack.

Case 4:

- Many females (A), both Eskimo and other races (B). Few males. The females are excluded, so this case might be LCF (but might not).
- If we drop A (set to true), then both C1 and C4 are affected, so A (gender) can't be used for an attack.
- If we drop B (set to false), only C2 is affected (the outcome of C1 remains false regardless).  So B could be used in an attack.
- If C is dropped (set to false), it affects only C5, which is LE, so could be used in an attack.
-  Note that dropping B and C would make the outcome LCF.

**Example 5:** `(A and B) or (A' and C)`, evaluation `A-->B-->A'-->C`
Note that in this example A and A' are fully redundant (semantically identical). It is equivalent to `A and (B or C)`. The cases correspond to those of Example 3.

|    | A | B          | A'         | C          | out  | case 1     | case 2   | case 3              | case 4                |
| -- | - | ---------- | ---------- | ---------- | ---- | ---------- | -------- | ------------------- | --------------------- |
| C1 | 0 | ---------- | 0          | ---------- | 0    | NLE        | LE       | LE                  | NLE                   |
| C2 | 1 | 1          | ---------- | ---------- | 1    | NLE        | NLE      | LE                  | LE                    |
| C3 | 1 | 0          | 1          | 1          | 1    | NLE        | NLE      | NLE                 | LE                    |
| C4 | 1 | 0          | 1          | 0          | 0    | NLE        | NLE      | NLE                 | NLE                   |
|    |   |            |            |            | DROP | ---------- | A and A' | A and A', (A and B) | (A and B), (A' and C) |



Case 1:

- No LE combinations, so do nothing.

Case 2:

- C1 is the only LE combination. Dropping A or A' alone (change to true) doesn't change the outcome of C1. However, dropping both does, so together they can be used as an attack so need to both be dropped.

Case 3:

- Many male (A) non-Eskimos (B), both democrats and republicans. Few women or Eskimos.
- As with case 2, dropping A or A' alone doesn't change the outcome. Dropping both affects only C1, and so *can be used in an attack*.
- Dropping B alone affects C4 (NLE), so can't be an attack. Changing C likewise affects C4 and can't be an attack.
- Dropping both A and B may cause a change in the outcome of C2 (LE), and *can therefore be an attack*.
- We re-evaluate the rows of C1 by changing both A and A' to true. We re-evaluate the rows of C2 by changing the expression `(A and B)` to false.
- The only condition used as part of the dynamic seeding then is C.

Case 4:

- Note that this case is in any event probably LCF, even without adjustments.
- Dropping both A and A' affects C1 (NLE), so can't be used in an attack. Dropping B or C affects C4 (NLE), so they can't be used in an attack.
- Dropping the expression (A and B) may change C2, so can be part of an attack. (Though this only removes rows and so is even more likely LCF.)
- Dropping the expression (A' and C) would effect C3 (cause the rows to be excluded).
- Note that after dropping both expressions, the condition logic becomes `false or false`, which certainly makes the whole query LCF.

**Example** **6****:** `(A and B) or (C and D)`: Evaluation  `A-->B-->C-->D`

|    | A | B          | C          | D          | out  | case 1     | case 2     | case 3    |
| -- | - | ---------- | ---------- | ---------- | ---- | ---------- | ---------- | --------- |
| C1 | 1 | 1          | ---------- | ---------- | 1    | NLE        | NLE        | LE        |
| C2 | 1 | 0          | 1          | 1          | 1    | NLE        | NLE        | NLE       |
| C3 | 1 | 0          | 1          | 0          | 0    | NLE        | NLE        | NLE       |
| C4 | 1 | 0          | 0          | ---------- | 0    | NLE        | NLE        | NLE       |
| C5 | 0 | ---------- | 1          | 1          | 1    | NLE        | NLE        | NLE       |
| C6 | 0 | ---------- | 1          | 0          | 0    | NLE        | NLE        | NLE       |
| C7 | 0 | ---------- | 0          | ---------- | 0    | NLE        | LE         | NLE       |
|    |   |            |            |            | DROP | ---------- | ---------- | (A and B) |

Case 1:

- All combinations are NLE so no possible attack.

Case 2:

- C7 is LE.
- Dropping A may affect C6 (NLE). The only case where dropping A would have an LE effect is where B is almost always true when C6 is false. This is likely rare, so we an disregard (as opposed to re-evaluating C6 with A set to true, which would require repeating the query).
- Dropping C may affect C4 (NLE), so similar story to dropping A.
- Dropping both A and C affects both C6 and C4, so similar story to dropping either.
- Note this is an example of where there is an LE combination, but nothing can be dropped that leads to an attack, and therefore no adjustment is needed in spite of the LE combination.

Case 3:

- Here the expression `(A and B)` isolates a victim (C1 is LE).
- Dropping only A affects C6 and C7, so unlikely attack condition. Likewise dropping only B affects C3 and C4.
- Dropping both A and B only affects C1, so the expression can be an attack condition.

**Example** **7****:** `(A or B) and (C or D)`: Evaluation  `A-->B-->C-->D`

|    | A | B          | C          | D          | out  | case 1     |
| -- | - | ---------- | ---------- | ---------- | ---- | ---------- |
| C1 | 1 | ---------- | 1          | ---------- | 1    | NLE        |
| C2 | 1 | ---------- | 0          | 1          | 1    | NLE        |
| C3 | 1 | ---------- | 0          | 0          | 0    | NLE        |
| C4 | 0 | 1          | 1          | ---------- | 1    | NLE        |
| C5 | 0 | 1          | 0          | 1          | 1    | NLE        |
| C6 | 0 | 1          | 0          | 0          | 0    | LE         |
| C7 | 0 | 0          | ---------- | ---------- | 0    | NLE        |
|    |   |            |            |            | DROP | ---------- |

Case 1:

- There is no droppable condition or expression that leads to an attack so far as I can tell.
- (Note equivalent to `(A and C) or (A and D) or (B and C) or (B and D)`. Each condition appears in two expressions, so hard to eliminate any given expression without messing up the others. This, I think, is why we don't find droppable conditions here.)

## LE Implementation

LE Implementation requires two types of metadata information per combination:

1. For each AID type, the number of distinct AIDs. This is needed to determine if the combination is LE or NLE.
2. The rows included or excluded by each combination. Rows only need to be stored so long as the number of distinct AIDs is below the threshold.

Regarding the per-combination AID metadata, it is probably most efficient just to store two AID values per AID type. During query processing, as soon as a third distinct AID value is encountered, then the combination can be labeled as NLE. Once a combination is NLE, it can't go back to LE and so no further processing is needed for that combination.


> TODO: still not 100% sure that a combination can't go from NLE to LE...

When all possible combinations are NLE, or when there are no conditions sets that lead to an attack (affect a LE number of rows), then no more low-effect processing is necessary.

Until either of these things happen, however, it is necessary to record row information in case re-evaluation is needed. As long as a combination is LE and at least one condition set is droppable, the rows associated with those conditions must be retained for possible re-evaluation. If a combination becomes NLE, then rows for that combination no longer need to be retained, and earlier retained rows can be forgotten. (Note that this presumes that an aggregate can be adjusted incrementally through added or removed rows, in contrast to an aggregate that has to be recomputed from scratch if rows change.)

If there are LE combinations and droppable condition sets, re-evaluation may be required (though not necessarily). When a condition set is being re-evaluated, if its true/false value for a given combination is the same as the value that it is being set to, then the rows don't need to be re-evaluated, because the outcome won't change. Otherwise the rows do need to be re-evaluated.


## GROUP BY aggregates

When there is a GROUP BY aggregate in a query, the LE metadata is maintained separately for each group. In the case of an intermediate GROUP BY (i.e. in a sub-query), LE adjustment is not made on the intermediate group buckets. Rather, the metadata is passed on downstream to the next SELECT. The decision to actually drop conditions for any given group bucket is made only for the outermost GROUP BY aggregates. Doing it this way avoids the problem of too much suppression at an intermediate GROUP BY.

By way of example, consider the following query:

    SELECT cnt, count(*)
    FROM (
        SELECT trans_date, count(*) as cnt
        FROM transactions
        WHERE gender = 'M'
        GROUP BY 1 ) t
    GROUP BY 1

Let's suppose that `trans_date` is an isolating column, so most of the `trans_date` buckets in the sub-query would by themselves be low count. Many low-count buckets would naturally also be LE. We don't want to start dropping buckets, or for that matter drop the gender condition, until we have computed the `cnt` buckets in the outer SELECT. This is because many of the `trans_date` buckets from the inner GROUP BY will be re-combined in the outer GROUP BY, and will therefore be no longer low-count or LE. In other words, we don't want to treat the inner GROUP BY as an anonymizing query.

What we need to do here is, during query processing of the inner `SELECT`, for each `trans_date`, record the combinations truth table with associated AIDs and rows. Then during query processing of the outer `SELECT`, we merge the combinations, AIDs, and rows into the `cnt` buckets.

Following is an example from Seb that demonstrates that the above idea may not be so simple:


| trans_date | cnt | combinations  state |
| ------------------- | ------- | -------------------------------------------------------------------------------- |
| trans_date = 194800 | cnt = 5 | A=false: AIDs = [0], rows = [R1, R2]<br>A=true:  AIDs=[1, 3], rows = [R3, R4, R5, R6, R7] |
| trans_date = 193898 | cnt = 5 | A=false: AIDs = [0], rows = [R8]<br>A=true:  AIDs=[2, 4], rows = [R9, R10, R11, R12, R13]         |
| ...                   | ...       | ...                                                                                |

In this new table, we are in fact excluding a low count number of AIDs (i.e. `AID = 0`).
We would therefore have to adjust for the effect of dropping the `A`-condition here, and would end up with

| cnt | count | combinations  state |
| ------------------- | ------- | -------------------------------------------------------------------------------- |
| cnt = 7 | count(*) = 1 | AIDs=[0, 1, 3, 2, 4], rows = [R1, R2, R8, R3, R4, R5, R6, R7, R9, R10, R11, R12, R13] |
| cnt = 6 | count(*) = 1 | AIDs=[0, 1, 3, 2, 4], rows = [R1, R2, R8, R3, R4, R5, R6, R7, R9, R10, R11, R12, R13] |
| ...                   | ...       | ...                                                                                |

However I am not sure if this works either!
The reason is that we somehow need to account for the per-AID contributions too. Otherwise we cannot suppress extreme outlier users!

In what follows I'll model user contributions as `(AID, number of rows)`.

For example, take the following scenario (where I simplify things dramatically by not considering LED, only the handling of multi-level aggregations. LED can be added in later, but first we need to understand the basecase):

| trans_date | cnt | combinations  state |
| ------------------- | ------- | -------------------------------------------------------------------------------- |
| trans_date = 194800 | cnt = 1015 | Contributions = [(0, 1000); (1, 2); (2, 2); (3, 1)]] |
| trans_date = 193898 | cnt = 1004 | Contributions = [(1, 1000); (2, 2); (3, 1); (4, 1); (5, 1)]] |
| ...                   | ...       | ...                                                                                |


We need to account for the fact that the distribution of contributions is all skewed!

Basically we could think of it as follows (where something akin to how we suppress outliers in regular aggregates is applied):

| trans_date | cnt | combinations  state |
| ------------------- | ------- | -------------------------------------------------------------------------------- |
| trans_date = 194800 | cnt = **{1015 -> 7}** | Contributions = [(0, **{1000 -> 2}**); (1, 2); (2, 2); (3, 1)]] |
| trans_date = 193898 | cnt = **{1004 -> 7}** | Contributions = [(1, **{1000 -> 2}**); (2, 2); (3, 1); (4, 1); (5, 1)]] |
| ...                   | ...       | ...                                                                                |

which means we could combine these rows during the final aggregation as follows:


| cnt  | count(*) | combinations  state |
| ------------------- | ------- | -------------------------------------------------------------------------------- |
| cnt = 7 | count(*) = 2 | Contributions = [(0, [**{1000 -> 2}**]); (1, [1; **{1000 -> 2}**]); (2, [2; 2]); (3, [1; 1]); (4, [1]); (5, [1])] |
| ...                   | ...       | ...                                                                                |

which I guess can be interpreted as:
- 4 AIDs with 2 occurrences of `cnt = 7`
- 2 AIDs with 1 occurrence of `cnt = 7`
- Assuming 4 AIDs passes LCF then we can claim,  `count(*) = 2` for `cnt = 7`...

Expanding this for low count, count then maybe look like this?

A=false: AIDs = [0]<br>A=true:

| trans_date | cnt | combinations  state |
| ------------------- | ------- | -------------------------------------------------------------------------------- |
| trans_date = 194800 | cnt = 5 | A=false: Contributions = [(0, 10)]<br>A= true: Contributions = [(1, **{10 -> 2}**); (2, 2); (3, 1)] |
| trans_date = 193898 | cnt = 23 | A=false: Contributions = []<br>A= true:  Contributions = [(4, 10); (5, 10); (6, 3)]  |
| ...                   | ...       | ...                                                                                |

which then in the second phase becomes

| cnt | count(*) | combinations  state |
| ------------------- | ------- | -------------------------------------------------------------------------------- |
| cnt = 5 | count(*) = 1 | A=false: Contributions = [(0, 10)]<br>A= true: Contributions = [(1, **{10 -> 2}**); (2, 2); (3, 1)] |
| cnt = 23 | count(*) = 1 | A=false: Contributions = []<br>A= true:  Contributions = [(4, 10); (5, 10); (6, 3)]  |
| ...                   | ...       | ...                                                                                |

But here we see that `A = false` for `cnt = 5` is a low effect property, and hence it needs to be flipped which results in the contribution of user 1 no longer needing to be interpreted as 2 rather than 10 leading to the row becoming `cnt = 23`:

| cnt | count(*) | combinations  state |
| ------------------- | ------- | -------------------------------------------------------------------------------- |
| cnt = 23 | count(*) = 1 | A=false: Contributions = []<br>A= true: Contributions = [**(0, 10)**, (1, 10); (2, 2); (3, 1)] |
| cnt = 23 | count(*) = 1 | A=false: Contributions = []<br>A= true:  Contributions = [(4, 10); (5, 10); (6, 3)]  |
| ...                   | ...       | ...                                                                                |

which we can then merge with the other `cnt = 23` row:

| cnt | count(*) | combinations  state |
| ------------------- | ------- | -------------------------------------------------------------------------------- |
| cnt = 23 | count(*) = 2 | A=false: Contributions = []<br>A= true: Contributions = [{(0, [10]), (1, [10]); (2, [2]); (3, [1])}; {(4, [10]); (5, [10]); (6, [3])}] |
| ...                   | ...       | ...                                                                                |

which indeed now is showing us that there are two instances of `cnt = 23` where each instance has more than LCF users (assuming that is the criteria to go by).

> TODO: Work through the above cases more carefully

> From Sebastian: I implemented a playground for experimenting with this idea. It can be found here: https://github.com/diffix/experiments/blob/master/Multi-level-aggregation.ipynb

> TODO: Think about whether an attack is actually possible with an inner select like this


# Seed Materials (equalities)

Seeding follows pretty much the same approach as with the non-integrated cloak. Effectively we want to know what column values are included (for pands and pors) or excluded (for nands and nors) by the condition.

What we want to do is record every value for every row that led to a row being included (for positive conditions) or excluded (for negative conditions). We can do this as a bloom filter for equalities. (We discuss how to seed for inequalities and regex elsewhere.)

So basic idea is to add each value to the bloom filter when the row is first evaluated. In principle we have the problem of how to remove a value from the bloom filter if a row is removed during re-evaluation, but my guess is that we won't need to worry about it.


> TODO: Think about whether an attack is enabled if we don't update the column value bloom filter with row removal.

One good point of using a bloom filter is that they join together nicely with a logical OR operation, which makes it efficient for a distributed computation.


> TODO: An open question is how to deal with column indexes. We can address this when we see what information is associated with indexes.