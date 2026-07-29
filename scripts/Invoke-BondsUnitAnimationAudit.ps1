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

function Get-BondsAuditRequiredProperty {
    param(
        [Parameter(Mandatory = $true)]
        [object]$InputObject,
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property -or [object]::ReferenceEquals($property.Value, $null)) {
        throw "$Context is missing required property $Name"
    }
    return $property
}

function Get-BondsAuditRequiredString {
    param(
        [Parameter(Mandatory = $true)]
        [object]$InputObject,
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    $value = (Get-BondsAuditRequiredProperty `
        -InputObject $InputObject `
        -Name $Name `
        -Context $Context).Value
    if ($value -isnot [string] -or [string]::IsNullOrWhiteSpace($value)) {
        throw "$Context property $Name must be a non-empty string"
    }
    return [string]$value
}

function Test-BondsAuditJsonNumber {
    param([object]$Value)

    if ([object]::ReferenceEquals($Value, $null)) {
        return $false
    }
    $typeCode = [System.Type]::GetTypeCode($Value.GetType())
    return @(
        [System.TypeCode]::SByte,
        [System.TypeCode]::Byte,
        [System.TypeCode]::Int16,
        [System.TypeCode]::UInt16,
        [System.TypeCode]::Int32,
        [System.TypeCode]::UInt32,
        [System.TypeCode]::Int64,
        [System.TypeCode]::UInt64,
        [System.TypeCode]::Single,
        [System.TypeCode]::Double,
        [System.TypeCode]::Decimal
    ) -contains $typeCode
}

function Get-BondsAuditRequiredInteger {
    param(
        [Parameter(Mandatory = $true)]
        [object]$InputObject,
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    $value = (Get-BondsAuditRequiredProperty `
        -InputObject $InputObject `
        -Name $Name `
        -Context $Context).Value
    if (-not (Test-BondsAuditJsonNumber $value)) {
        throw "$Context property $Name must be an integer"
    }
    $numericValue = [decimal]$value
    if (($numericValue -ne [decimal]::Truncate($numericValue)) `
        -or $numericValue -lt [int]::MinValue `
        -or $numericValue -gt [int]::MaxValue) {
        throw "$Context property $Name must be a 32-bit integer"
    }
    return [int]$numericValue
}

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

        $schemaVersion = Get-BondsAuditRequiredString `
            -InputObject $document `
            -Name 'schemaVersion' `
            -Context 'document'
        if ($schemaVersion -ne 'bonds-unit-animation-audit-v1') {
            throw "schemaVersion=$schemaVersion"
        }
        Get-BondsAuditRequiredString `
            -InputObject $document `
            -Name 'generatedAtUtc' `
            -Context 'document' | Out-Null
        $typeIdCount = Get-BondsAuditRequiredInteger `
            -InputObject $document `
            -Name 'typeIdCount' `
            -Context 'document'
        if ($typeIdCount -ne 99) {
            throw "typeIdCount=$typeIdCount"
        }
        $variantCount = Get-BondsAuditRequiredInteger `
            -InputObject $document `
            -Name 'variantCount' `
            -Context 'document'
        if ($variantCount -ne 182) {
            throw "variantCount=$variantCount"
        }

        $variants = @((Get-BondsAuditRequiredProperty `
            -InputObject $document `
            -Name 'variants' `
            -Context 'document').Value)
        if ($variants.Count -ne 182) {
            throw "variants.Count=$($variants.Count)"
        }

        $unitKeys = New-Object 'System.Collections.Generic.HashSet[string]' (
            [System.StringComparer]::Ordinal)
        $typeIds = New-Object 'System.Collections.Generic.HashSet[int]'
        foreach ($variant in $variants) {
            $unitKey = Get-BondsAuditRequiredString `
                -InputObject $variant `
                -Name 'unitKey' `
                -Context 'variant'
            if (-not $unitKeys.Add($unitKey)) {
                throw "duplicate unitKey=$unitKey"
            }
            $typeId = Get-BondsAuditRequiredInteger `
                -InputObject $variant `
                -Name 'typeId' `
                -Context "variant unitKey=$unitKey"
            $typeIds.Add($typeId) | Out-Null
            Get-BondsAuditRequiredString `
                -InputObject $variant `
                -Name 'sourceUnitKey' `
                -Context "variant unitKey=$unitKey" | Out-Null
            Get-BondsAuditRequiredString `
                -InputObject $variant `
                -Name 'skeletonDataResourcePath' `
                -Context "variant unitKey=$unitKey" | Out-Null
            Get-BondsAuditRequiredString `
                -InputObject $variant `
                -Name 'exactNameSignature' `
                -Context "variant unitKey=$unitKey" | Out-Null
            $tokens = @((Get-BondsAuditRequiredProperty `
                -InputObject $variant `
                -Name 'caseFoldedTokenSummary' `
                -Context "variant unitKey=$unitKey").Value)
            if (($tokens.Count -lt 1) `
                -or @($tokens | Where-Object {
                    $_ -isnot [string] -or [string]::IsNullOrWhiteSpace($_)
                }).Count -gt 0) {
                throw "unitKey=$unitKey has an invalid caseFoldedTokenSummary"
            }

            $animations = @((Get-BondsAuditRequiredProperty `
                -InputObject $variant `
                -Name 'animations' `
                -Context "variant unitKey=$unitKey").Value)
            if ($animations.Count -lt 1) {
                throw "unitKey=$unitKey has no animations"
            }
            $animationCount = Get-BondsAuditRequiredInteger `
                -InputObject $variant `
                -Name 'animationCount' `
                -Context "variant unitKey=$unitKey"
            if ($animationCount -ne $animations.Count) {
                throw "unitKey=$unitKey animationCount=$animationCount actual=$($animations.Count)"
            }

            foreach ($animation in $animations) {
                $name = Get-BondsAuditRequiredString `
                    -InputObject $animation `
                    -Name 'name' `
                    -Context "animation unitKey=$unitKey"
                $durationValue = (Get-BondsAuditRequiredProperty `
                    -InputObject $animation `
                    -Name 'durationSeconds' `
                    -Context "animation unitKey=$unitKey name=$name").Value
                if (-not (Test-BondsAuditJsonNumber $durationValue)) {
                    throw "unitKey=$unitKey animation=$name has a non-numeric duration"
                }
                $duration = [double]$durationValue
                if (($duration -lt 0) `
                    -or [double]::IsNaN($duration) `
                    -or [double]::IsInfinity($duration)) {
                    throw "unitKey=$unitKey animation=$name duration=$duration"
                }
            }
        }
        if ($typeIds.Count -ne 99) {
            throw "actual typeId coverage=$($typeIds.Count)"
        }

        $signatures = @((Get-BondsAuditRequiredProperty `
            -InputObject $document `
            -Name 'signatures' `
            -Context 'document').Value)
        if ($signatures.Count -lt 1) {
            throw 'document signatures must not be empty'
        }
        foreach ($signature in $signatures) {
            Get-BondsAuditRequiredString `
                -InputObject $signature `
                -Name 'exactNameSignature' `
                -Context 'signature' | Out-Null
            $signatureUnitKeys = @((Get-BondsAuditRequiredProperty `
                -InputObject $signature `
                -Name 'unitKeys' `
                -Context 'signature').Value)
            if ($signatureUnitKeys.Count -lt 1) {
                throw 'signature unitKeys must not be empty'
            }
        }

        $tokenSummary = @((Get-BondsAuditRequiredProperty `
            -InputObject $document `
            -Name 'tokenSummary' `
            -Context 'document').Value)
        if ($tokenSummary.Count -lt 1) {
            throw 'document tokenSummary must not be empty'
        }
        foreach ($token in $tokenSummary) {
            Get-BondsAuditRequiredString `
                -InputObject $token `
                -Name 'token' `
                -Context 'tokenSummary entry' | Out-Null
            Get-BondsAuditRequiredInteger `
                -InputObject $token `
                -Name 'variantCount' `
                -Context 'tokenSummary entry' | Out-Null
            Get-BondsAuditRequiredInteger `
                -InputObject $token `
                -Name 'animationCount' `
                -Context 'tokenSummary entry' | Out-Null
        }

        $diagnostics = @((Get-BondsAuditRequiredProperty `
            -InputObject $document `
            -Name 'diagnostics' `
            -Context 'document').Value)
        if (@($diagnostics | Where-Object {
                $_ -isnot [string] -or [string]::IsNullOrWhiteSpace($_)
            }).Count -gt 0) {
            throw 'document diagnostics contains an invalid entry'
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

function Assert-BondsAuditPathHasNoReparsePoint {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Boundary,
        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $fullBoundary = [System.IO.Path]::GetFullPath($Boundary).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $boundaryPrefix = $fullBoundary + [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith(
            $boundaryPrefix,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "BONDS_ANIMATION_AUDIT_REPARSE_POINT $Label escaped boundary: $fullPath"
    }

    $currentPath = $fullPath
    while (-not [string]::IsNullOrEmpty($currentPath)) {
        $insideBoundary = $currentPath.StartsWith(
            $boundaryPrefix,
            [System.StringComparison]::OrdinalIgnoreCase)
        $atBoundary = $currentPath.Equals(
            $fullBoundary,
            [System.StringComparison]::OrdinalIgnoreCase)
        if (-not $insideBoundary -and -not $atBoundary) {
            break
        }

        if ([System.IO.File]::Exists($currentPath) `
            -or [System.IO.Directory]::Exists($currentPath)) {
            $attributes = [System.IO.File]::GetAttributes($currentPath)
            if (($attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "BONDS_ANIMATION_AUDIT_REPARSE_POINT $Label path=$currentPath"
            }
        }
        if ($atBoundary) {
            return
        }
        $currentPath = [System.IO.Path]::GetDirectoryName($currentPath)
    }

    throw "BONDS_ANIMATION_AUDIT_REPARSE_POINT $Label cannot reach boundary from $fullPath"
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
    Assert-BondsAuditPathHasNoReparsePoint `
        -Path $outputFullPath `
        -Boundary $tempRoot `
        -Label 'OutputPath'
    Assert-BondsAuditPathHasNoReparsePoint `
        -Path $logFullPath `
        -Boundary $tempRoot `
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
    $handoffProcesses = @{}
    do {
        if (($null -eq $validatedOutputSnapshot) `
            -and [System.IO.File]::Exists($outputFullPath)) {
            try {
                $validatedOutputSnapshot =
                    Get-BondsUnitAnimationAuditSnapshot -Path $outputFullPath
            } catch {
                # A handoff process may still be completing the JSON write.
            }
        }
        $remainingProjectProcesses = @(
            Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" |
                Where-Object {
                    -not [string]::IsNullOrEmpty($_.CommandLine) `
                        -and $_.CommandLine.IndexOf(
                            $projectFullPath,
                            [System.StringComparison]::OrdinalIgnoreCase) -ge 0
                }
        )
        foreach ($remainingProcess in $remainingProjectProcesses) {
            if (($remainingProcess.ProcessId -eq $unityProcess.Id) `
                -or $handoffProcesses.ContainsKey($remainingProcess.ProcessId)) {
                continue
            }
            try {
                $handoffProcesses[$remainingProcess.ProcessId] =
                    Get-Process -Id $remainingProcess.ProcessId -ErrorAction Stop
            } catch {
                # The process may exit between the WMI query and handle capture.
            }
        }
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
    foreach ($entry in $handoffProcesses.GetEnumerator()) {
        $handoffExitCode = $null
        try {
            if (-not $entry.Value.HasExited) {
                $entry.Value.WaitForExit()
            }
            $handoffExitCode = $entry.Value.ExitCode
        } catch [System.InvalidOperationException] {
            # A process that vanished before its handle opened has no exit code.
        } finally {
            $entry.Value.Dispose()
        }
        if ($null -ne $handoffExitCode -and $handoffExitCode -ne 0) {
            throw "Unity handoff process $($entry.Key) exited with code $handoffExitCode. Log: $logFullPath"
        }
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
    Assert-BondsAuditPathHasNoReparsePoint `
        -Path $outputFullPath `
        -Boundary $tempRoot `
        -Label 'OutputPath'
    Assert-BondsAuditPathHasNoReparsePoint `
        -Path $logFullPath `
        -Boundary $tempRoot `
        -Label 'LogPath'
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
