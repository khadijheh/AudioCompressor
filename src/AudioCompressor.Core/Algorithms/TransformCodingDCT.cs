using AudioCompressor.Core.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AudioCompressor.Core.Algorithms;

public class TransformCodingDCT : ICompressionAlgorithm
{
    public string Name => "DCT Transform Coding";

    private const int BlockSize = 512;
    private const int KeptCoefficients = 64;

    private static readonly double[] Window = new double[BlockSize];
    private static readonly double[,] CosTable = new double[KeptCoefficients, BlockSize];

    static TransformCodingDCT()
    {
        for (int i = 0; i < BlockSize; i++)
        {
            Window[i] = 1.0;
        }

        for (int k = 0; k < KeptCoefficients; k++)
        {
            for (int n = 0; n < BlockSize; n++)
            {
                CosTable[k, n] = Math.Cos(Math.PI * (n + 0.5) * k / BlockSize);
            }
        }
    }

    public byte[] Compress(float[] samples, CompressionConfig config, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        int bits = config.TargetBitsPerSample;
        int levels = config.QuantizationLevels;

        // ÏÚã ÇáÓÊíÑíæ: ÝÕá ÇáÚíäÇÊ Åáì ÞäÇÊíä ãÓÊÞáÊíä áÍãÇíÉ ÇáÅÔÇÑÉ ãä ÇáÊÏãíÑ
        int numChannels = 2;
        int frames = samples.Length / numChannels;

        float[][] channelSamples = new float[numChannels][];
        for (int c = 0; c < numChannels; c++) channelSamples[c] = new float[frames];

        for (int i = 0; i < frames; i++)
        {
            channelSamples[0][i] = samples[i * numChannels];
            channelSamples[1][i] = samples[i * numChannels + 1];
        }

        int paddedLength = ((frames + BlockSize - 1) / BlockSize) * BlockSize;
        int blockCount = paddedLength / BlockSize;

        // ãÕÝæÝÉ ÇáãÚÇãáÇÊ ááÞäÇÊíä ãÚÇð
        double[] coeffs = new double[numChannels * blockCount * KeptCoefficients];
        object maxLock = new();
        double globalMax = 0.0;

        // ÊÔÛíá ÇáÎæÇÑÒãíÉ ÇáÃÕáíÉ ÊãÇãÇð áßá ÞäÇÉ ÈÔßá ãÓÊÞá
        for (int c = 0; c < numChannels; c++)
        {
            float[] padded = new float[paddedLength];
            Array.Copy(channelSamples[c], padded, channelSamples[c].Length);

            int channelOffset = c * blockCount * KeptCoefficients;

            Parallel.For(0, blockCount, block =>
            {
                int offset = block * BlockSize;
                double localMax = 0.0;

                for (int k = 0; k < KeptCoefficients; k++)
                {
                    double sum = 0.0;
                    double ck = (k == 0) ? Math.Sqrt(1.0 / BlockSize) : Math.Sqrt(2.0 / BlockSize);

                    for (int n = 0; n < BlockSize; n++)
                    {
                        sum += padded[offset + n] * Window[n] * CosTable[k, n];
                    }

                    double value = ck * sum;
                    coeffs[channelOffset + block * KeptCoefficients + k] = value;

                    double abs = Math.Abs(value);
                    if (abs > localMax) localMax = abs;
                }

                lock (maxLock)
                {
                    if (localMax > globalMax) globalMax = localMax;
                }
            });
        }

        if (globalMax < 1e-12) globalMax = 1.0;

        int coefficientCount = coeffs.Length;
        int headerBytes = 4;
        int dataBytes = (coefficientCount * bits + 7) / 8;
        byte[] output = new byte[headerBytes + dataBytes];

        Array.Copy(BitConverter.GetBytes((float)globalMax), 0, output, 0, 4);
        int bitPosition = 0;

try 
{
    for (int i = 0; i < coefficientCount; i++)
    {
        ct.ThrowIfCancellationRequested(); // السطر 100 الحالي

        double normalized = Math.Clamp(coeffs[i] / globalMax, -1.0, 1.0);
        double sign = Math.Sign(normalized);
        double compand = sign * Math.Pow(Math.Abs(normalized), 0.6);

        int q = (int)Math.Round((compand + 1.0) * 0.5 * (levels - 1));
        q = Math.Clamp(q, 0, levels - 1);

        WriteBits(output, headerBytes, ref bitPosition, q, bits);

        if ((i & 2047) == 0) progress?.Report((double)i / coefficientCount);
    }
}
catch (OperationCanceledException)
{
    // في حال تم طلب الإلغاء، سيرجع مصفوفة فارغة أو غير مكتملة بأمان دون كراش
    System.Diagnostics.Debug.WriteLine("تم إلغاء العملية بأمان بطلب من المستخدم.");
    return Array.Empty<byte>(); 
}

progress?.Report(1.0);
return output;
    }

    public float[] Decompress(byte[] compressedData, CompressionConfig config, int originalSampleCount, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        int bits = config.TargetBitsPerSample;
        int levels = config.QuantizationLevels;
        float globalMax = BitConverter.ToSingle(compressedData, 0);

        int numChannels = 2;
        int originalFrames = originalSampleCount / numChannels;

        int paddedLength = ((originalFrames + BlockSize - 1) / BlockSize) * BlockSize;
        int blockCount = paddedLength / BlockSize;

        double[] coeffs = new double[numChannels * blockCount * KeptCoefficients];
        int bitPosition = 0;

        for (int i = 0; i < coeffs.Length; i++)
        {
            int q = ReadBits(compressedData, 4, ref bitPosition, bits);
            double compand = (double)q / (levels - 1);
            compand = compand * 2.0 - 1.0;

            double sign = Math.Sign(compand);
            double normalized = sign * Math.Pow(Math.Abs(compand), 1.0 / 0.6);
            coeffs[i] = normalized * globalMax;
        }

        float[][] channelOutputs = new float[numChannels][];
        for (int c = 0; c < numChannels; c++) channelOutputs[c] = new float[originalFrames];

        for (int c = 0; c < numChannels; c++)
        {
            int channelOffset = c * blockCount * KeptCoefficients;
            float[] channelOut = channelOutputs[c];

            Parallel.For(0, blockCount, block =>
            {
                int offset = block * BlockSize;
                int coeffOffset = channelOffset + block * KeptCoefficients;

                for (int n = 0; n < BlockSize; n++)
                {
                    double sum = 0.0;
                    for (int k = 0; k < KeptCoefficients; k++)
                    {
                        double ck = (k == 0) ? Math.Sqrt(1.0 / BlockSize) : Math.Sqrt(2.0 / BlockSize);
                        sum += ck * coeffs[coeffOffset + k] * CosTable[k, n];
                    }

                    int index = offset + n;
                    if (index < originalFrames)
                    {
                        channelOut[index] = (float)Math.Clamp(sum, -1.0, 1.0);
                    }
                }
            });
        }

        // ÅÚÇÏÉ ÏãÌ ÇáÞäÇÊíä (Interleaving) áÊÌåíÒ ÇáãáÝ ÇáäåÇÆí ááÊÔÛíá
        float[] output = new float[originalSampleCount];
        for (int i = 0; i < originalFrames; i++)
        {
            output[i * numChannels] = channelOutputs[0][i];
            output[i * numChannels + 1] = channelOutputs[1][i];
        }

        progress?.Report(1.0);
        return output;
    }

    private static void WriteBits(byte[] buffer, int startOffset, ref int bitPos, int value, int bits)
    {
        while (bits > 0)
        {
            int byteIndex = startOffset + bitPos / 8;
            int bitOffset = bitPos % 8;
            int count = Math.Min(bits, 8 - bitOffset);
            int mask = (1 << count) - 1;
            int part = (value >> (bits - count)) & mask;

            buffer[byteIndex] |= (byte)(part << (8 - bitOffset - count));
            bits -= count;
            bitPos += count;
        }
    }

    private static int ReadBits(byte[] buffer, int startOffset, ref int bitPos, int bits)
    {
        int result = 0;
        while (bits > 0)
        {
            int byteIndex = startOffset + bitPos / 8;
            int bitOffset = bitPos % 8;
            int count = Math.Min(bits, 8 - bitOffset);
            int mask = (1 << count) - 1;
            int part = (buffer[byteIndex] >> (8 - bitOffset - count)) & mask;

            result = (result << count) | part;
            bits -= count;
            bitPos += count;
        }
        return result;
    }
}