using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace IslandPanel;
public class SelectionWindow : Window
{
    public Int32Rect? Selection { get; private set; }
    readonly Canvas overlay = new();
    readonly Rectangle box = new() { Stroke = new SolidColorBrush(Color.FromRgb(206, 177, 255)), StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(30, 206, 177, 255)) };
    readonly BitmapSource bitmap;
    Point start;
    bool dragging, color;
    public SelectionWindow(BitmapSource source, System.Drawing.Rectangle screen, bool pickColor)
    {
        bitmap = source; color = pickColor;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; Cursor = Cursors.Cross; Background = Brushes.Black;
        var root = new Grid(); root.Children.Add(new System.Windows.Controls.Image { Source = source, Stretch = Stretch.Fill });
        root.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(45, 0, 0, 0)), IsHitTestVisible = false }); root.Children.Add(overlay);
        var hint = new TextBlock { Text = pickColor ? "Выберите цвет пикселя · Esc — отмена" : "Выделите область мышью · Esc — отмена", Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromArgb(220, 15, 15, 20)), Padding = new Thickness(16), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 30, 0, 0), IsHitTestVisible = false };
        root.Children.Add(hint); overlay.Children.Add(box); Content = root;
        SourceInitialized += (_, _) => { var dpi = VisualTreeHelper.GetDpi(this); Left = screen.X / dpi.DpiScaleX; Top = screen.Y / dpi.DpiScaleY; Width = screen.Width / dpi.DpiScaleX; Height = screen.Height / dpi.DpiScaleY; };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        MouseLeftButtonDown += (_, e) => { start = e.GetPosition(root); dragging = true; CaptureMouse(); if (color) Finish(start); };
        MouseMove += (_, e) => { if (!dragging) return; var end = e.GetPosition(root); Canvas.SetLeft(box, Math.Min(start.X, end.X)); Canvas.SetTop(box, Math.Min(start.Y, end.Y)); box.Width = Math.Abs(end.X - start.X); box.Height = Math.Abs(end.Y - start.Y); };
        MouseLeftButtonUp += (_, e) => { if (dragging) Finish(e.GetPosition(root)); };
    }
    void Finish(Point end)
    {
        dragging = false; ReleaseMouseCapture();
        double sx = bitmap.PixelWidth / ActualWidth, sy = bitmap.PixelHeight / ActualHeight;
        int x = Math.Clamp((int)(Math.Min(start.X, end.X) * sx), 0, bitmap.PixelWidth - 1), y = Math.Clamp((int)(Math.Min(start.Y, end.Y) * sy), 0, bitmap.PixelHeight - 1);
        int w = color ? 1 : Math.Clamp((int)(Math.Abs(end.X - start.X) * sx), 0, bitmap.PixelWidth - x), h = color ? 1 : Math.Clamp((int)(Math.Abs(end.Y - start.Y) * sy), 0, bitmap.PixelHeight - y);
        if (w > 0 && h > 0) Selection = new Int32Rect(x, y, w, h); Close();
    }
}
