[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BuildDirectory,
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..\..')
)
$buildRoot = [IO.Path]::GetFullPath($BuildDirectory)
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$requiredPaths = @(
    (Join-Path $buildRoot 'ARKnoNIGHTS.exe'),
    (Join-Path $buildRoot 'ARKnoNIGHTS_Data'),
    (Join-Path $buildRoot 'UnityPlayer.dll')
)
foreach ($requiredPath in $requiredPaths) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Windows build payload is incomplete: $requiredPath"
    }
}

$iexpress = Join-Path $env:WINDIR 'System32\iexpress.exe'
if (-not (Test-Path -LiteralPath $iexpress -PathType Leaf)) {
    throw "Windows IExpress is unavailable: $iexpress"
}

[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
$portableZip = Join-Path $outputRoot 'ARKnoNIGHTS-Windows-x64.zip'
$setupPath = Join-Path $outputRoot 'ARKnoNIGHTS-Setup-x64.exe'
$summaryPath = Join-Path $outputRoot 'pc-installer-summary.txt'
if (Test-Path -LiteralPath $portableZip) {
    Remove-Item -LiteralPath $portableZip -Force
}
if (Test-Path -LiteralPath $setupPath) {
    Remove-Item -LiteralPath $setupPath -Force
}

Compress-Archive -Path (Join-Path $buildRoot '*') `
    -DestinationPath $portableZip `
    -CompressionLevel Optimal

$stagingRoot = Join-Path $outputRoot (
    '.iexpress-' + [Guid]::NewGuid().ToString('N')
)
[IO.Directory]::CreateDirectory($stagingRoot) | Out-Null
try {
    $payloadZip = Join-Path $stagingRoot 'ARKnoNIGHTS-Windows-x64.zip'
    $installScript = Join-Path $stagingRoot 'Install-ARKnoNIGHTS.ps1'
    $installerLauncher = Join-Path $stagingRoot (
        'Launch-ARKnoNIGHTS-Installer.cmd'
    )
    Copy-Item -LiteralPath $portableZip -Destination $payloadZip
    Copy-Item -LiteralPath (
        Join-Path $repositoryRoot 'scripts\packaging\Install-ARKnoNIGHTS.ps1'
    ) -Destination $installScript
    Copy-Item -LiteralPath (
        Join-Path $repositoryRoot (
            'scripts\packaging\Launch-ARKnoNIGHTS-Installer.cmd'
        )
    ) -Destination $installerLauncher

    $sourceRoot = $stagingRoot.TrimEnd('\') + '\'
    $sedPath = Join-Path $stagingRoot 'ARKnoNIGHTS-Setup.sed'
    $sed = @"
[Version]
Class=IEXPRESS
SEDVersion=3

[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=1
HideExtractAnimation=0
UseLongFileName=1
InsideCompressed=0
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=
DisplayLicense=
FinishMessage=ARKnoNIGHTS installation completed.
TargetName=$setupPath
FriendlyName=ARKnoNIGHTS Setup
AppLaunched=Launch-ARKnoNIGHTS-Installer.cmd
PostInstallCmd=<None>
AdminQuietInstCmd=Launch-ARKnoNIGHTS-Installer.cmd
UserQuietInstCmd=Launch-ARKnoNIGHTS-Installer.cmd
SourceFiles=SourceFiles

[Strings]
FILE0="ARKnoNIGHTS-Windows-x64.zip"
FILE1="Install-ARKnoNIGHTS.ps1"
FILE2="Launch-ARKnoNIGHTS-Installer.cmd"

[SourceFiles]
SourceFiles0=$sourceRoot

[SourceFiles0]
%FILE0%=
%FILE1%=
%FILE2%=
"@
    [IO.File]::WriteAllText(
        $sedPath,
        $sed,
        [Text.Encoding]::ASCII
    )

    $process = Start-Process -FilePath $iexpress `
        -ArgumentList @('/N', '/Q', $sedPath) `
        -PassThru `
        -Wait `
        -WindowStyle Hidden
    if ($process.ExitCode -ne 0) {
        throw "IExpress failed with exit code $($process.ExitCode)."
    }
    if (-not (Test-Path -LiteralPath $setupPath -PathType Leaf)) {
        throw 'IExpress did not create the setup executable.'
    }
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}

$zipHash = (Get-FileHash -LiteralPath $portableZip -Algorithm SHA256).Hash
$setupHash = (Get-FileHash -LiteralPath $setupPath -Algorithm SHA256).Hash
$zipLength = (Get-Item -LiteralPath $portableZip).Length
$setupLength = (Get-Item -LiteralPath $setupPath).Length
$summary = @(
    'result=Succeeded'
    "portableZip=$portableZip"
    "portableZipBytes=$zipLength"
    "portableZipSha256=$zipHash"
    "setup=$setupPath"
    "setupBytes=$setupLength"
    "setupSha256=$setupHash"
)
[IO.File]::WriteAllLines(
    $summaryPath,
    $summary,
    [Text.Encoding]::UTF8
)
$summary | ForEach-Object { Write-Host $_ }
