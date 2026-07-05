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
        [Tooltip("The physics wheel this dust emitter reads slip/rpm/ground-contact from.")]
        public WheelCollider wheel;
        [Tooltip("Particle system positioned at this wheel's contact patch - emission rate and bursts are driven entirely at runtime, not authored here.")]
        public ParticleSystem dust;
    }

    [Tooltip("One entry per wheel, pairing its WheelCollider with the dust ParticleSystem to drive.")]
    public WheelDust[] wheels = new WheelDust[4];

    [Header("Slip Dust (spinning / drifting / lockup)")]
    [Tooltip("Combined forward+sideways slip below this produces no dust.")]
    public float slipStartThreshold = 0.35f;
    [Tooltip("Combined slip at/above this produces maximum dust.")]
    public float slipFullThreshold = 1.4f;
    [Tooltip("Particles emitted per second at maximum slip (t = 1 on the slip/rpm curve below).")]
    public float maxEmissionRate = 70f;
    [Tooltip("Extra emission contribution from the wheel's own spin speed (rpm) - so a stationary burnout still kicks up plenty of sand even though ground speed is ~0.")]
    public float rpmEmissionBoost = 0.4f;

    [Header("Landing Impact Dust")]
    [Tooltip("Vertical speed (m/s) at touchdown below which no burst happens.")]
    public float landingImpactMinSpeed = 6f;
    [Tooltip("Vertical speed (m/s) at touchdown that produces a full-size burst.")]
    public float landingImpactMaxSpeed = 20f;
    [Tooltip("Particle count for a landing burst right at Landing Impact Min Speed.")]
    public int landingBurstMinParticles = 8;
    [Tooltip("Particle count for a landing burst at/above Landing Impact Max Speed.")]
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
