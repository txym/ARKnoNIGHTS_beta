$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$runnerPath = Join-Path $PSScriptRoot '..\Invoke-BondsUnitAnimationAudit.ps1'
. $runnerPath

$temporaryBase = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$temporaryRoot = [System.IO.Path]::Combine(
    $temporaryBase,
    'ARKnoNIGHTS-bonds-animation-audit-test-' + [Guid]::NewGuid().ToString('N'))
$temporaryRoot = [System.IO.Path]::GetFullPath($temporaryRoot)
$temporaryPrefix = $temporaryBase.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $temporaryRoot.StartsWith(
        $temporaryPrefix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Temporary test directory escaped the system temp directory: $temporaryRoot"
}

$strictUtf8 = New-Object System.Text.UTF8Encoding($false, $true)
$junctionPath = $null

function New-ValidAuditFixture {
    $variants = @(
        for ($index = 0; $index -lt 182; $index++) {
            [pscustomobject]@{
                typeId = 1000 + ($index % 99)
                unitKey = 'fixture_{0:D3}' -f $index
                sourceUnitKey = 'fixture_{0:D3}' -f $index
                skeletonDataResourcePath = 'Characters/fixture/enemy_fixture_SkeletonData'
                animationCount = 1
                animations = @(
                    [pscustomobject]@{
                        name = 'Idle'
                        durationSeconds = 1.0
                    }
                )
                exactNameSignature = 'Idle'
                caseFoldedTokenSummary = @('idle')
            }
        }
    )

    return [pscustomobject]@{
        schemaVersion = 'bonds-unit-animation-audit-v1'
        generatedAtUtc = '2026-07-28T00:00:00.0000000Z'
        typeIdCount = 99
        variantCount = 182
        variants = $variants
        signatures = @(
            [pscustomobject]@{
                exactNameSignature = 'Idle'
                unitKeys = @('fixture_000')
            }
        )
        tokenSummary = @(
            [pscustomobject]@{
                token = 'idle'
                variantCount = 182
                animationCount = 182
            }
        )
        diagnostics = @()
    }
}

function Write-AuditFixture {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Document,
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $json = $Document | ConvertTo-Json -Depth 12
    [System.IO.File]::WriteAllText($Path, $json, $strictUtf8)
}

function Assert-AuditFixtureRejected {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$CaseName
    )

    $rejected = $false
    try {
        Test-BondsUnitAnimationAuditOutput -Path $Path
    } catch {
        if (-not $_.Exception.Message.StartsWith(
                'BONDS_ANIMATION_AUDIT_OUTPUT_INVALID',
                [System.StringComparison]::Ordinal)) {
            throw
        }
        $rejected = $true
    }

    if (-not $rejected) {
        throw "Invalid audit fixture was accepted: $CaseName"
    }
}

try {
    [System.IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null

    $validPath = Join-Path $temporaryRoot 'valid.json'
    Write-AuditFixture -Document (New-ValidAuditFixture) -Path $validPath
    Test-BondsUnitAnimationAuditOutput -Path $validPath
    $validSnapshot = Get-BondsUnitAnimationAuditSnapshot -Path $validPath
    $validText = [System.IO.File]::ReadAllText($validPath, $strictUtf8)
    if ($validSnapshot -cne $validText) {
        throw 'The validated snapshot did not preserve the exact JSON text.'
    }
    [System.IO.File]::Delete($validPath)
    [System.IO.File]::WriteAllText($validPath, $validSnapshot, $strictUtf8)
    Test-BondsUnitAnimationAuditOutput -Path $validPath

    $invalidSchema = New-ValidAuditFixture
    $invalidSchema.schemaVersion = 'unknown'
    $invalidSchemaPath = Join-Path $temporaryRoot 'invalid-schema.json'
    Write-AuditFixture -Document $invalidSchema -Path $invalidSchemaPath
    Assert-AuditFixtureRejected -Path $invalidSchemaPath -CaseName 'schemaVersion'

    $invalidCount = New-ValidAuditFixture
    $invalidCount.variantCount = 171
    $invalidCountPath = Join-Path $temporaryRoot 'invalid-count.json'
    Write-AuditFixture -Document $invalidCount -Path $invalidCountPath
    Assert-AuditFixtureRejected -Path $invalidCountPath -CaseName 'variantCount'

    $duplicateUnitKey = New-ValidAuditFixture
    $duplicateUnitKey.variants[1].unitKey = $duplicateUnitKey.variants[0].unitKey
    $duplicateUnitKeyPath = Join-Path $temporaryRoot 'duplicate-unit-key.json'
    Write-AuditFixture -Document $duplicateUnitKey -Path $duplicateUnitKeyPath
    Assert-AuditFixtureRejected -Path $duplicateUnitKeyPath -CaseName 'duplicate unitKey'

    $missingDuration = New-ValidAuditFixture
    $missingDuration.variants[0].animations[0].PSObject.Properties.Remove(
        'durationSeconds')
    $missingDurationPath = Join-Path $temporaryRoot 'missing-duration.json'
    Write-AuditFixture -Document $missingDuration -Path $missingDurationPath
    Assert-AuditFixtureRejected -Path $missingDurationPath -CaseName 'missing durationSeconds'

    $missingDiagnostics = New-ValidAuditFixture
    $missingDiagnostics.PSObject.Properties.Remove('diagnostics')
    $missingDiagnosticsPath = Join-Path $temporaryRoot 'missing-diagnostics.json'
    Write-AuditFixture -Document $missingDiagnostics -Path $missingDiagnosticsPath
    Assert-AuditFixtureRejected -Path $missingDiagnosticsPath -CaseName 'missing diagnostics'

    $animationCountMismatch = New-ValidAuditFixture
    $animationCountMismatch.variants[0].animationCount = 2
    $animationCountMismatchPath = Join-Path $temporaryRoot 'animation-count-mismatch.json'
    Write-AuditFixture `
        -Document $animationCountMismatch `
        -Path $animationCountMismatchPath
    Assert-AuditFixtureRejected `
        -Path $animationCountMismatchPath `
        -CaseName 'animationCount mismatch'

    $junctionTarget = Join-Path $temporaryRoot 'junction-target'
    $junctionPath = Join-Path $temporaryRoot 'junction'
    [System.IO.Directory]::CreateDirectory($junctionTarget) | Out-Null
    New-Item `
        -ItemType Junction `
        -Path $junctionPath `
        -Target $junctionTarget | Out-Null
    $reparsePointRejected = $false
    try {
        Assert-BondsAuditPathHasNoReparsePoint `
            -Path (Join-Path $junctionPath 'output.json') `
            -Boundary $temporaryRoot `
            -Label 'FixturePath'
    } catch {
        if (-not $_.Exception.Message.StartsWith(
                'BONDS_ANIMATION_AUDIT_REPARSE_POINT',
                [System.StringComparison]::Ordinal)) {
            throw
        }
        $reparsePointRejected = $true
    }
    if (-not $reparsePointRejected) {
        throw 'A path beneath a junction was accepted.'
    }
    Remove-Item -LiteralPath $junctionPath -Force
    $junctionPath = $null

    Write-Output 'BONDS_ANIMATION_AUDIT_RUNNER_VALID'
} finally {
    if ((-not [string]::IsNullOrEmpty($junctionPath)) `
        -and [System.IO.Directory]::Exists($junctionPath)) {
        Remove-Item -LiteralPath $junctionPath -Force
    }
    if ([System.IO.Directory]::Exists($temporaryRoot)) {
        [System.IO.Directory]::Delete($temporaryRoot, $true)
    }
}
