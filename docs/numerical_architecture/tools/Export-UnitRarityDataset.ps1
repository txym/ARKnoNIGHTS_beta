[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BondSpecPath,

    [Parameter(Mandatory = $true)]
    [string]$StagingRoot,

    [Parameter(Mandatory = $true)]
    [string]$OutputCsvPath,

    # Windows PowerShell 5.1 can culture-bind "1007,1055" passed after -File as
    # one integer. Invoke this script from PowerShell with @(1007, 1055), such
    # as through -Command or a wrapper, so the public interface remains int[].
    [int[]]$PendingRemovalIds = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-ExistingPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $resolved = Resolve-Path -LiteralPath $Path -ErrorAction Stop
    return $resolved.Path
}

function Assert-Condition {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not $Condition) {
        throw "Dataset assertion failed: $Message"
    }
}

function Get-LevelZero {
    param(
        [Parameter(Mandatory = $true)]$LevelsDocument,
        [Parameter(Mandatory = $true)][string]$DirectoryPath
    )

    $matches = @($LevelsDocument.levels | Where-Object { $_.level -eq 0 })
    if ($matches.Count -ne 1) {
        throw "Expected exactly one level: 0 row in '$DirectoryPath'; found $($matches.Count)."
    }

    return $matches[0]
}

function Get-PhysicalDamage {
    param(
        [Parameter(Mandatory = $true)][int]$Attack,
        [Parameter(Mandatory = $true)][int]$Defense
    )

    return [int][Math]::Max($Attack - $Defense, [Math]::Floor($Attack * 5.0 / 100.0))
}

function Get-MagicDamage {
    param(
        [Parameter(Mandatory = $true)][int]$Attack,
        [Parameter(Mandatory = $true)][int]$MagicResistance
    )

    return [int][Math]::Max(
        [Math]::Floor($Attack * (100.0 - $MagicResistance) / 100.0),
        [Math]::Floor($Attack * 5.0 / 100.0)
    )
}

function Get-OrdinaryAttackDamage {
    param(
        [Parameter(Mandatory = $true)]$Attacker,
        [Parameter(Mandatory = $true)]$Defender
    )

    switch ([string]$Attacker.DamageType) {
        'Physical' { return Get-PhysicalDamage -Attack $Attacker.Attack -Defense $Defender.Defense }
        'Magic' { return Get-MagicDamage -Attack $Attacker.Attack -MagicResistance $Defender.MagicResistance }
        'True' { return [int]$Attacker.Attack }
        default { throw "Cannot calculate ordinary attack damage for type ID $($Attacker.TypeId) with damage type '$($Attacker.DamageType)'." }
    }
}

function Get-Percentile {
    param(
        [Parameter(Mandatory = $true)][decimal[]]$Values,
        [Parameter(Mandatory = $true)][decimal]$Percentile
    )

    Assert-Condition ($Values.Count -gt 0) 'Cannot calculate a percentile from an empty sample.'
    Assert-Condition ($Percentile -ge 0 -and $Percentile -le 1) "Invalid percentile $Percentile."

    $sortedValues = @($Values | Sort-Object)
    $position = ([decimal]($sortedValues.Count - 1)) * $Percentile
    $lowerIndex = [int][Math]::Floor([double]$position)
    $upperIndex = [int][Math]::Ceiling([double]$position)
    if ($lowerIndex -eq $upperIndex) {
        return $sortedValues[$lowerIndex]
    }

    $fraction = $position - $lowerIndex
    return $sortedValues[$lowerIndex] + (($sortedValues[$upperIndex] - $sortedValues[$lowerIndex]) * $fraction)
}

function Get-Median {
    param([Parameter(Mandatory = $true)][decimal[]]$Values)

    return Get-Percentile -Values $Values -Percentile ([decimal]0.5)
}

function Get-QuantileSummary {
    param([Parameter(Mandatory = $true)]$Defenders)

    $percentiles = [ordered]@{ P25 = [decimal]0.25; P50 = [decimal]0.5; P75 = [decimal]0.75; P90 = [decimal]0.9 }
    $summary = [ordered]@{}
    foreach ($property in @('Defense', 'MagicResistance', 'MaxHitPoints')) {
        $values = [decimal[]]@($Defenders | ForEach-Object { [decimal]$_.$property })
        $summary[$property] = [ordered]@{}
        foreach ($label in $percentiles.Keys) {
            $summary[$property][$label] = Get-Percentile -Values $values -Percentile $percentiles[$label]
        }
    }

    return $summary
}

$BondSpecPath = Resolve-ExistingPath $BondSpecPath
$StagingRoot = Resolve-ExistingPath $StagingRoot
$OutputCsvPath = [System.IO.Path]::GetFullPath($OutputCsvPath)
$pendingRemovalTypeIds = [System.Collections.Generic.HashSet[int]]::new()
foreach ($typeId in $PendingRemovalIds) {
    if (-not $pendingRemovalTypeIds.Add($typeId)) {
        throw "Pending-removal type ID '$typeId' was provided more than once."
    }
}

$specLines = Get-Content -LiteralPath $BondSpecPath -Encoding UTF8
$rarityLabel = [string]::Concat([char]0x7A00, [char]0x6709)
$regionSectionHeader = [string]::Concat('# ', [char]0x76EE, [char]0x524D, [char]0x8003, [char]0x8651, [char]0x4F7F, [char]0x7528, [char]0x7684, [char]0x5730, [char]0x533A)
$unitPattern = '^(?<TypeId>\d+)\s+(?<DisplayName>.+?)\s+' + $rarityLabel + '(?<Rarity>[1-6])(?:\s|$)'
$categoryPattern = '^##\s+(.+?)\s*$'
$nonShopTypeIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($typeId in @('1137', '1138', '10002', '2033')) {
    [void]$nonShopTypeIds.Add($typeId)
}

$roster = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
$category = $null
foreach ($line in $specLines) {
    if ($line -match $categoryPattern) {
        $category = $Matches[1]
        continue
    }

    if ($line -notmatch $unitPattern) {
        continue
    }

    $typeId = $Matches.TypeId
    if ($roster.ContainsKey($typeId)) {
        throw "Duplicate unit type ID '$typeId' in '$BondSpecPath'."
    }

    $roster.Add($typeId, [pscustomobject]@{
        TypeId = $typeId
        DisplayName = $Matches.DisplayName.Trim()
        Category = $category
        CurrentRarity = [int]$Matches.Rarity
        IsShopCandidate = -not $nonShopTypeIds.Contains($typeId)
    })
}

foreach ($typeId in $pendingRemovalTypeIds) {
    $typeIdKey = [string]$typeId
    if (-not $roster.ContainsKey($typeIdKey)) {
        throw "Pending-removal type ID '$typeId' does not exist in the roster."
    }
    if (-not $roster[$typeIdKey].IsShopCandidate) {
        throw "Pending-removal type ID '$typeId' is not a shop candidate."
    }
}

$regionsByTypeId = @{}
$inRegionSection = $false
$currentRegion = $null
foreach ($line in $specLines) {
    if (-not $inRegionSection) {
        if ($line -eq $regionSectionHeader) {
            $inRegionSection = $true
        }
        continue
    }

    if ($line -match $categoryPattern) {
        $currentRegion = $Matches[1]
        continue
    }

    if ($null -ne $currentRegion -and ($line -match '^\d+$') -and $roster.ContainsKey($line)) {
        if (-not $regionsByTypeId.ContainsKey($line)) {
            $regionsByTypeId[$line] = [System.Collections.Generic.List[string]]::new()
        }
        if (-not $regionsByTypeId[$line].Contains($currentRegion)) {
            $regionsByTypeId[$line].Add($currentRegion)
        }
    }
}

$rows = foreach ($entry in $roster.Values | Sort-Object { [int]$_.TypeId }) {
    $baseDirectories = @(
        Get-ChildItem -LiteralPath $StagingRoot -Directory -Filter "$($entry.TypeId)_*" |
            Where-Object { $_.Name -notmatch '_[23]$' }
    )
    if ($baseDirectories.Count -ne 1) {
        throw "Expected exactly one base package for type ID $($entry.TypeId); found $($baseDirectories.Count)."
    }

    $baseDirectory = $baseDirectories[0]
    $levelsPath = Join-Path $baseDirectory.FullName 'unit-levels.json'
    $sourcePath = Join-Path $baseDirectory.FullName 'unit-source-v1.json'
    $levelZero = Get-LevelZero -LevelsDocument (Get-Content -LiteralPath $levelsPath -Raw -Encoding UTF8 | ConvertFrom-Json) -DirectoryPath $baseDirectory.FullName
    $source = Get-Content -LiteralPath $sourcePath -Raw -Encoding UTF8 | ConvertFrom-Json

    $elite2Directory = Join-Path $StagingRoot "$($baseDirectory.Name)_2"
    $elite3Directory = Join-Path $StagingRoot "$($baseDirectory.Name)_3"
    $regions = if ($regionsByTypeId.ContainsKey($entry.TypeId)) { $regionsByTypeId[$entry.TypeId] -join ';' } else { '' }

    [pscustomobject][ordered]@{
        TypeId = [int]$entry.TypeId
        DisplayName = $entry.DisplayName
        Category = $entry.Category
        CurrentRarity = $entry.CurrentRarity
        IsShopCandidate = [bool]$entry.IsShopCandidate
        RosterStatus = if (-not $entry.IsShopCandidate) { 'NonShop' } elseif ($pendingRemovalTypeIds.Contains([int]$entry.TypeId)) { 'PendingRemoval' } else { 'Retained' }
        Regions = $regions
        ResourceDirectory = $baseDirectory.Name
        DamageType = $source.damageType
        MaxHitPoints = [int]$levelZero.maxHitPoints
        Attack = [int]$levelZero.attack
        Defense = [int]$levelZero.defense
        MagicResistance = [int]$levelZero.magicResistance
        AttackIntervalSeconds = [decimal]$levelZero.attackIntervalSeconds
        EffectiveAttackIntervalSeconds = [decimal]$levelZero.attackIntervalSeconds * [decimal]0.5
        MoveSpeedMetresPerSecond = [decimal]$levelZero.moveSpeedMetresPerSecond
        LifeDeduct = [int]$levelZero.lifeDeduct
        HasElite2 = Test-Path -LiteralPath $elite2Directory -PathType Container
        HasElite3 = Test-Path -LiteralPath $elite3Directory -PathType Container
    }
}

$shopRows = @($rows | Where-Object IsShopCandidate)
$nonShopRows = @($rows | Where-Object { -not $_.IsShopCandidate })
$retainedShopRows = @($shopRows | Where-Object { $_.RosterStatus -eq 'Retained' })
Assert-Condition ($shopRows.Count -eq 83) "Expected 83 unique shop candidates; found $($shopRows.Count)."
Assert-Condition ($nonShopRows.Count -eq 4) "Expected 4 unique non-shop units; found $($nonShopRows.Count)."

$unattackableDroneTypeIds = [System.Collections.Generic.HashSet[int]]::new()
foreach ($typeId in @(1017, 1042, 1355, 1146)) {
    [void]$unattackableDroneTypeIds.Add($typeId)
}
$firstPassDefenderRows = @($shopRows | Where-Object { -not $unattackableDroneTypeIds.Contains([int]$_.TypeId) })
Assert-Condition ($firstPassDefenderRows.Count -eq 79) "Expected 79 first-pass defenders; found $($firstPassDefenderRows.Count)."
$defenderRows = @($retainedShopRows | Where-Object { -not $unattackableDroneTypeIds.Contains([int]$_.TypeId) })

$firstPassQuantiles = Get-QuantileSummary -Defenders $firstPassDefenderRows
$expectedFirstPassQuantiles = @{
    Defense = @{ P25 = 100; P50 = 300; P75 = 775; P90 = 1040 }
    MagicResistance = @{ P25 = 0; P50 = 20; P75 = 32.5; P90 = 50 }
    MaxHitPoints = @{ P25 = 3100; P50 = 6000; P75 = 11500; P90 = 20000 }
}
foreach ($property in $expectedFirstPassQuantiles.Keys) {
    foreach ($label in $expectedFirstPassQuantiles[$property].Keys) {
        Assert-Condition (
            $firstPassQuantiles[$property][$label] -eq [decimal]$expectedFirstPassQuantiles[$property][$label]
        ) "Expected first-pass $property $label=$($expectedFirstPassQuantiles[$property][$label]); found $($firstPassQuantiles[$property][$label])."
    }
}
$secondPassQuantiles = Get-QuantileSummary -Defenders $defenderRows

foreach ($row in $rows) {
    Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$row.DamageType)) "Type ID $($row.TypeId) is missing a damage type."
}
$attackingRows = @($retainedShopRows | Where-Object { $_.DamageType -ne 'None' })
foreach ($row in $attackingRows) {
    Assert-Condition ($row.EffectiveAttackIntervalSeconds -gt 0) "Type ID $($row.TypeId) has a nonpositive effective attack interval."
}

$attackMetricsByTypeId = @{}
foreach ($attacker in $attackingRows) {
    $damages = [System.Collections.Generic.List[decimal]]::new()
    $dpsValues = [System.Collections.Generic.List[decimal]]::new()
    $ttks = [System.Collections.Generic.List[decimal]]::new()
    $physicalFloorTargets = 0
    foreach ($defender in $defenderRows) {
        $damage = Get-OrdinaryAttackDamage -Attacker $attacker -Defender $defender
        $effectiveInterval = [decimal]$attacker.EffectiveAttackIntervalSeconds
        [void]$damages.Add($damage)
        [void]$dpsValues.Add(([decimal]$damage / $effectiveInterval))
        [void]$ttks.Add(([decimal][Math]::Ceiling([double]([decimal]$defender.MaxHitPoints / $damage))) * $effectiveInterval)
        if ($attacker.DamageType -eq 'Physical' -and $damage -eq [Math]::Floor($attacker.Attack * 5.0 / 100.0)) {
            $physicalFloorTargets++
        }
    }

    $attackMetricsByTypeId[[int]$attacker.TypeId] = [pscustomobject]@{
        MedianDamagePerHit = Get-Median -Values $damages.ToArray()
        MedianDps = Get-Median -Values $dpsValues.ToArray()
        MedianTtkSeconds = Get-Median -Values $ttks.ToArray()
        P75TtkSeconds = Get-Percentile -Values $ttks.ToArray() -Percentile ([decimal]0.75)
        PhysicalFloorTargetRate = if ($attacker.DamageType -eq 'Physical') { [decimal]$physicalFloorTargets / $defenderRows.Count } else { $null }
    }
}

$medianIncomingTtdByTypeId = @{}
foreach ($defender in $retainedShopRows) {
    $incomingTtks = [System.Collections.Generic.List[decimal]]::new()
    foreach ($attacker in $attackingRows) {
        $damage = Get-OrdinaryAttackDamage -Attacker $attacker -Defender $defender
        [void]$incomingTtks.Add(([decimal][Math]::Ceiling([double]([decimal]$defender.MaxHitPoints / $damage))) * [decimal]$attacker.EffectiveAttackIntervalSeconds)
    }
    $medianIncomingTtdByTypeId[[int]$defender.TypeId] = Get-Median -Values $incomingTtks.ToArray()
}

$rows = foreach ($row in $rows) {
    $metrics = if ($attackMetricsByTypeId.ContainsKey([int]$row.TypeId)) { $attackMetricsByTypeId[[int]$row.TypeId] } else { $null }
    [pscustomobject][ordered]@{
        TypeId = $row.TypeId
        DisplayName = $row.DisplayName
        Category = $row.Category
        CurrentRarity = $row.CurrentRarity
        IsShopCandidate = $row.IsShopCandidate
        RosterStatus = $row.RosterStatus
        Regions = $row.Regions
        ResourceDirectory = $row.ResourceDirectory
        DamageType = $row.DamageType
        MaxHitPoints = $row.MaxHitPoints
        Attack = $row.Attack
        Defense = $row.Defense
        MagicResistance = $row.MagicResistance
        AttackIntervalSeconds = $row.AttackIntervalSeconds
        EffectiveAttackIntervalSeconds = $row.EffectiveAttackIntervalSeconds
        MoveSpeedMetresPerSecond = $row.MoveSpeedMetresPerSecond
        LifeDeduct = $row.LifeDeduct
        HasElite2 = $row.HasElite2
        HasElite3 = $row.HasElite3
        MedianDamagePerHit = if ($null -ne $metrics) { $metrics.MedianDamagePerHit } else { $null }
        MedianDps = if ($null -ne $metrics) { $metrics.MedianDps } else { $null }
        MedianTtkSeconds = if ($null -ne $metrics) { $metrics.MedianTtkSeconds } else { $null }
        P75TtkSeconds = if ($null -ne $metrics) { $metrics.P75TtkSeconds } else { $null }
        PhysicalFloorTargetRate = if ($null -ne $metrics) { $metrics.PhysicalFloorTargetRate } else { $null }
        MedianIncomingTtdSeconds = if ($medianIncomingTtdByTypeId.ContainsKey([int]$row.TypeId)) { $medianIncomingTtdByTypeId[[int]$row.TypeId] } else { $null }
        BaseChassisNotes = if ($row.RosterStatus -eq 'PendingRemoval') { 'pending removal; excluded from second-pass matchup populations' } elseif ($row.DamageType -eq 'None') { 'ability-only' } elseif ($row.IsShopCandidate) { 'raw-base ordinary attacks only; ability adjustment pending' } else { 'non-shop; raw-base survival reference only' }
    }
}

foreach ($row in $rows) {
    foreach ($property in @('MedianDamagePerHit', 'MedianDps', 'MedianTtkSeconds', 'P75TtkSeconds', 'PhysicalFloorTargetRate', 'MedianIncomingTtdSeconds')) {
        $value = $row.$property
        if ($null -eq $value) {
            continue
        }
        Assert-Condition (-not [double]::IsNaN([double]$value) -and -not [double]::IsInfinity([double]$value)) "Type ID $($row.TypeId) has invalid $property=$value."
    }
    foreach ($property in @('MedianTtkSeconds', 'P75TtkSeconds', 'MedianIncomingTtdSeconds')) {
        if ($null -ne $row.$property) {
            Assert-Condition ($row.$property -ge 0) "Type ID $($row.TypeId) has negative $property=$($row.$property)."
        }
    }
}

$expectedRows = @{
    1000 = @{ MaxHitPoints = 820; Attack = 190; Defense = 0; MagicResistance = 20; AttackIntervalSeconds = [decimal]1.4; DamageType = 'Physical' }
    1089 = @{ DamageType = 'Magic' }
    2031 = @{ MaxHitPoints = 35000; Attack = 800; Defense = 800; MagicResistance = 50; LifeDeduct = 5 }
    1169 = @{ MaxHitPoints = 4000; Attack = 300; Defense = 300; AttackIntervalSeconds = [decimal]2.5 }
}
foreach ($typeId in $expectedRows.Keys) {
    $row = @($rows | Where-Object { $_.TypeId -eq [int]$typeId })
    Assert-Condition ($row.Count -eq 1) "Expected known type ID $typeId exactly once."
    foreach ($property in $expectedRows[$typeId].Keys) {
        Assert-Condition ($row[0].$property -eq $expectedRows[$typeId][$property]) "Type ID $typeId expected $property=$($expectedRows[$typeId][$property]); found $($row[0].$property)."
    }
}

$outputDirectory = Split-Path -Parent $OutputCsvPath
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    [System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
}
[System.IO.File]::WriteAllLines($OutputCsvPath, @($rows | ConvertTo-Csv -NoTypeInformation), [System.Text.UTF8Encoding]::new($false))
Write-Host "Exported $($rows.Count) rows to '$OutputCsvPath' ($($retainedShopRows.Count) retained, $($shopRows.Count - $retainedShopRows.Count) pending removal, $($nonShopRows.Count) non-shop; $($defenderRows.Count) defenders, $($attackingRows.Count) attackers)."
Write-Host "Second-pass defender quantiles (P25/P50/P75/P90): DEF=$($secondPassQuantiles.Defense.P25)/$($secondPassQuantiles.Defense.P50)/$($secondPassQuantiles.Defense.P75)/$($secondPassQuantiles.Defense.P90); MR=$($secondPassQuantiles.MagicResistance.P25)/$($secondPassQuantiles.MagicResistance.P50)/$($secondPassQuantiles.MagicResistance.P75)/$($secondPassQuantiles.MagicResistance.P90); HP=$($secondPassQuantiles.MaxHitPoints.P25)/$($secondPassQuantiles.MaxHitPoints.P50)/$($secondPassQuantiles.MaxHitPoints.P75)/$($secondPassQuantiles.MaxHitPoints.P90)."
