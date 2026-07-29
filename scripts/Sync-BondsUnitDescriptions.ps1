[CmdletBinding()]
param(
    [string]$ImplementationPath,
    [string]$UnitSourceDirectory,
    [switch]$Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ImplementationPath)) {
    $ImplementationPath = Join-Path `
        $repositoryRoot `
        'docs/bonds/BONDS_IMPLEMENTATION.md'
}
if ([string]::IsNullOrWhiteSpace($UnitSourceDirectory)) {
    $UnitSourceDirectory = Join-Path `
        $repositoryRoot `
        'Assets/GameData/Units/EliteVariants/Json'
}

$strictUtf8 = [System.Text.UTF8Encoding]::new($false, $true)
$implementation = [System.IO.File]::ReadAllText(
    $ImplementationPath,
    $strictUtf8)
$entryPattern =
    '(?ms)^### `(?<typeId>\d+)`[^\r\n]*\r?\n.*?^- 能力描述：(?<description>[^\r\n]+)'
$entryMatches = @([regex]::Matches($implementation, $entryPattern))
if ($entryMatches.Count -ne 59) {
    throw "Expected 59 BONDS ability descriptions, actual=$($entryMatches.Count)"
}

$descriptionsByTypeId = @{}
foreach ($entry in $entryMatches) {
    $typeId = [int]$entry.Groups['typeId'].Value
    $description = $entry.Groups['description'].Value.Trim()
    if ([string]::IsNullOrWhiteSpace($description)) {
        throw "BONDS ability description is empty: TypeId=$typeId"
    }
    if ($descriptionsByTypeId.ContainsKey($typeId)) {
        throw "BONDS ability description TypeId is duplicated: TypeId=$typeId"
    }
    $descriptionsByTypeId[$typeId] = $description
}

$sourceByTypeId = @{}
foreach ($file in Get-ChildItem `
    -LiteralPath $UnitSourceDirectory `
    -Filter '*.json' `
    -File) {
    $text = [System.IO.File]::ReadAllText($file.FullName, $strictUtf8)
    try {
        $document = $text | ConvertFrom-Json
    } catch {
        throw "Unit source JSON is invalid: $($file.FullName)`n$($_.Exception.Message)"
    }
    $typeId = [int]$document.typeId
    if ($sourceByTypeId.ContainsKey($typeId)) {
        throw "Unit source TypeId is duplicated: TypeId=$typeId"
    }
    $sourceByTypeId[$typeId] = [pscustomobject]@{
        File = $file
        Text = $text
        Document = $document
    }
}

$variantOverrides = @{
    '1131:2' = '死亡时分裂两个<畸变恶性瘤>。'
    '1132:2' = '死亡时分裂两个<畸变恶性瘤>。'
}
$changedFiles = 0
$changedVariants = 0

foreach ($typeId in @($descriptionsByTypeId.Keys | Sort-Object)) {
    if (-not $sourceByTypeId.ContainsKey($typeId)) {
        throw "BONDS unit source is missing: TypeId=$typeId"
    }

    $source = $sourceByTypeId[$typeId]
    $updatedText = $source.Text
    foreach ($variant in @($source.Document.variants)) {
        $eliteLevel = [int]$variant.minEliteLevel
        $overrideKey = '{0}:{1}' -f $typeId, $eliteLevel
        $expectedDescription = if ($variantOverrides.ContainsKey($overrideKey)) {
            $variantOverrides[$overrideKey]
        } else {
            $descriptionsByTypeId[$typeId]
        }
        $encodedDescription = $expectedDescription | ConvertTo-Json -Compress
        $sourceVariant = [regex]::Escape([string]$variant.sourceVariant)
        $variantPattern =
            '(?ms)("sourceVariant"\s*:\s*"' + $sourceVariant +
            '".*?"skillDescriptionZhHans"\s*:\s*)"(?:\\.|[^"\\])*"'
        $matches = @([regex]::Matches($updatedText, $variantPattern))
        if ($matches.Count -ne 1) {
            throw "Expected one description field for sourceVariant=$($variant.sourceVariant), actual=$($matches.Count)"
        }
        $currentDescription = [string]$variant.skillDescriptionZhHans
        if ($currentDescription -eq $expectedDescription) {
            continue
        }
        if ($Check) {
            throw "BONDS unit description is out of date: TypeId=$typeId sourceVariant=$($variant.sourceVariant)"
        }

        $updatedText = [regex]::Replace(
            $updatedText,
            $variantPattern,
            '${1}' + $encodedDescription)
        $changedVariants++
    }

    if ($updatedText -ne $source.Text) {
        [System.IO.File]::WriteAllText(
            $source.File.FullName,
            $updatedText,
            $strictUtf8)
        $changedFiles++
    }
}

Write-Output (
    'result={0}; descriptions={1}; changedFiles={2}; changedVariants={3}' -f
        $(if ($Check) { 'checked' } else { 'updated' }),
        $descriptionsByTypeId.Count,
        $changedFiles,
        $changedVariants)
