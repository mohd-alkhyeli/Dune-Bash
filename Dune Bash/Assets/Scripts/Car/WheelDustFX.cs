using UnityEngine;

/// <summary>
/// Kicks up sand from each wheel's contact patch based on tire slip (spinning in place, drifting,
/// wheel lockup under braking) and on hard landings after airtime.
/// </summary>
public class WheelDustFX : MonoBehaviour
{
    [System.Serializable]
    public struct WheelDust
    {
        public WheelCollider wheel;
        public ParticleSystem dust;
    }

    public WheelDust[] wheels = new WheelDust[4];

    [Header("Slip Dust (spinning / drifting / lockup)")]
    [Tooltip("Combined forward+sideways slip below this produces no dust.")]
    public float slipStartThreshold = 0.35f;
    [Tooltip("Combined slip at/above this produces maximum dust.")]
    public float slipFullThreshold = 1.4f;
    public float maxEmissionRate = 70f;
    [Tooltip("Extra emission contribution from the wheel's own spin speed (rpm) - so a stationary burnout still kicks up plenty of sand even though ground speed is ~0.")]
    public float rpmEmissionBoost = 0.4f;

    [Header("Landing Impact Dust")]
    [Tooltip("Vertical speed (m/s) at touchdown below which no burst happens.")]
    public float landingImpactMinSpeed = 6f;
    [Tooltip("Vertical speed (m/s) at touchdown that produces a full-size burst.")]
    public float landingImpactMaxSpeed = 20f;
    public int landingBurstMinParticles = 8;
    public int landingBurstMaxParticles = 40;

    bool[] wasGrounded;
    float lastVerticalSpeed;
    Rigidbody rb;

    void Awake()
    {
        rb = GetComponentInParent<Rigidbody>();
        wasGrounded = new bool[wheels.Length];
        for (int i = 0; i < wheels.Length; i++) wasGrounded[i] = true;
    }

    void FixedUpdate()
    {
        float verticalSpeed = rb != null ? rb.linearVelocity.y : 0f;

        for (int i = 0; i < wheels.Length; i++)
        {
            WheelCollider wc = wheels[i].wheel;
            ParticleSystem ps = wheels[i].dust;
            if (wc == null || ps == null) continue;

            WheelHit hit;
            bool grounded = wc.GetGroundHit(out hit);
            var emission = ps.emission;

            if (grounded)
            {
                ps.transform.position = hit.point;

                float slip = Mathf.Sqrt(hit.forwardSlip * hit.forwardSlip + hit.sidewaysSlip * hit.sidewaysSlip);
                float rpmFactor = Mathf.Clamp01(Mathf.Abs(wc.rpm) * rpmEmissionBoost / 300f);
                float t = Mathf.InverseLerp(slipStartThreshold, slipFullThreshold, slip + rpmFactor);
                emission.rateOverTime = Mathf.Lerp(0f, maxEmissionRate, t);

                if (!wasGrounded[i] && Mathf.Abs(lastVerticalSpeed) > landingImpactMinSpeed)
                {
                    float impactT = Mathf.InverseLerp(landingImpactMinSpeed, landingImpactMaxSpeed, Mathf.Abs(lastVerticalSpeed));
                    ps.Emit(Mathf.RoundToInt(Mathf.Lerp(landingBurstMinParticles, landingBurstMaxParticles, impactT)));
                }
            }
            else
            {
                emission.rateOverTime = 0f;
            }

            wasGrounded[i] = grounded;
        }

        lastVerticalSpeed = verticalSpeed;
    }
}
