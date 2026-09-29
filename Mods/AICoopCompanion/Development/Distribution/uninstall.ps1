param([string]$GamePath, [string]$DshHome, [string]$Profile)
$ErrorActionPreference = 'Stop'
try {
    $statePath = Join-Path $PSScriptRoot 'install-state.json'
    if (Test-Path -LiteralPath $statePath) {
        $state = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
        if (!$GamePath) { $GamePath = $state.gamePath }
        if (!$DshHome) { $DshHome = $state.dshHome; if (!$Profile) { $Profile = $state.profile } }
    }
    if (!$GamePath) { $GamePath = Read-Host '游戏目录' }
    if (!$DshHome) { $DshHome = Read-Host '安装插件时使用的DSH Home目录' }
    $GamePath = (Resolve-Path -LiteralPath $GamePath.Trim().Trim('"')).Path
    $DshHome = (Resolve-Path -LiteralPath $DshHome.Trim().Trim('"')).Path
    if (!$Profile) {
        $profiles = @(Get-ChildItem -LiteralPath (Join-Path $DshHome 'profiles') -Directory | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'package.json') } | Select-Object -ExpandProperty Name)
        if ($profiles.Count -eq 1) { $Profile = $profiles[0] }
        else { $Profile = Read-Host ('请选择要卸载的配置（web/desktop）：' + ($profiles -join ', ')) }
    }
    if (!(Test-Path -LiteralPath (Join-Path $GamePath 'RimWorldWin64.exe')) -or $Profile -notmatch '^[a-zA-Z0-9_-]+$') { throw '目录或Profile不正确。' }
    if (Get-Process RimWorldWin64 -ErrorAction SilentlyContinue) { throw '请先退出游戏，并完全退出DSH。' }
    $target = [IO.Path]::GetFullPath((Join-Path $GamePath 'Mods\AICoopCompanion'))
    if ($PSScriptRoot.StartsWith($target + '\', [StringComparison]::OrdinalIgnoreCase) -or $PSScriptRoot -eq $target) { throw '请将安装包移到游戏Mod目录之外再卸载。' }
    if (Test-Path -LiteralPath $target) {
        $id = ([xml](Get-Content -LiteralPath (Join-Path $target 'About\About.xml') -Raw -Encoding UTF8)).ModMetaData.packageId
        if ($id -ne 'local.aicoopcompanion') { throw '目标不是环世界AI队友，未修改。' }
    }
    $path = Join-Path $DshHome "profiles\$Profile\cordis.patch.yml"
    if (Test-Path -LiteralPath $path) {
        $text = [IO.File]::ReadAllText($path)
        $pattern = '(?ms)^# BEGIN rimworld-ai-coop managed preset\r?\n.*?^# END rimworld-ai-coop managed preset(?=\r?$)'
        $count = [regex]::Matches($text, $pattern).Count
        if ($count -gt 1 -or ($text.Contains('# BEGIN rimworld-ai-coop managed preset') -and $count -ne 1)) { throw '插件注册块异常，未修改。请按手动卸载教程检查。' }
        $updated = [regex]::Replace($text, $pattern, '')
        if (($updated -replace '(?m)^\s*#.*$', '').Trim() -eq '') { $updated += "`r`n[]`r`n" }
        [IO.File]::WriteAllText($path, $updated, [Text.UTF8Encoding]::new($false))
    }
    $target = [IO.Path]::GetFullPath((Join-Path $GamePath 'Mods\AICoopCompanion'))
    if ($target -ne (Join-Path $GamePath 'Mods\AICoopCompanion')) { throw 'Mod路径校验失败。' }
    if (Test-Path -LiteralPath $target) {
        $packageId = ([xml](Get-Content -LiteralPath (Join-Path $target 'About\About.xml') -Raw -Encoding UTF8)).ModMetaData.packageId
        if ($packageId -ne 'local.aicoopcompanion') { throw '目标不是环世界AI队友，未移动。' }
        $backup = Join-Path $PSScriptRoot ('卸载保留\AICoopCompanion-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
        New-Item -ItemType Directory -Force -Path (Split-Path $backup) | Out-Null
        if (Test-Path -LiteralPath $backup) { throw '备份目标已存在。' }
        Move-Item -LiteralPath $target -Destination $backup
        Write-Host ('Mod已移至：' + $backup + '，个人提示词和学习内容仍在其中。')
    }
    Write-Host '插件注册已移除。未删除存档、模型设置、DSH或Harmony。移除Mod后旧存档的兼容性不作保证。'
} catch { Write-Host ('卸载失败：' + $_.Exception.Message) -ForegroundColor Red; exit 1 }
