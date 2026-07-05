using UnityEngine;
using UnityEditor;

/// <summary>
/// One-time migration for a Pickup rig still wired to the legacy CarController script: transplants
/// its wheel/mesh references onto VehicleDriveTrain + PlayerVehicleInput, carries over the tuned
/// values that matter (center of mass, handbrake grip), repoints the scene's PlayerCamera at it,
/// and removes CarController. Run with the Pickup GameObject selected in the Hierarchy, or it will
/// fall back to searching the open scene for a GameObject named "Pickup" (or "Pickup (1)", etc.)
/// that still has a CarController on it.
/// </summary>
public static class MigratePickupToDriveTrain
{
    [MenuItem("Dune Bash/Migrate Selected Pickup To VehicleDriveTrain")]
    public static void Migrate()
    {
        GameObject target = FindTarget();
        if (target == null)
        {
            Debug.LogError("MigratePickupToDriveTrain: no GameObject with a CarController found. Select the Pickup GameObject in the Hierarchy and run this again.");
            return;
        }

        CarController legacy = target.GetComponent<CarController>();
        if (legacy == null)
        {
            Debug.LogError($"MigratePickupToDriveTrain: '{target.name}' has no CarController to migrate from.");
            return;
        }

        if (legacy.wheelColliderFL == null || legacy.wheelColliderFR == null || legacy.wheelColliderRL == null || legacy.wheelColliderRR == null)
        {
            Debug.LogError($"MigratePickupToDriveTrain: '{target.name}' is missing one or more wheel collider references on CarController - aborting so nothing is left half-wired.");
            return;
        }

        Undo.SetCurrentGroupName("Migrate Pickup to VehicleDriveTrain");
        int undoGroup = Undo.GetCurrentGroup();

        VehicleDriveTrain driveTrain = Undo.AddComponent<VehicleDriveTrain>(target);
        driveTrain.wheels = new[]
        {
            legacy.wheelColliderFL.transform,
            legacy.wheelColliderFR.transform,
            legacy.wheelColliderRL.transform,
            legacy.wheelColliderRR.transform,
        };
        driveTrain.wheelMeshes = new[]
        {
            legacy.wheelMeshFL,
            legacy.wheelMeshFR,
            legacy.wheelMeshRL,
            legacy.wheelMeshRR,
        };
        driveTrain.centerOfMassOffset = legacy.centerOfMassOffset;
        driveTrain.enginePower = legacy.motorTorque;
        driveTrain.turnSpeed = legacy.maxSteerAngle;
        driveTrain.turnSmoothness = legacy.steerResponse;
        driveTrain.handbrakeRearGripMultiplier = legacy.handbrakeRearGripMultiplier;

        PlayerVehicleInput playerInput = Undo.AddComponent<PlayerVehicleInput>(target);
        playerInput.driveTrain = driveTrain;

        Undo.DestroyObjectImmediate(legacy);

        PlayerCamera playerCam = Object.FindFirstObjectByType<PlayerCamera>();
        if (playerCam != null)
        {
            Undo.RecordObject(playerCam, "Retarget PlayerCamera");
            playerCam.target = target.transform;
            playerCam.targetBody = target.GetComponent<Rigidbody>();
        }
        else
        {
            Debug.LogWarning("MigratePickupToDriveTrain: no PlayerCamera found in the scene to retarget - wire one up manually.");
        }

        Undo.CollapseUndoOperations(undoGroup);

        Debug.Log($"MigratePickupToDriveTrain: '{target.name}' now uses VehicleDriveTrain + PlayerVehicleInput. " +
                  "turnSpeed/turnSmoothness were seeded from maxSteerAngle/steerResponse as a starting point - retune to taste. " +
                  "This is a single Undo step if anything looks wrong.");
    }

    static GameObject FindTarget()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected != null && selected.GetComponent<CarController>() != null) return selected;

        CarController[] all = Object.FindObjectsByType<CarController>(FindObjectsSortMode.None);
        foreach (CarController c in all)
        {
            if (c.name.StartsWith("Pickup")) return c.gameObject;
        }
        return all.Length > 0 ? all[0].gameObject : null;
    }
}
