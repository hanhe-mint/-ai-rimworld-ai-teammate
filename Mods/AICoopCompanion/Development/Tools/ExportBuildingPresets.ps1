param(
    [string]$SavePath = '',
    [string]$OutputDir = (Join-Path $PSScriptRoot '..\..\Presets')
)

# Export numbered AICoopPresetMarker rooms. Coordinates in BUILD records are
# relative to the connected wall/door shell lower-left corner.
$saveRoot = 'C:\Users\Administrator\AppData\LocalLow\Ludeon Studios'
if ([string]::IsNullOrWhiteSpace($SavePath) -or !(Test-Path -LiteralPath $SavePath)) {
    $SavePath = (Get-ChildItem -LiteralPath $saveRoot -Recurse -File -Filter '*.rws' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
}
if ([string]::IsNullOrWhiteSpace($SavePath) -or !(Test-Path -LiteralPath $SavePath)) { throw 'No .rws save found under LocalLow/Ludeon Studios.' }
$xml = [xml](Get-Content -Raw -Encoding UTF8 -LiteralPath $SavePath)
$things = @($xml.savegame.game.maps.li.things.ChildNodes)
$markers = @($things | Where-Object { $_.markerLabel })
$excludedNumbers = @(1, 3, 6, 13)
$fileByNumber = @{
    2='room_collective_prison'; 4='room_hydroponics'; 5='room_kitchen_cold'; 7='room_mini_prison'
    8='room_pasture_hut'; 9='room_power_standard'; 10='room_recreation_dorm_dining'; 11='room_recreation_hall'
    12='room_research_medical'; 14='room_shared_dorm'; 15='room_single_bedrooms'; 16='room_starting_core'
    17='room_throne_hall'; 18='room_warehouse'; 19='room_workshop_1'; 20='room_workshop_2'
}

function Pos([object]$node) {
    $m = [regex]::Match([string]$node.pos, '\(([-0-9]+),\s*0,\s*([-0-9]+)\)')
    if (!$m.Success) { return $null }
    return [pscustomobject]@{ X=[int]$m.Groups[1].Value; Z=[int]$m.Groups[2].Value }
}
function Key([int]$x, [int]$z) { return "$x,$z" }
function IsShell([object]$node) { return $node.def -eq 'Wall' -or $node.def -eq 'Door' }
function IsBuilding([object]$node) { return ($node.Class -match '^Building') -or (IsShell $node) }

$thingByCell = @{}
$buildingRows = [Collections.Generic.List[object]]::new()
foreach ($thing in $things) {
    $p = Pos $thing
    if ($null -eq $p) { continue }
    $k = Key $p.X $p.Z
    if (!$thingByCell.ContainsKey($k)) { $thingByCell[$k] = @() }
    $thingByCell[$k] += $thing
    if (IsBuilding $thing) {
        $buildingRows.Add([pscustomobject]@{ Node=$thing; X=$p.X; Z=$p.Z })
    }
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
foreach ($marker in $markers) {
    $numberMatch = [regex]::Match([string]$marker.markerLabel, '[0-9]+')
    if (!$numberMatch.Success) { continue }
    $number = [int]$numberMatch.Value
    if ($excludedNumbers -contains $number -or !$fileByNumber.ContainsKey($number)) { continue }
    $mp = Pos $marker
    if ($null -eq $mp) { continue }

    # Marker is one cell left of the shell's lower-left corner.
    $queue = [Collections.Generic.Queue[string]]::new()
    $seen = [Collections.Generic.HashSet[string]]::new()
    $shell = [Collections.Generic.List[object]]::new()
    $queue.Enqueue((Key ($mp.X + 1) $mp.Z))
    while ($queue.Count -gt 0) {
        $k = $queue.Dequeue()
        if (!$seen.Add($k) -or !$thingByCell.ContainsKey($k)) { continue }
        $cellThings = @($thingByCell[$k] | Where-Object { IsShell $_ })
        if ($cellThings.Count -eq 0) { continue }
        $first = $cellThings[0]; $p = Pos $first; $shell.Add($first)
        $queue.Enqueue((Key ($p.X + 1) $p.Z)); $queue.Enqueue((Key ($p.X - 1) $p.Z))
        $queue.Enqueue((Key $p.X ($p.Z + 1))); $queue.Enqueue((Key $p.X ($p.Z - 1)))
    }
    if ($shell.Count -eq 0) { continue }
    $shellPos = @($shell | ForEach-Object { Pos $_ })
    $minX = ($shellPos | Measure-Object X -Minimum).Minimum; $maxX = ($shellPos | Measure-Object X -Maximum).Maximum
    $minZ = ($shellPos | Measure-Object Z -Minimum).Minimum; $maxZ = ($shellPos | Measure-Object Z -Maximum).Maximum
    $width = $maxX - $minX + 1; $height = $maxZ - $minZ + 1

    $label = [string]$marker.markerLabel
    $colon = $label.IndexOf([char]0xFF1A)
    $purpose = if ($colon -ge 0) { $label.Substring($colon + 1) } else { "preset_$number" }
    $lines = [Collections.Generic.List[string]]::new()
    $id = $fileByNumber[$number] -replace '^room_', ''
    $lines.Add("# Captured from 建筑.rws: $label")
    $lines.Add("ROOM id=$id purpose=$purpose priority=5")
    $lines.Add("FOOTPRINT outer=${width}x${height} anchor=$minX,$minZ")
    $lines.Add('RULE Captured walls, doors and facilities use relative coordinates; rotate the complete layout by 0/90/180/270 degrees only.')
    $defs = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($row in $buildingRows) {
        $thing = $row.Node
        if ($row.X -lt $minX -or $row.X -gt $maxX -or $row.Z -lt $minZ -or $row.Z -gt $maxZ) { continue }
        $rot = if ($thing.rot) { [string]$thing.rot } else { '0' }
        $stuff = if ($thing.stuff) { [string]$thing.stuff } else { '-' }
        $rx = $row.X - $minX; $rz = $row.Z - $minZ
        $lines.Add("BUILD $($thing.def) $rx $rz $rot $stuff")
        if ($thing.def -ne 'Wall' -and $thing.def -ne 'Door') { [void]$defs.Add([string]$thing.def) }
        if ($thing.def -eq 'Door') {
            $hold = if ($thing.holdOpen) { [string]$thing.holdOpen } else { 'False' }
            $lines.Add("STATE door $rx $rz hold_open=$hold")
        } elseif ($thing.def -eq 'Bed' -or $thing.def -eq 'HospitalBed' -or $thing.def -eq 'AnimalBed') {
            $medical = if ($thing.def -eq 'HospitalBed' -or [string]$thing.medical -eq 'True') { 'True' } else { 'False' }
            $prisoner = if ([string]$thing.forPrisoners -eq 'True') { 'True' } else { 'False' }
            $slave = if ([string]$thing.forSlaves -eq 'True') { [string]$thing.forSlaves } else { 'False' }
            $lines.Add("STATE bed $rx $rz medical=$medical prisoner=$prisoner slave=$slave")
        }
    }
    if ($defs.Count -gt 0) { $lines.Insert(3, "KNOWN_DEFS $($defs -join ',')") }
    $lines.Add("FLOOR terrainGrid_saved x1=0 z1=0 x2=$($width-1) z2=$($height-1) source_map=0 anchor=$minX,$minZ")

    foreach ($zone in @($xml.savegame.game.maps.li.zoneManager.allZones.ChildNodes | Where-Object { $_.Class -eq 'Zone_Growing' })) {
        $cells = @($zone.cells.li | ForEach-Object {
            $m = [regex]::Match([string]$_, '\(([-0-9]+),\s*0,\s*([-0-9]+)\)')
            if ($m.Success) { [pscustomobject]@{ X=[int]$m.Groups[1].Value; Z=[int]$m.Groups[2].Value } }
        } | Where-Object { $_.X -ge $minX -and $_.X -le $maxX -and $_.Z -ge $minZ -and $_.Z -le $maxZ })
        if ($cells.Count -gt 0) {
            $encoded = ($cells | Sort-Object Z,X | ForEach-Object { "$($_.X-$minX):$($_.Z-$minZ)" }) -join ','
            $plant = if ($zone.plantDefToGrow) { [string]$zone.plantDefToGrow } else { '-' }
            $lines.Add("ZONE growing plant=$plant cells=$encoded")
        }
    }
    $lines.Add('END_ROOM')
    $outPath = Join-Path $OutputDir ($fileByNumber[$number] + '.txt')
    [IO.File]::WriteAllLines($outPath, $lines, [Text.UTF8Encoding]::new($false))
}

# Retired templates (including the full-DLC starting hut).
foreach ($old in @('room_children_classroom.txt','room_sewage_power.txt','room_mech_factory.txt','room_containment.txt','room_starting_core_full_dlc.txt','room_starting_full_dlc.txt')) {
    $oldPath = Join-Path $OutputDir $old
    if (Test-Path -LiteralPath $oldPath) { Remove-Item -LiteralPath $oldPath -Force }
}
