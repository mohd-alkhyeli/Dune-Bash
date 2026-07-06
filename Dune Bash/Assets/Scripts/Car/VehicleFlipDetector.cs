using UnityEngine;

/// <summary>
/// Reports when a vehicle is stuck on its roof/side: tipped past a threshold and at rest for a
/// sustained duration. Deliberately does not trigger on a mid-air flip during a jump/trick, since
/// gravity keeps accelerating a falling vehicle past the rest threshold well before the duration
/// elapses - only a vehicle that's actually settled and can't right itself counts. Uses the
/// Rigidbody's own velocity rather than VehicleDriveTrain.IsGrounded deliberately: IsGrounded comes
/// from WheelCollider suspension rays checking each wheel's own local "down" direction, which points
/// up into the air once the chassis is actually flipped - so IsGrounded and "flipped" can never both
/// be true at once if you use it here. Also carries a debug key to flip the vehicle on demand for
/// testing, since waiting for a real crash every time is slow.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class VehicleFlipDetector : MonoBehaviour
{
    [Header("Flip Detection")]
    [Tooltip("Dot product between the vehicle's up vector and world up below which it counts as tipped (on its side or roof). 1 = perfectly upright, 0 = on its side, -1 = fully upside down.")]
    [Range(-1f, 1f)] public float flippedUpDotThreshold = 0.2f;
    [Tooltip("Linear speed (m/s) below which the vehicle counts as at rest rather than still tumbling or sliding.")]
    public float restLinearSpeedThreshold = 0.5f;
    [Tooltip("Angular speed (rad/s) below which the vehicle counts as at rest rather than still tumbling or sliding.")]
    public float restAngularSpeedThreshold = 0.5f;
    [Tooltip("How long (seconds) the vehicle must stay tipped over AND at rest before it's reported as stuck and needs a respawn.")]
    public float flippedDuration = 2f;

    [Header("Debug")]
    [Tooltip("Key that applies a strong flip torque to the vehicle, for testing respawn without waiting for a real crash.")]
    public KeyCode debugFlipKey = KeyCode.F9;
    [Tooltip("Torque impulse (Nm) applied around the vehicle's forward axis when Debug Flip Key is pressed - tuned to reliably roll it onto its roof.")]
    public float debugFlipTorque = 6000f;

    /// <summary>True once the vehicle has been tipped over and grounded for Flipped Duration seconds.</summary>
    public bool IsFlipped { get; private set; }

    Rigidbody rb;
    float flippedTimer;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    void Update()
    {
        if (Input.GetKeyDown(debugFlipKey))
            rb.AddTorque(transform.forward * debugFlipTorque, ForceMode.Impulse);
    }

    void FixedUpdate()
    {
        bool tippedOver = Vector3.Dot(transform.up, Vector3.up) < flippedUpDotThreshold;
        bool atRest = rb.linearVelocity.sqrMagnitude < restLinearSpeedThreshold * restLinearSpeedThreshold
                   && rb.angularVelocity.sqrMagnitude < restAngularSpeedThreshold * restAngularSpeedThreshold;
        bool stuck = tippedOver && atRest;

        flippedTimer = stuck ? flippedTimer + Time.fixedDeltaTime : 0f;
        IsFlipped = flippedTimer >= flippedDuration;
    }
}
