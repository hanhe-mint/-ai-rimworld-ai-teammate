param([string]$GamePath)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $GamePath) {
    if (Test-Path (Join-Path $root 'RimWorldWin64.exe')) { $GamePath = $root }
    elseif ($env:RIMWORLD_GAME_PATH) { $GamePath = $env:RIMWORLD_GAME_PATH }
    else { $GamePath = Read-Host 'RimWorld install directory (contains RimWorldWin64.exe)' }
}
$GamePath = (Resolve-Path -LiteralPath $GamePath.Trim().Trim('"')).Path
$managed = Join-Path $GamePath 'RimWorldWin64_Data\Managed'
$compiler = Join-Path $root 'packages\compilers\tasks\net472\csc.exe'
$harmony = Join-Path $root 'packages\harmony\lib\net472\0Harmony.dll'
$framework = Join-Path $root 'packages\net471\build\.NETFramework\v4.7.1'
if (-not (Test-Path -LiteralPath $framework)) {
    $framework = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.1'
}
$refs = @('Assembly-CSharp.dll','UnityEngine.CoreModule.dll','UnityEngine.IMGUIModule.dll','UnityEngine.TextRenderingModule.dll','netstandard.dll') |
    ForEach-Object { Join-Path $managed $_ }
$refs += $harmony
$refs += @('mscorlib.dll','System.dll','System.Core.dll','System.Xml.dll') | ForEach-Object { Join-Path $framework $_ }
foreach ($path in @($compiler) + $refs) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing build dependency: $path" }
}
$outDir = Join-Path $root 'Mods\AICoopCompanion\Assemblies'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$sources = Get-ChildItem $PSScriptRoot -Filter '*.cs' | ForEach-Object FullName
$arguments = @('/noconfig', '/target:library', '/langversion:latest', ('/out:' + (Join-Path $outDir 'AICoopCompanion.dll')))
$arguments += $refs | ForEach-Object { '/reference:' + $_ }
$arguments += $sources
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
# Harmony is supplied by the separate brrainz.harmony mod, not copied into this mod.
