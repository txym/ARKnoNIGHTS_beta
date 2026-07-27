[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$SourceRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$approvedSourceRoot = [IO.Path]::GetFullPath(('G:\' + [char]0x7D20 + [char]0x6750 + '\11.14\Unpacked_1763129662\Android\ui\autochess')).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
$approvedCombinedRoot = [IO.Path]::GetFullPath(('G:\' + [char]0x7D20 + [char]0x6750 + '\11.14\Combined_1763139377\Android\ui\autochess')).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
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
    'team_icon_frame', 'team_hp_back', 'btn_match_host_normal', 'btn_match_host_grey', 'btn_match_grey', 'btn_match_cancel',
    'card_bg', 'bg_top_normal', 'bg_top_ready', 'card_empty', 'card_deco_self', 'bg_plus', 'btn_match_normal', 'btn_topmenu_back', 'host_top_tag'
)

$roomSelectAssetNames = @(
    'room_select_right_bg', 'room_select_title_icon', 'room_select_dot', 'room_select_img_startroom',
    'room_select_create_btn_bg_down', 'room_select_create_left_line', 'room_select_create_logo', 'room_select_create_middleicon', 'room_select_create_text_01', 'room_select_create_text_02',
    'room_select_join_ban', 'room_select_join_blank', 'room_select_join_btn_bg_down', 'room_select_join_left_block', 'room_select_join_logo', 'room_select_join_middle_block', 'room_select_join_middle_block_mask', 'room_select_join_right_block', 'room_select_join_text_01', 'room_select_join_text_02', 'room_select_join_text_bg', 'room_select_join_triangle',
    'img_pointer', 'doc_frame_line'
)

$avatarAssetNames = @('icon_amiy', 'icon_clementi', 'icon_kirar', 'icon_zumam')

$projectRoot = Split-Path -Parent $PSScriptRoot
$destinationDirectory = Join-Path $projectRoot 'Assets\Resources\UI\Lobby'
$homeDestinationDirectory = Join-Path $destinationDirectory 'Home'
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

foreach ($assetName in $roomSelectAssetNames)
{
    if ($assetName.EndsWith('0', [StringComparison]::Ordinal))
    {
        throw "Unpacked room-select assets ending in 0 are forbidden: $assetName"
    }

    $sourceFile = Join-Path $sourceDirectory ($assetName + '.png')
    if (-not (Test-Path -LiteralPath $sourceFile -PathType Leaf))
    {
        throw "Required approved room-select source asset is missing: $sourceFile"
    }
}

$combinedAvatarDirectory = Join-Path $approvedCombinedRoot '[uc]autochesscommon'
if (-not (Test-Path -LiteralPath $combinedAvatarDirectory -PathType Container))
{
    throw "Approved Combined avatar directory is missing: $combinedAvatarDirectory"
}

foreach ($assetName in $avatarAssetNames)
{
    $sourceFile = Join-Path $combinedAvatarDirectory ($assetName + '.png')
    if (-not (Test-Path -LiteralPath $sourceFile -PathType Leaf))
    {
        throw "Required approved Combined avatar source asset is missing: $sourceFile"
    }
}

New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
foreach ($assetName in $assetNames)
{
    Copy-Item -LiteralPath (Join-Path $sourceDirectory ($assetName + '.png')) -Destination (Join-Path $destinationDirectory ($assetName + '.png')) -Force
}

New-Item -ItemType Directory -Path $homeDestinationDirectory -Force | Out-Null
foreach ($assetName in $roomSelectAssetNames)
{
    Copy-Item -LiteralPath (Join-Path $sourceDirectory ($assetName + '.png')) -Destination (Join-Path $homeDestinationDirectory ($assetName + '.png')) -Force
}

foreach ($assetName in $avatarAssetNames)
{
    Copy-Item -LiteralPath (Join-Path $combinedAvatarDirectory ($assetName + '.png')) -Destination (Join-Path $homeDestinationDirectory ($assetName + '.png')) -Force
}

Write-Host "Imported $($assetNames.Count) lobby, $($roomSelectAssetNames.Count) room-select, and $($avatarAssetNames.Count) Combined avatar PNGs."
