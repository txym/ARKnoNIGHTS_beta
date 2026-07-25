[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$exportScript = Join-Path $PSScriptRoot 'ExportLanLobbyEvidence.ps1'
$scratch = Join-Path $projectRoot ('Temp/LAN-LOBBY-ExportSmoke-' + [Guid]::NewGuid().ToString('N'))
$forbiddenAssetsOutput = Join-Path $projectRoot 'Assets/LanLobbyExportSmokeForbidden'

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
    if (-not $failed) { throw "Expected export failure: $ExpectedMessage" }
    if (Test-Path -LiteralPath $OutputPath) { throw "Export created output before validation: $OutputPath" }
}

try
{
    New-Item -ItemType Directory -Force -Path $scratch | Out-Null
    Assert-FailsWithoutOutput {
        & $exportScript -CaptureDirectory (Join-Path $scratch 'missing-captures') -OutputDirectory $forbiddenAssetsOutput
    } $forbiddenAssetsOutput 'safe ignored project directory'

    $captureDirectory = Join-Path $scratch 'captures'
    New-Item -ItemType Directory -Force -Path $captureDirectory | Out-Null
    $sourcePng = Join-Path $projectRoot 'Assets/Resources/UI/Lobby/bg_terrain.png'
    $records = @('home', 'discovered-prefill', 'room-host', 'room-ready', 'room-full') | ForEach-Object {
        [pscustomobject]@{
            name = $_
            path = $sourcePng
            spriteSources = @([pscustomobject]@{ spriteName = 'bg_terrain'; sourcePath = '[uc]autochessouter/bg_terrain.png' })
        }
    }
    [pscustomobject]@{ captures = $records } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $captureDirectory 'manifest.json') -Encoding UTF8

    $missingReferences = Join-Path $scratch 'missing-references'
    $missingOutput = Join-Path $scratch 'missing-reference-output'
    New-Item -ItemType Directory -Force -Path $missingReferences | Out-Null
    Assert-FailsWithoutOutput {
        & $exportScript -CaptureDirectory $captureDirectory -OutputDirectory $missingOutput -ReferenceDirectory $missingReferences
    } $missingOutput 'reference 9'

    $firstReferences = Join-Path $scratch 'references-a'
    $secondReferences = Join-Path $scratch 'references-b'
    foreach ($directory in @($firstReferences, $secondReferences))
    {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
        $figure9 = ([char]0x56FE).ToString() + '9.png'
        $figure10 = ([char]0x56FE).ToString() + '10.png'
        Copy-Item -LiteralPath $sourcePng -Destination (Join-Path $directory $figure9)
        Copy-Item -LiteralPath $sourcePng -Destination (Join-Path $directory $figure10)
    }
    $ambiguousOutput = Join-Path $scratch 'ambiguous-reference-output'
    Assert-FailsWithoutOutput {
        & $exportScript -CaptureDirectory $captureDirectory -OutputDirectory $ambiguousOutput -ReferenceDirectory @($firstReferences, $secondReferences)
    } $ambiguousOutput 'reference 9'

    $successfulOutput = Join-Path $scratch 'successful-output'
    & $exportScript -CaptureDirectory $captureDirectory -OutputDirectory $successfulOutput -ReferenceDirectory $firstReferences | Out-Null
    foreach ($captureName in @('home', 'discovered-prefill', 'room-host', 'room-ready', 'room-full'))
    {
        if (-not (Test-Path -LiteralPath (Join-Path $successfulOutput ($captureName + '-side-by-side.png'))))
        {
            throw "Expected side-by-side output for $captureName"
        }
    }
    if (-not (Test-Path -LiteralPath (Join-Path $successfulOutput 'manifest.json')))
    {
        throw 'Expected copied manifest in successful evidence export.'
    }

    Write-Output 'Export LAN lobby evidence smoke: PASS'
}
finally
{
    if (Test-Path -LiteralPath $forbiddenAssetsOutput) { Remove-Item -LiteralPath $forbiddenAssetsOutput -Force -Recurse }
    if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Force -Recurse }
}
