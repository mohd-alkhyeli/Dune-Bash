using UnityEngine;

/// <summary>
/// Tracks a car's overall structural health and reduces it on hard collisions. Deliberately a single
/// aggregate number for now, not per-part - other systems (EngineSmokeFX today, a damage UI or part
/// detachment later) read HealthFraction/LastImpactPoint rather than reacting to collisions themselves,
/// so a new reaction never needs its own collision hook. LastImpactPoint specifically is banked for a
/// future per-part damage/detachment system to work out which part got hit - nothing consumes it yet,
/// but capturing it here means that hook doesn't need to be reopened later.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class VehicleHealth : MonoBehaviour
{
    [Header("Health")]
    [Tooltip("Health this car starts (and respawns) with.")]
    public float maxHealth = 100f;

    [Header("Collision Damage")]
    [Tooltip("Relative impact speed (m/s) below which a collision deals no damage at all - filters out gentle scrapes and nudges rather than chipping health off every little bump.")]
    public float minDamageSpeed = 4f;
    [Tooltip("Relative impact speed (m/s) at/above which a collision deals full Max Damage Per Hit.")]
    public float maxDamageSpeed = 20f;
    [Tooltip("Health lost from a single collision at/above Max Damage Speed.")]
    public float maxDamagePerHit = 40f;

    /// <summary>Current health, from 0 to Max Health.</summary>
    public float Health { get; private set; }
    /// <summary>Current health normalized to 0-1, for UI meters and EngineSmokeFX.</summary>
    public float HealthFraction => maxHealth > 0f ? Health / maxHealth : 0f;
    /// <summary>True once Health has reached zero.</summary>
    public bool IsWrecked => Health <= 0f;
    /// <summary>World-space point of the most recent damaging collision.</summary>
    public Vector3 LastImpactPoint { get; private set; }

    void Awake()
    {
        Health = maxHealth;
    }

    void OnCollisionEnter(Collision collision)
    {
        float impactSpeed = collision.relativeVelocity.magnitude;
        if (impactSpeed < minDamageSpeed) return;

        float t = Mathf.InverseLerp(minDamageSpeed, maxDamageSpeed, impactSpeed);
        LastImpactPoint = collision.GetContact(0).point;
        ApplyDamage(Mathf.Lerp(0f, maxDamagePerHit, t));
    }

    /// <summary>Reduces Health directly, clamped at zero. Public so other systems (hazards, scripted damage) can hurt the car without going through a real collision.</summary>
    public void ApplyDamage(float amount)
    {
        Health = Mathf.Max(0f, Health - amount);
    }
}
