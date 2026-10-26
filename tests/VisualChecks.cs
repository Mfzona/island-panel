using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IslandPanel;

public static class VisualChecks
{
    static SpectrumAnalyzer Tone(double frequency, double amplitude = .5)
    {
        var a = new SpectrumAnalyzer(); for (int i = 0; i < SpectrumAnalyzer.Size; i++) a.Add((float)(amplitude * Math.Sin(2 * Math.PI * frequency * i / a.SampleRate))); a.Analyze(); return a;
    }
    static int Peak(SpectrumAnalyzer a) => Array.IndexOf(a.Bands, a.Bands.Max());
    public static void Run(Action<bool, string> check)
    {
        var silent = new SpectrumAnalyzer(); silent.Feed(new float[SpectrumAnalyzer.Size]); check(silent.Analyze() && silent.Bands.All(x => x == 0) && silent.Level == 0, "FFT silence stays silent without simulated bars");
        var mid = Tone(440); check(Peak(mid) >= 11 && Peak(mid) <= 14, "440 Hz tone appears in the correct logarithmic band");
        check(Peak(Tone(120)) < Peak(Tone(4000)), "Spectrum distinguishes bass from treble");
        check(Tone(440, .8).Bands.Max() > Tone(440, .05).Bands.Max(), "Spectrum responds to real amplitude");
        var invalid = new SpectrumAnalyzer(); for (int i = 0; i < SpectrumAnalyzer.Size; i++) invalid.Add(i % 2 == 0 ? float.NaN : float.PositiveInfinity); invalid.Analyze(); check(invalid.Bands.All(float.IsFinite) && invalid.Level == 0, "Invalid audio samples cannot poison the FFT");
        mid.Reset(); check(mid.Level == 0 && mid.Bands.All(x => x == 0), "Stopping analysis clears cached samples");
        var red = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[] { 20, 20, 220, 255, 20, 20, 220, 255, 20, 20, 220, 255, 20, 20, 220, 255 }, 8);
        var color = AlbumPalette.Extract(red); check(color.R > color.G * 2 && color.R > color.B * 2, "Player accent is derived from actual cover pixels");
        var pins = new List<string> { "a", "b", "c" }; ShellIcons.MovePin(pins, "a", "c"); check(pins.SequenceEqual(new[] { "b", "c", "a" }), "Dock pins reorder without moving files");
        ShellIcons.MovePin(pins, "missing", "b"); check(pins.Count == 3 && pins.Distinct().Count() == 3, "Invalid dock drag preserves pins");
        var existing = new Data { Files = new() { "custom-path" } }; ShellIcons.SeedDock(existing); check(existing.Files.SequenceEqual(new[] { "custom-path" }) && existing.DockInitialized && !ShellIcons.SeedDock(existing), "Dock migration preserves existing user pins");
        var icon = ShellIcons.Get(Environment.GetFolderPath(Environment.SpecialFolder.Windows)).GetAwaiter().GetResult(); check(icon != null && icon.IsFrozen, "Native Windows folder icon loads and is cached safely");
        using var live = new LoopbackSpectrum(); live.SetActiveAsync(true).GetAwaiter().GetResult();
        check(live.IsRunning, "Real WASAPI output loopback starts: " + (live.Error ?? "ok"));
        System.Threading.Thread.Sleep(220); var bands = new float[SpectrumAnalyzer.BandCount]; var wave = new float[SpectrumAnalyzer.WaveCount]; live.CopyFrame(bands, wave, out float level);
        check(bands.All(x => float.IsFinite(x) && x >= 0 && x <= 1) && float.IsFinite(level), "Live loopback produces valid spectrum values");
        Console.WriteLine("Loopback audio frames received: " + live.AudioFrames);
        live.SetActiveAsync(false).GetAwaiter().GetResult(); check(!live.IsRunning && !live.Requested, "Loopback releases the output device when stopped");
    }
}
