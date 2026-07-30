[CmdletBinding()]
param(
    [string]$BondSpecPath,
    [string]$MetadataStagingRoot = 'G:\ARKnoNIGHTS_tools\spine-fetcher-output-variants-20260725\staging',
    [string]$DeltaStagingRoot = 'G:\ARKnoNIGHTS_tools\spine-fetcher-output-bonds-delta-20260729\staging',
    [string]$AnimationAuditPath,
    [string]$CharacterRoot,
    [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:StrictUtf8 = [System.Text.UTF8Encoding]::new($false, $true)
$script:NoAttackTypeIds = @(1008, 1017, 1026, 1042, 1146, 1333, 1355, 10002)
$script:UnblockableTypeIds = @(1008, 1017, 1026, 1042, 1146, 1333, 1355)
$script:ActionMethod2TypeIds = @(1008, 1017, 1026, 1042, 1355)
$script:ActionMethod3TypeIds = @(1146)
$script:ActionMethod4TypeIds = @(10002)
$script:PreservedDocumentNames = @(
    '1000_gopro.json',
    '5503_arcslma.json',
    '5504_arcslmi.json'
)

function Read-StrictJson {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    if (-not [System.IO.File]::Exists($Path)) {
        throw "Required JSON file is missing: $Path"
    }
    $text = [System.IO.File]::ReadAllText($Path, $script:StrictUtf8)
    try {
        return $text | ConvertFrom-Json
    } catch {
        throw "JSON file is invalid: $Path`n$($_.Exception.Message)"
    }
}

function Assert-SourceLevelZeroStats {
    param(
        [Parameter(Mandatory)]
        [object]$Stats,
        [Parameter(Mandatory)]
        [string]$UnitKey
    )

    $requiredNumericProperties = @(
        'maxHitPoints',
        'attack',
        'defense',
        'magicResistance',
        'moveSpeedMetresPerSecond',
        'attackIntervalSeconds',
        'lifeDeduct'
    )
    $numericTypeCodes = @(
        [System.TypeCode]::Byte,
        [System.TypeCode]::SByte,
        [System.TypeCode]::Int16,
        [System.TypeCode]::UInt16,
        [System.TypeCode]::Int32,
        [System.TypeCode]::UInt32,
        [System.TypeCode]::Int64,
        [System.TypeCode]::UInt64,
        [System.TypeCode]::Single,
        [System.TypeCode]::Double,
        [System.TypeCode]::Decimal
    )

    foreach ($propertyName in $requiredNumericProperties) {
        $property = $Stats.PSObject.Properties[$propertyName]
        if ($null -eq $property -or $null -eq $property.Value) {
            throw "Source level-zero stat is missing or null: unitKey=$UnitKey property=$propertyName"
        }

        $typeCode = [System.Type]::GetTypeCode($property.Value.GetType())
        if ($typeCode -notin $numericTypeCodes) {
            throw "Source level-zero stat is not numeric: unitKey=$UnitKey property=$propertyName value=$($property.Value)"
        }

        $numericValue = [double]$property.Value
        if ([double]::IsNaN($numericValue) -or [double]::IsInfinity($numericValue)) {
            throw "Source level-zero stat is not finite: unitKey=$UnitKey property=$propertyName value=$($property.Value)"
        }
    }
}

function Read-BondsTypeIds {
    param(
        [Parameter(Mandatory)]
        [string]$Specification
    )

    $sections = @([regex]::Matches(
        $Specification,
        '(?ms)^## [^\r\n]+\s*```text\s*(?<ids>.*?)\s*```'))
    if ($sections.Count -ne 2) {
        throw "BONDS specification must contain exactly two TypeId text sections, actual=$($sections.Count)"
    }
    $parsed = @(
        $sections |
            ForEach-Object {
                [regex]::Matches($_.Groups['ids'].Value, '\d+') |
                    ForEach-Object { [int]$_.Value }
            }
    )
    $unique = @($parsed | Sort-Object -Unique)
    if ($parsed.Count -ne 99 -or $unique.Count -ne 99) {
        throw "BONDS specification must contain exactly 99 unique TypeIds, parsed=$($parsed.Count) unique=$($unique.Count)"
    }
    return $unique
}

function Read-BondsUnitFacts {
    param(
        [Parameter(Mandatory)]
        [string]$Specification,
        [Parameter(Mandatory)]
        [int[]]$TypeIds
    )

    $facts = @{}
    $tableMatch = [regex]::Match(
        $Specification,
        '(?ms)<!-- UNIT-COST-TABLE:START -->(?<table>.*?)<!-- UNIT-COST-TABLE:END -->')
    if (-not $tableMatch.Success) {
        throw 'BONDS unit cost table markers are missing.'
    }
    foreach ($match in [regex]::Matches(
        $tableMatch.Groups['table'].Value,
        '(?m)^\|\s*(?<id>\d+)\s*\|\s*(?<name>[^|]+?)\s*\|\s*(?<rarity>[1-6])\s*\|\s*(?<deploymentCost>\d+)\s*\|')) {
        $typeId = [int]$match.Groups['id'].Value
        if ($facts.ContainsKey($typeId)) {
            throw "Duplicate BONDS unit table TypeId=$typeId"
        }
        $facts[$typeId] = [pscustomobject]@{
            DisplayNameZhHans = $match.Groups['name'].Value.Trim()
            Rarity = [int]$match.Groups['rarity'].Value
            DeploymentCost =
                [int]$match.Groups['deploymentCost'].Value
        }
    }
    if ($facts.Count -ne 94) {
        throw "BONDS shop table must contain 94 units, actual=$($facts.Count)"
    }

    foreach ($typeId in @(1137, 1138, 2033, 5504, 10002)) {
        $matches = @([regex]::Matches(
            $Specification,
            "(?m)^$typeId\s+(?<name>.+?)\s+\u7A00\u6709(?<rarity>[1-6])(?:\s|\uFF08|$)"))
        if ($matches.Count -eq 0) {
            throw "BONDS non-shop unit fact is missing for TypeId=$typeId"
        }
        $names = @($matches | ForEach-Object { $_.Groups['name'].Value.Trim() } | Sort-Object -Unique)
        $rarities = @($matches | ForEach-Object { [int]$_.Groups['rarity'].Value } | Sort-Object -Unique)
        if ($names.Count -ne 1 -or $rarities.Count -ne 1) {
            throw "BONDS non-shop unit facts conflict for TypeId=$typeId"
        }
        $facts[$typeId] = [pscustomobject]@{
            DisplayNameZhHans = $names[0]
            Rarity = $rarities[0]
            DeploymentCost = 2
        }
    }

    $missing = @($TypeIds | Where-Object { -not $facts.ContainsKey($_) })
    $unexpected = @($facts.Keys | Where-Object { $_ -notin $TypeIds })
    if ($missing.Count -ne 0 -or $unexpected.Count -ne 0) {
        throw "BONDS unit facts do not match the TypeId overview: missing=$($missing -join ',') unexpected=$($unexpected -join ',')"
    }
    return $facts
}

function Get-SourceUnitKey {
    param(
        [Parameter(Mandatory)]
        [string]$ProjectUnitKey
    )

    if ($ProjectUnitKey -eq '1322_wdgyht') {
        return '1322_wdgyht_2'
    }
    if ($ProjectUnitKey -eq '1322_wdgyht_2') {
        return '1322_wdgyht'
    }
    return $ProjectUnitKey
}

function Get-SourceDirectory {
    param(
        [Parameter(Mandatory)]
        [string]$SourceUnitKey,
        [Parameter(Mandatory)]
        [string]$PrimaryRoot,
        [Parameter(Mandatory)]
        [string]$FallbackRoot
    )

    $primary = Join-Path $PrimaryRoot $SourceUnitKey
    if (Test-Path -LiteralPath $primary -PathType Container) {
        return $primary
    }
    $fallback = Join-Path $FallbackRoot $SourceUnitKey
    if (Test-Path -LiteralPath $fallback -PathType Container) {
        return $fallback
    }
    throw "No metadata source directory is available for unitKey=$SourceUnitKey"
}

function Get-EliteLevel {
    param(
        [Parameter(Mandatory)]
        [string]$UnitKey
    )

    if ($UnitKey -match '_(2|3)$') {
        return [int]$Matches[1]
    }
    return 0
}

function New-AnimationSpec {
    param(
        [Parameter(Mandatory)]
        [string]$Key,
        [Parameter(Mandatory)]
        [string]$Name
    )

    return [pscustomobject]@{ Key = $Key; Name = $Name }
}

function Get-AnimationPlan {
    param(
        [Parameter(Mandatory)]
        [int]$TypeId,
        [Parameter(Mandatory)]
        [string]$UnitKey,
        [Parameter(Mandatory)]
        [int]$AttackMethod
    )

    $idle = 'Idle'
    $move = 'Move'
    $attack = if ($AttackMethod -eq 1) { 'Attack' } else { $null }
    $death = 'Die'
    $additional = @()

    switch ($TypeId) {
        1001 {
            $move = 'Move_Loop'
            $additional += New-AnimationSpec -Key 'attack.2' -Name 'Attack2'
        }
        { $_ -in @(1006, 1061, 1081, 1108, 1170) } {
            $move = 'Move_Loop'
        }
        { $_ -in @(1008, 1042) } {
            $move = 'Move_Loop'
        }
        { $_ -in @(1043, 1077, 1087, 1165) } {
            $move = 'Run_Loop'
        }
        1107 {
            $attack = 'Combat'
        }
        1111 {
            $idle = 'Idile'
            $attack = 'Combat'
        }
        { $_ -in @(1116, 1119) } {
            $idle = 'Idle3'
            $move = 'Move3'
            $attack = 'Attack3'
            $death = 'Die3'
            $additional += @(
                (New-AnimationSpec -Key 'idle.2' -Name 'Idle2'),
                (New-AnimationSpec -Key 'move.2' -Name 'Move2'),
                (New-AnimationSpec -Key 'attack.2' -Name 'Attack2'),
                (New-AnimationSpec -Key 'death.2' -Name 'Die2'),
                (New-AnimationSpec -Key 'idle.released' -Name 'Idle'),
                (New-AnimationSpec -Key 'move.released' -Name 'Move'),
                (New-AnimationSpec -Key 'attack.released' -Name 'Attack'),
                (New-AnimationSpec -Key 'death.released' -Name 'Die')
            )
        }
        { $_ -in @(1118, 1121) } {
            $idle = 'Idle_grey'
            $move = 'Move_grey'
            $attack = 'Attack_grey'
            $death = 'Die_grey'
            $additional += @(
                (New-AnimationSpec -Key 'idle.orange' -Name 'Idle_orange'),
                (New-AnimationSpec -Key 'move.orange' -Name 'Move_orange'),
                (New-AnimationSpec -Key 'attack.orange' -Name 'Attack_orange'),
                (New-AnimationSpec -Key 'death.orange' -Name 'Die_orange'),
                (New-AnimationSpec -Key 'idle.red' -Name 'Idle_red'),
                (New-AnimationSpec -Key 'move.red' -Name 'Move_red'),
                (New-AnimationSpec -Key 'attack.red' -Name 'Attack_red'),
                (New-AnimationSpec -Key 'death.red' -Name 'Die_red')
            )
        }
        1254 {
            $attack = 'Attack_A'
            $additional += New-AnimationSpec -Key 'attack.b' -Name 'Attack_B'
        }
        2046 {
            $attack = 'Attack_A'
            $additional += New-AnimationSpec -Key 'attack.b' -Name 'Attack_B'
        }
        1281 {
            $death = 'Die_2'
        }
        { $_ -in @(1314, 1315, 1316) } {
            $idle = 'Idle_B'
            $move = 'Move_B'
            $attack = 'Attack'
            $death = 'Die_B'
        }
        1322 {
            $additional += @(
                (New-AnimationSpec -Key 'skill.begin' -Name 'Skill_Begin'),
                (New-AnimationSpec -Key 'skill.loop' -Name 'Skill_Loop'),
                (New-AnimationSpec -Key 'skill.end' -Name 'Skill_End')
            )
        }
        10001 {
            $idle = 'Idle_A'
            $move = 'Move_A'
            $attack = 'Attack_A'
            $death = 'Die_A'
            $additional += @(
                (New-AnimationSpec -Key 'idle.b' -Name 'Idle_B'),
                (New-AnimationSpec -Key 'move.b' -Name 'Move_B'),
                (New-AnimationSpec -Key 'attack.b' -Name 'Attack_B'),
                (New-AnimationSpec -Key 'death.b' -Name 'Die_B'),
                (New-AnimationSpec -Key 'skill.begin' -Name 'Skill_Begin')
            )
        }
        10002 {
            $additional += @(
                (New-AnimationSpec -Key 'start' -Name 'Start'),
                (New-AnimationSpec -Key 'skill.begin' -Name 'Skill_Begin'),
                (New-AnimationSpec -Key 'skill.loop' -Name 'Skill_Loop'),
                (New-AnimationSpec -Key 'skill.end' -Name 'Skill_End')
            )
        }
        10004 {
            $idle = 'Idle_A'
            $move = 'Move_A'
            $attack = 'Attack_A'
            $death = 'Die_A'
            $additional += @(
                (New-AnimationSpec -Key 'idle.b' -Name 'Idle_B'),
                (New-AnimationSpec -Key 'move.b' -Name 'Move_B'),
                (New-AnimationSpec -Key 'attack.b' -Name 'Attack_B'),
                (New-AnimationSpec -Key 'death.b' -Name 'Die_B'),
                (New-AnimationSpec -Key 'skill' -Name 'Skill_A')
            )
        }
        10005 {
            $move = 'Move1'
        }
        10006 {
            $move = 'Move_1'
            $additional += New-AnimationSpec -Key 'move.2' -Name 'Move_2'
        }
        { $_ -in @(10039, 10077) } {
            $additional += New-AnimationSpec -Key 'skill' -Name 'Skill'
        }
        { $_ -in @(2031, 2033) } {
            $additional += New-AnimationSpec -Key 'attack.skill' -Name 'Skill'
        }
        10126 {
            $idle = 'A_Idle'
            $move = 'A_Move'
            $attack = 'A_Attack'
            $death = 'A_Die'
        }
        10127 {
            $idle = 'A_Default'
            $move = 'A_Move'
            $attack = 'A_Attack'
            $death = 'A_Die'
        }
        1502 {
            # The data layer records both source animations without defining playback order.
            $additional += @(
                (New-AnimationSpec -Key 'blink.disappear' -Name 'Disappear'),
                (New-AnimationSpec -Key 'blink.appear' -Name 'Appear')
            )
        }
    }

    if ($UnitKey -eq '1131_sbeast_2') {
        $move = 'Move'
    }

    return [pscustomobject]@{
        Idle = $idle
        Move = $move
        Attack = $attack
        Death = $death
        Additional = @($additional)
    }
}

function Convert-AnimationPlan {
    param(
        [Parameter(Mandatory)]
        [object]$Plan,
        [Parameter(Mandatory)]
        [object]$AuditVariant,
        [Parameter(Mandatory)]
        [string]$UnitKey
    )

    $sourceAnimations = @{}
    foreach ($animation in @($AuditVariant.animations)) {
        if ($sourceAnimations.ContainsKey($animation.name)) {
            throw "Animation audit contains a duplicate name: unitKey=$UnitKey name=$($animation.name)"
        }
        $sourceAnimations[$animation.name] = [double]$animation.durationSeconds
    }

    $bindings = [System.Collections.Generic.List[object]]::new()
    foreach ($base in @(
            (New-AnimationSpec -Key 'idle' -Name $Plan.Idle),
            (New-AnimationSpec -Key 'move' -Name $Plan.Move))) {
        if (-not $sourceAnimations.ContainsKey($base.Name)) {
            throw "Required base animation is missing: unitKey=$UnitKey key=$($base.Key) name=$($base.Name)"
        }
        $bindings.Add([ordered]@{ key = $base.Key; name = $base.Name })
    }
    if (-not [string]::IsNullOrWhiteSpace($Plan.Attack)) {
        if (-not $sourceAnimations.ContainsKey($Plan.Attack)) {
            throw "Required attack animation is missing: unitKey=$UnitKey name=$($Plan.Attack)"
        }
        $duration = [Math]::Round(
            $sourceAnimations[$Plan.Attack],
            6,
            [MidpointRounding]::AwayFromZero)
        if ($duration -le 0) {
            throw "Required attack animation duration is invalid: unitKey=$UnitKey name=$($Plan.Attack)"
        }
        $bindings.Add([ordered]@{
            key = 'attack'
            name = $Plan.Attack
            durationSeconds = $duration
        })
    }
    if (-not $sourceAnimations.ContainsKey($Plan.Death)) {
        throw "Required death animation is missing: unitKey=$UnitKey name=$($Plan.Death)"
    }
    $bindings.Add([ordered]@{ key = 'death'; name = $Plan.Death })

    foreach ($additional in @($Plan.Additional)) {
        if (-not $sourceAnimations.ContainsKey($additional.Name)) {
            throw "Configured animation is missing: unitKey=$UnitKey key=$($additional.Key) name=$($additional.Name)"
        }
        $duration = [Math]::Round(
            $sourceAnimations[$additional.Name],
            6,
            [MidpointRounding]::AwayFromZero)
        if ($duration -le 0) {
            throw "Configured animation duration is invalid: unitKey=$UnitKey key=$($additional.Key) name=$($additional.Name)"
        }
        $bindings.Add([ordered]@{
            key = $additional.Key
            name = $additional.Name
            durationSeconds = $duration
        })
    }

    $keys = @($bindings | ForEach-Object { $_.key })
    if (@($keys | Sort-Object -Unique).Count -ne $keys.Count) {
        throw "Generated animation keys are not unique: unitKey=$UnitKey"
    }
    if (@($bindings | Where-Object { $_.name -like 'Default*' }).Count -ne 0) {
        throw "Default animations must not enter v2 JSON: unitKey=$UnitKey"
    }
    return @($bindings)
}

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($BondSpecPath)) {
    $BondSpecPath = Join-Path $repositoryRoot 'docs\bonds\BONDS_SPEC.md'
}
if ([string]::IsNullOrWhiteSpace($AnimationAuditPath)) {
    $AnimationAuditPath = Join-Path $repositoryRoot 'Artifacts\UnitDataImport20260729\bonds-unit-animation-audit-v1.json'
}
if ([string]::IsNullOrWhiteSpace($CharacterRoot)) {
    $CharacterRoot = Join-Path $repositoryRoot 'Assets\Resources\Characters'
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot 'Assets\GameData\Units\EliteVariants\Json'
}

foreach ($path in @($BondSpecPath, $AnimationAuditPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required input file is missing: $path"
    }
}
foreach ($path in @($MetadataStagingRoot, $DeltaStagingRoot, $CharacterRoot)) {
    if (-not (Test-Path -LiteralPath $path -PathType Container)) {
        throw "Required input directory is missing: $path"
    }
}
[System.IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null

$specification = [System.IO.File]::ReadAllText($BondSpecPath, $script:StrictUtf8)
$typeIds = @(Read-BondsTypeIds -Specification $specification)
$unitFacts = Read-BondsUnitFacts -Specification $specification -TypeIds $typeIds
$audit = Read-StrictJson -Path $AnimationAuditPath
if (($audit.schemaVersion -ne 'bonds-unit-animation-audit-v1') -or
    ([int]$audit.typeIdCount -ne 99) -or
    ([int]$audit.variantCount -ne 182)) {
    throw 'Animation audit does not match the current 99-TypeId/182-variant BONDS scope.'
}
$auditByUnitKey = @{}
foreach ($variant in @($audit.variants)) {
    if ($auditByUnitKey.ContainsKey($variant.unitKey)) {
        throw "Animation audit contains duplicate unitKey=$($variant.unitKey)"
    }
    $auditByUnitKey[$variant.unitKey] = $variant
}

$projectDirectories = @(Get-ChildItem -LiteralPath $CharacterRoot -Directory)
$projectVariants = @(
    foreach ($directory in $projectDirectories) {
        if ($directory.Name -notmatch '^(?<typeId>\d+)_') {
            continue
        }
        $typeId = [int]$Matches['typeId']
        if ($typeId -in $typeIds) {
            [pscustomobject]@{
                TypeId = $typeId
                UnitKey = $directory.Name
                EliteLevel = Get-EliteLevel -UnitKey $directory.Name
            }
        }
    }
)
if ($projectVariants.Count -ne 182) {
    throw "Project resource scope must contain 182 BONDS variants, actual=$($projectVariants.Count)"
}

$documents = [System.Collections.Generic.List[object]]::new()
$documentFileNames = [System.Collections.Generic.List[string]]::new()
$generatedVariantCount = 0
foreach ($typeId in $typeIds) {
    if ($typeId -in @(5503, 5504)) {
        continue
    }

    $variants = @(
        $projectVariants |
            Where-Object { $_.TypeId -eq $typeId } |
            Sort-Object EliteLevel, UnitKey
    )
    if ($variants.Count -eq 0 -or @($variants | Where-Object EliteLevel -eq 0).Count -ne 1) {
        throw "TypeId=$typeId must have exactly one elite-zero project variant."
    }

    $attackMethod = if ($typeId -in $script:NoAttackTypeIds) { 0 } else { 1 }
    $actionMethod = if ($typeId -in $script:ActionMethod2TypeIds) {
        2
    } elseif ($typeId -in $script:ActionMethod3TypeIds) {
        3
    } elseif ($typeId -in $script:ActionMethod4TypeIds) {
        4
    } else {
        1
    }
    $canBlock = $typeId -notin $script:UnblockableTypeIds
    $variantDocuments = [System.Collections.Generic.List[object]]::new()
    $damageTypes = [System.Collections.Generic.List[string]]::new()

    foreach ($variant in $variants) {
        if (-not $auditByUnitKey.ContainsKey($variant.UnitKey)) {
            throw "Animation audit is missing project unitKey=$($variant.UnitKey)"
        }
        $sourceUnitKey = Get-SourceUnitKey -ProjectUnitKey $variant.UnitKey
        $sourceDirectory = Get-SourceDirectory `
            -SourceUnitKey $sourceUnitKey `
            -PrimaryRoot $DeltaStagingRoot `
            -FallbackRoot $MetadataStagingRoot
        $source = Read-StrictJson -Path (Join-Path $sourceDirectory 'unit-source-v1.json')
        $levels = Read-StrictJson -Path (Join-Path $sourceDirectory 'unit-levels.json')
        if ([int]$source.typeId -ne $typeId) {
            throw "Source TypeId mismatch: unitKey=$($variant.UnitKey) source=$($source.typeId)"
        }
        $levelZero = @($levels.levels | Where-Object { [int]$_.level -eq 0 })
        if ($levelZero.Count -ne 1) {
            throw "Source level zero must be unique: unitKey=$($variant.UnitKey)"
        }
        $stats = $levelZero[0]
        Assert-SourceLevelZeroStats -Stats $stats -UnitKey $variant.UnitKey

        $damageType = [string]$source.damageType
        if ($typeId -eq 1238 -and [string]::IsNullOrWhiteSpace($damageType)) {
            $damageType = 'Physical'
        }
        if ($attackMethod -eq 0) {
            $damageType = 'None'
        }
        if ($damageType -notin @('Physical', 'Magic', 'None')) {
            throw "Damage type is missing or invalid: unitKey=$($variant.UnitKey) value=$damageType"
        }
        $damageTypes.Add($damageType)

        $resourceKey = $variant.UnitKey.Substring($variant.UnitKey.IndexOf('_') + 1)
        $plan = Get-AnimationPlan `
            -TypeId $typeId `
            -UnitKey $variant.UnitKey `
            -AttackMethod $attackMethod
        $animations = @(
            Convert-AnimationPlan `
                -Plan $plan `
                -AuditVariant $auditByUnitKey[$variant.UnitKey] `
                -UnitKey $variant.UnitKey
        )

        $attack = if ($attackMethod -eq 0) { 0 } else { [int]$stats.attack }
        $attackInterval = if ($attackMethod -eq 0) { 0 } else { [double]$stats.attackIntervalSeconds }
        $moveSpeed = if ($actionMethod -eq 4) { 0 } else { [double]$stats.moveSpeedMetresPerSecond }
        $displayName = if ($variant.EliteLevel -eq 0) {
            [string]$unitFacts[$typeId].DisplayNameZhHans
        } else {
            [string]$source.displayNameZhHans
        }
        if ([string]::IsNullOrWhiteSpace($displayName)) {
            throw "Display name is missing: unitKey=$($variant.UnitKey)"
        }

        $variantDocuments.Add([ordered]@{
            minEliteLevel = [int]$variant.EliteLevel
            sourceVariant = $variant.UnitKey
            statsLevel = 0
            displayNameZhHans = $displayName
            skillDescriptionZhHans = ''
            stats = [ordered]@{
                combat = [ordered]@{
                    maxHitPoints = [int]$stats.maxHitPoints
                    attack = $attack
                    defense = [int]$stats.defense
                    magicResistance = [int]$stats.magicResistance
                }
                shared = [ordered]@{
                    moveSpeedMetresPerSecond = $moveSpeed
                    attackIntervalSeconds = $attackInterval
                    lifeDeduct = [int]$stats.lifeDeduct
                }
            }
            model = [ordered]@{
                resourceKey = $resourceKey
                skeletonDataResourceName = "enemy_$($variant.UnitKey)_SkeletonData"
                profilePictureResourceName = "UIImage_$($variant.UnitKey)"
                animations = @($animations)
            }
            innateAbilityIds = @()
        })
    }

    $uniqueDamageTypes = @($damageTypes | Sort-Object -Unique)
    if ($uniqueDamageTypes.Count -ne 1) {
        throw "Damage type varies across model variants: TypeId=$typeId values=$($uniqueDamageTypes -join ',')"
    }
    $baseVariant = @($variants | Where-Object EliteLevel -eq 0)[0]
    $document = [ordered]@{
        schemaVersion = 'unit-elite-variants-v2'
        typeId = $typeId
        common = [ordered]@{
            rarity = [int]$unitFacts[$typeId].Rarity
            deploymentCost =
                [int]$unitFacts[$typeId].DeploymentCost
            attackMethod = $attackMethod
            actionMethod = $actionMethod
            attackRadiusMetres = 0
            blockRadiusMetres = 0
            canBlock = $canBlock
            blockCapacity = if ($canBlock) { 1 } else { 0 }
            tauntLevel = 0
            damageType = $uniqueDamageTypes[0]
        }
        variants = @($variantDocuments)
    }
    $fileName = "$($baseVariant.UnitKey).json"
    $documents.Add([pscustomobject]@{ FileName = $fileName; Document = $document })
    $documentFileNames.Add($fileName)
    $generatedVariantCount += $variantDocuments.Count
}

if ($documents.Count -ne 97 -or $generatedVariantCount -ne 180) {
    throw "Generated source scope mismatch: documents=$($documents.Count) variants=$generatedVariantCount"
}

$allowedExistingNames = @($documentFileNames) + $script:PreservedDocumentNames
$unexpectedExisting = @(
    Get-ChildItem -LiteralPath $OutputDirectory -File -Filter '*.json' |
        Where-Object { $_.Name -notin $allowedExistingNames }
)
if ($unexpectedExisting.Count -ne 0) {
    throw "Output directory contains unexpected JSON: $($unexpectedExisting.Name -join ',')"
}

foreach ($item in $documents | Sort-Object FileName) {
    $json = ($item.Document | ConvertTo-Json -Depth 20).Replace("`r`n", "`n") + "`n"
    $destination = Join-Path $OutputDirectory $item.FileName
    [System.IO.File]::WriteAllText($destination, $json, $script:StrictUtf8)
    if ([System.IO.File]::ReadAllText($destination, $script:StrictUtf8) -cne $json) {
        throw "Generated JSON verification failed: $destination"
    }
}

Write-Output (
    "BONDS_UNIT_ELITE_VARIANTS_V2_EXPORTED documents=$($documents.Count) variants=$generatedVariantCount " +
    "output=$([System.IO.Path]::GetFullPath($OutputDirectory))")
