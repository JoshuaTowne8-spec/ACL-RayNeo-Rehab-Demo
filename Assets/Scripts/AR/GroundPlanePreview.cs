using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ACLRehab.RayNeoSpatial
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class GroundPlanePreview : MonoBehaviour
    {
        [SerializeField] private GroundPlaneSelector groundSelector;
        [SerializeField, Min(0f)] private float surfaceOffset = 0.01f;
        [SerializeField, Min(0.1f)] private float gridSpacing = 0.5f;
        [SerializeField, Range(3, 25)] private int maximumGridLinesPerAxis = 13;
        [SerializeField, Min(0.002f)] private float gridLineWidth = 0.012f;
        [SerializeField, Min(0.002f)] private float axisLineWidth = 0.025f;
        [SerializeField, Min(0.1f)] private float poseSmoothing = 10f;
        [SerializeField, Min(0f)] private float boundaryChangeTolerance = 0.02f;

        private Mesh previewMesh;
        private MeshRenderer previewRenderer;
        private GameObject referenceLinesRoot;
        private GameObject candidateOutlinesRoot;
        private Material candidateOutlineMaterial;
        private readonly Dictionary<int, LineRenderer> candidateOutlines =
            new Dictionary<int, LineRenderer>();
        private readonly List<Material> runtimeLineMaterials = new List<Material>();
        private Vector2[] displayedBoundary;
        private Pose targetPose;
        private bool hasTargetPose;
        private bool requestedVisible;

        public void Configure(GroundPlaneSelector selector, Material material)
        {
            groundSelector = selector;
            previewRenderer = GetComponent<MeshRenderer>();
            previewRenderer.sharedMaterial = material;
        }

        private void Awake()
        {
            previewRenderer = GetComponent<MeshRenderer>();
            previewRenderer.shadowCastingMode = ShadowCastingMode.Off;
            previewRenderer.receiveShadows = false;
            previewRenderer.sortingOrder = 200;
            SetVisible(false);
        }

        private void OnEnable()
        {
            if (groundSelector != null)
            {
                groundSelector.GroundChanged += UpdatePreview;
                groundSelector.GroundLost += HidePreview;
                groundSelector.CandidatesChanged += UpdateCandidateOutlines;
            }
        }

        private void Start()
        {
            if (groundSelector != null)
            {
                groundSelector.GroundChanged -= UpdatePreview;
                groundSelector.GroundChanged += UpdatePreview;
                groundSelector.GroundLost -= HidePreview;
                groundSelector.GroundLost += HidePreview;
                groundSelector.CandidatesChanged -= UpdateCandidateOutlines;
                groundSelector.CandidatesChanged += UpdateCandidateOutlines;
            }
        }

        private void LateUpdate()
        {
            if (!hasTargetPose)
            {
                return;
            }

            float blend = 1f - Mathf.Exp(-poseSmoothing * Time.unscaledDeltaTime);
            float positionJump = Vector3.Distance(transform.position, targetPose.position);
            float rotationJump = Quaternion.Angle(transform.rotation, targetPose.rotation);
            if (positionJump > 0.8f || rotationJump > 30f)
            {
                transform.SetPositionAndRotation(targetPose.position, targetPose.rotation);
                return;
            }

            transform.SetPositionAndRotation(
                Vector3.Lerp(transform.position, targetPose.position, blend),
                Quaternion.Slerp(transform.rotation, targetPose.rotation, blend));
        }

        private void OnDisable()
        {
            if (groundSelector != null)
            {
                groundSelector.GroundChanged -= UpdatePreview;
                groundSelector.GroundLost -= HidePreview;
                groundSelector.CandidatesChanged -= UpdateCandidateOutlines;
            }

            SetVisible(false);
        }

        private void OnDestroy()
        {
            if (previewMesh != null)
            {
                Destroy(previewMesh);
            }

            ClearReferenceLines();
            ClearCandidateOutlines();
        }

        public void SetVisible(bool visible)
        {
            requestedVisible = visible;
            if (previewRenderer == null)
            {
                previewRenderer = GetComponent<MeshRenderer>();
            }

            bool show = visible && previewMesh != null;
            previewRenderer.enabled = show;
            if (referenceLinesRoot != null)
            {
                referenceLinesRoot.SetActive(show);
            }

            if (candidateOutlinesRoot != null)
            {
                candidateOutlinesRoot.SetActive(show);
            }
        }

        private void UpdatePreview(RayNeoSpatialBootstrap.PlaneSnapshot ground)
        {
            Vector2[] boundary = ground.Boundary;
            if (boundary == null || boundary.Length < 3)
            {
                HidePreview();
                return;
            }

            if (BoundaryChanged(boundary))
            {
                RebuildSurface(boundary);
                RebuildReferenceLines(boundary);
                displayedBoundary = (Vector2[])boundary.Clone();
            }

            targetPose = groundSelector != null && groundSelector.HasGround
                ? groundSelector.SelectedGroundWorldPose
                : ground.Pose;
            if (!hasTargetPose)
            {
                transform.SetPositionAndRotation(targetPose.position, targetPose.rotation);
                hasTargetPose = true;
            }

            SetVisible(true);
        }

        private bool BoundaryChanged(Vector2[] boundary)
        {
            if (displayedBoundary == null || displayedBoundary.Length != boundary.Length)
            {
                return true;
            }

            for (int i = 0; i < boundary.Length; i++)
            {
                if (Vector2.Distance(displayedBoundary[i], boundary[i]) >
                    boundaryChangeTolerance)
                {
                    return true;
                }
            }

            return false;
        }

        private void RebuildSurface(Vector2[] boundary)
        {
            Vector3[] vertices = new Vector3[boundary.Length];
            for (int i = 0; i < boundary.Length; i++)
            {
                vertices[i] = new Vector3(boundary[i].x, surfaceOffset, boundary[i].y);
            }

            int[] triangles = new int[(boundary.Length - 2) * 3];
            for (int i = 0; i < boundary.Length - 2; i++)
            {
                int triangle = i * 3;
                triangles[triangle] = 0;
                triangles[triangle + 1] = i + 2;
                triangles[triangle + 2] = i + 1;
            }

            if (previewMesh == null)
            {
                previewMesh = new Mesh { name = "RayNeo Selected Ground Preview" };
                GetComponent<MeshFilter>().sharedMesh = previewMesh;
            }
            else
            {
                previewMesh.Clear();
            }

            previewMesh.vertices = vertices;
            previewMesh.triangles = triangles;
            previewMesh.RecalculateNormals();
            previewMesh.RecalculateBounds();
        }

        private void UpdateCandidateOutlines(
            IReadOnlyList<GroundPlaneSelector.CandidateView> candidates)
        {
            EnsureCandidateOutlineRoot();
            HashSet<int> liveIds = new HashSet<int>();

            for (int i = 0; i < candidates.Count; i++)
            {
                GroundPlaneSelector.CandidateView candidate = candidates[i];
                if (candidate.IsSelected || candidate.Plane.Boundary == null ||
                    candidate.Plane.Boundary.Length < 3)
                {
                    continue;
                }

                liveIds.Add(candidate.Id);
                if (!candidateOutlines.TryGetValue(candidate.Id, out LineRenderer line) ||
                    line == null)
                {
                    GameObject outlineObject = new GameObject(
                        $"Ground Candidate {candidate.Id} Outline");
                    outlineObject.transform.SetParent(candidateOutlinesRoot.transform, false);
                    line = outlineObject.AddComponent<LineRenderer>();
                    line.useWorldSpace = false;
                    line.loop = true;
                    line.widthMultiplier = gridLineWidth * 1.5f;
                    line.numCapVertices = 2;
                    line.numCornerVertices = 2;
                    line.shadowCastingMode = ShadowCastingMode.Off;
                    line.receiveShadows = false;
                    line.sortingOrder = 205;
                    line.sharedMaterial = candidateOutlineMaterial;
                    candidateOutlines.Add(candidate.Id, line);
                }

                line.transform.SetPositionAndRotation(
                    candidate.WorldPose.position,
                    candidate.WorldPose.rotation);
                Vector2[] boundary = candidate.Plane.Boundary;
                line.positionCount = boundary.Length;
                for (int point = 0; point < boundary.Length; point++)
                {
                    line.SetPosition(
                        point,
                        new Vector3(
                            boundary[point].x,
                            surfaceOffset + 0.012f,
                            boundary[point].y));
                }
            }

            List<int> expiredIds = null;
            foreach (KeyValuePair<int, LineRenderer> pair in candidateOutlines)
            {
                if (liveIds.Contains(pair.Key))
                {
                    continue;
                }

                if (expiredIds == null)
                {
                    expiredIds = new List<int>();
                }

                expiredIds.Add(pair.Key);
            }

            if (expiredIds != null)
            {
                for (int i = 0; i < expiredIds.Count; i++)
                {
                    int id = expiredIds[i];
                    if (candidateOutlines.TryGetValue(id, out LineRenderer line) &&
                        line != null)
                    {
                        Destroy(line.gameObject);
                    }

                    candidateOutlines.Remove(id);
                }
            }

            candidateOutlinesRoot.SetActive(requestedVisible && previewMesh != null);
        }

        private void EnsureCandidateOutlineRoot()
        {
            if (candidateOutlinesRoot != null)
            {
                return;
            }

            candidateOutlinesRoot = new GameObject("Cached Ground Candidate Outlines");
            Transform parent = transform.parent;
            candidateOutlinesRoot.transform.SetParent(parent, false);
            candidateOutlineMaterial = CreateLineMaterial(
                "Ground Candidate Outline",
                new Color(1f, 0.82f, 0.18f, 0.65f));
            runtimeLineMaterials.Remove(candidateOutlineMaterial);
        }

        private void RebuildReferenceLines(Vector2[] boundary)
        {
            ClearReferenceLines();

            float minX = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float minY = float.PositiveInfinity;
            float maxY = float.NegativeInfinity;
            for (int i = 0; i < boundary.Length; i++)
            {
                minX = Mathf.Min(minX, boundary[i].x);
                maxX = Mathf.Max(maxX, boundary[i].x);
                minY = Mathf.Min(minY, boundary[i].y);
                maxY = Mathf.Max(maxY, boundary[i].y);
            }

            referenceLinesRoot = new GameObject("Plane Reference Grid - X Red, Y Blue");
            referenceLinesRoot.transform.SetParent(transform, false);
            float lineHeight = surfaceOffset + 0.008f;

            Material gridMaterial = CreateLineMaterial(
                "Ground Grid",
                new Color(0.82f, 0.9f, 1f, 0.72f));
            Material xMaterial = CreateLineMaterial(
                "Ground X Axis",
                new Color(1f, 0.18f, 0.18f, 1f));
            Material yMaterial = CreateLineMaterial(
                "Ground Y Axis",
                new Color(0.15f, 0.48f, 1f, 1f));

            float effectiveSpacingX = CalculateSpacing(minX, maxX);
            float effectiveSpacingY = CalculateSpacing(minY, maxY);
            int lineIndex = 0;

            float firstX = Mathf.Ceil(minX / effectiveSpacingX) * effectiveSpacingX;
            for (float x = firstX; x <= maxX + 0.001f; x += effectiveSpacingX)
            {
                CreateLine(
                    $"Grid X {lineIndex++}",
                    new Vector3(x, lineHeight, minY),
                    new Vector3(x, lineHeight, maxY),
                    gridLineWidth,
                    gridMaterial);
            }

            float firstY = Mathf.Ceil(minY / effectiveSpacingY) * effectiveSpacingY;
            for (float y = firstY; y <= maxY + 0.001f; y += effectiveSpacingY)
            {
                CreateLine(
                    $"Grid Y {lineIndex++}",
                    new Vector3(minX, lineHeight, y),
                    new Vector3(maxX, lineHeight, y),
                    gridLineWidth,
                    gridMaterial);
            }

            CreateLine(
                "Plane X Axis - Red",
                new Vector3(minX, lineHeight + 0.003f, 0f),
                new Vector3(maxX, lineHeight + 0.003f, 0f),
                axisLineWidth,
                xMaterial);
            CreateLine(
                "Plane Y Axis - Blue",
                new Vector3(0f, lineHeight + 0.003f, minY),
                new Vector3(0f, lineHeight + 0.003f, maxY),
                axisLineWidth,
                yMaterial);
        }

        private float CalculateSpacing(float minimum, float maximum)
        {
            float width = Mathf.Max(0.01f, maximum - minimum);
            float minimumSpacingForLimit = width / Mathf.Max(1, maximumGridLinesPerAxis - 1);
            return Mathf.Max(gridSpacing, minimumSpacingForLimit);
        }

        private void CreateLine(
            string lineName,
            Vector3 start,
            Vector3 end,
            float width,
            Material material)
        {
            GameObject lineObject = new GameObject(lineName);
            lineObject.transform.SetParent(referenceLinesRoot.transform, false);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.sharedMaterial = material;
            line.positionCount = 2;
            line.SetPosition(0, start);
            line.SetPosition(1, end);
            line.widthMultiplier = width;
            line.numCapVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sortingOrder = 210;
        }

        private Material CreateLineMaterial(string materialName, Color color)
        {
            Material source = previewRenderer != null
                ? previewRenderer.sharedMaterial
                : null;
            Material material = source != null
                ? new Material(source)
                : new Material(Shader.Find("Unlit/Color"));
            material.name = materialName + " (Runtime)";
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            runtimeLineMaterials.Add(material);
            return material;
        }

        private void ClearReferenceLines()
        {
            if (referenceLinesRoot != null)
            {
                Destroy(referenceLinesRoot);
                referenceLinesRoot = null;
            }

            for (int i = 0; i < runtimeLineMaterials.Count; i++)
            {
                if (runtimeLineMaterials[i] != null)
                {
                    Destroy(runtimeLineMaterials[i]);
                }
            }

            runtimeLineMaterials.Clear();
        }

        private void ClearCandidateOutlines()
        {
            candidateOutlines.Clear();
            if (candidateOutlinesRoot != null)
            {
                Destroy(candidateOutlinesRoot);
                candidateOutlinesRoot = null;
            }

            if (candidateOutlineMaterial != null)
            {
                Destroy(candidateOutlineMaterial);
            }

            candidateOutlineMaterial = null;
        }

        private void HidePreview()
        {
            hasTargetPose = false;
            SetVisible(false);
        }
    }
}
