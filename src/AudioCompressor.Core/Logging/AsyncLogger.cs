using System.Threading.Channels;

namespace AudioCompressor.Core.Logging;

public class AsyncLogger : IDisposable
{
    private readonly Channel<string> _channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    public event Action<string>? OnLog;

    public AsyncLogger()
    {
        _ = Task.Run(() => ConsumeAsync(_cts.Token));
    }

    public void Log(string message)
    {
        if (!_disposed)
            _channel.Writer.TryWrite(message);
    }

    private async Task ConsumeAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var msg in _channel.Reader.ReadAllAsync(ct))
            {
                Console.WriteLine($"[ALGO] {msg}");
                OnLog?.Invoke(msg);
            }
        }
        catch (OperationCanceledException) { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _channel.Writer.TryComplete();
        _cts.Cancel();
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }
}
