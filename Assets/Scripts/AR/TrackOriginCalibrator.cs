using System;
using RayNeo;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ACLRehab.RayNeoSpatial
{
    public sealed class TrackOriginCalibrator : MonoBehaviour
    {
        [SerializeField] private GroundPlaneSelector groundSelector;
        [SerializeField] private Transform trackedHead;
        [SerializeField] private Transform trackOrigin;
        [SerializeField] private bool autoCalibrate;
        [SerializeField, Range(2, 60)] private int stableSamplesRequired = 10;
        [SerializeField, Min(0.001f)] private float maximumPositionDelta = 0.05f;
        [SerializeField, Min(0.1f)] private float maximumAngleDelta = 3f;
        [SerializeField, Min(0.5f)] private float resetHoldSeconds = 2f;
        [SerializeField, Range(0.2f, 0.6f)] private float templeDoubleTapWindow = 0.35f;
        [SerializeField, Range(0.5f, 2f)] private float templeLongPressSeconds = 0.9f;
        [SerializeField, Min(0.5f)] private float defaultStandingHeadHeight = 1.6f;
        [SerializeField, Min(0.5f)] private float minimumStandingHeadHeight = 1.2f;
        [SerializeField, Min(0.6f)] private float maximumStandingHeadHeight = 2.1f;

        private Pose previousGroundPose;
        private int stableSamples;
        private bool hasPreviousGroundPose;
        private RayNeoInput rayNeoInput;
        private float resetHoldDuration;
        private bool waitForConfirmRelease;
        private bool templePressWasHeld;
        private bool templeLongPressInvoked;
        private bool suppressNextTempleTap;
        private int pendingTempleTapCount;
        private float templePressStartedAt;
        private float firstTempleTapTime;
        private Vector3 previousObservedHeadPosition;
        private bool hasPreviousObservedHeadPosition;

        public event Action<Transform> OriginCalibrated;
        public event Action OriginReset;
        public event Action<bool> ReadinessChanged;
        public event Action PostCalibrationTempleTapped;
        public event Action PostCalibrationTempleDoubleTapped;
        public event Action PostCalibrationTempleLongPressed;

        public bool IsCalibrated { get; private set; }
        public bool IsGroundStable { get; private set; }
        public int StableSampleCount => stableSamples;
        public int StableSamplesRequired => stableSamplesRequired;
        public Transform TrackOrigin => trackOrigin;
        public float EffectiveTranslationScale { get; private set; } = 1f;
        public float RawHeadFrameStep { get; private set; }
        public float MetricHeadFrameStep { get; private set; }
        public float CalibratedGroundDistance { get; private set; }

        public void Configure(
            GroundPlaneSelector selector,
            Transform head,
            Transform origin)
        {
            groundSelector = selector;
            trackedHead = head;
            trackOrigin = origin;
        }

        private void OnEnable()
        {
            if (groundSelector != null)
            {
                groundSelector.GroundChanged += ObserveGround;
                groundSelector.GroundLost += HandleGroundLost;
            }

            rayNeoInput = new RayNeoInput();
            rayNeoInput.SimpleTouch.Enable();
            rayNeoInput.Ring.Enable();
            HeadTrackedPoseDriver.OnPostUpdate += ObserveRayNeoHeadPose;
        }

        private void Start()
        {
            if (groundSelector != null)
            {
                groundSelector.GroundChanged -= ObserveGround;
                groundSelector.GroundChanged += ObserveGround;
                groundSelector.GroundLost -= HandleGroundLost;
                groundSelector.GroundLost += HandleGroundLost;
            }
        }

        private void OnDisable()
        {
            HeadTrackedPoseDriver.OnPostUpdate -= ObserveRayNeoHeadPose;

            if (groundSelector != null)
            {
                groundSelector.GroundChanged -= ObserveGround;
                groundSelector.GroundLost -= HandleGroundLost;
            }

            if (rayNeoInput != null)
            {
                rayNeoInput.SimpleTouch.Disable();
                rayNeoInput.Ring.Disable();
                rayNeoInput.Dispose();
                rayNeoInput = null;
            }
        }

        private void Update()
        {
            bool confirmPressed = Keyboard.current != null &&
                                  Keyboard.current.rKey.wasPressedThisFrame;
            bool resetPressed = Keyboard.current != null &&
                                Keyboard.current.backspaceKey.wasPressedThisFrame;
            bool debugRecordPressed = Keyboard.current != null &&
                                      Keyboard.current.vKey.wasPressedThisFrame;
            bool debugPhotoPressed = Keyboard.current != null &&
                                     Keyboard.current.pKey.wasPressedThisFrame;

            if (rayNeoInput != null)
            {
                bool templeTapped =
                    rayNeoInput.SimpleTouch.Tap.WasPerformedThisFrame();
                bool templePressed = rayNeoInput.SimpleTouch.Press.IsPressed();
                bool ringHeld = rayNeoInput.Ring.HeavyClick.IsPressed();

                // Long-press photography is available both before and after
                // origin calibration. The release tap is suppressed below so
                // taking a pre-calibration photo cannot confirm the origin.
                UpdatePostCalibrationTemplePress(templePressed);

                if (templeTapped)
                {
                    Debug.Log(
                        $"RayNeo right-temple tap received. calibrated={IsCalibrated}, " +
                        $"hasGround={groundSelector != null && groundSelector.HasGround}, " +
                        $"stable={IsGroundStable}, samples={stableSamples}/{stableSamplesRequired}.",
                        this);

                    if (suppressNextTempleTap)
                    {
                        suppressNextTempleTap = false;
                    }
                    else if (IsCalibrated)
                    {
                        RegisterPostCalibrationTempleTap();
                    }
                    else
                    {
                        confirmPressed = true;
                    }
                }

                if (waitForConfirmRelease)
                {
                    if (!ringHeld)
                    {
                        waitForConfirmRelease = false;
                    }
                }
                else if (IsCalibrated && ringHeld)
                {
                    resetHoldDuration += Time.unscaledDeltaTime;
                    if (resetHoldDuration >= resetHoldSeconds)
                    {
                        resetPressed = true;
                        waitForConfirmRelease = true;
                        resetHoldDuration = 0f;
                    }
                }
                else
                {
                    resetHoldDuration = 0f;
                }
            }

            if (IsCalibrated)
            {
                if (debugRecordPressed)
                {
                    pendingTempleTapCount = 0;
                    PostCalibrationTempleDoubleTapped?.Invoke();
                }

                if (debugPhotoPressed)
                {
                    pendingTempleTapCount = 0;
                    PostCalibrationTempleLongPressed?.Invoke();
                }

                ReleasePendingTempleSingleTap();
            }

            if (resetPressed && IsCalibrated)
            {
                ResetCalibration();
            }
            else if (confirmPressed && !IsCalibrated)
            {
                if (IsGroundStable)
                {
                    ConfirmCurrentGroundAsOrigin();
                }
                else
                {
                    Debug.LogWarning(
                        $"Origin confirmation ignored because the ground is not stable. " +
                        $"hasGround={groundSelector != null && groundSelector.HasGround}, " +
                        $"samples={stableSamples}/{stableSamplesRequired}.",
                        this);
                }
            }
        }

        private void RegisterPostCalibrationTempleTap()
        {
            if (suppressNextTempleTap)
            {
                suppressNextTempleTap = false;
                return;
            }

            if (pendingTempleTapCount == 1 &&
                Time.unscaledTime - firstTempleTapTime <= templeDoubleTapWindow)
            {
                pendingTempleTapCount = 0;
                PostCalibrationTempleDoubleTapped?.Invoke();
                return;
            }

            pendingTempleTapCount = 1;
            firstTempleTapTime = Time.unscaledTime;
        }

        private void ReleasePendingTempleSingleTap()
        {
            if (pendingTempleTapCount != 1 ||
                Time.unscaledTime - firstTempleTapTime < templeDoubleTapWindow)
            {
                return;
            }

            pendingTempleTapCount = 0;
            PostCalibrationTempleTapped?.Invoke();
        }

        private void UpdatePostCalibrationTemplePress(bool pressed)
        {
            if (pressed && !templePressWasHeld)
            {
                templePressStartedAt = Time.unscaledTime;
                templeLongPressInvoked = false;
            }

            if (pressed && !templeLongPressInvoked &&
                Time.unscaledTime - templePressStartedAt >= templeLongPressSeconds)
            {
                pendingTempleTapCount = 0;
                templeLongPressInvoked = true;
                suppressNextTempleTap = true;
                PostCalibrationTempleLongPressed?.Invoke();
            }

            if (!pressed && templePressWasHeld)
            {
                templeLongPressInvoked = false;
            }

            templePressWasHeld = pressed;
        }

        private void ObserveRayNeoHeadPose(Pose officialPose)
        {
            if (!hasPreviousObservedHeadPosition)
            {
                previousObservedHeadPosition = officialPose.position;
                hasPreviousObservedHeadPosition = true;
                RawHeadFrameStep = 0f;
                MetricHeadFrameStep = 0f;
                return;
            }

            Vector3 officialDelta = officialPose.position - previousObservedHeadPosition;
            previousObservedHeadPosition = officialPose.position;
            RawHeadFrameStep = officialDelta.magnitude;
            MetricHeadFrameStep = RawHeadFrameStep;

            // The X3 Pro SLAM pose is already expressed in Unity metres. This
            // observer deliberately never writes trackedHead: the official
            // HeadTrackedPoseDriver remains the sole owner of the camera pose.
            EffectiveTranslationScale = 1f;
        }

        private void ObserveGround(RayNeoSpatialBootstrap.PlaneSnapshot ground)
        {
            if (IsCalibrated || groundSelector == null || !groundSelector.HasGround)
            {
                return;
            }

            Pose observedPose = groundSelector.SelectedGroundWorldPose;

            if (!hasPreviousGroundPose)
            {
                stableSamples = 1;
                hasPreviousGroundPose = true;
            }
            else
            {
                Vector3 previousNormal = GetUpFacingNormal(
                    previousGroundPose.rotation * Vector3.up);
                Vector3 observedNormal = GetUpFacingNormal(
                    observedPose.rotation * Vector3.up);
                Vector3 comparisonNormal =
                    (previousNormal + observedNormal).normalized;
                float planeOffsetDelta = Mathf.Abs(Vector3.Dot(
                    observedPose.position - previousGroundPose.position,
                    comparisonNormal));
                float angleDelta = Vector3.Angle(
                    previousNormal,
                    observedNormal);
                stableSamples = planeOffsetDelta <= maximumPositionDelta &&
                                angleDelta <= maximumAngleDelta
                    ? Mathf.Min(stableSamples + 1, stableSamplesRequired)
                    : 1;
            }

            previousGroundPose = observedPose;
            SetGroundStable(stableSamples >= stableSamplesRequired);

            if (autoCalibrate && IsGroundStable)
            {
                ConfirmCurrentGroundAsOrigin();
            }
        }

        private void HandleGroundLost()
        {
            if (!IsCalibrated)
            {
                ResetStability();
            }
        }

        public void ConfirmCurrentGroundAsOrigin()
        {
            if (groundSelector == null || !groundSelector.HasGround ||
                !IsGroundStable || trackedHead == null || trackOrigin == null)
            {
                Debug.LogWarning(
                    $"Origin confirmation rejected. selector={groundSelector != null}, " +
                    $"hasGround={groundSelector != null && groundSelector.HasGround}, " +
                    $"stable={IsGroundStable}, trackedHead={trackedHead != null}, " +
                    $"trackOrigin={trackOrigin != null}.",
                    this);
                return;
            }

            // The manual-start workflow needs a level running surface. RayNeo's
            // plane pose rotation can be reported in a different basis on this
            // runtime, so use the accepted head-to-ground distance but keep the
            // training frame aligned with Unity/OpenXR world up.
            Vector3 up = Vector3.up;
            float detectedGroundDistance = groundSelector.CandidateHeadDistance;
            bool plausibleStandingHeight =
                !float.IsNaN(detectedGroundDistance) &&
                !float.IsInfinity(detectedGroundDistance) &&
                detectedGroundDistance >= minimumStandingHeadHeight &&
                detectedGroundDistance <= maximumStandingHeadHeight;
            CalibratedGroundDistance = plausibleStandingHeight
                ? detectedGroundDistance
                : defaultStandingHeadHeight;

            Vector3 originPosition =
                trackedHead.position - up * CalibratedGroundDistance;
            Vector3 forward = Vector3.ProjectOnPlane(trackedHead.forward, up).normalized;
            if (forward.sqrMagnitude < 0.01f)
            {
                forward = Vector3.forward;
            }

            trackOrigin.SetPositionAndRotation(
                originPosition,
                Quaternion.LookRotation(forward, up));

            RawHeadFrameStep = 0f;
            MetricHeadFrameStep = 0f;

            IsCalibrated = true;
            OriginCalibrated?.Invoke(trackOrigin);
            Debug.Log(
                "Track origin calibrated from the RayNeo head pose and selected ground. " +
                "The current facing direction is runway forward.",
                this);
        }

        public void ResetCalibration()
        {
            bool wasCalibrated = IsCalibrated;
            IsCalibrated = false;
            RawHeadFrameStep = 0f;
            MetricHeadFrameStep = 0f;
            CalibratedGroundDistance = 0f;
            pendingTempleTapCount = 0;
            templePressWasHeld = false;
            templeLongPressInvoked = false;
            suppressNextTempleTap = false;
            hasPreviousObservedHeadPosition = false;
            previousObservedHeadPosition = Vector3.zero;
            ResetStability();
            groundSelector?.ClearCandidates();
            if (wasCalibrated)
            {
                OriginReset?.Invoke();
                Debug.Log("Track origin reset. Scan the ground and confirm again.", this);
            }
        }

        private void ResetStability()
        {
            stableSamples = 0;
            hasPreviousGroundPose = false;
            SetGroundStable(false);
        }

        private static Vector3 GetUpFacingNormal(Vector3 normal)
        {
            if (normal.sqrMagnitude < 0.0001f)
            {
                return Vector3.up;
            }

            normal.Normalize();
            return Vector3.Dot(normal, Vector3.up) < 0f
                ? -normal
                : normal;
        }

        private void SetGroundStable(bool value)
        {
            if (IsGroundStable == value)
            {
                return;
            }

            IsGroundStable = value;
            ReadinessChanged?.Invoke(value);
        }
    }
}
