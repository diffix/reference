Please consult the [glossary](glossary.md) for definitions of terms used in this document.

- [Computing Per-AID Contributions](#computing-per-aid-contributions)
  - [Computing noise](#computing-noise)
    - [Problem Case: Proxy AIDs](#problem-case-proxy-aids)
    - [Pseudo-AIDs (or multi-column AIDs)](#pseudo-aids-or-multi-column-aids)
    - [Configuration](#configuration)
  - [Flattening - Determining extreme and top values](#flattening---determining-extreme-and-top-values)
    - [Redistribution of values](#redistribution-of-values)
    - [Algorithm](#algorithm)
    - [Examples](#examples)
      - [Early termination](#early-termination)
      - [Insufficient data](#insufficient-data)
      - [Base case](#base-case)
      - [Base case #2](#base-case-2)
      - [Multiple AID types](#multiple-aid-types)
        - [AID1](#aid1)
        - [AID2](#aid2)
        - [AID3](#aid3)
- [Seeding](#seeding)
- [Rationale](#rationale)

# Computing Per-AID Contributions

With an integrated cloak, we go back to the original style of computing noise, where we have information about individual AIDs. This is needed to compute noise about the various aggregates, including future aggregates and even user-defined aggregates. There are at least two types of aggregates:

1. Combined: An aggregate that is derived from multiple values. `sum()` is an example.
2. Individual: An aggregate that is derived from a single value. `median()` is an example.

> TODO: there may be other types. Maybe should have a look at a bunch of aggregates and see.

## Computing noise

When computing noise, the noise amount (standard deviation) is computed separately for each AID set, and the largest such standard deviation value is used. (Note that for `sum()`, for instance, the standard deviation value is taken from the average contribution of flattened AIDs, or 1/2 the average of the top group. This is how AID affects noise amount.)

When computing flattening (for `sum()`), a flattening value for each distinct AID column is computed separately, and the output is adjusted (flattened) by whichever value is greater. Note that it is possible for the AID columns used for noise and for flattening to be different.

For each new aggregate function, we'll need to take into account multiple AID columns when determining how to perturb the output.

Note that if two AID columns are identical (have the same values and refer to the same entities), then even if they are treated as distinct the right thing happens from an anonymity point of view. For instance, regarding LCF, a bucket might have AID1.1, AID1.2, AID2.1, and AID2.2. It so happens that `AID1.1=AID2.1` and `AID1.2=AID2.2`, but the mechanism doesn't care. The number of distinct AIDs to be used for LCF is two.

The same is the case for noise or flattening. If two AID columns are identical, then the same rows will be used for the top group for both AIDs or for computing the contribution of the AIDs.

Of course, if the AID columns are identical and they are treated as distinct, then the execution is less efficient because Diffix is in essence doing the same work twice. (Optimizations that deal with this are discussed later.)

In any event, the fact that the mechanism doesn't care if two AIDs are identical or different means that in general it isn't necessary for an analyst to JOIN tables simply for the purpose of ensuring that there is an AID. It also means that we can be much more relaxed about what constitutes an AID. In proxy Diffix, not only was there one AID, but that AID had to come from the same identifier space. This caused real difficulties in cases where more than one type of identifier was "AID-like" (i.e. an account number and a customer number). With a more flexible approach, an analyst can label what the "best" columns are to protect each kind of AID, and the mechanism will do the right thing in each case.

Let's look at some examples of this. Suppose we have three tables, `users`, `accounts`, and `atm`.

- `users` has columns: `ssn`, `age`, `gender`, `zip`, where `ssn` is the social security number.
- `accounts` has columns: `ssn`, `account`.
- `atm` has columns `amount`, `account`, where `amount` is the amount withdrawn from the atm.

Some accounts have two users, and some users have multiple accounts. `accounts` has one row per user per account.

Suppose that the administrator tags the following as being AID columns: `atm.account`, `accounts.ssn`, and `users.ssn`. (Strictly speaking the admin should tag `accounts.account` as well, but let's suppose that he or she doesn't.)

If the analyst queries just the `atm` table, then `atm.account` is protected. In some theoretical sense user is not protected, but it would be very hard for an attacker to exploit this. It might be that one user has four accounts, and that user has very high withdrawals in all those accounts, and so that user isn't adequately flattened, but this is a corner case that I wouldn't worry about in practice.

In any event, if the attacker wants to somehow exploit information about that user, then he has to JOIN with `users` and `accounts`, at which point `ssn` is also brought in as an AID, and so now the user is protected.

> Note that the noise depends on what gets JOINed. Not sure we can or need to do anything about this.

Suppose now that the attacker does a query with `atm JOIN users on amount = age`. Such a query would totally mix up `ssn` and `account` in that individual rows would contain `ssn` and `account` that have nothing to do with each other. Nevertheless, both `ssn` and `account` would be protected by virtue of the two AIDs.

### Problem Case: Proxy AIDs

A difficult case is where a column that is not protected (and cannot be tagged as AID) has a high correlation with something that is protected. An example is this:

Three tables, `paychecks`, `users`, and `companies`. We want to protect both users and companies.

- `paychecks`: `user_id`, `amount`, `date`
- `users`: **`user_id`**, `age`, `zip`
- `companies`: `user_id`, `company`

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

Or, this might then be better solved by specifying multiple columns as separate AID columns instead.

Say you could have:
- `SSN`
- `patient_id`
- `insurance_id`
- `phone_number`

all being classified as AIDs. The hope would then be that at least one of them is the same for a patient that was given multiple `patient_id`s in a system. Thereby at least low count filtering will work.

### Configuration

From the admin point of view, I think the steps to configuration is something like this:

1. Decide what entities will be protected (individual humans, devices, companies, etc.)
2. For each entity, try to find the single best column in each table that identifies that entity.
3. Take care to note that multiple different people can appear in different columns (sender/receiver).
4. Consider whether the identified columns are 100% effective in identifying the entity, or rather have some flaws (sometimes the same person has multiple AID values, sometime the same AID value has multiple persons). In these cases it may be necessary to use multiple columns for the same entity, or to assign multiple more-or-less redundant AIDs.
5. Take care not to tag redundant columns as AID (i.e. two columns that effectively identify the same entities). Doing so just slows down execution time.
6. Tag each column as AID (note not necessary to configure what entity is being protected, or how AIDs relate to each other).

> TODO: Make sure we include different AIDs having different LCF parameters.

> NOTE(Sebastian): I don't think #5 (not tagging redundant AIDs) should be a goal. In fact tagging all possible AID columns could be the conservative safe thing to do. It certainly adds runtime overhead but helps protect against dirty data (same individual under multiple IDs). I think we should measure the performance impact before making a recommendation against tagging multiple AIDs.


## Flattening - Determining extreme and top values

When aggregating, we flatten extreme values and replace their values with those of a representative top-group average.

The process for flattening extreme values is done separately for each AID set. That is to say, for a
dataset with AID sets `[email-1; email-2; company]`, the process is repeated three times, even though
there are only two kinds of AIDs. For each AID set, we calculate the absolute amount of flattening required.
We use the highest flattening and noise parameters calculated across all the AID sets.

We flatten aggregates in subqueries as well in the final anonymization step. The process is identical with two significant
differences:

- for the final anonymized aggregate value, we report `null` when we do not have enough distinct entities to produce an anonymous aggregate. When aggregating i-rows this is not the case, and we follow an alternative approach described in more detail below
- for the final anonymize aggregate value we add noise proportional to the top group average. No such noise is added to intermediate aggregate values

If the three AID sets in the example above resulted in the following flattening
and top group averages:

- `email-1`: flattening 4000, top group average 250
- `email-2`: flattening 4500, top group average 100
- `company`: flattening 250, top group average 500

Then we would use the flattening parameters associated with `email-2`, and the
top group average from `company` for the noise.
The real aggregate value would be flattened by 4500 and in the case of a
fully anonymize aggregate value we would additionally add noise value proportional to 500.


### Redistribution of values

When aggregating rows of data the rows might either be d-rows (each row belongs to a single AID per AID type, such as `email[1]`) or
i-rows that have already gone through one or more rounds of aggregation, in which case a row can belong to a set of AIDs per type (such as `email[1, 2, 3]`).

In the case where we have AID sets it can also happen that the same entity is present in multiple distinct AID sets.
For example, you could have three rows such as:

| Value | AID set               |
| ----: | --------------------- |
|     1 | email[**1**]          |
|     2 | email[**1**, 2]       |
|     3 | email[**1**, 3, 4, 5] |

As part of the flattening of outliers we calculate the contribution each entity has made.
In the example table above entity 1 has contributed to each of the three values.
When calculating an individuals contributions we split their shared contributions proportionally with
the other AIDs.

The entity with email AID 1 would end up with a contribution of: `1 + 2/2 + 3/4 = 2.75` (which could be interpreted as the entity having contributed 2.75 emails).

This approach to distributing contributions is not strictly speaking correct. That is to say that the resulting contributions after the redistribution
do not reflect reality. However, as the only way an AID set of multiple AID values can arise is through
aggregation, and as we always perform extreme value flattening when aggregating, it seems likely that this is not
going to cause insufficient flattening for extreme contributions (note: this is an assumption that has not been tested).


### Algorithm

The process for suppressing extreme values is as follows:

1. Produce per-AID contributions as per the aggregate being generated
   (count of rows, sum of row contributions, min/max etc). For aid sets such as
   `email[1]` and `email[1, 2, 3]` an AID would be something like the value 1.
   When a contribution is for a set of entities (such as `email[1, 2, 3]`) the
   contribution is made proportional per AID value.
2. Sort the contribution values to be flattened in descending order
3. Produce noisy extreme values count (`Ne`) and top count (`Nt`) values
4. Take the first `Ne` highest contributions as the extreme values.
5. Take next `Nt` highest contributions as the top count.
6. If there is a value within the `Ne + Nt` top values that appears for `minimum_allowed_aids` distinct AIDs, then use that value as the the top group average
7. (only for the final anonymizing aggregation) If there are less than `Ne + Nt` many distinct entities then stop and return `null` to indicate that
  there were was not enough data
8. (when intermediate aggregation) If there are less than `Ne + Nt` many distinct entities, then use:
   1. If no entities were chosen for the top group, then use the entity with the lowest contribution as the top group and continue, or
   2. If some values were chosen for the top group, but not quite `Nt` distinct ones, then use the values chosen and continue
9. Calculate the average of the `Nt` values
10. Calculate the **total distortion** as the sum of differences between the values of each entity in the `Ne` set with the `Nt`-average

Once the algorithm has been completed for each AID set individual, we continue based on the AID set with the largest amount of flattening.
In the case of the fully anonymizing aggregation in the top-most query we also add the largest noise amount required across the AID sets.

Below are concrete examples of the algorithm applied to data. The `minimum_allowed_aids` threshold is 2 across all AID types unless otherwise stated.

Note that the tables as shown are the row contributions used to calculate an aggregate.

For example, the following table should be read as AID 1 contributing 6 to the aggregate, AID 2 contributing 5, and AIDs 3 and 4 collectively contributing 4 (as a result of the rows already having been aggregated). `Value` is not to be confused with a row that is being grouped by!

| Value | AID sets  |
| ----: | --------- |
|     6 | AID[1]    |
|     5 | AID[2]    |
|     4 | AID[3, 4] |

The `Value`s can be interpreted as an entity having contributed that number of rows in the case of a `count` aggregator, or it could
the the per entity sum contribution in the case of a `sum` aggregator.

### Examples

#### Early termination

| Value | AID sets   |
| ----: | ---------- |
|    10 | AID1[1, 2] |

- We produce per-AID contributions. In this case the value `10` is a shared contribution between the AID values 1 and 2. We attribute 5 to each of them (see [redistribution of values](#redistribution-of-values) for details).

| Value | AID | Required flattening by AID |
| ----: | --- | -------------------------: |
|     5 | 1   |                          0 |
|     5 | 2   |                          0 |

- The rows are sorted in descending order of `Value`
- `Ne = 2`, `Nt = 2`
- We take the `Ne` first values as extreme values. The values are both the same, and since `minimum_allowed_aids = 2` they would satisfy low count filtering.
- We use 5 as the top group average which means there is no flattening necessary


#### Insufficient data

| Value | AID sets   |
| ----: | ---------- |
|    10 | AID1[1, 2] |
|     1 | AID1[1]    |

- We produce per-AID contributions. In this case the value `10` is a shared contribution between the AID values 1 and 2. We attribute 5 to each of them. AID 1 has an additional entry too, making for uneven total contributions for the two AIDs:

| Value | AID | Required flattening by AID if intermediate aggregator |
| ----: | --- | ----------------------------------------------------: |
|     6 | 1   |                                                     1 |
|     5 | 2   |                                                       |

- The rows are sorted in descending order of `Value`
- `Ne = 2`, `Nt = 2`
- We take the `Ne` first values as the extreme values
- We try taking the `Nt` next rows for the top group, but have run out of data.
  - If this was a final anonymized aggregate (i.e. the top level query), then we would terminate the process and return `null` to indicate that we could not produce an anonymous aggregate,
  - If this was an intermediate aggregation step meant, then we follow the alternative approach of instead using the value with the lowest contribution as the top group average. In this case the value 5 becomes the top group average substitute.
- The total distortion is _infinite_... sort of in the case of this being a top-level aggregator, and 1 for intermediate ones.


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

|           Value |  AID | Required flattening by AID |
| --------------: | ---: | -------------------------: |
| 11.5 = 10 + 3/2 |    1 |                          4 |
|  10.5 = 9 + 3/2 |    2 |                          3 |
|               8 |    3 |                            |
|               7 |    4 |                            |
|               6 |    5 |                            |
|               5 |    6 |                            |
|               4 |    7 |                            |

- `Ne = 2`, `Nt = 2`
- We take the `Ne` first values as extreme values (11.5 and 10.5)
- We take the `Nt` next values as the top group (8 and 7)
- We replace the `Ne` values with the average of the top group: 7.5
- The total flattening is **7** (11.5 - 7.5 + 10.5 - 7.5)


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

- We produce per-AID contributions. In this case, the value 9 is shared, and so are the values 5 and 4. These values are distributed amongst the corresponding AIDs.
- The rows are sorted in descending order of `Value`

The resulting list of AID contributions becomes:

|                 Value | AID | Required flattening by AID |
| --------------------: | --- | -------------------------: |
| 15.3 = 10 + 9/2 + 4/5 | 1   |                       9.75 |
|  13.3 = 8 + 9/2 + 4/5 | 2   |                       7.75 |
|   9.3 = 6 + 5/2 + 4/5 | 4   |                       3.75 |
|         7.8 = 7 + 4/5 | 3   |                            |
|       3.3 = 5/2 + 4/5 | 5   |                            |


- `Ne = 3`, `Nt = 2`
- We take the `Ne` first values as extreme values (15.3, 13.3, 9.3)
- We take the `Nt` next values as the top group (7.8 and 3.3)
- We replace the `Ne` values with the average of the top group: 5.55
- The total flattening is **21.25** (15.3 - 5.55 + 13.3 - 5.55 + 9.3 - 5.55)


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

- We produce per-AID contributions. In this case, the values 10 and 6 are shared. These are distributed amongst the corresponding AIDs.
- The rows are sorted in descending order of `Value`

|                   Value |  AID | Required flattening by AID |
| ----------------------: | ---: | -------------------------: |
| 26 = 8 + 7 + 10/2 + 6/1 |    1 |                      20.25 |
|         11 = 10/2 + 6/1 |    2 |                       5.25 |
|                       9 |    3 |                            |
|               2.5 = 5/2 |    4 |                            |
|               2.5 = 5/2 |    5 |                            |

- `Ne = 2`, `Nt = 2`
- We take the `Ne` first values as extreme values (26, 11)
- We take the `Nt` next values as the top group (9, 2.5)
- We replace the `Ne` values with the average of the top group: 5.75
- The total flattening for AID1 is **25.5** (26 - 5.75 + 11 - 5.75)


##### AID2

| Value | AID sets   |
| ----: | ---------- |
|    10 | AID2[1]    |
|     9 | AID2[2]    |
|     8 | AID2[1, 2] |
|     7 | AID2[3]    |
|     6 | AID2[1]    |
|     5 | AID2[4]    |

- We produce per-AID contributions. In this case, the value 8 is shared. It is distributed amongst the corresponding AIDs.
- The rows are sorted in descending order of `Value`

|         Value |  AID | Required flattening by AID |
| ------------: | ---: | -------------------------: |
| 14 = 10 + 8/2 |    1 |                          8 |
|  13 = 9 + 8/2 |    2 |                          7 |
|             7 |    3 |                            |
|             6 |    1 |                            |
|             5 |    4 |                            |

- `Ne = 2`, `Nt = 3`
- We take the `Ne` first values as extreme values (14, 13)
- We take the `Nt` next values as the top group (7, 6, 5)
- We replace the `Ne` values with the average of the top group: 6
- The total flattening for AID1 is **15** (14 - 6 + 13 - 6)


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
- We do not have enough extreme values to form a group of `Ne` extreme values
  - If this is an intermediate aggregation step, we flatten 0 and continue
  - If this is the top-level aggregation step producing an anonymous value we have to return `null` instead

Even though we could have produced an aggregate from the perspectives of AID1 and AID2,
we cannot produce a final aggregate as we have insufficiently many AID3 entities represented.
Assuming there had been enough AID3 entities and that the total flattening due to AID3 had been 10,
then we would have used the flattening by 25.5 due to AID1 and added noise proportional to the top group average 6 of AID2.


# Seeding

All AIDs that can influence the result in some way must be represented when seeding the random number generator
used to generate threshold and noise values during anonymization. An AID has the ability to influence the result
of a query as soon as the table in which it is defined is part of a query.

If we did not seed the random number generators by all AID values across all AIDs, then the threshold values used for one AID
might be entirely uninfluenced by one or more other AIDs. This could in turn lead to attacks whereby a value change for
one of these AIDs without influence over the noise might result in a query result change without a change in noise.

Where stateful random number generators are used, it is important that the analyst cannot influence
the order in which per-AID random values are generated. We can achieve this either through creating a consistent and
deterministic ordering of the AIDs for our internal data processing, or otherwise by using the same
extreme and top value thresholds for flattening across all AIDs.


# Rationale

The reason we choose the largest standard deviation (versus summing all of the standard deviations from all of the AIDs) is that the noise from a smaller standard deviation is masked by the noise of any larger standard deviation.

The reason we choose the largest flattening value (versus the sum of flattening values) is less obvious and best explained by example. Suppose that a query has two AIDs, AID1 and AID2. Suppose that the extreme values are as follows:

|  val | AID1 | AID2 |
| ---: | ---- | ---- |
| 2000 | 1    | A    |
|  900 | 2    | A    |
|  900 | 3    | A    |
|  900 | 4    | B    |
|  900 | 5    | B    |
|  900 | 6    | B    |
|  900 | 7    | B    |
|  500 | 8    | C    |
|  500 | 9    | D    |
|  ... | ...  | ...  |

where the ... rows have val=500, increasing values of AID1 and AID2.
Assuming that both the extreme and top groups (`Ne` and `Nt`) is of size two, then using the algorithm described above we
get the following suppression for AID1:

|  val | AID1 | flatten AID1 |
| ---: | ---- | -----------: |
| 2000 | 1    |         1100 |
|  900 | 2    |            0 |
|  900 | 3    |            - |
|  900 | 4    |            - |
|  900 | 5    |            - |
|  900 | 6    |            - |
|  900 | 7    |            - |
|  500 | 8    |            - |
|  500 | 9    |            - |
|  ... | ...  |          ... |

i.e. a total flattening of 1100 because the value 2000 for AID1 is replaced with the top group average of 900.

For AID2 the suppression ends up as follows:

|  val | AID2 | flatten AID2 |
| ---: | ---- | -----------: |
| 3800 | A    |         3300 |
| 3600 | B    |         3100 |
|  500 | C    |            - |
|  500 | D    |            - |
|  ... | ...  |          ... |

i.e. a total flattening of 6400 as the values 3800 for AID2[A] and 3600 for AID2[B] are both replaced with the top
group average of 500.

We choose the larger of the two, so `flatten = 6400`, and noise is proportional to 500.

Assume that `sum()` is the aggregate, and that the true sum in 20000. Then the noisy sum is 13600+noise.

Now suppose an attacker knows that user AID1[1] has extreme value 2000, and wants to determine if AID1[1]
is in the answer or not. So he generates another query with `WHERE AID1 <> 1`, thus removing AID1[1] from the second query for sure.
The resulting table looks like this:

|  val | AID1 | AID2 |
| ---: | ---- | ---- |
|  900 | 2    | A    |
|  900 | 3    | A    |
|  900 | 4    | B    |
|  900 | 5    | B    |
|  900 | 6    | B    |
|  900 | 7    | B    |
|  500 | 8    | C    |
|  500 | 9    | D    |
|  ... | ...  | ...  |

which yields the following for AID1:

| val | AID1 | flatten AID1 |
| --- | ---- | -----------: |
| 900 | 2    |            0 |
| 900 | 3    |            0 |
| 900 | 4    |            - |
| 900 | 5    |            - |
| 900 | 6    |            - |
| 900 | 7    |            - |
| 500 | 8    |            - |
| 500 | 9    |            - |
| ... | ...  |          ... |

and for AID2:

|  val | AID2 | flatten AID2 |
| ---: | ---- | -----------: |
| 3600 | B    |         3100 |
| 1800 | A    |         1300 |
|  500 | C    |            - |
|  500 | D    |            - |
|  ... | ...  |          ... |

In this case, there is no flattening for AID1, and flattening for AID2 is 4400. The true sum is now 18000 and the noisy sum is 13600+noise. So:

* First query: 13600+noise
* Second query: 13600+noise
* Difference: 0+noise

Here the noise is proportional to the absolute difference, and so the attacker is unsure if the victim AID1[1] was in the first query or not.

The general observation is that a given row will have some contribution to flattening for all of the AIDs, and so it is only necessary to flatten for one of the AIDs (that with the most flattening). Furthermore, if we flatten for all AIDs, then any given row is being flattened multiple times, which we do not want.

> TODO: There may be counter-examples, so we might want to look at this more.