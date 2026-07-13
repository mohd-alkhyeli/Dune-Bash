using UnityEngine;

/// <summary>
/// Spawns the player's car and respawns it whenever VehicleFlipDetector reports it's stuck. A respawn
/// keeps the player's lap progress (no penalty for crashing) and places the new car off to the side of
/// their last checkpoint, ground-snapped. The old car is left in place as an inert wreck - control,
/// lap tracking, and flip detection on it are disabled, but it stays physically present - a
/// placeholder until the totaled system replaces this.
/// </summary>
public class CarSpawner : MonoBehaviour
{
    [Tooltip("Prefab instantiated for the player's car, both on first spawn and on every respawn.")]
    public GameObject carPrefab;
    [Tooltip("The scene's player camera - retargeted to whichever car is currently active.")]
    public PlayerCamera playerCamera;
    [Tooltip("Optional - the HUD speed readout, retargeted to whichever car is currently active. Leave unassigned if you don't have one.")]
    public SpeedometerUI speedometerUI;
    [Tooltip("Optional - the HUD boost bar, retargeted to whichever car is currently active. Leave unassigned if you don't have one.")]
    public BoostMeterUI boostMeterUI;
    [Tooltip("Optional - the HUD lap/placement readout, retargeted to whichever car is currently active. Leave unassigned if you don't have one.")]
    public LapCounterUI lapCounterUI;
    [Tooltip("The race's start/finish checkpoint, used to initialize a freshly spawned car's LapTracker. Not needed if Car Prefab has no LapTracker.")]
    public Checkpoint startCheckpoint;
    [Tooltip("Used to ground-snap spawn point height so cars don't spawn floating or buried. Leave unassigned to spawn at each spawn point's authored height as-is.")]
    public Collider terrainCollider;
    [Tooltip("How far above/below a spawn point to search for the terrain surface.")]
    public float terrainSearchHeight = 200f;
    [Tooltip("Extra height (m) added above the raw terrain hit when placing a spawned car. Needed because a car's pivot isn't at its wheel-ground contact point, and a little extra buffer lets suspension settle it naturally rather than needing pixel-perfect placement. Raise this if the car still spawns partially buried; lower it if it spawns floating/falls too far.")]
    public float spawnHeightOffset = 1.2f;
    [Tooltip("The race-start grid. The player's first spawn (and any respawn with no last-checkpoint to go by) claims a random unique point from this instead of using a single fixed spot.")]
    public StartGrid startGrid;

    GameObject currentCar;
    VehicleFlipDetector currentFlipDetector;
    LapTracker currentLapTracker;

    void Start()
    {
        GetStartGridSpawn(out Vector3 pos, out Quaternion rot);
        SpawnCar(ProjectToTerrain(pos), rot, null);
    }

    void Update()
    {
        if (currentFlipDetector != null && currentFlipDetector.IsFlipped) Respawn();
    }

    void Respawn()
    {
        LapTracker previousTracker = currentLapTracker;
        Checkpoint fromCheckpoint = previousTracker != null ? previousTracker.CurrentCheckpoint : null;

        AbandonCurrentCar();

        Vector3 spawnPos;
        Quaternion spawnRot;
        if (fromCheckpoint != null)
        {
            fromCheckpoint.GetSpawnPoint(out spawnPos, out spawnRot);
        }
        else
        {
            GetStartGridSpawn(out spawnPos, out spawnRot);
        }

        SpawnCar(ProjectToTerrain(spawnPos), spawnRot, previousTracker);
    }

    void GetStartGridSpawn(out Vector3 position, out Quaternion rotation)
    {
        Transform point = startGrid != null ? startGrid.ClaimRandomPoint() : null;
        if (point != null)
        {
            position = point.position;
            rotation = point.rotation;
        }
        else
        {
            position = transform.position;
            rotation = Quaternion.identity;
        }
    }

    void SpawnCar(Vector3 position, Quaternion rotation, LapTracker previousTracker)
    {
        currentCar = Instantiate(carPrefab, position, rotation);

        VehicleDriveTrain driveTrain = currentCar.GetComponent<VehicleDriveTrain>();

        currentFlipDetector = currentCar.GetComponent<VehicleFlipDetector>();
        currentLapTracker = currentCar.GetComponent<LapTracker>();
        if (currentLapTracker != null)
        {
            if (previousTracker != null) currentLapTracker.CopyProgressFrom(previousTracker);
            else currentLapTracker.Initialize(startCheckpoint);
        }

        if (playerCamera != null)
        {
            playerCamera.target = currentCar.transform;
            playerCamera.targetBody = currentCar.GetComponent<Rigidbody>();
            playerCamera.targetDriveTrain = driveTrain;
        }

        // HUD scripts hold a direct reference to a specific car's components, same as the camera does -
        // without this they'd keep pointing at whatever car they were last wired to in the Inspector,
        // which is a destroyed/abandoned car after the very first respawn.
        if (speedometerUI != null) speedometerUI.driveTrain = driveTrain;
        if (boostMeterUI != null) boostMeterUI.driveTrain = driveTrain;
        if (lapCounterUI != null) lapCounterUI.lapTracker = currentLapTracker;
    }

    void AbandonCurrentCar()
    {
        if (currentCar == null) return;

        PlayerVehicleInput input = currentCar.GetComponent<PlayerVehicleInput>();
        if (input != null) input.ReleaseControl();

        if (currentLapTracker != null) currentLapTracker.enabled = false;
        if (currentFlipDetector != null) currentFlipDetector.enabled = false;
    }

    Vector3 ProjectToTerrain(Vector3 point)
    {
        if (terrainCollider == null) return point;

        Vector3 origin = new Vector3(point.x, point.y + terrainSearchHeight, point.z);
        if (terrainCollider.Raycast(new Ray(origin, Vector3.down), out RaycastHit hit, terrainSearchHeight * 2f))
            return new Vector3(point.x, hit.point.y + spawnHeightOffset, point.z);
        return point;
    }
}
