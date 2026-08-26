using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace ReelForge.Gate0.WpfMeasurementAdapter;

internal static class HostEvidence
{
    public static HostCapture Capture(Window window, IntPtr hwnd)
    {
        var windows = new { productName = ReadRegistry(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName"), displayVersion = ReadRegistry(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion"), buildNumber = ReadRegistry(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuildNumber"), ubr = ReadRegistry(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "UBR") };
        var adapters = NativeWindow.EnumerateDisplayAdapters().ToArray();
        var displays = NativeWindow.EnumerateDisplays().ToArray();
        var power = ReadPowerScheme();
        NativeWindow.GetWindowRect(hwnd, out var rect);
        var required = windows.productName is not null && windows.displayVersion is not null && windows.buildNumber is not null && windows.ubr is not null && adapters.Length > 0 && adapters.All(adapter => adapter.DriverVersion is not null && adapter.DriverDate is not null && adapter.DriverProvider is not null) && displays.Length > 0 && displays.All(display => display.MonitorBounds is not null && display.MonitorWorkArea is not null) && power.ExitCode == 0 && hwnd != IntPtr.Zero;
        return new HostCapture(required, windows, adapters, power, new { virtualBounds = new { left = SystemParameters.VirtualScreenLeft, top = SystemParameters.VirtualScreenTop, width = SystemParameters.VirtualScreenWidth, height = SystemParameters.VirtualScreenHeight }, monitors = displays }, new { hwnd = hwnd.ToInt64(), wpfIsVisible = window.IsVisible, windowState = window.WindowState.ToString(), nativeVisible = hwnd != IntPtr.Zero && NativeWindow.IsWindowVisible(hwnd), dpi = hwnd == IntPtr.Zero ? 0u : NativeWindow.GetDpiForWindow(hwnd), bounds = new { left = rect.Left, top = rect.Top, right = rect.Right, bottom = rect.Bottom }, visibleAndUnminimizedThroughout = "recorded-in-process-samples" });
    }

    public static HostCapture Fallback(string reason) => new(false, new { error = reason }, [], new PowerEvidence([], -1, string.Empty, string.Empty), new { error = reason }, new { error = reason });

    private static string? ReadRegistry(string path, string value) => Registry.GetValue($@"HKEY_LOCAL_MACHINE\{path}", value, null)?.ToString();

    private static PowerEvidence ReadPowerScheme()
    {
        var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "powercfg.exe");
        using var process = Process.Start(new ProcessStartInfo(executable, "/getactivescheme") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true });
        process!.WaitForExit();
        return new PowerEvidence(new[] { executable, "/getactivescheme" }, process.ExitCode, process.StandardOutput.ReadToEnd(), process.StandardError.ReadToEnd());
    }
}

internal sealed record HostCapture(bool RequiredFieldsAvailable, object Windows, IReadOnlyList<NativeWindow.AdapterRecord> GpuAndDriver, object Power, object Display, object Window);
internal sealed record PowerEvidence(IReadOnlyList<string> Command, int ExitCode, string Stdout, string Stderr);

internal static class NativeWindow
{
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr handle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetWindowRect(IntPtr handle, out Rect rect);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint desiredAccess);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumDisplayDevices(string? device, uint index, ref DisplayDevice displayDevice, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DevMode devMode);
    internal static bool IsInteractiveDesktop()
    {
        var desktop = OpenInputDesktop(0, false, 0x0001);
        if (desktop == IntPtr.Zero) return false;
        return CloseDesktop(desktop);
    }
    internal static IEnumerable<AdapterRecord> EnumerateDisplayAdapters()
    {
        for (uint index = 0; ; index++)
        {
            var device = DisplayDevice.Create();
            if (!EnumDisplayDevices(null, index, ref device, 0)) yield break;
            var driver = DriverValues(device.DeviceKey);
            yield return new AdapterRecord(device.DeviceName, device.DeviceString, device.DeviceKey, device.StateFlags.ToString(System.Globalization.CultureInfo.InvariantCulture), driver.Version, driver.Date, driver.Provider);
        }
    }
    internal static IEnumerable<DisplayRecord> EnumerateDisplays()
    {
        var monitors = EnumerateMonitors().ToDictionary(monitor => monitor.DeviceName, StringComparer.OrdinalIgnoreCase);
        foreach (var adapter in EnumerateDisplayAdapters())
        {
            var mode = DevMode.Create();
            if (EnumDisplaySettings(adapter.DeviceName, -1, ref mode))
            {
                monitors.TryGetValue(adapter.DeviceName, out var monitor);
                yield return new DisplayRecord(adapter.DeviceName, monitor?.Primary ?? false, monitor?.Bounds, monitor?.WorkArea, (int)mode.PelsWidth, (int)mode.PelsHeight, (int)mode.DisplayFrequency, (int)mode.DisplayOrientation, adapter.StateFlags);
            }
        }
    }
    private static (string? Version, string? Date, string? Provider) DriverValues(string deviceKey)
    {
        const string prefix = "\\Registry\\Machine\\";
        if (!deviceKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return (null, null, null);
        using var key = Registry.LocalMachine.OpenSubKey(deviceKey[prefix.Length..]);
        return (key?.GetValue("DriverVersion")?.ToString(), key?.GetValue("DriverDate")?.ToString(), key?.GetValue("ProviderName")?.ToString());
    }
    private static List<MonitorRecord> EnumerateMonitors()
    {
        var monitors = new List<MonitorRecord>();
        MonitorEnumProc callback = (handle, _, _, _) =>
        {
            var info = MonitorInfoEx.Create();
            if (GetMonitorInfo(handle, ref info)) monitors.Add(new MonitorRecord(info.Device, (info.Flags & 1) != 0, new BoundsRecord(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom), new BoundsRecord(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom)));
            return true;
        };
        _ = EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        return monitors;
    }
    internal readonly record struct AdapterRecord(string DeviceName, string DeviceString, string DeviceRegistryIdentity, string StateFlags, string? DriverVersion, string? DriverDate, string? DriverProvider);
    internal sealed record DisplayRecord(string DeviceName, bool Primary, BoundsRecord? MonitorBounds, BoundsRecord? MonitorWorkArea, int Width, int Height, int Frequency, int Orientation, string State);
    internal sealed record MonitorRecord(string DeviceName, bool Primary, BoundsRecord Bounds, BoundsRecord WorkArea);
    internal sealed record BoundsRecord(int Left, int Top, int Right, int Bottom);
    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct MonitorInfoEx { public int Size; public Rect Monitor; public Rect Work; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device; public static MonitorInfoEx Create() => new() { Size = Marshal.SizeOf<MonitorInfoEx>(), Device = string.Empty }; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct DisplayDevice
    {
        public int Size; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString; public int StateFlags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        public static DisplayDevice Create() => new() { Size = Marshal.SizeOf<DisplayDevice>(), DeviceName = string.Empty, DeviceString = string.Empty, DeviceId = string.Empty, DeviceKey = string.Empty };
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left; public int Top; public int Right; public int Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName; public short SpecVersion; public short DriverVersion; public short Size; public short DriverExtra; public int Fields; public short Orientation; public short PaperSize; public short PaperLength; public short PaperWidth; public short Scale; public short Copies; public short DefaultSource; public short PrintQuality; public short Color; public short Duplex; public short YResolution; public short TTOption; public short Collate; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName; public short LogPixels; public int BitsPerPel; public int PelsWidth; public int PelsHeight; public int DisplayFlags; public int DisplayFrequency; public int IcmMethod; public int IcmIntent; public int MediaType; public int DitherType; public int Reserved1; public int Reserved2; public int PanningWidth; public int PanningHeight; public short DisplayOrientation;
        public static DevMode Create() => new() { DeviceName = string.Empty, FormName = string.Empty, Size = (short)Marshal.SizeOf<DevMode>() };
    }
}
