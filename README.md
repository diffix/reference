# open-diffix reference implementation

- [Purpose](#purpose)
- [Development process](#development-process)
  - [Design considerations](#design-considerations)
  - [Organization](#organization)
  - [Branches](#branches)

## Purpose

This is a reference implementation of open-diffix.
As such, this serves as a sandbox in which we can quickly try, and validate, new ideas for anonymization.
The reference implementation is meant to offer anonymization quality matching that of a final product - however
not necessarily SQL parity. It is not mean to be be used in production. As such it will not receive the type of polish
and usability work a commercial product would. It can safely be used to anonymize data, but there will be rough
edges.

## Development process

The concepts implemented will at times be complex. We therefore do not skimp on code quality or legibility.
Code going into the `master`-branch is peer-reviewed. Tests should pass, etc.

The tests rely on the presence of a test database existing. 
For more information on how to create it, please read the [data/README](data/README.md).

### Design considerations

We use SQLite as a dumb backing store for data. Dumb in the sense that we just read the data out of it and
otherwise rely on our own code to perform the query execution. The reason for this is that it allows us to
perform all the operations we would otherwise do in a database, but without the constraints of having to work
around the complex and large codebase of a real world database engine.

### Organization

The codebase is currently organized in a number of projects:

- `OpenDiffix.Core` and `OpenDiffix.Core.Tests`: Contains the meat of this project. It is the query and anonymization engine.
- `OpenDiffix.CLI`: A command line interface that can be used to exercise the reference implementation.

### Branches

To avoid merge conflicts we work on feature branches. Once automated tests pass it can either be reviewed
or merged into `master`.