using AudioCompressor.Core.Models;

namespace AudioCompressor.Core.Algorithms;

public class DeltaModulation : ICompressionAlgorithm
{
    public string Name => "Delta Modulation";

    public byte[] Compress(float[] samples, CompressionConfig config,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        int totalSamples = samples.Length;
        int headerBytes = 4;
        int dataBytes = (totalSamples - 1 + 7) / 8;
        byte[] packed = new byte[headerBytes + dataBytes];

        int firstInt = BitConverter.SingleToInt32Bits(samples[0]);
        packed[0] = (byte)(firstInt >> 24);
        packed[1] = (byte)(firstInt >> 16);
        packed[2] = (byte)(firstInt >> 8);
        packed[3] = (byte)firstInt;

        double reconstructed = samples[0];
        double stepSize = config.StepSize;
        int[] recentBits = new int[3];
        int recentCount = 0;
        int reportInterval = Math.Max(1, totalSamples / 100);

        for (int i = 1; i < totalSamples; i++)
        {
            ct.ThrowIfCancellationRequested();

            double diff = samples[i] - reconstructed;
            int bit = diff >= 0 ? 1 : 0;

            int bitPos = i - 1;
            int byteIdx = headerBytes + bitPos / 8;
            int bitOff = bitPos % 8;

            if (bit == 1)
                packed[byteIdx] |= (byte)(1 << (7 - bitOff));

            if (config.UseAdaptiveDelta)
            {
                recentBits[recentCount % 3] = bit;
                recentCount++;
                if (recentCount >= 3)
                {
                    if ((recentBits[0] == recentBits[1]) && (recentBits[1] == recentBits[2]))
                        stepSize = Math.Min(stepSize * 1.5, 0.5);
                    else if ((recentBits[0] != recentBits[1]) && (recentBits[1] != recentBits[2]))
                        stepSize = Math.Max(stepSize * 0.66, 0.0005);
                }
            }

            reconstructed += (bit == 1 ? stepSize : -stepSize);
            reconstructed = Math.Clamp(reconstructed, -1.0, 1.0);

            if (i % reportInterval == 0)
                progress?.Report((double)i / totalSamples);
        }

        progress?.Report(1.0);
        return packed;
    }

    public float[] Decompress(byte[] compressedData, CompressionConfig config,
        int originalSampleCount, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        int headerBytes = 4;
        float[] result = new float[originalSampleCount];

        int firstInt = (compressedData[0] << 24) | (compressedData[1] << 16)
                     | (compressedData[2] << 8) | compressedData[3];
        result[0] = BitConverter.Int32BitsToSingle(firstInt);
        double reconstructed = result[0];
        double stepSize = config.StepSize;
        int[] recentBits = new int[3];
        int recentCount = 0;
        int reportInterval = Math.Max(1, originalSampleCount / 100);

        for (int i = 1; i < originalSampleCount; i++)
        {
            //ct.ThrowIfCancellationRequested();

            int bitPos = i - 1;
            int byteIdx = headerBytes + bitPos / 8;
            int bitOff = bitPos % 8;
            int bit = (compressedData[byteIdx] >> (7 - bitOff)) & 1;

            if (config.UseAdaptiveDelta)
            {
                recentBits[recentCount % 3] = bit;
                recentCount++;
                if (recentCount >= 3)
                {
                    if ((recentBits[0] == recentBits[1]) && (recentBits[1] == recentBits[2]))
                        stepSize = Math.Min(stepSize * 1.5, 0.5);
                    else if ((recentBits[0] != recentBits[1]) && (recentBits[1] != recentBits[2]))
                        stepSize = Math.Max(stepSize * 0.66, 0.0005);
                }
            }

            reconstructed += (bit == 1 ? stepSize : -stepSize);
            reconstructed = Math.Clamp(reconstructed, -1.0, 1.0);
            result[i] = (float)reconstructed;

            if (i % reportInterval == 0)
                progress?.Report((double)i / originalSampleCount);
        }

        progress?.Report(1.0);
        return result;
    }
}
