[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$UnityPath,

    [ValidateSet('EditMode', 'PlayMode')]
    [string]$TestPlatform = 'EditMode',

    [string]$TestFilter,

    [string]$ProjectPath,

    [string]$OutputDirectory,

    [ValidateRange(1, 3600)]
    [int]$TimeoutSeconds = 900,

    [ValidateRange(0, 120)]
    [int]$GracefulShutdownSeconds = 20,

    [switch]$NoGraphics
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($ProjectPath))
{
    $ProjectPath = Split-Path -Parent $PSScriptRoot
}

function Quote-Argument([string]$Value)
{
    if ($Value -notmatch '[\s"]') { return $Value }
    return '"' + ($Value -replace '(\\*)"', '$1$1\"' -replace '(\\*)$', '$1$1') + '"'
}

function Write-Summary([string]$Message)
{
    $Message | Set-Content -LiteralPath $summaryPath -Encoding utf8
    Write-Host $Message
}

$unityFullPath = [IO.Path]::GetFullPath($UnityPath)
$projectFullPath = [IO.Path]::GetFullPath($ProjectPath)
if (-not (Test-Path -LiteralPath $unityFullPath -PathType Leaf)) { throw "Unity.exe was not found: $unityFullPath" }
if (-not (Test-Path -LiteralPath (Join-Path $projectFullPath 'Assets') -PathType Container)) { throw "ProjectPath is not a Unity project: $projectFullPath" }

$projectPattern = [regex]::Escape($projectFullPath)
$existingProjectProcesses = Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" |
    Where-Object { $_.CommandLine -match $projectPattern }
if ($existingProjectProcesses)
{
    $ids = ($existingProjectProcesses | ForEach-Object ProcessId) -join ', '
    throw "Unity is already using this project (PID: $ids). Close it before starting a test run."
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory))
{
    $OutputDirectory = Join-Path $projectFullPath ('Temp/UnityTests/' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
}
$outputFullPath = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($outputFullPath) | Out-Null

$resultsPath = Join-Path $outputFullPath ($TestPlatform + 'Results.xml')
$logPath = Join-Path $outputFullPath ($TestPlatform + '.log')
$summaryPath = Join-Path $outputFullPath 'summary.txt'
$arguments = @('-batchmode', '-accept-apiupdate', '-projectPath', (Quote-Argument $projectFullPath), '-runTests', '-testPlatform', $TestPlatform, '-testResults', (Quote-Argument $resultsPath), '-logFile', (Quote-Argument $logPath))
if ($NoGraphics) { $arguments += '-nographics' }
if (-not [string]::IsNullOrWhiteSpace($TestFilter)) { $arguments += @('-testFilter', (Quote-Argument $TestFilter)) }

$unityProcess = $null
$resultParsed = $false
try
{
    $unityProcess = Start-Process -FilePath $unityFullPath -ArgumentList ($arguments -join ' ') -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline)
    {
        if (Test-Path -LiteralPath $resultsPath -PathType Leaf)
        {
            try
            {
                [xml]$resultXml = Get-Content -LiteralPath $resultsPath -Raw -Encoding utf8
                $testRun = $resultXml.'test-run'
                $total = [int]$testRun.total
                $failed = [int]$testRun.failed
                $skipped = [int]$testRun.skipped
                $inconclusive = [int]$testRun.inconclusive
                $notRun = [int]$testRun.GetAttribute('not-run')
                $notRunnable = @($resultXml.SelectNodes('//*[@runstate="NotRunnable" or @runstate="Not-Runnable" or @result="NotRunnable" or @result="Not-Runnable"]')).Count
                if ($total -le 0) { throw 'The test result XML reported zero tests.' }

                $resultParsed = $true
                if (-not $unityProcess.HasExited)
                {
                    if (-not $unityProcess.WaitForExit($GracefulShutdownSeconds * 1000))
                    {
                        Stop-Process -Id $unityProcess.Id -Force
                        $unityProcess.WaitForExit()
                        $shutdown = "forced-stop-after-results; graceSeconds=$GracefulShutdownSeconds"
                    }
                    else { $shutdown = 'normal-exit-after-results' }
                }
                else { $shutdown = "already-exited; exitCode=$($unityProcess.ExitCode)" }

                $summary = "result=$($testRun.result); total=$total; failed=$failed; skipped=$skipped; inconclusive=$inconclusive; notRun=$notRun; notRunnable=$notRunnable; shutdown=$shutdown; results=$resultsPath; log=$logPath"
                Write-Summary $summary
                if ($failed -gt 0 -or $skipped -gt 0 -or $inconclusive -gt 0 -or $notRun -gt 0 -or $notRunnable -gt 0 -or $testRun.result -ne 'Passed') { exit 1 }
                exit 0
            }
            catch [System.Xml.XmlException]
            {
                # Unity may expose the file just before its final write completes.
            }
        }

        if ($unityProcess.HasExited)
        {
            throw "Unity exited with code $($unityProcess.ExitCode) before producing a valid test result XML. Log: $logPath"
        }
        Start-Sleep -Milliseconds 500
    }
    throw "Unity test run exceeded $TimeoutSeconds seconds without a valid result XML. Log: $logPath"
}
finally
{
    if ($unityProcess -and -not $unityProcess.HasExited)
    {
        Stop-Process -Id $unityProcess.Id -Force
        $unityProcess.WaitForExit()
        if (-not $resultParsed) { Write-Summary "result=unverified; shutdown=forced-stop-before-valid-results; results=$resultsPath; log=$logPath" }
    }
}
