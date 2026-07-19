using UnityEngine;

/// <summary>
/// Turns a hand-placed sequence of nodes into a working checkpoint loop: adds a BoxCollider +
/// Checkpoint to each node, wires Next Checkpoints in order (wrapping the last node back to the
/// first, since lap counting needs a closed loop), assigns increasing trackProgress values, renames
/// each node to match its final position in the sequence, and orients each gate across the direction
/// of travel. The rename step exists because Add Node only names a node once at creation time - after
/// nodes get reordered/inserted/removed a few times, those creation-time names no longer match actual
/// track order, which Rebuild now corrects every time it runs. Everything only (re)builds when Rebuild
/// is pressed - same workflow as FenceSpline. Point CarSpawner's Start Checkpoint at nodes[0]'s
/// Checkpoint once built - a LapTracker is spawned fresh per car now, so there's no persistent scene
/// tracker here.
/// </summary>
public class CheckpointCircuit : MonoBehaviour
{
    [Header("Nodes")]
    [Tooltip("Checkpoints in order around the course. A closed loop is assumed - the last node's Next Checkpoint automatically wraps back to the first, since completing a lap means returning to the start.")]
    public Transform[] nodes;

    [Header("Gate")]
    [Tooltip("Width (m) of each checkpoint's trigger gate, across the direction of travel.")]
    public float gateWidth = 10f;
    [Tooltip("Height (m) of each checkpoint's trigger gate.")]
    public float gateHeight = 5f;
    [Tooltip("Depth (m) of each checkpoint's trigger gate along the direction of travel - keep this generous so a fast car can't skip through untouched between physics steps.")]
    public float gateDepth = 2f;

    public void Rebuild()
    {
        if (nodes == null || nodes.Length < 2)
        {
            Debug.LogWarning("CheckpointCircuit needs at least 2 nodes to build.", this);
            return;
        }
        foreach (Transform n in nodes)
        {
            if (n == null)
            {
                Debug.LogWarning("CheckpointCircuit has an empty node slot - fill or remove it before rebuilding.", this);
                return;
            }
        }

        Checkpoint[] checkpoints = new Checkpoint[nodes.Length];
        for (int i = 0; i < nodes.Length; i++)
        {
            checkpoints[i] = EnsureCheckpoint(nodes[i]);
            checkpoints[i].trackProgress = i;
            nodes[i].name = $"Checkpoint_{i}";
        }

        for (int i = 0; i < nodes.Length; i++)
        {
            Checkpoint next = checkpoints[(i + 1) % checkpoints.Length];
            checkpoints[i].nextCheckpoints = new[] { next };

            Vector3 dir = next.transform.position - nodes[i].position;
            if (dir.sqrMagnitude > 0.0001f)
                nodes[i].rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        }
    }

    Checkpoint EnsureCheckpoint(Transform node)
    {
        BoxCollider box = node.GetComponent<BoxCollider>();
        if (box == null) box = node.gameObject.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(gateWidth, gateHeight, gateDepth);
        box.center = Vector3.zero;

        Checkpoint checkpoint = node.GetComponent<Checkpoint>();
        if (checkpoint == null) checkpoint = node.gameObject.AddComponent<Checkpoint>();
        return checkpoint;
    }
}
