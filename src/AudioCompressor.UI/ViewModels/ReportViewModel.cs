using AudioCompressor.Core.Models;

namespace AudioCompressor.UI.ViewModels;

public class ReportViewModel
{
    public CompressionResult Result { get; }

    public ReportViewModel(CompressionResult result)
    {
        Result = result;
    }

    public string FileName => Result.FileName;
    public string OriginalSize => FormatBytes(Result.OriginalSize);
    public string CompressedSize => FormatBytes(Result.CompressedDataSize);
    public string DecompressedWavSize => FormatBytes(Result.DecompressedWavSize);
    public string CompressionRatio => $"{Result.CompressionRatio:F3}x";
    public string SavingsPercent => $"{Result.SavingsPercent:F1}%";
    public string DataRate => $"{Result.DataRate:F3} of original";
    public string Elapsed => Result.Elapsed.TotalSeconds switch
    {
        < 1.0 => $"{Result.Elapsed.TotalMilliseconds:F0} ms",
        < 60.0 => $"{Result.Elapsed.TotalSeconds:F3} seconds",
        _ => $"{Result.Elapsed.TotalMinutes:F1} min {Result.Elapsed.Seconds} sec"
    };
    public string Algorithm => Result.AlgorithmName;
    public string Settings => BuildSettings();
    public string OutputPath => Result.OutputPath;

    private string BuildSettings()
    {
        var c = Result.Config;
        return c.Algorithm switch
        {
            AlgorithmType.NonlinearQuantization =>
                $"Bits: {c.TargetBitsPerSample}, Law: {c.LawType}",
            AlgorithmType.DPCM =>
                $"Bits: {c.TargetBitsPerSample}, Order: {c.PredictorOrder}",
            AlgorithmType.DeltaModulation =>
                $"Step: {c.StepSize:F4}, Adaptive: {(c.UseAdaptiveDelta ? "Yes" : "No")}",
            _ => ""
        };
    }

    private static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} bytes",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            _ => $"{bytes / (1024.0 * 1024.0):F2} MB"
        };
    }
}
