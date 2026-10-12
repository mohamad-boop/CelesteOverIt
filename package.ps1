# Builds a ready-to-play release: BepInEx 5 + the plugin + install instructions, laid out so players can
# extract it straight into their Getting Over It folder. Produces dist\CelesteOverIt_v<version>\ and a .zip.
#
#   .\package.ps1 -GameDir "...\Getting Over It" -BepInExZip "...\BepInEx_win_x64_5.4.23.2.zip" [-OutDir "..."]
param(
    [string]$GameDir = $env:GOI_DIR,
    [Parameter(Mandatory = $true)][string]$BepInExZip,
    [string]$OutDir = "",
    [string]$Version = "1.2.0"
)
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $OutDir) { $OutDir = Join-Path $here "dist" }

& (Join-Path $here "build.ps1") -GameDir $GameDir -NoInstall

$name = "CelesteOverIt_v$Version"
$stage = Join-Path $OutDir $name
if (Test-Path $stage) { throw "$stage already exists - remove it or pick another -OutDir/-Version" }
New-Item -ItemType Directory -Force $stage | Out-Null

# BepInEx (LGPL-2.1, redistributed unmodified)
Expand-Archive $BepInExZip -DestinationPath $stage
# The plugin
$plugins = Join-Path $stage "BepInEx\plugins\CelesteOverIt"
New-Item -ItemType Directory -Force $plugins | Out-Null
Copy-Item (Join-Path $here "bin\CelesteOverIt.dll"), (Join-Path $here "bin\CelesteAudioHost.exe"), (Join-Path $here "bin\CelesteAudioHost64.exe") $plugins
# Docs
Copy-Item (Join-Path $here "INSTALL.txt") (Join-Path $stage "CelesteOverIt_README.txt")
Copy-Item (Join-Path $here "LICENSE") (Join-Path $stage "CelesteOverIt_LICENSE.txt")

$zip = Join-Path $OutDir "$name.zip"
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip
Write-Host "Package: $stage"
Write-Host "Zip:     $zip"
