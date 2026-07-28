[CmdletBinding()]
param(
    [string]$UnityPath,
    [string]$ProjectPath,
    [string]$OutputPath,
    [string]$LogPath,
    [ValidateRange(1, 3600)]
    [int]$TimeoutSeconds = 900,
    [switch]$NoGraphics
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$script:StrictUtf8 = New-Object System.Text.UTF8Encoding($false, $true)

function Test-BondsUnitAnimationAuditOutput {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    try {
        $fullPath = [System.IO.Path]::GetFullPath($Path)
        if (-not [System.IO.File]::Exists($fullPath)) {
            throw "file does not exist: $fullPath"
        }

        $json = [System.IO.File]::ReadAllText($fullPath, $script:StrictUtf8)
        $document = $json | ConvertFrom-Json
        if ($null -eq $document) {
            throw 'JSON document is null'
        }
        if ($document.schemaVersion -ne 'bonds-unit-animation-audit-v1') {
            throw "schemaVersion=$($document.schemaVersion)"
        }
        if ([int]$document.typeIdCount -ne 93) {
            throw "typeIdCount=$($document.typeIdCount)"
        }
        if ([int]$document.variantCount -ne 172) {
            throw "variantCount=$($document.variantCount)"
        }

        $variants = @($document.variants)
        if ($variants.Count -ne 172) {
            throw "variants.Count=$($variants.Count)"
        }

        $unitKeys = New-Object 'System.Collections.Generic.HashSet[string]' (
            [System.StringComparer]::Ordinal)
        foreach ($variant in $variants) {
            $unitKey = [string]$variant.unitKey
            if ([string]::IsNullOrWhiteSpace($unitKey)) {
                throw 'variant has an empty unitKey'
            }
            if (-not $unitKeys.Add($unitKey)) {
                throw "duplicate unitKey=$unitKey"
            }

            $animations = @($variant.animations)
            if ($animations.Count -lt 1) {
                throw "unitKey=$unitKey has no animations"
            }

            foreach ($animation in $animations) {
                $name = [string]$animation.name
                if ([string]::IsNullOrWhiteSpace($name)) {
                    throw "unitKey=$unitKey has an animation with an empty name"
                }

                try {
                    $duration = [double]$animation.durationSeconds
                } catch {
                    throw "unitKey=$unitKey animation=$name has a non-numeric duration"
                }
                if (($duration -lt 0) `
                    -or [double]::IsNaN($duration) `
                    -or [double]::IsInfinity($duration)) {
                    throw "unitKey=$unitKey animation=$name duration=$duration"
                }
            }
        }
    } catch {
        if ($_.Exception.Message.StartsWith(
                'BONDS_ANIMATION_AUDIT_OUTPUT_INVALID',
                [System.StringComparison]::Ordinal)) {
            throw
        }
        throw "BONDS_ANIMATION_AUDIT_OUTPUT_INVALID $($_.Exception.Message)"
    }
}

function Get-BondsUnitAnimationAuditSnapshot {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    Test-BondsUnitAnimationAuditOutput -Path $Path
    try {
        return [System.IO.File]::ReadAllText(
            [System.IO.Path]::GetFullPath($Path),
            $script:StrictUtf8)
    } catch {
        throw "BONDS_ANIMATION_AUDIT_OUTPUT_INVALID snapshot read failed: $($_.Exception.Message)"
    }
}

function ConvertTo-QuotedProcessArgument {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    if ($Value -notmatch '[\s"]') {
        return $Value
    }
    return '"' + ($Value -replace '(\\*)"', '$1$1\"' -replace '(\\*)$', '$1$1') + '"'
}

function Resolve-BondsAuditPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$BasePath
    )

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }
    return [System.IO.Path]::GetFullPath(
        [System.IO.Path]::Combine($BasePath, $Path))
}

function Assert-BondsAuditPathUnderDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Directory,
        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    $directoryPrefix = $Directory.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $Path.StartsWith(
            $directoryPrefix,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$Label must remain beneath the project Temp directory: $Path"
    }
}

function Invoke-BondsUnitAnimationAudit {
    [CmdletBinding()]
    param(
        [string]$RequestedUnityPath,
        [string]$RequestedProjectPath,
        [string]$RequestedOutputPath,
        [string]$RequestedLogPath,
        [ValidateRange(1, 3600)]
        [int]$RequestedTimeoutSeconds = 900,
        [switch]$RequestedNoGraphics
    )

    if ([string]::IsNullOrWhiteSpace($RequestedUnityPath)) {
        throw 'UnityPath is required when running the BONDS animation audit.'
    }
    if ([string]::IsNullOrWhiteSpace($RequestedProjectPath)) {
        $RequestedProjectPath = Split-Path -Parent $PSScriptRoot
    }

    $unityFullPath = [System.IO.Path]::GetFullPath($RequestedUnityPath)
    $projectFullPath = [System.IO.Path]::GetFullPath($RequestedProjectPath)
    if (-not [System.IO.File]::Exists($unityFullPath)) {
        throw "Unity.exe was not found: $unityFullPath"
    }
    foreach ($projectDirectory in @('Assets', 'Packages', 'ProjectSettings')) {
        $requiredPath = Join-Path $projectFullPath $projectDirectory
        if (-not [System.IO.Directory]::Exists($requiredPath)) {
            throw "ProjectPath is not a Unity project; missing $projectDirectory`: $projectFullPath"
        }
    }

    $tempRoot = [System.IO.Path]::GetFullPath(
        [System.IO.Path]::Combine($projectFullPath, 'Temp'))
    if ([string]::IsNullOrWhiteSpace($RequestedOutputPath)) {
        $RequestedOutputPath = 'Temp/bonds-unit-animation-audit-v1.json'
    }
    if ([string]::IsNullOrWhiteSpace($RequestedLogPath)) {
        $RequestedLogPath = 'Temp/bonds-unit-animation-audit.log'
    }

    $outputFullPath = Resolve-BondsAuditPath `
        -Path $RequestedOutputPath `
        -BasePath $projectFullPath
    $logFullPath = Resolve-BondsAuditPath `
        -Path $RequestedLogPath `
        -BasePath $projectFullPath
    Assert-BondsAuditPathUnderDirectory `
        -Path $outputFullPath `
        -Directory $tempRoot `
        -Label 'OutputPath'
    Assert-BondsAuditPathUnderDirectory `
        -Path $logFullPath `
        -Directory $tempRoot `
        -Label 'LogPath'

    $existingProjectProcesses = @(
        Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" |
            Where-Object {
                -not [string]::IsNullOrEmpty($_.CommandLine) `
                    -and $_.CommandLine.IndexOf(
                        $projectFullPath,
                        [System.StringComparison]::OrdinalIgnoreCase) -ge 0
            }
    )
    if ($existingProjectProcesses.Count -gt 0) {
        $processIds = ($existingProjectProcesses |
            ForEach-Object { $_.ProcessId }) -join ', '
        throw "Unity is already using this project (PID: $processIds). Close it before starting the audit."
    }

    foreach ($directoryPath in @(
            [System.IO.Path]::GetDirectoryName($outputFullPath),
            [System.IO.Path]::GetDirectoryName($logFullPath))) {
        [System.IO.Directory]::CreateDirectory($directoryPath) | Out-Null
    }
    foreach ($stalePath in @($outputFullPath, $logFullPath)) {
        if ([System.IO.File]::Exists($stalePath)) {
            Remove-Item -LiteralPath $stalePath -Force
        }
    }

    $arguments = @(
        '-batchmode',
        '-accept-apiupdate',
        '-projectPath',
        (ConvertTo-QuotedProcessArgument $projectFullPath),
        '-executeMethod',
        'BondsUnitAnimationAudit.Run',
        '-bondsAnimationAuditOutput',
        (ConvertTo-QuotedProcessArgument $outputFullPath),
        '-logFile',
        (ConvertTo-QuotedProcessArgument $logFullPath),
        '-quit'
    )
    if ($RequestedNoGraphics) {
        $arguments += '-nographics'
    }

    $unityProcess = $null
    $validatedOutputSnapshot = $null
    try {
        $unityProcess = Start-Process `
            -FilePath $unityFullPath `
            -ArgumentList ($arguments -join ' ') `
            -PassThru `
            -WindowStyle Hidden
        $deadline = [DateTime]::UtcNow.AddSeconds($RequestedTimeoutSeconds)
        while (-not $unityProcess.HasExited -and [DateTime]::UtcNow -lt $deadline) {
            if (($null -eq $validatedOutputSnapshot) `
                -and [System.IO.File]::Exists($outputFullPath)) {
                try {
                    $validatedOutputSnapshot =
                        Get-BondsUnitAnimationAuditSnapshot -Path $outputFullPath
                } catch {
                    # Unity may expose the file just before its final write completes.
                }
            }
            Start-Sleep -Milliseconds 100
        }
        if (-not $unityProcess.HasExited) {
            Stop-Process -Id $unityProcess.Id -Force
            $unityProcess.WaitForExit()
            throw "BONDS animation audit exceeded $RequestedTimeoutSeconds seconds. Log: $logFullPath"
        }
        if ($unityProcess.ExitCode -ne 0) {
            throw "Unity exited with code $($unityProcess.ExitCode). Log: $logFullPath"
        }
    } finally {
        if ($null -ne $unityProcess -and -not $unityProcess.HasExited) {
            Stop-Process -Id $unityProcess.Id -Force
            $unityProcess.WaitForExit()
        }
    }

    $remainingProjectProcesses = @()
    do {
        $remainingProjectProcesses = @(
            Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" |
                Where-Object {
                    -not [string]::IsNullOrEmpty($_.CommandLine) `
                        -and $_.CommandLine.IndexOf(
                            $projectFullPath,
                            [System.StringComparison]::OrdinalIgnoreCase) -ge 0
                }
        )
        if ($remainingProjectProcesses.Count -eq 0) {
            break
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($remainingProjectProcesses.Count -gt 0) {
        $processIds = ($remainingProjectProcesses |
            ForEach-Object { $_.ProcessId }) -join ', '
        throw "Unity project processes did not exit before the audit timeout (PID: $processIds). Log: $logFullPath"
    }

    if (-not [System.IO.File]::Exists($logFullPath)) {
        throw "Unity did not produce the audit log: $logFullPath"
    }
    $logText = [System.IO.File]::ReadAllText($logFullPath, $script:StrictUtf8)
    if ($logText.IndexOf(
            'BONDS_ANIMATION_AUDIT_COMPLETE',
            [System.StringComparison]::Ordinal) -lt 0) {
        throw "Unity log is missing BONDS_ANIMATION_AUDIT_COMPLETE: $logFullPath"
    }

    if (($null -eq $validatedOutputSnapshot) `
        -and [System.IO.File]::Exists($outputFullPath)) {
        $validatedOutputSnapshot =
            Get-BondsUnitAnimationAuditSnapshot -Path $outputFullPath
    }
    if ($null -eq $validatedOutputSnapshot) {
        throw "BONDS_ANIMATION_AUDIT_OUTPUT_INVALID no validated output was observed before Unity exited: $outputFullPath"
    }
    [System.IO.Directory]::CreateDirectory(
        [System.IO.Path]::GetDirectoryName($outputFullPath)) | Out-Null
    if ((-not [System.IO.File]::Exists($outputFullPath)) `
        -or [System.IO.File]::ReadAllText(
            $outputFullPath,
            $script:StrictUtf8) -cne $validatedOutputSnapshot) {
        [System.IO.File]::WriteAllText(
            $outputFullPath,
            $validatedOutputSnapshot,
            $script:StrictUtf8)
    }

    Test-BondsUnitAnimationAuditOutput -Path $outputFullPath
    $document = [System.IO.File]::ReadAllText(
        $outputFullPath,
        $script:StrictUtf8) | ConvertFrom-Json
    Write-Output (
        'BONDS_ANIMATION_AUDIT_RUN_VALID typeIds={0} variants={1} signatures={2} diagnostics={3} output={4} log={5}' `
            -f [int]$document.typeIdCount,
            [int]$document.variantCount,
            @($document.signatures).Count,
            @($document.diagnostics).Count,
            $outputFullPath,
            $logFullPath)
}

if ($MyInvocation.InvocationName -ne '.') {
    Invoke-BondsUnitAnimationAudit `
        -RequestedUnityPath $UnityPath `
        -RequestedProjectPath $ProjectPath `
        -RequestedOutputPath $OutputPath `
        -RequestedLogPath $LogPath `
        -RequestedTimeoutSeconds $TimeoutSeconds `
        -RequestedNoGraphics:$NoGraphics
}
