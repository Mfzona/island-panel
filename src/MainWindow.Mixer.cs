using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace IslandPanel;
public partial class MainWindow
{
    sealed class MixerApp
    {
        readonly MixerSession[] streams;
        public MixerApp(IEnumerable<MixerSession> sessions) { streams = sessions.ToArray(); }
        public string Name => streams[0].Name;
        public float Volume => streams.Average(s => s.Volume);
        public bool Muted => streams.All(s => s.Muted);
        public bool Active => streams.Any(s => s.Active);
        public void SetVolume(float value) { foreach (var s in streams) s.SetVolume(value); }
        public void SetMuted(bool value) { foreach (var s in streams) s.SetMuted(value); }
    }
    readonly List<MixerSession> mixerSessions = new();
    readonly List<(MixerApp Session, Slider Slider, TextBlock Label, Button Mute, TextBlock State)> mixerRows = new();
    StackPanel? mixerList;
    Slider? masterSlider;
    TextBlock? masterLabel;
    Button? masterMute;
    bool mixerSync, mixerDragging;
    string mixerSignature = "";
    void ReleaseMixer()
    {
        foreach (var s in mixerSessions) s.Dispose(); mixerSessions.Clear(); mixerRows.Clear(); mixerSignature = ""; mixerList = null; masterSlider = null; masterLabel = null; masterMute = null; mixerDragging = false;
    }
    UIElement MixerPage()
    {
        ReleaseMixer();
        var root = new DockPanel();
        var refresh = Btn("Обновить", () => RefreshMixer(true));
        var title = new DockPanel(); DockPanel.SetDock(refresh, Dock.Right); title.Children.Add(refresh); title.Children.Add(Text("Микшер громкости", 21));
        title.Margin = new Thickness(5, 0, 5, 8); DockPanel.SetDock(title, Dock.Top); root.Children.Add(title);
        masterSlider = new Slider { Minimum = 0, Maximum = 100, MinWidth = 180, Margin = new Thickness(15, 0, 12, 0) };
        masterLabel = Text("—", 12, "#AFA5BA"); masterLabel.Width = 38;
        masterMute = Btn("Звук", () => { try { Audio.ToggleMute(); RefreshMixer(); } catch { Toast("Устройство вывода недоступно"); } }); masterMute.Width = 95;
        var master = BuildMixerRow(Stack(Heading("ОБЩАЯ ГРОМКОСТЬ"), Text("Устройство по умолчанию", 12)), masterSlider, masterLabel, masterMute);
        var masterCard = Card(master, "#17131B", 11); DockPanel.SetDock(masterCard, Dock.Top); root.Children.Add(masterCard);
        WatchMixerDrag(masterSlider);
        masterSlider.ValueChanged += (_, _) => { if (mixerSync) return; try { Audio.Set((float)masterSlider.Value); masterLabel.Text = Math.Round(masterSlider.Value) + "%"; } catch { Toast("Не удалось изменить общую громкость"); } };
        var hint = Text("Приложения на текущем устройстве вывода. Если приложения нет — включите в нём звук.\nСписок обновляется только при открытом микшере.", 10, "#82798D"); hint.Margin = new Thickness(8); DockPanel.SetDock(hint, Dock.Bottom); root.Children.Add(hint);
        mixerList = new StackPanel(); root.Children.Add(Scroll(mixerList)); RefreshMixer(true); return root;
    }
    static Grid BuildMixerRow(UIElement name, UIElement slider, UIElement label, UIElement mute)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(1.1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(42) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        int i = 0; foreach (var c in new[] { name, slider, label, mute }) { Grid.SetColumn(c, i++); row.Children.Add(c); } return row;
    }
    void WatchMixerDrag(Slider slider)
    {
        slider.PreviewMouseLeftButtonDown += (_, _) => mixerDragging = true;
        slider.PreviewMouseLeftButtonUp += (_, _) => mixerDragging = false;
        slider.LostMouseCapture += (_, _) => mixerDragging = false;
    }
    void RefreshMixer(bool rebuild = false)
    {
        if (mixerList == null || masterSlider == null || mixerDragging || compact || active != "Микшер") return;
        mixerSync = true;
        try
        {
            masterSlider.Value = Audio.Get(); masterLabel!.Text = Math.Round(masterSlider.Value) + "%";
            masterMute!.Content = Audio.Muted() ? "Без звука" : "Звук вкл.";
            var fresh = Mixer.Sessions().OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(s => s.Id).ToList();
            string signature = string.Join("\n", fresh.Select(s => s.Id));
            if (rebuild || signature != mixerSignature)
            {
                foreach (var s in mixerSessions) s.Dispose(); mixerSessions.Clear(); mixerSessions.AddRange(fresh); mixerRows.Clear(); mixerList.Children.Clear(); mixerSignature = signature;
        foreach (var group in mixerSessions.GroupBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    var s = new MixerApp(group);
                    var name = Text(s.Name, 13); name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis; name.MaxWidth = 230; name.ToolTip = s.Name;
                    var state = Text("", 10, "#807888"); state.Margin = new Thickness(0, 3, 0, 0);
                    var slider = new Slider { Minimum = 0, Maximum = 100, Margin = new Thickness(15, 0, 12, 0), ToolTip = s.Name };
                    var label = Text("", 12, "#AEA4B8");
                    var mute = Btn("", () => { try { s.SetMuted(!s.Muted); RefreshMixer(); } catch { Toast("Аудиосессия уже закрыта"); } }); mute.Width = 95; mute.Padding = new Thickness(10, 6, 10, 6);
                    WatchMixerDrag(slider);
                    slider.ValueChanged += (_, _) => { if (mixerSync) return; try { s.SetVolume((float)slider.Value); label.Text = Math.Round(slider.Value) + "%"; } catch { Toast("Аудиосессия уже закрыта"); } };
                    mixerList.Children.Add(Card(BuildMixerRow(Stack(name, state), slider, label, mute), "#111113", 8));
                    mixerRows.Add((s, slider, label, mute, state));
                }
                if (mixerSessions.Count == 0) mixerList.Children.Add(Card(Text("Нет звуковых сессий. Запустите плеер, браузер или игру.", 13, "#A79AAF")));
            }
            else foreach (var s in fresh) s.Dispose();
            foreach (var row in mixerRows)
            {
                try { row.Slider.Value = row.Session.Volume; row.Label.Text = Math.Round(row.Slider.Value) + "%"; bool muted = row.Session.Muted; row.Mute.Content = muted ? "Без звука" : "Звук вкл."; row.Mute.Background = Brush(muted ? "#3B2630" : "#1D1A21"); row.State.Text = row.Session.Active ? "Воспроизводит звук" : "Неактивно"; }
                catch (System.Runtime.InteropServices.COMException) { row.State.Text = "Сессия закрыта"; }
            }
        }
        catch (Exception ex) { Store.Log(ex); Toast("Микшер временно недоступен. Проверьте устройство вывода."); }
        finally { mixerSync = false; }
    }
}
