[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$BondSpecPath,
    [Parameter(Mandatory = $true)][string]$StagingRoot,
    [string]$ExpectedCsvPath,
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

function Get-NormalizedUtf8Text {
    param([Parameter(Mandatory = $true)][string]$Path)

    $utf8 = [System.Text.UTF8Encoding]::new($false, $true)
    $text = [System.IO.File]::ReadAllText($Path, $utf8)
    if ($text.Length -gt 0 -and $text[0] -eq [char]0xFEFF) {
        $text = $text.Substring(1)
    }
    return ($text -replace "`r`n", "`n" -replace "`r", "`n")
}

function Test-PathEqualOrUnderRootCaseInsensitive {
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

function Assert-ExpectedCsvOutsideTestRoot {
    param(
        [Parameter(Mandatory = $true)][string]$ExpectedCsvPath,
        [Parameter(Mandatory = $true)][string]$TestRoot
    )

    $normalizedExpectedCsvPath = [System.IO.Path]::GetFullPath($ExpectedCsvPath)
    $normalizedTestRoot = [System.IO.Path]::GetFullPath($TestRoot)
    Assert-Condition (-not (Test-PathEqualOrUnderRootCaseInsensitive -CandidatePath $normalizedExpectedCsvPath -RootPath $normalizedTestRoot)) "Expected formal CSV '$normalizedExpectedCsvPath' must not equal or be contained by test output root '$normalizedTestRoot'."
    return $normalizedExpectedCsvPath
}

function Get-ExpectedAnalysisPath {
    param([Parameter(Mandatory = $true)][string]$ExpectedCsvPath)

    $directory = Split-Path -Parent $ExpectedCsvPath
    $baseName = [System.IO.Path]::GetFileNameWithoutExtension($ExpectedCsvPath)
    $analysisBaseName = if ($baseName.EndsWith('_dataset', [System.StringComparison]::Ordinal)) {
        $baseName.Substring(0, $baseName.Length - '_dataset'.Length) + '_analysis'
    }
    else {
        $baseName + '_analysis'
    }
    return Join-Path $directory ($analysisBaseName + '.md')
}

function Assert-MarkdownMarkerRowCount {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string[]]$Lines,
        [Parameter(Mandatory = $true)][string]$MarkerName,
        [Parameter(Mandatory = $true)][int]$ExpectedCount
    )

    $startMarker = "<!-- ${MarkerName}_START -->"
    $endMarker = "<!-- ${MarkerName}_END -->"
    $startIndexes = @(for ($index = 0; $index -lt $Lines.Count; $index++) { if ($Lines[$index] -ceq $startMarker) { $index } })
    $endIndexes = @(for ($index = 0; $index -lt $Lines.Count; $index++) { if ($Lines[$index] -ceq $endMarker) { $index } })
    Assert-Equal 1 $startIndexes.Count "$MarkerName start marker count"
    Assert-Equal 1 $endIndexes.Count "$MarkerName end marker count"
    Assert-Condition ($endIndexes[0] -gt $startIndexes[0]) "$MarkerName markers are reversed."
    $rowCount = @(
        for ($index = $startIndexes[0] + 1; $index -lt $endIndexes[0]; $index++) {
            if ($Lines[$index].StartsWith('| ', [System.StringComparison]::Ordinal)) {
                $Lines[$index]
            }
        }
    ).Count
    Assert-Equal $ExpectedCount $rowCount "$MarkerName Markdown data row count"
}

function Test-ExpectedCsvPathGuard {
    param(
        [Parameter(Mandatory = $true)][string]$SourceExpectedCsvPath,
        [Parameter(Mandatory = $true)][string]$TempRoot
    )

    $fixtureRoot = Join-Path $TempRoot ('ExpectedCsvPathGuard-' + [guid]::NewGuid().ToString('N'))
    $collisionTestRoot = Join-Path $fixtureRoot 'UnitCostModel'
    [System.IO.Directory]::CreateDirectory($collisionTestRoot) | Out-Null
    $collisionExpectedCsv = Join-Path $collisionTestRoot 'unit-cost-dataset.csv'
    [System.IO.File]::Copy($SourceExpectedCsvPath, $collisionExpectedCsv, $false)
    $collisionSnapshot = Get-FileIntegritySnapshot -Path $collisionExpectedCsv

    Assert-Throws {
        Assert-ExpectedCsvOutsideTestRoot -ExpectedCsvPath $collisionExpectedCsv -TestRoot $collisionTestRoot
    } 'ExpectedCsvPath equal to the test output CSV'
    Assert-Throws {
        Assert-ExpectedCsvOutsideTestRoot -ExpectedCsvPath $collisionExpectedCsv.ToUpperInvariant() -TestRoot $collisionTestRoot.ToLowerInvariant()
    } 'case-insensitive ExpectedCsvPath under the test output root'
    Assert-FileIntegritySnapshotEqual -Expected $collisionSnapshot -Actual (Get-FileIntegritySnapshot -Path $collisionExpectedCsv) -Name 'rejected ExpectedCsvPath collision input'
    Assert-Equal 1 @(Get-ChildItem -LiteralPath $collisionTestRoot -File).Count 'rejected ExpectedCsvPath collision output file count'
    Assert-Equal 0 @(Get-ChildItem -LiteralPath $collisionTestRoot -Directory).Count 'rejected ExpectedCsvPath collision output directory count'

    $siblingRoot = Join-Path $fixtureRoot 'UnitCostModel-sibling'
    [System.IO.Directory]::CreateDirectory($siblingRoot) | Out-Null
    $siblingExpectedCsv = Join-Path $siblingRoot 'expected.csv'
    [System.IO.File]::Copy($SourceExpectedCsvPath, $siblingExpectedCsv, $false)
    $siblingSnapshot = Get-FileIntegritySnapshot -Path $siblingExpectedCsv
    $acceptedSiblingPath = Assert-ExpectedCsvOutsideTestRoot -ExpectedCsvPath $siblingExpectedCsv -TestRoot $collisionTestRoot
    Assert-Equal ([System.IO.Path]::GetFullPath($siblingExpectedCsv)) $acceptedSiblingPath 'similar-prefix ExpectedCsvPath sibling'
    Assert-FileIntegritySnapshotEqual -Expected $siblingSnapshot -Actual (Get-FileIntegritySnapshot -Path $siblingExpectedCsv) -Name 'accepted ExpectedCsvPath sibling input'
}

function Assert-TestGeneratedCostMarkerRegion {
    param(
        [Parameter(Mandatory = $true)][string]$RegionText,
        [Parameter(Mandatory = $true)][string]$Newline,
        [Parameter(Mandatory = $true)][string]$StartMarker,
        [Parameter(Mandatory = $true)][string]$EndMarker,
        [Parameter(Mandatory = $true)][string]$CostHeading,
        [Parameter(Mandatory = $true)][string]$CostColumns,
        [Parameter(Mandatory = $true)][string[]]$ShopTypeIds,
        [Parameter(Mandatory = $true)][string[]]$NonShopTypeIds,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $lines = @($RegionText -split [regex]::Escape($Newline))
    Assert-Equal 99 $lines.Count "$Name line count"
    Assert-Equal $StartMarker $lines[0] "$Name start marker"
    Assert-Equal $CostHeading $lines[1] "$Name heading"
    Assert-Equal $CostColumns $lines[2] "$Name columns"
    Assert-Equal '| ---: | --- | ---: | ---: |' $lines[3] "$Name separator"
    Assert-Equal $EndMarker $lines[-1] "$Name end marker"

    $typeIds = [System.Collections.Generic.List[string]]::new()
    for ($index = 4; $index -lt $lines.Count - 1; $index++) {
        Assert-Condition ($lines[$index] -match '^\| (?<TypeId>[1-9][0-9]*) \| (?<DisplayName>(?:\\\||[^|])+?) \| (?<Rarity>[1-6]) \| (?<Cost>[0-9]+) \|$') "$Name data line '$($lines[$index])' has an invalid shape."
        $typeId = [string]$Matches.TypeId
        $displayName = [string]$Matches.DisplayName
        $rarity = [int]$Matches.Rarity
        $cost = [int]$Matches.Cost
        Assert-Condition (-not [string]::IsNullOrWhiteSpace($displayName)) "$Name TypeId '$typeId' has an empty display name."
        if ($rarity -eq 1) {
            Assert-Condition ($cost -ge 2 -and $cost -le 40) "$Name R1 TypeId '$typeId' Cost '$cost' is outside 2..40."
        }
        else {
            Assert-Condition ($cost -ge 5 -and $cost -le 40) "$Name R$rarity TypeId '$typeId' Cost '$cost' is outside 5..40."
        }
        Assert-Condition ($typeId -notin $NonShopTypeIds) "$Name contains non-shop TypeId '$typeId'."
        $typeIds.Add($typeId)
    }

    Assert-Equal 94 $typeIds.Count "$Name data row count"
    Assert-Equal 94 @($typeIds | Sort-Object -Unique).Count "$Name unique TypeId count"
    $numericOrder = @($typeIds | Sort-Object { [int64]$_ })
    Assert-Equal ($numericOrder -join '/') (@($typeIds) -join '/') "$Name numeric TypeId order"
    Assert-Equal (@($ShopTypeIds | Sort-Object { [int64]$_ }) -join '/') ($numericOrder -join '/') "$Name BONDS shop TypeId set"
}

function Test-BondsUnitCostTableUpdater {
    param(
        [Parameter(Mandatory = $true)][string]$BondSpecPath,
        [Parameter(Mandatory = $true)][string]$CostCsvPath,
        [Parameter(Mandatory = $true)][string]$TempRoot
    )

    $startMarker = '<!-- UNIT-COST-TABLE:START -->'
    $endMarker = '<!-- UNIT-COST-TABLE:END -->'
    $shopHeader = [string]::Concat('## ', [char]0x5546, [char]0x5E97, [char]0x5355, [char]0x4F4D, [char]0xFF08, '94', [char]0xFF09)
    $nonShopHeader = [string]::Concat('## ', [char]0x975E, [char]0x5546, [char]0x5E97, [char]0x5355, [char]0x4F4D, [char]0xFF08, '5', [char]0xFF09)
    $costHeading = [string]::Concat('## ', [char]0x5546, [char]0x5E97, [char]0x5355, [char]0x4F4D, [char]0x7CBE, [char]0x82F1, ' 0 ', [char]0x57FA, [char]0x7840, [char]0x90E8, [char]0x7F72, ' Cost')
    $costColumns = [string]::Concat('| TypeId | ', [char]0x540D, [char]0x79F0, ' | ', [char]0x7A00, [char]0x6709, [char]0x5EA6, ' | ', [char]0x7CBE, [char]0x82F1, '0', [char]0x57FA, [char]0x7840, 'Cost C0 |')
    $utf8 = [System.Text.UTF8Encoding]::new($false, $true)
    $realBondSnapshot = Get-FileIntegritySnapshot -Path $BondSpecPath
    $realBondBytes = [System.IO.File]::ReadAllBytes($BondSpecPath)
    $realBondText = $utf8.GetString($realBondBytes)
    $realShopPattern = '(?ms)^' + [regex]::Escape($shopHeader) + '\r?\n\s*\r?\n```text\r?\n(?<Ids>.*?)\r?\n```'
    $realShopMatch = [regex]::Match($realBondText, $realShopPattern)
    Assert-Condition $realShopMatch.Success 'Real BONDS shop TypeId block is missing or malformed.'
    $realShopTypeIds = @($realShopMatch.Groups['Ids'].Value -split '[,\s]+' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    Assert-Equal 94 $realShopTypeIds.Count 'Real BONDS shop TypeId count before marker normalization'
    Assert-Equal 94 @($realShopTypeIds | Sort-Object -Unique).Count 'Real BONDS unique shop TypeId count before marker normalization'
    $realNonShopPattern = '(?ms)^' + [regex]::Escape($nonShopHeader) + '\r?\n\s*\r?\n```text\r?\n(?<Ids>.*?)\r?\n```'
    $realNonShopMatch = [regex]::Match($realBondText, $realNonShopPattern)
    Assert-Condition $realNonShopMatch.Success 'Real BONDS non-shop TypeId block is missing or malformed.'
    $realNonShopTypeIds = @($realNonShopMatch.Groups['Ids'].Value -split '[,\s]+' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    Assert-Equal '1137/1138/2033/5504/10002' ($realNonShopTypeIds -join '/') 'Real BONDS non-shop TypeId set before marker normalization'
    $realStartCount = [regex]::Matches($realBondText, [regex]::Escape($startMarker)).Count
    $realEndCount = [regex]::Matches($realBondText, [regex]::Escape($endMarker)).Count
    $realMarkerStateIsValid = ($realStartCount -eq 0 -and $realEndCount -eq 0) -or ($realStartCount -eq 1 -and $realEndCount -eq 1)
    Assert-Condition $realMarkerStateIsValid 'Real BONDS must contain either no Cost-table markers or one exact marker pair.'

    $fixtureSourceBytes = $realBondBytes
    if ($realStartCount -eq 1) {
        $realStartIndex = $realBondText.IndexOf($startMarker, [System.StringComparison]::Ordinal)
        $realEndIndex = $realBondText.IndexOf($endMarker, [System.StringComparison]::Ordinal)
        Assert-Condition ($realStartIndex -ge 0 -and $realEndIndex -gt $realStartIndex) 'Real BONDS Cost-table markers are reversed.'
        $realRegionEnd = $realEndIndex + $endMarker.Length
        $realRegionText = $realBondText.Substring($realStartIndex, $realRegionEnd - $realStartIndex)
        $realNewline = if ($realBondText.Contains("`r`n")) { "`r`n" } else { "`n" }
        Assert-Condition ($realBondText.Substring($realRegionEnd).StartsWith($realNewline, [System.StringComparison]::Ordinal)) 'Real BONDS Cost-table end marker is not followed by the source newline.'
        $realRegionEnd += $realNewline.Length
        $realNonShopIndex = $realBondText.IndexOf($nonShopHeader, [System.StringComparison]::Ordinal)
        Assert-Equal $realRegionEnd $realNonShopIndex 'Real BONDS Cost-table marker immediate non-shop boundary'
        Assert-TestGeneratedCostMarkerRegion `
            -RegionText $realRegionText `
            -Newline $realNewline `
            -StartMarker $startMarker `
            -EndMarker $endMarker `
            -CostHeading $costHeading `
            -CostColumns $costColumns `
            -ShopTypeIds $realShopTypeIds `
            -NonShopTypeIds $realNonShopTypeIds `
            -Name 'Real BONDS existing Cost marker region'
        $fixtureSourceText = $realBondText.Remove($realStartIndex, $realRegionEnd - $realStartIndex)
        $fixtureSourceBytes = $utf8.GetBytes($fixtureSourceText)
    }

    $fixtureRoot = Join-Path $TempRoot ('BondsUnitCostTable-' + [guid]::NewGuid().ToString('N'))
    [System.IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
    $fixtureBondSpecPath = Join-Path $fixtureRoot 'BONDS_SPEC.md'
    [System.IO.File]::WriteAllBytes($fixtureBondSpecPath, $fixtureSourceBytes)
    $fixtureText = [System.IO.File]::ReadAllText($fixtureBondSpecPath, $utf8)
    Assert-Equal 0 ([regex]::Matches($fixtureText, '<!-- UNIT-COST-TABLE:START -->').Count) 'initial Temp BONDS Cost-table start marker count'
    Assert-Equal 0 ([regex]::Matches($fixtureText, '<!-- UNIT-COST-TABLE:END -->').Count) 'initial Temp BONDS Cost-table end marker count'

    $updaterPath = Join-Path $PSScriptRoot 'Update-BondsUnitCostTable.ps1'
    Assert-Condition (Test-Path -LiteralPath $updaterPath -PathType Leaf) "Missing BONDS Cost-table updater '$updaterPath'."

    $powerShellPath = Join-Path $PSHOME 'powershell.exe'
    $originalBytes = [System.IO.File]::ReadAllBytes($fixtureBondSpecPath)
    $originalSnapshot = Get-FileIntegritySnapshot -Path $fixtureBondSpecPath
    foreach ($path in @($fixtureRoot, $fixtureBondSpecPath)) {
        $item = Get-Item -LiteralPath $path -Force
        Assert-Condition (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -eq 0) "Task 8 fixture '$path' must be an ordinary file or directory, not a reparse point."
    }

    Assert-Condition ($originalBytes.Length -lt 3 -or -not ($originalBytes[0] -eq 0xEF -and $originalBytes[1] -eq 0xBB -and $originalBytes[2] -eq 0xBF)) 'Source Temp BONDS must be UTF-8 without BOM.'
    Assert-Condition (-not $fixtureText.Contains("`r`n") -and $fixtureText.Contains("`n")) 'Formal Temp BONDS baseline must use LF-only newlines.'

    $shopBlockPattern = '(?ms)^' + [regex]::Escape($shopHeader) + '\r?\n\s*\r?\n```text\r?\n(?<Ids>.*?)\r?\n```'
    $shopBlockMatch = [regex]::Match($fixtureText, $shopBlockPattern)
    Assert-Condition $shopBlockMatch.Success 'Temp BONDS shop TypeId block is missing or malformed.'
    $bondShopTypeIds = @($shopBlockMatch.Groups['Ids'].Value -split '[,\s]+' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    Assert-Equal 94 $bondShopTypeIds.Count 'Temp BONDS shop TypeId count'
    Assert-Equal 94 @($bondShopTypeIds | Sort-Object -Unique).Count 'Temp BONDS unique shop TypeId count'

    $nonShopBlockPattern = '(?ms)^' + [regex]::Escape($nonShopHeader) + '\r?\n\s*\r?\n```text\r?\n(?<Ids>.*?)\r?\n```'
    $nonShopBlockMatch = [regex]::Match($fixtureText, $nonShopBlockPattern)
    Assert-Condition $nonShopBlockMatch.Success 'Temp BONDS non-shop TypeId block is missing or malformed.'
    $bondNonShopTypeIds = @($nonShopBlockMatch.Groups['Ids'].Value -split '[,\s]+' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    Assert-Equal '1137/1138/2033/5504/10002' ($bondNonShopTypeIds -join '/') 'Temp BONDS non-shop TypeId set'

    $csvRows = @(Import-Csv -LiteralPath $CostCsvPath -Encoding UTF8)
    Assert-Equal 94 $csvRows.Count 'formal CSV Cost row count for updater'
    Assert-Equal (@($bondShopTypeIds | Sort-Object { [int]$_ }) -join '/') (@($csvRows.TypeId | Sort-Object { [int]$_ }) -join '/') 'formal CSV and Temp BONDS shop TypeId sets'
    foreach ($nonShopTypeId in $bondNonShopTypeIds) {
        Assert-Condition ($nonShopTypeId -notin $csvRows.TypeId) "Formal CSV contains non-shop TypeId '$nonShopTypeId'."
    }

    & $powerShellPath -NoProfile -ExecutionPolicy Bypass -File $updaterPath `
        -BondSpecPath $fixtureBondSpecPath `
        -CostCsvPath $CostCsvPath | Out-Null
    Assert-Equal 0 $LASTEXITCODE 'first Temp BONDS Cost-table updater exit code'
    $firstBytes = [System.IO.File]::ReadAllBytes($fixtureBondSpecPath)
    $firstText = $utf8.GetString($firstBytes)
    Assert-Condition ($firstBytes.Length -lt 3 -or -not ($firstBytes[0] -eq 0xEF -and $firstBytes[1] -eq 0xBB -and $firstBytes[2] -eq 0xBF)) 'Generated Temp BONDS must be UTF-8 without BOM.'
    Assert-Condition (-not $firstText.Contains("`r`n") -and $firstText.Contains("`n")) 'Generated Temp BONDS must preserve LF-only newlines.'
    Assert-Equal 1 ([regex]::Matches($firstText, [regex]::Escape($startMarker)).Count) 'first Temp BONDS Cost-table start marker count'
    Assert-Equal 1 ([regex]::Matches($firstText, [regex]::Escape($endMarker)).Count) 'first Temp BONDS Cost-table end marker count'

    $startIndex = $firstText.IndexOf($startMarker, [System.StringComparison]::Ordinal)
    $endIndex = $firstText.IndexOf($endMarker, [System.StringComparison]::Ordinal)
    Assert-Condition ($startIndex -ge 0 -and $endIndex -gt $startIndex) 'Generated Temp BONDS Cost-table markers are missing or reversed.'
    $regionEnd = $endIndex + $endMarker.Length
    $newline = if ($firstText.Contains("`r`n")) { "`r`n" } else { "`n" }
    Assert-Condition ($firstText.Substring($regionEnd).StartsWith($newline, [System.StringComparison]::Ordinal)) 'Generated Temp BONDS Cost-table end marker must be followed by the source newline.'
    $regionEnd += $newline.Length
    $normalizedOutsideText = $firstText.Remove($startIndex, $regionEnd - $startIndex)
    Assert-Condition ([string]::Equals($fixtureText, $normalizedOutsideText, [System.StringComparison]::Ordinal)) 'Temp BONDS content outside the generated marker region changed.'
    $normalizedOutsideBytes = $utf8.GetBytes($normalizedOutsideText)
    Assert-Equal ([System.Convert]::ToBase64String($originalBytes)) ([System.Convert]::ToBase64String($normalizedOutsideBytes)) 'Temp BONDS byte content outside marker region'

    $nonShopIndex = $firstText.IndexOf($nonShopHeader, [System.StringComparison]::Ordinal)
    Assert-Equal $regionEnd $nonShopIndex 'Generated Temp BONDS Cost table immediate non-shop boundary'

    $regionText = $firstText.Substring($startIndex, ($endIndex + $endMarker.Length) - $startIndex)
    $regionLines = @($regionText -split '\r?\n')
    Assert-Equal 99 $regionLines.Count 'generated Cost marker block line count'
    Assert-Equal $startMarker $regionLines[0] 'generated Cost start marker'
    Assert-Equal $costHeading $regionLines[1] 'generated Cost heading'
    Assert-Equal $costColumns $regionLines[2] 'generated Cost table columns'
    Assert-Equal '| ---: | --- | ---: | ---: |' $regionLines[3] 'generated Cost table separator'
    Assert-Equal $endMarker $regionLines[-1] 'generated Cost end marker'
    Assert-TestGeneratedCostMarkerRegion `
        -RegionText $regionText `
        -Newline $newline `
        -StartMarker $startMarker `
        -EndMarker $endMarker `
        -CostHeading $costHeading `
        -CostColumns $costColumns `
        -ShopTypeIds $bondShopTypeIds `
        -NonShopTypeIds $bondNonShopTypeIds `
        -Name 'Generated Temp BONDS Cost marker region'

    $csvByTypeId = @{}
    foreach ($csvRow in $csvRows) {
        $csvByTypeId[[string]$csvRow.TypeId] = $csvRow
    }
    $tableTypeIds = [System.Collections.Generic.List[string]]::new()
    for ($index = 4; $index -lt $regionLines.Count - 1; $index++) {
        Assert-Condition ($regionLines[$index] -match '^\| (?<TypeId>\d+) \| (?<Name>.*?) \| (?<Rarity>[1-6]) \| (?<Cost>\d+) \|$') "Generated Cost data line '$($regionLines[$index])' has an invalid shape."
        $typeId = [string]$Matches.TypeId
        $rarity = [int]$Matches.Rarity
        $cost = [int]$Matches.Cost
        Assert-Condition ($csvByTypeId.ContainsKey($typeId)) "Generated Cost table contains TypeId '$typeId' absent from the formal CSV."
        Assert-Equal (([string]$csvByTypeId[$typeId].DisplayName).Replace('|', '\|')) ([string]$Matches.Name) "generated Cost display name $typeId"
        Assert-Equal ([int]$csvByTypeId[$typeId].Rarity) $rarity "generated Cost rarity $typeId"
        Assert-Equal ([int]$csvByTypeId[$typeId].FinalBaseCost) $cost "generated Cost value $typeId"
        if ($rarity -eq 1) {
            Assert-Condition ($cost -ge 2 -and $cost -le 40) "Generated R1 TypeId '$typeId' Cost '$cost' is outside 2..40."
        }
        else {
            Assert-Condition ($cost -ge 5 -and $cost -le 40) "Generated R$rarity TypeId '$typeId' Cost '$cost' is outside 5..40."
        }
        $tableTypeIds.Add($typeId)
    }
    Assert-Equal 94 $tableTypeIds.Count 'generated Cost data row count'
    Assert-Equal 94 @($tableTypeIds | Sort-Object -Unique).Count 'generated Cost unique TypeId count'
    Assert-Equal (@($csvRows.TypeId | Sort-Object { [int]$_ }) -join '/') (@($tableTypeIds) -join '/') 'generated Cost numeric TypeId order and formal CSV set'
    foreach ($nonShopTypeId in @('1137', '1138', '2033', '5504', '10002')) {
        Assert-Condition ($nonShopTypeId -notin $tableTypeIds) "Generated Cost table contains non-shop TypeId '$nonShopTypeId'."
    }

    $firstSnapshot = Get-FileIntegritySnapshot -Path $fixtureBondSpecPath
    & $powerShellPath -NoProfile -ExecutionPolicy Bypass -File $updaterPath `
        -BondSpecPath $fixtureBondSpecPath `
        -CostCsvPath $CostCsvPath | Out-Null
    Assert-Equal 0 $LASTEXITCODE 'second Temp BONDS Cost-table updater exit code'
    Assert-FileIntegritySnapshotEqual -Expected $firstSnapshot -Actual (Get-FileIntegritySnapshot -Path $fixtureBondSpecPath) -Name 'second Temp BONDS Cost-table byte idempotence'

    $crlfSourcePath = Join-Path $fixtureRoot 'BONDS_SPEC_CRLF.md'
    $crlfOutputPath = Join-Path $fixtureRoot 'BONDS_SPEC_CRLF_OUTPUT.md'
    $crlfSourceText = $fixtureText.Replace("`n", "`r`n")
    [System.IO.File]::WriteAllText($crlfSourcePath, $crlfSourceText, $utf8)
    $crlfSourceSnapshot = Get-FileIntegritySnapshot -Path $crlfSourcePath
    & $powerShellPath -NoProfile -ExecutionPolicy Bypass -File $updaterPath `
        -BondSpecPath $crlfSourcePath `
        -CostCsvPath $CostCsvPath `
        -OutputPath $crlfOutputPath | Out-Null
    Assert-Equal 0 $LASTEXITCODE 'CRLF OutputPath Temp BONDS updater exit code'
    Assert-FileIntegritySnapshotEqual -Expected $crlfSourceSnapshot -Actual (Get-FileIntegritySnapshot -Path $crlfSourcePath) -Name 'CRLF OutputPath source BONDS'
    $crlfOutputBytes = [System.IO.File]::ReadAllBytes($crlfOutputPath)
    $crlfOutputText = $utf8.GetString($crlfOutputBytes)
    Assert-Condition ($crlfOutputText.Contains("`r`n") -and -not [regex]::IsMatch($crlfOutputText, '(?<!\r)\n')) 'OutputPath Temp BONDS must preserve CRLF-only newlines.'
    Assert-Condition ($crlfOutputBytes.Length -lt 3 -or -not ($crlfOutputBytes[0] -eq 0xEF -and $crlfOutputBytes[1] -eq 0xBB -and $crlfOutputBytes[2] -eq 0xBF)) 'OutputPath Temp BONDS must be UTF-8 without BOM.'
    foreach ($path in @($crlfSourcePath, $crlfOutputPath)) {
        $item = Get-Item -LiteralPath $path -Force
        Assert-Condition (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -eq 0) "OutputPath fixture '$path' must be an ordinary file, not a reparse point."
    }

    $markerBlockWithTrailingNewline = $firstText.Substring($startIndex, $regionEnd - $startIndex)
    $firstDataLine = $regionLines[4]
    $secondDataLine = $regionLines[5]
    $arbitraryRegion = $startMarker + $newline + 'arbitrary text' + $newline + $endMarker
    $extraRowRegion = $regionText.Replace($endMarker, $firstDataLine + $newline + $endMarker)
    $missingRowRegion = $regionText.Replace($firstDataLine + $newline, '')
    $duplicateTypeIdRegion = $regionText.Replace($secondDataLine, $firstDataLine)
    $outOfOrderRegion = $regionText.Replace($firstDataLine, '__FIRST_DATA_LINE__').Replace($secondDataLine, $firstDataLine).Replace('__FIRST_DATA_LINE__', $secondDataLine)
    Assert-Condition ($firstDataLine -match '^\| (?<TypeId>[1-9][0-9]*) \|') 'Generated first Cost data row is malformed before the legacy-value fixture.'
    $legacyValueDataLine = "| $([string]$Matches.TypeId) | Legacy Display Name | 4 | 30 |"
    $legacyValueRoot = Join-Path $fixtureRoot ('LegacyValues-' + [guid]::NewGuid().ToString('N'))
    [System.IO.Directory]::CreateDirectory($legacyValueRoot) | Out-Null
    $legacyValueBondPath = Join-Path $legacyValueRoot 'BONDS_SPEC.md'
    [System.IO.File]::WriteAllText($legacyValueBondPath, $firstText.Replace($firstDataLine, $legacyValueDataLine), $utf8)
    & $powerShellPath -NoProfile -ExecutionPolicy Bypass -File $updaterPath `
        -BondSpecPath $legacyValueBondPath `
        -CostCsvPath $CostCsvPath | Out-Null
    Assert-Equal 0 $LASTEXITCODE 'legal legacy-value existing Cost-table updater exit code'
    Assert-Equal ([System.Convert]::ToBase64String($firstBytes)) ([System.Convert]::ToBase64String([System.IO.File]::ReadAllBytes($legacyValueBondPath))) 'legal legacy-value existing Cost-table normalized output'
    Assert-Equal 1 @(Get-ChildItem -LiteralPath $legacyValueRoot -File).Count 'legal legacy-value existing Cost-table output file count'

    $invalidCases = [ordered]@{
        DuplicateStart = $firstText.Replace($startMarker, $startMarker + $newline + $startMarker)
        DuplicateEnd = $firstText.Replace($endMarker, $endMarker + $newline + $endMarker)
        MissingStart = $firstText.Replace($startMarker, '')
        MissingEnd = $firstText.Replace($endMarker, '')
        Reversed = $fixtureText.Insert($fixtureText.IndexOf($nonShopHeader, [System.StringComparison]::Ordinal), $endMarker + $newline + $startMarker + $newline)
        Malformed = $firstText.Replace($startMarker, '<!-- UNIT-COST-TABLE:STAR -->')
        OutsideBoundary = $fixtureText + $newline + $markerBlockWithTrailingNewline
        ArbitraryText = $firstText.Replace($regionText, $arbitraryRegion)
        WrongHeading = $firstText.Replace($costHeading, '## Wrong Cost Heading')
        WrongColumns = $firstText.Replace($costColumns, '| Wrong | Cost | Columns | Here |')
        ExtraRow = $firstText.Replace($regionText, $extraRowRegion)
        MissingRow = $firstText.Replace($regionText, $missingRowRegion)
        DuplicateTypeId = $firstText.Replace($regionText, $duplicateTypeIdRegion)
        OutOfOrderTypeId = $firstText.Replace($regionText, $outOfOrderRegion)
    }
    foreach ($caseName in $invalidCases.Keys) {
        $caseRoot = Join-Path $fixtureRoot ($caseName + '-' + [guid]::NewGuid().ToString('N'))
        [System.IO.Directory]::CreateDirectory($caseRoot) | Out-Null
        $caseBondSpecPath = Join-Path $caseRoot 'BONDS_SPEC.md'
        [System.IO.File]::WriteAllText($caseBondSpecPath, [string]$invalidCases[$caseName], $utf8)
        $caseSnapshot = Get-FileIntegritySnapshot -Path $caseBondSpecPath
        $previousErrorActionPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            & $powerShellPath -NoProfile -ExecutionPolicy Bypass -File $updaterPath `
                -BondSpecPath $caseBondSpecPath `
                -CostCsvPath $CostCsvPath 2>$null | Out-Null
            $caseExitCode = $LASTEXITCODE
        }
        finally {
            $ErrorActionPreference = $previousErrorActionPreference
        }
        Assert-Condition ($caseExitCode -ne 0) "Malformed marker case '$caseName' was accepted."
        Assert-FileIntegritySnapshotEqual -Expected $caseSnapshot -Actual (Get-FileIntegritySnapshot -Path $caseBondSpecPath) -Name "malformed marker case $caseName"
        Assert-Equal 1 @(Get-ChildItem -LiteralPath $caseRoot -File).Count "malformed marker case $caseName output file count"
    }

    $invalidCsvCases = [ordered]@{}
    $invalidCsvCases['Duplicate'] = @($csvRows | ForEach-Object { $_ | Select-Object * }) + @($csvRows[0] | Select-Object *)
    $invalidCsvCases['Missing'] = @($csvRows | Select-Object -First 93 | ForEach-Object { $_ | Select-Object * })
    $nonShopRows = @($csvRows | ForEach-Object { $_ | Select-Object * })
    $nonShopRows[-1].TypeId = '1137'
    $invalidCsvCases['NonShop'] = $nonShopRows
    foreach ($caseDefinition in @(
            [pscustomobject]@{ Name = 'R1BelowMinimum'; TypeId = '1008'; Value = '1' },
            [pscustomobject]@{ Name = 'R2BelowMinimum'; TypeId = '1026'; Value = '4' },
            [pscustomobject]@{ Name = 'AboveMaximum'; TypeId = '10039'; Value = '41' },
            [pscustomobject]@{ Name = 'NonInteger'; TypeId = '1001'; Value = '5.5' }
        )) {
        $caseRows = @($csvRows | ForEach-Object { $_ | Select-Object * })
        @($caseRows | Where-Object TypeId -eq $caseDefinition.TypeId)[0].FinalBaseCost = $caseDefinition.Value
        $invalidCsvCases[$caseDefinition.Name] = $caseRows
    }
    foreach ($caseName in $invalidCsvCases.Keys) {
        $caseRoot = Join-Path $fixtureRoot ('Csv-' + $caseName + '-' + [guid]::NewGuid().ToString('N'))
        [System.IO.Directory]::CreateDirectory($caseRoot) | Out-Null
        $caseBondSpecPath = Join-Path $caseRoot 'BONDS_SPEC.md'
        $caseCsvPath = Join-Path $caseRoot 'cost.csv'
        [System.IO.File]::WriteAllBytes($caseBondSpecPath, $originalBytes)
        [System.IO.File]::WriteAllLines($caseCsvPath, @($invalidCsvCases[$caseName] | ConvertTo-Csv -NoTypeInformation), $utf8)
        $caseBondSnapshot = Get-FileIntegritySnapshot -Path $caseBondSpecPath
        $caseCsvSnapshot = Get-FileIntegritySnapshot -Path $caseCsvPath
        $previousErrorActionPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            & $powerShellPath -NoProfile -ExecutionPolicy Bypass -File $updaterPath `
                -BondSpecPath $caseBondSpecPath `
                -CostCsvPath $caseCsvPath 2>$null | Out-Null
            $caseExitCode = $LASTEXITCODE
        }
        finally {
            $ErrorActionPreference = $previousErrorActionPreference
        }
        Assert-Condition ($caseExitCode -ne 0) "Invalid CSV case '$caseName' was accepted."
        Assert-FileIntegritySnapshotEqual -Expected $caseBondSnapshot -Actual (Get-FileIntegritySnapshot -Path $caseBondSpecPath) -Name "invalid CSV case $caseName BONDS"
        Assert-FileIntegritySnapshotEqual -Expected $caseCsvSnapshot -Actual (Get-FileIntegritySnapshot -Path $caseCsvPath) -Name "invalid CSV case $caseName CSV"
        Assert-Equal 2 @(Get-ChildItem -LiteralPath $caseRoot -File).Count "invalid CSV case $caseName output file count"
    }

    foreach ($collisionCase in @(
            [pscustomobject]@{
                Name = 'CostCsvEqualsBondSpec'
                BondSpecPath = $fixtureBondSpecPath
                CostCsvPath = $fixtureBondSpecPath
                OutputPath = $null
            },
            [pscustomobject]@{
                Name = 'CostCsvEqualsOutput'
                BondSpecPath = $fixtureBondSpecPath
                CostCsvPath = $CostCsvPath
                OutputPath = $CostCsvPath
            }
        )) {
        $collisionBondSnapshot = Get-FileIntegritySnapshot -Path $collisionCase.BondSpecPath
        $collisionCsvSnapshot = Get-FileIntegritySnapshot -Path $collisionCase.CostCsvPath
        $collisionArguments = @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $updaterPath,
            '-BondSpecPath', $collisionCase.BondSpecPath,
            '-CostCsvPath', $collisionCase.CostCsvPath
        )
        if (-not [string]::IsNullOrWhiteSpace($collisionCase.OutputPath)) {
            $collisionArguments += @('-OutputPath', $collisionCase.OutputPath)
        }
        $previousErrorActionPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            & $powerShellPath @collisionArguments 2>$null | Out-Null
            $caseExitCode = $LASTEXITCODE
        }
        finally {
            $ErrorActionPreference = $previousErrorActionPreference
        }
        Assert-Condition ($caseExitCode -ne 0) "Input collision case '$($collisionCase.Name)' was accepted."
        Assert-FileIntegritySnapshotEqual -Expected $collisionBondSnapshot -Actual (Get-FileIntegritySnapshot -Path $collisionCase.BondSpecPath) -Name "input collision case $($collisionCase.Name) BONDS"
        Assert-FileIntegritySnapshotEqual -Expected $collisionCsvSnapshot -Actual (Get-FileIntegritySnapshot -Path $collisionCase.CostCsvPath) -Name "input collision case $($collisionCase.Name) CSV"
    }

    Assert-FileIntegritySnapshotEqual -Expected $realBondSnapshot -Actual (Get-FileIntegritySnapshot -Path $BondSpecPath) -Name 'real BONDS after all Temp Cost-table updater tests'
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

function Get-TestMedian {
    param([Parameter(Mandatory = $true)][decimal[]]$Values)

    Assert-Condition ($Values.Count -gt 0) 'Test median sample is empty.'
    $sorted = @($Values | Sort-Object)
    $middle = [int][Math]::Floor($sorted.Count / 2)
    if (($sorted.Count % 2) -eq 1) {
        return [decimal]$sorted[$middle]
    }
    return ([decimal]$sorted[$middle - 1] + [decimal]$sorted[$middle]) / [decimal]2
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

    $auraRecipient = [pscustomobject]@{
        TypeId = 9901
        DamageType = 'Physical'
        Attack = [decimal]100
        EffectiveAttackIntervalSeconds = [decimal]1
        MaxHitPoints = [decimal]1000
        Defense = [decimal]0
        MagicResistance = [decimal]0
    }
    $auraAttacker = [pscustomobject]@{
        TypeId = 9902
        DamageType = 'Physical'
        Attack = [decimal]100
        EffectiveAttackIntervalSeconds = [decimal]1
    }
    $defenseOnlyAuraGain = Get-AuraPowerPerTarget -Recipients @($auraRecipient) -Attackers @($auraAttacker) -Parameters @{
        WindowSeconds = 20
        AuraDefenseBonus = 100
    }
    $attackDefenseAuraGain = Get-AuraPowerPerTarget -Recipients @($auraRecipient) -Attackers @($auraAttacker) -Parameters @{
        WindowSeconds = 20
        AuraAttackMultiplier = [decimal]1.10
        AuraDefenseBonus = 100
    }
    $expectedAuraOutputRatio = [decimal]110 / [decimal]100
    $expectedAuraDefenseRatio = [decimal]20
    $expectedAttackDefenseAuraGain = [decimal][Math]::Sqrt([double]($expectedAuraOutputRatio * $expectedAuraDefenseRatio)) - [decimal]1
    Assert-Condition ($attackDefenseAuraGain -gt $defenseOnlyAuraGain) 'ATK+DEF aura gain must exceed DEF-only aura gain for the same recipient.'
    Assert-Condition ([Math]::Abs([double]($attackDefenseAuraGain - $expectedAttackDefenseAuraGain)) -lt 0.000000000001) 'ATK+DEF aura gain is not reproducible from actual output and defense ratios.'
    Assert-Condition ($attackDefenseAuraGain -gt 0) '1080-style ATK+DEF aura contribution must be positive.'
    Assert-Throws {
        Get-AuraPowerPerTarget -Recipients @($auraRecipient) -Attackers @($auraAttacker) -Parameters @{
            WindowSeconds = 20
            AuraDefenseBonus = 100
            auraUnsupportedParameter = 1
        }
    } 'case-insensitive unknown aura parameter'

    $pathRoot = [System.IO.Path]::GetFullPath((Join-Path ([System.IO.Path]::GetTempPath()) 'UnitCostPathGuard'))
    Assert-Condition (Test-PathEqualOrUnderRoot -CandidatePath $pathRoot.ToUpperInvariant() -RootPath $pathRoot.ToLowerInvariant()) 'path guard must compare equal roots case-insensitively.'
    Assert-Condition (Test-PathEqualOrUnderRoot -CandidatePath (Join-Path $pathRoot 'nested\output.csv') -RootPath $pathRoot) 'path guard must detect a normalized descendant.'
    Assert-Condition (Test-PathEqualOrUnderRoot -CandidatePath (Join-Path $pathRoot 'nested\..\output.csv') -RootPath $pathRoot) 'path guard must normalize dot segments.'
    Assert-Condition (-not (Test-PathEqualOrUnderRoot -CandidatePath ($pathRoot + '-adjacent\output.csv') -RootPath $pathRoot)) 'path guard must not reject an adjacent prefix directory.'

    Assert-Throws { Get-PhysicalDamage -Attack -1 -Defense 0 } 'negative attack'
    Assert-Throws { Get-EffectiveAttackInterval -Attacker ([pscustomobject]@{ TypeId = 1; EffectiveAttackIntervalSeconds = 0 }) } 'nonpositive attack interval'
    Assert-Throws { Get-OrdinaryAttackDamage -Attacker ([pscustomobject]@{ TypeId = 1; DamageType = 'Unknown'; Attack = 100; EffectiveAttackIntervalSeconds = 1 }) -Defender ([pscustomobject]@{ Defense = 0; MagicResistance = 0 }) } 'unknown damage type'
}

function Test-CostCurveHelpers {
    param([Parameter(Mandatory = $true)][string]$ExporterPath)

    $helperScript = Get-UnitCostHelperScript -ExporterPath $ExporterPath
    . $helperScript `
        -BondSpecPath 'test-only' `
        -StagingRoot 'test-only' `
        -OutputCsvPath 'test-only.csv' `
        -AnalysisOutputPath 'test-only.json'

    $isotonic = @(Get-IsotonicNondecreasingValues -Values ([decimal[]]@(1, 3, 2, 5, 4, 6)))
    $expectedIsotonic = [decimal[]]@(1, 2.5, 2.5, 4.5, 4.5, 6)
    Assert-Equal $expectedIsotonic.Count $isotonic.Count 'synthetic PAVA output count'
    for ($index = 0; $index -lt $expectedIsotonic.Count; $index++) {
        Assert-Equal $expectedIsotonic[$index] ([decimal]$isotonic[$index]) "synthetic PAVA value $index"
    }

    $compressedAlpha = Get-CompressionAlpha -Rarity2Power 1 -Rarity6Power 8
    Assert-Condition ([Math]::Abs([double]$compressedAlpha - ([Math]::Log(4) / [Math]::Log(8))) -lt 0.000000000001) 'compression alpha does not apply ln(4)/ln(M6/M2) above ratio 4.'
    Assert-Equal ([decimal]1) (Get-CompressionAlpha -Rarity2Power 2 -Rarity6Power 8) 'compression alpha at ratio 4'
    Assert-Throws { Get-CompressionAlpha -Rarity2Power 4 -Rarity6Power 4 } 'compression alpha rejects M6 <= M2'

    $baseCosts = @(Get-RarityBaseCosts -IsotonicRarityMedians ([decimal[]]@(0.5, 1, 2, 4, 6, 8)) -CompressionAlpha $compressedAlpha -Rarity6Anchor 28)
    Assert-Equal ([decimal]28) ([decimal]$baseCosts[5]) 'R6 anchor'
    Assert-Condition (([decimal]$baseCosts[5] / [decimal]$baseCosts[1]) -le 4) 'synthetic B6/B2 exceeds 4.'

    Assert-Condition ([Math]::Abs([double](Get-WithinTierFactor -Power 1.5 -IsotonicRarityMedianPower 1) - 1.2754245006257907) -lt 0.000000000001) 'within-tier factor does not use exponent 0.6.'
    Assert-Equal ([decimal]0.75) (Get-WithinTierFactor -Power 0.01 -IsotonicRarityMedianPower 1) 'within-tier lower clamp'
    Assert-Equal ([decimal]1.35) (Get-WithinTierFactor -Power 10 -IsotonicRarityMedianPower 1) 'within-tier upper clamp'
    Assert-Equal 11 (ConvertTo-FinalBaseCost -RawCost 10.5) 'away-from-zero midpoint rounding'
    Assert-Equal 5 (ConvertTo-FinalBaseCost -RawCost 4.4) 'final Cost lower clamp'
    Assert-Equal 40 (ConvertTo-FinalBaseCost -RawCost 40.5) 'final Cost upper clamp'

    $syntheticRows = [System.Collections.Generic.List[object]]::new()
    foreach ($rarity in 1..4) {
        $syntheticRows.Add([pscustomobject]@{ TypeId = 9000 + $rarity; Rarity = $rarity; ContinuousPower = [decimal]@(1, 7, 12, 18)[$rarity - 1] })
    }
    for ($index = 0; $index -lt 12; $index++) {
        $syntheticRows.Add([pscustomobject]@{ TypeId = 9100 + $index; Rarity = 5; ContinuousPower = [decimal]23 })
    }
    for ($index = 0; $index -lt 9; $index++) {
        $syntheticRows.Add([pscustomobject]@{ TypeId = 9200 + $index; Rarity = 5; ContinuousPower = [decimal]230 })
    }
    $syntheticRows.Add([pscustomobject]@{ TypeId = 9300; Rarity = 6; ContinuousPower = [decimal]28 })
    $syntheticModel = Get-CostCurveModel -Rows $syntheticRows.ToArray()
    Assert-Condition $syntheticModel.ParameterScanTriggered 'synthetic top-sparsity scan did not trigger.'
    Assert-Equal ([decimal]28) ([decimal]$syntheticModel.SelectedRarity6Anchor) 'synthetic scan preserves default anchor when factor adjustment is sufficient'
    Assert-Equal ([decimal]1.32) ([decimal]$syntheticModel.SelectedMaxWithinTierFactor) 'synthetic scan chooses nearest valid factor cap'
    Assert-Condition ($syntheticModel.HighCostCount -le 8) 'synthetic scan did not enforce top sparsity.'

    $anchorScanRows = [System.Collections.Generic.List[object]]::new()
    foreach ($rarity in 1..5) {
        $anchorScanRows.Add([pscustomobject]@{ TypeId = 9400 + $rarity; Rarity = $rarity; ContinuousPower = [decimal](@(1, 7, 12, 18, 23)[$rarity - 1]) })
    }
    for ($index = 0; $index -lt 12; $index++) {
        $anchorScanRows.Add([pscustomobject]@{ TypeId = 9500 + $index; Rarity = 6; ContinuousPower = [decimal]28 })
    }
    for ($index = 0; $index -lt 9; $index++) {
        $anchorScanRows.Add([pscustomobject]@{ TypeId = 9600 + $index; Rarity = 6; ContinuousPower = [decimal]280 })
    }
    $anchorScanModel = Get-CostCurveModel -Rows $anchorScanRows.ToArray()
    Assert-Condition $anchorScanModel.ParameterScanTriggered 'synthetic anchor scan did not trigger.'
    Assert-Equal ([decimal]26.5) ([decimal]$anchorScanModel.SelectedRarity6Anchor) 'synthetic scan chooses nearest valid reduced anchor'
    Assert-Equal ([decimal]1.15) ([decimal]$anchorScanModel.SelectedMaxWithinTierFactor) 'synthetic anchor scan preserves the required factor reduction'
    Assert-Condition ($anchorScanModel.HighCostCount -le 8) 'synthetic anchor scan did not enforce top sparsity.'
}

function Test-R1CalibrationHelpers {
    param([Parameter(Mandatory = $true)][string]$ExporterPath)

    $helperScript = Get-UnitCostHelperScript -ExporterPath $ExporterPath
    . $helperScript `
        -BondSpecPath 'test-only' `
        -StagingRoot 'test-only' `
        -OutputCsvPath 'test-only.csv' `
        -AnalysisOutputPath 'test-only.json'

    $syntheticTier = @(
        [pscustomobject]@{ TypeId = 1; FinalBaseCost = 5 },
        [pscustomobject]@{ TypeId = 2; FinalBaseCost = 6 },
        [pscustomobject]@{ TypeId = 3; FinalBaseCost = 7 },
        [pscustomobject]@{ TypeId = 4; FinalBaseCost = 8 }
    )
    $formations = @(Get-TierFormationSet -Rows $syntheticTier -Budget 20)
    Assert-Equal 3 $formations.Count 'synthetic formation count'
    Assert-Equal 'Low/Median/High' (@($formations.Kind) -join '/') 'synthetic formation kinds'
    Assert-Condition (@($formations[0].TypeIds | Where-Object { $_ -notin @(1, 2) }).Count -eq 0) 'Low formation escaped the lower Cost half.'
    Assert-Condition (@($formations[2].TypeIds | Where-Object { $_ -notin @(3, 4) }).Count -eq 0) 'High formation escaped the upper Cost half.'

    $matchDefinitions = @(Get-CalibrationMatchDefinitions `
            -Rarity1Rows $syntheticTier `
            -Rarity2Rows $syntheticTier `
            -Budgets @(54, 99, 126, 195))
    Assert-Equal 72 $matchDefinitions.Count 'synthetic calibration match count'
    foreach ($budget in @(54, 99, 126, 195)) {
        Assert-Equal 18 @($matchDefinitions | Where-Object Budget -eq $budget).Count "synthetic match count for budget $budget"
    }

    $strong = [pscustomobject]@{
        TypeId = 10; Attack = 100; DamageType = 'Physical'; EffectiveAttackIntervalSeconds = 1
        MaxHitPoints = 100; Defense = 0; MagicResistance = 0; OutputScenarioMain = 1
        DefenseScenarioMain = 1; EquivalentEntityContribution = 0; ContinuousPower = 1
        PanelPower = 1; AbilityPowerMultiplier = 1; LifeDeduct = 2
    }
    $weak = [pscustomobject]@{
        TypeId = 20; Attack = 10; DamageType = 'Physical'; EffectiveAttackIntervalSeconds = 1
        MaxHitPoints = 10; Defense = 0; MagicResistance = 0; OutputScenarioMain = 1
        DefenseScenarioMain = 1; EquivalentEntityContribution = 0; ContinuousPower = 1
        PanelPower = 1; AbilityPowerMultiplier = 1; LifeDeduct = 1
    }
    $match = Invoke-FocusFireMatch -Rarity1Rows @($strong) -Rarity2Rows @($weak) -Direction HighValueFirst -EventLimit 8
    Assert-Equal 'R1' ([string]$match.Winner) 'synthetic focus-fire winner'
    Assert-Equal 0 ([int]$match.R2RemainingEntityCount) 'synthetic death removal'
    Assert-Condition ([int]$match.EventCount -le 8) 'synthetic match exceeded its event cap.'
    Assert-Throws {
        Invoke-FocusFireMatch -Rarity1Rows @($strong) -Rarity2Rows @($weak) -Direction HighValueFirst -EventLimit 0
    } 'fixed match event cap'

    $summoner = [pscustomobject]@{
        TypeId = 30; Attack = 100; DamageType = 'Physical'; EffectiveAttackIntervalSeconds = 1
        MaxHitPoints = 100; Defense = 0; MagicResistance = 0; OutputScenarioMain = 1
        DefenseScenarioMain = 1; EquivalentEntityContribution = 8; ContinuousPower = 12
        PanelPower = 2; AbilityPowerMultiplier = 2; LifeDeduct = 1
    }
    $summonerEntity = New-CalibrationBattleEntity -Row $summoner
    Assert-Equal ([decimal]3) ([decimal]$summonerEntity.EquivalentEntityCount) 'summon-equivalent entity factor'
    Assert-Equal ([decimal]300) ([decimal]$summonerEntity.RemainingHitPoints) 'summon-equivalent effective HP'
    Assert-Equal ([decimal]300) (Get-CalibrationTeamDps -Attackers @($summonerEntity) -Target (New-CalibrationBattleEntity -Row $weak)) 'summon-equivalent actual DPS'

    $pathUnit = [pscustomobject]@{
        TypeId = 40; Attack = 0; DamageType = 'None'; EffectiveAttackIntervalSeconds = 1
        MaxHitPoints = 100; Defense = 0; MagicResistance = 0; OutputScenarioMain = 1
        DefenseScenarioMain = 2; EquivalentEntityContribution = 20; ContinuousPower = 20
        PanelPower = $null; AbilityPowerMultiplier = 1; LifeDeduct = 1
    }
    $pathEntity = New-CalibrationBattleEntity -Row $pathUnit
    Assert-Equal ([decimal]1) ([decimal]$pathEntity.EquivalentEntityCount) 'path unit equivalent entity factor'
    Assert-Equal ([decimal]200) ([decimal]$pathEntity.RemainingHitPoints) 'path unit effective HP'
    Assert-Equal ([decimal]0) (Get-CalibrationTeamDps -Attackers @($pathEntity) -Target (New-CalibrationBattleEntity -Row $weak)) 'path unit does not fabricate DPS'

    $selectedCandidate = Select-R1CalibrationCandidate -Candidates @(
        [pscustomobject]@{ Kappa = [decimal]1.10; Rarity1WinRate = [decimal]0.50 },
        [pscustomobject]@{ Kappa = [decimal]0.90; Rarity1WinRate = [decimal]0.50 },
        [pscustomobject]@{ Kappa = [decimal]1.00; Rarity1WinRate = [decimal]0.51 },
        [pscustomobject]@{ Kappa = [decimal]0.50; Rarity1WinRate = [decimal]0.30 }
    )
    Assert-Equal ([decimal]0.90) ([decimal]$selectedCandidate.Kappa) 'kappa candidate tie break'

    $lowerFloorRows = @(Get-R1CalibratedRows -Rows @(
                [pscustomobject]@{ TypeId = 50; Rarity = 1; FinalBaseCost = 5 },
                [pscustomobject]@{ TypeId = 60; Rarity = 2; FinalBaseCost = 5 }
            ) -Kappa ([decimal]0.37))
    Assert-Equal 2 ([int]$lowerFloorRows[0].FinalBaseCost) 'R1 calibration-only lower Cost bound'
    Assert-Equal 5 ([int]$lowerFloorRows[1].FinalBaseCost) 'R2 retains global lower Cost bound'
    Assert-Equal ([decimal]0.37) ([decimal]$lowerFloorRows[0].R1CalibrationKappa) 'R1 lower-range kappa is exported'

    $kappaCandidates = @(Get-R1CalibrationKappaCandidates)
    Assert-Equal 150 $kappaCandidates.Count 'R1 calibration kappa candidate count'
    Assert-Equal ([decimal]0.01) ([decimal]$kappaCandidates[0]) 'R1 calibration minimum kappa'
    Assert-Equal ([decimal]1.50) ([decimal]$kappaCandidates[$kappaCandidates.Count - 1]) 'R1 calibration maximum kappa'
}

function Test-EliteAndEconomyHelpers {
    param([Parameter(Mandatory = $true)][string]$ExporterPath)

    $helperScript = Get-UnitCostHelperScript -ExporterPath $ExporterPath
    . $helperScript `
        -BondSpecPath 'test-only' `
        -StagingRoot 'test-only' `
        -OutputCsvPath 'test-only.csv' `
        -AnalysisOutputPath 'test-only.json'

    $genericPlan = Get-EliteResourcePlan `
        -DirectoryNames @('1001_bigbo', '1001_bigbo_2') `
        -TypeId '1001' `
        -E0ResourceDirectory '1001_bigbo'
    Assert-Equal '1001_bigbo_2' ([string]$genericPlan.Elite2ResourceDirectory) 'generic E2 dedicated directory'
    Assert-Equal 'Dedicated' ([string]$genericPlan.Elite2ResourceSource) 'generic E2 dedicated source'
    Assert-Equal '1001_bigbo_2' ([string]$genericPlan.Elite3ResourceDirectory) 'generic E3 nearest-lower inheritance'
    Assert-Equal 'InheritedE2' ([string]$genericPlan.Elite3ResourceSource) 'generic E3 inherited source'

    $fallbackPlan = Get-EliteResourcePlan `
        -DirectoryNames @('1008_ghost') `
        -TypeId '1008' `
        -E0ResourceDirectory '1008_ghost'
    Assert-Equal '1008_ghost' ([string]$fallbackPlan.Elite2ResourceDirectory) 'missing E2 inherits E0'
    Assert-Equal 'InheritedE0' ([string]$fallbackPlan.Elite2ResourceSource) 'missing E2 source'
    Assert-Equal '1008_ghost' ([string]$fallbackPlan.Elite3ResourceDirectory) 'missing E3 inherits nearest lower'
    Assert-Equal 'InheritedE2' ([string]$fallbackPlan.Elite3ResourceSource) 'missing E3 nearest-lower source'

    $reversePlan = Get-EliteResourcePlan `
        -DirectoryNames @('1322_wdgyht', '1322_wdgyht_2', '1322_wdgyht_2_2') `
        -TypeId '1322' `
        -E0ResourceDirectory '1322_wdgyht_2'
    Assert-Equal '1322_wdgyht' ([string]$reversePlan.Elite2ResourceDirectory) '1322 reverse E2 mapping'
    Assert-Equal 'Dedicated' ([string]$reversePlan.Elite2ResourceSource) '1322 reverse E2 source'
    Assert-Equal '1322_wdgyht' ([string]$reversePlan.Elite3ResourceDirectory) '1322 E3 nearest-lower inheritance'
    Assert-Equal 'InheritedE2' ([string]$reversePlan.Elite3ResourceSource) '1322 reverse E3 source'

    $e1 = Get-EliteStageCalculation `
        -EliteLevel 1 `
        -E0ContinuousPower ([decimal]12) `
        -E0Cost 4 `
        -E0PanelPower ([decimal]10) `
        -StagePanelPower ([decimal]10) `
        -AbilityPowerMultiplier ([decimal]1.1) `
        -EquivalentEntityContribution ([decimal]1) `
        -ResourceSource 'NoDedicatedPanel'
    Assert-Equal ([decimal]12) ([decimal]$e1.ContinuousPowerPerBody) 'E1 per-body power equals E0'
    Assert-Equal ([decimal]24) ([decimal]$e1.TotalPower) 'E1 total power'
    Assert-Equal 8 ([int]$e1.TotalCost) 'E1 total Cost'
    Assert-Equal ([decimal]3) ([decimal]$e1.PowerPerCostRatio) 'E1 efficiency equals E0'

    $e2Fallback = Get-EliteStageCalculation `
        -EliteLevel 2 `
        -E0ContinuousPower ([decimal]12) `
        -E0Cost 4 `
        -E0PanelPower ([decimal]10) `
        -StagePanelPower ([decimal]10) `
        -AbilityPowerMultiplier ([decimal]1.1) `
        -EquivalentEntityContribution ([decimal]1) `
        -ResourceSource 'InheritedE0'
    Assert-Equal ([decimal]12) ([decimal]$e2Fallback.ContinuousPowerPerBody) 'no-dedicated E2 per-body power equals E0'
    Assert-Equal ([decimal]36) ([decimal]$e2Fallback.TotalPower) 'E2 total power'
    Assert-Equal 12 ([int]$e2Fallback.TotalCost) 'E2 total Cost'
    Assert-Equal ([decimal]3) ([decimal]$e2Fallback.PowerPerCostRatio) 'no-dedicated E2 efficiency equals E0'

    $dedicatedStage = Get-EliteStageCalculation `
        -EliteLevel 3 `
        -E0ContinuousPower ([decimal]12) `
        -E0Cost 4 `
        -E0PanelPower ([decimal]10) `
        -StagePanelPower ([decimal]14) `
        -AbilityPowerMultiplier ([decimal]1.1) `
        -EquivalentEntityContribution ([decimal]1) `
        -ResourceSource 'Dedicated'
    Assert-Condition ([decimal]$dedicatedStage.ContinuousPowerPerBody -gt 0) 'dedicated stage power must be positive.'
    Assert-Condition (-not [double]::IsNaN([double]$dedicatedStage.PowerPerCostRatio) -and -not [double]::IsInfinity([double]$dedicatedStage.PowerPerCostRatio)) 'dedicated stage efficiency must be finite.'

    $inheritedE3Stage = Get-EliteStageCalculation `
        -EliteLevel 3 `
        -E0ContinuousPower ([decimal]12) `
        -E0Cost 4 `
        -E0PanelPower ([decimal]10) `
        -StagePanelPower ([decimal]14) `
        -AbilityPowerMultiplier ([decimal]1.1) `
        -EquivalentEntityContribution ([decimal]1) `
        -ResourceSource 'InheritedE2'
    Assert-Equal ([decimal]16.4) ([decimal]$inheritedE3Stage.ContinuousPowerPerBody) 'E3 inherits nearest-lower E2 panel power'
    Assert-Equal ([decimal]4.1) ([decimal]$inheritedE3Stage.PowerPerCostRatio) 'E3 inherited E2 absolute efficiency'

    $economy = Get-EconomyConstraintGrid -Rows @(
        [pscustomobject]@{ Rarity = 1; FinalBaseCost = 2 },
        [pscustomobject]@{ Rarity = 2; FinalBaseCost = 4 },
        [pscustomobject]@{ Rarity = 3; FinalBaseCost = 6 },
        [pscustomobject]@{ Rarity = 4; FinalBaseCost = 8 },
        [pscustomobject]@{ Rarity = 5; FinalBaseCost = 10 },
        [pscustomobject]@{ Rarity = 6; FinalBaseCost = 12 }
    )
    Assert-Equal 144 @($economy.Rows).Count 'economy grid row count'
    Assert-Equal '34/76/126/184/250/324' (@($economy.GoldBudgets) -join '/') 'economy Gold budgets'
    Assert-Equal '54/99/126/195' (@($economy.CostBudgets) -join '/') 'economy Cost budgets'
    foreach ($row in @($economy.Rows)) {
        Assert-Condition ([decimal]$row.Quantity -eq [Math]::Floor([decimal]$row.Quantity) -and [decimal]$row.Quantity -ge 0) 'economy quantity must be a nonnegative integer.'
    }
    $economyCase = @($economy.Rows | Where-Object { [int]$_.GoldBudget -eq 34 -and [int]$_.CostBudget -eq 54 })
    Assert-Equal 6 $economyCase.Count 'economy 34/54 rarity count'
    $r1 = @($economyCase | Where-Object Rarity -eq 1)[0]
    $r2 = @($economyCase | Where-Object Rarity -eq 2)[0]
    $r6 = @($economyCase | Where-Object Rarity -eq 6)[0]
    Assert-Equal '34/27/27/Cost' "$($r1.GoldLimitedQuantity)/$($r1.CostLimitedQuantity)/$($r1.Quantity)/$($r1.Limiter)" 'economy 34/54 R1'
    Assert-Equal '17/13/13/Cost' "$($r2.GoldLimitedQuantity)/$($r2.CostLimitedQuantity)/$($r2.Quantity)/$($r2.Limiter)" 'economy 34/54 R2'
    Assert-Equal '5/4/4/Cost' "$($r6.GoldLimitedQuantity)/$($r6.CostLimitedQuantity)/$($r6.Quantity)/$($r6.Limiter)" 'economy 34/54 R6'
    $ratioCase = @($economy.QuantityRatios | Where-Object { [int]$_.GoldBudget -eq 34 -and [int]$_.CostBudget -eq 54 })[0]
    Assert-Equal ([decimal]27 / [decimal]13) ([decimal]$ratioCase.R1ToR2) 'economy 34/54 R1/R2 quantity ratio'
    Assert-Equal ([decimal]3.25) ([decimal]$ratioCase.R2ToR6) 'economy 34/54 R2/R6 quantity ratio'
}

function Test-DirectVariantPanelPower {
    param(
        [Parameter(Mandatory = $true)][string]$ExporterPath,
        [Parameter(Mandatory = $true)][string]$StagingRoot,
        [Parameter(Mandatory = $true)][object[]]$Rows,
        [Parameter(Mandatory = $true)][System.Collections.IDictionary]$DamageTypeOverrides,
        [Parameter(Mandatory = $true)]$CombatModel
    )

    $variantStagingRoot = $StagingRoot
    $helperScript = Get-UnitCostHelperScript -ExporterPath $ExporterPath
    . $helperScript `
        -BondSpecPath 'test-only' `
        -StagingRoot 'test-only' `
        -OutputCsvPath 'test-only.csv' `
        -AnalysisOutputPath 'test-only.json'
    $allowedVariantDamageTypes = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($damageType in @('Physical', 'Magic', 'True', 'None')) {
        [void]$allowedVariantDamageTypes.Add($damageType)
    }
    $e0VariantSample = @($Rows | Where-Object PanelModelStatus -eq 'BaseOrdinaryCombat')
    foreach ($variantCase in @(
            [pscustomobject]@{ TypeId = '1006'; ResourceDirectory = '1006_shield_2'; CsvProperty = 'Elite2PanelPower' },
            [pscustomobject]@{ TypeId = '1006'; ResourceDirectory = '1006_shield_3'; CsvProperty = 'Elite3PanelPower' },
            [pscustomobject]@{ TypeId = '1322'; ResourceDirectory = '1322_wdgyht'; CsvProperty = 'Elite2PanelPower' }
        )) {
        $variantRecord = Get-ResourceCombatRecord `
            -ResourceDirectory (Get-Item -LiteralPath (Join-Path $variantStagingRoot $variantCase.ResourceDirectory)) `
            -TypeId $variantCase.TypeId `
            -DamageTypeOverrides $DamageTypeOverrides `
            -AllowedDamageTypes $allowedVariantDamageTypes
        Assert-Equal $variantCase.ResourceDirectory ([string]$variantRecord.ResourceDirectory) "Type ID $($variantCase.TypeId) direct variant level-0 resource"
        $variantPanelMetrics = Get-CombatPanelMetrics `
            -CombatRecord $variantRecord `
            -Defenders $e0VariantSample `
            -Attackers $e0VariantSample `
            -DpsLowerBound ([decimal]$CombatModel.DpsWinsorization.P5) `
            -DpsUpperBound ([decimal]$CombatModel.DpsWinsorization.P95) `
            -TtdLowerBound ([decimal]$CombatModel.TtdWinsorization.P5) `
            -TtdUpperBound ([decimal]$CombatModel.TtdWinsorization.P95) `
            -OutputReference ([decimal]$CombatModel.OutputReference) `
            -DefenseReference ([decimal]$CombatModel.DefenseReference)
        $variantRow = @($Rows | Where-Object TypeId -eq $variantCase.TypeId)[0]
        Assert-Equal ([decimal]$variantPanelMetrics.PanelPower) ([decimal]$variantRow.($variantCase.CsvProperty)) "Type ID $($variantCase.TypeId) $($variantCase.CsvProperty) direct level-0 reproducibility"
    }
}

function Get-ShopFixtureResourceDirectories {
    param(
        [Parameter(Mandatory = $true)][string]$BondSpecPath,
        [Parameter(Mandatory = $true)][string]$StagingRoot
    )

    $shopHeader = [string]::Concat('## ', [char]0x5546, [char]0x5E97, [char]0x5355, [char]0x4F4D, [char]0xFF08, '94', [char]0xFF09)
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
    Assert-Equal 94 $typeIds.Count 'shop TypeId count for external snapshot'

    $nonShopHeader = [string]::Concat('## ', [char]0x975E, [char]0x5546, [char]0x5E97, [char]0x5355, [char]0x4F4D, [char]0xFF08, '5', [char]0xFF09)
    $nonShopHeaderIndexes = @(for ($lineIndex = 0; $lineIndex -lt $lines.Count; $lineIndex++) { if ($lines[$lineIndex] -ceq $nonShopHeader) { $lineIndex } })
    Assert-Equal 1 $nonShopHeaderIndexes.Count 'non-shop header count for external snapshot'
    $nonShopIndex = $nonShopHeaderIndexes[0] + 1
    while ([string]::IsNullOrWhiteSpace($lines[$nonShopIndex])) { $nonShopIndex++ }
    Assert-Equal '```text' $lines[$nonShopIndex] 'non-shop code block marker for external snapshot'
    $nonShopIndex++
    $nonShopTypeIds = [System.Collections.Generic.List[string]]::new()
    while ($lines[$nonShopIndex] -cne '```') {
        foreach ($typeId in $lines[$nonShopIndex].Split(',')) {
            if (-not [string]::IsNullOrWhiteSpace($typeId)) {
                $nonShopTypeIds.Add($typeId.Trim())
            }
        }
        $nonShopIndex++
    }
    Assert-Equal '1137/1138/2033/5504/10002' ($nonShopTypeIds -join '/') 'non-shop TypeIds for external snapshot'

    $allDirectories = @(Get-ChildItem -LiteralPath $StagingRoot -Directory)
    $allDirectoryNames = @($allDirectories.Name)
    $directories = [System.Collections.Generic.List[string]]::new()
    foreach ($typeId in @($typeIds)) {
        if ($typeId -ceq '1322') {
            $directories.Add('1322_wdgyht_2')
        }
        else {
            $matches = @($allDirectories | Where-Object { $_.Name -like "${typeId}_*" -and $_.Name -notmatch '_[23]$' })
            Assert-Equal 1 $matches.Count "base resource directory count for TypeId $typeId external snapshot"
            $directories.Add($matches[0].Name)
        }
        $e0ResourceDirectory = $directories[$directories.Count - 1]
        $elite2DedicatedName = if ($typeId -ceq '1322') { '1322_wdgyht' } else { "${e0ResourceDirectory}_2" }
        $elite3DedicatedName = if ($typeId -ceq '1322') { '1322_wdgyht_3' } else { "${e0ResourceDirectory}_3" }
        if ($elite2DedicatedName -in $allDirectoryNames) {
            $directories.Add($elite2DedicatedName)
        }
        if ($elite3DedicatedName -in $allDirectoryNames) {
            $directories.Add($elite3DedicatedName)
        }
    }
    foreach ($typeId in @($nonShopTypeIds)) {
        $matches = @($allDirectories | Where-Object { $_.Name -like "${typeId}_*" -and $_.Name -notmatch '_[23]$' })
        Assert-Equal 1 $matches.Count "base non-shop resource directory count for TypeId $typeId external snapshot"
        $directories.Add($matches[0].Name)
    }
    Assert-Equal 181 @($directories | Sort-Object -Unique).Count 'shop/non-shop plus actual elite-resource snapshot directory count'
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
    foreach ($typeId in @('1058', '1078', '1080', '1081', '1083', '1095', '1281', '1502')) {
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
    Assert-Equal 52 $coveredAbilityTypeIds.Count 'marked and required-unmarked ability TypeId count'
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
    Assert-Equal 99 $validTypeIds.Count 'valid ability TypeId count'
    foreach ($typeId in @($scenarioKeys + $riskKeys + @($abilityInput.DamageTypeOverrides.Keys | ForEach-Object { [string]$_ }) | Sort-Object -Unique)) {
        Assert-Condition ($typeId -in $validTypeIds) "Ability input contains invalid top-level TypeId '$typeId'."
    }
    Assert-Condition ('1021' -notin @($scenarioKeys + $riskKeys)) 'removed TypeId 1021 remains in ability inputs.'

    $unit1080 = $abilityInput.UnitScenarios['1080']
    Assert-Equal 'GlobalSupportAura20Seconds' ([string]$unit1080.ModelKind) '1080 global aura model'
    Assert-Equal ([decimal]1.10) ([decimal]$unit1080.Parameters.AuraAttackMultiplier) '1080 aura attack multiplier'
    Assert-Equal ([decimal]100) ([decimal]$unit1080.Parameters.AuraDefenseBonus) '1080 aura defense bonus'
    Assert-Equal '1/3/5' (@(
            [int]$unit1080.Parameters.AuraTargetsLow,
            [int]$unit1080.Parameters.AuraTargetsMain,
            [int]$unit1080.Parameters.AuraTargetsHigh
        ) -join '/') '1080 aura target sensitivity'
    Assert-Condition ((@($abilityInput.ExplicitRiskOnly['1080']) -join '|') -match '1078' -and (@($abilityInput.ExplicitRiskOnly['1080']) -join '|') -match '1083' -and (@($abilityInput.ExplicitRiskOnly['1080']) -join '|') -match 'double') '1080 tactical-command synergy is not explicitly risk-only/no-double-pricing.'

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
    foreach ($typeId in @('1058', '1078', '1080', '1081', '1083', '1095', '1281', '1502')) {
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

function Get-FileIntegritySnapshot {
    param([Parameter(Mandatory = $true)][string]$Path)

    $file = Get-Item -LiteralPath $Path -ErrorAction Stop
    return [pscustomobject][ordered]@{
        FullPath = $file.FullName
        Length = $file.Length
        Sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    }
}

function Assert-FileIntegritySnapshotEqual {
    param(
        [Parameter(Mandatory = $true)]$Expected,
        [Parameter(Mandatory = $true)]$Actual,
        [Parameter(Mandatory = $true)][string]$Name
    )

    Assert-Equal $Expected.FullPath $Actual.FullPath "$Name path"
    Assert-Equal $Expected.Length $Actual.Length "$Name length"
    Assert-Equal $Expected.Sha256 $Actual.Sha256 "$Name SHA-256"
}

function New-JsonFixtureStagingRoot {
    param(
        [Parameter(Mandatory = $true)][string]$SourceStagingRoot,
        [Parameter(Mandatory = $true)][string]$DestinationRoot,
        [Parameter(Mandatory = $true)][object[]]$SourceSnapshot
    )

    [System.IO.Directory]::CreateDirectory($DestinationRoot) | Out-Null
    foreach ($sourceFile in $SourceSnapshot) {
        $destinationPath = Join-Path $DestinationRoot $sourceFile.RelativePath
        [System.IO.Directory]::CreateDirectory((Split-Path -Parent $destinationPath)) | Out-Null
        [System.IO.File]::Copy((Join-Path $SourceStagingRoot $sourceFile.RelativePath), $destinationPath, $false)
    }
    $fixtureSnapshot = Get-UnitJsonSnapshot -StagingRoot $DestinationRoot -ResourceDirectories @($SourceSnapshot | ForEach-Object { Split-Path -Parent $_.RelativePath } | Sort-Object -Unique)
    Assert-UnitJsonSnapshotsEqual -Expected $SourceSnapshot -Actual $fixtureSnapshot -Name 'fixture copy'
}

function New-InvalidDamageTypeStagingRoot {
    param(
        [Parameter(Mandatory = $true)][string]$SourceStagingRoot,
        [Parameter(Mandatory = $true)][string]$DestinationRoot,
        [Parameter(Mandatory = $true)][object[]]$SourceSnapshot,
        [Parameter(Mandatory = $true)][string]$DamageType
    )

    New-JsonFixtureStagingRoot -SourceStagingRoot $SourceStagingRoot -DestinationRoot $DestinationRoot -SourceSnapshot $SourceSnapshot
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

function Test-StagingOutputCollision {
    param(
        [Parameter(Mandatory = $true)][string]$BondSpecPath,
        [Parameter(Mandatory = $true)][string]$AbilityInputPath,
        [Parameter(Mandatory = $true)][string]$StagingRoot,
        [Parameter(Mandatory = $true)][string]$TestRoot,
        [Parameter(Mandatory = $true)][string]$ExporterPath,
        [Parameter(Mandatory = $true)][string]$PowerShellPath,
        [Parameter(Mandatory = $true)][string]$OutputParameterName,
        [Parameter(Mandatory = $true)][string[]]$FixtureResourceDirectories,
        [Parameter(Mandatory = $true)][object[]]$ExternalSnapshot,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Failures
    )

    $fixtureRoot = Join-Path $TestRoot ('staging-output-collision-' + $OutputParameterName + '-' + [guid]::NewGuid().ToString('N'))
    New-JsonFixtureStagingRoot -SourceStagingRoot $StagingRoot -DestinationRoot $fixtureRoot -SourceSnapshot $ExternalSnapshot
    $fixtureSnapshotBefore = Get-UnitJsonSnapshot -StagingRoot $fixtureRoot -ResourceDirectories $FixtureResourceDirectories
    $abilityFixturePath = Join-Path $TestRoot ('staging-output-ability-' + [guid]::NewGuid().ToString('N') + '.psd1')
    [System.IO.File]::Copy($AbilityInputPath, $abilityFixturePath, $false)
    $abilityFixtureBefore = Get-FileIntegritySnapshot -Path $abilityFixturePath
    $outputCsv = Join-Path $TestRoot ('staging-collision-' + [guid]::NewGuid().ToString('N') + '.csv')
    $analysisOutput = Join-Path $TestRoot ('staging-collision-' + [guid]::NewGuid().ToString('N') + '.json')
    $collisionPath = if ($OutputParameterName -ceq 'OutputCsvPath') {
        Join-Path $fixtureRoot '1238_ltmob\unit-source-v1.json'
    }
    else {
        Join-Path $fixtureRoot 'forbidden-analysis.json'
    }
    $collisionExistedBefore = Test-Path -LiteralPath $collisionPath -PathType Leaf
    $collisionBefore = if ($collisionExistedBefore) { Get-FileIntegritySnapshot -Path $collisionPath } else { $null }
    if ($OutputParameterName -ceq 'OutputCsvPath') {
        $outputCsv = $collisionPath
    }
    else {
        $analysisOutput = $collisionPath
    }

    & $PowerShellPath -NoProfile -ExecutionPolicy Bypass -File $ExporterPath `
        -BondSpecPath $BondSpecPath `
        -StagingRoot $fixtureRoot `
        -AbilityInputPath $abilityFixturePath `
        -OutputCsvPath $outputCsv `
        -AnalysisOutputPath $analysisOutput
    $exportExitCode = $LASTEXITCODE

    $fixtureSnapshotAfter = Get-UnitJsonSnapshot -StagingRoot $fixtureRoot -ResourceDirectories $FixtureResourceDirectories
    if ((Get-UnitJsonSnapshotHash -Snapshot $fixtureSnapshotAfter) -cne (Get-UnitJsonSnapshotHash -Snapshot $fixtureSnapshotBefore)) {
        Add-RegressionFailure -Failures $Failures -Message "$OutputParameterName inside StagingRoot modified fixture JSON before rejection."
    }
    if ($collisionExistedBefore) {
        try {
            Assert-FileIntegritySnapshotEqual -Expected $collisionBefore -Actual (Get-FileIntegritySnapshot -Path $collisionPath) -Name "$OutputParameterName staging collision target"
        }
        catch {
            Add-RegressionFailure -Failures $Failures -Message $_.Exception.Message
        }
    }
    elseif (Test-Path -LiteralPath $collisionPath) {
        Add-RegressionFailure -Failures $Failures -Message "$OutputParameterName inside StagingRoot created forbidden target '$collisionPath'."
    }
    try {
        Assert-FileIntegritySnapshotEqual -Expected $abilityFixtureBefore -Actual (Get-FileIntegritySnapshot -Path $abilityFixturePath) -Name "$OutputParameterName staging collision ability fixture"
        Assert-UnitJsonSnapshotsEqual -Expected $ExternalSnapshot -Actual (Get-UnitJsonSnapshot -StagingRoot $StagingRoot -ResourceDirectories $FixtureResourceDirectories) -Name "external staging after $OutputParameterName staging collision"
    }
    catch {
        Add-RegressionFailure -Failures $Failures -Message $_.Exception.Message
    }
    if ($exportExitCode -eq 0) {
        Add-RegressionFailure -Failures $Failures -Message "$OutputParameterName inside StagingRoot was accepted instead of rejected during preflight."
    }
}

function Test-AbilityInputOutputCollision {
    param(
        [Parameter(Mandatory = $true)][string]$BondSpecPath,
        [Parameter(Mandatory = $true)][string]$AbilityInputPath,
        [Parameter(Mandatory = $true)][string]$StagingRoot,
        [Parameter(Mandatory = $true)][string]$TestRoot,
        [Parameter(Mandatory = $true)][string]$ExporterPath,
        [Parameter(Mandatory = $true)][string]$PowerShellPath,
        [Parameter(Mandatory = $true)][string]$OutputParameterName,
        [Parameter(Mandatory = $true)][string[]]$FixtureResourceDirectories,
        [Parameter(Mandatory = $true)][object[]]$ExternalSnapshot,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Failures
    )

    $fixtureRoot = Join-Path $TestRoot ('ability-output-staging-' + $OutputParameterName + '-' + [guid]::NewGuid().ToString('N'))
    New-JsonFixtureStagingRoot -SourceStagingRoot $StagingRoot -DestinationRoot $fixtureRoot -SourceSnapshot $ExternalSnapshot
    $fixtureSnapshotBefore = Get-UnitJsonSnapshot -StagingRoot $fixtureRoot -ResourceDirectories $FixtureResourceDirectories
    $abilityFixturePath = Join-Path $TestRoot ('ability-output-collision-' + $OutputParameterName + '-' + [guid]::NewGuid().ToString('N') + '.psd1')
    [System.IO.File]::Copy($AbilityInputPath, $abilityFixturePath, $false)
    $abilityFixtureBefore = Get-FileIntegritySnapshot -Path $abilityFixturePath
    $outputCsv = Join-Path $TestRoot ('ability-collision-' + [guid]::NewGuid().ToString('N') + '.csv')
    $analysisOutput = Join-Path $TestRoot ('ability-collision-' + [guid]::NewGuid().ToString('N') + '.json')
    if ($OutputParameterName -ceq 'OutputCsvPath') {
        $outputCsv = $abilityFixturePath
    }
    else {
        $analysisOutput = $abilityFixturePath
    }

    & $PowerShellPath -NoProfile -ExecutionPolicy Bypass -File $ExporterPath `
        -BondSpecPath $BondSpecPath `
        -StagingRoot $fixtureRoot `
        -AbilityInputPath $abilityFixturePath `
        -OutputCsvPath $outputCsv `
        -AnalysisOutputPath $analysisOutput
    $exportExitCode = $LASTEXITCODE

    try {
        Assert-FileIntegritySnapshotEqual -Expected $abilityFixtureBefore -Actual (Get-FileIntegritySnapshot -Path $abilityFixturePath) -Name "$OutputParameterName ability input collision target"
    }
    catch {
        Add-RegressionFailure -Failures $Failures -Message $_.Exception.Message
    }
    $fixtureSnapshotAfter = Get-UnitJsonSnapshot -StagingRoot $fixtureRoot -ResourceDirectories $FixtureResourceDirectories
    if ((Get-UnitJsonSnapshotHash -Snapshot $fixtureSnapshotAfter) -cne (Get-UnitJsonSnapshotHash -Snapshot $fixtureSnapshotBefore)) {
        Add-RegressionFailure -Failures $Failures -Message "$OutputParameterName equal to AbilityInputPath modified fixture JSON."
    }
    try {
        Assert-UnitJsonSnapshotsEqual -Expected $ExternalSnapshot -Actual (Get-UnitJsonSnapshot -StagingRoot $StagingRoot -ResourceDirectories $FixtureResourceDirectories) -Name "external staging after $OutputParameterName ability input collision"
    }
    catch {
        Add-RegressionFailure -Failures $Failures -Message $_.Exception.Message
    }
    if ($exportExitCode -eq 0) {
        Add-RegressionFailure -Failures $Failures -Message "$OutputParameterName equal to AbilityInputPath was accepted instead of rejected during preflight."
    }
}

try {
    $testRoot = [System.IO.Path]::GetFullPath((Join-Path $TempRoot 'UnitCostModel'))
    if (-not [string]::IsNullOrWhiteSpace($ExpectedCsvPath)) {
        Assert-Condition (Test-Path -LiteralPath $ExpectedCsvPath -PathType Leaf) "Expected formal CSV '$ExpectedCsvPath' does not exist."
        $ExpectedCsvPath = (Resolve-Path -LiteralPath $ExpectedCsvPath).Path
        $ExpectedCsvPath = Assert-ExpectedCsvOutsideTestRoot -ExpectedCsvPath $ExpectedCsvPath -TestRoot $testRoot
        Test-ExpectedCsvPathGuard -SourceExpectedCsvPath $ExpectedCsvPath -TempRoot $TempRoot
        Test-BondsUnitCostTableUpdater -BondSpecPath $BondSpecPath -CostCsvPath $ExpectedCsvPath -TempRoot $TempRoot
    }

    [System.IO.Directory]::CreateDirectory($testRoot) | Out-Null

    $outputCsv = Join-Path $testRoot 'unit-cost-dataset.csv'
    $analysisOutput = Join-Path $testRoot 'unit-cost-analysis.json'
    Remove-Item -LiteralPath $outputCsv, $analysisOutput -Force -ErrorAction SilentlyContinue

    $exporterPath = Join-Path $PSScriptRoot 'Export-UnitCostDataset.ps1'
    $abilityInputPath = Join-Path $PSScriptRoot 'UnitCostAbilityInputs.psd1'
    $powershellPath = Join-Path $PSHOME 'powershell.exe'
    Test-CombatMetricHelpers -ExporterPath $exporterPath
    Test-CostCurveHelpers -ExporterPath $exporterPath
    Test-R1CalibrationHelpers -ExporterPath $exporterPath
    Test-EliteAndEconomyHelpers -ExporterPath $exporterPath
    $fixtureResourceDirectories = Get-ShopFixtureResourceDirectories -BondSpecPath $BondSpecPath -StagingRoot $StagingRoot
    $externalSnapshotBeforeWorkflow = Get-UnitJsonSnapshot -StagingRoot $StagingRoot -ResourceDirectories $fixtureResourceDirectories
    $bondSpecSnapshotBeforeWorkflow = Get-FileIntegritySnapshot -Path $BondSpecPath
    $abilityInputSnapshotBeforeWorkflow = Get-FileIntegritySnapshot -Path $abilityInputPath
    Assert-Equal 362 $externalSnapshotBeforeWorkflow.Count 'shop/non-shop plus actual elite-resource snapshot JSON file count'
    $shopTypeIds = @(
        $fixtureResourceDirectories |
            ForEach-Object { ($_ -split '_', 2)[0] } |
            Where-Object { $_ -notin @('1137', '1138', '2033', '5504', '10002') } |
            Sort-Object -Unique
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
    Assert-FileIntegritySnapshotEqual -Expected $bondSpecSnapshotBeforeWorkflow -Actual (Get-FileIntegritySnapshot -Path $BondSpecPath) -Name 'BONDS after main successful export'
    Assert-FileIntegritySnapshotEqual -Expected $abilityInputSnapshotBeforeWorkflow -Actual (Get-FileIntegritySnapshot -Path $abilityInputPath) -Name 'ability input after main successful export'
    Write-Host "Verified external staging snapshot unchanged after main successful export: $($externalSnapshotAfterMainExport.Count) JSON files, SHA-256 snapshot $(Get-UnitJsonSnapshotHash -Snapshot $externalSnapshotAfterMainExport)."

    $rows = @(Import-Csv -LiteralPath $outputCsv -Encoding UTF8)
    Assert-Equal 94 $rows.Count 'shop row count'
    Assert-Equal 94 @($rows.TypeId | Sort-Object -Unique).Count 'unique shop TypeId count'
    Assert-Equal 0 @($rows | Where-Object { $_.TypeId -in '1137', '1138', '2033', '5504', '10002' }).Count 'non-shop Cost rows'
    Assert-Equal 0 @($rows | Where-Object TypeId -in @('1000', '1021')).Count 'removed TypeIds 1000/1021'
    Assert-Equal 1 @($rows | Where-Object TypeId -eq '1014').Count 'new TypeId 1014'
    foreach ($property in @(
            'RawMedianDps', 'WinsorizedMedianDps', 'RawMedianTtdSeconds', 'WinsorizedMedianTtdSeconds',
            'OutputReference', 'DefenseReference', 'PanelPower', 'PanelModelStatus',
            'AbilityModelKind', 'OutputScenarioLow', 'OutputScenarioMain', 'OutputScenarioHigh',
            'DefenseScenarioMain', 'EquivalentEntityContribution', 'AbilityPowerMultiplier',
            'ContinuousPower', 'AbilityEvidence', 'RiskFlags', 'ScenarioEventCount',
            'ScenarioEventTimesSeconds', 'ScenarioAttackCount', 'ScenarioSpecialAttackCount',
            'RarityMedianPower', 'IsotonicRarityMedianPower', 'CompressionAlpha',
            'RarityBaseCost', 'WithinTierFactor', 'RawCostBeforeRounding', 'FinalBaseCost',
            'R1InitialCost', 'R1CalibrationKappa', 'R1CalibratedRawCost',
            'Elite1EntityCount', 'Elite1TotalPower', 'Elite1TotalCost', 'Elite1PowerPerCostRatio',
            'Elite2ResourceDirectory', 'Elite2ResourceSource', 'Elite2PanelPower',
            'Elite2ContinuousPowerPerBody', 'Elite2PanelPowerStatus', 'Elite2TotalPower',
            'Elite2TotalCost', 'Elite2PowerPerCostRatio',
            'Elite3ResourceDirectory', 'Elite3ResourceSource', 'Elite3PanelPower',
            'Elite3ContinuousPowerPerBody', 'Elite3PanelPowerStatus', 'Elite3TotalPower',
            'Elite3TotalCost', 'Elite3PowerPerCostRatio', 'EliteEfficiencyRisk'
        )) {
        Assert-Condition ($rows[0].PSObject.Properties.Name -contains $property) "CSV is missing combat metric column '$property'."
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedCsvPath)) {
        $expectedRows = @(Import-Csv -LiteralPath $ExpectedCsvPath -Encoding UTF8)
        Assert-Equal 94 $expectedRows.Count 'formal CSV shop row count'
        Assert-Equal 94 @($expectedRows.TypeId | Sort-Object -Unique).Count 'formal CSV unique TypeId count'
        Assert-Equal ($rows[0].PSObject.Properties.Name -join '/') ($expectedRows[0].PSObject.Properties.Name -join '/') 'formal CSV complete ordered columns'

        $expectedNumericOrder = @($expectedRows | Sort-Object { [int]$_.TypeId } | ForEach-Object { [string]$_.TypeId })
        Assert-Equal ($expectedNumericOrder -join '/') (@($expectedRows | ForEach-Object { [string]$_.TypeId }) -join '/') 'formal CSV stable numeric TypeId ordering'
        $temporaryNumericOrder = @($rows | Sort-Object { [int]$_.TypeId } | ForEach-Object { [string]$_.TypeId })
        Assert-Equal ($temporaryNumericOrder -join '/') (@($rows | ForEach-Object { [string]$_.TypeId }) -join '/') 'temporary CSV stable numeric TypeId ordering'
        $bondShopSet = @($shopTypeIds | Sort-Object { [int]$_ })
        Assert-Equal ($bondShopSet -join '/') (@($expectedRows.TypeId | Sort-Object { [int]$_ }) -join '/') 'formal CSV current BONDS shop TypeId set'

        $expectedNormalizedText = Get-NormalizedUtf8Text -Path $ExpectedCsvPath
        $temporaryNormalizedText = Get-NormalizedUtf8Text -Path $outputCsv
        Assert-Condition ([string]::Equals($expectedNormalizedText, $temporaryNormalizedText, [System.StringComparison]::Ordinal)) 'Formal CSV differs from the independently recomputed Temp CSV after UTF-8 BOM and newline normalization.'

        $expectedAnalysisPath = Get-ExpectedAnalysisPath -ExpectedCsvPath $ExpectedCsvPath
        Assert-Condition (Test-Path -LiteralPath $expectedAnalysisPath -PathType Leaf) "Expected formal analysis '$expectedAnalysisPath' does not exist."
        $formalAnalysisLines = @(Get-Content -LiteralPath $expectedAnalysisPath -Encoding UTF8)
        foreach ($heading in @(
                '## 1. Data integrity',
                '## 2. R2-R6 adjacent power and Cost growth',
                '## 3. R1 kappa and 72-match calibration',
                '### All 72 match details',
                '## 4. Cost distributions',
                '## 5. Cost > 30 audit',
                '## 6. Elite efficiency risks',
                '## 7. Unquantified abilities',
                '## 8. Economy pressure grid',
                '## 9. Verification scope'
            )) {
            Assert-Equal 1 @($formalAnalysisLines | Where-Object { $_ -ceq $heading }).Count "formal analysis heading '$heading'"
        }
        Assert-MarkdownMarkerRowCount -Lines $formalAnalysisLines -MarkerName 'R1_MATCH_ROWS' -ExpectedCount 72
        Assert-MarkdownMarkerRowCount -Lines $formalAnalysisLines -MarkerName 'HIGH_COST_ROWS' -ExpectedCount @($rows | Where-Object { [int]$_.FinalBaseCost -gt 30 }).Count
        Assert-MarkdownMarkerRowCount -Lines $formalAnalysisLines -MarkerName 'ELITE_RISK_ROWS' -ExpectedCount @($rows | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_.EliteEfficiencyRisk) }).Count
        Assert-MarkdownMarkerRowCount -Lines $formalAnalysisLines -MarkerName 'UNQUANTIFIED_ROWS' -ExpectedCount @($rows | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_.RiskFlags) }).Count
        Assert-MarkdownMarkerRowCount -Lines $formalAnalysisLines -MarkerName 'ECONOMY_ROWS' -ExpectedCount 144
        $formalAnalysisText = $formalAnalysisLines -join "`n"
        foreach ($requiredText in @(
                'R6/R2 median final Cost ratio:',
                'Permanent Cost income is unconfirmed.',
                'Unity compilation was not run.',
                'Unity EditMode tests were not run.',
                'Unity PlayMode tests were not run.',
                'A Unity player/platform build was not run.'
            )) {
            Assert-Condition ($formalAnalysisText.Contains($requiredText)) "Formal analysis is missing required text '$requiredText'."
        }
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
    foreach ($typeId in @('1014', '1031', '1039')) {
        $panelOnlyRow = @($rows | Where-Object TypeId -eq $typeId)
        Assert-Equal 1 $panelOnlyRow.Count "panel-only TypeId $typeId count"
        Assert-Equal 'None' ([string]$panelOnlyRow[0].AbilityModelKind) "panel-only TypeId $typeId ability model"
        Assert-Equal ([decimal]1) ([decimal]$panelOnlyRow[0].AbilityPowerMultiplier) "panel-only TypeId $typeId ability multiplier"
        Assert-Equal ([decimal]0) ([decimal]$panelOnlyRow[0].EquivalentEntityContribution) "panel-only TypeId $typeId equivalent contribution"
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

    $rarityAudit = @{}
    $expectedRarityCounts = @{ 1 = 9; 2 = 20; 3 = 14; 4 = 24; 5 = 21; 6 = 6 }
    foreach ($rarity in 1..6) {
        $rarityRows = @($rows | Where-Object { [int]$_.Rarity -eq $rarity })
        Assert-Equal $expectedRarityCounts[$rarity] $rarityRows.Count "R$rarity shop row count"
        $rawMedianPower = Get-TestMedian -Values ([decimal[]]@($rarityRows | ForEach-Object { [decimal]$_.ContinuousPower }))
        $exportedRawMedians = @($rarityRows.RarityMedianPower | ForEach-Object { [decimal]$_ } | Sort-Object -Unique)
        $exportedIsotonicMedians = @($rarityRows.IsotonicRarityMedianPower | ForEach-Object { [decimal]$_ } | Sort-Object -Unique)
        $exportedBaseCosts = @($rarityRows.RarityBaseCost | ForEach-Object { [decimal]$_ } | Sort-Object -Unique)
        Assert-Equal 1 $exportedRawMedians.Count "R$rarity exported raw median count"
        Assert-Condition ([Math]::Abs([double]($rawMedianPower - $exportedRawMedians[0])) -lt 0.000000000001) "R$rarity raw median power is not reproducible."
        Assert-Equal 1 $exportedIsotonicMedians.Count "R$rarity exported isotonic median count"
        Assert-Equal 1 $exportedBaseCosts.Count "R$rarity exported base Cost count"
        $rarityAudit[$rarity] = [pscustomobject]@{
            IsotonicMedian = $exportedIsotonicMedians[0]
            BaseCost = $exportedBaseCosts[0]
            MedianFinalCost = Get-TestMedian -Values ([decimal[]]@($rarityRows | ForEach-Object { [decimal]$_.FinalBaseCost }))
        }
    }
    for ($rarity = 2; $rarity -le 6; $rarity++) {
        Assert-Condition ($rarityAudit[$rarity].IsotonicMedian -ge $rarityAudit[$rarity - 1].IsotonicMedian) "PAVA output decreases from R$($rarity - 1) to R$rarity."
    }
    $exportedAlphas = @($rows.CompressionAlpha | ForEach-Object { [decimal]$_ } | Sort-Object -Unique)
    Assert-Equal 1 $exportedAlphas.Count 'exported compression alpha count'
    $compressionAlpha = $exportedAlphas[0]
    $isotonicRatio = [decimal]$rarityAudit[6].IsotonicMedian / [decimal]$rarityAudit[2].IsotonicMedian
    $expectedAlpha = [decimal][Math]::Min([double]1, [Math]::Log(4) / [Math]::Log([double]$isotonicRatio))
    Assert-Condition ([Math]::Abs([double]($compressionAlpha - $expectedAlpha)) -lt 0.000000000001) 'exported compression alpha is not reproducible from isotonic R2/R6 medians.'
    Assert-Condition ([Math]::Abs([double]([decimal]$rarityAudit[6].BaseCost - 28)) -lt 0.000000000001) 'actual data should retain the default R6 anchor 28.'
    $exportedBaseCostRatio = [decimal]$rarityAudit[6].BaseCost / [decimal]$rarityAudit[2].BaseCost
    Assert-Condition ($exportedBaseCostRatio -le ([decimal]4 + [decimal]0.000000000001)) 'exported B6/B2 exceeds 4 beyond numeric tolerance.'
    $expectedR1Base = [decimal]$rarityAudit[2].BaseCost * [decimal][Math]::Pow(
        [double]([decimal]$rarityAudit[1].IsotonicMedian / [decimal]$rarityAudit[2].IsotonicMedian),
        [double]$compressionAlpha
    )
    Assert-Condition ([Math]::Abs([double]([decimal]$rarityAudit[1].BaseCost - $expectedR1Base)) -lt 0.000000000001) 'R1 initial base Cost does not follow B2 * (M1/M2)^alpha.'

    foreach ($row in $rows) {
        $expectedFactor = [decimal][Math]::Pow(
            [double]([decimal]$row.ContinuousPower / [decimal]$row.IsotonicRarityMedianPower),
            [double][decimal]0.6
        )
        $expectedFactor = [decimal][Math]::Min([double]1.35, [Math]::Max([double]0.75, [double]$expectedFactor))
        Assert-Condition ([Math]::Abs([double]([decimal]$row.WithinTierFactor - $expectedFactor)) -lt 0.000000000001) "Type ID $($row.TypeId) within-tier factor is not reproducible."
        $expectedRawCost = [decimal]$row.RarityBaseCost * [decimal]$row.WithinTierFactor
        Assert-Condition ([Math]::Abs([double]([decimal]$row.RawCostBeforeRounding - $expectedRawCost)) -lt 0.000000000001) "Type ID $($row.TypeId) raw Cost is not reproducible."
        $initialCost = [int][Math]::Max(
            5,
            [Math]::Min(40, [int][Math]::Round($expectedRawCost, 0, [System.MidpointRounding]::AwayFromZero))
        )
        $expectedFinalCost = if ([int]$row.Rarity -eq 1) {
            Assert-Equal $initialCost ([int]$row.R1InitialCost) "Type ID $($row.TypeId) R1 initial Cost"
            $calibratedRawCost = [decimal]$row.R1CalibrationKappa * [decimal]$row.R1InitialCost
            Assert-Condition ([Math]::Abs([double]([decimal]$row.R1CalibratedRawCost - $calibratedRawCost)) -lt 0.000000000001) "Type ID $($row.TypeId) R1 calibrated raw Cost is not reproducible."
            [int][Math]::Max(
                2,
                [Math]::Min(40, [int][Math]::Round($calibratedRawCost, 0, [System.MidpointRounding]::AwayFromZero))
            )
        }
        else {
            Assert-Condition ([string]::IsNullOrWhiteSpace([string]$row.R1InitialCost)) "Non-R1 Type ID $($row.TypeId) has R1InitialCost."
            Assert-Condition ([string]::IsNullOrWhiteSpace([string]$row.R1CalibrationKappa)) "Non-R1 Type ID $($row.TypeId) has R1CalibrationKappa."
            Assert-Condition ([string]::IsNullOrWhiteSpace([string]$row.R1CalibratedRawCost)) "Non-R1 Type ID $($row.TypeId) has R1CalibratedRawCost."
            $initialCost
        }
        Assert-Equal $expectedFinalCost ([int]$row.FinalBaseCost) "Type ID $($row.TypeId) final base Cost"
        $minimumFinalCost = if ([int]$row.Rarity -eq 1) { 2 } else { 5 }
        Assert-Condition ([decimal]$row.FinalBaseCost -eq [decimal][int]$row.FinalBaseCost -and [int]$row.FinalBaseCost -ge $minimumFinalCost -and [int]$row.FinalBaseCost -le 40) "Type ID $($row.TypeId) has invalid integer Cost."
    }
    for ($rarity = 3; $rarity -le 6; $rarity++) {
        Assert-Condition ($rarityAudit[$rarity].MedianFinalCost -ge $rarityAudit[$rarity - 1].MedianFinalCost) "Median final Cost decreases from R$($rarity - 1) to R$rarity."
    }
    Assert-Condition (($rarityAudit[6].MedianFinalCost / $rarityAudit[2].MedianFinalCost) -le 4) 'R6/R2 median final Cost exceeds 4.'
    foreach ($rarity in 1..6) {
        $orderedRows = @($rows | Where-Object { [int]$_.Rarity -eq $rarity } | Sort-Object { [decimal]$_.ContinuousPower }, { [int]$_.TypeId })
        for ($index = 1; $index -lt $orderedRows.Count; $index++) {
            Assert-Condition ([int]$orderedRows[$index].FinalBaseCost -ge [int]$orderedRows[$index - 1].FinalBaseCost) "Rarity $rarity Cost decreases as ContinuousPower increases."
        }
    }
    Assert-Condition (@($rows | Where-Object { [int]$_.FinalBaseCost -gt 30 }).Count -le 8) 'More than eight units have FinalBaseCost > 30.'

    $elite2FallbackTypeIds = @('1008', '1017', '1026', '1042', '1243', '1434', '1502', '2031', '2035', '2043', '2046', '5503', '10039')
    Assert-Equal 81 @($rows | Where-Object Elite2ResourceSource -eq 'Dedicated').Count 'actual dedicated E2 row count'
    Assert-Equal 13 @($rows | Where-Object Elite2ResourceSource -eq 'InheritedE0').Count 'actual inherited E2 row count'
    Assert-Equal 1 @($rows | Where-Object Elite3ResourceSource -eq 'Dedicated').Count 'actual dedicated E3 row count'
    Assert-Equal 93 @($rows | Where-Object Elite3ResourceSource -eq 'InheritedE2').Count 'actual inherited E3 row count'
    foreach ($row in $rows) {
        $e0Power = [decimal]$row.ContinuousPower
        $e0Cost = [decimal]$row.FinalBaseCost
        $e0Efficiency = $e0Power / $e0Cost
        Assert-Equal 2 ([int]$row.Elite1EntityCount) "Type ID $($row.TypeId) E1 entity count"
        Assert-Equal ($e0Power * [decimal]2) ([decimal]$row.Elite1TotalPower) "Type ID $($row.TypeId) E1 total power"
        Assert-Equal ($e0Cost * [decimal]2) ([decimal]$row.Elite1TotalCost) "Type ID $($row.TypeId) E1 total Cost"
        Assert-Equal $e0Efficiency ([decimal]$row.Elite1PowerPerCostRatio) "Type ID $($row.TypeId) E1 absolute efficiency"
        foreach ($eliteLevel in @(2, 3)) {
            $entityCount = if ($eliteLevel -eq 2) { 3 } else { 5 }
            $stagePower = [decimal]$row.("Elite${eliteLevel}ContinuousPowerPerBody")
            $stageTotalPower = [decimal]$row.("Elite${eliteLevel}TotalPower")
            $stageTotalCost = [decimal]$row.("Elite${eliteLevel}TotalCost")
            $stageEfficiency = [decimal]$row.("Elite${eliteLevel}PowerPerCostRatio")
            Assert-Condition ($stagePower -gt 0 -and -not [double]::IsNaN([double]$stagePower) -and -not [double]::IsInfinity([double]$stagePower)) "Type ID $($row.TypeId) E$eliteLevel per-body power must be finite and positive."
            Assert-Equal ($stagePower * [decimal]$entityCount) $stageTotalPower "Type ID $($row.TypeId) E$eliteLevel total power"
            Assert-Equal ($e0Cost * [decimal]$entityCount) $stageTotalCost "Type ID $($row.TypeId) E$eliteLevel total Cost"
            Assert-Equal ($stagePower / $e0Cost) $stageEfficiency "Type ID $($row.TypeId) E$eliteLevel absolute efficiency"
            Assert-Condition ($stageEfficiency -gt 0 -and -not [double]::IsNaN([double]$stageEfficiency) -and -not [double]::IsInfinity([double]$stageEfficiency)) "Type ID $($row.TypeId) E$eliteLevel efficiency must be finite and positive."
        }
        if ([string]$row.Elite2ResourceSource -ceq 'InheritedE0') {
            Assert-Condition ([string]$row.TypeId -in $elite2FallbackTypeIds) "Unexpected E2 fallback Type ID $($row.TypeId)."
            Assert-Equal $e0Power ([decimal]$row.Elite2ContinuousPowerPerBody) "Type ID $($row.TypeId) inherited E2 power"
            Assert-Equal $e0Efficiency ([decimal]$row.Elite2PowerPerCostRatio) "Type ID $($row.TypeId) inherited E2 efficiency"
        }
        if ([string]$row.Elite3ResourceSource -ceq 'InheritedE2') {
            Assert-Equal ([decimal]$row.Elite2ContinuousPowerPerBody) ([decimal]$row.Elite3ContinuousPowerPerBody) "Type ID $($row.TypeId) inherited E3 power"
            Assert-Equal ([decimal]$row.Elite2PowerPerCostRatio) ([decimal]$row.Elite3PowerPerCostRatio) "Type ID $($row.TypeId) inherited E3 efficiency"
        }
        foreach ($eliteLevel in @(2, 3)) {
            if ([string]$row.("Elite${eliteLevel}ResourceSource") -cne 'Dedicated') {
                continue
            }
            if (-not [string]::IsNullOrWhiteSpace([string]$row.PanelPower)) {
                $stagePanelPower = [decimal]$row.("Elite${eliteLevel}PanelPower")
                Assert-Condition ($stagePanelPower -gt 0 -and -not [double]::IsNaN([double]$stagePanelPower) -and -not [double]::IsInfinity([double]$stagePanelPower)) "Type ID $($row.TypeId) dedicated E$eliteLevel PanelPower must be finite and positive."
            }
            else {
                Assert-Equal $e0Power ([decimal]$row.("Elite${eliteLevel}ContinuousPowerPerBody")) "Type ID $($row.TypeId) specialty E$eliteLevel inherited ContinuousPower"
                Assert-Equal 'InheritedE0ContinuousPowerNoPanelPower' ([string]$row.("Elite${eliteLevel}PanelPowerStatus")) "Type ID $($row.TypeId) specialty E$eliteLevel annotation"
            }
            $stageEfficiency = [decimal]$row.("Elite${eliteLevel}PowerPerCostRatio")
            if ($stageEfficiency -ne $e0Efficiency) {
                Assert-Condition ([string]$row.EliteEfficiencyRisk -match "E$eliteLevel" -and [string]$row.EliteEfficiencyRisk -match '%') "Type ID $($row.TypeId) dedicated E$eliteLevel efficiency delta lacks a percentage risk."
            }
        }
    }
    Assert-Equal ($elite2FallbackTypeIds -join '/') (@($rows | Where-Object Elite2ResourceSource -eq 'InheritedE0' | ForEach-Object { [string]$_.TypeId }) -join '/') 'actual E2 fallback TypeIds'
    $row1322 = @($rows | Where-Object TypeId -eq '1322')[0]
    Assert-Equal '1322_wdgyht' ([string]$row1322.Elite2ResourceDirectory) '1322 direct reverse E2 resource'
    Assert-Equal 'Dedicated' ([string]$row1322.Elite2ResourceSource) '1322 direct reverse E2 source'
    Assert-Condition ([string]$row1322.Elite2ResourceDirectory -cne '1322_wdgyht_2_2') '1322 generic suffix incorrectly overrode reverse E2 mapping.'
    $row1006 = @($rows | Where-Object TypeId -eq '1006')[0]
    Assert-Equal '1006_shield_3' ([string]$row1006.Elite3ResourceDirectory) '1006 direct E3 resource'
    Assert-Equal 'Dedicated' ([string]$row1006.Elite3ResourceSource) '1006 direct E3 source'
    foreach ($typeId in @('1025', '1131', '1132')) {
        $eliteAbilityRiskRow = @($rows | Where-Object TypeId -eq $typeId)[0]
        Assert-Condition ([string]$eliteAbilityRiskRow.EliteEfficiencyRisk -match 'E2 ability' -and [string]$eliteAbilityRiskRow.EliteEfficiencyRisk -match 'not separately remodel') "Type ID $typeId lacks the explicit E2 ability reuse risk."
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
    $row1080 = @($rows | Where-Object TypeId -eq '1080')[0]
    Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$row1080.PanelPower)) '1080 must retain its ordinary panel power.'
    Assert-Condition ([decimal]$row1080.EquivalentEntityContribution -gt 0) '1080 exported aura contribution must be positive.'
    Assert-Equal ([decimal]$row1080.EquivalentEntityContributionLow * [decimal]3) ([decimal]$row1080.EquivalentEntityContribution) '1080 1/3 target aura scaling'
    Assert-Equal ([decimal]$row1080.EquivalentEntityContributionLow * [decimal]5) ([decimal]$row1080.EquivalentEntityContributionHigh) '1080 1/5 target aura scaling'
    foreach ($typeId in @('1058', '1078', '1081', '1083', '1095', '1281', '1502')) {
        $riskRow = @($rows | Where-Object TypeId -eq $typeId)[0]
        Assert-Equal 'ExplicitRiskOnly' ([string]$riskRow.AbilityModelKind) "unmarked ability $typeId model kind"
        Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$riskRow.RiskFlags)) "Unmarked ability TypeId $typeId has empty exported RiskFlags."
        Assert-Equal ([decimal]1) ([decimal]$riskRow.AbilityPowerMultiplier) "unmarked ability $typeId numeric contribution"
        Assert-Equal ([decimal]0) ([decimal]$riskRow.EquivalentEntityContribution) "unmarked ability $typeId equivalent contribution"
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
        '1014' = @{ MaxHitPoints = '2000'; Attack = '350'; Defense = '100'; MagicResistance = '0'; AttackIntervalSeconds = '1.2'; DamageType = 'Physical' }
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
    Assert-Equal 'unit-cost-analysis-v6' ([string]$analysis.SchemaVersion) 'analysis schema version'
    Assert-Equal 94 ([int]$analysis.ShopRowCount) 'analysis shop row count'
    Assert-Equal '9/20/14/24/21/6' (@(
            [int]$analysis.RarityDistribution.R1,
            [int]$analysis.RarityDistribution.R2,
            [int]$analysis.RarityDistribution.R3,
            [int]$analysis.RarityDistribution.R4,
            [int]$analysis.RarityDistribution.R5,
            [int]$analysis.RarityDistribution.R6
        ) -join '/') 'analysis rarity distribution'
    Assert-Equal ([decimal]28) ([decimal]$analysis.CostModel.SelectedRarity6Anchor) 'analysis selected R6 anchor'
    Assert-Equal ([decimal]1.35) ([decimal]$analysis.CostModel.SelectedMaximumWithinTierFactor) 'analysis selected maximum within-tier factor'
    Assert-Condition (-not [bool]$analysis.CostModel.ParameterScanTriggered) 'actual data unexpectedly triggered the top-sparsity scan.'
    Assert-Equal 8 ([int]$analysis.CostModel.HighCostCount) 'analysis high-Cost count'
    Assert-Equal '1/2/3/5' (@($analysis.EliteModel.EntityCountMultipliers) -join '/') 'analysis elite entity multipliers'
    Assert-Equal 81 ([int]$analysis.EliteModel.DedicatedE2Count) 'analysis dedicated E2 count'
    Assert-Equal 1 ([int]$analysis.EliteModel.DedicatedE3Count) 'analysis dedicated E3 count'
    Assert-Equal 181 ([int]$analysis.EliteModel.ActuallyReadResourceDirectoryCount) 'analysis actually-read resource directory count'
    Assert-Equal 'Unconfirmed' ([string]$analysis.EconomyPressure.PermanentCostIncomeStatus) 'permanent Cost income status'
    Assert-Condition ([string]$analysis.EconomyPressure.ScopeNote -match 'permanent Cost income' -and [string]$analysis.EconomyPressure.ScopeNote -match 'unconfirmed') 'economy pressure note must state permanent Cost income is unconfirmed.'
    Assert-Equal '34/76/126/184/250/324' (@($analysis.EconomyPressure.GoldBudgets) -join '/') 'analysis economy Gold budgets'
    Assert-Equal '54/99/126/195' (@($analysis.EconomyPressure.CostBudgets) -join '/') 'analysis economy Cost budgets'
    Assert-Equal 144 @($analysis.EconomyPressure.Rows).Count 'analysis economy grid row count'
    Assert-Equal 24 @($analysis.EconomyPressure.QuantityRatios).Count 'analysis economy ratio case count'
    foreach ($economyRow in @($analysis.EconomyPressure.Rows)) {
        Assert-Condition ([decimal]$economyRow.Quantity -eq [Math]::Floor([decimal]$economyRow.Quantity) -and [decimal]$economyRow.Quantity -ge 0) 'analysis economy quantity must be a nonnegative integer.'
        Assert-Condition ([string]$economyRow.Limiter -in @('Gold', 'Cost', 'Both')) 'analysis economy limiter must be Gold, Cost, or Both.'
    }
    Test-DirectVariantPanelPower `
        -ExporterPath $exporterPath `
        -StagingRoot $StagingRoot `
        -Rows $rows `
        -DamageTypeOverrides $abilityInput.DamageTypeOverrides `
        -CombatModel $analysis.CombatModel
    Assert-Equal 'Calibrated' ([string]$analysis.CostModel.R1CalibrationStatus) 'R1 calibration status'
    Assert-Condition (@($analysis.CostModel.CandidateAudit).Count -ge 1) 'Cost model candidate audit is empty.'
    $r1Rows = @($rows | Where-Object { [int]$_.Rarity -eq 1 })
    $r1Kappas = @($r1Rows.R1CalibrationKappa | ForEach-Object { [decimal]$_ } | Sort-Object -Unique)
    Assert-Equal 1 $r1Kappas.Count 'R1 uniform calibration kappa count'
    Assert-Equal ([decimal]$r1Kappas[0]) ([decimal]$analysis.CostModel.R1Calibration.Kappa) 'analysis R1 calibration kappa'
    Assert-Equal 150 @($analysis.CostModel.R1Calibration.CandidateAudit).Count 'R1 calibration candidate count'
    Assert-Equal ([decimal]0.01) ([decimal]$analysis.CostModel.R1Calibration.CandidateRange[0]) 'analysis R1 minimum kappa'
    Assert-Equal ([decimal]1.50) ([decimal]$analysis.CostModel.R1Calibration.CandidateRange[1]) 'analysis R1 maximum kappa'
    Assert-Equal 72 @($analysis.CostModel.R1Calibration.SelectedMatches).Count 'R1 calibration selected match count'
    Assert-Condition ([decimal]$analysis.CostModel.R1Calibration.Rarity1WinRate -ge [decimal]0.4 -and [decimal]$analysis.CostModel.R1Calibration.Rarity1WinRate -le [decimal]0.6) 'R1 calibrated win rate is outside 40%-60%.'
    foreach ($matchAudit in @($analysis.CostModel.R1Calibration.SelectedMatches)) {
        Assert-Condition ([string]$matchAudit.Winner -in @('R1', 'R2', 'Draw')) 'R1 calibration match has an invalid winner.'
        foreach ($property in @(
                'R1RemainingEntityCount', 'R2RemainingEntityCount',
                'R1RemainingTargetValue', 'R2RemainingTargetValue'
            )) {
            Assert-Condition ($null -ne $matchAudit.$property -and [decimal]$matchAudit.$property -ge 0) "R1 calibration match has invalid $property."
        }
        Assert-Condition ([int]$matchAudit.EventCount -le [int]$analysis.CostModel.R1Calibration.MatchEventLimit) 'R1 calibration match exceeded the recorded event cap.'
    }
    $riskAuditTypeIds = @($analysis.RiskAudit | ForEach-Object { [string]$_.TypeId })
    foreach ($row in $rows | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_.RiskFlags) }) {
        Assert-Condition ([string]$row.TypeId -in $riskAuditTypeIds) "Risk-bearing TypeId $($row.TypeId) is absent from the analysis RiskAudit."
    }

    $regressionFailures = [System.Collections.Generic.List[string]]::new()
    Test-InvalidOverrideDamageType -BondSpecPath $BondSpecPath -StagingRoot $StagingRoot -TestRoot $testRoot -ExporterPath $exporterPath -PowerShellPath $powershellPath -Failures $regressionFailures -FixtureResourceDirectories $fixtureResourceDirectories -FixtureDamageType 'InvalidDamageType' -FailureMessage 'Illegal non-empty damageType for TypeId 1238 was accepted instead of rejected.'
    Assert-FileIntegritySnapshotEqual -Expected $bondSpecSnapshotBeforeWorkflow -Actual (Get-FileIntegritySnapshot -Path $BondSpecPath) -Name 'BONDS after invalid damageType regression test'
    Assert-FileIntegritySnapshotEqual -Expected $abilityInputSnapshotBeforeWorkflow -Actual (Get-FileIntegritySnapshot -Path $abilityInputPath) -Name 'ability input after invalid damageType regression test'
    Test-InvalidOverrideDamageType -BondSpecPath $BondSpecPath -StagingRoot $StagingRoot -TestRoot $testRoot -ExporterPath $exporterPath -PowerShellPath $powershellPath -Failures $regressionFailures -FixtureResourceDirectories $fixtureResourceDirectories -FixtureDamageType 'Magic' -FailureMessage 'Conflicting allowed damageType Magic for TypeId 1238 was accepted instead of rejected.'
    Assert-FileIntegritySnapshotEqual -Expected $bondSpecSnapshotBeforeWorkflow -Actual (Get-FileIntegritySnapshot -Path $BondSpecPath) -Name 'BONDS after conflicting damageType regression test'
    Assert-FileIntegritySnapshotEqual -Expected $abilityInputSnapshotBeforeWorkflow -Actual (Get-FileIntegritySnapshot -Path $abilityInputPath) -Name 'ability input after conflicting damageType regression test'
    foreach ($outputParameterName in @('OutputCsvPath', 'AnalysisOutputPath')) {
        Test-BondSpecOutputCollision -BondSpecPath $BondSpecPath -StagingRoot $StagingRoot -TestRoot $testRoot -ExporterPath $exporterPath -PowerShellPath $powershellPath -OutputParameterName $outputParameterName -FixtureResourceDirectories $fixtureResourceDirectories -Failures $regressionFailures
        Assert-FileIntegritySnapshotEqual -Expected $bondSpecSnapshotBeforeWorkflow -Actual (Get-FileIntegritySnapshot -Path $BondSpecPath) -Name "BONDS after $outputParameterName collision regression test"
        Assert-FileIntegritySnapshotEqual -Expected $abilityInputSnapshotBeforeWorkflow -Actual (Get-FileIntegritySnapshot -Path $abilityInputPath) -Name "ability input after $outputParameterName BONDS collision regression test"
        Test-StagingOutputCollision -BondSpecPath $BondSpecPath -AbilityInputPath $abilityInputPath -StagingRoot $StagingRoot -TestRoot $testRoot -ExporterPath $exporterPath -PowerShellPath $powershellPath -OutputParameterName $outputParameterName -FixtureResourceDirectories $fixtureResourceDirectories -ExternalSnapshot $externalSnapshotBeforeWorkflow -Failures $regressionFailures
        Assert-FileIntegritySnapshotEqual -Expected $bondSpecSnapshotBeforeWorkflow -Actual (Get-FileIntegritySnapshot -Path $BondSpecPath) -Name "BONDS after $OutputParameterName staging collision regression test"
        Assert-FileIntegritySnapshotEqual -Expected $abilityInputSnapshotBeforeWorkflow -Actual (Get-FileIntegritySnapshot -Path $abilityInputPath) -Name "ability input after $OutputParameterName staging collision regression test"
        Test-AbilityInputOutputCollision -BondSpecPath $BondSpecPath -AbilityInputPath $abilityInputPath -StagingRoot $StagingRoot -TestRoot $testRoot -ExporterPath $exporterPath -PowerShellPath $powershellPath -OutputParameterName $outputParameterName -FixtureResourceDirectories $fixtureResourceDirectories -ExternalSnapshot $externalSnapshotBeforeWorkflow -Failures $regressionFailures
        Assert-FileIntegritySnapshotEqual -Expected $bondSpecSnapshotBeforeWorkflow -Actual (Get-FileIntegritySnapshot -Path $BondSpecPath) -Name "BONDS after $OutputParameterName ability input collision regression test"
        Assert-FileIntegritySnapshotEqual -Expected $abilityInputSnapshotBeforeWorkflow -Actual (Get-FileIntegritySnapshot -Path $abilityInputPath) -Name "ability input after $OutputParameterName ability input collision regression test"
    }
    $regressionFailureMessage = if ($regressionFailures.Count -eq 0) { 'No regression failures.' } else { $regressionFailures -join [Environment]::NewLine }
    Assert-Condition ($regressionFailures.Count -eq 0) $regressionFailureMessage
    $externalSnapshotAfterWorkflow = Get-UnitJsonSnapshot -StagingRoot $StagingRoot -ResourceDirectories $fixtureResourceDirectories
    Assert-UnitJsonSnapshotsEqual -Expected $externalSnapshotBeforeWorkflow -Actual $externalSnapshotAfterWorkflow -Name 'external staging after complete workflow'
    $bondSpecSnapshotAfterWorkflow = Get-FileIntegritySnapshot -Path $BondSpecPath
    Assert-FileIntegritySnapshotEqual -Expected $bondSpecSnapshotBeforeWorkflow -Actual $bondSpecSnapshotAfterWorkflow -Name 'BONDS after complete workflow'
    $abilityInputSnapshotAfterWorkflow = Get-FileIntegritySnapshot -Path $abilityInputPath
    Assert-FileIntegritySnapshotEqual -Expected $abilityInputSnapshotBeforeWorkflow -Actual $abilityInputSnapshotAfterWorkflow -Name 'ability input after complete workflow'
    Write-Host "Verified external staging snapshot unchanged after complete workflow: $($externalSnapshotAfterWorkflow.Count) JSON files, SHA-256 snapshot $(Get-UnitJsonSnapshotHash -Snapshot $externalSnapshotAfterWorkflow)."
    Write-Host "Verified BONDS snapshot unchanged after complete workflow: $($bondSpecSnapshotAfterWorkflow.Length) bytes, SHA-256 $($bondSpecSnapshotAfterWorkflow.Sha256)."

    Write-Host "PASS: Unit Cost model self-test validated $($rows.Count) shop rows."
    exit 0
}
catch {
    Write-Error $_
    exit 1
}
