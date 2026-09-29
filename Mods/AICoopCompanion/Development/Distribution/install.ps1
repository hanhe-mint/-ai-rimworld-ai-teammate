param([string]$GamePath, [string]$DshHome, [string]$Profile)
$ErrorActionPreference = 'Stop'
try {
    if (!$GamePath) { $GamePath = Read-Host '游戏目录（包含RimWorldWin64.exe）' }
    if (!$DshHome) { $DshHome = Read-Host 'DSH的Home目录（包含profiles文件夹，不是DSH程序目录）' }
    $GamePath = (Resolve-Path -LiteralPath $GamePath.Trim().Trim('"')).Path
    $DshHome = (Resolve-Path -LiteralPath $DshHome.Trim().Trim('"')).Path
    if (!$Profile) {
        $profiles = @(Get-ChildItem -LiteralPath (Join-Path $DshHome 'profiles') -Directory | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'package.json') } | Select-Object -ExpandProperty Name)
        if ($profiles.Count -eq 1) { $Profile = $profiles[0] }
        else { $Profile = Read-Host ('请选择要安装的配置（网页端通常web，官方桌面版desktop）：' + ($profiles -join ', ')) }
    }
    if (!(Test-Path -LiteralPath (Join-Path $GamePath 'RimWorldWin64.exe'))) { throw '选择的不是游戏目录。' }
    if ($Profile -notmatch '^[a-zA-Z0-9_-]+$' -or !(Test-Path -LiteralPath (Join-Path $DshHome "profiles\$Profile"))) { throw 'Home或Profile不正确，请先启动一次DSH。' }
    if (Get-Process RimWorldWin64 -ErrorAction SilentlyContinue) { throw '请先退出游戏，并完全退出DSH。' }
    $target = Join-Path $GamePath 'Mods\AICoopCompanion'
    if (Test-Path -LiteralPath $target) { throw '已有旧Mod。请先备份旧Mod到游戏目录之外并移走，再安装；升级保留文件见教程。' }
    New-Item -ItemType Directory -Force -Path (Join-Path $GamePath 'Mods') | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Mod\AICoopCompanion') -Destination $target -Recurse
    New-Item -ItemType Directory -Force -Path (Join-Path $target 'Runtime') | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Runtime\Python') -Destination (Join-Path $target 'Runtime\Python') -Recurse
    $plugin = Join-Path $target 'DeepSeekHarnessPlugin'
    $python = Join-Path $target 'Runtime\Python\python.exe'
    [IO.File]::WriteAllText((Join-Path $target 'Runtime\Python\python314._pth'), "python314.zip`r`n.`r`n$plugin`r`n", [Text.UTF8Encoding]::new($false))
    & (Join-Path $plugin 'install-modern.ps1') -DshUserRoot $DshHome -Profile $Profile -PythonCommand $python
    $state = @{ gamePath=$GamePath; dshHome=$DshHome; profile=$Profile }
    [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'install-state.json'), ($state | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    Write-Host 'Mod和插件已安装。请自行启用适配1.6的Harmony（已有则不要重复装）。重启游戏和DSH，选择环世界AI队友模式连接游戏。'
} catch { Write-Host ('安装失败：' + $_.Exception.Message) -ForegroundColor Red; exit 1 }
