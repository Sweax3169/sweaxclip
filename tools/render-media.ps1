# Renders every README / site image from the demo archive (never the real one).
# usage: powershell -File tools\render-media.ps1 [-Exe path] [-SkipShots] [-SkipGif] [-Films panel,search]
param(
    [string] $Exe = "$PSScriptRoot\..\src\SweaxClip\bin\Debug\net9.0-windows10.0.19041.0\SweaxClip.exe",
    [switch] $SkipShots,
    [switch] $SkipGif,
    [string[]] $Films = @('panel', 'search')
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\.."
$media = Join-Path $root 'docs\media'
New-Item -ItemType Directory -Force $media | Out-Null

# film -> output name and width
$catalog = @{
    panel  = @{ Name = 'quick-panel'; Width = 600 }
    search = @{ Name = 'search';      Width = 460 }
}

foreach ($lang in 'en', 'tr') {
    foreach ($theme in 'dark', 'light') {
        if (-not $SkipShots) {
            $p = Start-Process $Exe -ArgumentList "--shots `"$(Join-Path $root "docs\shots\$lang")`" --lang $lang --theme $theme" -PassThru
            $p.WaitForExit()
        }
        if (-not $SkipGif) {
            foreach ($film in $Films) {
                $f = $catalog[$film]
                $frames = Join-Path $env:TEMP "sweaxclip-gif-$film-$lang-$theme"
                Remove-Item -Recurse -Force $frames -ErrorAction SilentlyContinue
                $p = Start-Process $Exe -ArgumentList "--gif `"$frames`" --film $film --lang $lang --theme $theme" -PassThru
                $p.WaitForExit()
                $pal = Join-Path $env:TEMP "pal-$film-$lang-$theme.png"
                $out = Join-Path $media "$($f.Name)-$lang-$theme.gif"
                ffmpeg -v error -y -framerate 25 -i "$frames\f%04d.png" -vf "scale=$($f.Width):-1:flags=lanczos,palettegen=max_colors=160:stats_mode=diff:reserve_transparent=0" $pal
                ffmpeg -v error -y -framerate 25 -i "$frames\f%04d.png" -i $pal -lavfi "scale=$($f.Width):-1:flags=lanczos[x];[x][1:v]paletteuse=dither=sierra2_4a:diff_mode=rectangle" -gifflags -transdiff -loop 0 $out
                Remove-Item -Recurse -Force $frames
            }
        }
        "$lang/$theme done"
    }
}
