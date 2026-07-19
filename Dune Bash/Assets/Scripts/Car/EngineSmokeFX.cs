using UnityEngine;

/// <summary>
/// Emits engine smoke that thickens and darkens as VehicleHealth's HealthFraction drops - a purely
/// visual damage readout, no gameplay effect of its own. Builds its own placeholder particle system at
/// runtime the same way WheelDustFX does, so the only manual step is adding this component (and
/// optionally assigning Smoke Origin to a point near the hood/engine bay once one exists - it defaults
/// to this object's own transform otherwise).
/// </summary>
[RequireComponent(typeof(VehicleHealth))]
public class EngineSmokeFX : MonoBehaviour
{
    [Header("Smoke Source")]
    [Tooltip("Where smoke originates from - ideally a child transform near the hood/engine bay. Defaults to this object's own transform if left unassigned.")]
    public Transform smokeOrigin;

    [Header("Health Thresholds")]
    [Tooltip("Health fraction (0-1) above which no smoke is emitted at all - a lightly dinged car shouldn't smoke.")]
    [Range(0f, 1f)] public float smokeStartHealth = 0.6f;
    [Tooltip("Health fraction (0-1) at/below which smoke reaches maximum emission - the engine's about to give out.")]
    [Range(0f, 1f)] public float smokeMaxHealth = 0.15f;
    [Tooltip("Particles emitted per second once Health is at/below Smoke Max Health.")]
    public float maxEmissionRate = 40f;

    [Header("Look (placeholder - swap for real art later)")]
    [Tooltip("Smoke color right as it starts at Smoke Start Health - a car just starting to smoke puffs out something thin and grey.")]
    public Color lightSmokeColor = new Color(0.6f, 0.6f, 0.6f, 0.5f);
    [Tooltip("Smoke color at/below Smoke Max Health - a dying engine puffs out thick, dark, near-black smoke.")]
    public Color heavySmokeColor = new Color(0.05f, 0.05f, 0.05f, 0.75f);
    [Tooltip("Reference particle size (m) - each particle also gets randomized 0.7x-1.3x on top of this at spawn.")]
    public float particleSize = 0.5f;
    [Tooltip("How long (s) a smoke particle lives before fading out.")]
    public float particleLifetime = 1.5f;
    [Tooltip("Size multiplier at the moment a particle spawns.")]
    [Range(0f, 2f)] public float startSizeScale = 0.5f;
    [Tooltip("Size multiplier right before a particle despawns - smoke should grow as it disperses, unlike dust settling back down.")]
    [Range(0f, 2f)] public float endSizeScale = 1.6f;
    [Tooltip("Upward drift speed (m/s) - smoke rises rather than falling like dust would.")]
    public float riseSpeed = 1.2f;

    VehicleHealth health;
    ParticleSystem smokeSystem;

    static Texture2D placeholderTexture;
    static Material placeholderMaterial;

    void Awake()
    {
        health = GetComponent<VehicleHealth>();
        if (smokeOrigin == null) smokeOrigin = transform;
        smokeSystem = CreateSmokeSystem();
    }

    ParticleSystem CreateSmokeSystem()
    {
        GameObject go = new GameObject("EngineSmoke");
        go.transform.SetParent(smokeOrigin, false);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(particleLifetime * 0.8f, particleLifetime * 1.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(riseSpeed * 0.7f, riseSpeed * 1.3f);
        main.startSize = new ParticleSystem.MinMaxCurve(particleSize * 0.7f, particleSize * 1.3f);
        main.startColor = lightSmokeColor;
        // Negative gravity modifier makes smoke drift upward under its own buoyancy instead of falling like dust would.
        main.gravityModifier = -0.15f;
        // Decouples emitted particles from the car's own motion once released, same reasoning as WheelDustFX.
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.rateOverTime = 0f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 12f;
        shape.radius = 0.05f;

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        AnimationCurve sizeCurve = AnimationCurve.EaseInOut(0f, startSizeScale, 1f, endSizeScale);
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        // Alpha-only envelope on flat white - the actual tint comes from main.startColor, which
        // FixedUpdate recolors every frame as health changes, rather than baking a fixed color in here.
        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = gradient;

        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.material = GetPlaceholderMaterial();

        return ps;
    }

    void FixedUpdate()
    {
        var emission = smokeSystem.emission;
        var main = smokeSystem.main;

        // 0 at/above Smoke Start Health (no smoke), 1 at/below Smoke Max Health (full smoke).
        float t = 1f - Mathf.InverseLerp(smokeMaxHealth, smokeStartHealth, health.HealthFraction);

        emission.rateOverTime = Mathf.Lerp(0f, maxEmissionRate, t);
        main.startColor = Color.Lerp(lightSmokeColor, heavySmokeColor, t);
    }

    /// <summary>Soft round white dot, generated once and shared by every car - tinted per-particle via startColor, so no external texture is needed until real art replaces it.</summary>
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
