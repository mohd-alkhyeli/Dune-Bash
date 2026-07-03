using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class BuildCarSetup
{
    const float WheelRadius = 0.42f;
    const float WheelWidth = 0.32f;
    const float TrackHalf = 0.83f;
    const float WheelbaseHalf = 1.35f;
    const float RootHeight = 0.9f; // root pivot height above resting wheel-ground contact
    const string ScenePath = "Assets/Scenes/SampleScene.unity";

    [MenuItem("Dune Bash/Build Car Rig")]
    public static void Build()
    {
        // In batch mode Unity starts on a blank untitled scene rather than the last-open one, so without
        // this, building would silently populate a throwaway scene that can't be saved (no file path) and
        // SampleScene.unity would never be touched.
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath) EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // --- clean any previous run, but remember where the car was left ---
        Vector3? previousCarPos = null;
        Quaternion? previousCarRot = null;
        var oldCar = GameObject.Find("Car");
        if (oldCar != null)
        {
            previousCarPos = oldCar.transform.position;
            previousCarRot = oldCar.transform.rotation;
            Object.DestroyImmediate(oldCar);
        }
        var oldGround = GameObject.Find("TestGround");
        if (oldGround != null) Object.DestroyImmediate(oldGround);

        Directory.CreateDirectory("Assets/Mats/Car");
        Directory.CreateDirectory("Assets/Physics");
        Directory.CreateDirectory("Assets/Prefabs");

        PhysicsMaterial sand = GetOrCreateSandMaterial();
        GameObject ground = BuildTestGround(sand);
        GameObject car = BuildCar();

        if (previousCarPos.HasValue)
        {
            car.transform.position = previousCarPos.Value;
            car.transform.rotation = previousCarRot.Value;
        }

        int fixedColliders = FixTerrainColliders(sand);
        DropCarOntoGround(car);

        SetUpCamera(car);

        scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();

        bool prefabSuccess;
        PrefabUtility.SaveAsPrefabAssetAndConnect(car, "Assets/Prefabs/Car.prefab", InteractionMode.AutomatedAction, out prefabSuccess);

        bool saved = EditorSceneManager.SaveScene(scene, scene.path);
        if (!saved) Debug.LogError("BuildCarSetup: EditorSceneManager.SaveScene failed for " + scene.path);

        CaptureVerificationShots(car);

        Debug.Log("BuildCarSetup: done. Car + camera + placeholder ground created. Prefab saved: " + prefabSuccess + ". Terrain colliders fixed: " + fixedColliders);
    }

    /// <summary>
    /// Finds any (broken) TerrainCollider components - which only work on Unity Terrain objects and do
    /// nothing on an imported mesh - and replaces them with a proper MeshCollider using the object's
    /// actual mesh, so imported dune terrain meshes are solid instead of the car falling through.
    /// </summary>
    [MenuItem("Dune Bash/Fix Terrain Colliders")]
    public static void FixTerrainCollidersMenuItem()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath) EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        scene = EditorSceneManager.GetActiveScene();

        PhysicsMaterial sand = GetOrCreateSandMaterial();
        int count = FixTerrainColliders(sand);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, scene.path);
        Debug.Log("BuildCarSetup: fixed " + count + " terrain collider(s).");
    }

    static int FixTerrainColliders(PhysicsMaterial sand)
    {
        int fixedCount = 0;
        TerrainCollider[] badColliders = Object.FindObjectsByType<TerrainCollider>(FindObjectsSortMode.None);
        foreach (TerrainCollider tc in badColliders)
        {
            GameObject go = tc.gameObject;
            Object.DestroyImmediate(tc);

            MeshFilter mf = go.GetComponent<MeshFilter>();
            if (mf == null) mf = go.GetComponentInChildren<MeshFilter>();
            if (mf == null || mf.sharedMesh == null)
            {
                Debug.LogWarning("BuildCarSetup: removed an empty TerrainCollider on '" + go.name + "' but found no mesh nearby to collide with.");
                continue;
            }

            GameObject meshGo = mf.gameObject;
            MeshCollider mc = meshGo.GetComponent<MeshCollider>();
            if (mc == null) mc = meshGo.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            mc.convex = false;
            mc.sharedMaterial = sand;
            fixedCount++;
            Debug.Log("BuildCarSetup: replaced TerrainCollider with MeshCollider on '" + meshGo.name + "' (" + mf.sharedMesh.vertexCount + " verts).");
        }
        return fixedCount;
    }

    /// <summary>Raycasts down from well above the car and drops it onto whatever ground collider it finds, so a stale manual Y position can't leave it floating or clipped.</summary>
    static void DropCarOntoGround(GameObject car)
    {
        Vector3 origin = new Vector3(car.transform.position.x, 1000f, car.transform.position.z);
        RaycastHit hit;
        if (Physics.Raycast(origin, Vector3.down, out hit, 5000f))
        {
            Vector3 pos = car.transform.position;
            pos.y = hit.point.y + 1.2f;
            car.transform.position = pos;
        }
        else
        {
            Debug.LogWarning("BuildCarSetup: no ground collider found under the car's spawn X/Z - leaving its Y position as-is.");
        }
    }

    static PhysicsMaterial GetOrCreateSandMaterial()
    {
        const string path = "Assets/Physics/Sand.physicMaterial";
        PhysicsMaterial mat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
        if (mat == null)
        {
            mat = new PhysicsMaterial("Sand");
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.dynamicFriction = 0.75f;
        mat.staticFriction = 0.8f;
        mat.bounciness = 0f;
        mat.frictionCombine = PhysicsMaterialCombine.Average;
        mat.bounceCombine = PhysicsMaterialCombine.Minimum;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Material CreateLit(string name, Color color, float smoothness = 0.25f, Color? emission = null)
    {
        string path = "Assets/Mats/Car/" + name + ".mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.color = color;
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        if (emission.HasValue)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission.Value);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    static GameObject BuildTestGround(PhysicsMaterial sand)
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "TestGround";
        ground.transform.position = Vector3.zero;
        ground.transform.localScale = new Vector3(40f, 1f, 40f); // 400x400 units
        MeshCollider col = ground.GetComponent<MeshCollider>();
        col.sharedMaterial = sand;
        Material groundMat = CreateLit("SandGround", new Color(0.82f, 0.72f, 0.52f), 0.05f);
        ground.GetComponent<MeshRenderer>().sharedMaterial = groundMat;
        return ground;
    }

    static GameObject BuildCar()
    {
        Material paint = CreateLit("BodyPaint", new Color(0.93f, 0.90f, 0.83f), 0.35f);
        Material dark = CreateLit("DarkTrim", new Color(0.14f, 0.14f, 0.16f), 0.3f);
        Material glass = CreateLit("GlassTint", new Color(0.10f, 0.16f, 0.22f), 0.75f);
        Material rubber = CreateLit("TireRubber", new Color(0.05f, 0.05f, 0.05f), 0.15f);
        Material headlight = CreateLit("Headlight", new Color(1f, 0.95f, 0.75f), 0.6f, new Color(1f, 0.9f, 0.5f) * 0.6f);
        Material taillight = CreateLit("Taillight", new Color(0.6f, 0.05f, 0.03f), 0.5f, new Color(0.8f, 0.05f, 0.02f) * 0.5f);

        GameObject root = new GameObject("Car");
        root.transform.position = new Vector3(0f, RootHeight + 1.0f, -8f);
        root.transform.rotation = Quaternion.identity;

        Rigidbody rb = root.AddComponent<Rigidbody>();
        rb.mass = 1450f;
        rb.linearDamping = 0.05f;
        rb.angularDamping = 1.2f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        BoxCollider body = root.AddComponent<BoxCollider>();
        body.center = new Vector3(0f, 0.475f, 0.05f);
        body.size = new Vector3(1.85f, 0.75f, 4.3f);

        // --- visual body ---
        Transform bodyRoot = new GameObject("Body").transform;
        bodyRoot.SetParent(root.transform, false);

        MakeBox(bodyRoot, "BodyMain", paint, new Vector3(1.85f, 0.9f, 4.3f), new Vector3(0f, 0.15f, 0.05f), Vector3.zero);
        MakeBox(bodyRoot, "Hood", paint, new Vector3(1.7f, 0.12f, 1.15f), new Vector3(0f, 0.42f, 1.95f), new Vector3(-10f, 0, 0));
        MakeBox(bodyRoot, "CabinLower", paint, new Vector3(1.75f, 0.55f, 2.1f), new Vector3(0f, 0.72f, -0.4f), Vector3.zero);
        MakeBox(bodyRoot, "CabinUpper", glass, new Vector3(1.5f, 0.5f, 1.65f), new Vector3(0f, 1.15f, -0.45f), Vector3.zero);
        MakeBox(bodyRoot, "Windshield", glass, new Vector3(1.5f, 0.5f, 0.08f), new Vector3(0f, 1.0f, 0.55f), new Vector3(-25f, 0, 0));
        MakeBox(bodyRoot, "RearGlass", glass, new Vector3(1.5f, 0.45f, 0.08f), new Vector3(0f, 1.05f, -1.3f), new Vector3(20f, 0, 0));
        MakeBox(bodyRoot, "RoofRack", dark, new Vector3(1.3f, 0.08f, 1.9f), new Vector3(0f, 1.45f, -0.4f), Vector3.zero);
        MakeBox(bodyRoot, "BullBar", dark, new Vector3(1.6f, 0.5f, 0.2f), new Vector3(0f, 0.05f, 2.25f), Vector3.zero);

        MakeBox(bodyRoot, "HeadlightL", headlight, new Vector3(0.4f, 0.15f, 0.06f), new Vector3(-0.55f, 0.1f, 2.14f), Vector3.zero);
        MakeBox(bodyRoot, "HeadlightR", headlight, new Vector3(0.4f, 0.15f, 0.06f), new Vector3(0.55f, 0.1f, 2.14f), Vector3.zero);
        MakeBox(bodyRoot, "TaillightL", taillight, new Vector3(0.35f, 0.13f, 0.06f), new Vector3(-0.55f, 0.05f, -2.14f), Vector3.zero);
        MakeBox(bodyRoot, "TaillightR", taillight, new Vector3(0.35f, 0.13f, 0.06f), new Vector3(0.55f, 0.05f, -2.14f), Vector3.zero);

        Vector3[] wheelPos =
        {
            new Vector3(-TrackHalf, -0.45f, WheelbaseHalf),
            new Vector3(TrackHalf, -0.45f, WheelbaseHalf),
            new Vector3(-TrackHalf, -0.45f, -WheelbaseHalf),
            new Vector3(TrackHalf, -0.45f, -WheelbaseHalf),
        };
        string[] wheelNames = { "FL", "FR", "RL", "RR" };
        for (int i = 0; i < 4; i++)
        {
            float sign = wheelPos[i].x < 0 ? 1f : -1f;
            MakeBox(bodyRoot, "FenderFlare" + wheelNames[i], dark, new Vector3(0.32f, 0.22f, 0.55f),
                new Vector3(wheelPos[i].x + sign * -0.02f, -0.15f, wheelPos[i].z), new Vector3(0f, 0f, sign * 12f));
        }

        // spare tire on tailgate
        GameObject spare = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        spare.name = "SpareTire";
        Object.DestroyImmediate(spare.GetComponent<Collider>());
        spare.transform.SetParent(bodyRoot, false);
        spare.transform.localPosition = new Vector3(0f, 0.35f, -2.18f);
        spare.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        spare.transform.localScale = new Vector3(WheelRadius * 2f, 0.14f, WheelRadius * 2f);
        spare.GetComponent<MeshRenderer>().sharedMaterial = rubber;

        // --- wheel colliders (physics) ---
        Transform wcRoot = new GameObject("WheelColliders").transform;
        wcRoot.SetParent(root.transform, false);
        WheelCollider[] colliders = new WheelCollider[4];
        for (int i = 0; i < 4; i++)
        {
            GameObject wc = new GameObject("WheelCollider_" + wheelNames[i]);
            wc.transform.SetParent(wcRoot, false);
            wc.transform.localPosition = wheelPos[i];
            WheelCollider collider = wc.AddComponent<WheelCollider>();
            collider.radius = WheelRadius;
            collider.mass = 20f;
            collider.suspensionDistance = 0.34f;
            colliders[i] = collider;
        }

        // --- wheel visuals ---
        // CarController repositions these every frame via WheelCollider.GetWorldPose(), which sets
        // *world* position/rotation directly - that would stomp a local axis-correction rotation applied
        // straight to the mesh. So each wheel is a pivot (driven by the physics pose every frame) with the
        // actual cylinder mesh as a child carrying a fixed local rotation to turn its default Y-axis into
        // the X-axis (the wheel's spin/axle axis). Without this indirection the wheels render lying flat.
        Transform wvRoot = new GameObject("WheelVisuals").transform;
        wvRoot.SetParent(root.transform, false);
        Transform[] visuals = new Transform[4];
        for (int i = 0; i < 4; i++)
        {
            GameObject pivot = new GameObject("WheelVisual_" + wheelNames[i]);
            pivot.transform.SetParent(wvRoot, false);
            pivot.transform.localPosition = wheelPos[i];

            GameObject wheel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            wheel.name = "WheelMesh_" + wheelNames[i];
            Object.DestroyImmediate(wheel.GetComponent<Collider>());
            wheel.transform.SetParent(pivot.transform, false);
            wheel.transform.localPosition = Vector3.zero;
            wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            wheel.transform.localScale = new Vector3(WheelRadius * 2f, WheelWidth * 0.5f, WheelRadius * 2f);
            wheel.GetComponent<MeshRenderer>().sharedMaterial = rubber;
            visuals[i] = pivot.transform;
        }

        CarController controller = root.AddComponent<CarController>();
        controller.wheelColliderFL = colliders[0];
        controller.wheelColliderFR = colliders[1];
        controller.wheelColliderRL = colliders[2];
        controller.wheelColliderRR = colliders[3];
        controller.wheelMeshFL = visuals[0];
        controller.wheelMeshFR = visuals[1];
        controller.wheelMeshRL = visuals[2];
        controller.wheelMeshRR = visuals[3];
        controller.ApplyTuning();

        // --- wheel dust ---
        Material dustMat = GetOrCreateSandDustMaterial();
        Transform dustRoot = new GameObject("WheelDust").transform;
        dustRoot.SetParent(root.transform, false);
        var dustFx = root.AddComponent<WheelDustFX>();
        dustFx.wheels = new WheelDustFX.WheelDust[4];
        for (int i = 0; i < 4; i++)
        {
            ParticleSystem ps = CreateDustParticleSystem(dustRoot, "Dust_" + wheelNames[i], dustMat);
            ps.transform.position = root.transform.TransformPoint(wheelPos[i] + new Vector3(0f, -WheelRadius, 0f));
            dustFx.wheels[i] = new WheelDustFX.WheelDust { wheel = colliders[i], dust = ps };
        }

        return root;
    }

    static ParticleSystem CreateDustParticleSystem(Transform parent, string name, Material dustMat)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localRotation = Quaternion.Euler(-100f, 0f, 0f); // spray up-and-back from the contact patch

        ParticleSystem ps = go.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.0f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.87f, 0.78f, 0.58f, 0.9f), new Color(0.75f, 0.65f, 0.45f, 0.9f));
        main.gravityModifier = 0.4f;
        main.maxParticles = 200;

        var emission = ps.emission;
        emission.rateOverTime = 0f; // driven at runtime by WheelDustFX based on slip

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 25f;
        shape.radius = 0.15f;

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.6f, 0.4f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = grad;

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 2.2f));

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.3f;
        noise.frequency = 0.5f;

        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = dustMat;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortMode = ParticleSystemSortMode.Distance;
        renderer.shadowCastingMode = ShadowCastingMode.Off;

        return ps;
    }

    static Material GetOrCreateSandDustMaterial()
    {
        const string path = "Assets/Mats/Car/SandDust.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            m = new Material(shader);
            AssetDatabase.CreateAsset(m, path);
        }

        Texture2D sprite = GetOrCreateDustSprite();
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", sprite);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", new Color(0.85f, 0.75f, 0.55f, 1f));

        // Configure as a standard alpha-blended transparent surface (URP Lit/Unlit/Particle shaders share these property names).
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_Cull", (float)CullMode.Off);
        m.renderQueue = (int)RenderQueue.Transparent;
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.SetOverrideTag("RenderType", "Transparent");
        EditorUtility.SetDirty(m);
        return m;
    }

    static Texture2D GetOrCreateDustSprite()
    {
        const string path = "Assets/Mats/Car/SandDustSprite.png";
        Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing != null) return existing;

        const int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Vector2 center = new Vector2(size / 2f, size / 2f);
        float maxDist = size / 2f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center) / maxDist;
                float alpha = Mathf.Pow(Mathf.Clamp01(1f - dist), 1.6f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply();

        byte[] png = tex.EncodeToPNG();
        Object.DestroyImmediate(tex);
        File.WriteAllBytes(path, png);
        AssetDatabase.ImportAsset(path);

        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static void MakeBox(Transform parent, string name, Material mat, Vector3 size, Vector3 localPos, Vector3 eulerRot)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(eulerRot);
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    static void SetUpCamera(GameObject car)
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            GameObject camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            cam = camGo.AddComponent<Camera>();
        }

        CarCamera carCam = cam.GetComponent<CarCamera>();
        if (carCam == null) carCam = cam.gameObject.AddComponent<CarCamera>();

        carCam.target = car.transform;
        carCam.targetBody = car.GetComponent<Rigidbody>();
        carCam.targetCar = car.GetComponent<CarController>();

        Vector3 startPos = car.transform.position - car.transform.forward * carCam.baseDistance + Vector3.up * carCam.baseHeight;
        cam.transform.position = startPos;
        cam.transform.LookAt(car.transform.position + Vector3.up * carCam.lookHeight);
        cam.fieldOfView = carCam.baseFov;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 2000f;
    }

    static void CaptureVerificationShots(GameObject car)
    {
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "CarSetupShots");
        Directory.CreateDirectory(dir);

        GameObject camGo = new GameObject("__VerifyCam");
        Camera cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.55f, 0.68f, 0.85f);
        cam.fieldOfView = 40f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 500f;

        Vector3 carPos = car.transform.position;

        RenderShot(cam, carPos + new Vector3(4.5f, 2.2f, -5.5f), carPos + Vector3.up * 0.9f, Path.Combine(dir, "car_3q_front.png"));
        RenderShot(cam, carPos + new Vector3(-4.5f, 2.0f, 5.0f), carPos + Vector3.up * 0.9f, Path.Combine(dir, "car_3q_rear.png"));
        RenderShot(cam, carPos + new Vector3(0f, 1.2f, -3.2f), carPos + Vector3.up * 0.8f, Path.Combine(dir, "car_side_close.png"));

        Object.DestroyImmediate(camGo);
        Debug.Log("BuildCarSetup: verification shots written to " + dir);
    }

    static void RenderShot(Camera cam, Vector3 pos, Vector3 lookAt, string path)
    {
        cam.transform.position = pos;
        cam.transform.LookAt(lookAt);

        RenderTexture rt = new RenderTexture(1024, 768, 24);
        cam.targetTexture = rt;
        Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);

        cam.Render();
        RenderTexture prevActive = RenderTexture.active;
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = prevActive;

        cam.targetTexture = null;
        Object.DestroyImmediate(rt);

        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }
}
