using UnityEngine;

/// <summary>
/// A trigger volume marking one point on a track. Stores which checkpoint(s) are valid to reach
/// immediately after this one, so a track is a graph rather than a strict linear sequence - a plain
/// loop just has one entry per checkpoint, while a fork/shortcut lists more than one, each a valid
/// route onward that's still tracked correctly by LapTracker.
/// </summary>
[RequireComponent(typeof(Collider))]
public class Checkpoint : MonoBehaviour
{
    [Tooltip("Checkpoint(s) a car is allowed to reach immediately after this one. A plain loop has exactly one; a fork/shortcut lists more than one, each a valid route onward.")]
    public Checkpoint[] nextCheckpoints;
    [Tooltip("How far along the course this checkpoint is, in whatever increasing unit you like (checkpoint order, meters from start, etc.) - used only to rank cars for standings (1st/2nd/...). Assign these in increasing order around the track. On a fork, give both branches a similar value to their shared rejoin point since ranking across diverging routes is inherently approximate.")]
    public float trackProgress;

    [Header("Spawn Point")]
    [Tooltip("Optional manual override for where a car respawns near this checkpoint - drag a Transform positioned wherever you like and it's used exactly as placed (no auto offset). Leave unassigned to auto-place a spawn point off to the side of the gate instead.")]
    public Transform spawnPointOverride;
    [Tooltip("Sideways offset (m) from this checkpoint - across its facing direction - used to auto-place a spawn point off the racing line when Spawn Point Override is not set.")]
    public float autoSpawnSideOffset = 6f;

    void Reset()
    {
        Collider col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        LapTracker tracker = other.GetComponentInParent<LapTracker>();
        // enabled is checked explicitly - GetComponentInParent still finds disabled components, and
        // NotifyCheckpoint is a direct method call, not a lifecycle callback, so disabling a tracker
        // (e.g. an abandoned wreck after respawn) wouldn't otherwise stop it from still registering hits.
        if (tracker != null && tracker.enabled) tracker.NotifyCheckpoint(this);
    }

    /// <summary>True if this checkpoint is listed as a valid next step from the given checkpoint.</summary>
    public bool IsValidNextFrom(Checkpoint from)
    {
        if (from == null || from.nextCheckpoints == null) return false;
        foreach (Checkpoint next in from.nextCheckpoints)
            if (next == this) return true;
        return false;
    }

    /// <summary>
    /// Raw (not terrain-height-snapped) spawn position/rotation near this checkpoint - whatever spawns
    /// a car there is expected to ground-snap the returned position itself.
    /// </summary>
    public void GetSpawnPoint(out Vector3 position, out Quaternion rotation)
    {
        if (spawnPointOverride != null)
        {
            position = spawnPointOverride.position;
            rotation = spawnPointOverride.rotation;
            return;
        }

        position = transform.position + transform.right * autoSpawnSideOffset;
        rotation = transform.rotation;
    }

    void OnDrawGizmos()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        if (box == null) return;

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0f, 1f, 1f, 0.25f);
        Gizmos.DrawCube(box.center, box.size);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(box.center, box.size);
        Gizmos.matrix = Matrix4x4.identity;
    }
}
