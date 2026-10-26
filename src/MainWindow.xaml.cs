using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using QRCoder;
using Windows.Media.Control;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using WinForms = System.Windows.Forms;
using BitmapFrame = System.Windows.Media.Imaging.BitmapFrame;
using BitmapDecoder = Windows.Graphics.Imaging.BitmapDecoder;

namespace IslandPanel;
public partial class MainWindow : Window
{
    readonly Data data = Store.Load();
    readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer delayedSave = new() { Interval = TimeSpan.FromMilliseconds(450) };
    readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };
    readonly List<(string Title, string Detail, DateTime Time)> events = new();
    readonly Dictionary<string, Button> tabButtons = new();
    readonly Dictionary<string, (long Size, DateTime At)> downloads = new();
    readonly List<string> appShortcuts = new();
    readonly DispatcherTimer hoverOpen = new() { Interval = TimeSpan.FromMilliseconds(280) };
    readonly DispatcherTimer hoverClose = new() { Interval = TimeSpan.FromMilliseconds(450) };
    IntPtr hwnd;
    WinForms.NotifyIcon? tray;
    GlobalSystemMediaTransportControlsSessionManager? mediaManager;
    GlobalSystemMediaTransportControlsSession? media;
    TextBlock? trackTitle, trackArtist, trackTime, timerText, timerHint, weatherText, homeClock;
    System.Windows.Controls.Image? album;
    Slider? mediaProgress;
    TextBox? editor;
    int activeNote, seconds;
    bool compact, pinned, mediaBusy, seeking, closing, hotkeyOk;
    bool morphing, hoverExpanded;
    int morphVersion;
    double expandedWidth = 760, expandedHeight = 450;
    DateTime lastDownloadsCheck = DateTime.MinValue, lastWeatherCheck = DateTime.MinValue;
    string active = "Главная", weatherCompact = "", weatherDetail = "Укажите город в настройках", latestTrack = "";
    Process? recorder;
    string? recordingFile;
    DateTime recordingStart;
    DateTime noticeUntil;
    string noticeCaption = "";
    bool homeDirty, clipboardDirty, mediaPending;
    string cachedArtist = "", cachedAlbum = "", coverKey = "";
    BitmapSource? cachedCover;
    public MainWindow()
    {
        InitializeComponent();
        Motion.Enabled = data.Settings.Animations; Motion.LowPower = data.Settings.LowPower;
        PinButton.Content = Icons.Create("pin", 14); MinimizeButton.Content = Icons.Create("minus", 14);
        PinButton.BorderBrush = MinimizeButton.BorderBrush = Brushes.Transparent;
        ShowInTaskbar = false;
        if (ShellIcons.SeedDock(data)) SaveSoon();
        if (data.Notes.Count == 0) data.Notes.Add(new());
        foreach (string folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu) })
        { try { appShortcuts.AddRange(Directory.EnumerateFiles(folder, "*.lnk", SearchOption.AllDirectories)); } catch { } }
        Topmost = data.Settings.Topmost;
        foreach (var (name, icon) in new[] { ("Главная", "home"), ("Буфер", "clipboard"), ("Файлы", "files"), ("Текст", "text"), ("Заметки", "note"), ("Музыка", "music"), ("Визуализация", "spectrum"), ("Микшер", "mixer"), ("Погода", "weather"), ("Настройки", "settings") })
        {
            var b = Btn("", () => ShowPage(name)); b.Content = Icons.Create(icon, 15); b.ToolTip = name; b.Tag = icon; b.Padding = new Thickness(11, 8, 11, 8); b.Background = Brushes.Transparent;
            b.BorderBrush = Brushes.Transparent;
            System.Windows.Automation.AutomationProperties.SetName(b, name);
            tabButtons[name] = b; Tabs.Children.Add(b);
        }
        SourceInitialized += (_, _) => InitNative();
        Loaded += async (_, _) =>
        {
            compact = data.Settings.StartCompact;
            Compact.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
            ExpandedLayer.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            UpdatePill(); Position(); Navigate("Главная", false);
            await RefreshMedia(); if (!string.IsNullOrWhiteSpace(data.Settings.City)) await LoadWeather();
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Collapse(); e.Handled = true; } };
        Deactivated += (_, _) => { if (data.Settings.AutoCollapse && !pinned && recorder == null && OwnedWindows.Count == 0) Collapse(); };
        Activated += (_, _) => { hoverExpanded = false; hoverClose.Stop(); };
        hoverOpen.Tick += (_, _) => { hoverOpen.Stop(); if (compact && !morphing && IslandFrame.IsMouseOver && data.Settings.HoverExpand) { hoverExpanded = true; Expand(false); } };
        hoverClose.Tick += (_, _) => { hoverClose.Stop(); if (hoverExpanded && !IslandFrame.IsMouseOver && !pinned && OwnedWindows.Count == 0) Collapse(); };
        IslandFrame.MouseEnter += (_, _) => { hoverClose.Stop(); if (compact && !morphing && data.Settings.HoverExpand) hoverOpen.Start(); };
        IslandFrame.MouseLeave += (_, _) => { hoverOpen.Stop(); Motion.Animate(IslandScale, ScaleTransform.ScaleXProperty, 1, 220); Motion.Animate(IslandScale, ScaleTransform.ScaleYProperty, 1, 220); if (hoverExpanded && !pinned) hoverClose.Start(); };
        Drop += OnDrop;
        DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None; e.Handled = true; };
        delayedSave.Tick += (_, _) => { delayedSave.Stop(); Store.Save(data); };
        tick.Tick += async (_, _) => await Tick(); tick.Start();
        Closing += (_, _) => ReleaseMixer();
        Closing += (_, _) => spectrum.Dispose();
        IsVisibleChanged += (_, _) => SyncVisualization();
        Closing += (_, _) => { closing = true; tick.Stop(); hoverOpen.Stop(); hoverClose.Stop(); Surface.Stop(); UnbindMedia(); Store.Save(data); Native.RemoveClipboardFormatListener(hwnd); Native.UnregisterHotKey(hwnd, 1); Native.UnregisterHotKey(hwnd, 2); tray?.Dispose(); http.Dispose(); if (recorder != null) { try { recorder.StandardInput.WriteLine("q"); recorder.WaitForExit(5000); } catch { } } };
    }
    static readonly Dictionary<string, Brush> brushes = new();
    static Brush Brush(string hex) { if (!brushes.TryGetValue(hex, out var b)) { b = (Brush)new BrushConverter().ConvertFromString(hex)!; b.Freeze(); brushes[hex] = b; } return b; }
    static TextBlock Text(string value, double size = 13, string? color = null)
        => new() { Text = value, FontSize = size, Foreground = Brush(color ?? "#EEEEF3"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    static Button Btn(string value, Action action)
    {
        var b = new Button { Content = value }; b.Click += (_, _) => action(); return b;
    }
    static StackPanel Stack(params UIElement[] children) { var s = new StackPanel(); foreach (var c in children) s.Children.Add(c); return s; }
    static StackPanel Row(params UIElement[] children) { var s = Stack(children); s.Orientation = Orientation.Horizontal; return s; }
    static Border Card(UIElement child, string color = "#111113", int padding = 14)
        => new() { Background = Brush(color), BorderBrush = Brush("#14FFFFFF"), BorderThickness = new Thickness(.7), CornerRadius = new CornerRadius(16), Padding = new Thickness(padding), Margin = new Thickness(3), Child = child };
    static Grid Columns(UIElement left, UIElement right, double ratio = 1.6)
    {
        var g = new Grid(); g.ColumnDefinitions.Add(new() { Width = new GridLength(ratio, GridUnitType.Star) }); g.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(right, 1); g.Children.Add(left); g.Children.Add(right); return g;
    }
    static ScrollViewer Scroll(UIElement child) => new() { Content = child, Style = (Style)System.Windows.Application.Current.FindResource("PanelScroll") };
    static TextBox Input(string text = "", string hint = "") => new() { Text = text, ToolTip = hint, Margin = new Thickness(3), MinHeight = 38 };
    static TextBlock Heading(string value) { var t = Text(value, 10, "#9B97A8"); t.Margin = new Thickness(0, 0, 0, 10); return t; }
    void SaveSoon() { delayedSave.Stop(); delayedSave.Start(); }
    void InitNative()
    {
        hwnd = new WindowInteropHelper(this).Handle;
        Native.MakeToolWindow(hwnd);
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        Native.AddClipboardFormatListener(hwnd);
        hotkeyOk = Native.RegisterHotKey(hwnd, 1, 0x4003, 0x20); // Ctrl+Alt+Space, no repeat
        Native.RegisterHotKey(hwnd, 2, 0x4003, 0x51); // Ctrl+Alt+Q
        tray = new WinForms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = "Island Panel · Ctrl+Alt+Space", Visible = true };
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Открыть панель", null, (_, _) => Expand());
        menu.Items.Add("Быстрая команда", null, (_, _) => ShowPage("Команда"));
        menu.Items.Add("Настройки", null, (_, _) => ShowPage("Настройки"));
        menu.Items.Add("Выход", null, (_, _) => Close()); tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => Expand();
    }
    IntPtr WndProc(IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled)
    {
        if ((uint)msg == Native.ShowMessage) { Expand(); handled = true; }
        if ((uint)msg == Native.CloseMessage) { Close(); handled = true; }
        if (msg == Native.WM_HOTKEY) { if (wp.ToInt32() == 2) ShowPage("Команда"); else if (compact) Expand(); else Collapse(); handled = true; }
        if (msg == Native.WM_CLIPBOARDUPDATE && data.Settings.CaptureClipboard) Dispatcher.BeginInvoke(new Action(CaptureClipboard), DispatcherPriority.Background);
        return IntPtr.Zero;
    }
    void Position(bool settleCompact = false)
    {
        var screens = WinForms.Screen.AllScreens;
        var s = screens[Math.Clamp(data.Settings.Monitor, 0, screens.Length - 1)];
        var dpi = VisualTreeHelper.GetDpi(this);
        expandedWidth = Math.Min(760, s.WorkingArea.Width / dpi.DpiScaleX - 24);
        expandedHeight = Math.Min(450, s.WorkingArea.Height / dpi.DpiScaleY - data.Settings.Offset - 18);
        bool small = compact && (!morphing || settleCompact);
        double width = small ? 260 : expandedWidth;
        double height = small ? 56 : expandedHeight + 8;
        double left = s.Bounds.Left / dpi.DpiScaleX + (s.Bounds.Width / dpi.DpiScaleX - width) / 2;
        double top = s.Bounds.Top / dpi.DpiScaleY + data.Settings.Offset;
        // WPF setters resize and move the native layered window separately, briefly
        // shifting the island by half the width difference. Commit all bounds at once.
        Native.SetBounds(hwnd, left, top, width, height, dpi.DpiScaleX, dpi.DpiScaleY);
        ExpandedLayer.Width = expandedWidth - 32; ExpandedLayer.Height = expandedHeight - 18;
        Surface.Docked = data.Settings.Offset == 0;
        if (!morphing || settleCompact) { IslandFrame.BeginAnimation(WidthProperty, null); IslandFrame.BeginAnimation(HeightProperty, null); IslandFrame.Width = compact ? 260 : expandedWidth; IslandFrame.Height = compact ? 48 : expandedHeight; }
        UpdateLayout();
    }
    void Expand(bool focus = true)
    {
        bool changed = compact; compact = false; hoverOpen.Stop(); hoverClose.Stop();
        if (focus) hoverExpanded = false;
        if (changed) morphing = true; Position();
        Show(); if (focus) { Activate(); Native.SetForegroundWindow(hwnd); }
        StopRecButton.Visibility = recorder == null ? Visibility.Collapsed : Visibility.Visible;
        if (active == "Главная" && homeDirty) { homeDirty = false; Page.Content = HomePage(); }
        if (active == "Буфер" && clipboardDirty) { clipboardDirty = false; Page.Content = ClipboardPage(); }
        SyncVisualization();
        if (!changed) { ExpandedLayer.Visibility = Visibility.Visible; return; }
        int version = ++morphVersion;
        ExpandedLayer.Visibility = Visibility.Visible; ExpandedLayer.Opacity = 0;
        Motion.Animate(Compact, OpacityProperty, 0, 75, () => { if (version == morphVersion) Compact.Visibility = Visibility.Collapsed; });
        Motion.Animate(IslandScale, ScaleTransform.ScaleXProperty, 1, 160); Motion.Animate(IslandScale, ScaleTransform.ScaleYProperty, 1, 160);
        Motion.Animate(IslandFrame, WidthProperty, expandedWidth, 310);
        Motion.Animate(IslandFrame, HeightProperty, expandedHeight, 330, () => { if (version == morphVersion) morphing = false; });
        Motion.Animate(ExpandedLayer, OpacityProperty, 1, 180, null, 85);
        Surface.Pulse(4.2);
    }
    void Collapse()
    {
        if (closing || compact) return;
        compact = true; hoverExpanded = false; hoverOpen.Stop(); hoverClose.Stop(); morphing = true;
        SyncVisualization();
        int version = ++morphVersion;
        Compact.Visibility = Visibility.Visible; Compact.Opacity = 0; UpdatePill();
        Motion.Animate(ExpandedLayer, OpacityProperty, 0, 90, () => { if (version == morphVersion) ExpandedLayer.Visibility = Visibility.Collapsed; });
        Motion.Animate(IslandFrame, WidthProperty, 260, 290);
        Motion.Animate(IslandFrame, HeightProperty, 48, 310, () => { if (version == morphVersion) { Position(true); morphing = false; } });
        Motion.Animate(Compact, OpacityProperty, 1, 140, null, 160); Surface.Pulse(3.5);
    }
    void CompactClick(object s, MouseButtonEventArgs e) => Expand();
    void CollapseClick(object s, RoutedEventArgs e) => Collapse();
    void StopRecordingClick(object s, RoutedEventArgs e) => _ = ToggleRecording();
    void PinClick(object s, RoutedEventArgs e) { pinned = !pinned; PinButton.Background = pinned ? Brush("#302838") : Brushes.Transparent; Toast(pinned ? "Панель закреплена" : "Автосворачивание включено"); }
    public void Toast(string message) { if (closing) return; Status.Text = message; }
    void Notify(string title, string detail)
    {
        events.Insert(0, (title, detail, DateTime.Now)); if (events.Count > 50) events.RemoveAt(events.Count - 1);
        tray?.ShowBalloonTip(6000, title, detail, WinForms.ToolTipIcon.Info); Toast(title + " · " + detail);
        noticeUntil = DateTime.Now.AddSeconds(8); noticeCaption = title;
        PillIcon.Text = "●"; PillInfo.Text = title;
    }
    void ShowPage(string name)
        => Navigate(name, true);
    void Navigate(string name, bool reveal)
    {
        if (name != "Микшер") ReleaseMixer();
        active = name; if (reveal) Expand();
        foreach (var (n, b) in tabButtons) { b.Background = n == name ? Brush("#242326") : Brushes.Transparent; var icon = Icons.Create((string)b.Tag, 15, Brush(n == name ? "#F1EDF5" : "#86818E")); b.Content = n == name ? Row(icon, new TextBlock { Text = n, FontSize = 12, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }) : icon; }
        Page.Content = name switch { "Буфер" => ClipboardPage(), "Файлы" => FilesPage(), "Текст" => TextPage(), "Заметки" => NotesPage(), "Музыка" => MusicPage(), "Визуализация" => VisualizationPage(), "Микшер" => MixerPage(), "Погода" => WeatherPage(), "Настройки" => SettingsPage(), "Команда" => CommandPage(), "События" => EventsPage(), _ => HomePage() };
        SyncVisualization();
        Page.BeginAnimation(OpacityProperty, null); Page.Opacity = Motion.Allowed ? .15 : 1; PageTranslation.BeginAnimation(TranslateTransform.YProperty, null); PageTranslation.Y = Motion.Allowed ? 9 : 0;
        Motion.Animate(Page, OpacityProperty, 1, 170); Motion.Animate(PageTranslation, TranslateTransform.YProperty, 0, 230);
        Toast(name == "Главная" ? "Ваш маленький центр управления" : name);
    }
    UIElement HomePage()
    {
        var clips = new StackPanel(); clips.Children.Add(Heading("НЕДАВНО СКОПИРОВАНО"));
        foreach (var c in data.Clips.OrderByDescending(c => c.Pinned).ThenByDescending(c => c.At).Take(3)) clips.Children.Add(ClipRow(c, false));
        if (data.Clips.Count == 0) clips.Children.Add(Text("Скопируйте текст или изображение.\nИстория появится здесь.", 13, "#96929F"));
        var volume = new Slider { Minimum = 0, Maximum = 100, Margin = new Thickness(12, 4, 8, 4), Width = 245, ToolTip = "Системная громкость" };
        try { volume.Value = Audio.Get(); } catch { volume.IsEnabled = false; }
        volume.ValueChanged += (_, _) => { try { Audio.Set((float)volume.Value); } catch (Exception e) { Toast("Аудиоустройство недоступно"); Store.Log(e); } };
        var mute = Btn("", () => { try { Audio.ToggleMute(); } catch { Toast("Аудиоустройство недоступно"); } }); mute.Content = Icons.Create("volume", 15); mute.ToolTip = "Выключить звук";
        var mixerButton = Btn("", () => ShowPage("Микшер")); mixerButton.Content = Icons.Create("mixer", 15); mixerButton.ToolTip = "Микшер приложений";
        System.Windows.Automation.AutomationProperties.SetName(mixerButton, "Микшер приложений");
        var v = Card(Row(mute, volume, mixerButton), "#111113", 3); v.Height = 45;
        var clock = Text(DateTime.Now.ToString("HH:mm"), 38); clock.FontWeight = FontWeights.Light; homeClock = clock;
        var date = Text(DateTime.Now.ToString("dddd\nd MMMM", CultureInfo.GetCultureInfo("ru-RU")), 12, "#96929F"); date.Margin = new Thickness(18, 0, 0, 0);
        var clockCard = Card(Row(clock, date), "#0F0F11", 10); clockCard.Height = 72;
        var left = string.IsNullOrWhiteSpace(latestTrack) ? Stack(clockCard, Card(clips, "#111113", 8), v) : Stack(HomeMedia(), v);
        var tools = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
        foreach (var (icon, label, action) in new (string, string, Action)[] {
            ("area", "Область", () => _ = Capture("area")), ("screen", "Весь экран", () => _ = Capture("screen")),
            ("ocr", "Текст с экрана", () => _ = Capture("ocr")), ("qr", "QR-код", ShowQr),
            ("picker", "Пипетка", () => _ = Capture("color")), ("clean", "Чистый текст", CleanClipboard),
            ("record", "Запись экрана", () => _ = ToggleRecording()), ("search", "Команда", () => ShowPage("Команда")) })
        { var b = Btn("", action); var t = Text(label, 12); t.Margin = new Thickness(9, 0, 0, 0); b.Content = Row(Icons.Create(icon, 17, Brush("#96909E")), t); b.Padding = new Thickness(9, 8, 9, 8); System.Windows.Automation.AutomationProperties.SetName(b, label); tools.Children.Add(b); }
        var body = new Grid(); body.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        body.Children.Add(Columns(left, tools, 1.5));
        var shelf = DockShelf();
        Grid.SetRow(shelf, 1); body.Children.Add(shelf); return body;
    }
    void CaptureClipboard()
    {
        if (!data.Settings.CaptureClipboard) return;
        try
        {
            Clip? c = null;
            if (System.Windows.Clipboard.ContainsText())
            {
                string text = System.Windows.Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(text) || text.Length > 1_000_000 || data.Clips.Any(x => x.Image == null && x.Text == text)) return;
                c = new() { Text = text };
            }
            else if (System.Windows.Clipboard.ContainsImage())
            {
                var img = System.Windows.Clipboard.GetImage(); if (img == null) return;
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(img)); using var bytes = new MemoryStream(); enc.Save(bytes);
                byte[] contents = bytes.ToArray(); string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(contents));
                string file = Path.Combine(Store.Images, hash + ".png");
                if (data.Clips.Any(x => x.Image == file)) return;
                File.WriteAllBytes(file, contents);
                c = new() { Image = file, Text = $"Изображение {img.PixelWidth} × {img.PixelHeight}" };
            }
            if (c == null) return;
            data.Clips.Insert(0, c);
            while (data.Clips.Count(x => !x.Pinned) > 100) { var old = data.Clips.Last(x => !x.Pinned); RemoveClip(old); }
            SaveSoon(); if (active == "Буфер") { if (compact) clipboardDirty = true; else Page.Content = ClipboardPage(); } if (active == "Главная") { if (compact) homeDirty = true; else { Page.Content = HomePage(); SyncVisualization(); } }
        }
        catch (System.Runtime.InteropServices.COMException) { } // Clipboard briefly owned by another process.
        catch (Exception e) { Store.Log(e); }
    }
    UIElement HomeMedia() => PlayerCard(false);
    void RemoveClip(Clip c)
    {
        data.Clips.Remove(c);
        if (c.Image != null && c.Image.StartsWith(Store.Images + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) { try { File.Delete(c.Image); } catch { } }
    }
    void CopyClip(Clip c)
    {
        try { if (c.Image != null) System.Windows.Clipboard.SetImage(LoadImage(c.Image)); else System.Windows.Clipboard.SetText(c.Text); Toast("Скопировано — вставьте через Ctrl+V"); }
        catch (Exception e) { Store.Log(e); Toast("Не удалось скопировать"); }
    }
    UIElement ClipRow(Clip c, bool full)
    {
        var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        g.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); g.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var t = Text((c.Pinned ? "◆  " : c.Image == null ? "T   " : "▣  ") + c.Text.Replace("\r", " ").Replace("\n", " "), 12);
        t.TextWrapping = TextWrapping.NoWrap; t.TextTrimming = TextTrimming.CharacterEllipsis;
        var copy = Btn("", () => CopyClip(c)); copy.Content = t; copy.HorizontalContentAlignment = HorizontalAlignment.Stretch; copy.ToolTip = c.Image == null ? c.Text[..Math.Min(c.Text.Length, 1500)] : c.Text; copy.Background = Brush("#19181C");
        if (!full) copy.Padding = new Thickness(8, 6, 8, 6);
        g.Children.Add(copy);
        if (full) { var actions = Row(Btn(c.Pinned ? "◆" : "◇", () => { c.Pinned = !c.Pinned; SaveSoon(); Page.Content = ClipboardPage(); }), Btn("×", () => { RemoveClip(c); SaveSoon(); Page.Content = ClipboardPage(); })); Grid.SetColumn(actions, 1); g.Children.Add(actions); }
        return g;
    }
    UIElement ClipboardPage()
    {
        var root = new DockPanel(); var search = Input("", "Поиск по истории"); search.SetCurrentValue(TextBox.TextProperty, "");
        var list = new StackPanel(); void Fill() { list.Children.Clear(); foreach (var c in data.Clips.OrderByDescending(c => c.Pinned).ThenByDescending(c => c.At).Where(c => c.Text.Contains(search.Text, StringComparison.OrdinalIgnoreCase))) list.Children.Add(ClipRow(c, true)); if (list.Children.Count == 0) list.Children.Add(Card(Text("История пуста. Скопируйте текст или картинку в любом приложении.", 13, "#A39CAB"))); }
        search.TextChanged += (_, _) => Fill();
        var head = new DockPanel(); var clear = Btn("Очистить", () => { if (System.Windows.MessageBox.Show(this, "Очистить незакреплённую историю?", "Буфер обмена", MessageBoxButton.YesNo) == MessageBoxResult.Yes) { foreach (var c in data.Clips.Where(c => !c.Pinned).ToList()) RemoveClip(c); SaveSoon(); Fill(); } }); DockPanel.SetDock(clear, Dock.Right); head.Children.Add(clear); head.Children.Add(search);
        DockPanel.SetDock(head, Dock.Top); root.Children.Add(head); var help = Text("Поиск по истории  ·  Нажмите запись, чтобы скопировать  ·  ◇ — закрепить", 11, "#96929F"); help.Margin = new Thickness(8); DockPanel.SetDock(help, Dock.Bottom); root.Children.Add(help); root.Children.Add(Scroll(list)); Fill(); return root;
    }
    UIElement FilesPage()
    {
        var list = new StackPanel(); list.Children.Add(Heading("ПОЛКА ФАЙЛОВ"));
        list.Children.Add(Text("Оригиналы остаются на месте. Перетаскивайте файлы на панель.", 12, "#96929F"));
        list.Children.Add(Row(Btn("＋ Файл", AddFiles), Btn("＋ Папка", AddDockFolder), Btn("↓ Загрузки", () => OpenPath(ShellIcons.Downloads)), Btn("▣ Снимки и записи", () => OpenPath(Store.Captures))));
        var gallery = new WrapPanel(); foreach (var path in data.Files.ToList()) gallery.Children.Add(FileTile(path)); list.Children.Add(gallery);
        list.Children.Add(Heading("ТЕКУЩИЕ ЗАГРУЗКИ"));
        var pending = FindDownloads();
        if (pending.Length == 0) list.Children.Add(Text("Нет активных .crdownload или .part в папке «Загрузки».", 12, "#96929F"));
        foreach (var f in pending) { try { list.Children.Add(Card(Text(f.Name + " · " + Bytes(f.Length)))); } catch { } }
        list.Children.Add(Text("Показывается записанный размер файла. Процент неизвестен без доступа к браузеру.", 11, "#777580"));
        return Scroll(list);
    }
    void AddFiles()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = "Добавить файлы на полку" }; if (dlg.ShowDialog(this) == true) AddPaths(dlg.FileNames);
    }
    void AddPaths(IEnumerable<string> paths, bool showFiles = true) { foreach (var p in paths) if ((File.Exists(p) || Directory.Exists(p)) && !data.Files.Contains(p, StringComparer.OrdinalIgnoreCase)) data.Files.Add(p); SaveSoon(); if (showFiles) ShowPage("Файлы"); else ShowPage("Главная"); }
    void OnDrop(object sender, System.Windows.DragEventArgs e) { if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) { AddPaths(paths, active != "Главная"); e.Handled = true; } }
    void OpenPath(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch (Exception e) { Store.Log(e); Toast("Не удалось открыть: " + path); }
    }
    UIElement TextPage()
    {
        editor = Input(data.TextDraft); editor.AcceptsReturn = true; editor.AcceptsTab = true; editor.TextWrapping = TextWrapping.Wrap; editor.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; editor.Height = 230;
        var count = Text($"{editor.Text.Length} символов", 11, "#96929F"); editor.TextChanged += (_, _) => { count.Text = $"{editor.Text.Length} симв. · {Regex.Matches(editor.Text, @"\S+").Count} слов"; data.TextDraft = editor.Text; SaveSoon(); };
        var left = Stack(Card(editor, "#151519", 0), Row(Btn("Вставить", () => { if (System.Windows.Clipboard.ContainsText()) editor.Text = System.Windows.Clipboard.GetText(); }), Btn("Скопировать", () => { if (editor.Text.Length > 0) { System.Windows.Clipboard.SetText(editor.Text); Toast("Текст скопирован"); } }), count));
        var tools = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
        foreach (string op in new[] { "Раскладка", "ВЕРХНИЙ", "нижний", "Каждое Слово", "Предложение", "Транслит", "Убрать переносы", "Лишние пробелы", "JSON красиво", "JSON в строку", "→ Base64", "Base64 →", "→ URL", "URL →" })
        { var b = Btn(op, () => { try { editor.Text = TextTools.Transform(editor.Text, op); if (editor.Text.Length > 0) System.Windows.Clipboard.SetText(editor.Text); Toast(op + " — готово, результат в буфере"); } catch { Toast("Некорректный формат для «" + op + "»"); } }); b.Padding = new Thickness(7, 9, 7, 9); tools.Children.Add(b); }
        return Columns(left, Scroll(tools), 1.45);
    }
    void CleanClipboard() { if (System.Windows.Clipboard.ContainsText()) { string t = System.Windows.Clipboard.GetText(TextDataFormat.UnicodeText); System.Windows.Clipboard.SetText(t); Toast("В буфере — текст без форматирования"); } else Toast("В буфере нет текста"); }
    UIElement NotesPage()
    {
        activeNote = Math.Clamp(activeNote, 0, data.Notes.Count - 1); var note = data.Notes[activeNote];
        var tabs = new WrapPanel();
        for (int i = 0; i < data.Notes.Count; i++) { int ix = i; var b = Btn(data.Notes[i].Title, () => { activeNote = ix; Page.Content = NotesPage(); }); b.Background = i == activeNote ? Brush("#3D304D") : Brush("#19191E"); tabs.Children.Add(b); }
        tabs.Children.Add(Btn("＋", () => { data.Notes.Add(new() { Title = "Заметка " + (data.Notes.Count + 1) }); activeNote = data.Notes.Count - 1; SaveSoon(); Page.Content = NotesPage(); }));
        var title = Input(note.Title); title.TextChanged += (_, _) => { note.Title = title.Text; SaveSoon(); };
        var edit = Input(note.Text); edit.AcceptsReturn = true; edit.AcceptsTab = true; edit.TextWrapping = TextWrapping.Wrap; edit.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; edit.MinHeight = 170;
        edit.TextChanged += (_, _) => { note.Text = edit.Text; SaveSoon(); };
        var dock = new DockPanel(); DockPanel.SetDock(tabs, Dock.Top); dock.Children.Add(tabs); DockPanel.SetDock(title, Dock.Top); dock.Children.Add(title);
        var actions = Row(Btn("Копировать", () => { if (edit.Text.Length > 0) System.Windows.Clipboard.SetText(edit.Text); }), Btn("☑ Задача", () => { edit.SelectedText = "☐ "; edit.Focus(); }), Btn("Экспорт .txt", () => ExportNote(note)), Btn("Удалить", () => { if (System.Windows.MessageBox.Show(this, "Удалить заметку?", "Заметки", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return; data.Notes.Remove(note); if (data.Notes.Count == 0) data.Notes.Add(new()); SaveSoon(); Page.Content = NotesPage(); }));
        DockPanel.SetDock(actions, Dock.Bottom); dock.Children.Add(actions); dock.Children.Add(edit);
        var timer = TimerCard(); return Columns(dock, Scroll(timer), 1.8);
    }
    void ExportNote(Note note) { var d = new SaveFileDialog { FileName = "Заметка.txt", Filter = "Текст|*.txt" }; if (d.ShowDialog(this) == true) { File.WriteAllText(d.FileName, note.Text, Encoding.UTF8); Toast("Заметка сохранена"); } }
    UIElement TimerCard()
    {
        timerText = Text(TimerLabel(), 38, "#DCC3FF"); timerText.HorizontalAlignment = HorizontalAlignment.Center;
        timerHint = Text(data.TimerEnd == null ? "Время сосредоточиться" : "Таймер идёт", 11, "#96929F"); timerHint.HorizontalAlignment = HorizontalAlignment.Center;
        var s = Stack(Heading("ФОКУС"), timerText, timerHint, Row(Btn("▶ / Ⅱ", ToggleTimer), Btn("+1 мин", () => { if (data.TimerEnd != null) data.TimerEnd = data.TimerEnd.Value.AddMinutes(1); else data.TimerLeft += 60; SaveSoon(); }), Btn("↺", () => { data.TimerEnd = null; data.TimerLeft = data.Settings.TimerMinutes * 60; SaveSoon(); })));
        s.Children.Add(Heading("НАПОМИНАНИЯ"));
        var input = Input("", "Например: Через 20 минут чай"); input.FontSize = 12;
        s.Children.Add(input); s.Children.Add(Btn("＋ Напомнить", () => { var r = TextTools.ParseReminder(input.Text); if (r == null) { Toast("Формат: «через 20 минут чай» или «завтра в 9:30 звонок»"); return; } data.Reminders.Add(r); SaveSoon(); Page.Content = NotesPage(); Toast("Напоминание: " + r.At.ToString("d MMM HH:mm")); }));
        foreach (var r in data.Reminders.Where(r => !r.Done).OrderBy(r => r.At))
        { s.Children.Add(Row(Text(r.At.ToString("dd.MM HH:mm") + "  " + r.Text, 11), Btn("×", () => { r.Done = true; SaveSoon(); Page.Content = NotesPage(); }))); }
        s.Children.Add(Text("Напоминания сработают, пока приложение запущено. Пропущенные — при следующем запуске.", 10, "#777580"));
        return Card(s);
    }
    double Remaining => data.TimerEnd is DateTime end ? Math.Max(0, (end - DateTime.Now).TotalSeconds) : data.TimerLeft;
    string TimerLabel() { double s = Remaining; return $"{(int)s / 60:00}:{(int)s % 60:00}"; }
    void ToggleTimer() { if (data.TimerEnd != null) { data.TimerLeft = Remaining; data.TimerEnd = null; } else { if (data.TimerLeft <= 0) data.TimerLeft = data.Settings.TimerMinutes * 60; data.TimerEnd = DateTime.Now.AddSeconds(data.TimerLeft); } SaveSoon(); }
    UIElement MusicPage()
    {
        var player = PlayerCard(true); PaintMedia(); _ = RefreshMedia();
        return Stack(player, Card(CreateSpectrum(62), "#100E13", 12));
    }
    async Task MediaAction(int action) { try { if (media == null) { Toast("Нет активной медиасессии"); return; } bool ok = action == 0 ? await media.TrySkipPreviousAsync() : action == 1 ? await media.TryTogglePlayPauseAsync() : await media.TrySkipNextAsync(); if (!ok) Toast("Команда недоступна в этом плеере"); await RefreshMedia(); } catch (Exception e) { Store.Log(e); Toast("Медиасессия недоступна"); } }
    void UnbindSession()
    {
        if (media == null) return;
        media.MediaPropertiesChanged -= OnMediaProperties;
        media.PlaybackInfoChanged -= OnPlaybackInfo;
        media.TimelinePropertiesChanged -= OnTimeline;
    }
    void UnbindMedia()
    {
        UnbindSession();
        if (mediaManager != null) mediaManager.CurrentSessionChanged -= OnCurrentSession;
    }
    void OnCurrentSession(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) => Dispatcher.BeginInvoke(new Action(() => _ = RefreshMedia(true)));
    void OnMediaProperties(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) => Dispatcher.BeginInvoke(new Action(() => _ = RefreshMedia(true)));
    void OnPlaybackInfo(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) => Dispatcher.BeginInvoke(new Action(UpdateMediaTimeline));
    void OnTimeline(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) => Dispatcher.BeginInvoke(new Action(UpdateMediaTimeline));
    void PaintMedia()
    {
        if ((active != "Музыка" && active != "Главная") || trackTitle == null) return;
        trackTitle.Text = string.IsNullOrWhiteSpace(latestTrack) ? "Пока ничего не играет" : latestTrack;
        trackArtist!.Text = media == null ? "Включите музыку в плеере или браузере" : cachedArtist;
        album!.Source = cachedCover; UpdatePlaybackButton(); UpdateMediaTimeline(); SyncVisualization();
    }
    void UpdateMediaTimeline()
    {
        if (closing || compact || (active != "Музыка" && active != "Главная") || media == null || trackTime == null) return;
        try
        {
            var time = media.GetTimelineProperties(); var position = time.Position;
            if (media.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) position += DateTimeOffset.Now - time.LastUpdatedTime;
            if (position < TimeSpan.Zero) position = TimeSpan.Zero; if (position > time.EndTime && time.EndTime > TimeSpan.Zero) position = time.EndTime;
            if (mediaProgress != null && !seeking) { mediaProgress.Maximum = Math.Max(1, time.EndTime.TotalSeconds); mediaProgress.Value = position.TotalSeconds; }
            trackTime.Text = position.ToString(@"mm\:ss");
            if (remainingTime != null) remainingTime.Text = "−" + (time.EndTime > position ? time.EndTime - position : TimeSpan.Zero).ToString(@"mm\:ss");
            UpdatePlaybackButton();
        }
        catch { } // A closing player can invalidate its session between events.
    }
    async Task RefreshMedia(bool force = false)
    {
        if (closing) return; if (mediaBusy) { mediaPending |= force; return; } mediaBusy = true;
        try
        {
            if (mediaManager == null) { mediaManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync(); mediaManager.CurrentSessionChanged += OnCurrentSession; force = true; }
            var current = mediaManager.GetCurrentSession();
            if (!ReferenceEquals(current, media)) { UnbindSession(); media = current; force = true; if (media != null) { media.MediaPropertiesChanged += OnMediaProperties; media.PlaybackInfoChanged += OnPlaybackInfo; media.TimelinePropertiesChanged += OnTimeline; } }
            if (media == null) { bool hadTrack = latestTrack.Length > 0; latestTrack = cachedArtist = cachedAlbum = coverKey = ""; cachedCover = null; PaintAlbumTheme(); if (active == "Главная" && hadTrack) { if (compact) homeDirty = true; else Page.Content = HomePage(); } PaintMedia(); return; }
            if (force)
            {
                bool firstTrack = latestTrack.Length == 0;
                var props = await media.TryGetMediaPropertiesAsync(); latestTrack = props.Title; cachedArtist = props.Artist; cachedAlbum = props.AlbumTitle;
                string key = latestTrack + "\0" + cachedArtist + "\0" + cachedAlbum;
                if (key != coverKey)
                {
                    coverKey = key; cachedCover = null;
                    if (props.Thumbnail != null) { using var stream = await props.Thumbnail.OpenReadAsync(); using var net = stream.AsStreamForRead(); var bmp = new BitmapImage(); bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.DecodePixelWidth = 144; bmp.StreamSource = net; bmp.EndInit(); bmp.Freeze(); cachedCover = bmp; }
                    PaintAlbumTheme();
                    if (!compact) Surface.Pulse(2);
                }
                if (active == "Главная" && firstTrack && latestTrack.Length > 0) { if (compact) homeDirty = true; else Page.Content = HomePage(); }
            }
            PaintMedia();
        }
        catch (Exception e) { latestTrack = ""; if (active == "Музыка") Toast("Медиа Windows недоступно: " + e.Message); }
        finally { mediaBusy = false; if (mediaPending && !closing) { mediaPending = false; _ = Dispatcher.BeginInvoke(new Action(() => _ = RefreshMedia(true))); } }
    }
    UIElement WeatherPage()
    {
        weatherText = Text(weatherDetail, 17); weatherText.Margin = new Thickness(5, 10, 5, 20);
        var city = Input(data.Settings.City, "Введите город, например Kyiv");
        return Card(Stack(Heading("ПОГОДА"), weatherText, Text("Город", 12, "#96929F"), city, Btn("Обновить прогноз", () => { data.Settings.City = city.Text.Trim(); SaveSoon(); _ = LoadWeather(); }), Text("Open-Meteo · Обновление раз в 30 минут.\nГород передаётся сервису геокодирования при запросе. Точное местоположение не запрашивается.", 11, "#777580")));
    }
    async Task LoadWeather()
    {
        lastWeatherCheck = DateTime.Now;
        string city = data.Settings.City; if (string.IsNullOrWhiteSpace(city)) { weatherCompact = ""; weatherDetail = "Укажите город в настройках"; if (weatherText != null) weatherText.Text = weatherDetail; return; }
        Toast("Загружаю погоду…");
        try
        {
            using var geo = JsonDocument.Parse(await http.GetStringAsync("https://geocoding-api.open-meteo.com/v1/search?name=" + Uri.EscapeDataString(city) + "&count=1&language=ru&format=json"));
            if (!geo.RootElement.TryGetProperty("results", out var results)) { Toast("Город не найден. Попробуйте название латиницей."); return; }
            var r = results[0]; string lat = r.GetProperty("latitude").GetDouble().ToString(CultureInfo.InvariantCulture), lon = r.GetProperty("longitude").GetDouble().ToString(CultureInfo.InvariantCulture);
            using var w = JsonDocument.Parse(await http.GetStringAsync($"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current=temperature_2m,apparent_temperature,weather_code,wind_speed_10m&hourly=temperature_2m,precipitation_probability&forecast_days=2&timezone=auto"));
            var c = w.RootElement.GetProperty("current"); double temp = c.GetProperty("temperature_2m").GetDouble(); int code = c.GetProperty("weather_code").GetInt32(); string condition = code == 0 ? "Ясно" : code <= 3 ? "Облачно" : code < 50 ? "Туман" : code < 70 ? "Дождь" : code < 80 ? "Снег" : code < 95 ? "Ливни" : "Гроза";
            weatherCompact = $"{temp:0}°";
            var hourly = w.RootElement.GetProperty("hourly"); var currentTime = DateTime.Parse(c.GetProperty("time").GetString()!, CultureInfo.InvariantCulture);
            var forecast = new List<string>();
            for (int i = 0; i < hourly.GetProperty("time").GetArrayLength() && forecast.Count < 5; i++) { var at = DateTime.Parse(hourly.GetProperty("time")[i].GetString()!, CultureInfo.InvariantCulture); if (at >= currentTime) forecast.Add($"{at:HH:mm}   {hourly.GetProperty("temperature_2m")[i].GetDouble():0}°   осадки {hourly.GetProperty("precipitation_probability")[i].GetInt32()}%"); }
            weatherDetail = $"{r.GetProperty("name").GetString()}  ·  {temp:0}°C  ·  {condition}\nОщущается {c.GetProperty("apparent_temperature").GetDouble():0}°  ·  ветер {c.GetProperty("wind_speed_10m").GetDouble():0} км/ч\n\n" + string.Join("\n", forecast) + $"\n\nОбновлено {DateTime.Now:HH:mm}";
            if (weatherText != null) weatherText.Text = weatherDetail; Toast("Погода обновлена");
        }
        catch (Exception e) { Store.Log(e); weatherDetail = "Не удалось обновить прогноз. Проверьте интернет и город."; weatherCompact = ""; if (weatherText != null) weatherText.Text = weatherDetail; Toast("Погода временно недоступна"); }
    }
    UIElement SettingsPage()
    {
        var s = Stack(Heading("ISLAND PANEL  /  1.3.2"), Text("Своя панель. Всё рядом.", 25), Text("Нативное приложение Windows · без ИИ и подписки", 12, "#96929F"));
        void Check(string label, bool value, Action<bool> set)
        { var c = new CheckBox { Content = label, IsChecked = value }; c.Click += (_, _) => { set(c.IsChecked == true); SaveSoon(); }; s.Children.Add(c); }
        Check("Поверх остальных окон (выключено по умолчанию)", data.Settings.Topmost, b => { data.Settings.Topmost = b; Topmost = b; });
        Check("Сворачивать при переходе в другое приложение", data.Settings.AutoCollapse, b => data.Settings.AutoCollapse = b);
        Check("Собирать локальную историю буфера обмена", data.Settings.CaptureClipboard, b => data.Settings.CaptureClipboard = b);
        Check("Запускать свёрнутой", data.Settings.StartCompact, b => data.Settings.StartCompact = b);
        Check("Глазки в острове следят за курсором", data.Settings.Eyes, b => data.Settings.Eyes = b);
        Check("Плавные анимации и жидкий контур", data.Settings.Animations, b => { data.Settings.Animations = b; Motion.Enabled = b; if (!b) Surface.Stop(); });
        Check("Экономия ресурсов: эффекты 30 кадров/с, реже фоновые проверки", data.Settings.LowPower, b => { data.Settings.LowPower = b; Motion.LowPower = b; });
        Check("Раскрывать по наведению без переключения фокуса", data.Settings.HoverExpand, b => data.Settings.HoverExpand = b);
        Check("Визуализация системного звука (без записи)", data.Settings.Visualizer, b => { data.Settings.Visualizer = b; SyncVisualization(); });
        var screens = new ComboBox { ItemsSource = WinForms.Screen.AllScreens.Select((x, i) => $"Монитор {i + 1} · {x.Bounds.Width} × {x.Bounds.Height}"), SelectedIndex = Math.Clamp(data.Settings.Monitor, 0, WinForms.Screen.AllScreens.Length - 1), MinWidth = 260 };
        screens.SelectionChanged += (_, _) => { data.Settings.Monitor = screens.SelectedIndex; Position(); SaveSoon(); };
        s.Children.Add(Row(Text("Экран", 12), screens));
        var offset = new Slider { Minimum = 0, Maximum = 100, Value = data.Settings.Offset, Width = 230, Margin = new Thickness(15), IsSnapToTickEnabled = true, TickFrequency = 1 };
        offset.ValueChanged += (_, _) => { data.Settings.Offset = (int)offset.Value; Position(); SaveSoon(); }; s.Children.Add(Row(Text("Отступ сверху", 12), offset));
        var city = Input(data.Settings.City); city.Width = 240; city.TextChanged += (_, _) => { data.Settings.City = city.Text.Trim(); SaveSoon(); };
        s.Children.Add(Row(Text("Город погоды", 12), city, Btn("Обновить", () => _ = LoadWeather())));
        var mins = new ComboBox { ItemsSource = new[] { 5, 10, 15, 25, 30, 45, 60 }, SelectedItem = data.Settings.TimerMinutes };
        mins.SelectionChanged += (_, _) => { if (mins.SelectedItem is int n) { data.Settings.TimerMinutes = n; if (data.TimerEnd == null) data.TimerLeft = n * 60; SaveSoon(); } };
        s.Children.Add(Row(Text("Таймер, минут", 12), mins));
        var fps = new ComboBox { ItemsSource = new[] { 30, 60 }, SelectedItem = data.Settings.Fps };
        fps.SelectionChanged += (_, _) => { if (fps.SelectedItem is int n) { data.Settings.Fps = n; SaveSoon(); } }; s.Children.Add(Row(Text("Запись, кадров/с", 12), fps));
        bool auto = File.Exists(AutostartPath);
        Check("Запускать вместе с Windows", auto, SetAutostart);
        s.Children.Add(Text((hotkeyOk ? "Ctrl+Alt+Space — открыть/свернуть" : "Ctrl+Alt+Space занят другим приложением. Используйте значок в трее.") + "\nCtrl+Alt+Q — команда · Esc — свернуть · значок в трее — меню", 12, "#B5A4CB"));
        s.Children.Add(Row(Btn("Папка данных", () => OpenPath(Store.Folder)), Btn("События", () => ShowPage("События")), Btn("Инструкция", () => OpenPath(Path.Combine(Store.Root, "README.txt"))), Btn("Выход", Close)));
        s.Children.Add(Text("Буфер, заметки и настройки сохраняются в data рядом с программой.\nИнтеграции с ИИ отсутствуют. Сетевые запросы: погода и курсы валют по запросу.", 11, "#777580"));
        return Scroll(Card(s));
    }
    static string AutostartPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "IslandPanel.lnk");
    void SetAutostart(bool enabled)
    {
        try
        {
            if (!enabled) { File.Delete(AutostartPath); return; }
            Type? type = Type.GetTypeFromProgID("WScript.Shell"); if (type == null) throw new InvalidOperationException("Windows Script Host недоступен");
            dynamic shell = Activator.CreateInstance(type)!; dynamic link = shell.CreateShortcut(AutostartPath);
            link.TargetPath = Environment.ProcessPath!; link.WorkingDirectory = AppContext.BaseDirectory; link.Description = "Island Panel"; link.Save();
            System.Runtime.InteropServices.Marshal.ReleaseComObject(link); System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
        }
        catch (Exception ex) { Store.Log(ex); Toast("Не удалось изменить автозапуск"); }
    }
    UIElement CommandPage()
    {
        var q = Input("", "Пример: (24+6)*3, 5 миль в км, 10 usd в uah"); q.FontSize = 19;
        var result = Text("Посчитайте, переведите единицы или найдите в интернете", 16, "#96929F");
        var apps = new StackPanel();
        string? answer = null; int revision = 0;
        async Task Update()
        {
            int rev = ++revision; answer = TextTools.Calculate(q.Text);
            result.Text = answer ?? "Enter — поиск в браузере";
            apps.Children.Clear();
            if (q.Text.Trim().Length >= 2) foreach (string shortcut in appShortcuts.Where(p => Path.GetFileNameWithoutExtension(p).Contains(q.Text.Trim(), StringComparison.OrdinalIgnoreCase)).Take(3))
            { var b = Btn("↗ " + Path.GetFileNameWithoutExtension(shortcut), () => OpenPath(shortcut)); b.HorizontalContentAlignment = HorizontalAlignment.Left; apps.Children.Add(b); }
            var m = Regex.Match(q.Text.Trim(), @"^(\d+(?:[.,]\d+)?)\s*(usd|eur|uah|rub|gbp|chf|pln|доллар\w*|евро|грив\w*|руб\w*)\s+(?:в|to)\s+(usd|eur|uah|rub|gbp|chf|pln|доллар\w*|евро|грив\w*|руб\w*)$", RegexOptions.IgnoreCase);
            if (!m.Success) return;
            result.Text = "Загружаю курс…";
            try
            {
                await Task.Delay(450); if (rev != revision) return;
                string Code(string a) { a = a.ToLowerInvariant(); return a.StartsWith("доллар") ? "usd" : a == "евро" ? "eur" : a.StartsWith("грив") ? "uah" : a.StartsWith("руб") ? "rub" : a; }
                string a = Code(m.Groups[2].Value), b = Code(m.Groups[3].Value);
                using var doc = JsonDocument.Parse(await http.GetStringAsync($"https://api.frankfurter.dev/v2/rate/{a}/{b}"));
                double amount = double.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture) * doc.RootElement.GetProperty("rate").GetDouble();
                if (rev != revision) return;
                answer = amount.ToString("0.00") + " " + b.ToUpperInvariant(); result.Text = answer + "\nКурс на " + doc.RootElement.GetProperty("date").GetString() + " · Frankfurter";
            }
            catch { if (rev == revision) { answer = null; result.Text = "Курс недоступен. Проверьте соединение или другую валютную пару."; } }
        }
        q.TextChanged += async (_, _) => await Update();
        void CopyAnswer() { if (answer != null) { System.Windows.Clipboard.SetText(answer); Toast("Результат скопирован"); } }
        q.KeyDown += (_, e) => { if (e.Key == Key.Enter) { if (answer != null) CopyAnswer(); else SearchWeb(q.Text); e.Handled = true; } };
        Dispatcher.BeginInvoke(new Action(() => q.Focus()));
        return Scroll(Card(Stack(q, Card(result, "#1C1824", 20), apps, Row(Btn("Копировать результат", CopyAnswer), Btn("Искать в браузере ↗", () => SearchWeb(q.Text))), Text("Примеры\n(24 + 6) * 3\n5 миль в км   ·   72 °f в °c   ·   10 кг в lb\n100 usd в uah   ·   20 eur в rub\nНазвание программы — найти ярлык из меню «Пуск».\n\nВычисления и единицы работают локально. Курсы — по запросу из Frankfurter.\nДоступные операции: + − * / и скобки. 10% означает 10/100.", 12, "#96929F"))));
    }
    void SearchWeb(string text) { if (!string.IsNullOrWhiteSpace(text)) OpenPath("https://www.google.com/search?q=" + Uri.EscapeDataString(text)); }
    UIElement EventsPage()
    {
        var s = Stack(Heading("СОБЫТИЯ ПАНЕЛИ"));
        if (events.Count == 0) s.Children.Add(Text("Здесь появятся напоминания, завершённые записи и загрузки.", 13, "#96929F"));
        foreach (var e in events) s.Children.Add(Card(Stack(Text(e.Title, 15), Text(e.Detail, 12, "#B1A7BD"), Text(e.Time.ToString("HH:mm"), 10, "#777580"))));
        s.Children.Add(Text("Уведомления других приложений Windows не перехватываются.", 11, "#777580")); return Scroll(s);
    }
    void ShowQr()
    {
        var q = Input("", "Текст или ссылка"); var image = new System.Windows.Controls.Image { Width = 230, Height = 230, Margin = new Thickness(5) };
        byte[]? png = null;
        var make = Btn("Создать QR-код", () => { try { if (q.Text.Length == 0) return; using var generator = new QRCodeGenerator(); using var code = generator.CreateQrCode(q.Text, QRCodeGenerator.ECCLevel.M); using var qr = new PngByteQRCode(code); png = qr.GetGraphic(8); using var ms = new MemoryStream(png); var b = new BitmapImage(); b.BeginInit(); b.CacheOption = BitmapCacheOption.OnLoad; b.StreamSource = ms; b.EndInit(); b.Freeze(); image.Source = b; Toast("QR-код создан локально"); } catch { Toast("Текст слишком длинный для QR-кода"); } });
        var save = Btn("Сохранить PNG", () => { if (png == null) return; var d = new SaveFileDialog { FileName = "QR.png", Filter = "PNG|*.png" }; if (d.ShowDialog(this) == true) File.WriteAllBytes(d.FileName, png); });
        Expand(); active = "QR"; Page.Content = Stack(q, Row(make, save), image);
    }
    public static BitmapImage LoadImage(string path) { var b = new BitmapImage(); b.BeginInit(); b.CacheOption = BitmapCacheOption.OnLoad; b.UriSource = new Uri(path); b.EndInit(); b.Freeze(); return b; }
    static void SavePng(BitmapSource bitmap, string path) { var e = new PngBitmapEncoder(); e.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); e.Save(stream); }
    async Task Capture(string mode)
    {
        try
        {
            Hide(); await Task.Delay(230);
            var screens = WinForms.Screen.AllScreens; var screen = screens[Math.Clamp(data.Settings.Monitor, 0, screens.Length - 1)]; var bounds = screen.Bounds;
            using var bmp = new System.Drawing.Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var graphics = System.Drawing.Graphics.FromImage(bmp)) graphics.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size);
            using var ms = new MemoryStream(); bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png); ms.Position = 0;
            var source = new BitmapImage(); source.BeginInit(); source.CacheOption = BitmapCacheOption.OnLoad; source.StreamSource = ms; source.EndInit(); source.Freeze();
            Int32Rect? crop = mode == "screen" ? new Int32Rect(0, 0, source.PixelWidth, source.PixelHeight) : null;
            if (crop == null)
            {
                var selection = new SelectionWindow(source, bounds, mode == "color");
                selection.ShowDialog(); crop = selection.Selection;
            }
            if (crop == null) return;
            var area = new CroppedBitmap(source, crop.Value); area.Freeze();
            if (mode == "color")
            {
                byte[] pixel = new byte[4]; var bgra = new FormatConvertedBitmap(area, PixelFormats.Bgra32, null, 0); bgra.CopyPixels(new Int32Rect(0, 0, 1, 1), pixel, 4, 0);
                string hex = $"#{pixel[2]:X2}{pixel[1]:X2}{pixel[0]:X2}"; System.Windows.Clipboard.SetText(hex); Toast(hex + " — цвет в буфере"); return;
            }
            if (mode == "ocr")
            {
                var engine = OcrEngine.TryCreateFromUserProfileLanguages();
                if (engine == null) { Toast("Нет языка OCR. Добавьте язык распознавания в Windows."); return; }
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(area)); using var bytes = new MemoryStream(); encoder.Save(bytes); bytes.Position = 0;
                using var ras = bytes.AsRandomAccessStream(); var decoder = await BitmapDecoder.CreateAsync(ras); using var software = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                if (software.PixelWidth > OcrEngine.MaxImageDimension || software.PixelHeight > OcrEngine.MaxImageDimension) { Toast("Область слишком большая. Выберите меньший фрагмент."); return; }
                var recognized = await engine.RecognizeAsync(software); if (string.IsNullOrWhiteSpace(recognized.Text)) Toast("Текст в области не найден"); else { System.Windows.Clipboard.SetText(recognized.Text); Toast("Текст с экрана скопирован"); } return;
            }
            string path = Path.Combine(Store.Captures, "Screenshot-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".png"); SavePng(area, path); System.Windows.Clipboard.SetImage(area); Notify("Снимок сохранён", Path.GetFileName(path) + " · также в буфере");
        }
        catch (Exception e) { Store.Log(e); Toast("Захват недоступен: " + e.Message); }
        finally { Show(); Expand(); }
    }
    async Task ToggleRecording()
    {
        if (recorder != null)
        {
            var p = recorder; recorder = null;
            try { await p.StandardInput.WriteLineAsync("q"); if (!await Task.Run(() => p.WaitForExit(12000))) { p.Kill(); Toast("Запись остановлена принудительно. Проверьте файл."); } else if (p.ExitCode == 0) Notify("Запись завершена", Path.GetFileName(recordingFile!)); else Toast("Ошибка записи. Смотрите data\\recording.log"); }
            catch (Exception e) { Store.Log(e); Toast("Не удалось завершить запись"); } finally { p.Dispose(); Expand(); }
            return;
        }
        string exe = Path.Combine(Store.Root, "tools", "ffmpeg.exe"); if (!File.Exists(exe)) { Toast("Нет tools\\ffmpeg.exe"); return; }
        var confirm = System.Windows.MessageBox.Show(this, "Записать выбранный монитор без звука?\n\nЗапись сохранится в captures. Остановить: нажмите остров и кнопку «Стоп».\nЧастота: " + data.Settings.Fps + " кадров/с (фактическая зависит от компьютера).", "Запись экрана", MessageBoxButton.OKCancel);
        if (confirm != MessageBoxResult.OK) return;
        try
        {
            var screens = WinForms.Screen.AllScreens; var b = screens[Math.Clamp(data.Settings.Monitor, 0, screens.Length - 1)].Bounds;
            recordingFile = Path.Combine(Store.Captures, "Recording-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".mp4");
            var psi = new ProcessStartInfo(exe) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardInput = true, RedirectStandardError = true };
            foreach (string a in new[] { "-hide_banner", "-y", "-f", "gdigrab", "-framerate", data.Settings.Fps.ToString(), "-offset_x", b.X.ToString(), "-offset_y", b.Y.ToString(), "-video_size", $"{b.Width}x{b.Height}", "-draw_mouse", "1", "-i", "desktop", "-an", "-vf", "pad=ceil(iw/2)*2:ceil(ih/2)*2", "-c:v", "libx264", "-preset", "ultrafast", "-crf", "23", "-pix_fmt", "yuv420p", "-movflags", "+faststart", recordingFile }) psi.ArgumentList.Add(a);
            recorder = new Process { StartInfo = psi, EnableRaisingEvents = true }; var log = new StringBuilder();
            recorder.ErrorDataReceived += (_, e) => { if (e.Data != null) { lock (log) { log.AppendLine(e.Data); if (log.Length > 40000) log.Remove(0, 20000); } } };
            recorder.Exited += (_, _) => { lock (log) { try { File.WriteAllText(Path.Combine(Store.Folder, "recording.log"), log.ToString()); } catch { } } };
            recorder.Start(); recorder.BeginErrorReadLine(); recordingStart = DateTime.Now; Collapse();
            await Task.Delay(1000);
            if (recorder != null && recorder.HasExited) { recorder.Dispose(); recorder = null; Toast("Запись не запустилась. Проверьте data\\recording.log"); Expand(); }
        }
        catch (Exception e) { recorder = null; Store.Log(e); Toast("Запись не запустилась: " + e.Message); }
    }
    async Task Tick()
    {
        if (closing) return;
        seconds++; UpdatePill(); if (!compact && active == "Главная" && homeClock != null) homeClock.Text = DateTime.Now.ToString("HH:mm");
        if (!compact && active == "Заметки" && timerText != null) timerText.Text = TimerLabel();
        if (!compact && active == "Заметки" && timerHint != null) timerHint.Text = data.TimerEnd != null ? "До " + data.TimerEnd.Value.ToString("HH:mm") : "На паузе";
        if (data.TimerEnd != null && Remaining <= 0) { data.TimerEnd = null; data.TimerLeft = 0; Notify("Таймер завершён", "Время сделать перерыв"); System.Media.SystemSounds.Exclamation.Play(); SaveSoon(); }
        foreach (var r in data.Reminders.Where(r => !r.Done && r.At <= DateTime.Now).ToList()) { r.Done = true; Notify("Напоминание", r.Text); System.Media.SystemSounds.Exclamation.Play(); SaveSoon(); }
        UpdateMediaTimeline();
        SyncVisualization();
        if (!compact && active == "Микшер" && seconds % 2 == 0) RefreshMixer();
        if (seconds % 60 == 0 && mediaManager == null) await RefreshMedia();
        if ((DateTime.Now - lastDownloadsCheck).TotalSeconds >= (data.Settings.LowPower ? 20 : 10)) { lastDownloadsCheck = DateTime.Now; CheckDownloads(); }
        if ((DateTime.Now - lastWeatherCheck).TotalMinutes >= 30 && !string.IsNullOrWhiteSpace(data.Settings.City)) await LoadWeather();
    }
    void UpdatePill()
    {
        PillClock.Text = recorder != null ? "● " + (DateTime.Now - recordingStart).ToString(@"mm\:ss") : DateTime.Now.ToString("HH:mm");
        PillInfo.Text = recorder != null ? "СТОП →" : data.TimerEnd != null ? TimerLabel() : weatherCompact.Length > 0 ? weatherCompact : latestTrack.Length > 0 ? latestTrack[..Math.Min(14, latestTrack.Length)] : "ISLAND";
        PillIcon.Text = recorder != null ? "●" : data.TimerEnd != null ? "◷" : latestTrack.Length > 0 ? "♫" : "◈";
        bool showEyes = data.Settings.Eyes && recorder == null && data.TimerEnd == null && latestTrack.Length == 0;
        if (DateTime.Now < noticeUntil && recorder == null) { PillInfo.Text = noticeCaption[..Math.Min(16, noticeCaption.Length)]; PillIcon.Text = "●"; showEyes = false; }
        PillEyes.Visibility = showEyes ? Visibility.Visible : Visibility.Collapsed; PillIcon.Visibility = showEyes ? Visibility.Collapsed : Visibility.Visible;
        if (compact && recorder != null) Compact.ToolTip = "Нажмите, чтобы остановить запись"; else Compact.ToolTip = "Открыть панель · Ctrl+Alt+Space";
        if (recorder != null && !compact) { Status.Text = "● Идёт запись  ·  " + (DateTime.Now - recordingStart).ToString(@"mm\:ss"); }
    }
    FileInfo[] FindDownloads()
    {
        try { string path = ShellIcons.Downloads; if (!Directory.Exists(path)) return Array.Empty<FileInfo>(); return new DirectoryInfo(path).EnumerateFiles().Where(f => f.Extension.Equals(".crdownload", StringComparison.OrdinalIgnoreCase) || f.Extension.Equals(".part", StringComparison.OrdinalIgnoreCase)).ToArray(); } catch { return Array.Empty<FileInfo>(); }
    }
    void CheckDownloads()
    {
        var files = FindDownloads(); var names = files.Select(f => f.FullName).ToHashSet();
        foreach (var old in downloads.Keys.Where(x => !names.Contains(x)).ToList())
        {
            string completed = old.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase) ? old[..^11] : old[..^5];
            if (File.Exists(completed)) Notify("Загрузка завершена", Path.GetFileName(completed)); downloads.Remove(old);
        }
        foreach (var f in files)
        {
            try { double speed = downloads.TryGetValue(f.FullName, out var last) ? Math.Max(0, (f.Length - last.Size) / Math.Max(1, (DateTime.Now - last.At).TotalSeconds)) : 0; downloads[f.FullName] = (f.Length, DateTime.Now); if (compact && recorder == null && data.TimerEnd == null) { PillInfo.Text = Bytes(f.Length); PillIcon.Text = "↓"; Compact.ToolTip = f.Name + " · " + Bytes(speed) + "/с"; } } catch { }
        }
    }
    static string Bytes(double n) => n >= 1073741824 ? $"{n / 1073741824:0.0} ГБ" : n >= 1048576 ? $"{n / 1048576:0.0} МБ" : $"{n / 1024:0} КБ";
}
