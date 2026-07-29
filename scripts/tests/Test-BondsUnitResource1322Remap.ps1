[CmdletBinding()]
param(
    [string]$BondSpecPath,
    [string]$StagingRoot = 'G:\ARKnoNIGHTS_tools\spine-fetcher-output-variants-20260725\staging'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($BondSpecPath)) {
    $BondSpecPath = Join-Path $PSScriptRoot '..\..\docs\bonds\BONDS_SPEC.md'
}

. (Join-Path $PSScriptRoot '..\Import-BondsUnitResources.ps1')
$manifest = @(Get-BondsUnitResourceImportManifest -BondSpecPath $BondSpecPath -StagingRoot $StagingRoot)
if ($manifest.Count -eq 0 -or -not $manifest[0].PSObject.Properties['SourceUnitKey']) {
    throw 'Expected import manifest entries to expose SourceUnitKey separately from the Unity resource key.'
}

$default = @($manifest | Where-Object { $_.SourceUnitKey -eq '1322_wdgyht_2' })
$eliteTwo = @($manifest | Where-Object { $_.SourceUnitKey -eq '1322_wdgyht' })

if ($default.Count -ne 1 -or $default[0].UnitKey -ne '1322_wdgyht' -or $default[0].VariantRole -ne 'Default') {
    throw '1322 default source must become the unsuffixed Unity resource key.'
}
if ($eliteTwo.Count -ne 1 -or $eliteTwo[0].UnitKey -ne '1322_wdgyht_2' -or $eliteTwo[0].VariantRole -ne 'Elite2') {
    throw '1322 elite-two source must become the _2 Unity resource key.'
}

Write-Output 'BONDS_1322_REMAP_MANIFEST_VALID'
