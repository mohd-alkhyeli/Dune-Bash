using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns a roster of AI cars and respawns each one individually whenever its own VehicleFlipDetector
/// reports it's stuck - same respawn-keeps-progress, abandon-the-wreck approach as CarSpawner, just for
/// N cars instead of one. Each AI car keeps its assigned color across respawns (a car doesn't change
/// color just because it crashed). Positions are claimed from the same StartGrid the player uses, so
/// neither can double-book a slot regardless of which spawner runs first.
/// </summary>
public class AISpawner : MonoBehaviour
{
    [Header("AI Roster")]
    [Tooltip("Prefab instantiated for each AI car (e.g. Jeep_AI).")]
    public GameObject aiCarPrefab;
    [Tooltip("How many AI cars to spawn at race start.")]
    public int aiCount = 3;

    [Header("Colors")]
    [Tooltip("Pool of materials randomly assigned to spawned AI cars, one each, never repeated within a race as long as there are at least Ai Count of them. Add more here any time - no code change needed.")]
    public Material[] materials;
    [Tooltip("Name of the child object on the AI car prefab whose Renderer gets recolored (the Jeep model's body mesh).")]
    public string bodyChildName = "Body";
    [Tooltip("How far around the color wheel a generated fallback color can shift from its source material, as a fraction of the full wheel (0 = no change, 1 = anywhere). Only used once every Materials entry is already claimed and there are more AI cars than colors - a fresh hue-shifted variant beats handing two cars the exact same color. This car's color comes from a baked texture (not a shader tint), so the shift is applied per-pixel to a generated copy of that texture - the source texture asset must have Read/Write Enabled in its Import Settings, or the fallback silently keeps the original color instead.")]
    [Range(0f, 1f)] public float hueShiftRange = 1f;

    [Header("Race")]
    [Tooltip("Shared race-start grid - each AI car claims a random unique point from this, same as the player's CarSpawner does.")]
    public StartGrid startGrid;
    [Tooltip("The race's start/finish checkpoint, used to initialize each AI car's LapTracker on first spawn.")]
    public Checkpoint startCheckpoint;

    [Header("Terrain")]
    [Tooltip("Used to ground-snap spawn height so AI cars don't spawn floating or buried. Leave unassigned to spawn at each spawn point's authored height as-is.")]
    public Collider terrainCollider;
    [Tooltip("How far above/below a spawn point to search for the terrain surface.")]
    public float terrainSearchHeight = 200f;
    [Tooltip("Extra height (m) added above the raw terrain hit when placing a spawned car - same purpose as CarSpawner's own value, and likely the same number unless this AI prefab has a differently-placed pivot.")]
    public float spawnHeightOffset = 1.2f;

    class AICarState
    {
        public GameObject car;
        public VehicleFlipDetector flipDetector;
        public LapTracker lapTracker;
        public Material assignedMaterial;
    }

    readonly List<AICarState> activeCars = new List<AICarState>();
    readonly List<Material> claimedMaterials = new List<Material>();

    void Start()
    {
        for (int i = 0; i < aiCount; i++)
        {
            SpawnAICar(i, null, ClaimRandomMaterial());
        }
    }

    void Update()
    {
        for (int i = 0; i < activeCars.Count; i++)
        {
            AICarState state = activeCars[i];
            if (state.flipDetector != null && state.flipDetector.IsFlipped) RespawnAICar(i);
        }
    }

    void RespawnAICar(int slotIndex)
    {
        AICarState state = activeCars[slotIndex];
        LapTracker previousTracker = state.lapTracker;

        AbandonCar(state);
        SpawnAICar(slotIndex, previousTracker, state.assignedMaterial);
    }

    void SpawnAICar(int slotIndex, LapTracker previousTracker, Material material)
    {
        Vector3 spawnPos;
        Quaternion spawnRot;
        Checkpoint fromCheckpoint = previousTracker != null ? previousTracker.CurrentCheckpoint : null;
        if (fromCheckpoint != null) fromCheckpoint.GetSpawnPoint(out spawnPos, out spawnRot);
        else GetStartGridSpawn(out spawnPos, out spawnRot);

        GameObject car = Instantiate(aiCarPrefab, ProjectToTerrain(spawnPos), spawnRot);
        ApplyMaterial(car, material);

        LapTracker lapTracker = car.GetComponent<LapTracker>();
        if (lapTracker != null)
        {
            if (previousTracker != null) lapTracker.CopyProgressFrom(previousTracker);
            else lapTracker.Initialize(startCheckpoint);
        }

        AICarState state = new AICarState
        {
            car = car,
            flipDetector = car.GetComponent<VehicleFlipDetector>(),
            lapTracker = lapTracker,
            assignedMaterial = material
        };

        if (slotIndex < activeCars.Count) activeCars[slotIndex] = state;
        else activeCars.Add(state);
    }

    void AbandonCar(AICarState state)
    {
        if (state.car == null) return;

        AIVehicleInput input = state.car.GetComponent<AIVehicleInput>();
        if (input != null) input.ReleaseControl();

        if (state.lapTracker != null) state.lapTracker.enabled = false;
        if (state.flipDetector != null) state.flipDetector.enabled = false;
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

    Material ClaimRandomMaterial()
    {
        if (materials == null || materials.Length == 0) return null;

        List<Material> available = new List<Material>();
        foreach (Material m in materials)
            if (m != null && !claimedMaterials.Contains(m)) available.Add(m);

        if (available.Count > 0)
        {
            Material chosen = available[Random.Range(0, available.Count)];
            claimedMaterials.Add(chosen);
            return chosen;
        }

        // Ran out of distinct pool entries - generate a hue-shifted variant of a random one instead of
        // handing out an exact duplicate.
        Material baseMaterial = materials[Random.Range(0, materials.Length)];
        if (baseMaterial == null) return null;

        Debug.LogWarning("AISpawner ran out of unique materials - generating a hue-shifted variant. Add more materials to the pool for fully authored colors.", this);
        return CreateHueShiftedVariant(baseMaterial);
    }

    /// <summary>
    /// Clones source and randomly shifts its hue, so an overflow AI car still reads as "the same paint,
    /// different color" rather than an unrelated material. Shifts both the tint color (in case a future
    /// material actually uses one) and, since these Jeep materials get their real color from a baked
    /// texture atlas rather than a tint, a per-pixel hue-shifted copy of that texture too - covering
    /// whichever mechanism the source material actually relies on.
    /// </summary>
    Material CreateHueShiftedVariant(Material source)
    {
        Material variant = new Material(source);
        float hueOffset = Random.Range(-hueShiftRange, hueShiftRange) * 0.5f;

        Color.RGBToHSV(source.color, out float h, out float s, out float v);
        h = Mathf.Repeat(h + hueOffset, 1f);
        Color shiftedColor = Color.HSVToRGB(h, s, v);
        shiftedColor.a = source.color.a;
        variant.color = shiftedColor;

        Texture2D sourceTexture = source.mainTexture as Texture2D;
        if (sourceTexture != null && sourceTexture.isReadable)
        {
            Texture2D shiftedTexture = CreateHueShiftedTexture(sourceTexture, hueOffset);
            if (variant.HasProperty("_BaseMap")) variant.SetTexture("_BaseMap", shiftedTexture);
            if (variant.HasProperty("_MainTex")) variant.SetTexture("_MainTex", shiftedTexture);
        }
        else if (sourceTexture != null)
        {
            Debug.LogWarning($"AISpawner can't hue-shift '{sourceTexture.name}' - enable Read/Write in its Import Settings. Falling back to the tint-only shift, which won't be visible on a white-tinted material.", this);
        }

        return variant;
    }

    /// <summary>Copies source pixel-for-pixel into a new texture with every pixel's hue rotated by hueOffset (saturation/value/alpha preserved), so whatever colors are actually baked into the art shift together rather than being multiplied by an unrelated tint.</summary>
    static Texture2D CreateHueShiftedTexture(Texture2D source, float hueOffset)
    {
        Color[] pixels = source.GetPixels();
        for (int i = 0; i < pixels.Length; i++)
        {
            Color.RGBToHSV(pixels[i], out float h, out float s, out float v);
            h = Mathf.Repeat(h + hueOffset, 1f);
            Color shifted = Color.HSVToRGB(h, s, v);
            shifted.a = pixels[i].a;
            pixels[i] = shifted;
        }

        Texture2D result = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        result.SetPixels(pixels);
        result.Apply();
        return result;
    }

    void ApplyMaterial(GameObject car, Material material)
    {
        if (material == null) return;

        Transform body = car.transform.Find(bodyChildName);
        Renderer renderer = body != null ? body.GetComponent<Renderer>() : null;
        if (renderer != null) renderer.sharedMaterial = material;
        else Debug.LogWarning($"AISpawner couldn't find a Renderer on '{bodyChildName}' to recolor.", this);
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
