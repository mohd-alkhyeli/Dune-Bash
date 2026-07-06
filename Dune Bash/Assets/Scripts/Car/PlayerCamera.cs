using UnityEngine;

/// <summary>
/// Chase camera: damped follow that blends between facing and velocity direction (drift-friendly),
/// speed-based FOV, turn lean, ground-clearance clamping, and shake on hard hits or landings.
/// </summary>
public class PlayerCamera : MonoBehaviour
{
    [Header("Target")]
    public Transform target;
    public Rigidbody targetBody;

    [Header("Follow")]
    [Tooltip("Base distance (m) behind the vehicle the camera sits.")]
    public float distance = 8f;
    [Tooltip("Base height (m) above the vehicle the camera sits.")]
    public float height = 3.5f;
    [Tooltip("Higher = camera catches up to the target position faster; lower feels heavier/laggier.")]
    public float followDamping = 8f;
    [Tooltip("Height (m) above the vehicle the camera aims at.")]
    public float lookHeight = 1.3f;
    [Tooltip("0 = camera always trails the vehicle's facing direction, 1 = fully trails its velocity direction. Raise this so the camera follows a drift/slide instead of staying locked to the nose.")]
    [Range(0f, 1f)] public float velocityFollowBlend = 0.6f;
    [Tooltip("Below this speed (m/s), the camera trails facing direction only - avoids the follow direction spinning erratically at a near-standstill, where velocity direction is meaningless.")]
    public float velocityBlendMinSpeed = 3f;

    [Header("Speed Feel (FOV)")]
    [Tooltip("Field of view (degrees) at a standstill.")]
    public float baseFov = 66f;
    [Tooltip("Field of view (degrees) at/above Fov Speed For Max, before any boost kick is added.")]
    public float maxFov = 88f;
    [Tooltip("Speed (m/s) at which FOV reaches Max Fov. Lower this to make the FOV widen sooner/more aggressively at everyday speeds.")]
    public float fovSpeedForMax = 24f;
    [Tooltip("Higher = FOV catches up to its target value faster.")]
    public float fovDamping = 4f;

    [Header("Boost Feel")]
    [Tooltip("Optional - the vehicle's drive train, used only to detect actual boosting (input held AND fuel remaining) for the FOV kick and jitter below. Leave unassigned to disable both.")]
    public VehicleDriveTrain targetDriveTrain;
    [Tooltip("Extra field of view (degrees) added on top of the speed-based FOV while boosting.")]
    public float boostFovKick = 8f;
    [Tooltip("Maximum shake strength (0-1) while boosting, distinct from the impact/landing shake. Scaled down by how close current speed is to Boost Top Speed, so it ramps up from ~0 as the vehicle accelerates into the boost instead of snapping to full strength immediately.")]
    [Range(0f, 1f)] public float boostShakeStrength = 0.3f;

    [Header("Impact Shake")]
    [Tooltip("Speed lost (m/s) in a single frame above which a shake triggers - a quick way to sell hard hits.")]
    public float shakeSpeedLossThreshold = 8f;
    [Tooltip("Downward speed (m/s) below which the vehicle is considered falling, so a landing can be detected.")]
    public float fallSpeedThreshold = 3f;
    [Tooltip("Vertical speed (m/s) recovered in a single frame upon landing, above which a shake triggers - separate from Shake Speed Loss Threshold since a clean jump landing barely changes total speed but the vertical impact is still large.")]
    public float landingShakeThreshold = 6f;
    [Tooltip("Fall speed (m/s) right before landing that produces a full-strength shake.")]
    public float landingShakeMaxSpeed = 20f;
    [Tooltip("Maximum camera position offset (m) at full shake strength.")]
    public float maxShakeOffset = 0.3f;
    [Tooltip("How quickly a triggered shake decays back to nothing.")]
    public float shakeDecaySpeed = 5f;

    [Header("Turn Lean")]
    [Tooltip("Max camera roll (degrees) from lateral speed - leans the camera into turns.")]
    public float maxRollTilt = 6f;
    public float lateralToTiltFactor = 0.6f;

    [Header("Ground Clearance")]
    [Tooltip("Layers the ground-clearance raycast checks against.")]
    public LayerMask groundMask = ~0;
    [Tooltip("Minimum height (m) the camera keeps above whatever terrain is directly below the vehicle - stops the camera dipping below a dune face or into a jump's landing slope.")]
    public float minGroundClearance = 1.2f;

    Camera cam;
    Vector3 posVelocity;
    float lastSpeed;
    float lastVerticalSpeed;
    float shakeStrength;

    void Awake()
    {
        cam = GetComponent<Camera>();
        if (cam == null) cam = Camera.main;
    }

    public void Shake(float magnitude01)
    {
        shakeStrength = Mathf.Max(shakeStrength, Mathf.Clamp01(magnitude01));
    }

    void LateUpdate()
    {
        if (target == null) return;

        Vector3 vel = targetBody != null ? targetBody.linearVelocity : Vector3.zero;
        float speed = vel.magnitude;

        Vector3 velDir = speed > velocityBlendMinSpeed ? vel.normalized : target.forward;
        Vector3 followDir = Vector3.Slerp(target.forward, velDir, velocityFollowBlend);
        if (followDir.sqrMagnitude < 0.0001f) followDir = target.forward;
        followDir.Normalize();

        Vector3 desiredPos = target.position - followDir * distance + Vector3.up * height;

        RaycastHit groundHit;
        if (Physics.Raycast(target.position + Vector3.up * 2f, Vector3.down, out groundHit, 200f, groundMask))
        {
            float minY = groundHit.point.y + minGroundClearance;
            if (desiredPos.y < minY) desiredPos.y = minY;
        }

        transform.position = Vector3.SmoothDamp(transform.position, desiredPos, ref posVelocity, 1f / followDamping);

        Vector3 lookAt = target.position + Vector3.up * lookHeight;
        Vector3 aimDir = lookAt - transform.position;
        if (aimDir.sqrMagnitude > 0.0001f)
        {
            Quaternion aimRot = Quaternion.LookRotation(aimDir.normalized, Vector3.up);

            float lateralSpeed = Vector3.Dot(vel, target.right);
            float rollTarget = Mathf.Clamp(-lateralSpeed * lateralToTiltFactor, -maxRollTilt, maxRollTilt);
            Quaternion rollRot = Quaternion.AngleAxis(rollTarget, aimDir.normalized);

            transform.rotation = rollRot * aimRot;
        }

        float speedDrop = lastSpeed - speed;
        if (speedDrop > shakeSpeedLossThreshold)
        {
            Shake(Mathf.InverseLerp(shakeSpeedLossThreshold, shakeSpeedLossThreshold * 3f, speedDrop));
        }
        lastSpeed = speed;

        bool isBoosting = targetDriveTrain != null && targetDriveTrain.IsBoosting;
        if (isBoosting)
        {
            float boostSpeedRatio = Mathf.Clamp01(speed / Mathf.Max(0.01f, targetDriveTrain.boostTopSpeed / 2));
            Shake(boostShakeStrength * boostSpeedRatio);
        }

        DetectLandingImpact(vel.y);
        ApplyShake();

        if (cam != null)
        {
            float fovT = Mathf.Clamp01(speed / Mathf.Max(0.01f, fovSpeedForMax));
            float targetFov = Mathf.Lerp(baseFov, maxFov, fovT) + (isBoosting ? boostFovKick : 0f);
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, Time.deltaTime * fovDamping);
        }
    }

    void DetectLandingImpact(float verticalSpeed)
    {
        // A landing shows up as a sudden upward correction in vertical speed right after a period of
        // falling - no grounded flag needed, so the camera stays decoupled from the drivetrain.
        bool wasFalling = lastVerticalSpeed < -fallSpeedThreshold;
        float suddenUpwardChange = verticalSpeed - lastVerticalSpeed;
        if (wasFalling && suddenUpwardChange > landingShakeThreshold)
        {
            float impact = Mathf.Abs(lastVerticalSpeed);
            float t = Mathf.InverseLerp(landingShakeThreshold, landingShakeMaxSpeed, impact);
            Shake(t);
        }
        lastVerticalSpeed = verticalSpeed;
    }

    void ApplyShake()
    {
        if (shakeStrength <= 0f) return;
        shakeStrength = Mathf.Max(0f, shakeStrength - Time.deltaTime * shakeDecaySpeed);
        transform.position += Random.insideUnitSphere * (shakeStrength * maxShakeOffset);
    }
}
