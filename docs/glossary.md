# Glossary

The following terms are frequently used throughout the other documents.

- [Glossary](#glossary)
  - [AID - Anonymization Identifier](#aid---anonymization-identifier)
  - [bucket](#bucket)
  - [d-row - database row](#d-row---database-row)
  - [entity](#entity)
  - [i-row - intermediate row](#i-row---intermediate-row)
  - [Relation](#relation)
  - [Selectable](#selectable)


## AID - Anonymization Identifier

AID stands for Anonymization Identifier. It is the term we use to specify a column used for the purposes of anonymization.

AID replaces the term UID (user identifier) which we used in earlier versions of Diffix. The name change is meant to allow for specifying sensitive entities other than human beings.

There are multiple terms relating to AIDs that serve slightly different purposes.
These are:

- AID or AID-column
- AID instance or AID-i
- AID value
- AID value set

You can see how these terms relate in the graphic below:

![The hierarchy of AID related terms](graphics/AID%20terminology.png)

These terms might become clearer when illustrated with an example.
Let's say we have a `patients` table with an `id` column.
The `id` column is used to identify the patients and the patients are the [entities](#entity) we
want to protect through anonymization. In this case, the `id` is the AID-column (or AID for short).

When writing a query an instance of this AID-column will be used for anonymization.
In the query example below we even have two instances of the same AID, one stemming from the left side of
the join, and the other from the right. We would call these AID instances `id-1` and `id-2`, or
`patients.id-1` and `patients.id-2` if you want to fully qualify them.

```sql
SELECT count(*)
FROM patients as left, patients as right
```

A patient's id might have a value such as `#1` or `#2`. We call these AID values. An AID value is
what uniquely identifies an entity (in this case, a patient).
As a result of intermediate aggregation, you can end up with multiple AID values being associated with
a single [i-row](#i-row---intermediate-row). These are called AID value sets and could be written as `patients.id-1[#1, #2]`
indicating a value contributed collectively by patients `#1` and `#2`.

The equivalent of the graphic above for the `patients` table's `id` column would be:

![AID terms for patients table](graphics/AID%20terminology%20patients.png)


## bucket

An anonymous row in the output result table (with or without associated AID metadata).


## d-row - database row

A row from the database. As supposed to an [i-row](#irow---intermediate-row) which can contain arbitrary additional metadata.


## entity

An **entity** is a thing we look to protect through anonymization.
In older versions of Diffix we used **user**, but have since moved away from this term as
it implies that what is being protected is always a human being.

An entity might be a human being (such as a patient or customer), but it could equally well
be some abstract thing like a circuit, computer, or cell phone.

An entity might have multiple AIDs associated with it. For example, we have might have the AIDs
`customer_no`, `ssn` and `email` all associated with the same customer entity.


## i-row - intermediate row

A row that has undergone intermediate processing but is not a database row or an anonymized bucket.
Such intermediate processing might be non-anonymization intermediate aggregation which doesn't result in an anonymous value.


## Relation

**relation** is the term used by Postgres to specify something that, broadly speaking, can be selected from. In our usage it will most often refer to a database table or a view.

In Postgres the following types of relations exist:

> r = ordinary table, i = index, S = sequence, v = view, m = materialized view, c = composite type, t = TOAST table, f = foreign table


## Selectable

Is a **relation** or a **subquery**. It is broadly speaking anything that produces a set of column values that can be consumed by a query.