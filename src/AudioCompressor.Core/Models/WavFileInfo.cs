namespace AudioCompressor.Core.Models;

public record WavFileInfo
{
    public required string FilePath { get; init; }
    public long FileSize { get; init; }
    public TimeSpan Duration { get; init; }
    public int SampleRate { get; init; }
    public short Channels { get; init; }
    public int BitRate { get; init; }
    public string Encoding { get; init; } = "PCM";
    public short BitsPerSample { get; init; }
    public int DataSize { get; init; }
}
