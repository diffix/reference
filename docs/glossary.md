# Glossary

The following terms are frequently used throughout the other documents.

- [Glossary](#glossary)
  - [AID - Anonymization Identifier](#aid---anonymization-identifier)
  - [bucket](#bucket)
  - [d-row - database row](#d-row---database-row)
  - [i-row - intermediate row](#i-row---intermediate-row)
  - [Relation](#relation)

## AID - Anonymization Identifier

AID stands for Anonymization Identifier. It is the term we use to specify a column or column value that is used
by the system to identify the entity a piece of data belongs to.

AID replaces the term UID (user identifier) which we used in earlier versions of Diffix. The name change is meant to allow for specifying sensitive entities other than human beings.

A piece of data (for example a database row) might belong to multiple AIDs. Examples of this is where a database row describes something like a transaction taking place between a sender and a recipient (in which case the row has two AID types defined) or where an aggregate row describes something that might pertain to multiple distinct entities of the same AID type.


## bucket

An anonymous row in the output result table (with or without associated AID metadata).


## d-row - database row

A row from the database. As supposed to an [i-row](#irow---intermediate-row) which can contain arbitrary additional metadata.


## i-row - intermediate row

A row that has undergone intermediate processing but is not a database row or an anonymized bucket.
Such intermediate processing might be non-anonymization intermediate aggregation which doesn't result in an anonymous value.


## Relation

**relation** is the term used by Postgres to specify something that, broadly speaking, can be selected from.
A relation might be a database table, or it could be a view, or for that matter a subquery.