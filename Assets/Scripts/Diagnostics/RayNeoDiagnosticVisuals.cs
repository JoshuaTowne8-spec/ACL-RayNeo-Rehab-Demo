using UnityEngine;
using UnityEngine.UI;

namespace ACLRehab.RayNeoDiagnostics
{
    public sealed class RayNeoDiagnosticVisuals : MonoBehaviour
    {
        [SerializeField] private RayNeoSpatialPoseProbe poseProbe;
        [SerializeField] private RayNeoPlaneCoordinateProbe planeProbe;
        [SerializeField] private RayNeoDiagnosticRecorder recorder;
        [SerializeField, Min(0.05f)] private float refreshInterval = 0.2f;

        private GameObject hudRoot;
        private Text hudText;
        private float nextRefreshTime;

        public void Configure(
            RayNeoSpatialPoseProbe pose,
            RayNeoPlaneCoordinateProbe planes,
            RayNeoDiagnosticRecorder diagnosticRecorder)
        {
            poseProbe = pose;
            planeProbe = planes;
            recorder = diagnosticRecorder;
        }

        private void Start()
        {
            EnsureHud();
            RefreshHud();
        }

        private void Update()
        {
            if (hudRoot == null)
            {
                EnsureHud();
            }

            if (Time.unscaledTime >= nextRefreshTime)
            {
                nextRefreshTime = Time.unscaledTime + refreshInterval;
                RefreshHud();
            }
        }

        private void OnDestroy()
        {
            if (hudRoot != null)
            {
                Destroy(hudRoot);
            }
        }

        private void EnsureHud()
        {
            if (hudRoot != null)
            {
                return;
            }

            Camera camera = Camera.main;
            if (camera == null)
            {
                camera = FindObjectOfType<Camera>();
            }

            if (camera == null)
            {
                return;
            }

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            hudRoot = new GameObject(
                "Spatial Diagnostic HUD",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler));
            // Do not parent diagnostic UI to the tracked Head. On the affected
            // X3 Pro runtime the official 6DoF translation can jump by tens of
            // Unity units per frame. A world-space child then exposes the same
            // late-latching mismatch as the content under test and becomes
            // unreadable. ScreenSpaceCamera keeps the UI in the render camera's
            // image plane without reading or modifying any tracked pose.
            hudRoot.transform.SetParent(null, false);

            Canvas canvas = hudRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 0.5f;
            canvas.sortingOrder = 2000;
            canvas.pixelPerfect = true;

            CanvasScaler scaler = hudRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            GameObject background = new GameObject(
                "Top Diagnostic Panel",
                typeof(RectTransform),
                typeof(Image));
            background.transform.SetParent(hudRoot.transform, false);
            RectTransform backgroundRect = background.GetComponent<RectTransform>();
            backgroundRect.anchorMin = new Vector2(0.5f, 1f);
            backgroundRect.anchorMax = new Vector2(0.5f, 1f);
            backgroundRect.pivot = new Vector2(0.5f, 1f);
            backgroundRect.anchoredPosition = new Vector2(0f, -18f);
            backgroundRect.sizeDelta = new Vector2(1840f, 720f);
            background.GetComponent<Image>().color =
                new Color(0.012f, 0.018f, 0.04f, 0.96f);

            GameObject textObject = new GameObject(
                "Status",
                typeof(RectTransform),
                typeof(Text),
                typeof(Outline));
            textObject.transform.SetParent(background.transform, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(34f, 24f);
            textRect.offsetMax = new Vector2(-34f, -24f);

            hudText = textObject.GetComponent<Text>();
            hudText.font = font;
            hudText.fontSize = 44;
            hudText.resizeTextForBestFit = false;
            hudText.supportRichText = true;
            hudText.lineSpacing = 1.08f;
            hudText.alignment = TextAnchor.UpperLeft;
            hudText.horizontalOverflow = HorizontalWrapMode.Wrap;
            hudText.verticalOverflow = VerticalWrapMode.Truncate;
            hudText.color = Color.white;
            hudText.raycastTarget = false;

            Outline outline = textObject.GetComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.95f);
            outline.effectDistance = new Vector2(2f, -2f);
            outline.useGraphicAlpha = true;
        }

        private void RefreshHud()
        {
            if (hudText == null)
            {
                return;
            }

            string slam = planeProbe != null
                ? planeProbe.SlamState.ToString().Replace("FFVINS_", string.Empty)
                : "NO_PROBE";
            string rawPose = poseProbe != null && poseProbe.HasRawPose
                ? poseProbe.RawPose.position.ToString("F3")
                : "UNAVAILABLE";
            string worldPose = poseProbe != null
                ? poseProbe.HeadWorldPose.position.ToString("F3")
                : "UNAVAILABLE";
            string xrPose = poseProbe != null && poseProbe.HasXrDevicePose
                ? poseProbe.XrDevicePose.position.ToString("F3")
                : "UNAVAILABLE";
            string planeLine = planeProbe != null && planeProbe.ObservationCount > 0
                ? planeProbe.GetShortObservationSummary()
                : "PLANE -";
            string file = recorder != null && !string.IsNullOrEmpty(recorder.OutputFilePath)
                ? System.IO.Path.GetFileName(recorder.OutputFilePath)
                : "opening...";

            hudText.text =
                "<b>RAYNEO 6DOF / PLANE DIAGNOSTIC</b>\n" +
                $"SLAM {slam}   PLANES {(planeProbe != null ? planeProbe.ObservationCount : 0)}   " +
                $"SNAP {(planeProbe != null ? planeProbe.SnapshotSerial : 0)}\n" +
                $"SDK P {rawPose}   dP {(poseProbe != null ? poseProbe.RawPoseStep : 0f):F4}\n" +
                $"HEAD P {worldPose}   dP {(poseProbe != null ? poseProbe.HeadWorldStep : 0f):F4}   " +
                $"dR {(poseProbe != null ? poseProbe.HeadWorldAngleStep : 0f):F2}\n" +
                $"XR P {xrPose}\n" +
                "NATIVE UNSUPPORTED (ENTRY POINT ABSENT)\n" +
                planeLine + "\n" +
                "GREEN direct   BLUE origin-child   YELLOW world\n" +
                "RIGHT TEMPLE TAP: FREEZE PLANES\n" +
                $"LOG {file}";
        }

        public static Material CreateTransparentMaterial(
            string materialName,
            Color color,
            int renderQueue)
        {
            Shader shader = Shader.Find("Sprites/Default") ??
                            Shader.Find("UI/Default") ??
                            Shader.Find("Unlit/Color");
            Material material = new Material(shader)
            {
                name = materialName,
                color = color,
                renderQueue = renderQueue
            };
            return material;
        }

        public static GameObject CreateWorldReference(
            Vector3 headPosition,
            Vector3 headForward)
        {
            Vector3 up = Vector3.up;
            Vector3 forward = Vector3.ProjectOnPlane(headForward, up).normalized;
            if (forward.sqrMagnitude < 0.01f)
            {
                forward = Vector3.forward;
            }

            Vector3 right = Vector3.Cross(up, forward).normalized;
            GameObject root = new GameObject("WORLD LOCKED REFERENCE - NEVER REPARENT");

            CreateMarkerCube(
                "Centre marker 2m",
                root.transform,
                headPosition + forward * 2f,
                new Color(1f, 1f, 1f, 0.95f),
                0.18f);
            CreateMarkerCube(
                "Left marker",
                root.transform,
                headPosition + forward * 2f - right * 0.65f,
                new Color(1f, 0.2f, 0.75f, 0.95f),
                0.14f);
            CreateMarkerCube(
                "Right marker",
                root.transform,
                headPosition + forward * 2f + right * 0.65f,
                new Color(0.15f, 1f, 1f, 0.95f),
                0.14f);

            Vector3 floorOrigin = headPosition - up * 1.6f + forward;
            CreateAxisLine(
                "Floor X red",
                root.transform,
                floorOrigin - right * 0.5f,
                floorOrigin + right * 0.5f,
                Color.red);
            CreateAxisLine(
                "Floor Y green",
                root.transform,
                floorOrigin,
                floorOrigin + up * 0.7f,
                Color.green);
            CreateAxisLine(
                "Floor Z blue",
                root.transform,
                floorOrigin - forward * 0.5f,
                floorOrigin + forward * 1.5f,
                Color.blue);

            CreateMarkerCube(
                "One metre marker",
                root.transform,
                floorOrigin + forward,
                new Color(1f, 0.55f, 0.05f, 0.95f),
                0.12f);
            return root;
        }

        private static void CreateMarkerCube(
            string objectName,
            Transform parent,
            Vector3 worldPosition,
            Color color,
            float size)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = objectName;
            marker.transform.SetParent(parent, true);
            marker.transform.position = worldPosition;
            marker.transform.rotation = Quaternion.identity;
            marker.transform.localScale = Vector3.one * size;
            Collider collider = marker.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            Renderer renderer = marker.GetComponent<Renderer>();
            renderer.sharedMaterial = CreateTransparentMaterial(
                objectName + " Material",
                color,
                3010);
        }

        private static void CreateAxisLine(
            string objectName,
            Transform parent,
            Vector3 start,
            Vector3 end,
            Color color)
        {
            GameObject lineObject = new GameObject(objectName, typeof(LineRenderer));
            lineObject.transform.SetParent(parent, true);
            LineRenderer line = lineObject.GetComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.SetPosition(0, start);
            line.SetPosition(1, end);
            line.startWidth = 0.018f;
            line.endWidth = 0.018f;
            line.numCapVertices = 4;
            line.sharedMaterial = CreateTransparentMaterial(
                objectName + " Material",
                color,
                3011);
        }
    }
}
