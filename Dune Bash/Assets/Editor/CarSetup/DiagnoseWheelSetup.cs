using UnityEngine;
using UnityEditor;

/// <summary>
/// Reports each wheel's local position, world scale, rotation, and suspension/friction settings
/// relative to its chassis, then flags any left/right asymmetry between the front and rear pairs.
/// Anti-roll (and suspension travel generally) assumes mirrored, axis-aligned wheel colliders -
/// an imported rig can easily violate that (inherited non-uniform scale, a slightly rotated wheel
/// collider, mismatched suspensionDistance) in ways that are hard to spot by eye in the Inspector.
/// </summary>
public static class DiagnoseWheelSetup
{
    const float PositionTolerance = 0.01f;
    const float ScaleTolerance = 0.001f;
    const float RotationToleranceDeg = 1f;
    const float NumericTolerance = 0.01f;

    [MenuItem("Dune Bash/Diagnose Wheel Setup")]
    public static void Diagnose()
    {
        VehicleDriveTrain driveTrain = FindTarget();
        if (driveTrain == null)
        {
            Debug.LogError("DiagnoseWheelSetup: no VehicleDriveTrain found. Select the vehicle in the Hierarchy and run this again.");
            return;
        }

        if (driveTrain.wheels == null || driveTrain.wheels.Length < 4)
        {
            Debug.LogError($"DiagnoseWheelSetup: '{driveTrain.name}' does not have 4 wheels assigned.");
            return;
        }

        Debug.Log($"--- Wheel diagnostics for '{driveTrain.name}' ---");

        string[] labels = { "FL (0)", "FR (1)", "RL (2)", "RR (3)" };
        WheelInfo[] infos = new WheelInfo[4];
        for (int i = 0; i < 4; i++)
        {
            infos[i] = ReadWheel(driveTrain.transform, driveTrain.wheels[i], labels[i]);
            Debug.Log(infos[i].ToString());
        }

        ComparePair(infos[0], infos[1], "front (FL vs FR)");
        ComparePair(infos[2], infos[3], "rear (RL vs RR)");

        Debug.Log("--- End wheel diagnostics - any asymmetry above is logged as a warning above this line ---");
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

    struct WheelInfo
    {
        public string label;
        public Vector3 localPos;
        public Vector3 lossyScale;
        public Vector3 localEuler;
        public float radius;
        public float suspensionDistance;
        public float springForce;
        public float damper;
        public float sidewaysStiffness;
        public float forwardStiffness;

        public override string ToString()
        {
            return $"{label}: localPos={localPos:F3} lossyScale={lossyScale:F3} localEuler={localEuler:F1} " +
                   $"radius={radius:F3} suspensionDistance={suspensionDistance:F3} spring={springForce:F0} damper={damper:F0} " +
                   $"sidewaysStiffness={sidewaysStiffness:F2} forwardStiffness={forwardStiffness:F2}";
        }
    }

    static WheelInfo ReadWheel(Transform chassisRoot, Transform wheelTransform, string label)
    {
        WheelInfo info = new WheelInfo { label = label };
        if (wheelTransform == null)
        {
            Debug.LogWarning($"DiagnoseWheelSetup: {label} transform is not assigned.");
            return info;
        }

        info.localPos = chassisRoot.InverseTransformPoint(wheelTransform.position);
        info.lossyScale = wheelTransform.lossyScale;
        info.localEuler = (Quaternion.Inverse(chassisRoot.rotation) * wheelTransform.rotation).eulerAngles;

        WheelCollider wc = wheelTransform.GetComponent<WheelCollider>();
        if (wc == null)
        {
            Debug.LogWarning($"DiagnoseWheelSetup: {label} ('{wheelTransform.name}') has no WheelCollider component.");
            return info;
        }

        info.radius = wc.radius;
        info.suspensionDistance = wc.suspensionDistance;
        info.springForce = wc.suspensionSpring.spring;
        info.damper = wc.suspensionSpring.damper;
        info.sidewaysStiffness = wc.sidewaysFriction.stiffness;
        info.forwardStiffness = wc.forwardFriction.stiffness;
        return info;
    }

    static void ComparePair(WheelInfo left, WheelInfo right, string pairLabel)
    {
        // Mirrored across the chassis X axis: left.x should be ~ -right.x, y/z should match.
        CheckClose(left.localPos.x, -right.localPos.x, PositionTolerance,
            $"{pairLabel}: X position not mirrored ({left.label}={left.localPos.x:F3}, {right.label}={right.localPos.x:F3}).");
        CheckClose(left.localPos.y, right.localPos.y, PositionTolerance,
            $"{pairLabel}: Y position mismatch ({left.label}={left.localPos.y:F3}, {right.label}={right.localPos.y:F3}) - one side sits higher/lower than the other.");
        CheckClose(left.localPos.z, right.localPos.z, PositionTolerance,
            $"{pairLabel}: Z position mismatch ({left.label}={left.localPos.z:F3}, {right.label}={right.localPos.z:F3}) - wheels aren't level front-to-back.");

        CheckClose(left.lossyScale.x, right.lossyScale.x, ScaleTolerance,
            $"{pairLabel}: X scale mismatch ({left.label}={left.lossyScale.x:F4}, {right.label}={right.lossyScale.x:F4}) - non-uniform/inherited scale can skew the suspension travel math.");
        CheckClose(left.lossyScale.y, right.lossyScale.y, ScaleTolerance,
            $"{pairLabel}: Y scale mismatch ({left.label}={left.lossyScale.y:F4}, {right.label}={right.lossyScale.y:F4}) - non-uniform/inherited scale can skew the suspension travel math.");
        CheckClose(left.lossyScale.z, right.lossyScale.z, ScaleTolerance,
            $"{pairLabel}: Z scale mismatch ({left.label}={left.lossyScale.z:F4}, {right.label}={right.lossyScale.z:F4}) - non-uniform/inherited scale can skew the suspension travel math.");

        CheckClose(left.localEuler.x, right.localEuler.x, RotationToleranceDeg,
            $"{pairLabel}: X rotation mismatch ({left.label}={left.localEuler.x:F2}, {right.label}={right.localEuler.x:F2}) - a tilted wheel collider throws off suspension travel readings.");
        CheckClose(left.localEuler.z, right.localEuler.z, RotationToleranceDeg,
            $"{pairLabel}: Z rotation mismatch ({left.label}={left.localEuler.z:F2}, {right.label}={right.localEuler.z:F2}) - a tilted wheel collider throws off suspension travel readings.");

        CheckClose(left.radius, right.radius, NumericTolerance,
            $"{pairLabel}: radius mismatch ({left.label}={left.radius:F3}, {right.label}={right.radius:F3}).");
        CheckClose(left.suspensionDistance, right.suspensionDistance, NumericTolerance,
            $"{pairLabel}: suspensionDistance mismatch ({left.label}={left.suspensionDistance:F3}, {right.label}={right.suspensionDistance:F3}) - directly skews the anti-roll travel-fraction comparison.");
        CheckClose(left.springForce, right.springForce, 1f,
            $"{pairLabel}: suspension spring mismatch ({left.label}={left.springForce:F0}, {right.label}={right.springForce:F0}).");
        CheckClose(left.damper, right.damper, 1f,
            $"{pairLabel}: suspension damper mismatch ({left.label}={left.damper:F0}, {right.label}={right.damper:F0}).");
        CheckClose(left.sidewaysStiffness, right.sidewaysStiffness, 0.01f,
            $"{pairLabel}: sideways friction stiffness mismatch ({left.label}={left.sidewaysStiffness:F2}, {right.label}={right.sidewaysStiffness:F2}).");
    }

    static void CheckClose(float a, float b, float tolerance, string warningMessage)
    {
        if (Mathf.Abs(a - b) > tolerance)
        {
            Debug.LogWarning("DiagnoseWheelSetup: " + warningMessage);
        }
    }
}
