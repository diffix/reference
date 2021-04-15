# Glossary

The following terms are frequently used throughout the other documents.

- [Glossary](#glossary)
  - [AID - Anonymization Identifier](#aid---anonymization-identifier)
    - [AID type](#aid-type)
    - [AID](#aid)
    - [AID value](#aid-value)
    - [AID value set](#aid-value-set)
  - [bucket](#bucket)
  - [d-row - database row](#d-row---database-row)
  - [entity](#entity)
  - [i-row - intermediate row](#i-row---intermediate-row)
  - [Relation](#relation)
  - [Selectable](#selectable)


## AID - Anonymization Identifier

AID stands for Anonymization Identifier. It is the term we use to specify a column that is used
by the system to identify the entities that are protected.

AID replaces the term UID (user identifier) that we used in earlier versions of Diffix. The name change allows us to specify
sensitive entities other than human beings.

There are several AID related terms. Let's go through them one by one:

### AID type

And AID type is a column in a specific table. For example we might have a social security number column in
a patients table: `patients.ssn`. An AID type might have anonymization properties attached to it, such as how
many distinct AID values must be present (`minimum_allowed_aids`) or the sizes of the extreme and top groups
during aggregation.

### AID

An AID type might occur multiple times in the same query! Each of these AID instances is an AID.
To distinguish between such AIDs of the same AID type in writing, we tend to suffix them with an index.
So two instances of a `ssn` AID type in a query result might be represented as `ssn-1` and `ssn-2`.
Each of these AIDs share the anonymization properties of their AID type. So if `minimum_allowed_aids = 2`
for `ssn`, then so does is the `minimum_allowed_aids` for both `ssn-1` and `ssn-2` as well.

Multiple AIDs of the same type might occur as a result of joining a table with itself:

```sql
SELECT a.ssn, b.ssn, count(*)
FROM patients a, patients b ON a.dayOfMonth = b.dayOfMonth
GROUP BY 1, 2
```

### AID value

An AID likely represents multiple entities.
For example, in our `patients` table, we likely have the data of multiple patients.
Each of these patients has an AID value of their own.

An example might be: `patients.ssn[110385-12345]`

An AID value uniquely defines an entity.


### AID value set

Through intermediate aggregation (in a subquery, for example) we get rows belonging to multiple entities.
For example, if we count the numbers of doctors visit by day, then for a given day, the AID values
`patients.ssn[1]`, `patients.ssn[2]`, and `patients.ssn[3]` might all have data in the dataset.
The count for that given day represents all three entities, and we express this with an AID value set.
We denote such a set as `patients.ssn[1, 2, 3]`.


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

An entity might have multiple AIDs associated with it. For example we have might have the AIDs
`customer_no`, `ssn` and `email` all associated with the a customer entity.


## i-row - intermediate row

A row that has undergone intermediate processing but is not a database row or an anonymized bucket.
Such intermediate processing might be non-anonymization intermediate aggregation which doesn't result in an anonymous value.


## Relation

**relation** is the term used by Postgres to specify something that, broadly speaking, can be selected from. In our usage it will most often refer to a database table or a view.

In Postgres the following types of relations exist:

> r = ordinary table, i = index, S = sequence, v = view, m = materialized view, c = composite type, t = TOAST table, f = foreign table


## Selectable

Is a **relation** or a **subquery**. It is broadly speaking anything that produces a set of column values that can be consumed by a query.