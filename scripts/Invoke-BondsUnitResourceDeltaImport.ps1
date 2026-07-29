[CmdletBinding()]
param(
    [string]$BondSpecPath,
    [string]$StagingRoot = 'G:\ARKnoNIGHTS_tools\spine-fetcher-output-bonds-delta-20260729\staging',
    [string]$CharacterRoot,
    [string]$ProfilePictureRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($BondSpecPath)) {
    $BondSpecPath = Join-Path $repositoryRoot 'docs\bonds\BONDS_SPEC.md'
}
if ([string]::IsNullOrWhiteSpace($CharacterRoot)) {
    $CharacterRoot = Join-Path $repositoryRoot 'Assets\Resources\Characters'
}
if ([string]::IsNullOrWhiteSpace($ProfilePictureRoot)) {
    $ProfilePictureRoot = Join-Path $repositoryRoot 'Assets\Resources\ProfilePicture'
}

. (Join-Path $PSScriptRoot 'Import-BondsUnitResources.ps1')

$deltaTypeIds = @(1014, 1031, 1039, 1078, 1080, 1081, 1083, 1502)
$manifest = @(
    Get-BondsUnitResourceImportManifest `
        -BondSpecPath $BondSpecPath `
        -StagingRoot $StagingRoot `
        -TypeIds $deltaTypeIds
)
if ($manifest.Count -ne 15) {
    throw "Expected 15 BONDS delta variants, actual=$($manifest.Count)"
}

$results = @(
    Invoke-BondsUnitResourceImport `
        -Manifest $manifest `
        -CharacterRoot $CharacterRoot `
        -ProfilePictureRoot $ProfilePictureRoot
)
$verification = @(
    Test-BondsUnitResourceImport `
        -Manifest $manifest `
        -CharacterRoot $CharacterRoot `
        -ProfilePictureRoot $ProfilePictureRoot
)
$invalid = @($verification | Where-Object { -not $_.IsValid })
if ($invalid.Count -ne 0) {
    $details = $invalid |
        ForEach-Object { "$($_.UnitKey):$($_.Errors -join '|')" }
    throw "BONDS delta resource verification failed: $($details -join ';')"
}

$characterDirectories = @(Get-ChildItem -LiteralPath $CharacterRoot -Directory)
$profilePictures = @(Get-ChildItem -LiteralPath $ProfilePictureRoot -File -Filter '*.png')
$characterTypeIds = @(
    $characterDirectories.Name |
        ForEach-Object {
            if ($_ -match '^(\d+)_') {
                [int]$Matches[1]
            }
        } |
        Sort-Object -Unique
)
$profileTypeIds = @(
    $profilePictures.BaseName |
        ForEach-Object {
            if ($_ -match '^UIImage_(\d+)_') {
                [int]$Matches[1]
            }
        } |
        Sort-Object -Unique
)

if ($characterDirectories.Count -ne 185 -or $characterTypeIds.Count -ne 100) {
    throw "Unexpected character resource inventory: variants=$($characterDirectories.Count) typeIds=$($characterTypeIds.Count)"
}
if ($profilePictures.Count -ne 185 -or $profileTypeIds.Count -ne 100) {
    throw "Unexpected profile-picture inventory: variants=$($profilePictures.Count) typeIds=$($profileTypeIds.Count)"
}
if (-not ($characterDirectories.Name -contains '1000_gopro')) {
    throw 'Legacy 1000_gopro resources must remain present.'
}
if ($characterTypeIds -contains 1021 -or $profileTypeIds -contains 1021) {
    throw 'Removed TypeId 1021 must not remain in project resources.'
}

$copied = ($results | Measure-Object -Property CopiedFileCount -Sum).Sum
$unchanged = ($results | Measure-Object -Property UnchangedFileCount -Sum).Sum
Write-Output (
    "BONDS_RESOURCE_DELTA_IMPORT_COMPLETE variants=$($results.Count) copied=$copied unchanged=$unchanged " +
    "projectVariants=$($characterDirectories.Count) projectTypeIds=$($characterTypeIds.Count)")
