using System;
using ACLRehab.Running;
using RayNeo.API;
using UnityEngine;

namespace ACLRehab.RayNeoSpatial
{
    public enum RunningSpatialSessionState
    {
        InitializingSlam,
        ScanningGround,
        ReadyToConfirm,
        Running,
        TrackingLost
    }

    public sealed class RunningSpatialSessionController : MonoBehaviour
    {
        [SerializeField] private RayNeoSpatialBootstrap spatialBootstrap;
        [SerializeField] private GroundPlaneSelector groundSelector;
        [SerializeField] private TrackOriginCalibrator originCalibrator;
        [SerializeField] private GroundPlanePreview groundPreview;
        [SerializeField] private GameObject routeSystem;
        [SerializeField] private TrackRouteController routeController;
        [SerializeField, Min(0.5f)] private float trackingLossGraceSeconds = 5f;

        private bool hadSuccessfulTracking;
        private bool trackingLossPending;
        private float trackingLossStartedAt;

        public event Action<RunningSpatialSessionState> StateChanged;

        public RunningSpatialSessionState State { get; private set; } =
            RunningSpatialSessionState.InitializingSlam;
        public bool IsRouteActive =>
            routeSystem != null && routeSystem.activeInHierarchy;
        public float RoutePlayerProgress =>
            routeController != null ? routeController.playerProgress : 0f;
        public bool IsTrackingLossPending => trackingLossPending;
        public float TrackingLossElapsed => trackingLossPending
            ? Mathf.Max(0f, Time.unscaledTime - trackingLossStartedAt)
            : 0f;
        public float TrackingLossGraceSeconds => trackingLossGraceSeconds;

        public string StatusMessage
        {
            get
            {
                switch (State)
                {
                    case RunningSpatialSessionState.ScanningGround:
                        return "SCAN FLOOR / CHECK GRID: X RED, Y BLUE";
                    case RunningSpatialSessionState.ReadyToConfirm:
                        return "LOOK AT CORRECT GRID / TAP RIGHT TEMPLE";
                    case RunningSpatialSessionState.Running:
                        return "ORIGIN LOCKED / TRAINING READY";
                    case RunningSpatialSessionState.TrackingLost:
                        return "TRACKING LOST / STOP AND RESCAN";
                    default:
                        return "INITIALIZING SLAM";
                }
            }
        }

        public void Configure(
            RayNeoSpatialBootstrap bootstrap,
            GroundPlaneSelector selector,
            TrackOriginCalibrator calibrator,
            GroundPlanePreview preview,
            GameObject runningRouteSystem,
            TrackRouteController runningRouteController)
        {
            spatialBootstrap = bootstrap;
            groundSelector = selector;
            originCalibrator = calibrator;
            groundPreview = preview;
            routeSystem = runningRouteSystem;
            routeController = runningRouteController;
        }

        private void OnEnable()
        {
            if (spatialBootstrap != null)
            {
                spatialBootstrap.SlamStateChanged += HandleSlamStateChanged;
            }

            if (originCalibrator != null)
            {
                originCalibrator.OriginCalibrated += HandleOriginCalibrated;
                originCalibrator.OriginReset += HandleOriginReset;
                originCalibrator.ReadinessChanged += HandleReadinessChanged;
            }
        }

        private void Start()
        {
            SetRouteActive(false);
            SetGroundPreviewActive(true);
            RefreshState();
        }

        private void OnDisable()
        {
            if (spatialBootstrap != null)
            {
                spatialBootstrap.SlamStateChanged -= HandleSlamStateChanged;
            }

            if (originCalibrator != null)
            {
                originCalibrator.OriginCalibrated -= HandleOriginCalibrated;
                originCalibrator.OriginReset -= HandleOriginReset;
                originCalibrator.ReadinessChanged -= HandleReadinessChanged;
            }
        }

        private void Update()
        {
            if (spatialBootstrap == null)
            {
                return;
            }

            if (spatialBootstrap.IsTracking ||
                spatialBootstrap.SlamState == Algorithm.SlamState.FFVINS_INITIALIZING)
            {
                CancelPendingTrackingLoss();
                return;
            }

            if (State == RunningSpatialSessionState.Running)
            {
                BeginPendingTrackingLoss();
                if (TrackingLossElapsed >= trackingLossGraceSeconds)
                {
                    HandleTrackingLost();
                }
            }
        }

        private void HandleSlamStateChanged(Algorithm.SlamState state)
        {
            if (state == Algorithm.SlamState.FFVINS_TRACKING_SUCCESS)
            {
                hadSuccessfulTracking = true;
                CancelPendingTrackingLoss();
                RefreshState();
                return;
            }

            if (state == Algorithm.SlamState.FFVINS_TRACKING_FAIL &&
                hadSuccessfulTracking &&
                State == RunningSpatialSessionState.Running)
            {
                BeginPendingTrackingLoss();
            }
            else if (!hadSuccessfulTracking)
            {
                SetState(RunningSpatialSessionState.InitializingSlam);
            }
        }

        private void HandleOriginCalibrated(Transform calibratedOrigin)
        {
            CancelPendingTrackingLoss();
            if (routeSystem != null && calibratedOrigin != null)
            {
                routeSystem.transform.SetPositionAndRotation(
                    calibratedOrigin.position,
                    calibratedOrigin.rotation);
            }

            SetRouteActive(true);
            if (routeController != null)
            {
                routeController.ResetRoute();
            }

            // Stop listening for later plane updates while training is active.
            // Hiding the renderer alone is not enough because GroundChanged can
            // otherwise make the green preview mesh visible again next frame.
            SetGroundPreviewActive(false);

            Debug.Log(
                $"Running route aligned to calibrated origin. " +
                $"origin={calibratedOrigin.position:F3}, " +
                $"routeRoot={(routeSystem != null ? routeSystem.transform.position.ToString("F3") : "missing")}",
                this);

            SetState(RunningSpatialSessionState.Running);
        }

        private void HandleOriginReset()
        {
            CancelPendingTrackingLoss();
            SetRouteActive(false);
            SetGroundPreviewActive(true);
            groundPreview?.SetVisible(groundSelector != null && groundSelector.HasGround);

            RefreshState();
        }

        private void HandleReadinessChanged(bool ready)
        {
            RefreshState();
        }

        private void HandleTrackingLost()
        {
            CancelPendingTrackingLoss();
            SetRouteActive(false);
            if (originCalibrator != null)
            {
                // Reset the metric Head origin even when calibration has not
                // completed. A recovered SLAM session may use a new raw map
                // origin and must not be integrated from the old one.
                originCalibrator.ResetCalibration();
            }

            if (groundSelector != null)
            {
                // A recovered SLAM session can establish a new tracking
                // origin. Discard cached plane identities and any locked
                // head-relative fallback before scanning again.
                groundSelector.ClearCandidates();
            }

            if (groundPreview != null)
            {
                SetGroundPreviewActive(false);
            }

            SetState(RunningSpatialSessionState.TrackingLost);
        }

        private void BeginPendingTrackingLoss()
        {
            if (trackingLossPending)
            {
                return;
            }

            trackingLossPending = true;
            trackingLossStartedAt = Time.unscaledTime;
            Debug.LogWarning(
                $"RayNeo tracking loss detected; waiting " +
                $"{trackingLossGraceSeconds:F1}s before clearing the calibrated route.",
                this);
        }

        private void CancelPendingTrackingLoss()
        {
            trackingLossPending = false;
            trackingLossStartedAt = 0f;
        }

        private void RefreshState()
        {
            if (originCalibrator != null && originCalibrator.IsCalibrated)
            {
                SetState(RunningSpatialSessionState.Running);
            }
            else if (spatialBootstrap == null || !spatialBootstrap.IsTracking)
            {
                SetState(hadSuccessfulTracking
                    ? RunningSpatialSessionState.TrackingLost
                    : RunningSpatialSessionState.InitializingSlam);
            }
            else if (originCalibrator != null && originCalibrator.IsGroundStable)
            {
                SetState(RunningSpatialSessionState.ReadyToConfirm);
            }
            else
            {
                SetState(RunningSpatialSessionState.ScanningGround);
            }
        }

        private void SetRouteActive(bool active)
        {
            if (routeSystem != null && routeSystem.activeSelf != active)
            {
                routeSystem.SetActive(active);
            }
        }

        private void SetGroundPreviewActive(bool active)
        {
            if (groundPreview == null)
            {
                return;
            }

            if (!active)
            {
                groundPreview.SetVisible(false);
            }

            groundPreview.enabled = active;
        }

        private void SetState(RunningSpatialSessionState state)
        {
            if (State == state)
            {
                return;
            }

            State = state;
            StateChanged?.Invoke(state);
            Debug.Log("RayNeo running spatial state: " + state, this);
        }
    }
}
