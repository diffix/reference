# open-diffix reference implementation

- [open-diffix reference implementation](#open-diffix-reference-implementation)
  - [Purpose](#purpose)
  - [Gotcha](#gotcha)
  - [Development process](#development-process)
    - [Anonymization design](#anonymization-design)
    - [Design considerations](#design-considerations)
    - [Organization](#organization)
    - [Branches](#branches)
  - [Creating a release](#creating-a-release)
  - [Using CLI](#using-cli)


## Purpose

This is a reference implementation of open-diffix.
As such, this serves as a sandbox in which we can quickly try, and validate, new ideas for anonymization.
The reference implementation is meant to offer anonymization quality matching that of a final product - however
not necessarily SQL parity. It is not mean to be be used in production. As such it will not receive the type of polish
and usability work a commercial product would. It can safely be used to anonymize data, but there will be rough
edges.

## Gotcha

Due to the way hashes are generated from the AID values, please take the following into consideration when generating
test data for the reference implementation:

**If you use numerical AIDs please make sure they are all positive, or all negative. Failing to do so will result in
the AID space collapsing and the anonymization going wrong. More specifically user -1 will be seen as identical to
user 0, user -2 will be seen as identical to user 1, etc.**

## Development process

The concepts implemented will at times be complex. We therefore do not skimp on code quality or legibility.
Code going into the `master`-branch is peer-reviewed. Tests should pass, etc.

The tests rely on the presence of a test database existing.
For more information on how to create it, please read the [data/README](data/README.md).

### Anonymization design

We keep design nodes in the docs folder.
Currently we describe:

- anonymization specific terms in [glossary.md](docs/glossary.md)
- noise computation in [computing noise.md](docs/computing%20noise.md)
- low count filtering in [lcf.md](docs/lcf.md)
- low effect detection in [led.md](docs/led.md)
- handling of multiple AIDs in [multiple-aid.md](docs/multiple-aid.md)
- how to deal with multiple levels of aggregation in [multi-level-aggregation.md](docs/multi-level-aggregation.md)
- high level attacks and related gotchas in [attacks.md](docs/attacks.md)


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

## Creating a release

To generate an executable of the command line interface, run one of:

- `make release-linux` for Linux
- `make release-macos` for macOS
- execute the `release.bat` file

If the build succeeds, the binary will be placed in the `build` folder. It is self-contained and can be moved anywhere
you desire.

## Using CLI

The reference implementation can be used through the provided command line interface offered as part of the solution.
See "Creating a release" for more information on how to build the command line interface.

The `-h` command gives you an overview of the available paramters. Typical usage should achievable with one of the
two following sample commands:

- Run a single query: `OpenDiffix.CLI -d data/data.sqlite --aid-columns customers.id -q "SELECT city, count(*) FROM
  customers GROUP BY city"`.
- Run a batch of queries (significantly faster if you want to run many queries at one time): `OpenDiffix.CLI
  --queries-file queries-sample.json`. For an example of what the input file format should look like,
  please consult [queries-sample.json].

In both cases the query result will be written back to the terminal (standard out).

