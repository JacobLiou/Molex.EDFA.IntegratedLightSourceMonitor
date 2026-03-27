using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace LightSourceMonitor.Drivers;

public class WavelengthMeterDriver : IWavelengthMeterDriver
{
    private readonly ILogger<WavelengthMeterDriver> _logger;
    private dynamic? _engine;
    private dynamic? _wm;
    private bool _disposed;

    public bool IsInitialized => _engine != null && _wm != null;

    public WavelengthMeterDriver(ILogger<WavelengthMeterDriver> logger)
    {
        _logger = logger;
    }

    public bool Init(string configXmlPath)
    {
        try
        {
            var engineType = Type.GetTypeFromProgID("UDL2_Server.UDL2_Engine");
            var wmType = Type.GetTypeFromProgID("UDL2_Server.UDL2_WM");

            if (engineType == null || wmType == null)
            {
                _logger.LogError("UDL2_Server COM components not registered");
                return false;
            }

            _engine = Activator.CreateInstance(engineType);
            _wm = Activator.CreateInstance(wmType);

            if (_engine == null || _wm == null)
            {
                _logger.LogError("Failed to create UDL2 COM instances");
                return false;
            }

            int hr = _engine!.LoadConfiguration(configXmlPath);
            if (hr != 0)
            {
                string errMsg = _engine.GetLastErrorMessage();
                _logger.LogError("LoadConfiguration failed: {Error}", errMsg);
                return false;
            }

            hr = _engine.OpenEngine();
            if (hr != 0)
            {
                string errMsg = _engine.GetLastErrorMessage();
                _logger.LogError("OpenEngine failed: {Error}", errMsg);
                return false;
            }

            _logger.LogInformation("WavelengthMeter initialized with config: {Path}", configXmlPath);
            return true;
        }
        catch (COMException ex)
        {
            _logger.LogError(ex, "COM error during WM initialization");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during WM initialization");
            return false;
        }
    }

    public bool SetParameters(int chIndex, int wlUnit, double startWl, double stopWl,
                              double peakExcursion, double peakThreshold)
    {
        if (_wm == null) return false;
        try
        {
            int hr = _wm.SetWMParameters(chIndex, wlUnit, startWl, stopWl, peakExcursion, peakThreshold);
            return hr == 0;
        }
        catch (COMException ex)
        {
            _logger.LogError(ex, "SetWMParameters failed");
            return false;
        }
    }

    public bool ExecuteSingleSweep(int chIndex)
    {
        if (_wm == null) return false;
        try
        {
            int hr = _wm.ExecuteWMSingleSweep(chIndex);
            return hr == 0;
        }
        catch (COMException ex)
        {
            _logger.LogError(ex, "ExecuteWMSingleSweep failed");
            return false;
        }
    }

    public (double[] wavelengths, double[] powers, int count)? GetResult(int chIndex)
    {
        if (_wm == null) return null;
        try
        {
            int wlCount = 0;
            object? wavelengths = null;
            object? powers = null;

            int hr = _wm.GetWMChResult(chIndex, ref wlCount, ref wavelengths, ref powers);
            if (hr != 0 || wavelengths == null || powers == null)
            {
                _logger.LogWarning("GetWMChResult returned hr={Hr}, count={Count}", hr, wlCount);
                return null;
            }

            var wlArray = (double[])wavelengths;
            var pwrArray = (double[])powers;
            return (wlArray, pwrArray, wlCount);
        }
        catch (COMException ex)
        {
            _logger.LogError(ex, "GetWMChResult failed");
            return null;
        }
    }

    public void Close()
    {
        try
        {
            if (_engine != null)
            {
                try { _engine.CloseEngine(); } catch { }
                Marshal.ReleaseComObject(_engine);
                _engine = null;
            }
            if (_wm != null)
            {
                Marshal.ReleaseComObject(_wm);
                _wm = null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during WM cleanup");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Close();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~WavelengthMeterDriver() => Dispose();
}
