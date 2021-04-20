namespace OpenDiffix.Core.AnonymizerTypes

type Threshold =
  {
    Lower: int
    Upper: int
  }

  static member Default = { Lower = 2; Upper = 5 }

type NoiseParam =
  {
    StandardDev: float
    Cutoff: float
  }

  static member Default = { StandardDev = 2.; Cutoff = 3. }

type TableSettings = { AidColumns: string list }

type AnonymizationParams =
  {
    TableSettings: Map<string, TableSettings>
    Seed: int
    MinimumAllowedAids: int

    // Count params
    OutlierCount: Threshold
    TopCount: Threshold
    Noise: NoiseParam
  }

  static member Default =
    {
      TableSettings = Map.empty
      Seed = 0
      MinimumAllowedAids = 2
      OutlierCount = Threshold.Default
      TopCount = Threshold.Default
      Noise = NoiseParam.Default
    }
