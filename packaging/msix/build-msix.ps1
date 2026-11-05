# Builds an unsigned x64 .msix for the Microsoft Store (the Store signs it).
param([string]$Version = "1.0.0.0")
$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^\d+\.\d+\.\d+\.0$') { throw "Version must be 4 dotted numbers ending in .0 (Store requirement), got '$Version'." }
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$artifacts = Join-Path $repo 'artifacts'
$layout = Join-Path $artifacts 'msix\layout'
$msix = Join-Path $artifacts "Samaat-$Version-x64.msix"

if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
New-Item -ItemType Directory -Path $layout | Out-Null
if (Test-Path $msix) { Remove-Item $msix -Force }

$short = ($Version -split '\.')[0..2] -join '.'
dotnet publish (Join-Path $repo 'src\Samaat.App\Samaat.App.csproj') -c Release -f net10.0-windows10.0.19041.0 -r win-x64 --self-contained "-p:Version=$short" -o $layout
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }

$manifestPath = Join-Path $layout 'AppxManifest.xml'
$manifest = (Get-Content (Join-Path $PSScriptRoot 'AppxManifest.xml') -Raw).Replace('__VERSION__', $Version)
[IO.File]::WriteAllText($manifestPath, $manifest, (New-Object Text.UTF8Encoding($false)))

Add-Type -AssemblyName System.Drawing
$assets = Join-Path $layout 'Assets'
New-Item -ItemType Directory -Path $assets -Force | Out-Null
$src = [Drawing.Image]::FromFile((Join-Path $repo 'src\Samaat.UI\Assets\samaat-icon.png'))
try {
    foreach ($a in @{ 'Square150x150Logo.png' = 150; 'Square44x44Logo.png' = 44; 'StoreLogo.png' = 50 }.GetEnumerator()) {
        $bmp = New-Object Drawing.Bitmap($a.Value, $a.Value, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [Drawing.Graphics]::FromImage($bmp)
        $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::HighQuality
        $g.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.DrawImage($src, 0, 0, $a.Value, $a.Value)
        $g.Dispose()
        $bmp.Save((Join-Path $assets $a.Key), [Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
    }
}
finally { $src.Dispose() }

$xml = [xml](Get-Content $manifestPath -Raw)
$refs = @($xml.SelectNodes('//@*') | Where-Object { $_.Value -like 'Assets\*.png' } | ForEach-Object { $_.Value }) +
    @($xml.SelectNodes('//*[local-name()="Logo"]') | ForEach-Object { $_.InnerText })
foreach ($r in ($refs | Select-Object -Unique)) {
    if (-not (Test-Path (Join-Path $layout $r))) { throw "Manifest references missing asset: $r" }
}

$kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
$makeappx = Get-ChildItem $kits -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match '^\d+\.' } | Sort-Object { [version]$_.Name } -Descending |
    ForEach-Object { Join-Path $_.FullName 'x64\makeappx.exe' } | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $makeappx) { throw "makeappx.exe not found. Install the Windows SDK (Windows 10/11 SDK) and retry." }

& $makeappx pack /d $layout /p $msix /o
if ($LASTEXITCODE -ne 0) { throw "makeappx failed ($LASTEXITCODE)." }
Write-Host "Created $msix"
