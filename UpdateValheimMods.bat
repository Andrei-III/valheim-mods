<# : batch part (PowerShell treats this line as the start of a comment)
@echo off
rem Installs or updates Andrei's Valheim mods from the latest GitHub release.
rem Double-click to run. The PowerShell code below is read from this same file.
powershell -NoProfile -ExecutionPolicy Bypass -Command "iex (Get-Content -LiteralPath '%~f0' -Raw)"
echo.
pause
exit /b
#>

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$ZipUrl   = 'https://github.com/Andrei-III/valheim-mods/releases/latest/download/valheim-mods.zip'
$StateDir = Join-Path $env:LOCALAPPDATA 'AndreiValheimMods'
$PathFile = Join-Path $StateDir 'plugins-path.txt'

function Find-ValheimPlugins {
    # Saved from an earlier run (or typed in by hand).
    if (Test-Path $PathFile) {
        $saved = (Get-Content $PathFile -Raw).Trim()
        if ($saved -and (Test-Path $saved)) { return $saved }
    }
    # Steam's install folder, plus every Steam library listed in libraryfolders.vdf.
    $steam = $null
    foreach ($key in 'HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam') {
        try {
            $p = Get-ItemProperty $key -ErrorAction Stop
            if ($p.SteamPath) { $steam = $p.SteamPath; break }
            if ($p.InstallPath) { $steam = $p.InstallPath; break }
        } catch { }
    }
    $libraries = @()
    if ($steam) {
        $libraries += $steam
        $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
        if (Test-Path $vdf) {
            foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) {
                $libraries += $m.Groups[1].Value -replace '\\\\', '\'
            }
        }
    }
    foreach ($lib in $libraries | Select-Object -Unique) {
        $bepinex = Join-Path $lib 'steamapps\common\Valheim\BepInEx'
        if (Test-Path $bepinex) { return (Join-Path $bepinex 'plugins') }
    }
    return $null
}

Write-Host "Andrei's Valheim mods updater" -ForegroundColor Cyan
Write-Host ''

$plugins = Find-ValheimPlugins
if (-not $plugins) {
    Write-Host "Couldn't find Valheim with BepInEx installed."
    $plugins = (Read-Host 'Paste the full path of your BepInEx\plugins folder').Trim('" ')
    if (-not (Test-Path (Split-Path $plugins -Parent))) { throw "Folder not found: $plugins" }
}
New-Item -ItemType Directory -Force -Path $StateDir, $plugins | Out-Null
Set-Content -Path $PathFile -Value $plugins
Write-Host "Plugins folder: $plugins"

if (-not (Get-ChildItem $plugins -Recurse -Filter 'Jotunn.dll' -ErrorAction SilentlyContinue)) {
    Write-Host 'Warning: Jotunn is not installed. These mods need it: https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/' -ForegroundColor Yellow
}

while (Get-Process -Name 'valheim' -ErrorAction SilentlyContinue) {
    Write-Host 'Valheim is running. Close it, then press Enter.' -ForegroundColor Yellow
    [void](Read-Host)
}

$tmp = Join-Path $env:TEMP ('valheim-mods-' + [guid]::NewGuid())
New-Item -ItemType Directory -Path $tmp | Out-Null
try {
    Write-Host 'Downloading the latest release...'
    $zip = Join-Path $tmp 'valheim-mods.zip'
    Invoke-WebRequest -Uri $ZipUrl -OutFile $zip -UseBasicParsing
    Expand-Archive -Path $zip -DestinationPath (Join-Path $tmp 'mods')
    $src = Join-Path $tmp 'mods'

    $lines = Get-Content (Join-Path $src 'versions.txt')
    $release = $lines[0]
    $versions = @{}
    foreach ($l in $lines | Select-Object -Skip 1) { $parts = $l -split ' '; if ($parts.Count -eq 2) { $versions[$parts[0]] = $parts[1] } }

    $installedFile = Join-Path $StateDir 'installed.txt'
    $before = @{}
    if (Test-Path $installedFile) {
        foreach ($l in Get-Content $installedFile) { $parts = $l -split ' '; if ($parts.Count -eq 2) { $before[$parts[0]] = $parts[1] } }
    }

    Write-Host "Release $release" -ForegroundColor Cyan
    foreach ($dir in Get-ChildItem $src -Directory | Sort-Object Name) {
        $mod = $dir.Name
        $dll = "$mod.dll"
        $target = Join-Path $plugins $mod
        # Remove copies of this mod installed somewhere else (e.g. loose in plugins\), so it doesn't load twice.
        Get-ChildItem $plugins -Recurse -Filter $dll -ErrorAction SilentlyContinue |
            Where-Object { $_.DirectoryName -ne $target } |
            ForEach-Object { Remove-Item $_.FullName -Force; Write-Host "  removed old copy $($_.FullName)" -ForegroundColor DarkGray }
        New-Item -ItemType Directory -Force -Path $target | Out-Null
        Copy-Item (Join-Path $dir.FullName $dll) (Join-Path $target $dll) -Force

        $new = $versions[$mod]
        $old = $before[$mod]
        if (-not $old)          { Write-Host ("  {0,-14} {1,-8} new" -f $mod, $new) -ForegroundColor Green }
        elseif ($old -ne $new)  { Write-Host ("  {0,-14} {1,-8} updated from {2}" -f $mod, $new, $old) -ForegroundColor Green }
        else                    { Write-Host ("  {0,-14} {1,-8} up to date" -f $mod, $new) }
    }
    $versions.GetEnumerator() | ForEach-Object { "$($_.Key) $($_.Value)" } | Set-Content $installedFile
    Write-Host ''
    Write-Host 'Done. Start Valheim to use them.' -ForegroundColor Cyan
}
catch {
    Write-Host ''
    Write-Host "Update failed: $($_.Exception.Message)" -ForegroundColor Red
}
finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
