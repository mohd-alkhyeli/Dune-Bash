using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Kicks up sand from each wheel's contact patch, driven entirely by WheelCollider physics - combined
/// forward+sideways slip (spinning in place, drifting, lockup under braking) controls emission rate,
/// and a hard landing after airtime triggers an extra burst. Reads its wheel list straight from
/// VehicleDriveTrain.wheels and builds its own placeholder particle systems at runtime, so there is
/// nothing to wire up by hand beyond adding this component - swap in real art later by replacing
/// GetPlaceholderDustTexture()'s texture (or just assigning a different material on each "Dust" child
/// once real art exists).
/// </summary>
[RequireComponent(typeof(VehicleDriveTrain))]
public class WheelDustFX : MonoBehaviour
{
    [Header("Slip Dust (spinning / drifting / lockup)")]
    [Tooltip("Combined forward+sideways slip below this produces no dust.")]
    public float slipStartThreshold = 0.35f;
    [Tooltip("Combined slip at/above this produces maximum dust.")]
    public float slipFullThreshold = 1.4f;
    [Tooltip("Particles emitted per second at maximum slip.")]
    public float maxEmissionRate = 70f;

    [Header("Landing Impact Dust")]
    [Tooltip("Vertical speed (m/s) at touchdown below which no burst happens.")]
    public float landingImpactMinSpeed = 6f;
    [Tooltip("Vertical speed (m/s) at touchdown that produces a full-size burst.")]
    public float landingImpactMaxSpeed = 20f;
    [Tooltip("Particle count for a landing burst right at Landing Impact Min Speed.")]
    public int landingBurstMinParticles = 8;
    [Tooltip("Particle count for a landing burst at/above Landing Impact Max Speed.")]
    public int landingBurstMaxParticles = 40;

    [Header("Spray Direction")]
    [Tooltip("Spray elevation (degrees above parallel-to-ground) at zero slip. A cleanly rolling wheel barely disturbs sand, so it should stay low and close to the ground.")]
    [Range(0f, 90f)] public float minSprayAngle = 15f;
    [Tooltip("Spray elevation (degrees above parallel-to-ground) at maximum slip. A spinning or sliding wheel flings material tangentially off the tread, producing a steep 'roostertail' arc - this is what makes wheelspin in place read clearly even at zero ground speed, which vehicle speed alone couldn't do.")]
    [Range(0f, 90f)] public float maxSprayAngle = 70f;

    [Header("Look (placeholder - swap for real art later)")]
    [Tooltip("Tint applied to the placeholder dust sprite. Alpha controls how opaque a single particle looks.")]
    public Color dustColor = new Color(0.76f, 0.64f, 0.42f, 0.55f);
    [Tooltip("Reference particle size (m) that Start/End Size Scale below multiply against - each particle's actual size also gets randomized 0.7x-1.3x on top of that at spawn.")]
    public float particleSize = 0.35f;
    [Tooltip("How long (s) a dust particle lives before fading out.")]
    public float particleLifetime = 0.6f;
    [Tooltip("Size multiplier at the moment a particle spawns. Below 1 makes it appear small and grow; 1 spawns it at full size; above 1 makes it shrink from an oversized start.")]
    [Range(0f, 2f)] public float startSizeScale = 0.4f;
    [Tooltip("Size multiplier right before a particle despawns. Combined with Start Size Scale, this is the full 'starting size vs ending size' range each particle eases through over its life - e.g. 0.4 to 1 reads as a puff expanding as it disperses.")]
    [Range(0f, 2f)] public float endSizeScale = 1f;
    [Tooltip("How fast sand is flung outward from the contact patch (m/s) - this is what sells 'kicked up' rather than a puff that just appears in place.")]
    public float kickSpeed = 2.5f;

    VehicleDriveTrain driveTrain;
    Rigidbody rb;
    ParticleSystem[] dustSystems;
    bool[] wasGrounded;

    static Texture2D placeholderTexture;
    static Material placeholderMaterial;

    void Awake()
    {
        driveTrain = GetComponent<VehicleDriveTrain>();
        rb = GetComponent<Rigidbody>();
        BuildDustSystems();
    }

    void BuildDustSystems()
    {
        Transform[] wheels = driveTrain.wheels;
        dustSystems = new ParticleSystem[wheels.Length];
        wasGrounded = new bool[wheels.Length];

        for (int i = 0; i < wheels.Length; i++)
        {
            wasGrounded[i] = true;
            if (wheels[i] == null) continue;
            dustSystems[i] = CreateDustSystem(wheels[i]);
        }
    }

    ParticleSystem CreateDustSystem(Transform wheel)
    {
        GameObject go = new GameObject("Dust");
        go.transform.SetParent(wheel, false);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(particleLifetime * 0.8f, particleLifetime * 1.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(kickSpeed * 0.7f, kickSpeed * 1.3f);
        main.startSize = new ParticleSystem.MinMaxCurve(particleSize * 0.7f, particleSize * 1.3f);
        main.startColor = dustColor;
        // Sand falls back down under its own weight instead of drifting upward like smoke would.
        main.gravityModifier = 1f;
        // Decouples flung particles from the wheel's own spin/position once emitted.
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.rateOverTime = 0f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 20f;
        shape.radius = 0.05f;

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        AnimationCurve sizeCurve = AnimationCurve.EaseInOut(0f, startSizeScale, 1f, endSizeScale);
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(dustColor, 0f), new GradientColorKey(dustColor, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(dustColor.a, 0.15f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = gradient;

        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.material = GetPlaceholderMaterial();

        return ps;
    }

    void FixedUpdate()
    {
        Transform[] wheels = driveTrain.wheels;
        float verticalSpeed = rb != null ? rb.linearVelocity.y : 0f;

        for (int i = 0; i < wheels.Length; i++)
        {
            if (wheels[i] == null || dustSystems[i] == null) continue;
            WheelCollider wc = wheels[i].GetComponent<WheelCollider>();
            if (wc == null) continue;

            ParticleSystem ps = dustSystems[i];
            bool grounded = wc.GetGroundHit(out WheelHit hit);
            var emission = ps.emission;

            if (grounded)
            {
                ps.transform.position = hit.point;

                float slip = Mathf.Sqrt(hit.forwardSlip * hit.forwardSlip + hit.sidewaysSlip * hit.sidewaysSlip);
                float t = Mathf.InverseLerp(slipStartThreshold, slipFullThreshold, slip);

                // Aim the emission cone backward relative to the vehicle, tilted up from parallel-to-
                // ground by an angle driven by the same slip that drives emission rate - clean rolling
                // stays low, spinning/sliding kicks up into a steeper roostertail arc.
                float sprayAngle = Mathf.Lerp(minSprayAngle, maxSprayAngle, t);
                Vector3 kickDirection = Quaternion.AngleAxis(sprayAngle, transform.right) * -transform.forward;
                ps.transform.rotation = Quaternion.LookRotation(kickDirection);

                emission.rateOverTime = Mathf.Lerp(0f, maxEmissionRate, t);

                if (!wasGrounded[i] && Mathf.Abs(verticalSpeed) > landingImpactMinSpeed)
                {
                    float impactT = Mathf.InverseLerp(landingImpactMinSpeed, landingImpactMaxSpeed, Mathf.Abs(verticalSpeed));
                    ps.Emit(Mathf.RoundToInt(Mathf.Lerp(landingBurstMinParticles, landingBurstMaxParticles, impactT)));
                }
            }
            else
            {
                emission.rateOverTime = 0f;
            }

            wasGrounded[i] = grounded;
        }
    }

    /// <summary>
    /// Visualizes the Min/Max Spray Angle range at every wheel without needing Play Mode: a gray line
    /// for the parallel-to-ground baseline (0 degrees), sandy lines for the min and max spray
    /// directions, and an arc sweeping between them - the range the spray actually swings through as
    /// slip goes from none to maximum.
    /// </summary>
    void OnDrawGizmosSelected()
    {
        VehicleDriveTrain dt = driveTrain != null ? driveTrain : GetComponent<VehicleDriveTrain>();
        if (dt == null || dt.wheels == null) return;

        foreach (Transform wheel in dt.wheels)
        {
            if (wheel != null) DrawSprayGizmo(GetGizmoOrigin(wheel));
        }
    }

    /// <summary>
    /// Matches the real emission point exactly while playing and grounded (the same WheelHit.point the
    /// particle system itself is repositioned to every FixedUpdate). Outside Play Mode there's no
    /// physics to query, so this approximates the resting contact patch using the wheel's own Radius -
    /// still far closer than the wheel's pivot, which sits a full tire-radius above where dust actually
    /// kicks up.
    /// </summary>
    Vector3 GetGizmoOrigin(Transform wheel)
    {
        WheelCollider wc = wheel.GetComponent<WheelCollider>();
        if (wc == null) return wheel.position;

        if (Application.isPlaying && wc.GetGroundHit(out WheelHit hit)) return hit.point;
        return wheel.position - wheel.up * wc.radius;
    }

    void DrawSprayGizmo(Vector3 origin)
    {
        const float length = 1.5f;
        Vector3 groundDirection = -transform.forward;
        Vector3 minDirection = Quaternion.AngleAxis(minSprayAngle, transform.right) * groundDirection;
        Vector3 maxDirection = Quaternion.AngleAxis(maxSprayAngle, transform.right) * groundDirection;

        Gizmos.color = Color.gray;
        Gizmos.DrawLine(origin, origin + groundDirection * length);

        Color sandy = new Color(0.9f, 0.7f, 0.3f);
        Gizmos.color = sandy;
        Gizmos.DrawLine(origin, origin + minDirection * length);
        Gizmos.DrawLine(origin, origin + maxDirection * length);

#if UNITY_EDITOR
        Handles.color = new Color(sandy.r, sandy.g, sandy.b, 0.35f);
        Handles.DrawSolidArc(origin, transform.right, minDirection, maxSprayAngle - minSprayAngle, length);
        Handles.color = sandy;
        Handles.Label(origin + minDirection * length, $"{minSprayAngle:0}°");
        Handles.Label(origin + maxDirection * length, $"{maxSprayAngle:0}°");
#endif
    }

    /// <summary>Soft round white dot, generated once and shared by every wheel/car - tinted per-particle via Dust Color, so no external texture is needed until real art replaces it.</summary>
    static Material GetPlaceholderMaterial()
    {
        if (placeholderMaterial != null) return placeholderMaterial;

        placeholderMaterial = new Material(Shader.Find("Sprites/Default"));
        placeholderMaterial.mainTexture = GetPlaceholderTexture();
        return placeholderMaterial;
    }

    static Texture2D GetPlaceholderTexture()
    {
        if (placeholderTexture != null) return placeholderTexture;

        const int size = 32;
        placeholderTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Vector2 center = new Vector2(size - 1, size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center) / (size * 0.5f);
                float alpha = Mathf.Clamp01(1f - dist);
                alpha *= alpha;
                placeholderTexture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        placeholderTexture.Apply();
        return placeholderTexture;
    }
}
