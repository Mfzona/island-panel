using System;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Linq;

namespace IslandPanel;
public static class Native
{
    public const int WM_CLIPBOARDUPDATE = 0x031D, WM_HOTKEY = 0x0312;
    public static readonly uint ShowMessage = RegisterWindowMessage("IslandPanel.Show.v1");
    public static readonly uint CloseMessage = RegisterWindowMessage("IslandPanel.Close.v1");
    public const long ToolWindow = 0x00000080, AppWindow = 0x00040000;
    public static object CreateAudioEnumerator() => Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!)!;
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetWindowLong32(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] static extern int SetWindowLong32(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    public static void SetBounds(IntPtr hwnd, double left, double top, double width, double height, double scaleX, double scaleY)
        => SetWindowPos(hwnd, IntPtr.Zero, (int)Math.Round(left * scaleX), (int)Math.Round(top * scaleY), (int)Math.Round(width * scaleX), (int)Math.Round(height * scaleY), 0x14);
    public static long ExtendedStyle(IntPtr hwnd) => IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, -20).ToInt64() : GetWindowLong32(hwnd, -20);
    public static void MakeToolWindow(IntPtr hwnd)
    {
        long style = (ExtendedStyle(hwnd) | ToolWindow) & ~AppWindow;
        if (IntPtr.Size == 8) SetWindowLongPtr64(hwnd, -20, new IntPtr(style)); else SetWindowLong32(hwnd, -20, (int)style);
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x37); // Refresh style without moving, resizing, reordering or activating.
    }
    [DllImport("user32.dll")] public static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string text);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wparam, IntPtr lparam);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    delegate bool WindowVisitor(IntPtr hwnd, IntPtr state);
    [DllImport("user32.dll")] static extern bool EnumWindows(WindowVisitor visitor, IntPtr state);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    static void Signal(uint message)
    {
        // Broadcast messages skip some borderless tool windows. Address this app's
        // top-level handles directly so reopening and graceful exit also work collapsed.
        using var current = Process.GetCurrentProcess();
        var peers = Process.GetProcessesByName(current.ProcessName);
        try
        {
            var ids = peers.Where(p => p.Id != current.Id).Select(p => (uint)p.Id).ToHashSet();
            EnumWindows((handle, _) => { GetWindowThreadProcessId(handle, out uint id); if (ids.Contains(id)) PostMessage(handle, message, IntPtr.Zero, IntPtr.Zero); return true; }, IntPtr.Zero);
        }
        finally { foreach (var peer in peers) peer.Dispose(); }
    }
    public static void SignalExisting() => Signal(ShowMessage);
    public static void SignalClose() => Signal(CloseMessage);
}
[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IDevices
{
    int EnumAudioEndpoints(int flow, int mask, out IntPtr collection);
    int GetDefaultAudioEndpoint(int flow, int role, out IDevice device);
}
[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IDevice
{
    int Activate(ref Guid iid, int context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object obj);
}
[ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IVolume
{
    int RegisterControlChangeNotify(IntPtr ptr); int UnregisterControlChangeNotify(IntPtr ptr); int GetChannelCount(out uint count);
    int SetMasterVolumeLevel(float value, Guid context); int SetMasterVolumeLevelScalar(float value, Guid context);
    int GetMasterVolumeLevel(out float value); int GetMasterVolumeLevelScalar(out float value);
    int SetChannelVolumeLevel(uint c, float v, Guid ctx); int SetChannelVolumeLevelScalar(uint c, float v, Guid ctx);
    int GetChannelVolumeLevel(uint c, out float v); int GetChannelVolumeLevelScalar(uint c, out float v);
    int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, Guid context); int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
}
public static class Audio
{
    static T With<T>(Func<IVolume, T> action)
    {
        object enumerator = Native.CreateAudioEnumerator(); IDevice? dev = null; object? obj = null;
        try { Marshal.ThrowExceptionForHR(((IDevices)enumerator).GetDefaultAudioEndpoint(0, 1, out dev)); Guid iid = typeof(IVolume).GUID; Marshal.ThrowExceptionForHR(dev.Activate(ref iid, 23, IntPtr.Zero, out obj)); return action((IVolume)obj); }
        finally { if (obj != null) Marshal.ReleaseComObject(obj); if (dev != null) Marshal.ReleaseComObject(dev); Marshal.ReleaseComObject(enumerator); }
    }
    public static float Get() => With(v => { Marshal.ThrowExceptionForHR(v.GetMasterVolumeLevelScalar(out float n)); return n * 100; });
    public static bool Muted() => With(v => { Marshal.ThrowExceptionForHR(v.GetMute(out bool n)); return n; });
    public static void Set(float n) => With(v => { Marshal.ThrowExceptionForHR(v.SetMasterVolumeLevelScalar(Math.Clamp(n / 100, 0, 1), Guid.Empty)); return 0; });
    public static void ToggleMute() => With(v => { Marshal.ThrowExceptionForHR(v.GetMute(out bool b)); Marshal.ThrowExceptionForHR(v.SetMute(!b, Guid.Empty)); return 0; });
}
