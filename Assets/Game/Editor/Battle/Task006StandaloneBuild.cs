using System;
using System.IO;
using System.Linq;
using ArknoNights.Battle.Demo;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>Reproducible Windows x86_64 build entry point for TASK-006 acceptance.</summary>
public static class Task006StandaloneBuild
{
    private const string OutputEnvironmentVariable = "ARKNIGHTS_BUILD_OUTPUT";
    private const string DefaultOutputPath = "Temp/TASK-006/WindowsStandalone/ARKnoNIGHTS.exe";
    private const string SummaryPath = "Temp/TASK-006/windows-standalone-build-summary.txt";

    public static void BuildWindowsX64()
    {
        try
        {
            var scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled && !string.IsNullOrWhiteSpace(scene.path))
                .Select(scene => scene.path)
                .ToArray();
            if (scenes.Length == 0) throw new InvalidOperationException("No enabled scenes are configured in Build Settings.");

            var outputPath = ResolveOutputPath();
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            var report = BuildPipeline.BuildPlayer(scenes, outputPath, BuildTarget.StandaloneWindows64, BuildOptions.StrictMode);
            var summary = report.summary;
            var message = "result=" + summary.result + "; platform=" + summary.platform + "; output=" + summary.outputPath + "; totalSize=" + summary.totalSize + "; totalTime=" + summary.totalTime + "; errors=" + summary.totalErrors + "; warnings=" + summary.totalWarnings;
            WriteSummary(message);

            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError("[TASK-006][build.failed] " + message);
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log("[TASK-006][build.succeeded] " + message);
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            var message = "result=Exception; type=" + exception.GetType().Name + "; message=" + exception.Message;
            WriteSummary(message);
            Debug.LogError("[TASK-006][build.exception] " + message);
            EditorApplication.Exit(1);
        }
    }

    public static void RunEditorAcceptance()
    {
        if (!Task006AcceptanceSummary.TryCaptureOne(out var summary, out var failure) || !Task006AcceptanceSummary.VerifyTenRuns(summary, out failure))
        {
            Debug.LogError("[TASK-006][editor.acceptance] status=failed; detail=" + failure);
            EditorApplication.Exit(1);
            return;
        }

        Debug.Log("[TASK-006][editor.acceptance] status=passed; iterations=10; " + summary);
        EditorApplication.Exit(0);
    }

    private static string ResolveOutputPath()
    {
        var configured = Environment.GetEnvironmentVariable(OutputEnvironmentVariable);
        return Path.GetFullPath(string.IsNullOrWhiteSpace(configured) ? DefaultOutputPath : configured);
    }

    private static void WriteSummary(string message)
    {
        var path = Path.GetFullPath(SummaryPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, message + Environment.NewLine);
    }
}
