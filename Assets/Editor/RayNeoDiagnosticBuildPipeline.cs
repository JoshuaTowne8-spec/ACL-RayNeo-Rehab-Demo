using System;
using System.IO;
using System.Linq;
using Unity.XR.RayNeo.OpenXR.ARDK;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.PackageManager.UI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.OpenXR;

/// <summary>
/// Creates and builds three isolated RayNeo APKs for diagnosing native
/// OpenXR startup failures. None of the generated diagnostic scenes replace
/// ESP32_Test or RunningPrototype_Debug.
/// </summary>
internal static class RayNeoDiagnosticBuildPipeline
{
    private const string RayNeoPackageName = "com.unity.xr.rayneo.openxr";
    private const string RayNeoPackageVersion = "1.1.2";
    private const string RayNeoSampleName = "Hello RayNeo";
    private const string RayNeoRigPath =
        "Packages/com.unity.xr.rayneo.openxr/SDK/Runtime/Resources/Prefab/XR Plugin.prefab";

    private const string DiagnosticFolder = "Assets/Scenes/RayNeoDiagnostics";
    private const string ThreeDofScenePath =
        DiagnosticFolder + "/RayNeo_Diagnostic_3DOF.unity";
    private const string Esp32ScenePath = "Assets/Scenes/ESP32_Test.unity";
    private const string OutputFolder = "Builds/RayNeoDiagnostics";

    [MenuItem("Tools/ACL Rehab/RayNeo Diagnostics/1 - Prepare Diagnostic Scenes")]
    public static void PrepareDiagnosticScenes()
    {
        Directory.CreateDirectory(DiagnosticFolder);
        CreateThreeDofScene();
        ImportOfficialRayNeoSample();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Debug.Log(
            "RayNeo diagnostic preparation complete. If Unity recompiles the imported " +
            "sample scripts, wait for compilation to finish before building the APKs.");
    }

    [MenuItem("Tools/ACL Rehab/RayNeo Diagnostics/2 - Build All Diagnostic APKs")]
    public static void BuildAllDiagnosticApks()
    {
        ConfigureCommonAndroidSettings();
        string slamScenePath = FindOfficialPlaneDetectionScene();

        BuildOne(
            ThreeDofScenePath,
            OutputFolder + "/RayNeo_Diagnostic_3DOF.apk",
            "com.aclrehab.rayneo.diagnostic3dof",
            "RayNeo Diagnostic 3DOF",
            StartupCameraAttitudeType.DOF3);

        BuildOne(
            slamScenePath,
            OutputFolder + "/RayNeo_Diagnostic_SLAM.apk",
            "com.aclrehab.rayneo.diagnosticslam",
            "RayNeo Diagnostic SLAM",
            StartupCameraAttitudeType.SLAM);

        BuildOne(
            Esp32ScenePath,
            OutputFolder + "/ESP32_Test_OpenXR_1.7.0.apk",
            "com.aclrehab.rayneo.demo",
            "ACL RayNeo Rehab Demo",
            StartupCameraAttitudeType.SLAM);

        // Keep the working project in the intended full-application state.
        ConfigureRayNeoMode(StartupCameraAttitudeType.SLAM);
        PlayerSettings.SetApplicationIdentifier(
            BuildTargetGroup.Android,
            "com.aclrehab.rayneo.demo");
        PlayerSettings.productName = "ACL RayNeo Rehab Demo";
        AssetDatabase.SaveAssets();
        Debug.Log("All RayNeo diagnostic APKs built successfully in " + OutputFolder + ".");
    }

    [MenuItem("Tools/ACL Rehab/RayNeo Diagnostics/Build 3DOF APK Only")]
    public static void BuildThreeDofApk()
    {
        ConfigureCommonAndroidSettings();
        BuildOne(
            ThreeDofScenePath,
            OutputFolder + "/RayNeo_Diagnostic_3DOF.apk",
            "com.aclrehab.rayneo.diagnostic3dof",
            "RayNeo Diagnostic 3DOF",
            StartupCameraAttitudeType.DOF3);
    }

    [MenuItem("Tools/ACL Rehab/RayNeo Diagnostics/Build SLAM APK Only")]
    public static void BuildSlamApk()
    {
        ConfigureCommonAndroidSettings();
        BuildOne(
            FindOfficialPlaneDetectionScene(),
            OutputFolder + "/RayNeo_Diagnostic_SLAM.apk",
            "com.aclrehab.rayneo.diagnosticslam",
            "RayNeo Diagnostic SLAM",
            StartupCameraAttitudeType.SLAM);
    }

    [MenuItem("Tools/ACL Rehab/RayNeo Diagnostics/Build Corrected ESP32 Test APK Only")]
    public static void BuildCorrectedEsp32Apk()
    {
        ConfigureCommonAndroidSettings();
        BuildOne(
            Esp32ScenePath,
            OutputFolder + "/ESP32_Test_OpenXR_1.7.0.apk",
            "com.aclrehab.rayneo.demo",
            "ACL RayNeo Rehab Demo",
            StartupCameraAttitudeType.SLAM);
    }

    private static void CreateThreeDofScene()
    {
        GameObject rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RayNeoRigPath);
        if (rigPrefab == null)
        {
            throw new InvalidOperationException(
                "RayNeo XR Plugin prefab was not found: " + RayNeoRigPath);
        }

        Scene scene = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene,
            NewSceneMode.Single);

        GameObject rig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab, scene);
        rig.name = "XR Plugin";
        rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        // The official prefab uses a legacy StandaloneInputModule. This diagnostic
        // does not need UI and the project uses Input System-only mode.
        Transform eventSystem = rig.transform.Find("EventSystem");
        if (eventSystem != null)
        {
            eventSystem.gameObject.SetActive(false);
        }

        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        marker.name = "3DOF Render Marker";
        marker.transform.SetPositionAndRotation(
            new Vector3(0f, 0f, 2f),
            Quaternion.identity);
        marker.transform.localScale = Vector3.one * 0.25f;
        SceneManager.MoveGameObjectToScene(marker, scene);

        GameObject lightObject = new GameObject("Directional Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1f;
        lightObject.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
        SceneManager.MoveGameObjectToScene(lightObject, scene);

        EditorSceneManager.SaveScene(scene, ThreeDofScenePath);
    }

    private static void ImportOfficialRayNeoSample()
    {
        Sample sample = Sample
            .FindByPackage(RayNeoPackageName, RayNeoPackageVersion)
            .FirstOrDefault(candidate => candidate.displayName == RayNeoSampleName);

        if (string.IsNullOrEmpty(sample.displayName))
        {
            throw new InvalidOperationException(
                "RayNeo ARDK 1.1.2 Hello RayNeo sample was not found in the package.");
        }

        if (!sample.isImported &&
            !sample.Import(Sample.ImportOptions.HideImportWindow))
        {
            throw new InvalidOperationException(
                "Unity could not import the official Hello RayNeo sample.");
        }
    }

    private static string FindOfficialPlaneDetectionScene()
    {
        string[] sceneGuids = AssetDatabase.FindAssets(
            "PlaneDetection t:Scene",
            new[] { "Assets/Samples" });

        string scenePath = sceneGuids
            .Select(AssetDatabase.GUIDToAssetPath)
            .FirstOrDefault(path =>
                path.Replace('\\', '/').EndsWith(
                    "/Hello RayNeo/Scenes/Algorithm/PlaneDetection.unity",
                    StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrEmpty(scenePath))
        {
            throw new InvalidOperationException(
                "The official RayNeo PlaneDetection scene is not imported. Run " +
                "Tools > ACL Rehab > RayNeo Diagnostics > 1 - Prepare Diagnostic Scenes first.");
        }

        return scenePath;
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
        PlayerSettings.Android.forceInternetPermission = true;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(
            BuildTarget.Android,
            new[] { GraphicsDeviceType.OpenGLES3 });
    }

    private static void ConfigureRayNeoMode(StartupCameraAttitudeType mode)
    {
        OpenXRSettings settings =
            OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
        if (settings == null)
        {
            throw new InvalidOperationException("Android OpenXR settings were not found.");
        }

        RayNeoSupportFeature support = settings.GetFeature<RayNeoSupportFeature>();
        if (support == null)
        {
            throw new InvalidOperationException("RayNeo Support OpenXR feature was not found.");
        }

        support.enabled = true;
        support.CameraAttitudeType = mode;
        support.OpenSLAMOnStart = false;
        EditorUtility.SetDirty(support);
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
    }

    private static void BuildOne(
        string scenePath,
        string outputPath,
        string applicationIdentifier,
        string productName,
        StartupCameraAttitudeType mode)
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
        {
            throw new InvalidOperationException("Build scene does not exist: " + scenePath);
        }

        Directory.CreateDirectory(OutputFolder);
        string previousIdentifier = PlayerSettings.GetApplicationIdentifier(
            BuildTargetGroup.Android);
        string previousProductName = PlayerSettings.productName;
        RayNeoSupportFeature support = GetRayNeoSupportFeature();
        StartupCameraAttitudeType previousMode = support.CameraAttitudeType;
        bool previousOpenSlamOnStart = support.OpenSLAMOnStart;

        try
        {
            ConfigureRayNeoMode(mode);
            PlayerSettings.SetApplicationIdentifier(
                BuildTargetGroup.Android,
                applicationIdentifier);
            PlayerSettings.productName = productName;
            AssetDatabase.SaveAssets();

            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { scenePath },
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = BuildOptions.Development | BuildOptions.AllowDebugging
            });

            BuildSummary summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"RayNeo diagnostic build failed: {outputPath}; result={summary.result}; " +
                    $"errors={summary.totalErrors}; warnings={summary.totalWarnings}");
            }

            Debug.Log(
                $"RayNeo diagnostic build succeeded: {outputPath} " +
                $"({summary.totalSize} bytes, mode={mode}, package={applicationIdentifier})");
        }
        finally
        {
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

    private static RayNeoSupportFeature GetRayNeoSupportFeature()
    {
        OpenXRSettings settings =
            OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
        RayNeoSupportFeature support = settings != null
            ? settings.GetFeature<RayNeoSupportFeature>()
            : null;

        if (support == null)
        {
            throw new InvalidOperationException("RayNeo Support OpenXR feature was not found.");
        }

        return support;
    }
}
