[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BondSpecPath,

    [Parameter(Mandatory = $true)]
    [string]$StagingRoot,

    [Parameter(Mandatory = $true)]
    [string]$OutputCsvPath
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

$BondSpecPath = Resolve-ExistingPath $BondSpecPath
$StagingRoot = Resolve-ExistingPath $StagingRoot
$OutputCsvPath = [System.IO.Path]::GetFullPath($OutputCsvPath)

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
Assert-Condition ($shopRows.Count -eq 83) "Expected 83 unique shop candidates; found $($shopRows.Count)."
Assert-Condition ($nonShopRows.Count -eq 4) "Expected 4 unique non-shop units; found $($nonShopRows.Count)."

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
Write-Host "Exported $($rows.Count) rows to '$OutputCsvPath' ($($shopRows.Count) shop candidates, $($nonShopRows.Count) non-shop units)."
