using UnityEngine;

/// <summary>
/// Minimal 4-wheel drivetrain: throttle, brake, and steering over WheelColliders only - no sand
/// grip/drag tuning, no extra gravity or suspension compensation, no boost/handbrake/air control. Exists
/// so AIVehicleInput's pathfinding/avoidance can be tested on a flat hard-surface plane with a
/// primitive-built kart, isolating it from VehicleDriveTrain's sand-simulation and arcade-feel extras -
/// if the AI still struggles here, the problem is the AI logic itself, not the sand physics.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class KartDriveTrain : MonoBehaviour, IVehicleDriveTrain
{
    [Header("Drive")]
    public float enginePower = 1500f;
    public float topSpeed = 20f;
    public float turnSpeed = 30f;
    public float turnSmoothness = 5f;
    public float brakeTorque = 3000f;
    public float coastBrakeTorque = 200f;

    [Header("Wheels")]
    [Tooltip("Each entry needs a WheelCollider component. First two entries steer; the rest are driven (rear-wheel drive, like a real kart).")]
    public Transform[] wheels;
    [Tooltip("Visual wheel meshes, matched 1:1 by index to Wheels.")]
    public Transform[] wheelMeshes;
    [Tooltip("Local-space correction applied on top of WheelCollider's own rotation before it's assigned to each mesh in Wheel Meshes. WheelCollider assumes a wheel mesh's rolling axis is its local X - if a mesh was authored/exported with a different convention (e.g. the rim facing forward instead of sideways), the wheel will render with the wrong orientation even though the physics itself is unaffected. Tune this until the wheels look right; 90 degrees on Y is the most common fix for a mesh whose disc faces local Z instead of local X.")]
    public Vector3 wheelMeshRotationOffset = new Vector3(0f, 90f, 0f);

    [Header("Mass")]
    public Vector3 centerOfMassOffset;

    [Header("Stability - Anti-Roll")]
    [Tooltip("Anti-roll bar stiffness for the front axle (Wheels indices 0/1). Resists body roll by pushing down on whichever side has more suspension travel left, same as a real anti-roll bar - without this, the two sides' suspension react completely independently and a side-to-side rock has nothing damping it out, which is how a rock turns into a rollover. Raise this if the kart tips over sideways too easily.")]
    public float antiRollFront = 1500f;
    [Tooltip("Anti-roll bar stiffness for the rear axle (Wheels indices 2/3). Tuned separately since the driven rear wheels can lose grip differently than the front.")]
    public float antiRollRear = 1500f;

    public float ThrottleInput { get; set; }
    public float SteerInput { get; set; }
    public bool BoostInput { get; set; }
    public bool HandbrakeInput { get; set; }
    public float SpeedKmh { get; private set; }
    public bool IsGrounded { get; private set; }

    Rigidbody rb;
    float currentTurnAngle;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.centerOfMass = centerOfMassOffset;
    }

    void FixedUpdate()
    {
        SpeedKmh = rb.linearVelocity.magnitude * 3.6f;
        UpdateGroundedState();

        float targetTurnAngle = Mathf.Clamp(SteerInput, -1f, 1f) * turnSpeed;
        currentTurnAngle = Mathf.Lerp(currentTurnAngle, targetTurnAngle, Time.deltaTime * turnSmoothness);

        float throttle = Mathf.Clamp(ThrottleInput, -1f, 1f);
        float forwardSpeed = Vector3.Dot(rb.linearVelocity, transform.forward);
        float speedRatio = Mathf.Clamp01(Mathf.Abs(forwardSpeed) / Mathf.Max(0.01f, topSpeed));

        float torque = 0f;
        float brake = 0f;
        if (throttle > 0.01f)
        {
            if (forwardSpeed < -1f) brake = brakeTorque;
            else torque = throttle * enginePower * (1f - speedRatio);
        }
        else if (throttle < -0.01f)
        {
            if (forwardSpeed > 1f) brake = brakeTorque;
            else torque = throttle * enginePower * (1f - speedRatio);
        }
        else
        {
            brake = coastBrakeTorque;
        }

        for (int i = 0; i < wheels.Length; i++)
        {
            if (wheels[i] == null) continue;
            WheelCollider wc = wheels[i].GetComponent<WheelCollider>();
            if (wc == null) continue;

            wc.steerAngle = i < 2 ? currentTurnAngle : 0f;
            wc.motorTorque = i >= 2 ? torque : 0f;
            wc.brakeTorque = brake;

            if (wheelMeshes != null && i < wheelMeshes.Length && wheelMeshes[i] != null)
            {
                wc.GetWorldPose(out Vector3 pos, out Quaternion rot);
                wheelMeshes[i].position = pos;
                wheelMeshes[i].rotation = rot * Quaternion.Euler(wheelMeshRotationOffset);
            }
        }

        if (wheels.Length >= 4)
        {
            ApplyAntiRoll(GetWheelCollider(0), GetWheelCollider(1), antiRollFront);
            ApplyAntiRoll(GetWheelCollider(2), GetWheelCollider(3), antiRollRear);
        }
    }

    WheelCollider GetWheelCollider(int i)
    {
        return wheels[i] != null ? wheels[i].GetComponent<WheelCollider>() : null;
    }

    /// <summary>
    /// Compares how far each side's suspension is compressed (0 = fully drooped, 1 = fully compressed)
    /// and pushes down on whichever side has more travel left - the same mechanical effect as a real
    /// anti-roll bar linking the two wheels, resisting body roll in corners or from an uneven bump.
    /// Without this, left and right suspension react completely independently, so a small side-to-side
    /// rock has nothing damping it out and can grow into a rollover instead of settling.
    /// </summary>
    void ApplyAntiRoll(WheelCollider left, WheelCollider right, float antiRoll)
    {
        if (left == null || right == null) return;

        WheelHit hit;
        float travelLeft = 1f, travelRight = 1f;

        bool groundedLeft = left.GetGroundHit(out hit);
        if (groundedLeft) travelLeft = Mathf.Clamp01((-left.transform.InverseTransformPoint(hit.point).y - left.radius) / left.suspensionDistance);

        bool groundedRight = right.GetGroundHit(out hit);
        if (groundedRight) travelRight = Mathf.Clamp01((-right.transform.InverseTransformPoint(hit.point).y - right.radius) / right.suspensionDistance);

        float antiRollForce = (travelLeft - travelRight) * antiRoll;

        if (groundedLeft) rb.AddForceAtPosition(left.transform.up * -antiRollForce, left.transform.position);
        if (groundedRight) rb.AddForceAtPosition(right.transform.up * antiRollForce, right.transform.position);
    }

    void UpdateGroundedState()
    {
        int grounded = 0;
        for (int i = 0; i < wheels.Length; i++)
        {
            WheelCollider wc = GetWheelCollider(i);
            if (wc != null && wc.isGrounded) grounded++;
        }
        IsGrounded = wheels.Length > 0 && grounded * 2 >= wheels.Length;
    }
}
