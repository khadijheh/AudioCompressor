using AudioCompressor.Core.Models;
using System;
using System.Threading;

namespace AudioCompressor.Core.Algorithms;

public class AdaptiveDeltaModulation : ICompressionAlgorithm
{
	public string Name => "Adaptive Delta Modulation (ADM)";

	private const float InitialStep = 0.01f;
	private const float MinStep = 0.0001f;
	private const float MaxStep = 1.0f;

	private const float IncreaseFactor = 1.5f;
	private const float DecreaseFactor = 0.75f;

	public byte[] Compress(
		float[] samples,
		CompressionConfig config,
		IProgress<double>? progress = null,
		CancellationToken ct = default)
	{
		int sampleCount = samples.Length;

		int dataBytes = (sampleCount + 7) / 8;

		byte[] output = new byte[8 + dataBytes];

		float predictor = 0f;
		float step = InitialStep;

		Array.Copy(BitConverter.GetBytes(step), 0, output, 0, 4);
		Array.Copy(BitConverter.GetBytes(predictor), 0, output, 4, 4);

		int byteIndex = 8;
		int bitIndex = 0;

		int previousBit = -1;

		int reportInterval = Math.Max(1, sampleCount / 100);

		for (int i = 0; i < sampleCount; i++)
		{
			ct.ThrowIfCancellationRequested();

			float sample = Math.Clamp(samples[i], -1f, 1f);

			int bit;

			if (sample >= predictor)
			{
				bit = 1;
				predictor += step;
			}
			else
			{
				bit = 0;
				predictor -= step;
			}

			predictor = Math.Clamp(predictor, -1f, 1f);

			if (previousBit != -1)
			{
				if (bit == previousBit)
				{
					step *= IncreaseFactor;
				}
				else
				{
					step *= DecreaseFactor;
				}

				step = Math.Clamp(step, MinStep, MaxStep);
			}

			previousBit = bit;

			output[byteIndex] |= (byte)(bit << (7 - bitIndex));

			bitIndex++;

			if (bitIndex == 8)
			{
				bitIndex = 0;
				byteIndex++;
			}

			if (i % reportInterval == 0)
			{
				progress?.Report((double)i / sampleCount);
			}
		}

		progress?.Report(1.0);

		return output;
	}

	public float[] Decompress(
		byte[] compressedData,
		CompressionConfig config,
		int originalSampleCount,
		IProgress<double>? progress = null,
		CancellationToken ct = default)
	{
		float[] output = new float[originalSampleCount];

		float step = BitConverter.ToSingle(compressedData, 0);
		float predictor = BitConverter.ToSingle(compressedData, 4);

		int byteIndex = 8;
		int bitIndex = 0;

		int previousBit = -1;

		int reportInterval = Math.Max(1, originalSampleCount / 100);

		for (int i = 0; i < originalSampleCount; i++)
		{
			ct.ThrowIfCancellationRequested();

			int bit =
				(compressedData[byteIndex] >> (7 - bitIndex)) & 1;

			if (bit == 1)
			{
				predictor += step;
			}
			else
			{
				predictor -= step;
			}

			predictor = Math.Clamp(predictor, -1f, 1f);

			output[i] = predictor;

			if (previousBit != -1)
			{
				if (bit == previousBit)
				{
					step *= IncreaseFactor;
				}
				else
				{
					step *= DecreaseFactor;
				}

				step = Math.Clamp(step, MinStep, MaxStep);
			}

			previousBit = bit;

			bitIndex++;

			if (bitIndex == 8)
			{
				bitIndex = 0;
				byteIndex++;
			}

			if (i % reportInterval == 0)
			{
				progress?.Report((double)i / originalSampleCount);
			}
		}

		progress?.Report(1.0);

		return output;
	}
}