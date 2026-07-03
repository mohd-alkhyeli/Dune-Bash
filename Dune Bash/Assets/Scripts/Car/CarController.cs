using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class CarController : MonoBehaviour
{
    public enum DriveType { FrontWheelDrive, RearWheelDrive, AllWheelDrive }

    [Header("Wheel Colliders")]
    public WheelCollider wheelColliderFL;
    public WheelCollider wheelColliderFR;
    public WheelCollider wheelColliderRL;
    public WheelCollider wheelColliderRR;

    [Header("Wheel Visuals")]
    public Transform wheelMeshFL;
    public Transform wheelMeshFR;
    public Transform wheelMeshRL;
    public Transform wheelMeshRR;

    [Header("Steering")]
    [Tooltip("Max steering angle in degrees at a standstill.")]
    public float maxSteerAngle = 32f;
    [Tooltip("Speed (m/s) at which steer angle has fallen to its high-speed minimum.")]
    public float steerSpeedFalloff = 30f;
    [Tooltip("Steer angle is multiplied by this at/above steerSpeedFalloff.")]
    [Range(0.1f, 1f)] public float minSteerFactorAtSpeed = 0.45f;
    [Tooltip("How fast (deg/sec, scaled by maxSteerAngle) the steer angle moves toward the target.")]
    public float steerResponse = 6f;

    [Header("Drivetrain")]
    public DriveType driveType = DriveType.AllWheelDrive;
    [Tooltip("Motor torque (Nm) applied to each driven wheel at full throttle.")]
    public float motorTorque = 2200f;
    [Tooltip("Top speed under normal throttle, m/s.")]
    public float topSpeed = 34f;
    [Tooltip("Top speed while boosting, m/s.")]
    public float boostTopSpeed = 48f;
    public float brakeTorque = 4000f;
    [Tooltip("Passive engine-brake / rolling resistance torque applied with no throttle input.")]
    public float coastBrakeTorque = 250f;
    [Tooltip("Reverse throttle is multiplied by this (weaker than forward).")]
    [Range(0.1f, 1f)] public float reversePowerFactor = 0.6f;

    [Header("Handbrake / Drift")]
    public float handbrakeTorque = 6000f;
    [Tooltip("Rear sideways grip is multiplied by this while the handbrake is held, to let the tail slide.")]
    [Range(0f, 1f)] public float handbrakeRearGripMultiplier = 0.35f;

    [Header("Suspension (soft, long-travel for dune bumps)")]
    public float suspensionDistance = 0.34f;
    public float suspensionSpring = 38000f;
    public float suspensionDamper = 4200f;
    [Range(0f, 1f)] public float suspensionTargetPosition = 0.5f;
    public float wheelMass = 20f;

    [Header("Tire Grip (sand-tuned friction curves)")]
    [Tooltip("Applied to all four wheels' forward/sideways friction curves on Awake.")]
    public float forwardExtremumSlip = 0.4f;
    public float forwardExtremumValue = 1f;
    public float forwardAsymptoteSlip = 0.85f;
    public float forwardAsymptoteValue = 0.55f;
    public float forwardStiffness = 1.6f;
    public float sidewaysExtremumSlip = 0.28f;
    public float sidewaysExtremumValue = 1f;
    public float sidewaysAsymptoteSlip = 0.55f;
    public float sidewaysAsymptoteValue = 0.7f;
    public float sidewaysStiffness = 1.5f;

    [Header("Stability")]
    public Vector3 centerOfMassOffset = new Vector3(0f, -0.55f, 0f);
    [Tooltip("Extra downward force scaled by forward speed - keeps the car planted at speed.")]
    public float downforce = 12f;
    [Tooltip("Anti-roll bar stiffness, front axle. Resists body roll / wheel lift on uneven dunes.")]
    public float antiRollFront = 12000f;
    [Tooltip("Anti-roll bar stiffness, rear axle.")]
    public float antiRollRear = 12000f;
    [Tooltip("Extra downward acceleration (m/s^2) added on top of Physics.gravity, for a punchier arcade jump arc.")]
    public float extraGravity = 4f;

    [Header("Air Control (save the flip)")]
    public float airPitchTorque = 5.5f;
    public float airRollTorque = 6.5f;
    [Tooltip("Damps angular velocity while airborne so spins don't run away.")]
    public float airAngularDrag = 0.6f;

    [Header("Nitro Boost")]
    public float boostForce = 9000f;
    [Tooltip("Boost meter (0-100) drain per second while boosting.")]
    public float boostDrainRate = 32f;
    [Tooltip("Boost meter gain per second while drifting.")]
    public float boostGainDrift = 18f;
    [Tooltip("Boost meter gain per second while airborne.")]
    public float boostGainAir = 22f;
    [Tooltip("Minimum lateral slip speed (m/s) to count as a drift for boost gain.")]
    public float driftSlipThreshold = 4.5f;
    public float boostMeter = 0f;

    public bool IsGrounded { get; private set; }
    public float SpeedKmh { get; private set; }
    public float ForwardSpeedMs { get; private set; }
    public bool IsBoosting { get; private set; }
    public bool IsDrifting { get; private set; }

    Rigidbody rb;
    float steerAngleCurrent;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.centerOfMass = centerOfMassOffset;
        ApplyTuning();
    }

    [ContextMenu("Apply Tuning")]
    public void ApplyTuning()
    {
        WheelCollider[] wheels = { wheelColliderFL, wheelColliderFR, wheelColliderRL, wheelColliderRR };
        foreach (var wc in wheels)
        {
            if (wc == null) continue;

            wc.suspensionDistance = suspensionDistance;
            wc.mass = wheelMass;

            JointSpring spring = wc.suspensionSpring;
            spring.spring = suspensionSpring;
            spring.damper = suspensionDamper;
            spring.targetPosition = suspensionTargetPosition;
            wc.suspensionSpring = spring;

            WheelFrictionCurve ff = wc.forwardFriction;
            ff.extremumSlip = forwardExtremumSlip;
            ff.extremumValue = forwardExtremumValue;
            ff.asymptoteSlip = forwardAsymptoteSlip;
            ff.asymptoteValue = forwardAsymptoteValue;
            ff.stiffness = forwardStiffness;
            wc.forwardFriction = ff;

            WheelFrictionCurve sf = wc.sidewaysFriction;
            sf.extremumSlip = sidewaysExtremumSlip;
            sf.extremumValue = sidewaysExtremumValue;
            sf.asymptoteSlip = sidewaysAsymptoteSlip;
            sf.asymptoteValue = sidewaysAsymptoteValue;
            sf.stiffness = sidewaysStiffness;
            wc.sidewaysFriction = sf;
        }
    }

    void OnValidate()
    {
        if (Application.isPlaying && rb != null) ApplyTuning();
    }

    void FixedUpdate()
    {
        float throttle = Input.GetAxis("Vertical");
        float steer = Input.GetAxis("Horizontal");
        bool handbrake = Input.GetKey(KeyCode.Space);
        IsBoosting = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) && boostMeter > 1f;

        ForwardSpeedMs = Vector3.Dot(rb.linearVelocity, transform.forward);
        SpeedKmh = rb.linearVelocity.magnitude * 3.6f;

        UpdateGroundedState();
        HandleSteering(steer);
        HandleDrive(throttle, handbrake);
        HandleStability();
        HandleAirControl(throttle, steer);
        HandleBoostMeter(handbrake);
    }

    void UpdateGroundedState()
    {
        int count = 0;
        if (wheelColliderFL != null && wheelColliderFL.isGrounded) count++;
        if (wheelColliderFR != null && wheelColliderFR.isGrounded) count++;
        if (wheelColliderRL != null && wheelColliderRL.isGrounded) count++;
        if (wheelColliderRR != null && wheelColliderRR.isGrounded) count++;
        IsGrounded = count >= 2;
    }

    void HandleSteering(float steerInput)
    {
        float speedT = Mathf.Clamp01(Mathf.Abs(ForwardSpeedMs) / Mathf.Max(0.01f, steerSpeedFalloff));
        float targetAngle = steerInput * maxSteerAngle * Mathf.Lerp(1f, minSteerFactorAtSpeed, speedT);
        steerAngleCurrent = Mathf.MoveTowards(steerAngleCurrent, targetAngle, steerResponse * maxSteerAngle * Time.fixedDeltaTime);
        if (wheelColliderFL != null) wheelColliderFL.steerAngle = steerAngleCurrent;
        if (wheelColliderFR != null) wheelColliderFR.steerAngle = steerAngleCurrent;
    }

    void HandleDrive(float throttle, bool handbrake)
    {
        float curTopSpeed = IsBoosting ? boostTopSpeed : topSpeed;
        float speedRatio = Mathf.Clamp01(Mathf.Abs(ForwardSpeedMs) / Mathf.Max(0.01f, curTopSpeed));

        float torque = 0f;
        float brake = 0f;

        if (throttle > 0.01f)
        {
            if (ForwardSpeedMs < -1f) brake = brakeTorque;
            else torque = throttle * motorTorque * (1f - speedRatio);
        }
        else if (throttle < -0.01f)
        {
            if (ForwardSpeedMs > 1f) brake = brakeTorque;
            else torque = throttle * motorTorque * reversePowerFactor * (1f - speedRatio);
        }
        else
        {
            brake = coastBrakeTorque;
        }

        if (IsBoosting && throttle > 0.01f)
        {
            rb.AddForce(transform.forward * boostForce * Time.fixedDeltaTime, ForceMode.Acceleration);
        }

        float frontTorque = driveType != DriveType.RearWheelDrive ? torque : 0f;
        float rearTorque = driveType != DriveType.FrontWheelDrive ? torque : 0f;
        float rearBrake = handbrake ? handbrakeTorque : brake;

        ApplyMotor(wheelColliderFL, frontTorque, brake);
        ApplyMotor(wheelColliderFR, frontTorque, brake);
        ApplyMotor(wheelColliderRL, rearTorque, rearBrake);
        ApplyMotor(wheelColliderRR, rearTorque, rearBrake);

        float rearGripMul = handbrake ? handbrakeRearGripMultiplier : 1f;
        SetSidewaysStiffness(wheelColliderRL, sidewaysStiffness * rearGripMul);
        SetSidewaysStiffness(wheelColliderRR, sidewaysStiffness * rearGripMul);

        float lateralSpeed = Vector3.Dot(rb.linearVelocity, transform.right);
        IsDrifting = IsGrounded && Mathf.Abs(lateralSpeed) > driftSlipThreshold && rb.linearVelocity.magnitude > 6f;
    }

    static void ApplyMotor(WheelCollider wc, float torque, float brake)
    {
        if (wc == null) return;
        wc.motorTorque = torque;
        wc.brakeTorque = brake;
    }

    void SetSidewaysStiffness(WheelCollider wc, float stiffness)
    {
        if (wc == null) return;
        WheelFrictionCurve sf = wc.sidewaysFriction;
        sf.stiffness = stiffness;
        wc.sidewaysFriction = sf;
    }

    void HandleStability()
    {
        rb.AddForce(Vector3.down * extraGravity, ForceMode.Acceleration);
        rb.AddForce(-transform.up * downforce * rb.linearVelocity.magnitude);
        ApplyAntiRoll(wheelColliderFL, wheelColliderFR, antiRollFront);
        ApplyAntiRoll(wheelColliderRL, wheelColliderRR, antiRollRear);
    }

    void ApplyAntiRoll(WheelCollider L, WheelCollider R, float antiRoll)
    {
        if (L == null || R == null) return;
        WheelHit hit;
        float travelL = 1f, travelR = 1f;
        bool groundedL = L.GetGroundHit(out hit);
        if (groundedL) travelL = (-L.transform.InverseTransformPoint(hit.point).y - L.radius) / L.suspensionDistance;
        bool groundedR = R.GetGroundHit(out hit);
        if (groundedR) travelR = (-R.transform.InverseTransformPoint(hit.point).y - R.radius) / R.suspensionDistance;

        float antiRollForce = (travelL - travelR) * antiRoll;

        if (groundedL) rb.AddForceAtPosition(L.transform.up * -antiRollForce, L.transform.position);
        if (groundedR) rb.AddForceAtPosition(R.transform.up * antiRollForce, R.transform.position);
    }

    void HandleAirControl(float throttle, float steer)
    {
        if (IsGrounded) return;
        rb.AddTorque(transform.right * throttle * airPitchTorque, ForceMode.Acceleration);
        rb.AddTorque(-transform.forward * steer * airRollTorque, ForceMode.Acceleration);
        rb.angularVelocity *= Mathf.Clamp01(1f - airAngularDrag * Time.fixedDeltaTime);
    }

    void HandleBoostMeter(bool handbrake)
    {
        if (IsBoosting)
        {
            boostMeter = Mathf.Max(0f, boostMeter - boostDrainRate * Time.fixedDeltaTime);
        }
        else
        {
            if (IsDrifting) boostMeter = Mathf.Min(100f, boostMeter + boostGainDrift * Time.fixedDeltaTime);
            if (!IsGrounded) boostMeter = Mathf.Min(100f, boostMeter + boostGainAir * Time.fixedDeltaTime);
        }
    }

    void Update()
    {
        UpdateWheelVisual(wheelColliderFL, wheelMeshFL);
        UpdateWheelVisual(wheelColliderFR, wheelMeshFR);
        UpdateWheelVisual(wheelColliderRL, wheelMeshRL);
        UpdateWheelVisual(wheelColliderRR, wheelMeshRR);
    }

    static void UpdateWheelVisual(WheelCollider wc, Transform mesh)
    {
        if (wc == null || mesh == null) return;
        Vector3 pos;
        Quaternion rot;
        wc.GetWorldPose(out pos, out rot);
        mesh.SetPositionAndRotation(pos, rot);
    }
}
