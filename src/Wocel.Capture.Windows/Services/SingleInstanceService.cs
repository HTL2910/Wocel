using System.IO.Pipes;

namespace Wocel.Capture.Windows.Services;

public sealed class SingleInstanceService : IDisposable
{
    private readonly string _pipeName;
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _shutdown = new();

    public SingleInstanceService(string name)
    {
        _pipeName = name.Replace('.', '-');
        _mutex = new Mutex(true, name, out var created);
        IsPrimary = created;
        if (IsPrimary)
        {
            _ = ListenAsync(_shutdown.Token);
        }
    }

    public bool IsPrimary { get; }
    public event EventHandler? Activated;

    public void NotifyPrimary()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
            client.Connect(500);
            client.WriteByte(1);
        }
        catch (TimeoutException)
        {
            // The primary process may still be starting. Exiting remains safe.
        }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                _ = server.ReadByte();
                Activated?.Invoke(this, EventArgs.Empty);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _shutdown.Dispose();
        if (IsPrimary)
        {
            _mutex.ReleaseMutex();
        }
        _mutex.Dispose();
    }
}
