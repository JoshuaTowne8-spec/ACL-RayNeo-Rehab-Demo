using System;
using System.Collections.Generic;
using com.rayneo.xr.extensions;
using RayNeo.API;
using UnityEngine;

namespace ACLRehab.RayNeoDiagnostics
{
    [DefaultExecutionOrder(-200)]
    public sealed class RayNeoPlaneCoordinateProbe : MonoBehaviour
    {
        [Serializable]
        public sealed class PlaneObservation
        {
            public int Index;
            public XRPlaneProperty Property;
            public ulong Timestamp;
            public Pose RawConvertedPose;
            public float Area;
            public int PolygonCount;
            public Vector2 LocalRange;
            public Pose OfficialDirectWorldPose;
            public Pose OriginChildWorldPose;
            public Pose ExplicitWorldPose;
            public float RawPositionStep;
            public float RawAngleStep;
        }

        private sealed class PlaneVisualSet
        {
            public GameObject OfficialDirect;
            public GameObject OriginChild;
            public GameObject ExplicitWorld;
        }

        [SerializeField] private Transform trackedHead;
        [SerializeField] private Transform xrOrigin;
        [SerializeField, Range(1, 32)] private int maxPlaneCount = 16;
        [SerializeField, Min(0.05f)] private float pollInterval = 0.2f;

        private XRPlaneInfo[] planeBuffer;
        private PlaneVisualSet[] visualSets;
        private readonly List<PlaneObservation> observations =
            new List<PlaneObservation>();
        private readonly List<Pose> previousRawPoses = new List<Pose>();
        private readonly List<bool> previousPoseValid = new List<bool>();
        private Material officialMaterial;
        private Material originChildMaterial;
        private Material explicitWorldMaterial;
        private GameObject liveRoot;
        private GameObject snapshotRoot;
        private float nextPollTime;
        private bool runtimeStarted;

        public Algorithm.SlamState SlamState { get; private set; } =
            Algorithm.SlamState.FFVINS_INITIALIZING;
        public bool IsTracking => SlamState ==
                                  Algorithm.SlamState.FFVINS_TRACKING_SUCCESS;
        public int RawPlaneCount { get; private set; }
        public int ObservationCount => observations.Count;
        public IReadOnlyList<PlaneObservation> Observations => observations;
        public int SnapshotSerial { get; private set; }
        public float LastPollRealtime { get; private set; }

        public void Configure(Transform head, Transform origin)
        {
            trackedHead = head;
            xrOrigin = origin;
        }

        private void Awake()
        {
            AllocateBuffers();
            CreateMaterials();
            liveRoot = new GameObject("LIVE PLANES - UPDATED BY SDK");
        }

        private void OnEnable()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            Algorithm.EnableSlamHeadTracker();
            Algorithm.EnablePlaneDetection();
            runtimeStarted = true;
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
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!runtimeStarted || Time.unscaledTime < nextPollTime)
            {
                return;
            }

            nextPollTime = Time.unscaledTime + pollInterval;
            PollPlanes();
#endif
        }

        private void OnDestroy()
        {
            if (liveRoot != null)
            {
                Destroy(liveRoot);
            }

            if (snapshotRoot != null)
            {
                Destroy(snapshotRoot);
            }

            DestroyMaterial(officialMaterial);
            DestroyMaterial(originChildMaterial);
            DestroyMaterial(explicitWorldMaterial);
        }

        private void AllocateBuffers()
        {
            int count = Mathf.Max(1, maxPlaneCount);
            planeBuffer = new XRPlaneInfo[count];
            visualSets = new PlaneVisualSet[count];
            for (int i = 0; i < count; i++)
            {
                planeBuffer[i].local_polygon = new float[100];
                previousRawPoses.Add(default);
                previousPoseValid.Add(false);
            }
        }

        private void CreateMaterials()
        {
            officialMaterial = RayNeoDiagnosticVisuals.CreateTransparentMaterial(
                "Diagnostic Official Direct Green",
                new Color(0.12f, 1f, 0.22f, 0.28f),
                3000);
            originChildMaterial = RayNeoDiagnosticVisuals.CreateTransparentMaterial(
                "Diagnostic XR Origin Child Blue",
                new Color(0.08f, 0.4f, 1f, 0.28f),
                3001);
            explicitWorldMaterial = RayNeoDiagnosticVisuals.CreateTransparentMaterial(
                "Diagnostic Explicit World Yellow",
                new Color(1f, 0.82f, 0.05f, 0.28f),
                3002);
        }

        private void PollPlanes()
        {
            LastPollRealtime = Time.unscaledTime;
            SlamState = Algorithm.GetSlamStatus();
            if (!IsTracking)
            {
                RawPlaneCount = 0;
                observations.Clear();
                HideUnusedVisuals(0);
                return;
            }

            int returnedCount = Algorithm.GetPlaneInfo(planeBuffer);
            RawPlaneCount = Mathf.Clamp(returnedCount, 0, planeBuffer.Length);
            observations.Clear();

            for (int i = 0; i < RawPlaneCount; i++)
            {
                XRPlaneInfo info = planeBuffer[i];
                if (info.local_polygon == null || info.local_polygon_size < 3)
                {
                    SetVisualActive(i, false);
                    continue;
                }

                PlaneVisualSet visuals = EnsureVisualSet(i);
                Pose convertedPose = new Pose(
                    Algorithm.ConvertPlanePosition(info),
                    Algorithm.ConvertPlaneRotation(info));

                Algorithm.CreatePlaneMesh(
                    info,
                    visuals.OfficialDirect,
                    false,
                    officialMaterial);
                visuals.OfficialDirect.transform.SetParent(liveRoot.transform, false);
                visuals.OfficialDirect.transform.SetPositionAndRotation(
                    convertedPose.position,
                    convertedPose.rotation);

                Algorithm.CreatePlaneMesh(
                    info,
                    visuals.OriginChild,
                    false,
                    originChildMaterial);
                visuals.OriginChild.transform.SetParent(
                    xrOrigin != null ? xrOrigin : liveRoot.transform,
                    false);
                visuals.OriginChild.transform.localPosition = convertedPose.position;
                visuals.OriginChild.transform.localRotation = convertedPose.rotation;

                Algorithm.CreatePlaneMesh(
                    info,
                    visuals.ExplicitWorld,
                    false,
                    explicitWorldMaterial);
                visuals.ExplicitWorld.transform.SetParent(liveRoot.transform, false);
                if (xrOrigin != null)
                {
                    visuals.ExplicitWorld.transform.SetPositionAndRotation(
                        xrOrigin.TransformPoint(convertedPose.position),
                        xrOrigin.rotation * convertedPose.rotation);
                }
                else
                {
                    visuals.ExplicitWorld.transform.SetPositionAndRotation(
                        convertedPose.position,
                        convertedPose.rotation);
                }

                SetVisualActive(i, true);

                float step = 0f;
                float angleStep = 0f;
                if (previousPoseValid[i])
                {
                    step = Vector3.Distance(
                        previousRawPoses[i].position,
                        convertedPose.position);
                    angleStep = Quaternion.Angle(
                        previousRawPoses[i].rotation,
                        convertedPose.rotation);
                }

                previousRawPoses[i] = convertedPose;
                previousPoseValid[i] = true;

                observations.Add(new PlaneObservation
                {
                    Index = i,
                    Property = info.property,
                    Timestamp = info.pose.timestamp,
                    RawConvertedPose = convertedPose,
                    Area = CalculateArea(info),
                    PolygonCount = info.local_polygon_size,
                    LocalRange = new Vector2(info.local_range.x, info.local_range.z),
                    OfficialDirectWorldPose = GetWorldPose(
                        visuals.OfficialDirect.transform),
                    OriginChildWorldPose = GetWorldPose(
                        visuals.OriginChild.transform),
                    ExplicitWorldPose = GetWorldPose(
                        visuals.ExplicitWorld.transform),
                    RawPositionStep = step,
                    RawAngleStep = angleStep
                });
            }

            HideUnusedVisuals(RawPlaneCount);
        }

        public void CaptureSnapshot()
        {
            if (snapshotRoot != null)
            {
                Destroy(snapshotRoot);
            }

            snapshotRoot = new GameObject(
                $"FROZEN PLANE SNAPSHOT {SnapshotSerial + 1} - WORLD ROOT");
            int copied = 0;
            for (int i = 0; i < visualSets.Length; i++)
            {
                PlaneVisualSet set = visualSets[i];
                if (set == null)
                {
                    continue;
                }

                copied += CloneFrozenVisual(set.OfficialDirect, "Frozen GREEN official");
                copied += CloneFrozenVisual(set.OriginChild, "Frozen BLUE origin child");
                copied += CloneFrozenVisual(set.ExplicitWorld, "Frozen YELLOW explicit");
            }

            SnapshotSerial++;
            Debug.Log(
                $"RAYNEO_DIAG SNAPSHOT serial={SnapshotSerial}, clonedVisuals={copied}, " +
                $"head={(trackedHead != null ? trackedHead.position.ToString("F4") : "NONE")}",
                this);
        }

        public string GetShortObservationSummary()
        {
            if (observations.Count == 0)
            {
                return "PLANE -";
            }

            PlaneObservation p = observations[0];
            return $"PLANE0 {p.Property.ToString().Replace("PLANE_", string.Empty)} | " +
                   $"TS {p.Timestamp} | AREA {p.Area:F2} | " +
                   $"RAW P {p.RawConvertedPose.position:F2} | " +
                   $"dP {p.RawPositionStep:F3} dR {p.RawAngleStep:F1}";
        }

        private PlaneVisualSet EnsureVisualSet(int index)
        {
            if (visualSets[index] != null)
            {
                return visualSets[index];
            }

            PlaneVisualSet set = new PlaneVisualSet
            {
                OfficialDirect = new GameObject($"Plane {index} GREEN Official Direct"),
                OriginChild = new GameObject($"Plane {index} BLUE XR Origin Child"),
                ExplicitWorld = new GameObject($"Plane {index} YELLOW Explicit World")
            };
            set.OfficialDirect.transform.SetParent(liveRoot.transform, false);
            set.OriginChild.transform.SetParent(
                xrOrigin != null ? xrOrigin : liveRoot.transform,
                false);
            set.ExplicitWorld.transform.SetParent(liveRoot.transform, false);
            visualSets[index] = set;
            return set;
        }

        private void SetVisualActive(int index, bool active)
        {
            PlaneVisualSet set = visualSets[index];
            if (set == null)
            {
                return;
            }

            set.OfficialDirect.SetActive(active);
            set.OriginChild.SetActive(active);
            set.ExplicitWorld.SetActive(active);
        }

        private void HideUnusedVisuals(int usedCount)
        {
            for (int i = usedCount; i < visualSets.Length; i++)
            {
                SetVisualActive(i, false);
                previousPoseValid[i] = false;
            }
        }

        private int CloneFrozenVisual(GameObject source, string cloneName)
        {
            if (source == null || !source.activeInHierarchy)
            {
                return 0;
            }

            MeshFilter sourceFilter = source.GetComponent<MeshFilter>();
            MeshRenderer sourceRenderer = source.GetComponent<MeshRenderer>();
            if (sourceFilter == null || sourceFilter.sharedMesh == null ||
                sourceRenderer == null)
            {
                return 0;
            }

            GameObject clone = new GameObject(
                cloneName,
                typeof(MeshFilter),
                typeof(MeshRenderer));
            clone.transform.SetParent(snapshotRoot.transform, true);
            clone.transform.SetPositionAndRotation(
                source.transform.position,
                source.transform.rotation);
            clone.transform.localScale = source.transform.lossyScale;
            clone.GetComponent<MeshFilter>().sharedMesh =
                Instantiate(sourceFilter.sharedMesh);

            Color color = sourceRenderer.sharedMaterial != null
                ? sourceRenderer.sharedMaterial.color
                : Color.white;
            color.a = 0.72f;
            clone.GetComponent<MeshRenderer>().sharedMaterial =
                RayNeoDiagnosticVisuals.CreateTransparentMaterial(
                    cloneName + " Material",
                    color,
                    3020);
            return 1;
        }

        private static Pose GetWorldPose(Transform transform)
        {
            return new Pose(transform.position, transform.rotation);
        }

        private static float CalculateArea(XRPlaneInfo info)
        {
            if (info.local_polygon == null || info.local_polygon_size < 3)
            {
                return 0f;
            }

            int count = Mathf.Min(
                info.local_polygon_size,
                info.local_polygon.Length / 2);
            float twiceArea = 0f;
            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                float ax = info.local_polygon[i * 2];
                float ay = info.local_polygon[i * 2 + 1];
                float bx = info.local_polygon[next * 2];
                float by = info.local_polygon[next * 2 + 1];
                twiceArea += ax * by - bx * ay;
            }

            return Mathf.Abs(twiceArea) * 0.5f;
        }

        private static void DestroyMaterial(Material material)
        {
            if (material != null)
            {
                Destroy(material);
            }
        }
    }
}
