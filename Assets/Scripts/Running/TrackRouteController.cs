using System;
using UnityEngine;

namespace ACLRehab.Running
{
    [Serializable]
    public struct RunningRouteSegment
    {
        public int index;
        public Vector3 start;
        public Vector3 forward;
        public float length;
        public float width;

        public Vector3 End => start + forward.normalized * length;
        public Vector3 Right => Vector3.Cross(Vector3.up, forward.normalized);
    }

    /// <summary>
    /// Owns the two visible route segments. All route data is expressed in the
    /// local coordinate system of routeOrigin, so the same controller can be
    /// parented to a desktop debug origin or a RayNeo calibrated origin.
    /// </summary>
    public sealed class TrackRouteController : MonoBehaviour
    {
        [Header("References")]
        public Transform routeOrigin;
        public Transform trackedPlayer;
        public TrackRenderer trackRenderer;
        public EchoStepSpawner echoStepSpawner;

        [Header("Route")]
        [Min(1f)] public float segmentLength = 7f;
        [Min(0.5f)] public float trackWidth = 1.2f;
        [Range(0f, 180f)] public float turnAngle = 90f;
        [Min(2f)] public float routeAreaSize = 20f;
        public bool enforceRouteBounds = true;
        public int randomSeed = 2026;
        public bool initializeOnStart = true;
        public bool autoAdvance = true;
        [Min(0f)] public float transitionTolerance = 0.15f;

        [Header("Runtime")]
        public RunningRouteSegment currentSegment;
        public RunningRouteSegment nextSegment;
        public float playerProgress;

        private System.Random random;
        private float previousPlayerProgress;
        private Vector3 previousLocalPlayerPosition;
        private bool hasPlayerSample;

        private void Start()
        {
            if (initializeOnStart)
            {
                ResetRoute();
            }
        }

        private void Update()
        {
            if (!autoAdvance || trackedPlayer == null || routeOrigin == null)
            {
                return;
            }

            Vector3 localPlayer = routeOrigin.InverseTransformPoint(trackedPlayer.position);
            float newProgress = Vector3.Dot(
                localPlayer - currentSegment.start,
                currentSegment.forward.normalized);
            float lateralDistance = Mathf.Abs(Vector3.Dot(
                localPlayer - currentSegment.start,
                currentSegment.Right.normalized));

            if (!hasPlayerSample)
            {
                StorePlayerSample(localPlayer, newProgress);
                return;
            }

            float threshold = currentSegment.length - transitionTolerance;
            float positionStep = Vector3.Distance(
                localPlayer,
                previousLocalPlayerPosition);
            bool crossedEnd = previousPlayerProgress < threshold &&
                              newProgress >= threshold;
            bool isNearTrack = lateralDistance <= currentSegment.width * 0.5f + 1f;
            bool isPlausibleTrackingStep = positionStep <= 2f;

            playerProgress = newProgress;

            if (crossedEnd && isNearTrack && isPlausibleTrackingStep)
            {
                AdvanceRoute();
                return;
            }

            StorePlayerSample(localPlayer, newProgress);
        }

        [ContextMenu("Reset Route")]
        public void ResetRoute()
        {
            if (routeOrigin == null)
            {
                routeOrigin = transform.parent != null ? transform.parent : transform;
            }

            random = new System.Random(randomSeed);
            currentSegment = CreateSegment(0, Vector3.zero, Vector3.forward);
            nextSegment = CreateNextSegment(currentSegment);
            ResetPlayerSample();
            RefreshViews();
        }

        [ContextMenu("Advance Route")]
        public void AdvanceRoute()
        {
            currentSegment = nextSegment;
            nextSegment = CreateNextSegment(currentSegment);
            ResetPlayerSample();
            RefreshViews();
        }

        private void ResetPlayerSample()
        {
            if (trackedPlayer == null || routeOrigin == null)
            {
                playerProgress = 0f;
                previousPlayerProgress = 0f;
                previousLocalPlayerPosition = Vector3.zero;
                hasPlayerSample = false;
                return;
            }

            Vector3 localPlayer = routeOrigin.InverseTransformPoint(trackedPlayer.position);
            float progress = Vector3.Dot(
                localPlayer - currentSegment.start,
                currentSegment.forward.normalized);
            StorePlayerSample(localPlayer, progress);
        }

        private void StorePlayerSample(Vector3 localPlayer, float progress)
        {
            previousLocalPlayerPosition = localPlayer;
            previousPlayerProgress = progress;
            playerProgress = progress;
            hasPlayerSample = true;
        }

        private RunningRouteSegment CreateNextSegment(RunningRouteSegment previous)
        {
            if (random == null)
            {
                random = new System.Random(randomSeed);
            }

            float firstTurn = random.Next(0, 2) == 0 ? -1f : 1f;
            RunningRouteSegment firstCandidate = CreateTurnCandidate(previous, firstTurn);
            RunningRouteSegment secondCandidate = CreateTurnCandidate(previous, -firstTurn);

            if (!enforceRouteBounds || SegmentFitsRouteArea(firstCandidate))
            {
                return firstCandidate;
            }

            if (SegmentFitsRouteArea(secondCandidate))
            {
                return secondCandidate;
            }

            Debug.LogWarning(
                "Neither 90-degree route candidate fits the configured route area. " +
                "Using the candidate closest to the origin.");
            return firstCandidate.End.sqrMagnitude <= secondCandidate.End.sqrMagnitude
                ? firstCandidate
                : secondCandidate;
        }

        private RunningRouteSegment CreateTurnCandidate(
            RunningRouteSegment previous,
            float direction)
        {
            Vector3 nextForward = Quaternion.AngleAxis(
                direction * turnAngle,
                Vector3.up) * previous.forward.normalized;
            return CreateSegment(previous.index + 1, previous.End, nextForward);
        }

        private bool SegmentFitsRouteArea(RunningRouteSegment segment)
        {
            float halfArea = routeAreaSize * 0.5f;
            float halfWidth = segment.width * 0.5f;
            Vector3 side = segment.Right.normalized * halfWidth;

            return PointFits(segment.start + side, halfArea) &&
                   PointFits(segment.start - side, halfArea) &&
                   PointFits(segment.End + side, halfArea) &&
                   PointFits(segment.End - side, halfArea);
        }

        private static bool PointFits(Vector3 point, float halfArea)
        {
            return Mathf.Abs(point.x) <= halfArea && Mathf.Abs(point.z) <= halfArea;
        }

        private RunningRouteSegment CreateSegment(int index, Vector3 start, Vector3 forward)
        {
            return new RunningRouteSegment
            {
                index = index,
                start = start,
                forward = forward.normalized,
                length = segmentLength,
                width = trackWidth
            };
        }

        private void RefreshViews()
        {
            if (trackRenderer != null)
            {
                trackRenderer.RenderSegments(currentSegment, nextSegment);
            }

            if (echoStepSpawner != null)
            {
                echoStepSpawner.BuildEchoes(currentSegment, nextSegment);
            }
        }

        private void OnValidate()
        {
            segmentLength = Mathf.Max(1f, segmentLength);
            trackWidth = Mathf.Max(0.5f, trackWidth);
            routeAreaSize = Mathf.Max(2f, routeAreaSize);
            transitionTolerance = Mathf.Clamp(transitionTolerance, 0f, segmentLength * 0.5f);
        }

        private void OnDrawGizmosSelected()
        {
            Transform origin = routeOrigin != null ? routeOrigin : transform;
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Gizmos.matrix = origin.localToWorldMatrix;
            Gizmos.color = new Color(0.45f, 0.2f, 1f, 0.8f);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(routeAreaSize, 0.02f, routeAreaSize));
            Gizmos.matrix = previousMatrix;
        }
    }
}
