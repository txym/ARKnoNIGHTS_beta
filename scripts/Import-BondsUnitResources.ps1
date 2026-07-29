[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-BondsUnitResourceImportManifest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$BondSpecPath,

        [Parameter(Mandatory)]
        [string]$StagingRoot,

        [int[]]$TypeIds
    )

    if (-not (Test-Path -LiteralPath $BondSpecPath -PathType Leaf)) {
        throw "BONDS specification is missing: $BondSpecPath"
    }
    if (-not (Test-Path -LiteralPath $StagingRoot -PathType Container)) {
        throw "Staging root is missing: $StagingRoot"
    }

    $specification = [System.IO.File]::ReadAllText(
        (Resolve-Path -LiteralPath $BondSpecPath),
        [System.Text.UTF8Encoding]::new($false, $true))
    $bondsTypeIds = @()
    $matches = @([regex]::Matches($specification, '(?ms)^## [^\r\n]+\s*```text\s*(?<ids>.*?)\s*```'))
    if ($matches.Count -ne 2) {
        throw "BONDS specification must contain exactly two TypeId text sections, actual=$($matches.Count)"
    }
    foreach ($match in $matches) {
        $bondsTypeIds += @($match.Groups['ids'].Value -split '[,\s]+' |
            Where-Object { $_ -match '^\d+$' } |
            ForEach-Object { [int]$_ })
    }
    $bondsTypeIds = @($bondsTypeIds | Sort-Object -Unique)
    if ($bondsTypeIds.Count -ne 99) {
        throw "BONDS specification must contain 99 unique TypeIds, actual=$($bondsTypeIds.Count)"
    }

    if ($PSBoundParameters.ContainsKey('TypeIds')) {
        $requestedTypeIds = @($TypeIds | Sort-Object -Unique)
        if ($requestedTypeIds.Count -ne @($TypeIds).Count) {
            throw 'Explicit TypeIds must not contain duplicates.'
        }
        $unexpectedTypeIds = @($requestedTypeIds | Where-Object { $_ -notin $bondsTypeIds })
        if ($unexpectedTypeIds.Count -ne 0) {
            throw "Explicit TypeIds are not present in BONDS: $($unexpectedTypeIds -join ',')"
        }
        $typeIds = $requestedTypeIds
    } else {
        $typeIds = $bondsTypeIds
    }

    $stagedDirectories = @(Get-ChildItem -LiteralPath $StagingRoot -Directory)
    $manifest = foreach ($typeId in $typeIds) {
        $prefix = "$typeId`_"
        $variants = @($stagedDirectories |
            Where-Object { $_.Name.StartsWith($prefix, [System.StringComparison]::Ordinal) } |
            Sort-Object -Property Name)
        if ($variants.Count -eq 0) {
            throw "No staged resource variants are available for BONDS TypeId=$typeId"
        }

        foreach ($variant in $variants) {
            $sourceUnitKey = $variant.Name
            $unitKey = if ($sourceUnitKey -eq '1322_wdgyht_2') {
                '1322_wdgyht'
            } elseif ($sourceUnitKey -eq '1322_wdgyht') {
                '1322_wdgyht_2'
            } else {
                $sourceUnitKey
            }
            $variantRole = if ($sourceUnitKey -eq '1322_wdgyht_2') {
                'Default'
            } elseif ($sourceUnitKey -eq '1322_wdgyht') {
                'Elite2'
            } else {
                'Unspecified'
            }
            [PSCustomObject]@{
                TypeId = $typeId
                UnitKey = $unitKey
                SourceUnitKey = $sourceUnitKey
                VariantRole = $variantRole
                SourceDirectory = $variant.FullName
                CharacterFolderName = $unitKey
                ProfilePictureFileName = "UIImage_$unitKey.png"
                RequiredSourceFiles = @(
                    "enemy_$sourceUnitKey.atlas.txt",
                    "enemy_$sourceUnitKey.png",
                    "enemy_$sourceUnitKey.skel.bytes",
                    "UIImage_$sourceUnitKey.png"
                )
                DestinationResourceFiles = @(
                    "enemy_$unitKey.atlas.txt",
                    "enemy_$unitKey.png",
                    "enemy_$unitKey.skel.bytes",
                    "UIImage_$unitKey.png"
                )
            }
        }
    }

    return @($manifest)
}

function Invoke-BondsUnitResourceImport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [object[]]$Manifest,

        [Parameter(Mandatory)]
        [string]$CharacterRoot,

        [Parameter(Mandatory)]
        [string]$ProfilePictureRoot
    )

    $results = foreach ($entry in $Manifest) {
        $identityIsIncomplete = $null -eq $entry
        $identityIsIncomplete = $identityIsIncomplete -or [string]::IsNullOrWhiteSpace($entry.UnitKey)
        $identityIsIncomplete = $identityIsIncomplete -or [string]::IsNullOrWhiteSpace($entry.SourceUnitKey)
        $identityIsIncomplete = $identityIsIncomplete -or [string]::IsNullOrWhiteSpace($entry.SourceDirectory)
        $identityIsIncomplete = $identityIsIncomplete -or [string]::IsNullOrWhiteSpace($entry.CharacterFolderName)
        $identityIsIncomplete = $identityIsIncomplete -or [string]::IsNullOrWhiteSpace($entry.ProfilePictureFileName)
        if ($identityIsIncomplete) {
            throw 'Each import manifest entry must contain a complete unit-resource identity.'
        }
        if (-not (Test-Path -LiteralPath $entry.SourceDirectory -PathType Container)) {
            throw "Source directory is missing: $($entry.SourceDirectory)"
        }

        $expectedSourceFiles = @(
            "enemy_$($entry.SourceUnitKey).atlas.txt",
            "enemy_$($entry.SourceUnitKey).png",
            "enemy_$($entry.SourceUnitKey).skel.bytes",
            "UIImage_$($entry.SourceUnitKey).png"
        )
        $expectedDestinationFiles = @(
            "enemy_$($entry.UnitKey).atlas.txt",
            "enemy_$($entry.UnitKey).png",
            "enemy_$($entry.UnitKey).skel.bytes",
            "UIImage_$($entry.UnitKey).png"
        )
        $actualFileSet = @($entry.RequiredSourceFiles | Sort-Object) -join "`n"
        $expectedFileSet = @($expectedSourceFiles | Sort-Object) -join "`n"
        $actualDestinationFileSet = @($entry.DestinationResourceFiles | Sort-Object) -join "`n"
        $expectedDestinationFileSet = @($expectedDestinationFiles | Sort-Object) -join "`n"
        if (@($entry.RequiredSourceFiles).Count -ne $expectedSourceFiles.Count -or $actualFileSet -ne $expectedFileSet -or $actualDestinationFileSet -ne $expectedDestinationFileSet) {
            throw "Import manifest has an unexpected resource file set: $($entry.UnitKey)"
        }

        $characterDirectory = Join-Path $CharacterRoot $entry.CharacterFolderName
        $copiedFileCount = 0
        $unchangedFileCount = 0
        for ($index = 0; $index -lt $expectedSourceFiles.Count; $index++) {
            $sourceFileName = $expectedSourceFiles[$index]
            $destinationFileName = $expectedDestinationFiles[$index]
            $sourcePath = Join-Path $entry.SourceDirectory $sourceFileName
            if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
                throw "Required staged resource is missing: $sourcePath"
            }

            $destinationDirectory = if ($destinationFileName -eq $entry.ProfilePictureFileName) {
                $ProfilePictureRoot
            } else {
                $characterDirectory
            }
            $destinationPath = Join-Path $destinationDirectory $destinationFileName
            [System.IO.Directory]::CreateDirectory($destinationDirectory) | Out-Null

            if ($sourceFileName.EndsWith('.atlas.txt', [System.StringComparison]::Ordinal) -and $entry.SourceUnitKey -ne $entry.UnitKey) {
                $encoding = [System.Text.UTF8Encoding]::new($false, $true)
                $expectedAtlasText = [System.IO.File]::ReadAllText($sourcePath, $encoding).Replace($entry.SourceUnitKey, $entry.UnitKey)
                $actualAtlasText = if (Test-Path -LiteralPath $destinationPath -PathType Leaf) {
                    [System.IO.File]::ReadAllText($destinationPath, $encoding)
                } else {
                    $null
                }
                if ($actualAtlasText -eq $expectedAtlasText) {
                    $unchangedFileCount++
                    continue
                }
                [System.IO.File]::WriteAllText($destinationPath, $expectedAtlasText, $encoding)
                if ([System.IO.File]::ReadAllText($destinationPath, $encoding) -ne $expectedAtlasText) {
                    throw "Copied atlas text does not match remapped source: $destinationPath"
                }
                $copiedFileCount++
                continue
            }

            $sourceHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
            $destinationHash = if (Test-Path -LiteralPath $destinationPath -PathType Leaf) {
                (Get-FileHash -LiteralPath $destinationPath -Algorithm SHA256).Hash
            } else {
                $null
            }

            if ($sourceHash -eq $destinationHash) {
                $unchangedFileCount++
                continue
            }

            Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Force
            if ((Get-FileHash -LiteralPath $destinationPath -Algorithm SHA256).Hash -ne $sourceHash) {
                throw "Copied resource hash does not match source: $destinationPath"
            }
            $copiedFileCount++
        }

        [PSCustomObject]@{
            TypeId = $entry.TypeId
            UnitKey = $entry.UnitKey
            VariantRole = $entry.VariantRole
            CopiedFileCount = $copiedFileCount
            UnchangedFileCount = $unchangedFileCount
        }
    }

    return @($results)
}

function Test-BondsUnitResourceImport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [object[]]$Manifest,

        [Parameter(Mandatory)]
        [string]$CharacterRoot,

        [Parameter(Mandatory)]
        [string]$ProfilePictureRoot
    )

    $results = foreach ($entry in $Manifest) {
        $errors = [System.Collections.Generic.List[string]]::new()
        $characterDirectory = Join-Path $CharacterRoot $entry.CharacterFolderName
        if (-not (Test-Path -LiteralPath $characterDirectory -PathType Container)) {
            $errors.Add("Character folder is missing: $characterDirectory")
        }

        for ($index = 0; $index -lt @($entry.RequiredSourceFiles).Count; $index++) {
            $sourceFileName = $entry.RequiredSourceFiles[$index]
            $destinationFileName = $entry.DestinationResourceFiles[$index]
            $sourcePath = Join-Path $entry.SourceDirectory $sourceFileName
            $destinationPath = if ($destinationFileName -eq $entry.ProfilePictureFileName) {
                Join-Path $ProfilePictureRoot $destinationFileName
            } else {
                Join-Path $characterDirectory $destinationFileName
            }
            if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
                $errors.Add("Staged source is missing: $sourcePath")
                continue
            }
            if (-not (Test-Path -LiteralPath $destinationPath -PathType Leaf)) {
                $errors.Add("Imported resource is missing: $destinationPath")
                continue
            }

            if ($sourceFileName.EndsWith('.atlas.txt', [System.StringComparison]::Ordinal) -and $entry.SourceUnitKey -ne $entry.UnitKey) {
                $encoding = [System.Text.UTF8Encoding]::new($false, $true)
                $expectedAtlasText = [System.IO.File]::ReadAllText($sourcePath, $encoding).Replace($entry.SourceUnitKey, $entry.UnitKey)
                $actualAtlasText = [System.IO.File]::ReadAllText($destinationPath, $encoding)
                if ($actualAtlasText -ne $expectedAtlasText) {
                    $errors.Add("Imported atlas text does not match remapped staging source: $destinationPath")
                }
                continue
            }

            $sourceHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
            $destinationHash = (Get-FileHash -LiteralPath $destinationPath -Algorithm SHA256).Hash
            if ($sourceHash -ne $destinationHash) {
                $errors.Add("Imported resource hash does not match staging: $destinationPath")
            }
        }

        foreach ($stagingMetadataFile in 'manifest.json', 'unit-levels.json', 'unit-source-v1.json') {
            if (Test-Path -LiteralPath (Join-Path $characterDirectory $stagingMetadataFile) -PathType Leaf) {
                $errors.Add("Staging metadata was copied into Unity resources: $characterDirectory\\$stagingMetadataFile")
            }
        }

        [PSCustomObject]@{
            TypeId = $entry.TypeId
            UnitKey = $entry.UnitKey
            SourceUnitKey = $entry.SourceUnitKey
            VariantRole = $entry.VariantRole
            IsValid = $errors.Count -eq 0
            Errors = @($errors)
        }
    }

    return @($results)
}
