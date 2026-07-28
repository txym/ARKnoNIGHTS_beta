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
    param([Parameter(Mandatory = $true)][decimal]$AttackSpeedBonus)

    $factor = ([decimal]200 + $AttackSpeedBonus) / [decimal]200
    Assert-Condition ($factor -gt 0) "Attack-speed bonus '$AttackSpeedBonus' produces a nonpositive attack-rate factor."
    return $factor
}

function Get-AttackStateOutputRatio {
    param(
        [Parameter(Mandatory = $true)]$Row,
        [Parameter(Mandatory = $true)][object[]]$Defenders,
        [Parameter(Mandatory = $true)][decimal]$BaselineMedianDps,
        [decimal]$AttackMultiplier = 1,
        [decimal]$AttackSpeedBonus = 0,
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
    $attackRateFactor = Get-AttackRateFactor -AttackSpeedBonus $AttackSpeedBonus
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

    if (Test-MapKey -Map $Parameters -Key 'AuraAttackSpeedBonus') {
        $incomingRate = Get-AttackRateFactor -AttackSpeedBonus ([decimal]$Parameters.AuraAttackSpeedBonus)
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
    Assert-Condition ($recipientAttackers.Count -gt 0) 'Aura recipient attacker sample is empty.'
    $powerGains = foreach ($recipient in $Recipients) {
        $defenseRatio = Get-DefenseScenarioRatio -Row $recipient -Attackers $recipientAttackers -Parameters $recipientParameters
        [decimal][Math]::Max([double]0, [Math]::Sqrt([double]$defenseRatio) - 1)
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
    $abilityInput = Import-PowerShellDataFile -LiteralPath $AbilityInputPath
    Assert-Condition ($null -ne $abilityInput) "Ability input '$AbilityInputPath' is empty."
    Assert-Condition ((@($abilityInput.Keys | Sort-Object) -join '/') -ceq 'DamageTypeOverrides/ExplicitRiskOnly/UnitScenarios') "Ability input must contain exactly DamageTypeOverrides, UnitScenarios and ExplicitRiskOnly."
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
    $validAbilityTypeIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($typeId in @($shopTypeIds + $nonShopTypeIds)) {
        [void]$validAbilityTypeIds.Add($typeId)
    }
    Assert-Condition ($validAbilityTypeIds.Count -eq 93) "Expected 93 valid shop/non-shop ability TypeIds; found $($validAbilityTypeIds.Count)."
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

    $powerByTypeId = @{}
    foreach ($row in $rows | Where-Object { $null -ne $_.PanelPower }) {
        $powerByTypeId[[int]$row.TypeId] = [decimal]$row.PanelPower
    }
    foreach ($typeId in $nonShopTypeIds) {
        $resourceDirectory = Get-OnlyDirectory -Directories $directories -TypeId $typeId
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
                    $attackSpeedBonus = if ($segment.ContainsKey('AttackSpeedBonus')) { [decimal]$segment.AttackSpeedBonus } else { [decimal]0 }
                    $targetLow = if ($segment.ContainsKey('TargetCountLow')) { [decimal]$segment.TargetCountLow } else { [decimal]1 }
                    $targetMain = if ($segment.ContainsKey('TargetCountMain')) { [decimal]$segment.TargetCountMain } else { $targetLow }
                    $targetHigh = if ($segment.ContainsKey('TargetCountHigh')) { [decimal]$segment.TargetCountHigh } else { $targetMain }
                    $outputLow += $duration * (Get-AttackStateOutputRatio -Row $row -Defenders $defenderRows -BaselineMedianDps $baselineDps -AttackMultiplier $attackMultiplier -AttackSpeedBonus $attackSpeedBonus -TargetCount $targetLow)
                    $outputMain += $duration * (Get-AttackStateOutputRatio -Row $row -Defenders $defenderRows -BaselineMedianDps $baselineDps -AttackMultiplier $attackMultiplier -AttackSpeedBonus $attackSpeedBonus -TargetCount $targetMain)
                    $outputHigh += $duration * (Get-AttackStateOutputRatio -Row $row -Defenders $defenderRows -BaselineMedianDps $baselineDps -AttackMultiplier $attackMultiplier -AttackSpeedBonus $attackSpeedBonus -TargetCount $targetHigh)
                    $totalDuration += $duration
                }
                Assert-Condition ($totalDuration -eq $window) "Type ID $($row.TypeId) output segments cover '$totalDuration' seconds, expected '$window'."
                $outputLow /= $window
                $outputMain /= $window
                $outputHigh /= $window
            }
            if ($scenario.ModelKind -ceq 'OpeningHitsThenSteady20Seconds') {
                $openingCount = [decimal]$parameters.OpeningAttackCount
                $openingSpeedBonus = [decimal]$parameters.OpeningAttackSpeedBonus
                $openingDuration = $openingCount * [decimal]$row.EffectiveAttackIntervalSeconds / (Get-AttackRateFactor -AttackSpeedBonus $openingSpeedBonus)
                $openingDuration = [decimal][Math]::Min([double]$window, [double]$openingDuration)
                $steadyDuration = $window - $openingDuration
                $openingRatio = Get-AttackStateOutputRatio -Row $row -Defenders $defenderRows -BaselineMedianDps $baselineDps -AttackSpeedBonus $openingSpeedBonus
                $steadyIgnore = Get-MapDecimal -Map $parameters -Key 'SteadyDefenseIgnoreFraction' -Default 0
                $steadyRatio = Get-AttackStateOutputRatio -Row $row -Defenders $defenderRows -BaselineMedianDps $baselineDps -AttackMultiplier ([decimal]$parameters.SteadyAttackMultiplier) -DefenseIgnoreFraction $steadyIgnore
                $outputLow = $outputMain = $outputHigh = (($openingRatio * $openingDuration) + ($steadyRatio * $steadyDuration)) / $window
            }
            if (Test-MapKey -Map $parameters -Key 'AttackCycleMultipliers') {
                $multipliers = @($parameters.AttackCycleMultipliers)
                foreach ($scenarioName in @('Low', 'Main', 'High')) {
                    $targetKey = 'AttackCycleTargetCounts' + $scenarioName
                    $targetCounts = if ($parameters.ContainsKey($targetKey)) { @($parameters[$targetKey]) } else { @(for ($index = 0; $index -lt $multipliers.Count; $index++) { 1 }) }
                    Assert-Condition ($targetCounts.Count -eq $multipliers.Count) "Type ID $($row.TypeId) cycle target count does not match its multiplier count."
                    $cycleRatio = [decimal]0
                    for ($index = 0; $index -lt $multipliers.Count; $index++) {
                        $cycleRatio += Get-AttackStateOutputRatio -Row $row -Defenders $defenderRows -BaselineMedianDps $baselineDps -AttackMultiplier ([decimal]$multipliers[$index]) -TargetCount ([decimal]$targetCounts[$index])
                    }
                    $cycleRatio /= [decimal]$multipliers.Count
                    Set-Variable -Name ('output' + $scenarioName) -Value $cycleRatio
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
            if ($parameters.ContainsKey('SpawnTimesSeconds')) {
                foreach ($spawnTime in @($parameters.SpawnTimesSeconds)) {
                    $contribution = $summonPower * [decimal]$parameters.SummonCountPerCast * [decimal][Math]::Max([double]0, [double](($window - [decimal]$spawnTime) / $window))
                    $equivalentMain += $contribution
                }
            }
            elseif ($scenario.ModelKind -ceq 'AttackCycleSummon20Seconds') {
                $spawnInterval = [decimal]$parameters.AttacksPerSummon * [decimal]$row.EffectiveAttackIntervalSeconds
                for ($spawnTime = $spawnInterval; $spawnTime -le $window; $spawnTime += $spawnInterval) {
                    $equivalentMain += $summonPower * (($window - $spawnTime) / $window)
                }
            }
            else {
                $summonCount = [decimal]$parameters.SummonCount
                $spawnTime = [decimal]$parameters.SummonAtSeconds
                $equivalentMain += $summonPower * $summonCount * (($window - $spawnTime) / $window)
            }
            $equivalentLow = $equivalentHigh = $equivalentMain
        }
        if ($null -ne $scenario -and $parameters.ContainsKey('RandomSummons')) {
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
        [pscustomobject]$properties
    }
    $rows = @($abilityRows)

    $analysis = [ordered]@{
        SchemaVersion = 'unit-cost-analysis-v3'
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
        AbilityModel = [ordered]@{
            WindowSeconds = 20
            OutputDefenseMergeFormula = 'AbilityPowerMultiplier = sqrt(OutputScenarioMain * DefenseScenarioMain)'
            ContinuousPowerFormula = 'PanelPower * AbilityPowerMultiplier + EquivalentEntityContribution; specialty rows without PanelPower use EquivalentEntityContribution.'
            AoeTargets = @(1, 2, 3)
            AuraTargets = @(1, 3, 5)
            ScenarioCount = $abilityInput.UnitScenarios.Count
            ExplicitRiskTypeIdCount = $abilityInput.ExplicitRiskOnly.Count
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
