namespace OpenDiffix.Core.AnonymizerTypes

type LowCountParams =
  {
    Lower: float
    Mean: float
    StandardDev: float
  }

  static member Default = { Lower = 2.; Mean = 4.; StandardDev = 1.8 }

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

type TableSettings = { AidColumns: string list }

type AnonymizationParams =
  {
    TableSettings: Map<string, TableSettings>
    Seed: int
    LowCountParams: LowCountParams

    // Count params
    OutlierCount: Threshold
    TopCount: Threshold
    Noise: NoiseParam
  }

  static member Default =
    {
      TableSettings = Map.empty
      Seed = 0
      LowCountParams = LowCountParams.Default
      OutlierCount = Threshold.Default
      TopCount = Threshold.Default
      Noise = NoiseParam.Default
    }
