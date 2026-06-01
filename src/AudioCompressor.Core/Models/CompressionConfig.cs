namespace AudioCompressor.Core.Models;

public enum AlgorithmType
{
    NonlinearQuantization,
    DPCM,
    DeltaModulation
}

public enum MuLawType
{
    MuLaw,
    ALaw
}

public class CompressionConfig
{
    public AlgorithmType Algorithm { get; set; } = AlgorithmType.NonlinearQuantization;
    public int TargetBitsPerSample { get; set; } = 8;
    public int QuantizationLevels => 1 << TargetBitsPerSample;
    public int PredictorOrder { get; set; } = 1;
    public double StepSize { get; set; } = 0.02;
    public bool UseAdaptiveDelta { get; set; } = false;
    public MuLawType LawType { get; set; } = MuLawType.MuLaw;
    public double MuLawMu { get; set; } = 255.0;
    public double ALawA { get; set; } = 87.6;
}
