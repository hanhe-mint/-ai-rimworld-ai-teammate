param(
    [string]$DshUserRoot = '',
    [string]$PythonCommand = ''
)

if ([string]::IsNullOrWhiteSpace($DshUserRoot)) {
    if (-not [string]::IsNullOrWhiteSpace($env:DSH_HOME)) {
        $DshUserRoot = $env:DSH_HOME
    } else {
        $launcherConfig = Join-Path $env:APPDATA 'in.dsh-plug.dsh-launcher\config.json'
        if (Test-Path -LiteralPath $launcherConfig -PathType Leaf) {
            $launcher = Get-Content -Raw -LiteralPath $launcherConfig | ConvertFrom-Json
            $homePaths = @($launcher.homes | ForEach-Object { $_.path } | Where-Object {
                $_ -and (Test-Path -LiteralPath $_ -PathType Container)
            } | Select-Object -Unique)
            if ($homePaths.Count -eq 1) { $DshUserRoot = $homePaths[0] }
        }
    }
    if ([string]::IsNullOrWhiteSpace($DshUserRoot)) {
        throw 'Cannot identify a unique DSH home. Pass -DshUserRoot with your actual DSH_HOME directory.'
    }
}
if (-not (Test-Path -LiteralPath $DshUserRoot -PathType Container)) {
    throw "DSH home does not exist: $DshUserRoot"
}
$DshUserRoot = (Resolve-Path -LiteralPath $DshUserRoot).Path

$pluginEntry = (Resolve-Path (Join-Path $PSScriptRoot 'index.js')).Path.Replace('\', '/')
if ([string]::IsNullOrWhiteSpace($PythonCommand)) {
    $python = Get-Command python.exe -ErrorAction SilentlyContinue
    if ($null -eq $python) {
        throw 'python.exe was not found. Install Python 3.9+ or pass -PythonCommand.'
    }
    $PythonCommand = $python.Source
}
$resolvedPython = Get-Command $PythonCommand -ErrorAction SilentlyContinue
if ($null -ne $resolvedPython) {
    $PythonCommand = $resolvedPython.Source
} elseif (-not (Test-Path -LiteralPath $PythonCommand -PathType Leaf)) {
    throw "Python was not found: $PythonCommand"
}
$pythonEntry = ([System.IO.Path]::GetFullPath($PythonCommand)).Replace('\', '/')

function Quote-Yaml([string]$value) {
    return "'" + $value.Replace("'", "''") + "'"
}

$presetDir = Join-Path $DshUserRoot '.agent-presets\rimworld'
New-Item -ItemType Directory -Force -Path $presetDir | Out-Null
$presetBase64 = 'bmFtZTog546v5LiW55WMIEFJIOmYn+WPiwpkZXNjcmlwdGlvbjog5L2/55SoIERlZXBTZWVrIEhhcm5lc3Mg5LiK5LiL5paH5ZKMIFJpbVdvcmxkIOWOn+eUn+eKtuaAgeW3peWFt++8jOS4jueOqeWutuaMgee7reWQiOS9nOmAmuWFs+OAggo='
$preset = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($presetBase64))
$composition = @"
- id: rimworld-agent
  name: $(Quote-Yaml $pluginEntry)
  config:
    enabled: true
    pythonCommand: $(Quote-Yaml $pythonEntry)
    serverName: rimworld
    toolCallTimeoutMs: 60000

- id: compaction
  name: cordis:group
  group: true
  isolate:
    compaction: true
    toolResultPruner: true
  config:
    - id: compaction-basic
      name: '@deepseek-ai/dsh-compaction-basic'
      config:
        thresholdRatio: 0.8
        retainRatio: 0.16

    - id: tool-result-pruner
      name: '@deepseek-ai/dsh-compaction-tool-result-pruner'
      config:
        thresholdChars: 8192
        headChars: 4096
        tailChars: 1024
"@
$utf8 = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText((Join-Path $presetDir 'preset.yml'), $preset, $utf8)
[System.IO.File]::WriteAllText((Join-Path $presetDir 'agent.cordis.yml'), $composition, $utf8)
Write-Host "DeepSeek Harness preset installed: $presetDir"
Write-Host 'Create a new Harness session and select the RimWorld AI teammate preset.'
