using System.IO.Pipes;
using Wocel.Capture.Platform.Services;

namespace Wocel.Capture.Platform.Windows;

public sealed class WindowsSingleInstanceService : ISingleInstanceService
{
    private readonly string _pipeName;
    // Named event chứ không dùng Mutex: Mutex gắn với thread tạo ra nó nên Dispose từ thread khác sẽ lỗi.
    // Event không có chủ sở hữu; tên được giải phóng khi handle cuối cùng đóng.
    private readonly EventWaitHandle _instanceHandle;
    private readonly CancellationTokenSource _shutdown = new();

    public WindowsSingleInstanceService(string name = "Wocel.Capture.Desktop")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _pipeName = new string(name.Select(character => char.IsAsciiLetterOrDigit(character) ? character : '-').ToArray());
        _instanceHandle = new EventWaitHandle(false, EventResetMode.ManualReset, name, out var created);
        IsPrimary = created;
        if (IsPrimary) _ = ListenAsync(_shutdown.Token);
    }

    public bool IsPrimary { get; }
    public event EventHandler? Activated;

    public void NotifyPrimary()
    {
        if (IsPrimary) return;
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
            client.Connect(2000);
            client.WriteByte(1);
        }
        catch (TimeoutException)
        {
            // The primary may still be starting; the secondary can exit safely.
        }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
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
        _instanceHandle.Dispose();
    }
}
