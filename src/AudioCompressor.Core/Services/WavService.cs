using System.Buffers.Binary;
using System.Runtime.InteropServices;
using AudioCompressor.Core.Models;
using NAudio.Wave;

namespace AudioCompressor.Core.Services;

public class WavService : IWavService
{
    public WavFileInfo ReadFileInfo(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var reader = new BinaryReader(stream);

        var riff = reader.ReadBytes(4);
        if (riff[0] != 'R' || riff[1] != 'I' || riff[2] != 'F' || riff[3] != 'F')
            throw new InvalidDataException("Not a valid RIFF file.");

        reader.ReadInt32(); // file size - 8 (unused)

        var wave = reader.ReadBytes(4);
        if (wave[0] != 'W' || wave[1] != 'A' || wave[2] != 'V' || wave[3] != 'E')
            throw new InvalidDataException("Not a valid WAV file.");

        short audioFormat = 0;
        short numChannels = 0;
        int sampleRate = 0;
        short bitsPerSample = 0;
        int dataSize = 0;

        while (stream.Position < stream.Length - 8)
        {
            var chunkId = reader.ReadBytes(4);
            var chunkSize = reader.ReadInt32();
            var chunkIdStr = System.Text.Encoding.ASCII.GetString(chunkId);

            switch (chunkIdStr)
            {
                case "fmt ":
                    audioFormat = reader.ReadInt16();
                    numChannels = reader.ReadInt16();
                    sampleRate = reader.ReadInt32();
                    reader.ReadInt32(); // byte rate
                    reader.ReadInt16(); // block align
                    bitsPerSample = reader.ReadInt16();
                    if (chunkSize > 16)
                        reader.ReadBytes(chunkSize - 16);
                    break;

                case "data":
                    dataSize = chunkSize;
                    break;

                default:
                    reader.ReadBytes(chunkSize);
                    break;
            }

            if (chunkIdStr == "data" && dataSize > 0)
                break;
        }

        if (audioFormat != 1)
            throw new InvalidDataException($"Unsupported audio format: {audioFormat}. Only PCM (1) is supported.");

        var fileInfo = new FileInfo(filePath);
        var byteRate = sampleRate * numChannels * bitsPerSample / 8;
        var durationSeconds = byteRate > 0 ? (double)dataSize / byteRate : 0.0;
        var bitRate = sampleRate * bitsPerSample * numChannels;

        return new WavFileInfo
        {
            FilePath = Path.GetFullPath(filePath),
            FileSize = fileInfo.Length,
            Duration = TimeSpan.FromSeconds(durationSeconds),
            SampleRate = sampleRate,
            Channels = numChannels,
            BitRate = bitRate,
            Encoding = "PCM",
            BitsPerSample = bitsPerSample,
            DataSize = dataSize
        };
    }

    public float[] ReadSamples(string filePath)
    {
        var info = ReadFileInfo(filePath);
        var totalSamples = info.DataSize / (info.BitsPerSample / 8);
        var result = new float[totalSamples];

        using var stream = File.OpenRead(filePath);
        using var reader = new BinaryReader(stream);

        stream.Seek(0, SeekOrigin.Begin);

        var riff = reader.ReadBytes(4);
        reader.ReadInt32();
        reader.ReadBytes(4);

        int dataOffset = 0;
        while (stream.Position < stream.Length - 8)
        {
            var chunkId = reader.ReadBytes(4);
            var chunkSize = reader.ReadInt32();
            var chunkIdStr = System.Text.Encoding.ASCII.GetString(chunkId);

            if (chunkIdStr == "data")
            {
                dataOffset = (int)stream.Position;
                break;
            }

            reader.ReadBytes(chunkSize);
        }

        if (dataOffset == 0)
            throw new InvalidDataException("No data chunk found.");

        stream.Seek(dataOffset, SeekOrigin.Begin);
        var rawData = reader.ReadBytes(info.DataSize);

        for (int i = 0; i < totalSamples; i++)
        {
            if (info.BitsPerSample == 8)
                result[i] = (rawData[i] - 128) / 128f;
            else if (info.BitsPerSample == 16)
            {
                var sample = BinaryPrimitives.ReadInt16LittleEndian(
                    new ReadOnlySpan<byte>(rawData, i * 2, 2));
                result[i] = sample / 32768f;
            }
            else if (info.BitsPerSample == 24)
            {
                var sample = (rawData[i * 3] | (rawData[i * 3 + 1] << 8) | (rawData[i * 3 + 2] << 16));
                if ((sample & 0x800000) != 0)
                    sample |= unchecked((int)0xFF000000);
                result[i] = sample / 8388608f;
            }
            else if (info.BitsPerSample == 32)
            {
                var sample = BinaryPrimitives.ReadInt32LittleEndian(
                    new ReadOnlySpan<byte>(rawData, i * 4, 4));
                result[i] = sample / 2147483648f;
            }
        }

        return result;
    }

    public void WriteFile(string outputPath, float[] samples, WavFileInfo originalInfo)
    {
        var bitsPerSample = originalInfo.BitsPerSample;
        var channels = originalInfo.Channels;
        var sampleRate = originalInfo.SampleRate;
        var blockAlign = (short)(channels * bitsPerSample / 8);
        var byteRate = sampleRate * blockAlign;
        var dataSize = samples.Length * (bitsPerSample / 8);
        var fileSize = 36 + dataSize;

        using var stream = File.Create(outputPath);
        using var writer = new BinaryWriter(stream);

        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(fileSize);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16); // subchunk1 size
        writer.Write((short)1); // PCM
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);

        for (int i = 0; i < samples.Length; i++)
        {
            if (bitsPerSample == 8)
            {
                var val = (byte)Math.Clamp((samples[i] * 128f) + 128, 0, 255);
                writer.Write(val);
            }
            else if (bitsPerSample == 16)
            {
                var val = (short)Math.Clamp(samples[i] * 32768f, -32768, 32767);
                writer.Write(val);
            }
            else if (bitsPerSample == 24)
            {
                var val = (int)Math.Clamp(samples[i] * 8388608f, -8388608, 8388607);
                writer.Write((byte)(val & 0xFF));
                writer.Write((byte)((val >> 8) & 0xFF));
                writer.Write((byte)((val >> 16) & 0xFF));
            }
            else if (bitsPerSample == 32)
            {
                var val = (int)Math.Clamp(samples[i] * 2147483648f, -2147483648, 2147483647);
                writer.Write(val);
            }
        }
    }

    public void ResampleFile(string inputPath, string outputPath, int targetSampleRate)
    {
        using var reader = new AudioFileReader(inputPath);
        using var resampler = new MediaFoundationResampler(reader, targetSampleRate)
        {
            ResamplerQuality = 60
        };

        WaveFileWriter.CreateWaveFile(outputPath, resampler);
    }
}
