using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace IslandPanel;
public partial class MainWindow
{
    const string PinFormat = "IslandPanel.PinPath";
    Border DockShelf()
    {
        var icons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (string path in data.Files) icons.Children.Add(DockPin(path));
        var add = Btn("＋", () => { }); add.FontSize = 24; add.Width = 48; add.Height = 48; add.Padding = new Thickness(0); add.Style = (Style)System.Windows.Application.Current.FindResource("DockAddButton");
        var menu = new ContextMenu(); menu.Items.Add(Menu("Добавить папку…", AddDockFolder)); menu.Items.Add(Menu("Добавить программу или файл…", AddDockFiles)); menu.Items.Add(new Separator()); menu.Items.Add(Menu("Открыть полку файлов", () => ShowPage("Файлы")));
        add.ContextMenu = menu; add.Click += (_, _) => { menu.PlacementTarget = add; menu.IsOpen = true; }; add.ToolTip = "Добавить папку или программу"; icons.Children.Add(add);
        if (data.Files.Count == 0) icons.Children.Add(Text("Перетащите сюда папки, файлы или ярлыки", 12, "#827887"));
        var scroll = new ScrollViewer { Content = icons, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, PanningMode = PanningMode.HorizontalOnly };
        scroll.PreviewMouseWheel += (_, e) => { scroll.ScrollToHorizontalOffset(Math.Clamp(scroll.HorizontalOffset - e.Delta, 0, scroll.ScrollableWidth)); e.Handled = true; };
        var shelf = Card(scroll, "#101012", 3); shelf.Height = 64; return shelf;
    }
    static MenuItem Menu(string title, Action action) { var item = new MenuItem { Header = title }; item.Click += (_, _) => action(); return item; }
    Button DockPin(string path)
    {
        var b = Btn("", () => OpenPath(path)); b.Width = 50; b.Height = 50; b.Padding = new Thickness(5); b.Margin = new Thickness(2, 0, 2, 0); b.Background = Brushes.Transparent; b.BorderBrush = Brushes.Transparent;
        b.ToolTip = Path.GetFileName(path.TrimEnd('\\')) + "\n" + path;
        System.Windows.Automation.AutomationProperties.SetName(b, "Открыть " + Path.GetFileName(path.TrimEnd('\\')));
        var fallback = Icons.Create(Directory.Exists(path) ? "files" : "note", 30, Brush("#B09AB8"));
        var image = new System.Windows.Controls.Image { Width = 34, Height = 34, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        var icon = new Grid { Width = 38, Height = 38, RenderTransformOrigin = new Point(.5, .6) }; icon.Children.Add(fallback); icon.Children.Add(image);
        var transforms = new TransformGroup(); var scale = new ScaleTransform(1, 1); var lift = new TranslateTransform(); transforms.Children.Add(scale); transforms.Children.Add(lift); icon.RenderTransform = transforms; b.Content = icon;
        b.MouseEnter += (_, _) => { Motion.Animate(scale, ScaleTransform.ScaleXProperty, 1.14, 150); Motion.Animate(scale, ScaleTransform.ScaleYProperty, 1.14, 150); Motion.Animate(lift, TranslateTransform.YProperty, -2, 150); };
        b.MouseLeave += (_, _) => { Motion.Animate(scale, ScaleTransform.ScaleXProperty, 1, 220); Motion.Animate(scale, ScaleTransform.ScaleYProperty, 1, 220); Motion.Animate(lift, TranslateTransform.YProperty, 0, 220); };
        _ = FillDockIcon(image, fallback, path);
        var menu = new ContextMenu(); menu.Items.Add(Menu("Открыть", () => OpenPath(path))); menu.Items.Add(Menu("Открыть расположение", () => { var parent = Path.GetDirectoryName(path.TrimEnd('\\')); if (parent != null) OpenPath(parent); }));
        menu.Items.Add(Menu("Копировать путь", () => { System.Windows.Clipboard.SetText(path); Toast("Путь скопирован"); })); menu.Items.Add(new Separator());
        menu.Items.Add(Menu("Передвинуть влево", () => { int i = data.Files.IndexOf(path); if (i > 0) { ShellIcons.MovePin(data.Files, path, data.Files[i - 1]); DockChanged(); } }));
        menu.Items.Add(Menu("Передвинуть вправо", () => { int i = data.Files.IndexOf(path); if (i >= 0 && i < data.Files.Count - 1) { ShellIcons.MovePin(data.Files, path, data.Files[i + 1]); DockChanged(); } }));
        menu.Items.Add(Menu("Убрать с панели", () => { data.Files.Remove(path); DockChanged(); })); b.ContextMenu = menu;
        Point start = default; bool dragging = false;
        b.PreviewMouseLeftButtonDown += (_, e) => start = e.GetPosition(b);
        b.PreviewMouseMove += (_, e) =>
        {
            if (dragging || e.LeftButton != MouseButtonState.Pressed) return; var p = e.GetPosition(b);
            if (Math.Abs(p.X - start.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(p.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            dragging = true; try { var payload = new System.Windows.DataObject(); payload.SetData(PinFormat, path); payload.SetData(DataFormats.FileDrop, new[] { path }); DragDrop.DoDragDrop(b, payload, DragDropEffects.Move | DragDropEffects.Copy | DragDropEffects.Link); } finally { dragging = false; }
        };
        b.AllowDrop = true;
        b.Drop += (_, e) => { if (e.Data.GetData(PinFormat) is string from) { ShellIcons.MovePin(data.Files, from, path); DockChanged(); e.Handled = true; } };
        return b;
    }
    static async System.Threading.Tasks.Task FillDockIcon(System.Windows.Controls.Image image, System.Windows.Shapes.Path fallback, string path)
    { var bmp = await ShellIcons.Get(path).ConfigureAwait(false); if (bmp != null && !image.Dispatcher.HasShutdownStarted) await image.Dispatcher.InvokeAsync(() => { image.Source = bmp; fallback.Visibility = Visibility.Collapsed; }); }
    Button FileTile(string path)
    {
        var b = Btn("", () => OpenPath(path)); b.Width = 337; b.Height = 75; b.Padding = new Thickness(12, 10, 12, 10); b.ToolTip = path; b.HorizontalContentAlignment = HorizontalAlignment.Left;
        var fallback = Icons.Create(Directory.Exists(path) ? "files" : "note", 32, Brush("#AC94BC")); var image = new System.Windows.Controls.Image { Width = 36, Height = 36 };
        var icon = new Grid { Width = 40, Height = 40, Margin = new Thickness(0, 0, 13, 0) }; icon.Children.Add(fallback); icon.Children.Add(image); _ = FillDockIcon(image, fallback, path);
        var name = Text(Path.GetFileName(path.TrimEnd('\\')), 14); name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis; name.MaxWidth = 235; name.FontWeight = FontWeights.SemiBold;
        var type = Text(Directory.Exists(path) ? "Папка" : Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase) ? "Ярлык" : Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase) ? "Программа" : "Файл " + Path.GetExtension(path), 11, "#8C7D98"); type.Margin = new Thickness(0, 4, 0, 0);
        b.Content = Row(icon, Stack(name, type));
        var menu = new ContextMenu(); menu.Items.Add(Menu("Открыть", () => OpenPath(path))); menu.Items.Add(Menu("Копировать путь", () => System.Windows.Clipboard.SetText(path))); menu.Items.Add(Menu("Открепить", () => { data.Files.Remove(path); DockChanged(); })); b.ContextMenu = menu; return b;
    }
    void AddDockFolder() { var d = new OpenFolderDialog { Title = "Закрепить папку на панели" }; if (d.ShowDialog(this) == true) AddPaths(new[] { d.FolderName }, false); }
    void AddDockFiles() { var d = new OpenFileDialog { Title = "Закрепить программы, ярлыки или файлы", Multiselect = true }; if (d.ShowDialog(this) == true) AddPaths(d.FileNames, false); }
    void DockChanged() { SaveSoon(); if (active == "Главная") { Page.Content = HomePage(); SyncVisualization(); } else if (active == "Файлы") Page.Content = FilesPage(); }
}
