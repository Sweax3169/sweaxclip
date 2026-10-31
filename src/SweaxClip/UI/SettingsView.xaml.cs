using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SweaxClip.Core;

namespace SweaxClip.UI;

public partial class SettingsView : UserControl
{
    readonly App _app;
    readonly LibraryWindow _owner;
    bool _loading;

    public SettingsView(App app, LibraryWindow owner)
    {
        _app = app;
        _owner = owner;
        InitializeComponent();
    }

    public void Refresh()
    {
        _loading = true;
        var s = _app.Settings;
        startup.IsChecked = AutoStart.IsEnabled();
        plain.IsChecked = s.PastePlainByDefault;
        toTop.IsChecked = s.MoveToTopOnPaste;
        capText.IsChecked = s.CaptureText;
        capImages.IsChecked = s.CaptureImages;
        capFiles.IsChecked = s.CaptureFiles;
        watch.IsChecked = s.WatchScreenshots;
        ocr.IsChecked = s.Ocr;
        privacy.IsChecked = s.RespectPrivateFlags;
        backups.IsChecked = s.DailyBackups;
        ignored.Text = string.Join(Environment.NewLine, s.IgnoredApps);
        langBox.SelectedIndex = s.Language switch { "en" => 1, "tr" => 2, _ => 0 };
        var hk = Hotkey.Parse(s.Hotkey);
        hotkeyBox.SelectedIndex = hk.Ctrl && hk.Shift ? 1 : hk.Ctrl && hk.Alt ? 2 : 0;
        dataPath.Text = Paths.Data;
        version.Text = Loc.Format("SetVersion", typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");
        using (var p = Process.GetCurrentProcess()) memory.Text = $"{Loc.T("SetMemory")}: {Bytes.Format(p.WorkingSet64)}";
        styleTimeline.IsChecked = s.PanelStyle != "deck";
        styleDeck.IsChecked = s.PanelStyle == "deck";
        themeSystem.IsChecked = s.ThemeMode is not ("light" or "dark");
        themeLight.IsChecked = s.ThemeMode == "light";
        themeDark.IsChecked = s.ThemeMode == "dark";
        LoadPalettes();
        _loading = false;
        _ = UpdateDataStats();
    }

    // ── appearance ───────────────────────────────────────────────────────────

    sealed record PaletteChoice(string Key, string Name, Brush Desk, Brush Surface, Brush Accent, Brush Paper, Brush Slate, Brush Edge, bool Selected);

    /// <summary>Each palette drawn in the current mode, so the swatches show what you will get.</summary>
    void LoadPalettes()
    {
        bool dark = ThemeManager.IsDark;
        palettes.ItemsSource = Palettes.All.Select(p =>
        {
            var (desk, surface, accent, paper, slate, edge) = Palettes.Preview(p, dark);
            return new PaletteChoice(p.Key, p.Name, desk, surface, accent, paper, slate, edge, p.Key == _app.Settings.Palette);
        }).ToList();
    }

    void Style_Click(object sender, RoutedEventArgs e)
    {
        if (_loading || (sender as FrameworkElement)?.Tag is not string style || style == _app.Settings.PanelStyle) return;
        _app.Settings.PanelStyle = style;
        _app.Settings.Save();
        _app.ApplyPanelStyle();
    }

    void Theme_Click(object sender, RoutedEventArgs e)
    {
        if (_loading || (sender as FrameworkElement)?.Tag is not string mode) return;
        _app.Settings.ThemeMode = mode;
        _app.Settings.Save();
        ThemeManager.Mode = mode;
        ThemeManager.Apply();
        LoadPalettes();
    }

    void Palette_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string key) return;
        _app.Settings.Palette = key;
        _app.Settings.Save();
        ThemeManager.PaletteKey = key;
        ThemeManager.Apply();
        LoadPalettes();
    }

    async Task UpdateDataStats()
    {
        long size = await Task.Run(Backup.FolderSize);
        var counts = await Task.Run(_app.Store.Counts);
        dataStats.Text = $"{Loc.Format("ClipsStored", counts[Scope.All].ToString("N0", Loc.Culture))} · {Loc.Format("OnDisk", Bytes.Format(size))}";
    }

    void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var s = _app.Settings;
        bool on = (sender as CheckBox)?.IsChecked == true;
        switch ((sender as FrameworkElement)?.Name)
        {
            case "startup": s.StartWithWindows = on; AutoStart.Set(on); break;
            case "plain": s.PastePlainByDefault = on; break;
            case "toTop": s.MoveToTopOnPaste = on; break;
            case "capText": s.CaptureText = on; break;
            case "capImages": s.CaptureImages = on; break;
            case "capFiles": s.CaptureFiles = on; break;
            case "watch": s.WatchScreenshots = on; _app.ApplyWatcher(); break;
            case "ocr": s.Ocr = on; break;
            case "privacy": s.RespectPrivateFlags = on; break;
            case "backups": s.DailyBackups = on; break;
        }
        s.Save();
    }

    void Hotkey_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || hotkeyBox.SelectedItem is not ComboBoxItem { Tag: string hk }) return;
        _app.Settings.Hotkey = hk;
        _app.Settings.Save();
        _app.ApplyHotkey();
    }

    void Lang_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || langBox.SelectedItem is not ComboBoxItem { Tag: string lang }) return;
        _app.Settings.Language = lang;
        _app.Settings.Save();
        _owner.Toast(Loc.T("SetLanguageNote"));
    }

    void Ignored_LostFocus(object sender, RoutedEventArgs e)
    {
        _app.Settings.IgnoredApps = ignored.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _app.Settings.Save();
    }

    void Import_Click(object sender, RoutedEventArgs e)
    {
        importBtn.IsEnabled = false;
        _app.ImportScreenshots((n, total) => Dispatcher.BeginInvoke(() => importStatus.Text = Loc.Format("Importing", n, total)),
            added => Dispatcher.BeginInvoke(() =>
            {
                importStatus.Text = Loc.Format("Imported", added);
                importBtn.IsEnabled = true;
                _ = UpdateDataStats();
            }));
    }

    void OpenFolder_Click(object sender, RoutedEventArgs e) => App.Shell(Paths.Data);

    async void BackupNow_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string file = await Task.Run(() => Backup.Snapshot(_app.Store));
            backupStatus.Text = $"{Loc.T("SetBackupDone")} → {file}";
        }
        catch (Exception ex) { backupStatus.Text = ex.Message; }
    }

    async void Export_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"sweaxclip-export-{DateTime.Now:yyyy-MM-dd}.zip",
            Filter = "Zip (*.zip)|*.zip",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        exportBtn.IsEnabled = false;
        try
        {
            await Task.Run(() => Backup.Export(_app.Store, dlg.FileName));
            backupStatus.Text = $"{Loc.T("SetExportDone")} → {dlg.FileName}";
        }
        catch (Exception ex) { backupStatus.Text = ex.Message; }
        finally { exportBtn.IsEnabled = true; }
    }

    void Source_Click(object sender, RoutedEventArgs e) => App.Shell("https://github.com/Sweax3169/sweaxclip");
}
