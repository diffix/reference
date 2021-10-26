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

type VersionDetails =
  {
    Version: string
    CommitNumber: int
    Branch: string
    Sha: string
    DirtyBuild: bool
  }

type Version = { Name: string; Version: VersionDetails }

let version =
  let semVerString =
    sprintf "%s.%s.%s" ThisAssembly.Git.SemVer.Major ThisAssembly.Git.SemVer.Minor ThisAssembly.Git.SemVer.Patch

  {
    Name = "OpenDiffix Reference implementation"
    Version =
      {
        Version = semVerString
        CommitNumber = (int ThisAssembly.Git.Commits)
        Branch = ThisAssembly.Git.Branch
        Sha = ThisAssembly.Git.Commit
        DirtyBuild = ThisAssembly.Git.IsDirty
      }
  }
