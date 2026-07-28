[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$BondSpecPath,
    [Parameter(Mandatory = $true)][string]$StagingRoot,
    [string]$AbilityInputPath,
    [Parameter(Mandatory = $true)][string]$OutputCsvPath,
    [Parameter(Mandatory = $true)][string]$AnalysisOutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Condition {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Resolve-ExistingPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Required input path '$Path' does not exist."
    }
    return (Resolve-Path -LiteralPath $Path).Path
}

function Get-TypeIdsFromCodeBlock {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string[]]$Lines,
        [Parameter(Mandatory = $true)][string]$Header,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $headerIndexes = @(
        for ($index = 0; $index -lt $Lines.Count; $index++) {
            if ($Lines[$index] -ceq $Header) {
                $index
            }
        }
    )
    Assert-Condition ($headerIndexes.Count -eq 1) "Expected exactly one $Label header '$Header'; found $($headerIndexes.Count)."

    $index = $headerIndexes[0] + 1
    while ($index -lt $Lines.Count -and [string]::IsNullOrWhiteSpace($Lines[$index])) {
        $index++
    }
    Assert-Condition ($index -lt $Lines.Count -and $Lines[$index] -ceq '```text') "$Label header '$Header' is missing its text code block."

    $index++
    $content = [System.Collections.Generic.List[string]]::new()
    while ($index -lt $Lines.Count -and $Lines[$index] -cne '```') {
        $content.Add($Lines[$index])
        $index++
    }
    Assert-Condition ($index -lt $Lines.Count) "$Label text code block is not closed."

    $typeIds = [System.Collections.Generic.List[string]]::new()
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($candidate in (($content -join ' ') -split ',')) {
        $typeId = $candidate.Trim()
        Assert-Condition (-not [string]::IsNullOrWhiteSpace($typeId)) "$Label code block contains an empty TypeId."
        Assert-Condition ($typeId -match '^\d+$') "$Label code block contains invalid TypeId '$typeId'."
        Assert-Condition ($seen.Add($typeId)) "$Label code block contains duplicate TypeId '$typeId'."
        $typeIds.Add($typeId)
    }
    return $typeIds.ToArray()
}

function Get-Number {
    param(
        [Parameter(Mandatory = $true)]$Value,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][bool]$RequireInteger,
        [Parameter(Mandatory = $true)][decimal]$Minimum
    )

    Assert-Condition ($null -ne $Value) "$Name is missing."
    try {
        $number = [decimal]$Value
    }
    catch {
        throw "$Name '$Value' is not numeric."
    }
    Assert-Condition ($number -ge $Minimum) "$Name '$Value' is below the minimum '$Minimum'."
    if ($RequireInteger) {
        Assert-Condition ($number -eq [decimal][int]$number) "$Name '$Value' must be an integer."
        return [int]$number
    }
    return $number
}

function Get-RequiredLevelZero {
    param(
        [Parameter(Mandatory = $true)]$LevelsDocument,
        [Parameter(Mandatory = $true)][string]$DirectoryName
    )

    Assert-Condition ($null -ne $LevelsDocument.levels) "Resource directory '$DirectoryName' is missing levels."
    $levelZeroRows = @($LevelsDocument.levels | Where-Object { $_.level -eq 0 })
    Assert-Condition ($levelZeroRows.Count -eq 1) "Resource directory '$DirectoryName' must contain exactly one level 0 row; found $($levelZeroRows.Count)."
    $levelZero = $levelZeroRows[0]

    return [pscustomobject]@{
        MaxHitPoints = Get-Number -Value $levelZero.maxHitPoints -Name "$DirectoryName maxHitPoints" -RequireInteger $true -Minimum 0
        Attack = Get-Number -Value $levelZero.attack -Name "$DirectoryName attack" -RequireInteger $true -Minimum 0
        Defense = Get-Number -Value $levelZero.defense -Name "$DirectoryName defense" -RequireInteger $true -Minimum 0
        MagicResistance = Get-Number -Value $levelZero.magicResistance -Name "$DirectoryName magicResistance" -RequireInteger $true -Minimum 0
        AttackIntervalSeconds = Get-Number -Value $levelZero.attackIntervalSeconds -Name "$DirectoryName attackIntervalSeconds" -RequireInteger $false -Minimum 0
        MoveSpeedMetresPerSecond = Get-Number -Value $levelZero.moveSpeedMetresPerSecond -Name "$DirectoryName moveSpeedMetresPerSecond" -RequireInteger $false -Minimum 0
        LifeDeduct = Get-Number -Value $levelZero.lifeDeduct -Name "$DirectoryName lifeDeduct" -RequireInteger $true -Minimum 0
    }
}

function Get-OnlyDirectory {
    param(
        [Parameter(Mandatory = $true)]$Directories,
        [Parameter(Mandatory = $true)][string]$TypeId
    )

    $prefix = '^' + [regex]::Escape($TypeId) + '_.+$'
    $matches = @($Directories | Where-Object { $_.Name -match $prefix -and $_.Name -notmatch '_[23]$' })
    Assert-Condition ($matches.Count -eq 1) "Expected exactly one base resource directory for TypeId '$TypeId'; found $($matches.Count)."
    return $matches[0]
}

function Get-DirectoryByName {
    param(
        [Parameter(Mandatory = $true)]$Directories,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $matches = @($Directories | Where-Object { $_.Name -ceq $Name })
    Assert-Condition ($matches.Count -eq 1) "Expected exactly one resource directory '$Name'; found $($matches.Count)."
    return $matches[0]
}

function Get-NonNegativeCombatValue {
    param(
        [Parameter(Mandatory = $true)][decimal]$Value,
        [Parameter(Mandatory = $true)][string]$Name
    )

    Assert-Condition ($Value -ge 0) "$Name '$Value' must not be negative."
    return $Value
}

function Get-PhysicalDamage {
    param(
        [Parameter(Mandatory = $true)][decimal]$Attack,
        [Parameter(Mandatory = $true)][decimal]$Defense
    )

    $Attack = Get-NonNegativeCombatValue -Value $Attack -Name 'Attack'
    $Defense = Get-NonNegativeCombatValue -Value $Defense -Name 'Defense'
    return [decimal][Math]::Max(
        [double]($Attack - $Defense),
        [Math]::Floor([double]($Attack * [decimal]0.05))
    )
}

function Get-MagicDamage {
    param(
        [Parameter(Mandatory = $true)][decimal]$Attack,
        [Parameter(Mandatory = $true)][decimal]$MagicResistance
    )

    $Attack = Get-NonNegativeCombatValue -Value $Attack -Name 'Attack'
    $MagicResistance = Get-NonNegativeCombatValue -Value $MagicResistance -Name 'MagicResistance'
    return [decimal][Math]::Max(
        [Math]::Floor([double]($Attack * ([decimal]100 - $MagicResistance) / [decimal]100)),
        [Math]::Floor([double]($Attack * [decimal]0.05))
    )
}

function Get-TrueDamage {
    param([Parameter(Mandatory = $true)][decimal]$Attack)

    return Get-NonNegativeCombatValue -Value $Attack -Name 'Attack'
}

function Get-OrdinaryAttackDamage {
    param(
        [Parameter(Mandatory = $true)]$Attacker,
        [Parameter(Mandatory = $true)]$Defender
    )

    Assert-Condition ($null -ne $Attacker.Attack) "Type ID $($Attacker.TypeId) is missing Attack."
    Assert-Condition ([decimal]$Attacker.Attack -gt 0) "Type ID $($Attacker.TypeId) must have positive Attack for an ordinary attack."
    switch ([string]$Attacker.DamageType) {
        'Physical' { return Get-PhysicalDamage -Attack $Attacker.Attack -Defense $Defender.Defense }
        'Magic' { return Get-MagicDamage -Attack $Attacker.Attack -MagicResistance $Defender.MagicResistance }
        'True' { return Get-TrueDamage -Attack $Attacker.Attack }
        default { throw "Cannot calculate ordinary attack damage for type ID $($Attacker.TypeId) with damage type '$($Attacker.DamageType)'." }
    }
}

function Get-EffectiveAttackInterval {
    param([Parameter(Mandatory = $true)]$Attacker)

    Assert-Condition ($null -ne $Attacker.EffectiveAttackIntervalSeconds) "Type ID $($Attacker.TypeId) is missing EffectiveAttackIntervalSeconds."
    $interval = [decimal]$Attacker.EffectiveAttackIntervalSeconds
    Assert-Condition ($interval -gt 0) "Type ID $($Attacker.TypeId) has nonpositive effective attack interval '$interval'."
    return $interval
}

function Get-Percentile {
    param(
        [Parameter(Mandatory = $true)][decimal[]]$Values,
        [Parameter(Mandatory = $true)][decimal]$Percentile
    )

    Assert-Condition ($Values.Count -gt 0) 'Cannot calculate a percentile from an empty sample.'
    Assert-Condition ($Percentile -ge 0 -and $Percentile -le 1) "Invalid percentile '$Percentile'."
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

function Get-WinsorizedValues {
    param(
        [Parameter(Mandatory = $true)][decimal[]]$Values,
        [Parameter(Mandatory = $true)][decimal]$LowerPercentile,
        [Parameter(Mandatory = $true)][decimal]$UpperPercentile
    )

    Assert-Condition ($LowerPercentile -ge 0 -and $LowerPercentile -le $UpperPercentile -and $UpperPercentile -le 1) "Invalid winsorization range $LowerPercentile/$UpperPercentile."
    $lowerBound = Get-Percentile -Values $Values -Percentile $LowerPercentile
    $upperBound = Get-Percentile -Values $Values -Percentile $UpperPercentile
    return Get-ClampedCombatValues -Values $Values -LowerBound $lowerBound -UpperBound $upperBound
}

function Get-ClampedCombatValues {
    param(
        [Parameter(Mandatory = $true)][decimal[]]$Values,
        [Parameter(Mandatory = $true)][decimal]$LowerBound,
        [Parameter(Mandatory = $true)][decimal]$UpperBound
    )

    Assert-Condition ($Values.Count -gt 0) 'Cannot clamp an empty sample.'
    Assert-Condition ($LowerBound -le $UpperBound) "Invalid clamp bounds $LowerBound/$UpperBound."
    return [decimal[]]@($Values | ForEach-Object { [decimal][Math]::Min([double]$UpperBound, [Math]::Max([double]$LowerBound, [double]$_)) })
}

function Get-WinsorizedUnitAxis {
    param([Parameter(Mandatory = $true)][decimal[]]$RawMedians)

    $p5 = Get-Percentile -Values $RawMedians -Percentile ([decimal]0.05)
    $p95 = Get-Percentile -Values $RawMedians -Percentile ([decimal]0.95)
    return [pscustomobject]@{
        P5 = $p5
        P95 = $p95
        WinsorizedValues = Get-ClampedCombatValues -Values $RawMedians -LowerBound $p5 -UpperBound $p95
    }
}

function Get-GeometricCombinedValue {
    param(
        [Parameter(Mandatory = $true)][decimal]$Output,
        [Parameter(Mandatory = $true)][decimal]$Defense
    )

    Assert-Condition ($Output -gt 0) "Output '$Output' must be positive."
    Assert-Condition ($Defense -gt 0) "Defense '$Defense' must be positive."
    return [decimal][Math]::Sqrt([double]($Output * $Defense))
}

function Write-Utf8File {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string[]]$Lines
    )

    [System.IO.File]::WriteAllLines($Path, $Lines, [System.Text.UTF8Encoding]::new($false))
}

try {
    $BondSpecPath = Resolve-ExistingPath $BondSpecPath
    $StagingRoot = Resolve-ExistingPath $StagingRoot
    if (-not [string]::IsNullOrWhiteSpace($AbilityInputPath)) {
        $AbilityInputPath = Resolve-ExistingPath $AbilityInputPath
        Assert-Condition ([System.IO.Path]::GetExtension($AbilityInputPath) -ieq '.psd1') "AbilityInputPath '$AbilityInputPath' must use the .psd1 extension."
    }
    $OutputCsvPath = [System.IO.Path]::GetFullPath($OutputCsvPath)
    $AnalysisOutputPath = [System.IO.Path]::GetFullPath($AnalysisOutputPath)
    Assert-Condition ($OutputCsvPath -cne $AnalysisOutputPath) 'OutputCsvPath and AnalysisOutputPath must be different files.'
    Assert-Condition (-not [string]::Equals($OutputCsvPath, $BondSpecPath, [System.StringComparison]::OrdinalIgnoreCase)) 'OutputCsvPath must not overwrite BondSpecPath.'
    Assert-Condition (-not [string]::Equals($AnalysisOutputPath, $BondSpecPath, [System.StringComparison]::OrdinalIgnoreCase)) 'AnalysisOutputPath must not overwrite BondSpecPath.'

    $shopHeader = [string]::Concat('## ', [char]0x5546, [char]0x5E97, [char]0x5355, [char]0x4F4D, [char]0xFF08, '88', [char]0xFF09)
    $nonShopHeader = [string]::Concat('## ', [char]0x975E, [char]0x5546, [char]0x5E97, [char]0x5355, [char]0x4F4D, [char]0xFF08, '5', [char]0xFF09)
    $rarityLabel = [string]::Concat([char]0x7A00, [char]0x6709)
    $specLines = @(Get-Content -LiteralPath $BondSpecPath -Encoding UTF8)
    $shopTypeIds = @(Get-TypeIdsFromCodeBlock -Lines $specLines -Header $shopHeader -Label 'shop')
    $nonShopTypeIds = @(Get-TypeIdsFromCodeBlock -Lines $specLines -Header $nonShopHeader -Label 'non-shop')
    Assert-Condition ($shopTypeIds.Count -eq 88) "Expected 88 shop TypeIds; found $($shopTypeIds.Count)."
    Assert-Condition (($nonShopTypeIds -join '/') -ceq '1137/1138/2033/5504/10002') "Non-shop TypeIds must be 1137/1138/2033/5504/10002; found '$($nonShopTypeIds -join '/')'."

    $shopSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($typeId in $shopTypeIds) {
        [void]$shopSet.Add($typeId)
    }
    $nonShopSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($typeId in $nonShopTypeIds) {
        Assert-Condition ($nonShopSet.Add($typeId)) "Duplicate non-shop TypeId '$typeId'."
        Assert-Condition (-not $shopSet.Contains($typeId)) "TypeId '$typeId' appears in both shop and non-shop lists."
    }

    $unitPattern = '^(?<TypeId>\d+)\s+(?<DisplayName>.+?)\s+' + $rarityLabel + '(?<Rarity>[1-6])(?:\s|$)'
    $definitions = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
    foreach ($line in $specLines) {
        if ($line -notmatch $unitPattern) {
            continue
        }

        $typeId = $Matches.TypeId
        if (-not $shopSet.Contains($typeId)) {
            continue
        }
        $definition = [pscustomobject]@{
            DisplayName = $Matches.DisplayName.Trim()
            Rarity = [int]$Matches.Rarity
        }
        if ($definitions.ContainsKey($typeId)) {
            $existing = $definitions[$typeId]
            Assert-Condition ($existing.DisplayName -ceq $definition.DisplayName -and $existing.Rarity -eq $definition.Rarity) "TypeId '$typeId' has conflicting display-name or rarity definitions."
        }
        else {
            $definitions.Add($typeId, $definition)
        }
    }
    foreach ($typeId in $shopTypeIds) {
        Assert-Condition ($definitions.ContainsKey($typeId)) "Shop TypeId '$typeId' has no unit definition."
    }

    $expectedRarityCounts = @{ 1 = 9; 2 = 18; 3 = 12; 4 = 23; 5 = 20; 6 = 6 }
    foreach ($rarity in 1..6) {
        $count = @($definitions.Values | Where-Object { $_.Rarity -eq $rarity }).Count
        Assert-Condition ($count -eq $expectedRarityCounts[$rarity]) "Expected R$rarity=$($expectedRarityCounts[$rarity]); found $count."
    }

    $directories = @(Get-ChildItem -LiteralPath $StagingRoot -Directory)
    $damageTypeOverrides = @{ '1238' = 'Physical'; '1243' = 'Physical'; '10039' = 'Physical' }
    $allowedDamageTypes = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($damageType in @('Physical', 'Magic', 'True', 'None')) {
        [void]$allowedDamageTypes.Add($damageType)
    }
    $eliteEvidence = [System.Collections.Generic.List[object]]::new()
    $rows = foreach ($typeId in ($shopTypeIds | Sort-Object { [int]$_ })) {
        $resourceDirectory = if ($typeId -ceq '1322') {
            Get-DirectoryByName -Directories $directories -Name '1322_wdgyht_2'
        }
        else {
            Get-OnlyDirectory -Directories $directories -TypeId $typeId
        }
        if ($typeId -ceq '1322') {
            $eliteDirectory = Get-DirectoryByName -Directories $directories -Name '1322_wdgyht'
            $eliteEvidence.Add([pscustomobject][ordered]@{ TypeId = 1322; EliteLevel = 2; ResourceDirectory = $eliteDirectory.Name })
        }

        $levelsPath = Join-Path $resourceDirectory.FullName 'unit-levels.json'
        $sourcePath = Join-Path $resourceDirectory.FullName 'unit-source-v1.json'
        Assert-Condition (Test-Path -LiteralPath $levelsPath -PathType Leaf) "Resource directory '$($resourceDirectory.Name)' is missing unit-levels.json."
        Assert-Condition (Test-Path -LiteralPath $sourcePath -PathType Leaf) "Resource directory '$($resourceDirectory.Name)' is missing unit-source-v1.json."
        try {
            $levelsDocument = Get-Content -LiteralPath $levelsPath -Raw -Encoding UTF8 | ConvertFrom-Json
            $sourceDocument = Get-Content -LiteralPath $sourcePath -Raw -Encoding UTF8 | ConvertFrom-Json
        }
        catch {
            throw "Resource directory '$($resourceDirectory.Name)' contains invalid JSON: $($_.Exception.Message)"
        }
        Assert-Condition ([string]$sourceDocument.typeId -ceq $typeId) "Resource directory '$($resourceDirectory.Name)' source typeId '$($sourceDocument.typeId)' does not match '$typeId'."
        $levelZero = Get-RequiredLevelZero -LevelsDocument $levelsDocument -DirectoryName $resourceDirectory.Name

        $stagingDamageType = [string]$sourceDocument.damageType
        if ([string]::IsNullOrWhiteSpace($stagingDamageType)) {
            Assert-Condition ($damageTypeOverrides.ContainsKey($typeId)) "TypeId '$typeId' is missing a damageType."
            $damageType = $damageTypeOverrides[$typeId]
            $damageTypeSource = 'ConfirmedOverride'
        }
        else {
            Assert-Condition ($allowedDamageTypes.Contains($stagingDamageType)) "TypeId '$typeId' has invalid damageType '$stagingDamageType'."
            if ($damageTypeOverrides.ContainsKey($typeId)) {
                Assert-Condition ($stagingDamageType -ceq $damageTypeOverrides[$typeId]) "TypeId '$typeId' has conflicting damageType '$stagingDamageType'; confirmed damageType is '$($damageTypeOverrides[$typeId])'."
            }
            $damageType = $stagingDamageType
            $damageTypeSource = 'Staging'
        }

        [pscustomobject][ordered]@{
            TypeId = [int]$typeId
            DisplayName = $definitions[$typeId].DisplayName
            Rarity = $definitions[$typeId].Rarity
            ResourceDirectory = $resourceDirectory.Name
            DamageType = $damageType
            DamageTypeSource = $damageTypeSource
            MaxHitPoints = $levelZero.MaxHitPoints
            Attack = $levelZero.Attack
            Defense = $levelZero.Defense
            MagicResistance = $levelZero.MagicResistance
            AttackIntervalSeconds = $levelZero.AttackIntervalSeconds
            EffectiveAttackIntervalSeconds = $levelZero.AttackIntervalSeconds * [decimal]0.5
            MoveSpeedMetresPerSecond = $levelZero.MoveSpeedMetresPerSecond
            LifeDeduct = $levelZero.LifeDeduct
        }
    }

    $rows = @($rows)
    Assert-Condition ($rows.Count -eq 88) "Expected 88 exported rows; found $($rows.Count)."
    foreach ($row in $rows) {
        Assert-Condition ($row.TypeId -notin @(1137, 1138, 2033, 5504, 10002)) "Non-shop TypeId '$($row.TypeId)' was exported."
    }

    # BONDS_SPEC.md confirms these drones cannot be attacked.  It also confirms
    # that the path units and traffic police do not make ordinary attacks.
    $unattackableDroneTypeIds = [System.Collections.Generic.HashSet[int]]::new()
    foreach ($typeId in @(1017, 1042, 1146, 1355)) {
        [void]$unattackableDroneTypeIds.Add($typeId)
    }
    $ordinaryAttackExcludedTypeIds = [System.Collections.Generic.HashSet[int]]::new()
    foreach ($typeId in @(1008, 1026, 1333)) {
        [void]$ordinaryAttackExcludedTypeIds.Add($typeId)
    }
    $pathUnitTypeIds = [System.Collections.Generic.HashSet[int]]::new()
    foreach ($typeId in @(1008, 1026)) {
        [void]$pathUnitTypeIds.Add($typeId)
    }

    $ordinaryCombatRows = @($rows | Where-Object {
            $_.DamageType -ne 'None' -and
            -not $unattackableDroneTypeIds.Contains([int]$_.TypeId) -and
            -not $ordinaryAttackExcludedTypeIds.Contains([int]$_.TypeId)
        })
    $defenderRows = $ordinaryCombatRows
    $attackingRows = $ordinaryCombatRows
    Assert-Condition ($defenderRows.Count -gt 0) 'The defender sample is empty.'
    Assert-Condition ($attackingRows.Count -gt 0) 'The ordinary attacker sample is empty.'
    foreach ($attacker in $attackingRows) {
        [void](Get-EffectiveAttackInterval -Attacker $attacker)
    }

    $dpsValuesByAttacker = @{}
    $ttdValuesByDefender = @{}
    foreach ($attacker in $attackingRows) {
        $dpsValues = [System.Collections.Generic.List[decimal]]::new()
        $effectiveInterval = Get-EffectiveAttackInterval -Attacker $attacker
        foreach ($defender in $defenderRows) {
            $damagePerHit = Get-OrdinaryAttackDamage -Attacker $attacker -Defender $defender
            Assert-Condition ($damagePerHit -gt 0) "Type ID $($attacker.TypeId) produced nonpositive damage '$damagePerHit'."
            $dps = $damagePerHit / $effectiveInterval
            [void]$dpsValues.Add($dps)
        }
        $dpsValuesByAttacker[[int]$attacker.TypeId] = $dpsValues.ToArray()
    }
    foreach ($defender in $defenderRows) {
        $ttdValues = [System.Collections.Generic.List[decimal]]::new()
        foreach ($attacker in $attackingRows) {
            $damagePerHit = Get-OrdinaryAttackDamage -Attacker $attacker -Defender $defender
            $effectiveInterval = Get-EffectiveAttackInterval -Attacker $attacker
            $ttd = [decimal][Math]::Ceiling([double]([decimal]$defender.MaxHitPoints / $damagePerHit)) * $effectiveInterval
            Assert-Condition ($ttd -ge 0) "Type ID $($defender.TypeId) produced negative TTD '$ttd'."
            [void]$ttdValues.Add($ttd)
        }
        $ttdValuesByDefender[[int]$defender.TypeId] = $ttdValues.ToArray()
    }

    $rawDpsMediansByTypeId = @{}
    foreach ($typeId in $dpsValuesByAttacker.Keys) {
        $rawDpsMediansByTypeId[$typeId] = Get-Median -Values ([decimal[]]$dpsValuesByAttacker[$typeId])
    }
    $rawTtdMediansByTypeId = @{}
    foreach ($typeId in $ttdValuesByDefender.Keys) {
        $rawTtdMediansByTypeId[$typeId] = Get-Median -Values ([decimal[]]$ttdValuesByDefender[$typeId])
    }
    $dpsUnitAxis = Get-WinsorizedUnitAxis -RawMedians ([decimal[]]@($rawDpsMediansByTypeId.Values))
    $ttdUnitAxis = Get-WinsorizedUnitAxis -RawMedians ([decimal[]]@($rawTtdMediansByTypeId.Values))
    $dpsP5 = $dpsUnitAxis.P5
    $dpsP95 = $dpsUnitAxis.P95
    $ttdP5 = $ttdUnitAxis.P5
    $ttdP95 = $ttdUnitAxis.P95
    $outputMetricsByTypeId = @{}
    foreach ($typeId in $rawDpsMediansByTypeId.Keys) {
        $rawMedian = [decimal]$rawDpsMediansByTypeId[$typeId]
        $outputMetricsByTypeId[$typeId] = [pscustomobject]@{
            RawMedian = $rawMedian
            WinsorizedMedian = (Get-ClampedCombatValues -Values ([decimal[]]@($rawMedian)) -LowerBound $dpsP5 -UpperBound $dpsP95)[0]
        }
    }
    $defenseMetricsByTypeId = @{}
    foreach ($typeId in $rawTtdMediansByTypeId.Keys) {
        $rawMedian = [decimal]$rawTtdMediansByTypeId[$typeId]
        $defenseMetricsByTypeId[$typeId] = [pscustomobject]@{
            RawMedian = $rawMedian
            WinsorizedMedian = (Get-ClampedCombatValues -Values ([decimal[]]@($rawMedian)) -LowerBound $ttdP5 -UpperBound $ttdP95)[0]
        }
    }
    $scorableTypeIds = @($rows | Where-Object {
            $outputMetricsByTypeId.ContainsKey([int]$_.TypeId) -and
            $defenseMetricsByTypeId.ContainsKey([int]$_.TypeId) -and
            -not $unattackableDroneTypeIds.Contains([int]$_.TypeId)
        } | ForEach-Object { [int]$_.TypeId })
    Assert-Condition ($scorableTypeIds.Count -gt 0) 'The scorable ordinary-combat population is empty.'
    $outputReference = Get-Median -Values ([decimal[]]@($scorableTypeIds | ForEach-Object { $outputMetricsByTypeId[$_].WinsorizedMedian }))
    $defenseReference = Get-Median -Values ([decimal[]]@($scorableTypeIds | ForEach-Object { $defenseMetricsByTypeId[$_].WinsorizedMedian }))
    Assert-Condition ($outputReference -gt 0) "Output reference '$outputReference' must be positive."
    Assert-Condition ($defenseReference -gt 0) "Defense reference '$defenseReference' must be positive."

    $rows = foreach ($row in $rows) {
        $typeId = [int]$row.TypeId
        $outputMetrics = if ($outputMetricsByTypeId.ContainsKey($typeId)) { $outputMetricsByTypeId[$typeId] } else { $null }
        $defenseMetrics = if ($defenseMetricsByTypeId.ContainsKey($typeId)) { $defenseMetricsByTypeId[$typeId] } else { $null }
        $isScorable = $scorableTypeIds -contains $typeId
        $status = if ($unattackableDroneTypeIds.Contains($typeId)) { 'UnattackableDrone' } elseif ($pathUnitTypeIds.Contains($typeId)) { 'PathUnit' } elseif ($ordinaryAttackExcludedTypeIds.Contains($typeId) -or $row.DamageType -eq 'None') { 'NoOrdinaryAttack' } else { 'BaseOrdinaryCombat' }
        [pscustomobject][ordered]@{
            TypeId = $row.TypeId
            DisplayName = $row.DisplayName
            Rarity = $row.Rarity
            ResourceDirectory = $row.ResourceDirectory
            DamageType = $row.DamageType
            DamageTypeSource = $row.DamageTypeSource
            MaxHitPoints = $row.MaxHitPoints
            Attack = $row.Attack
            Defense = $row.Defense
            MagicResistance = $row.MagicResistance
            AttackIntervalSeconds = $row.AttackIntervalSeconds
            EffectiveAttackIntervalSeconds = $row.EffectiveAttackIntervalSeconds
            MoveSpeedMetresPerSecond = $row.MoveSpeedMetresPerSecond
            LifeDeduct = $row.LifeDeduct
            RawMedianDps = if ($null -ne $outputMetrics) { $outputMetrics.RawMedian } else { $null }
            WinsorizedMedianDps = if ($null -ne $outputMetrics) { $outputMetrics.WinsorizedMedian } else { $null }
            RawMedianTtdSeconds = if ($null -ne $defenseMetrics) { $defenseMetrics.RawMedian } else { $null }
            WinsorizedMedianTtdSeconds = if ($null -ne $defenseMetrics) { $defenseMetrics.WinsorizedMedian } else { $null }
            OutputReference = if ($isScorable) { $outputReference } else { $null }
            DefenseReference = if ($isScorable) { $defenseReference } else { $null }
            PanelPower = if ($isScorable) { Get-GeometricCombinedValue -Output ($outputMetrics.WinsorizedMedian / $outputReference) -Defense ($defenseMetrics.WinsorizedMedian / $defenseReference) } else { $null }
            PanelModelStatus = $status
        }
    }
    foreach ($row in $rows) {
        foreach ($property in @('RawMedianDps', 'WinsorizedMedianDps', 'RawMedianTtdSeconds', 'WinsorizedMedianTtdSeconds', 'OutputReference', 'DefenseReference', 'PanelPower')) {
            if ($null -ne $row.$property) {
                Assert-Condition (-not [double]::IsNaN([double]$row.$property) -and -not [double]::IsInfinity([double]$row.$property)) "Type ID $($row.TypeId) has invalid $property=$($row.$property)."
            }
        }
        foreach ($property in @('RawMedianTtdSeconds', 'WinsorizedMedianTtdSeconds')) {
            if ($null -ne $row.$property) {
                Assert-Condition ($row.$property -ge 0) "Type ID $($row.TypeId) has negative $property=$($row.$property)."
            }
        }
        if ($row.PanelModelStatus -eq 'BaseOrdinaryCombat') {
            Assert-Condition ($null -ne $row.PanelPower -and $row.PanelPower -gt 0) "Type ID $($row.TypeId) must have a positive PanelPower."
        }
        else {
            foreach ($property in @('RawMedianDps', 'WinsorizedMedianDps', 'RawMedianTtdSeconds', 'WinsorizedMedianTtdSeconds', 'OutputReference', 'DefenseReference', 'PanelPower')) {
                Assert-Condition ($null -eq $row.$property) "Type ID $($row.TypeId) must leave $property empty before an ability scenario is modeled."
            }
        }
    }

    $analysis = [ordered]@{
        SchemaVersion = 'unit-cost-analysis-v2'
        ShopRowCount = $rows.Count
        RarityDistribution = [ordered]@{ R1 = 9; R2 = 18; R3 = 12; R4 = 23; R5 = 20; R6 = 6 }
        EliteEvidence = @($eliteEvidence)
        CombatModel = [ordered]@{
            DefenderCount = $defenderRows.Count
            OrdinaryAttackerCount = $attackingRows.Count
            ScorableOrdinaryCombatCount = $scorableTypeIds.Count
            DpsWinsorization = [ordered]@{ P5 = $dpsP5; P95 = $dpsP95 }
            TtdWinsorization = [ordered]@{ P5 = $ttdP5; P95 = $ttdP95 }
            OutputReference = $outputReference
            DefenseReference = $defenseReference
        }
    }

    $outputDirectory = Split-Path -Parent $OutputCsvPath
    $analysisDirectory = Split-Path -Parent $AnalysisOutputPath
    [System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
    [System.IO.Directory]::CreateDirectory($analysisDirectory) | Out-Null
    $csvTemporaryPath = Join-Path $outputDirectory ('.' + [System.IO.Path]::GetRandomFileName())
    $analysisTemporaryPath = Join-Path $analysisDirectory ('.' + [System.IO.Path]::GetRandomFileName())
    try {
        Write-Utf8File -Path $csvTemporaryPath -Lines @($rows | ConvertTo-Csv -NoTypeInformation)
        Write-Utf8File -Path $analysisTemporaryPath -Lines @($analysis | ConvertTo-Json -Depth 5)
        Move-Item -LiteralPath $csvTemporaryPath -Destination $OutputCsvPath -Force
        Move-Item -LiteralPath $analysisTemporaryPath -Destination $AnalysisOutputPath -Force
    }
    finally {
        Remove-Item -LiteralPath $csvTemporaryPath, $analysisTemporaryPath -Force -ErrorAction SilentlyContinue
    }

    Write-Host "Exported $($rows.Count) shop rows to '$OutputCsvPath'."
    exit 0
}
catch {
    Write-Error $_
    exit 1
}
