using System;
using System.Collections.Generic;
using com.rayneo.xr.extensions;
using UnityEngine;

namespace ACLRehab.RayNeoSpatial
{
    public sealed class GroundPlaneSelector : MonoBehaviour
    {
        public readonly struct CandidateView
        {
            public readonly int Id;
            public readonly RayNeoSpatialBootstrap.PlaneSnapshot Plane;
            public readonly Pose WorldPose;
            public readonly float HeadDistance;
            public readonly float TiltDegrees;
            public readonly float LastSeenAge;
            public readonly bool IsSelected;

            public CandidateView(
                int id,
                RayNeoSpatialBootstrap.PlaneSnapshot plane,
                Pose worldPose,
                float headDistance,
                float tiltDegrees,
                float lastSeenAge,
                bool isSelected)
            {
                Id = id;
                Plane = plane;
                WorldPose = worldPose;
                HeadDistance = headDistance;
                TiltDegrees = tiltDegrees;
                LastSeenAge = lastSeenAge;
                IsSelected = isSelected;
            }
        }

        private sealed class TrackedCandidate
        {
            public int Id;
            public RayNeoSpatialBootstrap.PlaneSnapshot Plane;
            public Pose WorldPose;
            public float HeadDistance;
            public float TiltDegrees;
            public string DistanceMode;
            public float LastSeenTime;
        }

        [SerializeField] private RayNeoSpatialBootstrap spatialBootstrap;
        [SerializeField] private Transform trackedHead;
        [SerializeField] private Transform selectedGroundReference;
        [SerializeField, Min(0f)] private float minimumArea = 1.5f;
        [SerializeField, Min(0.1f)] private float maximumHeadDistance = 4f;
        [SerializeField, Min(0.5f)] private float maximumHorizontalDistanceFromHead = 8f;

        [Header("Ground validation")]
        [SerializeField, Range(1f, 45f)] private float maximumTiltDegrees = 15f;
        [SerializeField, Min(0.5f)] private float minimumGroundHeight = 1.1f;
        [SerializeField, Min(0.6f)] private float maximumGroundHeight = 2.2f;

        [Header("Candidate memory and selection")]
        [SerializeField, Range(2, 12)] private int maximumCachedCandidates = 6;
        [SerializeField, Min(1f)] private float candidateRetentionSeconds = 8f;
        [Tooltip("Maximum separation along the plane normal for two observations to be treated as the same ground plane.")]
        [SerializeField, Min(0.1f)] private float candidateMergeDistance = 0.75f;
        [SerializeField, Min(0f)] private float selectionHysteresis = 0.35f;
        [SerializeField, Min(0.1f)] private float poseSmoothing = 8f;

        private readonly List<TrackedCandidate> trackedCandidates =
            new List<TrackedCandidate>();
        private readonly List<CandidateView> candidateViews =
            new List<CandidateView>();
        private int nextCandidateId = 1;
        private int selectedCandidateId = -1;
        private bool hasLockedHeadRelativeFallback;
        private Pose lockedHeadRelativeFallbackPose;

        public event Action<RayNeoSpatialBootstrap.PlaneSnapshot> GroundChanged;
        public event Action GroundLost;
        public event Action<IReadOnlyList<CandidateView>> CandidatesChanged;

        public bool HasGround { get; private set; }
        public RayNeoSpatialBootstrap.PlaneSnapshot SelectedGround { get; private set; }
        public Pose SelectedGroundWorldPose { get; private set; }
        public int SelectedCandidateId => selectedCandidateId;
        public IReadOnlyList<CandidateView> Candidates => candidateViews;
        public int CandidateCount => candidateViews.Count;
        public int RawPlaneCount { get; private set; }
        public int ValidPlaneCount { get; private set; }
        public bool HasCandidate { get; private set; }
        public XRPlaneProperty CandidateProperty { get; private set; }
        public float CandidateArea { get; private set; }
        public float CandidateTiltDegrees { get; private set; }
        public float CandidateHeadDistance { get; private set; }
        public float CandidateHorizontalDistance { get; private set; }
        public string CandidateDistanceMode { get; private set; } = "NONE";
        public string CandidateDecision { get; private set; } = "NO_PLANES";
        public Vector3 CandidateRawPosition { get; private set; }
        public Vector3 CandidateResolvedPosition { get; private set; }
        public Vector3 CandidateWorldNormal { get; private set; } = Vector3.up;
        public float MinimumArea => minimumArea;
        public float MaximumHeadDistance => maximumHeadDistance;
        public float MaximumTiltDegrees => maximumTiltDegrees;

        public void Configure(
            RayNeoSpatialBootstrap bootstrap,
            Transform head,
            Transform groundReference)
        {
            spatialBootstrap = bootstrap;
            trackedHead = head;
            selectedGroundReference = groundReference;
        }

        private void OnEnable()
        {
            if (spatialBootstrap != null)
            {
                spatialBootstrap.PlanesUpdated += SelectGround;
            }
        }

        private void Start()
        {
            if (spatialBootstrap != null)
            {
                spatialBootstrap.PlanesUpdated -= SelectGround;
                spatialBootstrap.PlanesUpdated += SelectGround;
            }
        }

        private void OnDisable()
        {
            if (spatialBootstrap != null)
            {
                spatialBootstrap.PlanesUpdated -= SelectGround;
            }
        }

        private void SelectGround(
            IReadOnlyList<RayNeoSpatialBootstrap.PlaneSnapshot> planes)
        {
            float now = Time.unscaledTime;
            RawPlaneCount = planes.Count;
            ValidPlaneCount = 0;
            ResetDiagnosticCandidate();

            for (int i = 0; i < planes.Count; i++)
            {
                RayNeoSpatialBootstrap.PlaneSnapshot plane = planes[i];
                ResolveWorldPoseAndDistance(
                    plane,
                    out Pose worldPose,
                    out Vector3 worldNormal,
                    out float headDistance,
                    out float horizontalDistance,
                    out string distanceMode);

                float upAlignment = Mathf.Abs(Mathf.Clamp(
                    Vector3.Dot(worldNormal, Vector3.up),
                    -1f,
                    1f));
                float tiltDegrees = Mathf.Acos(upAlignment) * Mathf.Rad2Deg;

                string decision = EvaluateCandidate(
                    plane,
                    tiltDegrees,
                    headDistance,
                    distanceMode);
                UpdateDiagnosticCandidate(
                    plane,
                    tiltDegrees,
                    headDistance,
                    horizontalDistance,
                    distanceMode,
                    worldPose,
                    worldNormal,
                    decision);

                if (decision != "ACCEPTED")
                {
                    continue;
                }

                ValidPlaneCount++;
                AddOrUpdateTrackedCandidate(
                    plane,
                    worldPose,
                    worldNormal,
                    headDistance,
                    tiltDegrees,
                    distanceMode,
                    now);
            }

            RemoveExpiredCandidates(now);
            SelectBestCandidate(now);
            RebuildCandidateViews(now);
        }

        private string EvaluateCandidate(
            RayNeoSpatialBootstrap.PlaneSnapshot plane,
            float tiltDegrees,
            float headDistance,
            string distanceMode)
        {
            // RayNeo distinguishes horizontal planes by the direction of the
            // reported normal. A physical floor can therefore arrive as
            // HORIZONTAL_UP or HORIZONTAL_DOWN depending on the runtime's
            // current plane orientation. Both are horizontal ground
            // candidates; the tilt and head-height checks below still reject
            // walls and implausible surfaces.
            bool isHorizontal =
                plane.Property == XRPlaneProperty.PLANE_HORIZONTAL_UP ||
                plane.Property == XRPlaneProperty.PLANE_HORIZONTAL_DOWN;
            if (!isHorizontal)
            {
                return plane.Property == XRPlaneProperty.PLANE_VERTICAL
                    ? "WRONG_TYPE_VERTICAL"
                    : "WRONG_TYPE_NON";
            }

            if (tiltDegrees > maximumTiltDegrees)
            {
                return "TILT_TOO_HIGH";
            }

            if (plane.Area < minimumArea)
            {
                return "AREA_TOO_SMALL";
            }

            if (distanceMode == "UNRESOLVED" || headDistance > maximumHeadDistance)
            {
                return "TOO_FAR";
            }

            if (headDistance < minimumGroundHeight)
            {
                return "TOO_HIGH_FOR_FLOOR";
            }

            if (headDistance > maximumGroundHeight)
            {
                return "TOO_LOW_FOR_FLOOR";
            }

            return "ACCEPTED";
        }

        private void AddOrUpdateTrackedCandidate(
            RayNeoSpatialBootstrap.PlaneSnapshot plane,
            Pose worldPose,
            Vector3 worldNormal,
            float headDistance,
            float tiltDegrees,
            string distanceMode,
            float now)
        {
            TrackedCandidate match = FindMatchingCandidate(worldPose, worldNormal);
            if (match == null)
            {
                if (trackedCandidates.Count >= maximumCachedCandidates)
                {
                    RemoveOldestCandidate();
                }

                Vector3 up = GetUpFacingNormal(worldNormal);
                Vector3 forward = ResolveStableForward(up, Vector3.zero);
                trackedCandidates.Add(new TrackedCandidate
                {
                    Id = nextCandidateId++,
                    Plane = plane,
                    WorldPose = new Pose(
                        worldPose.position,
                        Quaternion.LookRotation(forward, up)),
                    HeadDistance = headDistance,
                    TiltDegrees = tiltDegrees,
                    DistanceMode = distanceMode,
                    LastSeenTime = now
                });
                return;
            }

            float elapsed = Mathf.Max(0.001f, now - match.LastSeenTime);
            float blend = 1f - Mathf.Exp(-poseSmoothing * elapsed);
            Vector3 targetUp = GetUpFacingNormal(worldNormal);
            Vector3 targetForward = ResolveStableForward(
                targetUp,
                match.WorldPose.rotation * Vector3.forward);
            Quaternion targetRotation = Quaternion.LookRotation(
                targetForward,
                targetUp);

            // A plane detector is free to move the polygon centre while it
            // refines or extends the same physical surface. Preserve the
            // candidate's tangent position and update only its offset along
            // the plane normal so the visual reference does not slide under
            // the user.
            float normalOffset = Vector3.Dot(
                worldPose.position - match.WorldPose.position,
                targetUp);
            Vector3 targetPosition =
                match.WorldPose.position + targetUp * normalOffset;
            match.Plane = plane;
            match.WorldPose = new Pose(
                Vector3.Lerp(match.WorldPose.position, targetPosition, blend),
                Quaternion.Slerp(match.WorldPose.rotation, targetRotation, blend));
            match.HeadDistance = Mathf.Lerp(match.HeadDistance, headDistance, blend);
            match.TiltDegrees = Mathf.Lerp(match.TiltDegrees, tiltDegrees, blend);
            match.DistanceMode = distanceMode;
            match.LastSeenTime = now;
        }

        private TrackedCandidate FindMatchingCandidate(
            Pose worldPose,
            Vector3 worldNormal)
        {
            TrackedCandidate best = null;
            float bestDistance = candidateMergeDistance;
            Vector3 newNormal = GetUpFacingNormal(worldNormal);

            for (int i = 0; i < trackedCandidates.Count; i++)
            {
                TrackedCandidate candidate = trackedCandidates[i];
                Vector3 candidateNormal = GetUpFacingNormal(
                    candidate.WorldPose.rotation * Vector3.up);
                float normalAlignment = Vector3.Dot(
                    candidateNormal,
                    newNormal);
                Vector3 comparisonNormal = (candidateNormal + newNormal).normalized;
                float distance = Mathf.Abs(Vector3.Dot(
                    worldPose.position - candidate.WorldPose.position,
                    comparisonNormal));

                if (distance <= bestDistance && normalAlignment >= 0.94f)
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private void RemoveExpiredCandidates(float now)
        {
            for (int i = trackedCandidates.Count - 1; i >= 0; i--)
            {
                if (now - trackedCandidates[i].LastSeenTime <= candidateRetentionSeconds)
                {
                    continue;
                }

                int removedId = trackedCandidates[i].Id;
                trackedCandidates.RemoveAt(i);
                if (selectedCandidateId == removedId)
                {
                    selectedCandidateId = -1;
                }
            }
        }

        private void RemoveOldestCandidate()
        {
            if (trackedCandidates.Count == 0)
            {
                return;
            }

            int oldestIndex = 0;
            for (int i = 1; i < trackedCandidates.Count; i++)
            {
                if (trackedCandidates[i].LastSeenTime <
                    trackedCandidates[oldestIndex].LastSeenTime)
                {
                    oldestIndex = i;
                }
            }

            int removedId = trackedCandidates[oldestIndex].Id;
            trackedCandidates.RemoveAt(oldestIndex);
            if (selectedCandidateId == removedId)
            {
                selectedCandidateId = -1;
            }
        }

        private void SelectBestCandidate(float now)
        {
            if (trackedCandidates.Count == 0)
            {
                ClearGround();
                return;
            }

            TrackedCandidate current = FindCandidateById(selectedCandidateId);
            TrackedCandidate best = null;
            float bestScore = float.NegativeInfinity;

            for (int i = 0; i < trackedCandidates.Count; i++)
            {
                TrackedCandidate candidate = trackedCandidates[i];
                float score = ScoreCandidate(candidate, now);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            if (current != null && best != null && current.Id != best.Id)
            {
                float currentScore = ScoreCandidate(current, now);
                if (bestScore < currentScore + selectionHysteresis)
                {
                    best = current;
                }
            }

            if (best == null)
            {
                ClearGround();
                return;
            }

            selectedCandidateId = best.Id;
            SelectedGround = best.Plane;
            SelectedGroundWorldPose = best.WorldPose;
            HasGround = true;
            CandidateProperty = best.Plane.Property;
            CandidateArea = best.Plane.Area;
            CandidateTiltDegrees = best.TiltDegrees;
            CandidateHeadDistance = best.HeadDistance;
            CandidateDistanceMode = best.DistanceMode;
            CandidateDecision = "ACCEPTED";

            if (selectedGroundReference != null)
            {
                selectedGroundReference.SetPositionAndRotation(
                    best.WorldPose.position,
                    best.WorldPose.rotation);
            }

            GroundChanged?.Invoke(best.Plane);
        }

        private float ScoreCandidate(TrackedCandidate candidate, float now)
        {
            float agePenalty = Mathf.Max(0f, now - candidate.LastSeenTime) * 0.3f;
            float heightPenalty = Mathf.Abs(candidate.HeadDistance - 1.65f) * 0.6f;
            float areaBonus = Mathf.Min(candidate.Plane.Area, 20f) * 0.025f;
            float gazeBonus = 0f;

            if (trackedHead != null)
            {
                Vector3 toCandidate =
                    candidate.WorldPose.position - trackedHead.position;
                if (toCandidate.sqrMagnitude > 0.001f)
                {
                    gazeBonus = Vector3.Dot(
                        trackedHead.forward,
                        toCandidate.normalized) * 1.5f;
                }
            }

            return gazeBonus + areaBonus - heightPenalty - agePenalty;
        }

        private TrackedCandidate FindCandidateById(int id)
        {
            for (int i = 0; i < trackedCandidates.Count; i++)
            {
                if (trackedCandidates[i].Id == id)
                {
                    return trackedCandidates[i];
                }
            }

            return null;
        }

        private void RebuildCandidateViews(float now)
        {
            candidateViews.Clear();
            for (int i = 0; i < trackedCandidates.Count; i++)
            {
                TrackedCandidate candidate = trackedCandidates[i];
                candidateViews.Add(new CandidateView(
                    candidate.Id,
                    candidate.Plane,
                    candidate.WorldPose,
                    candidate.HeadDistance,
                    candidate.TiltDegrees,
                    Mathf.Max(0f, now - candidate.LastSeenTime),
                    candidate.Id == selectedCandidateId));
            }

            CandidatesChanged?.Invoke(candidateViews);
        }

        private void ResetDiagnosticCandidate()
        {
            HasCandidate = false;
            CandidateArea = 0f;
            CandidateTiltDegrees = 180f;
            CandidateHeadDistance = 0f;
            CandidateHorizontalDistance = 0f;
            CandidateDistanceMode = "NONE";
            CandidateDecision = RawPlaneCount == 0 ? "NO_PLANES" : "NO_VALID_GROUND";
            CandidateRawPosition = Vector3.zero;
            CandidateResolvedPosition = Vector3.zero;
            CandidateWorldNormal = Vector3.up;
        }

        private void UpdateDiagnosticCandidate(
            RayNeoSpatialBootstrap.PlaneSnapshot plane,
            float tiltDegrees,
            float headDistance,
            float horizontalDistance,
            string distanceMode,
            Pose resolvedWorldPose,
            Vector3 worldNormal,
            string decision)
        {
            if (HasCandidate && plane.Area <= CandidateArea)
            {
                return;
            }

            HasCandidate = true;
            CandidateProperty = plane.Property;
            CandidateArea = plane.Area;
            CandidateTiltDegrees = tiltDegrees;
            CandidateHeadDistance = headDistance;
            CandidateHorizontalDistance = horizontalDistance;
            CandidateDistanceMode = distanceMode;
            CandidateDecision = decision;
            CandidateRawPosition = plane.Pose.position;
            CandidateResolvedPosition = resolvedWorldPose.position;
            CandidateWorldNormal = GetUpFacingNormal(worldNormal);
        }

        public void ClearGround()
        {
            bool hadGround = HasGround;
            HasGround = false;
            SelectedGround = default;
            SelectedGroundWorldPose = default;
            selectedCandidateId = -1;

            if (hadGround)
            {
                GroundLost?.Invoke();
            }
        }

        public void ClearCandidates()
        {
            trackedCandidates.Clear();
            candidateViews.Clear();
            hasLockedHeadRelativeFallback = false;
            lockedHeadRelativeFallbackPose = default;
            ClearGround();
            CandidatesChanged?.Invoke(candidateViews);
        }

        private void ResolveWorldPoseAndDistance(
            RayNeoSpatialBootstrap.PlaneSnapshot candidate,
            out Pose worldPose,
            out Vector3 worldNormal,
            out float headDistance,
            out float horizontalDistance,
            out string distanceMode)
        {
            Vector3 localNormal =
                (candidate.Pose.rotation * Vector3.up).normalized;
            worldPose = candidate.Pose;
            worldNormal = localNormal;
            headDistance = 0f;
            horizontalDistance = 0f;
            distanceMode = "NO_HEAD";

            if (trackedHead == null)
            {
                return;
            }

            // RayNeo's official sample applies the converted plane pose in the
            // XR tracking origin's local space. Transform both pose and normal
            // through the same XR Plugin root used by the tracked Head.
            Transform trackingOrigin = trackedHead.root;
            worldPose = new Pose(
                trackingOrigin.TransformPoint(candidate.Pose.position),
                trackingOrigin.rotation * candidate.Pose.rotation);
            worldNormal = (trackingOrigin.rotation * localNormal).normalized;
            Vector3 worldHeadOffset = trackedHead.position - worldPose.position;
            float worldDistance = Mathf.Abs(Vector3.Dot(
                worldHeadOffset,
                worldNormal));
            float worldHorizontalDistance = Vector3.ProjectOnPlane(
                worldHeadOffset,
                worldNormal).magnitude;
            if (worldDistance <= maximumHeadDistance &&
                worldHorizontalDistance <= maximumHorizontalDistanceFromHead)
            {
                headDistance = worldDistance;
                horizontalDistance = worldHorizontalDistance;
                distanceMode = "XR_ORIGIN";
                return;
            }

            float headRelativeDistance = Mathf.Abs(Vector3.Dot(
                -candidate.Pose.position,
                localNormal));
            if (headRelativeDistance <= maximumHeadDistance)
            {
                if (!hasLockedHeadRelativeFallback)
                {
                    Vector3 fallbackUp = GetUpFacingNormal(worldNormal);
                    Vector3 fallbackForward = ResolveStableForward(
                        fallbackUp,
                        Vector3.zero);
                    lockedHeadRelativeFallbackPose = new Pose(
                        trackedHead.position - fallbackUp * headRelativeDistance,
                        Quaternion.LookRotation(fallbackForward, fallbackUp));
                    hasLockedHeadRelativeFallback = true;
                }

                worldPose = lockedHeadRelativeFallbackPose;
                worldNormal =
                    lockedHeadRelativeFallbackPose.rotation * Vector3.up;
                headDistance = Mathf.Abs(Vector3.Dot(
                    trackedHead.position - worldPose.position,
                    worldNormal));
                horizontalDistance = Vector3.ProjectOnPlane(
                    trackedHead.position - worldPose.position,
                    worldNormal).magnitude;
                distanceMode = "HEAD_RELATIVE_LOCKED";
                return;
            }

            headDistance = worldDistance;
            horizontalDistance = worldHorizontalDistance;
            distanceMode = "UNRESOLVED";
        }

        private Vector3 ResolveStableForward(
            Vector3 up,
            Vector3 preferredForward)
        {
            Vector3 forward = Vector3.ProjectOnPlane(
                preferredForward,
                up).normalized;
            if (forward.sqrMagnitude < 0.01f && trackedHead != null)
            {
                forward = Vector3.ProjectOnPlane(
                    trackedHead.forward,
                    up).normalized;
            }

            if (forward.sqrMagnitude < 0.01f)
            {
                forward = Vector3.ProjectOnPlane(
                    Vector3.forward,
                    up).normalized;
            }

            return forward;
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

        private void OnValidate()
        {
            maximumGroundHeight = Mathf.Max(
                minimumGroundHeight + 0.1f,
                maximumGroundHeight);
            maximumHeadDistance = Mathf.Max(maximumHeadDistance, maximumGroundHeight);
        }
    }
}
