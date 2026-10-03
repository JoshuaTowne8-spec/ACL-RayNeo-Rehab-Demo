using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the requested ESP32 desktop test scene and wires all references.
/// It runs once after these scripts are imported and remains available as a menu command.
/// </summary>
public static class AclEsp32TestSceneSetup
{
    private const string ScenePath = "Assets/Scenes/ESP32_Test.unity";
    private const string MaterialPath = "Assets/Materials/EchoTargetMaterial.mat";
    private const string SessionKey = "ACL.ESP32TestSceneSetup.v1";

    [MenuItem("ACL Rehab/Build Legacy Desktop ESP32 Test Scene")]
    public static void BuildOrRepairSceneFromMenu()
    {
        BuildOrRepairScene(true);
    }

    private static void BuildOrRepairScene(bool selectSensorManager)
    {
        SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        if (sceneAsset == null)
        {
            throw new InvalidOperationException("Scene not found: " + ScenePath);
        }

        Scene previousActiveScene = SceneManager.GetActiveScene();
        Scene targetScene = SceneManager.GetSceneByPath(ScenePath);
        bool openedAdditively = !targetScene.IsValid() || !targetScene.isLoaded;

        if (openedAdditively)
        {
            targetScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        }

        SceneManager.SetActiveScene(targetScene);

        try
        {
            GameObject sensorManager = EnsureRoot(targetScene, "SensorManager");
            sensorManager.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            Esp32UdpReceiver receiver = GetOrAdd<Esp32UdpReceiver>(sensorManager);
            receiver.listenPort = 4210;
            receiver.connectionTimeout = 0.3f;

            SensorDebugOverlay overlay = GetOrAdd<SensorDebugOverlay>(sensorManager);
            overlay.receiver = receiver;

            GameObject simulatedRayNeo = EnsureRoot(targetScene, "SimulatedRayNeo");
            simulatedRayNeo.transform.SetPositionAndRotation(new Vector3(0f, 1.6f, 0f), Quaternion.identity);
            simulatedRayNeo.transform.localScale = Vector3.one;

            DesktopRayNeoSimulator simulator = GetOrAdd<DesktopRayNeoSimulator>(simulatedRayNeo);
            simulator.moveSpeed = 2.5f;
            simulator.fastMoveMultiplier = 2f;
            simulator.lookSensitivity = 2f;

            GameObject mainCamera = FindGameObject(targetScene, "Main Camera");
            if (mainCamera == null)
            {
                mainCamera = new GameObject("Main Camera");
                SceneManager.MoveGameObjectToScene(mainCamera, targetScene);
            }

            mainCamera.tag = "MainCamera";
            GetOrAdd<Camera>(mainCamera);
            GetOrAdd<AudioListener>(mainCamera);
            mainCamera.transform.SetParent(simulatedRayNeo.transform, false);
            mainCamera.transform.localPosition = Vector3.zero;
            mainCamera.transform.localRotation = Quaternion.identity;
            mainCamera.transform.localScale = Vector3.one;

            GameObject ground = EnsurePrimitive(targetScene, "Ground", PrimitiveType.Plane);
            ground.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            ground.transform.localScale = new Vector3(2f, 1f, 2f);

            GameObject echoTarget = EnsurePrimitive(targetScene, "EchoTarget", PrimitiveType.Sphere);
            echoTarget.transform.SetPositionAndRotation(new Vector3(0f, 1.2f, 5f), Quaternion.identity);
            echoTarget.transform.localScale = Vector3.one * 0.5f;

            Material echoMaterial = EnsureEchoMaterial();
            Renderer echoRenderer = echoTarget.GetComponent<Renderer>();
            if (echoRenderer != null)
            {
                echoRenderer.sharedMaterial = echoMaterial;
            }

            RehabEchoTest echoTest = GetOrAdd<RehabEchoTest>(echoTarget);
            echoTest.receiver = receiver;
            echoTest.trackedHead = mainCamera.transform;
            echoTest.activationDistance = 0.3f;
            echoTest.requiredKneeAngle = 10f;
            echoTest.requiredPressure = 60;
            echoTest.pressureSensor = PressureSensorRequirement.Either;
            echoTest.requiresKneeAngle = true;

            EnsureRoot(targetScene, "TestEnvironment");
            EnsureDirectionalLight(targetScene);

            // UDP on an Android-based headset requires the INTERNET permission.
            PlayerSettings.Android.forceInternetPermission = true;
            EnsureSceneInBuildSettings();

            EditorUtility.SetDirty(receiver);
            EditorUtility.SetDirty(overlay);
            EditorUtility.SetDirty(simulator);
            EditorUtility.SetDirty(echoTest);
            EditorSceneManager.MarkSceneDirty(targetScene);
            EditorSceneManager.SaveScene(targetScene, ScenePath);
            AssetDatabase.SaveAssets();

            if (selectSensorManager && !openedAdditively)
            {
                Selection.activeGameObject = sensorManager;
                EditorGUIUtility.PingObject(sensorManager);
            }

            Debug.Log("ESP32 test scene is ready: " + ScenePath);
        }
        finally
        {
            if (openedAdditively)
            {
                if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                {
                    SceneManager.SetActiveScene(previousActiveScene);
                }

                EditorSceneManager.CloseScene(targetScene, true);
            }
        }
    }

    private static GameObject EnsureRoot(Scene scene, string objectName)
    {
        GameObject existing = scene.GetRootGameObjects()
            .FirstOrDefault(root => root.name == objectName);

        if (existing != null)
        {
            return existing;
        }

        GameObject created = new GameObject(objectName);
        SceneManager.MoveGameObjectToScene(created, scene);
        return created;
    }

    private static GameObject EnsurePrimitive(Scene scene, string objectName, PrimitiveType primitiveType)
    {
        GameObject existing = FindGameObject(scene, objectName);
        if (existing != null)
        {
            return existing;
        }

        GameObject created = GameObject.CreatePrimitive(primitiveType);
        created.name = objectName;
        SceneManager.MoveGameObjectToScene(created, scene);
        return created;
    }

    private static GameObject FindGameObject(Scene scene, string objectName)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Transform match = root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(candidate => candidate.name == objectName);

            if (match != null)
            {
                return match.gameObject;
            }
        }

        return null;
    }

    private static T GetOrAdd<T>(GameObject gameObject) where T : Component
    {
        T component = gameObject.GetComponent<T>();
        return component != null ? component : gameObject.AddComponent<T>();
    }

    private static Material EnsureEchoMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            Shader shader = Shader.Find("Standard") ??
                            Shader.Find("Universal Render Pipeline/Lit");

            if (shader == null)
            {
                throw new InvalidOperationException("No supported Lit shader was found.");
            }

            material = new Material(shader) { name = "EchoTargetMaterial" };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }

        Color baseColor = new Color(0.55f, 0.08f, 1f, 1f);
        Color emissionColor = new Color(1.6f, 0.15f, 4f, 1f);

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", baseColor);
        }

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", baseColor);
        }

        if (material.HasProperty("_EmissionColor"))
        {
            material.SetColor("_EmissionColor", emissionColor);
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        EditorUtility.SetDirty(material);
        return material;
    }

    private static void EnsureDirectionalLight(Scene scene)
    {
        GameObject lightObject = FindGameObject(scene, "Directional Light");
        if (lightObject == null)
        {
            lightObject = new GameObject("Directional Light");
            SceneManager.MoveGameObjectToScene(lightObject, scene);
        }

        Light directionalLight = GetOrAdd<Light>(lightObject);
        directionalLight.type = LightType.Directional;
        directionalLight.color = new Color(1f, 0.956f, 0.839f);
        directionalLight.intensity = 1f;
        lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
    }

    private static void EnsureSceneInBuildSettings()
    {
        EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
        int existingIndex = Array.FindIndex(scenes, scene => scene.path == ScenePath);

        if (existingIndex >= 0)
        {
            if (!scenes[existingIndex].enabled)
            {
                scenes[existingIndex] = new EditorBuildSettingsScene(ScenePath, true);
                EditorBuildSettings.scenes = scenes;
            }

            return;
        }

        EditorBuildSettings.scenes = scenes
            .Concat(new[] { new EditorBuildSettingsScene(ScenePath, true) })
            .ToArray();
    }
}
