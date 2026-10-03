using System;
using System.IO;
using ACLRehab.RayNeoDiagnostics;
using RayNeo;
using Unity.XR.RayNeo.OpenXR.ARDK;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.OpenXR;

internal static class RayNeoSpatialDiagnosticBuilder
{
    private const string RigPath =
        "Packages/com.unity.xr.rayneo.openxr/SDK/Runtime/Resources/Prefab/XR Plugin.prefab";
    private const string SceneFolder = "Assets/Scenes/RayNeoDiagnostics";
    private const string ScenePath =
        SceneFolder + "/RayNeo_SpatialPose_Diagnostic.unity";
    private const string OutputFolder = "Builds/RayNeoDiagnostics";
    private const string OutputPath =
        OutputFolder + "/RayNeo_SpatialPose_Diagnostic.apk";
    private const string PackageName =
        "com.aclrehab.rayneo.spatialdiagnostic";

    [MenuItem("Tools/ACL Rehab/RayNeo Diagnostics/3 - Prepare Spatial Pose Diagnostic")]
    public static void PrepareSpatialDiagnosticScene()
    {
        Directory.CreateDirectory(SceneFolder);
        GameObject rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPath);
        if (rigPrefab == null)
        {
            throw new InvalidOperationException(
                "RayNeo XR Plugin prefab was not found: " + RigPath);
        }

        Scene scene = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene,
            NewSceneMode.Single);
        GameObject rig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab, scene);
        rig.name = "XR Plugin - OFFICIAL UNMODIFIED POSE";
        rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        Transform eventSystem = rig.transform.Find("EventSystem");
        if (eventSystem != null)
        {
            eventSystem.gameObject.SetActive(false);
        }

        HeadTrackedPoseDriver driver =
            rig.GetComponentInChildren<HeadTrackedPoseDriver>(true);
        if (driver == null)
        {
            throw new InvalidOperationException(
                "The official XR Plugin prefab does not contain HeadTrackedPoseDriver.");
        }

        Transform xrOrigin = driver.m_Origin != null
            ? driver.m_Origin.transform
            : rig.transform;

        GameObject diagnosticSystem = new GameObject(
            "RayNeo Spatial Pose Diagnostic - NO GAMEPLAY");
        SceneManager.MoveGameObjectToScene(diagnosticSystem, scene);
        RayNeoPlaneCoordinateProbe planes =
            diagnosticSystem.AddComponent<RayNeoPlaneCoordinateProbe>();
        RayNeoSpatialPoseProbe pose =
            diagnosticSystem.AddComponent<RayNeoSpatialPoseProbe>();
        RayNeoDiagnosticRecorder recorder =
            diagnosticSystem.AddComponent<RayNeoDiagnosticRecorder>();
        RayNeoDiagnosticVisuals visuals =
            diagnosticSystem.AddComponent<RayNeoDiagnosticVisuals>();

        planes.Configure(driver.transform, xrOrigin);
        pose.Configure(driver.transform, xrOrigin, planes, recorder);
        recorder.Configure(pose, planes);
        visuals.Configure(pose, planes, recorder);

        EditorUtility.SetDirty(planes);
        EditorUtility.SetDirty(pose);
        EditorUtility.SetDirty(recorder);
        EditorUtility.SetDirty(visuals);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Debug.Log(
            "RayNeo spatial pose diagnostic scene prepared without changing " +
            "ESP32_Test or EditorBuildSettings: " + ScenePath);
    }

    [MenuItem("Tools/ACL Rehab/RayNeo Diagnostics/4 - Build Spatial Pose Diagnostic APK")]
    public static void BuildSpatialDiagnosticApk()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
        {
            PrepareSpatialDiagnosticScene();
        }

        ConfigureCommonAndroidSettings();
        Directory.CreateDirectory(OutputFolder);

        string previousIdentifier = PlayerSettings.GetApplicationIdentifier(
            BuildTargetGroup.Android);
        string previousProductName = PlayerSettings.productName;
        RayNeoSupportFeature support = GetRayNeoSupportFeature();
        bool previousFeatureEnabled = support.enabled;
        StartupCameraAttitudeType previousMode = support.CameraAttitudeType;
        bool previousOpenSlamOnStart = support.OpenSLAMOnStart;

        try
        {
            support.enabled = true;
            support.CameraAttitudeType = StartupCameraAttitudeType.SLAM;
            support.OpenSLAMOnStart = false;
            EditorUtility.SetDirty(support);
            PlayerSettings.SetApplicationIdentifier(
                BuildTargetGroup.Android,
                PackageName);
            PlayerSettings.productName = "RayNeo Spatial Pose Diagnostic";
            AssetDatabase.SaveAssets();

            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = OutputPath,
                target = BuildTarget.Android,
                options = BuildOptions.Development | BuildOptions.AllowDebugging
            });

            BuildSummary summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Spatial diagnostic build failed: result={summary.result}, " +
                    $"errors={summary.totalErrors}, warnings={summary.totalWarnings}");
            }

            Debug.Log(
                $"RayNeo spatial diagnostic build succeeded: {OutputPath}, " +
                $"bytes={summary.totalSize}, package={PackageName}, mode=SLAM/6DOF.");
        }
        finally
        {
            support.enabled = previousFeatureEnabled;
            support.CameraAttitudeType = previousMode;
            support.OpenSLAMOnStart = previousOpenSlamOnStart;
            EditorUtility.SetDirty(support);
            PlayerSettings.SetApplicationIdentifier(
                BuildTargetGroup.Android,
                previousIdentifier);
            PlayerSettings.productName = previousProductName;
            AssetDatabase.SaveAssets();
        }
    }

    public static void BuildSpatialDiagnosticFromCommandLine()
    {
        PrepareSpatialDiagnosticScene();
        BuildSpatialDiagnosticApk();
    }

    private static void ConfigureCommonAndroidSettings()
    {
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.SetScriptingBackend(
            BuildTargetGroup.Android,
            ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel30;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(
            BuildTarget.Android,
            new[] { GraphicsDeviceType.OpenGLES3 });
    }

    private static RayNeoSupportFeature GetRayNeoSupportFeature()
    {
        OpenXRSettings settings =
            OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
        RayNeoSupportFeature support = settings != null
            ? settings.GetFeature<RayNeoSupportFeature>()
            : null;
        if (support == null)
        {
            throw new InvalidOperationException(
                "RayNeo Support OpenXR feature was not found for Android.");
        }

        return support;
    }
}
