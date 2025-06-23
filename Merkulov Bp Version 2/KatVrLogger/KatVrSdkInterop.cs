using System;
using System.Runtime.InteropServices;
using System.Reflection;

namespace Merkulov_Bp_Version_2.KatVrLogger;

public static class KatVrSdkInterop
{
    private const string SdkLib = "KATSDKWarpper.dll";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int DeviceCountDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate TreadMillData GetWalkStatusDelegate([MarshalAs(UnmanagedType.LPStr)] string sn);

    public static DeviceCountDelegate DeviceCount;
    public static GetWalkStatusDelegate GetWalkStatus;

    public static void LoadSdk()
    {
        IntPtr libHandle = LoadLibrary(SdkLib);
        if (libHandle == IntPtr.Zero)
            throw new Exception("Не удалось загрузить SDK DLL: " + SdkLib);

        DeviceCount = GetDelegate<DeviceCountDelegate>(libHandle, "DeviceCount");
        GetWalkStatus = GetDelegate<GetWalkStatusDelegate>(libHandle, "GetWalkStatus");
    }

    // DllImport для загрузки и получения функций (P/Invoke-объявления WinAPI)
    [DllImport("kernel32", SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32", SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

    private static T GetDelegate<T>(IntPtr dllHandle, string functionName) where T : Delegate
    {
        IntPtr funcPtr = GetProcAddress(dllHandle, functionName);
        if (funcPtr == IntPtr.Zero) throw new Exception($"Функция {functionName} не найдена в {SdkLib}!");
        return Marshal.GetDelegateForFunctionPointer<T>(funcPtr);
    }
}
