# Renders every README / site image from the demo archive (never the real one).
# usage: powershell -File tools\render-media.ps1 [-Exe path] [-SkipShots] [-SkipGif]
param(
    [string] $Exe = "$PSScriptRoot\..\src\SweaxClip\bin\Debug\net9.0-windows10.0.19041.0\SweaxClip.exe",
    [switch] $SkipShots,
    [switch] $SkipGif
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\.."
$media = Join-Path $root 'docs\media'
New-Item -ItemType Directory -Force $media | Out-Null

foreach ($lang in 'en', 'tr') {
    foreach ($theme in 'dark', 'light') {
        if (-not $SkipShots) {
            & $Exe --shots (Join-Path $root "docs\shots\$lang") --lang $lang --theme $theme | Out-Null
            Wait-Process -Name SweaxClip -ErrorAction SilentlyContinue -Timeout 1
        }
        if (-not $SkipGif) {
            $frames = Join-Path $env:TEMP "sweaxclip-gif-$lang-$theme"
            Remove-Item -Recurse -Force $frames -ErrorAction SilentlyContinue
            $p = Start-Process $Exe -ArgumentList "--gif `"$frames`" --lang $lang --theme $theme" -PassThru
            $p.WaitForExit()
            $pal = Join-Path $env:TEMP "pal-$lang-$theme.png"
            ffmpeg -v error -y -framerate 25 -i "$frames\f%04d.png" -vf "scale=600:-1:flags=lanczos,palettegen=max_colors=192:stats_mode=full:reserve_transparent=0" $pal
            ffmpeg -v error -y -framerate 25 -i "$frames\f%04d.png" -i $pal -lavfi "scale=600:-1:flags=lanczos[x];[x][1:v]paletteuse=dither=sierra2_4a:diff_mode=rectangle" -gifflags -transdiff -loop 0 (Join-Path $media "quick-panel-$lang-$theme.gif")
            Remove-Item -Recurse -Force $frames
        }
        "$lang/$theme done"
    }
}
