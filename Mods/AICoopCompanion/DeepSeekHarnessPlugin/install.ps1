param(
    [Parameter(Mandatory=$true)][string]$DshUserRoot,
    [string]$Profile,
    [string]$PythonCommand = 'python.exe'
)
$ErrorActionPreference = 'Stop'
# Keep the old entry point, but never create obsolete .agent-presets registrations.
& (Join-Path $PSScriptRoot 'install-modern.ps1') -DshUserRoot $DshUserRoot -Profile $Profile -PythonCommand $PythonCommand
