Please consult the [glossary](glossary.md) for definitions of terms used in this document.

- [Distinct aggregates](#distinct-aggregates)
  - [Intuition](#intuition)
    - [Definition of extreme contributor](#definition-of-extreme-contributor)
  - [Pre-processing](#pre-processing)
  - [Algorithm sketch in detail](#algorithm-sketch-in-detail)
    - [Worked example](#worked-example)
      - [Processing for email](#processing-for-email)
      - [Processing for first_name](#processing-for-first_name)

# Distinct aggregates

## Intuition

Our regular aggregates work on the notion that all entities contribute some part of a whole,
and that these individual parts can be combined to a whole.
This intuition, and these design assumptions, do not hold when aggregating distinct values.
A distinct value should only occur once irrespective of how many entities report to have it.

### Definition of extreme contributor

An entity is an extreme contributor if their presence has a noticeable difference on the aggregate.
In a distinct aggregate what makes an entity stand out differs from what makes an entity stand
out in a non-distinct aggregate. We therefore cannot apply flattening as we would for other aggregates.

The reason that this is so is because, unlike in a non-distinct aggregate,
contributions in a distinct aggregate might be shared. For example, let's consider the following
example dataset:

| AID sets  | Value |
| --------- | ----: |
| [1, 1000] |     1 |
| [2, 1000] |     2 |
| [3, 1000] |     3 |
| [4, 1000] |     4 |
...
| [999, 1000] |   999 |

In this example table AID 1000 has each and every value in the dataset. While the presence or
absence would half or double the total count in a non-distinct count, it does not at all affect
the distinct count as all values are shared.

The way in which an entity can be an extreme contributor to a distinct aggregate is if they have
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

We have validated designs for our other aggregates. What we want to do is map distinct aggregates
onto non-distinct ones so we can reuse our existing work and machinery.

The mapping we want to achieve is such that it spreads the individual values across entities such
that the individual contributions can be combined like a regular aggregate would. This way existing
extreme value flattening can also apply.

Care must be taken in this mapping to ensure the spread of values across AIDs is as wide as possible.
Otherwise individual entities will unnecessarily exhibit what our aggregates will consider extreme contribution
behavior, which in turn will lead to potentially unnecessary flattening.

Unfortunately it is not clear how this mapping can happen be done such that it accounts for
multiple AID types at the same time. It therefore maps for each AID type individually.
Much like for other aggregates we want to use the most extreme flattening required. This leads to
distinct aggregates having to be processed as follows:

For each AID type do:

- map values onto individual contributors
- aggregate using existing non-distinct aggregates and determine amount of flattening needed,
  the largest of which is subsequently used for the final distinct aggregate


## Algorithm sketch in detail

A simple implementation of this algorithm can be found [in the experiments](https://github.com/diffix/experiments/blob/master/count%20distinct/CountDistinctPlayground/CountDistinctPlayground/CountDistinctExperiment.fs) repository.

The following algorithm is done by AID type individually.
That is to say that if you have a row with an AID such as `[email-1[alice, bob]; email-2[alice, cynthia]; ssn[1, 2, 3]]`
then it is individually run to completion for `email-1`, `email-2` and `ssn`. The run yielding the largest flattening
is used.

**Algorithm**
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

we process this table by AID type, i.e. `email` and `first_name`, separately.

#### Processing for email

Split into individual contributions we end up with:

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

grouping by AID and sorting by number of contributions, yields:

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

If the original aggregate was `count(distinct value)`, then we would now
pass the list through our regular `count(value)` aggregator which would determine
that Cristian is an extreme contributor (with 2 contributions vs 1 for the others)
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

The overall flattening of 2 is therefore used as it exceeds the flattening of 1
resulting from processing the data based on `email`.