using UnityEngine;

/// <summary>
/// Tracks one vehicle's progress around a Checkpoint graph and counts laps. A lap completes when the
/// tracker returns to Start Checkpoint having genuinely left it via a valid checkpoint chain first
/// (so sitting on the start line at spawn doesn't itself count as a lap). The race is won once Laps
/// To Win laps are completed.
/// </summary>
public class LapTracker : MonoBehaviour
{
    [Tooltip("The start/finish checkpoint. Progress begins here, and returning to it (via any valid route through the checkpoint graph) completes a lap.")]
    public Checkpoint startCheckpoint;
    [Tooltip("Number of completed laps needed to win the race.")]
    public int lapsToWin = 3;

    public int LapCount { get; private set; }
    public bool HasWon { get; private set; }
    /// <summary>The last checkpoint reached - exposed read-only, e.g. for a future AI driver to steer toward Current Checkpoint.nextCheckpoints.</summary>
    public Checkpoint CurrentCheckpoint { get; private set; }

    bool hasLeftStart;

    void OnEnable()
    {
        RaceManager.Register(this);
    }

    void OnDisable()
    {
        RaceManager.Unregister(this);
    }

    void Awake()
    {
        // Awake (not Start) so this is already correct by the time code right after Instantiate()
        // runs, e.g. CarSpawner.Initialize/CopyProgressFrom - Start would fire too late and clobber it.
        CurrentCheckpoint = startCheckpoint;
    }

    /// <summary>Sets up a freshly spawned tracker with no prior progress - used for a car's first spawn.</summary>
    public void Initialize(Checkpoint start)
    {
        startCheckpoint = start;
        CurrentCheckpoint = start;
        LapCount = 0;
        HasWon = false;
        hasLeftStart = false;
    }

    /// <summary>Carries over another tracker's progress - used when a car respawns so a crash doesn't cost race position.</summary>
    public void CopyProgressFrom(LapTracker other)
    {
        if (other == null) return;
        startCheckpoint = other.startCheckpoint;
        CurrentCheckpoint = other.CurrentCheckpoint;
        LapCount = other.LapCount;
        HasWon = other.HasWon;
        hasLeftStart = other.hasLeftStart;
    }

    public void NotifyCheckpoint(Checkpoint checkpoint)
    {
        if (HasWon || checkpoint == null || checkpoint == CurrentCheckpoint) return;

        // Ignores wrong-way hits and skipped checkpoints rather than penalizing them - a car that
        // isn't following a valid graph edge from where it currently is just doesn't make progress.
        if (!checkpoint.IsValidNextFrom(CurrentCheckpoint)) return;

        CurrentCheckpoint = checkpoint;

        if (checkpoint != startCheckpoint)
        {
            hasLeftStart = true;
            return;
        }

        if (!hasLeftStart) return; // reached start without having left it yet - not a lap

        hasLeftStart = false;
        LapCount++;
        if (LapCount >= lapsToWin) HasWon = true;
    }

    /// <summary>
    /// 0-1 progress through the segment from Current Checkpoint toward its (first) next checkpoint,
    /// based on straight-line distance closed so far. Used only to break standings ties between two
    /// cars that share the same checkpoint/lap - not meant to be a precise route-distance metric.
    /// </summary>
    public float SegmentProgress()
    {
        if (CurrentCheckpoint == null || CurrentCheckpoint.nextCheckpoints == null || CurrentCheckpoint.nextCheckpoints.Length == 0)
            return 0f;

        Checkpoint next = CurrentCheckpoint.nextCheckpoints[0];
        if (next == null) return 0f;

        float segmentLength = Vector3.Distance(CurrentCheckpoint.transform.position, next.transform.position);
        if (segmentLength < 0.01f) return 0f;

        float distanceToNext = Vector3.Distance(transform.position, next.transform.position);
        return Mathf.Clamp01(1f - distanceToNext / segmentLength);
    }
}
