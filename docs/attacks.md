# Attacks

Contents:
- [Attacks](#attacks)
  - [Wide then narrow](#wide-then-narrow)
    - [AID value sets are merged](#aid-value-sets-are-merged)
      - [Attack query](#attack-query)
    - [AID value sets are column value properties and do not mix](#aid-value-sets-are-column-value-properties-and-do-not-mix)
      - [Attack query](#attack-query-1)
  - [Missing AID data](#missing-aid-data)


## Wide then narrow

"Wide then narrow" is an attack whereby an attacker joins tables in a subquery, but later only selects from one of the two join halves in a parent query. The other side of the join is only used to influence what data makes it to the parent query, and thereby into the final result, without itself being present in the result.

These attacks can be done in ways where the values that are selected seem harmless to the anonymization engine and are thus not anonymized properly, letting information about single individuals through.

**NOTE**: The two attack forms presented below are prevented by being very particular about how AID instances originating from different selectables are treated.
The [multiple-aid](multiple-aid.md) document describes this design in details, but to explain why the design is the way it is, let's consider two other ways in which AID instances and AID value sets could be joined, and how these make a "wide then narrow" attack possible.

Alternative handling of AIDs:
1. AID value sets are merged
2. AID value sets are column value properties and do not mix

In both of the attack sketches below, we will be operating on the following example `patients` table:

| Name    | SSN  | HasCancer | SmokesToMuch | City  | Comment                              |
| ------- | ---- | --------- | ------------ | ----- | ------------------------------------ |
| Alice   | 1234 | True      | True         | Cairo | This is the victim we want to attack |
| Bob     | 2345 | ...       | ...          | Cairo |                                      |
| Cynthia | ...  | ...       | ...          | Cairo |                                      |
| Donny   | ...  | ...       | ...          | Cairo |                                      |
| Elsa    | ...  | ...       | ...          | Cairo |                                      |
| Fredrik | ...  | ...       | ...          | Cairo |                                      |
| Günther | ...  | ...       | ...          | Cairo |                                      |

### AID value sets are merged

Say we are joining two instances of the same `patients` table. `ssn` has been defined as the AID column.
Under this particular scheme of handling AIDs the resulting AID value set for a row is the union of the AID value sets of the rows that are joined.
In other words, we merge the AID value sets for a given AID column.

If we joined the rows from `left` and `right` on the `City` columns:

`left`:

| Name  | AID value set | City  |
| ----- | ------------- | ----- |
| Alice | **SSN[1234]** | Cairo |

`right`:

| Name | AID value set | City  |
| ---- | ------------- | ----- |
| Bob  | **SSN[2345]** | Cairo |

Then the resulting joined row would look like this:

| Left.name | Right.Name | Combined AID value set | {Left, Right}.City |
| --------- | ---------- | ---------------------- | ------------------ |
| Alice     | Bob        | **SSN[1234, 2345]**    | Cairo              |

i.e. the row now both belongs to Alice _and_ Bob (`SSN[1234, 2345]`).

#### Attack query

The "wide then narrow" attack can be executed with the following query:

```sql
SELECT left.*
FROM (
  SELECT *
  FROM patients
) left, (
  SELECT city, count(*)
  FROM patients
  GROUP BY city
) right
```

The `left` side of the join is the original table as shown above.
The `right` side however is the single row:

| City  | Count | AID value set        |
| ----- | ----- | -------------------- |
| Cairo | 7     | SSN[1234, 2345, ...] |

Since we are doing a cross join here, each resulting row would look like this

| Left.Name | Left.SSN | Left.HasCancer | Left.SmokesToMuch | {Left, Right}.City | Combined AID value set |
| --------- | -------- | -------------- | ----------------- | ------------------ | ---------------------- |
| Alice     | 1234     | True           | True              | Cairo              | SSN[1234, 2345, ...]   |
| Bob       | 2345     | ...            | ...               | Cairo              | SSN[1234, 2345, ...]   |
| Cynthia   | ...      | ...            | ...               | Cairo              | SSN[1234, 2345, ...]   |
| Donny     | ...      | ...            | ...               | Cairo              | SSN[1234, 2345, ...]   |
| Elsa      | ...      | ...            | ...               | Cairo              | SSN[1234, 2345, ...]   |
| Fredrik   | ...      | ...            | ...               | Cairo              | SSN[1234, 2345, ...]   |
| Günther   | ...      | ...            | ...               | Cairo              | SSN[1234, 2345, ...]   |

As a result, when it comes to performing the low count filter check, it will appear to the anonymizer as if every single row has more than the `minimum_allowed_ids` and hence are safe to be reported. No filtering will take place, and as a result, the attacker has been able to read the entirety of the `patients` table in a single query.


### AID value sets are column value properties and do not mix

Say we are joining two instances of the same `patients` table. `ssn` has been defined as the AID column.
Under this particular scheme of handling AIDs, the AID value sets are associated with the column values themselves.
As such the AID instances from two halves of a join do not mix.

Under this particular scheme of handling AIDs, if we joined the rows from `left` and `right` on the `City` columns:

`left`:

| Name  | AID value set | City  |
| ----- | ------------- | ----- |
| Alice | SSN[1234]     | Cairo |

`right`:

| Name | AID value set | City  |
| ---- | ------------- | ----- |
| Bob  | SSN[2345]     | Cairo |

Then the resulting joined row would look like this:

| Left.name | Left AID value set | Right.Name | {Left, Right}.City | Right AID value set |
| --------- | ------------------ | ---------- | ------------------ | ------------------- |
| Alice     | SSN[1234]          | Bob        | Cairo              | SSN[2345]           |


#### Attack query

The "wide then narrow" attack can be executed with the following query:

```sql
SELECT right.victimExistsIfPresent
FROM (
  SELECT city
  FROM patients
  WHERE name = "Alice" and hasCancer and smokesTooMuch
) left INNER JOIN (
  SELECT city, count(*) victimExistsIfPresent
  FROM patients
  GROUP BY city
) right ON left.city = right.city
```

The `left` side of the join is either the singleton table:

| City  | AID value set |
| ----- | ------------- |
| Cairo | SSN[1234]     |

Or entirely an empty table.

The `right` side of the query results in the following table:

| City  | Count | AID value set        |
| ----- | ----- | -------------------- |
| Cairo | 7     | SSN[1234, 2345, ...] |

Since we are doing an inner join the join will either yield something if the victim has the properties,
or an empty result set if the victim does not exist with the given properties.

Through this attack we can learn any property of any entity by asking a series of yes/no queries.

## Missing AID data

It is unclear whether this attack is exploitable, but here it is recorded for posterity.

In a case where AID data is incomplete, say in a table like this where half of the AID values for one
of the AID columns are missing:

| AID1 value | AID2 value | Value |
| ---------- | ---------- | ----: |
| 1          | 1          |    10 |
| 2          | 2          |    10 |
| 3          | 3          |    10 |
| 4          | 4          |    10 |
| 5          | null       |    10 |
| 6          | null       |    10 |
| 7          | null       |    10 |
| 8          | null       |    10 |

We might end up with insufficient suppression when aggregating.
This could happen if the data with the missing AID values really should have belonged
to the same entity. We have seen this in the past at one of Aircloak's customers.
An entity with outlier behavior had been poorly entered into the system and would therefore not be
properly accounted for when flattening during aggregation.

The question is whether this is exploitable at all. At present we do not know of a way to do so.

One way to mitigate a situation like this would be to specify redundant AID columns per entity.
These could act as pseudonyms of sorts. There is a chance that at least one of these will be non-null
when the others are left blank.