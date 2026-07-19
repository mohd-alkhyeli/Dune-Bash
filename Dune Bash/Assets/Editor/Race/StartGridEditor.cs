using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Scene-view point dragging + facing preview for StartGrid, plus Add/Remove/Rebuild buttons - same
/// workflow as FenceSplineEditor and CheckpointCircuitEditor.
/// </summary>
[CustomEditor(typeof(StartGrid))]
public class StartGridEditor : Editor
{
    void OnSceneGUI()
    {
        StartGrid grid = (StartGrid)target;

        if (grid.gridOrigin != null)
        {
            Handles.color = Color.magenta;
            EditorGUI.BeginChangeCheck();
            Vector3 newOriginPos = Handles.PositionHandle(grid.gridOrigin.position, grid.gridOrigin.rotation);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(grid.gridOrigin, "Move Start Grid Origin");
                grid.gridOrigin.position = newOriginPos;
            }
            Handles.Label(grid.gridOrigin.position + Vector3.up * 1.2f, "Grid Origin");
        }

        if (grid.points == null) return;

        Handles.color = Color.green;
        for (int i = 0; i < grid.points.Length; i++)
        {
            Transform point = grid.points[i];
            if (point == null) continue;

            EditorGUI.BeginChangeCheck();
            Vector3 newPos = Handles.PositionHandle(point.position, point.rotation);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(point, "Move Start Grid Point");
                point.position = newPos;
            }

            Handles.Label(point.position + Vector3.up * 0.6f, $"Grid {i}");
        }
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        StartGrid grid = (StartGrid)target;

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Add Spawn Point")) AddPoint(grid);
            if (GUILayout.Button("Remove Last Spawn Point")) RemoveLastPoint(grid);
        }

        if (GUILayout.Button("Rebuild Grid")) grid.Rebuild();

        if (grid.gridOrigin == null && GUILayout.Button("Create Grid Origin")) CreateGridOrigin(grid);

        EditorGUILayout.Space();
        if (GUILayout.Button("Generate Grid Layout (replaces existing points)")) grid.GenerateGrid();
    }

    static void CreateGridOrigin(StartGrid grid)
    {
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Create Start Grid Origin");

        GameObject go = new GameObject("GridOrigin");
        Checkpoint startCheckpoint = RaceConfig.StartCheckpoint;
        go.transform.position = startCheckpoint != null ? startCheckpoint.transform.position : grid.transform.position;
        go.transform.SetParent(grid.transform, true);
        Undo.RegisterCreatedObjectUndo(go, "Create Start Grid Origin");

        Undo.RecordObject(grid, "Assign Start Grid Origin");
        grid.gridOrigin = go.transform;

        Undo.CollapseUndoOperations(group);
        EditorUtility.SetDirty(grid);
    }

    static void AddPoint(StartGrid grid)
    {
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Add Start Grid Point");

        int count = grid.points != null ? grid.points.Length : 0;
        GameObject go = new GameObject($"GridPoint_{count}");

        Vector3 pos = grid.transform.position;
        if (count >= 1 && grid.points[count - 1] != null)
        {
            // Offset sideways from the last point rather than stacking on top of it.
            pos = grid.points[count - 1].position + grid.points[count - 1].right * 3f;
        }
        go.transform.position = pos;
        go.transform.SetParent(grid.transform, true);
        Undo.RegisterCreatedObjectUndo(go, "Create Start Grid Point");

        Undo.RecordObject(grid, "Add Start Grid Point");
        List<Transform> list = grid.points != null ? new List<Transform>(grid.points) : new List<Transform>();
        list.Add(go.transform);
        grid.points = list.ToArray();

        Undo.CollapseUndoOperations(group);
        EditorUtility.SetDirty(grid);
    }

    static void RemoveLastPoint(StartGrid grid)
    {
        if (grid.points == null || grid.points.Length == 0) return;

        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Remove Start Grid Point");

        Transform last = grid.points[grid.points.Length - 1];

        Undo.RecordObject(grid, "Remove Start Grid Point");
        List<Transform> list = new List<Transform>(grid.points);
        list.RemoveAt(list.Count - 1);
        grid.points = list.ToArray();

        if (last != null) Undo.DestroyObjectImmediate(last.gameObject);

        Undo.CollapseUndoOperations(group);
        EditorUtility.SetDirty(grid);
    }
}
