using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Windows.Media.Control;

namespace IslandPanel;
public partial class MainWindow
{
    readonly LoopbackSpectrum spectrum = new();
    readonly List<WeakReference<SpectrumView>> spectrumViews = new();
    Color albumAccent = AlbumPalette.Default;
    Border? playerSurface;
    Button? playbackButton;
    TextBlock? remainingTime, visualizationStatus;
    bool PlaybackActive()
    { try { return media?.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing; } catch { return false; } }
    void SyncVisualization()
    {
        bool wanted = !closing && !compact && IsVisible && data.Settings.Visualizer && (active == "Визуализация" || (active == "Главная" || active == "Музыка") && PlaybackActive());
        spectrum.LowPower = data.Settings.LowPower; _ = spectrum.SetActiveAsync(wanted);
        for (int i = spectrumViews.Count - 1; i >= 0; i--) { if (spectrumViews[i].TryGetTarget(out var view)) view.Configure(spectrum, wanted, albumAccent, data.Settings.VisualizerStyle == "Волна"); else spectrumViews.RemoveAt(i); }
        if (active == "Визуализация" && visualizationStatus != null) visualizationStatus.Text = !data.Settings.Visualizer ? "Визуализация выключена в настройках" : spectrum.Error != null ? "Звук недоступен: " + spectrum.Error : spectrum.IsRunning ? "Системный звук · локальный анализ · без записи" : "Подключение к устройству вывода…";
    }
    SpectrumView CreateSpectrum(double height, double width = double.NaN)
    {
        var v = new SpectrumView { Height = height, Width = width, Margin = new Thickness(0, 3, 0, 0) }; spectrumViews.Add(new(v));
        v.Configure(spectrum, false, albumAccent, data.Settings.VisualizerStyle == "Волна"); return v;
    }
    UIElement VisualizationPage()
    {
        var title = Text("Музыка в движении", 23); title.FontWeight = FontWeights.SemiBold;
        var modes = Row(); foreach (string mode in new[] { "Спектр", "Волна" }) { var b = Btn(mode, () => { data.Settings.VisualizerStyle = mode; SaveSoon(); ShowPage("Визуализация"); }); b.Background = Brush(data.Settings.VisualizerStyle == mode ? "#3C2A3A" : "#171519"); modes.Children.Add(b); }
        var top = new DockPanel(); DockPanel.SetDock(modes, Dock.Right); top.Children.Add(modes); top.Children.Add(title);
        visualizationStatus = Text("", 11, "#A196AC"); visualizationStatus.Margin = new Thickness(4, 8, 4, 10);
        var canvas = Card(CreateSpectrum(175), "#131016", 20); canvas.Background = AlbumPalette.Background(albumAccent);
        return Stack(top, visualizationStatus, canvas, Text("Реальный спектр звука устройства вывода. В тишине полосы затихают.\nВ свёрнутом состоянии и на других вкладках анализ звука отключён.", 11, "#827788"));
    }
    UIElement PlayerCard(bool large)
    {
        int size = large ? 96 : 88;
        album = new System.Windows.Controls.Image { Width = size, Height = size, Source = cachedCover, Stretch = Stretch.UniformToFill, Clip = new RectangleGeometry(new Rect(0, 0, size, size), 14, 14) };
        var cover = new Grid { Width = size, Height = size, Margin = new Thickness(0, 0, 15, 0) }; cover.Children.Add(new Border { CornerRadius = new CornerRadius(14), Background = AlbumPalette.Background(albumAccent), Child = Icons.Create("music", 30, Brush("#CFB9D1")) }); cover.Children.Add(album);
        cover.Children.Add(new Border { BorderBrush = Brush("#25FFFFFF"), BorderThickness = new Thickness(.7), CornerRadius = new CornerRadius(14), IsHitTestVisible = false });
        string player = "СЕЙЧАС ИГРАЕТ"; try { string id = media?.SourceAppUserModelId ?? ""; if (id.Contains("spotify", StringComparison.OrdinalIgnoreCase)) player = "SPOTIFY"; else if (!string.IsNullOrWhiteSpace(id)) player = System.IO.Path.GetFileNameWithoutExtension(id).ToUpperInvariant(); } catch { }
        var tag = Heading(player.Length > 22 ? "СЕЙЧАС ИГРАЕТ" : player); tag.Margin = new Thickness(0, 0, 0, 5);
        trackTitle = Text(string.IsNullOrWhiteSpace(latestTrack) ? "Пока ничего не играет" : latestTrack, large ? 21 : 17); trackTitle.FontWeight = FontWeights.SemiBold; trackTitle.MaxWidth = large ? 470 : 265; trackTitle.TextWrapping = TextWrapping.NoWrap; trackTitle.TextTrimming = TextTrimming.CharacterEllipsis;
        trackTitle.HorizontalAlignment = HorizontalAlignment.Left;
        trackArtist = Text(cachedArtist, 12, "#ACA0AF"); trackArtist.TextWrapping = TextWrapping.NoWrap; trackArtist.MaxWidth = large ? 470 : 265; trackArtist.TextTrimming = TextTrimming.CharacterEllipsis;
        trackArtist.HorizontalAlignment = HorizontalAlignment.Left;
        var mini = CreateSpectrum(19, 115); mini.HorizontalAlignment = HorizontalAlignment.Left; mini.Margin = new Thickness(0, 8, 0, 0);
        var info = Stack(tag, trackTitle, trackArtist, mini); info.VerticalAlignment = VerticalAlignment.Center;
        var header = new Grid(); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); header.ColumnDefinitions.Add(new()); Grid.SetColumn(info, 1); header.Children.Add(cover); header.Children.Add(info);
        mediaProgress = new Slider { Minimum = 0, Maximum = 1, Foreground = new SolidColorBrush(albumAccent), Margin = new Thickness(0, 1, 0, 0) };
        mediaProgress.PreviewMouseDown += (_, _) => seeking = true;
        mediaProgress.PreviewMouseUp += async (_, _) => { try { if (media != null) await media.TryChangePlaybackPositionAsync(TimeSpan.FromSeconds(mediaProgress.Value).Ticks); } catch { Toast("Перемотка недоступна в этом плеере"); } finally { seeking = false; } };
        trackTime = Text("—", 10, "#827585"); remainingTime = Text("—", 10, "#827585"); remainingTime.HorizontalAlignment = HorizontalAlignment.Right;
        var times = new Grid(); times.Children.Add(trackTime); times.Children.Add(remainingTime); times.Margin = new Thickness(0, -5, 0, 1);
        var previous = Btn("", () => _ = MediaAction(0)); previous.Content = Icons.Create("previous", 19); previous.Background = previous.BorderBrush = Brushes.Transparent;
        var next = Btn("", () => _ = MediaAction(2)); next.Content = Icons.Create("next", 19); next.Background = next.BorderBrush = Brushes.Transparent;
        playbackButton = Btn("", () => _ = MediaAction(1)); playbackButton.Style = (Style)System.Windows.Application.Current.FindResource("PlaybackButton"); playbackButton.Margin = new Thickness(12, 0, 12, 0);
        var controls = Row(previous, playbackButton, next); controls.HorizontalAlignment = HorizontalAlignment.Center;
        playerSurface = Card(Stack(header, mediaProgress, times, controls), "#151115", 13); playerSurface.Background = AlbumPalette.Background(albumAccent); playerSurface.Height = large ? 222 : 212;
        UpdatePlaybackButton(); UpdateMediaTimeline(); return playerSurface;
    }
    void UpdatePlaybackButton()
    {
        if (playbackButton == null) return; bool playing = PlaybackActive();
        if (playbackButton.Content == null || playbackButton.Tag is not bool old || old != playing) { playbackButton.Tag = playing; playbackButton.Content = Icons.Create(playing ? "pause" : "play", 16, Brushes.Black); }
    }
    void PaintAlbumTheme()
    {
        albumAccent = AlbumPalette.Extract(cachedCover);
        if (playerSurface != null) playerSurface.Background = AlbumPalette.Background(albumAccent);
        if (mediaProgress != null) { var b = new SolidColorBrush(albumAccent); b.Freeze(); mediaProgress.Foreground = b; }
        SyncVisualization();
    }
}
