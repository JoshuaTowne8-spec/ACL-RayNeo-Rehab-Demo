using RayNeo;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace ACLRehab.RayNeoDiagnostics
{
    [DefaultExecutionOrder(-100)]
    public sealed class RayNeoSpatialPoseProbe : MonoBehaviour
    {
        [SerializeField] private Transform trackedHead;
        [SerializeField] private Transform xrOrigin;
        [SerializeField] private RayNeoPlaneCoordinateProbe planeProbe;
        [SerializeField] private RayNeoDiagnosticRecorder recorder;
        [SerializeField, Min(0.01f)] private float positionJumpThreshold = 0.15f;
        [SerializeField, Min(0.1f)] private float angleJumpThreshold = 5f;

        private RayNeoInput rayNeoInput;
        private Pose previousRawPose;
        private Pose previousHeadWorldPose;
        private bool hasPreviousRawPose;
        private bool hasPreviousHeadWorldPose;
        private bool worldReferenceCreated;
        private float nextJumpLogTime;

        public bool HasRawPose { get; private set; }
        public Pose RawPose { get; private set; }
        public float RawPoseStep { get; private set; }
        public float RawPoseAngleStep { get; private set; }
        public Pose HeadLocalPose { get; private set; }
        public Pose HeadWorldPose { get; private set; }
        public float HeadWorldStep { get; private set; }
        public float HeadWorldAngleStep { get; private set; }
        public Pose XrOriginWorldPose { get; private set; }
        public bool HasXrDevicePose { get; private set; }
        public Pose XrDevicePose { get; private set; }
        public int NativeHeadPoseResult { get; private set; } = int.MinValue;
        public bool HasNativeHeadPose { get; private set; }
        public Pose NativeHeadPose { get; private set; }
        public float NativeHeadPoseStep { get; private set; }
        public float NativeHeadPoseAngleStep { get; private set; }
        public GameObject WorldReferenceRoot { get; private set; }

        public void Configure(
            Transform head,
            Transform origin,
            RayNeoPlaneCoordinateProbe planes,
            RayNeoDiagnosticRecorder diagnosticRecorder)
        {
            trackedHead = head;
            xrOrigin = origin;
            planeProbe = planes;
            recorder = diagnosticRecorder;
        }

        private void OnEnable()
        {
            HeadTrackedPoseDriver.OnPostUpdate += CaptureOfficialPoseCallback;
            rayNeoInput = new RayNeoInput();
            rayNeoInput.SimpleTouch.Enable();
        }

        private void OnDisable()
        {
            HeadTrackedPoseDriver.OnPostUpdate -= CaptureOfficialPoseCallback;
            if (rayNeoInput != null)
            {
                rayNeoInput.SimpleTouch.Disable();
                rayNeoInput.Dispose();
                rayNeoInput = null;
            }
        }

        private void Update()
        {
            bool snapshotPressed =
                Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
            if (rayNeoInput != null)
            {
                snapshotPressed |=
                    rayNeoInput.SimpleTouch.Tap.WasPerformedThisFrame();
            }

            if (snapshotPressed)
            {
                planeProbe?.CaptureSnapshot();
                recorder?.MarkEvent("RIGHT_TEMPLE_SNAPSHOT");
            }
        }

        private void LateUpdate()
        {
            if (trackedHead == null)
            {
                return;
            }

            HeadLocalPose = new Pose(
                trackedHead.localPosition,
                trackedHead.localRotation);
            HeadWorldPose = new Pose(
                trackedHead.position,
                trackedHead.rotation);
            XrOriginWorldPose = xrOrigin != null
                ? new Pose(xrOrigin.position, xrOrigin.rotation)
                : default;

            if (hasPreviousHeadWorldPose)
            {
                HeadWorldStep = Vector3.Distance(
                    previousHeadWorldPose.position,
                    HeadWorldPose.position);
                HeadWorldAngleStep = Quaternion.Angle(
                    previousHeadWorldPose.rotation,
                    HeadWorldPose.rotation);
            }
            else
            {
                HeadWorldStep = 0f;
                HeadWorldAngleStep = 0f;
                hasPreviousHeadWorldPose = true;
            }

            previousHeadWorldPose = HeadWorldPose;
            ReadUnityXrPose();

            if (!worldReferenceCreated &&
                planeProbe != null && planeProbe.IsTracking)
            {
                WorldReferenceRoot =
                    RayNeoDiagnosticVisuals.CreateWorldReference(
                        trackedHead.position,
                        trackedHead.forward);
                worldReferenceCreated = true;
                recorder?.MarkEvent("WORLD_REFERENCE_CREATED");
                Debug.Log(
                    "RAYNEO_DIAG world-locked reference created without parenting " +
                    "to the Head or XR Origin.",
                    this);
            }

            if ((HeadWorldStep > positionJumpThreshold ||
                 HeadWorldAngleStep > angleJumpThreshold) &&
                Time.unscaledTime >= nextJumpLogTime)
            {
                nextJumpLogTime = Time.unscaledTime + 0.25f;
                string eventName =
                    $"HEAD_WORLD_JUMP_dp={HeadWorldStep:F4}_dr={HeadWorldAngleStep:F2}";
                recorder?.MarkEvent(eventName);
                Debug.LogWarning("RAYNEO_DIAG " + eventName, this);
            }
        }

        private void OnDestroy()
        {
            if (WorldReferenceRoot != null)
            {
                Destroy(WorldReferenceRoot);
            }
        }

        private void CaptureOfficialPoseCallback(Pose pose)
        {
            RawPose = pose;
            HasRawPose = true;
            if (hasPreviousRawPose)
            {
                RawPoseStep = Vector3.Distance(
                    previousRawPose.position,
                    pose.position);
                RawPoseAngleStep = Quaternion.Angle(
                    previousRawPose.rotation,
                    pose.rotation);
            }
            else
            {
                RawPoseStep = 0f;
                RawPoseAngleStep = 0f;
                hasPreviousRawPose = true;
            }

            previousRawPose = pose;
        }

        private void ReadUnityXrPose()
        {
            UnityEngine.XR.InputDevice device =
                InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
            Vector3 position = Vector3.zero;
            Quaternion rotation = Quaternion.identity;
            bool hasPosition = device.isValid &&
                               device.TryGetFeatureValue(
                                   UnityEngine.XR.CommonUsages.centerEyePosition,
                                   out position);
            bool hasRotation = device.isValid &&
                               device.TryGetFeatureValue(
                                   UnityEngine.XR.CommonUsages.centerEyeRotation,
                                   out rotation);
            HasXrDevicePose = hasPosition && hasRotation;
            XrDevicePose = HasXrDevicePose
                ? new Pose(position, rotation)
                : default;
        }

    }
}
