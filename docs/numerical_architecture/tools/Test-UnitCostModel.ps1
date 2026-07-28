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
    & $powershellPath -NoProfile -ExecutionPolicy Bypass -File $exporterPath `
        -BondSpecPath $BondSpecPath `
        -StagingRoot $StagingRoot `
        -OutputCsvPath $outputCsv `
        -AnalysisOutputPath $analysisOutput
    if ($LASTEXITCODE -ne 0) {
        throw "Export-UnitCostDataset.ps1 failed with exit code $LASTEXITCODE."
    }

    $rows = @(Import-Csv -LiteralPath $outputCsv -Encoding UTF8)
    Assert-Equal 88 $rows.Count 'shop row count'
    Assert-Equal 88 @($rows.TypeId | Sort-Object -Unique).Count 'unique shop TypeId count'
    Assert-Equal 0 @($rows | Where-Object { $_.TypeId -in '1137', '1138', '2033', '5504', '10002' }).Count 'non-shop Cost rows'
    Assert-Equal 1 @($rows | Where-Object TypeId -eq '1000').Count 'known TypeId 1000'

    foreach ($row in $rows) {
        foreach ($property in @(
                'TypeId', 'DisplayName', 'Rarity', 'ResourceDirectory', 'DamageType', 'DamageTypeSource',
                'MaxHitPoints', 'Attack', 'Defense', 'MagicResistance', 'AttackIntervalSeconds',
                'EffectiveAttackIntervalSeconds', 'MoveSpeedMetresPerSecond', 'LifeDeduct'
            )) {
            Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$row.$property)) "Type ID $($row.TypeId) has an empty $property."
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
    $fixtureResourceDirectories = @($rows.ResourceDirectory | Sort-Object -Unique)
    $fixtureResourceDirectories += '1322_wdgyht'
    Assert-Equal 89 @($fixtureResourceDirectories | Sort-Object -Unique).Count 'fixture resource directory count'
    Test-InvalidOverrideDamageType -BondSpecPath $BondSpecPath -StagingRoot $StagingRoot -TestRoot $testRoot -ExporterPath $exporterPath -PowerShellPath $powershellPath -Failures $regressionFailures -FixtureResourceDirectories $fixtureResourceDirectories -FixtureDamageType 'InvalidDamageType' -FailureMessage 'Illegal non-empty damageType for TypeId 1238 was accepted instead of rejected.'
    Test-InvalidOverrideDamageType -BondSpecPath $BondSpecPath -StagingRoot $StagingRoot -TestRoot $testRoot -ExporterPath $exporterPath -PowerShellPath $powershellPath -Failures $regressionFailures -FixtureResourceDirectories $fixtureResourceDirectories -FixtureDamageType 'Magic' -FailureMessage 'Conflicting allowed damageType Magic for TypeId 1238 was accepted instead of rejected.'
    foreach ($outputParameterName in @('OutputCsvPath', 'AnalysisOutputPath')) {
        Test-BondSpecOutputCollision -BondSpecPath $BondSpecPath -StagingRoot $StagingRoot -TestRoot $testRoot -ExporterPath $exporterPath -PowerShellPath $powershellPath -OutputParameterName $outputParameterName -FixtureResourceDirectories $fixtureResourceDirectories -Failures $regressionFailures
    }
    $regressionFailureMessage = if ($regressionFailures.Count -eq 0) { 'No regression failures.' } else { $regressionFailures -join [Environment]::NewLine }
    Assert-Condition ($regressionFailures.Count -eq 0) $regressionFailureMessage

    Write-Host "PASS: Unit Cost model self-test validated $($rows.Count) shop rows."
    exit 0
}
catch {
    Write-Error $_
    exit 1
}
