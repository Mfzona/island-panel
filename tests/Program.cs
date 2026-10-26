using System;
using System.Globalization;
using System.Text.Json;
using System.Linq;
using IslandPanel;
using QRCoder;
using Windows.Media.Control;
using Windows.Media.Ocr;

class Program
{
    static int count;
    static void Assert(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); count++; Console.WriteLine("PASS: " + name); }
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var quietDefaults = new Settings();
            Assert(!quietDefaults.Topmost && !quietDefaults.HoverExpand && quietDefaults.StartCompact, "New installations start quietly without always-on-top or hover expansion");
            Assert(Audio.Get() >= 0, "Master audio initializes before loopback without COM conflicts");
            VisualChecks.Run(Assert);
            Assert(TextTools.Transform("ghbdtn", "Раскладка") == "привет", "EN to RU layout");
            Assert(TextTools.Transform("руддщ", "Раскладка") == "hello", "RU to EN layout");
            var large = new string('q', 100_000); var watch = System.Diagnostics.Stopwatch.StartNew(); var converted = TextTools.Transform(large, "Раскладка");
            Assert(converted.Length == large.Length && converted[0] == 'й' && watch.Elapsed.TotalSeconds < 2, "Large clipboard linear-time layout conversion");
            Assert(TextTools.Transform("привет мир", "ВЕРХНИЙ") == "ПРИВЕТ МИР", "Unicode uppercase");
            Assert(TextTools.Transform(TextTools.Transform("Привет 👋", "→ Base64"), "Base64 →") == "Привет 👋", "Base64 Unicode roundtrip");
            Assert(TextTools.Transform(TextTools.Transform("текст /?=+", "→ URL"), "URL →") == "текст /?=+", "URL Unicode roundtrip");
            Assert(TextTools.Transform("a   b\t c", "Лишние пробелы") == "a b c", "Whitespace cleanup");
            Assert(TextTools.Transform("a\r\nb\nc", "Убрать переносы") == "a b c", "Newline cleanup");
            Assert(TextTools.Transform("Привет мир", "Транслит") == "Privet mir", "Transliteration");
            Assert(TextTools.Transform("{\"a\": 2}", "JSON в строку") == "{\"a\":2}", "JSON minify");
            Assert(TextTools.Transform("{\"a\":2}", "JSON красиво").Contains("\n"), "JSON pretty");
            bool badJson = false; try { TextTools.Transform("oops", "JSON красиво"); } catch (JsonException) { badJson = true; } Assert(badJson, "Bad JSON rejected");
            Assert(TextTools.Calculate("(24+6)*3") == "90", "Arithmetic precedence");
            Assert(TextTools.Calculate("5 миль в км") == "8.04672 км", "Miles to kilometres");
            Assert(TextTools.Calculate("72 °f в °c") == "22.22 °C", "Fahrenheit to Celsius");
            Assert(TextTools.Calculate("10 кг в lb") == "22.04622622 lb", "Mass conversion");
            Assert(TextTools.Calculate("1 км в kg") == null, "Incompatible units rejected");
            Assert(TextTools.Calculate("Process.Start('x')") == null, "No arbitrary code evaluation");
            Assert(TextTools.Calculate("1/0") == null, "Division by zero rejected");
            var reminder = TextTools.ParseReminder("через 20 минут чай");
            Assert(reminder != null && reminder.Text == "чай" && Math.Abs((reminder.At - DateTime.Now).TotalMinutes - 20) < .1, "Relative reminders");
            var tomorrow = TextTools.ParseReminder("позвонить завтра в 9:30");
            Assert(tomorrow != null && tomorrow.At.Date == DateTime.Today.AddDays(1) && tomorrow.At.Hour == 9 && tomorrow.At.Minute == 30, "Tomorrow reminder");
            Assert(TextTools.ParseReminder("в 25:00 звонок") == null, "Invalid hour rejected");
            Assert(TextTools.ParseReminder("через 0 минут чай") == null, "Zero delay rejected");
            var d = new Data(); d.Notes[0].Text = "Русский текст"; d.TimerEnd = DateTime.Now.AddMinutes(5); d.Clips.Add(new Clip { Text = "тест", Pinned = true });
            var roundtrip = JsonSerializer.Deserialize<Data>(JsonSerializer.Serialize(d));
            Assert(roundtrip?.Notes[0].Text == "Русский текст" && roundtrip.Clips[0].Pinned && roundtrip.TimerEnd == d.TimerEnd, "State roundtrip");
            using var gen = new QRCodeGenerator(); using var code = gen.CreateQrCode("https://example.com/тест", QRCodeGenerator.ECCLevel.M); using var qr = new PngByteQRCode(code);
            Assert(qr.GetGraphic(8).Length > 100, "QR PNG generation");
            float volume = Audio.Get(); Assert(volume >= 0 && volume <= 100, "Real Windows audio endpoint");
            var sessions = Mixer.Sessions();
            try
            {
                Assert(sessions.All(s => !string.IsNullOrWhiteSpace(s.Id)) && sessions.Select(s => s.Id).Distinct().Count() == sessions.Count, "Windows mixer enumerates distinct audio sessions");
                Assert(sessions.All(s => s.Volume >= 0 && s.Volume <= 100), "Per-application volume can be read");
                if (sessions.Count > 0) { var s = sessions[0]; float level = s.Volume; bool muted = s.Muted; s.SetVolume(level); s.SetMuted(muted); Assert(Math.Abs(s.Volume - level) < .01 && s.Muted == muted, "Per-application setters roundtrip unchanged volume and mute"); }
                Console.WriteLine("Mixer sessions: " + sessions.Count);
            }
            finally { foreach (var s in sessions) s.Dispose(); }
            Console.WriteLine("OCR languages: " + OcrEngine.AvailableRecognizerLanguages.Count);
            var manager = GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask().GetAwaiter().GetResult();
            Assert(manager != null, "Windows media session API");
            if (args.Contains("--ui")) UiChecks.Run(Assert, args.LastOrDefault() == "--ui" ? null : args.LastOrDefault());
            Console.WriteLine($"SUCCESS: {count} checks passed"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
