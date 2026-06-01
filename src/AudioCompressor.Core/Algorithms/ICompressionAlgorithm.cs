using AudioCompressor.Core.Models;

namespace AudioCompressor.Core.Algorithms;

public interface ICompressionAlgorithm
{
    string Name { get; }
    byte[] Compress(float[] samples, CompressionConfig config,
        IProgress<double>? progress = null, CancellationToken ct = default);
    float[] Decompress(byte[] compressedData, CompressionConfig config,
        int originalSampleCount, IProgress<double>? progress = null, CancellationToken ct = default);
}
