using NAudio.Wave;

namespace AudioCompressor.Core.Services;

public class NAudioPlaybackService : IAudioPlaybackService
{
    private WaveOutEvent? _outputDevice;
    private AudioFileReader? _audioFileReader;

    public bool IsPlaying => _outputDevice?.PlaybackState == PlaybackState.Playing;

    public void Play(string filePath)
    {
        Stop();
        _audioFileReader = new AudioFileReader(filePath);
        _outputDevice = new WaveOutEvent();
        _outputDevice.Init(_audioFileReader);
        _outputDevice.Play();
    }

    public void Stop()
    {
        _outputDevice?.Stop();
        _outputDevice?.Dispose();
        _outputDevice = null;
        _audioFileReader?.Dispose();
        _audioFileReader = null;
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
