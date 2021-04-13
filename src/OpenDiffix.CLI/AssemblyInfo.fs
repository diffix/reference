module OpenDiffix.CLI.AssemblyInfo

open System.Reflection

[<assembly: AssemblyVersion(ThisAssembly.Git.BaseVersion.Major
                            + "."
                            + ThisAssembly.Git.BaseVersion.Minor
                            + "."
                            + ThisAssembly.Git.BaseVersion.Patch)>]

[<assembly: AssemblyFileVersion(ThisAssembly.Git.SemVer.Major
                                + "."
                                + ThisAssembly.Git.SemVer.Minor
                                + "."
                                + ThisAssembly.Git.SemVer.Patch)>]

[<assembly: AssemblyInformationalVersion(ThisAssembly.Git.SemVer.Major
                                         + "."
                                         + ThisAssembly.Git.SemVer.Minor
                                         + "."
                                         + ThisAssembly.Git.SemVer.Patch
                                         + "."
                                         + ThisAssembly.Git.Commits
                                         + "-"
                                         + ThisAssembly.Git.Branch
                                         + "+"
                                         + ThisAssembly.Git.Commit)>]

do ()

open Thoth.Json.Net

let versionJsonValue =
  Encode.object [
    "name", Encode.string "OpenDiffix Reference implementation"
    "version",
    Encode.object [
      "version",
      Encode.string (
        sprintf "%s.%s.%s" ThisAssembly.Git.SemVer.Major ThisAssembly.Git.SemVer.Minor ThisAssembly.Git.SemVer.Patch
      )
      "commit_number", Encode.int (int ThisAssembly.Git.Commits)
      "branch", Encode.string ThisAssembly.Git.Branch
      "sha", Encode.string ThisAssembly.Git.Commit
      "dirty_build", Encode.bool ThisAssembly.Git.IsDirty
    ]
  ]
