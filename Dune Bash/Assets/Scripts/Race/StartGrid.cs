using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Race-start spawn positions, separate from Checkpoint's mid-race respawn points. Any number of
/// points is supported - add/remove via the editor tool. Rebuild snaps every point's height to the
/// terrain and forces all of them to face the same direction (Start Checkpoint's facing) so the grid
/// reads as a real starting line rather than points aimed wherever they were dragged. CarSpawner (and
/// later AISpawner) call ClaimRandomPoint() to get a unique, non-repeating point per competitor.
/// </summary>
public class StartGrid : MonoBehaviour
{
    [Header("Spawn Points")]
    [Tooltip("Race-start positions. Any count is supported. Only position matters when placing these - rotation is overwritten on Rebuild to keep the whole grid facing the same direction.")]
    public Transform[] points;

    [Header("Facing")]
    [Tooltip("All spawn points are rotated to match this checkpoint's facing on Rebuild, keeping the grid parallel. This checkpoint's own rotation already points toward its Next Checkpoints if it was built via CheckpointCircuit. Also used as the origin/orientation for Generate Grid Layout below.")]
    public Checkpoint startCheckpoint;

    [Header("Terrain")]
    [Tooltip("Used to ground-snap each spawn point's height. Leave unassigned to use each point's authored height as-is.")]
    public Collider terrainCollider;
    [Tooltip("How far above/below a spawn point to search for the terrain surface.")]
    public float terrainSearchHeight = 200f;

    [Header("Grid Layout")]
    [Tooltip("Where the grid is centered - drag this wherever you want the grid to sit. Facing still comes from Start Checkpoint, but position is entirely independent of it. Leave unassigned to fall back to Start Checkpoint's position (the old behavior).")]
    public Transform gridOrigin;
    [Tooltip("Columns (side to side) in the generated grid - the 'A' in an AxB layout. Shown as a live yellow wireframe preview even before you press Generate Grid Layout.")]
    [Min(1)] public int gridColumns = 2;
    [Tooltip("Rows (front to back) in the generated grid - the 'B' in an AxB layout.")]
    [Min(1)] public int gridRows = 2;
    [Tooltip("Distance (m) between adjacent columns, side to side, relative to Start Checkpoint's right vector.")]
    public float spacingX = 4f;
    [Tooltip("Distance (m) between adjacent rows, front to back, relative to Start Checkpoint's facing - row 0 sits at Start Checkpoint, each following row further behind it.")]
    public float spacingZ = 5f;

    readonly List<Transform> claimed = new List<Transform>();

    public void Rebuild()
    {
        if (points == null || points.Length == 0)
        {
            Debug.LogWarning("StartGrid has no points to build.", this);
            return;
        }

        Quaternion facing = startCheckpoint != null ? startCheckpoint.transform.rotation : transform.rotation;

        foreach (Transform point in points)
        {
            if (point == null) continue;

            point.rotation = facing;

            if (terrainCollider != null)
            {
                Vector3 origin = point.position + Vector3.up * terrainSearchHeight;
                if (terrainCollider.Raycast(new Ray(origin, Vector3.down), out RaycastHit hit, terrainSearchHeight * 2f))
                    point.position = new Vector3(point.position.x, hit.point.y, point.position.z);
            }
        }
    }

    /// <summary>
    /// Replaces Points with a fresh Grid Columns x Grid Rows layout, spaced by Spacing X/Z relative to
    /// Start Checkpoint's position and facing, then runs the same Rebuild (terrain snap + parallel
    /// facing) on the result. Destroys whatever node objects Points previously held.
    /// </summary>
    public void GenerateGrid()
    {
        if (startCheckpoint == null)
        {
            Debug.LogWarning("StartGrid needs Start Checkpoint assigned before it can generate a grid layout.", this);
            return;
        }

        ClearPoints();

        Vector3 origin = GridOrigin();
        Vector3 right = startCheckpoint.transform.right;
        Vector3 back = -startCheckpoint.transform.forward;

        List<Transform> generated = new List<Transform>();
        for (int r = 0; r < gridRows; r++)
        {
            for (int c = 0; c < gridColumns; c++)
            {
                Vector3 pos = origin + right * GridColumnOffset(c) + back * (r * spacingZ);

                GameObject go = new GameObject($"GridPoint_{r}_{c}");
                go.transform.SetParent(transform, true);
                go.transform.position = pos;
                generated.Add(go.transform);
            }
        }

        points = generated.ToArray();
        Rebuild();
    }

    float GridColumnOffset(int column)
    {
        return (column - (gridColumns - 1) / 2f) * spacingX;
    }

    Vector3 GridOrigin()
    {
        return gridOrigin != null ? gridOrigin.position : startCheckpoint.transform.position;
    }

    void ClearPoints()
    {
        if (points == null) return;
        foreach (Transform p in points)
            SafeDestroy(p != null ? p.gameObject : null);
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

    /// <summary>
    /// Returns a random point not yet claimed this race, marking it claimed. Returns null once every
    /// point is taken. Callers (CarSpawner, AISpawner) apply their own vehicle-specific height offset
    /// on top of the returned position - this only guarantees a unique, ground-snapped, correctly
    /// oriented slot.
    /// </summary>
    public Transform ClaimRandomPoint()
    {
        if (points == null) return null;

        List<Transform> available = new List<Transform>();
        foreach (Transform p in points)
            if (p != null && !claimed.Contains(p)) available.Add(p);

        if (available.Count == 0) return null;

        Transform chosen = available[Random.Range(0, available.Count)];
        claimed.Add(chosen);
        return chosen;
    }

    /// <summary>Clears all claims so every point becomes available again - call at the start of a new race.</summary>
    public void ResetClaims()
    {
        claimed.Clear();
    }

    void OnDrawGizmos()
    {
        if (points != null)
        {
            Gizmos.color = Color.green;
            foreach (Transform p in points)
            {
                if (p == null) continue;
                Gizmos.DrawSphere(p.position, 0.5f);
                Gizmos.DrawLine(p.position, p.position + p.forward * 3f);
            }
        }

        DrawGridPreview();
    }

    /// <summary>
    /// Live wireframe preview of what Generate Grid Layout would produce with the current Grid
    /// Columns/Rows/Spacing values - updates instantly as you edit them, no button press needed just
    /// to see the shape. Yellow and wireframe specifically to read as "not committed yet" next to the
    /// solid green of the real Points.
    /// </summary>
    void DrawGridPreview()
    {
        if (startCheckpoint == null) return;

        Vector3 origin = GridOrigin();
        Vector3 right = startCheckpoint.transform.right;
        Vector3 back = -startCheckpoint.transform.forward;

        Gizmos.color = Color.yellow;
        for (int r = 0; r < gridRows; r++)
        {
            for (int c = 0; c < gridColumns; c++)
            {
                Vector3 pos = origin + right * GridColumnOffset(c) + back * (r * spacingZ);

                if (terrainCollider != null)
                {
                    Vector3 rayOrigin = pos + Vector3.up * terrainSearchHeight;
                    if (terrainCollider.Raycast(new Ray(rayOrigin, Vector3.down), out RaycastHit hit, terrainSearchHeight * 2f))
                        pos.y = hit.point.y;
                }

                Gizmos.DrawWireSphere(pos, 0.6f);
            }
        }
    }
}
