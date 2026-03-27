using LightSourceMonitor.Models;

namespace LightSourceMonitor.Services.Acquisition;

public interface IAcquisitionService
{
    bool IsRunning { get; }
    event Action<Dictionary<int, MeasurementRecord>>? DataAcquired;
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
