using UnityEngine;

/// <summary>
/// Drives a vehicle's ThrottleInput/SteerInput toward LapTracker.CurrentCheckpoint's next checkpoint,
/// reusing the checkpoint graph already built for lap tracking as a ready-made waypoint path - no
/// separate navigation/waypoint system needed. Deliberately simple for a first pass: proportional
/// steering toward the next checkpoint, throttle reduced for sharp turns, no boost/handbrake/avoidance
/// yet. On a fork (multiple Next Checkpoints) it always takes index 0 - the same approximation already
/// accepted in LapTracker.SegmentProgress, not a new one.
/// </summary>
[RequireComponent(typeof(VehicleDriveTrain))]
[RequireComponent(typeof(LapTracker))]
public class AIVehicleInput : MonoBehaviour
{
    [Header("Navigation")]
    [Tooltip("Steering saturates to full lock once the angle to the next checkpoint reaches this many degrees. Lower = twitchier/more sensitive steering, higher = gentler/more gradual.")]
    public float maxSteerAngle = 40f;
    [Tooltip("Throttle reduces as the needed turn angle grows, reaching Min Cornering Throttle once the angle reaches this many degrees - keeps the AI from blowing through corners at full speed.")]
    public float slowdownAngle = 70f;
    [Tooltip("Throttle floor while navigating a sharp turn - never fully stops, just slows down.")]
    [Range(0f, 1f)] public float minCorneringThrottle = 0.4f;
    [Tooltip("Maximum lateral distance (m) this car's aim point can sit from each checkpoint's exact center, picked randomly once at spawn. Without this, every AI targets the identical point and ends up nose-to-tail in a single-file line - a fixed per-car lane offset spreads them across the track width instead.")]
    public float laneOffsetRange = 3f;

    VehicleDriveTrain driveTrain;
    LapTracker lapTracker;
    float laneOffset;

    void Awake()
    {
        driveTrain = GetComponent<VehicleDriveTrain>();
        lapTracker = GetComponent<LapTracker>();
        laneOffset = Random.Range(-laneOffsetRange, laneOffsetRange);
    }

    void Update()
    {
        if (driveTrain == null) return;

        Checkpoint current = lapTracker != null ? lapTracker.CurrentCheckpoint : null;
        Checkpoint next = current != null && current.nextCheckpoints != null && current.nextCheckpoints.Length > 0
            ? current.nextCheckpoints[0] : null;

        if (next == null)
        {
            // No checkpoint to navigate toward yet (e.g. LapTracker not initialized) - sit idle rather
            // than drive blind.
            driveTrain.ThrottleInput = 0f;
            driveTrain.SteerInput = 0f;
            driveTrain.BoostInput = false;
            driveTrain.HandbrakeInput = false;
            return;
        }

        Vector3 aimPoint = next.transform.position + next.transform.right * laneOffset;
        Vector3 toNext = aimPoint - transform.position;
        toNext.y = 0f;
        Vector3 flatForward = transform.forward;
        flatForward.y = 0f;

        float angle = Vector3.SignedAngle(flatForward, toNext, Vector3.up);
        float steer = Mathf.Clamp(angle / maxSteerAngle, -1f, 1f);

        float absAngle = Mathf.Abs(angle);
        float throttle = driveTrain.IsGrounded
            ? Mathf.Lerp(1f, minCorneringThrottle, Mathf.Clamp01(absAngle / slowdownAngle))
            : 0f; // Airborne: off the pedal rather than flooring it into a nose-dive - landing correction comes later.

        driveTrain.SteerInput = steer;
        driveTrain.ThrottleInput = throttle;
        driveTrain.BoostInput = false;
        driveTrain.HandbrakeInput = false;
    }

    /// <summary>Stops responding and zeroes the drive train's inputs - used to abandon a car on respawn so it doesn't sit there flooring the throttle.</summary>
    public void ReleaseControl()
    {
        enabled = false;
        if (driveTrain == null) return;

        driveTrain.ThrottleInput = 0f;
        driveTrain.SteerInput = 0f;
        driveTrain.BoostInput = false;
        driveTrain.HandbrakeInput = false;
    }
}
