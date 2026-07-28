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

function Test-PathEqualOrUnderRoot {
    param(
        [Parameter(Mandatory = $true)][string]$CandidatePath,
        [Parameter(Mandatory = $true)][string]$RootPath
    )

    $normalizedCandidate = [System.IO.Path]::GetFullPath($CandidatePath)
    $normalizedRoot = [System.IO.Path]::GetFullPath($RootPath)
    if ([string]::Equals($normalizedCandidate, $normalizedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }
    $trimCharacters = [char[]]@([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $rootWithSeparator = $normalizedRoot.TrimEnd($trimCharacters) + [System.IO.Path]::DirectorySeparatorChar
    return $normalizedCandidate.StartsWith($rootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)
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

function Get-ResourceCombatRecord {
    param(
        [Parameter(Mandatory = $true)][System.IO.DirectoryInfo]$ResourceDirectory,
        [Parameter(Mandatory = $true)][string]$TypeId,
        [Parameter(Mandatory = $true)][System.Collections.IDictionary]$DamageTypeOverrides,
        [Parameter(Mandatory = $true)][System.Collections.Generic.HashSet[string]]$AllowedDamageTypes
    )

    $levelsPath = Join-Path $ResourceDirectory.FullName 'unit-levels.json'
    $sourcePath = Join-Path $ResourceDirectory.FullName 'unit-source-v1.json'
    Assert-Condition (Test-Path -LiteralPath $levelsPath -PathType Leaf) "Resource directory '$($ResourceDirectory.Name)' is missing unit-levels.json."
    Assert-Condition (Test-Path -LiteralPath $sourcePath -PathType Leaf) "Resource directory '$($ResourceDirectory.Name)' is missing unit-source-v1.json."
    try {
        $levelsDocument = Get-Content -LiteralPath $levelsPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $sourceDocument = Get-Content -LiteralPath $sourcePath -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        throw "Resource directory '$($ResourceDirectory.Name)' contains invalid JSON: $($_.Exception.Message)"
    }
    Assert-Condition ([string]$sourceDocument.typeId -ceq $TypeId) "Resource directory '$($ResourceDirectory.Name)' source typeId '$($sourceDocument.typeId)' does not match '$TypeId'."
    $levelZero = Get-RequiredLevelZero -LevelsDocument $levelsDocument -DirectoryName $ResourceDirectory.Name

    $stagingDamageType = [string]$sourceDocument.damageType
    if ([string]::IsNullOrWhiteSpace($stagingDamageType)) {
        Assert-Condition ($DamageTypeOverrides.Contains($TypeId)) "TypeId '$TypeId' is missing a damageType."
        $damageType = [string]$DamageTypeOverrides[$TypeId]
        $damageTypeSource = 'ConfirmedOverride'
    }
    else {
        Assert-Condition ($AllowedDamageTypes.Contains($stagingDamageType)) "TypeId '$TypeId' has invalid damageType '$stagingDamageType'."
        if ($DamageTypeOverrides.Contains($TypeId)) {
            Assert-Condition ($stagingDamageType -ceq [string]$DamageTypeOverrides[$TypeId]) "TypeId '$TypeId' has conflicting damageType '$stagingDamageType'; confirmed damageType is '$($DamageTypeOverrides[$TypeId])'."
        }
        $damageType = $stagingDamageType
        $damageTypeSource = 'Staging'
    }

    return [pscustomobject][ordered]@{
        TypeId = [int]$TypeId
        ResourceDirectory = $ResourceDirectory.Name
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

function Get-IsotonicNondecreasingValues {
    param([Parameter(Mandatory = $true)][decimal[]]$Values)

    Assert-Condition ($Values.Count -gt 0) 'Cannot fit isotonic values from an empty sample.'
    $blocks = [System.Collections.Generic.List[object]]::new()
    for ($index = 0; $index -lt $Values.Count; $index++) {
        Assert-Condition ($Values[$index] -gt 0) "Isotonic input at index $index must be positive."
        $blocks.Add([pscustomobject]@{
                Start = $index
                End = $index
                Weight = [decimal]1
                Value = [decimal]$Values[$index]
            })
        while ($blocks.Count -ge 2 -and $blocks[$blocks.Count - 2].Value -gt $blocks[$blocks.Count - 1].Value) {
            $left = $blocks[$blocks.Count - 2]
            $right = $blocks[$blocks.Count - 1]
            $merged = [pscustomobject]@{
                Start = $left.Start
                End = $right.End
                Weight = [decimal]$left.Weight + [decimal]$right.Weight
                Value = (([decimal]$left.Value * [decimal]$left.Weight) + ([decimal]$right.Value * [decimal]$right.Weight)) /
                    ([decimal]$left.Weight + [decimal]$right.Weight)
            }
            $blocks.RemoveAt($blocks.Count - 1)
            $blocks.RemoveAt($blocks.Count - 1)
            $blocks.Add($merged)
        }
    }

    $result = [decimal[]]::new($Values.Count)
    foreach ($block in $blocks) {
        for ($index = $block.Start; $index -le $block.End; $index++) {
            $result[$index] = [decimal]$block.Value
        }
    }
    return $result
}

function Get-CompressionAlpha {
    param(
        [Parameter(Mandatory = $true)][decimal]$Rarity2Power,
        [Parameter(Mandatory = $true)][decimal]$Rarity6Power
    )

    Assert-Condition ($Rarity2Power -gt 0) "R2 isotonic median power '$Rarity2Power' must be positive."
    Assert-Condition ($Rarity6Power -gt $Rarity2Power) "R6 isotonic median power '$Rarity6Power' must exceed R2 '$Rarity2Power'."
    $powerRatio = [double]($Rarity6Power / $Rarity2Power)
    return [decimal][Math]::Min([double]1, [Math]::Log(4) / [Math]::Log($powerRatio))
}

function Get-RarityBaseCosts {
    param(
        [Parameter(Mandatory = $true)][decimal[]]$IsotonicRarityMedians,
        [Parameter(Mandatory = $true)][decimal]$CompressionAlpha,
        [Parameter(Mandatory = $true)][decimal]$Rarity6Anchor
    )

    Assert-Condition ($IsotonicRarityMedians.Count -eq 6) "Expected six isotonic rarity medians; found $($IsotonicRarityMedians.Count)."
    Assert-Condition ($CompressionAlpha -gt 0 -and $CompressionAlpha -le 1) "Compression alpha '$CompressionAlpha' must be in (0, 1]."
    Assert-Condition ($Rarity6Anchor -gt 0) "R6 anchor '$Rarity6Anchor' must be positive."
    for ($index = 0; $index -lt $IsotonicRarityMedians.Count; $index++) {
        Assert-Condition ($IsotonicRarityMedians[$index] -gt 0) "Isotonic rarity median at index $index must be positive."
        if ($index -gt 0) {
            Assert-Condition ($IsotonicRarityMedians[$index] -ge $IsotonicRarityMedians[$index - 1]) 'Isotonic rarity medians must be nondecreasing.'
        }
    }

    $baseCosts = [decimal[]]::new(6)
    for ($index = 0; $index -lt $baseCosts.Count; $index++) {
        $baseCosts[$index] = [decimal](
            [double]$Rarity6Anchor *
            [Math]::Pow(
                [double]($IsotonicRarityMedians[$index] / $IsotonicRarityMedians[5]),
                [double]$CompressionAlpha
            )
        )
    }
    return $baseCosts
}

function Get-WithinTierFactor {
    param(
        [Parameter(Mandatory = $true)][decimal]$Power,
        [Parameter(Mandatory = $true)][decimal]$IsotonicRarityMedianPower,
        [decimal]$Exponent = [decimal]0.6,
        [decimal]$MinimumFactor = [decimal]0.75,
        [decimal]$MaximumFactor = [decimal]1.35
    )

    Assert-Condition ($Power -gt 0 -and $IsotonicRarityMedianPower -gt 0) 'Within-tier power inputs must be positive.'
    Assert-Condition ($Exponent -gt 0) "Within-tier exponent '$Exponent' must be positive."
    Assert-Condition ($MinimumFactor -gt 0 -and $MaximumFactor -ge $MinimumFactor) "Invalid within-tier factor bounds '$MinimumFactor..$MaximumFactor'."
    $unclamped = [Math]::Pow([double]($Power / $IsotonicRarityMedianPower), [double]$Exponent)
    return [decimal][Math]::Min([double]$MaximumFactor, [Math]::Max([double]$MinimumFactor, $unclamped))
}

function ConvertTo-FinalBaseCost {
    param(
        [Parameter(Mandatory = $true)][decimal]$RawCost,
        [int]$MinimumCost = 5,
        [int]$MaximumCost = 40
    )

    Assert-Condition ($RawCost -ge 0) "Raw Cost '$RawCost' must not be negative."
    Assert-Condition ($MinimumCost -ge 0 -and $MaximumCost -ge $MinimumCost) "Invalid final Cost bounds '$MinimumCost..$MaximumCost'."
    $rounded = [int][Math]::Round($RawCost, 0, [System.MidpointRounding]::AwayFromZero)
    return [int][Math]::Max($MinimumCost, [Math]::Min($MaximumCost, $rounded))
}

function Get-CostCurveCandidate {
    param(
        [Parameter(Mandatory = $true)][object[]]$Rows,
        [Parameter(Mandatory = $true)][decimal[]]$RarityMedians,
        [Parameter(Mandatory = $true)][decimal[]]$IsotonicRarityMedians,
        [Parameter(Mandatory = $true)][decimal]$CompressionAlpha,
        [Parameter(Mandatory = $true)][decimal]$Rarity6Anchor,
        [Parameter(Mandatory = $true)][decimal]$MaximumWithinTierFactor,
        [decimal]$WithinTierExponent = [decimal]0.6,
        [decimal]$MinimumWithinTierFactor = [decimal]0.75,
        [int]$MinimumCost = 5,
        [int]$MaximumCost = 40,
        [int]$MaximumHighCostCount = 8
    )

    $baseCosts = @(Get-RarityBaseCosts -IsotonicRarityMedians $IsotonicRarityMedians -CompressionAlpha $CompressionAlpha -Rarity6Anchor $Rarity6Anchor)
    $candidateRows = foreach ($row in $Rows) {
        $rarity = [int]$row.Rarity
        Assert-Condition ($rarity -ge 1 -and $rarity -le 6) "Type ID $($row.TypeId) has invalid rarity '$rarity' for Cost."
        $power = [decimal]$row.ContinuousPower
        $factor = Get-WithinTierFactor `
            -Power $power `
            -IsotonicRarityMedianPower $IsotonicRarityMedians[$rarity - 1] `
            -Exponent $WithinTierExponent `
            -MinimumFactor $MinimumWithinTierFactor `
            -MaximumFactor $MaximumWithinTierFactor
        $rawCost = [decimal]$baseCosts[$rarity - 1] * $factor
        $properties = [ordered]@{}
        foreach ($property in $row.PSObject.Properties) {
            $properties[$property.Name] = $property.Value
        }
        $properties.RarityMedianPower = $RarityMedians[$rarity - 1]
        $properties.IsotonicRarityMedianPower = $IsotonicRarityMedians[$rarity - 1]
        $properties.CompressionAlpha = $CompressionAlpha
        $properties.RarityBaseCost = $baseCosts[$rarity - 1]
        $properties.WithinTierFactor = $factor
        $properties.RawCostBeforeRounding = $rawCost
        $properties.FinalBaseCost = ConvertTo-FinalBaseCost -RawCost $rawCost -MinimumCost $MinimumCost -MaximumCost $MaximumCost
        [pscustomobject]$properties
    }
    $candidateRows = @($candidateRows)

    $medianCosts = [decimal[]]::new(5)
    for ($rarity = 2; $rarity -le 6; $rarity++) {
        $medianCosts[$rarity - 2] = Get-Median -Values ([decimal[]]@(
                $candidateRows |
                    Where-Object { [int]$_.Rarity -eq $rarity } |
                    ForEach-Object { [decimal]$_.FinalBaseCost }
            ))
    }
    $failures = [System.Collections.Generic.List[string]]::new()
    for ($index = 1; $index -lt $medianCosts.Count; $index++) {
        if ($medianCosts[$index] -lt $medianCosts[$index - 1]) {
            $failures.Add('R2-R6 median Cost is not nondecreasing.')
            break
        }
    }
    $rarityCostRatio = $medianCosts[4] / $medianCosts[0]
    if ($rarityCostRatio -gt 4) {
        $failures.Add("R6/R2 median Cost ratio '$rarityCostRatio' exceeds 4.")
    }
    foreach ($row in $candidateRows | Where-Object { [int]$_.Rarity -ge 2 }) {
        $cost = [decimal]$row.FinalBaseCost
        if ($cost -ne [decimal][int]$cost -or $cost -lt $MinimumCost -or $cost -gt $MaximumCost) {
            $failures.Add("Type ID $($row.TypeId) has invalid R2-R6 final Cost '$cost'.")
            break
        }
    }
    foreach ($rarity in 1..6) {
        $orderedRows = @(
            $candidateRows |
                Where-Object { [int]$_.Rarity -eq $rarity } |
                Sort-Object { [decimal]$_.ContinuousPower }, { [int]$_.TypeId }
        )
        for ($index = 1; $index -lt $orderedRows.Count; $index++) {
            if ([int]$orderedRows[$index].FinalBaseCost -lt [int]$orderedRows[$index - 1].FinalBaseCost) {
                $failures.Add("Rarity $rarity final Cost decreases as ContinuousPower increases.")
                break
            }
        }
    }
    $highCostCount = @($candidateRows | Where-Object { [int]$_.FinalBaseCost -gt 30 }).Count
    if ($highCostCount -gt $MaximumHighCostCount) {
        $failures.Add("Final Cost >30 count '$highCostCount' exceeds '$MaximumHighCostCount'.")
    }

    return [pscustomobject]@{
        Rows = $candidateRows
        Rarity6Anchor = $Rarity6Anchor
        MaximumWithinTierFactor = $MaximumWithinTierFactor
        ParameterDistance = (($Rarity6Anchor - [decimal]28) * ($Rarity6Anchor - [decimal]28)) +
            (($MaximumWithinTierFactor - [decimal]1.35) * ($MaximumWithinTierFactor - [decimal]1.35))
        HighCostCount = $highCostCount
        Rarity2To6MedianCosts = $medianCosts
        Rarity6ToRarity2MedianCostRatio = $rarityCostRatio
        ConstraintFailures = $failures.ToArray()
        IsValid = ($failures.Count -eq 0)
    }
}

function Get-CostCurveModel {
    param([Parameter(Mandatory = $true)][object[]]$Rows)

    Assert-Condition ($Rows.Count -gt 0) 'Cannot build a Cost curve from no rows.'
    $rarityMedians = [decimal[]]::new(6)
    foreach ($rarity in 1..6) {
        $rarityRows = @($Rows | Where-Object { [int]$_.Rarity -eq $rarity })
        Assert-Condition ($rarityRows.Count -gt 0) "Cost curve has no R$rarity rows."
        $rarityMedians[$rarity - 1] = Get-Median -Values ([decimal[]]@($rarityRows | ForEach-Object { [decimal]$_.ContinuousPower }))
        Assert-Condition ($rarityMedians[$rarity - 1] -gt 0) "R$rarity median ContinuousPower must be positive."
    }
    $isotonicMedians = [decimal[]]@(Get-IsotonicNondecreasingValues -Values $rarityMedians)
    $compressionAlpha = Get-CompressionAlpha -Rarity2Power $isotonicMedians[1] -Rarity6Power $isotonicMedians[5]

    $evaluated = [System.Collections.Generic.List[object]]::new()
    $defaultCandidate = Get-CostCurveCandidate `
        -Rows $Rows `
        -RarityMedians $rarityMedians `
        -IsotonicRarityMedians $isotonicMedians `
        -CompressionAlpha $compressionAlpha `
        -Rarity6Anchor 28 `
        -MaximumWithinTierFactor 1.35
    $evaluated.Add($defaultCandidate)
    $selected = if ($defaultCandidate.IsValid) { $defaultCandidate } else { $null }
    $scanTriggered = $defaultCandidate.HighCostCount -gt 8
    if ($null -eq $selected -and -not $scanTriggered) {
        throw "Default Cost model violates a non-sparsity invariant: $($defaultCandidate.ConstraintFailures -join ' | ')"
    }

    if ($null -eq $selected) {
        $factorCandidates = [System.Collections.Generic.List[object]]::new()
        for ($hundredths = 134; $hundredths -ge 115; $hundredths--) {
            $candidate = Get-CostCurveCandidate `
                -Rows $Rows `
                -RarityMedians $rarityMedians `
                -IsotonicRarityMedians $isotonicMedians `
                -CompressionAlpha $compressionAlpha `
                -Rarity6Anchor 28 `
                -MaximumWithinTierFactor ([decimal]$hundredths / [decimal]100)
            $evaluated.Add($candidate)
            if ($candidate.IsValid) {
                $factorCandidates.Add($candidate)
            }
        }
        if ($factorCandidates.Count -gt 0) {
            $selected = @($factorCandidates | Sort-Object ParameterDistance, @{ Expression = 'MaximumWithinTierFactor'; Descending = $true })[0]
        }
    }

    if ($null -eq $selected) {
        $expandedCandidates = [System.Collections.Generic.List[object]]::new()
        for ($quarters = 111; $quarters -ge 104; $quarters--) {
            $anchor = [decimal]$quarters / [decimal]4
            for ($hundredths = 135; $hundredths -ge 115; $hundredths--) {
                $candidate = Get-CostCurveCandidate `
                    -Rows $Rows `
                    -RarityMedians $rarityMedians `
                    -IsotonicRarityMedians $isotonicMedians `
                    -CompressionAlpha $compressionAlpha `
                    -Rarity6Anchor $anchor `
                    -MaximumWithinTierFactor ([decimal]$hundredths / [decimal]100)
                $evaluated.Add($candidate)
                if ($candidate.IsValid) {
                    $expandedCandidates.Add($candidate)
                }
            }
        }
        if ($expandedCandidates.Count -gt 0) {
            $selected = @(
                $expandedCandidates |
                    Sort-Object ParameterDistance,
                        @{ Expression = 'Rarity6Anchor'; Descending = $true },
                        @{ Expression = 'MaximumWithinTierFactor'; Descending = $true }
            )[0]
        }
    }

    if ($null -eq $selected) {
        throw 'No unified Cost parameter candidate satisfies the model invariants and top-sparsity limit.'
    }
    $candidateAudit = @($evaluated | ForEach-Object {
            [pscustomobject][ordered]@{
                Rarity6Anchor = $_.Rarity6Anchor
                MaximumWithinTierFactor = $_.MaximumWithinTierFactor
                ParameterDistance = $_.ParameterDistance
                HighCostCount = $_.HighCostCount
                Rarity2To6MedianCosts = $_.Rarity2To6MedianCosts
                Rarity6ToRarity2MedianCostRatio = $_.Rarity6ToRarity2MedianCostRatio
                ConstraintFailures = $_.ConstraintFailures
                IsValid = $_.IsValid
            }
        })
    return [pscustomobject]@{
        Rows = $selected.Rows
        RarityMedians = $rarityMedians
        IsotonicRarityMedians = $isotonicMedians
        CompressionAlpha = $compressionAlpha
        SelectedRarity6Anchor = $selected.Rarity6Anchor
        SelectedMaxWithinTierFactor = $selected.MaximumWithinTierFactor
        HighCostCount = $selected.HighCostCount
        Rarity2To6MedianCosts = $selected.Rarity2To6MedianCosts
        Rarity6ToRarity2MedianCostRatio = $selected.Rarity6ToRarity2MedianCostRatio
        ParameterScanTriggered = $scanTriggered
        CandidateAudit = $candidateAudit
    }
}

function New-StableBudgetFormation {
    param(
        [Parameter(Mandatory = $true)][object[]]$Candidates,
        [Parameter(Mandatory = $true)][int]$Budget,
        [Parameter(Mandatory = $true)][string]$Kind
    )

    Assert-Condition ($Candidates.Count -gt 0) "Cannot build a $Kind formation from no candidates."
    Assert-Condition ($Budget -ge 0) "Formation budget '$Budget' must not be negative."
    $minimumCandidateCost = [int](@($Candidates | ForEach-Object { [int]$_.FinalBaseCost } | Measure-Object -Minimum).Minimum)
    Assert-Condition ($minimumCandidateCost -gt 0) "$Kind formation contains a nonpositive Cost."
    $selectedRows = [System.Collections.Generic.List[object]]::new()
    $remainingBudget = $Budget
    $candidateIndex = 0
    while ($remainingBudget -ge $minimumCandidateCost) {
        $selectedIndex = -1
        for ($offset = 0; $offset -lt $Candidates.Count; $offset++) {
            $index = ($candidateIndex + $offset) % $Candidates.Count
            if ([int]$Candidates[$index].FinalBaseCost -le $remainingBudget) {
                $selectedIndex = $index
                break
            }
        }
        Assert-Condition ($selectedIndex -ge 0) "$Kind formation could not select an affordable candidate despite remaining budget '$remainingBudget'."
        $selected = $Candidates[$selectedIndex]
        $selectedRows.Add($selected)
        $remainingBudget -= [int]$selected.FinalBaseCost
        $candidateIndex = ($selectedIndex + 1) % $Candidates.Count
    }

    return [pscustomobject][ordered]@{
        Kind = $Kind
        Budget = $Budget
        SpentCost = $Budget - $remainingBudget
        RemainingBudget = $remainingBudget
        Rows = $selectedRows.ToArray()
        TypeIds = @($selectedRows | ForEach-Object { [int]$_.TypeId })
    }
}

function Get-TierFormationSet {
    param(
        [Parameter(Mandatory = $true)][object[]]$Rows,
        [Parameter(Mandatory = $true)][int]$Budget
    )

    Assert-Condition ($Rows.Count -gt 0) 'Cannot build tier formations from no rows.'
    foreach ($row in $Rows) {
        Assert-Condition ([int]$row.FinalBaseCost -gt 0) "Type ID $($row.TypeId) has nonpositive formation Cost '$($row.FinalBaseCost)'."
    }
    $tierMedian = Get-Median -Values ([decimal[]]@($Rows | ForEach-Object { [decimal]$_.FinalBaseCost }))
    $halfCount = [int][Math]::Ceiling([double]$Rows.Count / 2)
    $lowCandidates = @(
        $Rows |
            Sort-Object { [int]$_.FinalBaseCost }, { [int]$_.TypeId } |
            Select-Object -First $halfCount
    )
    $medianCandidates = @(
        $Rows |
            Sort-Object { [Math]::Abs([double]([decimal]$_.FinalBaseCost - $tierMedian)) }, { [int]$_.TypeId }
    )
    $highCandidates = @(
        $Rows |
            Sort-Object @{ Expression = { [int]$_.FinalBaseCost }; Descending = $true }, { [int]$_.TypeId } |
            Select-Object -First $halfCount
    )
    return @(
        New-StableBudgetFormation -Candidates $lowCandidates -Budget $Budget -Kind 'Low'
        New-StableBudgetFormation -Candidates $medianCandidates -Budget $Budget -Kind 'Median'
        New-StableBudgetFormation -Candidates $highCandidates -Budget $Budget -Kind 'High'
    )
}

function Get-CalibrationMatchDefinitions {
    param(
        [Parameter(Mandatory = $true)][object[]]$Rarity1Rows,
        [Parameter(Mandatory = $true)][object[]]$Rarity2Rows,
        [Parameter(Mandatory = $true)][int[]]$Budgets
    )

    $matches = [System.Collections.Generic.List[object]]::new()
    foreach ($budget in $Budgets) {
        $rarity1Formations = @(Get-TierFormationSet -Rows $Rarity1Rows -Budget $budget)
        $rarity2Formations = @(Get-TierFormationSet -Rows $Rarity2Rows -Budget $budget)
        Assert-Condition ($rarity1Formations.Count -eq 3 -and $rarity2Formations.Count -eq 3) "Budget $budget did not produce three formations per rarity."
        foreach ($rarity1Formation in $rarity1Formations) {
            foreach ($rarity2Formation in $rarity2Formations) {
                foreach ($direction in @('HighValueFirst', 'LowValueFirst')) {
                    $matches.Add([pscustomobject][ordered]@{
                            Budget = $budget
                            Rarity1FormationKind = $rarity1Formation.Kind
                            Rarity2FormationKind = $rarity2Formation.Kind
                            Direction = $direction
                            Rarity1Rows = $rarity1Formation.Rows
                            Rarity2Rows = $rarity2Formation.Rows
                            Rarity1SpentCost = $rarity1Formation.SpentCost
                            Rarity2SpentCost = $rarity2Formation.SpentCost
                        })
                }
            }
        }
    }
    return $matches.ToArray()
}

function New-CalibrationBattleEntity {
    param([Parameter(Mandatory = $true)]$Row)

    $continuousPower = [decimal]$Row.ContinuousPower
    Assert-Condition ($continuousPower -gt 0) "Type ID $($Row.TypeId) has nonpositive ContinuousPower for calibration."
    $hasPanelPower = $null -ne $Row.PanelPower
    $equivalentEntityCount = [decimal]1
    if ($hasPanelPower) {
        $baseAdjustedPower = [decimal]$Row.PanelPower * [decimal]$Row.AbilityPowerMultiplier
        Assert-Condition ($baseAdjustedPower -gt 0) "Type ID $($Row.TypeId) has nonpositive base-adjusted power for calibration."
        $equivalentEntityCount += [decimal]$Row.EquivalentEntityContribution / $baseAdjustedPower
    }
    $effectiveHitPoints = [decimal]$Row.MaxHitPoints * [decimal]$Row.DefenseScenarioMain
    if ($hasPanelPower) {
        $effectiveHitPoints *= $equivalentEntityCount
    }
    Assert-Condition ($effectiveHitPoints -gt 0) "Type ID $($Row.TypeId) has nonpositive effective HP for calibration."
    Assert-Condition ($equivalentEntityCount -gt 0) "Type ID $($Row.TypeId) has nonpositive equivalent entity count for calibration."

    return [pscustomobject]@{
        TypeId = [int]$Row.TypeId
        Attack = [decimal]$Row.Attack
        DamageType = [string]$Row.DamageType
        EffectiveAttackIntervalSeconds = [decimal]$Row.EffectiveAttackIntervalSeconds
        OutputScenarioMain = [decimal]$Row.OutputScenarioMain
        Defense = [decimal]$Row.Defense
        MagicResistance = [decimal]$Row.MagicResistance
        RemainingHitPoints = $effectiveHitPoints
        EquivalentEntityCount = $equivalentEntityCount
        BlockCapacity = [decimal]1
        TargetValue = [decimal]$Row.LifeDeduct * $equivalentEntityCount
    }
}

function Get-CalibrationTeamDps {
    param(
        [Parameter(Mandatory = $true)][object[]]$Attackers,
        [Parameter(Mandatory = $true)]$Target
    )

    $dps = [decimal]0
    foreach ($attacker in $Attackers) {
        if ($attacker.DamageType -ceq 'None' -or [decimal]$attacker.Attack -le 0) {
            continue
        }
        $attack = [decimal]$attacker.Attack
        $damagePerHit = switch ([string]$attacker.DamageType) {
            'Physical' {
                [decimal][Math]::Max(
                    [double]($attack - [decimal]$Target.Defense),
                    [Math]::Floor([double]($attack * [decimal]0.05))
                )
                break
            }
            'Magic' {
                [decimal][Math]::Max(
                    [Math]::Floor([double]($attack * ([decimal]100 - [decimal]$Target.MagicResistance) / [decimal]100)),
                    [Math]::Floor([double]($attack * [decimal]0.05))
                )
                break
            }
            'True' { $attack; break }
            default { throw "Cannot calculate calibration DPS for Type ID $($attacker.TypeId) with damage type '$($attacker.DamageType)'." }
        }
        $interval = [decimal]$attacker.EffectiveAttackIntervalSeconds
        Assert-Condition ($interval -gt 0) "Type ID $($attacker.TypeId) has nonpositive calibration attack interval '$interval'."
        $dps += ($damagePerHit / $interval) *
            [decimal]$attacker.OutputScenarioMain *
            [decimal]$attacker.EquivalentEntityCount
    }
    return $dps
}

function Get-CalibrationTarget {
    param(
        [Parameter(Mandatory = $true)][object[]]$Entities,
        [Parameter(Mandatory = $true)][ValidateSet('HighValueFirst', 'LowValueFirst')][string]$Direction
    )

    if ($Direction -ceq 'HighValueFirst') {
        return @(
            $Entities |
                Sort-Object @{ Expression = { [decimal]$_.TargetValue }; Descending = $true },
                    @{ Expression = { [int]$_.TypeId }; Descending = $false }
        )[0]
    }
    return @(
        $Entities |
            Sort-Object @{ Expression = { [decimal]$_.TargetValue }; Descending = $false },
                @{ Expression = { [int]$_.TypeId }; Descending = $true }
    )[0]
}

function Get-CalibrationRemainingSummary {
    param([Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Entities)

    $equivalentEntityCount = [decimal]0
    $targetValue = [decimal]0
    $blockCapacity = [decimal]0
    foreach ($entity in $Entities) {
        $equivalentEntityCount += [decimal]$entity.EquivalentEntityCount
        $targetValue += [decimal]$entity.TargetValue
        $blockCapacity += [decimal]$entity.BlockCapacity
    }
    return [pscustomobject]@{
        EntityCount = $Entities.Count
        EquivalentEntityCount = $equivalentEntityCount
        TargetValue = $targetValue
        BlockCapacity = $blockCapacity
    }
}

function Invoke-FocusFireMatch {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Rarity1Rows,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Rarity2Rows,
        [Parameter(Mandatory = $true)][ValidateSet('HighValueFirst', 'LowValueFirst')][string]$Direction,
        [int]$EventLimit = 1024,
        [switch]$IncludeEventTrace
    )

    Assert-Condition ($EventLimit -gt 0) "Match event limit '$EventLimit' must be positive."
    $rarity1Unordered = @($Rarity1Rows | ForEach-Object { New-CalibrationBattleEntity -Row $_ })
    $rarity2Unordered = @($Rarity2Rows | ForEach-Object { New-CalibrationBattleEntity -Row $_ })
    $rarity1Ordered = if ($Direction -ceq 'HighValueFirst') {
        @($rarity1Unordered | Sort-Object @{ Expression = { [decimal]$_.TargetValue }; Descending = $true }, @{ Expression = { [int]$_.TypeId }; Descending = $false })
    }
    else {
        @($rarity1Unordered | Sort-Object @{ Expression = { [decimal]$_.TargetValue }; Descending = $false }, @{ Expression = { [int]$_.TypeId }; Descending = $true })
    }
    $rarity2Ordered = if ($Direction -ceq 'HighValueFirst') {
        @($rarity2Unordered | Sort-Object @{ Expression = { [decimal]$_.TargetValue }; Descending = $true }, @{ Expression = { [int]$_.TypeId }; Descending = $false })
    }
    else {
        @($rarity2Unordered | Sort-Object @{ Expression = { [decimal]$_.TargetValue }; Descending = $false }, @{ Expression = { [int]$_.TypeId }; Descending = $true })
    }
    $rarity1Entities = [System.Collections.Generic.List[object]]::new()
    foreach ($entity in $rarity1Ordered) { $rarity1Entities.Add($entity) }
    $rarity2Entities = [System.Collections.Generic.List[object]]::new()
    foreach ($entity in $rarity2Ordered) { $rarity2Entities.Add($entity) }
    $eventCount = 0
    $resolution = 'Elimination'
    $eventTrace = [System.Collections.Generic.List[object]]::new()

    while ($rarity1Entities.Count -gt 0 -and $rarity2Entities.Count -gt 0) {
        Assert-Condition ($eventCount -lt $EventLimit) "Focus-fire match exceeded fixed event limit '$EventLimit'."
        $rarity1Target = $rarity1Entities[0]
        $rarity2Target = $rarity2Entities[0]
        $rarity1Dps = Get-CalibrationTeamDps -Attackers $rarity1Entities.ToArray() -Target $rarity2Target
        $rarity2Dps = Get-CalibrationTeamDps -Attackers $rarity2Entities.ToArray() -Target $rarity1Target
        if ($rarity1Dps -le 0 -and $rarity2Dps -le 0) {
            $resolution = 'NoDamageTiebreak'
            break
        }
        $rarity1KillTime = if ($rarity1Dps -gt 0) { [decimal]$rarity2Target.RemainingHitPoints / $rarity1Dps } else { [decimal]::MaxValue }
        $rarity2KillTime = if ($rarity2Dps -gt 0) { [decimal]$rarity1Target.RemainingHitPoints / $rarity2Dps } else { [decimal]::MaxValue }
        $rarity2TargetDies = $rarity1KillTime -le $rarity2KillTime
        $rarity1TargetDies = $rarity2KillTime -le $rarity1KillTime
        $eventTime = if ($rarity1KillTime -le $rarity2KillTime) { $rarity1KillTime } else { $rarity2KillTime }
        Assert-Condition ($eventTime -gt 0 -and -not [double]::IsInfinity([double]$eventTime)) "Focus-fire match produced invalid event time '$eventTime'."
        $rarity1HitPointsBefore = [decimal]$rarity1Target.RemainingHitPoints
        $rarity2HitPointsBefore = [decimal]$rarity2Target.RemainingHitPoints
        if ($rarity1Dps -gt 0) { $rarity2Target.RemainingHitPoints = [decimal]$rarity2Target.RemainingHitPoints - ($rarity1Dps * $eventTime) }
        if ($rarity2Dps -gt 0) { $rarity1Target.RemainingHitPoints = [decimal]$rarity1Target.RemainingHitPoints - ($rarity2Dps * $eventTime) }
        if ($IncludeEventTrace) {
            $eventTrace.Add([pscustomobject][ordered]@{
                    EventIndex = $eventCount + 1
                    EventTime = $eventTime
                    R1TargetTypeId = $rarity1Target.TypeId
                    R2TargetTypeId = $rarity2Target.TypeId
                    R1TargetHitPointsBefore = $rarity1HitPointsBefore
                    R2TargetHitPointsBefore = $rarity2HitPointsBefore
                    R1TeamDps = $rarity1Dps
                    R2TeamDps = $rarity2Dps
                    R1TargetHitPointsAfter = $rarity1Target.RemainingHitPoints
                    R2TargetHitPointsAfter = $rarity2Target.RemainingHitPoints
                    R1TargetDies = $rarity1TargetDies
                    R2TargetDies = $rarity2TargetDies
                })
        }
        if ($rarity1TargetDies) { $rarity1Entities.RemoveAt(0) }
        if ($rarity2TargetDies) { $rarity2Entities.RemoveAt(0) }
        $eventCount++
    }

    $rarity1Summary = Get-CalibrationRemainingSummary -Entities $rarity1Entities.ToArray()
    $rarity2Summary = Get-CalibrationRemainingSummary -Entities $rarity2Entities.ToArray()
    $winner = if ($rarity1Summary.EntityCount -gt 0 -and $rarity2Summary.EntityCount -eq 0) {
        'R1'
    }
    elseif ($rarity2Summary.EntityCount -gt 0 -and $rarity1Summary.EntityCount -eq 0) {
        'R2'
    }
    else {
        $comparison = 0
        foreach ($property in @('EquivalentEntityCount', 'TargetValue', 'BlockCapacity')) {
            if ([decimal]$rarity1Summary.$property -gt [decimal]$rarity2Summary.$property) { $comparison = 1; break }
            if ([decimal]$rarity1Summary.$property -lt [decimal]$rarity2Summary.$property) { $comparison = -1; break }
        }
        if ($comparison -gt 0) { 'R1' } elseif ($comparison -lt 0) { 'R2' } else { 'Draw' }
    }

    return [pscustomobject][ordered]@{
        Winner = $winner
        Resolution = $resolution
        EventCount = $eventCount
        R1RemainingEntityCount = $rarity1Summary.EntityCount
        R2RemainingEntityCount = $rarity2Summary.EntityCount
        R1RemainingEquivalentEntities = $rarity1Summary.EquivalentEntityCount
        R2RemainingEquivalentEntities = $rarity2Summary.EquivalentEntityCount
        R1RemainingTargetValue = $rarity1Summary.TargetValue
        R2RemainingTargetValue = $rarity2Summary.TargetValue
        R1RemainingBlockCapacity = $rarity1Summary.BlockCapacity
        R2RemainingBlockCapacity = $rarity2Summary.BlockCapacity
        EventTrace = $eventTrace.ToArray()
    }
}

function Select-R1CalibrationCandidate {
    param([Parameter(Mandatory = $true)][object[]]$Candidates)

    $eligible = @($Candidates | Where-Object {
            [decimal]$_.Rarity1WinRate -ge [decimal]0.4 -and
            [decimal]$_.Rarity1WinRate -le [decimal]0.6
        })
    if ($eligible.Count -eq 0) {
        return $null
    }
    return @(
        $eligible |
            Sort-Object { [Math]::Abs([double]([decimal]$_.Rarity1WinRate - [decimal]0.5)) },
                { [Math]::Abs([double]([decimal]$_.Kappa - [decimal]1)) },
                { [decimal]$_.Kappa }
    )[0]
}

function Get-R1CalibrationKappaCandidates {
    return [decimal[]]@(for ($hundredths = 1; $hundredths -le 150; $hundredths++) {
            [decimal]$hundredths / [decimal]100
        })
}

function Get-R1CalibratedRows {
    param(
        [Parameter(Mandatory = $true)][object[]]$Rows,
        [Parameter(Mandatory = $true)][decimal]$Kappa
    )

    Assert-Condition ($Kappa -ge [decimal]0.01 -and $Kappa -le [decimal]1.5) "R1 calibration kappa '$Kappa' is outside 0.01..1.50."
    return @($Rows | ForEach-Object {
            $row = $_
            $properties = [ordered]@{}
            foreach ($property in $row.PSObject.Properties) {
                $properties[$property.Name] = $property.Value
            }
            if ([int]$row.Rarity -eq 1) {
                $initialCost = [int]$row.FinalBaseCost
                $calibratedRawCost = $Kappa * [decimal]$initialCost
                $properties.FinalBaseCost = ConvertTo-FinalBaseCost -RawCost $calibratedRawCost -MinimumCost 2
                $properties.R1InitialCost = $initialCost
                $properties.R1CalibrationKappa = $Kappa
                $properties.R1CalibratedRawCost = $calibratedRawCost
            }
            else {
                $properties.R1InitialCost = $null
                $properties.R1CalibrationKappa = $null
                $properties.R1CalibratedRawCost = $null
            }
            [pscustomobject]$properties
        })
}

function Get-EliteResourcePlan {
    param(
        [Parameter(Mandatory = $true)][string[]]$DirectoryNames,
        [Parameter(Mandatory = $true)][string]$TypeId,
        [Parameter(Mandatory = $true)][string]$E0ResourceDirectory
    )

    $nameCounts = @{}
    foreach ($directoryName in $DirectoryNames) {
        $nameCounts[$directoryName] = 1 + $(if ($nameCounts.ContainsKey($directoryName)) { [int]$nameCounts[$directoryName] } else { 0 })
    }
    Assert-Condition ($nameCounts.ContainsKey($E0ResourceDirectory) -and [int]$nameCounts[$E0ResourceDirectory] -eq 1) "TypeId '$TypeId' must have exactly one E0 resource directory '$E0ResourceDirectory'."

    $elite2DedicatedName = if ($TypeId -ceq '1322') { '1322_wdgyht' } else { "${E0ResourceDirectory}_2" }
    $elite3DedicatedName = if ($TypeId -ceq '1322') { '1322_wdgyht_3' } else { "${E0ResourceDirectory}_3" }
    $elite2DedicatedCount = if ($nameCounts.ContainsKey($elite2DedicatedName)) { [int]$nameCounts[$elite2DedicatedName] } else { 0 }
    $elite3DedicatedCount = if ($nameCounts.ContainsKey($elite3DedicatedName)) { [int]$nameCounts[$elite3DedicatedName] } else { 0 }
    Assert-Condition ($elite2DedicatedCount -le 1) "TypeId '$TypeId' has ambiguous E2 resource directory '$elite2DedicatedName'."
    Assert-Condition ($elite3DedicatedCount -le 1) "TypeId '$TypeId' has ambiguous E3 resource directory '$elite3DedicatedName'."

    $elite2IsDedicated = $elite2DedicatedCount -eq 1
    $elite2ResourceDirectory = if ($elite2IsDedicated) { $elite2DedicatedName } else { $E0ResourceDirectory }
    $elite3IsDedicated = $elite3DedicatedCount -eq 1
    $elite3ResourceDirectory = if ($elite3IsDedicated) { $elite3DedicatedName } else { $elite2ResourceDirectory }

    return [pscustomobject][ordered]@{
        TypeId = $TypeId
        E0ResourceDirectory = $E0ResourceDirectory
        Elite2ResourceDirectory = $elite2ResourceDirectory
        Elite2ResourceSource = if ($elite2IsDedicated) { 'Dedicated' } else { 'InheritedE0' }
        Elite2IsDedicated = $elite2IsDedicated
        Elite3ResourceDirectory = $elite3ResourceDirectory
        Elite3ResourceSource = if ($elite3IsDedicated) { 'Dedicated' } else { 'InheritedE2' }
        Elite3IsDedicated = $elite3IsDedicated
    }
}

function Get-EliteStageCalculation {
    param(
        [Parameter(Mandatory = $true)][ValidateSet(1, 2, 3)][int]$EliteLevel,
        [Parameter(Mandatory = $true)][decimal]$E0ContinuousPower,
        [Parameter(Mandatory = $true)][int]$E0Cost,
        [AllowNull()][Nullable[decimal]]$E0PanelPower,
        [AllowNull()][Nullable[decimal]]$StagePanelPower,
        [Parameter(Mandatory = $true)][decimal]$AbilityPowerMultiplier,
        [Parameter(Mandatory = $true)][decimal]$EquivalentEntityContribution,
        [Parameter(Mandatory = $true)][string]$ResourceSource
    )

    Assert-Condition ($E0ContinuousPower -gt 0) "E0 ContinuousPower '$E0ContinuousPower' must be positive."
    Assert-Condition ($E0Cost -gt 0) "E0 Cost '$E0Cost' must be positive."
    Assert-Condition ($AbilityPowerMultiplier -gt 0) "AbilityPowerMultiplier '$AbilityPowerMultiplier' must be positive."
    Assert-Condition ($EquivalentEntityContribution -ge 0) "EquivalentEntityContribution '$EquivalentEntityContribution' must be nonnegative."

    $entityCount = switch ($EliteLevel) {
        1 { 2 }
        2 { 3 }
        3 { 5 }
    }
    $hasBasePanelPower = $null -ne $E0PanelPower
    $hasStagePanelPower = $null -ne $StagePanelPower
    $usesVariantPanel = $EliteLevel -gt 1 -and $ResourceSource -in @('Dedicated', 'InheritedE2') -and $hasBasePanelPower -and $hasStagePanelPower
    $continuousPowerPerBody = if ($usesVariantPanel) {
        ([decimal]$StagePanelPower * $AbilityPowerMultiplier) + $EquivalentEntityContribution
    }
    else {
        $E0ContinuousPower
    }
    Assert-Condition ($continuousPowerPerBody -gt 0) "E$EliteLevel ContinuousPower '$continuousPowerPerBody' must be positive."

    $totalPower = $continuousPowerPerBody * [decimal]$entityCount
    $totalCost = $E0Cost * $entityCount
    return [pscustomobject][ordered]@{
        EliteLevel = $EliteLevel
        EntityCount = $entityCount
        ResourceSource = $ResourceSource
        ContinuousPowerPerBody = $continuousPowerPerBody
        TotalPower = $totalPower
        TotalCost = $totalCost
        PowerPerCostRatio = $continuousPowerPerBody / [decimal]$E0Cost
        PanelPowerStatus = if ($EliteLevel -eq 1) {
            'NoDedicatedPanel'
        }
        elseif (-not $hasBasePanelPower) {
            'InheritedE0ContinuousPowerNoPanelPower'
        }
        elseif ($ResourceSource -ceq 'Dedicated' -and $usesVariantPanel) {
            'DedicatedPanelPower'
        }
        elseif ($ResourceSource -ceq 'InheritedE2' -and $usesVariantPanel) {
            'InheritedE2PanelPower'
        }
        else {
            'InheritedE0ContinuousPower'
        }
    }
}

function Get-CombatPanelMetrics {
    param(
        [Parameter(Mandatory = $true)]$CombatRecord,
        [Parameter(Mandatory = $true)][object[]]$Defenders,
        [Parameter(Mandatory = $true)][object[]]$Attackers,
        [Parameter(Mandatory = $true)][decimal]$DpsLowerBound,
        [Parameter(Mandatory = $true)][decimal]$DpsUpperBound,
        [Parameter(Mandatory = $true)][decimal]$TtdLowerBound,
        [Parameter(Mandatory = $true)][decimal]$TtdUpperBound,
        [Parameter(Mandatory = $true)][decimal]$OutputReference,
        [Parameter(Mandatory = $true)][decimal]$DefenseReference
    )

    Assert-Condition ([string]$CombatRecord.DamageType -cne 'None') "Type ID $($CombatRecord.TypeId) cannot calculate PanelPower with damageType None."
    $effectiveInterval = Get-EffectiveAttackInterval -Attacker $CombatRecord
    $dpsValues = [decimal[]]@($Defenders | ForEach-Object {
            (Get-OrdinaryAttackDamage -Attacker $CombatRecord -Defender $_) / $effectiveInterval
        })
    $ttdValues = [decimal[]]@($Attackers | ForEach-Object {
            $damagePerHit = Get-OrdinaryAttackDamage -Attacker $_ -Defender $CombatRecord
            [decimal][Math]::Ceiling([double]([decimal]$CombatRecord.MaxHitPoints / $damagePerHit)) * (Get-EffectiveAttackInterval -Attacker $_)
        })
    $rawMedianDps = Get-Median -Values $dpsValues
    $rawMedianTtd = Get-Median -Values $ttdValues
    $winsorizedMedianDps = (Get-ClampedCombatValues -Values ([decimal[]]@($rawMedianDps)) -LowerBound $DpsLowerBound -UpperBound $DpsUpperBound)[0]
    $winsorizedMedianTtd = (Get-ClampedCombatValues -Values ([decimal[]]@($rawMedianTtd)) -LowerBound $TtdLowerBound -UpperBound $TtdUpperBound)[0]
    $panelPower = Get-GeometricCombinedValue `
        -Output ($winsorizedMedianDps / $OutputReference) `
        -Defense ($winsorizedMedianTtd / $DefenseReference)
    Assert-Condition ($panelPower -gt 0 -and -not [double]::IsNaN([double]$panelPower) -and -not [double]::IsInfinity([double]$panelPower)) "Type ID $($CombatRecord.TypeId) produced invalid variant PanelPower '$panelPower'."
    return [pscustomobject][ordered]@{
        RawMedianDps = $rawMedianDps
        WinsorizedMedianDps = $winsorizedMedianDps
        RawMedianTtdSeconds = $rawMedianTtd
        WinsorizedMedianTtdSeconds = $winsorizedMedianTtd
        PanelPower = $panelPower
    }
}

function Get-EconomyConstraintGrid {
    param([Parameter(Mandatory = $true)][object[]]$Rows)

    $goldBudgets = [int[]]@(34, 76, 126, 184, 250, 324)
    $costBudgets = [int[]]@(54, 99, 126, 195)
    $medianCostByRarity = @{}
    foreach ($rarity in 1..6) {
        $costs = [decimal[]]@($Rows | Where-Object { [int]$_.Rarity -eq $rarity } | ForEach-Object { [decimal]$_.FinalBaseCost })
        Assert-Condition ($costs.Count -gt 0) "Economy grid has no R$rarity Cost sample."
        $medianCost = Get-Median -Values $costs
        Assert-Condition ($medianCost -gt 0) "Economy grid R$rarity median Cost '$medianCost' must be positive."
        $medianCostByRarity[$rarity] = $medianCost
    }

    $gridRows = [System.Collections.Generic.List[object]]::new()
    $quantityRatios = [System.Collections.Generic.List[object]]::new()
    foreach ($goldBudget in $goldBudgets) {
        foreach ($costBudget in $costBudgets) {
            $caseRows = [System.Collections.Generic.List[object]]::new()
            foreach ($rarity in 1..6) {
                $medianCost = [decimal]$medianCostByRarity[$rarity]
                $goldLimitedQuantity = [int][Math]::Floor([decimal]$goldBudget / [decimal]$rarity)
                $costLimitedQuantity = [int][Math]::Floor([decimal]$costBudget / $medianCost)
                $quantity = [Math]::Min($goldLimitedQuantity, $costLimitedQuantity)
                $limiter = if ($goldLimitedQuantity -lt $costLimitedQuantity) {
                    'Gold'
                }
                elseif ($costLimitedQuantity -lt $goldLimitedQuantity) {
                    'Cost'
                }
                else {
                    'Both'
                }
                $caseRow = [pscustomobject][ordered]@{
                    GoldBudget = $goldBudget
                    CostBudget = $costBudget
                    Rarity = $rarity
                    MedianCost = $medianCost
                    GoldLimitedQuantity = $goldLimitedQuantity
                    CostLimitedQuantity = $costLimitedQuantity
                    Quantity = $quantity
                    Limiter = $limiter
                }
                $caseRows.Add($caseRow)
                $gridRows.Add($caseRow)
            }
            $r1Quantity = [decimal]$caseRows[0].Quantity
            $r2Quantity = [decimal]$caseRows[1].Quantity
            $r6Quantity = [decimal]$caseRows[5].Quantity
            $quantityRatios.Add([pscustomobject][ordered]@{
                    GoldBudget = $goldBudget
                    CostBudget = $costBudget
                    R1ToR2 = if ($r2Quantity -gt 0) { $r1Quantity / $r2Quantity } else { $null }
                    R2ToR6 = if ($r6Quantity -gt 0) { $r2Quantity / $r6Quantity } else { $null }
                })
        }
    }

    return [pscustomobject][ordered]@{
        GoldBudgets = $goldBudgets
        CostBudgets = $costBudgets
        MedianCostByRarity = [pscustomobject][ordered]@{
            R1 = $medianCostByRarity[1]
            R2 = $medianCostByRarity[2]
            R3 = $medianCostByRarity[3]
            R4 = $medianCostByRarity[4]
            R5 = $medianCostByRarity[5]
            R6 = $medianCostByRarity[6]
        }
        Rows = $gridRows.ToArray()
        QuantityRatios = $quantityRatios.ToArray()
    }
}

function Get-R1CalibrationModel {
    param(
        [Parameter(Mandatory = $true)][object[]]$Rows,
        [int[]]$Budgets = @(54, 99, 126, 195),
        [int]$MatchEventLimit = 1024
    )

    Assert-Condition ((@($Budgets) -join '/') -ceq '54/99/126/195') "R1 calibration budgets must be exactly 54/99/126/195; found '$(@($Budgets) -join '/')'."
    Assert-Condition ($MatchEventLimit -gt 0) "R1 calibration match event limit '$MatchEventLimit' must be positive."
    Assert-Condition (@($Rows | Where-Object { [int]$_.Rarity -eq 1 }).Count -gt 0) 'R1 calibration has no R1 rows.'
    Assert-Condition (@($Rows | Where-Object { [int]$_.Rarity -eq 2 }).Count -gt 0) 'R1 calibration has no R2 rows.'
    $evaluatedCandidates = [System.Collections.Generic.List[object]]::new()
    $matchResultCache = @{}

    foreach ($kappa in @(Get-R1CalibrationKappaCandidates)) {
        $candidateRows = @(Get-R1CalibratedRows -Rows $Rows -Kappa $kappa)
        $rarity1Rows = @($candidateRows | Where-Object { [int]$_.Rarity -eq 1 })
        $rarity2Rows = @($candidateRows | Where-Object { [int]$_.Rarity -eq 2 })
        $matchDefinitions = @(Get-CalibrationMatchDefinitions -Rarity1Rows $rarity1Rows -Rarity2Rows $rarity2Rows -Budgets $Budgets)
        Assert-Condition ($matchDefinitions.Count -eq 72) "R1 calibration kappa '$kappa' produced $($matchDefinitions.Count) matches instead of 72."
        $matchAudit = [System.Collections.Generic.List[object]]::new()
        $rarity1Wins = 0
        $rarity2Wins = 0
        $draws = 0
        $simulatedMatchCount = 0
        $reusedMatchCount = 0
        foreach ($definition in $matchDefinitions) {
            $matchKey = '{0}|{1}|{2}' -f `
                $definition.Direction, `
                (@($definition.Rarity1Rows | ForEach-Object { [int]$_.TypeId }) -join ','), `
                (@($definition.Rarity2Rows | ForEach-Object { [int]$_.TypeId }) -join ',')
            $cacheHit = $matchResultCache.ContainsKey($matchKey)
            if ($cacheHit) {
                $result = $matchResultCache[$matchKey]
                $reusedMatchCount++
            }
            else {
                $result = Invoke-FocusFireMatch `
                    -Rarity1Rows $definition.Rarity1Rows `
                    -Rarity2Rows $definition.Rarity2Rows `
                    -Direction $definition.Direction `
                    -EventLimit $MatchEventLimit
                $matchResultCache[$matchKey] = $result
                $simulatedMatchCount++
            }
            switch ([string]$result.Winner) {
                'R1' { $rarity1Wins++ }
                'R2' { $rarity2Wins++ }
                'Draw' { $draws++ }
                default { throw "R1 calibration produced invalid winner '$($result.Winner)'." }
            }
            $matchAudit.Add([pscustomobject][ordered]@{
                    Budget = $definition.Budget
                    Rarity1FormationKind = $definition.Rarity1FormationKind
                    Rarity2FormationKind = $definition.Rarity2FormationKind
                    Direction = $definition.Direction
                    Rarity1SpentCost = $definition.Rarity1SpentCost
                    Rarity2SpentCost = $definition.Rarity2SpentCost
                    Winner = $result.Winner
                    Resolution = $result.Resolution
                    SimulationCacheHit = $cacheHit
                    EventCount = $result.EventCount
                    R1RemainingEntityCount = $result.R1RemainingEntityCount
                    R2RemainingEntityCount = $result.R2RemainingEntityCount
                    R1RemainingEquivalentEntities = $result.R1RemainingEquivalentEntities
                    R2RemainingEquivalentEntities = $result.R2RemainingEquivalentEntities
                    R1RemainingTargetValue = $result.R1RemainingTargetValue
                    R2RemainingTargetValue = $result.R2RemainingTargetValue
                    R1RemainingBlockCapacity = $result.R1RemainingBlockCapacity
                    R2RemainingBlockCapacity = $result.R2RemainingBlockCapacity
                })
        }
        $rarity1WinRate = ([decimal]$rarity1Wins + ([decimal]$draws / [decimal]2)) / [decimal]$matchDefinitions.Count
        $evaluatedCandidates.Add([pscustomobject]@{
                Kappa = $kappa
                Rows = $candidateRows
                Matches = $matchAudit.ToArray()
                MatchCount = $matchDefinitions.Count
                Rarity1Wins = $rarity1Wins
                Rarity2Wins = $rarity2Wins
                Draws = $draws
                Rarity1WinRate = $rarity1WinRate
                SimulatedMatchCount = $simulatedMatchCount
                ReusedMatchCount = $reusedMatchCount
            })
    }

    $candidateAudit = @($evaluatedCandidates | ForEach-Object {
            [pscustomobject][ordered]@{
                Kappa = $_.Kappa
                MatchCount = $_.MatchCount
                Rarity1Wins = $_.Rarity1Wins
                Rarity2Wins = $_.Rarity2Wins
                Draws = $_.Draws
                Rarity1WinRate = $_.Rarity1WinRate
                SimulatedMatchCount = $_.SimulatedMatchCount
                ReusedMatchCount = $_.ReusedMatchCount
                IsEligible = ([decimal]$_.Rarity1WinRate -ge [decimal]0.4 -and [decimal]$_.Rarity1WinRate -le [decimal]0.6)
            }
        })
    $selected = Select-R1CalibrationCandidate -Candidates $evaluatedCandidates.ToArray()
    if ($null -eq $selected) {
        $summary = $candidateAudit | ConvertTo-Json -Depth 3 -Compress
        throw "No uniform R1 calibration candidate achieved a 40%-60% win rate. Complete candidate summary: $summary"
    }

    return [pscustomobject]@{
        Rows = $selected.Rows
        Kappa = $selected.Kappa
        MatchCount = $selected.MatchCount
        Rarity1Wins = $selected.Rarity1Wins
        Rarity2Wins = $selected.Rarity2Wins
        Draws = $selected.Draws
        Rarity1WinRate = $selected.Rarity1WinRate
        MatchEventLimit = $MatchEventLimit
        Budgets = $Budgets
        CandidateAudit = $candidateAudit
        SelectedMatches = $selected.Matches
    }
}

function Test-MapKey {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Map,
        [Parameter(Mandatory = $true)][string]$Key
    )

    return $Map.ContainsKey($Key)
}

function Get-MapDecimal {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Map,
        [Parameter(Mandatory = $true)][string]$Key,
        [Parameter(Mandatory = $true)][decimal]$Default
    )

    if (-not (Test-MapKey -Map $Map -Key $Key)) {
        return $Default
    }
    return [decimal]$Map[$Key]
}

function Get-AttackRateFactor {
    param(
        [Parameter(Mandatory = $true)][decimal]$AttackSpeedAdditive,
        [Parameter(Mandatory = $true)][decimal]$FinalAttackSpeedMultiplier
    )

    $finalAttackSpeed = [decimal][Math]::Max(
        [double]0,
        [double](([decimal]100 + $AttackSpeedAdditive) * $FinalAttackSpeedMultiplier)
    )
    return $finalAttackSpeed / [decimal]100
}

function Get-AutomaticSkillCastTimes {
    param(
        [Parameter(Mandatory = $true)][decimal]$WindowSeconds,
        [Parameter(Mandatory = $true)][decimal]$SkillPointsPerSecond,
        [Parameter(Mandatory = $true)][decimal]$InitialSkillPoints,
        [Parameter(Mandatory = $true)][decimal]$SkillPointCost
    )

    Assert-Condition ($WindowSeconds -gt 0) "Automatic-skill window '$WindowSeconds' must be positive."
    Assert-Condition ($SkillPointsPerSecond -gt 0) "Automatic-skill SP rate '$SkillPointsPerSecond' must be positive."
    Assert-Condition ($InitialSkillPoints -ge 0 -and $InitialSkillPoints -lt $SkillPointCost) "Automatic-skill initial SP '$InitialSkillPoints' must be nonnegative and below cost '$SkillPointCost'."
    Assert-Condition ($SkillPointCost -gt 0) "Automatic-skill cost '$SkillPointCost' must be positive."
    $firstCast = ($SkillPointCost - $InitialSkillPoints) / $SkillPointsPerSecond
    $castInterval = $SkillPointCost / $SkillPointsPerSecond
    $times = [System.Collections.Generic.List[decimal]]::new()
    for ($castTime = $firstCast; $castTime -le $WindowSeconds; $castTime += $castInterval) {
        $times.Add($castTime)
    }
    return $times.ToArray()
}

function Get-DiscreteCycleOutputRatio {
    param(
        [Parameter(Mandatory = $true)]$Row,
        [Parameter(Mandatory = $true)][object[]]$Defenders,
        [Parameter(Mandatory = $true)][decimal]$WindowSeconds,
        [Parameter(Mandatory = $true)][decimal[]]$AttackCycleMultipliers,
        [Parameter(Mandatory = $true)][decimal[]]$AttackCycleTargetCounts
    )

    Assert-Condition ($WindowSeconds -gt 0) "Type ID $($Row.TypeId) has a nonpositive cycle window."
    Assert-Condition ($AttackCycleMultipliers.Count -gt 0 -and $AttackCycleMultipliers.Count -eq $AttackCycleTargetCounts.Count) "Type ID $($Row.TypeId) has inconsistent cycle data."
    $attackCount = [int][Math]::Floor([double]($WindowSeconds / [decimal]$Row.EffectiveAttackIntervalSeconds))
    $specialAttackCount = [int][Math]::Floor([double]($attackCount / $AttackCycleMultipliers.Count))
    if ($attackCount -eq 0) {
        return [pscustomobject]@{ Ratio = [decimal]1; AttackCount = 0; SpecialAttackCount = 0 }
    }
    $ordinaryAttackCount = $attackCount - $specialAttackCount
    $specialMultiplier = [decimal]$AttackCycleMultipliers[-1]
    $specialTargetCount = [decimal]$AttackCycleTargetCounts[-1]
    $baselineTotals = [System.Collections.Generic.List[decimal]]::new()
    $scenarioTotals = [System.Collections.Generic.List[decimal]]::new()
    foreach ($defender in $Defenders) {
        $ordinaryDamage = Get-OrdinaryAttackDamage -Attacker $Row -Defender $defender
        $specialAttacker = [pscustomobject]@{
            TypeId = $Row.TypeId
            DamageType = $Row.DamageType
            Attack = [decimal]$Row.Attack * $specialMultiplier
            EffectiveAttackIntervalSeconds = $Row.EffectiveAttackIntervalSeconds
        }
        $specialDamage = Get-OrdinaryAttackDamage -Attacker $specialAttacker -Defender $defender
        $baselineTotals.Add($ordinaryDamage * $attackCount)
        $scenarioTotals.Add(
            ($ordinaryDamage * $ordinaryAttackCount) +
            ($specialDamage * $specialAttackCount * $specialTargetCount)
        )
    }
    return [pscustomobject]@{
        Ratio = (Get-Median -Values $scenarioTotals.ToArray()) / (Get-Median -Values $baselineTotals.ToArray())
        AttackCount = $attackCount
        SpecialAttackCount = $specialAttackCount
    }
}

function Get-AttackStateOutputRatio {
    param(
        [Parameter(Mandatory = $true)]$Row,
        [Parameter(Mandatory = $true)][object[]]$Defenders,
        [Parameter(Mandatory = $true)][decimal]$BaselineMedianDps,
        [decimal]$AttackMultiplier = 1,
        [decimal]$AttackSpeedAdditive = 0,
        [decimal]$FinalAttackSpeedMultiplier = 1,
        [decimal]$TargetCount = 1,
        [decimal]$DefenseIgnoreFraction = 0,
        [decimal]$FlatAttackBonus = 0
    )

    Assert-Condition ($BaselineMedianDps -gt 0) "Type ID $($Row.TypeId) has no positive baseline DPS for an output scenario."
    Assert-Condition ($AttackMultiplier -ge 0 -and $TargetCount -ge 0) "Type ID $($Row.TypeId) has invalid attack-state multipliers."
    Assert-Condition ($DefenseIgnoreFraction -ge 0 -and $DefenseIgnoreFraction -le 1) "Type ID $($Row.TypeId) has invalid defense-ignore fraction '$DefenseIgnoreFraction'."
    $attacker = [pscustomobject]@{
        TypeId = $Row.TypeId
        DamageType = $Row.DamageType
        Attack = ([decimal]$Row.Attack * $AttackMultiplier) + $FlatAttackBonus
        EffectiveAttackIntervalSeconds = $Row.EffectiveAttackIntervalSeconds
    }
    $attackRateFactor = Get-AttackRateFactor -AttackSpeedAdditive $AttackSpeedAdditive -FinalAttackSpeedMultiplier $FinalAttackSpeedMultiplier
    $dpsValues = foreach ($defender in $Defenders) {
        $effectiveDefender = [pscustomobject]@{
            Defense = [decimal]$defender.Defense * ([decimal]1 - $DefenseIgnoreFraction)
            MagicResistance = $defender.MagicResistance
        }
        (Get-OrdinaryAttackDamage -Attacker $attacker -Defender $effectiveDefender) /
            [decimal]$attacker.EffectiveAttackIntervalSeconds *
            $attackRateFactor *
            $TargetCount
    }
    return (Get-Median -Values ([decimal[]]$dpsValues)) / $BaselineMedianDps
}

function Get-DefenseScenarioRatio {
    param(
        [Parameter(Mandatory = $true)]$Row,
        [Parameter(Mandatory = $true)][object[]]$Attackers,
        [Parameter(Mandatory = $true)][hashtable]$Parameters
    )

    $window = Get-MapDecimal -Map $Parameters -Key 'WindowSeconds' -Default 20
    Assert-Condition ($window -gt 0) "Type ID $($Row.TypeId) has a nonpositive scenario window."
    $activeStart = Get-MapDecimal -Map $Parameters -Key 'DefenseActiveStartSeconds' -Default 0
    $activeEnd = Get-MapDecimal -Map $Parameters -Key 'DefenseActiveEndSeconds' -Default $window
    $activeStart = [decimal][Math]::Max([double]0, [Math]::Min([double]$window, [double]$activeStart))
    $activeEnd = [decimal][Math]::Max([double]$activeStart, [Math]::Min([double]$window, [double]$activeEnd))
    $activeDuration = $activeEnd - $activeStart
    $inactiveDuration = $window - $activeDuration

    $defenseMultiplier = Get-MapDecimal -Map $Parameters -Key 'DefenseMultiplier' -Default 1
    $defenseBonus = Get-MapDecimal -Map $Parameters -Key 'DefenseBonus' -Default 0
    $mrBonus = Get-MapDecimal -Map $Parameters -Key 'MagicResistanceBonus' -Default 0
    $physicalTaken = Get-MapDecimal -Map $Parameters -Key 'PhysicalDamageTakenMultiplier' -Default 1
    $magicTaken = Get-MapDecimal -Map $Parameters -Key 'MagicDamageTakenMultiplier' -Default 1
    $physicalEvasion = Get-MapDecimal -Map $Parameters -Key 'PhysicalEvasionProbability' -Default 0
    $magicEvasion = Get-MapDecimal -Map $Parameters -Key 'MagicEvasionProbability' -Default 0
    foreach ($probability in @($physicalEvasion, $magicEvasion)) {
        Assert-Condition ($probability -ge 0 -and $probability -lt 1) "Type ID $($Row.TypeId) has invalid evasion probability '$probability'."
    }
    foreach ($multiplier in @($defenseMultiplier, $physicalTaken, $magicTaken)) {
        Assert-Condition ($multiplier -ge 0) "Type ID $($Row.TypeId) has negative defense multiplier '$multiplier'."
    }

    $baseDpsValues = [System.Collections.Generic.List[decimal]]::new()
    $scenarioDpsValues = [System.Collections.Generic.List[decimal]]::new()
    foreach ($attacker in $Attackers) {
        $interval = Get-EffectiveAttackInterval -Attacker $attacker
        $baseDamage = Get-OrdinaryAttackDamage -Attacker $attacker -Defender $Row
        $baseDps = $baseDamage / $interval
        $activeDefender = [pscustomobject]@{
            Defense = ([decimal]$Row.Defense * $defenseMultiplier) + $defenseBonus
            MagicResistance = [decimal]$Row.MagicResistance + $mrBonus
        }
        $activeDamage = Get-OrdinaryAttackDamage -Attacker $attacker -Defender $activeDefender
        switch ([string]$attacker.DamageType) {
            'Physical' { $activeDamage *= $physicalTaken * ([decimal]1 - $physicalEvasion) }
            'Magic' { $activeDamage *= $magicTaken * ([decimal]1 - $magicEvasion) }
        }
        $scenarioDps = (($baseDps * $inactiveDuration) + (($activeDamage / $interval) * $activeDuration)) / $window
        $baseDpsValues.Add($baseDps)
        $scenarioDpsValues.Add($scenarioDps)
    }
    $baseMedianDps = Get-Median -Values $baseDpsValues.ToArray()
    $scenarioMedianDps = Get-Median -Values $scenarioDpsValues.ToArray()
    Assert-Condition ($baseMedianDps -gt 0 -and $scenarioMedianDps -gt 0) "Type ID $($Row.TypeId) produced a nonpositive incoming-DPS scenario."

    $regen = Get-MapDecimal -Map $Parameters -Key 'RegenPerSecond' -Default 0
    $selfDamage = Get-MapDecimal -Map $Parameters -Key 'SelfDamagePerSecond' -Default 0
    $healFraction = Get-MapDecimal -Map $Parameters -Key 'HealFractionOfMaxHp' -Default 0
    $effectiveHitPoints = [decimal]$Row.MaxHitPoints + ($regen * $activeDuration) - ($selfDamage * $window) + ([decimal]$Row.MaxHitPoints * $healFraction)
    $effectiveHitPoints = [decimal][Math]::Max(0.000001, [double]$effectiveHitPoints)
    return ($effectiveHitPoints / $scenarioMedianDps) / ([decimal]$Row.MaxHitPoints / $baseMedianDps)
}

function Get-AuraPowerPerTarget {
    param(
        [Parameter(Mandatory = $true)][object[]]$Recipients,
        [Parameter(Mandatory = $true)][object[]]$Attackers,
        [Parameter(Mandatory = $true)][hashtable]$Parameters
    )

    $supportedAuraParameters = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($parameterName in @(
            'AuraAttackSpeedAdditive', 'AuraAttackMultiplier',
            'AuraDefenseBonus', 'AuraMagicResistanceBonus', 'AuraRegenPerSecond',
            'AuraTargetsLow', 'AuraTargetsMain', 'AuraTargetsHigh'
        )) {
        [void]$supportedAuraParameters.Add($parameterName)
    }
    foreach ($parameterName in $Parameters.Keys) {
        if ([string]$parameterName -like 'Aura*') {
            Assert-Condition ($supportedAuraParameters.Contains([string]$parameterName)) "Unsupported aura parameter '$parameterName'."
        }
    }

    if (Test-MapKey -Map $Parameters -Key 'AuraAttackSpeedAdditive') {
        foreach ($incompatibleParameterName in @('AuraAttackMultiplier', 'AuraDefenseBonus', 'AuraMagicResistanceBonus', 'AuraRegenPerSecond')) {
            Assert-Condition (-not (Test-MapKey -Map $Parameters -Key $incompatibleParameterName)) "AuraAttackSpeedAdditive cannot silently bypass '$incompatibleParameterName'."
        }
        $incomingRate = Get-AttackRateFactor -AttackSpeedAdditive ([decimal]$Parameters.AuraAttackSpeedAdditive) -FinalAttackSpeedMultiplier 1
        Assert-Condition ($incomingRate -gt 0) 'An aura that reduces final attack speed to zero needs a separate finite survival-window model.'
        return [decimal][Math]::Sqrt([double]([decimal]1 / $incomingRate)) - [decimal]1
    }

    $recipientParameters = @{ WindowSeconds = Get-MapDecimal -Map $Parameters -Key 'WindowSeconds' -Default 20 }
    $recipientAttackers = $Attackers
    if (Test-MapKey -Map $Parameters -Key 'AuraDefenseBonus') { $recipientParameters.DefenseBonus = [decimal]$Parameters.AuraDefenseBonus }
    if (Test-MapKey -Map $Parameters -Key 'AuraMagicResistanceBonus') {
        $recipientParameters.MagicResistanceBonus = [decimal]$Parameters.AuraMagicResistanceBonus
        if (-not (Test-MapKey -Map $Parameters -Key 'AuraDefenseBonus') -and -not (Test-MapKey -Map $Parameters -Key 'AuraRegenPerSecond')) {
            $recipientAttackers = @($Attackers | Where-Object DamageType -eq 'Magic')
        }
    }
    elseif ((Test-MapKey -Map $Parameters -Key 'AuraDefenseBonus') -and -not (Test-MapKey -Map $Parameters -Key 'AuraRegenPerSecond')) {
        $recipientAttackers = @($Attackers | Where-Object DamageType -eq 'Physical')
    }
    if (Test-MapKey -Map $Parameters -Key 'AuraRegenPerSecond') { $recipientParameters.RegenPerSecond = [decimal]$Parameters.AuraRegenPerSecond }
    $hasDefenseEffect = @(
        @('AuraDefenseBonus', 'AuraMagicResistanceBonus', 'AuraRegenPerSecond') |
            Where-Object { Test-MapKey -Map $Parameters -Key $_ }
    ).Count -gt 0
    if ($hasDefenseEffect) {
        Assert-Condition ($recipientAttackers.Count -gt 0) 'Aura recipient attacker sample is empty.'
    }
    $hasAttackEffect = Test-MapKey -Map $Parameters -Key 'AuraAttackMultiplier'
    $auraAttackMultiplier = if ($hasAttackEffect) { [decimal]$Parameters.AuraAttackMultiplier } else { [decimal]1 }
    Assert-Condition ($auraAttackMultiplier -gt 0) 'AuraAttackMultiplier must be positive.'
    $powerGains = foreach ($recipient in $Recipients) {
        $outputRatio = [decimal]1
        if ($hasAttackEffect) {
            $scenarioAttacker = [pscustomobject]@{
                TypeId = $recipient.TypeId
                DamageType = $recipient.DamageType
                Attack = [decimal]$recipient.Attack * $auraAttackMultiplier
                EffectiveAttackIntervalSeconds = Get-EffectiveAttackInterval -Attacker $recipient
            }
            $baselineDps = Get-Median -Values ([decimal[]]@($Recipients | ForEach-Object {
                        (Get-OrdinaryAttackDamage -Attacker $recipient -Defender $_) / (Get-EffectiveAttackInterval -Attacker $recipient)
                    }))
            $scenarioDps = Get-Median -Values ([decimal[]]@($Recipients | ForEach-Object {
                        (Get-OrdinaryAttackDamage -Attacker $scenarioAttacker -Defender $_) / (Get-EffectiveAttackInterval -Attacker $scenarioAttacker)
                    }))
            Assert-Condition ($baselineDps -gt 0 -and $scenarioDps -gt 0) "Aura recipient Type ID $($recipient.TypeId) produced nonpositive output power."
            $outputRatio = $scenarioDps / $baselineDps
        }
        $defenseRatio = if ($hasDefenseEffect) {
            Get-DefenseScenarioRatio -Row $recipient -Attackers $recipientAttackers -Parameters $recipientParameters
        }
        else {
            [decimal]1
        }
        [decimal][Math]::Max([double]0, [Math]::Sqrt([double]($outputRatio * $defenseRatio)) - 1)
    }
    return Get-Median -Values ([decimal[]]$powerGains)
}

function Get-PathPressure {
    param(
        [Parameter(Mandatory = $true)]$Row,
        [Parameter(Mandatory = $true)][object[]]$Attackers,
        [Parameter(Mandatory = $true)][decimal]$LifeDeductReference,
        [Parameter(Mandatory = $true)][decimal]$MoveSpeedReference,
        [decimal]$WindowSeconds = 20
    )

    Assert-Condition ($LifeDeductReference -gt 0 -and $MoveSpeedReference -gt 0 -and $WindowSeconds -gt 0) 'Path-pressure references must be positive.'
    $ttdValues = foreach ($attacker in $Attackers) {
        $damage = Get-OrdinaryAttackDamage -Attacker $attacker -Defender $Row
        [decimal][Math]::Ceiling([double]([decimal]$Row.MaxHitPoints / $damage)) * (Get-EffectiveAttackInterval -Attacker $attacker)
    }
    $survivalFraction = [decimal][Math]::Min([double]1, [double]((Get-Median -Values ([decimal[]]$ttdValues)) / $WindowSeconds))
    return ([decimal]$Row.LifeDeduct / $LifeDeductReference) *
        ([decimal]$Row.MoveSpeedMetresPerSecond / $MoveSpeedReference) *
        $survivalFraction
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
    if ([string]::IsNullOrWhiteSpace($AbilityInputPath)) {
        $AbilityInputPath = Join-Path $PSScriptRoot 'UnitCostAbilityInputs.psd1'
    }
    $AbilityInputPath = Resolve-ExistingPath $AbilityInputPath
    Assert-Condition ([System.IO.Path]::GetExtension($AbilityInputPath) -ieq '.psd1') "AbilityInputPath '$AbilityInputPath' must use the .psd1 extension."
    $OutputCsvPath = [System.IO.Path]::GetFullPath($OutputCsvPath)
    $AnalysisOutputPath = [System.IO.Path]::GetFullPath($AnalysisOutputPath)
    Assert-Condition (-not [string]::Equals($OutputCsvPath, $AnalysisOutputPath, [System.StringComparison]::OrdinalIgnoreCase)) 'OutputCsvPath and AnalysisOutputPath must be different files.'
    Assert-Condition (-not [string]::Equals($OutputCsvPath, $BondSpecPath, [System.StringComparison]::OrdinalIgnoreCase)) 'OutputCsvPath must not overwrite BondSpecPath.'
    Assert-Condition (-not [string]::Equals($AnalysisOutputPath, $BondSpecPath, [System.StringComparison]::OrdinalIgnoreCase)) 'AnalysisOutputPath must not overwrite BondSpecPath.'
    Assert-Condition (-not [string]::Equals($OutputCsvPath, $AbilityInputPath, [System.StringComparison]::OrdinalIgnoreCase)) 'OutputCsvPath must not overwrite AbilityInputPath.'
    Assert-Condition (-not [string]::Equals($AnalysisOutputPath, $AbilityInputPath, [System.StringComparison]::OrdinalIgnoreCase)) 'AnalysisOutputPath must not overwrite AbilityInputPath.'
    Assert-Condition (-not (Test-PathEqualOrUnderRoot -CandidatePath $OutputCsvPath -RootPath $StagingRoot)) 'OutputCsvPath must not equal or be contained by StagingRoot.'
    Assert-Condition (-not (Test-PathEqualOrUnderRoot -CandidatePath $AnalysisOutputPath -RootPath $StagingRoot)) 'AnalysisOutputPath must not equal or be contained by StagingRoot.'
    $abilityInput = Import-PowerShellDataFile -LiteralPath $AbilityInputPath
    Assert-Condition ($null -ne $abilityInput) "Ability input '$AbilityInputPath' is empty."
    Assert-Condition ((@($abilityInput.Keys | Sort-Object) -join '/') -ceq 'DamageTypeOverrides/ExplicitRiskOnly/UnitScenarios') "Ability input must contain exactly DamageTypeOverrides, UnitScenarios and ExplicitRiskOnly."

    $shopHeader = [string]::Concat('## ', [char]0x5546, [char]0x5E97, [char]0x5355, [char]0x4F4D, [char]0xFF08, '94', [char]0xFF09)
    $nonShopHeader = [string]::Concat('## ', [char]0x975E, [char]0x5546, [char]0x5E97, [char]0x5355, [char]0x4F4D, [char]0xFF08, '5', [char]0xFF09)
    $rarityLabel = [string]::Concat([char]0x7A00, [char]0x6709)
    $specLines = @(Get-Content -LiteralPath $BondSpecPath -Encoding UTF8)
    $shopTypeIds = @(Get-TypeIdsFromCodeBlock -Lines $specLines -Header $shopHeader -Label 'shop')
    $nonShopTypeIds = @(Get-TypeIdsFromCodeBlock -Lines $specLines -Header $nonShopHeader -Label 'non-shop')
    Assert-Condition ($shopTypeIds.Count -eq 94) "Expected 94 shop TypeIds; found $($shopTypeIds.Count)."
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
    $validAbilityTypeIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($typeId in @($shopTypeIds + $nonShopTypeIds)) {
        [void]$validAbilityTypeIds.Add($typeId)
    }
    Assert-Condition ($validAbilityTypeIds.Count -eq 99) "Expected 99 valid shop/non-shop ability TypeIds; found $($validAbilityTypeIds.Count)."
    $scenarioTypeIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($scenarioKey in $abilityInput.UnitScenarios.Keys) {
        $typeId = [string]$scenarioKey
        Assert-Condition ($validAbilityTypeIds.Contains($typeId)) "UnitScenarios contains invalid TypeId '$typeId'."
        Assert-Condition ($scenarioTypeIds.Add($typeId)) "UnitScenarios contains duplicate TypeId '$typeId'."
        $scenario = $abilityInput.UnitScenarios[$scenarioKey]
        Assert-Condition ([int]$scenario.TypeId -eq [int]$typeId) "UnitScenarios key '$typeId' conflicts with embedded TypeId '$($scenario.TypeId)'."
        Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$scenario.ModelKind)) "Unit scenario '$typeId' is missing ModelKind."
        Assert-Condition ($scenario.Parameters -is [hashtable]) "Unit scenario '$typeId' Parameters must be a hashtable."
        Assert-Condition (@($scenario.Evidence).Count -gt 0) "Unit scenario '$typeId' is missing Evidence."
        Assert-Condition ($null -ne $scenario.UnquantifiedRisk) "Unit scenario '$typeId' is missing UnquantifiedRisk."
        foreach ($referenceTypeId in @(
                if ($scenario.Parameters.ContainsKey('SummonTypeId')) { [string]$scenario.Parameters.SummonTypeId }
                if ($scenario.Parameters.ContainsKey('RandomSummons')) { @($scenario.Parameters.RandomSummons | ForEach-Object { [string]$_.TypeId }) }
            )) {
            Assert-Condition ($validAbilityTypeIds.Contains($referenceTypeId)) "Unit scenario '$typeId' references invalid TypeId '$referenceTypeId'."
        }
    }
    $riskTypeIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($riskKey in $abilityInput.ExplicitRiskOnly.Keys) {
        $typeId = [string]$riskKey
        Assert-Condition ($validAbilityTypeIds.Contains($typeId)) "ExplicitRiskOnly contains invalid TypeId '$typeId'."
        Assert-Condition ($riskTypeIds.Add($typeId)) "ExplicitRiskOnly contains duplicate TypeId '$typeId'."
        Assert-Condition (@($abilityInput.ExplicitRiskOnly[$riskKey]).Count -gt 0) "ExplicitRiskOnly '$typeId' has no risk text."
    }
    foreach ($overrideKey in $abilityInput.DamageTypeOverrides.Keys) {
        Assert-Condition ($validAbilityTypeIds.Contains([string]$overrideKey)) "DamageTypeOverrides contains invalid TypeId '$overrideKey'."
    }
    $abilityMarkerPattern = '(?:\u80fd\u529b\u63cf\u8ff0|\u80fd\u529b\u8be6\u60c5|\u80fd\u529b\uff1a)'
    foreach ($line in $specLines) {
        if ($line -match ('^(?<TypeId>\d+)\s+.+?' + $abilityMarkerPattern) -and $shopSet.Contains($Matches.TypeId)) {
            Assert-Condition ($scenarioTypeIds.Contains($Matches.TypeId) -or $riskTypeIds.Contains($Matches.TypeId)) "BONDS ability TypeId '$($Matches.TypeId)' is absent from UnitScenarios and ExplicitRiskOnly."
        }
    }
    foreach ($typeId in @('1058', '1078', '1080', '1081', '1083', '1095', '1281', '1502')) {
        Assert-Condition ($shopSet.Contains($typeId)) "Required unmarked ability TypeId '$typeId' is not a shop unit."
        Assert-Condition ($scenarioTypeIds.Contains($typeId) -or $riskTypeIds.Contains($typeId)) "Required unmarked ability TypeId '$typeId' is absent from UnitScenarios and ExplicitRiskOnly."
    }

    $unitPattern = '^(?<TypeId>\d+)\s+(?<DisplayName>.+?)\s+' + $rarityLabel + '(?<Rarity>[1-6])(?:[。\.\s]|$)'
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

    $expectedRarityCounts = @{ 1 = 9; 2 = 20; 3 = 14; 4 = 24; 5 = 21; 6 = 6 }
    foreach ($rarity in 1..6) {
        $count = @($definitions.Values | Where-Object { $_.Rarity -eq $rarity }).Count
        Assert-Condition ($count -eq $expectedRarityCounts[$rarity]) "Expected R$rarity=$($expectedRarityCounts[$rarity]); found $count."
    }

    $directories = @(Get-ChildItem -LiteralPath $StagingRoot -Directory)
    $directoryNames = [string[]]@($directories.Name)
    $actuallyReadResourceDirectories = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $damageTypeOverrides = $abilityInput.DamageTypeOverrides
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
        [void]$actuallyReadResourceDirectories.Add($resourceDirectory.Name)

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
    Assert-Condition ($rows.Count -eq 94) "Expected 94 exported rows; found $($rows.Count)."
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

    $powerByTypeId = @{}
    foreach ($row in $rows | Where-Object { $null -ne $_.PanelPower }) {
        $powerByTypeId[[int]$row.TypeId] = [decimal]$row.PanelPower
    }
    foreach ($typeId in $nonShopTypeIds) {
        $resourceDirectory = Get-OnlyDirectory -Directories $directories -TypeId $typeId
        [void]$actuallyReadResourceDirectories.Add($resourceDirectory.Name)
        $levelsDocument = Get-Content -LiteralPath (Join-Path $resourceDirectory.FullName 'unit-levels.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        $sourceDocument = Get-Content -LiteralPath (Join-Path $resourceDirectory.FullName 'unit-source-v1.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        $levelZero = Get-RequiredLevelZero -LevelsDocument $levelsDocument -DirectoryName $resourceDirectory.Name
        $damageType = [string]$sourceDocument.damageType
        if ($typeId -ceq '10002' -or [string]::IsNullOrWhiteSpace($damageType) -or $damageType -ceq 'None') {
            $powerByTypeId[[int]$typeId] = [decimal]0
            continue
        }
        Assert-Condition ($allowedDamageTypes.Contains($damageType)) "Referenced non-shop TypeId '$typeId' has invalid damageType '$damageType'."
        $referenceRow = [pscustomobject]@{
            TypeId = [int]$typeId
            DamageType = $damageType
            MaxHitPoints = $levelZero.MaxHitPoints
            Attack = $levelZero.Attack
            Defense = $levelZero.Defense
            MagicResistance = $levelZero.MagicResistance
            EffectiveAttackIntervalSeconds = $levelZero.AttackIntervalSeconds * [decimal]0.5
        }
        $referenceDpsValues = foreach ($defender in $defenderRows) {
            (Get-OrdinaryAttackDamage -Attacker $referenceRow -Defender $defender) / (Get-EffectiveAttackInterval -Attacker $referenceRow)
        }
        $referenceTtdValues = foreach ($attacker in $attackingRows) {
            $damage = Get-OrdinaryAttackDamage -Attacker $attacker -Defender $referenceRow
            [decimal][Math]::Ceiling([double]([decimal]$referenceRow.MaxHitPoints / $damage)) * (Get-EffectiveAttackInterval -Attacker $attacker)
        }
        $referenceDps = Get-Median -Values ([decimal[]]$referenceDpsValues)
        $referenceTtd = Get-Median -Values ([decimal[]]$referenceTtdValues)
        $referenceDpsWinsorized = (Get-ClampedCombatValues -Values ([decimal[]]@($referenceDps)) -LowerBound $dpsP5 -UpperBound $dpsP95)[0]
        $referenceTtdWinsorized = (Get-ClampedCombatValues -Values ([decimal[]]@($referenceTtd)) -LowerBound $ttdP5 -UpperBound $ttdP95)[0]
        $powerByTypeId[[int]$typeId] = Get-GeometricCombinedValue -Output ($referenceDpsWinsorized / $outputReference) -Defense ($referenceTtdWinsorized / $defenseReference)
    }

    $lifeDeductReference = Get-Median -Values ([decimal[]]@($rows | Where-Object { $_.LifeDeduct -gt 0 } | ForEach-Object { [decimal]$_.LifeDeduct }))
    $moveSpeedReference = Get-Median -Values ([decimal[]]@($rows | Where-Object { $_.MoveSpeedMetresPerSecond -gt 0 } | ForEach-Object { [decimal]$_.MoveSpeedMetresPerSecond }))
    $abilityRows = foreach ($row in $rows) {
        $typeIdKey = [string]$row.TypeId
        $scenario = if ($abilityInput.UnitScenarios.ContainsKey($typeIdKey)) { $abilityInput.UnitScenarios[$typeIdKey] } else { $null }
        $parameters = if ($null -ne $scenario) { [hashtable]$scenario.Parameters } else { @{} }
        $window = Get-MapDecimal -Map $parameters -Key 'WindowSeconds' -Default 20
        $outputLow = [decimal]1
        $outputMain = [decimal]1
        $outputHigh = [decimal]1
        $defenseMain = [decimal]1
        $equivalentLow = [decimal]0
        $equivalentMain = [decimal]0
        $equivalentHigh = [decimal]0
        $scenarioEventTimes = [System.Collections.Generic.List[decimal]]::new()
        $scenarioAttackCount = $null
        $scenarioSpecialAttackCount = $null
        $baselineDps = if ($rawDpsMediansByTypeId.ContainsKey([int]$row.TypeId)) { [decimal]$rawDpsMediansByTypeId[[int]$row.TypeId] } else { [decimal]0 }

        if ($null -ne $scenario -and $baselineDps -gt 0) {
            if (Test-MapKey -Map $parameters -Key 'OutputSegments') {
                $outputLow = 0
                $outputMain = 0
                $outputHigh = 0
                $totalDuration = [decimal]0
                foreach ($segment in @($parameters.OutputSegments)) {
                    $duration = [decimal]$segment.DurationSeconds
                    Assert-Condition ($duration -ge 0) "Type ID $($row.TypeId) has a negative output-segment duration."
                    $attackMultiplier = if ($segment.ContainsKey('AttackMultiplier')) { [decimal]$segment.AttackMultiplier } else { [decimal]1 }
                    $attackSpeedAdditive = if ($segment.ContainsKey('AttackSpeedAdditive')) { [decimal]$segment.AttackSpeedAdditive } else { [decimal]0 }
                    $finalAttackSpeedMultiplier = if ($segment.ContainsKey('FinalAttackSpeedMultiplier')) { [decimal]$segment.FinalAttackSpeedMultiplier } else { [decimal]1 }
                    $targetLow = if ($segment.ContainsKey('TargetCountLow')) { [decimal]$segment.TargetCountLow } else { [decimal]1 }
                    $targetMain = if ($segment.ContainsKey('TargetCountMain')) { [decimal]$segment.TargetCountMain } else { $targetLow }
                    $targetHigh = if ($segment.ContainsKey('TargetCountHigh')) { [decimal]$segment.TargetCountHigh } else { $targetMain }
                    $outputLow += $duration * (Get-AttackStateOutputRatio -Row $row -Defenders $defenderRows -BaselineMedianDps $baselineDps -AttackMultiplier $attackMultiplier -AttackSpeedAdditive $attackSpeedAdditive -FinalAttackSpeedMultiplier $finalAttackSpeedMultiplier -TargetCount $targetLow)
                    $outputMain += $duration * (Get-AttackStateOutputRatio -Row $row -Defenders $defenderRows -BaselineMedianDps $baselineDps -AttackMultiplier $attackMultiplier -AttackSpeedAdditive $attackSpeedAdditive -FinalAttackSpeedMultiplier $finalAttackSpeedMultiplier -TargetCount $targetMain)
                    $outputHigh += $duration * (Get-AttackStateOutputRatio -Row $row -Defenders $defenderRows -BaselineMedianDps $baselineDps -AttackMultiplier $attackMultiplier -AttackSpeedAdditive $attackSpeedAdditive -FinalAttackSpeedMultiplier $finalAttackSpeedMultiplier -TargetCount $targetHigh)
                    $totalDuration += $duration
                }
                Assert-Condition ($totalDuration -eq $window) "Type ID $($row.TypeId) output segments cover '$totalDuration' seconds, expected '$window'."
                $outputLow /= $window
                $outputMain /= $window
                $outputHigh /= $window
            }
            if ($scenario.ModelKind -ceq 'OpeningHitsThenSteady20Seconds') {
                $openingCount = [decimal]$parameters.OpeningAttackCount
                $openingSpeedAdditive = [decimal]$parameters.OpeningAttackSpeedAdditive
                $openingAttackRate = Get-AttackRateFactor -AttackSpeedAdditive $openingSpeedAdditive -FinalAttackSpeedMultiplier 1
                Assert-Condition ($openingAttackRate -gt 0) "Type ID $($row.TypeId) opening state cannot reach its release attack count at zero attack speed."
                $openingDuration = $openingCount * [decimal]$row.EffectiveAttackIntervalSeconds / $openingAttackRate
                $openingDuration = [decimal][Math]::Min([double]$window, [double]$openingDuration)
                $steadyDuration = $window - $openingDuration
                $openingRatio = Get-AttackStateOutputRatio -Row $row -Defenders $defenderRows -BaselineMedianDps $baselineDps -AttackSpeedAdditive $openingSpeedAdditive
                $steadyIgnore = Get-MapDecimal -Map $parameters -Key 'SteadyDefenseIgnoreFraction' -Default 0
                $steadyRatio = Get-AttackStateOutputRatio -Row $row -Defenders $defenderRows -BaselineMedianDps $baselineDps -AttackMultiplier ([decimal]$parameters.SteadyAttackMultiplier) -DefenseIgnoreFraction $steadyIgnore
                $outputLow = $outputMain = $outputHigh = (($openingRatio * $openingDuration) + ($steadyRatio * $steadyDuration)) / $window
            }
            if (Test-MapKey -Map $parameters -Key 'AttackCycleMultipliers') {
                $multipliers = [decimal[]]@($parameters.AttackCycleMultipliers)
                foreach ($scenarioName in @('Low', 'Main', 'High')) {
                    $targetKey = 'AttackCycleTargetCounts' + $scenarioName
                    $targetCounts = if ($parameters.ContainsKey($targetKey)) { [decimal[]]@($parameters[$targetKey]) } else { [decimal[]]@(for ($index = 0; $index -lt $multipliers.Count; $index++) { 1 }) }
                    $cycleResult = Get-DiscreteCycleOutputRatio -Row $row -Defenders $defenderRows -WindowSeconds $window -AttackCycleMultipliers $multipliers -AttackCycleTargetCounts $targetCounts
                    Set-Variable -Name ('output' + $scenarioName) -Value $cycleResult.Ratio
                    $scenarioAttackCount = $cycleResult.AttackCount
                    $scenarioSpecialAttackCount = $cycleResult.SpecialAttackCount
                }
            }
            if (Test-MapKey -Map $parameters -Key 'FirstAttackMultiplier') {
                $attackCount = [decimal][Math]::Max([double]1, [Math]::Floor([double]($window / [decimal]$row.EffectiveAttackIntervalSeconds)))
                $firstRatio = Get-AttackStateOutputRatio -Row $row -Defenders $defenderRows -BaselineMedianDps $baselineDps -AttackMultiplier ([decimal]$parameters.FirstAttackMultiplier)
                $outputLow = $outputMain = $outputHigh = (($attackCount - 1) + $firstRatio) / $attackCount
            }
            if (Test-MapKey -Map $parameters -Key 'RampAttackFlatPerStack') {
                $stackCount = [decimal][Math]::Min([double][decimal]$parameters.RampMaxStacks, [Math]::Floor([double]([decimal]$row.EffectiveAttackIntervalSeconds / [decimal]$parameters.RampCheckIntervalSeconds)))
                $flatBonus = $stackCount * [decimal]$parameters.RampAttackFlatPerStack
                $outputLow = $outputMain = $outputHigh = Get-AttackStateOutputRatio -Row $row -Defenders $defenderRows -BaselineMedianDps $baselineDps -FlatAttackBonus $flatBonus
            }
            if (Test-MapKey -Map $parameters -Key 'OutputTargetCountLow') {
                $outputLow = Get-AttackStateOutputRatio -Row $row -Defenders $defenderRows -BaselineMedianDps $baselineDps -TargetCount ([decimal]$parameters.OutputTargetCountLow)
                $outputMain = Get-AttackStateOutputRatio -Row $row -Defenders $defenderRows -BaselineMedianDps $baselineDps -TargetCount ([decimal]$parameters.OutputTargetCountMain)
                $outputHigh = Get-AttackStateOutputRatio -Row $row -Defenders $defenderRows -BaselineMedianDps $baselineDps -TargetCount ([decimal]$parameters.OutputTargetCountHigh)
            }
            if (Test-MapKey -Map $parameters -Key 'SelfDamagePerSecond') {
                $uptime = [decimal][Math]::Min([double]1, [double]([decimal]$row.MaxHitPoints / ([decimal]$parameters.SelfDamagePerSecond * $window)))
                $outputLow *= $uptime
                $outputMain *= $uptime
                $outputHigh *= $uptime
            }
            if (Test-MapKey -Map $parameters -Key 'BurstAttackMultiplier') {
                $deathAtSeconds = if ($parameters.ContainsKey('DeathAtSeconds')) {
                    [decimal]$parameters.DeathAtSeconds
                }
                elseif ($parameters.ContainsKey('SelfDamagePerSecond')) {
                    [decimal]$row.MaxHitPoints / [decimal]$parameters.SelfDamagePerSecond
                }
                else {
                    throw "Type ID $($row.TypeId) burst has no auditable death-time input."
                }
                $burstResolutionSeconds = $deathAtSeconds + [decimal]$parameters.BurstDelaySeconds
                if ($burstResolutionSeconds -le $window) {
                    $scenarioEventTimes.Add($burstResolutionSeconds)
                    $burstAttacker = [pscustomobject]@{
                        TypeId = $row.TypeId
                        DamageType = if ($parameters.ContainsKey('BurstDamageType')) { [string]$parameters.BurstDamageType } else { [string]$row.DamageType }
                        Attack = [decimal]$row.Attack * [decimal]$parameters.BurstAttackMultiplier
                        EffectiveAttackIntervalSeconds = 1
                    }
                    $burstDamage = Get-Median -Values ([decimal[]]@($defenderRows | ForEach-Object { Get-OrdinaryAttackDamage -Attacker $burstAttacker -Defender $_ }))
                    $outputLow += ($burstDamage * [decimal]$parameters.BurstTargetsLow / $window) / $baselineDps
                    $outputMain += ($burstDamage * [decimal]$parameters.BurstTargetsMain / $window) / $baselineDps
                    $outputHigh += ($burstDamage * [decimal]$parameters.BurstTargetsHigh / $window) / $baselineDps
                }
            }
            if (Test-MapKey -Map $parameters -Key 'CounterMagicDamagePerHit') {
                $counterAttacker = [pscustomobject]@{ TypeId = $row.TypeId; DamageType = 'Magic'; Attack = [decimal]$parameters.CounterMagicDamagePerHit; EffectiveAttackIntervalSeconds = 1 }
                $counterDps = (Get-Median -Values ([decimal[]]@($defenderRows | ForEach-Object { Get-OrdinaryAttackDamage -Attacker $counterAttacker -Defender $_ }))) * [decimal]$parameters.IncomingHitsPerSecond
                $counterRatio = $counterDps / $baselineDps
                $outputLow += $counterRatio
                $outputMain += $counterRatio
                $outputHigh += $counterRatio
            }
        }

        $defenseKeys = @(
            'DefenseMultiplier', 'DefenseBonus', 'MagicResistanceBonus',
            'PhysicalDamageTakenMultiplier', 'MagicDamageTakenMultiplier',
            'PhysicalEvasionProbability', 'MagicEvasionProbability',
            'RegenPerSecond', 'SelfDamagePerSecond', 'HealFractionOfMaxHp'
        )
        if ($null -ne $scenario -and @($defenseKeys | Where-Object { $parameters.ContainsKey($_) }).Count -gt 0 -and -not $unattackableDroneTypeIds.Contains([int]$row.TypeId)) {
            $defenseMain = Get-DefenseScenarioRatio -Row $row -Attackers $attackingRows -Parameters $parameters
        }

        if ($null -ne $scenario -and $scenario.ModelKind -match 'SupportAura|Aura') {
            $perTarget = Get-AuraPowerPerTarget -Recipients $defenderRows -Attackers $attackingRows -Parameters $parameters
            $equivalentLow = $perTarget * [decimal]$parameters.AuraTargetsLow
            $equivalentMain = $perTarget * [decimal]$parameters.AuraTargetsMain
            $equivalentHigh = $perTarget * [decimal]$parameters.AuraTargetsHigh
        }
        if ($null -ne $scenario -and $scenario.ModelKind -ceq 'PathPressure') {
            $pathPressure = Get-PathPressure -Row $row -Attackers $attackingRows -LifeDeductReference $lifeDeductReference -MoveSpeedReference $moveSpeedReference -WindowSeconds $window
            $equivalentLow = $equivalentMain = $equivalentHigh = $pathPressure
        }
        if ($null -ne $scenario -and $parameters.ContainsKey('SummonTypeId')) {
            $summonPower = [decimal]$powerByTypeId[[int]$parameters.SummonTypeId]
            if ($parameters.ContainsKey('SkillPointsPerSecond')) {
                $spawnTimes = @(Get-AutomaticSkillCastTimes `
                        -WindowSeconds $window `
                        -SkillPointsPerSecond ([decimal]$parameters.SkillPointsPerSecond) `
                        -InitialSkillPoints ([decimal]$parameters.InitialSkillPoints) `
                        -SkillPointCost ([decimal]$parameters.SkillPointCost))
                foreach ($spawnTime in $spawnTimes) {
                    $scenarioEventTimes.Add([decimal]$spawnTime)
                    $contribution = $summonPower * [decimal]$parameters.SummonCountPerCast * [decimal][Math]::Max([double]0, [double](($window - [decimal]$spawnTime) / $window))
                    $equivalentMain += $contribution
                }
            }
            elseif ($scenario.ModelKind -ceq 'AttackCycleSummon20Seconds') {
                $spawnInterval = [decimal]$parameters.AttacksPerSummon * [decimal]$row.EffectiveAttackIntervalSeconds
                for ($spawnTime = $spawnInterval; $spawnTime -le $window; $spawnTime += $spawnInterval) {
                    $scenarioEventTimes.Add($spawnTime)
                    $equivalentMain += $summonPower * (($window - $spawnTime) / $window)
                }
            }
            else {
                $summonCount = [decimal]$parameters.SummonCount
                $spawnTime = [decimal]$parameters.SummonAtSeconds
                $scenarioEventTimes.Add($spawnTime)
                $equivalentMain += $summonPower * $summonCount * (($window - $spawnTime) / $window)
            }
            $equivalentLow = $equivalentHigh = $equivalentMain
        }
        if ($null -ne $scenario -and $parameters.ContainsKey('RandomSummons')) {
            $scenarioEventTimes.Add([decimal]$parameters.SummonAtSeconds)
            foreach ($randomSummon in @($parameters.RandomSummons)) {
                $equivalentMain += [decimal]$powerByTypeId[[int]$randomSummon.TypeId] * [decimal]$randomSummon.Probability * (($window - [decimal]$parameters.SummonAtSeconds) / $window)
            }
            $equivalentLow = $equivalentHigh = $equivalentMain
        }

        foreach ($value in @($outputLow, $outputMain, $outputHigh, $defenseMain, $equivalentLow, $equivalentMain, $equivalentHigh)) {
            Assert-Condition (-not [double]::IsNaN([double]$value) -and -not [double]::IsInfinity([double]$value) -and $value -ge 0) "Type ID $($row.TypeId) has an invalid ability scenario component '$value'."
        }
        Assert-Condition ($outputMain -gt 0 -and $defenseMain -gt 0) "Type ID $($row.TypeId) has a nonpositive main ability multiplier."
        $abilityPowerMultiplier = Get-GeometricCombinedValue -Output $outputMain -Defense $defenseMain
        $continuousPower = if ($null -ne $row.PanelPower) {
            ([decimal]$row.PanelPower * $abilityPowerMultiplier) + $equivalentMain
        }
        else {
            $equivalentMain
        }
        Assert-Condition ($continuousPower -gt 0) "Type ID $($row.TypeId) has nonpositive ContinuousPower '$continuousPower'."
        $evidence = if ($null -ne $scenario) { @($scenario.Evidence) -join ' | ' } else { 'No quantified ability scenario; base panel only.' }
        $riskText = [System.Collections.Generic.List[string]]::new()
        if ($null -ne $scenario) {
            foreach ($risk in @($scenario.UnquantifiedRisk)) {
                if (-not [string]::IsNullOrWhiteSpace([string]$risk) -and -not $riskText.Contains([string]$risk)) { $riskText.Add([string]$risk) }
            }
        }
        if ($abilityInput.ExplicitRiskOnly.ContainsKey($typeIdKey)) {
            foreach ($risk in @($abilityInput.ExplicitRiskOnly[$typeIdKey])) {
                if (-not [string]::IsNullOrWhiteSpace([string]$risk) -and -not $riskText.Contains([string]$risk)) { $riskText.Add([string]$risk) }
            }
        }

        $properties = [ordered]@{}
        foreach ($property in $row.PSObject.Properties) {
            $properties[$property.Name] = $property.Value
        }
        $properties.AbilityModelKind = if ($null -ne $scenario) { [string]$scenario.ModelKind } elseif ($riskText.Count -gt 0) { 'ExplicitRiskOnly' } else { 'None' }
        $properties.OutputScenarioLow = $outputLow
        $properties.OutputScenarioMain = $outputMain
        $properties.OutputScenarioHigh = $outputHigh
        $properties.DefenseScenarioMain = $defenseMain
        $properties.EquivalentEntityContributionLow = $equivalentLow
        $properties.EquivalentEntityContribution = $equivalentMain
        $properties.EquivalentEntityContributionHigh = $equivalentHigh
        $properties.AbilityPowerMultiplier = $abilityPowerMultiplier
        $properties.ContinuousPower = $continuousPower
        $properties.AbilityEvidence = $evidence
        $properties.RiskFlags = $riskText -join ' | '
        $properties.ScenarioEventCount = $scenarioEventTimes.Count
        $properties.ScenarioEventTimesSeconds = @($scenarioEventTimes | ForEach-Object { $_.ToString('0.############################', [System.Globalization.CultureInfo]::InvariantCulture) }) -join '/'
        $properties.ScenarioAttackCount = $scenarioAttackCount
        $properties.ScenarioSpecialAttackCount = $scenarioSpecialAttackCount
        [pscustomobject]$properties
    }
    $costCurveModel = Get-CostCurveModel -Rows @($abilityRows)
    $r1CalibrationModel = Get-R1CalibrationModel -Rows @($costCurveModel.Rows)
    $rows = @($r1CalibrationModel.Rows)

    $eliteEvidence = [System.Collections.Generic.List[object]]::new()
    $eliteRows = foreach ($row in $rows) {
        $typeId = [string]$row.TypeId
        $resourcePlan = Get-EliteResourcePlan `
            -DirectoryNames $directoryNames `
            -TypeId $typeId `
            -E0ResourceDirectory ([string]$row.ResourceDirectory)
        $e0PanelPower = if ($null -ne $row.PanelPower) { [Nullable[decimal]]([decimal]$row.PanelPower) } else { $null }
        $elite2PanelPower = $e0PanelPower
        if ($resourcePlan.Elite2IsDedicated) {
            $elite2Directory = Get-DirectoryByName -Directories $directories -Name $resourcePlan.Elite2ResourceDirectory
            [void]$actuallyReadResourceDirectories.Add($elite2Directory.Name)
            $elite2Record = Get-ResourceCombatRecord `
                -ResourceDirectory $elite2Directory `
                -TypeId $typeId `
                -DamageTypeOverrides $damageTypeOverrides `
                -AllowedDamageTypes $allowedDamageTypes
            if ($null -ne $e0PanelPower) {
                $elite2PanelMetrics = Get-CombatPanelMetrics `
                    -CombatRecord $elite2Record `
                    -Defenders $defenderRows `
                    -Attackers $attackingRows `
                    -DpsLowerBound $dpsP5 `
                    -DpsUpperBound $dpsP95 `
                    -TtdLowerBound $ttdP5 `
                    -TtdUpperBound $ttdP95 `
                    -OutputReference $outputReference `
                    -DefenseReference $defenseReference
                $elite2PanelPower = [Nullable[decimal]]([decimal]$elite2PanelMetrics.PanelPower)
            }
            else {
                $elite2PanelPower = $null
            }
            $eliteEvidence.Add([pscustomobject][ordered]@{
                    TypeId = [int]$typeId
                    EliteLevel = 2
                    ResourceDirectory = $elite2Directory.Name
                    PanelPower = $elite2PanelPower
                    PanelPowerStatus = if ($null -ne $elite2PanelPower) { 'RecomputedFromDedicatedLevel0' } else { 'NotApplicableE0HasNoPanelPower' }
                })
        }

        $elite3PanelPower = $elite2PanelPower
        if ($resourcePlan.Elite3IsDedicated) {
            $elite3Directory = Get-DirectoryByName -Directories $directories -Name $resourcePlan.Elite3ResourceDirectory
            [void]$actuallyReadResourceDirectories.Add($elite3Directory.Name)
            $elite3Record = Get-ResourceCombatRecord `
                -ResourceDirectory $elite3Directory `
                -TypeId $typeId `
                -DamageTypeOverrides $damageTypeOverrides `
                -AllowedDamageTypes $allowedDamageTypes
            if ($null -ne $e0PanelPower) {
                $elite3PanelMetrics = Get-CombatPanelMetrics `
                    -CombatRecord $elite3Record `
                    -Defenders $defenderRows `
                    -Attackers $attackingRows `
                    -DpsLowerBound $dpsP5 `
                    -DpsUpperBound $dpsP95 `
                    -TtdLowerBound $ttdP5 `
                    -TtdUpperBound $ttdP95 `
                    -OutputReference $outputReference `
                    -DefenseReference $defenseReference
                $elite3PanelPower = [Nullable[decimal]]([decimal]$elite3PanelMetrics.PanelPower)
            }
            else {
                $elite3PanelPower = $null
            }
            $eliteEvidence.Add([pscustomobject][ordered]@{
                    TypeId = [int]$typeId
                    EliteLevel = 3
                    ResourceDirectory = $elite3Directory.Name
                    PanelPower = $elite3PanelPower
                    PanelPowerStatus = if ($null -ne $elite3PanelPower) { 'RecomputedFromDedicatedLevel0' } else { 'NotApplicableE0HasNoPanelPower' }
                })
        }

        $elite1 = Get-EliteStageCalculation `
            -EliteLevel 1 `
            -E0ContinuousPower ([decimal]$row.ContinuousPower) `
            -E0Cost ([int]$row.FinalBaseCost) `
            -E0PanelPower $e0PanelPower `
            -StagePanelPower $e0PanelPower `
            -AbilityPowerMultiplier ([decimal]$row.AbilityPowerMultiplier) `
            -EquivalentEntityContribution ([decimal]$row.EquivalentEntityContribution) `
            -ResourceSource 'NoDedicatedPanel'
        $elite2 = Get-EliteStageCalculation `
            -EliteLevel 2 `
            -E0ContinuousPower ([decimal]$row.ContinuousPower) `
            -E0Cost ([int]$row.FinalBaseCost) `
            -E0PanelPower $e0PanelPower `
            -StagePanelPower $elite2PanelPower `
            -AbilityPowerMultiplier ([decimal]$row.AbilityPowerMultiplier) `
            -EquivalentEntityContribution ([decimal]$row.EquivalentEntityContribution) `
            -ResourceSource $resourcePlan.Elite2ResourceSource
        $elite3 = Get-EliteStageCalculation `
            -EliteLevel 3 `
            -E0ContinuousPower ([decimal]$row.ContinuousPower) `
            -E0Cost ([int]$row.FinalBaseCost) `
            -E0PanelPower $e0PanelPower `
            -StagePanelPower $elite3PanelPower `
            -AbilityPowerMultiplier ([decimal]$row.AbilityPowerMultiplier) `
            -EquivalentEntityContribution ([decimal]$row.EquivalentEntityContribution) `
            -ResourceSource $resourcePlan.Elite3ResourceSource

        $eliteRisks = [System.Collections.Generic.List[string]]::new()
        $e0Efficiency = [decimal]$row.ContinuousPower / [decimal]$row.FinalBaseCost
        foreach ($stageAudit in @(
                [pscustomobject]@{ EliteLevel = 2; IsDedicated = [bool]$resourcePlan.Elite2IsDedicated; Calculation = $elite2 },
                [pscustomobject]@{ EliteLevel = 3; IsDedicated = [bool]$resourcePlan.Elite3IsDedicated; Calculation = $elite3 }
            )) {
            if (-not $stageAudit.IsDedicated) {
                continue
            }
            if ($null -eq $e0PanelPower) {
                $eliteRisks.Add("E$($stageAudit.EliteLevel) has a dedicated resource but E0 has no PanelPower; E0 ContinuousPower is inherited.")
                continue
            }
            $stageEfficiency = [decimal]$stageAudit.Calculation.PowerPerCostRatio
            $deltaPercent = (($stageEfficiency / $e0Efficiency) - [decimal]1) * [decimal]100
            if ($deltaPercent -ne 0 -and -not [double]::IsNaN([double]$deltaPercent) -and -not [double]::IsInfinity([double]$deltaPercent)) {
                $formattedDelta = $deltaPercent.ToString('+0.############################;-0.############################;0', [System.Globalization.CultureInfo]::InvariantCulture)
                $eliteRisks.Add("E$($stageAudit.EliteLevel) dedicated variant efficiency differs from E0 by $formattedDelta%; C0 is unchanged.")
            }
        }
        if ($typeId -in @('1025', '1131', '1132')) {
            $eliteRisks.Add('E2 ability definition from BONDS is not separately remodeled; the E0 ability contribution is reused without an invented numeric modifier.')
        }

        $properties = [ordered]@{}
        foreach ($property in $row.PSObject.Properties) {
            $properties[$property.Name] = $property.Value
        }
        $properties.Elite1EntityCount = $elite1.EntityCount
        $properties.Elite1TotalPower = $elite1.TotalPower
        $properties.Elite1TotalCost = $elite1.TotalCost
        $properties.Elite1PowerPerCostRatio = $elite1.PowerPerCostRatio
        $properties.Elite2ResourceDirectory = $resourcePlan.Elite2ResourceDirectory
        $properties.Elite2ResourceSource = $resourcePlan.Elite2ResourceSource
        $properties.Elite2PanelPower = $elite2PanelPower
        $properties.Elite2ContinuousPowerPerBody = $elite2.ContinuousPowerPerBody
        $properties.Elite2PanelPowerStatus = $elite2.PanelPowerStatus
        $properties.Elite2TotalPower = $elite2.TotalPower
        $properties.Elite2TotalCost = $elite2.TotalCost
        $properties.Elite2PowerPerCostRatio = $elite2.PowerPerCostRatio
        $properties.Elite3ResourceDirectory = $resourcePlan.Elite3ResourceDirectory
        $properties.Elite3ResourceSource = $resourcePlan.Elite3ResourceSource
        $properties.Elite3PanelPower = $elite3PanelPower
        $properties.Elite3ContinuousPowerPerBody = $elite3.ContinuousPowerPerBody
        $properties.Elite3PanelPowerStatus = $elite3.PanelPowerStatus
        $properties.Elite3TotalPower = $elite3.TotalPower
        $properties.Elite3TotalCost = $elite3.TotalCost
        $properties.Elite3PowerPerCostRatio = $elite3.PowerPerCostRatio
        $properties.EliteEfficiencyRisk = $eliteRisks -join ' | '
        [pscustomobject]$properties
    }
    $rows = @($eliteRows)
    Assert-Condition (@($rows | Where-Object { $_.Elite2ResourceSource -ceq 'Dedicated' }).Count -eq 81) 'Expected 81 dedicated E2 resources.'
    Assert-Condition (@($rows | Where-Object { $_.Elite3ResourceSource -ceq 'Dedicated' }).Count -eq 1) 'Expected one dedicated E3 resource.'
    Assert-Condition ($actuallyReadResourceDirectories.Count -eq 181) "Expected 181 actually-read resource directories; found $($actuallyReadResourceDirectories.Count)."
    $economyPressure = Get-EconomyConstraintGrid -Rows $rows

    $rawRarityMedianPower = [ordered]@{}
    $isotonicRarityMedianPower = [ordered]@{}
    $rarityBaseCosts = [ordered]@{}
    foreach ($rarity in 1..6) {
        $rarityKey = "R$rarity"
        $rawRarityMedianPower[$rarityKey] = $costCurveModel.RarityMedians[$rarity - 1]
        $isotonicRarityMedianPower[$rarityKey] = $costCurveModel.IsotonicRarityMedians[$rarity - 1]
        $rarityBaseCosts[$rarityKey] = [decimal]@($rows | Where-Object { [int]$_.Rarity -eq $rarity })[0].RarityBaseCost
    }

    $analysis = [ordered]@{
        SchemaVersion = 'unit-cost-analysis-v6'
        ShopRowCount = $rows.Count
        RarityDistribution = [ordered]@{ R1 = 9; R2 = 20; R3 = 14; R4 = 24; R5 = 21; R6 = 6 }
        EliteEvidence = @($eliteEvidence)
        EliteModel = [ordered]@{
            EntityCountMultipliers = @(1, 2, 3, 5)
            CostMultipliers = @(1, 2, 3, 5)
            Elite1PanelRule = 'No dedicated E1 panel; per-body ContinuousPower and absolute efficiency equal E0.'
            VariantPanelRule = 'Dedicated E2/E3 level 0 is rescored against the E0 defender/attacker samples, E0 winsor bounds, and E0 references.'
            ContinuousPowerFormula = 'Stage PanelPower * E0 AbilityPowerMultiplier + E0 EquivalentEntityContribution.'
            SpecialtyRule = 'Rows without E0 PanelPower inherit E0 ContinuousPower and are annotated.'
            DedicatedE2Count = @($rows | Where-Object { $_.Elite2ResourceSource -ceq 'Dedicated' }).Count
            DedicatedE3Count = @($rows | Where-Object { $_.Elite3ResourceSource -ceq 'Dedicated' }).Count
            InheritedE2Count = @($rows | Where-Object { $_.Elite2ResourceSource -ceq 'InheritedE0' }).Count
            InheritedE3Count = @($rows | Where-Object { $_.Elite3ResourceSource -ceq 'InheritedE2' }).Count
            ActuallyReadResourceDirectoryCount = $actuallyReadResourceDirectories.Count
            ActuallyReadJsonFileCount = $actuallyReadResourceDirectories.Count * 2
            ActuallyReadResourceDirectories = @($actuallyReadResourceDirectories | Sort-Object)
            EliteEfficiencyRiskCount = @($rows | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_.EliteEfficiencyRisk) }).Count
            AbilityReuseRiskTypeIds = @(1025, 1131, 1132)
        }
        EconomyPressure = [ordered]@{
            ScopeNote = 'Stress check only; permanent Cost income is unconfirmed.'
            PermanentCostIncomeStatus = 'Unconfirmed'
            QuantityFormula = 'min(floor(GoldBudget / rarity), floor(CostBudget / median final Cost for rarity))'
            GoldBudgets = $economyPressure.GoldBudgets
            CostBudgets = $economyPressure.CostBudgets
            MedianCostByRarity = $economyPressure.MedianCostByRarity
            Rows = $economyPressure.Rows
            QuantityRatios = $economyPressure.QuantityRatios
        }
        CombatModel = [ordered]@{
            DefenderCount = $defenderRows.Count
            OrdinaryAttackerCount = $attackingRows.Count
            ScorableOrdinaryCombatCount = $scorableTypeIds.Count
            DpsWinsorization = [ordered]@{ P5 = $dpsP5; P95 = $dpsP95 }
            TtdWinsorization = [ordered]@{ P5 = $ttdP5; P95 = $ttdP95 }
            OutputReference = $outputReference
            DefenseReference = $defenseReference
        }
        AbilityModel = [ordered]@{
            WindowSeconds = 20
            OutputDefenseMergeFormula = 'AbilityPowerMultiplier = sqrt(OutputScenarioMain * DefenseScenarioMain)'
            ContinuousPowerFormula = 'PanelPower * AbilityPowerMultiplier + EquivalentEntityContribution; specialty rows without PanelPower use EquivalentEntityContribution.'
            AoeTargets = @(1, 2, 3)
            AuraTargets = @(1, 3, 5)
            ScenarioCount = $abilityInput.UnitScenarios.Count
            ExplicitRiskTypeIdCount = $abilityInput.ExplicitRiskOnly.Count
        }
        CostModel = [ordered]@{
            RawRarityMedianPower = $rawRarityMedianPower
            IsotonicRarityMedianPower = $isotonicRarityMedianPower
            CompressionAlpha = $costCurveModel.CompressionAlpha
            RarityBaseCosts = $rarityBaseCosts
            DefaultRarity6Anchor = [decimal]28
            SelectedRarity6Anchor = $costCurveModel.SelectedRarity6Anchor
            WithinTierExponent = [decimal]0.6
            MinimumWithinTierFactor = [decimal]0.75
            DefaultMaximumWithinTierFactor = [decimal]1.35
            SelectedMaximumWithinTierFactor = $costCurveModel.SelectedMaxWithinTierFactor
            FinalCostRange = @(5, 40)
            MaximumHighCostCount = 8
            HighCostCount = $costCurveModel.HighCostCount
            ParameterScanTriggered = $costCurveModel.ParameterScanTriggered
            Rarity2To6MedianCosts = $costCurveModel.Rarity2To6MedianCosts
            Rarity6ToRarity2MedianCostRatio = $costCurveModel.Rarity6ToRarity2MedianCostRatio
            R1CalibrationStatus = 'Calibrated'
            R1Calibration = [ordered]@{
                Kappa = $r1CalibrationModel.Kappa
                CandidateRange = @([decimal]0.01, [decimal]1.5)
                CandidateStep = [decimal]0.01
                Budgets = $r1CalibrationModel.Budgets
                FormationKinds = @('Low', 'Median', 'High')
                MatchEventLimit = $r1CalibrationModel.MatchEventLimit
                MatchCount = $r1CalibrationModel.MatchCount
                Rarity1Wins = $r1CalibrationModel.Rarity1Wins
                Rarity2Wins = $r1CalibrationModel.Rarity2Wins
                Draws = $r1CalibrationModel.Draws
                Rarity1WinRate = $r1CalibrationModel.Rarity1WinRate
                WinRateFormula = '(R1 wins + 0.5 * draws) / 72'
                CombatMapping = [ordered]@{
                    BaseAdjustedPower = 'PanelPower * AbilityPowerMultiplier'
                    EquivalentEntityFactor = 'Ordinary unit: 1 + EquivalentEntityContribution / BaseAdjustedPower; no-PanelPower unit: 1'
                    ActualDps = 'Recomputed ordinary damage against current target / EffectiveAttackIntervalSeconds * OutputScenarioMain * EquivalentEntityFactor; no-attack rows remain 0'
                    EffectiveHitPoints = 'Ordinary unit: MaxHitPoints * DefenseScenarioMain * EquivalentEntityFactor; no-PanelPower unit omits the entity factor'
                    TargetValue = 'LifeDeduct * EquivalentEntityFactor'
                    BlockCapacity = '1 per E0 deployed copy; unquantified unit-specific modifiers remain RiskFlags'
                }
                CandidateAudit = $r1CalibrationModel.CandidateAudit
                SelectedMatches = $r1CalibrationModel.SelectedMatches
            }
            CandidateAudit = $costCurveModel.CandidateAudit
        }
        RiskAudit = @($rows | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_.RiskFlags) } | ForEach-Object {
                [ordered]@{ TypeId = $_.TypeId; RiskFlags = $_.RiskFlags }
            })
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
