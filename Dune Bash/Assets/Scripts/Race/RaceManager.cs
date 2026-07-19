using System.Collections.Generic;

/// <summary>
/// Registry of every active LapTracker, used to compute live race standings (1st, 2nd, ...). Cars
/// rank by lap count first, then by Checkpoint.trackProgress of their current checkpoint, then by
/// live progress through the segment toward their next checkpoint - in that priority order.
/// </summary>
public static class RaceManager
{
    static readonly List<LapTracker> activeTrackers = new List<LapTracker>();

    public static void Register(LapTracker tracker)
    {
        if (!activeTrackers.Contains(tracker)) activeTrackers.Add(tracker);
    }

    public static void Unregister(LapTracker tracker)
    {
        activeTrackers.Remove(tracker);
    }

    /// <summary>Every active LapTracker, unordered - cheaper than GetStandings() for callers that just need to iterate all cars (e.g. AI avoidance) rather than rank them.</summary>
    public static IReadOnlyList<LapTracker> GetActive() => activeTrackers;

    /// <summary>Every active LapTracker ordered from 1st place to last.</summary>
    public static List<LapTracker> GetStandings()
    {
        List<LapTracker> ranked = new List<LapTracker>(activeTrackers);
        ranked.Sort((a, b) =>
        {
            int lapCompare = b.LapCount.CompareTo(a.LapCount);
            if (lapCompare != 0) return lapCompare;

            float aProgress = a.CurrentCheckpoint != null ? a.CurrentCheckpoint.trackProgress : 0f;
            float bProgress = b.CurrentCheckpoint != null ? b.CurrentCheckpoint.trackProgress : 0f;
            int checkpointCompare = bProgress.CompareTo(aProgress);
            if (checkpointCompare != 0) return checkpointCompare;

            return b.SegmentProgress().CompareTo(a.SegmentProgress());
        });
        return ranked;
    }
}
