using UnityEngine;
using UnityEngine.Rendering;

namespace ACLRehab.Running
{
    /// <summary>Draws the current and next route segments as one continuous translucent ribbon.</summary>
    public sealed class TrackRenderer : MonoBehaviour
    {
        public Material trackMaterial;
        [Min(0.001f)] public float height = 0.015f;

        private GameObject renderedRibbon;
        private Mesh renderedMesh;

        public void RenderSegments(RunningRouteSegment current, RunningRouteSegment next)
        {
            ClearRibbon();

            renderedRibbon = new GameObject($"Visible Track Ribbon {current.index:00}-{next.index:00}");
            renderedRibbon.transform.SetParent(transform, false);

            MeshFilter meshFilter = renderedRibbon.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = renderedRibbon.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = trackMaterial;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.sortingOrder = 100;

            renderedMesh = BuildRibbonMesh(current, next);
            meshFilter.sharedMesh = renderedMesh;
        }

        private Mesh BuildRibbonMesh(RunningRouteSegment current, RunningRouteSegment next)
        {
            float currentHalfWidth = current.width * 0.5f;
            float nextHalfWidth = next.width * 0.5f;

            Vector3 currentRight = Vector3.Cross(Vector3.up, current.forward).normalized;
            Vector3 nextRight = Vector3.Cross(Vector3.up, next.forward).normalized;
            Vector3 miterDirection = (currentRight + nextRight).normalized;
            float denominator = Mathf.Max(0.01f, Vector3.Dot(miterDirection, currentRight));
            Vector3 jointOffset = miterDirection * (Mathf.Min(currentHalfWidth, nextHalfWidth) / denominator);

            Vector3 joint = current.End;
            Vector3 up = Vector3.up * height;
            Vector3[] vertices =
            {
                current.start - currentRight * currentHalfWidth + up,
                current.start + currentRight * currentHalfWidth + up,
                joint - jointOffset + up,
                joint + jointOffset + up,
                next.End - nextRight * nextHalfWidth + up,
                next.End + nextRight * nextHalfWidth + up
            };

            int[] triangles =
            {
                0, 2, 1,
                1, 2, 3,
                2, 4, 3,
                3, 4, 5
            };

            Vector2[] uv =
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, current.length),
                new Vector2(1f, current.length),
                new Vector2(0f, current.length + next.length),
                new Vector2(1f, current.length + next.length)
            };

            Mesh mesh = new Mesh { name = "Continuous Running Track Mesh" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.uv = uv;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void ClearRibbon()
        {
            if (renderedRibbon != null)
            {
                Destroy(renderedRibbon);
                renderedRibbon = null;
            }

            if (renderedMesh != null)
            {
                Destroy(renderedMesh);
                renderedMesh = null;
            }
        }

        private void OnDestroy()
        {
            ClearRibbon();
        }
    }
}
