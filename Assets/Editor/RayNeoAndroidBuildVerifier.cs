using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

internal static class RayNeoAndroidBuildVerifier
{
    private const string ScenePath = "Assets/Scenes/ESP32_Test.unity";
    private const string OutputPath = "Builds/ACL_RayNeo_X3_Spatial_Verification.apk";

    [MenuItem("Tools/ACL Rehab/Build RayNeo X3 Verification APK _F8")]
    private static void BuildVerificationApk()
    {
        Directory.CreateDirectory("Builds");

        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = OutputPath,
            target = BuildTarget.Android,
            options = BuildOptions.Development
        });

        BuildSummary summary = report.summary;
        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"RayNeo Android verification build succeeded: {OutputPath} " +
                      $"({summary.totalSize} bytes)");
        }
        else
        {
            Debug.LogError($"RayNeo Android verification build failed: {summary.result}, " +
                           $"errors={summary.totalErrors}, warnings={summary.totalWarnings}");
        }
    }
}
