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

Low-Effect Detection is where we find conditions or groups of conditions that have very little effect on the AIDs that comprise the result of a query. 

In principle, LED can be used to defend against difference attacks by removing the effect of LE conditions on the answer. This would have the same effect as dropping the condition from the query. There are however two difficulties here. First, one can't simply drop the condition, because a given condition or condition combination can be LE for some answer buckets, and not LE (NLE) for others. This suggests that the mechanism for LED can't be dropping conditions per se, but rather adjusting answers to nullify the condition effect.

Second, it seems unlikely to me that we'll be able to perfectly eliminate the effect of a condition in all cases. Counts we can probably do alright, but there might be small errors do to machine precision or something for non-integer aggregates. Therefore we'll probably still need some kind of dynamic noise to protect against first derivative difference attacks. There may also be cases where we can't adjust aggregate outputs at all.

Given this, the basic idea now is to detect LE conditions using a noisy threshold, build dynamic layers as described above (use condition semantics of NLE conditions), and adjust aggregate outputs based on LE conditions.



> TODO: Determine whether adjusting the aggregate output allows us to eliminate the need for the AID noise layers, or reduce noise. This might be a good noise reduction technique for common cases.


## Identifying LE conditions (combinations)

As of this writing, I'm assuming that the identification of LE conditions takes place in the query engine itself. I'm assuming that the query engine processes conditions in some order, and stops when a given row is determined to be true (include in the answer) or false. In other words, not all conditions are examined, and it could even be that some conditions are never examined.

The basic idea to identifying LE combinations is to build a truth table for each bucket, where by *bucket* I mean the output rows of the query. Each condition is labeled as true (1), false (0) or unknown (-), and the outcome of each combination is labeled as true or false. Each row in the truth table is a combination. For each combination encountered by the query engine, we keep track of the number of distinct AIDs for each AID column so long as the number of distinct AIDs is below the LE threshold.

A *combination* is a set of one or more conditions. A combination is LE when the number of distinct AIDs associated with the combination is below a noisy threshold.

Here is an example of such a truth table with four combinations (C1, C2, C3, and C4):

|    | A     | B          | C          | outcome | AID1 | AID2 |
| -- | ----- | ---------- | ---------- | ------- | ---- | ---- |
| C1 | false | ---------- | ---------- | false   | NLE  | NLE  |
| C2 | true  | true       | ---------- | true    | NLE  | NLE  |
| C3 | true  | false      | true       | true    | LE   | NLE  |
| C4 | true  | false      | false      | false   | NLE  | NLE  |

This corresponds to the logic `A and (B or C)` evaluated in the order  `A-->B-->C` (where `A` represents a condition like `age <> 0`).

At the end of query engine processing, we know if any combinations are LE for the given bucket. For instance, in the above example, the combination C3 is LE for AID1.

If any combination is LE, then it may be that we need to adjust the aggregate output. We only want to do that, however, if the analyst could in fact generate an attack pair by removing one or more conditions. This is the case when dropping or negating the condition 1) changes the logical outcome of the LE combination, and 2) does not change the logical outcome for NLE combinations.

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

zzzz flesh this out with example ... Unlike the case with prior Diffix versions, we can now always have layers seeded by the column values, including for instance for ranges. This will allow us for instance to automatically prevent extra noise samples the case where a range is equivalent to an equality, i.e. `col BETWEEN 1 and 2` where `col` is an integer type.

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

- (This represents attack where A is `dept=``'``CS``'` and B is `gender=``'``M``'` and there is one woman in the CS dept.)
- C3 is LE. Dropping B (setting it to true) only changes C3, so the C3 rows can be flipped, and B is dropped from dynamic seed material.

Case 3:

- (This represents the case where conditions A and B are semantically identical. An attacker might try this for instance to amplify the noise component and try to deduce what the noise is.)
- Same outcome as case 2, but here there are no rows to flip.
- Note that this example, among others, presumes that condition evaluation by the query engine always stops as soon as C1 is determined to be false. If this is not the case, then  most likely C1 would be split into two combinations, both NLE with `A=false`, and one with `B=true` and one with `B=false`. In this case, the LE logic would need to be smart enough to recognize that setting B to true would not change the outcome of combinations with `A=false`.

Case 4:

- (This represents a case where B and A mostly overlap, but neither is a subset of the other. Note this is extremely rare.)
- Both C1 and C3 are LE. Dropping A would only change C1, and dropping B would only change C3. An attacker could make an attack with either. Note in particular that flipping the rows associated with C1 or C3 would cause those rows to fall under C2. Since C2 is already NLE, it would remain so after flipping, so we flip the rows for both C1 and C3. 
    - Note here that effectively we are evaluating what would happen if we drop A but not B, and separately evaluating what would happen if we drop but not A. In other words, we don't evaluate what would happen if we were to drop both A and B together, which would in fact only produce the logic (`true AND true)`.
- We drop both A and B from dynamic seeding. In this case, the dynamic seeding material is some default value.

Case 5:

- (This represents the case where B is a subset of A. In other words, whenever A is true, B is also true.)
- This similar to Case 4, with the exception that there is nothing to flip for C3.


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
- Dropping B only effect the C3 rows (LE), so we flip the C3 rows and drop B from dynamic seeding.

Case 4:

- (This represents a case where B and A mostly overlap, but neither is a subset of the other.)
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

- Here both C1 and C2 are LE, but one with an outcome of false, and one with an outcome of true. Let's suppose that A is `gender =` `'``M``'`, and that there is one female which is why C1 is LE. Let's suppose that B is `race =` `'``Eskimo``'`, and that there is only one Eskimo, which is why C2 is LE. Suppose that C is `party =` `'``Dem``'`. The attacker could drop either A or B in an attack. Note that the woman cannot also be the Eskimo, because then condition C2 would not have occurred at all.
- Suppose that the attacker drops A. Then the woman's row flips to included, and contributes to either C3 or C4, depending on whether the woman is democrat or not. Either way, the row won't go to C2 (because the woman isn't an Eskimo), so there is no chance that dropping A causes C2 to become NLE.
- Suppose that the attacker drops B. Then B is set to false, in which case the row will either match C3 (and still be included) or c4 (and be excluded). Either way, the row will not change C1, and so there is no way that C1 can go from LE to NLE.
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

- A can't be used in an attack because it would change the NLE C1 rows. Be can't be used in an attack because it would change C1 or C2 rows (probably both). C can't be used in an attack because it would change the NLE C5 rows.

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

**Example 5:** `(A and B) or (A``'` `and C)`, evaluation `A-->B-->A``'``-->C`
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

When all possible combinations are NLE, or when there are no conditions sets that lead to an attack (affect an LE number of rows), then no more low-effect processing is necessary. 

Until either of these things happen, however, it is necessary to record row information in case re-evaluation is needed. As long as a combination is LE and at least one condition set is droppable, the rows associated with those conditions must be retained for possible re-evaluation. If a combination becomes NLE, then rows for that combination no longer need to be retained, and earlier retained rows can be forgotten. (Note that this presumes that an aggregate can be adjusted incrementally through added or removed rows, in contrast to an aggregate that has to be recomputed from scratch if rows change.)

If there are LE combinations and droppable condition sets, re-evaluation may be required (though not necessarily). When a condition set is being re-evaluated, if its true/false value for a given combination is the same as the value that it is being set to, then the rows don't need to be re-evaluated, because the outcome won't change. Otherwise the rows do need to be re-evaluated.


## GROUP BY aggregates

When there is a GROUP BY (GB) aggregate in a query, the LE metadata is maintained separately for each group. In the case of an intermediate GB (i.e. in a sub-query), LE adjustment is not made on the intermediate group buckets. Rather, the metadata is passed on downstream to the next SELECT. The decision to actually drop conditions for any given group bucket is made only for the outermost GB aggregates. Doing it this way avoids the problem of too much suppression at an intermediate GB.

By way of example, consider the following query:

    SELECT cnt, count(*)
    FROM (
        SELECT trans_date, count(*) as cnt
        FROM transactions
        WHERE gender = 'M'
        GROUP BY 1 ) t
    GROUP BY 1

Let's suppose that `trans_date` is an isolating column, so most of the `trans_date` buckets in the sub-query would by themselves be low count. Many low-count buckets would naturally also be LE. We don't want to start dropping buckets, or for that matter drop the gender condition, until we have computed the `cnt` buckets in the outer SELECT. This is because many of the `trans_date` buckets from the inner GB will be re-combined in the outer GB, and will therefore be no longer low-count or LE. In other words, we don't want to treat the inner GB as an anonymizing query.

What we need to do here is, during query processing of the inner `SELECT`, for each `trans_date`, record the combinations truth table with associated AIDs and rows. Then during query processing of the outer `SELECT`, we merge the combinations, AIDs, and rows into the `cnt` buckets.

For example, after query processing of the inner `SELECT`, we might have this (where here `A` represents the condition `gender =` `'``M``'`):

| trans_date = 194800 | cnt = 5 | A=false: AIDs = [1], rows = [R1, R2]<br>A=true:  AIDs=[1,3], rows = [R4, R5, R6] |
| ------------------- | ------- | -------------------------------------------------------------------------------- |
| trans_date = 193898 | cnt = 5 | A=false: AIDs = [1], rows = [R7]<br>A=true:  AIDs=[2,4], rows = [R8, R9]         |
| ...                   | ...       | ...                                                                                |

The first two rows both have `cnt=5` and so would be combined in the outer select. The combined data would be:

| cnt = 5 | count(*) = 2 | A=false: AIDs = [1], rows = [R1, R2, R7]<br>A=true:  AIDs=[1,3, 2, 4], rows = [R4, R5, R6, R8, R9] |

Here we see that the combination `A=true` is NLE, while `A=false` is LE. For this bucket, then, we would drop the condition, and include rows R1, R2, and R7. Doing so, however would cause the `trans_date = 194800` bucket to go from `cnt=5` ot `cnt=7` (because R1 and R2 are now included), and would cause the `trans_date = 193898` bucket to go from `cnt=5` ot `cnt=6` (because R7 is now included). As a result, the `count()` value for the `cnt=5` bucket would be reduced by two, and the `count()` **values for both the `cnt=6` and `cnt=7` buckets would be increased by one each.


> TODO: Think about whether an attack is actually possible with an inner select like this


# Seed Materials (equalities)

Seeding will follow pretty much the same approach as we have today with the non-integrated cloak. Effectively we want to know what column values are included (for pands and pors) or excluded (for nands and nors) by the condition. 

What we want to do is record every value for every row for positive conditions that lead to an included row, and for negative conditions that lead to an excluded row. We can do this as a bloom filter for equalities. (We discuss how to seed for inequalities and regex elsewhere.)

So basic idea is to add each value to the bloom filter when the row is first evaluated. In principle we have the problem of how to remove a value from the bloom filter if a row is removed during re-evaluation, but my guess is that we won't need to worry about it. 


> TODO: Think about whether an attack is enabled if we don't update the column value bloom filter with row removal.

One good point of using a bloom filter is that they join together nicely with a logical OR operation, which makes it efficient for a distributed computation.


> TODO: An open question is how to deal with column indexes. We can address this when we see what information is associated with indexes.
----------
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


