namespace LightSourceMonitor.Services.Tms;

public interface ITmsService
{
    Task UploadPendingRecordsAsync(CancellationToken cancellationToken = default);
    Task<bool> TestConnectionAsync();
}
