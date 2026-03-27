using System.Runtime.InteropServices;

namespace LightSourceMonitor.Drivers.Native;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
public struct KLST_DEV_COMMON_INFO
{
    public int Vid;
    public int Pid;
    public int MI;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string InstanceID;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
public struct KLST_DEVINFO
{
    public KLST_DEV_COMMON_INFO Common;
    public int DriverID;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string DeviceInterfaceGUID;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string DeviceID;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string ClassGUID;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string Mfg;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string DeviceDesc;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string Service;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string SymbolicLink;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string DevicePath;
    public int LUsb0FilterIndex;
    [MarshalAs(UnmanagedType.Bool)]
    public bool Connected;
    public int SyncFlags;
    public int BusNumber;
    public int DeviceAddress;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string SerialNumber;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct WINUSB_SETUP_PACKET
{
    public byte RequestType;
    public byte Request;
    public ushort Value;
    public ushort Index;
    public ushort Length;
}

[StructLayout(LayoutKind.Sequential)]
public struct KUSB_DRIVER_API_INFO
{
    public int DriverID;
    public int FunctionCount;
}

[StructLayout(LayoutKind.Sequential, Size = 512)]
public struct KUSB_DRIVER_API
{
    public KUSB_DRIVER_API_INFO Info;
    public IntPtr Init;
    public IntPtr Free;
    public IntPtr ClaimInterface;
    public IntPtr ReleaseInterface;
    public IntPtr SetAltInterface;
    public IntPtr GetAltInterface;
    public IntPtr GetDescriptor;
    public IntPtr ControlTransfer;
}

[UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)]
public delegate bool KusbInitDelegate(out IntPtr InterfaceHandle, IntPtr DevInfo);

[UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)]
public delegate bool KusbFreeDelegate(IntPtr InterfaceHandle);

[UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)]
public delegate bool KusbControlTransferDelegate(
    IntPtr InterfaceHandle,
    WINUSB_SETUP_PACKET SetupPacket,
    byte[] Buffer,
    uint BufferLength,
    out uint LengthTransferred,
    IntPtr Overlapped);
