using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Adds the lower cyber-city environment beneath and around the Level 1 route (UIWorldDemo).
///
/// Purely additive and purely decorative. EVERY object created here has its collider stripped,
/// so nothing can catch a falling player, block a jump, or be stood on as a shortcut.
///
/// Two hard spatial rules, both asserted at the end of the build:
///   1. Nothing may intersect the validator's play corridor  x -13..13, y 0..24, z -9..104.
///      Below-route geometry therefore tops out at y <= -0.5; flanking geometry starts at
///      |x| >= 15.5 (matching the existing City_* blocks).
///   2. Nothing gets a collider.
///
/// Style follows the existing scene exactly: dark blue-grey architecture (Mat_Dark), window
/// panels that are bright BASE COLOURS rather than emissive, and emission reserved for the
/// route markers. The two new neon materials peak at 0.80 emission - 4x dimmer than the
/// 3.20 takeoff/landing markers - and are used on only ~22 objects.
///
/// Never opens or writes IndustrialParkour.unity.
/// </summary>
public static class CyberCityBuilder
{
    private const string ScenePath = "Assets/Scenes/UIWorldDemo.unity";
    private const string MaterialFolder = "Assets/UIWorldDemo/Materials";

    // validator play corridor
    private static readonly Vector3 CorMin = new Vector3(-13f, 0f, -9f);
    private static readonly Vector3 CorMax = new Vector3(13f, 24f, 104f);

    private const float CanyonFloor = -16.5f;   // above the existing Ground at -18, so Ground never shows
    private const float BlockBottom = -22f;
    private const float DeepBottom = -30f;

    private static Material MDark, MConcrete, MWinLit, MWinBlue, MGlass, MNeonCyan, MNeonAmber;
    private static System.Random rng;

    // ---------------------------------------------------------------- helpers

    private static Material Load(string n) => AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/{n}.mat");

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

    /// <summary>Collider-free prop. Everything in this file goes through here.</summary>
    private static GameObject D(string name, Transform parent, Vector3 centre, Vector3 size, Material mat,
        PrimitiveType type = PrimitiveType.Cube)
    {
        Transform ex = parent.Find(name);
        GameObject go = ex != null ? ex.gameObject : GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = centre;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = size;
        MeshRenderer r = go.GetComponent<MeshRenderer>();
        if (r != null && mat != null) r.sharedMaterial = mat;
        Collider c = go.GetComponent<Collider>();
        if (c != null) Object.DestroyImmediate(c);
        return go;
    }

    /// <summary>Box from x/z extents with an explicit roof height and bottom.</summary>
    private static GameObject Blk(string name, Transform p, float x0, float x1, float z0, float z1,
        float roofY, float bottomY, Material mat)
    {
        return D(name, p, new Vector3((x0 + x1) * 0.5f, (roofY + bottomY) * 0.5f, (z0 + z1) * 0.5f),
                 new Vector3(x1 - x0, roofY - bottomY, z1 - z0), mat);
    }

    private static float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

    private static void PointLight(string name, Transform p, Vector3 pos, Color c, float range, float intensity)
    {
        Transform ex = p.Find(name);
        GameObject go = ex != null ? ex.gameObject : new GameObject(name);
        go.transform.SetParent(p, false);
        go.transform.localPosition = pos;
        Light l = go.GetComponent<Light>();
        if (l == null) l = go.AddComponent<Light>();
        l.type = LightType.Point; l.color = c; l.range = range; l.intensity = intensity;
        l.shadows = LightShadows.None;
    }

    // ---------------------------------------------------------------- build

    [MenuItem("Tools/Parkour/C - Lower City Environment")]
    public static void Build()
    {
        Scene s = SceneManager.GetActiveScene();
        if (s.path != ScenePath)
        {
            Debug.LogError($"[CyberCity] ABORT - active scene is '{s.path}', expected {ScenePath}.");
            return;
        }

        rng = new System.Random(20260815);   // deterministic, so rebuilds are identical

        MDark = Load("Mat_Dark");
        MConcrete = Load("Mat_Concrete");
        MWinLit = Load("Mat_WindowLit");
        MWinBlue = Load("Mat_WindowBlue");
        MGlass = Load("Mat_Glass");
        // Emission peaks at 0.80 - deliberately 4x below the 3.20 route markers.
        MNeonCyan = Ensure("Mat_City_NeonCyan", new Color(0.05f, 0.62f, 0.80f), new Color(0.10f, 0.62f, 0.80f), 0.5f, 0f);
        MNeonAmber = Ensure("Mat_City_NeonAmber", new Color(0.80f, 0.42f, 0.10f), new Color(0.80f, 0.34f, 0.06f), 0.5f, 0f);
        AssetDatabase.SaveAssets();

        Transform low = Group("ENV_LOWERCITY");
        Transform sky = Group("ENV_SKYLINE");

        // ============ LAYER 1A - flanking rooftop masses, roofs -2.5..-7.5
        // Four columns either side of a central street canyon, split by z with 4-5 m alleys.
        float[][] cols =
        {
            new[] { -40f, -27f }, new[] { -24f, -12f }, new[] { 12f, 24f }, new[] { 27f, 40f },
        };
        float[][] zsegs =
        {
            new[] { -30f, -10f }, new[] { -5f, 18f }, new[] { 22f, 46f },
            new[] { 50f, 74f }, new[] { 78f, 100f }, new[] { 104f, 124f },
        };
        int n = 0;
        for (int c = 0; c < cols.Length; c++)
        {
            for (int z = 0; z < zsegs.Length; z++)
            {
                if ((c + z) % 7 == 3) continue;                    // leave a few open wells
                float roof = -2.5f - (((c * 3 + z * 5) % 6) * 0.85f) - R(0f, 0.6f);
                Blk($"LC_Roof_{n:00}", low, cols[c][0], cols[c][1], zsegs[z][0], zsegs[z][1],
                    roof, BlockBottom, MDark);
                n++;
            }
        }

        // ============ LAYER 1B - cross-blocks bridging the canyon, roofs -6..-9
        var cross = new (float z0, float z1, float roof)[]
        { (4f, 14f, -6.5f), (36f, 46f, -8.0f), (62f, 71f, -7.0f), (88f, 97f, -9.0f) };
        for (int i = 0; i < cross.Length; i++)
            Blk($"LC_Cross_{i:00}", low, -12f, 12f, cross[i].z0, cross[i].z1, cross[i].roof, BlockBottom, MDark);

        // ============ LAYER 2 - mid blocks seen through the alleys, roofs -9..-15
        var mid = new (float x0, float x1, float z0, float z1, float roof)[]
        {
            (-27f, -24f, -8f, 20f, -10.5f), (-27f, -24f, 24f, 52f, -13.0f), (-27f, -24f, 56f, 84f, -11.0f),
            (24f, 27f, -8f, 22f, -12.0f), (24f, 27f, 26f, 54f, -9.5f), (24f, 27f, 58f, 86f, -14.0f),
            (-12f, 12f, 18f, 22f, -11.5f), (-12f, 12f, 46f, 50f, -13.5f), (-12f, 12f, 74f, 78f, -10.0f),
            (-40f, -27f, 18f, 22f, -12.5f), (27f, 40f, 46f, 50f, -15.0f), (-12f, 12f, 100f, 104f, -12.0f),
        };
        for (int i = 0; i < mid.Length; i++)
            Blk($"LC_Mid_{i:00}", low, mid[i].x0, mid[i].x1, mid[i].z0, mid[i].z1, mid[i].roof, -24f, MDark);

        // ============ LAYER 3 - canyon floor (hides the flat Ground) + shafts + street strips
        var floors = new (float z0, float z1)[]
        { (-32f, 6f), (6f, 40f), (40f, 66f), (66f, 92f), (92f, 126f) };
        for (int i = 0; i < floors.Length; i++)
            Blk($"LC_Floor_{i:00}", low, -13f, 13f, floors[i].z0, floors[i].z1, CanyonFloor, CanyonFloor - 1.2f, MConcrete);
        // side alley floors, also above the Ground plane
        Blk("LC_AlleyFloor_W", low, -28f, -11f, -32f, 126f, CanyonFloor - 0.4f, CanyonFloor - 1.6f, MConcrete);
        Blk("LC_AlleyFloor_E", low, 11f, 28f, -32f, 126f, CanyonFloor - 0.4f, CanyonFloor - 1.6f, MConcrete);
        Blk("LC_AlleyFloor_FW", low, -42f, -26f, -32f, 126f, CanyonFloor - 0.8f, CanyonFloor - 2.0f, MConcrete);
        Blk("LC_AlleyFloor_FE", low, 26f, 42f, -32f, 126f, CanyonFloor - 0.8f, CanyonFloor - 2.0f, MConcrete);
        // deep shafts punching below, for a bottomless read in a few spots
        var shafts = new (float x, float z, float w, float d)[]
        { (-6f, 26f, 5f, 6f), (7f, 56f, 4f, 5f), (-4f, 82f, 5f, 5f), (9f, -2f, 4f, 4f) };
        for (int i = 0; i < shafts.Length; i++)
            D($"LC_Shaft_{i:00}", low, new Vector3(shafts[i].x, (CanyonFloor + DeepBottom) * 0.5f, shafts[i].z),
              new Vector3(shafts[i].w, CanyonFloor - DeepBottom, shafts[i].d), MGlass);
        // street light strips along the canyon
        for (int i = 0; i < 12; i++)
        {
            float z = -26f + i * 12.5f;
            D($"LC_Strip_{i:00}", low, new Vector3(i % 2 == 0 ? -8.5f : 8.5f, CanyonFloor + 0.16f, z),
              new Vector3(0.35f, 0.08f, 7f), i % 3 == 0 ? MNeonAmber : MNeonCyan);
        }

        // ============ rooftop clutter on the lower roofs (tops stay <= -1.0)
        int cl = 0;
        foreach (Transform t in low)
        {
            if (!t.name.StartsWith("LC_Roof_")) continue;
            MeshRenderer mr = t.GetComponent<MeshRenderer>();
            Bounds b = mr.bounds;
            int props = 2 + (cl % 3);
            for (int k = 0; k < props; k++)
            {
                float px = R(b.min.x + 2f, b.max.x - 2f);
                float pz = R(b.min.z + 2f, b.max.z - 2f);
                float h = R(0.6f, 1.4f);
                if (b.max.y + h > -1.0f) h = Mathf.Max(0.4f, -1.0f - b.max.y);
                int kind = (cl + k) % 4;
                string nm = kind == 0 ? "HVAC" : kind == 1 ? "Vent" : kind == 2 ? "Duct" : "Util";
                Vector3 size = kind == 0 ? new Vector3(R(2f, 3.5f), h, R(2f, 3f))
                             : kind == 1 ? new Vector3(R(1f, 1.8f), h, R(1f, 1.8f))
                             : kind == 2 ? new Vector3(R(4f, 9f), h * 0.6f, R(0.8f, 1.2f))
                                         : new Vector3(R(1.2f, 2f), h, R(1.2f, 2f));
                D($"LC_{nm}_{cl:00}_{k}", low, new Vector3(px, b.max.y + size.y * 0.5f, pz), size,
                  kind == 2 ? MConcrete : MDark);
            }
            // tall masts only on the outer roofs, where the corridor cannot be reached
            if (Mathf.Abs(b.center.x) > 15.5f && cl % 2 == 0)
                D($"LC_Mast_{cl:00}", low, new Vector3(b.center.x + R(-4f, 4f), b.max.y + 4f, b.center.z + R(-6f, 6f)),
                  new Vector3(0.18f, 8f, 0.18f), MConcrete);
            cl++;
        }

        // ============ structural supports under the larger decks, capped at y -0.6
        var sup = new (string deck, float x, float z, float w)[]
        {
            ("Platform_Start", -5f, -3f, 1.1f), ("Platform_Start", 5f, 3f, 1.1f),
            ("Deck_North", -9f, 77.5f, 1.2f), ("Deck_North", 9f, 80f, 1.2f),
            ("Deck_South", 6f, 3f, 1.0f), ("Deck_South", 11f, 7f, 1.0f),
            ("Plat_K7", -3f, 77.5f, 0.9f), ("Plat_K7", 3f, 80.5f, 0.9f),
            ("Mid_A", -11f, 30f, 0.9f), ("Mid_B", 11f, 54f, 0.9f),
            ("Mid_C", -11f, 92f, 0.9f), ("Mid_D", 11f, 14f, 0.9f),
        };
        for (int i = 0; i < sup.Length; i++)
        {
            float top = -0.6f, bot = -14f;
            D($"LC_Support_{i:00}", low, new Vector3(sup[i].x, (top + bot) * 0.5f, sup[i].z),
              new Vector3(sup[i].w, top - bot, sup[i].w), MConcrete);
        }

        // ============ LAYER 4 - flanking towers filling the z-gaps, |x| >= 15.5
        var towers = new (float x0, float x1, float z0, float z1, float top)[]
        {
            (15.5f, 27f, 22f, 32f, 24f), (-27f, -15.5f, 20f, 30f, 18f),
            (15.5f, 26f, 50f, 60f, 28f), (-26f, -15.5f, 48f, 58f, 34f),
            (16f, 28f, 76f, 84f, 20f), (-28f, -16f, 74f, 82f, 26f),
            (15.5f, 25f, -14f, -2f, 22f), (-25f, -15.5f, -16f, -4f, 16f),
            (32f, 46f, 8f, 24f, 40f), (-46f, -32f, 14f, 30f, 36f),
            (34f, 50f, 78f, 96f, 45f), (-50f, -34f, 84f, 100f, 42f),
        };
        for (int i = 0; i < towers.Length; i++)
            Blk($"LC_Tower_{i:00}", sky, towers[i].x0, towers[i].x1, towers[i].z0, towers[i].z1,
                towers[i].top, -2f, MDark);

        // ============ LAYER 5 - distant skyline, fading in the existing 55..320 fog
        var far = new (float x, float z, float w, float d, float h)[]
        {
            (-70f, 30f, 26f, 30f, 56f), (72f, 44f, 30f, 26f, 64f), (-88f, 96f, 34f, 30f, 48f),
            (94f, 100f, 30f, 34f, 72f), (-64f, 150f, 30f, 34f, 60f), (66f, 158f, 34f, 30f, 80f),
            (-120f, 60f, 44f, 40f, 70f), (126f, 70f, 40f, 44f, 88f), (-30f, 190f, 44f, 40f, 66f),
            (40f, 205f, 40f, 44f, 78f), (-150f, 130f, 50f, 46f, 84f), (160f, 140f, 46f, 50f, 90f),
            (-96f, -60f, 40f, 36f, 52f), (100f, -70f, 36f, 40f, 58f), (0f, 250f, 60f, 50f, 74f),
            (-200f, 40f, 56f, 52f, 62f), (210f, 190f, 52f, 56f, 68f), (-40f, 300f, 70f, 60f, 58f),
        };
        for (int i = 0; i < far.Length; i++)
            Blk($"SK_Far_{i:00}", sky, far[i].x - far[i].w * 0.5f, far[i].x + far[i].w * 0.5f,
                far[i].z - far[i].d * 0.5f, far[i].z + far[i].d * 0.5f, far[i].h, -4f, MDark);

        // ============ windows - bright base colours, matching the existing convention
        int w1 = 0;
        foreach (Transform t in sky)
        {
            if (!t.name.StartsWith("LC_Tower_")) continue;
            Bounds b = t.GetComponent<MeshRenderer>().bounds;
            float faceX = b.center.x > 0f ? b.min.x - 0.06f : b.max.x + 0.06f;
            int rowsN = Mathf.Clamp(Mathf.FloorToInt((b.max.y - 2f) / 6f), 2, 6);
            for (int r = 0; r < rowsN; r++)
            {
                for (int k = 0; k < 2; k++)
                {
                    float py = 3f + r * 5.5f;
                    if (py > b.max.y - 2f) continue;
                    float pz = Mathf.Lerp(b.min.z + 2.5f, b.max.z - 2.5f, k == 0 ? 0.25f : 0.72f);
                    D($"LC_Win_{w1:000}", sky, new Vector3(faceX, py, pz), new Vector3(0.1f, 2.2f, 4.2f),
                      (w1 % 3 == 0) ? MWinBlue : MWinLit);
                    w1++;
                }
            }
        }
        // windows on the lower-city masses, facing the canyon so they read from the route
        int w2 = 0;
        foreach (Transform t in low)
        {
            if (!t.name.StartsWith("LC_Roof_")) continue;
            Bounds b = t.GetComponent<MeshRenderer>().bounds;
            bool east = b.center.x > 0f;
            float faceX = east ? b.min.x - 0.06f : b.max.x + 0.06f;
            for (int r = 0; r < 2; r++)
            {
                float py = b.max.y - 2.2f - r * 3.4f;
                if (py < -14f) continue;
                float pz = Mathf.Lerp(b.min.z + 3f, b.max.z - 3f, r == 0 ? 0.3f : 0.7f);
                D($"LC_WinLow_{w2:000}", low, new Vector3(faceX, py, pz), new Vector3(0.1f, 1.8f, 4.5f),
                  (w2 % 4 == 0) ? MWinLit : MWinBlue);
                w2++;
            }
        }

        // ============ neon: vertical strips on tower corners + a few sign panels
        int nn = 0;
        foreach (Transform t in sky)
        {
            if (!t.name.StartsWith("LC_Tower_")) continue;
            if (nn >= 16) break;
            Bounds b = t.GetComponent<MeshRenderer>().bounds;
            float faceX = b.center.x > 0f ? b.min.x - 0.08f : b.max.x + 0.08f;
            float h = Mathf.Min(14f, b.max.y - 3f);
            D($"LC_Neon_{nn:00}", sky, new Vector3(faceX, b.max.y - h * 0.5f - 1.5f, b.min.z + 1.4f),
              new Vector3(0.16f, h, 0.34f), nn % 2 == 0 ? MNeonCyan : MNeonAmber);
            nn++;
        }
        var signs = new (float x, float y, float z, float w, float h)[]
        {
            (-33f, 16f, 20f, 0.2f, 5f), (33f, 20f, 56f, 0.2f, 6f), (-34f, 24f, 90f, 0.2f, 5f),
            (35f, 14f, -6f, 0.2f, 4f), (-47f, 28f, 26f, 0.2f, 7f), (47f, 32f, 86f, 0.2f, 6f),
        };
        for (int i = 0; i < signs.Length; i++)
            D($"LC_Sign_{i:00}", sky, new Vector3(signs[i].x, signs[i].y, signs[i].z),
              new Vector3(signs[i].w, signs[i].h, 7f), i % 2 == 0 ? MNeonCyan : MNeonAmber);

        // ============ lights - all well below the route, dim, shadowless
        Color cool = new Color(0.55f, 0.75f, 1.00f);
        Color amber = new Color(1.00f, 0.66f, 0.36f);
        PointLight("LC_Light_01", low, new Vector3(0f, -12f, 4f), cool, 22f, 8f);
        PointLight("LC_Light_02", low, new Vector3(0f, -12f, 34f), cool, 22f, 8f);
        PointLight("LC_Light_03", low, new Vector3(0f, -12f, 62f), amber, 20f, 7f);
        PointLight("LC_Light_04", low, new Vector3(0f, -12f, 92f), cool, 22f, 8f);
        PointLight("LC_Light_05", low, new Vector3(-25f, -9f, 20f), cool, 18f, 6f);
        PointLight("LC_Light_06", low, new Vector3(25f, -9f, 52f), amber, 18f, 6f);
        PointLight("LC_Light_07", low, new Vector3(-25f, -9f, 82f), cool, 18f, 6f);
        PointLight("LC_Light_08", low, new Vector3(25f, -9f, 8f), cool, 18f, 6f);

        // ---------------------------------------------------------------- assertions
        Bounds corridor = new Bounds();
        corridor.SetMinMax(CorMin, CorMax);
        int intr = 0, cols2 = 0, total = 0;
        foreach (Transform g in new[] { low, sky })
        {
            foreach (var mr in g.GetComponentsInChildren<MeshRenderer>(true))
            {
                total++;
                if (corridor.Intersects(mr.bounds)) { Debug.LogError($"[CyberCity] CORRIDOR HIT {mr.name} {mr.bounds}"); intr++; }
            }
            cols2 += g.GetComponentsInChildren<Collider>(true).Length;
        }

        EditorSceneManager.MarkSceneDirty(s);
        EditorSceneManager.SaveScene(s, ScenePath);
        AssetDatabase.SaveAssets();

        Debug.Log($"[CyberCity] built {total} objects (ENV_LOWERCITY {low.childCount}, ENV_SKYLINE {sky.childCount}); "
                + $"corridor intersections={intr}; colliders={cols2}. Scene saved.");
    }
}
