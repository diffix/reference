For the assumed background knowledge, please consult the:
- [glossary](glossary.md) for definitions of terms used in this document
- [multi-aid](multiple-aid.md) for a description of how AIDs spread through a query

- [Aggregation across query boundaries](#aggregation-across-query-boundaries)
  - [Intermediate extreme value flattening](#intermediate-extreme-value-flattening)
    - [Example "twilight zone" attack query](#example-twilight-zone-attack-query)

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

Flattening is done the same way for intermediate aggregates as it is for the top-level fully anonymized aggregates.
Read more about how this is done in the [computing noise](computing%20noise.md) document.

### Example "twilight zone" attack query

Let's assume we have the following three tables:

`medical_history`

|  AID | hasCancer |
| ---: | --------- |
|    1 | true      |
|  ... | ...       |

`purchases`

|    # |  AID | ProductId |
| ---: | ---: | --------: |
|    1 |    1 |       ... |
|    2 |    1 |       ... |
|    3 |    1 |       ... |
|  ... |  ... |       ... |
| 1000 |    1 |       ... |
| 1001 |    2 |       ... |
| 1002 |    3 |       ... |
| 1003 |    4 |       ... |
| 1004 |    5 |       ... |

`products`

| Name        | Price |
| ----------- | ----- |
| Apple Juice | 10    |
| Carrots     | 3     |
| iPhone      | 900   |

If we didn't do intermediate flattening, the product name reported would give an accurate
estimate of the number of purchases having taken place.

```sql
SELECT products.Name
FROM (
  SELECT count(*) as numPurchases
  FROM purchases INNER JOIN medical_history on purchases.AID = medical_history.AID
  WHERE medical_history.hasCancer
) t INNER JOIN products ON t.numPurchases > products.Price
```

It would in this instance also rely on:
- there being sufficiently many other patients with cancer
- some knowledge about the user having unnaturally many purchases

With intermediate outlier suppression `Carrots` would have been the only product name returned.