[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$BondSpecPath,
    [Parameter(Mandatory = $true)][string]$CostCsvPath,
    [string]$OutputPath
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

function Test-PathEqualCaseInsensitive {
    param(
        [Parameter(Mandatory = $true)][string]$Left,
        [Parameter(Mandatory = $true)][string]$Right
    )

    return [string]::Equals(
        [System.IO.Path]::GetFullPath($Left),
        [System.IO.Path]::GetFullPath($Right),
        [System.StringComparison]::OrdinalIgnoreCase
    )
}

function Assert-OrdinaryItem {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][bool]$MustBeFile
    )

    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    if ($MustBeFile) {
        Assert-Condition (-not $item.PSIsContainer) "$Name '$Path' must be a file."
    }
    else {
        Assert-Condition $item.PSIsContainer "$Name '$Path' must be a directory."
    }
    Assert-Condition (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -eq 0) "$Name '$Path' must not be a reparse point."
}

function Get-StrictUtf8Text {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    Assert-Condition (-not $hasBom) "$Name '$Path' must be UTF-8 without BOM."
    $encoding = [System.Text.UTF8Encoding]::new($false, $true)
    try {
        return $encoding.GetString($bytes)
    }
    catch {
        throw "$Name '$Path' is not valid UTF-8."
    }
}

function Get-NewlineConvention {
    param([Parameter(Mandatory = $true)][string]$Text)

    if ($Text.Contains("`r`n")) {
        $withoutCrLf = $Text.Replace("`r`n", '')
        Assert-Condition (-not $withoutCrLf.Contains("`r") -and -not $withoutCrLf.Contains("`n")) 'BONDS contains mixed or invalid newline sequences.'
        return "`r`n"
    }

    Assert-Condition (-not $Text.Contains("`r")) 'BONDS contains mixed or invalid newline sequences.'
    Assert-Condition $Text.Contains("`n") 'BONDS must contain newline-delimited Markdown.'
    return "`n"
}

function Get-TypeIdsFromCodeBlock {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string]$Header,
        [Parameter(Mandatory = $true)][int]$ExpectedCount,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $headerPattern = '(?m)^' + [regex]::Escape($Header) + '\r?$'
    $headerMatches = [regex]::Matches($Text, $headerPattern)
    Assert-Condition ($headerMatches.Count -eq 1) "BONDS must contain exactly one $Name header."

    $blockPattern = '(?ms)^' + [regex]::Escape($Header) + '\r?\n(?:[ \t]*\r?\n)+```text\r?\n(?<Ids>[0-9,\s]+?)\r?\n```'
    $blockMatch = [regex]::Match($Text, $blockPattern)
    Assert-Condition $blockMatch.Success "BONDS $Name TypeId block is missing or malformed."

    $typeIds = @($blockMatch.Groups['Ids'].Value -split '[,\s]+' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    Assert-Condition ($typeIds.Count -eq $ExpectedCount) "BONDS $Name TypeId block must contain exactly $ExpectedCount rows."
    foreach ($typeId in $typeIds) {
        Assert-Condition ($typeId -match '^[1-9][0-9]*$') "BONDS $Name TypeId '$typeId' is not a canonical positive integer."
    }
    Assert-Condition (@($typeIds | Sort-Object -Unique).Count -eq $ExpectedCount) "BONDS $Name TypeId block contains duplicates."

    return [pscustomobject]@{
        TypeIds = $typeIds
        Match = $blockMatch
    }
}

function Get-NumericTypeIdSetKey {
    param([Parameter(Mandatory = $true)][string[]]$TypeIds)

    return (@($TypeIds | Sort-Object { [int64]$_ }) -join '/')
}

function Assert-ExistingCostTableRegion {
    param(
        [Parameter(Mandatory = $true)][string]$RegionText,
        [Parameter(Mandatory = $true)][string]$Newline,
        [Parameter(Mandatory = $true)][string]$StartMarker,
        [Parameter(Mandatory = $true)][string]$EndMarker,
        [Parameter(Mandatory = $true)][string]$CostHeading,
        [Parameter(Mandatory = $true)][string]$CostColumns,
        [Parameter(Mandatory = $true)][string[]]$ShopTypeIds,
        [Parameter(Mandatory = $true)][string[]]$NonShopTypeIds
    )

    $lines = @($RegionText -split [regex]::Escape($Newline))
    Assert-Condition ($lines.Count -eq 99) 'Existing BONDS Cost-table marker region must contain exactly 99 lines.'
    Assert-Condition ($lines[0] -ceq $StartMarker) 'Existing BONDS Cost-table start marker is invalid.'
    Assert-Condition ($lines[1] -ceq $CostHeading) 'Existing BONDS Cost-table heading is invalid.'
    Assert-Condition ($lines[2] -ceq $CostColumns) 'Existing BONDS Cost-table columns are invalid.'
    Assert-Condition ($lines[3] -ceq '| ---: | --- | ---: | ---: |') 'Existing BONDS Cost-table separator is invalid.'
    Assert-Condition ($lines[-1] -ceq $EndMarker) 'Existing BONDS Cost-table end marker is invalid.'

    $typeIds = [System.Collections.Generic.List[string]]::new()
    for ($index = 4; $index -lt $lines.Count - 1; $index++) {
        $line = $lines[$index]
        Assert-Condition ($line -match '^\| (?<TypeId>[1-9][0-9]*) \| (?<DisplayName>(?:\\\||[^|])+?) \| (?<Rarity>[1-6]) \| (?<Cost>[0-9]+) \|$') "Existing BONDS Cost-table data line '$line' is malformed."
        $typeId = [string]$Matches.TypeId
        $displayName = [string]$Matches.DisplayName
        $rarity = [int]$Matches.Rarity
        $cost = [int]$Matches.Cost
        Assert-Condition (-not [string]::IsNullOrWhiteSpace($displayName)) "Existing BONDS Cost-table TypeId '$typeId' has an empty display name."
        if ($rarity -eq 1) {
            Assert-Condition ($cost -ge 2 -and $cost -le 40) "Existing BONDS Cost-table R1 TypeId '$typeId' Cost '$cost' is outside 2..40."
        }
        else {
            Assert-Condition ($cost -ge 5 -and $cost -le 40) "Existing BONDS Cost-table R$rarity TypeId '$typeId' Cost '$cost' is outside 5..40."
        }
        Assert-Condition ($typeId -notin $NonShopTypeIds) "Existing BONDS Cost-table contains non-shop TypeId '$typeId'."
        $typeIds.Add($typeId)
    }

    Assert-Condition ($typeIds.Count -eq 94) 'Existing BONDS Cost-table must contain exactly 94 data rows.'
    Assert-Condition (@($typeIds | Sort-Object -Unique).Count -eq 94) 'Existing BONDS Cost-table contains duplicate TypeId rows.'
    $numericOrderKey = Get-NumericTypeIdSetKey -TypeIds $typeIds
    Assert-Condition (($typeIds -join '/') -ceq $numericOrderKey) 'Existing BONDS Cost-table TypeIds are not in strict numeric order.'
    Assert-Condition ($numericOrderKey -ceq (Get-NumericTypeIdSetKey -TypeIds $ShopTypeIds)) 'Existing BONDS Cost-table TypeId set differs from the BONDS shop set.'
}

try {
    Assert-Condition (-not [string]::IsNullOrWhiteSpace($BondSpecPath)) 'BondSpecPath must not be empty.'
    Assert-Condition (-not [string]::IsNullOrWhiteSpace($CostCsvPath)) 'CostCsvPath must not be empty.'

    $bondFullPath = [System.IO.Path]::GetFullPath($BondSpecPath)
    $csvFullPath = [System.IO.Path]::GetFullPath($CostCsvPath)
    Assert-Condition (Test-Path -LiteralPath $bondFullPath -PathType Leaf) "BONDS file '$bondFullPath' does not exist."
    Assert-Condition (Test-Path -LiteralPath $csvFullPath -PathType Leaf) "Cost CSV '$csvFullPath' does not exist."
    Assert-OrdinaryItem -Path $bondFullPath -Name 'BONDS input' -MustBeFile $true
    Assert-OrdinaryItem -Path $csvFullPath -Name 'Cost CSV input' -MustBeFile $true
    Assert-Condition (-not (Test-PathEqualCaseInsensitive -Left $bondFullPath -Right $csvFullPath)) 'BondSpecPath and CostCsvPath must identify different files.'

    if ([string]::IsNullOrWhiteSpace($OutputPath)) {
        $outputFullPath = $bondFullPath
    }
    else {
        $outputFullPath = [System.IO.Path]::GetFullPath($OutputPath)
    }
    Assert-Condition (-not (Test-PathEqualCaseInsensitive -Left $outputFullPath -Right $csvFullPath)) 'OutputPath and CostCsvPath must identify different files.'

    $outputDirectory = Split-Path -Parent $outputFullPath
    Assert-Condition (-not [string]::IsNullOrWhiteSpace($outputDirectory)) "OutputPath '$outputFullPath' must have a parent directory."
    Assert-Condition (Test-Path -LiteralPath $outputDirectory -PathType Container) "Output directory '$outputDirectory' does not exist."
    Assert-OrdinaryItem -Path $outputDirectory -Name 'Output directory' -MustBeFile $false
    if (Test-Path -LiteralPath $outputFullPath) {
        Assert-Condition (Test-Path -LiteralPath $outputFullPath -PathType Leaf) "OutputPath '$outputFullPath' must not be a directory."
        Assert-OrdinaryItem -Path $outputFullPath -Name 'Existing output' -MustBeFile $true
    }

    $bondText = Get-StrictUtf8Text -Path $bondFullPath -Name 'BONDS input'
    $csvText = Get-StrictUtf8Text -Path $csvFullPath -Name 'Cost CSV input'
    $newline = Get-NewlineConvention -Text $bondText

    $shopHeader = [string]::Concat('## ', [char]0x5546, [char]0x5E97, [char]0x5355, [char]0x4F4D, [char]0xFF08, '94', [char]0xFF09)
    $nonShopHeader = [string]::Concat('## ', [char]0x975E, [char]0x5546, [char]0x5E97, [char]0x5355, [char]0x4F4D, [char]0xFF08, '5', [char]0xFF09)
    $costHeading = [string]::Concat('## ', [char]0x5546, [char]0x5E97, [char]0x5355, [char]0x4F4D, [char]0x7CBE, [char]0x82F1, ' 0 ', [char]0x57FA, [char]0x7840, [char]0x90E8, [char]0x7F72, ' Cost')
    $costColumns = [string]::Concat('| TypeId | ', [char]0x540D, [char]0x79F0, ' | ', [char]0x7A00, [char]0x6709, [char]0x5EA6, ' | ', [char]0x7CBE, [char]0x82F1, '0', [char]0x57FA, [char]0x7840, 'Cost C0 |')
    $startMarker = '<!-- UNIT-COST-TABLE:START -->'
    $endMarker = '<!-- UNIT-COST-TABLE:END -->'

    $shopBlock = Get-TypeIdsFromCodeBlock -Text $bondText -Header $shopHeader -ExpectedCount 94 -Name 'shop'
    $nonShopBlock = Get-TypeIdsFromCodeBlock -Text $bondText -Header $nonShopHeader -ExpectedCount 5 -Name 'non-shop'
    $expectedNonShopKey = '1137/1138/2033/5504/10002'
    Assert-Condition ((Get-NumericTypeIdSetKey -TypeIds $nonShopBlock.TypeIds) -eq $expectedNonShopKey) "BONDS non-shop TypeId set must be '$expectedNonShopKey'."

    $csvRows = @($csvText | ConvertFrom-Csv)
    Assert-Condition ($csvRows.Count -eq 94) 'Cost CSV must contain exactly 94 data rows.'
    $requiredColumns = @('TypeId', 'DisplayName', 'Rarity', 'FinalBaseCost')
    if ($csvRows.Count -gt 0) {
        $actualColumns = @($csvRows[0].PSObject.Properties.Name)
        foreach ($requiredColumn in $requiredColumns) {
            Assert-Condition ($requiredColumn -in $actualColumns) "Cost CSV is missing required column '$requiredColumn'."
        }
    }

    $csvTypeIds = [System.Collections.Generic.List[string]]::new()
    $validatedRows = [System.Collections.Generic.List[object]]::new()
    foreach ($row in $csvRows) {
        $typeId = [string]$row.TypeId
        $displayName = [string]$row.DisplayName
        $rarityText = [string]$row.Rarity
        $costText = [string]$row.FinalBaseCost

        Assert-Condition ($typeId -match '^[1-9][0-9]*$') "Cost CSV TypeId '$typeId' is not a canonical positive integer."
        Assert-Condition (-not [string]::IsNullOrWhiteSpace($displayName)) "Cost CSV TypeId '$typeId' has an empty DisplayName."
        Assert-Condition (-not $displayName.Contains("`r") -and -not $displayName.Contains("`n")) "Cost CSV TypeId '$typeId' DisplayName contains a newline."
        Assert-Condition ($rarityText -match '^[1-6]$') "Cost CSV TypeId '$typeId' Rarity '$rarityText' must be an integer from 1 through 6."
        Assert-Condition ($costText -match '^[0-9]+$') "Cost CSV TypeId '$typeId' FinalBaseCost '$costText' must be an integer."

        $rarity = [int]$rarityText
        $cost = [int]$costText
        if ($rarity -eq 1) {
            Assert-Condition ($cost -ge 2 -and $cost -le 40) "Cost CSV R1 TypeId '$typeId' FinalBaseCost '$cost' is outside 2..40."
        }
        else {
            Assert-Condition ($cost -ge 5 -and $cost -le 40) "Cost CSV R$rarity TypeId '$typeId' FinalBaseCost '$cost' is outside 5..40."
        }

        $csvTypeIds.Add($typeId)
        $validatedRows.Add([pscustomobject]@{
                TypeId = [int64]$typeId
                TypeIdText = $typeId
                DisplayName = $displayName
                Rarity = $rarity
                Cost = $cost
            })
    }

    Assert-Condition (@($csvTypeIds | Sort-Object -Unique).Count -eq 94) 'Cost CSV contains duplicate TypeId rows.'
    Assert-Condition ((Get-NumericTypeIdSetKey -TypeIds $csvTypeIds) -eq (Get-NumericTypeIdSetKey -TypeIds $shopBlock.TypeIds)) 'Cost CSV and BONDS shop TypeId sets differ.'
    foreach ($nonShopTypeId in $nonShopBlock.TypeIds) {
        Assert-Condition ($nonShopTypeId -notin $csvTypeIds) "Cost CSV contains non-shop TypeId '$nonShopTypeId'."
    }

    $suspiciousMarkerLines = @(
        $bondText -split '\r?\n' |
            Where-Object { $_.Contains('UNIT-COST-TABLE') -and $_ -cne $startMarker -and $_ -cne $endMarker }
    )
    Assert-Condition ($suspiciousMarkerLines.Count -eq 0) 'BONDS contains a malformed UNIT-COST-TABLE marker.'
    $startCount = [regex]::Matches($bondText, [regex]::Escape($startMarker)).Count
    $endCount = [regex]::Matches($bondText, [regex]::Escape($endMarker)).Count
    $validMarkerCounts = ($startCount -eq 0 -and $endCount -eq 0) -or ($startCount -eq 1 -and $endCount -eq 1)
    Assert-Condition $validMarkerCounts 'BONDS must contain either no Cost-table markers or one exact marker pair.'

    $nonShopHeaderIndex = $bondText.IndexOf($nonShopHeader, [System.StringComparison]::Ordinal)
    Assert-Condition ($shopBlock.Match.Index -lt $nonShopHeaderIndex) 'BONDS shop TypeId block must precede the non-shop section.'
    $replaceStart = $nonShopHeaderIndex
    $replaceLength = 0
    if ($startCount -eq 1) {
        $startIndex = $bondText.IndexOf($startMarker, [System.StringComparison]::Ordinal)
        $endIndex = $bondText.IndexOf($endMarker, [System.StringComparison]::Ordinal)
        Assert-Condition ($startIndex -ge 0 -and $endIndex -gt $startIndex) 'BONDS Cost-table markers are reversed.'
        Assert-Condition ($startIndex -gt ($shopBlock.Match.Index + $shopBlock.Match.Length)) 'BONDS Cost-table marker region is outside the required shop/non-shop boundary.'
        Assert-Condition ($endIndex -lt $nonShopHeaderIndex) 'BONDS Cost-table marker region is outside the required shop/non-shop boundary.'
        Assert-Condition ($startIndex -eq 0 -or $bondText.Substring($startIndex - $newline.Length, $newline.Length) -ceq $newline) 'BONDS Cost-table start marker must begin on its own line.'

        $regionEnd = $endIndex + $endMarker.Length
        $existingRegionText = $bondText.Substring($startIndex, $regionEnd - $startIndex)
        Assert-Condition ($bondText.Substring($regionEnd).StartsWith($newline, [System.StringComparison]::Ordinal)) 'BONDS Cost-table end marker must be followed by the source newline.'
        $regionEnd += $newline.Length
        Assert-Condition ($regionEnd -eq $nonShopHeaderIndex) 'BONDS Cost-table marker region must be immediately before the non-shop heading.'
        Assert-ExistingCostTableRegion `
            -RegionText $existingRegionText `
            -Newline $newline `
            -StartMarker $startMarker `
            -EndMarker $endMarker `
            -CostHeading $costHeading `
            -CostColumns $costColumns `
            -ShopTypeIds $shopBlock.TypeIds `
            -NonShopTypeIds $nonShopBlock.TypeIds
        $replaceStart = $startIndex
        $replaceLength = $regionEnd - $startIndex
    }

    $tableLines = [System.Collections.Generic.List[string]]::new()
    $tableLines.Add($startMarker)
    $tableLines.Add($costHeading)
    $tableLines.Add($costColumns)
    $tableLines.Add('| ---: | --- | ---: | ---: |')
    foreach ($row in @($validatedRows | Sort-Object TypeId)) {
        $escapedDisplayName = ([string]$row.DisplayName).Replace('|', '\|')
        $tableLines.Add("| $($row.TypeIdText) | $escapedDisplayName | $($row.Rarity) | $($row.Cost) |")
    }
    $tableLines.Add($endMarker)
    Assert-Condition ($tableLines.Count -eq 99) 'Generated Cost-table marker block must contain exactly 99 lines.'
    $markerBlockWithTrailingNewline = ([string]::Join($newline, $tableLines)) + $newline
    $outputText = $bondText.Remove($replaceStart, $replaceLength).Insert($replaceStart, $markerBlockWithTrailingNewline)
    $outputBytes = [System.Text.UTF8Encoding]::new($false, $true).GetBytes($outputText)

    if (Test-Path -LiteralPath $outputFullPath -PathType Leaf) {
        $existingBytes = [System.IO.File]::ReadAllBytes($outputFullPath)
        if ([System.Linq.Enumerable]::SequenceEqual([byte[]]$existingBytes, [byte[]]$outputBytes)) {
            Write-Host "BONDS Cost table is already current: $outputFullPath"
            exit 0
        }
    }

    $operationId = [guid]::NewGuid().ToString('N')
    $temporaryPath = Join-Path $outputDirectory ('.unit-cost-table-' + $operationId + '.tmp')
    $backupPath = Join-Path $outputDirectory ('.unit-cost-table-backup-' + $operationId + '.tmp')
    try {
        [System.IO.File]::WriteAllBytes($temporaryPath, $outputBytes)
        if (Test-Path -LiteralPath $outputFullPath -PathType Leaf) {
            [System.IO.File]::Replace($temporaryPath, $outputFullPath, $backupPath)
        }
        else {
            [System.IO.File]::Move($temporaryPath, $outputFullPath)
        }
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
        if (Test-Path -LiteralPath $backupPath -PathType Leaf) {
            Remove-Item -LiteralPath $backupPath -Force
        }
    }

    Write-Host "Updated BONDS Cost table: $outputFullPath"
}
catch {
    Write-Error $_
    exit 1
}
