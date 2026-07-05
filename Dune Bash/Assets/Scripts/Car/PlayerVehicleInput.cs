using UnityEngine;

/// <summary>
/// Reads keyboard input and drives any VehicleDriveTrain assigned to it. Keys are changeable per
/// instance so different vehicles (or a future split-screen setup) can use different bindings.
/// </summary>
public class PlayerVehicleInput : MonoBehaviour
{
    public VehicleDriveTrain driveTrain;

    [Header("Keybinds")]
    public KeyCode forwardKey = KeyCode.W;
    public KeyCode backKey = KeyCode.S;
    public KeyCode leftKey = KeyCode.A;
    public KeyCode rightKey = KeyCode.D;
    public KeyCode boostKey = KeyCode.LeftShift;
    public KeyCode handbrakeKey = KeyCode.Space;

    void Update()
    {
        if (driveTrain == null) return;

        float throttle = 0f;
        if (Input.GetKey(forwardKey)) throttle += 1f;
        if (Input.GetKey(backKey)) throttle -= 1f;

        float steer = 0f;
        if (Input.GetKey(rightKey)) steer += 1f;
        if (Input.GetKey(leftKey)) steer -= 1f;

        driveTrain.ThrottleInput = throttle;
        driveTrain.SteerInput = steer;
        driveTrain.BoostInput = Input.GetKey(boostKey);
        driveTrain.HandbrakeInput = Input.GetKey(handbrakeKey);
    }
}
