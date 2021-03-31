For the assumed background knowledge, please consult the:
- [glossary](glossary.md) for definitions of terms used in this document
- [multi-aid](multiple-aid.md) for a description of how AIDs spread through a query

# Aggregation across query boundaries

Our goal is to limit the distortion due to anonymization.
Whereas older versions of Diffix anonymized intermediate queries if they aggregated across distinct AID values
causing severe loss of data quality, we now delay the final anonymization decisions to the top-most query.

This design constraint complicates how we handle aggregates.

Consider the following query:

```sql
SELECT num_transactions, count(*), min(avg_spend), max(avg_spend)
FROM (
  SELECT card_type, count(*) as num_transaction, avg(amount) as avg_spend
  FROM transactions
  GROUP BY card_type
) t
GROUP BY num_transactions
```

In this query:

- neither aggregate in sub query `t` should be anonymizing. If they were, we would lose out on infrequent
  card types that we would like to account for in the top-level `count` aggregate
- each `num_transactions` aggregate contains data (potentially) pertaining to multiple entities, some of which
  might have an outsize contribution on the aggregate produced. Keeping track of the individual contributions
  across query boundaries in order to have the ability to later do extreme value flattening if necessary is feasible
  for `count` and `sum` aggregates, at the cost of some rather complex and subtle logistics
- the `avg_spend` aggregate, much like the `num_transactions` aggregate, (potentially) contains data pertaining
  to multiple entities. While it is relatively easy to carry forward the contributions of individual entities
  for a `sum` or `count`, it is entirely unclear how an individual contributes to an average that is calculated
  across multiple entities, particularly if this information must be kept in a form that allows subsequent
  flattening

## Intermediate extreme value flattening

Experiments show that repeated aggregation (aggregation of aggregates without any form for anonymization or extreme value flattening)
tend to produce values collapsing down to the number 1, after ~4 rounds of aggregation. After 2 rounds of aggregation the difference between the
largest and smallest value reported does not generally exceed 2. These results have held true irrespective of if the dataset includes extreme values or not and
show that the aggregate values themselves quickly become harmless.

If only a single level of aggregation is done, like in the following query:

```sql
SELECT num_transaction, ...
FROM (
  SELECT city, count(*) as num_transactions
  FROM credit_card_transactions
  GROUP BY city
) t
...
```

then the reported `num_transaction` values in the anonymized buckets are directly influenced by individual extreme value contributors
in the `count` aggregate in subquery `t`. As a result it is important that extreme value flattening takes place.

When in the twilight zone before an aggregate fully collapses (around two nested aggregates) an analyst might be able to tell
the difference between two results, one containing an extreme value and one without the extreme value, but might not always be
able to determine which is which due to the noise we add during anonymization being of a similar magnitude.
In such a case an aggregate value in an intermediate query can still pose a risk in other ways, for example when used as join conditions. If a join is made that
uses the value of a row in a nested query that is an extreme value, then this might in turn influence what other rows are included in the final
result, thereby produce a visible effect that can be controlled. This effect, like any other aggregate would vanish
as a result of repeated aggregation, but shows that it is not sufficient to only perform extreme value flattening at the very end.

Performing intermediate extreme value flattening has the added benefit that we no longer need to carry forward any information about
how much each entity has contributed to an aggregate. As the aggregate is mostly safe, it is sufficient to know which AIDs were
involved.


## Extreme and top values

When aggregating we flatten extreme values and replace their values with those of a representative top-group average.

The process for suppressing extreme values is done separately for each AID set type. That is to say for a
dataset with AID sets `[email-1; email-2; company]` the process is repeated three times, even though
there are only two kinds of AIDs. For each AID set type we calculate the absolute distortion. For our final aggregate we choose,
and use, the largest of the available distortions.

Please note that if any of the processes fails due to insufficiently many distinct AIDs, the aggregate as a whole returns `null`.

### Algorithm

The process for suppressing extreme values is as follows:

1. Produce per-AID contributions as per the aggregate being generated
   (count of rows, sum of row contributions, min/max etc). For aid sets such as
   `email[1]` and `email[1, 2, 3]` an AID would be something like the value 1.
   When a contribution is for a set of entities (such as `email[1, 2, 3]`) the
   contribution is made proportional per AID value.
2. Sort the contribution values to be flattened in descending order
3. Produce noisy extreme values count (`Ne`) and top count (`Nt`) values
4. Take the first `Ne` highest contributions as the extreme values. If any of them appear for `minimum_allowed_aids` distinct AIDs, use that value
5. Take next `Nt` highest contributions as the top count.
6. Replace each `Ne` value with an average of the `Nt` values

In step 1 above it is mentioned that contributions for an AID set is split proportionally across the AID entities.
Say the AID set `email[1, 2, 3]` had sent 9 emails, in that case we would attribute a count of 3 for each of the AIDs
individually on top of what other contributions they might already have.
This is not strictly speaking entirely correct. That is to say the resulting contributions stemming from this simplified redistributions
do not reflect reality. However since the only way an AID set of multiple AID values can arise is through
aggregation, and since we always perform extreme value flattening when aggregating, it seems likely that this is not
going to cause insufficient flattening for extreme contributions (note: this is an assumption that hasn't been fully validated!).
In fact it might have a positive effect by potentially limiting further unnecessary flattening.

Below follows some concrete examples. In all examples I have made the simplified assumption, unless otherwise stated,
that the minimum allowed aids threshold 2 for all AID types.

Note that the tables as shown are the input values to an anonymizing aggregate.
Imagine they are the result of running a query such as:

```sql
SELECT count(*)
FROM table
```

or alternatively the input for the aggregate for one of the `card_type` values in a query such as:

```sql
SELECT card_type, count(*)
FROM table
GROUP BY card_type
```


### Examples

#### Early termination

| Value | AID sets   |
| ----: | ---------- |
|    10 | AID1[1, 2] |

- We produce per-AID contributions. In this case the value `10` is a shared contribution between the AID values 1 and 2. We attribute 5 to each of them.

| Value | AID |
| ----: | --- |
|     5 | 1   |
|     5 | 2   |

- The rows are sorted in descending order of `Value`
- `Ne = 2`, `Nt = 2`
- We take the `Ne` first values as extreme values. The values are both the same, and since `minimum_allowed_aids = 2` they would satisfy low count filtering.
- We abort without any suppression


#### Insufficient data

| Value | AID sets   |
| ----: | ---------- |
|    10 | AID1[1, 2] |
|     1 | AID1[1]    |

- We produce per-AID contributions. In this case the value `10` is a shared contribution between the AID values 1 and 2. We attribute 5 to each of them. AID 1 has an additional entry too, making for uneven total contributions for the two AIDs:

| Value | AID |
| ----: | --- |
|     6 | 1   |
|     5 | 2   |

- The rows are sorted in descending order of `Value`
- `Ne = 2`, `Nt = 2`
- We take the `Ne` first values as the extreme values
- We try taking the `Nt` next rows for the top group, but have run out of data. We terminate the process and return `Null` to indicate that we couldn't produce an anonymous aggregate
- The total distortion is _infinite_... sort of


#### Base case

| Value | AID sets   |
| ----: | ---------- |
|    10 | AID1[1]    |
|     9 | AID1[2]    |
|     8 | AID1[3]    |
|     7 | AID1[4]    |
|     6 | AID1[5]    |
|     5 | AID1[6]    |
|     4 | AID1[7]    |
|     3 | AID1[1, 2] |

- We produce per-AID contributions. In this case the value `3` is a shared contribution between the AID values 1 and 2. We attribute 1.5 to each of them.
- The rows are sorted in descending order of `Value`

|           Value |  AID | Required distortion by AID |
| --------------: | ---: | -------------------------: |
| 11.5 = 10 + 3/2 |    1 |                        7.5 |
|  10.5 = 9 + 3/2 |    2 |                        7.5 |
|               8 |    3 |                            |
|               7 |    4 |                            |
|               6 |    5 |                            |
|               5 |    6 |                            |
|               4 |    7 |                            |

- `Ne = 2`, `Nt = 2`
- We take the `Ne` first values as extreme values (11.5 and 10.5)
- We take the `Nt` next values as the top group (8 and 7)
- We replace the `Ne` values with the average of the top group: 7.5
- The total distortion is **7** (11.5 - 7.5 + 10.5 - 7.5)


#### Base case #2

| Value | AID sets            |
| ----: | ------------------- |
|    10 | AID1[1]             |
|     9 | AID1[1, 2]          |
|     8 | AID1[2]             |
|     7 | AID1[3]             |
|     6 | AID1[4]             |
|     5 | AID1[4, 5]          |
|     4 | AID1[1, 2, 3, 4, 5] |

- We produce per-AID contributions. In this case the value 9 is shared, and so is the value 5 and 4. These are distributed amongst the corresponding AIDs. The resulting list of AID contributions, becomes:
- The rows are sorted in descending order of `Value`

|                 Value | AID | Required distortion by AID |
| --------------------: | --- | -------------------------- |
| 15.3 = 10 + 9/2 + 4/5 | 1   | 5.55                       |
|  13.3 = 8 + 9/2 + 4/5 | 2   | 5.55                       |
|   9.3 = 6 + 5/2 + 4/5 | 4   | 5.55                       |
|         7.8 = 7 + 4/5 | 3   |                            |
|       3.3 = 5/2 + 4/5 | 5   |                            |


- `Ne = 3`, `Nt = 2`
- We take the `Ne` first values as extreme values (15.3, 13.3, 9.3)
- We take the `Nt` next values as the top group (7.8 and 3.3)
- We replace the `Ne` values with the average of the top group: 5.55
- The total distortion is **21.25** (15.3 - 5.55 + 13.3 - 5.55 + 9.3 - 5.55)


#### Multiple AID types

| Value | AID sets                     |
| ----: | ---------------------------- |
|    10 | AID1[1, 2], AID2[1], AID3[1] |
|     9 | AID1[3], AID2[2], AID3[1]    |
|     8 | AID1[1], AID2[1, 2], AID3[1] |
|     7 | AID1[1], AID2[3], AID3[1]    |
|     6 | AID1[1,2], AID2[1], AID3[1]  |
|     5 | AID1[4, 5], AID2[4], AID3[1] |

- We split the values by AID type and then repeat the process for each of the AID sets

##### AID1

| Value | AID sets   |
| ----: | ---------- |
|    10 | AID1[1, 2] |
|     9 | AID1[3]    |
|     8 | AID1[1]    |
|     7 | AID1[1]    |
|     6 | AID1[1, 2] |
|     5 | AID1[4, 5] |

- We produce per-AID contributions. In this case the values 10 and 6 are shared. These are distributed amongst the corresponding AIDs.
- The rows are sorted in descending order of `Value`

|                   Value |  AID | Required distortion by AID |
| ----------------------: | ---: | -------------------------: |
| 26 = 8 + 7 + 10/2 + 6/1 |    1 |                       5.75 |
|         11 = 10/2 + 6/1 |    2 |                       5.75 |
|                       9 |    3 |                            |
|               2.5 = 5/2 |    4 |                            |
|               2.5 = 5/2 |    5 |                            |

- `Ne = 2`, `Nt = 2`
- We take the `Ne` first values as extreme values (26, 11)
- We take the `Nt` next values as the top group (9, 2.5)
- We replace the `Ne` values with the average of the top group: 5.75
- The total distortion for AID1 is **25.5** (26 - 5.75 + 11 - 5.75)


##### AID2

| Value | AID sets   |
| ----: | ---------- |
|    10 | AID2[1]    |
|     9 | AID2[2]    |
|     8 | AID2[1, 2] |
|     7 | AID2[3]    |
|     6 | AID2[1]    |
|     5 | AID2[4]    |

- We produce per-AID contributions. In this case the value 8 is shared. It is distributed amongst the corresponding AIDs.
- The rows are sorted in descending order of `Value`

|         Value |  AID | Required distortion by AID |
| ------------: | ---: | -------------------------: |
| 14 = 10 + 8/2 |    1 |                          6 |
|  13 = 9 + 8/2 |    2 |                          6 |
|             7 |    3 |                            |
|             6 |    1 |                            |
|             5 |    4 |                            |

- `Ne = 2`, `Nt = 3`
- We take the `Ne` first values as extreme values (14, 13)
- We take the `Nt` next values as the top group (7, 6, 5)
- We replace the `Ne` values with the average of the top group: 6
- The total distortion for AID1 is **15** (14 - 6 + 13 - 6)


##### AID3

| Value | AID sets |
| ----: | -------- |
|    10 | AID3[1]  |
|     9 | AID3[1]  |
|     8 | AID3[1]  |
|     7 | AID3[1]  |
|     6 | AID3[1]  |
|     5 | AID3[1]  |

- We produce per-AID contributions
- The rows are sorted in descending order of `Value`

|                       Value |  AID |
| --------------------------: | ---: |
| 45 = 10 + 9 + 8 + 7 + 6 + 5 |    1 |

- `Ne = 2`, `Nt = 2`
- We do not have enough extreme values to form a group of `Ne` extreme values, we abort.

Even though we could have produced an aggregate from the perspectives of AID1 and AID2,
we cannot produce a final aggregate as we have insufficiently many AID3 entities represented.
Assuming there had been enough AID3 entities and the total distortion due to AID3 would have been 10,
then we would have used the distortion due to AID1 as it's the largest.