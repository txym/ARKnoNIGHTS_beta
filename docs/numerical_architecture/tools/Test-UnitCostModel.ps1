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
    $powershellPath = Join-Path $PSHOME 'powershell.exe'
    Test-CombatMetricHelpers -ExporterPath $exporterPath
    $fixtureResourceDirectories = Get-ShopFixtureResourceDirectories -BondSpecPath $BondSpecPath -StagingRoot $StagingRoot
    $externalSnapshotBeforeWorkflow = Get-UnitJsonSnapshot -StagingRoot $StagingRoot -ResourceDirectories $fixtureResourceDirectories
    Assert-Equal 178 $externalSnapshotBeforeWorkflow.Count 'external staging workflow snapshot JSON file count'
    & $powershellPath -NoProfile -ExecutionPolicy Bypass -File $exporterPath `
        -BondSpecPath $BondSpecPath `
        -StagingRoot $StagingRoot `
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
            'OutputReference', 'DefenseReference', 'PanelPower', 'PanelModelStatus'
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
