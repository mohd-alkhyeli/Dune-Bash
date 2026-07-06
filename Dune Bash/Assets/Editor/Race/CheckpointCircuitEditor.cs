using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Scene-view node dragging + loop preview for CheckpointCircuit, plus Add Node/Remove Last Node/
/// Rebuild Circuit buttons - same workflow as FenceSplineEditor.
/// </summary>
[CustomEditor(typeof(CheckpointCircuit))]
public class CheckpointCircuitEditor : Editor
{
    void OnSceneGUI()
    {
        CheckpointCircuit circuit = (CheckpointCircuit)target;
        if (circuit.nodes == null) return;

        Handles.color = Color.yellow;
        for (int i = 0; i < circuit.nodes.Length; i++)
        {
            Transform node = circuit.nodes[i];
            if (node == null) continue;

            EditorGUI.BeginChangeCheck();
            Vector3 newPos = Handles.PositionHandle(node.position, node.rotation);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(node, "Move Checkpoint Node");
                node.position = newPos;
            }

            Handles.Label(node.position + Vector3.up * 0.5f, $"Checkpoint {i}");
        }

        List<Transform> valid = new List<Transform>();
        foreach (Transform n in circuit.nodes) if (n != null) valid.Add(n);

        if (valid.Count >= 2)
        {
            Handles.color = Color.cyan;
            for (int i = 0; i < valid.Count; i++)
            {
                Vector3 a = valid[i].position;
                Vector3 b = valid[(i + 1) % valid.Count].position; // wraps to close the loop
                Handles.DrawLine(a, b);
            }
        }
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        CheckpointCircuit circuit = (CheckpointCircuit)target;

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Add Node")) AddNode(circuit);
            if (GUILayout.Button("Remove Last Node")) RemoveLastNode(circuit);
        }

        if (GUILayout.Button("Rebuild Circuit")) circuit.Rebuild();
    }

    static void AddNode(CheckpointCircuit circuit)
    {
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Add Checkpoint Node");

        int count = circuit.nodes != null ? circuit.nodes.Length : 0;
        GameObject go = new GameObject($"Checkpoint_{count}");

        Vector3 pos = circuit.transform.position;
        if (count == 1 && circuit.nodes[0] != null)
        {
            pos = circuit.nodes[0].position + Vector3.forward * 20f;
        }
        else if (count >= 2 && circuit.nodes[count - 1] != null && circuit.nodes[count - 2] != null)
        {
            Vector3 last = circuit.nodes[count - 1].position;
            Vector3 dir = last - circuit.nodes[count - 2].position;
            pos = last + dir;
        }
        go.transform.position = pos;
        go.transform.SetParent(circuit.transform, true);
        Undo.RegisterCreatedObjectUndo(go, "Create Checkpoint Node");

        Undo.RecordObject(circuit, "Add Checkpoint Node");
        List<Transform> list = circuit.nodes != null ? new List<Transform>(circuit.nodes) : new List<Transform>();
        list.Add(go.transform);
        circuit.nodes = list.ToArray();

        Undo.CollapseUndoOperations(group);
        EditorUtility.SetDirty(circuit);
    }

    static void RemoveLastNode(CheckpointCircuit circuit)
    {
        if (circuit.nodes == null || circuit.nodes.Length == 0) return;

        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Remove Checkpoint Node");

        Transform last = circuit.nodes[circuit.nodes.Length - 1];

        Undo.RecordObject(circuit, "Remove Checkpoint Node");
        List<Transform> list = new List<Transform>(circuit.nodes);
        list.RemoveAt(list.Count - 1);
        circuit.nodes = list.ToArray();

        if (last != null) Undo.DestroyObjectImmediate(last.gameObject);

        Undo.CollapseUndoOperations(group);
        EditorUtility.SetDirty(circuit);
    }
}
