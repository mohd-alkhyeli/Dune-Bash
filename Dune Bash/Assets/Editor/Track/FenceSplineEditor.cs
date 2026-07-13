using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Scene-view node dragging + curve preview for FenceSpline, plus Rebuild/Add Node/Remove Last Node
/// buttons so the whole "place nodes, see the curve, rebuild" workflow stays in one place.
/// </summary>
[CustomEditor(typeof(FenceSpline))]
[CanEditMultipleObjects]
public class FenceSplineEditor : Editor
{
    void OnSceneGUI()
    {
        FenceSpline fence = (FenceSpline)target;
        if (fence.nodes == null) return;

        Handles.color = Color.yellow;
        for (int i = 0; i < fence.nodes.Length; i++)
        {
            Transform node = fence.nodes[i];
            if (node == null) continue;

            EditorGUI.BeginChangeCheck();
            Vector3 newPos = Handles.PositionHandle(node.position, node.rotation);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(node, "Move Fence Node");
                node.position = newPos;
            }

            Handles.Label(node.position + Vector3.up * 0.3f, $"Node {i}");
        }

        List<Vector3> preview = fence.GetPreviewPoints();
        if (preview.Count >= 2)
        {
            Handles.color = Color.cyan;
            for (int i = 0; i < preview.Count - 1; i++)
                Handles.DrawLine(preview[i], preview[i + 1]);
        }
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Add Node")) ForEachFence(AddNode);
            if (GUILayout.Button("Remove Last Node")) ForEachFence(RemoveLastNode);
        }

        if (GUILayout.Button("Drop Nodes To Terrain")) ForEachFence(DropNodesToTerrain);

        if (GUILayout.Button("Rebuild Fence")) ForEachFence(f => f.Rebuild());
    }

    /// <summary>Runs an action on every selected FenceSpline, so the buttons work with multiple fences selected at once (e.g. an inner and outer boundary).</summary>
    void ForEachFence(System.Action<FenceSpline> action)
    {
        foreach (Object t in targets)
        {
            FenceSpline fence = t as FenceSpline;
            if (fence != null) action(fence);
        }
    }

    /// <summary>
    /// Rebuilds every FenceSpline in the open scene regardless of what's currently selected - a
    /// guaranteed-to-work alternative to multi-selecting in the Inspector, since Unity's multi-object
    /// editing can be inconsistent about which selected instance a button call actually reaches. Only
    /// calls the existing Rebuild() on each one - node data is untouched either way.
    /// </summary>
    [MenuItem("Dune Bash/Rebuild All Fences")]
    static void RebuildAllFences()
    {
        FenceSpline[] fences = Object.FindObjectsByType<FenceSpline>(FindObjectsSortMode.None);
        foreach (FenceSpline fence in fences) fence.Rebuild();
        Debug.Log($"Rebuilt {fences.Length} fence(s).");
    }

    static void DropNodesToTerrain(FenceSpline fence)
    {
        if (fence.terrainCollider == null)
        {
            Debug.LogWarning("Assign a Terrain Collider on the FenceSpline before dropping nodes.", fence);
            return;
        }
        if (fence.nodes == null || fence.nodes.Length == 0) return;

        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Drop Fence Nodes To Terrain");

        int missed = 0;
        foreach (Transform node in fence.nodes)
        {
            if (node == null) continue;
            Undo.RecordObject(node, "Snap Fence Node To Terrain");
            if (!fence.TrySnapNodeToTerrain(node)) missed++;
        }

        Undo.CollapseUndoOperations(group);

        if (missed > 0)
            Debug.LogWarning($"{missed} fence node(s) had no terrain hit within range and were left unchanged.", fence);

        fence.Rebuild();
    }

    static void AddNode(FenceSpline fence)
    {
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Add Fence Node");

        int count = fence.nodes != null ? fence.nodes.Length : 0;
        GameObject go = new GameObject($"Node {count}");

        Vector3 pos = fence.transform.position;
        if (count == 1 && fence.nodes[0] != null)
        {
            pos = fence.nodes[0].position + Vector3.forward * 5f;
        }
        else if (count >= 2 && fence.nodes[count - 1] != null && fence.nodes[count - 2] != null)
        {
            Vector3 last = fence.nodes[count - 1].position;
            Vector3 dir = last - fence.nodes[count - 2].position;
            pos = last + dir;
        }
        go.transform.position = pos;
        go.transform.SetParent(fence.transform, true);
        Undo.RegisterCreatedObjectUndo(go, "Create Fence Node");

        Undo.RecordObject(fence, "Add Fence Node");
        List<Transform> list = fence.nodes != null ? new List<Transform>(fence.nodes) : new List<Transform>();
        list.Add(go.transform);
        fence.nodes = list.ToArray();

        Undo.CollapseUndoOperations(group);
        EditorUtility.SetDirty(fence);
    }

    static void RemoveLastNode(FenceSpline fence)
    {
        if (fence.nodes == null || fence.nodes.Length == 0) return;

        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Remove Fence Node");

        Transform last = fence.nodes[fence.nodes.Length - 1];

        Undo.RecordObject(fence, "Remove Fence Node");
        List<Transform> list = new List<Transform>(fence.nodes);
        list.RemoveAt(list.Count - 1);
        fence.nodes = list.ToArray();

        if (last != null) Undo.DestroyObjectImmediate(last.gameObject);

        Undo.CollapseUndoOperations(group);
        EditorUtility.SetDirty(fence);
    }
}
