using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generates a soft-boundary chain-link fence along a hand-placed sequence of nodes: a Catmull-Rom
/// curve runs through every node, a flat textured sheet follows it for the visual fence, a post is
/// placed at each node, and a chain of box colliders follows the same curve for collision. Everything
/// is (re)built on demand by pressing Rebuild in the inspector/scene tool - nothing regenerates at
/// runtime, since boundary geometry is static level dressing.
/// </summary>
public class FenceSpline : MonoBehaviour
{
    [Header("Nodes")]
    [Tooltip("Fence posts in order along the boundary. A Catmull-Rom curve is drawn through these points, so add more nodes wherever you need a tighter bend. Move them freely in the Scene view - press Rebuild afterward to regenerate the mesh/colliders.")]
    public Transform[] nodes;

    [Header("Posts")]
    [Tooltip("Prefab instantiated at each node as the visible post. Leave unassigned to fall back to a plain generated cylinder.")]
    public GameObject postPrefab;
    [Tooltip("Height (m) of the fallback cylinder used when Post Prefab is unassigned.")]
    public float fallbackPostHeight = 2f;
    [Tooltip("Radius (m) of the fallback cylinder used when Post Prefab is unassigned.")]
    public float fallbackPostRadius = 0.08f;

    [Header("Fence Sheet")]
    [Tooltip("Material applied to the generated fence sheet. Use a chain-link cutout/transparent texture on a URP Unlit or Lit shader with Render Face set to Both, so the fence reads as flimsy and is visible from either side.")]
    public Material fenceMaterial;
    [Tooltip("Height (m) of the fence sheet from the ground to its top edge.")]
    public float fenceHeight = 1.6f;
    [Tooltip("World-space distance (m) the fence texture tiles over horizontally - lower this if the chain-link pattern looks stretched, raise it if the pattern looks too busy/repetitive.")]
    public float textureTileLength = 2f;
    [Tooltip("How many points are sampled along the curve between each pair of nodes. Higher gives a smoother curve on tight bends at the cost of more triangles and more collider segments.")]
    [Range(2, 32)] public int samplesPerSegment = 8;

    [Header("Collision")]
    [Tooltip("Thickness (m) of each generated box collider segment. Keep this generous relative to how fast cars can hit the fence - a too-thin collider risks a fast-moving car tunneling straight through between physics steps.")]
    public float colliderThickness = 0.3f;

    [Header("Terrain Snap")]
    [Tooltip("Collider to drop nodes onto with the Drop Nodes To Terrain button in the inspector - drag in your terrain/ground object's collider. Only a node's height (Y) is changed; X/Z stay exactly where you placed them.")]
    public Collider terrainCollider;
    [Tooltip("How far above a node to start searching downward for the terrain surface (and how far below it to keep searching) when snapping. Raise this if a node sits far above or below the terrain and the snap fails to find it.")]
    public float snapSearchHeight = 200f;

    [SerializeField, HideInInspector] Transform postsContainer;
    [SerializeField, HideInInspector] Transform collidersContainer;
    [SerializeField, HideInInspector] MeshFilter meshFilter;
    [SerializeField, HideInInspector] MeshRenderer meshRenderer;

    [ContextMenu("Rebuild Fence")]
    public void Rebuild()
    {
        if (nodes == null || nodes.Length < 2)
        {
            Debug.LogWarning("FenceSpline needs at least 2 nodes to build.", this);
            return;
        }
        foreach (Transform n in nodes)
        {
            if (n == null)
            {
                Debug.LogWarning("FenceSpline has an empty node slot - fill or remove it before rebuilding.", this);
                return;
            }
        }

        if (meshFilter == null) meshFilter = GetComponent<MeshFilter>();
        if (meshFilter == null) meshFilter = gameObject.AddComponent<MeshFilter>();
        if (meshRenderer == null) meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer == null) meshRenderer = gameObject.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = fenceMaterial;

        postsContainer = EnsureContainer("Posts");
        collidersContainer = EnsureContainer("Colliders");

        List<Vector3> curvePoints = SampleCurve();
        BuildMesh(curvePoints);
        BuildPosts();
        BuildColliders(curvePoints);
    }

    /// <summary>
    /// Raycasts straight down (and up, via the search range) from the node's current X/Z onto
    /// Terrain Collider and moves the node's Y to match. Leaves X/Z untouched. Returns false if
    /// nothing was hit within Snap Search Height.
    /// </summary>
    public bool TrySnapNodeToTerrain(Transform node)
    {
        if (node == null) return false;
        if (!RaycastTerrainHeight(node.position, out float y)) return false;

        node.position = new Vector3(node.position.x, y, node.position.z);
        return true;
    }

    /// <summary>
    /// Raycasts straight down from above the given point onto Terrain Collider. Used to correct
    /// height everywhere along the fence (not just at nodes) so the sheet/posts/colliders hug the
    /// ground even where terrain rises or dips between two nodes.
    /// </summary>
    bool RaycastTerrainHeight(Vector3 point, out float height)
    {
        height = point.y;
        if (terrainCollider == null) return false;

        Vector3 origin = new Vector3(point.x, point.y + snapSearchHeight, point.z);
        if (terrainCollider.Raycast(new Ray(origin, Vector3.down), out RaycastHit hit, snapSearchHeight * 2f))
        {
            height = hit.point.y;
            return true;
        }
        return false;
    }

    Vector3 ProjectToTerrain(Vector3 point)
    {
        RaycastTerrainHeight(point, out float y);
        return new Vector3(point.x, y, point.z);
    }

    /// <summary>Curve points for scene-view preview drawing - same sampling Rebuild uses.</summary>
    public List<Vector3> GetPreviewPoints()
    {
        if (nodes == null || nodes.Length < 2) return new List<Vector3>();
        foreach (Transform n in nodes) if (n == null) return new List<Vector3>();
        return SampleCurve();
    }

    List<Vector3> SampleCurve()
    {
        List<Vector3> points = new List<Vector3>();
        int count = nodes.Length;

        for (int i = 0; i < count - 1; i++)
        {
            Vector3 p0 = nodes[Mathf.Max(i - 1, 0)].position;
            Vector3 p1 = nodes[i].position;
            Vector3 p2 = nodes[i + 1].position;
            Vector3 p3 = nodes[Mathf.Min(i + 2, count - 1)].position;

            int startJ = i == 0 ? 0 : 1; // skip j=0 after the first segment - it's the same point the previous segment ended on
            for (int j = startJ; j <= samplesPerSegment; j++)
            {
                float t = j / (float)samplesPerSegment;
                // Re-project onto the terrain rather than trusting the curve's interpolated height -
                // otherwise a dune rising between two correctly-placed nodes pokes through the fence.
                points.Add(ProjectToTerrain(CatmullRom(p0, p1, p2, p3, t)));
            }
        }
        return points;
    }

    static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        return 0.5f * (
            2f * p1 +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
    }

    void BuildMesh(List<Vector3> worldPoints)
    {
        int n = worldPoints.Count;
        Vector3[] verts = new Vector3[n * 2];
        Vector2[] uvs = new Vector2[n * 2];
        float dist = 0f;

        for (int i = 0; i < n; i++)
        {
            if (i > 0) dist += Vector3.Distance(worldPoints[i - 1], worldPoints[i]);
            Vector3 local = transform.InverseTransformPoint(worldPoints[i]);
            verts[i * 2] = local;
            verts[i * 2 + 1] = local + Vector3.up * fenceHeight;

            float u = dist / Mathf.Max(0.01f, textureTileLength);
            uvs[i * 2] = new Vector2(u, 0f);
            uvs[i * 2 + 1] = new Vector2(u, 1f);
        }

        int[] tris = new int[(n - 1) * 6];
        int ti = 0;
        for (int i = 0; i < n - 1; i++)
        {
            int bl = i * 2, tl = i * 2 + 1, br = (i + 1) * 2, tr = (i + 1) * 2 + 1;
            tris[ti++] = bl; tris[ti++] = tl; tris[ti++] = br;
            tris[ti++] = tl; tris[ti++] = tr; tris[ti++] = br;
        }

        Mesh mesh = meshFilter.sharedMesh;
        if (mesh == null)
        {
            mesh = new Mesh { name = "FenceSheet" };
            meshFilter.sharedMesh = mesh;
        }
        mesh.Clear();
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    void BuildPosts()
    {
        ClearContainer(postsContainer);
        foreach (Transform node in nodes)
        {
            // Project each post onto the terrain too, rather than trusting the node's raw stored
            // height - keeps posts sitting on the ground even if a node was placed slightly off, or
            // the terrain was reshaped since the node was last snapped.
            Vector3 basePos = ProjectToTerrain(node.position);

            GameObject post;
            if (postPrefab != null)
            {
                post = Instantiate(postPrefab, basePos, node.rotation, postsContainer);
            }
            else
            {
                post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                post.transform.SetParent(postsContainer, false);
                post.transform.position = basePos + Vector3.up * (fallbackPostHeight * 0.5f);
                post.transform.localScale = new Vector3(fallbackPostRadius * 2f, fallbackPostHeight * 0.5f, fallbackPostRadius * 2f);
                SafeDestroy(post.GetComponent<Collider>());
            }
            post.name = "Post";
        }
    }

    void BuildColliders(List<Vector3> worldPoints)
    {
        ClearContainer(collidersContainer);
        for (int i = 0; i < worldPoints.Count - 1; i++)
        {
            Vector3 a = worldPoints[i];
            Vector3 b = worldPoints[i + 1];
            float length = Vector3.Distance(a, b);
            if (length < 0.001f) continue;

            GameObject go = new GameObject($"Collider_{i}");
            go.transform.SetParent(collidersContainer, false);
            go.transform.position = (a + b) * 0.5f + Vector3.up * (fenceHeight * 0.5f);
            go.transform.rotation = Quaternion.LookRotation((b - a).normalized, Vector3.up);

            BoxCollider box = go.AddComponent<BoxCollider>();
            // Extend slightly past the segment length so neighboring boxes overlap through bends instead of leaving seam gaps.
            box.size = new Vector3(colliderThickness, fenceHeight, length + colliderThickness);
        }
    }

    Transform EnsureContainer(string name)
    {
        Transform t = transform.Find(name);
        if (t == null)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(transform, false);
            t = go.transform;
        }
        return t;
    }

    void ClearContainer(Transform container)
    {
        for (int i = container.childCount - 1; i >= 0; i--)
            SafeDestroy(container.GetChild(i).gameObject);
    }

    static void SafeDestroy(Object obj)
    {
        if (obj == null) return;
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            DestroyImmediate(obj);
            return;
        }
#endif
        Destroy(obj);
    }
}
