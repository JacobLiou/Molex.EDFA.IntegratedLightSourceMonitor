using LightSourceMonitor.Drivers.Native;
using LightSourceMonitor.Models;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace LightSourceMonitor.Drivers;

public class PdArrayDriver : IPdArrayDriver
{
    private const int PdVid = 0x05A6;
    private const int PdPid = 0x49A5;
    private const int MaxPdChannel = 33;

    // Vendor IN (0xC4): same as legacy GetActualPower / OEM 0xFFxx commands (bRequest=0xFF, wValue low byte = sub-command).
    private const byte VendorRequestIn = 0xC4;

    private readonly ILogger<PdArrayDriver> _logger;
    private IntPtr _usbHandle;
    private IntPtr _deviceList;
    private IntPtr _deviceInfo;
    private KusbInitDelegate? _kusbInit;
    private KusbFreeDelegate? _kusbFree;
    private KusbControlTransferDelegate? _controlTransfer;
    private bool _disposed;

    /// <summary>PD channel count from MFG string (e.g. &quot;PD Count: 32&quot;). USB 0xFF0A wLength must not exceed this.</summary>
    private int _pdChannelCount = MaxPdChannel;

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
        _pdChannelCount = MaxPdChannel;

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
                (devInfo.Common.InstanceID.Contains(instanceIdSubstring, StringComparison.OrdinalIgnoreCase) || instanceIdSubstring.Contains(devInfo.Common.InstanceID, StringComparison.OrdinalIgnoreCase)))
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
            var le = Marshal.GetLastWin32Error();
            _logger.LogError("UsbAPI.Init failed. Error: 0x{Error:X8}", le);

            Close();
            return false;
        }

        if (driverApi.ClaimInterface != IntPtr.Zero)
        {
            var claim = Marshal.GetDelegateForFunctionPointer<KusbClaimInterfaceDelegate>(driverApi.ClaimInterface);
            var claimed = claim(_usbHandle, 0, false);

            if (!claimed)
                _logger.LogWarning("UsbAPI.ClaimInterface(0) failed. Error: 0x{Error:X8}", Marshal.GetLastWin32Error());
        }

        _logger.LogInformation("PD Array opened: {SN}", DeviceSN);
        return true;
    }

    public bool Initialize()
    {
        if (!IsOpen) return false;

        // NCS1K OEM: only 0xFF01 Get Mfg Information is required for init (no 0xFF00 MFG mode, no power-mode sequence).
        if (!TryGetMfgInformation(out var transferred, out var mfgText))
        {
            _logger.LogError("Get Mfg Information (0xFF01) failed for {DeviceSN}", DeviceSN);
            return false;
        }

        _logger.LogInformation("PD MFG information ({Bytes} bytes) for {DeviceSN}:\n{Text}", transferred, DeviceSN, mfgText);

        if (TryParsePdCountFromMfgText(mfgText, out var parsedPd))
        {
            _pdChannelCount = parsedPd;
            _logger.LogInformation("PD channel count from MFG for {DeviceSN}: {PdCount}", DeviceSN, _pdChannelCount);
        }
        else
            _logger.LogDebug("PD Count not parsed from MFG for {DeviceSN}; using default {Default}", DeviceSN, _pdChannelCount);

        return true;
    }

    public double[]? GetActualPower(int channelCount = MaxPdChannel)
    {
        if (!IsOpen) return null;
        if (channelCount < 1) channelCount = 1;
        if (channelCount > MaxPdChannel) channelCount = MaxPdChannel;

        // Firmware expects a full PD snapshot (wLength = _pdChannelCount*2); partial reads fail with ERROR_GEN_FAILURE.
        // Callers index by ChannelIndex into the returned array (same pattern as OEM/MFC).
        int xferPd = _pdChannelCount;
        if (xferPd < 1) xferPd = 1;
        if (xferPd > MaxPdChannel) xferPd = MaxPdChannel;

        var pkt = new WINUSB_SETUP_PACKET
        {
            RequestType = VendorRequestIn,
            Request = 0xFF,
            Value = 0x000A,
            Index = 0x0000,
            Length = (ushort)(xferPd * 2)
        };

        var buffer = new byte[xferPd * 2];
        if (!SendControlTransfer("GetActualPower", pkt, buffer, out _))
            return null;

        var powers = new double[xferPd];
        for (int i = 0; i < xferPd; i++)
        {
            short raw = BitConverter.ToInt16(buffer, i * 2);
            powers[i] = raw / 100.0;
        }
        return powers;
    }

    private static bool TryParsePdCountFromMfgText(string text, out int pdCount)
    {
        pdCount = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var m = Regex.Match(text, @"PD\s*Count\s*:\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!m.Success)
            return false;

        if (!int.TryParse(m.Groups[1].Value, out var n) || n < 1)
            return false;

        pdCount = Math.Min(n, MaxPdChannel);
        return true;
    }

    public WbaTelemetrySnapshot? GetWbaTelemetry()
    {
        // WBA is handled by independent WbaDeviceManager
        return null;
    }

    /// <summary>0xFF01 Get Mfg Information — same vendor pattern as 0xFF0A: Request=0xFF, wValue low byte = command index.</summary>
    private bool TryGetMfgInformation(out uint transferredLength, out string text)
    {
        transferredLength = 0;
        text = "";

        const int bufLen = 512;
        var pkt = new WINUSB_SETUP_PACKET
        {
            RequestType = VendorRequestIn,
            Request = 0xFF,
            Value = 0x0001,
            Index = 0x0000,
            Length = bufLen
        };

        var buffer = new byte[bufLen];
        if (!SendControlTransfer("GetMfgInformation", pkt, buffer, out transferredLength))
            return false;

        var n = (int)Math.Min(transferredLength, buffer.Length);
        while (n > 0 && buffer[n - 1] == 0)
            n--;

        if (n <= 0)
            return true;

        try
        {
            text = Encoding.UTF8.GetString(buffer, 0, n);
        }
        catch
        {
            text = Encoding.ASCII.GetString(buffer, 0, n);
        }

        return true;
    }

    private bool SendControlTransfer(string operation, WINUSB_SETUP_PACKET setupPacket, byte[] buffer, out uint transferredLength)
    {
        transferredLength = 0;

        if (!IsOpen || _controlTransfer == null)
        {
            _logger.LogWarning("{Operation} skipped because device is not open", operation);
            return false;
        }

        if (buffer.Length < setupPacket.Length)
        {
            _logger.LogError(
                "{Operation} buffer is shorter than setup packet length. BufferLength={BufferLength}, ExpectedLength={ExpectedLength}",
                operation,
                buffer.Length,
                setupPacket.Length);
            return false;
        }

        GCHandle pin = default;
        IntPtr bufferPtr;
        if (setupPacket.Length == 0)
        {
            bufferPtr = IntPtr.Zero;
        }
        else
        {
            pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            bufferPtr = pin.AddrOfPinnedObject();
        }

        bool success;
        try
        {
            success = _controlTransfer(_usbHandle, setupPacket, bufferPtr, setupPacket.Length, out transferredLength, IntPtr.Zero);
        }
        finally
        {
            if (pin.IsAllocated)
                pin.Free();
        }

        if (!success)
        {
            var err = Marshal.GetLastWin32Error();
            _logger.LogWarning(
                "{Operation} ControlTransfer failed. RequestType=0x{RequestType:X2}, Request=0x{Request:X2}, Value=0x{Value:X4}, Index=0x{Index:X4}, Length={Length}, LastError=0x{Error:X8}",
                operation,
                setupPacket.RequestType,
                setupPacket.Request,
                setupPacket.Value,
                setupPacket.Index,
                setupPacket.Length,
                err);

            return false;
        }

        if (setupPacket.Length > 0 && transferredLength != setupPacket.Length)
        {
            _logger.LogDebug(
                "{Operation} completed with partial transfer. Transferred={TransferredLength}, Expected={ExpectedLength}",
                operation,
                transferredLength,
                setupPacket.Length);
        }

        return true;
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
