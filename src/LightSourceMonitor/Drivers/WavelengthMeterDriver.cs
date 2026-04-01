using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace LightSourceMonitor.Drivers;

/// <summary>
/// 波长计驱动 - 通过UDL2_Server COM接口与AQ6150波长计通信
/// 架构与PdArrayDriver保持一致，为未来转换为P/Invoke预留接口
/// </summary>
public class WavelengthMeterDriver : IWavelengthMeterDriver
{
    private static readonly Guid EngineClsid = new("25500971-ceca-4041-b81c-4b500df01c31");
    private static readonly Guid WmClsid = new("3873e640-bf69-4ceb-9a1e-0d9585e4dd17");

    private readonly ILogger<WavelengthMeterDriver> _logger;
    
    // COM对象引用
    private dynamic? _engine;
    private dynamic? _wm;
    
    // 状态跟踪
    private bool _isInitialized;
    private bool _disposed;
    
    // 配置参数缓存
    private readonly Dictionary<int, WmChannelConfig> _channelConfigs = new();

    public bool IsInitialized => _isInitialized && _engine != null && _wm != null;

    public WavelengthMeterDriver(ILogger<WavelengthMeterDriver> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 初始化波长计 - 类似PdArrayDriver.Open()
    /// </summary>
    public bool Init(string configXmlPath)
    {
        try
        {
            if (!File.Exists(configXmlPath))
            {
                _logger.LogError("Config file not found: {Path}", configXmlPath);
                return false;
            }

            // 初始化COM
            CoInitialize(IntPtr.Zero);

            // 创建COM实例 - 使用ProgID优先，失败后使用CLSID
            var engineType = ResolveComType("UDL2_Server.UDL2_Engine", EngineClsid);
            var wmType = ResolveComType("UDL2_Server.UDL2_WM", WmClsid);

            if (engineType == null || wmType == null)
            {
                _logger.LogError("Failed to resolve UDL2_Server COM types");
                return false;
            }

            try
            {
                _engine = Activator.CreateInstance(engineType);
                _wm = Activator.CreateInstance(wmType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create COM instances");
                return false;
            }

            if (_engine == null || _wm == null)
            {
                _logger.LogError("COM instances are null after creation");
                return false;
            }

            // 加载配置文件 - 对应MFC中的LoadConfiguration
            int hr = _engine!.LoadConfiguration(configXmlPath);
            if (hr != 0)
            {
                string errMsg = GetComErrorMessage() ?? "Unknown error";
                _logger.LogError("LoadConfiguration failed (hr=0x{Hr:X8}): {Error}", hr, errMsg);
                Close();
                return false;
            }

            _logger.LogInformation("WM: Configuration loaded from {Path}", configXmlPath);

            // 打开引擎 - 对应MFC中的OpenEngine
            hr = _engine.OpenEngine();
            if (hr != 0)
            {
                string errMsg = GetComErrorMessage() ?? "Unknown error";
                _logger.LogError("OpenEngine failed (hr=0x{Hr:X8}): {Error}", hr, errMsg);
                Close();
                return false;
            }

            _isInitialized = true;
            _logger.LogInformation("WavelengthMeter initialized successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during WM initialization");
            Close();
            return false;
        }
    }

    /// <summary>
    /// 设置波长扫描参数 - 对应MFC中的SetWMParameters
    /// </summary>
    public bool SetParameters(int chIndex, int wlUnit, double startWl, double stopWl,
                              double peakExcursion, double peakThreshold)
    {
        if (!IsInitialized)
        {
            _logger.LogWarning("WM not initialized");
            return false;
        }

        try
        {
            // 缓存配置参数，用于调试
            _channelConfigs[chIndex] = new WmChannelConfig
            {
                ChannelIndex = chIndex,
                WlUnit = wlUnit,
                StartWl = startWl,
                StopWl = stopWl,
                PeakExcursion = peakExcursion,
                PeakThreshold = peakThreshold
            };

            int hr = _wm!.SetWMParameters(chIndex, wlUnit, startWl, stopWl, peakExcursion, peakThreshold);
            if (hr != 0)
            {
                _logger.LogWarning("SetWMParameters failed (ch={Ch}, hr=0x{Hr:X8})", chIndex, hr);
                return false;
            }

            _logger.LogDebug("WM: SetWMParameters ch={Ch}, wl={Start}-{Stop}nm", 
                chIndex, startWl, stopWl);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SetWMParameters failed for channel {Ch}", chIndex);
            return false;
        }
    }

    /// <summary>
    /// 执行单次扫描 - 对应MFC中的ExecuteWMSingleSweep
    /// </summary>
    public bool ExecuteSingleSweep(int chIndex)
    {
        if (!IsInitialized)
        {
            _logger.LogWarning("WM not initialized");
            return false;
        }

        try
        {
            int hr = _wm!.ExecuteWMSingleSweep(chIndex);
            if (hr != 0)
            {
                _logger.LogWarning("ExecuteWMSingleSweep failed (ch={Ch}, hr=0x{Hr:X8})", chIndex, hr);
                return false;
            }

            _logger.LogDebug("WM: ExecuteSingleSweep ch={Ch}", chIndex);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExecuteWMSingleSweep failed for channel {Ch}", chIndex);
            return false;
        }
    }

    /// <summary>
    /// 获取扫描结果 - 对应MFC中的GetWMChResult
    /// 返回 (波长数组, 功率数组, 结果计数)
    /// </summary>
    public (double[] wavelengths, double[] powers, int count)? GetResult(int chIndex)
    {
        if (!IsInitialized)
        {
            _logger.LogWarning("WM not initialized");
            return null;
        }

        try
        {
            int wlCount = 0;
            object? wavelengths = null;
            object? powers = null;

            // GetWMChResult(chIndex, ref wlCount, ref wavelengths[], ref powers[])
            int hr = _wm!.GetWMChResult(chIndex, ref wlCount, ref wavelengths, ref powers);
            
            if (hr != 0)
            {
                _logger.LogWarning("GetWMChResult failed (ch={Ch}, hr=0x{Hr:X8})", chIndex, hr);
                return null;
            }

            if (wavelengths == null || powers == null)
            {
                _logger.LogWarning("GetWMChResult returned null arrays (ch={Ch})", chIndex);
                return null;
            }

            if (wlCount <= 0)
            {
                _logger.LogWarning("GetWMChResult returned count=0 (ch={Ch})", chIndex);
                return null;
            }

            try
            {
                var wlArray = (double[])wavelengths;
                var pwrArray = (double[])powers;

                // 验证数组长度
                if (wlArray.Length < wlCount || pwrArray.Length < wlCount)
                {
                    _logger.LogWarning(
                        "WMChResult array size mismatch: count={Count}, wlLen={WlLen}, pwrLen={PwrLen}",
                        wlCount, wlArray.Length, pwrArray.Length);
                    return null;
                }

                _logger.LogDebug("WM: GetResult ch={Ch}, count={Count}, wl[0]={Wl:F2}nm, pwr[0]={Pwr:F2}",
                    chIndex, wlCount, wlArray[0], pwrArray[0]);

                return (wlArray, pwrArray, wlCount);
            }
            catch (InvalidCastException ex)
            {
                _logger.LogError(ex, "Array casting failed in GetResult");
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetWMChResult failed for channel {Ch}", chIndex);
            return null;
        }
    }

    /// <summary>
    /// 关闭波长计连接
    /// </summary>
    public void Close()
    {
        try
        {
            _isInitialized = false;

            // 关闭引擎
            if (_engine != null)
            {
                try
                {
                    int hr = _engine.CloseEngine();
                    if (hr != 0)
                    {
                        _logger.LogWarning("CloseEngine returned hr=0x{Hr:X8}", hr);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error closing engine");
                }

                try
                {
                    Marshal.ReleaseComObject(_engine);
                }
                catch { }

                _engine = null;
            }

            // 释放WM对象
            if (_wm != null)
            {
                try
                {
                    Marshal.ReleaseComObject(_wm);
                }
                catch { }

                _wm = null;
            }

            _logger.LogInformation("WavelengthMeter closed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during WM close");
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        Close();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~WavelengthMeterDriver()
    {
        Dispose();
    }

    #region Private Helpers

    /// <summary>
    /// 获取COM错误消息
    /// </summary>
    private string? GetComErrorMessage()
    {
        try
        {
            if (_engine != null)
            {
                return _engine.GetLastErrorMessage() as string;
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// 解析COM类型 - 尝试ProgID，失败后用CLSID
    /// </summary>
    private Type? ResolveComType(string progId, Guid clsid)
    {
        try
        {
            // 尝试ProgID（更稳定）
            var type = Type.GetTypeFromProgID(progId);
            if (type != null)
            {
                _logger.LogDebug("Resolved COM type via ProgID: {ProgId}", progId);
                return type;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to resolve COM via ProgID: {ProgId}", progId);
        }

        try
        {
            // 回退到CLSID
            var type = Type.GetTypeFromCLSID(clsid, throwOnError: false);
            if (type != null)
            {
                _logger.LogDebug("Resolved COM type via CLSID: {Clsid}", clsid);
                return type;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve COM via CLSID: {Clsid}", clsid);
        }

        return null;
    }

    [DllImport("ole32.dll")]
    private static extern void CoInitialize(IntPtr pvReserved);

    #endregion

    /// <summary>
    /// WM通道配置 - 仅供调试/日志记录
    /// </summary>
    private class WmChannelConfig
    {
        public int ChannelIndex { get; set; }
        public int WlUnit { get; set; }
        public double StartWl { get; set; }
        public double StopWl { get; set; }
        public double PeakExcursion { get; set; }
        public double PeakThreshold { get; set; }
    }
}
