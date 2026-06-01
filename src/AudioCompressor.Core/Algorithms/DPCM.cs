using AudioCompressor.Core.Models;

namespace AudioCompressor.Core.Algorithms;

public class DPCM : ICompressionAlgorithm
{
    public string Name => "DPCM";

    public byte[] Compress(float[] samples, CompressionConfig config,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        int bits = config.TargetBitsPerSample;
        int levels = config.QuantizationLevels;
        int totalSamples = samples.Length;
        double stepSize = 2.0 / levels;

        int firstSampleBits = 16;
        int headerBytes = firstSampleBits / 8;
        int dataBytes = ((totalSamples - 1) * bits + 7) / 8;
        byte[] packed = new byte[headerBytes + dataBytes];

        short firstInt = (short)Math.Clamp(samples[0] * 32768f, -32768, 32767);
        packed[0] = (byte)(firstInt >> 8);
        packed[1] = (byte)(firstInt & 0xFF);

        double predicted = samples[0];
        int dataBitPos = 0;
        int reportInterval = Math.Max(1, totalSamples / 100);

        for (int i = 1; i < totalSamples; i++)
        {
            ct.ThrowIfCancellationRequested();

            double error = samples[i] - predicted;
            int quantized = (int)Math.Round(error / stepSize);
            quantized = Math.Clamp(quantized, -(levels / 2), levels / 2 - 1);
            int unsignedIdx = quantized + (levels / 2);

            int byteIdx = headerBytes + dataBitPos / 8;
            int bitOff = dataBitPos % 8;
            int val = unsignedIdx;
            int remaining = bits;

            while (remaining > 0)
            {
                int bitsInThisByte = Math.Min(remaining, 8 - bitOff);
                int mask = (1 << bitsInThisByte) - 1;
                int shifted = (val >> (remaining - bitsInThisByte)) & mask;
                packed[byteIdx] |= (byte)(shifted << (8 - bitOff - bitsInThisByte));
                bitOff += bitsInThisByte;
                if (bitOff == 8) { bitOff = 0; byteIdx++; }
                remaining -= bitsInThisByte;
            }
            dataBitPos += bits;

            double dequantizedError = (unsignedIdx - levels / 2) * stepSize;
            predicted += dequantizedError;

            if (i % reportInterval == 0)
                progress?.Report((double)i / totalSamples);
        }

        progress?.Report(1.0);
        return packed;
    }

    public float[] Decompress(byte[] compressedData, CompressionConfig config,
        int originalSampleCount, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        int bits = config.TargetBitsPerSample;
        int levels = config.QuantizationLevels;
        double stepSize = 2.0 / levels;
        int firstSampleBits = 16;
        int headerBytes = firstSampleBits / 8;

        float[] result = new float[originalSampleCount];

        short firstInt = (short)((compressedData[0] << 8) | compressedData[1]);
        result[0] = firstInt / 32768f;
        double predicted = result[0];

        int dataBitPos = 0;
        int reportInterval = Math.Max(1, originalSampleCount / 100);

        for (int i = 1; i < originalSampleCount; i++)
        {
            ct.ThrowIfCancellationRequested();

            int byteIdx = headerBytes + dataBitPos / 8;
            int bitOff = dataBitPos % 8;
            int remaining = bits;
            int unsignedIdx = 0;

            while (remaining > 0)
            {
                int bitsInThisByte = Math.Min(remaining, 8 - bitOff);
                int mask = (1 << bitsInThisByte) - 1;
                int shifted = (compressedData[byteIdx] >> (8 - bitOff - bitsInThisByte)) & mask;
                unsignedIdx = (unsignedIdx << bitsInThisByte) | shifted;
                bitOff += bitsInThisByte;
                if (bitOff == 8) { bitOff = 0; byteIdx++; }
                remaining -= bitsInThisByte;
            }
            dataBitPos += bits;

            double dequantizedError = (unsignedIdx - levels / 2) * stepSize;
            predicted += dequantizedError;
            result[i] = (float)Math.Clamp(predicted, -1.0, 1.0);

            if (i % reportInterval == 0)
                progress?.Report((double)i / originalSampleCount);
        }

        progress?.Report(1.0);
        return result;
    }
}
