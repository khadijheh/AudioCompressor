using AudioCompressor.Core.Models;

namespace AudioCompressor.Core.Algorithms;

public class NonlinearQuantization : ICompressionAlgorithm
{
    public string Name => "Nonlinear Quantization";

    public byte[] Compress(float[] samples, CompressionConfig config,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        int levels = config.QuantizationLevels;
        int bits = config.TargetBitsPerSample;
        int totalSamples = samples.Length;
        byte[] packed = new byte[(totalSamples * bits + 7) / 8];
        int reportInterval = Math.Max(1, totalSamples / 100);

        for (int i = 0; i < totalSamples; i++)
        {
            //ct.ThrowIfCancellationRequested();

            double companded = Compand(samples[i], config);
            int quantized = (int)Math.Round((companded + 1.0) / 2.0 * (levels - 1));
            quantized = Math.Clamp(quantized, 0, levels - 1);

            int bitPos = i * bits;
            int byteIdx = bitPos / 8;
            int bitOff = bitPos % 8;
            int remaining = bits;
            int val = quantized;

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

            if (i % reportInterval == 0)
                progress?.Report((double)i / totalSamples);
        }

        progress?.Report(1.0);
        return packed;
    }

    public float[] Decompress(byte[] compressedData, CompressionConfig config,
        int originalSampleCount, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        int levels = config.QuantizationLevels;
        int bits = config.TargetBitsPerSample;
        float[] result = new float[originalSampleCount];
        int reportInterval = Math.Max(1, originalSampleCount / 100);

        for (int i = 0; i < originalSampleCount; i++)
        {
            //ct.ThrowIfCancellationRequested();

            int bitPos = i * bits;
            int byteIdx = bitPos / 8;
            int bitOff = bitPos % 8;
            int remaining = bits;
            int quantized = 0;

            while (remaining > 0)
            {
                int bitsInThisByte = Math.Min(remaining, 8 - bitOff);
                int mask = (1 << bitsInThisByte) - 1;
                int shifted = (compressedData[byteIdx] >> (8 - bitOff - bitsInThisByte)) & mask;
                quantized = (quantized << bitsInThisByte) | shifted;
                bitOff += bitsInThisByte;
                if (bitOff == 8) { bitOff = 0; byteIdx++; }
                remaining -= bitsInThisByte;
            }

            double dequantized = (quantized / (double)(levels - 1)) * 2.0 - 1.0;
            result[i] = (float)Expand(dequantized, config);

            if (i % reportInterval == 0)
                progress?.Report((double)i / originalSampleCount);
        }

        progress?.Report(1.0);
        return result;
    }

    private double Compand(double sample, CompressionConfig config)
    {
        double abs = Math.Abs(sample);
        double sign = Math.Sign(sample);

        if (config.LawType == MuLawType.MuLaw)
        {
            double mu = config.MuLawMu;
            return sign * Math.Log(1.0 + mu * abs) / Math.Log(1.0 + mu);
        }
        else
        {
            double A = config.ALawA;
            if (abs < 1.0 / A)
                return sign * A * abs / (1.0 + Math.Log(A));
            else
                return sign * (1.0 + Math.Log(A * abs)) / (1.0 + Math.Log(A));
        }
    }

    private double Expand(double compressed, CompressionConfig config)
    {
        double abs = Math.Abs(compressed);
        double sign = Math.Sign(compressed);

        if (config.LawType == MuLawType.MuLaw)
        {
            double mu = config.MuLawMu;
            return sign * (Math.Pow(1.0 + mu, abs) - 1.0) / mu;
        }
        else
        {
            double A = config.ALawA;
            double limit = 1.0 / (1.0 + Math.Log(A));
            if (abs < limit)
                return sign * abs * (1.0 + Math.Log(A)) / A;
            else
                return sign * Math.Exp(abs * (1.0 + Math.Log(A)) - 1.0) / A;
        }
    }
}
