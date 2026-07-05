using UnityEngine;
using UnityEditor;

/// <summary>
/// Mirrors the right-side wheels' X position onto the left side's, fixing an asymmetric track
/// width (as reported by DiagnoseWheelSetup) without touching Y/Z, rotation, or any WheelCollider
/// settings. Left (index 0/2) is treated as the reference; right (index 1/3) gets moved to match.
/// </summary>
public static class FixWheelSymmetry
{
    [MenuItem("Dune Bash/Mirror Right Wheels To Left (Selected Vehicle)")]
    public static void MirrorRightWheelsToLeft()
    {
        VehicleDriveTrain driveTrain = FindTarget();
        if (driveTrain == null)
        {
            Debug.LogError("FixWheelSymmetry: no VehicleDriveTrain found. Select the vehicle in the Hierarchy and run this again.");
            return;
        }

        if (driveTrain.wheels == null || driveTrain.wheels.Length < 4)
        {
            Debug.LogError($"FixWheelSymmetry: '{driveTrain.name}' does not have 4 wheels assigned.");
            return;
        }

        Undo.SetCurrentGroupName("Mirror Wheel X Positions");
        int undoGroup = Undo.GetCurrentGroup();

        MirrorPair(driveTrain.transform, driveTrain.wheels[0], driveTrain.wheels[1], "FL -> FR");
        MirrorPair(driveTrain.transform, driveTrain.wheels[2], driveTrain.wheels[3], "RL -> RR");

        Undo.CollapseUndoOperations(undoGroup);
        Debug.Log($"FixWheelSymmetry: mirrored right-side wheel X positions on '{driveTrain.name}' to match the left side. Run Diagnose Wheel Setup again to confirm.");
    }

    static VehicleDriveTrain FindTarget()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected != null)
        {
            VehicleDriveTrain dt = selected.GetComponent<VehicleDriveTrain>();
            if (dt != null) return dt;
        }
        VehicleDriveTrain[] all = Object.FindObjectsByType<VehicleDriveTrain>(FindObjectsSortMode.None);
        return all.Length > 0 ? all[0] : null;
    }

    static void MirrorPair(Transform chassisRoot, Transform left, Transform right, string label)
    {
        if (left == null || right == null)
        {
            Debug.LogWarning($"FixWheelSymmetry: {label} - one side is not assigned, skipping.");
            return;
        }

        Vector3 leftLocal = chassisRoot.InverseTransformPoint(left.position);
        Vector3 rightLocal = chassisRoot.InverseTransformPoint(right.position);
        Vector3 mirroredLocal = new Vector3(-leftLocal.x, rightLocal.y, rightLocal.z);

        Undo.RecordObject(right, "Mirror Wheel Position");
        right.position = chassisRoot.TransformPoint(mirroredLocal);

        Debug.Log($"FixWheelSymmetry: {label} - moved '{right.name}' X from {rightLocal.x:F3} to {mirroredLocal.x:F3}.");
    }
}
