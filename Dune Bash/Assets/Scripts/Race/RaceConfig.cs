using UnityEngine;

/// <summary>
/// Single shared source of truth for values every spawner/track tool needs to agree on: which
/// collider counts as "the ground", and which checkpoint/grid mark the race start. Centralizes what
/// used to be separate fields independently duplicated across FenceSpline, StartGrid, CarSpawner, and
/// AISpawner - assign each one once here instead of reassigning every consumer whenever the terrain or
/// start setup changes (e.g. swapping the sand terrain for a flat testing plane). Runs in Edit Mode too
/// (ExecuteAlways) since FenceSpline's node/post terrain-snapping happens from an Editor button, not
/// just at runtime - a plain Awake()-only static would still be null when that button is pressed
/// outside Play Mode.
/// </summary>
[ExecuteAlways]
public class RaceConfig : MonoBehaviour
{
    [Tooltip("Collider treated as \"the ground\" for terrain-snapping spawn points and fence nodes/posts. Swapping terrain means reassigning this one field, not one per script.")]
    [SerializeField] Collider terrainCollider;
    [Tooltip("The race's start/finish checkpoint - used to orient the start grid and to initialize each freshly spawned car's LapTracker.")]
    [SerializeField] Checkpoint startCheckpoint;
    [Tooltip("The shared start grid spawners claim points from, so the player and AI can't double-book the same slot.")]
    [SerializeField] StartGrid startGrid;

    public static Collider TerrainCollider { get; private set; }
    public static Checkpoint StartCheckpoint { get; private set; }
    public static StartGrid StartGrid { get; private set; }

    void OnEnable()
    {
        TerrainCollider = terrainCollider;
        StartCheckpoint = startCheckpoint;
        StartGrid = startGrid;
    }

    void OnDisable()
    {
        // Only clear a value if we're the one who set it - avoids this instance's OnDisable wiping out
        // another RaceConfig's values (e.g. during an Editor domain reload's enable/disable churn).
        if (TerrainCollider == terrainCollider) TerrainCollider = null;
        if (StartCheckpoint == startCheckpoint) StartCheckpoint = null;
        if (StartGrid == startGrid) StartGrid = null;
    }
}
