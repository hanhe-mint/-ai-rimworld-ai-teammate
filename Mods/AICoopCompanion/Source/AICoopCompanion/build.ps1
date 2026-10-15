param([string]$GamePath)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $GamePath) {
    if (Test-Path (Join-Path $root '..\..\RimWorldWin64.exe')) { $GamePath = Join-Path $root '..\..' }
    elseif (Test-Path (Join-Path $root '..\..\RimWorld.exe')) { $GamePath = Join-Path $root '..\..' }
    elseif ($env:RIMWORLD_GAME_PATH) { $GamePath = $env:RIMWORLD_GAME_PATH }
    else { $GamePath = Read-Host 'RimWorld install directory (contains RimWorldWin64.exe or RimWorld.exe)' }
}
$GamePath = (Resolve-Path -LiteralPath $GamePath.Trim().Trim('"')).Path
$compiler = Join-Path $root 'Development\packages\compilers\tasks\net472\csc.exe'
$harmony = Join-Path $root 'Development\packages\harmony\lib\net472\0Harmony.dll'

# RimWorld 1.7 (Waylith engine) flattens every reference assembly into the game root and
# replaces the UnityEngine*.dll modules with a single UnityStubs.dll compatibility shim.
# 1.6 keeps the classic RimWorldWin64_Data\Managed layout. Detect whichever is present.
$managed17 = $GamePath
$managed16 = Join-Path $GamePath 'RimWorldWin64_Data\Managed'
$isWaylith = Test-Path -LiteralPath (Join-Path $managed17 'UnityStubs.dll')
if ($isWaylith) {
    $managed = $managed17
} elseif (Test-Path -LiteralPath (Join-Path $managed16 'Assembly-CSharp.dll')) {
    $managed = $managed16
} else {
    throw "No RimWorld assemblies found under $GamePath (looked for UnityStubs.dll and RimWorldWin64_Data\Managed)."
}
$framework = Join-Path $root 'Development\packages\net471\build\.NETFramework\v4.7.1'
if (-not (Test-Path -LiteralPath $framework)) {
    $framework = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.1'
}

if ($isWaylith) {
    # Every reference is taken from the folder that holds Assembly-CSharp.dll, so the
    # compile targets exactly what the runtime loads. Note that mscorlib.dll still exists
    # here - it is a thin net10 type forwarder that supplies the predefined types the C#
    # compiler demands, and it must not be replaced by the net471 mscorlib from
    # Development\packages (mixing the two collides on AssemblyVersionAttribute, CS0433).
    $managedRefs = @(
        'Assembly-CSharp.dll','UnityStubs.dll','System.Private.CoreLib.dll','System.Runtime.dll','netstandard.dll',
        'System.Collections.dll','System.Linq.dll','System.Runtime.Extensions.dll',
        'System.Threading.dll','System.Threading.Thread.dll','System.Threading.ThreadPool.dll',
        'System.Reflection.dll','System.IO.dll','System.IO.Pipes.dll','System.ObjectModel.dll',
        'System.Text.RegularExpressions.dll','System.Runtime.InteropServices.dll',
        'System.Security.Cryptography.dll','System.Collections.Concurrent.dll',
        'System.Runtime.Serialization.Primitives.dll','System.Console.dll'
    )
    $refs = $managedRefs | ForEach-Object { Join-Path $managed $_ } | Where-Object { Test-Path -LiteralPath $_ }
    $refs += $harmony
    # Predefined types must come from the net10 core library the game actually runs on.
    # The game root ships its own mscorlib.dll (a net10 forwarder) - never mix in the
    # net471 mscorlib, or AssemblyVersionAttribute collides (CS0433).
    $refs += @('mscorlib.dll','System.dll','System.Core.dll','System.Xml.dll') | ForEach-Object { Join-Path $managed $_ }
    $refs = $refs | Where-Object { Test-Path -LiteralPath $_ }
    $nostdlib = @()
} else {
    $managedRefs = @('Assembly-CSharp.dll','UnityEngine.CoreModule.dll','UnityEngine.IMGUIModule.dll','UnityEngine.TextRenderingModule.dll','UnityEngine.InputLegacyModule.dll','netstandard.dll')
    $refs = $managedRefs | ForEach-Object { Join-Path $managed $_ }
    $refs += $harmony
    $refs += @('mscorlib.dll','System.dll','System.Core.dll','System.Xml.dll') | ForEach-Object { Join-Path $framework $_ }
    $nostdlib = @()
}
foreach ($path in @($compiler) + $refs) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing build dependency: $path" }
}
Write-Host ("RimWorld layout: " + $(if ($isWaylith) { '1.7 Waylith (.NET 10, UnityStubs)' } else { '1.6 Mono (Managed)' }))
$outDir = Join-Path $root 'Assemblies'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$sources = Get-ChildItem $PSScriptRoot -Filter '*.cs' | ForEach-Object FullName
$arguments = @('/noconfig', '/target:library', '/langversion:latest') + $nostdlib + @(('/out:' + (Join-Path $outDir 'AICoopCompanion.dll')))
$arguments += $refs | ForEach-Object { '/reference:' + $_ }
$arguments += $sources
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
# Harmony is supplied by the separate brrainz.harmony mod, not copied into this mod.
