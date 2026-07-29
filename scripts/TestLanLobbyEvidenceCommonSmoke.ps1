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
$script:assertionCount = 0
$script:fixtureCount = 0

function Assert-True([bool] $Condition, [string] $Message)
{
    $script:assertionCount++
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

function Convert-FromUtf8Base64([string] $Value)
{
    return [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($Value))
}

try
{
    . $commonScript
    New-Item -ItemType Directory -Force -Path $scratch | Out-Null

    $documentationContracts = [ordered]@{
        HostStartsReady = Convert-FromUtf8Base64 '5Yib5bu65oi/6Ze05pe277yM5oi/5Li75Yid5aeL5Li65bey5YeG5aSH44CC'
        GuestStartsUnready = Convert-FromUtf8Base64 '5paw5Yqg5YWl55qE5oiQ5ZGY5Yid5aeL5Li65pyq5YeG5aSH44CC'
        PresentMembersStart = Convert-FromUtf8Base64 '5byA5aeL5ri45oiP5Y+q6KaB5rGC5b2T5YmN5oi/6Ze05YaF5omA5pyJ5oiQ5ZGY5bey5YeG5aSH77yb56m65qe95L2N5LiN5Y+C5LiO5Yik5pat77yM5Lmf5LiN6KaB5rGC5ruh5Zub5Lq644CC'
        HostOnlyStart = Convert-FromUtf8Base64 '5LuF5oi/5Li75LiA5Lq655qE5oi/6Ze05Y+v5Lul56uL5Y2z5byA5aeL5ri45oiP44CC'
        HostLeaveDissolves = Convert-FromUtf8Base64 '5oi/5Li756a75byA5Lya6Kej5pWj5oi/6Ze05bm25YGc5q2i5p2D5aiB5oi/6Ze05pyN5Yqh44CC'
        NoHostMigration = Convert-FromUtf8Base64 '5LiN5pSv5oyB5oi/5Li76L+B56e75oiW5bCG5YW25LuW5oiQ5ZGY5pmL5Y2H5Li65oi/5Li744CC'
        GuestLeaveRestoresEmpty = Convert-FromUtf8Base64 '6Z2e5oi/5Li756a75byA5Y+q56e76Zmk6K+l5oiQ5ZGY5bm25oGi5aSN56m65qe944CC'
        GuestActionLabels = Convert-FromUtf8Base64 '5oiQ5ZGY5pON5L2c5qCH562+5Li64oCc5YeG5aSH5bCx57uq4oCd5LiO4oCc5Y+W5raI5YeG5aSH4oCd44CC'
        HostActionLabel = Convert-FromUtf8Base64 '5oi/5Li75pON5L2c5qCH562+5Li64oCc5Y2P6K6u5ZCv5Yqo4oCd44CC'
        PopupExclusion = Convert-FromUtf8Base64 '5Zu+MTLkuI7lm74xM+WboOWPs+S+p+W8ueeql+mBruaMoeiAjOaOkumZpOWujOaVtOeahOesrOWbm+S4queOqeWutuanve+8jOS4lOaOkumZpOmhueS4jeiusOS4uumAmui/h+OAgg=='
        VisibleArtworkAuthority = Convert-FromUtf8Base64 '6KeG6KeJ6aqM5pS25Lul5a6e6ZmF5riy5p+T55qE5Y+v6KeB5Zu+5b2i5Li65YeG77yM6ICM5LiN5piv57q555CG55+p5b2i5oiWUmVjdFRyYW5zZm9ybeS4reW/g+OAgg=='
    }
    $forbiddenDocumentationPatterns = [ordered]@{
        ObsoleteFourPlayerStart = Convert-FromUtf8Base64 '5oi/6Ze05YaFXHMq6L6+5YiwXHMqNFxzKuWQjeeOqeWutuWQjlxzKlss77yMXT9ccyrnlLHmiL/kuLvngrnlh7tccypb4oCcIl0/5byA5aeL5ri45oiPW+KAnSJdP1xzKlvjgIIuXT8='
    }
    foreach ($documentationPath in @(
        (Join-Path $projectRoot 'docs/SPEC.md'),
        (Join-Path $projectRoot 'docs/TEST_PLAN.md'),
        (Join-Path $projectRoot 'docs/LAN-LOBBY-REPORT.md')
    ))
    {
        $documentationText = [IO.File]::ReadAllText($documentationPath, [Text.Encoding]::UTF8)
        $script:fixtureCount++
        foreach ($contract in $documentationContracts.GetEnumerator())
        {
            Assert-True $documentationText.Contains([string]$contract.Value) "$([IO.Path]::GetFileName($documentationPath)) must state documentation contract '$($contract.Key)'."
        }
        foreach ($forbiddenPattern in $forbiddenDocumentationPatterns.GetEnumerator())
        {
            Assert-True (-not [regex]::IsMatch($documentationText, [string]$forbiddenPattern.Value)) "$([IO.Path]::GetFileName($documentationPath)) must not state obsolete documentation rule '$($forbiddenPattern.Key)'."
        }
    }

    $captureImage = Join-Path $projectRoot 'Assets/Resources/UI/Lobby/bg_terrain.png'
    $records = @('home', 'discovered-prefill', 'room-host', 'room-ready', 'room-full') | ForEach-Object {
        [pscustomobject]@{
            name = $_
            path = $captureImage
            spriteSources = @([pscustomobject]@{
                node = 'LanLobbyRoot/Terrain'
                spriteName = 'bg_terrain'
                resourcesPath = 'UI/Lobby/bg_terrain'
                sourcePath = '[uc]autochessouter/bg_terrain.png'
                sha256 = 'ECE7B6159268276287C20E3B3A82A5165BCC1D344EDFA6DE3B88EE24A76F988C'
                captures = @($_)
                occurrenceCount = 1
            })
        }
    }
    $manifestPath = Join-Path $scratch 'manifest.json'
    [pscustomobject]@{ captures = $records } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    $script:fixtureCount++

    $manifest = Get-LanLobbyCaptureManifest -ManifestPath $manifestPath
    $usage = @(Get-LanLobbySpriteUsage -ProjectRoot $projectRoot -Manifest $manifest -AssetMapPath $assetMapPath)
    Assert-True ($usage.Count -eq 5) 'One bg_terrain row is required per capture.'
    $first = $usage | Select-Object -First 1
    Assert-True ($first.SourcePath -eq '[uc]autochessouter/bg_terrain.png') 'Approved source path must be preserved.'
    Assert-True ($first.ResourcesPath -eq 'UI/Lobby/bg_terrain') 'Resources path must be parsed from ASSET_MAP.'
    Assert-True ($first.ImportedSha256 -match '^[A-Fa-f0-9]{64}$') 'Imported PNG SHA-256 must be complete.'

    $provenanceMutations = @(
        [pscustomobject]@{ Name = 'empty Resources path'; Field = 'resourcesPath'; Value = ''; Expected = 'Resources path' },
        [pscustomobject]@{ Name = 'wrong Resources path'; Field = 'resourcesPath'; Value = 'UI/Lobby/not-approved'; Expected = 'Resources path' },
        [pscustomobject]@{ Name = 'empty SHA-256'; Field = 'sha256'; Value = ''; Expected = 'SHA-256' },
        [pscustomobject]@{ Name = 'wrong SHA-256'; Field = 'sha256'; Value = ('0' * 64); Expected = 'SHA-256' },
        [pscustomobject]@{ Name = 'empty captures'; Field = 'captures'; Value = @(); Expected = 'capture list' },
        [pscustomobject]@{ Name = 'wrong captures'; Field = 'captures'; Value = @('room-ready'); Expected = 'capture list' },
        [pscustomobject]@{ Name = 'zero occurrence count'; Field = 'occurrenceCount'; Value = 0; Expected = 'occurrence count' },
        [pscustomobject]@{ Name = 'wrong occurrence count'; Field = 'occurrenceCount'; Value = 2; Expected = 'occurrence count' }
    )
    foreach ($mutation in $provenanceMutations)
    {
        $mutatedManifest = ([pscustomobject]@{ captures = $records } | ConvertTo-Json -Depth 8 | ConvertFrom-Json)
        $mutatedManifest.captures[0].spriteSources[0].($mutation.Field) = $mutation.Value
        $mutationOutput = Join-Path $scratch ('mutation-' + ($mutation.Name -replace '[^A-Za-z0-9]+', '-'))
        $script:fixtureCount++
        Assert-FailsWithoutOutput {
            Get-LanLobbySpriteUsage -ProjectRoot $projectRoot -Manifest $mutatedManifest -AssetMapPath $assetMapPath | Out-Null
        } $mutationOutput $mutation.Expected
    }

    $nullRecordManifestPath = Join-Path $scratch 'manifest-with-null-record.json'
    [pscustomobject]@{ captures = @($records; $null) } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $nullRecordManifestPath -Encoding UTF8
    $script:fixtureCount++
    Assert-FailsWithoutOutput {
        Get-LanLobbyCaptureManifest -ManifestPath $nullRecordManifestPath | Out-Null
    } $nullRecordOutput 'exactly five capture records'

    Assert-FailsWithoutOutput {
        Assert-LanLobbySafeOutputDirectory -ProjectRoot $projectRoot -OutputDirectory $unsafeOutput
    } $unsafeOutput 'safe ignored project directory'

    $unmappedManifest = [pscustomobject]@{ captures = @(
        [pscustomobject]@{ name = 'home'; path = $captureImage; spriteSources = @([pscustomobject]@{
            node = 'LanLobbyRoot/NotApproved'
            spriteName = 'not-approved'
            resourcesPath = 'UI/Lobby/not-approved'
            sourcePath = '[uc]autochessouter/not-approved.png'
            sha256 = ('0' * 64)
            captures = @('home')
            occurrenceCount = 1
        }) }
    ) }
    Assert-FailsWithoutOutput {
        Get-LanLobbySpriteUsage -ProjectRoot $projectRoot -Manifest $unmappedManifest -AssetMapPath $assetMapPath | Out-Null
    } $unmappedOutput 'Unmapped or non-approved sprite source'

    Assert-True ($script:fixtureCount -gt 0) 'zero generated fixtures is an explicit smoke failure'
    Assert-True ($script:assertionCount -gt 0) 'zero assertions is an explicit smoke failure'
    Write-Output "LAN lobby evidence common smoke: PASS (fixtures=$script:fixtureCount; assertions=$script:assertionCount)"
}
finally
{
    if (Test-Path -LiteralPath $unsafeOutput) { Remove-Item -LiteralPath $unsafeOutput -Force -Recurse }
    if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Force -Recurse }
}
