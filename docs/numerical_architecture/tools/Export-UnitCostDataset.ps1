[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$BondSpecPath,
    [Parameter(Mandatory = $true)][string]$StagingRoot,
    [string]$AbilityInputPath,
    [Parameter(Mandatory = $true)][string]$OutputCsvPath,
    [Parameter(Mandatory = $true)][string]$AnalysisOutputPath
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

function Resolve-ExistingPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Required input path '$Path' does not exist."
    }
    return (Resolve-Path -LiteralPath $Path).Path
}

function Get-TypeIdsFromCodeBlock {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string[]]$Lines,
        [Parameter(Mandatory = $true)][string]$Header,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $headerIndexes = @(
        for ($index = 0; $index -lt $Lines.Count; $index++) {
            if ($Lines[$index] -ceq $Header) {
                $index
            }
        }
    )
    Assert-Condition ($headerIndexes.Count -eq 1) "Expected exactly one $Label header '$Header'; found $($headerIndexes.Count)."

    $index = $headerIndexes[0] + 1
    while ($index -lt $Lines.Count -and [string]::IsNullOrWhiteSpace($Lines[$index])) {
        $index++
    }
    Assert-Condition ($index -lt $Lines.Count -and $Lines[$index] -ceq '```text') "$Label header '$Header' is missing its text code block."

    $index++
    $content = [System.Collections.Generic.List[string]]::new()
    while ($index -lt $Lines.Count -and $Lines[$index] -cne '```') {
        $content.Add($Lines[$index])
        $index++
    }
    Assert-Condition ($index -lt $Lines.Count) "$Label text code block is not closed."

    $typeIds = [System.Collections.Generic.List[string]]::new()
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($candidate in (($content -join ' ') -split ',')) {
        $typeId = $candidate.Trim()
        Assert-Condition (-not [string]::IsNullOrWhiteSpace($typeId)) "$Label code block contains an empty TypeId."
        Assert-Condition ($typeId -match '^\d+$') "$Label code block contains invalid TypeId '$typeId'."
        Assert-Condition ($seen.Add($typeId)) "$Label code block contains duplicate TypeId '$typeId'."
        $typeIds.Add($typeId)
    }
    return $typeIds.ToArray()
}

function Get-Number {
    param(
        [Parameter(Mandatory = $true)]$Value,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][bool]$RequireInteger,
        [Parameter(Mandatory = $true)][decimal]$Minimum
    )

    Assert-Condition ($null -ne $Value) "$Name is missing."
    try {
        $number = [decimal]$Value
    }
    catch {
        throw "$Name '$Value' is not numeric."
    }
    Assert-Condition ($number -ge $Minimum) "$Name '$Value' is below the minimum '$Minimum'."
    if ($RequireInteger) {
        Assert-Condition ($number -eq [decimal][int]$number) "$Name '$Value' must be an integer."
        return [int]$number
    }
    return $number
}

function Get-RequiredLevelZero {
    param(
        [Parameter(Mandatory = $true)]$LevelsDocument,
        [Parameter(Mandatory = $true)][string]$DirectoryName
    )

    Assert-Condition ($null -ne $LevelsDocument.levels) "Resource directory '$DirectoryName' is missing levels."
    $levelZeroRows = @($LevelsDocument.levels | Where-Object { $_.level -eq 0 })
    Assert-Condition ($levelZeroRows.Count -eq 1) "Resource directory '$DirectoryName' must contain exactly one level 0 row; found $($levelZeroRows.Count)."
    $levelZero = $levelZeroRows[0]

    return [pscustomobject]@{
        MaxHitPoints = Get-Number -Value $levelZero.maxHitPoints -Name "$DirectoryName maxHitPoints" -RequireInteger $true -Minimum 0
        Attack = Get-Number -Value $levelZero.attack -Name "$DirectoryName attack" -RequireInteger $true -Minimum 0
        Defense = Get-Number -Value $levelZero.defense -Name "$DirectoryName defense" -RequireInteger $true -Minimum 0
        MagicResistance = Get-Number -Value $levelZero.magicResistance -Name "$DirectoryName magicResistance" -RequireInteger $true -Minimum 0
        AttackIntervalSeconds = Get-Number -Value $levelZero.attackIntervalSeconds -Name "$DirectoryName attackIntervalSeconds" -RequireInteger $false -Minimum 0
        MoveSpeedMetresPerSecond = Get-Number -Value $levelZero.moveSpeedMetresPerSecond -Name "$DirectoryName moveSpeedMetresPerSecond" -RequireInteger $false -Minimum 0
        LifeDeduct = Get-Number -Value $levelZero.lifeDeduct -Name "$DirectoryName lifeDeduct" -RequireInteger $true -Minimum 0
    }
}

function Get-OnlyDirectory {
    param(
        [Parameter(Mandatory = $true)]$Directories,
        [Parameter(Mandatory = $true)][string]$TypeId
    )

    $prefix = '^' + [regex]::Escape($TypeId) + '_.+$'
    $matches = @($Directories | Where-Object { $_.Name -match $prefix -and $_.Name -notmatch '_[23]$' })
    Assert-Condition ($matches.Count -eq 1) "Expected exactly one base resource directory for TypeId '$TypeId'; found $($matches.Count)."
    return $matches[0]
}

function Get-DirectoryByName {
    param(
        [Parameter(Mandatory = $true)]$Directories,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $matches = @($Directories | Where-Object { $_.Name -ceq $Name })
    Assert-Condition ($matches.Count -eq 1) "Expected exactly one resource directory '$Name'; found $($matches.Count)."
    return $matches[0]
}

function Write-Utf8File {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string[]]$Lines
    )

    [System.IO.File]::WriteAllLines($Path, $Lines, [System.Text.UTF8Encoding]::new($false))
}

try {
    $BondSpecPath = Resolve-ExistingPath $BondSpecPath
    $StagingRoot = Resolve-ExistingPath $StagingRoot
    if (-not [string]::IsNullOrWhiteSpace($AbilityInputPath)) {
        $AbilityInputPath = Resolve-ExistingPath $AbilityInputPath
        Assert-Condition ([System.IO.Path]::GetExtension($AbilityInputPath) -ieq '.psd1') "AbilityInputPath '$AbilityInputPath' must use the .psd1 extension."
    }
    $OutputCsvPath = [System.IO.Path]::GetFullPath($OutputCsvPath)
    $AnalysisOutputPath = [System.IO.Path]::GetFullPath($AnalysisOutputPath)
    Assert-Condition ($OutputCsvPath -cne $AnalysisOutputPath) 'OutputCsvPath and AnalysisOutputPath must be different files.'

    $shopHeader = [string]::Concat('## ', [char]0x5546, [char]0x5E97, [char]0x5355, [char]0x4F4D, [char]0xFF08, '88', [char]0xFF09)
    $nonShopHeader = [string]::Concat('## ', [char]0x975E, [char]0x5546, [char]0x5E97, [char]0x5355, [char]0x4F4D, [char]0xFF08, '5', [char]0xFF09)
    $rarityLabel = [string]::Concat([char]0x7A00, [char]0x6709)
    $specLines = @(Get-Content -LiteralPath $BondSpecPath -Encoding UTF8)
    $shopTypeIds = @(Get-TypeIdsFromCodeBlock -Lines $specLines -Header $shopHeader -Label 'shop')
    $nonShopTypeIds = @(Get-TypeIdsFromCodeBlock -Lines $specLines -Header $nonShopHeader -Label 'non-shop')
    Assert-Condition ($shopTypeIds.Count -eq 88) "Expected 88 shop TypeIds; found $($shopTypeIds.Count)."
    Assert-Condition (($nonShopTypeIds -join '/') -ceq '1137/1138/2033/5504/10002') "Non-shop TypeIds must be 1137/1138/2033/5504/10002; found '$($nonShopTypeIds -join '/')'."

    $shopSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($typeId in $shopTypeIds) {
        [void]$shopSet.Add($typeId)
    }
    $nonShopSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($typeId in $nonShopTypeIds) {
        Assert-Condition ($nonShopSet.Add($typeId)) "Duplicate non-shop TypeId '$typeId'."
        Assert-Condition (-not $shopSet.Contains($typeId)) "TypeId '$typeId' appears in both shop and non-shop lists."
    }

    $unitPattern = '^(?<TypeId>\d+)\s+(?<DisplayName>.+?)\s+' + $rarityLabel + '(?<Rarity>[1-6])(?:\s|$)'
    $definitions = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
    foreach ($line in $specLines) {
        if ($line -notmatch $unitPattern) {
            continue
        }

        $typeId = $Matches.TypeId
        if (-not $shopSet.Contains($typeId)) {
            continue
        }
        $definition = [pscustomobject]@{
            DisplayName = $Matches.DisplayName.Trim()
            Rarity = [int]$Matches.Rarity
        }
        if ($definitions.ContainsKey($typeId)) {
            $existing = $definitions[$typeId]
            Assert-Condition ($existing.DisplayName -ceq $definition.DisplayName -and $existing.Rarity -eq $definition.Rarity) "TypeId '$typeId' has conflicting display-name or rarity definitions."
        }
        else {
            $definitions.Add($typeId, $definition)
        }
    }
    foreach ($typeId in $shopTypeIds) {
        Assert-Condition ($definitions.ContainsKey($typeId)) "Shop TypeId '$typeId' has no unit definition."
    }

    $expectedRarityCounts = @{ 1 = 9; 2 = 18; 3 = 12; 4 = 23; 5 = 20; 6 = 6 }
    foreach ($rarity in 1..6) {
        $count = @($definitions.Values | Where-Object { $_.Rarity -eq $rarity }).Count
        Assert-Condition ($count -eq $expectedRarityCounts[$rarity]) "Expected R$rarity=$($expectedRarityCounts[$rarity]); found $count."
    }

    $directories = @(Get-ChildItem -LiteralPath $StagingRoot -Directory)
    $damageTypeOverrides = @{ '1238' = 'Physical'; '1243' = 'Physical'; '10039' = 'Physical' }
    $allowedDamageTypes = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($damageType in @('Physical', 'Magic', 'None')) {
        [void]$allowedDamageTypes.Add($damageType)
    }
    $eliteEvidence = [System.Collections.Generic.List[object]]::new()
    $rows = foreach ($typeId in ($shopTypeIds | Sort-Object { [int]$_ })) {
        $resourceDirectory = if ($typeId -ceq '1322') {
            Get-DirectoryByName -Directories $directories -Name '1322_wdgyht_2'
        }
        else {
            Get-OnlyDirectory -Directories $directories -TypeId $typeId
        }
        if ($typeId -ceq '1322') {
            $eliteDirectory = Get-DirectoryByName -Directories $directories -Name '1322_wdgyht'
            $eliteEvidence.Add([pscustomobject][ordered]@{ TypeId = 1322; EliteLevel = 2; ResourceDirectory = $eliteDirectory.Name })
        }

        $levelsPath = Join-Path $resourceDirectory.FullName 'unit-levels.json'
        $sourcePath = Join-Path $resourceDirectory.FullName 'unit-source-v1.json'
        Assert-Condition (Test-Path -LiteralPath $levelsPath -PathType Leaf) "Resource directory '$($resourceDirectory.Name)' is missing unit-levels.json."
        Assert-Condition (Test-Path -LiteralPath $sourcePath -PathType Leaf) "Resource directory '$($resourceDirectory.Name)' is missing unit-source-v1.json."
        try {
            $levelsDocument = Get-Content -LiteralPath $levelsPath -Raw -Encoding UTF8 | ConvertFrom-Json
            $sourceDocument = Get-Content -LiteralPath $sourcePath -Raw -Encoding UTF8 | ConvertFrom-Json
        }
        catch {
            throw "Resource directory '$($resourceDirectory.Name)' contains invalid JSON: $($_.Exception.Message)"
        }
        Assert-Condition ([string]$sourceDocument.typeId -ceq $typeId) "Resource directory '$($resourceDirectory.Name)' source typeId '$($sourceDocument.typeId)' does not match '$typeId'."
        $levelZero = Get-RequiredLevelZero -LevelsDocument $levelsDocument -DirectoryName $resourceDirectory.Name

        if ($damageTypeOverrides.ContainsKey($typeId)) {
            $damageType = $damageTypeOverrides[$typeId]
            $damageTypeSource = 'ConfirmedOverride'
        }
        else {
            $damageType = [string]$sourceDocument.damageType
            Assert-Condition (-not [string]::IsNullOrWhiteSpace($damageType)) "TypeId '$typeId' is missing a damageType."
            Assert-Condition ($allowedDamageTypes.Contains($damageType)) "TypeId '$typeId' has invalid damageType '$damageType'."
            $damageTypeSource = 'Staging'
        }

        [pscustomobject][ordered]@{
            TypeId = [int]$typeId
            DisplayName = $definitions[$typeId].DisplayName
            Rarity = $definitions[$typeId].Rarity
            ResourceDirectory = $resourceDirectory.Name
            DamageType = $damageType
            DamageTypeSource = $damageTypeSource
            MaxHitPoints = $levelZero.MaxHitPoints
            Attack = $levelZero.Attack
            Defense = $levelZero.Defense
            MagicResistance = $levelZero.MagicResistance
            AttackIntervalSeconds = $levelZero.AttackIntervalSeconds
            EffectiveAttackIntervalSeconds = $levelZero.AttackIntervalSeconds * [decimal]0.5
            MoveSpeedMetresPerSecond = $levelZero.MoveSpeedMetresPerSecond
            LifeDeduct = $levelZero.LifeDeduct
        }
    }

    $rows = @($rows)
    Assert-Condition ($rows.Count -eq 88) "Expected 88 exported rows; found $($rows.Count)."
    foreach ($row in $rows) {
        Assert-Condition ($row.TypeId -notin @(1137, 1138, 2033, 5504, 10002)) "Non-shop TypeId '$($row.TypeId)' was exported."
    }

    $analysis = [ordered]@{
        SchemaVersion = 'unit-cost-analysis-v1'
        ShopRowCount = $rows.Count
        RarityDistribution = [ordered]@{ R1 = 9; R2 = 18; R3 = 12; R4 = 23; R5 = 20; R6 = 6 }
        EliteEvidence = @($eliteEvidence)
    }

    $outputDirectory = Split-Path -Parent $OutputCsvPath
    $analysisDirectory = Split-Path -Parent $AnalysisOutputPath
    [System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
    [System.IO.Directory]::CreateDirectory($analysisDirectory) | Out-Null
    $csvTemporaryPath = Join-Path $outputDirectory ('.' + [System.IO.Path]::GetRandomFileName())
    $analysisTemporaryPath = Join-Path $analysisDirectory ('.' + [System.IO.Path]::GetRandomFileName())
    try {
        Write-Utf8File -Path $csvTemporaryPath -Lines @($rows | ConvertTo-Csv -NoTypeInformation)
        Write-Utf8File -Path $analysisTemporaryPath -Lines @($analysis | ConvertTo-Json -Depth 5)
        Move-Item -LiteralPath $csvTemporaryPath -Destination $OutputCsvPath -Force
        Move-Item -LiteralPath $analysisTemporaryPath -Destination $AnalysisOutputPath -Force
    }
    finally {
        Remove-Item -LiteralPath $csvTemporaryPath, $analysisTemporaryPath -Force -ErrorAction SilentlyContinue
    }

    Write-Host "Exported $($rows.Count) shop rows to '$OutputCsvPath'."
    exit 0
}
catch {
    Write-Error $_
    exit 1
}
