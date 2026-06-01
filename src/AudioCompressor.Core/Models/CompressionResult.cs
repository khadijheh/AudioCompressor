namespace AudioCompressor.Core.Models;

public class CompressionResult
{
    public string FileName { get; set; } = string.Empty;
    public long OriginalSize { get; set; }
    public long CompressedDataSize { get; set; }
    public long DecompressedWavSize { get; set; }
    public double CompressionRatio => OriginalSize > 0
        ? (double)OriginalSize / CompressedDataSize
        : 0;
    public double SavingsPercent => OriginalSize > 0
        ? (1.0 - (double)CompressedDataSize / OriginalSize) * 100
        : 0;
    public double DataRate => OriginalSize > 0
        ? (double)CompressedDataSize / OriginalSize
        : 0;
    public TimeSpan Elapsed { get; set; }
    public string AlgorithmName { get; set; } = string.Empty;
    public CompressionConfig Config { get; set; } = new();
    public string OutputPath { get; set; } = string.Empty;
    public string OriginalPath { get; set; } = string.Empty;
    public byte[]? CompressedBytes { get; set; }
    public string? CompressedFilePath { get; set; }
    public int OriginalSampleCount { get; set; }
}
