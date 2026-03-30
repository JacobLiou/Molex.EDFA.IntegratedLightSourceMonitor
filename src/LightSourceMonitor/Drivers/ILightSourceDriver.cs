using LightSourceMonitor.Models;

namespace LightSourceMonitor.Drivers;

public interface IPdArrayDriver : IDisposable
{
    bool IsOpen { get; }
    string DeviceSN { get; }
    bool Open(string instanceIdSubstring);
    bool Initialize();
    double[]? GetActualPower(int channelCount = 33);
    WbaTelemetrySnapshot? GetWbaTelemetry();
    void Close();
}

public interface IWavelengthMeterDriver : IDisposable
{
    bool IsInitialized { get; }
    bool Init(string configXmlPath);
    bool SetParameters(int chIndex, int wlUnit, double startWl, double stopWl, double peakExcursion, double peakThreshold);
    bool ExecuteSingleSweep(int chIndex);
    (double[] wavelengths, double[] powers, int count)? GetResult(int chIndex);
    void Close();
}

public interface IWavelengthServiceDriver : IDisposable
{
    bool IsConnected { get; }
    Task<bool> ConnectAsync(string host, int port);
    Task<bool> DisconnectAsync();
    Task<(double wavelength, double power)> GetWavelengthAsync(string deviceId, int channelIndex);
}
