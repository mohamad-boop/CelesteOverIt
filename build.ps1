# Builds Celeste Over It with the C# compiler that ships with Windows (.NET Framework 4.x) - no SDK needed.
#
#   .\build.ps1 -GameDir "D:\Steam\steamapps\common\Getting Over It"            # build + install into the game
#   .\build.ps1 -GameDir "..." -NoInstall                                        # just build into .\bin
#
# The game folder must already contain BepInEx 5 (x64); its Managed DLLs are used as compile references.
param(
    [string]$GameDir = $env:GOI_DIR,
    [switch]$NoInstall
)
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

if (-not $GameDir) {
    foreach ($c in @("C:\Program Files (x86)\Steam\steamapps\common\Getting Over It", "C:\Program Files\Steam\steamapps\common\Getting Over It")) {
        if (Test-Path (Join-Path $c "GettingOverIt.exe")) { $GameDir = $c; break }
    }
}
if (-not $GameDir -or -not (Test-Path (Join-Path $GameDir "GettingOverIt_Data\Managed"))) {
    throw "Pass -GameDir with your Getting Over It folder (or set GOI_DIR)."
}
$managed = Join-Path $GameDir "GettingOverIt_Data\Managed"
$core = Join-Path $GameDir "BepInEx\core"
if (-not (Test-Path "$core\BepInEx.dll")) { throw "BepInEx isn't installed in $GameDir (missing BepInEx\core\BepInEx.dll)." }

$csc64 = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$csc32 = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"

$refs = @(
    "mscorlib.dll", "System.dll", "System.Core.dll", "System.Xml.dll", "netstandard.dll",
    "UnityEngine.dll", "UnityEngine.CoreModule.dll", "UnityEngine.Physics2DModule.dll",
    "UnityEngine.IMGUIModule.dll", "UnityEngine.InputLegacyModule.dll", "UnityEngine.ImageConversionModule.dll",
    "UnityEngine.AudioModule.dll", "UnityEngine.TextRenderingModule.dll", "UnityEngine.ScreenCaptureModule.dll",
    "UnityEngine.ParticleSystemModule.dll"
) | ForEach-Object { "/r:" + (Join-Path $managed $_) }
$refs += "/r:$core\BepInEx.dll"

$bin = Join-Path $here "bin"
New-Item -ItemType Directory -Force $bin | Out-Null

# The plugin
& $csc64 /nologo /nostdlib /noconfig /target:library /optimize+ "/out:$bin\CelesteOverIt.dll" $refs (Join-Path $here "src\*.cs")
if ($LASTEXITCODE -ne 0) { throw "Plugin build failed" }

# Audio hosts: Celeste's FMOD is 32-bit (XNA build) or 64-bit (FNA build); the game is 64-bit, so sound is
# played by a small helper process of the matching bitness.
$hostSrc = Join-Path $here "audiohost\CelesteAudioHost.cs"
& $csc32 /nologo /platform:x86 /target:exe /optimize+ "/out:$bin\CelesteAudioHost.exe" $hostSrc
if ($LASTEXITCODE -ne 0) { throw "Audio host (x86) build failed" }
& $csc64 /nologo /platform:x64 /target:exe /optimize+ "/out:$bin\CelesteAudioHost64.exe" $hostSrc
if ($LASTEXITCODE -ne 0) { throw "Audio host (x64) build failed" }

if (-not $NoInstall) {
    $plugins = Join-Path $GameDir "BepInEx\plugins\CelesteOverIt"
    New-Item -ItemType Directory -Force $plugins | Out-Null
    Copy-Item "$bin\CelesteOverIt.dll", "$bin\CelesteAudioHost.exe", "$bin\CelesteAudioHost64.exe" $plugins -Force
    Write-Host "Built and installed -> $plugins"
}
else { Write-Host "Built -> $bin" }
