# End-to-end smoke test against a running SweaxClip that uses a portable test archive.
# usage: powershell -File tools\smoke.ps1 -Exe <path to SweaxClip.exe>
param([Parameter(Mandatory)] [string] $Exe)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices; using System.Text; using System.Collections.Generic;
public static class W {
  delegate bool P(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(P p, IntPtr l);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  public static List<string> Visible(int pid) {
    var r = new List<string>();
    EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && IsWindowVisible(h)) { var sb = new StringBuilder(256); GetWindowText(h, sb, 256); r.Add(sb.ToString()); } return true; }, IntPtr.Zero);
    return r;
  }
}
"@

$dataDir = Join-Path (Split-Path $Exe) 'sweaxclip-data'
$db = Join-Path $dataDir 'sweaxclip.db'
$py = @'
import sqlite3, sys, json
con = sqlite3.connect(sys.argv[1])
rows = con.execute("select id, kind, substr(coalesce(text,''),1,60), uses, app, width, height, blob is not null, ocr_state from clips order by used desc limit 12").fetchall()
print(json.dumps(rows, ensure_ascii=False))
print("total", con.execute("select count(*) from clips").fetchone()[0], "shots", con.execute("select count(*) from clips where kind=5").fetchone()[0], "ocr_done", con.execute("select count(*) from clips where ocr_state=2 and ocr is not null").fetchone()[0])
'@
$pyFile = Join-Path $env:TEMP 'sweaxclip-smoke.py'
Set-Content $pyFile $py -Encoding utf8
function Dump { python -I $pyFile $db }

function Clip-Text($t) { [System.Windows.Forms.Clipboard]::SetText($t); Start-Sleep -Milliseconds 700 }

$proc = Get-Process SweaxClip -ErrorAction SilentlyContinue | Select-Object -First 1
"running pid: $($proc.Id)"

Clip-Text "smoke: hello from the smoke test $(Get-Date -Format HHmmss)"
Clip-Text "https://github.com/Sweax3169/sweaxclip"
Clip-Text "#FFE500"
Clip-Text "public static void Main(string[] args)`r`n{`r`n    Console.WriteLine(`"hi`");`r`n    return;`r`n}"
Clip-Text "https://github.com/Sweax3169/sweaxclip"   # duplicate → uses should become 2
$bmp = New-Object System.Drawing.Bitmap 320, 200
$g = [System.Drawing.Graphics]::FromImage($bmp); $g.Clear([System.Drawing.Color]::FromArgb(255, 255, 229, 0)); $g.DrawString("SMOKE IMAGE", (New-Object System.Drawing.Font 'Arial', 28), [System.Drawing.Brushes]::Black, 20, 70); $g.Dispose()
[System.Windows.Forms.Clipboard]::SetImage($bmp); Start-Sleep -Milliseconds 1200
$files = New-Object System.Collections.Specialized.StringCollection; [void]$files.Add($Exe)
[System.Windows.Forms.Clipboard]::SetFileDropList($files); Start-Sleep -Milliseconds 900
Start-Sleep -Seconds 2
Dump

"--- toggle panel"
Start-Process $Exe -ArgumentList '--toggle' -Wait
Start-Sleep -Milliseconds 800
"visible windows: " + (([W]::Visible($proc.Id)) -join ' | ')
Start-Process $Exe -ArgumentList '--toggle' -Wait
Start-Sleep -Milliseconds 600
"after 2nd toggle: " + (([W]::Visible($proc.Id)) -join ' | ')

$proc.Refresh()
"memory: private={0:N1} MB  workingset={1:N1} MB  threads={2}  cpu={3:N2}s" -f ($proc.PrivateMemorySize64/1MB), ($proc.WorkingSet64/1MB), $proc.Threads.Count, $proc.TotalProcessorTime.TotalSeconds
