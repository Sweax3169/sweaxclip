using System.IO;
using System.IO.Compression;
using Microsoft.Win32;

namespace SweaxClip.Core;

public static class AutoStart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "SweaxClip";

    static string Exe => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "SweaxClip.exe");

    public static bool IsEnabled()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(Name) is string s && s.Contains(Exe, StringComparison.OrdinalIgnoreCase);
    }

    public static void Set(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey, true);
            if (on) k.SetValue(Name, $"\"{Exe}\" --background");
            else k.DeleteValue(Name, false);
        }
        catch (Exception ex) { Paths.Log("autostart: " + ex.Message); }
    }

    /// <summary>Start menu entry so "SweaxClip" can be found from Windows search.</summary>
    public static void EnsureStartMenuShortcut()
    {
        try
        {
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            string lnk = Path.Combine(dir, "SweaxClip.lnk");
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic sc = shell.CreateShortcut(lnk);
            if (File.Exists(lnk) && string.Equals((string)sc.TargetPath, Exe, StringComparison.OrdinalIgnoreCase)) return;
            sc.TargetPath = Exe;
            sc.WorkingDirectory = Path.GetDirectoryName(Exe);
            sc.Description = "SweaxClip — a clipboard that never forgets";
            sc.IconLocation = Exe + ",0";
            sc.Save();
        }
        catch (Exception ex) { Paths.Log("shortcut: " + ex.Message); }
    }
}

/// <summary>
/// Copies the app to %LOCALAPPDATA%\Programs\SweaxClip the first time it is started from somewhere
/// temporary (Downloads, Desktop, a zip preview…), so deleting the download never breaks it.
/// </summary>
public static class Installer
{
    public static string Home => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "SweaxClip");

    /// <summary>Returns the installed exe when a copy was made, otherwise null.</summary>
    public static string? RelocateIfNeeded()
    {
        try
        {
            string here = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
            if (string.Equals(here, Home, StringComparison.OrdinalIgnoreCase)) return null;
            if (Directory.Exists(Path.Combine(here, "sweaxclip-data"))) return null; // portable mode
            if (here.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase) || here.Contains(@"\artifacts\", StringComparison.OrdinalIgnoreCase)) return null; // dev build
            string exe = Environment.ProcessPath!;
            Directory.CreateDirectory(Home);
            foreach (var f in Directory.EnumerateFiles(here))
            {
                string name = Path.GetFileName(f);
                if (name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)) continue;
                File.Copy(f, Path.Combine(Home, name), true);
            }
            return Path.Combine(Home, Path.GetFileName(exe));
        }
        catch (Exception ex)
        {
            Paths.Log("install: " + ex.Message);
            return null;
        }
    }
}

public static class Backup
{
    /// <summary>Consistent snapshot of the database. Image files are immutable and named by their hash, so they never need copying.</summary>
    public static string Snapshot(ClipStore store)
    {
        string file = Path.Combine(Paths.Backups, $"sweaxclip-{DateTime.Now:yyyy-MM-dd}.db");
        store.BackupTo(file);
        Prune();
        return file;
    }

    public static void DailyIfDue(ClipStore store, Settings settings)
    {
        string today = DateTime.Now.ToString("yyyy-MM-dd");
        if (!settings.DailyBackups || settings.LastBackupDay == today) return;
        try
        {
            Snapshot(store);
            settings.LastBackupDay = today;
            settings.Save();
        }
        catch (Exception ex) { Paths.Log("backup: " + ex.Message); }
    }

    /// <summary>Keep 7 daily snapshots plus one per month, forever.</summary>
    static void Prune()
    {
        var files = Directory.GetFiles(Paths.Backups, "sweaxclip-*.db").OrderByDescending(f => f).ToList();
        var keep = new HashSet<string>(files.Take(7));
        foreach (var g in files.GroupBy(f => Path.GetFileName(f)[10..17])) keep.Add(g.Last());
        foreach (var f in files.Where(f => !keep.Contains(f)))
        {
            try { File.Delete(f); } catch { }
        }
    }

    public static void Export(ClipStore store, string zipPath)
    {
        string snap = Path.Combine(Path.GetTempPath(), $"sweaxclip-export-{Guid.NewGuid():N}.db");
        store.BackupTo(snap);
        try
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            zip.CreateEntryFromFile(snap, "sweaxclip.db", CompressionLevel.Optimal);
            foreach (var dir in new[] { "blobs", "thumbs" })
            {
                string root = Path.Combine(Paths.Data, dir);
                if (!Directory.Exists(root)) continue;
                foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    if (f.EndsWith(".tmp")) continue;
                    zip.CreateEntryFromFile(f, Path.GetRelativePath(Paths.Data, f).Replace('\\', '/'), CompressionLevel.NoCompression);
                }
            }
            string settings = Path.Combine(Paths.Data, "settings.json");
            if (File.Exists(settings)) zip.CreateEntryFromFile(settings, "settings.json");
        }
        finally
        {
            try { File.Delete(snap); } catch { }
        }
    }

    public static long FolderSize()
    {
        long total = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(Paths.Data, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(f).Length; } catch { }
            }
        }
        catch { }
        return total;
    }
}
