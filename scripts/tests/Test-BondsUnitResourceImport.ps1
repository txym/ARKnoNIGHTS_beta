[CmdletBinding()]
param(
    [string]$BondSpecPath,
    [string]$StagingRoot = 'G:\ARKnoNIGHTS_tools\spine-fetcher-output-bonds-delta-20260729\staging',
    [int[]]$TypeIds = @(1014, 1031, 1039, 1078, 1080, 1081, 1083, 1502)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($BondSpecPath)) {
    $BondSpecPath = Join-Path $PSScriptRoot '..\..\docs\bonds\BONDS_SPEC.md'
}

$importScript = Join-Path $PSScriptRoot '..\Import-BondsUnitResources.ps1'
if (-not (Test-Path -LiteralPath $importScript)) {
    throw "Expected BONDS resource import script is missing: $importScript"
}

. $importScript

$manifest = @(Get-BondsUnitResourceImportManifest -BondSpecPath $BondSpecPath -StagingRoot $StagingRoot -TypeIds $TypeIds)
if ($manifest.Count -ne 15) {
    throw "Expected 15 BONDS delta resource variants, actual=$($manifest.Count)"
}

$typeIds = @($manifest | Select-Object -ExpandProperty TypeId -Unique)
if ($typeIds.Count -ne 8) {
    throw "Expected 8 unique BONDS delta TypeIds, actual=$($typeIds.Count)"
}

foreach ($entry in $manifest) {
    if ([string]::IsNullOrWhiteSpace($entry.SourceUnitKey)) {
        throw "Source unit key is missing: $($entry.UnitKey)"
    }
    if ($entry.CharacterFolderName -ne $entry.UnitKey) {
        throw "Character folder must preserve the full unit key: $($entry.UnitKey)"
    }

    if ($entry.ProfilePictureFileName -ne "UIImage_$($entry.UnitKey).png") {
        throw "Profile-picture name is not canonical: $($entry.UnitKey)"
    }

    foreach ($requiredFile in $entry.RequiredSourceFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $entry.SourceDirectory $requiredFile))) {
            throw "Required staged file is missing: unitKey=$($entry.UnitKey) file=$requiredFile"
        }
    }

    $expectedDestinationFiles = @(
        "enemy_$($entry.UnitKey).atlas.txt",
        "enemy_$($entry.UnitKey).png",
        "enemy_$($entry.UnitKey).skel.bytes",
        "UIImage_$($entry.UnitKey).png"
    )
    if ((@($entry.DestinationResourceFiles | Sort-Object) -join "`n") -ne (@($expectedDestinationFiles | Sort-Object) -join "`n")) {
        throw "Destination resource names are not canonical: source=$($entry.SourceUnitKey) destination=$($entry.UnitKey)"
    }
}

if (-not (Get-Command -Name Invoke-BondsUnitResourceImport -ErrorAction SilentlyContinue)) {
    throw 'Expected Invoke-BondsUnitResourceImport to copy the selected resource variants.'
}

$sample = @($manifest | Where-Object { $_.UnitKey -eq '1014_rogue' })
if ($sample.Count -ne 1) {
    throw 'The expected 1014_rogue sample variant is missing.'
}

$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('ARKnoNIGHTS-BONDS-resource-import-' + [guid]::NewGuid().ToString('N'))
try {
    $characterRoot = Join-Path $temporaryRoot 'Assets\Resources\Characters'
    $profilePictureRoot = Join-Path $temporaryRoot 'Assets\Resources\ProfilePicture'
    $sampleResults = @(Invoke-BondsUnitResourceImport -Manifest $sample -CharacterRoot $characterRoot -ProfilePictureRoot $profilePictureRoot)
    if ($sampleResults.Count -ne 1 -or $sampleResults[0].CopiedFileCount -ne 4 -or $sampleResults[0].UnchangedFileCount -ne 0) {
        throw 'The empty temporary destination must receive exactly four resource files.'
    }

    $characterDirectory = Join-Path $characterRoot $sample[0].CharacterFolderName
    foreach ($requiredFile in $sample[0].RequiredSourceFiles | Where-Object { -not $_.StartsWith('UIImage_', [System.StringComparison]::Ordinal) }) {
        if (-not (Test-Path -LiteralPath (Join-Path $characterDirectory $requiredFile))) {
            throw "Imported character resource is missing: $requiredFile"
        }
    }
    if (-not (Test-Path -LiteralPath (Join-Path $profilePictureRoot $sample[0].ProfilePictureFileName))) {
        throw 'Imported profile picture is missing.'
    }
    if (@(Get-ChildItem -LiteralPath $characterDirectory -File | Where-Object { $_.Name -match '^(manifest|unit-levels|unit-source-v1)\.json$' }).Count -ne 0) {
        throw 'Staging JSON metadata must not be copied into Unity character resources.'
    }
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}

if (-not (Get-Command -Name Test-BondsUnitResourceImport -ErrorAction SilentlyContinue)) {
    throw 'Expected Test-BondsUnitResourceImport to verify the project resource destination.'
}

$projectResults = @(Test-BondsUnitResourceImport -Manifest $manifest -CharacterRoot '.\Assets\Resources\Characters' -ProfilePictureRoot '.\Assets\Resources\ProfilePicture')
if ($projectResults.Count -ne 15) {
    throw "Expected 15 project-resource validation results, actual=$($projectResults.Count)"
}
if (@($projectResults | Where-Object { -not $_.IsValid }).Count -ne 0) {
    throw 'The project resource destination has at least one invalid BONDS variant.'
}

Write-Output "BONDS_RESOURCE_DELTA_IMPORT_VALID variants=$($manifest.Count) typeIds=$($typeIds.Count)"
