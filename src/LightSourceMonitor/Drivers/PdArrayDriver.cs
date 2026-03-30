using System.Runtime.InteropServices;
using LightSourceMonitor.Drivers.Native;
using LightSourceMonitor.Models;
using Microsoft.Extensions.Logging;

namespace LightSourceMonitor.Drivers;

public class PdArrayDriver : IPdArrayDriver
{
    private const int PdVid = 0x05A6;
    private const int PdPid = 0x49A5;
    private const int MaxPdChannel = 33;

    private readonly ILogger<PdArrayDriver> _logger;
    private IntPtr _usbHandle;
    private IntPtr _deviceList;
    private IntPtr _deviceInfo;
    private KusbInitDelegate? _kusbInit;
    private KusbFreeDelegate? _kusbFree;
    private KusbControlTransferDelegate? _controlTransfer;
    private bool _disposed;

    public bool IsOpen => _usbHandle != IntPtr.Zero;
    public string DeviceSN { get; private set; } = "";

    public PdArrayDriver(ILogger<PdArrayDriver> logger)
    {
        _logger = logger;
    }

    public static List<string> EnumerateDevices()
    {
        var result = new List<string>();
        if (!LibUsbK.LstK_Init(out var deviceList, 0))
            return result;

        try
        {
            LibUsbK.LstK_Count(deviceList, out var count);
            if (count == 0) return result;

            LibUsbK.LstK_MoveReset(deviceList);
            for (uint i = 0; i < count; i++)
            {
                if (!LibUsbK.LstK_MoveNext(deviceList, out var devInfoPtr)) break;
                var devInfo = Marshal.PtrToStructure<KLST_DEVINFO>(devInfoPtr);
                if (devInfo.Common.Vid == PdVid && devInfo.Common.Pid == PdPid)
                    result.Add(devInfo.Common.InstanceID);
            }
        }
        finally
        {
            LibUsbK.LstK_Free(deviceList);
        }
        return result;
    }

    public bool Open(string instanceIdSubstring)
    {
        Close();

        if (!LibUsbK.LstK_Init(out _deviceList, 0))
        {
            _logger.LogError("Failed to initialize device list. Error: 0x{Error:X8}", Marshal.GetLastWin32Error());
            return false;
        }

        LibUsbK.LstK_Count(_deviceList, out var count);
        if (count == 0)
        {
            _logger.LogWarning("No USB devices connected");
            LibUsbK.LstK_Free(_deviceList);
            _deviceList = IntPtr.Zero;
            return false;
        }

        LibUsbK.LstK_MoveReset(_deviceList);
        bool found = false;
        KLST_DEVINFO devInfo = default;

        for (uint i = 0; i < count; i++)
        {
            if (!LibUsbK.LstK_MoveNext(_deviceList, out _deviceInfo)) break;
            devInfo = Marshal.PtrToStructure<KLST_DEVINFO>(_deviceInfo);

            if (devInfo.Common.Vid == PdVid && devInfo.Common.Pid == PdPid &&
                devInfo.Common.InstanceID.Contains(instanceIdSubstring, StringComparison.OrdinalIgnoreCase))
            {
                found = true;
                DeviceSN = devInfo.Common.InstanceID;
                break;
            }
        }

        if (!found)
        {
            _logger.LogWarning("Device not found: {SN}", instanceIdSubstring);
            LibUsbK.LstK_Free(_deviceList);
            _deviceList = IntPtr.Zero;
            return false;
        }

        if (!LibUsbK.LibK_LoadDriverAPI(out var driverApi, devInfo.DriverID))
        {
            _logger.LogError("Failed to load driver API for DriverID={DriverID}", devInfo.DriverID);
            Close();
            return false;
        }

        _kusbInit = Marshal.GetDelegateForFunctionPointer<KusbInitDelegate>(driverApi.Init);
        _kusbFree = Marshal.GetDelegateForFunctionPointer<KusbFreeDelegate>(driverApi.Free);
        _controlTransfer = Marshal.GetDelegateForFunctionPointer<KusbControlTransferDelegate>(driverApi.ControlTransfer);

        if (!_kusbInit(out _usbHandle, _deviceInfo))
        {
            _logger.LogError("UsbAPI.Init failed. Error: 0x{Error:X8}", Marshal.GetLastWin32Error());
            Close();
            return false;
        }

        _logger.LogInformation("PD Array opened: {SN}", DeviceSN);
        return true;
    }

    public bool Initialize()
    {
        if (!IsOpen) return false;
        if (!SetMfgMode())
        {
            _logger.LogError("SetMFGMode failed");
            return false;
        }
        Thread.Sleep(500);
        if (!SetPowerMode(true))
        {
            _logger.LogWarning("SetPowerMode(High) returned error, continuing...");
        }
        return true;
    }

    public double[]? GetActualPower(int channelCount = MaxPdChannel)
    {
        if (!IsOpen || _controlTransfer == null) return null;
        if (channelCount > MaxPdChannel) channelCount = MaxPdChannel;

        var pkt = new WINUSB_SETUP_PACKET
        {
            RequestType = 0xC4,
            Request = 0xFF,
            Value = 0x000A,
            Index = 0x0000,
            Length = (ushort)(channelCount * 2)
        };

        var buffer = new byte[channelCount * 2];
        if (!_controlTransfer(_usbHandle, pkt, buffer, (uint)buffer.Length, out _, IntPtr.Zero))
        {
            _logger.LogWarning("GetActualPower ControlTransfer failed");
            return null;
        }

        var powers = new double[channelCount];
        for (int i = 0; i < channelCount; i++)
        {
            short raw = BitConverter.ToInt16(buffer, i * 2);
            powers[i] = raw / 100.0;
        }
        return powers;
    }

    public WbaTelemetrySnapshot? GetWbaTelemetry()
    {
        // WBA protocol command is not defined yet in hardware mode.
        return null;
    }

    private bool SetMfgMode()
    {
        if (_controlTransfer == null) return false;
        var pkt = new WINUSB_SETUP_PACKET
        {
            RequestType = 0x44,
            Request = 0xFF,
            Value = 0x0000,
            Index = 0x0000,
            Length = 0x0008
        };
        var data = System.Text.Encoding.ASCII.GetBytes("MFG_CMD\0");
        return _controlTransfer(_usbHandle, pkt, data, (uint)data.Length, out _, IntPtr.Zero);
    }

    private bool SetPowerMode(bool high)
    {
        if (_controlTransfer == null) return false;
        var pkt = new WINUSB_SETUP_PACKET
        {
            RequestType = 0x44,
            Request = 0x00,
            Value = 0x0001,
            Index = 0x0000,
            Length = 0x0001
        };
        var data = new byte[] { (byte)(high ? 1 : 0) };
        return _controlTransfer(_usbHandle, pkt, data, 1, out _, IntPtr.Zero);
    }

    public bool GetPowerMode()
    {
        if (_controlTransfer == null) return false;
        var pkt = new WINUSB_SETUP_PACKET
        {
            RequestType = 0xC4,
            Request = 0x01,
            Value = 0x0002,
            Index = 0x0000,
            Length = 0x0001
        };
        var data = new byte[1];
        if (!_controlTransfer(_usbHandle, pkt, data, 1, out _, IntPtr.Zero))
            return false;
        return data[0] == 1;
    }

    public void Close()
    {
        if (_usbHandle != IntPtr.Zero && _kusbFree != null)
        {
            _kusbFree(_usbHandle);
            _usbHandle = IntPtr.Zero;
        }
        if (_deviceList != IntPtr.Zero)
        {
            LibUsbK.LstK_Free(_deviceList);
            _deviceList = IntPtr.Zero;
        }
        _deviceInfo = IntPtr.Zero;
        _kusbInit = null;
        _kusbFree = null;
        _controlTransfer = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Close();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~PdArrayDriver() => Dispose();
}
