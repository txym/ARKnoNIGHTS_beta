[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$commonScript = Join-Path $PSScriptRoot 'LanLobbyEvidence.Common.ps1'
$scratch = Join-Path $projectRoot ('Temp/LAN-LOBBY-CommonSmoke-' + [Guid]::NewGuid().ToString('N'))
$unsafeOutput = Join-Path $projectRoot 'Assets/LanLobbyCommonSmokeForbidden'
$unmappedOutput = Join-Path $scratch 'unmapped-output'
$nullRecordOutput = Join-Path $scratch 'null-record-output'
$assetMapPath = Join-Path $projectRoot 'docs/references/ui/lobby/ASSET_MAP.md'

function Assert-True([bool] $Condition, [string] $Message)
{
    if (-not $Condition) { throw "Assertion failed: $Message" }
}

function Assert-FailsWithoutOutput([scriptblock] $Action, [string] $OutputPath, [string] $ExpectedMessage)
{
    $failed = $false
    try { & $Action }
    catch
    {
        $failed = $true
        if ($_.Exception.Message -notlike ('*' + $ExpectedMessage + '*'))
        {
            throw "Expected failure containing '$ExpectedMessage', actual: $($_.Exception.Message)"
        }
    }
    if (-not $failed) { throw "Expected failure: $ExpectedMessage" }
    Assert-True (-not (Test-Path -LiteralPath $OutputPath)) "Output must not exist after failure: $OutputPath"
}

try
{
    . $commonScript
    New-Item -ItemType Directory -Force -Path $scratch | Out-Null
    $captureImage = Join-Path $projectRoot 'Assets/Resources/UI/Lobby/bg_terrain.png'
    $records = @('home', 'discovered-prefill', 'room-host', 'room-ready', 'room-full') | ForEach-Object {
        [pscustomobject]@{
            name = $_
            path = $captureImage
            spriteSources = @([pscustomobject]@{ spriteName = 'bg_terrain'; sourcePath = '[uc]autochessouter/bg_terrain.png' })
        }
    }
    $manifestPath = Join-Path $scratch 'manifest.json'
    [pscustomobject]@{ captures = $records } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

    $manifest = Get-LanLobbyCaptureManifest -ManifestPath $manifestPath
    $usage = @(Get-LanLobbySpriteUsage -ProjectRoot $projectRoot -Manifest $manifest -AssetMapPath $assetMapPath)
    Assert-True ($usage.Count -eq 5) 'One bg_terrain row is required per capture.'
    $first = $usage | Select-Object -First 1
    Assert-True ($first.SourcePath -eq '[uc]autochessouter/bg_terrain.png') 'Approved source path must be preserved.'
    Assert-True ($first.ResourcesPath -eq 'UI/Lobby/bg_terrain') 'Resources path must be parsed from ASSET_MAP.'
    Assert-True ($first.ImportedSha256 -match '^[A-Fa-f0-9]{64}$') 'Imported PNG SHA-256 must be complete.'

    $nullRecordManifestPath = Join-Path $scratch 'manifest-with-null-record.json'
    [pscustomobject]@{ captures = @($records; $null) } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $nullRecordManifestPath -Encoding UTF8
    Assert-FailsWithoutOutput {
        Get-LanLobbyCaptureManifest -ManifestPath $nullRecordManifestPath | Out-Null
    } $nullRecordOutput 'exactly five capture records'

    Assert-FailsWithoutOutput {
        Assert-LanLobbySafeOutputDirectory -ProjectRoot $projectRoot -OutputDirectory $unsafeOutput
    } $unsafeOutput 'safe ignored project directory'

    $unmappedManifest = [pscustomobject]@{ captures = @(
        [pscustomobject]@{ name = 'home'; path = $captureImage; spriteSources = @([pscustomobject]@{ spriteName = 'not-approved'; sourcePath = '[uc]autochessouter/not-approved.png' }) }
    ) }
    Assert-FailsWithoutOutput {
        Get-LanLobbySpriteUsage -ProjectRoot $projectRoot -Manifest $unmappedManifest -AssetMapPath $assetMapPath | Out-Null
    } $unmappedOutput 'Unmapped or non-approved sprite source'

    Write-Output 'LAN lobby evidence common smoke: PASS'
}
finally
{
    if (Test-Path -LiteralPath $unsafeOutput) { Remove-Item -LiteralPath $unsafeOutput -Force -Recurse }
    if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Force -Recurse }
}
