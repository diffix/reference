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

AID stands for Anonymization Identifier. It is the term we use to specify a column or column value that is used
by the system to identify the entity that is protected.

AID replaces the term UID (user identifier) which we used in earlier versions of Diffix. The name change is meant to allow for specifying sensitive entities other than human beings.

A piece of data (for example a d-row) might belong to multiple AIDs. Examples of this is where a d-row describes something like a transaction taking place between a sender and a recipient (in which case the row has two AID types defined) or where an aggregate row describes something that might pertain to multiple distinct entities of the same AID type.


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