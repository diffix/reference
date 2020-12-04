# open-diffix prototype

A live version of this prototype can be found at [db-proto.probsteide.com](https://db-proto.probsteide.com).

- [Purpose](#purpose)
- [Development process](#development-process)
  - [Design considerations](#design-considerations)
  - [Features](#features)
  - [Organization](#organization)
  - [Branches](#branches)
- [API](#api)

## Purpose

This is a prototype. As such, the code written here is not meant to go into production.
The prototype is a playground to test and validate anonymization concept. Both in order to test the quality of anonymization,
and to test our implementation ideas for our anonymization features that hopefully can help guide how a later (more costly) 
implementation in Postgres could be done.

## Development process

This is a low ceremony prototype. The concepts we implement will at times be complex. We therefore do not skimp on
code quality or legibility. We are not however building something that should ever make it into production.
We do not want bad code, code smells, or dead code. Treat it as a properly engineered code base with the standards
we are used to from Aircloak. We do not however necessarily require code reviews for all inclusions.

If you are working on a complex concept and want another pair of eyes, then please feel free to request a review.
If what you are building is fairly straight forward, then please go ahead and merge it yourself without review.

## Design considerations

We use SQLite as a dumb backing store for testing. Dumb in the sense that we just read the data out of it and
otherwise rely mostly on providing the functionality we need in our own codebase. The reason for this is that
it allows us to pretend to be in the middle of the database engine without having to hack or alter any existing
database engine codebase.

### Features

We keep the prototype as simple as possible, adding exactly as much functionality as is required to test the concepts 
we want to validate, not more. For the time being that means supporting queries over single tables (i.e. no JOINs)
and requiring the AID to be explicitly specified by the entity querying the system.

At present the following queries are supported:

- `SHOW tables`
- `SHOW columns FROM tableName`
- `SELECT col1, col2 FROM table`

### Organization

The codebase is currently organized in a number of projects:

- `DiffixEngine` and `DiffixEngine.Tests`: Contains the meat of this project. It is the anonymization engine.
- `SqlParser` and `SqlParser.Tests`: Simplistic SQL parser and corresponding tests.
- `WebFrontend`: API endpoint that is served on [db-proto.probsteide.com](https://db-proto.probsteide.com) and used for testing and validating the anonymization quality

### Branches

To avoid merge conflicts and other nasties we work on feature branches. Once automated tests pass it can either be reviewed
or merged into `master`.

## API

- `GET /`: HTTP endpoint for issuing queries through the browser
- `POST /api`: HTTP API endpoint for issuing queries (see format of queries below)
- `POST /api/upload-db`: Endpoint for uploading new SQLite databases for testing

### `/api`

The `/api` endpoint expects the body of the request to be a JSON payload with the following format:

```json
{
  "query": "SELECT ...",
  "database": "test-db.sqlite",
  "aid_columns": ["aid1"],
  "seed": 1
}
```

- The `database` field is required and is the name of a database that has been previously uploaded and that should be used
- The `aid_columns` is an array. It is optional in the payload but required for `SELECT ...` queries. Currently only a single `AID` value is supported
- The `seed` column is optional and determines the seed used with the PRNG for anonymization

### '/api/upload-db'

Requires two HTTP headers to be set:

- `db-name`: the name to be used for the database when subsequently querying it
- `password`: a password to prevent third parties uploading new databases

The body of the request should be the binary representation of the SQLite database file itself.