using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace IslandPanel;
public static class Motion
{
    public static bool Enabled { get; set; } = true;
    public static bool LowPower { get; set; }
    public static bool Allowed => Enabled && SystemParameters.ClientAreaAnimation;
    public static int FrameRate => LowPower ? 30 : 60;
    static readonly DependencyProperty WiredProperty = DependencyProperty.RegisterAttached("Wired", typeof(bool), typeof(Motion), new PropertyMetadata(false));
    public static void Animate(Animatable target, DependencyProperty property, double value, double milliseconds = 180, Action? completed = null, double delay = 0)
    {
        double from = (double)target.GetValue(property);
        target.BeginAnimation(property, null); target.SetValue(property, from);
        if (!Allowed || milliseconds <= 0 || Math.Abs(from - value) < .001) { target.SetValue(property, value); completed?.Invoke(); return; }
        var a = new DoubleAnimation(from, value, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.HoldEnd, BeginTime = TimeSpan.FromMilliseconds(delay) };
        Timeline.SetDesiredFrameRate(a, FrameRate);
        a.Completed += (_, _) => { target.SetValue(property, value); target.BeginAnimation(property, null); completed?.Invoke(); };
        target.BeginAnimation(property, a, HandoffBehavior.SnapshotAndReplace);
    }
    public static void Animate(UIElement target, DependencyProperty property, double value, double milliseconds = 180, Action? completed = null, double delay = 0)
    {
        double from = (double)target.GetValue(property);
        target.BeginAnimation(property, null); target.SetValue(property, from);
        if (!Allowed || milliseconds <= 0 || Math.Abs(from - value) < .001) { target.SetValue(property, value); completed?.Invoke(); return; }
        var a = new DoubleAnimation(from, value, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.HoldEnd, BeginTime = TimeSpan.FromMilliseconds(delay) };
        Timeline.SetDesiredFrameRate(a, FrameRate);
        a.Completed += (_, _) => { target.SetValue(property, value); target.BeginAnimation(property, null); completed?.Invoke(); };
        target.BeginAnimation(property, a, HandoffBehavior.SnapshotAndReplace);
    }
    public static void WireButton(Button button)
    {
        if ((bool)button.GetValue(WiredProperty)) return;
        button.SetValue(WiredProperty, true);
        if (button.Template.FindName("HoverWash", button) is not Border wash || button.Template.FindName("B", button) is not Border body) return;
        var transform = new ScaleTransform(1, 1); body.RenderTransformOrigin = new Point(.5, .5); body.RenderTransform = transform;
        button.MouseEnter += (_, _) => Animate(wash, UIElement.OpacityProperty, .075, 140);
        button.MouseLeave += (_, _) => { Animate(wash, UIElement.OpacityProperty, 0, 200); Animate(transform, ScaleTransform.ScaleXProperty, 1, 180); Animate(transform, ScaleTransform.ScaleYProperty, 1, 180); };
        button.PreviewMouseLeftButtonDown += (_, _) => { Animate(transform, ScaleTransform.ScaleXProperty, .97, 85); Animate(transform, ScaleTransform.ScaleYProperty, .97, 85); };
        button.PreviewMouseLeftButtonUp += (_, _) => { Animate(transform, ScaleTransform.ScaleXProperty, 1, 180); Animate(transform, ScaleTransform.ScaleYProperty, 1, 180); };
    }
}
