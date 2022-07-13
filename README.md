# Open Diffix reference implementation

- [Purpose](#purpose)
- [Development process](#development-process)
  - [Design considerations](#design-considerations)
  - [Organization](#organization)
  - [Branches](#branches)
- [Creating a release](#creating-a-release)
- [Using CLI](#using-cli)


## Purpose

This is a reference implementation of Open Diffix.
As such, this serves as a sandbox in which we can quickly try, and validate, new ideas for anonymization.
The reference implementation is meant to offer anonymization quality matching that of a final product - however
not necessarily SQL parity. It will not receive the type of polish and usability work a commercial product would.
It can safely be used to anonymize data, but there will be rough edges.

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

- `OpenDiffix.Core`: Contains the meat of this project. It is the query and anonymization engine.
  The main public interface to it is the [`QueryEngine.run` function here](src/OpenDiffix.Core/QueryEngine.fs)
- `OpenDiffix.CLI`: A command line interface that can be used to exercise the reference implementation.

### Branches

To avoid merge conflicts we work on feature branches. Once automated tests pass it can either be reviewed
or merged into `master`.

### Formatting

We use `fantomas` for formatting.
It might be beneficial to have a `pre-commit` git hook like the following to ensure the code
you commit to the repository has been formatted:

`.git/hooks/pre-commit`:

```
#!/bin/sh
git diff --cached --name-only --diff-filter=ACM -z | xargs -0 dotnet fantomas
git diff --cached --name-only --diff-filter=ACM -z | xargs -0 git add -p
```

## Creating a release

To generate an executable of the command line interface, run:

```
dotnet publish -r <win|linux|osx>-x64 -o build -c Release \
  /p:PublishSingleFile=true /p:PublishTrimmed=true /p:IncludeNativeLibrariesForSelfExtract=true \
  --self-contained true src/OpenDiffix.CLI/
```

If the build succeeds, the binary will be placed in the `build` folder.
It is self-contained and can be moved anywhere you desire.

## Using CLI

The reference implementation can be used through the provided command line interface offered as part of the solution.
See "Creating a release" for more information on how to build the command line interface.

The `-h` command gives you an overview of the available parameters. Typical usage should achievable with one of the
two following sample commands:

- Run a single query: `OpenDiffix.CLI -f data/data.sqlite --aid-columns customers.id -q "SELECT city, count(*) FROM
  customers GROUP BY city"`.
- Run a batch of queries (significantly faster if you want to run many queries at one time): `OpenDiffix.CLI
  --queries-path queries-sample.json`. For an example of what the input file format should look like,
  please consult [queries-sample.json].

In both cases the query result will be written back to the terminal (standard out).
