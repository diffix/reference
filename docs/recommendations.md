Please consult the [glossary](glossary.md) for definitions of terms used in this document.

- [Configuring AIDs](#configuring-aids)
  - [Multiple AID values per entity](#multiple-aid-values-per-entity)
    - [Missing AID values](#missing-aid-values)
    - [Risk of too many AID columns](#risk-of-too-many-aid-columns)

# Configuring AIDs

It is recommended to specify redundant AID columns for an entity.
In a `patients` table one might for example mark both `ssn` and a `patient_id` columns as AIDs.
This redundancy, while it comes at the cost of additional runtime overhead, guards against dirty data.

Dirty data can come in many forms:

- multiple AID values per entity
- missing AID values

## Multiple AID values per entity

When record-keeping is done manually, chances are that an entity might end up
registered multiple times, under multiple AID values. If only an internal system ID is
used as the AID, then such a system might leak PII if an individual occurs frequently
enough.

If multiple columns might identify the individual, there is the chance that
one of these will be the same across the distinct copies of the records of an entity.

In the following example, we see that even though Bob has been entered four times into the
database, the use of three AIDs (`system aid`, `ssn`, and `phone`) collapse what would otherwise
appear to the anonymizer as four distinct individuals into a single one for the purposes of
low count filtering and aggregate flattening:

| System AID value | SSN AID value | Phone AID value | Name        |
| ---------------- | ------------- | --------------: | ----------- |
| 1                | 1234          |  0176 7777 1212 | Bob Fisher  |
| 2                | 1234          |  0176 7777 1212 | B. Fisher   |
| 3                | 1234          |   176 7777 1212 | Bob Fischer |
| 4                | 1235          |  0176 7777 1212 | Bob Fisher  |


### Missing AID values

At times AID values are missing. This can lead to insufficient extreme value flattening as records
cannot be tied back to an entity. By declaring multiple AIDs per entity, there is the chance that one of
these might be non-null and therefore usable for anonymization.

In the following example, we see that even though the transactions records are spottily kept,
the partially missing AID values still allow the records to be sufficiently tied together
using the `user_id` and `ssn` AIDs:

| user_id AID value | SSN AID value |    Amount | Description          |
| ----------------- | ------------- | --------: | -------------------- |
| 1                 | null          |       2.7 | Metro ticket         |
| 1                 | 1234          |      10.0 | Cinema ticket        |
| null              | 1234          |       540 | Airline ticket       |
| 1                 | null          | 198999.99 | Space shuttle ticket |


### Risk of too many AID columns

One shouldn't go overboard when configuring AIDs. Columns that stand a good chance of not being unique
(like a surname or [birth date](https://en.wikipedia.org/wiki/Birthday_problem)) could very well cause over-flattening,
larger noise values than would be necessary, and unnecessary suppression of values.

A database that is large enough might also encounter values being recycled. An example could be phone numbers
which can see reuse if abandoned by a subscriber.

While the strength of the anonymization is unlikely to be reduced by specifying too many AID columns,
it is likely that both the data quality and runtime characteristics will suffer unnecessarily.