using System;
using System.Runtime.InteropServices;

namespace NativeLookupAPI.Proxy;

public static class Kernel32
{
    [DllImport("kernel32.dll", EntryPoint = "GetProcAddress", SetLastError = true, CharSet = CharSet.Ansi)]
    internal static extern IntPtr GetExportedFunctionAddress(IntPtr hModule, string funcName);
}