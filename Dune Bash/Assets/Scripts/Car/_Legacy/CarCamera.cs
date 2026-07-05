using UnityEngine;

public class CarCamera : MonoBehaviour
{
    [Header("Target")]
    public Transform target;
    public Rigidbody targetBody;
    public CarController targetCar;

    [Header("Follow Position")]
    public float baseDistance = 7.5f;
    public float baseHeight = 3.0f;
    [Tooltip("Camera pulls back this many extra meters per m/s of speed.")]
    public float distancePerSpeed = 0.045f;
    [Tooltip("Camera rises this many extra meters per m/s of speed.")]
    public float heightPerSpeed = 0.018f;
    [Tooltip("Higher = camera catches up to the target position faster.")]
    public float positionDamping = 9f;
    [Tooltip("0 = camera always trails the car's facing direction, 1 = fully trails its velocity direction (drift-friendly).")]
    [Range(0f, 1f)] public float velocityFollowBlend = 0.55f;
    [Tooltip("Below this speed (m/s), the camera trails facing direction only - avoids spin at a standstill.")]
    public float velocityBlendMinSpeed = 3f;

    [Header("Look / Aim")]
    public float lookAheadBase = 2.5f;
    public float lookAheadPerSpeed = 0.05f;
    public float lookHeight = 1.3f;
    [Tooltip("Higher = camera rotation snaps to the aim point faster.")]
    public float aimDamping = 12f;

    [Header("Speed Feel (FOV)")]
    public float baseFov = 66f;
    public float maxFov = 84f;
    [Tooltip("Speed (m/s) at which FOV reaches maxFov.")]
    public float fovSpeedForMax = 40f;
    public float fovDamping = 4f;
    public float boostFovKick = 6f;

    [Header("Roll / Tilt")]
    [Tooltip("Max camera roll (degrees) from lateral G / drifting.")]
    public float maxRollTilt = 5f;
    public float lateralToTiltFactor = 0.6f;

    [Header("Ground Clearance")]
    public LayerMask groundMask = ~0;
    public float minGroundClearance = 1.2f;

    [Header("Landing Impact Shake")]
    [Tooltip("Vertical landing speed (m/s) above which a shake triggers.")]
    public float landingShakeThreshold = 6f;
    [Tooltip("Landing speed (m/s) that produces a full-strength shake.")]
    public float landingShakeMaxSpeed = 20f;
    public float maxShakeOffset = 0.35f;
    public float shakeDecaySpeed = 5f;

    Camera cam;
    Vector3 posVelocity;
    float shakeTime;
    float shakeMagnitude;
    bool wasGrounded = true;
    float lastVerticalSpeed;

    void Awake()
    {
        cam = GetComponent<Camera>();
        if (cam == null) cam = Camera.main;
    }

    public void Shake(float magnitude01)
    {
        shakeMagnitude = Mathf.Max(shakeMagnitude, Mathf.Clamp01(magnitude01));
        shakeTime = 1f;
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

        float dist = baseDistance + speed * distancePerSpeed;
        float height = baseHeight + speed * heightPerSpeed;
        Vector3 desiredPos = target.position - followDir * dist + Vector3.up * height;

        RaycastHit hit;
        if (Physics.Raycast(target.position + Vector3.up * 2f, Vector3.down, out hit, 200f, groundMask))
        {
            float minY = hit.point.y + minGroundClearance;
            if (desiredPos.y < minY) desiredPos.y = minY;
        }

        transform.position = Vector3.SmoothDamp(transform.position, desiredPos, ref posVelocity, 1f / positionDamping);

        Vector3 lookAt = target.position + Vector3.up * lookHeight + followDir * (lookAheadBase + speed * lookAheadPerSpeed);
        Vector3 aimDir = lookAt - transform.position;
        if (aimDir.sqrMagnitude < 0.0001f) aimDir = target.forward;
        Quaternion targetRot = Quaternion.LookRotation(aimDir.normalized, Vector3.up);

        float lateralSpeed = Vector3.Dot(vel, target.right);
        float rollTarget = Mathf.Clamp(-lateralSpeed * lateralToTiltFactor, -maxRollTilt, maxRollTilt);
        Quaternion rollRot = Quaternion.AngleAxis(rollTarget, aimDir.normalized);

        transform.rotation = Quaternion.Slerp(transform.rotation, rollRot * targetRot, Time.deltaTime * aimDamping);

        DetectLandingImpact(vel);
        ApplyShake();

        if (cam != null)
        {
            float fovT = Mathf.Clamp01(speed / Mathf.Max(0.01f, fovSpeedForMax));
            bool boosting = targetCar != null && targetCar.IsBoosting;
            float targetFov = Mathf.Lerp(baseFov, maxFov, fovT) + (boosting ? boostFovKick : 0f);
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, Time.deltaTime * fovDamping);
        }
    }

    void DetectLandingImpact(Vector3 vel)
    {
        bool grounded = targetCar == null || targetCar.IsGrounded;
        if (grounded && !wasGrounded)
        {
            float impact = Mathf.Abs(lastVerticalSpeed);
            if (impact > landingShakeThreshold)
            {
                float t = Mathf.InverseLerp(landingShakeThreshold, landingShakeMaxSpeed, impact);
                Shake(t);
            }
        }
        wasGrounded = grounded;
        lastVerticalSpeed = vel.y;
    }

    void ApplyShake()
    {
        if (shakeTime <= 0f) return;
        shakeTime -= Time.deltaTime * shakeDecaySpeed;
        float m = shakeMagnitude * Mathf.Max(0f, shakeTime) * maxShakeOffset;
        transform.position += Random.insideUnitSphere * m;
    }
}
