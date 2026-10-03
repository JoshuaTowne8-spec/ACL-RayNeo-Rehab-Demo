using System;
using System.Collections.Generic;
using com.rayneo.xr.extensions;
using RayNeo.API;
using UnityEngine;

namespace ACLRehab.RayNeoSpatial
{
    [DefaultExecutionOrder(-200)]
    public sealed class RayNeoSpatialBootstrap : MonoBehaviour
    {
        [Serializable]
        public readonly struct PlaneSnapshot
        {
            public readonly XRPlaneProperty Property;
            public readonly Pose Pose;
            public readonly Vector2[] Boundary;
            public readonly float Area;

            public PlaneSnapshot(XRPlaneInfo source)
            {
                Property = source.property;
                Pose = new Pose(
                    Algorithm.ConvertPlanePosition(source),
                    Algorithm.ConvertPlaneRotation(source));

                int count = source.local_polygon == null
                    ? 0
                    : Mathf.Min(source.local_polygon_size, source.local_polygon.Length / 2);
                Boundary = new Vector2[count];

                float twiceArea = 0f;
                for (int i = 0; i < count; i++)
                {
                    Boundary[i] = new Vector2(
                        source.local_polygon[i * 2],
                        source.local_polygon[i * 2 + 1]);
                }

                for (int i = 0; i < count; i++)
                {
                    Vector2 a = Boundary[i];
                    Vector2 b = Boundary[(i + 1) % count];
                    twiceArea += a.x * b.y - b.x * a.y;
                }

                Area = Mathf.Abs(twiceArea) * 0.5f;
            }
        }

        [Header("RayNeo polling")]
        [SerializeField, Range(1, 32)] private int maxPlaneCount = 16;
        [SerializeField, Min(0.05f)] private float pollInterval = 0.2f;
        [SerializeField] private bool simulateGroundInEditor = true;

        private XRPlaneInfo[] nativePlanes;
        private readonly List<PlaneSnapshot> planes = new List<PlaneSnapshot>();
        private float nextPollTime;
        private bool runtimeStarted;
        private bool loggedRuntimeError;

        public event Action<IReadOnlyList<PlaneSnapshot>> PlanesUpdated;
        public event Action<Algorithm.SlamState> SlamStateChanged;

        public IReadOnlyList<PlaneSnapshot> Planes => planes;
        public int PlaneCount => planes.Count;
        public Algorithm.SlamState SlamState { get; private set; } =
            Algorithm.SlamState.FFVINS_INITIALIZING;
        public bool IsTracking => SlamState == Algorithm.SlamState.FFVINS_TRACKING_SUCCESS;

        private void OnEnable()
        {
            AllocatePlaneBuffer();

#if UNITY_ANDROID && !UNITY_EDITOR
            Algorithm.EnableSlamHeadTracker();
            Algorithm.EnablePlaneDetection();
            runtimeStarted = true;
#elif UNITY_EDITOR
            runtimeStarted = simulateGroundInEditor;
#endif
        }

        private void OnDisable()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (runtimeStarted)
            {
                Algorithm.DisablePlaneDetection();
                Algorithm.DisableSlamHeadTracker();
            }
#endif
            runtimeStarted = false;
        }

        private void Update()
        {
            if (!runtimeStarted || Time.unscaledTime < nextPollTime)
            {
                return;
            }

            nextPollTime = Time.unscaledTime + pollInterval;
#if UNITY_ANDROID && !UNITY_EDITOR
            PollRayNeoPlanes();
#elif UNITY_EDITOR
            PollEditorGround();
#endif
        }

        private void AllocatePlaneBuffer()
        {
            nativePlanes = new XRPlaneInfo[Mathf.Max(1, maxPlaneCount)];
            for (int i = 0; i < nativePlanes.Length; i++)
            {
                nativePlanes[i].local_polygon = new float[100];
            }
        }

        private void PollRayNeoPlanes()
        {
            try
            {
                SetSlamState(Algorithm.GetSlamStatus());
                if (!IsTracking)
                {
                    ClearPlanes();
                    return;
                }

                int count = Mathf.Clamp(Algorithm.GetPlaneInfo(nativePlanes), 0, nativePlanes.Length);
                planes.Clear();

                for (int i = 0; i < count; i++)
                {
                    if (nativePlanes[i].local_polygon_size >= 3)
                    {
                        planes.Add(new PlaneSnapshot(nativePlanes[i]));
                    }
                }

                PlanesUpdated?.Invoke(planes);
                loggedRuntimeError = false;
            }
            catch (Exception exception)
            {
                if (!loggedRuntimeError)
                {
                    Debug.LogError($"RayNeo SLAM/plane polling failed: {exception}", this);
                    loggedRuntimeError = true;
                }
            }
        }

#if UNITY_EDITOR
        private void PollEditorGround()
        {
            SetSlamState(Algorithm.SlamState.FFVINS_TRACKING_SUCCESS);

            XRPlaneInfo editorGround = new XRPlaneInfo
            {
                property = XRPlaneProperty.PLANE_HORIZONTAL_UP,
                local_polygon = new[]
                {
                    -10f, -10f,
                    -10f, 10f,
                    10f, 10f,
                    10f, -10f
                },
                local_polygon_size = 4
            };
            editorGround.pose.rotation.w = 1f;

            planes.Clear();
            planes.Add(new PlaneSnapshot(editorGround));
            PlanesUpdated?.Invoke(planes);
        }
#endif

        private void SetSlamState(Algorithm.SlamState state)
        {
            if (SlamState == state)
            {
                return;
            }

            SlamState = state;
            SlamStateChanged?.Invoke(state);
        }

        private void ClearPlanes()
        {
            if (planes.Count == 0)
            {
                return;
            }

            planes.Clear();
            PlanesUpdated?.Invoke(planes);
        }
    }
}
