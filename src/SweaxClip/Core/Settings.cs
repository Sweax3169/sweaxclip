using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SweaxClip.Core;

public sealed class Settings
{
    public string Language { get; set; } = "auto";
    public bool StartWithWindows { get; set; } = true;
    public string Hotkey { get; set; } = "Win+V";
    public bool PastePlainByDefault { get; set; }
    public bool MoveToTopOnPaste { get; set; } = true;
    public bool CaptureText { get; set; } = true;
    public bool CaptureImages { get; set; } = true;
    public bool CaptureFiles { get; set; } = true;
    public bool WatchScreenshots { get; set; } = true;
    public bool ScreenshotsImported { get; set; }
    public bool Ocr { get; set; } = true;
    public bool RespectPrivateFlags { get; set; } = true;
    public bool DailyBackups { get; set; } = true;
    public List<string> IgnoredApps { get; set; } = ["keepass.exe", "keepassxc.exe", "1password.exe", "bitwarden.exe", "lastpass.exe", "dashlane.exe", "enpass.exe"];
    public bool Paused { get; set; }
    public string? LastBackupDay { get; set; }
    public double LibraryWidth { get; set; } = 1280;
    public double LibraryHeight { get; set; } = 800;
    public bool LibraryGrid { get; set; } = true;

    [JsonIgnore] public string FilePath { get; private set; } = "";

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static Settings Load(string dir)
    {
        string path = Path.Combine(dir, "settings.json");
        Settings s;
        try { s = File.Exists(path) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Json) ?? new() : new(); }
        catch { s = new(); }
        s.FilePath = path;
        return s;
    }

    public void Save()
    {
        try
        {
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, FilePath, true);
        }
        catch { }
    }
}

/// <summary>Where the archive lives. A portable "data" folder next to the exe wins, so the whole thing can live on a USB stick.</summary>
public static class Paths
{
    public static string Data { get; private set; } = "";
    public static string Db => Path.Combine(Data, "sweaxclip.db");
    public static string Blobs => Path.Combine(Data, "blobs");
    public static string Thumbs => Path.Combine(Data, "thumbs");
    public static string Backups => Path.Combine(Data, "backups");
    public static string Logs => Path.Combine(Data, "logs");

    public static void Init(string? overrideDir = null)
    {
        string exeDir = AppContext.BaseDirectory;
        string portable = Path.Combine(exeDir, "sweaxclip-data");
        Data = overrideDir
               ?? (Directory.Exists(portable) ? portable
               : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SweaxClip"));
        foreach (var d in new[] { Data, Blobs, Thumbs, Backups, Logs }) Directory.CreateDirectory(d);
    }

    public static string Abs(string rel) => Path.Combine(Data, rel);

    public static void Log(string msg)
    {
        try
        {
            string f = Path.Combine(Logs, "sweaxclip.log");
            if (File.Exists(f) && new FileInfo(f).Length > 1_000_000) File.Move(f, f + ".old", true);
            File.AppendAllText(f, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {msg}{Environment.NewLine}");
        }
        catch { }
    }
}
