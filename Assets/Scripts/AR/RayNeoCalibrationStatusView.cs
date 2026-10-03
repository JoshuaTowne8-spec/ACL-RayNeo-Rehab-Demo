using UnityEngine;
using UnityEngine.UI;

namespace ACLRehab.RayNeoSpatial
{
    public sealed class RayNeoCalibrationStatusView : MonoBehaviour
    {
        private const int UserHudLayer = 30;

        [SerializeField] private RunningSpatialSessionController session;
        [SerializeField] private Esp32UdpReceiver sensorReceiver;
        [SerializeField] private bool visible = true;
        [SerializeField, Min(0.05f)] private float refreshInterval = 0.2f;

        private RayNeoSpatialBootstrap spatialBootstrap;
        private GroundPlaneSelector groundSelector;
        private TrackOriginCalibrator originCalibrator;
        private GameObject hudRoot;
        private Text titleText;
        private Text detailText;
        private float nextCameraSearchTime;
        private float nextHudRefreshTime;
        private float nextDiagnosticLogTime;
        private string lastDiagnosticSummary;
        private RayNeoMixedRealityCapture mixedRealityCapture;

        public bool IsVisible => visible;
        public string CurrentTitle { get; private set; } = "INITIALIZING RAYNEO SESSION";
        public string CurrentDetails { get; private set; } = string.Empty;

        public void Configure(
            RunningSpatialSessionController sessionController,
            Esp32UdpReceiver receiver)
        {
            session = sessionController;
            sensorReceiver = receiver;
        }

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            if (session != null)
            {
                session.StateChanged -= HandleStateChanged;
                session.StateChanged += HandleStateChanged;
            }

            if (originCalibrator != null)
            {
                originCalibrator.PostCalibrationTempleTapped -=
                    HandlePostCalibrationTempleTapped;
                originCalibrator.PostCalibrationTempleTapped +=
                    HandlePostCalibrationTempleTapped;
            }

            if (hudRoot != null)
            {
                hudRoot.SetActive(visible);
            }
        }

        private void Start()
        {
            EnsureWorldSpaceHud();
            RefreshHud();
        }

        private void Update()
        {
            if (!visible)
            {
                if (hudRoot != null && hudRoot.activeSelf)
                {
                    hudRoot.SetActive(false);
                }
            }

            if (hudRoot == null && Time.unscaledTime >= nextCameraSearchTime)
            {
                nextCameraSearchTime = Time.unscaledTime + 1f;
                EnsureWorldSpaceHud();
            }

            if (Time.unscaledTime >= nextHudRefreshTime)
            {
                nextHudRefreshTime = Time.unscaledTime + refreshInterval;
                RefreshHud();
            }

            LogDiagnosticsWhenChanged();
        }

        private void OnDisable()
        {
            if (session != null)
            {
                session.StateChanged -= HandleStateChanged;
            }

            if (originCalibrator != null)
            {
                originCalibrator.PostCalibrationTempleTapped -=
                    HandlePostCalibrationTempleTapped;
            }

            if (hudRoot != null)
            {
                hudRoot.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (hudRoot != null)
            {
                Destroy(hudRoot);
            }
        }

        private void ResolveReferences()
        {
            if (session == null)
            {
                session = GetComponent<RunningSpatialSessionController>();
            }

            if (sensorReceiver == null)
            {
                sensorReceiver = FindObjectOfType<Esp32UdpReceiver>();
            }

            spatialBootstrap = GetComponent<RayNeoSpatialBootstrap>();
            groundSelector = GetComponent<GroundPlaneSelector>();
            originCalibrator = GetComponent<TrackOriginCalibrator>();

            mixedRealityCapture = GetComponent<RayNeoMixedRealityCapture>();
            if (mixedRealityCapture == null)
            {
                mixedRealityCapture = gameObject.AddComponent<RayNeoMixedRealityCapture>();
            }

            mixedRealityCapture.Configure(
                session,
                sensorReceiver,
                originCalibrator,
                this);
        }

        private void EnsureWorldSpaceHud()
        {
            if (hudRoot != null)
            {
                hudRoot.SetActive(visible);
                return;
            }

            Camera xrCamera = Camera.main;
            if (xrCamera == null)
            {
                xrCamera = FindObjectOfType<Camera>();
            }

            if (xrCamera == null)
            {
                return;
            }

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            hudRoot = new GameObject(
                "RayNeo Calibration HUD",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler));
            hudRoot.layer = UserHudLayer;
            xrCamera.cullingMask |= 1 << UserHudLayer;
            hudRoot.transform.SetParent(xrCamera.transform, false);
            hudRoot.transform.localPosition = new Vector3(0f, 0.06f, 1.8f);
            hudRoot.transform.localRotation = Quaternion.identity;
            hudRoot.transform.localScale = Vector3.one * 0.00075f;

            RectTransform rootRect = hudRoot.GetComponent<RectTransform>();
            rootRect.sizeDelta = new Vector2(720f, 330f);

            Canvas canvas = hudRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = xrCamera;
            canvas.sortingOrder = 1000;

            CanvasScaler scaler = hudRoot.GetComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 2f;
            scaler.referencePixelsPerUnit = 100f;

            GameObject background = CreateUiObject("Background", hudRoot.transform);
            Image backgroundImage = background.AddComponent<Image>();
            backgroundImage.color = new Color(0.16f, 0.08f, 0.32f, 0.82f);
            Stretch(background.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);

            titleText = CreateText(
                "Status",
                hudRoot.transform,
                font,
                28,
                FontStyle.Bold,
                Color.white);
            titleText.resizeTextForBestFit = false;
            SetRect(titleText.rectTransform, 20f, 210f, -20f, -12f);

            detailText = CreateText(
                "Diagnostics",
                hudRoot.transform,
                font,
                18,
                FontStyle.Normal,
                new Color(0.88f, 0.84f, 1f));
            detailText.resizeTextForBestFit = false;
            SetRect(detailText.rectTransform, 20f, 12f, -20f, -120f);

            SetLayerRecursively(hudRoot, UserHudLayer);
            hudRoot.SetActive(visible);
            Debug.Log(
                $"RayNeo world-space calibration HUD created on camera '{xrCamera.name}'.",
                this);
        }

        private void RefreshHud()
        {
            if (hudRoot == null || titleText == null || detailText == null)
            {
                return;
            }

            if (visible && !hudRoot.activeSelf)
            {
                hudRoot.SetActive(true);
            }

            CurrentTitle = session != null
                ? session.StatusMessage
                : "INITIALIZING RAYNEO SESSION";

            string slamText = spatialBootstrap != null
                ? spatialBootstrap.SlamState.ToString().Replace("FFVINS_", string.Empty)
                : "NO BOOTSTRAP";
            int planeCount = spatialBootstrap != null ? spatialBootstrap.PlaneCount : 0;
            int validPlaneCount = groundSelector != null
                ? groundSelector.ValidPlaneCount
                : 0;
            int candidateCount = groundSelector != null
                ? groundSelector.CandidateCount
                : 0;
            bool hasGround = groundSelector != null && groundSelector.HasGround;
            int stableCount = originCalibrator != null
                ? originCalibrator.StableSampleCount
                : 0;
            int stableRequired = originCalibrator != null
                ? originCalibrator.StableSamplesRequired
                : 0;

            Esp32SensorData data = sensorReceiver != null
                ? sensorReceiver.latestData
                : null;
            string sensorText = sensorReceiver != null && sensorReceiver.isConnected && data != null
                ? $"ESP32 ON | P1 {data.pressure1} | P2 {data.pressure2} | KNEE {data.kneeAngle:F1}"
                : "ESP32 OFF (NOT REQUIRED)";

            string candidateText = groundSelector != null && groundSelector.HasCandidate
                ? $"SEL {groundSelector.SelectedCandidateId} | " +
                  $"TYPE {groundSelector.CandidateProperty.ToString().Replace("PLANE_", string.Empty)} | " +
                  $"MODE {groundSelector.CandidateDistanceMode}\n" +
                  $"POLY AREA {groundSelector.CandidateArea:F2} | " +
                  $"TILT {groundSelector.CandidateTiltDegrees:F0}"
                : "SEL - | TYPE - | MODE -\nPOLY AREA 0.00 | TILT -";
            string decision = groundSelector != null
                ? groundSelector.CandidateDecision
                : "NO_SELECTOR";
            string routeText = session != null
                ? $"ROUTE {(session.IsRouteActive ? "ON" : "OFF")} | " +
                  $"PROG {session.RoutePlayerProgress:F1} | " +
                  $"SCALE {(originCalibrator != null ? originCalibrator.EffectiveTranslationScale : 1f):F3} | " +
                  $"STEP {(originCalibrator != null ? originCalibrator.MetricHeadFrameStep : 0f):F3} | " +
                  $"GROUND H {(groundSelector != null ? groundSelector.CandidateHeadDistance : 0f):F1} | " +
                  (session.IsTrackingLossPending
                      ? $"LOSS {session.TrackingLossElapsed:F1}/{session.TrackingLossGraceSeconds:F0}"
                      : "LOSS -")
                : "ROUTE NO SESSION";

            CurrentDetails =
                $"SLAM {slamText} | RAW {planeCount} | VALID {validPlaneCount} | CAND {candidateCount}\n" +
                candidateText + "\n" +
                $"GROUND {(hasGround ? "YES" : "NO")} | STABLE {stableCount}/{stableRequired} | {decision}\n" +
                routeText + "\n" +
                sensorText + "\n" +
                (mixedRealityCapture != null ? mixedRealityCapture.StatusText : "CAPTURE INITIALIZING") +
                "\nGRID: X RED | Y BLUE";

            titleText.text = CurrentTitle;
            detailText.text = CurrentDetails;

            if (!visible && hudRoot.activeSelf)
            {
                hudRoot.SetActive(false);
            }
        }

        private void LogDiagnosticsWhenChanged()
        {
            if (Time.unscaledTime < nextDiagnosticLogTime)
            {
                return;
            }

            nextDiagnosticLogTime = Time.unscaledTime + 1f;
            string summary =
                $"state={(session != null ? session.State.ToString() : "NO_SESSION")}, " +
                $"slam={(spatialBootstrap != null ? spatialBootstrap.SlamState.ToString() : "NO_BOOTSTRAP")}, " +
                $"planes={(spatialBootstrap != null ? spatialBootstrap.PlaneCount : 0)}, " +
                $"validPlanes={(groundSelector != null ? groundSelector.ValidPlaneCount : 0)}, " +
                $"cachedCandidates={(groundSelector != null ? groundSelector.CandidateCount : 0)}, " +
                $"selectedCandidate={(groundSelector != null ? groundSelector.SelectedCandidateId : -1)}, " +
                $"ground={groundSelector != null && groundSelector.HasGround}, " +
                $"candidateType={(groundSelector != null && groundSelector.HasCandidate ? groundSelector.CandidateProperty.ToString() : "NONE")}, " +
                $"candidateArea={(groundSelector != null ? groundSelector.CandidateArea : 0f):F2}, " +
                $"candidateTilt={(groundSelector != null ? groundSelector.CandidateTiltDegrees : 0f):F1}, " +
                $"candidateDistance={(groundSelector != null ? groundSelector.CandidateHeadDistance : 0f):F2}, " +
                $"candidateHorizontalDistance={(groundSelector != null ? groundSelector.CandidateHorizontalDistance : 0f):F2}, " +
                $"distanceMode={(groundSelector != null ? groundSelector.CandidateDistanceMode : "NONE")}, " +
                $"rawPlanePosition={(groundSelector != null ? groundSelector.CandidateRawPosition.ToString("F3") : "NONE")}, " +
                $"resolvedPlanePosition={(groundSelector != null ? groundSelector.CandidateResolvedPosition.ToString("F3") : "NONE")}, " +
                $"headPosition={(Camera.main != null ? Camera.main.transform.position.ToString("F3") : "NONE")}, " +
                $"rawHeadStep={(originCalibrator != null ? originCalibrator.RawHeadFrameStep : 0f):F4}, " +
                $"metricHeadStep={(originCalibrator != null ? originCalibrator.MetricHeadFrameStep : 0f):F4}, " +
                $"decision={(groundSelector != null ? groundSelector.CandidateDecision : "NO_SELECTOR")}, " +
                $"stable={(originCalibrator != null ? originCalibrator.StableSampleCount : 0)}/" +
                $"{(originCalibrator != null ? originCalibrator.StableSamplesRequired : 0)}, " +
                $"calibrated={originCalibrator != null && originCalibrator.IsCalibrated}, " +
                $"esp32={sensorReceiver != null && sensorReceiver.isConnected}";

            if (summary == lastDiagnosticSummary)
            {
                return;
            }

            lastDiagnosticSummary = summary;
            Debug.Log("RayNeo spatial diagnostics: " + summary, this);
        }

        private void HandleStateChanged(RunningSpatialSessionState state)
        {
            if (state != RunningSpatialSessionState.Running)
            {
                SetHudVisible(true);
            }
            else
            {
                RefreshHud();
            }

            Debug.Log("RayNeo HUD session state changed: " + state, this);
        }

        private void HandlePostCalibrationTempleTapped()
        {
            if (originCalibrator == null || !originCalibrator.IsCalibrated)
            {
                return;
            }

            SetHudVisible(!visible);
            Debug.Log(
                "RayNeo calibration HUD " + (visible ? "shown" : "hidden") +
                " by right-temple tap.",
                this);
        }

        public void SetHudVisible(bool value)
        {
            visible = value;

            if (hudRoot != null)
            {
                hudRoot.SetActive(visible);
            }

            if (!visible)
            {
                return;
            }

            EnsureWorldSpaceHud();
            RefreshHud();
        }

        private static GameObject CreateUiObject(string name, Transform parent)
        {
            GameObject result = new GameObject(name, typeof(RectTransform));
            result.transform.SetParent(parent, false);
            return result;
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            root.layer = layer;
            foreach (Transform child in root.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }

        private static Text CreateText(
            string name,
            Transform parent,
            Font font,
            int fontSize,
            FontStyle fontStyle,
            Color color)
        {
            GameObject textObject = CreateUiObject(name, parent);
            Text text = textObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private static void Stretch(
            RectTransform rect,
            float left,
            float bottom,
            float right,
            float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private static void SetRect(
            RectTransform rect,
            float left,
            float bottom,
            float right,
            float top)
        {
            Stretch(rect, left, bottom, -right, -top);
        }
    }
}
