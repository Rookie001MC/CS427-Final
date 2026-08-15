using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Navigation-only pass for the Stage 6 -> Stage 7 hand-off on the old Finish_TowerCap.
///
/// The player arrives from Ledge_E1 on the EAST side travelling west, so the exit (the cap's
/// NE corner, heading 46.8 deg, 9.06 m gap, 1.00 m drop) ends up behind them - while every
/// bright object sits in the middle of the deck. This adds a directional read-out that pulls
/// the eye to the corner and makes T7_Span_A legible even though it sits 1 m BELOW cap level.
///
/// Visuals only. Every object is collider-free; no platform, checkpoint, collider, material of
/// an existing route surface, or player value is touched. Cues live in the CHECKPOINTS group,
/// which the reachability validator already exempts, alongside the existing CP*_Top markers.
/// </summary>
public static class Level1TransitionCue
{
    private const string ScenePath = "Assets/Scenes/UIWorldDemo.unity";
    private const string MaterialFolder = "Assets/UIWorldDemo/Materials";

    private static Material MTakeoff, MLand, MConcrete;
    private static Material Load(string n) => AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/{n}.mat");

    private static Transform Group(string name)
    {
        GameObject world = GameObject.Find("WORLD");
        Transform t = world.transform.Find(name);
        if (t == null) { GameObject g = new GameObject(name); g.transform.SetParent(world.transform, false); t = g.transform; }
        return t;
    }

    /// <summary>Collider-free marker.</summary>
    private static GameObject D(string name, Transform parent, Vector3 pos, Vector3 size, Material mat, float yaw = 0f)
    {
        Transform ex = parent.Find(name);
        GameObject go = ex != null ? ex.gameObject : GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        go.transform.localScale = size;
        MeshRenderer r = go.GetComponent<MeshRenderer>();
        if (r != null && mat != null) r.sharedMaterial = mat;
        Collider c = go.GetComponent<Collider>();
        if (c != null) Object.DestroyImmediate(c);
        return go;
    }

    /// <summary>A ">" chevron whose point sits at <paramref name="tip"/> aiming along <paramref name="yaw"/>.</summary>
    private static void Chevron(string name, Transform p, Vector3 tip, float len, float thick, float yaw, Material mat)
    {
        for (int s = -1; s <= 1; s += 2)
        {
            float ang = yaw + s * 135f;
            Vector3 dir = new Vector3(Mathf.Sin(ang * Mathf.Deg2Rad), 0f, Mathf.Cos(ang * Mathf.Deg2Rad));
            D($"{name}_{(s < 0 ? "L" : "R")}", p, tip + dir * (len * 0.5f),
              new Vector3(thick, 0.12f, len), mat, ang);
        }
    }

    private static void PointLight(string name, Transform p, Vector3 pos, Color c, float range, float intensity)
    {
        Transform ex = p.Find(name);
        GameObject go = ex != null ? ex.gameObject : new GameObject(name);
        go.transform.SetParent(p, false); go.transform.localPosition = pos;
        Light l = go.GetComponent<Light>(); if (l == null) l = go.AddComponent<Light>();
        l.type = LightType.Point; l.color = c; l.range = range; l.intensity = intensity;
        l.shadows = LightShadows.None;
    }

    [MenuItem("Tools/Parkour/G - Transition Cue (Stage 6 -> 7)")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { Debug.LogError("[Cue] ABORT - exit play mode first."); return; }
        Scene s = SceneManager.GetActiveScene();
        if (s.path != ScenePath) { Debug.LogError($"[Cue] ABORT - active scene is '{s.path}'."); return; }

        MTakeoff = Load("Mat_Edge_Takeoff");   // orange - "leave from here"
        MLand = Load("Mat_Edge_Land");         // cyan   - "land there"
        MConcrete = Load("Mat_Concrete");

        Transform g = Group("CHECKPOINTS");
        Bounds cap = GameObject.Find("Finish_TowerCap").GetComponent<Renderer>().bounds;
        Bounds span = GameObject.Find("T7_Span_A").GetComponent<Renderer>().bounds;

        Vector3 corner = new Vector3(cap.max.x, cap.max.y, cap.max.z);              // (8.8, 22.0, 99.8)
        Vector3 landNear = new Vector3(span.center.x, span.max.y, span.min.z);      // (17.0, 21.0, 106.0)
        Vector3 flat = landNear - corner; flat.y = 0f;
        float yaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;                    // 46.8 deg

        // retire the weak first-pass cues (created by this project, not original geometry)
        foreach (string old in new[] { "CP_Transit_Chevron_0", "CP_Transit_Chevron_1", "CP_Transit_Chevron_2", "CP_Transit_Arch" })
        {
            Transform t = g.Find(old);
            if (t != null) Object.DestroyImmediate(t.gameObject);
        }

        // ---- 1. chevron lane sweeping the eye from the deck centre out to the NE corner
        Vector3 laneStart = new Vector3(-1.5f, cap.max.y + 0.07f, 88.5f);
        Vector3 laneEnd = new Vector3(7.0f, cap.max.y + 0.07f, 97.6f);
        for (int i = 0; i < 6; i++)
        {
            float t = i / 5f;
            Vector3 pos = Vector3.Lerp(laneStart, laneEnd, t);
            Chevron($"Cue_Lane_{i:00}", g, pos, 1.7f + t * 1.5f, 0.30f + t * 0.16f, yaw, MTakeoff);
        }

        // ---- 2. takeoff pad + a wide edge band on the NE corner (replaces a 2.6 m sliver)
        // Unrotated: a yawed 4.6 m square inflates its footprint to 6.4 m and would hang off the
        // deck corner. The chevrons carry the direction, so the pad only needs to mark the spot.
        D("Cue_TakeoffPad", g, new Vector3(cap.max.x - 2.6f, cap.max.y + 0.03f, cap.max.z - 2.6f),
          new Vector3(4.8f, 0.10f, 4.8f), MTakeoff);
        D("Cue_EdgeBand_N", g, new Vector3(cap.max.x - 3.2f, cap.max.y + 0.06f, cap.max.z - 0.22f), new Vector3(6.4f, 0.14f, 0.44f), MTakeoff);
        D("Cue_EdgeBand_E", g, new Vector3(cap.max.x - 0.22f, cap.max.y + 0.06f, cap.max.z - 3.2f), new Vector3(0.44f, 0.14f, 6.4f), MTakeoff);

        // ---- 3. departure gantry at the corner, square to the jump line, framing the target
        // pulled inboard so both posts land on the deck once the +-2.4 m span is yawed
        Vector3 gate = corner + new Vector3(-3.2f, 0f, -3.4f);
        Vector3 rightV = new Vector3(Mathf.Cos(yaw * Mathf.Deg2Rad), 0f, -Mathf.Sin(yaw * Mathf.Deg2Rad));
        D("Cue_Gate_L", g, gate - rightV * 2.4f + Vector3.up * 1.7f, new Vector3(0.26f, 3.4f, 0.26f), MConcrete, yaw);
        D("Cue_Gate_R", g, gate + rightV * 2.4f + Vector3.up * 1.7f, new Vector3(0.26f, 3.4f, 0.26f), MConcrete, yaw);
        D("Cue_Gate_Top", g, gate + Vector3.up * 3.5f, new Vector3(5.2f, 0.30f, 0.30f), MTakeoff, yaw);
        D("Cue_Gate_Glow", g, gate + Vector3.up * 3.16f, new Vector3(4.9f, 0.12f, 0.16f), MLand, yaw);

        // ---- 4. approach chevrons along the jump arc: cyan, so they read as "fly there"
        for (int i = 0; i < 4; i++)
        {
            float t = 0.22f + i * 0.2f;
            Vector3 p = Vector3.Lerp(corner, landNear, t);
            p.y = Mathf.Lerp(corner.y, landNear.y, t) + 1.15f * Mathf.Sin(Mathf.PI * t) + 0.45f;
            Chevron($"Cue_Air_{i:00}", g, p, 2.3f - i * 0.25f, 0.26f, yaw, MLand);
        }

        // ---- 5. landing readability: T7_Span_A sits 1 m below the cap, so mark it vertically
        float sx = span.center.x, sz = span.min.z + 1.0f, sy = span.max.y;
        D("Cue_LandPad", g, new Vector3(sx, sy + 0.03f, span.min.z + 1.6f), new Vector3(2.9f, 0.10f, 3.0f), MLand);
        D("Cue_LandPylon_L", g, new Vector3(span.min.x + 0.3f, sy + 2.1f, sz), new Vector3(0.24f, 4.2f, 0.24f), MLand);
        D("Cue_LandPylon_R", g, new Vector3(span.max.x - 0.3f, sy + 2.1f, sz), new Vector3(0.24f, 4.2f, 0.24f), MLand);
        D("Cue_LandBanner", g, new Vector3(sx, sy + 4.1f, sz), new Vector3(3.4f, 0.28f, 0.28f), MLand);
        D("Cue_LandBannerGlow", g, new Vector3(sx, sy + 3.82f, sz), new Vector3(3.1f, 0.12f, 0.16f), MTakeoff);
        // a forward chevron on the far end so the player knows the route keeps going north
        Chevron("Cue_LandOnward", g, new Vector3(sx, sy + 0.07f, span.max.z - 1.2f), 2.0f, 0.28f, 0f, MTakeoff);

        // ---- 6. two guide lights (dim, shadowless)
        PointLight("Cue_Light_Takeoff", g, corner + new Vector3(-1.5f, 2.2f, -1.5f), new Color(1f, 0.55f, 0.18f), 14f, 11f);
        PointLight("Cue_Light_Landing", g, new Vector3(sx, sy + 3.2f, sz + 1.5f), new Color(0.35f, 0.85f, 1f), 20f, 16f);

        // ---- assertions
        int cols = 0, made = 0;
        foreach (Transform t in g)
        {
            if (!t.name.StartsWith("Cue_")) continue;
            made++;
            if (t.GetComponent<Collider>() != null) cols++;
        }

        EditorSceneManager.MarkSceneDirty(s);
        EditorSceneManager.SaveScene(s, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Cue] transition cues built: {made} objects, colliders={cols}, jump yaw={yaw:F1} deg. Scene saved.");
    }
}
