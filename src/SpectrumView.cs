using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace IslandPanel;
public sealed class SpectrumView : FrameworkElement
{
    readonly DispatcherTimer timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(34) };
    readonly float[] target = new float[SpectrumAnalyzer.BandCount], smooth = new float[SpectrumAnalyzer.BandCount], wave = new float[SpectrumAnalyzer.WaveCount];
    Brush ink = Brushes.Orchid;
    Pen line = new(Brushes.Orchid, 1.5);
    LoopbackSpectrum? source;
    bool active;
    Color currentColor;
    DateTime quiet = DateTime.Now;
    public bool IsRendering => timer.IsEnabled;
    public bool WaveMode { get; set; }
    public long FramesDrawn { get; private set; }
    public float Level { get; private set; }
    public SpectrumView()
    {
        Loaded += (_, _) => Sync(); Unloaded += (_, _) => timer.Stop(); IsVisibleChanged += (_, _) => Sync(); timer.Tick += (_, _) => Step();
        IsHitTestVisible = false;
    }
    public void Configure(LoopbackSpectrum engine, bool enabled, Color color, bool waveMode)
    {
        if (ReferenceEquals(source, engine) && active == enabled && currentColor == color && WaveMode == waveMode) { Sync(); return; }
        if (!enabled) { Array.Clear(smooth); Array.Clear(wave); Level = 0; }
        source = engine; active = enabled; currentColor = color; WaveMode = waveMode;
        var light = Color.FromRgb((byte)Math.Min(255, color.R + 45), (byte)Math.Min(255, color.G + 40), (byte)Math.Min(255, color.B + 45));
        var gradient = new LinearGradientBrush(color, light, 90); gradient.Freeze(); ink = gradient;
        line = new Pen(ink, 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }; line.Freeze(); Sync(); InvalidateVisual();
    }
    void Sync() { if (IsLoaded && IsVisible && active && source != null) timer.Start(); else timer.Stop(); }
    void Step()
    {
        if (!active || !IsVisible || source == null) { timer.Stop(); return; }
        source.CopyFrame(target, wave, out float level); Level = level;
        if (level > .0002f) quiet = DateTime.Now;
        float change = 0;
        for (int i = 0; i < smooth.Length; i++) { float old = smooth[i]; smooth[i] += (target[i] - smooth[i]) * (target[i] > smooth[i] ? .55f : .2f); change = Math.Max(change, Math.Abs(smooth[i] - old)); }
        if (change > .001 || WaveMode && level > .0001) InvalidateVisual();
        timer.Interval = TimeSpan.FromMilliseconds((DateTime.Now - quiet).TotalSeconds > 1.5 ? 200 : Motion.LowPower ? 67 : 34);
    }
    protected override void OnRender(DrawingContext dc)
    {
        FramesDrawn++; double w = ActualWidth, h = ActualHeight; if (w <= 0 || h <= 0) return;
        if (WaveMode)
        {
            var g = new StreamGeometry(); using (var c = g.Open()) { c.BeginFigure(new Point(0, h / 2), false, false); for (int i = 0; i < wave.Length; i++) c.LineTo(new Point(w * i / (wave.Length - 1), h / 2 - Math.Clamp(wave[i] * 1.8, -1, 1) * h * .43), true, false); } g.Freeze(); dc.DrawGeometry(null, line, g);
        }
        else
        {
            int count = w < 160 ? 16 : SpectrumAnalyzer.BandCount; double pitch = w / count, gap = Math.Max(2, pitch * .3), bw = Math.Max(1.5, pitch - gap);
            for (int i = 0; i < count; i++) { float v = count == 16 ? Math.Max(smooth[i * 2], smooth[i * 2 + 1]) : smooth[i]; double bh = Math.Max(2, v * (h - 2)); dc.DrawRoundedRectangle(ink, null, new Rect(i * pitch + gap / 2, h - bh, bw, bh), Math.Min(3, bw / 2), Math.Min(3, bw / 2)); }
        }
    }
}
