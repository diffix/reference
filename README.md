# open-diffix reference implementation

A live version of the reference implementation can be found at [prototype.open-diffix.org](https://prototype.open-diffix.org).

- [Purpose](#purpose)
- [Development process](#development-process)
  - [Design considerations](#design-considerations)
  - [Organization](#organization)
  - [Branches](#branches)
- [API](#api)

## Purpose

This is a reference implementation of open-diffix.
As such, this serves as a sandbox in which we can quickly try, and validate, new ideas for anonymization.
The reference implementation is meant to offer anonymization quality matching that of a final product - however
not necessarily SQL parity. It is not mean to be productized. As such it will not receive the type of polish
and usability work a commercial product would. It can safely be used to anonymize data, but there will be rough
edges.

## Development process

The concepts implemented will at times be complex. We therefore do not skimp on code quality or legibility.
Code going into the `master`-branch is peer-reviewed. Tests should pass, etc.

### Design considerations

We use SQLite as a dumb backing store for data. Dumb in the sense that we just read the data out of it and
otherwise rely on our own code to perform the query execution. The reason for this is that it allows us to
perform all the operations we would otherwise do in a database, but without the constraints of having to work
around the complex and large codebase of a real world database engine.

### Organization

The codebase is currently organized in a number of projects:

- `OpenDiffix.Core` and `OpenDiffix.Core.Tests`: Contains the meat of this project. It is the query and anonymization engine.
- `OpenDiffix.Web`: API endpoint that is served on [prototype.open-diffix.org](https://prototype.open-diffix.org) and can be used for simple testing with external tools.

### Branches

To avoid merge conflicts we work on feature branches. Once automated tests pass it can either be reviewed
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
  "anonymization_parameters": {
    "aid_columns": ["table_name.aid_column"],
    "seed": 1,
    "low_count_filter": {
      "threshold": 5.0,
      "std_dev": 2.0
    }
  }
}
```

- The `query` field is required and should specify the SQL query to be executed
- The `database` field is required and is the name of a database that has been previously uploaded and that should be used
- The `aid_columns` parameter in the `anonymization_parameters` section is the only required anonymization parameter

### `/api/upload-db`

Requires two HTTP headers to be set:

- `db-name`: the name to be used for the database when subsequently querying it
- `password`: a password to prevent third parties uploading new databases

The body of the request should be the binary representation of the SQLite database file itself.
