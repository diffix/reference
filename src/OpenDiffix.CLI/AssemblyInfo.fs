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

let version =
  sprintf
    "%s.%s.%s (commits: %s / %s / %s) %s"
    ThisAssembly.Git.SemVer.Major
    ThisAssembly.Git.SemVer.Minor
    ThisAssembly.Git.SemVer.Patch
    ThisAssembly.Git.Commits
    ThisAssembly.Git.Branch
    ThisAssembly.Git.Commit
    (if ThisAssembly.Git.IsDirtyString = "true" then "- Contains uncommitted changes!" else "")
