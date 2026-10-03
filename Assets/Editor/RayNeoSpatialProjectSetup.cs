using System;
using System.Linq;
using ACLRehab.RayNeoSpatial;
using ACLRehab.Running;
using Unity.XR.RayNeo.OpenXR.ARDK;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;

[InitializeOnLoad]
internal static class RayNeoSpatialProjectSetup
{
    private const string ScenePath = "Assets/Scenes/ESP32_Test.unity";
    private const string EchoStickerPrefabPath = "Assets/Prefabs/Running/EchoSticker.prefab";
    private const string TrackOriginPrefabPath = "Assets/Prefabs/TrackOrigin.prefab";
    private const string RouteSystemPrefabPath = "Assets/Prefabs/Running/RunningRouteSystem.prefab";
    private const string TrackMaterialPath = "Assets/Materials/RunningTrack_7333FF.mat";
    private const string GroundPreviewMaterialPath = "Assets/Materials/RayNeoGroundPreview.mat";
    private const string RayNeoRigPath =
        "Packages/com.unity.xr.rayneo.openxr/SDK/Runtime/Resources/Prefab/XR Plugin.prefab";
    private const string SetupObjectName = "RayNeo Spatial System";
    private const string SessionKey = "ACLRehab.RayNeoSpatialProjectSetup.v5";

    static RayNeoSpatialProjectSetup()
    {
        EditorApplication.delayCall += TryApplyAutomatically;
    }

    [MenuItem("Tools/ACL Rehab/Build ESP32 Test for RayNeo X3")]
    private static void ApplyFromMenu()
    {
        ApplySetup();
    }

    private static void TryApplyAutomatically()
    {
        if (SessionState.GetBool(SessionKey, false) ||
            EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode)
        {
            if (!SessionState.GetBool(SessionKey, false) &&
                (EditorApplication.isCompiling || EditorApplication.isUpdating))
            {
                EditorApplication.delayCall += TryApplyAutomatically;
            }

            return;
        }

        SessionState.SetBool(SessionKey, true);
        try
        {
            ApplySetup();
        }
        catch (Exception exception)
        {
            SessionState.SetBool(SessionKey, false);
            Debug.LogException(exception);
        }
    }

    private static void ApplySetup()
    {
        AssetDatabase.Refresh();
        ConfigureAndroidPlayer();
        ConfigureOpenXR();
        ConfigureSceneAndPrefabs();
        ConfigureBuildScenes();
        AssetDatabase.SaveAssets();
        Debug.Log("ESP32_Test RayNeo running spatial setup applied successfully.");
    }

    private static void ConfigureAndroidPlayer()
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

    private static void ConfigureOpenXR()
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
        support.CameraAttitudeType = StartupCameraAttitudeType.SLAM;
        support.OpenSLAMOnStart = false;
        EditorUtility.SetDirty(support);

        RayNeoControllerProfile controller = settings.GetFeature<RayNeoControllerProfile>();
        if (controller != null)
        {
            controller.enabled = true;
            EditorUtility.SetDirty(controller);
        }

        EditorUtility.SetDirty(settings);
    }

    private static void ConfigureSceneAndPrefabs()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
        {
            throw new InvalidOperationException("Target scene does not exist: " + ScenePath);
        }

        Material trackMaterial = AssetDatabase.LoadAssetAtPath<Material>(TrackMaterialPath);
        GameObject echoPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EchoStickerPrefabPath);
        if (trackMaterial == null || echoPrefab == null)
        {
            throw new InvalidOperationException(
                "Running prototype material or echo sticker prefab is missing.");
        }

        Material groundPreviewMaterial = EnsureGroundPreviewMaterial();
        GameObject routePrefab = CreateRunningRouteSystemPrefab(trackMaterial, echoPrefab);
        GameObject trackOriginPrefab = CreateTrackOriginPrefab();

        EditorSceneManager.SaveOpenScenes();
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        DestroyRoot(scene, SetupObjectName);
        DestroyRoot(scene, "SimulatedRayNeo");
        DestroyRoot(scene, "EchoTarget");
        DestroyRoot(scene, "Ground");
        DestroyRoot(scene, "TestEnvironment");

        GameObject rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RayNeoRigPath);
        if (rigPrefab == null)
        {
            throw new InvalidOperationException("RayNeo XR Plugin prefab was not found: " + RayNeoRigPath);
        }

        GameObject rig = FindRoot(scene, "XR Plugin");
        if (rig == null)
        {
            rig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab, scene);
            rig.name = "XR Plugin";
        }

        rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        rig.transform.localScale = Vector3.one;
        Transform head = rig.transform.Find("CameraOffset/Head");
        if (head == null)
        {
            throw new InvalidOperationException(
                "Head transform was not found in the RayNeo XR Plugin prefab.");
        }

        // The official prefab's EventSystem uses the legacy StandaloneInputModule.
        // This project reads the ring directly through RayNeoInput, so the legacy
        // UI module is not needed and would conflict with Input System-only mode.
        Transform legacyEventSystem = rig.transform.Find("EventSystem");
        if (legacyEventSystem != null)
        {
            legacyEventSystem.gameObject.SetActive(false);
        }

        Esp32UdpReceiver receiver = UnityEngine.Object.FindObjectOfType<Esp32UdpReceiver>();
        if (receiver == null)
        {
            GameObject sensorManager = new GameObject("SensorManager");
            SceneManager.MoveGameObjectToScene(sensorManager, scene);
            receiver = sensorManager.AddComponent<Esp32UdpReceiver>();
            receiver.listenPort = 4210;
            receiver.connectionTimeout = 0.3f;
        }

        foreach (SensorDebugOverlay overlay in
                 UnityEngine.Object.FindObjectsOfType<SensorDebugOverlay>(true))
        {
            overlay.enabled = false;
            EditorUtility.SetDirty(overlay);
        }

        foreach (Esp32DebugTransmitter transmitter in
                 UnityEngine.Object.FindObjectsOfType<Esp32DebugTransmitter>(true))
        {
            transmitter.enabled = false;
            EditorUtility.SetDirty(transmitter);
        }

        GameObject system = new GameObject(SetupObjectName);
        SceneManager.MoveGameObjectToScene(system, scene);

        Transform groundReference = new GameObject("Selected Ground Reference").transform;
        groundReference.SetParent(system.transform, false);

        GameObject trackOriginObject =
            (GameObject)PrefabUtility.InstantiatePrefab(trackOriginPrefab, scene);
        trackOriginObject.transform.SetParent(system.transform, false);
        trackOriginObject.name = "Track Origin";

        GameObject routeSystem =
            (GameObject)PrefabUtility.InstantiatePrefab(routePrefab, trackOriginObject.transform);
        routeSystem.name = "Running Route System";

        TrackRouteController routeController = routeSystem.GetComponent<TrackRouteController>();
        EchoStepSpawner echoSpawner = routeSystem.GetComponentInChildren<EchoStepSpawner>(true);
        EchoSequenceController sequence =
            routeSystem.GetComponentInChildren<EchoSequenceController>(true);
        routeController.routeOrigin = trackOriginObject.transform;
        routeController.trackedPlayer = head;
        routeController.initializeOnStart = false;
        echoSpawner.trackedHead = head;
        echoSpawner.receiver = receiver;
        sequence.receiver = receiver;
        routeSystem.SetActive(false);

        RayNeoSpatialBootstrap bootstrap = system.AddComponent<RayNeoSpatialBootstrap>();
        GroundPlaneSelector selector = system.AddComponent<GroundPlaneSelector>();
        selector.Configure(bootstrap, head, groundReference);

        TrackOriginCalibrator calibrator = system.AddComponent<TrackOriginCalibrator>();
        calibrator.Configure(selector, head, trackOriginObject.transform);

        GameObject previewObject = new GameObject("Selected Ground Preview");
        previewObject.transform.SetParent(system.transform, false);
        previewObject.AddComponent<MeshFilter>();
        MeshRenderer previewRenderer = previewObject.AddComponent<MeshRenderer>();
        previewRenderer.shadowCastingMode = ShadowCastingMode.Off;
        previewRenderer.receiveShadows = false;
        GroundPlanePreview preview = previewObject.AddComponent<GroundPlanePreview>();
        preview.Configure(selector, groundPreviewMaterial);

        RunningSpatialSessionController session =
            system.AddComponent<RunningSpatialSessionController>();
        session.Configure(
            bootstrap,
            selector,
            calibrator,
            preview,
            routeSystem,
            routeController);

        RayNeoCalibrationStatusView statusView =
            system.AddComponent<RayNeoCalibrationStatusView>();
        statusView.Configure(session, receiver);

        EditorUtility.SetDirty(receiver);
        EditorUtility.SetDirty(routeController);
        EditorUtility.SetDirty(echoSpawner);
        EditorUtility.SetDirty(sequence);
        EditorUtility.SetDirty(bootstrap);
        EditorUtility.SetDirty(selector);
        EditorUtility.SetDirty(calibrator);
        EditorUtility.SetDirty(preview);
        EditorUtility.SetDirty(session);
        EditorUtility.SetDirty(statusView);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static GameObject CreateRunningRouteSystemPrefab(
        Material trackMaterial,
        GameObject echoPrefab)
    {
        GameObject root = new GameObject("Running Route System");
        GameObject rendererObject = new GameObject("TrackRenderer");
        rendererObject.transform.SetParent(root.transform, false);
        GameObject spawnerObject = new GameObject("EchoStepSpawner");
        spawnerObject.transform.SetParent(root.transform, false);
        GameObject sequenceObject = new GameObject("EchoSequenceController");
        sequenceObject.transform.SetParent(root.transform, false);

        TrackRenderer trackRenderer = rendererObject.AddComponent<TrackRenderer>();
        trackRenderer.trackMaterial = trackMaterial;
        trackRenderer.height = 0.015f;

        EchoStepSpawner spawner = spawnerObject.AddComponent<EchoStepSpawner>();
        spawner.echoPrefab = echoPrefab;
        spawner.firstStepOffset = 0.8f;
        spawner.stepSpacing = 1.1f;
        spawner.lateralOffset = 0.2f;
        spawner.echoSize = 0.7f;
        spawner.groundOffset = 0.025f;
        spawner.activationDistance = 0.3f;
        spawner.requiredKneeAngle = 10f;
        spawner.requiredPressure = 60;

        EchoSequenceController sequence = sequenceObject.AddComponent<EchoSequenceController>();
        sequence.spawner = spawner;

        TrackRouteController route = root.AddComponent<TrackRouteController>();
        route.trackRenderer = trackRenderer;
        route.echoStepSpawner = spawner;
        route.segmentLength = 7f;
        route.trackWidth = 1.2f;
        route.turnAngle = 90f;
        route.routeAreaSize = 20f;
        route.enforceRouteBounds = true;
        route.randomSeed = 2026;
        route.initializeOnStart = false;
        route.autoAdvance = true;
        route.transitionTolerance = 0.15f;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, RouteSystemPrefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        if (prefab == null)
        {
            throw new InvalidOperationException(
                "Could not create running route prefab: " + RouteSystemPrefabPath);
        }

        return prefab;
    }

    private static GameObject CreateTrackOriginPrefab()
    {
        GameObject root = new GameObject("Track Origin");
        GameObject forward = new GameObject("Forward (+Z)");
        forward.transform.SetParent(root.transform, false);
        forward.transform.localPosition = Vector3.forward;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, TrackOriginPrefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        return prefab;
    }

    private static Material EnsureGroundPreviewMaterial()
    {
        Shader shader = Shader.Find("ACL Rehab/Unlit Transparent Double Sided");
        if (shader == null)
        {
            throw new InvalidOperationException("Ground preview shader was not found.");
        }

        Material material =
            AssetDatabase.LoadAssetAtPath<Material>(GroundPreviewMaterialPath);
        if (material == null)
        {
            material = new Material(shader) { name = "RayNeoGroundPreview" };
            AssetDatabase.CreateAsset(material, GroundPreviewMaterialPath);
        }
        else
        {
            material.shader = shader;
        }

        material.SetColor("_Color", new Color(0.2f, 1f, 0.72f, 0.22f));
        material.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void ConfigureBuildScenes()
    {
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(ScenePath, true),
            new EditorBuildSettingsScene(
                "Assets/Scenes/RunningPrototype_Debug.unity",
                false),
            new EditorBuildSettingsScene("Assets/Scenes/SampleScene.unity", false)
        };
    }

    private static void DestroyRoot(Scene scene, string objectName)
    {
        GameObject root = FindRoot(scene, objectName);
        if (root != null)
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static GameObject FindRoot(Scene scene, string objectName)
    {
        return scene.GetRootGameObjects().FirstOrDefault(root => root.name == objectName);
    }
}
