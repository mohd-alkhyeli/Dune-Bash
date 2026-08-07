using UnityEngine;

/// <summary>
/// Reads keyboard input and drives any IVehicleDriveTrain assigned to it - VehicleDriveTrain (full
/// sand-buggy physics) or KartDriveTrain (minimal test rig) both work, same as AIVehicleInput. Exposed
/// as a plain MonoBehaviour field rather than the interface directly - Unity's default Inspector can't
/// render a bare interface-typed field as an assignable slot at all, so drag any component implementing
/// IVehicleDriveTrain onto Drive Train and it's cast where actually used. Keys are changeable per
/// instance so different vehicles (or a future split-screen setup) can use different bindings.
/// </summary>
public class PlayerVehicleInput : MonoBehaviour
{
    [Tooltip("Any component implementing IVehicleDriveTrain (VehicleDriveTrain or KartDriveTrain).")]
    public MonoBehaviour driveTrain;

    [Header("Keybinds")]
    public KeyCode forwardKey = KeyCode.W;
    public KeyCode backKey = KeyCode.S;
    public KeyCode leftKey = KeyCode.A;
    public KeyCode rightKey = KeyCode.D;
    public KeyCode boostKey = KeyCode.LeftShift;
    public KeyCode handbrakeKey = KeyCode.Space;

    IVehicleDriveTrain Drive => driveTrain as IVehicleDriveTrain;

    void Awake()
    {
        // Catches the easy mistake of dragging in something that isn't actually a drive train - without
        // this, it fails exactly like leaving the field empty (Update just no-ops), which is much harder
        // to tell apart from "forgot to assign it" than a loud warning right at startup.
        if (driveTrain != null && Drive == null)
            Debug.LogError($"{name}: PlayerVehicleInput.driveTrain is a {driveTrain.GetType().Name}, which doesn't implement IVehicleDriveTrain.", this);
    }

    void Update()
    {
        IVehicleDriveTrain drive = Drive;
        if (drive == null) return;

        float throttle = 0f;
        if (Input.GetKey(forwardKey)) throttle += 1f;
        if (Input.GetKey(backKey)) throttle -= 1f;

        float steer = 0f;
        if (Input.GetKey(rightKey)) steer += 1f;
        if (Input.GetKey(leftKey)) steer -= 1f;

        drive.ThrottleInput = throttle;
        drive.SteerInput = steer;
        drive.BoostInput = Input.GetKey(boostKey);
        drive.HandbrakeInput = Input.GetKey(handbrakeKey);
    }

    /// <summary>Stops responding to input and zeroes the drive train's inputs - used to abandon a car on respawn so it doesn't sit there flooring the throttle.</summary>
    public void ReleaseControl()
    {
        enabled = false;
        IVehicleDriveTrain drive = Drive;
        if (drive == null) return;

        drive.ThrottleInput = 0f;
        drive.SteerInput = 0f;
        drive.BoostInput = false;
        drive.HandbrakeInput = false;
    }
}
