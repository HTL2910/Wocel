using Wocel.Capture.Cloud;
using Wocel.Capture.Logging;

namespace Wocel.Capture.Desktop.Services;

public sealed class CaptureWorkflow(UploadQueue uploadQueue, IActivityLog activityLog, TimeProvider timeProvider)
{
    public async Task<int> StartAsync(CancellationToken cancellationToken = default)
    {
        await activityLog.PruneAsync(timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        return await uploadQueue.RecoverInterruptedAsync(cancellationToken).ConfigureAwait(false);
    }
}
