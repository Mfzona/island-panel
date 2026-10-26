using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IslandPanel;
public class Clip
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public string? Image { get; set; }
    public bool Pinned { get; set; }
    public DateTime At { get; set; } = DateTime.Now;
}
public class Note { public string Title { get; set; } = "Заметка"; public string Text { get; set; } = ""; }
public class Reminder { public string Text { get; set; } = ""; public DateTime At { get; set; } public bool Done { get; set; } }
public class Settings
{
    public bool CaptureClipboard { get; set; } = true;
    public bool Topmost { get; set; }
    public bool AutoCollapse { get; set; } = true;
    public int Monitor { get; set; }
    public int Offset { get; set; } = 8;
    public int TimerMinutes { get; set; } = 25;
    public string City { get; set; } = "";
    public int Fps { get; set; } = 30;
    public bool StartCompact { get; set; } = true;
    public bool Eyes { get; set; } = true;
    public bool Animations { get; set; } = true;
    public bool LowPower { get; set; }
    public bool HoverExpand { get; set; }
    public bool Visualizer { get; set; } = true;
    public string VisualizerStyle { get; set; } = "Спектр";
}
public class Data
{
    public Settings Settings { get; set; } = new();
    public List<Clip> Clips { get; set; } = new();
    public List<Note> Notes { get; set; } = new() { new Note { Title = "Заметка" } };
    public List<string> Files { get; set; } = new();
    public List<Reminder> Reminders { get; set; } = new();
    public DateTime? TimerEnd { get; set; }
    public double TimerLeft { get; set; } = 1500;
    public string TextDraft { get; set; } = "";
    public bool DockInitialized { get; set; }
}
public static class Store
{
    public static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
    public static readonly string Folder = Path.Combine(Root, "data");
    public static readonly string Captures = Path.Combine(Root, "captures");
    public static readonly string Images = Path.Combine(Folder, "clipboard");
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    static Store() { Directory.CreateDirectory(Folder); Directory.CreateDirectory(Captures); Directory.CreateDirectory(Images); }
    public static Data Load()
    {
        try { return JsonSerializer.Deserialize<Data>(File.ReadAllText(Path.Combine(Folder, "state.json"))) ?? new(); }
        catch (FileNotFoundException) { return new(); }
        catch (Exception e) { Log(e); return new(); }
    }
    public static void Save(Data data)
    {
        try { string p = Path.Combine(Folder, "state.json"); File.WriteAllText(p + ".tmp", JsonSerializer.Serialize(data, Options)); File.Move(p + ".tmp", p, true); }
        catch (Exception e) { Log(e); }
    }
    public static void Log(Exception e) { try { File.AppendAllText(Path.Combine(Folder, "error.log"), DateTime.Now + " " + e + Environment.NewLine); } catch { } }
}
public static class TextTools
{
    public static string Transform(string text, string op)
    {
        const string en = "`qwertyuiop[]asdfghjkl;'zxcvbnm,./~QWERTYUIOP{}ASDFGHJKL:\"ZXCVBNM<>?";
        const string ru = "ёйцукенгшщзхъфывапролджэячсмитьбю.ЁЙЦУКЕНГШЩЗХЪФЫВАПРОЛДЖЭЯЧСМИТЬБЮ,";
        if (op == "Раскладка")
        {
            bool toRu = text.Count(x => ru.Contains(x)) < text.Count(x => en.Contains(x));
            return new string(text.Select(c => { int i = (toRu ? en : ru).IndexOf(c); return i < 0 ? c : (toRu ? ru : en)[i]; }).ToArray());
        }
        return op switch
        {
            "ВЕРХНИЙ" => text.ToUpperInvariant(), "нижний" => text.ToLowerInvariant(),
            "Каждое Слово" => CultureInfo.GetCultureInfo("ru-RU").TextInfo.ToTitleCase(text.ToLowerInvariant()),
            "Предложение" => Regex.Replace(text.ToLowerInvariant(), @"(^\s*\p{L}|[.!?]\s+\p{L})", m => m.Value.ToUpperInvariant()),
            "Транслит" => Transliterate(text),
            "Убрать переносы" => Regex.Replace(text, @"\s*\r?\n\s*", " "),
            "Лишние пробелы" => Regex.Replace(text, @"[\t ]+", " ").Trim(),
            "JSON красиво" => JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(text), new JsonSerializerOptions { WriteIndented = true }),
            "JSON в строку" => JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(text)),
            "→ Base64" => Convert.ToBase64String(Encoding.UTF8.GetBytes(text)),
            "Base64 →" => Encoding.UTF8.GetString(Convert.FromBase64String(text)),
            "→ URL" => Uri.EscapeDataString(text), "URL →" => Uri.UnescapeDataString(text),
            "Чистый текст" => Regex.Replace(text, "<[^>]*>", ""),
            _ => text
        };
    }
    static string Transliterate(string s)
    {
        string abc = "абвгдеёжзийклмнопрстуфхцчшщъыьэюя";
        string[] repl = "a b v g d e yo zh z i y k l m n o p r s t u f kh ts ch sh shch | y | e yu ya".Split(' ');
        return string.Concat(s.Select(c => { int i = abc.IndexOf(char.ToLowerInvariant(c)); if (i < 0) return c.ToString(); string r = repl[i] == "|" ? "" : repl[i]; return char.IsUpper(c) ? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(r) : r; }));
    }
    public static string? Calculate(string text)
    {
        string q = text.Trim().ToLowerInvariant().Replace(",", ".");
        var m = Regex.Match(q, @"^(-?\d+(?:\.\d+)?)\s*([\p{L}°]+)\s+(?:в|to|in)\s+([\p{L}°]+)$");
        if (m.Success)
        {
            double n = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            string a = m.Groups[2].Value, b = m.Groups[3].Value;
            var units = new Dictionary<string, (string kind, double scale)>();
            void Add(string names, string kind, double scale) { foreach (var name in names.Split('|')) units[name] = (kind, scale); }
            Add("км|km|километр|километры|километров", "length", 1000); Add("м|m|метр|метры|метров", "length", 1); Add("см|cm", "length", .01); Add("мм|mm", "length", .001); Add("миль|мили|миля|mi|mile|miles", "length", 1609.344); Add("фут|футов|ft", "length", .3048); Add("дюйм|дюймов|in", "length", .0254);
            Add("кг|kg", "mass", 1); Add("г|g", "mass", .001); Add("lb|lbs|фунт|фунтов", "mass", .45359237);
            Add("ч|час|часов|h", "time", 3600); Add("мин|минут|mинуты|min", "time", 60); Add("сек|с|s", "time", 1);
            if (units.TryGetValue(a, out var ua) && units.TryGetValue(b, out var ub) && ua.kind == ub.kind) return (n * ua.scale / ub.scale).ToString("0.########", CultureInfo.CurrentCulture) + " " + b;
            if ((a == "c" || a == "°c") && (b == "f" || b == "°f")) return (n * 1.8 + 32).ToString("0.##") + " °F";
            if ((a == "f" || a == "°f") && (b == "c" || b == "°c")) return ((n - 32) / 1.8).ToString("0.##") + " °C";
        }
        if (q.Length > 200 || !Regex.IsMatch(q, @"^[\d\s.+*/()%\-]+$") || !Regex.IsMatch(q, @"\d")) return null;
        try { object v = new DataTable().Compute(q.Replace("%", "/100"), ""); double d = Convert.ToDouble(v); return double.IsFinite(d) ? d.ToString("0.##########", CultureInfo.CurrentCulture) : null; } catch { return null; }
    }
    public static Reminder? ParseReminder(string text)
    {
        var m = Regex.Match(text, @"через\s+(\d+)\s*(мин\w*|час\w*|сек\w*)", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out int n) && n > 0 && n <= 100000)
        {
            double seconds = n * (m.Groups[2].Value.StartsWith("час", StringComparison.OrdinalIgnoreCase) ? 3600d : m.Groups[2].Value.StartsWith("сек", StringComparison.OrdinalIgnoreCase) ? 1d : 60d);
            string label = (text.Remove(m.Index, m.Length)).Trim(' ', ',', ':', '—');
            return new() { Text = label.Length == 0 ? "Напоминание" : label, At = DateTime.Now.AddSeconds(seconds) };
        }
        m = Regex.Match(text, @"(?:завтра\s+)?в\s+(\d{1,2})(?::(\d{2}))?", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out int h) && h < 24)
        {
            int min = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0; if (min > 59) return null;
            DateTime at = DateTime.Today.AddHours(h).AddMinutes(min); if (text.Contains("завтра", StringComparison.OrdinalIgnoreCase) || at <= DateTime.Now) at = at.AddDays(1);
            string label = text.Remove(m.Index, m.Length).Trim(' ', ',', ':');
            return new() { Text = label.Length == 0 ? "Напоминание" : label, At = at };
        }
        return null;
    }
}
