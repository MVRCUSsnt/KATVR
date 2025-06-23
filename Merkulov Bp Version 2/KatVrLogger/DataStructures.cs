
using System;
using System.IO;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Threading;

namespace Merkulov_Bp_Version_2.KatVrLogger 
{
    // Базовые структуры (Vector3 и Quaternion)
    [StructLayout(LayoutKind.Sequential)]
    public struct Vector3
    {
        public float x, y, z;
        public float Magnitude => (float)Math.Sqrt(x * x + y * y + z * z);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Quaternion
    {
        public float x, y, z, w;
    }

    // DeviceData и TreadMillData (пример, можно расширить если надо)
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct DeviceData
    {
        [MarshalAs(UnmanagedType.I1)]
        public bool btnPressed;
        [MarshalAs(UnmanagedType.I1)]
        public bool isBatteryCharging;
        public float batteryLevel;
        public byte firmwareVersion;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
    public struct TreadMillData
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string deviceName;
        [MarshalAs(UnmanagedType.I1)]
        public bool connected;
        public double lastUpdateTimePoint;
        public Quaternion bodyRotationRaw;
        public Vector3 moveSpeed; // (x, y, z)
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public DeviceData[] deviceDatas;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
        public byte[] extraData;
    }
}
