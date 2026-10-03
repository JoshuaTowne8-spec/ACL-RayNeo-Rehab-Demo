using System;
using System.Linq;
using ACLRehab.Running;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class RunningPrototypeDebugSceneSetup
{
    private const string ScenePath = "Assets/Scenes/RunningPrototype_Debug.unity";
    private const string TrackMaterialPath = "Assets/Materials/RunningTrack_7333FF.mat";
    private const string EchoMaterialPath = "Assets/Materials/RunningEchoPlaceholder.mat";
    private const string EchoStickerTexturePath = "Assets/Art/Echos/YourEcho.png";
    private const string EchoStickerMaterialPath = "Assets/Materials/RunningEchoSticker.mat";
    private const string EchoStickerPrefabPath = "Assets/Prefabs/Running/EchoSticker.prefab";
    private const string GroundMaterialPath = "Assets/Materials/RunningDebugGround.mat";
    private const string SessionKey = "ACL.RunningPrototypeDebugSceneSetup.v5";

    static RunningPrototypeDebugSceneSetup()
    {
        EditorApplication.delayCall += RunAutomaticSetupOnce;
    }

    [MenuItem("ACL Rehab/Build Running Prototype Debug Scene")]
    public static void BuildFromMenu()
    {
        BuildScene(true);
    }

    private static void RunAutomaticSetupOnce()
    {
        if (SessionState.GetBool(SessionKey, false) ||
            EditorApplication.isPlayingOrWillChangePlaymode ||
            EditorApplication.isCompiling)
        {
            return;
        }

        SessionState.SetBool(SessionKey, true);

        try
        {
            BuildScene(false);
        }
        catch (Exception exception)
        {
            Debug.LogError("Running prototype debug scene setup failed: " + exception);
        }
    }

    private static void BuildScene(bool selectRouteSystem)
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
            Material trackMaterial = EnsureTrackMaterial();
            Material echoMaterial = EnsureEchoMaterial();
            Material groundMaterial = EnsureGroundMaterial();
            GameObject echoStickerPrefab = EnsureEchoStickerPrefab();

            GameObject desktopPlayer = EnsureRoot(targetScene, "DesktopPlayer");
            desktopPlayer.transform.SetPositionAndRotation(new Vector3(0f, 1.7f, 0f), Quaternion.identity);
            desktopPlayer.transform.localScale = Vector3.one;

            DesktopRayNeoSimulator simulator = GetOrAdd<DesktopRayNeoSimulator>(desktopPlayer);
            simulator.moveSpeed = 2.5f;
            simulator.fastMoveMultiplier = 2f;
            simulator.lookSensitivity = 2f;

            GameObject mainCamera = FindGameObject(targetScene, "Main Camera") ??
                                    CreateGameObject(targetScene, "Main Camera");
            mainCamera.tag = "MainCamera";
            Camera camera = GetOrAdd<Camera>(mainCamera);
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 100f;
            GetOrAdd<AudioListener>(mainCamera);
            mainCamera.transform.SetParent(desktopPlayer.transform, false);
            mainCamera.transform.localPosition = Vector3.zero;
            mainCamera.transform.localRotation = Quaternion.identity;
            mainCamera.transform.localScale = Vector3.one;

            GameObject sensorManager = EnsureRoot(targetScene, "SensorManager");
            Esp32UdpReceiver receiver = GetOrAdd<Esp32UdpReceiver>(sensorManager);
            receiver.listenPort = 4210;
            receiver.connectionTimeout = 0.3f;
            SensorDebugOverlay overlay = GetOrAdd<SensorDebugOverlay>(sensorManager);
            overlay.receiver = receiver;
            Esp32DebugTransmitter debugTransmitter = GetOrAdd<Esp32DebugTransmitter>(sensorManager);
            debugTransmitter.destinationAddress = "127.0.0.1";
            debugTransmitter.destinationPort = 4210;
            debugTransmitter.sendRateHz = 20f;
            debugTransmitter.transmissionEnabled = true;
            debugTransmitter.validPressure = 80;
            debugTransmitter.validKneeAngle = 20f;

            GameObject debugTrackOrigin = EnsureRoot(targetScene, "DebugTrackOrigin");
            debugTrackOrigin.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            debugTrackOrigin.transform.localScale = Vector3.one;

            GameObject routeSystem = EnsureChild(debugTrackOrigin.transform, "Running Route System");
            GameObject trackRendererObject = EnsureChild(routeSystem.transform, "TrackRenderer");
            GameObject echoSpawnerObject = EnsureChild(routeSystem.transform, "EchoStepSpawner");
            GameObject echoSequenceObject = EnsureChild(routeSystem.transform, "EchoSequenceController");

            TrackRenderer trackRenderer = GetOrAdd<TrackRenderer>(trackRendererObject);
            trackRenderer.trackMaterial = trackMaterial;
            trackRenderer.height = 0.015f;

            EchoStepSpawner echoSpawner = GetOrAdd<EchoStepSpawner>(echoSpawnerObject);
            echoSpawner.echoPrefab = echoStickerPrefab;
            echoSpawner.placeholderMaterial = echoMaterial;
            echoSpawner.trackedHead = mainCamera.transform;
            echoSpawner.receiver = receiver;
            echoSpawner.firstStepOffset = 0.8f;
            echoSpawner.stepSpacing = 1.1f;
            echoSpawner.lateralOffset = 0.2f;
            echoSpawner.echoSize = 0.7f;
            echoSpawner.groundOffset = 0.025f;
            echoSpawner.activationDistance = 0.3f;
            echoSpawner.requiredKneeAngle = 10f;
            echoSpawner.requiredPressure = 60;

            EchoSequenceController sequence = GetOrAdd<EchoSequenceController>(echoSequenceObject);
            sequence.spawner = echoSpawner;
            sequence.receiver = receiver;

            TrackRouteController routeController = GetOrAdd<TrackRouteController>(routeSystem);
            routeController.routeOrigin = debugTrackOrigin.transform;
            routeController.trackedPlayer = mainCamera.transform;
            routeController.trackRenderer = trackRenderer;
            routeController.echoStepSpawner = echoSpawner;
            routeController.segmentLength = 7f;
            routeController.trackWidth = 1.2f;
            routeController.turnAngle = 90f;
            routeController.routeAreaSize = 20f;
            routeController.enforceRouteBounds = true;
            routeController.randomSeed = 2026;
            routeController.initializeOnStart = true;
            routeController.autoAdvance = true;
            routeController.transitionTolerance = 0.15f;

            GameObject debugGround = EnsurePrimitive(targetScene, "DebugGround", PrimitiveType.Plane);
            debugGround.transform.SetPositionAndRotation(new Vector3(0f, -0.01f, 15f), Quaternion.identity);
            debugGround.transform.localScale = new Vector3(5f, 1f, 5f);
            Renderer groundRenderer = debugGround.GetComponent<Renderer>();
            if (groundRenderer != null)
            {
                groundRenderer.sharedMaterial = groundMaterial;
            }

            EnsureDirectionalLight(targetScene);

            EditorUtility.SetDirty(simulator);
            EditorUtility.SetDirty(receiver);
            EditorUtility.SetDirty(overlay);
            EditorUtility.SetDirty(debugTransmitter);
            EditorUtility.SetDirty(trackRenderer);
            EditorUtility.SetDirty(echoSpawner);
            EditorUtility.SetDirty(sequence);
            EditorUtility.SetDirty(routeController);
            EditorSceneManager.MarkSceneDirty(targetScene);
            EditorSceneManager.SaveScene(targetScene, ScenePath);
            AssetDatabase.SaveAssets();

            if (selectRouteSystem && !openedAdditively)
            {
                Selection.activeGameObject = routeSystem;
                EditorGUIUtility.PingObject(routeSystem);
            }

            Debug.Log("Running prototype debug scene is ready: " + ScenePath);
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

    private static Material EnsureTrackMaterial()
    {
        Material material = EnsureMaterial(TrackMaterialPath, "RunningTrack_7333FF");
        Color color = new Color(115f / 255f, 51f / 255f, 1f, 0.65f);
        material.SetColor("_Color", color);
        material.SetFloat("_Mode", 3f);
        material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material EnsureEchoMaterial()
    {
        Material material = EnsureMaterial(EchoMaterialPath, "RunningEchoPlaceholder");
        Color color = new Color(0.75f, 0.95f, 1f, 1f);
        material.SetColor("_Color", color);
        if (material.HasProperty("_EmissionColor"))
        {
            material.SetColor("_EmissionColor", color * 1.5f);
            material.EnableKeyword("_EMISSION");
        }

        EditorUtility.SetDirty(material);
        return material;
    }

    private static GameObject EnsureEchoStickerPrefab()
    {
        Texture2D texture = ConfigureEchoStickerTexture();
        Material material = EnsureEchoStickerMaterial(texture);
        EnsureFolder("Assets/Prefabs", "Running");

        GameObject root = new GameObject("EchoSticker");
        RehabEchoTest test = root.AddComponent<RehabEchoTest>();
        test.activationDistance = 0.3f;
        test.requiredKneeAngle = 10f;
        test.requiredPressure = 60;
        test.pressureSensor = PressureSensorRequirement.Either;
        test.requiresKneeAngle = true;

        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Quad);
        visual.name = "Sticker Visual";
        visual.transform.SetParent(root.transform, false);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        visual.transform.localScale = Vector3.one;

        Collider collider = visual.GetComponent<Collider>();
        if (collider != null)
        {
            UnityEngine.Object.DestroyImmediate(collider);
        }

        Renderer renderer = visual.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, EchoStickerPrefabPath);
        UnityEngine.Object.DestroyImmediate(root);

        if (prefab == null)
        {
            throw new InvalidOperationException("Could not create echo sticker prefab: " + EchoStickerPrefabPath);
        }

        return prefab;
    }

    private static Texture2D ConfigureEchoStickerTexture()
    {
        TextureImporter importer = AssetImporter.GetAtPath(EchoStickerTexturePath) as TextureImporter;
        if (importer == null)
        {
            throw new InvalidOperationException("Echo sticker texture not found: " + EchoStickerTexturePath);
        }

        importer.textureType = TextureImporterType.Default;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.sRGBTexture = true;
        importer.mipmapEnabled = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.maxTextureSize = 2048;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.SaveAndReimport();

        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(EchoStickerTexturePath);
        if (texture == null)
        {
            throw new InvalidOperationException("Could not import echo sticker texture: " + EchoStickerTexturePath);
        }

        return texture;
    }

    private static Material EnsureEchoStickerMaterial(Texture2D texture)
    {
        Shader shader = Shader.Find("ACL Rehab/Unlit Transparent Double Sided");
        if (shader == null)
        {
            throw new InvalidOperationException("Echo sticker shader was not imported.");
        }

        Material material = AssetDatabase.LoadAssetAtPath<Material>(EchoStickerMaterialPath);
        if (material == null)
        {
            material = new Material(shader) { name = "RunningEchoSticker" };
            AssetDatabase.CreateAsset(material, EchoStickerMaterialPath);
        }
        else
        {
            material.shader = shader;
        }

        material.SetTexture("_MainTex", texture);
        material.SetColor("_Color", Color.white);
        material.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material EnsureGroundMaterial()
    {
        Material material = EnsureMaterial(GroundMaterialPath, "RunningDebugGround");
        material.SetColor("_Color", new Color(0.14f, 0.16f, 0.19f, 1f));
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material EnsureMaterial(string path, string name)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null)
        {
            return material;
        }

        Shader shader = Shader.Find("Standard");
        if (shader == null)
        {
            throw new InvalidOperationException("Built-in Standard shader was not found.");
        }

        material = new Material(shader) { name = name };
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void EnsureFolder(string parent, string child)
    {
        string path = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(path))
        {
            AssetDatabase.CreateFolder(parent, child);
        }
    }

    private static void EnsureDirectionalLight(Scene scene)
    {
        GameObject lightObject = FindGameObject(scene, "Directional Light") ??
                                 CreateGameObject(scene, "Directional Light");
        Light light = GetOrAdd<Light>(lightObject);
        light.type = LightType.Directional;
        light.color = new Color(1f, 0.956f, 0.839f);
        light.intensity = 1f;
        lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
    }

    private static GameObject EnsureRoot(Scene scene, string objectName)
    {
        GameObject existing = scene.GetRootGameObjects()
            .FirstOrDefault(root => root.name == objectName);
        return existing ?? CreateGameObject(scene, objectName);
    }

    private static GameObject EnsureChild(Transform parent, string objectName)
    {
        Transform existing = parent.Cast<Transform>()
            .FirstOrDefault(child => child.name == objectName);
        if (existing != null)
        {
            return existing.gameObject;
        }

        GameObject created = new GameObject(objectName);
        created.transform.SetParent(parent, false);
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

    private static GameObject CreateGameObject(Scene scene, string objectName)
    {
        GameObject created = new GameObject(objectName);
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
        T existing = gameObject.GetComponent<T>();
        return existing != null ? existing : gameObject.AddComponent<T>();
    }
}
