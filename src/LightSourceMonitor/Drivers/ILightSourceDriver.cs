namespace LightSourceMonitor.Drivers;

public interface IPdArrayDriver : IDisposable
{
    bool IsOpen { get; }
    string DeviceSN { get; }
    bool Open(string instanceIdSubstring);
    bool Initialize();
    double[]? GetActualPower(int channelCount = 33);
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
