[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$SourceRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$approvedSourceRoot = [IO.Path]::GetFullPath(('G:\' + [char]0x7D20 + [char]0x6750 + '\11.14\Unpacked_1763129662\Android\ui\autochess')).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
$resolvedSourceRoot = (Resolve-Path -LiteralPath $SourceRoot -ErrorAction Stop).Path.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
if (-not [string]::Equals($resolvedSourceRoot, $approvedSourceRoot, [StringComparison]::OrdinalIgnoreCase))
{
    throw "SourceRoot must resolve to the approved autochess root: $approvedSourceRoot"
}

$sourceDirectory = Join-Path $resolvedSourceRoot '[uc]autochessouter'
if (-not (Test-Path -LiteralPath $sourceDirectory -PathType Container))
{
    throw "Approved source directory is missing: $sourceDirectory"
}

$assetNames = @(
    'bg_terrain', 'shallow_main', 'room_create_btn_bg', 'room_join_btn_bg', 'create_icon', 'join_icon',
    'img_player_bkg', 'img_player_confirmed', 'player_card_waiting', 'player_card_ready', 'player_card_self_frame',
    'team_icon_frame', 'team_hp_back', 'btn_match_host_normal', 'btn_match_host_grey', 'btn_match_grey', 'btn_match_cancel'
)

$projectRoot = Split-Path -Parent $PSScriptRoot
$destinationDirectory = Join-Path $projectRoot 'Assets\Resources\UI\Lobby'
if (Test-Path -LiteralPath $destinationDirectory)
{
    $unexpected = Get-ChildItem -LiteralPath $destinationDirectory -File -Filter '*.png' |
        Where-Object { $_.BaseName -notin $assetNames }
    if ($unexpected)
    {
        throw "Destination contains PNGs outside the approved lobby whitelist: $($unexpected.Name -join ', ')"
    }
}

foreach ($assetName in $assetNames)
{
    $sourceFile = Join-Path $sourceDirectory ($assetName + '.png')
    if (-not (Test-Path -LiteralPath $sourceFile -PathType Leaf))
    {
        throw "Required approved source asset is missing: $sourceFile"
    }
}

New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
foreach ($assetName in $assetNames)
{
    Copy-Item -LiteralPath (Join-Path $sourceDirectory ($assetName + '.png')) -Destination (Join-Path $destinationDirectory ($assetName + '.png')) -Force
}

Write-Host "Imported $($assetNames.Count) approved LAN lobby UI PNGs from [uc]autochessouter."
