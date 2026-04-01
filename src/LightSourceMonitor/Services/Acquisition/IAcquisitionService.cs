using LightSourceMonitor.Models;

namespace LightSourceMonitor.Services.Acquisition;

public interface IAcquisitionService
{
    bool IsRunning { get; }
    bool IsPdConnected { get; }
    IReadOnlyDictionary<string, bool> PdDeviceStates { get; }
    int SamplingIntervalMs { get; set; }
    int WmSweepEveryN { get; set; }
    int DbWriteEveryN { get; set; }
    event Action<Dictionary<int, MeasurementRecord>>? DataAcquired;
    event Action<WavelengthTableSnapshot>? WavelengthTableUpdated;
    event Action<IReadOnlyDictionary<string, WbaTelemetrySnapshot>>? WbaTelemetryAcquired;
    event Action<bool>? PdConnectionChanged;
    event Action<IReadOnlyDictionary<string, bool>>? PdDeviceConnectionChanged;
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
