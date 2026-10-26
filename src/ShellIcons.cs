using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace IslandPanel;
public static class ShellIcons
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct Info
    {
        public IntPtr Icon; public int IconIndex; public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SHGetFileInfo(string path, uint attributes, out Info info, uint cb, uint flags);
    [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath(ref Guid folder, uint flags, IntPtr token, out IntPtr path);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    static readonly Dictionary<string, Task<BitmapSource?>> cache = new(StringComparer.OrdinalIgnoreCase);
    public static string Downloads
    {
        get { Guid id = new("374DE290-123F-4565-9164-39C4925E467B"); IntPtr p = IntPtr.Zero; try { if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out p) == 0) return Marshal.PtrToStringUni(p)!; } finally { if (p != IntPtr.Zero) Marshal.FreeCoTaskMem(p); } return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"); }
    }
    public static Task<BitmapSource?> Get(string path)
    {
        lock (cache)
        {
            if (cache.TryGetValue(path, out var pending)) return pending;
            if (cache.Count > 128) cache.Clear();
            return cache[path] = Task.Run(() =>
            {
                Info info = default;
                try { if (SHGetFileInfo(path, 0, out info, (uint)Marshal.SizeOf<Info>(), 0x100) == IntPtr.Zero || info.Icon == IntPtr.Zero) return null; var bmp = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); bmp.Freeze(); return bmp; }
                catch { return null; }
                finally { if (info.Icon != IntPtr.Zero) DestroyIcon(info.Icon); }
            });
        }
    }
    public static bool SeedDock(Data data)
    {
        if (data.DockInitialized) return false; data.DockInitialized = true;
        if (data.Files.Count != 0) return true;
        string system = Environment.GetFolderPath(Environment.SpecialFolder.System), windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        foreach (string path in new[] { Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Downloads, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Path.Combine(system, "notepad.exe"), Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe"), Path.Combine(windows, "explorer.exe") })
            if ((Directory.Exists(path) || File.Exists(path)) && !data.Files.Contains(path)) data.Files.Add(path);
        return true;
    }
    public static void MovePin(List<string> paths, string source, string target)
    {
        int from = paths.IndexOf(source), to = paths.IndexOf(target); if (from < 0 || to < 0 || from == to) return; paths.RemoveAt(from); paths.Insert(to, source);
    }
}
