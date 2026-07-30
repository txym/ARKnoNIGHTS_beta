[CmdletBinding()]
param(
    [string]$InstallRoot = $env:ARKNIGHTS_INSTALL_ROOT,
    [switch]$SkipShortcuts
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
    $InstallRoot = Join-Path $env:LOCALAPPDATA 'Programs\ARKnoNIGHTS'
}
if ($env:ARKNIGHTS_INSTALL_SKIP_SHORTCUTS -eq '1') {
    $SkipShortcuts = $true
}
$archiveName = 'ARKnoNIGHTS-Windows-x64.zip'
$archivePath = Join-Path $PSScriptRoot $archiveName
if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) {
    throw "Installer payload is missing: $archiveName"
}

$installRootFull = [IO.Path]::GetFullPath($InstallRoot)
$installParent = Split-Path -Parent $installRootFull
if ([string]::IsNullOrWhiteSpace($installParent) -or
    $installRootFull -eq [IO.Path]::GetPathRoot($installRootFull)) {
    throw "Unsafe install root: $installRootFull"
}

[IO.Directory]::CreateDirectory($installParent) | Out-Null
$stagingRoot = $installRootFull + '.installing-' + [Guid]::NewGuid().ToString('N')
$backupRoot = $installRootFull + '.previous-' + [Guid]::NewGuid().ToString('N')

try {
    Expand-Archive -LiteralPath $archivePath -DestinationPath $stagingRoot
    $stagedExecutable = Join-Path $stagingRoot 'ARKnoNIGHTS.exe'
    $stagedData = Join-Path $stagingRoot 'ARKnoNIGHTS_Data'
    if (-not (Test-Path -LiteralPath $stagedExecutable -PathType Leaf) -or
        -not (Test-Path -LiteralPath $stagedData -PathType Container)) {
        throw 'Expanded payload is not a complete Unity Windows build.'
    }

    if (Test-Path -LiteralPath $installRootFull) {
        Move-Item -LiteralPath $installRootFull -Destination $backupRoot
    }
    Move-Item -LiteralPath $stagingRoot -Destination $installRootFull

    if (-not $SkipShortcuts) {
        $shell = New-Object -ComObject WScript.Shell
        $desktopShortcut = Join-Path (
            [Environment]::GetFolderPath('Desktop')
        ) 'ARKnoNIGHTS.lnk'
        $startMenuDirectory = Join-Path (
            [Environment]::GetFolderPath('Programs')
        ) 'ARKnoNIGHTS'
        [IO.Directory]::CreateDirectory($startMenuDirectory) | Out-Null
        foreach ($shortcutPath in @(
            $desktopShortcut,
            (Join-Path $startMenuDirectory 'ARKnoNIGHTS.lnk')
        )) {
            $shortcut = $shell.CreateShortcut($shortcutPath)
            $shortcut.TargetPath = Join-Path $installRootFull 'ARKnoNIGHTS.exe'
            $shortcut.WorkingDirectory = $installRootFull
            $shortcut.Save()
        }
    }

    if (Test-Path -LiteralPath $backupRoot) {
        Remove-Item -LiteralPath $backupRoot -Recurse -Force
    }
    Write-Host "ARKnoNIGHTS installed to $installRootFull"
}
catch {
    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
    if ((Test-Path -LiteralPath $backupRoot) -and
        -not (Test-Path -LiteralPath $installRootFull)) {
        Move-Item -LiteralPath $backupRoot -Destination $installRootFull
    }
    throw
}
