using AudioCompressor.Core.Algorithms;
using AudioCompressor.Core.Models;

namespace AudioCompressor.Tests;

public class AlgorithmTests
{
    private static float[] GenerateSineWave(int sampleRate, int durationSec, float freq)
    {
        int n = sampleRate * durationSec;
        float[] samples = new float[n];
        for (int i = 0; i < n; i++)
            samples[i] = (float)Math.Sin(2 * Math.PI * freq * i / sampleRate) * 0.8f;
        return samples;
    }

    private static float[] GenerateNoise(int count)
    {
        var rng = new Random(42);
        float[] samples = new float[count];
        for (int i = 0; i < count; i++)
            samples[i] = (float)(rng.NextDouble() * 2.0 - 1.0) * 0.5f;
        return samples;
    }

    private static float[] GenerateSweep(int sampleRate, int durationSec)
    {
        int n = sampleRate * durationSec;
        float[] samples = new float[n];
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / sampleRate;
            double freq = 200.0 + (2000.0 - 200.0) * t / durationSec;
            samples[i] = (float)Math.Sin(2 * Math.PI * freq * t) * 0.7f;
        }
        return samples;
    }

    private static double ComputeSNR(float[] original, float[] reconstructed)
    {
        double signalPower = 0, noisePower = 0;
        int n = Math.Min(original.Length, reconstructed.Length);
        for (int i = 0; i < n; i++)
        {
            signalPower += original[i] * original[i];
            double diff = original[i] - reconstructed[i];
            noisePower += diff * diff;
        }
        if (noisePower < 1e-15) return 100.0;
        return 10.0 * Math.Log10(signalPower / noisePower);
    }

    [Fact]
    public void NonlinearQuant_MuLaw8bit_RoundTrip_SNR_Above30()
    {
        var samples = GenerateSineWave(44100, 1, 440);
        var algo = new NonlinearQuantization();
        var config = new CompressionConfig
        {
            Algorithm = AlgorithmType.NonlinearQuantization,
            TargetBitsPerSample = 8,
            LawType = MuLawType.MuLaw
        };

        var compressed = algo.Compress(samples, config);
        var decompressed = algo.Decompress(compressed, config, samples.Length);

        Assert.Equal(samples.Length, decompressed.Length);
        double snr = ComputeSNR(samples, decompressed);
        Assert.True(snr >= 30, $"SNR={snr:F1}dB too low for 8-bit μ-law");
    }

    [Fact]
    public void NonlinearQuant_ALaw8bit_RoundTrip_SNR_Above28()
    {
        var samples = GenerateSineWave(44100, 1, 440);
        var algo = new NonlinearQuantization();
        var config = new CompressionConfig
        {
            Algorithm = AlgorithmType.NonlinearQuantization,
            TargetBitsPerSample = 8,
            LawType = MuLawType.ALaw
        };

        var compressed = algo.Compress(samples, config);
        var decompressed = algo.Decompress(compressed, config, samples.Length);

        double snr = ComputeSNR(samples, decompressed);
        Assert.True(snr >= 28, $"SNR={snr:F1}dB too low for 8-bit A-law");
    }

    [Fact]
    public void NonlinearQuant_4Bit_RoundTrip_ProducesOutput()
    {
        var samples = GenerateSineWave(44100, 1, 440);
        var algo = new NonlinearQuantization();
        var config = new CompressionConfig
        {
            Algorithm = AlgorithmType.NonlinearQuantization,
            TargetBitsPerSample = 4,
            LawType = MuLawType.MuLaw
        };

        var compressed = algo.Compress(samples, config);
        var decompressed = algo.Decompress(compressed, config, samples.Length);

        Assert.Equal(samples.Length, decompressed.Length);
        int expectedBytes = (samples.Length * config.TargetBitsPerSample + 7) / 8;
        Assert.True(compressed.Length <= expectedBytes,
            $"Compressed size {compressed.Length} should be ≤ {expectedBytes}");
    }

    [Fact]
    public void DPCM_8Bit_RoundTrip_SNR_Above25()
    {
        var samples = GenerateSweep(44100, 1);
        var algo = new DPCM();
        var config = new CompressionConfig
        {
            Algorithm = AlgorithmType.DPCM,
            TargetBitsPerSample = 8
        };

        var compressed = algo.Compress(samples, config);
        var decompressed = algo.Decompress(compressed, config, samples.Length);

        Assert.Equal(samples.Length, decompressed.Length);
        double snr = ComputeSNR(samples, decompressed);
        Assert.True(snr >= 25, $"SNR={snr:F1}dB too low for 8-bit DPCM on sweep");
    }

    [Fact]
    public void DPCM_4Bit_RoundTrip_CompressionWorks()
    {
        var samples = GenerateSweep(44100, 1);
        var algo = new DPCM();
        var config = new CompressionConfig
        {
            Algorithm = AlgorithmType.DPCM,
            TargetBitsPerSample = 4
        };

        var compressed = algo.Compress(samples, config);
        var decompressed = algo.Decompress(compressed, config, samples.Length);

        int expectedCompressedBytes = 2 + ((samples.Length - 1) * config.TargetBitsPerSample + 7) / 8;
        Assert.Equal(samples.Length, decompressed.Length);
        Assert.True(compressed.Length <= expectedCompressedBytes,
            $"DPCM compressed {compressed.Length} > expected {expectedCompressedBytes}");
    }

    [Fact]
    public void DeltaMod_FixedStep_RoundTrip_ProducesOutput()
    {
        var samples = GenerateSineWave(8000, 1, 400);
        var algo = new DeltaModulation();
        var config = new CompressionConfig
        {
            Algorithm = AlgorithmType.DeltaModulation,
            StepSize = 0.02,
            UseAdaptiveDelta = false
        };

        var compressed = algo.Compress(samples, config);
        var decompressed = algo.Decompress(compressed, config, samples.Length);

        Assert.Equal(samples.Length, decompressed.Length);
        int expectedDeltaBytes = 4 + ((samples.Length - 1) + 7) / 8;
        Assert.True(compressed.Length <= expectedDeltaBytes,
            $"DeltaMod compressed {compressed.Length} > expected {expectedDeltaBytes}");
    }

    [Fact]
    public void DeltaMod_Adaptive_RoundTrip_ProducesOutput()
    {
        var samples = GenerateSineWave(8000, 1, 400);
        var algo = new DeltaModulation();
        var config = new CompressionConfig
        {
            Algorithm = AlgorithmType.DeltaModulation,
            StepSize = 0.01,
            UseAdaptiveDelta = true
        };

        var compressed = algo.Compress(samples, config);
        var decompressed = algo.Decompress(compressed, config, samples.Length);

        Assert.Equal(samples.Length, decompressed.Length);
        double snr = ComputeSNR(samples, decompressed);
        Assert.True(snr > 0, $"SNR={snr:F1}dB should be >0 (signal correlated)");
    }

    [Fact]
    public void AllAlgorithms_NoiseInput_NoCrash()
    {
        var samples = GenerateNoise(10000);
        var configs = new[]
        {
            new CompressionConfig { Algorithm = AlgorithmType.NonlinearQuantization, TargetBitsPerSample = 8 },
            new CompressionConfig { Algorithm = AlgorithmType.DPCM, TargetBitsPerSample = 8 },
            new CompressionConfig { Algorithm = AlgorithmType.DeltaModulation, StepSize = 0.02 }
        };
        ICompressionAlgorithm[] algos = [new NonlinearQuantization(), new DPCM(), new DeltaModulation()];

        for (int i = 0; i < algos.Length; i++)
        {
            var compressed = algos[i].Compress(samples, configs[i]);
            var decompressed = algos[i].Decompress(compressed, configs[i], samples.Length);
            Assert.Equal(samples.Length, decompressed.Length);
        }
    }

    [Fact]
    public void NonlinearQuant_Identity_AtMaxBits_Unaltered()
    {
        var samples = GenerateSineWave(44100, 1, 1000);
        var algo = new NonlinearQuantization();

        var config16 = new CompressionConfig
        {
            TargetBitsPerSample = 16,
            LawType = MuLawType.MuLaw
        };
        var compressed = algo.Compress(samples, config16);
        var decompressed = algo.Decompress(compressed, config16, samples.Length);

        double snr = ComputeSNR(samples, decompressed);
        Assert.True(snr >= 85, $"16-bit μ-law should be near lossless, SNR={snr:F1}dB");
    }

    [Fact]
    public void DPCM_ConstantSignal_PerfectReconstruction()
    {
        float[] samples = Enumerable.Repeat(0.5f, 1000).ToArray();
        var algo = new DPCM();
        var config = new CompressionConfig
        {
            Algorithm = AlgorithmType.DPCM,
            TargetBitsPerSample = 4
        };

        var compressed = algo.Compress(samples, config);
        var decompressed = algo.Decompress(compressed, config, samples.Length);

        double snr = ComputeSNR(samples, decompressed);
        Assert.True(snr >= 50, $"Constant DPCM should be near perfect, SNR={snr:F1}dB");
    }
    [Fact]
    public void TransformCodingDCT_8Bit_RoundTrip_CompressionWorks()
    {
        // توليد موجة جيبية بسيطة بتردد 440Hz
        var samples = GenerateSineWave(44100, 1, 440);
        var algo = new TransformCodingDCT();

        var config = new CompressionConfig
        {
            // تأكدي من إضافة هذا الخيار إلى AlgorithmType Enum
            Algorithm = AlgorithmType.TransformCodingDCT,
            TargetBitsPerSample = 8
        };

        var compressed = algo.Compress(samples, config);
        var decompressed = algo.Decompress(compressed, config, samples.Length);

        // حساب الطول المبطن (Padded) لأن الخوارزمية تقسم البيانات لكتل بحجم 64
        int blockSize = 64;
        int paddedLength = ((samples.Length + blockSize - 1) / blockSize) * blockSize;

        // الحجم المتوقع: 4 بايت للترويسة (Max Value) + حجم البيانات المضغوطة
        int expectedBytes = 4 + (paddedLength * config.TargetBitsPerSample + 7) / 8;

        Assert.Equal(samples.Length, decompressed.Length);
        Assert.True(compressed.Length <= expectedBytes,
            $"حجم الملف المضغوط {compressed.Length} أكبر من المتوقع {expectedBytes}");

        double snr = ComputeSNR(samples, decompressed);

        // نسبة الإشارة إلى الضوضاء يجب أن تكون جيدة لإشارة بسيطة
        Assert.True(snr >= 25, $"SNR={snr:F1}dB منخفض جداً لخوارزمية DCT");
    }
    [Fact]
    public void ADPCM_RoundTrip_CompressionWorks()
    {
        var samples = GenerateSweep(44100, 1);
        var algo = new ADPCM();
        var config = new CompressionConfig { Algorithm = AlgorithmType.ADPCM }; // تأكدي من إضافة ADPCM لـ Enum

        var compressed = algo.Compress(samples, config);
        var decompressed = algo.Decompress(compressed, config, samples.Length);

        // التحقق من أن حجم الملف المضغوط هو تقريباً الربع (4 بت للعينة) + 4 بايت للترويسة
        int expectedBytes = 4 + (samples.Length + 1) / 2;

        Assert.Equal(samples.Length, decompressed.Length);
        Assert.Equal(expectedBytes, compressed.Length);

        double snr = ComputeSNR(samples, decompressed);
        Assert.True(snr >= 20, $"SNR={snr:F1}dB too low for ADPCM on sweep");
    }
    [Fact]
    public void ADM_RoundTrip_CompressionWorks()
    {
        var samples = GenerateSineWave(8000, 1, 400);

        var algo = new AdaptiveDeltaModulation();

        var config = new CompressionConfig
        {
            Algorithm = AlgorithmType.AdaptiveDeltaModulation
        };

        var compressed = algo.Compress(samples, config);
        var decompressed = algo.Decompress(compressed, config, samples.Length);

        Assert.Equal(samples.Length, decompressed.Length);

        // 8 بايت Header + 1 bit لكل sample
        int expectedBytes = 8 + (samples.Length + 7) / 8;

        Assert.Equal(expectedBytes, compressed.Length);

        double snr = ComputeSNR(samples, decompressed);

        Assert.True(snr > 0,
            $"ADM reconstruction failed, SNR={snr:F2}dB");
    }
    [Fact]
    public void ADM_NoiseInput_NoCrash()
    {
        var samples = GenerateNoise(10000);

        var algo = new AdaptiveDeltaModulation();

        var config = new CompressionConfig
        {
            Algorithm = AlgorithmType.AdaptiveDeltaModulation
        };

        var compressed = algo.Compress(samples, config);
        var decompressed = algo.Decompress(compressed, config, samples.Length);

        Assert.Equal(samples.Length, decompressed.Length);
    }
}
