using System.Runtime.InteropServices;

namespace LightSourceMonitor.Drivers.Native;

internal static class LibUsbK
{
    private const string DllName = "libusbK.dll";

    [DllImport(DllName, SetLastError = true)]
    public static extern bool LstK_Init(out IntPtr DeviceList, int Flags);

    [DllImport(DllName, SetLastError = true)]
    public static extern bool LstK_Free(IntPtr DeviceList);

    [DllImport(DllName, SetLastError = true)]
    public static extern bool LstK_Count(IntPtr DeviceList, out uint Count);

    [DllImport(DllName, SetLastError = true)]
    public static extern bool LstK_MoveReset(IntPtr DeviceList);

    [DllImport(DllName, SetLastError = true)]
    public static extern bool LstK_MoveNext(IntPtr DeviceList, out IntPtr DeviceInfo);

    [DllImport(DllName, SetLastError = true)]
    public static extern bool LstK_FindByVidPid(IntPtr DeviceList, int Vid, int Pid, out IntPtr DeviceInfo);

    [DllImport(DllName, SetLastError = true)]
    public static extern bool LibK_LoadDriverAPI(out KUSB_DRIVER_API DriverAPI, int DriverID);
}
