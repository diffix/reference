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

  static member Default = { StandardDev = 2.; Cutoff = 5. }

type AIDSetting = { Name: string; MinimumAllowed: int }

type TableSettings = { AidColumns: AIDSetting list }

type AnonymizationParams =
  {
    TableSettings: Map<string, TableSettings>
    Seed: int

    // Count params
    OutlierCount: Threshold
    TopCount: Threshold
    Noise: NoiseParam
  }

  static member Default =
    {
      TableSettings = Map.empty
      Seed = 0
      OutlierCount = Threshold.Default
      TopCount = Threshold.Default
      Noise = NoiseParam.Default
    }
