using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
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
        (s.Language switch { "en" => langEn, "tr" => langTr, _ => langAuto }).IsChecked = true;
        var hk = Hotkey.Parse(s.Hotkey);
        (hk.Ctrl && hk.Shift ? hkCtrlShiftV : hk.Ctrl && hk.Alt ? hkCtrlAltV : hkWinV).IsChecked = true;
        dataPath.Text = Paths.Data;
        version.Text = Loc.Format("SetVersion", typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");
        using (var p = Process.GetCurrentProcess()) memory.Text = $"{Loc.T("SetMemory")}: {Bytes.Format(p.WorkingSet64)}";
        _loading = false;
        _ = UpdateDataStats();
    }

    async Task UpdateDataStats()
    {
        long size = await Task.Run(Backup.FolderSize);
        var counts = await Task.Run(_app.Store.Counts);
        dataStats.Text = $"{Loc.Format("ClipsStored", counts[Scope.All].ToString("N0", Loc.Culture))}  ·  {Loc.Format("OnDisk", Bytes.Format(size))}";
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

    void Hotkey_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _app.Settings.Hotkey = sender == hkCtrlShiftV ? "Ctrl+Shift+V" : sender == hkCtrlAltV ? "Ctrl+Alt+V" : "Win+V";
        _app.Settings.Save();
        _app.ApplyHotkey();
    }

    void Lang_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _app.Settings.Language = sender == langEn ? "en" : sender == langTr ? "tr" : "auto";
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

    void Back_Click(object sender, RoutedEventArgs e) => _owner.CloseSettings();

    void Link_Navigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        App.Shell(e.Uri.AbsoluteUri);
        e.Handled = true;
    }
}
