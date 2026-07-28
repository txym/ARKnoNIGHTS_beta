[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$BondSpecPath,
    [Parameter(Mandatory = $true)][string]$StagingRoot,
    [string]$TempRoot = (Join-Path (Get-Location) 'Temp')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Equal {
    param(
        [Parameter(Mandatory = $true)]$Expected,
        [Parameter(Mandatory = $true)]$Actual,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if ($Expected -ne $Actual) {
        throw "${Name}: expected '$Expected', found '$Actual'."
    }
}

function Assert-Condition {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Assert-Throws {
    param(
        [Parameter(Mandatory = $true)][scriptblock]$Action,
        [Parameter(Mandatory = $true)][string]$Name
    )

    try {
        & $Action
    }
    catch {
        return
    }
    throw "${Name}: expected an explicit failure."
}

function Get-UnitCostHelperScript {
    param([Parameter(Mandatory = $true)][string]$ExporterPath)

    # The exporter keeps its reusable calculation helpers before its main try
    # block.  Load that real code without running its external-data workflow.
    $source = [System.IO.File]::ReadAllText($ExporterPath)
    $mainBlockIndex = [regex]::Match($source, '(?m)^try \{').Index
    Assert-Condition ($mainBlockIndex -gt 0) "Exporter '$ExporterPath' does not expose a helper section before its main workflow."
    return [scriptblock]::Create($source.Substring(0, $mainBlockIndex))
}

function Test-CombatMetricHelpers {
    param([Parameter(Mandatory = $true)][string]$ExporterPath)

    $helperScript = Get-UnitCostHelperScript -ExporterPath $ExporterPath
    . $helperScript `
        -BondSpecPath 'test-only' `
        -StagingRoot 'test-only' `
        -OutputCsvPath 'test-only.csv' `
        -AnalysisOutputPath 'test-only.json'

    Assert-Equal 5 (Get-PhysicalDamage -Attack 100 -Defense 500) 'physical 5% floor'
    Assert-Equal 60 (Get-PhysicalDamage -Attack 100 -Defense 40) 'physical subtraction'
    Assert-Equal 5 (Get-MagicDamage -Attack 100 -MagicResistance 100) 'magic 5% floor'
    Assert-Equal 75 (Get-MagicDamage -Attack 100 -MagicResistance 25) 'magic resistance'
    Assert-Equal 100 (Get-TrueDamage -Attack 100) 'true damage'

    $sample = [decimal[]]@(1, 10, 100, 1000)
    Assert-Equal ([decimal]55) (Get-Median -Values $sample) 'synthetic raw median'
    Assert-Equal ([decimal]2.35) (Get-Percentile -Values $sample -Percentile ([decimal]0.05)) 'synthetic P5'
    Assert-Equal ([decimal]865) (Get-Percentile -Values $sample -Percentile ([decimal]0.95)) 'synthetic P95'
    $winsorized = @(Get-WinsorizedValues -Values $sample -LowerPercentile ([decimal]0.05) -UpperPercentile ([decimal]0.95))
    Assert-Equal ([decimal]2.35) $winsorized[0] 'synthetic winsorized low value'
    Assert-Equal ([decimal]865) $winsorized[3] 'synthetic winsorized high value'
    Assert-Equal ([decimal]55) (Get-Median -Values ([decimal[]]$winsorized)) 'synthetic winsorized median'
    # These are unit-level raw medians, not attacker-by-defender pair values.
    # A pair-level population with uneven matchup counts would have different
    # bounds, so this verifies the intended scoring axis explicitly.
    $unitAxis = Get-WinsorizedUnitAxis -RawMedians ([decimal[]]@(10, 20, 30, 1000))
    Assert-Equal ([decimal]11.5) $unitAxis.P5 'unit-axis P5 uses unit raw medians'
    Assert-Equal ([decimal]854.5) $unitAxis.P95 'unit-axis P95 uses unit raw medians'
    Assert-Equal ([decimal]11.5) $unitAxis.WinsorizedValues[0] 'unit-axis low clamp'
    Assert-Equal ([decimal]854.5) $unitAxis.WinsorizedValues[3] 'unit-axis high clamp'
    Assert-Equal ([decimal]6) (Get-GeometricCombinedValue -Output ([decimal]4) -Defense ([decimal]9)) 'synthetic geometric combination'
    Assert-Equal ([decimal]18) (Get-GeometricCombinedValue -Output ([decimal]12) -Defense ([decimal]27)) 'duplicated population geometric combination'
    Assert-Equal ([decimal]2) (Get-AttackRateFactor -AttackSpeedAdditive 100 -FinalAttackSpeedMultiplier 1) 'attack speed +100 doubles attack rate'
    Assert-Equal ([decimal]0.5) (Get-AttackRateFactor -AttackSpeedAdditive -50 -FinalAttackSpeedMultiplier 1) 'attack speed -50 halves attack rate'
    Assert-Equal ([decimal]1.5) (Get-AttackRateFactor -AttackSpeedAdditive 100 -FinalAttackSpeedMultiplier ([decimal]0.75)) 'attack speed final multiplier applies after additive zone'
    $zeroAttackRate = Get-AttackRateFactor -AttackSpeedAdditive -100 -FinalAttackSpeedMultiplier 1
    Assert-Equal ([decimal]0) $zeroAttackRate 'attack speed clamped to zero'
    Assert-Condition (-not [double]::IsNaN([double]$zeroAttackRate) -and -not [double]::IsInfinity([double]$zeroAttackRate)) 'zero attack speed produced a non-finite attack rate.'

    Assert-Throws { Get-PhysicalDamage -Attack -1 -Defense 0 } 'negative attack'
    Assert-Throws { Get-EffectiveAttackInterval -Attacker ([pscustomobject]@{ TypeId = 1; EffectiveAttackIntervalSeconds = 0 }) } 'nonpositive attack interval'
    Assert-Throws { Get-OrdinaryAttackDamage -Attacker ([pscustomobject]@{ TypeId = 1; DamageType = 'Unknown'; Attack = 100; EffectiveAttackIntervalSeconds = 1 }) -Defender ([pscustomobject]@{ Defense = 0; MagicResistance = 0 }) } 'unknown damage type'
}

function Get-ShopFixtureResourceDirectories {
    param(
        [Parameter(Mandatory = $true)][string]$BondSpecPath,
        [Parameter(Mandatory = $true)][string]$StagingRoot
    )

    $shopHeader = [string]::Concat('## ', [char]0x5546, [char]0x5E97, [char]0x5355, [char]0x4F4D, [char]0xFF08, '88', [char]0xFF09)
    $lines = @(Get-Content -LiteralPath $BondSpecPath -Encoding UTF8)
    $headerIndexes = @(for ($index = 0; $index -lt $lines.Count; $index++) { if ($lines[$index] -ceq $shopHeader) { $index } })
    Assert-Equal 1 $headerIndexes.Count 'shop header count for external snapshot'
    $index = $headerIndexes[0] + 1
    while ([string]::IsNullOrWhiteSpace($lines[$index])) { $index++ }
    Assert-Equal '```text' $lines[$index] 'shop code block marker for external snapshot'
    $index++
    $typeIds = [System.Collections.Generic.List[string]]::new()
    while ($lines[$index] -cne '```') {
        foreach ($typeId in $lines[$index].Split(',')) {
            if (-not [string]::IsNullOrWhiteSpace($typeId)) {
                $typeIds.Add($typeId.Trim())
            }
        }
        $index++
    }
    Assert-Equal 88 $typeIds.Count 'shop TypeId count for external snapshot'

    $directories = [System.Collections.Generic.List[string]]::new()
    foreach ($typeId in $typeIds) {
        if ($typeId -ceq '1322') {
            $directories.Add('1322_wdgyht_2')
            continue
        }
        $matches = @(Get-ChildItem -LiteralPath $StagingRoot -Directory -Filter "${typeId}_*" | Where-Object { $_.Name -notmatch '_[23]$' })
        Assert-Equal 1 $matches.Count "base resource directory count for TypeId $typeId external snapshot"
        $directories.Add($matches[0].Name)
    }
    $directories.Add('1322_wdgyht')
    Assert-Equal 89 @($directories | Sort-Object -Unique).Count 'external snapshot resource directory count'
    return @($directories | Sort-Object -Unique)
}

function Get-ShopAbilityTypeIds {
    param(
        [Parameter(Mandatory = $true)][string]$BondSpecPath,
        [Parameter(Mandatory = $true)][string[]]$ShopTypeIds
    )

    $shopSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($typeId in $ShopTypeIds) {
        [void]$shopSet.Add($typeId)
    }
    $abilityTypeIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $abilityMarkerPattern = '(?:\u80fd\u529b\u63cf\u8ff0|\u80fd\u529b\u8be6\u60c5|\u80fd\u529b\uff1a)'
    foreach ($line in Get-Content -LiteralPath $BondSpecPath -Encoding UTF8) {
        if ($line -match ('^(?<TypeId>\d+)\s+.+?' + $abilityMarkerPattern) -and $shopSet.Contains($Matches.TypeId)) {
            [void]$abilityTypeIds.Add($Matches.TypeId)
        }
    }
    foreach ($typeId in @('1058', '1095', '1281')) {
        Assert-Condition ($shopSet.Contains($typeId)) "Required unmarked ability TypeId '$typeId' is not a shop unit."
        [void]$abilityTypeIds.Add($typeId)
    }
    return @($abilityTypeIds | Sort-Object { [int]$_ })
}

function Test-AbilityInputContract {
    param(
        [Parameter(Mandatory = $true)][string]$AbilityInputPath,
        [Parameter(Mandatory = $true)][string]$BondSpecPath,
        [Parameter(Mandatory = $true)][string[]]$ShopTypeIds
    )

    Assert-Condition (Test-Path -LiteralPath $AbilityInputPath -PathType Leaf) "Ability input '$AbilityInputPath' does not exist."
    $abilityInput = Import-PowerShellDataFile -LiteralPath $AbilityInputPath
    Assert-Equal 'DamageTypeOverrides/ExplicitRiskOnly/UnitScenarios' (($abilityInput.Keys | Sort-Object) -join '/') 'ability input top-level keys'

    $scenarioKeys = @($abilityInput.UnitScenarios.Keys | ForEach-Object { [string]$_ })
    $riskKeys = @($abilityInput.ExplicitRiskOnly.Keys | ForEach-Object { [string]$_ })
    $coveredAbilityTypeIds = @(Get-ShopAbilityTypeIds -BondSpecPath $BondSpecPath -ShopTypeIds $ShopTypeIds)
    Assert-Equal 48 $coveredAbilityTypeIds.Count 'marked and required-unmarked ability TypeId count'
    foreach ($typeId in $coveredAbilityTypeIds) {
        Assert-Condition ($typeId -in $scenarioKeys -or $typeId -in $riskKeys) "BONDS ability TypeId '$typeId' is absent from UnitScenarios and ExplicitRiskOnly."
    }
    foreach ($scenarioKey in $scenarioKeys) {
        $scenario = $abilityInput.UnitScenarios[$scenarioKey]
        Assert-Equal ([int]$scenarioKey) ([int]$scenario.TypeId) "scenario $scenarioKey TypeId"
        Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$scenario.ModelKind)) "Scenario $scenarioKey has no ModelKind."
        Assert-Condition (@($scenario.Evidence).Count -gt 0) "Scenario $scenarioKey has no Evidence."
        Assert-Condition ($null -ne $scenario.UnquantifiedRisk) "Scenario $scenarioKey has no UnquantifiedRisk collection."
    }

    $validTypeIds = @($ShopTypeIds + @('1137', '1138', '2033', '5504', '10002') | Sort-Object -Unique)
    Assert-Equal 93 $validTypeIds.Count 'valid ability TypeId count'
    foreach ($typeId in @($scenarioKeys + $riskKeys + @($abilityInput.DamageTypeOverrides.Keys | ForEach-Object { [string]$_ }) | Sort-Object -Unique)) {
        Assert-Condition ($typeId -in $validTypeIds) "Ability input contains invalid top-level TypeId '$typeId'."
    }

    $unit10039 = $abilityInput.UnitScenarios['10039']
    Assert-Equal ([decimal]0.10) ([decimal]$unit10039.Parameters.PhysicalDamageTakenMultiplier) '10039 physical damage-taken multiplier'
    Assert-Equal ([decimal]0.10) ([decimal]$unit10039.Parameters.MagicDamageTakenMultiplier) '10039 magic damage-taken multiplier'
    Assert-Condition ((@($abilityInput.ExplicitRiskOnly['10039']) -join '|') -match 'charge' -and (@($abilityInput.ExplicitRiskOnly['10039']) -join '|') -match 'range' -and (@($abilityInput.ExplicitRiskOnly['10039']) -join '|') -match 'block') '10039 unknown charge/range/blocking details are not all risk-only.'

    $unit10077 = $abilityInput.UnitScenarios['10077']
    Assert-Equal 10073 ([int]$unit10077.Parameters.SummonTypeId) '10077 summon TypeId'
    Assert-Equal ([decimal]2) ([decimal]$unit10077.Parameters.SkillPointsPerSecond) '10077 SP/s'
    Assert-Equal ([decimal]3) ([decimal]$unit10077.Parameters.InitialSkillPoints) '10077 initial SP'
    Assert-Equal ([decimal]5) ([decimal]$unit10077.Parameters.SkillPointCost) '10077 SP cost'
    Assert-Condition (-not $unit10077.Parameters.ContainsKey('SpawnTimesSeconds')) '10077 must derive summon times from SP parameters instead of storing them.'
    foreach ($scenario in $abilityInput.UnitScenarios.Values) {
        Assert-Condition (-not $scenario.Parameters.ContainsKey('BurstAtSeconds')) "TypeId $($scenario.TypeId) stores an unused BurstAtSeconds."
        Assert-Condition (-not $scenario.Parameters.ContainsKey('HealAtSeconds')) "TypeId $($scenario.TypeId) stores an unused HealAtSeconds."
    }
    Assert-Equal ([decimal]10.2) ([decimal]$abilityInput.UnitScenarios['1131'].Parameters.SummonAtSeconds) '1131 summon time includes death delay'
    Assert-Equal ([decimal]10.2) ([decimal]$abilityInput.UnitScenarios['1132'].Parameters.SummonAtSeconds) '1132 summon time includes death delay'
    foreach ($typeId in @('1058', '1095', '1281')) {
        Assert-Condition ($abilityInput.ExplicitRiskOnly.ContainsKey($typeId)) "Unmarked ability TypeId $typeId is absent from ExplicitRiskOnly."
        Assert-Condition (@($abilityInput.ExplicitRiskOnly[$typeId]).Count -gt 0) "Unmarked ability TypeId $typeId has no explicit risk."
    }

    foreach ($typeId in @('10031', '1238', '1243')) {
        $scenario = $abilityInput.UnitScenarios[$typeId]
        Assert-Equal 'OrdinaryBaseline' ([string]$scenario.ModelKind) "$typeId baseline model"
        $auditText = (@($scenario.Evidence) + @($scenario.UnquantifiedRisk)) -join '|'
        Assert-Condition ($auditText -notmatch '\u9690\u533f|Stealth|\u9644\u52a0\u6cd5\u672f|\u591a\u65b9\u5411') "TypeId $typeId contains a forbidden unsupported modifier in scoring evidence."
    }
    Assert-Equal 'Physical' ([string]$abilityInput.DamageTypeOverrides['1238']) '1238 physical override'
    Assert-Equal 'Physical' ([string]$abilityInput.DamageTypeOverrides['1243']) '1243 physical override'

    return $abilityInput
}

function Get-UnitJsonSnapshot {
    param(
        [Parameter(Mandatory = $true)][string]$StagingRoot,
        [Parameter(Mandatory = $true)][string[]]$ResourceDirectories
    )

    $snapshot = [System.Collections.Generic.List[object]]::new()
    foreach ($resourceDirectory in $ResourceDirectories | Sort-Object -Unique) {
        $directoryPath = Join-Path $StagingRoot $resourceDirectory
        $directory = Get-Item -LiteralPath $directoryPath -ErrorAction Stop
        Assert-Condition (-not [bool]($directory.Attributes -band [System.IO.FileAttributes]::ReparsePoint)) "Resource directory '$directoryPath' must not be a reparse point."
        foreach ($fileName in @('unit-levels.json', 'unit-source-v1.json')) {
            $filePath = Join-Path $directoryPath $fileName
            $file = Get-Item -LiteralPath $filePath -ErrorAction Stop
            Assert-Condition (-not [bool]($file.Attributes -band [System.IO.FileAttributes]::ReparsePoint)) "Resource file '$filePath' must not be a reparse point."
            $snapshot.Add([pscustomobject][ordered]@{
                    RelativePath = (Join-Path $resourceDirectory $fileName)
                    Length = $file.Length
                    Sha256 = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash
                })
        }
    }
    return @($snapshot | Sort-Object RelativePath)
}

function Assert-UnitJsonSnapshotsEqual {
    param(
        [Parameter(Mandatory = $true)][object[]]$Expected,
        [Parameter(Mandatory = $true)][object[]]$Actual,
        [Parameter(Mandatory = $true)][string]$Name
    )

    Assert-Equal $Expected.Count $Actual.Count "$Name file count"
    for ($index = 0; $index -lt $Expected.Count; $index++) {
        Assert-Equal $Expected[$index].RelativePath $Actual[$index].RelativePath "$Name file $index path"
        Assert-Equal $Expected[$index].Length $Actual[$index].Length "$Name file $index length"
        Assert-Equal $Expected[$index].Sha256 $Actual[$index].Sha256 "$Name file $index SHA-256"
    }
}

function Get-UnitJsonSnapshotHash {
    param([Parameter(Mandatory = $true)][object[]]$Snapshot)

    $content = [string]::Join("`n", @($Snapshot | ForEach-Object { "$($_.RelativePath)|$($_.Length)|$($_.Sha256)" }))
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([System.BitConverter]::ToString($algorithm.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($content)))).Replace('-', '')
    }
    finally {
        $algorithm.Dispose()
    }
}

function New-InvalidDamageTypeStagingRoot {
    param(
        [Parameter(Mandatory = $true)][string]$SourceStagingRoot,
        [Parameter(Mandatory = $true)][string]$DestinationRoot,
        [Parameter(Mandatory = $true)][object[]]$SourceSnapshot,
        [Parameter(Mandatory = $true)][string]$DamageType
    )

    [System.IO.Directory]::CreateDirectory($DestinationRoot) | Out-Null
    foreach ($sourceFile in $SourceSnapshot) {
        $destinationPath = Join-Path $DestinationRoot $sourceFile.RelativePath
        [System.IO.Directory]::CreateDirectory((Split-Path -Parent $destinationPath)) | Out-Null
        [System.IO.File]::Copy((Join-Path $SourceStagingRoot $sourceFile.RelativePath), $destinationPath, $false)
    }
    $fixtureSnapshot = Get-UnitJsonSnapshot -StagingRoot $DestinationRoot -ResourceDirectories @($SourceSnapshot | ForEach-Object { Split-Path -Parent $_.RelativePath } | Sort-Object -Unique)
    Assert-UnitJsonSnapshotsEqual -Expected $SourceSnapshot -Actual $fixtureSnapshot -Name 'fixture copy'

    $sourcePath = Join-Path $DestinationRoot '1238_ltmob\unit-source-v1.json'
    $sourceDocument = Get-Content -LiteralPath $sourcePath -Raw -Encoding UTF8 | ConvertFrom-Json
    $sourceDocument.damageType = $DamageType
    [System.IO.File]::WriteAllText($sourcePath, ($sourceDocument | ConvertTo-Json -Depth 8), [System.Text.UTF8Encoding]::new($false))
}

function Add-RegressionFailure {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Failures,
        [Parameter(Mandatory = $true)][string]$Message
    )

    $Failures.Add($Message)
}

function Test-InvalidOverrideDamageType {
    param(
        [Parameter(Mandatory = $true)][string]$BondSpecPath,
        [Parameter(Mandatory = $true)][string]$StagingRoot,
        [Parameter(Mandatory = $true)][string]$TestRoot,
        [Parameter(Mandatory = $true)][string]$ExporterPath,
        [Parameter(Mandatory = $true)][string]$PowerShellPath,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Failures,
        [Parameter(Mandatory = $true)][string[]]$FixtureResourceDirectories,
        [Parameter(Mandatory = $true)][string]$FixtureDamageType,
        [Parameter(Mandatory = $true)][string]$FailureMessage
    )

    $fixtureRoot = Join-Path $TestRoot ('invalid-damage-staging-' + [guid]::NewGuid().ToString('N'))
    $externalSnapshotBefore = Get-UnitJsonSnapshot -StagingRoot $StagingRoot -ResourceDirectories $FixtureResourceDirectories
    New-InvalidDamageTypeStagingRoot -SourceStagingRoot $StagingRoot -DestinationRoot $fixtureRoot -SourceSnapshot $externalSnapshotBefore -DamageType $FixtureDamageType
    $outputCsv = Join-Path $TestRoot 'invalid-damage.csv'
    $analysisOutput = Join-Path $TestRoot 'invalid-damage.json'
    & $PowerShellPath -NoProfile -ExecutionPolicy Bypass -File $ExporterPath `
        -BondSpecPath $BondSpecPath `
        -StagingRoot $fixtureRoot `
        -OutputCsvPath $outputCsv `
        -AnalysisOutputPath $analysisOutput
    $externalSnapshotAfter = Get-UnitJsonSnapshot -StagingRoot $StagingRoot -ResourceDirectories $FixtureResourceDirectories
    Assert-UnitJsonSnapshotsEqual -Expected $externalSnapshotBefore -Actual $externalSnapshotAfter -Name 'external staging after invalid damageType regression test'
    Write-Host "Verified external staging snapshot unchanged after invalid damageType regression test: $($externalSnapshotAfter.Count) JSON files, SHA-256 snapshot $(Get-UnitJsonSnapshotHash -Snapshot $externalSnapshotAfter)."
    if ($LASTEXITCODE -eq 0) {
        Add-RegressionFailure -Failures $Failures -Message $FailureMessage
    }
}

function Test-BondSpecOutputCollision {
    param(
        [Parameter(Mandatory = $true)][string]$BondSpecPath,
        [Parameter(Mandatory = $true)][string]$StagingRoot,
        [Parameter(Mandatory = $true)][string]$TestRoot,
        [Parameter(Mandatory = $true)][string]$ExporterPath,
        [Parameter(Mandatory = $true)][string]$PowerShellPath,
        [Parameter(Mandatory = $true)][string]$OutputParameterName,
        [Parameter(Mandatory = $true)][string[]]$FixtureResourceDirectories,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Failures
    )

    $fixtureBondSpecPath = Join-Path $TestRoot ('bond-spec-collision-' + $OutputParameterName + '-' + [guid]::NewGuid().ToString('N') + '.md')
    [System.IO.File]::Copy($BondSpecPath, $fixtureBondSpecPath)
    $originalContent = [System.IO.File]::ReadAllText($fixtureBondSpecPath)
    $outputCsv = Join-Path $TestRoot ('collision-' + [guid]::NewGuid().ToString('N') + '.csv')
    $analysisOutput = Join-Path $TestRoot ('collision-' + [guid]::NewGuid().ToString('N') + '.json')
    if ($OutputParameterName -ceq 'OutputCsvPath') {
        $outputCsv = $fixtureBondSpecPath
    }
    else {
        $analysisOutput = $fixtureBondSpecPath
    }

    $externalSnapshotBefore = Get-UnitJsonSnapshot -StagingRoot $StagingRoot -ResourceDirectories $FixtureResourceDirectories
    & $PowerShellPath -NoProfile -ExecutionPolicy Bypass -File $ExporterPath `
        -BondSpecPath $fixtureBondSpecPath `
        -StagingRoot $StagingRoot `
        -OutputCsvPath $outputCsv `
        -AnalysisOutputPath $analysisOutput
    $externalSnapshotAfter = Get-UnitJsonSnapshot -StagingRoot $StagingRoot -ResourceDirectories $FixtureResourceDirectories
    Assert-UnitJsonSnapshotsEqual -Expected $externalSnapshotBefore -Actual $externalSnapshotAfter -Name "external staging after $OutputParameterName collision regression test"
    Write-Host "Verified external staging snapshot unchanged after $OutputParameterName collision regression test: $($externalSnapshotAfter.Count) JSON files, SHA-256 snapshot $(Get-UnitJsonSnapshotHash -Snapshot $externalSnapshotAfter)."
    if ($LASTEXITCODE -eq 0) {
        Add-RegressionFailure -Failures $Failures -Message "$OutputParameterName equal to BondSpecPath was accepted instead of rejected."
    }
    if ([System.IO.File]::ReadAllText($fixtureBondSpecPath) -cne $originalContent) {
        Add-RegressionFailure -Failures $Failures -Message "$OutputParameterName equal to BondSpecPath modified the input fixture."
    }
}

try {
    $testRoot = Join-Path $TempRoot 'UnitCostModel'
    [System.IO.Directory]::CreateDirectory($testRoot) | Out-Null

    $outputCsv = Join-Path $testRoot 'unit-cost-dataset.csv'
    $analysisOutput = Join-Path $testRoot 'unit-cost-analysis.json'
    Remove-Item -LiteralPath $outputCsv, $analysisOutput -Force -ErrorAction SilentlyContinue

    $exporterPath = Join-Path $PSScriptRoot 'Export-UnitCostDataset.ps1'
    $abilityInputPath = Join-Path $PSScriptRoot 'UnitCostAbilityInputs.psd1'
    $powershellPath = Join-Path $PSHOME 'powershell.exe'
    Test-CombatMetricHelpers -ExporterPath $exporterPath
    $fixtureResourceDirectories = Get-ShopFixtureResourceDirectories -BondSpecPath $BondSpecPath -StagingRoot $StagingRoot
    $externalSnapshotBeforeWorkflow = Get-UnitJsonSnapshot -StagingRoot $StagingRoot -ResourceDirectories $fixtureResourceDirectories
    Assert-Equal 178 $externalSnapshotBeforeWorkflow.Count 'external staging workflow snapshot JSON file count'
    $shopTypeIds = @(
        $fixtureResourceDirectories |
            Where-Object { $_ -ne '1322_wdgyht' } |
            ForEach-Object { ($_ -split '_', 2)[0] }
    )
    $abilityInput = Test-AbilityInputContract -AbilityInputPath $abilityInputPath -BondSpecPath $BondSpecPath -ShopTypeIds $shopTypeIds
    & $powershellPath -NoProfile -ExecutionPolicy Bypass -File $exporterPath `
        -BondSpecPath $BondSpecPath `
        -StagingRoot $StagingRoot `
        -AbilityInputPath $abilityInputPath `
        -OutputCsvPath $outputCsv `
        -AnalysisOutputPath $analysisOutput
    if ($LASTEXITCODE -ne 0) {
        throw "Export-UnitCostDataset.ps1 failed with exit code $LASTEXITCODE."
    }
    $externalSnapshotAfterMainExport = Get-UnitJsonSnapshot -StagingRoot $StagingRoot -ResourceDirectories $fixtureResourceDirectories
    Assert-UnitJsonSnapshotsEqual -Expected $externalSnapshotBeforeWorkflow -Actual $externalSnapshotAfterMainExport -Name 'external staging after main successful export'
    Write-Host "Verified external staging snapshot unchanged after main successful export: $($externalSnapshotAfterMainExport.Count) JSON files, SHA-256 snapshot $(Get-UnitJsonSnapshotHash -Snapshot $externalSnapshotAfterMainExport)."

    $rows = @(Import-Csv -LiteralPath $outputCsv -Encoding UTF8)
    Assert-Equal 88 $rows.Count 'shop row count'
    Assert-Equal 88 @($rows.TypeId | Sort-Object -Unique).Count 'unique shop TypeId count'
    Assert-Equal 0 @($rows | Where-Object { $_.TypeId -in '1137', '1138', '2033', '5504', '10002' }).Count 'non-shop Cost rows'
    Assert-Equal 1 @($rows | Where-Object TypeId -eq '1000').Count 'known TypeId 1000'
    foreach ($property in @(
            'RawMedianDps', 'WinsorizedMedianDps', 'RawMedianTtdSeconds', 'WinsorizedMedianTtdSeconds',
            'OutputReference', 'DefenseReference', 'PanelPower', 'PanelModelStatus',
            'AbilityModelKind', 'OutputScenarioLow', 'OutputScenarioMain', 'OutputScenarioHigh',
            'DefenseScenarioMain', 'EquivalentEntityContribution', 'AbilityPowerMultiplier',
            'ContinuousPower', 'AbilityEvidence', 'RiskFlags', 'ScenarioEventCount',
            'ScenarioEventTimesSeconds', 'ScenarioAttackCount', 'ScenarioSpecialAttackCount'
        )) {
        Assert-Condition ($rows[0].PSObject.Properties.Name -contains $property) "CSV is missing combat metric column '$property'."
    }

    foreach ($row in $rows) {
        foreach ($property in @(
                'TypeId', 'DisplayName', 'Rarity', 'ResourceDirectory', 'DamageType', 'DamageTypeSource',
                'MaxHitPoints', 'Attack', 'Defense', 'MagicResistance', 'AttackIntervalSeconds',
                'EffectiveAttackIntervalSeconds', 'MoveSpeedMetresPerSecond', 'LifeDeduct'
            )) {
            Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$row.$property)) "Type ID $($row.TypeId) has an empty $property."
        }
    }

    $scorableRows = @($rows | Where-Object PanelModelStatus -eq 'BaseOrdinaryCombat')
    Assert-Condition ($scorableRows.Count -gt 0) 'No base ordinary-combat rows were scored.'
    foreach ($row in $scorableRows) {
        $panelPower = [decimal]$row.PanelPower
        Assert-Condition ($panelPower -gt 0) "Type ID $($row.TypeId) has nonpositive PanelPower '$panelPower'."
        Assert-Condition (-not [double]::IsNaN([double]$panelPower) -and -not [double]::IsInfinity([double]$panelPower)) "Type ID $($row.TypeId) has invalid PanelPower '$panelPower'."
        Assert-Condition ([decimal]$row.OutputReference -gt 0 -and [decimal]$row.DefenseReference -gt 0) "Type ID $($row.TypeId) has invalid combat references."
    }
    foreach ($row in $rows) {
        $continuousPower = [decimal]$row.ContinuousPower
        Assert-Condition ($continuousPower -gt 0) "Type ID $($row.TypeId) has nonpositive ContinuousPower '$continuousPower'."
        Assert-Condition (-not [double]::IsNaN([double]$continuousPower) -and -not [double]::IsInfinity([double]$continuousPower)) "Type ID $($row.TypeId) has invalid ContinuousPower '$continuousPower'."
        $expectedAbilityMultiplier = [decimal][Math]::Sqrt([double]([decimal]$row.OutputScenarioMain * [decimal]$row.DefenseScenarioMain))
        Assert-Condition ([Math]::Abs([double]([decimal]$row.AbilityPowerMultiplier - $expectedAbilityMultiplier)) -lt 0.000000001) "Type ID $($row.TypeId) AbilityPowerMultiplier is not reproducible from exported scenario components."
        $expectedContinuousPower = if ([string]::IsNullOrWhiteSpace([string]$row.PanelPower)) {
            [decimal]$row.EquivalentEntityContribution
        }
        else {
            ([decimal]$row.PanelPower * [decimal]$row.AbilityPowerMultiplier) + [decimal]$row.EquivalentEntityContribution
        }
        Assert-Condition ([Math]::Abs([double]($continuousPower - $expectedContinuousPower)) -lt 0.000000001) "Type ID $($row.TypeId) ContinuousPower is not reproducible from exported components."
    }
    foreach ($typeId in @('10031', '1238', '1243')) {
        $row = @($rows | Where-Object TypeId -eq $typeId)[0]
        Assert-Condition ([string]$row.AbilityEvidence -notmatch '\u9690\u533f|Stealth|\u9644\u52a0\u6cd5\u672f|\u591a\u65b9\u5411') "Type ID $typeId contains a forbidden unsupported modifier in exported scoring evidence."
    }
    $row10039 = @($rows | Where-Object TypeId -eq '10039')[0]
    Assert-Equal ([decimal]10) ([decimal]$row10039.DefenseScenarioMain) '10039 exported main defense scenario'

    $row10073 = @($rows | Where-Object TypeId -eq '10073')[0]
    $row10077 = @($rows | Where-Object TypeId -eq '10077')[0]
    Assert-Equal 8 ([int]$row10077.ScenarioEventCount) '10077 exported summon count'
    Assert-Equal '1/3.5/6/8.5/11/13.5/16/18.5' ([string]$row10077.ScenarioEventTimesSeconds) '10077 derived summon times'
    Assert-Equal ([decimal]$row10073.PanelPower * [decimal]4.1) ([decimal]$row10077.EquivalentEntityContribution) '10077 derived summon contribution'

    $row1089 = @($rows | Where-Object TypeId -eq '1089')[0]
    Assert-Equal ([decimal]1) ([decimal]$row1089.OutputScenarioMain) '1089 no in-window death burst'
    Assert-Equal 0 ([int]$row1089.ScenarioEventCount) '1089 in-window burst event count'
    $row1021 = @($rows | Where-Object TypeId -eq '1021')[0]
    Assert-Equal 1 ([int]$row1021.ScenarioEventCount) '1021 in-window burst event count'
    Assert-Equal '11' ([string]$row1021.ScenarioEventTimesSeconds) '1021 death plus burst-delay resolution time'

    foreach ($typeId in @('1058', '1095', '1281')) {
        $riskRow = @($rows | Where-Object TypeId -eq $typeId)[0]
        Assert-Equal 'ExplicitRiskOnly' ([string]$riskRow.AbilityModelKind) "unmarked ability $typeId model kind"
        Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$riskRow.RiskFlags)) "Unmarked ability TypeId $typeId has empty exported RiskFlags."
        Assert-Equal ([decimal]1) ([decimal]$riskRow.AbilityPowerMultiplier) "unmarked ability $typeId numeric contribution"
    }
    $row1058 = @($rows | Where-Object TypeId -eq '1058')[0]
    Assert-Condition ([string]$row1058.RiskFlags -match 'block count \+2') '1058 exported risk must describe block count +2, not total block count 2.'

    $row1131 = @($rows | Where-Object TypeId -eq '1131')[0]
    $row1132 = @($rows | Where-Object TypeId -eq '1132')[0]
    Assert-Equal '10.2' ([string]$row1131.ScenarioEventTimesSeconds) '1131 exported summon time'
    Assert-Equal '10.2' ([string]$row1132.ScenarioEventTimesSeconds) '1132 exported summon time'
    Assert-Equal ([decimal]1.5) ([decimal]$row1132.EquivalentEntityContribution / [decimal]$row1131.EquivalentEntityContribution) '1131/1132 delayed summon count ratio'

    foreach ($typeId in @('1371', '1372')) {
        $cycleRow = @($rows | Where-Object TypeId -eq $typeId)[0]
        Assert-Equal 20 ([int]$cycleRow.ScenarioAttackCount) "$typeId 20-second attack count"
        Assert-Equal 6 ([int]$cycleRow.ScenarioSpecialAttackCount) "$typeId complete three-hit cycle count"
    }
    $row1371 = @($rows | Where-Object TypeId -eq '1371')[0]
    Assert-Equal ([decimal]'2.0714285714285714285714285714') ([decimal]$row1371.OutputScenarioMain) '1371 ratio of median scenario total to median baseline total'
    $row1372 = @($rows | Where-Object TypeId -eq '1372')[0]
    Assert-Equal ([decimal]1) ([decimal]$row1372.OutputScenarioLow) '1372 low tail-aware output'
    Assert-Equal ([decimal]1.3) ([decimal]$row1372.OutputScenarioMain) '1372 main tail-aware output'
    Assert-Equal ([decimal]1.6) ([decimal]$row1372.OutputScenarioHigh) '1372 high tail-aware output'
    foreach ($typeId in @('1017', '1042', '1146', '1355', '1008', '1026', '1333')) {
        $row = @($rows | Where-Object TypeId -eq $typeId)
        Assert-Equal 1 $row.Count "special combat TypeId $typeId count"
        foreach ($property in @('RawMedianDps', 'WinsorizedMedianDps', 'RawMedianTtdSeconds', 'WinsorizedMedianTtdSeconds', 'OutputReference', 'DefenseReference', 'PanelPower')) {
            Assert-Condition ([string]::IsNullOrWhiteSpace([string]$row[0].$property)) "Type ID $typeId must leave $property empty before an ability scenario is modeled."
        }
    }

    $expectedRows = @{
        '1000' = @{ MaxHitPoints = '820'; Attack = '190'; Defense = '0'; MagicResistance = '20'; AttackIntervalSeconds = '1.4'; DamageType = 'Physical' }
        '1089' = @{ DamageType = 'Magic' }
        '2031' = @{ MaxHitPoints = '35000'; Attack = '800'; Defense = '800'; MagicResistance = '50'; LifeDeduct = '5' }
        '1169' = @{ MaxHitPoints = '4000'; Attack = '300'; Defense = '300'; AttackIntervalSeconds = '2.5' }
        '1322' = @{ ResourceDirectory = '1322_wdgyht_2' }
    }
    foreach ($typeId in $expectedRows.Keys) {
        $row = @($rows | Where-Object TypeId -eq $typeId)
        Assert-Equal 1 $row.Count "known TypeId $typeId count"
        foreach ($property in $expectedRows[$typeId].Keys) {
            Assert-Equal $expectedRows[$typeId][$property] ([string]$row[0].$property) "TypeId $typeId $property"
        }
    }

    Assert-Condition (Test-Path -LiteralPath $analysisOutput -PathType Leaf) "Missing analysis output '$analysisOutput'."
    $analysis = Get-Content -LiteralPath $analysisOutput -Raw -Encoding UTF8 | ConvertFrom-Json
    $riskAuditTypeIds = @($analysis.RiskAudit | ForEach-Object { [string]$_.TypeId })
    foreach ($row in $rows | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_.RiskFlags) }) {
        Assert-Condition ([string]$row.TypeId -in $riskAuditTypeIds) "Risk-bearing TypeId $($row.TypeId) is absent from the analysis RiskAudit."
    }

    $regressionFailures = [System.Collections.Generic.List[string]]::new()
    Test-InvalidOverrideDamageType -BondSpecPath $BondSpecPath -StagingRoot $StagingRoot -TestRoot $testRoot -ExporterPath $exporterPath -PowerShellPath $powershellPath -Failures $regressionFailures -FixtureResourceDirectories $fixtureResourceDirectories -FixtureDamageType 'InvalidDamageType' -FailureMessage 'Illegal non-empty damageType for TypeId 1238 was accepted instead of rejected.'
    Test-InvalidOverrideDamageType -BondSpecPath $BondSpecPath -StagingRoot $StagingRoot -TestRoot $testRoot -ExporterPath $exporterPath -PowerShellPath $powershellPath -Failures $regressionFailures -FixtureResourceDirectories $fixtureResourceDirectories -FixtureDamageType 'Magic' -FailureMessage 'Conflicting allowed damageType Magic for TypeId 1238 was accepted instead of rejected.'
    foreach ($outputParameterName in @('OutputCsvPath', 'AnalysisOutputPath')) {
        Test-BondSpecOutputCollision -BondSpecPath $BondSpecPath -StagingRoot $StagingRoot -TestRoot $testRoot -ExporterPath $exporterPath -PowerShellPath $powershellPath -OutputParameterName $outputParameterName -FixtureResourceDirectories $fixtureResourceDirectories -Failures $regressionFailures
    }
    $regressionFailureMessage = if ($regressionFailures.Count -eq 0) { 'No regression failures.' } else { $regressionFailures -join [Environment]::NewLine }
    Assert-Condition ($regressionFailures.Count -eq 0) $regressionFailureMessage
    $externalSnapshotAfterWorkflow = Get-UnitJsonSnapshot -StagingRoot $StagingRoot -ResourceDirectories $fixtureResourceDirectories
    Assert-UnitJsonSnapshotsEqual -Expected $externalSnapshotBeforeWorkflow -Actual $externalSnapshotAfterWorkflow -Name 'external staging after complete workflow'
    Write-Host "Verified external staging snapshot unchanged after complete workflow: $($externalSnapshotAfterWorkflow.Count) JSON files, SHA-256 snapshot $(Get-UnitJsonSnapshotHash -Snapshot $externalSnapshotAfterWorkflow)."

    Write-Host "PASS: Unit Cost model self-test validated $($rows.Count) shop rows."
    exit 0
}
catch {
    Write-Error $_
    exit 1
}
