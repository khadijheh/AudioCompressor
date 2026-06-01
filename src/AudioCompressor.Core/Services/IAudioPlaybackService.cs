namespace AudioCompressor.Core.Services;

public interface IAudioPlaybackService : IDisposable
{
    void Play(string filePath);
    void Stop();
    bool IsPlaying { get; }
}
