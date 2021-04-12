Please consult the [glossary](glossary.md) for definitions of terms used in this document.

- [Distinct aggregates](#distinct-aggregates)
  - [Intuition](#intuition)
  - [Design goal](#design-goal)
    - [Definition of extreme contributor](#definition-of-extreme-contributor)
  - [Pre-processing](#pre-processing)
    - [Per-AID type algorithm sketch in detail](#per-aid-type-algorithm-sketch-in-detail)
      - [Algorithm](#algorithm)
    - [Worked example #1](#worked-example-1)
      - [Processing for email](#processing-for-email)
      - [Processing for first_name](#processing-for-first_name)
    - [Worked example #2](#worked-example-2)


# Distinct aggregates

## Intuition

Regular aggregators work on the notion that all entities contribute a part of a whole
that can be combined. For example, if we are counting card transactions, and
Alice had  card transactions, Bob 5, Cynthia 2 then the total would be 17
(give and take some noise, extreme value flattening).

When aggregating distinct values the contributions of individuals cannot be combined in the same way.
A distinct aggregate might be "how many distinct card types were used for card transactions",
where card types are not considered protectable entities, whereas their owners would be.
In this case, Alice, Bob, and Cynthia might all have used their "premium" cards. This should result
in a distinct count of 1, despite the result involving 3 individuals.

## Design goal

We have two design goals for our distinct aggregators:

1. We want to make use of our regular aggregators where possible (hence why the title of this
   document contains "pre-processing" in its name)
2. We want to avoid unnecessarily distorting results where no distortion is necessary.

An example of the latter might be a dataset where the query:

```sql
SELECT card_type, count(distinct customer_id)
FROM cards
GROUP BY card_type
```

produces the table

| card_type | count |
| --------- | ----: |
| gold      |  1000 |
| silver    |  1000 |
| diamond   |    10 |

In this case, each card type is safe to include in the result as there are enough distinct customer entities for each
card. The related distinct count query:

```sql
SELECT count(distinct card_type)
FROM cards
```

can therefore return the count 3 without any distortion.


### Definition of extreme contributor

An entity is an extreme contributor if their presence has a noticeable impact on the aggregate.
In a distinct aggregator, what makes an entity stand out differs from what makes an entity stand
out in a non-distinct aggregator. We, therefore, cannot flatten as we would for other aggregators.

Unlike a non-distinct aggregator, the contributions made to a distinct aggregator might be shared by multiple entities.
For example, let's consider the following example dataset:

| AID sets  | Value |
| --------- | ----: |
| [1, 1000] |     1 |
| [2, 1000] |     2 |
| [3, 1000] |     3 |
| [4, 1000] |     4 |
...
| [999, 1000] |   999 |

In this example table, AID 1000 has each value in the dataset. While the presence or
the absence of the entity with AID 1000 would half or double the total count in a non-distinct count aggregate,
it does not at all affect the distinct count as all values are shared.

An entity can be an extreme contributor to a distinct aggregate if they have
significantly more unique values than other entities.

To map it onto our previous example, AID 1000 would be an extreme contributor if the table looked like this:

| AID sets | Value |
| -------- | ----: |
| 1        |     1 |
| 2        |     2 |
| 3        |     3 |
| 4        |     4 |
| 1000     |     5 |
...
| 1000     |  1000 |


## Pre-processing

We have validated designs for our other aggregators. What we want to do is map distinct aggregates
onto non-distinct ones so we can reuse our existing work and machinery.

The mapping we want to achieve spreads the individual values across different entities in such a way
that the individual contributions can be combined like a regular aggregate would.

We must spread the values across as many AIDs as possible. Otherwise, individual entities will unnecessarily
exhibit what our aggregators will consider extreme-contribution behaviour, which will lead to potentially unnecessary flattening.

It is not clear how we can map in a way that accounts for multiple AID types.
The proposed design, therefore, maps each AID type individually.
Much like for other aggregators, we want to use the most extreme flattening required. Doing so leads to
distinct aggregators having to be processed as follows:

Globally:

- filter out all values that pass the low count filter. These are safe and can be aggregated as they are
  without further processing.

For each AID-type:

- map the remaining values onto individual contributors
- aggregate using existing non-distinct aggregators and determine the amount of flattening needed

Post-processing:

- Aggregate the the values that passed low count filtering without any further noise
- For the remaining values use the flattening and noise amount from the per-AID type
  processing for the AID type that yielded the amount of flattening


### Per-AID type algorithm sketch in detail

An implementation of this algorithm exists [in the experiments](https://github.com/diffix/experiments/blob/master/count%20distinct/CountDistinctPlayground/CountDistinctPlayground/CountDistinctExperiment.fs) repository.

The following algorithm is applied individually for each AID type.
If you have a row with an AID such as `[email-1[alice, bob]; email-2[alice, cynthia]; ssn[1, 2, 3]]`
then the algorithm is individually run for `email-1`, `email-2` and `ssn`.
The run yielding the largest flattening is used. Additionally, noise is applied proportional to the
top group average, as specified by the particular non-distinct aggregator used.


#### Algorithm

- Split AID sets into individual contributions. A shared contribution of value `A` by AIDs `email-1[alice, bob]`
  is treated as if they were individual contributions of value `A` by `email-1[alice]` and `email-1[bob]`.
- Group the values by AID, producing sets of values per AID (no duplicates)
- Order AID groupings in ascending order by the number of distinct values
- Repeatedly scan through the list of AID groupings until the list has been exhausted, taking a value that has
  not yet been assigned to another AID and output it as belonging to the particular AID
- Process the output using the corresponding regular aggregate function

The first step requires some explanation. For regular aggregate values, a shared contribution
is divided across each contributing AID. I.e. an Apple was contributed collectively by Alice and Bob,
then we normally assume Alice and Bob contributed half an apple each.
For a distinct aggregator this doesn't make sense as what we are recording is
that a particular value was contributed, not how many times it was contributed.


### Worked example #1

Let's say we have the following table:

| AIDs                                            | Value      |
| ----------------------------------------------- | ---------- |
| [email[Paul; Sebastian]; first_name[Sebastian]] | Apple      |
| [email[Paul; Edon]; first_name[Sebastian]]      | Apple      |
| [email[Sebastian]; first_name[Sebastian]]       | Apple      |
| [email[Cristian]; first_name[Paul]]             | Apple      |
| [email[Edon]; first_name[Paul]]                 | Apple      |
| [email[Edon]; first_name[Paul]]                 | Pear       |
| [email[Paul]; first_name[Paul]]                 | Pineapple  |
| [email[Cristian]; first_name[Paul]]             | Lemon      |
| [email[Cristian]; first_name[Felix]]            | Orange     |
| [email[Felix]; first_name[Edon]]                | Banana     |
| [email[Edon]; first_name[Cristian]]             | Grapefruit |

Apple was contributed by 4 `email` entities (`Paul`, `Sebastian, `Cristian`, and `Edon`),
and 2 `first_name` entities (`Sebastian` and `Paul`). It is therefore safe and we set it aside.

The remaining values are processed separately by AID type (i.e. `email` and `first_name`).

#### Processing for email

After splitting the per-AID contributions into individual contributions, we end up with:

| AID      | Value      |
| -------- | ---------- |
| Edon     | Pear       |
| Paul     | Pineapple  |
| Cristian | Lemon      |
| Cristian | Orange     |
| Felix    | Banana     |
| Edon     | Grapefruit |

grouping by AID and sorting by the number of contributions, yields:

| AID      | Values             |
| -------- | ------------------ |
| Paul     | [Pineapple]        |
| Felix    | [Banana]           |
| Edon     | [Pear, Grapefruit] |
| Cristian | [Lemon; Orange]    |

repeatedly scanning and assigning unassigned values from the list,
after the first pass through the list yields the following assigned values:

| AID      | Value     |
| -------- | --------- |
| Paul     | Pineapple |
| Felix    | Banana    |
| Edon     | Pear      |
| Cristian | Lemon     |

leaving the following table of unassigned values:

| AID      | Values       |
| -------- | ------------ |
| Edon     | [Grapefruit] |
| Cristian | [Orange]     |

repeating the process consumes all available values resulting in a final
contribution list of:

| AID      | Value      |
| -------- | ---------- |
| Paul     | Pineapple  |
| Felix    | Banana     |
| Edon     | Pear       |
| Cristian | Lemon      |
| Edon     | Grapefruit |
| Cristian | Orange     |

If the aggregator is count (i.e. the analyst asked for `count(distinct value)`), then we can now
pass this table through our regular `count(value)` aggregator. Assuming `Ne = 2` and `Nt = 2` the
algorithm would deduce that Cristian and Edon are extreme contributors (having 2 contributions each vs 1 for Felix and Paul)
and the total flattening resulting from AID type `email` would be 2 (one for each of Edon and Cristian).


#### Processing for first_name

The process is similar to that done for `email`. We split the values
across users, group by AID and order ascending by number of distinct
contributed values:

| AID      | Values                   |
| -------- | ------------------------ |
| Edon     | [Banana]                 |
| Cristian | [Grapefruit]             |
| Felix    | [Orange]                 |
| Paul     | [Pear; Pineapple; Lemon] |

Iteratively scanning through this list and assigning values to AIDs would yield
the following contribution list:

| AID      | Values     |
| -------- | ---------- |
| Edon     | Banana     |
| Cristian | Grapefruit |
| Felix    | Orange     |
| Paul     | Pear       |
| Paul     | Pineapple  |
| Paul     | Lemon      |

Which, when used as input for the `count(value)` aggregator with an `Ne = 2` and `Nt = 2`, places both Paul and Felix
in the extreme value group and Edon and Cristian in the top group. The required flattening is 2 reducing Paul down
from a count of 3 to 1.

In this case the flattening is the same for both AID types. In both cases we have a flattening of 2 and a top-group average of 1.
The final aggregate therefore is: `apple + other fruits - flattening + noise` = `1 + 6 - 2 + noise proportional to 1`.


### Worked example #2

Let's say we have the following table:

| AIDs                                            | Value     |
| ----------------------------------------------- | --------- |
| [email[Paul; Sebastian]; first_name[Sebastian]] | Apple     |
| [email[Paul; Edon]; first_name[Sebastian]]      | Apple     |
| [email[Sebastian]; first_name[Sebastian]]       | Apple     |
| [email[Cristian]; first_name[Paul]]             | Apple     |
| [email[Edon]; first_name[Paul]]                 | Apple     |
| [email[Edon]; first_name[Paul]]                 | Orange    |
| [email[Paul]; first_name[Paul]]                 | Orange    |
| [email[Cristian]; first_name[Felix]]            | Orange    |
| [email[Cristian]; first_name[Felix]]            | Orange    |

The `minimum_allowed_aids` is 2 for both `email` and `first_name`.
In this case neither Apple nor Orange have to be low count filtered.
Apple occurs for 5 distinct `email` AIDs and 2 distinct `first_name` AIDs.
Orange occurs for 3 distinct `email` AIDs and 2 distinct `first_name` AIDs.

If the requested aggregate was `count(distinct value)` then we would return the
completely unaltered count of 2.