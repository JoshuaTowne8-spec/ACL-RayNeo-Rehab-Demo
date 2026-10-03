using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using ACLRehab.Running;
using NatSuite.Recorders;
using NatSuite.Recorders.Clocks;
using NatSuite.Recorders.Inputs;
using RayNeo.API;
using UnityEngine;
using UnityEngine.UI;
using static com.rayneo.xr.extensions.XRCamera;

namespace ACLRehab.RayNeoSpatial
{
    /// <summary>
    /// Captures the RayNeo RGB camera and the live Unity world into separate
    /// files without changing the optical view.
    /// </summary>
    public sealed class RayNeoMixedRealityCapture : MonoBehaviour
    {
        private const int CameraBackgroundLayer = 5;
        private const int UserHudLayer = 30;

        [SerializeField, Range(15, 30)] private int videoFrameRate = 30;
        [SerializeField, Range(2_000_000, 12_000_000)]
        private int videoBitrate = 6_000_000;
        [SerializeField, Range(640, 1920)] private int maximumVideoWidth = 1280;
        [SerializeField, Range(1280, 2560)] private int photoWidth = 1920;
        [SerializeField, Range(2f, 8f)] private float cameraStartTimeout = 4f;
        [SerializeField, Range(0.8f, 3f)] private float feedbackDuration = 1.5f;

        private RunningSpatialSessionController session;
        private Esp32UdpReceiver sensorReceiver;
        private TrackOriginCalibrator originCalibrator;
        private RayNeoCalibrationStatusView statusView;
        private Camera sourceCamera;

        private GameObject captureRoot;
        private Camera backgroundCamera;
        private Camera compositeCamera;
        private RawImage cameraImage;
        private GameObject feedbackRoot;
        private Image feedbackBackground;
        private Text feedbackText;
        private RenderTexture realityTarget;
        private RenderTexture compositeTarget;
        private XRCameraHandler rayNeoCamera;
        private XRResolution rayNeoCameraResolution;

        private RealtimeClock recorderClock;
        private MP4Recorder realityRecorder;
        private MP4Recorder unityRecorder;
        private CameraInput realityRecorderInput;
        private CameraInput unityRecorderInput;
        private Coroutine pendingOperation;
        private float recordingStartedAt;
        private string recordingTimestamp;
        private bool startPending;
        private bool stopPending;
        private bool feedbackShowsRecording;
        private Coroutine feedbackRoutine;

        public bool IsRecording { get; private set; }
        public string LastSavedPath { get; private set; } = string.Empty;
        public string LastRealitySavedPath { get; private set; } = string.Empty;
        public string LastUnitySavedPath { get; private set; } = string.Empty;
        public string StatusText { get; private set; } =
            "PHOTO READY: HOLD / VIDEO AFTER ORIGIN";

        public void Configure(
            RunningSpatialSessionController sessionController,
            Esp32UdpReceiver receiver,
            TrackOriginCalibrator calibrator,
            RayNeoCalibrationStatusView calibrationStatusView)
        {
            session = sessionController;
            sensorReceiver = receiver;
            originCalibrator = calibrator;
            statusView = calibrationStatusView;
            ResolveReferences();
            SubscribeToInput();
        }

        private void OnEnable()
        {
            ResolveReferences();
            SubscribeToInput();
        }

        private void OnDisable()
        {
            UnsubscribeFromInput();
            if (IsRecording || startPending)
            {
                StopRecording();
            }
        }

        private void OnDestroy()
        {
            UnsubscribeFromInput();
            realityRecorderInput?.Dispose();
            realityRecorderInput = null;
            unityRecorderInput?.Dispose();
            unityRecorderInput = null;
            CloseRayNeoCamera();
            ReleasePipeline();
            if (feedbackRoot != null)
            {
                Destroy(feedbackRoot);
                feedbackRoot = null;
            }
        }

        private void LateUpdate()
        {
            ResolveSourceCamera();
            if (sourceCamera == null || compositeCamera == null)
            {
                return;
            }

            compositeCamera.transform.SetPositionAndRotation(
                sourceCamera.transform.position,
                sourceCamera.transform.rotation);
            compositeCamera.nearClipPlane = sourceCamera.nearClipPlane;
            compositeCamera.farClipPlane = sourceCamera.farClipPlane;

            if (feedbackShowsRecording && IsRecording && feedbackText != null)
            {
                feedbackText.text =
                    $"● REC  {Time.unscaledTime - recordingStartedAt:00.0}s";
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && IsRecording)
            {
                StopRecording();
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

            if (originCalibrator == null)
            {
                originCalibrator = GetComponent<TrackOriginCalibrator>();
            }

            if (statusView == null)
            {
                statusView = GetComponent<RayNeoCalibrationStatusView>();
            }

            ResolveSourceCamera();
        }

        private void ResolveSourceCamera()
        {
            if (sourceCamera == null)
            {
                sourceCamera = Camera.main != null
                    ? Camera.main
                    : FindObjectOfType<Camera>();
            }
        }

        private void SubscribeToInput()
        {
            if (originCalibrator == null)
            {
                return;
            }

            UnsubscribeFromInput();
            originCalibrator.PostCalibrationTempleDoubleTapped += ToggleRecording;
            originCalibrator.PostCalibrationTempleLongPressed += CapturePhoto;
            originCalibrator.OriginReset += HandleOriginReset;
        }

        private void UnsubscribeFromInput()
        {
            if (originCalibrator == null)
            {
                return;
            }

            originCalibrator.PostCalibrationTempleDoubleTapped -= ToggleRecording;
            originCalibrator.PostCalibrationTempleLongPressed -= CapturePhoto;
            originCalibrator.OriginReset -= HandleOriginReset;
        }

        private void ToggleRecording()
        {
            if (stopPending)
            {
                ShowTransientFeedback(
                    "PLEASE WAIT",
                    new Color(0.95f, 0.72f, 0.18f, 0.92f));
                return;
            }

            if (IsRecording || startPending)
            {
                StopRecording();
            }
            else
            {
                StartRecording();
            }
        }

        private void StartRecording()
        {
            if (originCalibrator == null || !originCalibrator.IsCalibrated)
            {
                StatusText = "CAPTURE BLOCKED: CONFIRM ORIGIN";
                ShowTransientFeedback(
                    "CONFIRM ORIGIN FIRST",
                    new Color(0.95f, 0.48f, 0.16f, 0.92f));
                return;
            }

            if (startPending || IsRecording)
            {
                return;
            }

            startPending = true;
            StatusText = "CAPTURE STARTING CAMERA";
            ShowPersistentFeedback(
                "STARTING VIDEO...",
                new Color(0.95f, 0.72f, 0.18f, 0.92f));
            pendingOperation = StartCoroutine(StartRecordingRoutine());
        }

        private IEnumerator StartRecordingRoutine()
        {
            yield return EnsureRayNeoCameraReady(maximumVideoWidth);
            if (rayNeoCamera == null || rayNeoCamera.texture == null)
            {
                startPending = false;
                pendingOperation = null;
                StatusText = "CAPTURE CAMERA FAILED";
                ShowTransientFeedback(
                    "VIDEO CAMERA FAILED",
                    new Color(0.88f, 0.18f, 0.22f, 0.94f));
                CloseRayNeoCamera();
                yield break;
            }

            int outputWidth = Mathf.Min(maximumVideoWidth, rayNeoCameraResolution.width);
            outputWidth = Mathf.Max(640, outputWidth) & ~1;
            int outputHeight = Mathf.RoundToInt(
                outputWidth * rayNeoCameraResolution.height /
                (float)Mathf.Max(1, rayNeoCameraResolution.width)) & ~1;
            outputHeight = Mathf.Max(400, outputHeight);

            EnsurePipeline(outputWidth, outputHeight);
            cameraImage.texture = rayNeoCamera.texture;
            SetPipelineActive(true);

            try
            {
                recorderClock = new RealtimeClock();
                recordingTimestamp = DateTime.Now.ToString("yyyy_MM_dd_HH_mm_ss_fff");
                realityRecorder = new MP4Recorder(
                    outputWidth,
                    outputHeight,
                    videoFrameRate,
                    0,
                    0,
                    videoBitrate,
                    2);
                unityRecorder = new MP4Recorder(
                    outputWidth,
                    outputHeight,
                    videoFrameRate,
                    0,
                    0,
                    videoBitrate,
                    2);
                realityRecorderInput = new CameraInput(
                    realityRecorder,
                    recorderClock,
                    backgroundCamera);
                unityRecorderInput = new CameraInput(
                    unityRecorder,
                    recorderClock,
                    compositeCamera);
            }
            catch (Exception exception)
            {
                Debug.LogError("RayNeo split recorder failed to start: " + exception, this);
                realityRecorderInput?.Dispose();
                realityRecorderInput = null;
                unityRecorderInput?.Dispose();
                unityRecorderInput = null;
                realityRecorder = null;
                unityRecorder = null;
                recorderClock = null;
                startPending = false;
                pendingOperation = null;
                StatusText = "CAPTURE ENCODER FAILED";
                ShowTransientFeedback(
                    "VIDEO START FAILED",
                    new Color(0.88f, 0.18f, 0.22f, 0.94f));
                SetPipelineActive(false);
                CloseRayNeoCamera();
                yield break;
            }

            recordingStartedAt = Time.unscaledTime;
            IsRecording = true;
            startPending = false;
            pendingOperation = null;
            StatusText =
                $"RECORDING UNITY + REALITY {outputWidth}x{outputHeight} {videoFrameRate}FPS";
            ShowRecordingFeedback();
            Debug.Log("RayNeo split Unity/reality recording started. " + StatusText, this);
        }

        private async void StopRecording()
        {
            if (stopPending)
            {
                return;
            }

            bool canceledPendingStart = startPending;
            if (startPending && pendingOperation != null)
            {
                StopCoroutine(pendingOperation);
                pendingOperation = null;
                startPending = false;
            }

            if (!IsRecording || realityRecorder == null || unityRecorder == null)
            {
                StatusText = originCalibrator != null && originCalibrator.IsCalibrated
                    ? "CAPTURE READY: DOUBLE TAP / HOLD FOR PHOTO"
                    : "PHOTO READY: HOLD / VIDEO AFTER ORIGIN";
                ShowTransientFeedback(
                    canceledPendingStart ? "VIDEO CANCELED" : "VIDEO NOT RECORDING",
                    new Color(0.95f, 0.72f, 0.18f, 0.92f));
                SetPipelineActive(false);
                CloseRayNeoCamera();
                return;
            }

            stopPending = true;
            IsRecording = false;
            StatusText = "SAVING VIDEO";
            ShowPersistentFeedback(
                "SAVING VIDEO...",
                new Color(0.95f, 0.72f, 0.18f, 0.92f));
            realityRecorderInput?.Dispose();
            realityRecorderInput = null;
            unityRecorderInput?.Dispose();
            unityRecorderInput = null;
            MP4Recorder realityRecorderToFinish = realityRecorder;
            MP4Recorder unityRecorderToFinish = unityRecorder;
            realityRecorder = null;
            unityRecorder = null;
            recorderClock = null;

            try
            {
                Task<string> realitySaveTask = realityRecorderToFinish.FinishWriting();
                Task<string> unitySaveTask = unityRecorderToFinish.FinishWriting();
                string[] savedPaths = await Task.WhenAll(realitySaveTask, unitySaveTask);

                LastRealitySavedPath = MoveSavedMedia(
                    savedPaths[0],
                    "reality_recording_" + recordingTimestamp + ".mp4");
                LastUnitySavedPath = MoveSavedMedia(
                    savedPaths[1],
                    "unity_recording_" + recordingTimestamp + ".mp4");
                LastSavedPath = LastUnitySavedPath;
                StatusText = "VIDEO SAVED: UNITY + REALITY";
                ShowTransientFeedback(
                    "2 VIDEOS SAVED",
                    new Color(0.18f, 0.78f, 0.42f, 0.94f));
                Debug.Log(
                    $"RayNeo reality video saved to: {LastRealitySavedPath}\n" +
                    $"RayNeo Unity video saved to: {LastUnitySavedPath}",
                    this);
            }
            catch (Exception exception)
            {
                StatusText = "VIDEO SAVE FAILED";
                ShowTransientFeedback(
                    "VIDEO SAVE FAILED",
                    new Color(0.88f, 0.18f, 0.22f, 0.94f));
                Debug.LogError("RayNeo split video save failed: " + exception, this);
            }
            finally
            {
                stopPending = false;
                SetPipelineActive(false);
                CloseRayNeoCamera();
            }
        }

        private void CapturePhoto()
        {
            if (pendingOperation != null || stopPending)
            {
                ShowTransientFeedback(
                    "CAPTURE BUSY",
                    new Color(0.95f, 0.72f, 0.18f, 0.92f));
                return;
            }

            StatusText = "OPENING CAMERA FOR PHOTO";
            ShowPersistentFeedback(
                "TAKING PHOTO...",
                new Color(0.28f, 0.58f, 0.96f, 0.94f));
            pendingOperation = StartCoroutine(CapturePhotoRoutine());
        }

        private IEnumerator CapturePhotoRoutine()
        {
            bool cameraWasAlreadyOpen = rayNeoCamera != null;
            yield return EnsureRayNeoCameraReady(
                IsRecording ? maximumVideoWidth : photoWidth);
            if (rayNeoCamera == null || rayNeoCamera.texture == null)
            {
                StatusText = "PHOTO CAMERA FAILED";
                ShowTransientFeedback(
                    "PHOTO CAMERA FAILED",
                    new Color(0.88f, 0.18f, 0.22f, 0.94f));
                pendingOperation = null;
                yield break;
            }

            int targetWidth;
            int targetHeight;
            if (IsRecording && compositeTarget != null && realityTarget != null)
            {
                targetWidth = compositeTarget.width;
                targetHeight = compositeTarget.height;
            }
            else
            {
                targetWidth = Mathf.Max(1280, photoWidth) & ~1;
                targetHeight = Mathf.RoundToInt(
                    targetWidth * rayNeoCameraResolution.height /
                    (float)Mathf.Max(1, rayNeoCameraResolution.width)) & ~1;
                EnsurePipeline(targetWidth, targetHeight);
                cameraImage.texture = rayNeoCamera.texture;
                SetPipelineActive(true);
            }

            StatusText = $"CAPTURING UNITY + REALITY PHOTOS {targetWidth}x{targetHeight}";
            yield return new WaitForEndOfFrame();

            backgroundCamera.Render();
            compositeCamera.Render();
            Texture2D realityImage = ReadRenderTexture(realityTarget);
            Texture2D unityImage = ReadRenderTexture(compositeTarget);

            string timestamp = DateTime.Now.ToString("yyyy_MM_dd_HH_mm_ss_fff");
            string realityFilename = "reality_photo_" + timestamp + ".png";
            string unityFilename = "unity_photo_" + timestamp + ".png";
            string realityPath = Path.Combine(
                Application.persistentDataPath,
                realityFilename);
            string unityPath = Path.Combine(
                Application.persistentDataPath,
                unityFilename);
            try
            {
                File.WriteAllBytes(realityPath, realityImage.EncodeToPNG());
                File.WriteAllBytes(unityPath, unityImage.EncodeToPNG());
                LastRealitySavedPath = realityPath;
                LastUnitySavedPath = unityPath;
                LastSavedPath = LastUnitySavedPath;
                StatusText = "PHOTOS SAVED: UNITY + REALITY";
                ShowTransientFeedback(
                    "2 PHOTOS SAVED",
                    new Color(0.18f, 0.78f, 0.42f, 0.94f));
                Debug.Log(
                    $"RayNeo reality photo saved to: {realityPath}\n" +
                    $"RayNeo Unity photo saved to: {unityPath}",
                    this);
            }
            catch (Exception exception)
            {
                StatusText = "PHOTO SAVE FAILED";
                ShowTransientFeedback(
                    "PHOTO SAVE FAILED",
                    new Color(0.88f, 0.18f, 0.22f, 0.94f));
                Debug.LogError("RayNeo split photo save failed: " + exception, this);
            }
            finally
            {
                Destroy(realityImage);
                Destroy(unityImage);
                if (!IsRecording)
                {
                    SetPipelineActive(false);
                    if (!cameraWasAlreadyOpen)
                    {
                        CloseRayNeoCamera();
                    }
                }

                pendingOperation = null;
            }
        }

        private static Texture2D ReadRenderTexture(RenderTexture target)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            Texture2D image = new Texture2D(
                target.width,
                target.height,
                TextureFormat.RGB24,
                false);
            image.ReadPixels(
                new Rect(0f, 0f, target.width, target.height),
                0,
                0,
                false);
            image.Apply(false, false);
            RenderTexture.active = previous;
            return image;
        }

        private static string MoveSavedMedia(
            string sourcePath,
            string destinationFilename)
        {
            string destinationPath = Path.Combine(
                Application.persistentDataPath,
                destinationFilename);
            if (string.Equals(
                    Path.GetFullPath(sourcePath),
                    Path.GetFullPath(destinationPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                return sourcePath;
            }

            File.Move(sourcePath, destinationPath);
            return destinationPath;
        }

        private IEnumerator EnsureRayNeoCameraReady(int requestedMaximumWidth)
        {
            XRResolution requestedResolution =
                SelectCameraResolution(requestedMaximumWidth);
            if (rayNeoCamera != null && !IsRecording &&
                (rayNeoCameraResolution.width != requestedResolution.width ||
                 rayNeoCameraResolution.height != requestedResolution.height))
            {
                CloseRayNeoCamera();
            }

            if (rayNeoCamera == null)
            {
                rayNeoCameraResolution = requestedResolution;
                try
                {
                    rayNeoCamera = ShareCamera.OpenCamera(
                        XRCameraType.RGB,
                        rayNeoCameraResolution,
                        cameraImage,
                        videoFrameRate);
                }
                catch (Exception exception)
                {
                    rayNeoCamera = null;
                    Debug.LogError("RayNeo RGB camera open failed: " + exception, this);
                }
            }

            float deadline = Time.unscaledTime + cameraStartTimeout;
            while (rayNeoCamera != null && rayNeoCamera.texture == null &&
                   Time.unscaledTime < deadline)
            {
                yield return null;
            }
        }

        private XRResolution SelectCameraResolution(int requestedMaximumWidth)
        {
            XRResolution fallback = new XRResolution(640, 400);
            try
            {
                XRResolution[] supported = ShareCamera.getSupportResolutions(XRCameraType.RGB);
                if (supported == null || supported.Length == 0)
                {
                    return fallback;
                }

                XRResolution best = fallback;
                long bestArea = 0;
                foreach (XRResolution resolution in supported)
                {
                    if (resolution.width <= 0 || resolution.height <= 0 ||
                        resolution.width > requestedMaximumWidth)
                    {
                        continue;
                    }

                    long area = (long)resolution.width * resolution.height;
                    if (area > bestArea)
                    {
                        best = resolution;
                        bestArea = area;
                    }
                }

                return bestArea > 0 ? best : supported[0];
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Could not query RayNeo RGB camera resolutions: " + exception.Message, this);
                return fallback;
            }
        }

        private void EnsurePipeline(int width, int height)
        {
            ResolveSourceCamera();
            if (captureRoot == null)
            {
                CreatePipelineObjects();
            }

            if (compositeTarget != null && realityTarget != null &&
                compositeTarget.width == width && compositeTarget.height == height &&
                realityTarget.width == width && realityTarget.height == height)
            {
                return;
            }

            backgroundCamera.targetTexture = null;
            compositeCamera.targetTexture = null;
            ReleaseRenderTarget(ref realityTarget);
            ReleaseRenderTarget(ref compositeTarget);

            realityTarget = CreateRenderTarget(
                "RayNeo Reality Capture",
                width,
                height);
            compositeTarget = CreateRenderTarget(
                "RayNeo Unity Capture",
                width,
                height);
            backgroundCamera.targetTexture = realityTarget;
            compositeCamera.targetTexture = compositeTarget;
            ApplyPhysicalCameraProjection(compositeCamera);
        }

        private static RenderTexture CreateRenderTarget(
            string targetName,
            int width,
            int height)
        {
            RenderTexture target = new RenderTexture(
                width,
                height,
                24,
                RenderTextureFormat.ARGB32)
            {
                name = targetName,
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false
            };
            target.Create();
            return target;
        }

        private static void ReleaseRenderTarget(ref RenderTexture target)
        {
            if (target == null)
            {
                return;
            }

            target.Release();
            Destroy(target);
            target = null;
        }

        private void CreatePipelineObjects()
        {
            captureRoot = new GameObject("RayNeo Mixed Reality Capture Pipeline");
            captureRoot.transform.SetParent(transform, false);

            backgroundCamera = new GameObject("RGB Background Camera")
                .AddComponent<Camera>();
            backgroundCamera.transform.SetParent(captureRoot.transform, false);
            backgroundCamera.clearFlags = CameraClearFlags.SolidColor;
            backgroundCamera.backgroundColor = Color.black;
            backgroundCamera.cullingMask = 1 << CameraBackgroundLayer;
            backgroundCamera.depth = -100f;
            backgroundCamera.stereoTargetEye = StereoTargetEyeMask.None;

            compositeCamera = new GameObject("Unity Capture Camera")
                .AddComponent<Camera>();
            compositeCamera.transform.SetParent(captureRoot.transform, false);
            compositeCamera.clearFlags = CameraClearFlags.SolidColor;
            compositeCamera.backgroundColor = Color.black;
            int sourceMask = sourceCamera != null ? sourceCamera.cullingMask : ~0;
            compositeCamera.cullingMask =
                sourceMask & ~(1 << CameraBackgroundLayer) & ~(1 << UserHudLayer);
            compositeCamera.depth = -99f;
            compositeCamera.stereoTargetEye = StereoTargetEyeMask.None;

            CreateCameraBackground();
            SetPipelineActive(false);
        }

        private void EnsureUserFeedbackHud()
        {
            ResolveSourceCamera();
            if (feedbackRoot != null || sourceCamera == null)
            {
                return;
            }

            feedbackRoot = new GameObject(
                "RayNeo Capture Feedback HUD",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler));
            feedbackRoot.layer = UserHudLayer;
            sourceCamera.cullingMask |= 1 << UserHudLayer;
            feedbackRoot.transform.SetParent(sourceCamera.transform, false);
            feedbackRoot.transform.localPosition = new Vector3(0f, -0.17f, 1.55f);
            feedbackRoot.transform.localRotation = Quaternion.identity;
            feedbackRoot.transform.localScale = Vector3.one * 0.0007f;

            RectTransform rootRect = feedbackRoot.GetComponent<RectTransform>();
            rootRect.sizeDelta = new Vector2(320f, 64f);

            Canvas canvas = feedbackRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = sourceCamera;
            canvas.sortingOrder = 2000;

            CanvasScaler scaler = feedbackRoot.GetComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 2f;
            scaler.referencePixelsPerUnit = 100f;

            GameObject background = CreateUiObject("Feedback Background", feedbackRoot.transform);
            background.layer = feedbackRoot.layer;
            feedbackBackground = background.AddComponent<Image>();
            Stretch(background.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            feedbackText = CreateText(
                "Feedback Text",
                feedbackRoot.transform,
                font,
                22,
                FontStyle.Bold,
                Color.white);
            feedbackText.gameObject.layer = feedbackRoot.layer;
            feedbackText.alignment = TextAnchor.MiddleCenter;
            Stretch(feedbackText.rectTransform, 12f, 6f, 12f, 6f);
            SetLayerRecursively(feedbackRoot, UserHudLayer);
            feedbackRoot.SetActive(false);
        }

        private void ShowRecordingFeedback()
        {
            StopFeedbackRoutine();
            feedbackShowsRecording = true;
            SetFeedbackVisual(
                "● REC  00.0s",
                new Color(0.72f, 0.06f, 0.10f, 0.94f));
        }

        private void ShowPersistentFeedback(string message, Color backgroundColor)
        {
            StopFeedbackRoutine();
            feedbackShowsRecording = false;
            SetFeedbackVisual(message, backgroundColor);
        }

        private void ShowTransientFeedback(string message, Color backgroundColor)
        {
            StopFeedbackRoutine();
            feedbackShowsRecording = false;
            SetFeedbackVisual(message, backgroundColor);
            feedbackRoutine = StartCoroutine(HideFeedbackRoutine());
        }

        private void SetFeedbackVisual(string message, Color backgroundColor)
        {
            EnsureUserFeedbackHud();
            if (feedbackRoot == null || feedbackBackground == null || feedbackText == null)
            {
                return;
            }

            feedbackBackground.color = backgroundColor;
            feedbackText.text = message;
            feedbackRoot.SetActive(true);
        }

        private IEnumerator HideFeedbackRoutine()
        {
            yield return new WaitForSecondsRealtime(feedbackDuration);
            feedbackRoutine = null;

            if (IsRecording)
            {
                feedbackShowsRecording = true;
                SetFeedbackVisual(
                    $"● REC  {Time.unscaledTime - recordingStartedAt:00.0}s",
                    new Color(0.72f, 0.06f, 0.10f, 0.94f));
            }
            else if (feedbackRoot != null)
            {
                feedbackShowsRecording = false;
                feedbackRoot.SetActive(false);
            }
        }

        private void StopFeedbackRoutine()
        {
            if (feedbackRoutine == null)
            {
                return;
            }

            StopCoroutine(feedbackRoutine);
            feedbackRoutine = null;
        }

        private void CreateCameraBackground()
        {
            GameObject canvasObject = new GameObject(
                "RGB Camera Background",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler));
            canvasObject.layer = CameraBackgroundLayer;
            canvasObject.transform.SetParent(captureRoot.transform, false);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = backgroundCamera;
            canvas.planeDistance = 1f;
            canvas.sortingOrder = -1000;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);

            GameObject imageObject = new GameObject(
                "RayNeo RGB Image",
                typeof(RectTransform),
                typeof(RawImage));
            imageObject.layer = CameraBackgroundLayer;
            imageObject.transform.SetParent(canvasObject.transform, false);
            cameraImage = imageObject.GetComponent<RawImage>();
            cameraImage.color = Color.white;
            Stretch(imageObject.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            root.layer = layer;
            foreach (Transform child in root.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }

        private void ApplyPhysicalCameraProjection(Camera camera)
        {
            Vector4 intrinsics = RayNeoInfo.GetPhysicalCameraParams();
            float baseWidth = 640f;
            float baseHeight = 480f;
            float scaleX = rayNeoCameraResolution.width / baseWidth;
            float scaleY = rayNeoCameraResolution.height / baseHeight;
            float fx = intrinsics.x * scaleX;
            float fy = intrinsics.y * scaleY;
            float cx = intrinsics.z * scaleX;
            float cy = intrinsics.w * scaleY;
            camera.projectionMatrix = PerspectiveOffCenter(
                fx,
                fy,
                cx,
                cy,
                rayNeoCameraResolution.width,
                rayNeoCameraResolution.height,
                camera.nearClipPlane,
                camera.farClipPlane);
        }

        private static Matrix4x4 PerspectiveOffCenter(
            float fx,
            float fy,
            float cx,
            float cy,
            float width,
            float height,
            float near,
            float far)
        {
            Matrix4x4 matrix = new Matrix4x4();
            matrix[0] = 2f * fx / width;
            matrix[5] = 2f * fy / height;
            matrix[8] = (-2f * cx + width) / width;
            matrix[9] = (2f * cy - height) / height;
            matrix[10] = -(far + near) / (far - near);
            matrix[11] = -1f;
            matrix[14] = -2f * far * near / (far - near);
            return matrix;
        }

        private void SetPipelineActive(bool active)
        {
            if (backgroundCamera != null)
            {
                backgroundCamera.enabled = active;
            }

            if (compositeCamera != null)
            {
                compositeCamera.enabled = active;
            }
        }

        private void CloseRayNeoCamera()
        {
            if (rayNeoCamera == null)
            {
                return;
            }

            try
            {
                ShareCamera.CloseCamera(rayNeoCamera);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("RayNeo RGB camera close failed: " + exception.Message, this);
            }

            rayNeoCamera = null;
            if (cameraImage != null)
            {
                cameraImage.texture = null;
            }
        }

        private void HandleOriginReset()
        {
            if (IsRecording || startPending)
            {
                StopRecording();
            }

            StatusText = "PHOTO READY: HOLD / VIDEO AFTER ORIGIN";
        }

        private void ReleasePipeline()
        {
            if (backgroundCamera != null)
            {
                backgroundCamera.targetTexture = null;
            }

            if (compositeCamera != null)
            {
                compositeCamera.targetTexture = null;
            }

            ReleaseRenderTarget(ref realityTarget);
            ReleaseRenderTarget(ref compositeTarget);

            if (captureRoot != null)
            {
                Destroy(captureRoot);
                captureRoot = null;
            }
        }

        private static GameObject CreateUiObject(string name, Transform parent)
        {
            GameObject result = new GameObject(name, typeof(RectTransform));
            result.layer = 5;
            result.transform.SetParent(parent, false);
            return result;
        }

        private static Text CreateText(
            string name,
            Transform parent,
            Font font,
            int fontSize,
            FontStyle style,
            Color color)
        {
            GameObject textObject = CreateUiObject(name, parent);
            Text text = textObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
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
            Stretch(rect, left, bottom, right, top);
        }
    }
}
