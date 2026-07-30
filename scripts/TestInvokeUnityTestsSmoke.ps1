[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = Split-Path -Parent $PSScriptRoot
$fixtureRoot = Join-Path $projectRoot ('Temp/InvokeUnityTestsSmoke/' + [Guid]::NewGuid().ToString('N'))
$fakeProject = Join-Path $fixtureRoot 'Project'
$fakeUnity = Join-Path $fixtureRoot 'FakeUnity.exe'
$wrapper = Join-Path $PSScriptRoot 'Invoke-UnityTests.ps1'

function Write-TestRunFixture
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Body
    )

    [IO.File]::WriteAllText($Path, $Body, [Text.UTF8Encoding]::new($false))
}

function Invoke-Fixture
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Xml,

        [Parameter(Mandatory = $true)]
        [int]$ExpectedExitCode,

        [switch]$StagedWrites
    )

    $fixturePath = Join-Path $fixtureRoot ($Name + '.xml')
    $outputPath = Join-Path $fixtureRoot ('Output-' + $Name)
    Write-TestRunFixture -Path $fixturePath -Body $Xml
    $env:ARKNIGHTS_UNITY_TEST_XML_FIXTURE = $fixturePath
    $env:ARKNIGHTS_UNITY_TEST_STAGED_WRITES =
        if ($StagedWrites) { '1' } else { '0' }

    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $wrapper `
        -UnityPath $fakeUnity `
        -ProjectPath $fakeProject `
        -TestPlatform EditMode `
        -OutputDirectory $outputPath `
        -TimeoutSeconds 10 `
        -GracefulShutdownSeconds 0
    $actualExitCode = $LASTEXITCODE
    if ($actualExitCode -ne $ExpectedExitCode)
    {
        throw "$Name expected exit code $ExpectedExitCode but received $actualExitCode."
    }
}

try
{
    New-Item -ItemType Directory -Path (Join-Path $fakeProject 'Assets') -Force | Out-Null
    $fakeUnitySource = @'
using System;
using System.IO;
using System.Text;
using System.Threading;

public static class FakeUnity
{
    public static int Main(string[] args)
    {
        string resultsPath = null;
        for (var index = 0; index + 1 < args.Length; index++)
        {
            if (string.Equals(args[index], "-testResults", StringComparison.Ordinal))
            {
                resultsPath = args[index + 1].Trim('"');
                break;
            }
        }

        var fixturePath = Environment.GetEnvironmentVariable("ARKNIGHTS_UNITY_TEST_XML_FIXTURE");
        if (string.IsNullOrWhiteSpace(resultsPath) || string.IsNullOrWhiteSpace(fixturePath))
        {
            return 2;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(resultsPath));
        if (string.Equals(
                Environment.GetEnvironmentVariable("ARKNIGHTS_UNITY_TEST_STAGED_WRITES"),
                "1",
                StringComparison.Ordinal))
        {
            File.WriteAllText(resultsPath, string.Empty, new UTF8Encoding(false));
            Thread.Sleep(600);
            File.WriteAllText(resultsPath, "<test-run", new UTF8Encoding(false));
            Thread.Sleep(600);
            File.WriteAllText(
                resultsPath,
                "<test-run result=\"Passed\"></test-run>",
                new UTF8Encoding(false));
            Thread.Sleep(600);
        }
        File.WriteAllText(resultsPath, File.ReadAllText(fixturePath, Encoding.UTF8), new UTF8Encoding(false));
        return 0;
    }
}
'@
    Add-Type -TypeDefinition $fakeUnitySource -OutputAssembly $fakeUnity -OutputType ConsoleApplication

    Invoke-Fixture -Name 'all-passed' -ExpectedExitCode 0 -Xml @'
<?xml version="1.0" encoding="utf-8"?>
<test-run result="Passed" testcasecount="2" total="2" passed="2" failed="0" inconclusive="0" skipped="0" asserts="2">
  <test-suite runstate="Runnable" result="Passed" total="2" passed="2" failed="0" inconclusive="0" skipped="0" />
</test-run>
'@
    Invoke-Fixture -Name 'skipped' -ExpectedExitCode 1 -Xml @'
<?xml version="1.0" encoding="utf-8"?>
<test-run result="Passed" testcasecount="2" total="2" passed="1" failed="0" inconclusive="0" skipped="1" asserts="1">
  <test-suite runstate="Runnable" result="Passed" total="2" passed="1" failed="0" inconclusive="0" skipped="1" />
</test-run>
'@
    Invoke-Fixture -Name 'inconclusive' -ExpectedExitCode 1 -Xml @'
<?xml version="1.0" encoding="utf-8"?>
<test-run result="Passed" testcasecount="2" total="2" passed="1" failed="0" inconclusive="1" skipped="0" asserts="1">
  <test-suite runstate="Runnable" result="Passed" total="2" passed="1" failed="0" inconclusive="1" skipped="0" />
</test-run>
'@
    Invoke-Fixture -Name 'not-runnable' -ExpectedExitCode 1 -Xml @'
<?xml version="1.0" encoding="utf-8"?>
<test-run result="Passed" testcasecount="1" total="1" passed="1" failed="0" inconclusive="0" skipped="0" asserts="1">
  <test-suite runstate="Runnable" result="Passed" total="1" passed="1" failed="0" inconclusive="0" skipped="0">
    <test-case runstate="NotRunnable" result="Passed" />
  </test-suite>
</test-run>
'@
    Invoke-Fixture -Name 'zero-tests' -ExpectedExitCode 1 -Xml @'
<?xml version="1.0" encoding="utf-8"?>
<test-run result="Passed" testcasecount="0" total="0" passed="0" failed="0" inconclusive="0" skipped="0" asserts="0" />
'@

    Invoke-Fixture -Name 'staged-write' -ExpectedExitCode 0 -StagedWrites -Xml @'
<?xml version="1.0" encoding="utf-8"?>
<test-run result="Passed" testcasecount="1" total="1" passed="1" failed="0" inconclusive="0" skipped="0" asserts="1">
  <test-suite runstate="Runnable" result="Passed" total="1" passed="1" failed="0" inconclusive="0" skipped="0" />
</test-run>
'@

    Write-Host 'Invoke-UnityTests smoke: PASS (6 fixtures)'
}
finally
{
    Remove-Item Env:ARKNIGHTS_UNITY_TEST_XML_FIXTURE -ErrorAction SilentlyContinue
    Remove-Item Env:ARKNIGHTS_UNITY_TEST_STAGED_WRITES -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $fixtureRoot)
    {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}
