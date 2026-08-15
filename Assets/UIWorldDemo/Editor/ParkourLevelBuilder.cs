using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the redesigned parkour course for UIWorldDemo.
/// Every menu item is idempotent: existing GameObjects are reused (looked up by name) and
/// only re-positioned, so re-running a stage does not duplicate geometry.
///
/// Movement envelope this level is designed against (read from BasicFirstPersonController):
///   jumpHeight 1.5, gravity -9  =>  launch 5.196 m/s, peak rise 1.50 m, flat airtime 1.155 s
///   walk 6 m/s  => 6.93 m flat range        sprint 9 m/s => 10.39 m flat range
///   stepOffset 0.30 m is free (no jump needed)
/// Use "Tools/Parkour/8 - Validate Reachability" to re-verify every jump against live geometry.
/// </summary>
public static class ParkourLevelBuilder
{
    private const string MaterialFolder = "Assets/UIWorldDemo/Materials";
    private const float Footing = 0.4f;      // takeoff + landing footing allowance (m)
    private const float LipInset = 0.09f;
    private const float LipHeight = 0.12f;
    private const float LipDepth = 0.18f;

    private static Dictionary<string, GameObject> index;

    // ---------------------------------------------------------------- infrastructure

    private static void Begin()
    {
        index = new Dictionary<string, GameObject>();
        Scene scene = SceneManager.GetActiveScene();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Collect(root.transform);
        }
    }

    private static void Collect(Transform t)
    {
        if (!index.ContainsKey(t.gameObject.name))
        {
            index[t.gameObject.name] = t.gameObject;
        }

        for (int i = 0; i < t.childCount; i++)
        {
            Collect(t.GetChild(i));
        }
    }

    private static void End(string label)
    {
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[ParkourBuilder] {label} complete.");
    }

    private static GameObject Find(string name)
    {
        return index.TryGetValue(name, out GameObject go) ? go : null;
    }

    private static void Rename(string oldName, string newName)
    {
        if (index.ContainsKey(newName))
        {
            return;
        }

        GameObject go = Find(oldName);
        if (go == null)
        {
            return;
        }

        Undo.RecordObject(go, "Rename parkour object");
        go.name = newName;
        index.Remove(oldName);
        index[newName] = go;
    }

    private static Transform Group(string name)
    {
        GameObject world = Find("WORLD");
        if (world == null)
        {
            world = new GameObject("WORLD");
            index["WORLD"] = world;
        }

        GameObject group = Find(name);
        if (group == null)
        {
            group = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(group, "Create parkour group");
            index[name] = group;
        }

        group.transform.SetParent(world.transform, false);
        group.transform.localPosition = Vector3.zero;
        group.transform.localRotation = Quaternion.identity;
        group.transform.localScale = Vector3.one;
        return group.transform;
    }

    private static GameObject Prim(string name, Transform parent, PrimitiveType type)
    {
        GameObject go = Find(name);
        if (go == null)
        {
            go = GameObject.CreatePrimitive(type);
            go.name = name;
            Undo.RegisterCreatedObjectUndo(go, "Create parkour object");
            index[name] = go;
        }

        Undo.RecordObject(go.transform, "Move parkour object");
        if (go.transform.parent != parent)
        {
            go.transform.SetParent(parent, false);
        }

        return go;
    }

    private static void Paint(GameObject go, Material mat)
    {
        if (mat == null)
        {
            return;
        }

        MeshRenderer r = go.GetComponent<MeshRenderer>();
        if (r != null && r.sharedMaterial != mat)
        {
            Undo.RecordObject(r, "Paint parkour object");
            r.sharedMaterial = mat;
        }
    }

    /// <summary>Slab whose long axis runs along Z (the usual lane platform).</summary>
    private static GameObject Slab(string name, Transform parent, float xCenter, float width,
        float zLo, float zHi, float topY, float thickness, Material mat)
    {
        GameObject go = Prim(name, parent, PrimitiveType.Cube);
        go.transform.localPosition = new Vector3(xCenter, topY - thickness * 0.5f, (zLo + zHi) * 0.5f);
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = new Vector3(width, thickness, zHi - zLo);
        Paint(go, mat);
        return go;
    }

    /// <summary>Slab whose long axis runs along X (used for the tower's north-face ledges).</summary>
    private static GameObject SlabX(string name, Transform parent, float xLo, float xHi,
        float zCenter, float depth, float topY, float thickness, Material mat)
    {
        GameObject go = Prim(name, parent, PrimitiveType.Cube);
        go.transform.localPosition = new Vector3((xLo + xHi) * 0.5f, topY - thickness * 0.5f, zCenter);
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = new Vector3(xHi - xLo, thickness, depth);
        Paint(go, mat);
        return go;
    }

    private static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 scale, Material mat)
    {
        GameObject go = Prim(name, parent, PrimitiveType.Cube);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = scale;
        Paint(go, mat);
        return go;
    }

    private static GameObject Post(string name, Transform parent, Vector3 pos, Vector3 scale, Material mat)
    {
        GameObject go = Prim(name, parent, PrimitiveType.Capsule);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = scale;
        Paint(go, mat);
        return go;
    }

    private enum Edge { ZLo, ZHi, XLo, XHi }

    /// <summary>Emissive edge strip flush with the top surface of a platform.</summary>
    private static void Lip(string name, Transform parent, GameObject platform, Edge edge, Material mat)
    {
        if (platform == null)
        {
            return;
        }

        Vector3 p = platform.transform.localPosition;
        Vector3 s = platform.transform.localScale;
        float top = p.y + s.y * 0.5f;
        Vector3 pos;
        Vector3 scale;

        switch (edge)
        {
            case Edge.ZHi:
                pos = new Vector3(p.x, top + LipHeight * 0.5f, p.z + s.z * 0.5f - LipInset);
                scale = new Vector3(s.x * 0.9f, LipHeight, LipDepth);
                break;
            case Edge.ZLo:
                pos = new Vector3(p.x, top + LipHeight * 0.5f, p.z - s.z * 0.5f + LipInset);
                scale = new Vector3(s.x * 0.9f, LipHeight, LipDepth);
                break;
            case Edge.XHi:
                pos = new Vector3(p.x + s.x * 0.5f - LipInset, top + LipHeight * 0.5f, p.z);
                scale = new Vector3(LipDepth, LipHeight, s.z * 0.9f);
                break;
            default:
                pos = new Vector3(p.x - s.x * 0.5f + LipInset, top + LipHeight * 0.5f, p.z);
                scale = new Vector3(LipDepth, LipHeight, s.z * 0.9f);
                break;
        }

        Box(name, parent, pos, scale, mat);
    }

    // ---------------------------------------------------------------- materials

    private static Material EnsureMaterial(string name, Color baseColor, Color emission, float smoothness, float metallic)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            m = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(m, path);
        }

        m.SetColor("_BaseColor", baseColor);
        if (m.HasProperty("_Color"))
        {
            m.SetColor("_Color", baseColor);
        }

        m.SetFloat("_Smoothness", smoothness);
        m.SetFloat("_Metallic", metallic);

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

    private static Material MatDeck, MatJump, MatPrecision, MatTakeoff, MatLand;
    private static Material MatCity, MatWindowLit, MatWindowCool, MatGoal, MatRail;

    private static void LoadPalette()
    {
        MatDeck = EnsureMaterial("Mat_Path_Deck", new Color(0.74f, 0.71f, 0.65f), Color.black, 0.18f, 0f);
        MatJump = EnsureMaterial("Mat_Path_Jump", new Color(0.42f, 0.44f, 0.48f), Color.black, 0.24f, 0f);
        MatPrecision = EnsureMaterial("Mat_Path_Precision", new Color(0.15f, 0.16f, 0.19f), Color.black, 0.35f, 0.1f);
        MatTakeoff = EnsureMaterial("Mat_Edge_Takeoff", new Color(0.9f, 0.34f, 0.06f), new Color(3.2f, 0.85f, 0.12f), 0.5f, 0f);
        MatLand = EnsureMaterial("Mat_Edge_Land", new Color(0.1f, 0.7f, 0.85f), new Color(0.2f, 2.4f, 3.2f), 0.5f, 0f);
        MatGoal = EnsureMaterial("Mat_Goal_Glow", new Color(0.85f, 0.9f, 0.6f), new Color(2.6f, 2.9f, 1.4f), 0.5f, 0f);

        MatCity = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/Mat_Dark.mat");
        MatWindowLit = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/Mat_WindowLit.mat");
        MatWindowCool = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/Mat_WindowBlue.mat");
        MatRail = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/Mat_Concrete.mat");

        if (MatCity == null) MatCity = MatPrecision;
        if (MatWindowLit == null) MatWindowLit = MatGoal;
        if (MatWindowCool == null) MatWindowCool = MatLand;
        if (MatRail == null) MatRail = MatJump;
    }

    [MenuItem("Tools/Parkour/1 - Materials and Legacy Rename")]
    public static void Step1()
    {
        Begin();
        LoadPalette();

        // Explicit reuse map: legacy route pieces become the new course platforms.
        Rename("Route_Runway", "Plat_S1");
        Rename("Route_Step", "Plat_S2");
        Rename("Obstacle_Low_A", "Plat_S3");
        Rename("Obstacle_Low_B", "Plat_S4");
        Rename("Route_Jump_01", "Plat_S5");
        Rename("Route_Jump_02", "Plat_S6");
        Rename("Route_Jump_03", "Plat_S7");
        Rename("Route_UpperDeck", "Deck_North");
        Rename("Route_BalanceBeam", "Plat_B7_Beam");
        Rename("Shortcut_01", "Plat_B4");
        Rename("Shortcut_02", "Plat_B5");
        Rename("Shortcut_03", "Plat_B6");
        Rename("Hazard_Jump01", "Lip_S2_Land");
        Rename("Hazard_Jump02", "Lip_S3_Land");
        Rename("Hazard_Jump03", "Lip_S4_Land");
        Rename("UpperDeck_Rail_L", "Rail_DeckNorth_L");
        Rename("UpperDeck_Rail_R", "Rail_DeckNorth_R");
        Rename("Route_Ramp", "Roof_Slope_R02");
        Rename("Ramp_CenterGlow", "Roof_Slope_R02_Glow");
        Rename("Route_FinishDeck", "Roof_Helipad_L03");

        AssetDatabase.SaveAssets();
        End("Step 1 (materials + legacy rename)");
    }

    // ---------------------------------------------------------------- stage 1 + 2

    [MenuItem("Tools/Parkour/2 - Stage 1-2 (Lane A)")]
    public static void Step2()
    {
        Begin();
        LoadPalette();

        Transform g1 = Group("STAGE_1_Runway");
        Transform g2 = Group("STAGE_2_Stagger");
        Transform lips = Group("EDGE_LIPS");

        // --- Stage 1: wide, near-flat, walk-only. gaps 2.5-3.5, rises 0-0.4
        GameObject s0 = Slab("Platform_Start", g1, 0f, 14f, -8f, 8f, 0.50f, 1f, MatDeck);
        GameObject s1 = Slab("Plat_S1", g1, -4f, 8f, 10.5f, 17f, 0.50f, 1f, MatJump);
        GameObject s2 = Slab("Plat_S2", g1, -6f, 7f, 20f, 26f, 0.90f, 1f, MatJump);
        GameObject s3 = Slab("Plat_S3", g1, -6.5f, 6f, 29f, 35f, 1.30f, 1f, MatJump);
        GameObject s4 = Slab("Plat_S4", g1, -6.5f, 6f, 38.5f, 44f, 1.70f, 1f, MatJump);

        // --- Stage 2: narrows to 2.5-4 m, lateral weave, rises 0.5-0.7
        GameObject s5 = Slab("Plat_S5", g2, -9f, 4f, 48f, 53f, 2.20f, 1f, MatJump);
        GameObject s6 = Slab("Plat_S6", g2, -4f, 4f, 56.5f, 61f, 2.70f, 1f, MatJump);
        GameObject s7 = Slab("Plat_S7", g2, -9f, 3.5f, 64f, 68.5f, 3.30f, 1f, MatJump);
        GameObject s8 = Slab("Plat_S8_Pillar", g2, -5f, 2.5f, 71.5f, 74f, 4.00f, 0.6f, MatPrecision);
        GameObject dn = Slab("Deck_North", g2, 0f, 26f, 76f, 81.5f, 4.60f, 1f, MatDeck);

        // Rails on the safe deck only (absence of rail == danger signal)
        Box("Rail_DeckNorth_L", g2, new Vector3(-12.9f, 5.2f, 78.75f), new Vector3(0.2f, 1.2f, 5.5f), MatRail);
        Box("Rail_DeckNorth_R", g2, new Vector3(12.9f, 5.2f, 78.75f), new Vector3(0.2f, 1.2f, 5.5f), MatRail);

        // Start plaza dressing
        Box("Start_BackWall", g1, new Vector3(0f, 3.5f, -7.5f), new Vector3(14f, 6f, 1f), MatRail);
        Box("Start_LeftRail", g1, new Vector3(-6.9f, 1.25f, 0f), new Vector3(0.25f, 1.5f, 16f), MatRail);
        Box("Start_RightRail", g1, new Vector3(6.9f, 1.25f, 0f), new Vector3(0.25f, 1.5f, 16f), MatRail);
        Box("Start_CenterLine", g1, new Vector3(0f, 0.56f, 1f), new Vector3(0.12f, 0.12f, 14f), MatLand);

        // Orange = takeoff, cyan = landing. Applied consistently across the whole course.
        Box("Start_OrangeMark", g1, new Vector3(0f, 0.56f, 7.4f), new Vector3(10f, 0.12f, 0.18f), MatTakeoff);
        Lip("Lip_S1_Land", lips, s1, Edge.ZLo, MatLand);
        Lip("Lip_S1_Takeoff", lips, s1, Edge.ZHi, MatTakeoff);
        Lip("Lip_S2_Land", lips, s2, Edge.ZLo, MatLand);
        Lip("Lip_S2_Takeoff", lips, s2, Edge.ZHi, MatTakeoff);
        Lip("Lip_S3_Land", lips, s3, Edge.ZLo, MatLand);
        Lip("Lip_S3_Takeoff", lips, s3, Edge.ZHi, MatTakeoff);
        Lip("Lip_S4_Land", lips, s4, Edge.ZLo, MatLand);
        Lip("Lip_S4_Takeoff", lips, s4, Edge.ZHi, MatTakeoff);
        Lip("Lip_S5_Land", lips, s5, Edge.ZLo, MatLand);
        Lip("Lip_S5_Takeoff", lips, s5, Edge.ZHi, MatTakeoff);
        Lip("Lip_S6_Land", lips, s6, Edge.ZLo, MatLand);
        Lip("Lip_S6_Takeoff", lips, s6, Edge.ZHi, MatTakeoff);
        Lip("Lip_S7_Land", lips, s7, Edge.ZLo, MatLand);
        Lip("Lip_S7_Takeoff", lips, s7, Edge.ZHi, MatTakeoff);
        Lip("Lip_S8_Land", lips, s8, Edge.ZLo, MatLand);
        Lip("Lip_S8_Takeoff", lips, s8, Edge.ZHi, MatTakeoff);
        // Lane A arrives on Deck_North's south edge and Lane B departs from it, so one
        // shared marker serves both directions.
        Lip("Lip_DeckNorth_South", lips, dn, Edge.ZLo, MatLand);

        // Guide arrows: fade out the hand-holding after stage 1.
        // Must lie flat ON the deck (surface + half thickness). They were previously authored
        // 0.48 m too high, leaving waist-height slabs floating in the running line.
        Box("Guide_Arrow_01", g1, new Vector3(-4f, 0.53f, 14f), new Vector3(0.9f, 0.06f, 2.2f), MatLand);
        Box("Guide_Arrow_02", g1, new Vector3(-6f, 0.93f, 23f), new Vector3(0.9f, 0.06f, 2.2f), MatLand);

        End("Step 2 (Stage 1-2, Lane A)");
    }

    // ---------------------------------------------------------------- stage 3 + 4

    [MenuItem("Tools/Parkour/3 - Stage 3-4 (Lane B)")]
    public static void Step3()
    {
        Begin();
        LoadPalette();

        Transform g3 = Group("STAGE_3_Rhythm");
        Transform g4 = Group("STAGE_4_Precision");
        Transform lips = Group("EDGE_LIPS");

        // --- Stage 3: first sprint-mandated jumps. three identical 6 m gaps, forgiving 4.5 m pads
        GameObject b1 = Slab("Plat_B1", g3, 7f, 4.5f, 68f, 73f, 4.80f, 1f, MatJump);
        GameObject b2 = Slab("Plat_B2", g3, 7f, 4.5f, 57f, 62f, 5.20f, 1f, MatJump);
        GameObject b3 = Slab("Plat_B3", g3, 7f, 4.5f, 46f, 51f, 5.60f, 1f, MatJump);

        // --- Stage 4: small zig-zag pads, narrow beam, then stair-climb to skybridge height
        GameObject b4 = Slab("Plat_B4", g4, 5.5f, 2.4f, 40f, 42.4f, 6.30f, 0.6f, MatPrecision);
        GameObject b5 = Slab("Plat_B5", g4, 8.0f, 2.4f, 34f, 36.4f, 7.00f, 0.6f, MatPrecision);
        GameObject b6 = Slab("Plat_B6", g4, 5.5f, 2.4f, 28f, 30.4f, 7.70f, 0.6f, MatPrecision);
        GameObject b7 = Slab("Plat_B7_Beam", g4, 7f, 1.3f, 18f, 27f, 8.20f, 0.6f, MatPrecision);
        GameObject b8 = Slab("Plat_B8_Ledge", g4, 9f, 3f, 14f, 17f, 9.20f, 0.6f, MatPrecision);
        GameObject b9 = Slab("Plat_B9_Ledge", g4, 5.5f, 3f, 10f, 13f, 10.20f, 0.6f, MatPrecision);
        GameObject ds = Slab("Deck_South", g4, 8.5f, 9f, 1f, 9f, 11.00f, 1f, MatDeck);

        // Beam edge glow (repurposed Beam_Glow_L/R) - the only lighting cue on the beam
        Box("Beam_Glow_L", g4, new Vector3(6.41f, 8.25f, 22.5f), new Vector3(0.12f, 0.12f, 9f), MatLand);
        Box("Beam_Glow_R", g4, new Vector3(7.59f, 8.25f, 22.5f), new Vector3(0.12f, 0.12f, 9f), MatLand);

        // Lane B travels -Z, so takeoff is the ZLo edge and landing the ZHi edge.
        Lip("Lip_B1_Land", lips, b1, Edge.ZHi, MatLand);
        Lip("Lip_B1_Takeoff", lips, b1, Edge.ZLo, MatTakeoff);
        Lip("Lip_B2_Land", lips, b2, Edge.ZHi, MatLand);
        Lip("Lip_B2_Takeoff", lips, b2, Edge.ZLo, MatTakeoff);
        Lip("Lip_B3_Land", lips, b3, Edge.ZHi, MatLand);
        Lip("Lip_B3_Takeoff", lips, b3, Edge.ZLo, MatTakeoff);
        Lip("Lip_B4_Land", lips, b4, Edge.ZHi, MatLand);
        Lip("Lip_B4_Takeoff", lips, b4, Edge.ZLo, MatTakeoff);
        Lip("Lip_B5_Land", lips, b5, Edge.ZHi, MatLand);
        Lip("Lip_B5_Takeoff", lips, b5, Edge.ZLo, MatTakeoff);
        Lip("Lip_B6_Land", lips, b6, Edge.ZHi, MatLand);
        Lip("Lip_B6_Takeoff", lips, b6, Edge.ZLo, MatTakeoff);
        Lip("Lip_B7_Land", lips, b7, Edge.ZHi, MatLand);
        Lip("Lip_B7_Takeoff", lips, b7, Edge.ZLo, MatTakeoff);
        Lip("Lip_B8_Land", lips, b8, Edge.ZHi, MatLand);
        Lip("Lip_B8_Takeoff", lips, b8, Edge.ZLo, MatTakeoff);
        Lip("Lip_B9_Land", lips, b9, Edge.ZHi, MatLand);
        Lip("Lip_B9_Takeoff", lips, b9, Edge.ZLo, MatTakeoff);
        // Lane B arrives on Deck_South's north edge and Stage 5 departs from it.
        Lip("Lip_DeckSouth_North", lips, ds, Edge.ZHi, MatLand);

        End("Step 3 (Stage 3-4, Lane B)");
    }

    // ---------------------------------------------------------------- stage 5

    [MenuItem("Tools/Parkour/4 - Stage 5 (Skybridge)")]
    public static void Step4()
    {
        Begin();
        LoadPalette();

        Transform g5 = Group("STAGE_5_Skybridge");
        Transform lips = Group("EDGE_LIPS");

        // Narrow central spine over the whole completed course. Repeated 6-6.5 m sprint gaps.
        GameObject k1 = Slab("Plat_K1", g5, 0f, 3.0f, 13f, 19f, 11.20f, 0.6f, MatPrecision);
        GameObject k2 = Slab("Plat_K2", g5, 0f, 2.6f, 25f, 30f, 11.60f, 0.6f, MatPrecision);
        GameObject k3 = Slab("Plat_K3", g5, 0f, 2.6f, 36f, 41f, 12.00f, 0.6f, MatPrecision);
        GameObject k4 = Slab("Plat_K4", g5, 0f, 2.2f, 47.5f, 52f, 12.60f, 0.6f, MatPrecision);
        GameObject k5 = Slab("Plat_K5", g5, 0f, 2.0f, 58f, 62f, 13.20f, 0.6f, MatPrecision);
        GameObject k6 = Slab("Plat_K6", g5, 0f, 2.0f, 68f, 72f, 13.80f, 0.6f, MatPrecision);
        GameObject k7 = Slab("Plat_K7_TowerShelf", g5, 0f, 10f, 76f, 82f, 14.40f, 1f, MatDeck);

        Lip("Lip_K1_Land", lips, k1, Edge.ZLo, MatLand);
        Lip("Lip_K1_Takeoff", lips, k1, Edge.ZHi, MatTakeoff);
        Lip("Lip_K2_Land", lips, k2, Edge.ZLo, MatLand);
        Lip("Lip_K2_Takeoff", lips, k2, Edge.ZHi, MatTakeoff);
        Lip("Lip_K3_Land", lips, k3, Edge.ZLo, MatLand);
        Lip("Lip_K3_Takeoff", lips, k3, Edge.ZHi, MatTakeoff);
        Lip("Lip_K4_Land", lips, k4, Edge.ZLo, MatLand);
        Lip("Lip_K4_Takeoff", lips, k4, Edge.ZHi, MatTakeoff);
        Lip("Lip_K5_Land", lips, k5, Edge.ZLo, MatLand);
        Lip("Lip_K5_Takeoff", lips, k5, Edge.ZHi, MatTakeoff);
        Lip("Lip_K6_Land", lips, k6, Edge.ZLo, MatLand);
        Lip("Lip_K6_Takeoff", lips, k6, Edge.ZHi, MatTakeoff);
        Lip("Lip_K7_Land", lips, k7, Edge.ZLo, MatLand);
        Lip("Lip_K7_Takeoff", lips, k7, Edge.XLo, MatTakeoff);

        End("Step 4 (Stage 5, Skybridge)");
    }

    // ---------------------------------------------------------------- stage 6

    [MenuItem("Tools/Parkour/5 - Stage 6 (Tower Ascent)")]
    public static void Step5()
    {
        Begin();
        LoadPalette();

        Transform g6 = Group("STAGE_6_Tower");
        Transform lips = Group("EDGE_LIPS");

        // Tower shortened 36 -> 22 m so the spiral is 8 ledges rather than 20.
        // Footprint x -9..9, z 82..100, walkable roof at y 22.
        Box("Finish_TowerBase", g6, new Vector3(0f, 11f, 91f), new Vector3(18f, 22f, 18f), MatCity);
        // Cap is inset 0.2 m from the base sides: avoids coplanar z-fighting and, critically,
        // means it never overhangs a spiral ledge at head height.
        GameObject roof = Box("Finish_TowerCap", g6, new Vector3(0f, 21.75f, 91f), new Vector3(17.6f, 0.5f, 17.6f), MatDeck);

        // West face ledges (abut the wall at x = -9), +1.0 m per step
        GameObject w1 = Slab("Ledge_W1", g6, -10.3f, 2.6f, 82f, 87f, 15.40f, 0.5f, MatPrecision);
        GameObject w2 = Slab("Ledge_W2", g6, -10.3f, 2.6f, 89f, 94f, 16.40f, 0.5f, MatPrecision);
        GameObject w3 = Slab("Ledge_W3", g6, -10.3f, 2.6f, 96f, 100f, 17.40f, 0.5f, MatPrecision);

        // North face ledges: span X, 2.6 m deep, at z 101.8..104.4.
        // N1 reaches west to x -11.6 and N3 east to x 11.6 so they overlap the west/east ledges in
        // X. Without that overlap the corner transitions demand blind two-axis air steering around
        // a solid wall (a straight jump north off W3 at x -10.3 would land in the void).
        // They also stand 1.8 m clear of the tower's north face rather than abutting it. Abutting
        // made the W3->N1 corner unclearable: with a zero gap and a 1.0 m rise the player reaches
        // N1's side face at y 18.09, still 0.31 m below its surface, and has to climb it with no
        // horizontal run-up. A real gap gives the room needed to gain that height first.
        const float nz = 103.1f;
        GameObject n1 = SlabX("Ledge_N1", g6, -11.6f, -4f, nz, 2.6f, 18.40f, 0.5f, MatPrecision);
        GameObject n2 = SlabX("Ledge_N2", g6, -2f, 3f, nz, 2.6f, 19.40f, 0.5f, MatPrecision);
        GameObject n3 = SlabX("Ledge_N3", g6, 5f, 11.6f, nz, 2.6f, 20.40f, 0.5f, MatPrecision);

        // East face ledge, abutting the wall at x = 9
        GameObject e1 = Slab("Ledge_E1", g6, 10.3f, 2.6f, 95f, 100f, 21.20f, 0.5f, MatPrecision);

        // Goal dressing on the roof (roof surface is y 22)
        Box("Finish_GlowPad", g6, new Vector3(0f, 22.06f, 91f), new Vector3(6f, 0.12f, 5f), MatGoal);
        Post("Finish_Beacon", g6, new Vector3(0f, 23.5f, 95f), new Vector3(0.5f, 1.5f, 0.5f), MatGoal);
        // Roof top 22.00 -> head 24.00, so the finish banner sits at 24.40 to stay walkable-under.
        Post("Finish_Left", g6, new Vector3(-4.5f, 23.2f, 91f), new Vector3(0.28f, 1.2f, 0.28f), MatRail);
        Post("Finish_Right", g6, new Vector3(4.5f, 23.2f, 91f), new Vector3(0.28f, 1.2f, 0.28f), MatRail);
        Box("Finish_Top", g6, new Vector3(0f, 24.4f, 91f), new Vector3(9.4f, 0.35f, 0.35f), MatTakeoff);

        Lip("Lip_W1_Land", lips, w1, Edge.XHi, MatLand);
        Lip("Lip_W2_Land", lips, w2, Edge.ZLo, MatLand);
        Lip("Lip_W3_Land", lips, w3, Edge.ZLo, MatLand);
        Lip("Lip_N1_Land", lips, n1, Edge.XLo, MatLand);
        Lip("Lip_N2_Land", lips, n2, Edge.XLo, MatLand);
        Lip("Lip_N3_Land", lips, n3, Edge.XLo, MatLand);
        Lip("Lip_E1_Land", lips, e1, Edge.ZHi, MatLand);
        Lip("Lip_Roof_Land", lips, roof, Edge.XHi, MatLand);

        End("Step 5 (Stage 6, Tower Ascent)");
    }

    // ---------------------------------------------------------------- environment

    private struct Building
    {
        public string Name;
        public Vector3 Pos;
        public Vector3 Scale;
        public Building(string n, Vector3 p, Vector3 s) { Name = n; Pos = p; Scale = s; }
        public float Top => Pos.y + Scale.y * 0.5f;
        public float InwardSign => Pos.x < 0f ? 1f : -1f;
        public float InwardFaceX => Pos.x + InwardSign * (Scale.x * 0.5f);
    }

    [MenuItem("Tools/Parkour/6 - Environment and Lighting")]
    public static void Step6()
    {
        Begin();
        LoadPalette();

        Transform env = Group("ENVIRONMENT");
        Transform details = Group("ENV_DETAILS");

        // Decorative street plane. Sits below fallResetHeight (-12) so it is never landed on.
        Box("Ground", env, new Vector3(0f, -18f, 46f), new Vector3(100f, 1f, 130f), MatCity);
        // Far backdrop, 40 m beyond the tower and 40 m below it: unreachable by design.
        Box("Horizon_Block", env, new Vector3(0f, -20f, 150f), new Vector3(200f, 30f, 20f), MatCity);

        // Three depth bands, all outside the play corridor (|x| >= 16, corridor is x -13..13.1)
        Building[] buildings =
        {
            new Building("City_L01", new Vector3(-21f, 8f, 10f), new Vector3(10f, 16f, 14f)),
            new Building("City_R01", new Vector3(21f, 10f, 14f), new Vector3(10f, 20f, 14f)),
            new Building("City_L02", new Vector3(-22f, 13f, 38f), new Vector3(12f, 26f, 16f)),
            new Building("City_R02", new Vector3(22f, 15f, 42f), new Vector3(12f, 30f, 16f)),
            new Building("City_L03", new Vector3(-20f, 9f, 66f), new Vector3(10f, 18f, 14f)),
            new Building("City_R03", new Vector3(20f, 11f, 70f), new Vector3(10f, 22f, 14f)),
            new Building("City_L04", new Vector3(-24f, 16f, 92f), new Vector3(14f, 32f, 18f)),
            new Building("City_R04", new Vector3(24f, 19f, 96f), new Vector3(14f, 38f, 18f)),
            new Building("City_Far_L", new Vector3(-38f, 22f, 50f), new Vector3(18f, 44f, 20f)),
            new Building("City_Far_R", new Vector3(40f, 25f, 60f), new Vector3(20f, 50f, 20f)),
            new Building("City_Back_L", new Vector3(-16f, 20f, 122f), new Vector3(24f, 40f, 22f)),
            new Building("City_Back_R", new Vector3(20f, 27f, 126f), new Vector3(22f, 54f, 22f))
        };

        foreach (Building b in buildings)
        {
            Box(b.Name, env, b.Pos, b.Scale, MatCity);
        }

        // Roof glow strips on four of the near-band towers
        string[] glowTargets = { "City_L01", "City_R01", "City_L04", "City_R04" };
        string[] glowNames = { "RoofGlow_L01", "RoofGlow_R01", "RoofGlow_L03", "RoofGlow_R04" };
        for (int i = 0; i < glowTargets.Length; i++)
        {
            Building b = System.Array.Find(buildings, x => x.Name == glowTargets[i]);
            Box(glowNames[i], details, new Vector3(b.Pos.x, b.Top + 0.15f, b.Pos.z),
                new Vector3(b.Scale.x + 0.4f, 0.3f, b.Scale.z + 0.4f), MatWindowCool);
        }

        // Redistribute the existing emissive window panels across the inward-facing walls.
        List<string> windowPool = new List<string>();
        foreach (KeyValuePair<string, GameObject> kv in index)
        {
            if (kv.Key.StartsWith("Windows_"))
            {
                windowPool.Add(kv.Key);
            }
        }

        windowPool.Sort();
        int w = 0;
        foreach (Building b in buildings)
        {
            int rows = Mathf.Clamp(Mathf.FloorToInt(b.Scale.y / 9f), 1, 4);
            for (int r = 0; r < rows && w < windowPool.Count; r++)
            {
                float y = b.Pos.y - b.Scale.y * 0.35f + r * (b.Scale.y * 0.7f / Mathf.Max(1, rows - 1 + (rows == 1 ? 1 : 0)));
                for (int c = 0; c < 2 && w < windowPool.Count; c++)
                {
                    float z = b.Pos.z + (c == 0 ? -1f : 1f) * b.Scale.z * 0.22f;
                    Material m = ((r + c) % 3 == 0) ? MatWindowCool : MatWindowLit;
                    Box(windowPool[w], details,
                        new Vector3(b.InwardFaceX + b.InwardSign * 0.07f, y, z),
                        new Vector3(0.12f, Mathf.Min(2.4f, b.Scale.y * 0.18f), b.Scale.z * 0.3f), m);
                    w++;
                }
            }
        }

        // Any leftover panels dress the tower's south face (the wall you climb past).
        int leftover = 0;
        while (w < windowPool.Count && leftover < 12)
        {
            int row = leftover / 3;
            int col = leftover % 3;
            Box(windowPool[w], details,
                new Vector3(-5f + col * 5f, 5f + row * 4.5f, 81.9f),
                new Vector3(3.2f, 2.2f, 0.12f), (leftover % 3 == 0) ? MatWindowCool : MatWindowLit);
            w++;
            leftover++;
        }

        // Remaining panels are parked far off-camera rather than left intersecting the course.
        while (w < windowPool.Count)
        {
            Box(windowPool[w], details, new Vector3(-60f, 6f + (w % 6) * 3f, 40f + (w % 4) * 8f),
                new Vector3(0.12f, 2.2f, 4f), MatWindowLit);
            w++;
        }

        // Roof plant on four building tops
        string[] hvacTargets = { "City_L02", "City_R02", "City_L03", "City_R03", "City_Far_L", "City_Far_R" };
        string[] hvacNames = { "HVAC_Upper_A", "HVAC_Upper_B", "HVAC_Finish_A", "HVAC_Finish_B", "HVAC_Upper_Vent", "HVAC_Finish_Vent" };
        for (int i = 0; i < hvacNames.Length; i++)
        {
            Building b = System.Array.Find(buildings, x => x.Name == hvacTargets[i]);
            Box(hvacNames[i], details, new Vector3(b.Pos.x + 2f, b.Top + 0.9f, b.Pos.z - 2f),
                new Vector3(3.2f, 1.8f, 2.6f), MatCity);
        }

        // Vertical accent strips
        Box("Accent_R01_Vertical", details, new Vector3(15.9f, 10f, 14f), new Vector3(0.14f, 18f, 0.6f), MatWindowCool);
        Box("Accent_L02_Vertical", details, new Vector3(-15.9f, 13f, 38f), new Vector3(0.14f, 24f, 0.6f), MatWindowCool);
        // Stops at y 13, below the K7 tower shelf (y 13.4..14.4). At its original full height it
        // pierced the shelf and left a collider standing in the middle of the walkway.
        Box("Accent_FinishTower", details, new Vector3(0f, 7f, 81.9f), new Vector3(0.6f, 12f, 0.14f), MatTakeoff);

        // Repurposed Route_FinishDeck. In the old layout this sat at z 62..72 / y 9.65, which is
        // directly beneath the Stage 5 skybridge: it would have caught a missed K5->K6 jump and
        // stranded the player on an unreachable slab. Moved out of the corridor as a roof helipad.
        Box("Roof_Helipad_L03", details, new Vector3(-20f, 18.4f, 66f), new Vector3(10f, 0.8f, 12f), MatDeck);

        // Sloped roof panel (repurposed Route_Ramp) for skyline variety
        GameObject slope = Prim("Roof_Slope_R02", details, PrimitiveType.Cube);
        slope.transform.localPosition = new Vector3(22f, 30.6f, 42f);
        slope.transform.localRotation = Quaternion.Euler(-16f, 0f, 0f);
        slope.transform.localScale = new Vector3(12f, 0.5f, 16f);
        Paint(slope, MatCity);
        GameObject slopeGlow = Prim("Roof_Slope_R02_Glow", details, PrimitiveType.Cube);
        slopeGlow.transform.localPosition = new Vector3(22f, 30.95f, 42f);
        slopeGlow.transform.localRotation = Quaternion.Euler(-16f, 0f, 0f);
        slopeGlow.transform.localScale = new Vector3(0.2f, 0.14f, 15f);
        Paint(slopeGlow, MatWindowCool);

        // Landmarks: crane silhouette above Deck_North, antenna above Deck_South
        Box("Crane_Mast", details, new Vector3(-24f, 43f, 92f), new Vector3(0.5f, 22f, 0.5f), MatCity);
        Box("Crane_Boom", details, new Vector3(-17f, 53f, 92f), new Vector3(16f, 0.45f, 0.45f), MatCity);
        Box("Crane_Counter", details, new Vector3(-29f, 53f, 92f), new Vector3(7f, 0.45f, 0.45f), MatCity);
        Box("Crane_Cable", details, new Vector3(-12f, 47f, 92f), new Vector3(0.12f, 12f, 0.12f), MatCity);
        Box("Crane_Hook", details, new Vector3(-12f, 40.5f, 92f), new Vector3(0.8f, 0.8f, 0.8f), MatTakeoff);
        Box("Antenna_Mast", details, new Vector3(21f, 27f, 14f), new Vector3(0.35f, 14f, 0.35f), MatCity);
        Box("Antenna_Arm", details, new Vector3(23.5f, 32f, 14f), new Vector3(5f, 0.3f, 0.3f), MatCity);
        Box("Antenna_Lamp", details, new Vector3(26f, 32f, 14f), new Vector3(0.7f, 0.7f, 0.7f), MatTakeoff);

        // Lamp panels are mounted flush on the inward building walls (|x| > 13, i.e. outside the
        // play corridor) so they read as wall fixtures instead of slabs floating in mid-air.
        Box("LampPanel_Left", details, new Vector3(-15.9f, 8f, 44f), new Vector3(0.16f, 1.6f, 3.2f), MatWindowLit);
        Box("LampPanel_Right", details, new Vector3(15.9f, 11f, 46f), new Vector3(0.16f, 1.6f, 3.2f), MatWindowLit);
        Box("LampPanel_Upper", details, new Vector3(-14.9f, 14f, 64f), new Vector3(0.16f, 1.6f, 3.2f), MatWindowLit);

        BuildLighting();
        BuildPostFX();
        End("Step 6 (environment + lighting + post)");
    }

    private static void BuildLighting()
    {
        // Low warm key light rakes across the corridor: long shadows make the fold-back read.
        GameObject sunGo = Find("Directional Light");
        if (sunGo != null)
        {
            Undo.RecordObject(sunGo.transform, "Aim sun");
            sunGo.transform.localPosition = new Vector3(0f, 30f, 20f);
            sunGo.transform.localRotation = Quaternion.Euler(18f, 205f, 0f);
            Light sun = sunGo.GetComponent<Light>();
            if (sun != null)
            {
                Undo.RecordObject(sun, "Tune sun");
                sun.color = new Color(1f, 0.87f, 0.71f);
                sun.intensity = 1.6f;
                sun.shadows = LightShadows.Soft;
            }
        }

        PointLight("Light_CP1", new Vector3(0f, 8f, 79f), new Color(0.55f, 0.9f, 1f), 26f, 24f);
        PointLight("Light_CP2", new Vector3(8.5f, 14.5f, 5f), new Color(0.55f, 0.9f, 1f), 26f, 24f);
        PointLight("Light_Finish", new Vector3(0f, 26f, 91f), new Color(1f, 0.95f, 0.7f), 34f, 40f);
        PointLight("PracticalLight_Left", new Vector3(-9f, 7f, 58f), new Color(1f, 0.8f, 0.55f), 18f, 12f);
        PointLight("PracticalLight_Right", new Vector3(9f, 10f, 44f), new Color(1f, 0.8f, 0.55f), 18f, 12f);
        PointLight("PracticalLight_Upper", new Vector3(0f, 16f, 49.75f), new Color(0.7f, 0.9f, 1f), 22f, 16f);

        // Depth fog: without it a 500 m far plane over grey blocks reads flat.
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.33f, 0.40f, 0.52f);
        RenderSettings.fogStartDistance = 55f;
        RenderSettings.fogEndDistance = 320f;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.38f, 0.45f, 0.58f);
        RenderSettings.ambientEquatorColor = new Color(0.28f, 0.30f, 0.36f);
        RenderSettings.ambientGroundColor = new Color(0.12f, 0.12f, 0.15f);
    }

    /// <summary>
    /// Populates the Daylight_PostFX volume profile. It shipped with three null component
    /// references and no actual overrides, so the emissive edge language rendered with no glow.
    /// </summary>
    private static void BuildPostFX()
    {
        const string profilePath = "Assets/UIWorldDemo/Profiles/Daylight_PostFX.asset";
        UnityEngine.Rendering.VolumeProfile profile =
            AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(profilePath);
        if (profile == null)
        {
            Debug.LogWarning($"[ParkourBuilder] Volume profile not found at {profilePath}; skipping post FX.");
            return;
        }

        // Purge the broken null entries and any stale override sub-assets, then rebuild.
        // VolumeProfile.Add<T> alone leaves the component un-serialised (it writes fileID: 0),
        // so each override must also be registered as a sub-asset of the profile.
        int removed = profile.components.RemoveAll(c => c == null);
        foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(profilePath))
        {
            if (sub is UnityEngine.Rendering.VolumeComponent stale)
            {
                profile.components.Remove(stale);
                Object.DestroyImmediate(stale, true);
            }
        }

        if (removed > 0)
        {
            Debug.Log($"[ParkourBuilder] Removed {removed} null override(s) from Daylight_PostFX.");
        }

        // Bloom is what makes the orange/cyan edge strips read as light rather than paint.
        UnityEngine.Rendering.Universal.Bloom bloom =
            AddOverride<UnityEngine.Rendering.Universal.Bloom>(profile);
        bloom.active = true;
        bloom.threshold.overrideState = true;
        bloom.threshold.value = 0.85f;
        bloom.intensity.overrideState = true;
        bloom.intensity.value = 1.05f;
        bloom.scatter.overrideState = true;
        bloom.scatter.value = 0.68f;

        UnityEngine.Rendering.Universal.Tonemapping tone =
            AddOverride<UnityEngine.Rendering.Universal.Tonemapping>(profile);
        tone.active = true;
        tone.mode.overrideState = true;
        tone.mode.value = UnityEngine.Rendering.Universal.TonemappingMode.ACES;

        UnityEngine.Rendering.Universal.ColorAdjustments color =
            AddOverride<UnityEngine.Rendering.Universal.ColorAdjustments>(profile);
        color.active = true;
        color.postExposure.overrideState = true;
        color.postExposure.value = 0.15f;
        color.contrast.overrideState = true;
        color.contrast.value = 12f;
        color.saturation.overrideState = true;
        color.saturation.value = 8f;

        // Vignette sells the height on the exposed sections.
        UnityEngine.Rendering.Universal.Vignette vignette =
            AddOverride<UnityEngine.Rendering.Universal.Vignette>(profile);
        vignette.active = true;
        vignette.intensity.overrideState = true;
        vignette.intensity.value = 0.3f;
        vignette.smoothness.overrideState = true;
        vignette.smoothness.value = 0.36f;

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(profilePath, ImportAssetOptions.ForceUpdate);
        Debug.Log($"[ParkourBuilder] Daylight_PostFX rebuilt with {profile.components.Count} overrides.");
    }

    /// <summary>
    /// Creates a volume override and registers it as a sub-asset so it actually serialises.
    /// </summary>
    private static T AddOverride<T>(UnityEngine.Rendering.VolumeProfile profile)
        where T : UnityEngine.Rendering.VolumeComponent
    {
        T component = ScriptableObject.CreateInstance<T>();
        component.name = typeof(T).Name;
        component.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;
        profile.components.Add(component);
        AssetDatabase.AddObjectToAsset(component, profile);
        return component;
    }

    private static void PointLight(string name, Vector3 pos, Color color, float range, float intensity)
    {
        GameObject go = Find(name);
        Transform parent = Group("ENV_LIGHTS");
        if (go == null)
        {
            go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create light");
            index[name] = go;
        }

        go.transform.SetParent(parent, false);
        Undo.RecordObject(go.transform, "Move light");
        go.transform.localPosition = pos;

        Light l = go.GetComponent<Light>();
        if (l == null)
        {
            l = go.AddComponent<Light>();
        }

        Undo.RecordObject(l, "Tune light");
        l.type = LightType.Point;
        l.color = color;
        l.range = range;
        l.intensity = intensity;
        l.shadows = LightShadows.None;
    }

    // ---------------------------------------------------------------- checkpoints

    [MenuItem("Tools/Parkour/7 - Checkpoints")]
    public static void Step7()
    {
        Begin();

        Transform parent = Group("CHECKPOINTS");
        LoadPalette();

        // Deck_North top 4.6 -> respawn feet at 4.65 (mirrors the original 0.5 -> 0.55 offset)
        MakeCheckpoint("Checkpoint_DeckNorth", parent, "Deck North",
            new Vector3(0f, 5.8f, 78.75f), new Vector3(24f, 2.4f, 5f),
            new Vector3(0f, 4.65f, 78.75f));

        // Deck_South top 11.0 -> respawn feet at 11.05
        MakeCheckpoint("Checkpoint_DeckSouth", parent, "Deck South",
            new Vector3(8.5f, 12.2f, 5f), new Vector3(8f, 2.4f, 8f),
            new Vector3(8.5f, 11.05f, 5f));

        // Visual gates marking the two checkpoints.
        // The crossbar must clear the player's head: feet + height 2.0 + margin. Authored lower it
        // sat inside the capsule and physically blocked the player from walking through the gate.
        // Deck_North top 4.60 -> head 6.60, bar at 7.00. Posts span deck to bar.
        Post("CP1_Left", parent, new Vector3(-4f, 5.8f, 78.75f), new Vector3(0.18f, 1.2f, 0.18f), MatRail);
        Post("CP1_Right", parent, new Vector3(4f, 5.8f, 78.75f), new Vector3(0.18f, 1.2f, 0.18f), MatRail);
        Box("CP1_Top", parent, new Vector3(0f, 7.0f, 78.75f), new Vector3(8.2f, 0.22f, 0.22f), MatLand);
        // Deck_South top 11.00 -> head 13.00, bar at 13.40.
        Post("CP2_Left", parent, new Vector3(5f, 12.2f, 5f), new Vector3(0.18f, 1.2f, 0.18f), MatRail);
        Post("CP2_Right", parent, new Vector3(12f, 12.2f, 5f), new Vector3(0.18f, 1.2f, 0.18f), MatRail);
        Box("CP2_Top", parent, new Vector3(8.5f, 13.4f, 5f), new Vector3(7.2f, 0.22f, 0.22f), MatLand);

        End("Step 7 (checkpoints)");
    }

    private static void MakeCheckpoint(string name, Transform parent, string label,
        Vector3 center, Vector3 size, Vector3 respawn)
    {
        GameObject go = Find(name);
        if (go == null)
        {
            go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create checkpoint");
            index[name] = go;
        }

        go.transform.SetParent(parent, false);
        Undo.RecordObject(go.transform, "Move checkpoint");
        go.transform.localPosition = center;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        BoxCollider box = go.GetComponent<BoxCollider>();
        if (box == null)
        {
            box = go.AddComponent<BoxCollider>();
        }

        Undo.RecordObject(box, "Configure checkpoint collider");
        box.isTrigger = true;
        box.center = Vector3.zero;
        box.size = size;

        string pointName = name + "_Respawn";
        GameObject point = Find(pointName);
        if (point == null)
        {
            point = new GameObject(pointName);
            Undo.RegisterCreatedObjectUndo(point, "Create respawn point");
            index[pointName] = point;
        }

        point.transform.SetParent(go.transform, true);
        point.transform.position = respawn;
        point.transform.localRotation = Quaternion.identity;
        point.transform.localScale = Vector3.one;

        CheckpointVolume volume = go.GetComponent<CheckpointVolume>();
        if (volume == null)
        {
            volume = go.AddComponent<CheckpointVolume>();
        }

        SerializedObject so = new SerializedObject(volume);
        so.FindProperty("respawnPoint").objectReferenceValue = point.transform;
        so.FindProperty("checkpointName").stringValue = label;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ---------------------------------------------------------------- validation

    private struct Jump
    {
        public string From;
        public string To;
        public string Speed; // "walk" or "sprint"
        public Jump(string f, string t, string s) { From = f; To = t; Speed = s; }
    }

    [MenuItem("Tools/Parkour/8 - Validate Reachability")]
    public static void Step8()
    {
        Begin();

        BasicFirstPersonController controller = null;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            controller = root.GetComponentInChildren<BasicFirstPersonController>(true);
            if (controller != null)
            {
                break;
            }
        }

        if (controller == null)
        {
            Debug.LogError("[Validate] No BasicFirstPersonController in scene.");
            return;
        }

        SerializedObject so = new SerializedObject(controller);
        float walk = so.FindProperty("walkSpeed").floatValue;
        float sprint = so.FindProperty("sprintSpeed").floatValue;
        float jumpHeight = so.FindProperty("jumpHeight").floatValue;
        float gravity = so.FindProperty("gravity").floatValue;
        float launch = Mathf.Sqrt(jumpHeight * -2f * gravity);
        float g = -gravity;

        Debug.Log($"[Validate] walk={walk} sprint={sprint} jumpHeight={jumpHeight} gravity={gravity} launch={launch:F3} m/s");

        Jump[] jumps =
        {
            new Jump("Platform_Start", "Plat_S1", "walk"),
            new Jump("Plat_S1", "Plat_S2", "walk"),
            new Jump("Plat_S2", "Plat_S3", "walk"),
            new Jump("Plat_S3", "Plat_S4", "walk"),
            new Jump("Plat_S4", "Plat_S5", "walk"),
            new Jump("Plat_S5", "Plat_S6", "walk"),
            new Jump("Plat_S6", "Plat_S7", "walk"),
            new Jump("Plat_S7", "Plat_S8_Pillar", "walk"),
            new Jump("Plat_S8_Pillar", "Deck_North", "walk"),
            new Jump("Deck_North", "Plat_B1", "walk"),
            new Jump("Plat_B1", "Plat_B2", "sprint"),
            new Jump("Plat_B2", "Plat_B3", "sprint"),
            new Jump("Plat_B3", "Plat_B4", "walk"),
            new Jump("Plat_B4", "Plat_B5", "walk"),
            new Jump("Plat_B5", "Plat_B6", "walk"),
            new Jump("Plat_B6", "Plat_B7_Beam", "walk"),
            new Jump("Plat_B7_Beam", "Plat_B8_Ledge", "walk"),
            new Jump("Plat_B8_Ledge", "Plat_B9_Ledge", "walk"),
            new Jump("Plat_B9_Ledge", "Deck_South", "walk"),
            new Jump("Deck_South", "Plat_K1", "walk"),
            new Jump("Plat_K1", "Plat_K2", "sprint"),
            new Jump("Plat_K2", "Plat_K3", "sprint"),
            new Jump("Plat_K3", "Plat_K4", "sprint"),
            new Jump("Plat_K4", "Plat_K5", "sprint"),
            new Jump("Plat_K5", "Plat_K6", "sprint"),
            new Jump("Plat_K6", "Plat_K7_TowerShelf", "walk"),
            new Jump("Plat_K7_TowerShelf", "Ledge_W1", "sprint"),
            new Jump("Ledge_W1", "Ledge_W2", "walk"),
            new Jump("Ledge_W2", "Ledge_W3", "walk"),
            new Jump("Ledge_W3", "Ledge_N1", "walk"),
            new Jump("Ledge_N1", "Ledge_N2", "walk"),
            new Jump("Ledge_N2", "Ledge_N3", "walk"),
            new Jump("Ledge_N3", "Ledge_E1", "walk"),
            new Jump("Ledge_E1", "Finish_TowerCap", "walk")
        };

        int fail = 0;
        int warn = 0;
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine("jump                                    rise   gap   reach  slack  speed   verdict");

        foreach (Jump j in jumps)
        {
            GameObject a = Find(j.From);
            GameObject b = Find(j.To);
            if (a == null || b == null)
            {
                sb.AppendLine($"{j.From} -> {j.To}: MISSING GEOMETRY");
                fail++;
                continue;
            }

            Bounds ba = a.GetComponent<Renderer>().bounds;
            Bounds bb = b.GetComponent<Renderer>().bounds;

            float rise = bb.max.y - ba.max.y;
            float dx = Mathf.Max(0f, Mathf.Max(ba.min.x - bb.max.x, bb.min.x - ba.max.x));
            float dz = Mathf.Max(0f, Mathf.Max(ba.min.z - bb.max.z, bb.min.z - ba.max.z));
            float gap = Mathf.Sqrt(dx * dx + dz * dz);

            float speed = j.Speed == "sprint" ? sprint : walk;

            string verdict;
            float reach;
            float slack;

            if (rise >= jumpHeight)
            {
                reach = 0f;
                slack = -999f;
                verdict = "FAIL rise>=jumpHeight";
                fail++;
            }
            else if (rise <= 0.30f && gap <= 0.01f)
            {
                reach = 0f;
                slack = 999f;
                verdict = "OK walk-up (stepOffset)";
            }
            else
            {
                // time to fall back to the landing height, then usable horizontal distance
                float disc = launch * launch - 2f * g * rise;
                float t = (launch + Mathf.Sqrt(Mathf.Max(0f, disc))) / g;
                reach = speed * t;
                slack = reach - Footing - gap;

                if (slack < 0f) { verdict = "FAIL unreachable"; fail++; }
                else if (slack < 0.75f) { verdict = "WARN tight"; warn++; }
                else { verdict = "OK"; }
            }

            sb.AppendLine($"{j.From,-22} -> {j.To,-20} {rise,5:F2} {gap,5:F2} {reach,6:F2} {slack,6:F2}  {j.Speed,-6} {verdict}");
        }

        sb.AppendLine($"--- {jumps.Length} jumps checked: {fail} fail, {warn} tight ---");

        // Orphan guard: every WORLD child should be one of the organising group nodes. A leftover
        // mesh parked directly under WORLD means a legacy object was never repositioned.
        sb.AppendLine();
        sb.AppendLine("orphan check (WORLD children that still carry geometry):");
        GameObject worldGo = Find("WORLD");
        int orphans = 0;
        if (worldGo != null)
        {
            foreach (Transform child in worldGo.transform)
            {
                if (child.GetComponent<Renderer>() != null || child.GetComponent<Collider>() != null)
                {
                    sb.AppendLine($"  ORPHAN {child.name} at {child.localPosition}");
                    orphans++;
                }
            }
        }

        sb.AppendLine(orphans == 0 ? "  none - all geometry is grouped" : $"  {orphans} orphan(s)");
        fail += orphans;

        // Intrusion guard: nothing outside the course groups may sit inside the play corridor
        // (x -13..13, y 0..24, z -9..104) where it could catch a fall or block a jump.
        sb.AppendLine();
        sb.AppendLine("corridor intrusion check (non-course geometry inside the play volume):");
        Bounds corridor = new Bounds();
        corridor.SetMinMax(new Vector3(-13f, 0f, -9f), new Vector3(13f, 24f, 104f));
        string[] courseGroups =
        {
            "STAGE_1_Runway", "STAGE_2_Stagger", "STAGE_3_Rhythm", "STAGE_4_Precision",
            "STAGE_5_Skybridge", "STAGE_6_Tower", "EDGE_LIPS", "CHECKPOINTS"
        };

        int intrusions = 0;
        foreach (KeyValuePair<string, GameObject> kv in index)
        {
            GameObject go = kv.Value;
            if (go == null)
            {
                continue;
            }

            Renderer r = go.GetComponent<Renderer>();
            if (r == null)
            {
                continue;
            }

            Transform group = go.transform.parent;
            string groupName = group != null ? group.name : "<root>";
            if (System.Array.IndexOf(courseGroups, groupName) >= 0)
            {
                continue;
            }

            if (corridor.Intersects(r.bounds))
            {
                sb.AppendLine($"  INTRUSION {go.name} (group {groupName}) bounds {r.bounds.center} size {r.bounds.size}");
                intrusions++;
            }
        }

        sb.AppendLine(intrusions == 0 ? "  none" : $"  {intrusions} intrusion(s) - review each");

        // Unity's console keeps only the first line of a multi-line entry, so persist the
        // full table next to the scene backups where it can be read and diffed.
        string reportDir = System.IO.Path.Combine(
            System.IO.Directory.GetParent(Application.dataPath).FullName, "SceneBackups");
        System.IO.Directory.CreateDirectory(reportDir);
        string reportPath = System.IO.Path.Combine(reportDir, "reachability_report.txt");
        System.IO.File.WriteAllText(reportPath,
            $"walk={walk} sprint={sprint} jumpHeight={jumpHeight} gravity={gravity} launch={launch:F3}\n"
            + $"footing allowance={Footing} m\n\n" + sb.ToString());
        Debug.Log($"[Validate] report written to {reportPath}");

        if (fail > 0)
        {
            Debug.LogError(sb.ToString());
        }
        else if (warn > 0)
        {
            Debug.LogWarning(sb.ToString());
        }
        else
        {
            Debug.Log(sb.ToString());
        }
    }

    [MenuItem("Tools/Parkour/9 - Build All")]
    public static void BuildAll()
    {
        Step1();
        Step2();
        Step3();
        Step4();
        Step5();
        Step6();
        Step7();
        Step8();
    }
}
