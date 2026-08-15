using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Adds the coolant hazard pit beneath the IndustrialParkour route.
///
/// Purely additive and purely decorative: EVERY object created here has its collider stripped.
/// That is not a style choice - the respawn fires when the player's feet pass y = -12
/// (BasicFirstPersonController.fallResetHeight), so any collider above that line would catch the
/// player and silently break fall/respawn. Collider-free geometry cannot affect the route at all.
///
/// The coolant surface sits at y = -12.5, just below the reset line, so the reset fires a frame
/// before the player would visually touch the liquid.
///
/// Touches nothing that already exists: no platform, checkpoint, light, material or setting.
/// </summary>
public static class IndustrialPitBuilder
{
    private const string MaterialFolder = "Assets/UIWorldDemo/Materials";

    // basin footprint and tier geometry
    private const float X0 = -48f, X1 = 44f, Z0 = -14f, Z1 = 90f;
    private const float LiquidY = -12.5f;

    private static Material MCoolant, MHot, MSteam, MWarnLight, MConcrete, MSteel, MRust, MDark, MGrate;

    // ---------------------------------------------------------------- materials

    private static Material Load(string name)
    {
        return AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/{name}.mat");
    }

    private static Material Ensure(string name, Color baseColor, Color emission, float smooth, float metal)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            m = new Material(sh) { name = name };
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", baseColor);
        if (m.HasProperty("_Color")) m.SetColor("_Color", baseColor);
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", metal);
        if (emission.maxColorComponent > 0f)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        else
        {
            m.DisableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    /// <summary>Stock URP/Lit switched to alpha-blended transparent - no custom shader needed.</summary>
    private static Material EnsureTransparent(string name, Color rgba)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            m = new Material(sh) { name = name };
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetFloat("_Surface", 1f);                 // 0 opaque, 1 transparent
        m.SetFloat("_Blend", 0f);                   // alpha blend
        m.SetFloat("_AlphaClip", 0f);
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_Smoothness", 0f);
        m.SetFloat("_Metallic", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.DisableKeyword("_ALPHATEST_ON");
        m.DisableKeyword("_EMISSION");
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        m.SetColor("_BaseColor", rgba);
        if (m.HasProperty("_Color")) m.SetColor("_Color", rgba);
        EditorUtility.SetDirty(m);
        return m;
    }

    private static void LoadPalette()
    {
        MConcrete = Load("Mat_Ind_Concrete");
        MSteel = Load("Mat_Ind_Steel");
        MRust = Load("Mat_Ind_Rust");
        MDark = Load("Mat_Ind_Dark");
        MGrate = Load("Mat_Ind_Grate");

        // near-black coolant with a low orange-red bloom; deliberately far dimmer than the
        // HDR route lips (3.2) so it never competes with takeoff/landing readability
        MCoolant = Ensure("Mat_Ind_Coolant", new Color(0.07f, 0.045f, 0.035f),
                          new Color(0.38f, 0.10f, 0.025f), 0.72f, 0.55f);
        MHot = Ensure("Mat_Ind_CoolantHot", new Color(0.14f, 0.06f, 0.035f),
                      new Color(1.15f, 0.30f, 0.06f), 0.68f, 0.4f);
        MWarnLight = Ensure("Mat_Ind_WarnLight", new Color(0.55f, 0.07f, 0.03f),
                            new Color(2.10f, 0.22f, 0.06f), 0.4f, 0f);
        MSteam = EnsureTransparent("Mat_Ind_Steam", new Color(0.58f, 0.50f, 0.46f, 0.075f));
        AssetDatabase.SaveAssets();
    }

    // ---------------------------------------------------------------- primitives

    private static Transform Group(string name)
    {
        GameObject world = GameObject.Find("WORLD");
        if (world == null) world = new GameObject("WORLD");
        Transform t = world.transform.Find(name);
        if (t == null)
        {
            GameObject g = new GameObject(name);
            g.transform.SetParent(world.transform, false);
            t = g.transform;
        }
        return t;
    }

    /// <summary>Collider-free prop. Everything in the pit goes through here.</summary>
    private static GameObject D(string name, Transform parent, Vector3 centre, Vector3 size, Material mat,
        PrimitiveType type = PrimitiveType.Cube, Vector3 euler = default)
    {
        Transform existing = parent.Find(name);
        GameObject go = existing != null ? existing.gameObject : GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = centre;
        go.transform.localRotation = Quaternion.Euler(euler);
        go.transform.localScale = size;
        MeshRenderer r = go.GetComponent<MeshRenderer>();
        if (r != null && mat != null) r.sharedMaterial = mat;
        Collider c = go.GetComponent<Collider>();
        if (c != null) Object.DestroyImmediate(c);
        return go;
    }

    /// <summary>Axis-aligned box from min/max corners, so basin tiers are easy to reason about.</summary>
    private static void Slab(string name, Transform p, float x0, float x1, float yTop, float h,
        float z0, float z1, Material mat)
    {
        D(name, p, new Vector3((x0 + x1) * 0.5f, yTop - h * 0.5f, (z0 + z1) * 0.5f),
          new Vector3(x1 - x0, h, z1 - z0), mat);
    }

    /// <summary>Horizontal pipe of diameter d spanning from a to b along one axis.</summary>
    private static void Pipe(string name, Transform p, Vector3 a, Vector3 b, float d, Material mat)
    {
        Vector3 mid = (a + b) * 0.5f;
        float len = Vector3.Distance(a, b);
        Vector3 dir = (b - a).normalized;
        Quaternion rot = Quaternion.FromToRotation(Vector3.up, dir);
        Transform existing = p.Find(name);
        GameObject go = existing != null ? existing.gameObject : GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.transform.SetParent(p, false);
        go.transform.localPosition = mid;
        go.transform.localRotation = rot;
        go.transform.localScale = new Vector3(d, len * 0.5f, d);
        MeshRenderer r = go.GetComponent<MeshRenderer>();
        if (r != null && mat != null) r.sharedMaterial = mat;
        Collider c = go.GetComponent<Collider>();
        if (c != null) Object.DestroyImmediate(c);
    }

    private static void WarnLight(string name, Transform p, Vector3 pos)
    {
        D($"{name}_Fixture", p, pos, new Vector3(0.55f, 0.35f, 0.55f), MWarnLight);
        Transform ex = p.Find($"{name}_Light");
        GameObject go = ex != null ? ex.gameObject : new GameObject($"{name}_Light");
        go.transform.SetParent(p, false);
        go.transform.localPosition = pos + Vector3.up * 0.4f;
        Light l = go.GetComponent<Light>();
        if (l == null) l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = new Color(1.00f, 0.15f, 0.05f);
        l.intensity = 8f;
        l.range = 15f;
        l.shadows = LightShadows.None;
    }

    // ---------------------------------------------------------------- build

    [MenuItem("Tools/Industrial/P - Hazard Pit")]
    public static void BuildPit()
    {
        Scene s = SceneManager.GetActiveScene();
        if (s.path != IndustrialLevelBuilder.ScenePath)
        {
            Debug.LogError($"[Pit] ABORT - active scene is '{s.path}', expected {IndustrialLevelBuilder.ScenePath}.");
            return;
        }

        LoadPalette();
        Transform g = Group("ENV_PIT");

        // ---- 1. basin tiers: three stepped rings, -13 up to -2, so the pit reads as sunken
        float[] tierTop = { -2.0f, -6.0f, -10.0f };
        float[] tierH = { 4.0f, 4.0f, 3.0f };
        float[] tierW = { 6.0f, 6.0f, 5.0f };
        float ax0 = X0, ax1 = X1, az0 = Z0, az1 = Z1;
        for (int t = 0; t < 3; t++)
        {
            float w = tierW[t], yt = tierTop[t], h = tierH[t];
            Material mat = t == 2 ? MDark : MConcrete;
            Slab($"Basin_T{t + 1}_S", g, ax0, ax1, yt, h, az0, az0 + w, mat);
            Slab($"Basin_T{t + 1}_N", g, ax0, ax1, yt, h, az1 - w, az1, mat);
            Slab($"Basin_T{t + 1}_W", g, ax0, ax0 + w, yt, h, az0 + w, az1 - w, mat);
            Slab($"Basin_T{t + 1}_E", g, ax1 - w, ax1, yt, h, az0 + w, az1 - w, mat);
            ax0 += w; ax1 -= w; az0 += w; az1 -= w;
        }
        // inner basin now spans ax0..ax1, az0..az1  (=  -31..27, 3..73)

        // ---- 2. coolant surface + hotter patches
        Slab("Coolant_Surface", g, ax0, ax1, LiquidY, 0.25f, az0, az1, MCoolant);
        D("Coolant_Hot_A", g, new Vector3(-18f, LiquidY - 0.05f, 20f), new Vector3(14f, 0.16f, 11f), MHot);
        D("Coolant_Hot_B", g, new Vector3(12f, LiquidY - 0.05f, 46f), new Vector3(17f, 0.16f, 13f), MHot);
        D("Coolant_Hot_C", g, new Vector3(-6f, LiquidY - 0.05f, 62f), new Vector3(11f, 0.16f, 9f), MHot);
        D("Coolant_Hot_D", g, new Vector3(20f, LiquidY - 0.05f, 12f), new Vector3(9f, 0.16f, 8f), MHot);

        // ---- 3. submerged machinery breaking the liquid plane
        var sunk = new (float x, float z, float w, float h, float d)[]
        {
            (-26f, 10f, 6f, 4.5f, 6f), (-14f, 34f, 7f, 3.2f, 5f), (-2f, 8f, 5f, 5.0f, 5f),
            (8f,  28f, 6f, 3.8f, 7f), (22f, 36f, 5f, 4.4f, 5f), (-22f, 56f, 6f, 3.4f, 6f),
            (2f,  66f, 7f, 4.0f, 6f), (18f, 60f, 5f, 3.0f, 5f), (-28f, 40f, 5f, 5.2f, 5f),
            (14f,  6f, 6f, 3.6f, 6f),
        };
        for (int i = 0; i < sunk.Length; i++)
        {
            var m = sunk[i];
            D($"Sunk_Machine_{i:00}", g, new Vector3(m.x, LiquidY + m.h * 0.5f - 1.0f, m.z),
              new Vector3(m.w, m.h, m.d), i % 3 == 0 ? MRust : MDark);
        }

        // ---- 4. large pipes crossing the pit, plus vertical downpipes into the coolant
        Pipe("Pit_Pipe_01", g, new Vector3(-44f, -4.0f, 12f), new Vector3(40f, -4.0f, 12f), 1.8f, MRust);
        Pipe("Pit_Pipe_02", g, new Vector3(-44f, -6.5f, 30f), new Vector3(40f, -6.5f, 30f), 1.4f, MRust);
        Pipe("Pit_Pipe_03", g, new Vector3(-44f, -3.2f, 54f), new Vector3(40f, -3.2f, 54f), 2.0f, MSteel);
        Pipe("Pit_Pipe_04", g, new Vector3(-44f, -7.5f, 70f), new Vector3(40f, -7.5f, 70f), 1.2f, MRust);
        Pipe("Pit_Pipe_05", g, new Vector3(-26f, -5.0f, -10f), new Vector3(-26f, -5.0f, 86f), 1.6f, MSteel);
        Pipe("Pit_Pipe_06", g, new Vector3(16f, -8.0f, -10f), new Vector3(16f, -8.0f, 86f), 1.3f, MRust);
        Pipe("Pit_Down_01", g, new Vector3(-30f, -2.0f, 22f), new Vector3(-30f, -12.0f, 22f), 1.1f, MRust);
        Pipe("Pit_Down_02", g, new Vector3(24f, -3.0f, 44f), new Vector3(24f, -12.0f, 44f), 1.3f, MSteel);
        Pipe("Pit_Down_03", g, new Vector3(-8f, -4.0f, 68f), new Vector3(-8f, -12.0f, 68f), 0.9f, MRust);
        Pipe("Pit_Down_04", g, new Vector3(6f, -2.5f, 4f), new Vector3(6f, -12.0f, 4f), 1.0f, MRust);

        // ---- 5. structural supports: column under a spread of decks, capped 1 m below each
        string[] supported =
        {
            "Bay_Gantry", "Conv_02", "Conv_04", "Hall_Deck", "Boil_P2", "Boil_P4", "Boil_Deck",
            "Scaf_B1", "Scaf_B2", "Scaf_Deck", "Crane_Arm_A", "Crane_Hook_1", "Crane_Arm_B", "Crane_Deck",
        };
        foreach (string n in supported)
        {
            IndustrialLevelBuilder.Row row = null;
            foreach (var r in IndustrialLevelBuilder.Rows) if (r.Name == n) { row = r; break; }
            if (row == null) continue;
            float top = row.TopY - row.Thick - 1.0f;      // stop 1 m clear of the deck underside
            float bottom = -12f;
            if (top <= bottom + 1f) continue;
            D($"Support_{n}", g, new Vector3(row.X, (top + bottom) * 0.5f, row.Z),
              new Vector3(0.55f, top - bottom, 0.55f), MSteel);
            D($"SupportFoot_{n}", g, new Vector3(row.X, bottom + 0.4f, row.Z),
              new Vector3(1.9f, 0.8f, 1.9f), MConcrete);
        }

        // ---- 6. derelict grate catwalks, mid-depth layer
        var walks = new (float x, float z, float y, float w, float d)[]
        {
            (-20f, 18f, -5.0f, 16f, 2.4f), (6f, 26f, -6.2f, 2.4f, 18f), (-4f, 50f, -4.4f, 20f, 2.4f),
            (20f, 58f, -6.8f, 2.4f, 14f), (-24f, 66f, -5.6f, 12f, 2.4f), (26f, 20f, -4.0f, 2.4f, 16f),
        };
        for (int i = 0; i < walks.Length; i++)
        {
            var w = walks[i];
            D($"Pit_Catwalk_{i:00}", g, new Vector3(w.x, w.y, w.z), new Vector3(w.w, 0.18f, w.d), MGrate);
        }

        // ---- 7. machinery silhouettes massed at the pit edges
        var mach = new (string n, Vector3 p, Vector3 s)[]
        {
            ("Pit_Pump_A", new Vector3(-40f, -6.0f, 8f), new Vector3(7f, 9f, 7f)),
            ("Pit_Pump_B", new Vector3(-40f, -7.0f, 78f), new Vector3(6f, 8f, 6f)),
            ("Pit_Tank_A", new Vector3(36f, -5.5f, 24f), new Vector3(8f, 10f, 8f)),
            ("Pit_Tank_B", new Vector3(36f, -6.5f, 64f), new Vector3(7f, 9f, 7f)),
            ("Pit_Duct_A", new Vector3(-12f, -3.0f, -8f), new Vector3(22f, 3.5f, 4f)),
            ("Pit_Duct_B", new Vector3(14f, -4.0f, 84f), new Vector3(20f, 3.0f, 4f)),
            ("Pit_Block_A", new Vector3(-34f, -9.0f, 34f), new Vector3(6f, 5f, 9f)),
            ("Pit_Block_B", new Vector3(30f, -9.5f, 44f), new Vector3(5f, 4f, 10f)),
            ("Pit_Block_C", new Vector3(-2f, -10.0f, 78f), new Vector3(11f, 4f, 6f)),
            ("Pit_Block_D", new Vector3(8f, -10.5f, -6f), new Vector3(10f, 3.5f, 6f)),
        };
        foreach (var m in mach) D(m.n, g, m.p, m.s, MDark);

        // ---- 8. warning lights around the rim and over the coolant
        WarnLight("PitWarn_01", g, new Vector3(-30f, -3.4f, 6f));
        WarnLight("PitWarn_02", g, new Vector3(26f, -3.4f, 16f));
        WarnLight("PitWarn_03", g, new Vector3(-30f, -3.4f, 48f));
        WarnLight("PitWarn_04", g, new Vector3(26f, -3.4f, 60f));
        WarnLight("PitWarn_05", g, new Vector3(-6f, -7.6f, 30f));
        WarnLight("PitWarn_06", g, new Vector3(12f, -7.6f, 70f));
        WarnLight("PitWarn_07", g, new Vector3(-22f, -9.2f, 62f));
        WarnLight("PitWarn_08", g, new Vector3(18f, -9.2f, 8f));

        // ---- 9. steam: stacked translucent decks just above the coolant + vertical plumes
        var decks = new (float x, float z, float y, float w, float d)[]
        {
            (-16f, 18f, -11.4f, 40f, 34f), (10f, 40f, -10.9f, 44f, 36f), (-8f, 62f, -11.1f, 38f, 28f),
            (18f, 14f, -10.4f, 30f, 26f), (-24f, 44f, -10.2f, 26f, 30f), (2f, 30f, -9.6f, 46f, 40f),
            (-12f, 70f, -9.2f, 30f, 22f), (22f, 52f, -8.8f, 24f, 26f), (-4f, 12f, -8.4f, 34f, 24f),
            (6f, 56f, -8.0f, 32f, 28f),
        };
        for (int i = 0; i < decks.Length; i++)
        {
            var dk = decks[i];
            D($"Steam_Deck_{i:00}", g, new Vector3(dk.x, dk.y, dk.z), new Vector3(dk.w, 0.06f, dk.d), MSteam);
        }
        var plumes = new (float x, float z)[] { (-18f, 20f), (12f, 46f), (-6f, 62f), (20f, 12f) };
        for (int i = 0; i < plumes.Length; i++)
        {
            D($"Steam_Plume_{i:00}", g, new Vector3(plumes[i].x, -9.5f, plumes[i].z),
              new Vector3(5.5f, 6.5f, 5.5f), MSteam);
        }

        EditorSceneManager.MarkSceneDirty(s);
        EditorSceneManager.SaveScene(s, IndustrialLevelBuilder.ScenePath);
        AssetDatabase.SaveAssets();

        int props = g.childCount;
        Debug.Log($"[Pit] hazard pit built: {props} objects under ENV_PIT, all collider-free. Scene saved.");
    }
}
