Please consult the [glossary](glossary.md) for definitions of terms used in this document.

- [Distinct aggregates](#distinct-aggregates)
  - [Intuition](#intuition)
  - [Design goal](#design-goal)
    - [Definition of extreme contributor](#definition-of-extreme-contributor)
  - [Pre-processing](#pre-processing)
  - [Algorithm sketch in detail](#algorithm-sketch-in-detail)
    - [Algorithm](#algorithm)
    - [Worked example](#worked-example)
      - [Processing for email](#processing-for-email)
      - [Processing for first_name](#processing-for-first_name)


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

An entity is an extreme contributor if their presence has a noticeable difference on the aggregate.
In a distinct aggregator, what makes an entity stand out differs from what makes an entity stand
out in a non-distinct aggregator. We, therefore, cannot flatten as we would for other aggregators.

Unlike a non-distinct aggregator, the contributions made to a distinct aggregator might be shared.
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
the absence would half or double the total count in a non-distinct count it does not at all affect
the distinct count as all values are shared.

An entity can be an extreme contributor to a distinct aggregate if they have
significantly more values than other entities for which they are the sole contributor within their AID type.

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

The mapping we want to achieve spreads the individual values across entities such
that the individual contributions can be combined like a regular aggregate would.
With such a mapping in place, the existing aggregator implementations can perform flattening and noise adding
in their usual way.

We must spread the values across as many AIDs as possible. Otherwise, individual entities will unnecessarily
exhibit what our aggregators will consider extreme-contribution behaviour, which will lead to potentially unnecessary flattening.

It is not clear how we can map in a way that accounts for multiple AID types.
The proposed design, therefore, maps each AID type individually.
Much like for other aggregators, we want to use the most extreme flattening required. Doing so leads to
distinct aggregators having to be processed as follows:

For each AID-type:

- map values onto individual contributors
- aggregate using existing non-distinct aggregators and determine the amount of flattening needed,
  using the largest of value returned for the final aggregate result
- the non-distinct aggregator applies noise as usual


## Algorithm sketch in detail

An implementation of this algorithm exists [in the experiments](https://github.com/diffix/experiments/blob/master/count%20distinct/CountDistinctPlayground/CountDistinctPlayground/CountDistinctExperiment.fs) repository.

The following algorithm is applied individually for each AID type.
If you have a row with an AID such as `[email-1[alice, bob]; email-2[alice, cynthia]; ssn[1, 2, 3]]`
then then the algorithm is individually run, to completion, for `email-1`, `email-2` and `ssn`.
The run yielding the largest flattening is used. Additionally, noise is applied proportional to the
top group average, as specified by the particular non-distinct aggregator used.


### Algorithm

- Split AID sets into individual contributions. A shared contribution of value `A` by AIDs `email-1[alice, bob]`
  is treated as if they were individual contributions of value `A` by `email-1[alice]` and `email-1[bob]`.
- Group the values by AID, producing sets of values per AID (no duplicates)
- Order AID groupings in ascending order by the number of distinct values
- Repeatedly scan through the list of AID groupings until the list has been exhausted, taking a value that has
  not yet been assigned to another AID and output it as belonging to the particular AID
- Process the output using the corresponding regular aggregate function

The first step requires some explanation. For regular aggregates a shared contribution
is divided across each contributing AID. I.e. an Apple contributed by Alice and Bob,
would mean that both Alice and Bob contributed half an apple.
For a distinct aggregate this doesn't make sense as what we are recording is the
fact that a value was contributed and now how many times. Therefore it is valid
to say that both Alice and Bob fully contributed the Apple.


### Worked example

Let's say we have the following table:

| AIDs                                            | Value     |
| ----------------------------------------------- | --------- |
| [email[Paul; Sebastian]; first_name[Sebastian]] | Apple     |
| [email[Paul; Edon]; first_name[Sebastian]]      | Apple     |
| [email[Sebastian]; first_name[Sebastian]]       | Apple     |
| [email[Cristian]; first_name[Paul]]             | Apple     |
| [email[Edon]; first_name[Paul]]                 | Apple     |
| [email[Edon]; first_name[Paul]]                 | Pear      |
| [email[Paul]; first_name[Paul]]                 | Pineapple |
| [email[Cristian]; first_name[Felix]]            | Lemon     |
| [email[Cristian]; first_name[Felix]]            | Orange    |

We process this table by AID type, i.e. `email` and `first_name`, separately.

#### Processing for email

After splitting the per-AID contributions into individual contributions, we end up with:

| AID       | Value     |
| --------- | --------- |
| Paul      | Apple     |
| Sebastian | Apple     |
| Edon      | Apple     |
| Cristian  | Apple     |
| Edon      | Pear      |
| Paul      | Pineapple |
| Cristian  | Lemon     |
| Cristian  | Orange    |

grouping by AID and sorting by the number of contributions, yields:

| AID       | Values                 |
| --------- | ---------------------- |
| Sebastian | [Apple]                |
| Paul      | [Apple; Pineapple]     |
| Edon      | [Apple; Pear]          |
| Cristian  | [Apple; Lemon; Orange] |

repeatedly scanning and assigning unassigned values from the list,
after the first pass through the list yields the following assigned values:

| AID       | Value     |
| --------- | --------- |
| Sebastian | Apple     |
| Paul      | Pineapple |
| Edon      | Pear      |
| Cristian  | Lemon     |

leaving the following table of unassigned values:

| AID      | Values   |
| -------- | -------- |
| Cristian | [Orange] |

repeating the process consumes all available values resulting in a final
contribution list of:

| AID       | Value     |
| --------- | --------- |
| Sebastian | Apple     |
| Paul      | Pineapple |
| Edon      | Pear      |
| Cristian  | Lemon     |
| Cristian  | Orange    |

If the original aggregator is `count(distinct value)`, then we would now
pass the list through our regular `count(value)` aggregator, which would determine
that Cristian is an extreme contributor (having 2 contributions vs 1 for the others)
and a total flattening of 1.


#### Processing for first_name

The process is similar as it was for `email`. We split values
across users, group by AID and order ascending by number of distinct
contributed values:

| AID       | Values                   |
| --------- | ------------------------ |
| Sebastian | [Apple]                  |
| Felix     | [Lemon; Orange]          |
| Paul      | [Apple; Pear; Pineapple] |

Iteratively scanning through this list and assigning values to AIDs would yield
the following contribution list:

| AID       | Values    |
| --------- | --------- |
| Sebastian | Apple     |
| Felix     | Lemon     |
| Paul      | Pear      |
| Felix     | Orange    |
| Paul      | Pineapple |

Which, when used as input for the `count(value)` aggregate, might consider both Felix and Paul extreme contributors (with 2 values each as opposed to 1 for Sebastian) and
a required flattening of 2.

We use an overall flattening of 2 taken from aggregating based on `first_name` as it exceeds the flattening of 1 resulting from
aggregating based on `email`. Additionally, if this is the top-level anonymizing aggregator, we would add noise proportional to the
top-group average reported by the `count(value)` processing done based on the `first_name` AID.