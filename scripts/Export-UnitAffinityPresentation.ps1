[CmdletBinding()]
param(
    [string]$SpecificationPath,
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($SpecificationPath))
{
    $SpecificationPath = Join-Path $repositoryRoot 'docs/bonds/BONDS_SPEC.md'
}
if ([string]::IsNullOrWhiteSpace($OutputPath))
{
    $OutputPath = Join-Path $repositoryRoot 'Assets/Resources/UI/Data/unit-affinity-presentation-v1.json'
}

$specificationFullPath = [IO.Path]::GetFullPath($SpecificationPath)
$outputFullPath = [IO.Path]::GetFullPath($OutputPath)
if (-not (Test-Path -LiteralPath $specificationFullPath -PathType Leaf))
{
    throw "BONDS specification was not found: $specificationFullPath"
}

function Expand-Unicode
{
    param([Parameter(Mandatory = $true)][string]$Value)
    return [regex]::Unescape($Value)
}

$regionDefinitions = @(
    [ordered]@{ id = 'ursus'; displayName = (Expand-Unicode '\u4e4c\u8428\u65af'); iconResourcePath = 'UI/Texture/region/logo_ursus' },
    [ordered]@{ id = 'leithanien'; displayName = (Expand-Unicode '\u83b1\u5854\u5c3c\u4e9a'); iconResourcePath = 'UI/Texture/region/logo_Leithanien' },
    [ordered]@{ id = 'victoria'; displayName = (Expand-Unicode '\u7ef4\u591a\u5229\u4e9a'); iconResourcePath = 'UI/Texture/region/logo_victoria' },
    [ordered]@{ id = 'columbia'; displayName = (Expand-Unicode '\u54e5\u4f26\u6bd4\u4e9a'); iconResourcePath = 'UI/Texture/region/logo_columbia' },
    [ordered]@{ id = 'sargon'; displayName = (Expand-Unicode '\u8428\u5c14\u8d21'); iconResourcePath = 'UI/Texture/region/logo_sargon' },
    [ordered]@{ id = 'aegir'; displayName = (Expand-Unicode '\u963f\u6208\u5c14'); iconResourcePath = 'UI/Texture/region/logo_egir' },
    [ordered]@{ id = 'siracusa'; displayName = (Expand-Unicode '\u53d9\u62c9\u53e4'); iconResourcePath = 'UI/Texture/region/logo_siracusa' },
    [ordered]@{ id = 'reunion'; displayName = (Expand-Unicode '\u6574\u5408\u8fd0\u52a8'); iconResourcePath = 'UI/Texture/region/logo_reunionMovement' }
)

$occupationDefinitions = @(
    [ordered]@{ id = 'infected'; displayName = (Expand-Unicode '\u611f\u67d3\u751f\u7269'); iconResourcePath = 'UI/Texture/occupation/r_enemy_slime_repbsl_3' },
    [ordered]@{ id = 'drone'; displayName = (Expand-Unicode '\u65e0\u4eba\u673a'); iconResourcePath = 'UI/Texture/occupation/r_defdrn_up_2' },
    [ordered]@{ id = 'creation'; displayName = (Expand-Unicode '\u9020\u7269'); iconResourcePath = 'UI/Texture/occupation/r_global_magic_resist_2' },
    [ordered]@{ id = 'mechanical'; displayName = (Expand-Unicode '\u673a\u68b0'); iconResourcePath = 'UI/Texture/occupation/CHIPS' },
    [ordered]@{ id = 'collapsal'; displayName = (Expand-Unicode '\u574d\u7f29\u4f53'); iconResourcePath = 'UI/Texture/occupation/logo_sami' },
    [ordered]@{ id = 'other'; displayName = (Expand-Unicode '\u5176\u4ed6'); iconResourcePath = '' }
)

function Find-ExactLine
{
    param(
        [string[]]$Lines,

        [Parameter(Mandatory = $true)]
        [string]$Expected
    )

    for ($index = 0; $index -lt $Lines.Count; $index++)
    {
        if ($Lines[$index].Trim() -ceq $Expected) { return $index }
    }

    throw "Required BONDS heading is missing: $Expected"
}

function Read-ShopTypeIds
{
    param([string[]]$Lines)

    $headingIndex = Find-ExactLine -Lines $Lines -Expected (
        '## ' + (Expand-Unicode '\u5546\u5e97\u5355\u4f4d\uff0894\uff09'))
    $inCodeBlock = $false
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    for ($index = $headingIndex + 1; $index -lt $Lines.Count; $index++)
    {
        $line = $Lines[$index].Trim()
        if ($line -ceq '```text')
        {
            $inCodeBlock = $true
            continue
        }
        if ($inCodeBlock -and $line -ceq '```') { break }
        if (-not $inCodeBlock) { continue }

        foreach ($match in [regex]::Matches($line, '\d+'))
        {
            if (-not $ids.Add($match.Value))
            {
                throw "Duplicate shop TypeId in BONDS list: $($match.Value)"
            }
        }
    }

    if ($ids.Count -ne 94)
    {
        throw "BONDS shop list must contain exactly 94 unique TypeIds; actual=$($ids.Count)"
    }

    return $ids
}

function Read-Membership
{
    param(
        [string[]]$Lines,

        [Parameter(Mandatory = $true)]
        [string]$StartHeading,

        [string]$EndHeading,

        [Parameter(Mandatory = $true)]
        [hashtable]$DefinitionIdsByName,

        [Parameter(Mandatory = $true)]
        [Collections.Generic.HashSet[string]]$ShopTypeIds
    )

    $startIndex = Find-ExactLine -Lines $Lines -Expected $StartHeading
    $endIndex = if ([string]::IsNullOrWhiteSpace($EndHeading))
    {
        $Lines.Count
    }
    else
    {
        Find-ExactLine -Lines $Lines -Expected $EndHeading
    }
    $result = @{}
    $currentId = $null
    for ($index = $startIndex + 1; $index -lt $endIndex; $index++)
    {
        $line = $Lines[$index].Trim()
        if ($line.StartsWith('## ', [StringComparison]::Ordinal))
        {
            $name = $line.Substring(3).Trim()
            $currentId = if ($DefinitionIdsByName.ContainsKey($name))
            {
                [string]$DefinitionIdsByName[$name]
            }
            else
            {
                $null
            }
            continue
        }

        if ($null -eq $currentId) { continue }
        $match = [regex]::Match($line, '^(\d+)\b')
        if (-not $match.Success) { continue }
        $typeId = $match.Groups[1].Value
        if (-not $ShopTypeIds.Contains($typeId)) { continue }
        if ($result.ContainsKey($typeId))
        {
            throw "TypeId appears in multiple '$StartHeading' sections: $typeId"
        }
        $result[$typeId] = $currentId
    }

    return $result
}

$lines = [IO.File]::ReadAllLines($specificationFullPath, [Text.Encoding]::UTF8)
$shopTypeIds = Read-ShopTypeIds -Lines $lines
if ($shopTypeIds.Contains('1000'))
{
    throw "Compatibility TypeId 1000 is canonical again; remove or revise the explicit compatibility record."
}

$occupationIdsByName = @{}
foreach ($definition in $occupationDefinitions)
{
    $occupationIdsByName[$definition.displayName] = $definition.id
}
$regionIdsByName = @{}
foreach ($definition in $regionDefinitions)
{
    $regionIdsByName[$definition.displayName] = $definition.id
}

$occupationsByTypeId = Read-Membership `
    -Lines $lines `
    -StartHeading ('# ' + (Expand-Unicode '\u6309\u79cd\u7c7b\u7ec4\u7ec7\u7684\u5355\u4f4d')) `
    -EndHeading ('# ' + (Expand-Unicode '\u79cd\u7c7b\u4e0e\u5730\u533a\u8986\u76d6\u5ba1\u8ba1')) `
    -DefinitionIdsByName $occupationIdsByName `
    -ShopTypeIds $shopTypeIds
$regionsByTypeId = Read-Membership `
    -Lines $lines `
    -StartHeading ('# ' + (Expand-Unicode '\u6309\u5730\u533a\u7ec4\u7ec7\u7684\u5355\u4f4d')) `
    -DefinitionIdsByName $regionIdsByName `
    -ShopTypeIds $shopTypeIds

foreach ($typeId in $shopTypeIds)
{
    if (-not $occupationsByTypeId.ContainsKey($typeId) -and -not $regionsByTypeId.ContainsKey($typeId))
    {
        throw "Shop TypeId has neither occupation nor region in BONDS: $typeId"
    }
}
if ($occupationsByTypeId.Count -ne 86)
{
    throw "Occupation membership count mismatch; expected=86 actual=$($occupationsByTypeId.Count)"
}
if ($regionsByTypeId.Count -ne 81)
{
    throw "Region membership count mismatch; expected=81 actual=$($regionsByTypeId.Count)"
}
$noRegionCount = $shopTypeIds.Count - $regionsByTypeId.Count
if ($noRegionCount -ne 13)
{
    throw "No-region shop unit count mismatch; expected=13 actual=$noRegionCount"
}

$unitRecords = @(
    $shopTypeIds |
        Sort-Object { [int]$_ } |
        ForEach-Object {
            $typeId = $_
            [ordered]@{
                typeId = $typeId
                regionId = if ($regionsByTypeId.ContainsKey($typeId)) { $regionsByTypeId[$typeId] } else { '' }
                occupationId = if ($occupationsByTypeId.ContainsKey($typeId)) { $occupationsByTypeId[$typeId] } else { '' }
            }
        }
    [ordered]@{
        typeId = '1000'
        regionId = 'reunion'
        occupationId = 'infected'
    }
)
$unitRecords = @($unitRecords | Sort-Object { [int]$_.typeId })

$document = [ordered]@{
    schemaVersion = 'unit-affinity-presentation-v1'
    regions = $regionDefinitions
    occupations = $occupationDefinitions
    units = $unitRecords
}
$json = $document | ConvertTo-Json -Depth 6
$outputDirectory = Split-Path -Parent $outputFullPath
[IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
$utf8WithoutBom = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText($outputFullPath, $json + [Environment]::NewLine, $utf8WithoutBom)

Write-Host ('AFFINITY_UI_EXPORTED schema=unit-affinity-presentation-v1' + " canonicalUnits=$($shopTypeIds.Count)" + ' compatibilityUnits=1' + " totalUnits=$($unitRecords.Count)" + " canonicalRegions=$($regionsByTypeId.Count)" + " canonicalNoRegion=$noRegionCount" + " canonicalOccupations=$($occupationsByTypeId.Count)" + " canonicalNoOccupation=$($shopTypeIds.Count - $occupationsByTypeId.Count)" + " occupationDefinitions=$($occupationDefinitions.Count)")
