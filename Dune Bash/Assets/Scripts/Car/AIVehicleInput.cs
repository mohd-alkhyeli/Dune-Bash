using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Drives a vehicle's ThrottleInput/SteerInput using Pure Pursuit for the desired heading, resolved
/// through a small context-steering map rather than summing independent steering deltas. Every frame:
/// Pure Pursuit's desired heading (toward a point some distance ahead along LapTracker's checkpoint
/// chain, scaled by speed via LapTracker.GetLookaheadPoint) is spread into interest[] across 9 sampled
/// directions (BuildInterest); everything else this car needs to react to - other cars, the fence,
/// static scenery, anything solid that isn't Terrain or this car's own hull - is sensed by one raycast
/// fan across those same directions and spreads danger[] into them (AddObstacleDanger); ResolveSteer
/// then picks the score-weighted best heading from interest minus danger. This is what a naive sum of
/// independent steering deltas got wrong: two opposing corrections (dodge a car toward the fence, sense
/// the fence, push back) could cancel to near-zero right when both mattered most - here they just make
/// their shared directions score badly, so the resolved heading shifts to whatever's left instead.
/// Lane offset is still clamped against each checkpoint's own gate width (GetSafeHalfWidth) so a car's
/// personal lane bias can't aim it past the track boundary on a narrower part of the track - that
/// gate-width margin only shapes the aim point (what the car wants), while the obstacle raycasts are
/// what actually contribute danger now, since ground-truth sensing makes a separate current-position
/// boundary clamp on the danger side redundant. Deliberately simple beyond that for a first pass: no
/// boost/handbrake yet. On a fork (multiple Next Checkpoints) it always takes index 0 - the same
/// approximation already accepted in LapTracker.SegmentProgress/GetLookaheadPoint, not a new one.
/// </summary>
[RequireComponent(typeof(LapTracker))]
public class AIVehicleInput : MonoBehaviour
{
    [Header("Navigation")]
    [Tooltip("Steering saturates to full lock once the resolved heading is this many degrees from forward. Lower = twitchier/more sensitive steering, higher = gentler/more gradual.")]
    public float maxSteerAngle = 40f;
    [Tooltip("Throttle reduces as the needed turn angle grows, reaching Min Cornering Throttle once the angle reaches this many degrees - keeps the AI from blowing through corners at full speed.")]
    public float slowdownAngle = 70f;
    [Tooltip("Throttle floor while navigating a sharp turn - never fully stops, just slows down.")]
    [Range(0f, 1f)] public float minCorneringThrottle = 0.4f;
    [Tooltip("Maximum lateral distance (m) this car's aim point can sit from the track's center, picked randomly once at spawn. Without this, every AI targets the identical line and ends up nose-to-tail in a single-file line - a fixed per-car lane offset spreads them across the track width instead.")]
    public float laneOffsetRange = 3f;

    [Header("Pure Pursuit")]
    [Tooltip("How far ahead (in seconds of current travel) the aim point sits on the path - the core Pure Pursuit tuning knob. The resulting distance (current speed x this) is clamped between Min/Max Lookahead Distance below, then GetLookaheadPoint walks that far down the checkpoint chain.")]
    public float lookaheadTime = 0.8f;
    [Tooltip("Lookahead distance (m) floor - keeps the aim point meaningfully ahead of the car even at low/zero speed, rather than collapsing onto the car's own position and leaving it with no steering signal.")]
    public float minLookaheadDistance = 4f;
    [Tooltip("Lookahead distance (m) ceiling - keeps a very fast car from aiming so far down the path that it cuts corners short.")]
    public float maxLookaheadDistance = 25f;

    [Header("Context Steering")]
    [Tooltip("Degrees around a threat's exact bearing that also receive scaled-down danger, so nearby directions aren't ignored just because the threat isn't dead-on that heading. Shared by both car-avoidance and obstacle-avoidance.")]
    public float dangerSpreadDegrees = 60f;

    [Header("Avoidance")]
    [Tooltip("Forward dot-product threshold above which a sensed car counts as 'ahead' rather than 'alongside' for the speed-following leniency check below - 1 = dead ahead only, 0 = anything in the forward hemisphere. Converted internally (via acos) to an angle compared against the fixed sampled directions, since obstacle sensing now works by raycasting exact directions rather than measuring a raw bearing to another car's position.")]
    [Range(0f, 1f)] public float aheadDotThreshold = 0.5f;
    [Tooltip("A car ahead moving at least this fraction of this car's own speed is treated as racing normally and left alone, rather than braked/swerved around - keeps the AI from panicking over a car it should just be following. A car alongside is always treated as a collision risk regardless of speed. Only applies to cars - every other obstacle (fence, scenery, a stopped/wrecked car) is always dangerous when sensed, no leniency.")]
    [Range(0f, 1f)] public float racingSpeedRatio = 0.85f;
    [Tooltip("How strongly a dangerous nearby car contributes danger at maximum urgency (right on top of it).")]
    [Range(0f, 1f)] public float avoidanceSteerStrength = 0.6f;
    [Tooltip("How much throttle is cut when anything dangerous sits close ahead, at maximum urgency - whatever it is, not just a car.")]
    [Range(0f, 1f)] public float avoidanceThrottleCut = 0.7f;

    [Header("Obstacle Sensing")]
    [Tooltip("Fraction of a checkpoint's gate width considered safely usable for lane offset before risking the track boundary - leaves room for the car's own width plus a buffer. 1 = use the full gate width (no buffer), lower = more conservative.")]
    [Range(0.1f, 1f)] public float trackWidthSafetyMargin = 0.7f;
    [Tooltip("Gate width (m) assumed when a checkpoint's own collider isn't a BoxCollider (so its authored width can't be read directly) - matches CheckpointCircuit's own default gate width.")]
    public float fallbackGateWidth = 10f;
    [Tooltip("Layers raycast against for obstacle sensing - anything solid that isn't the Terrain layer counts (other cars, the fence, static scenery like trees/rocks, anything dropped into the scene later needs no extra setup beyond a collider). This car's own hull is never hit regardless of layer - see Self Clearance Radius below - so Vehicle does NOT need excluding here; leaving it in is what lets other cars be sensed. Left at 0 (Nothing) in Awake falls back to computing 'everything except Terrain' automatically, so a fresh prefab still senses correctly without hand-configuring this mask.")]
    public LayerMask obstacleLayerMask;
    [Tooltip("Distance (m) within which an obstacle is sensed, in every sampled direction. Wider than you might expect for a 'hard wall' - context steering weighs danger against interest per-direction rather than summing a global steer delta, so a longer sense distance gives smoother advance warning instead of the constant background push it would have caused in the old additive system.")]
    public float obstacleSenseDistance = 10f;
    [Tooltip("How strongly a sensed obstacle contributes danger at maximum urgency (right up against it).")]
    [Range(0f, 1f)] public float obstacleAvoidanceStrength = 0.8f;

    [Header("Tie-Breaking")]
    [Tooltip("Degrees nudged toward this car's own persistent left/right bias, added after resolution. Fixes a real degenerate case: a threat directly ahead while also aiming directly ahead spreads perfectly symmetric interest and danger, so the score-weighted average of angles sums to exactly zero - no steering signal to go around it at all. A small consistent nudge breaks that tie without meaningfully distorting genuinely asymmetric decisions.")]
    public float tieBreakDegrees = 4f;

    static readonly float[] DirectionAngles = { -90f, -67.5f, -45f, -22.5f, 0f, 22.5f, 45f, 67.5f, 90f };

    IVehicleDriveTrain driveTrain;
    LapTracker lapTracker;
    float laneOffset;
    float tieBreakBias;

    // This car's own bounding radius (half-diagonal of its Collider's world AABB), computed once in
    // Awake. Obstacle-sensing rays start this far out from the car's center along their own direction,
    // not at the center itself - the only way to let Obstacle Layer Mask include Vehicle (so other cars
    // are sensed) without ever hitting this car's own hull, since a shared layer can't distinguish self
    // from another car on the same layer.
    float selfClearanceRadius;

    readonly float[] interest = new float[DirectionAngles.Length];
    readonly float[] danger = new float[DirectionAngles.Length];

    // The strongest car-threat and non-car-obstacle-threat this frame, tracked separately as a side
    // effect of AddObstacleDanger's single sensing pass (not a separate reimplementation for drawing) -
    // OnDrawGizmos just reads these back, so "is this car reacting, and to what" can never drift from
    // what actually influenced this frame's steering. Kept apart rather than one combined "biggest
    // threat wins" value, since a car and an obstacle often matter at the same time - combining them
    // would hide whichever one happened to be weaker that frame.
    Vector3 debugCarThreatPoint;
    float debugCarThreatWeight;
    Vector3 debugObstacleThreatPoint;
    float debugObstacleThreatWeight;

    bool loggedObstacleMaskFallback;

    void Awake()
    {
        driveTrain = GetComponent<IVehicleDriveTrain>();
        lapTracker = GetComponent<LapTracker>();
        laneOffset = Random.Range(-laneOffsetRange, laneOffsetRange);
        tieBreakBias = Random.value < 0.5f ? -1f : 1f;

        Collider ownCollider = GetComponent<Collider>();
        selfClearanceRadius = ownCollider != null ? ownCollider.bounds.extents.magnitude : 1f;

        // A LayerMask's C# default is "Nothing", not "everything except Terrain" - without this
        // fallback, a prefab that never had this field explicitly configured would silently sense no
        // obstacles at all instead of failing loudly.
        if (obstacleLayerMask.value == 0)
        {
            int terrainLayer = LayerMask.NameToLayer("Terrain");
            obstacleLayerMask = ~(terrainLayer >= 0 ? 1 << terrainLayer : 0);
            if (!loggedObstacleMaskFallback)
            {
                loggedObstacleMaskFallback = true;
                Debug.LogWarning($"{name}: AIVehicleInput.obstacleLayerMask was left unset - falling back to 'everything except Terrain'. Set it explicitly on the prefab to silence this.", this);
            }
        }
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

        float speedMps = driveTrain.SpeedKmh / 3.6f;
        float lookaheadDistance = Mathf.Clamp(speedMps * lookaheadTime, minLookaheadDistance, maxLookaheadDistance);
        Vector3 lookaheadPoint = lapTracker.GetLookaheadPoint(lookaheadDistance);
        // Lane offset still keyed off the immediate next checkpoint's facing, not the (possibly
        // further-away) lookahead point - close enough since lookahead distance rarely spans more than
        // a segment or two, and it keeps the offset stable rather than swinging with a distant point's
        // orientation on a curving track. Clamped to that checkpoint's own safe half-width so a car's
        // personal lane bias can never aim it past the fence, even on a narrower part of the track.
        float clampedLaneOffset = Mathf.Clamp(laneOffset, -GetSafeHalfWidth(next), GetSafeHalfWidth(next));
        Vector3 aimPoint = lookaheadPoint + next.transform.right * clampedLaneOffset;
        Vector3 toAim = aimPoint - transform.position;
        toAim.y = 0f;
        Vector3 flatForward = transform.forward;
        flatForward.y = 0f;

        float desiredAngle = Vector3.SignedAngle(flatForward, toAim, Vector3.up);

        BuildInterest(desiredAngle);
        System.Array.Clear(danger, 0, danger.Length);
        debugCarThreatWeight = 0f;
        debugObstacleThreatWeight = 0f;
        AddObstacleDanger(out float avoidanceThrottleMultiplier);

        float resolvedAngle = ResolveSteer();
        float steer = Mathf.Clamp(resolvedAngle / maxSteerAngle, -1f, 1f);

        float absAngle = Mathf.Abs(desiredAngle);
        float baseThrottle = driveTrain.IsGrounded
            ? Mathf.Lerp(1f, minCorneringThrottle, Mathf.Clamp01(absAngle / slowdownAngle))
            : 0f; // Airborne: off the pedal rather than flooring it into a nose-dive - landing correction comes later.
        float throttle = baseThrottle * avoidanceThrottleMultiplier;

        driveTrain.SteerInput = steer;
        driveTrain.ThrottleInput = throttle;
        driveTrain.BoostInput = false;
        driveTrain.HandbrakeInput = false;
    }

    /// <summary>
    /// Fills interest[] with how much this car wants to head in each sampled direction - a cosine
    /// falloff centered on desiredAngle (the raw Pure Pursuit heading): full strength at the exact
    /// heading, fading to zero by 90 degrees away on either side.
    /// </summary>
    void BuildInterest(float desiredAngle)
    {
        for (int i = 0; i < DirectionAngles.Length; i++)
        {
            float delta = Mathf.DeltaAngle(desiredAngle, DirectionAngles[i]);
            interest[i] = Mathf.Clamp01(Mathf.Cos(delta * Mathf.Deg2Rad));
        }
    }

    /// <summary>
    /// Writes weight into danger[] for every sampled direction near bearingAngle, falling off linearly
    /// to zero by Danger Spread Degrees away. Uses max rather than += - if two different threats both
    /// mark the same direction dangerous, the true severity is whichever is worse, not their sum, so
    /// several simultaneous mild threats can't stack into an artificially high danger none of them
    /// individually justified.
    /// </summary>
    void SpreadDanger(float bearingAngle, float weight)
    {
        for (int i = 0; i < DirectionAngles.Length; i++)
        {
            float delta = Mathf.DeltaAngle(bearingAngle, DirectionAngles[i]);
            float falloff = Mathf.Clamp01(1f - Mathf.Abs(delta) / dangerSpreadDegrees);
            danger[i] = Mathf.Max(danger[i], weight * falloff);
        }
    }

    /// <summary>
    /// Half of how far sideways from a checkpoint's centerline is considered safe, derived from that
    /// checkpoint's own BoxCollider width (the same gate width CheckpointCircuit built it with) scaled
    /// by Track Width Safety Margin - reuses already-authored track-width data instead of a separate
    /// value that could drift out of sync with the actual gate.
    /// </summary>
    float GetSafeHalfWidth(Checkpoint checkpoint)
    {
        float gateWidth = fallbackGateWidth;
        if (checkpoint != null)
        {
            BoxCollider box = checkpoint.GetComponent<BoxCollider>();
            if (box != null) gateWidth = box.size.x;
        }
        return gateWidth * 0.5f * trackWidthSafetyMargin;
    }

    /// <summary>
    /// Senses everything this car needs to react to - other cars, the track boundary fence, static
    /// scenery, anything else that isn't Terrain or this car's own hull - via a single raycast fan
    /// across all 9 sampled DirectionAngles, spreading danger into the shared map at whichever angles
    /// hit. Casting every sampled angle directly (rather than only +/-90 and relying on SpreadDanger's
    /// falloff to bleed into the rest) catches a diagonal corner or a car/prop sitting off-center that a
    /// pure left/right check could miss.
    ///
    /// A hit on another car gets the same speed-relative leniency the old car-only logic had: alongside
    /// always counts as dangerous, but ahead only counts if that car isn't already moving at a
    /// comparable or greater pace (so following a car racing normally doesn't trigger panic-swerving).
    /// Everything else - fence, scenery, a stopped/wrecked car - is always dangerous when sensed, no
    /// leniency, which also means an abandoned wreck now gets dodged regardless of whether it's still in
    /// RaceManager's active-car registry. Also returns a throttle multiplier that cuts power the closer
    /// a genuine danger sits dead ahead, whatever it is.
    ///
    /// Self-hits are avoided by construction, not by excluding this car's own layer from the mask - each
    /// ray's origin starts Self Clearance Radius out from the car's center along its own direction
    /// (computed once in Awake from this car's own Collider bounds), so a ray only ever starts at the
    /// edge of this car's own hull. That's what lets Obstacle Layer Mask include Vehicle at all (so
    /// other cars are sensed) while still never hitting this car itself, and it also keeps the
    /// wedged-against-something case correct: an obstacle already touching the car's hull reads as
    /// distance zero from that offset origin - maximum danger - rather than from the car's center, which
    /// would either falsely include half the car's own length or (if excluded) miss a genuinely touching
    /// obstacle entirely. Explicitly ignores triggers regardless of layer - checkpoint gate colliders are
    /// triggers (not solid geometry) and must never register as an obstacle no matter what layer they
    /// end up on.
    /// </summary>
    void AddObstacleDanger(out float throttleMultiplier)
    {
        float closestAheadDanger = 0f;
        float aheadAngleThreshold = Mathf.Acos(Mathf.Clamp(aheadDotThreshold, -1f, 1f)) * Mathf.Rad2Deg;
        float rayDistance = obstacleSenseDistance - selfClearanceRadius;

        Vector3 flatForward = transform.forward; flatForward.y = 0f; flatForward.Normalize();
        Vector3 up = Vector3.up * 0.5f;

        for (int i = 0; i < DirectionAngles.Length; i++)
        {
            if (rayDistance <= 0f) continue; // car's own hull is bigger than the sense range - nothing left to check

            Vector3 direction = Quaternion.AngleAxis(DirectionAngles[i], Vector3.up) * flatForward;
            Vector3 origin = transform.position + up + direction * selfClearanceRadius;
            if (!Physics.Raycast(origin, direction, out RaycastHit hit, rayDistance, obstacleLayerMask, QueryTriggerInteraction.Ignore)) continue;

            IVehicleDriveTrain otherDriveTrain = hit.collider.GetComponentInParent<IVehicleDriveTrain>();
            bool isCar = otherDriveTrain != null && !ReferenceEquals(otherDriveTrain, driveTrain);
            bool isAhead = Mathf.Abs(DirectionAngles[i]) < aheadAngleThreshold;

            if (isCar)
            {
                bool isDanger = !isAhead || otherDriveTrain.SpeedKmh < driveTrain.SpeedKmh * racingSpeedRatio;
                if (!isDanger) continue;
            }

            float proximityWeight = Mathf.Clamp01(1f - hit.distance / rayDistance);
            float strength = isCar ? avoidanceSteerStrength : obstacleAvoidanceStrength;
            float weight = proximityWeight * strength;
            SpreadDanger(DirectionAngles[i], weight);

            if (isCar)
            {
                if (weight > debugCarThreatWeight)
                {
                    debugCarThreatWeight = weight;
                    debugCarThreatPoint = hit.point;
                }
            }
            else if (weight > debugObstacleThreatWeight)
            {
                debugObstacleThreatWeight = weight;
                debugObstacleThreatPoint = hit.point;
            }

            if (isAhead) closestAheadDanger = Mathf.Max(closestAheadDanger, proximityWeight);
        }

        throttleMultiplier = 1f - closestAheadDanger * avoidanceThrottleCut;
    }

    /// <summary>
    /// Picks the best heading given everything currently known: for each sampled direction, score = how
    /// much this car wants to go that way minus how dangerous it is, floored at zero, then returns the
    /// score-weighted average of the direction angles. This is what replaces summing independent
    /// steering deltas - two threats marking the same direction dangerous just makes that direction
    /// score badly, so the average shifts toward whatever's left, rather than opposing corrections
    /// cancelling each other out.
    ///
    /// A small per-car Tie Break Degrees nudge is added at the end - a threat directly ahead while this
    /// car is also aiming directly ahead spreads perfectly symmetric interest and danger left-right, so
    /// the score-weighted average sums to exactly zero with nothing to break the tie. The nudge is
    /// small enough not to meaningfully change any genuinely asymmetric decision, but enough to start a
    /// real corrective turn instead of driving straight through whatever's directly ahead.
    /// </summary>
    float ResolveSteer()
    {
        float totalScore = 0f;
        float weightedAngle = 0f;

        for (int i = 0; i < DirectionAngles.Length; i++)
        {
            float score = Mathf.Max(0f, interest[i] - danger[i]);
            totalScore += score;
            weightedAngle += score * DirectionAngles[i];
        }

        if (totalScore < 0.001f) return 0f; // every direction looks equally bad/uninteresting - go straight
        return weightedAngle / totalScore + tieBreakBias * tieBreakDegrees;
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

    /// <summary>
    /// Visualizes this car's live Pure Pursuit lookahead every frame (not just when selected), plus a
    /// clean signal for whether it's currently making a reactive turn and why:
    /// - Cyan line/sphere: raw lookahead point on the checkpoint chain.
    /// - Magenta line/sphere: final aim point after this car's (clamped) lane offset.
    /// - Orange line/sphere + "reacting: car" label: only appears when a car threat was strong enough
    ///   this frame to matter (above the 0.05 cutoff), pointing at the strongest one.
    /// - Red line/sphere + "reacting: obstacle" label: same, for any sensed obstacle. Tracked completely
    ///   separately from the car threat above rather than one combined "biggest wins" signal, so both
    ///   can show up at once - a car and an obstacle often matter in the same frame, and hiding
    ///   whichever happened to be weaker that moment would hide exactly the case worth watching for.
    /// No lines at all means the car is just following its path, not reacting to anything.
    /// Hide it anytime via the Gizmos dropdown's per-script toggle if a full field of cars gets too busy
    /// to read.
    /// </summary>
    void OnDrawGizmos()
    {
        IVehicleDriveTrain dt = driveTrain != null ? driveTrain : GetComponent<IVehicleDriveTrain>();
        LapTracker tracker = lapTracker != null ? lapTracker : GetComponent<LapTracker>();
        if (dt == null || tracker == null) return;

        Checkpoint current = tracker.CurrentCheckpoint;
        Checkpoint next = current != null && current.nextCheckpoints != null && current.nextCheckpoints.Length > 0
            ? current.nextCheckpoints[0] : null;
        if (next == null) return;

        float speedMps = dt.SpeedKmh / 3.6f;
        float lookaheadDistance = Mathf.Clamp(speedMps * lookaheadTime, minLookaheadDistance, maxLookaheadDistance);
        Vector3 lookaheadPoint = tracker.GetLookaheadPoint(lookaheadDistance);

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(transform.position, lookaheadPoint);
        Gizmos.DrawSphere(lookaheadPoint, 0.35f);

        float clampedOffset = Mathf.Clamp(laneOffset, -GetSafeHalfWidth(next), GetSafeHalfWidth(next));
        Vector3 aimPoint = lookaheadPoint + next.transform.right * clampedOffset;

        Gizmos.color = Color.magenta;
        Gizmos.DrawLine(lookaheadPoint, aimPoint);
        Gizmos.DrawSphere(aimPoint, 0.35f);

#if UNITY_EDITOR
        Handles.color = Color.cyan;
        Handles.Label(lookaheadPoint + Vector3.up * 0.6f, $"{lookaheadDistance:0.0}m");
#endif

        const float reactingThreshold = 0.05f;

        if (debugCarThreatWeight > reactingThreshold)
        {
            Gizmos.color = new Color(1f, 0.5f, 0f);
            Gizmos.DrawLine(transform.position, debugCarThreatPoint);
            Gizmos.DrawWireSphere(debugCarThreatPoint, 1f);
#if UNITY_EDITOR
            Handles.color = new Color(1f, 0.5f, 0f);
            Handles.Label(transform.position + Vector3.up * 2f, "reacting: car");
#endif
        }

        if (debugObstacleThreatWeight > reactingThreshold)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, debugObstacleThreatPoint);
            Gizmos.DrawWireSphere(debugObstacleThreatPoint, 1f);
#if UNITY_EDITOR
            Handles.color = Color.red;
            Handles.Label(transform.position + Vector3.up * 2.4f, "reacting: obstacle");
#endif
        }
    }
}
