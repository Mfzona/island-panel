using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
static class Launcher
{
    [STAThread] static void Main()
    {
        string root = AppDomain.CurrentDomain.BaseDirectory;
        string exe = Path.Combine(root, "app", "IslandPanel.exe");
        if (!File.Exists(exe)) { MessageBox.Show("Не найден app\\IslandPanel.exe. Сохраните всю папку программы целиком.", "Island Panel"); return; }
        try { Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = root, UseShellExecute = true }); }
        catch (Exception e) { MessageBox.Show(e.Message, "Island Panel"); }
    }
}
