using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace IslandPanel;
// One vector surface. No bitmap blur, shader, perpetual storyboard or idle render loop.
public sealed class IslandSurface : FrameworkElement
{
    static readonly Brush Fill = Frozen(new SolidColorBrush(Color.FromRgb(6, 6, 8)));
    static readonly Pen Outline = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(26, 255, 255, 255)), .7));
    static readonly Pen Accent = Frozen(new Pen(new LinearGradientBrush(new GradientStopCollection { new(Color.FromArgb(0, 168, 130, 238), 0), new(Color.FromArgb(135, 185, 157, 236), .35), new(Color.FromArgb(100, 224, 184, 122), .7), new(Color.FromArgb(0, 224, 184, 122), 1) }, 0), 1));
    static T Frozen<T>(T value) where T : Freezable { value.Freeze(); return value; }
    readonly Stopwatch elapsed = new();
    double amplitude, lastFrame;
    bool subscribed;
    public bool Docked { get; set; }
    public bool IsAnimating => subscribed;
    public IslandSurface() { IsHitTestVisible = false; Unloaded += (_, _) => Stop(); IsVisibleChanged += (_, _) => { if (!IsVisible) Stop(); }; }
    public void Pulse(double strength = 4)
    {
        if (!Motion.Allowed || !IsVisible) { Stop(); return; }
        amplitude = Motion.LowPower ? Math.Min(2, strength) : strength; elapsed.Restart(); lastFrame = -1;
        if (!subscribed) { CompositionTarget.Rendering += Frame; subscribed = true; }
    }
    void Frame(object? sender, EventArgs e)
    {
        double now = elapsed.Elapsed.TotalMilliseconds;
        if (now > 620) { Stop(); return; }
        if (now - lastFrame < 1000d / Motion.FrameRate) return;
        lastFrame = now; InvalidateVisual();
    }
    public void Stop() { if (subscribed) CompositionTarget.Rendering -= Frame; subscribed = false; amplitude = 0; elapsed.Stop(); InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight - 5; if (w < 20 || h < 15) return;
        double r = Math.Min(27, h / 2), top = Docked ? 0 : Math.Min(23, h / 2);
        double amp = amplitude * Math.Exp(-elapsed.Elapsed.TotalSeconds * 7);
        var path = new StreamGeometry();
        using (var c = path.Open())
        {
            c.BeginFigure(new Point(top, .6), true, true); c.LineTo(new Point(w - top, .6), true, false);
            c.QuadraticBezierTo(new Point(w - .6, .6), new Point(w - .6, top), true, false);
            c.LineTo(new Point(w - .6, h - r), true, false); c.QuadraticBezierTo(new Point(w - .6, h), new Point(w - r, h), true, false);
            for (int i = 1; i <= 36; i++) { double x = (w - r) - (w - 2 * r) * i / 36; double v = i / 36d; double wave = amp * Math.Sin(v * Math.PI) * Math.Sin(v * Math.PI * 7 - elapsed.Elapsed.TotalSeconds * 19); c.LineTo(new Point(x, h + wave), true, false); }
            c.QuadraticBezierTo(new Point(.6, h), new Point(.6, h - r), true, false); c.LineTo(new Point(.6, top), true, false); c.QuadraticBezierTo(new Point(.6, .6), new Point(top, .6), true, false);
        }
        path.Freeze(); dc.DrawGeometry(Fill, Outline, path);
        var edge = new StreamGeometry();
        using (var c = edge.Open())
        {
            c.BeginFigure(new Point(r, h - .4), false, false);
            for (int i = 1; i <= 36; i++) { double x = r + (w - 2 * r) * i / 36, v = i / 36d; double wave = amp * Math.Sin(v * Math.PI) * Math.Sin((1 - v) * Math.PI * 7 - elapsed.Elapsed.TotalSeconds * 19); c.LineTo(new Point(x, h + wave - .4), true, false); }
        }
        edge.Freeze(); dc.DrawGeometry(null, Accent, edge);
    }
}
