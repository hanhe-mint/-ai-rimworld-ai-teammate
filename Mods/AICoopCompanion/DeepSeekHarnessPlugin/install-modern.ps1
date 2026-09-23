param(
    [Parameter(Mandatory=$true)][string]$DshUserRoot,
    [string]$Profile = 'web',
    [string]$PythonCommand = 'python.exe'
)
$ErrorActionPreference = 'Stop'
if ($Profile -notmatch '^[a-zA-Z0-9_-]+$') { throw 'Invalid profile name.' }
$root = (Resolve-Path -LiteralPath $DshUserRoot).Path
$profileDir = Join-Path $root "profiles\$Profile"
if (-not (Test-Path -LiteralPath $profileDir -PathType Container)) { throw 'Start this DSH profile once before installation.' }
# DSH's loader calls import(name) directly for any name not starting with ".".
# On Windows a bare absolute path throws ERR_UNSUPPORTED_ESM_URL_SCHEME, so the
# preset would show as failed ("never started"). Emit a file:// URL instead.
$entryPath = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot 'index.js')).Path
$entry = [System.Uri]::new($entryPath).AbsoluteUri
$python = (Get-Command $PythonCommand -ErrorAction Stop).Source.Replace('\','/')
$entry = $entry.Replace("'", "''")
$python = $python.Replace("'", "''")
$path = Join-Path $profileDir 'cordis.patch.yml'
$existing = if (Test-Path -LiteralPath $path) { [IO.File]::ReadAllText($path) } else { '' }
$start = '# BEGIN rimworld-ai-coop managed preset'
$end = '# END rimworld-ai-coop managed preset'
$block = @"
$start
- insert:
    - id: preset-rimworld
      name: '@deepseek-ai/dsh-agent-preset'
      config:
        id: rimworld
        name: 环世界 AI 队友
        description: 通过游戏原生状态与CLI工具，和玩家合作游玩环世界。
        order: 20
        plugins:
          - id: tool-web
            name: '@deepseek-ai/dsh-tool-web'
          - id: rimworld-agent
            name: '$entry'
            config:
              enabled: true
              pythonCommand: '$python'
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
$end
"@
$pattern = '(?ms)^' + [regex]::Escape($start) + '\r?\n.*?^' + [regex]::Escape($end) + '(?=\r?$)'
if ($existing.Contains($start)) {
    if ([regex]::Matches($existing,$pattern).Count -ne 1) { throw 'Managed block is incomplete or duplicated; no changes made.' }
    $updated = [regex]::Replace($existing,$pattern,[Text.RegularExpressions.MatchEvaluator]{ param($match) $block })
} else {
    if ($existing -match 'preset-rimworld') { throw 'An unmanaged RimWorld preset already exists; no changes made.' }
    # The shipped template is a comment header followed by an empty "[]" document.
    # Appending rows after that "[]" yields invalid YAML, so drop the empty
    # document line before appending the managed block.
    if ($existing -match '(?m)^\[\][ \t]*\r?$') {
        $existing = [regex]::Replace($existing, '(?m)^\[\][ \t]*\r?\n?', '')
    }
    $updated = $existing.TrimEnd() + "`r`n" + $block + "`r`n"
}
[IO.File]::WriteAllText($path,$updated,[Text.UTF8Encoding]::new($false))
Write-Host "Registered RimWorld preset in $path. Restart DSH and create a new session."
