using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace IslandPanel;
public partial class App : System.Windows.Application
{
    void ButtonLoaded(object sender, RoutedEventArgs e) { if (sender is System.Windows.Controls.Button button) Motion.WireButton(button); }
    private Mutex? singleton;
    public bool WindowSuppliedByHost { get; set; }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (WindowSuppliedByHost) return;
        if (Array.IndexOf(e.Args, "--exit") >= 0) { Native.SignalClose(); Shutdown(); return; }
        singleton = new Mutex(true, "Local\\IslandPanel.SelfMade.v1", out bool first);
        if (!first) { Native.SignalExisting(); Shutdown(); return; }
        DispatcherUnhandledException += (_, a) => { Store.Log(a.Exception); a.Handled = true; if (MainWindow is MainWindow w) w.Toast("Не удалось выполнить действие. Подробности — data\\error.log"); };
        try { MainWindow = new MainWindow(); MainWindow.Show(); }
        catch (Exception ex) { Store.Log(ex); System.Windows.MessageBox.Show(ex.Message, "Island Panel"); Shutdown(); }
    }
    protected override void OnExit(ExitEventArgs e) { singleton?.Dispose(); base.OnExit(e); }
}
