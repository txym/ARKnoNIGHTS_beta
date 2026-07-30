using System;
using System.IO;
using System.Linq;
using ArknoNights.Battle.Demo;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>Reproducible desktop and Android build entry points for acceptance packages.</summary>
public static class Task006StandaloneBuild
{
    private const string OutputEnvironmentVariable = "ARKNIGHTS_BUILD_OUTPUT";
    private const string AndroidOutputEnvironmentVariable =
        "ARKNIGHTS_ANDROID_APK_OUTPUT";
    private const string DefaultOutputPath = "Temp/TASK-006/WindowsStandalone/ARKnoNIGHTS.exe";
    private const string DefaultAndroidOutputPath =
        "Artifacts/Release/Android/ARKnoNIGHTS.apk";
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

    public static void BuildAndroidApk()
    {
        try
        {
            var scenes = EditorBuildSettings.scenes
                .Where(scene =>
                    scene.enabled
                    && !string.IsNullOrWhiteSpace(scene.path))
                .Select(scene => scene.path)
                .ToArray();
            if (scenes.Length == 0)
                throw new InvalidOperationException(
                    "No enabled scenes are configured in Build Settings.");

            var configured = Environment.GetEnvironmentVariable(
                AndroidOutputEnvironmentVariable);
            var outputPath = Path.GetFullPath(
                string.IsNullOrWhiteSpace(configured)
                    ? DefaultAndroidOutputPath
                    : configured);
            Directory.CreateDirectory(
                Path.GetDirectoryName(outputPath));
            EditorUserBuildSettings.buildAppBundle = false;
            var report = BuildPipeline.BuildPlayer(
                scenes,
                outputPath,
                BuildTarget.Android,
                BuildOptions.StrictMode);
            var summary = report.summary;
            var message = "result=" + summary.result
                + "; platform=" + summary.platform
                + "; output=" + summary.outputPath
                + "; totalSize=" + summary.totalSize
                + "; totalTime=" + summary.totalTime
                + "; errors=" + summary.totalErrors
                + "; warnings=" + summary.totalWarnings;
            File.WriteAllText(
                Path.Combine(
                    Path.GetDirectoryName(outputPath),
                    "android-build-summary.txt"),
                message + Environment.NewLine);

            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError(
                    "[ANDROID-APK][build.failed] " + message);
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log(
                "[ANDROID-APK][build.succeeded] " + message);
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "[ANDROID-APK][build.exception] type="
                + exception.GetType().Name
                + "; message="
                + exception.Message);
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
