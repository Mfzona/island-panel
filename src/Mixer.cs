using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IslandPanel;
[ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface ISessionManager
{
    [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, uint flags, out IntPtr control);
    [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, uint flags, out IntPtr volume);
    [PreserveSig] int GetSessionEnumerator(out ISessionEnumerator sessions);
}
[ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface ISessionEnumerator
{
    [PreserveSig] int GetCount(out int count);
    [PreserveSig] int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object control);
}
[ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface ISessionControl
{
    [PreserveSig] int GetState(out int state);
    [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
    [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid context);
    [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
    [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid context);
    [PreserveSig] int GetGroupingParam(out Guid group);
    [PreserveSig] int SetGroupingParam(ref Guid group, ref Guid context);
    [PreserveSig] int RegisterAudioSessionNotification(IntPtr events);
    [PreserveSig] int UnregisterAudioSessionNotification(IntPtr events);
    [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetProcessId(out uint pid);
    [PreserveSig] int IsSystemSoundsSession();
    [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
}
[ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface ISessionVolume
{
    [PreserveSig] int SetMasterVolume(float level, ref Guid context);
    [PreserveSig] int GetMasterVolume(out float level);
    [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
    [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
}
public sealed class MixerSession : IDisposable
{
    ISessionControl? control;
    ISessionVolume VolumeControl => (ISessionVolume)(object)(control ?? throw new ObjectDisposedException(nameof(MixerSession)));
    public string Id { get; }
    public string Name { get; }
    public uint ProcessId { get; }
    public bool Active { get { Marshal.ThrowExceptionForHR(control!.GetState(out int s)); return s == 1; } }
    public float Volume { get { Marshal.ThrowExceptionForHR(VolumeControl.GetMasterVolume(out float v)); return v * 100; } }
    public bool Muted { get { Marshal.ThrowExceptionForHR(VolumeControl.GetMute(out bool b)); return b; } }
    internal MixerSession(ISessionControl session)
    {
        control = session;
        Marshal.ThrowExceptionForHR(session.GetSessionInstanceIdentifier(out string id)); Id = id;
        session.GetProcessId(out uint pid); ProcessId = pid;
        session.GetDisplayName(out string label);
        if (session.IsSystemSoundsSession() == 0) Name = "Системные звуки";
        else
        {
            string name = string.IsNullOrWhiteSpace(label) || label.StartsWith("@") ? "Приложение " + pid : label;
            try { using var p = Process.GetProcessById((int)pid); name = p.MainModule?.FileVersionInfo.FileDescription ?? p.ProcessName; if (string.IsNullOrWhiteSpace(name)) name = p.ProcessName; } catch { }
            Name = name;
        }
    }
    public void SetVolume(float percent) { Guid context = Guid.Empty; Marshal.ThrowExceptionForHR(VolumeControl.SetMasterVolume(Math.Clamp(percent / 100, 0, 1), ref context)); }
    public void SetMuted(bool muted) { Guid context = Guid.Empty; Marshal.ThrowExceptionForHR(VolumeControl.SetMute(muted, ref context)); }
    public void Dispose() { if (control != null) { Marshal.ReleaseComObject(control); control = null; } }
}
public static class Mixer
{
    public static List<MixerSession> Sessions()
    {
        object enumerator = Native.CreateAudioEnumerator(); IDevice? device = null; object? manager = null; ISessionEnumerator? sessions = null;
        var result = new List<MixerSession>();
        try
        {
            Marshal.ThrowExceptionForHR(((IDevices)enumerator).GetDefaultAudioEndpoint(0, 1, out device));
            Guid iid = typeof(ISessionManager).GUID; Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23, IntPtr.Zero, out manager));
            Marshal.ThrowExceptionForHR(((ISessionManager)manager).GetSessionEnumerator(out sessions));
            Marshal.ThrowExceptionForHR(sessions.GetCount(out int count));
            for (int i = 0; i < count; i++)
            {
                object? raw = null;
                try { Marshal.ThrowExceptionForHR(sessions.GetSession(i, out raw)); var control = (ISessionControl)raw; Marshal.ThrowExceptionForHR(control.GetState(out int state)); if (state == 2) continue; var entry = new MixerSession(control); result.Add(entry); raw = null; }
                catch (COMException) { } // Applications can exit during enumeration.
                finally { if (raw != null) Marshal.ReleaseComObject(raw); }
            }
            return result;
        }
        catch { foreach (var s in result) s.Dispose(); throw; }
        finally { if (sessions != null) Marshal.ReleaseComObject(sessions); if (manager != null) Marshal.ReleaseComObject(manager); if (device != null) Marshal.ReleaseComObject(device); Marshal.ReleaseComObject(enumerator); }
    }
}
