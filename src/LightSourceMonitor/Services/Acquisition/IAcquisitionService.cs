using LightSourceMonitor.Models;

namespace LightSourceMonitor.Services.Acquisition;

public interface IAcquisitionService
{
    bool IsRunning { get; }
    bool IsPdConnected { get; }
    int SamplingIntervalMs { get; set; }
    int WmSweepEveryN { get; set; }
    int DbWriteEveryN { get; set; }
    event Action<Dictionary<int, MeasurementRecord>>? DataAcquired;
    event Action<bool>? PdConnectionChanged;
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
