using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Bare drivetrain physics for a 4-wheel vehicle: steering, motor torque, wheel mesh sync.
/// Takes no player input directly - ThrottleInput/SteerInput are set externally by whatever is
/// controlling the vehicle (PlayerVehicleInput today, an AI driver later).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class VehicleDriveTrain : MonoBehaviour
{
    [Header("Drive")]
    [Tooltip("Motor torque (Nm) applied to each wheel at full throttle.")]
    public float enginePower = 2000f;
    [Tooltip("Top forward speed (m/s) under normal throttle. Motor torque tapers off as the vehicle approaches this, so it accelerates hard off the line and levels out at a designed, tunable top speed instead of an emergent one.")]
    public float topSpeed = 30f;
    [Tooltip("Top forward speed (m/s) while BoostInput is true. Should be higher than Top Speed so boost actually raises the ceiling, not just how fast you get there.")]
    public float boostTopSpeed = 42f;
    [Tooltip("Maximum steering angle (degrees) the front wheels can reach.")]
    public float turnSpeed = 25f;
    [Tooltip("How quickly the steering angle moves toward its target - higher is snappier, lower feels heavier/more damped.")]
    public float turnSmoothness = 5f;
    [Tooltip("Brake torque (Nm) applied when throttle input opposes the current direction of travel - lets you stop sharply instead of coasting to a halt or having to wait out reverse torque.")]
    public float brakeTorque = 4000f;
    [Tooltip("Light brake torque applied automatically when there's no throttle input, so releasing the gas settles the vehicle down instead of coasting forever.")]
    public float coastBrakeTorque = 250f;

    [Header("Wheels")]
    [Tooltip("Each entry needs a WheelCollider component. First two entries steer; the rest stay straight.")]
    public Transform[] wheels;
    [Tooltip("Visual wheel meshes, matched 1:1 by index to Wheels.")]
    public Transform[] wheelMeshes;

    [Header("Mass")]
    [Tooltip("Local-space offset of the vehicle's center of mass from its pivot. Lowering this (negative Y) makes the vehicle much harder to flip.")]
    public Vector3 centerOfMassOffset;

    [Header("Arcade Feel")]
    [Tooltip("Engine power is multiplied by this while boosting.")]
    public float boostMultiplier = 1.6f;
    [Tooltip("Rear wheels' (index 2+) sideways grip is multiplied by this while HandbrakeInput is true, letting the tail slide out for arcade drifts.")]
    [Range(0f, 1f)] public float handbrakeRearGripMultiplier = 0.3f;

    [Header("Boost Fuel")]
    [Tooltip("Maximum boost fuel, in seconds of continuous boost at Boost Drain Rate. Starts full.")]
    public float maxBoostFuel = 3f;
    [Tooltip("Boost fuel drained per second while boosting.")]
    public float boostDrainRate = 1f;
    [Tooltip("Boost fuel gained per second while airborne - rewards jumps rather than just holding throttle.")]
    public float airTimeBoostGainRate = 0.6f;
    [Tooltip("Boost fuel gained per second while drifting (HandbrakeInput held and grounded) - rewards sliding through corners instead of driving straight.")]
    public float driftBoostGainRate = 0.5f;

    [Header("Stability - Anti-Roll")]
    [Tooltip("Anti-roll bar stiffness for the front axle (wheels 0/1). Resists body roll by pushing down on the wheel with more suspension travel - real suspension physics, not an artificial correction. Raise this if the vehicle tips over in hard corners.")]
    public float antiRollFront = 12000f;
    [Tooltip("Anti-roll bar stiffness for the rear axle (wheels 2/3). Same effect as Anti Roll Front, tuned separately since rear grip loss (drifting) changes how much rear roll resistance feels right.")]
    public float antiRollRear = 12000f;

    [Header("Extra Gravity")]
    [Tooltip("Additional downward acceleration (m/s^2) applied every frame, on top of Unity's normal gravity. Makes falls resolve faster and landings hit sooner - the standard fix for a vehicle that feels like it's floating rather than falling.")]
    public float extraGravity = 3f;
    [Tooltip("How much to stiffen suspension spring/damper (applied once on Awake) to compensate for the added downward load from Extra Gravity, so ride height and available travel stay close to normal instead of sinking. 0 = no compensation (car sits lower, bottoms out more easily), 1 = fully compensated (same ride height as with no Extra Gravity at all).")]
    [Range(0f, 1f)] public float suspensionCompensation = 0.5f;

    [Header("Air Control")]
    [Tooltip("Pitch torque (nose up/down) applied from ThrottleInput while airborne. This is player-driven only - there is no automatic self-righting, so landing upright is on the player, not the physics.")]
    public float airPitchTorque = 4f;
    [Tooltip("Roll torque (barrel roll) applied from SteerInput while airborne.")]
    public float airRollTorque = 5f;
    [Tooltip("Damps angular velocity while airborne so holding air control input doesn't spin the vehicle up indefinitely over a long jump.")]
    public float airAngularDrag = 0.8f;

    public float ThrottleInput { get; set; }
    public float SteerInput { get; set; }
    public bool BoostInput { get; set; }
    public bool HandbrakeInput { get; set; }
    public float SpeedKmh { get; private set; }
    public bool IsGrounded { get; private set; }
    /// <summary>Remaining boost fuel, in the same seconds-of-boost unit as Max Boost Fuel.</summary>
    public float BoostFuel { get; private set; }
    /// <summary>Remaining boost fuel normalized to 0-1, for UI meters.</summary>
    public float BoostFuel01 => maxBoostFuel > 0f ? Mathf.Clamp01(BoostFuel / maxBoostFuel) : 0f;
    /// <summary>True only while boost is actually being applied - BoostInput held AND fuel remaining.</summary>
    public bool IsBoosting { get; private set; }

    Rigidbody rb;
    float currentTurnAngle;
    WheelFrictionCurve[] rearBaseSidewaysFriction;
    bool rearFrictionCached;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.centerOfMass = centerOfMassOffset;
        ApplySuspensionCompensation();
        BoostFuel = maxBoostFuel;
    }

    void FixedUpdate()
    {
        if (!rearFrictionCached) CacheRearSidewaysFriction();

        rb.AddForce(Vector3.down * extraGravity, ForceMode.Acceleration);

        SpeedKmh = rb.linearVelocity.magnitude * 3.6f;
        UpdateGroundedState();

        IsBoosting = BoostInput && BoostFuel > 0f;
        UpdateBoostFuel();

        float targetTurnAngle = Mathf.Clamp(SteerInput, -1f, 1f) * turnSpeed;
        currentTurnAngle = Mathf.Lerp(currentTurnAngle, targetTurnAngle, Time.deltaTime * turnSmoothness);

        float throttle = Mathf.Clamp(ThrottleInput, -1f, 1f);
        float power = enginePower * (IsBoosting ? boostMultiplier : 1f);
        float curTopSpeed = IsBoosting ? boostTopSpeed : topSpeed;
        float forwardSpeed = Vector3.Dot(rb.linearVelocity, transform.forward);
        float speedRatio = Mathf.Clamp01(Mathf.Abs(forwardSpeed) / Mathf.Max(0.01f, curTopSpeed));

        float torque = 0f;
        float brake = 0f;
        if (throttle > 0.01f)
        {
            if (forwardSpeed < -1f) brake = brakeTorque;
            else torque = throttle * power * (1f - speedRatio);
        }
        else if (throttle < -0.01f)
        {
            if (forwardSpeed > 1f) brake = brakeTorque;
            else torque = throttle * power * (1f - speedRatio);
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
            wc.motorTorque = torque;
            wc.brakeTorque = brake;

            if (i >= 2) ApplyRearGrip(wc, i);

            if (wheelMeshes != null && i < wheelMeshes.Length && wheelMeshes[i] != null)
            {
                wc.GetWorldPose(out Vector3 pos, out Quaternion rot);
                wheelMeshes[i].position = pos;
                wheelMeshes[i].rotation = rot;
            }
        }

        if (wheels.Length >= 4)
        {
            ApplyAntiRoll(GetWheelCollider(0), GetWheelCollider(1), antiRollFront);
            ApplyAntiRoll(GetWheelCollider(2), GetWheelCollider(3), antiRollRear);
        }

        if (!IsGrounded) HandleAirControl();
    }

    WheelCollider GetWheelCollider(int i)
    {
        return wheels[i] != null ? wheels[i].GetComponent<WheelCollider>() : null;
    }

    void ApplySuspensionCompensation()
    {
        if (extraGravity <= 0f || suspensionCompensation <= 0f) return;

        // Extra Gravity increases the downward load the suspension has to support at rest, which
        // would otherwise sink ride height and eat into available travel. Fully compensating means
        // scaling spring/damper by the same ratio the total downward acceleration increased by.
        float gravityMagnitude = Physics.gravity.magnitude;
        float loadRatio = (gravityMagnitude + extraGravity) / Mathf.Max(0.01f, gravityMagnitude);
        float springScale = Mathf.Lerp(1f, loadRatio, suspensionCompensation);

        for (int i = 0; i < wheels.Length; i++)
        {
            WheelCollider wc = GetWheelCollider(i);
            if (wc == null) continue;

            JointSpring spring = wc.suspensionSpring;
            spring.spring *= springScale;
            spring.damper *= springScale;
            wc.suspensionSpring = spring;
        }
    }

    void UpdateBoostFuel()
    {
        if (IsBoosting)
        {
            BoostFuel -= boostDrainRate * Time.fixedDeltaTime;
        }
        else
        {
            // Airborne and drifting are mutually exclusive (drifting needs grounded wheels to slide),
            // so only one gain rate ever applies at a time.
            float gain = !IsGrounded ? airTimeBoostGainRate : (HandbrakeInput ? driftBoostGainRate : 0f);
            BoostFuel += gain * Time.fixedDeltaTime;
        }
        BoostFuel = Mathf.Clamp(BoostFuel, 0f, maxBoostFuel);
    }

    void UpdateGroundedState()
    {
        int grounded = 0;
        for (int i = 0; i < wheels.Length; i++)
        {
            WheelCollider wc = GetWheelCollider(i);
            if (wc != null && wc.isGrounded) grounded++;
        }
        IsGrounded = grounded * 2 >= wheels.Length;
    }

    void HandleAirControl()
    {
        Vector3 pitchTorque = transform.right * (Mathf.Clamp(ThrottleInput, -1f, 1f) * airPitchTorque);
        Vector3 rollTorque = -transform.forward * (Mathf.Clamp(SteerInput, -1f, 1f) * airRollTorque);

        // Acceleration mode ignores mass/inertia, so this feels consistent across differently-sized vehicles.
        rb.AddTorque(pitchTorque + rollTorque, ForceMode.Acceleration);
        rb.angularVelocity *= Mathf.Clamp01(1f - airAngularDrag * Time.fixedDeltaTime);
    }

    void CacheRearSidewaysFriction()
    {
        rearBaseSidewaysFriction = new WheelFrictionCurve[wheels.Length];
        for (int i = 2; i < wheels.Length; i++)
        {
            WheelCollider wc = GetWheelCollider(i);
            if (wc != null) rearBaseSidewaysFriction[i] = wc.sidewaysFriction;
        }
        rearFrictionCached = true;
    }

    void ApplyRearGrip(WheelCollider wc, int i)
    {
        WheelFrictionCurve friction = rearBaseSidewaysFriction[i];
        if (HandbrakeInput) friction.stiffness *= handbrakeRearGripMultiplier;
        wc.sidewaysFriction = friction;
    }

    void ApplyAntiRoll(WheelCollider left, WheelCollider right, float antiRoll)
    {
        if (left == null || right == null) return;

        // Compares how far each side's suspension is compressed (0 = fully drooped, 1 = fully
        // compressed) and pushes down on whichever side has more travel left - the same mechanical
        // effect as a real anti-roll bar linking the two wheels, resisting body roll in corners.
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

    void OnDrawGizmosSelected()
    {
        Rigidbody gizmoRb = rb != null ? rb : GetComponent<Rigidbody>();
        if (gizmoRb == null) return;

        Vector3 comWorld = transform.TransformPoint(centerOfMassOffset);
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(comWorld, 0.1f);
#if UNITY_EDITOR
        Handles.color = Color.yellow;
        Handles.Label(comWorld + Vector3.up * 0.15f, "Center of Mass");
#endif
    }
}
