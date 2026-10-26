using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace IslandPanel;
public class EyesControl : FrameworkElement
{
    static readonly Brush Pupil = Frozen(new SolidColorBrush(Color.FromRgb(23, 21, 27)));
    static T Frozen<T>(T value) where T : Freezable { value.Freeze(); return value; }
    readonly DispatcherTimer timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
    Point previous;
    DateTime moved = DateTime.Now, nextBlink = DateTime.Now.AddSeconds(5), blinkEnd;
    double pupilX, pupilY;
    bool sleeping, blinking;
    public bool IsPolling => timer.IsEnabled;
    public EyesControl()
    {
        Loaded += (_, _) => Sync(); Unloaded += (_, _) => timer.Stop(); IsVisibleChanged += (_, _) => Sync();
        timer.Tick += (_, _) => Step();
    }
    void Sync() { if (IsLoaded && IsVisible) { timer.Start(); Step(); } else timer.Stop(); }
    void Step()
    {
        if (!IsVisible || !IsLoaded) { timer.Stop(); return; }
        var cursor = System.Windows.Forms.Cursor.Position; var screen = new Point(cursor.X, cursor.Y); var now = DateTime.Now;
        bool changed = screen != previous; if (changed) { previous = screen; moved = now; }
        bool sleep = (now - moved).TotalSeconds > 40;
        bool blink = !sleep && now >= nextBlink;
        if (blink && !blinking) blinkEnd = now.AddMilliseconds(135);
        if (blinking && now >= blinkEnd) { nextBlink = now.AddSeconds(5); blink = false; }
        var local = PointFromScreen(screen);
        double x = Math.Clamp((local.X - 18) / 160, -2.6, 2.6), y = Math.Clamp((local.Y - 12) / 160, -2.8, 2.8);
        bool dirty = Math.Abs(x - pupilX) > .03 || Math.Abs(y - pupilY) > .03 || sleep != sleeping || blink != blinking;
        pupilX = Motion.Allowed ? pupilX + (x - pupilX) * .7 : x; pupilY = Motion.Allowed ? pupilY + (y - pupilY) * .7 : y;
        sleeping = sleep; blinking = blink;
        if (dirty) InvalidateVisual();
        timer.Interval = TimeSpan.FromMilliseconds(blinking ? 135 : (now - moved).TotalSeconds < .6 ? (Motion.LowPower ? 125 : 66) : (Motion.LowPower ? 750 : 500));
    }
    protected override void OnRender(DrawingContext dc)
    {
        foreach (double x in new[] { 10d, 26d })
        {
            if (sleeping || blinking) dc.DrawRoundedRectangle(Brushes.WhiteSmoke, null, new Rect(x - 4, 12, 8, 1.8), 1, 1);
            else { dc.DrawEllipse(Brushes.WhiteSmoke, null, new Point(x, 12), 4.7, 7.3); dc.DrawEllipse(Pupil, null, new Point(x + pupilX, 12 + pupilY), 2.1, 3.0); }
        }
    }
}
