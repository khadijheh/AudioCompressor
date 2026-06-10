using AudioCompressor.Core.Models;
using System;
using System.Threading;

namespace AudioCompressor.Core.Algorithms;

public class ADPCM : ICompressionAlgorithm
{
    public string Name => "ADPCM (Adaptive)";


    private static readonly int[] IndexTable = {
        -1, -1, -1, -1, 2, 4, 6, 8,
        -1, -1, -1, -1, 2, 4, 6, 8
    };

    private static readonly int[] StepSizeTable = {
        7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45,
        50, 55, 60, 66, 73, 80, 88, 97, 107, 118, 130, 143, 157, 173, 190, 209, 230,
        253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658, 724, 796, 876, 963,
        1060, 1166, 1282, 1411, 1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024, 3327,
        3660, 4026, 4428, 4871, 5358, 5894, 6484, 7132, 7845, 8630, 9493, 10442,
        11487, 12635, 13899, 15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767
    };

    public byte[] Compress(float[] samples, CompressionConfig config,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        int totalSamples = samples.Length;

        int dataBytes = (totalSamples + 1) / 2;
        byte[] packed = new byte[4 + dataBytes];

        int predicted = 0;
        int index = 0;


        packed[0] = (byte)(predicted & 0xFF);
        packed[1] = (byte)((predicted >> 8) & 0xFF);
        packed[2] = (byte)index;
        packed[3] = 0;

        int byteIdx = 4;
        bool highNibble = false;
        int reportInterval = Math.Max(1, totalSamples / 100);

        for (int i = 0; i < totalSamples; i++)
        {
            ct.ThrowIfCancellationRequested();


            int sample = (int)(Math.Clamp(samples[i], -1.0f, 1.0f) * 32767.0f);

            int step = StepSizeTable[index];
            int diff = sample - predicted;
            int sign = (diff < 0) ? 8 : 0;
            if (sign != 0) diff = -diff;

            int delta = 0;
            int vpdiff = (step >> 3);

            if (diff >= step)
            {
                delta |= 4;
                diff -= step;
                vpdiff += step;
            }
            step >>= 1;
            if (diff >= step)
            {
                delta |= 2;
                diff -= step;
                vpdiff += step;
            }
            step >>= 1;
            if (diff >= step)
            {
                delta |= 1;
                vpdiff += step;
            }

            if (sign != 0)
                predicted -= vpdiff;
            else
                predicted += vpdiff;

            predicted = Math.Clamp(predicted, -32768, 32767);
            delta |= sign;
            index += IndexTable[delta];
            index = Math.Clamp(index, 0, 88);


            if (highNibble)
            {
                packed[byteIdx] |= (byte)((delta << 4) & 0xF0);
                byteIdx++;
            }
            else
            {
                packed[byteIdx] = (byte)(delta & 0x0F);
            }
            highNibble = !highNibble;

            if (i % reportInterval == 0)
                progress?.Report((double)i / totalSamples);
        }

        progress?.Report(1.0);
        return packed;
    }

    public float[] Decompress(byte[] compressedData, CompressionConfig config,
        int originalSampleCount, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        float[] result = new float[originalSampleCount];


        int predicted = (short)(compressedData[0] | (compressedData[1] << 8));
        int index = compressedData[2];

        int byteIdx = 4;
        bool highNibble = false;
        int reportInterval = Math.Max(1, originalSampleCount / 100);

        for (int i = 0; i < originalSampleCount; i++)
        {
            ct.ThrowIfCancellationRequested();

            int delta;
            if (highNibble)
            {
                delta = (compressedData[byteIdx] >> 4) & 0x0F;
                byteIdx++;
            }
            else
            {
                delta = compressedData[byteIdx] & 0x0F;
            }
            highNibble = !highNibble;

            int step = StepSizeTable[index];
            int vpdiff = step >> 3;

            if ((delta & 4) != 0) vpdiff += step;
            if ((delta & 2) != 0) vpdiff += (step >> 1);
            if ((delta & 1) != 0) vpdiff += (step >> 2);

            if ((delta & 8) != 0)
                predicted -= vpdiff;
            else
                predicted += vpdiff;

            predicted = Math.Clamp(predicted, -32768, 32767);
            index += IndexTable[delta];
            index = Math.Clamp(index, 0, 88);


            result[i] = predicted / 32768.0f;

            if (i % reportInterval == 0)
                progress?.Report((double)i / originalSampleCount);
        }

        progress?.Report(1.0);
        return result;
    }
}