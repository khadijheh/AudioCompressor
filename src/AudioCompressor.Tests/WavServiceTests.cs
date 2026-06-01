using System.Text;
using AudioCompressor.Core.Models;
using AudioCompressor.Core.Services;

namespace AudioCompressor.Tests;

public class WavServiceTests
{
    private static string CreateTestWav(short channels, int sampleRate, short bitsPerSample, int durationMillis)
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.wav");
        var numSamples = sampleRate * channels * durationMillis / 1000;
        var blockAlign = (short)(channels * bitsPerSample / 8);
        var byteRate = sampleRate * blockAlign;
        var dataSize = numSamples * (bitsPerSample / 8);
        var fileSize = 36 + dataSize;

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(fileSize);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);

        var rng = new Random(42);
        for (int i = 0; i < numSamples; i++)
        {
            if (bitsPerSample == 16)
                writer.Write((short)(rng.Next(-32768, 32767)));
            else if (bitsPerSample == 8)
                writer.Write((byte)rng.Next(0, 255));
        }

        return path;
    }

    [Fact]
    public void ReadFileInfo_ReturnsCorrectMetadata()
    {
        var path = CreateTestWav(channels: 2, sampleRate: 44100, bitsPerSample: 16, durationMillis: 1000);

        var service = new WavService();
        var info = service.ReadFileInfo(path);

        Assert.Equal(2, info.Channels);
        Assert.Equal(44100, info.SampleRate);
        Assert.Equal(16, info.BitsPerSample);
        Assert.Equal(1411200, info.BitRate);
        Assert.Equal("PCM", info.Encoding);
        Assert.True(info.Duration.TotalMilliseconds >= 990 && info.Duration.TotalMilliseconds <= 1010);
        Assert.True(info.FileSize > 0);
        Assert.True(info.DataSize > 0);

        File.Delete(path);
    }

    [Fact]
    public void ReadFileInfo_ThrowsOnNonPcmFormat()
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_nonpcm_{Guid.NewGuid()}.wav");
        {
            using var stream = File.Create(path);
            using var writer = new BinaryWriter(stream);
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)2);
            writer.Write((short)1);
            writer.Write(44100);
            writer.Write(88200);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(0);
        }

        var service = new WavService();
        Assert.Throws<InvalidDataException>(() => service.ReadFileInfo(path));

        File.Delete(path);
    }

    [Fact]
    public void ReadFileInfo_ThrowsOnInvalidRiff()
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_invalid_{Guid.NewGuid()}.wav");
        File.WriteAllBytes(path, [0, 0, 0, 0]);

        var service = new WavService();
        Assert.Throws<InvalidDataException>(() => service.ReadFileInfo(path));

        File.Delete(path);
    }

    [Fact]
    public void ReadSamples_ReturnsCorrectCount()
    {
        var path = CreateTestWav(channels: 1, sampleRate: 8000, bitsPerSample: 16, durationMillis: 500);

        var service = new WavService();
        var samples = service.ReadSamples(path);

        var expectedCount = 8000 * 1 * 500 / 1000;
        Assert.Equal(expectedCount, samples.Length);
        Assert.All(samples, s => Assert.InRange(s, -1.0f, 1.0f));

        File.Delete(path);
    }

    [Fact]
    public void WriteFile_RoundTrip_ProducesIdenticalSamples()
    {
        var originalPath = CreateTestWav(channels: 1, sampleRate: 16000, bitsPerSample: 16, durationMillis: 200);
        var outputPath = Path.Combine(Path.GetTempPath(), $"test_rt_{Guid.NewGuid()}.wav");

        var service = new WavService();
        var originalInfo = service.ReadFileInfo(originalPath);
        var originalSamples = service.ReadSamples(originalPath);

        service.WriteFile(outputPath, originalSamples, originalInfo);

        var roundTripInfo = service.ReadFileInfo(outputPath);
        var roundTripSamples = service.ReadSamples(outputPath);

        Assert.Equal(originalInfo.Channels, roundTripInfo.Channels);
        Assert.Equal(originalInfo.SampleRate, roundTripInfo.SampleRate);
        Assert.Equal(originalInfo.BitsPerSample, roundTripInfo.BitsPerSample);
        Assert.Equal(originalSamples.Length, roundTripSamples.Length);

        for (int i = 0; i < originalSamples.Length; i++)
            Assert.Equal(originalSamples[i], roundTripSamples[i], 3);

        File.Delete(originalPath);
        File.Delete(outputPath);
    }

    [Fact]
    public void ReadFileInfo_WithExtraChunks_StillParsesCorrectly()
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_extra_{Guid.NewGuid()}.wav");
        {
            using var stream = File.Create(path);
            using var writer = new BinaryWriter(stream);
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + 12 + 100);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)2);
            writer.Write(44100);
            writer.Write(176400);
            writer.Write((short)4);
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("LIST"));
            writer.Write(100);
            writer.Write(new byte[100]);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(0);
        }

        var service = new WavService();
        var info = service.ReadFileInfo(path);

        Assert.Equal(2, info.Channels);
        Assert.Equal(44100, info.SampleRate);

        File.Delete(path);
    }
}
