Set-StrictMode -Version Latest

function Import-LanLobbyEvidenceCommon
{
    # The file is intentionally dot-sourced by callers. This explicit import hook makes that
    # contract visible without creating a PowerShell module or writing any output.
    return $true
}

function Get-LanLobbyEvidenceProjectRoot
{
    param([string] $ProjectRoot)

    $root = [IO.Path]::GetFullPath($ProjectRoot)
    foreach ($required in @('Assets', 'Packages', 'ProjectSettings'))
    {
        if (-not (Test-Path -LiteralPath (Join-Path $root $required) -PathType Container))
        {
            throw "Unity project root is invalid; missing ${required}: $root"
        }
    }
    return $root
}

function Test-LanLobbyPathWithin
{
    param([string] $Candidate, [string] $Root)

    $candidatePath = [IO.Path]::GetFullPath($Candidate).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    return $candidatePath -ceq $rootPath -or $candidatePath.StartsWith($rootPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}

function Assert-LanLobbySafeOutputDirectory
{
    param([Parameter(Mandatory = $true)] [string] $ProjectRoot, [Parameter(Mandatory = $true)] [string] $OutputDirectory)

    $root = Get-LanLobbyEvidenceProjectRoot -ProjectRoot $ProjectRoot
    $candidate = [IO.Path]::GetFullPath($OutputDirectory)
    $temporaryRoot = Join-Path $root 'Temp'
    $artifactsRoot = Join-Path $root 'Artifacts'
    if (-not (Test-LanLobbyPathWithin -Candidate $candidate -Root $temporaryRoot) -and -not (Test-LanLobbyPathWithin -Candidate $candidate -Root $artifactsRoot))
    {
        throw "Evidence output must be inside the safe ignored project directory Temp/ or Artifacts/: $candidate"
    }
    return $candidate
}

function Get-LanLobbyCaptureManifest
{
    param([Parameter(Mandatory = $true)] [string] $ManifestPath)

    if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) { throw "Capture manifest not found: $ManifestPath" }
    try { $manifest = Get-Content -Raw -LiteralPath $ManifestPath | ConvertFrom-Json }
    catch { throw "Capture manifest is invalid JSON: $ManifestPath. $($_.Exception.Message)" }

    $captures = @($manifest.captures | Where-Object { $null -ne $_ })
    $expected = @('home', 'discovered-prefill', 'room-host', 'room-ready', 'room-full')
    if ($captures.Count -ne $expected.Count) { throw 'The manifest must contain exactly five capture records.' }
    if ((@($captures.name | Sort-Object) -join '|') -ne (@($expected | Sort-Object) -join '|')) { throw 'Capture names do not match the approved lobby evidence set.' }
    foreach ($capture in $captures)
    {
        if ([string]::IsNullOrWhiteSpace([string]$capture.path) -or -not (Test-Path -LiteralPath $capture.path -PathType Leaf))
        {
            throw "Capture image missing: $($capture.path)"
        }
    }
    return $manifest
}

function Get-LanLobbyAssetMap
{
    param([Parameter(Mandatory = $true)] [string] $ProjectRoot, [Parameter(Mandatory = $true)] [string] $AssetMapPath)

    $root = Get-LanLobbyEvidenceProjectRoot -ProjectRoot $ProjectRoot
    if (-not (Test-Path -LiteralPath $AssetMapPath -PathType Leaf)) { throw "Approved asset map not found: $AssetMapPath" }
    $entries = @{}
    foreach ($line in Get-Content -LiteralPath $AssetMapPath)
    {
        if ($line -notmatch '^\|\s*([^|]+\.png)\s*\|\s*([^|]+)\s*\|\s*([^|]+)\s*\|') { continue }
        $spriteFile = $matches[1].Trim()
        $sourcePath = $matches[2].Trim()
        $resourcesPath = $matches[3].Trim()
        $spriteName = [IO.Path]::GetFileNameWithoutExtension($spriteFile)
        if ($entries.ContainsKey($spriteName)) { throw "Asset map contains duplicate sprite entry: $spriteName" }
        $importedPng = Join-Path $root ('Assets/Resources/' + $resourcesPath + '.png')
        if (-not (Test-Path -LiteralPath $importedPng -PathType Leaf)) { throw "Approved imported sprite is missing: $importedPng" }
        $entries[$spriteName] = [pscustomobject]@{
            SpriteName = $spriteName
            ResourcesPath = $resourcesPath
            SourcePath = $sourcePath
            ImportedSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $importedPng).Hash
        }
    }
    if ($entries.Count -eq 0) { throw "Approved asset map has no usable entries: $AssetMapPath" }
    return $entries
}

function Get-LanLobbySpriteUsage
{
    param([Parameter(Mandatory = $true)] [string] $ProjectRoot, [Parameter(Mandatory = $true)] $Manifest, [Parameter(Mandatory = $true)] [string] $AssetMapPath)

    $assetMap = Get-LanLobbyAssetMap -ProjectRoot $ProjectRoot -AssetMapPath $AssetMapPath
    $usage = @()
    foreach ($capture in @($Manifest.captures))
    {
        $sources = @($capture.spriteSources | Where-Object { $null -ne $_ })
        if ($sources.Count -eq 0) { throw "Capture $($capture.name) has no sprite provenance." }
        $counts = @{}
        foreach ($sprite in $sources)
        {
            $spriteName = [string]$sprite.spriteName
            $sourcePath = [string]$sprite.sourcePath
            if ([string]::IsNullOrWhiteSpace($spriteName) -or [string]::IsNullOrWhiteSpace($sourcePath) -or -not $assetMap.ContainsKey($spriteName) -or $assetMap[$spriteName].SourcePath -cne $sourcePath)
            {
                throw "Unmapped or non-approved sprite source: $spriteName -> $sourcePath"
            }
            if (-not $counts.ContainsKey($spriteName)) { $counts[$spriteName] = 0 }
            $counts[$spriteName]++
        }
        foreach ($spriteName in ($counts.Keys | Sort-Object))
        {
            $entry = $assetMap[$spriteName]
            $usage += [pscustomobject]@{
                CaptureName = [string]$capture.name
                SpriteName = $entry.SpriteName
                ResourcesPath = $entry.ResourcesPath
                SourcePath = $entry.SourcePath
                ImportedSha256 = $entry.ImportedSha256
                OccurrenceCount = [int]$counts[$spriteName]
            }
        }
    }
    return $usage
}

function Resolve-LanLobbyReferenceImage
{
    param([Parameter(Mandatory = $true)] [ValidateSet('9', '10')] [string] $Suffix, [Parameter(Mandatory = $true)] [string[]] $ReferenceDirectory)

    $expectedName = ([char]0x56FE).ToString() + $Suffix + '.png'
    $matches = @(
        foreach ($directory in $ReferenceDirectory)
        {
            if (-not (Test-Path -LiteralPath $directory -PathType Container)) { throw "Reference directory not found: $directory" }
            Get-ChildItem -LiteralPath $directory -File -Filter '*.png' | Where-Object { $_.Name -ceq $expectedName }
        }
    )
    if ($matches.Count -ne 1) { throw "Expected exactly one reference $Suffix named $expectedName; found $($matches.Count)." }
    return $matches[0].FullName
}
