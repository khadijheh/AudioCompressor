using AudioCompressor.Core.Models;

namespace AudioCompressor.Core.Services;

public interface IWavService
{
    WavFileInfo ReadFileInfo(string filePath);
    float[] ReadSamples(string filePath);
    void WriteFile(string outputPath, float[] samples, WavFileInfo originalInfo);
    void ResampleFile(string inputPath, string outputPath, int targetSampleRate);
}
