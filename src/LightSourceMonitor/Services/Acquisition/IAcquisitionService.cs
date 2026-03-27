using LightSourceMonitor.Models;

namespace LightSourceMonitor.Services.Acquisition;

public interface IAcquisitionService
{
    bool IsRunning { get; }
    int SamplingIntervalMs { get; set; }
    int WmSweepEveryN { get; set; }
    int DbWriteEveryN { get; set; }
    event Action<Dictionary<int, MeasurementRecord>>? DataAcquired;
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
