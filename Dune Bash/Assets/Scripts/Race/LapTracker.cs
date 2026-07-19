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
    /// used only to break standings ties between two cars that share the same checkpoint/lap. Measured
    /// by projecting the car's position onto the segment line (not raw distance to the next checkpoint) -
    /// distance-to-endpoint alone falsely rewards a car that's merely geometrically close to the next
    /// checkpoint (e.g. sitting off to the side early in the segment) over one that's actually travelled
    /// further along it. Still assumes the segment is a straight line between the two checkpoints, so a
    /// wide curve between them is only approximated - genuine curve accuracy would need denser
    /// checkpoints along the curve, not a smarter formula here.
    /// </summary>
    public float SegmentProgress()
    {
        if (CurrentCheckpoint == null || CurrentCheckpoint.nextCheckpoints == null || CurrentCheckpoint.nextCheckpoints.Length == 0)
            return 0f;

        Checkpoint next = CurrentCheckpoint.nextCheckpoints[0];
        if (next == null) return 0f;

        Vector3 segment = next.transform.position - CurrentCheckpoint.transform.position;
        float segmentLengthSqr = segment.sqrMagnitude;
        if (segmentLengthSqr < 0.0001f) return 0f;

        Vector3 toCar = transform.position - CurrentCheckpoint.transform.position;
        float t = Vector3.Dot(toCar, segment) / segmentLengthSqr;
        return Mathf.Clamp01(t);
    }

    /// <summary>
    /// World-space point Lookahead Distance (m) ahead of this car along the checkpoint chain - the aim
    /// point for Pure Pursuit steering. Starts by projecting the car onto its current segment (same
    /// projection SegmentProgress uses) and walks forward through subsequent checkpoints if the
    /// distance runs past the current segment's end, so a large lookahead on a track with closely
    /// spaced checkpoints still resolves to a real point on the path rather than stopping short at the
    /// next checkpoint. On a fork, always follows nextCheckpoints[0] - the same approximation already
    /// accepted everywhere else this graph is walked.
    /// </summary>
    public Vector3 GetLookaheadPoint(float lookaheadDistance)
    {
        Checkpoint from = CurrentCheckpoint;
        if (from == null) return transform.position;

        Checkpoint to = NextOf(from);
        if (to == null) return transform.position;

        Vector3 a = from.transform.position;
        Vector3 b = to.transform.position;
        Vector3 segment = b - a;
        float segmentLength = segment.magnitude;

        Vector3 fallback = b;
        float remaining;

        if (segmentLength < 0.0001f)
        {
            remaining = lookaheadDistance;
        }
        else
        {
            float t = Mathf.Clamp01(Vector3.Dot(transform.position - a, segment) / (segmentLength * segmentLength));
            float distanceRemainingInSegment = segmentLength * (1f - t);

            if (lookaheadDistance <= distanceRemainingInSegment)
                return a + segment * (t + lookaheadDistance / segmentLength);

            remaining = lookaheadDistance - distanceRemainingInSegment;
        }

        // Lookahead point falls beyond this segment - keep walking the chain. The track is a closed
        // loop so this always terminates; the guard only protects against a malformed graph.
        Checkpoint current = to;
        for (int i = 0; i < 64 && remaining > 0f; i++)
        {
            Checkpoint nextCheckpoint = NextOf(current);
            if (nextCheckpoint == null) break;

            Vector3 segStart = current.transform.position;
            Vector3 segEnd = nextCheckpoint.transform.position;
            float segLength = Vector3.Distance(segStart, segEnd);

            if (segLength < 0.0001f) { current = nextCheckpoint; continue; }

            if (remaining <= segLength)
                return segStart + (segEnd - segStart) * (remaining / segLength);

            remaining -= segLength;
            fallback = segEnd;
            current = nextCheckpoint;
        }

        return fallback;
    }

    static Checkpoint NextOf(Checkpoint checkpoint)
    {
        if (checkpoint == null || checkpoint.nextCheckpoints == null || checkpoint.nextCheckpoints.Length == 0) return null;
        return checkpoint.nextCheckpoints[0];
    }
}
