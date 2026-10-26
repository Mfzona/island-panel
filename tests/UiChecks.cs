using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Linq;
using System.Windows.Input;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using IslandPanel;

public static class UiChecks
{
    static T? Descendant<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T value) return value;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var found = Descendant<T>(VisualTreeHelper.GetChild(root, i)); if (found != null) return found; }
        return null;
    }
    static void CheckTooltip(MainWindow w, Action<bool, string> check, string? folder)
    {
        var tip = new ToolTip { Content = "Музыка\nE:\\Музыка", PlacementTarget = w };
        try
        {
            tip.IsOpen = true; Pump(150); tip.ApplyTemplate(); tip.UpdateLayout();
            var body = tip.Template.FindName("TipBody", tip) as Border;
            check(body?.Background is SolidColorBrush fill && fill.Color.R < 60 && fill.Color.G < 60 && fill.Color.B < 60 && body.CornerRadius.TopLeft > 0, "Actual tooltip popup uses the dark rounded template");
            var label = Descendant<TextBlock>(tip);
            check(label?.Foreground is SolidColorBrush ink && ink.Color.R > 180 && ink.Color.G > 180 && ink.Color.B > 180 && label.Text.Contains("Музыка"), "Tooltip text remains readable on its background");
            if (folder != null)
            {
                Directory.CreateDirectory(folder); var bmp = new RenderTargetBitmap((int)Math.Ceiling(tip.ActualWidth), (int)Math.Ceiling(tip.ActualHeight), 96, 96, PixelFormats.Pbgra32); bmp.Render(tip);
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp)); using var stream = File.Create(Path.Combine(folder, "tooltip.png")); enc.Save(stream);
            }
        }
        finally { tip.IsOpen = false; }
    }
    [StructLayout(LayoutKind.Sequential)] struct WindowPosition { public IntPtr Hwnd, After; public int X, Y, Width, Height; public uint Flags; }
    static void CheckTransitionPlacement(MainWindow w, Action<bool, string> check)
    {
        var frame = (Grid)w.FindName("IslandFrame");
        Call(w, "Collapse"); Pump(750);
        double expectedCenter = w.Left + w.Width / 2;
        var moves = new List<WindowPosition>();
        Point expectedFrame = frame.PointToScreen(new Point(frame.ActualWidth / 2, 0)); var renderedPositions = new List<Point>();
        EventHandler render = (_, _) => renderedPositions.Add(frame.PointToScreen(new Point(frame.ActualWidth / 2, 0)));
        HwndSourceHook hook = (IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled) => { if (msg == 0x0047) moves.Add(Marshal.PtrToStructure<WindowPosition>(lp)); return IntPtr.Zero; };
        var source = HwndSource.FromHwnd(new WindowInteropHelper(w).Handle); source.AddHook(hook);
        CompositionTarget.Rendering += render;
        try
        {
            Call(w, "Expand", false); Pump(750); Call(w, "Collapse"); Pump(750);
            double scale = VisualTreeHelper.GetDpi(w).DpiScaleX;
            var positions = moves.Where(p => (p.Flags & 3) != 3).ToArray();
            double drift = positions.Length == 0 ? 0 : positions.Max(p => Math.Abs((p.X + p.Width / 2d) / scale - expectedCenter));
            Console.WriteLine("Transition native placements: " + positions.Length + "; maximum center drift: " + drift.ToString("0.##") + " DIP");
            check(drift <= 1, "Native window stays centered throughout expand and collapse");
            check(renderedPositions.Count > 4 && renderedPositions.All(p => Math.Abs(p.X - expectedFrame.X) <= 1 && Math.Abs(p.Y - expectedFrame.Y) <= 1), "Rendered island keeps its screen anchor on every transition frame");
        }
        finally { source.RemoveHook(hook); CompositionTarget.Rendering -= render; }
        Call(w, "Expand", false); Pump(750);
    }
    static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer(DispatcherPriority.Send) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
    static void Call(MainWindow w, string method, params object[] values) => typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(w, values);
    static T Field<T>(MainWindow w, string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(w)!;
    static void Render(MainWindow w, string? folder, string name)
    {
        if (folder == null) return; Directory.CreateDirectory(folder); w.UpdateLayout();
        var bmp = new RenderTargetBitmap((int)w.ActualWidth, (int)w.ActualHeight, 96, 96, PixelFormats.Pbgra32); bmp.Render(w);
        var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp)); using var s = File.Create(Path.Combine(folder, name + ".png")); enc.Save(s);
    }
    public static void Run(Action<bool, string> check, string? artifacts)
    {
        var app = new App { WindowSuppliedByHost = true }; app.InitializeComponent(); var w = new MainWindow();
        var d = Field<Data>(w, "data"); d.Settings.CaptureClipboard = false; d.Settings.AutoCollapse = false; d.Settings.HoverExpand = false; d.Settings.City = "";
        d.Settings.StartCompact = true; d.Settings.Topmost = false; w.Topmost = false; int activations = 0; w.Activated += (_, _) => activations++;
        try
        {
            w.Show(); Pump(850);
            var handle = new WindowInteropHelper(w).Handle;
            check(!w.ShowInTaskbar && (Native.ExtendedStyle(handle) & Native.ToolWindow) != 0 && (Native.ExtendedStyle(handle) & Native.AppWindow) == 0, "Native tool window excluded from taskbar and Alt+Tab");
            Console.WriteLine("WPF render tier: " + (RenderCapability.Tier >> 16) + "; animations allowed: " + Motion.Allowed);
            var surface = (IslandSurface)w.FindName("Surface"); var eyes = (EyesControl)w.FindName("PillEyes"); var frame = (Grid)w.FindName("IslandFrame");
            check(Field<bool>(w, "compact") && w.Width == 260 && ((Grid)w.FindName("ExpandedLayer")).Visibility == Visibility.Collapsed, "Quiet startup presents only the compact island without an opening animation");
            check(activations == 0, "Startup never activates the panel or takes keyboard focus");
            check(!w.Topmost && (Native.ExtendedStyle(handle) & 0x8) == 0, "Panel has no native always-on-top style");
            Call(w, "Expand", false); Pump(750);
            CheckTooltip(w, check, artifacts);
            check(!surface.IsAnimating && !eyes.IsPolling, "No contour render loop or eye timer while expanded and idle");
            CheckTransitionPlacement(w, check);
            d.Settings.HoverExpand = true; Call(w, "Collapse");
            frame.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseEnterEvent });
            check(!Field<DispatcherTimer>(w, "hoverOpen").IsEnabled, "Pointer entry during collapse does not schedule another expansion");
            check(Math.Abs(((ScaleTransform)frame.RenderTransform).ScaleX - 1) < .001, "Hover cannot stretch the island during a transition");
            d.Settings.HoverExpand = false; Pump(750); Call(w, "Expand", false); Pump(750);
            foreach (string page in new[] { "Главная", "Буфер", "Файлы", "Текст", "Заметки", "Музыка", "Визуализация", "Микшер", "Погода", "Настройки", "Команда" })
            {
                Call(w, "ShowPage", page); Pump(280); w.UpdateLayout(); var host = (ContentControl)w.FindName("Page");
                check(host.Content is UIElement && host.ActualHeight > 100, "UI page loads: " + page);
                if (page == "Погода") check(Field<System.Collections.Generic.List<MixerSession>>(w, "mixerSessions").Count == 0, "Mixer releases audio sessions after leaving the page");
                if (page == "Главная" || page == "Файлы" || page == "Текст" || page == "Заметки" || page == "Микшер" || page == "Визуализация" || page == "Музыка") Render(w, artifacts, page);
            }
            Call(w, "ShowPage", "Визуализация"); Pump(500);
            var spectrum = Field<LoopbackSpectrum>(w, "spectrum"); check(spectrum.Requested && spectrum.IsRunning, "Visible visualization activates real output analysis");
            d.Settings.VisualizerStyle = "Волна"; Call(w, "ShowPage", "Визуализация"); Pump(300);
            check(Field<System.Collections.Generic.List<WeakReference<SpectrumView>>>(w, "spectrumViews").Any(r => r.TryGetTarget(out var v) && v.IsVisible && v.WaveMode && v.FramesDrawn > 0), "Wave mode renders real output samples");
            Render(w, artifacts, "Волна"); d.Settings.VisualizerStyle = "Спектр"; Call(w, "ShowPage", "Визуализация"); Pump(100);
            Call(w, "Collapse"); Pump(80);
            if (Motion.Allowed) check(frame.ActualWidth > 260 && frame.ActualWidth < 760, "Morph produces intermediate sizes instead of jumping");
            Call(w, "Expand", false); Pump(75); Call(w, "Collapse"); Pump(800);
            check(Math.Abs(frame.ActualWidth - 260) < 1 && Math.Abs(frame.ActualHeight - 48) < 1, "Rapidly reversed morph settles at correct compact size");
            check(w.Width == 260 && w.Height == 56, "Compact mode releases the large transparent native surface");
            check((Native.ExtendedStyle(handle) & Native.ToolWindow) != 0 && (Native.ExtendedStyle(handle) & Native.AppWindow) == 0, "Tool window styles survive expand/collapse transitions");
            check(!surface.IsAnimating && !Field<bool>(w, "morphing"), "All finite shape animations stop after settling");
            check(!spectrum.Requested && !spectrum.IsRunning, "Collapsing the island stops WASAPI analysis");
            check(Field<System.Collections.Generic.List<WeakReference<SpectrumView>>>(w, "spectrumViews").All(r => !r.TryGetTarget(out var v) || !v.IsRendering), "Hidden visualizers have no frame timers");
            check(((Grid)w.FindName("ExpandedLayer")).Visibility == Visibility.Collapsed, "Expanded controls removed from compact hit area");
            Render(w, artifacts, "compact");
            w.Hide(); Pump(50); check(!eyes.IsPolling, "Hidden window stops eye timer");
            Motion.Enabled = false; Call(w, "Expand", false); Call(w, "Collapse");
            check(Math.Abs(frame.Width - 260) < .1 && !surface.IsAnimating, "Disabled animations apply final state immediately");
            w.Close();
        }
        finally { if (w.IsLoaded) w.Close(); app.Shutdown(); }
    }
}
