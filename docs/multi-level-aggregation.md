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
  across query boundaries in order to have the ability to later do outlier flattening if necessary is feasible
  for `count` and `sum` aggregates, at the cost of some rather complex and subtle logistics
- the `avg_spend` aggregate, much like the `num_transactions` aggregate, (potentially) contains data pertaining
  to multiple entities. While it is relatively easy to carry forward the contributions of individual entities
  for a `sum` or `count`, it is entirely unclear how an individual contributes to an average that is calculated
  across multiple entities, particularly if this information must be kept in a form that allows subsequent
  flattening

## Intermediate outlier flattening

Experiments show that repeated aggregation (aggregation of aggregates without any form for anonymization or outlier flattening)
tend to produce values collapsing down to 1 after ~4 rounds of aggregation. After 2 rounds of aggregation the min-max difference
does not generally exceed 2. These results have held true irrespective of if the dataset includes extreme outliers or not and
show that the aggregate values themselves quickly become harmless. When in the twilight zone before an aggregate fully collapses
(around two nested aggregates) an analyst might be able to tell the difference between two results, one containing an outlier
and one without the outlier, but is likely not able to determine which is which due to the noise we add during anonymization
being of a similar magnitude.
Intermediate aggregate values can pose a risk in other ways, for example when used as JOIN conditions. If a join is made that
uses the value of an outlying row within a nested query, then this might in turn influence what other rows are included in the final
result, thereby produce a visible effect that can, potentially, be controlled. This effect, like any other aggregate would vanish
as a result of repeated aggregation, but shows that it is not sufficient to only perform outlier flattening at the very end.
Performing intermediate outlier flattening has the added benefit that we no longer need to carry forward any information about
how much each entity has contributed to an aggregate. As the aggregate is mostly safe, it is sufficient to know which AIDs were
involved.


## Outliers and top values

When aggregating we suppress outliers and replace their values with those of a representative top-group average.
This is fairly straight forward in the case where each value represents a single entity or individual. It becomes
marginally more complex when multiple AID sets have to be taken into account.

The process for suppressing outliers is as follows:

1. Sort the column values to be suppressed in descending order
2. Produce noisy outlier count (`No`) and top count (`Nt`) values for each AID set
3. Take outlier values until one of the following criteria has been met:
   1. One of the values passes low count filtering for all the AID sets.
      If such a value is found, then the more extreme values (if any) are all replaced by this value
      and the algorithm is considered completed
   2. The cardinality of the combined AID set meets or exceeds `No` for each of the AID sets
4. Take top values according to the same rules as taking outlier values:
   1. Stop as soon as a value is found which individually passes low count filtering for all AID types
   1. Stop as soon as the cardinality of the combined AID set meets or exceeds `Nt` for each of the AID types
6. Replace each `No` value with an average of the `Nt` values weighted by their AID contributions.

Below follows some concrete examples. In all examples I have made the simplified assumption, unless otherwise stated,
that the low count filter threshold is 2 for all AID types.

### Early termination

| Value | AID sets   |
| ----: | ---------- |
|    10 | AID1[1, 2] |

- The columns are sorted in descending order of `Value`
- `No = 2`, `Nt = 2`
- We immediately terminate the process due to meeting requirement `3.1` of the value 10 passing low count filtering.
  No flattening is needed

### Base case

| Value | AID sets |
| ----: | -------- |
|    10 | AID1[1]  |
|     9 | AID1[2]  |
|     8 | AID1[3]  |
|     7 | AID1[4]  |
|     6 | AID1[5]  |
|     5 | AID1[6]  |
|     4 | AID1[7]  |

- The columns are sorted in descending order of `Value`
- `No = 2`, `Nt = 2`
- The outlier values meet criteria `3.2` after we have taken values 10 and 9
- The top values meet criteria `4.2` after we have taken values 8 and 7
- The weighted average is `8 * 1/2 + 7 * 1/2 = 7.5` which is used in place of the values 10 and 9

### Expanded base case

| Value | AID sets   |
| ----: | ---------- |
|    10 | AID1[1]    |
|     9 | AID1[1, 2] |
|     8 | AID1[2]    |
|     7 | AID1[3]    |
|     6 | AID1[4]    |
|     5 | AID1[5]    |

In this example we are using a low count threshold of 5 (just in order for it not to trigger, and make this example work).

- The columns are sorted in descending order of `Value`
- `No = 3`, `Nt = 2`
- The outlier values meet criteria `3.2` after we have taken values 10, 9, 8, and 7! We cannot stop after 8,
  as the AIDs repeat and do not increase the cardinality of the AID set
- The top values meet criteria `4.2` after we have taken values 6 and 5
- The weighted average is `6 * 1/2 + 5 * 1/2 = 2.5` which is used in place of the values 10 through 7.

### Multiple AID types

| Value | AID sets            |
| ----: | ------------------- |
|    10 | AID1[1, 2], AID2[1] |
|     9 | AID1[3], AID2[2]    |
|     8 | AID1[1], AID2[1, 2] |
|     7 | AID1[1], AID2[3]    |
|     6 | AID1[1,2], AID2[1]  |

- The columns are sorted in descending order of `Value`
- `AID1.No = 2`, `AID1.Nt = 2`, `AID2.No = 2`, `AID2.Nt = 2`
- Value 10 both meets the low count and `3.1` criteria for AID1, but we additionally need to take value 9 as well in order to fully satisfy `3.1` for both AID types
- The top values meet criteria `4.2` for the top values by taking values 8, 7, and 6
- The weighted average becomes: `8 * 3/8 + 7 * 2/8 + 6 * 3/8 = 7` and is used to replace values 10 and 9