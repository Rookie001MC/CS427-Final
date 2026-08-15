using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Extends Level 1 with Stage 7 (Neon Transit Spine) and Stage 8 (Broadcast Tower Finale).
///
/// Every platform coordinate was validated offline against the same reach formula the
/// reachability validator uses (launch 5.196 m/s, footing 0.4 m): 20 new jumps, 0 unreachable,
/// 0 tight. New route geometry sits entirely OUTSIDE the validator play corridor
/// (x -13..13, y 0..24, z -9..104), so the original 34-jump report is unaffected.
///
/// Existing Level 1 geometry is never moved, resized or re-collidered. The one approved
/// exception is cosmetic: Finish_GlowPad / Finish_Beacon are re-materialed from the goal
/// glow to the cyan checkpoint language, because the old cap is now a transition, not the end.
///
/// Never opens or writes IndustrialParkour.unity.
/// </summary>
public static class Level1ExtensionBuilder
{
    private const string ScenePath = "Assets/Scenes/UIWorldDemo.unity";
    private const string MaterialFolder = "Assets/UIWorldDemo/Materials";

    public const float CX = 76f, CZ = 148f, R = 9.5f, StartDeg = -166f;
    public const int Rungs = 9;
    public const float CoreHalf = 5.5f, SummitTop = 36.40f, MastTop = 75f;

    // ---------------------------------------------------------------- course table

    public sealed class Row
    {
        public readonly string Name; public readonly float X, TopY, Z, SX, Thick, SZ;
        public readonly int Stage; public readonly bool Sprint; public readonly string Note;
        public Row(string n, float x, float top, float z, float sx, float th, float sz, int st, bool sp, string note)
        { Name = n; X = x; TopY = top; Z = z; SX = sx; Thick = th; SZ = sz; Stage = st; Sprint = sp; Note = note; }
    }

    private static readonly List<Row> Ext = new List<Row>();
    private static void P(string n, float x, float top, float z, float sx, float th, float sz, int st, bool sp, string note)
        => Ext.Add(new Row(n, x, top, z, sx, th, sz, st, sp, note));

    static Level1ExtensionBuilder()
    {
        // ---- Stage 7  Neon Transit Spine
        // Routed through the z 105-115 gap between City_R04 (z 87..105) and City_Back_R
        // (x 9..31, z 115..137), then east of City_Back_R. Both are original Level 1 buildings.
        P("T7_Span_A", 17.00f, 21.00f, 110.00f, 3.2f, 0.8f, 8.0f, 7, true, "skybridge span");
        P("T7_Vent_B", 23.50f, 21.60f, 110.00f, 3.6f, 0.8f, 3.6f, 7, false, "vent platform");
        P("T7_Beam_C", 32.00f, 22.20f, 110.00f, 9.0f, 0.6f, 1.4f, 7, false, "rooftop beam");
        P("T7_Roof_D", 43.50f, 22.80f, 110.00f, 6.0f, 1.0f, 6.0f, 7, false, "rooftop");
        P("T7_Plat_E", 43.50f, 23.20f, 119.30f, 4.0f, 0.8f, 4.0f, 7, false, "maintenance platform (turn north)");
        P("T7_Span_F", 55.00f, 23.60f, 119.30f, 3.2f, 0.8f, 8.0f, 7, true, "long skybridge sprint");
        P("T7_Vent_G", 60.00f, 24.20f, 128.00f, 3.4f, 0.8f, 3.4f, 7, false, "vent platform");
        P("T7_Beam_H", 60.00f, 24.80f, 137.00f, 1.4f, 0.6f, 8.0f, 7, false, "rooftop beam");
        P("T7_Deck_I", 60.00f, 25.40f, 145.50f, 8.0f, 1.0f, 6.0f, 7, false, "CP3 transit deck");

        // ---- Stage 8  Broadcast Tower Finale: 9-rung helix, rise 1.00, ring radius 9.5
        float top = 26.40f;
        for (int i = 0; i < Rungs; i++)
        {
            float a = Mathf.Deg2Rad * (StartDeg + (360f / Rungs) * i);
            float x = CX + R * Mathf.Cos(a), z = CZ + R * Mathf.Sin(a);
            bool tangX = Mathf.Abs(Mathf.Sin(a)) > Mathf.Abs(Mathf.Cos(a));
            float sx = tangX ? 3.0f : 2.0f, sz = tangX ? 2.0f : 3.0f;
            P($"T8_Ledge_{i + 1:00}", Mathf.Round(x * 100f) / 100f, top, Mathf.Round(z * 100f) / 100f,
              sx, 0.6f, sz, 8, false, i == 4 ? "CP4 antenna shelf" : "tower ledge");
            top += 1.00f;
        }
        float a0 = Mathf.Deg2Rad * StartDeg;
        P("T8_MastShelf", Mathf.Round((CX + 12f * Mathf.Cos(a0)) * 100f) / 100f, top,
          Mathf.Round((CZ + 12f * Mathf.Sin(a0)) * 100f) / 100f, 4.5f, 0.8f, 4.5f, 8, false, "mast shelf");
        P("T8_Summit", CX, top + 1.00f, CZ, 11.0f, 1.0f, 11.0f, 8, false, "GOAL broadcast summit");
    }

    public static IReadOnlyList<Row> Rows => Ext;
    public static Row Get(string n) { foreach (var r in Ext) if (r.Name == n) return r; return null; }

    // ---------------------------------------------------------------- materials

    private static Material MDeck, MJump, MPrec, MDark, MConcrete, MTakeoff, MLand, MGoal,
                            MWinLit, MWinBlue, MNeonCyan, MNeonAmber;

    private static Material Load(string n) => AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/{n}.mat");

    private static void LoadPalette()
    {
        MDeck = Load("Mat_Path_Deck"); MJump = Load("Mat_Path_Jump"); MPrec = Load("Mat_Path_Precision");
        MDark = Load("Mat_Dark"); MConcrete = Load("Mat_Concrete");
        MTakeoff = Load("Mat_Edge_Takeoff"); MLand = Load("Mat_Edge_Land"); MGoal = Load("Mat_Goal_Glow");
        MWinLit = Load("Mat_WindowLit"); MWinBlue = Load("Mat_WindowBlue");
        MNeonCyan = Load("Mat_City_NeonCyan"); MNeonAmber = Load("Mat_City_NeonAmber");
    }

    // ---------------------------------------------------------------- primitives

    private static Transform Group(string name)
    {
        GameObject world = GameObject.Find("WORLD");
        if (world == null) world = new GameObject("WORLD");
        Transform t = world.transform.Find(name);
        if (t == null) { GameObject g = new GameObject(name); g.transform.SetParent(world.transform, false); t = g.transform; }
        return t;
    }

    /// <summary>Solid, collidable object - only used for actual route platforms and the tower shaft.</summary>
    private static GameObject Solid(string name, Transform parent, Vector3 centre, Vector3 size, Material mat)
    {
        Transform ex = parent.Find(name);
        GameObject go = ex != null ? ex.gameObject : GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = centre; go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = size;
        MeshRenderer r = go.GetComponent<MeshRenderer>();
        if (r != null && mat != null) r.sharedMaterial = mat;
        if (go.GetComponent<Collider>() == null) go.AddComponent<BoxCollider>();
        return go;
    }

    /// <summary>Collider-free prop. Everything decorative goes through here.</summary>
    private static GameObject D(string name, Transform parent, Vector3 centre, Vector3 size, Material mat,
        PrimitiveType type = PrimitiveType.Cube)
    {
        Transform ex = parent.Find(name);
        GameObject go = ex != null ? ex.gameObject : GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = centre; go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = size;
        MeshRenderer r = go.GetComponent<MeshRenderer>();
        if (r != null && mat != null) r.sharedMaterial = mat;
        Collider c = go.GetComponent<Collider>();
        if (c != null) Object.DestroyImmediate(c);
        return go;
    }

    private static GameObject Pad(Row row, Transform parent, Material mat)
        => Solid(row.Name, parent, new Vector3(row.X, row.TopY - row.Thick * 0.5f, row.Z),
                 new Vector3(row.SX, row.Thick, row.SZ), mat);

    private static void PointLight(string name, Transform p, Vector3 pos, Color c, float range, float intensity)
    {
        Transform ex = p.Find(name);
        GameObject go = ex != null ? ex.gameObject : new GameObject(name);
        go.transform.SetParent(p, false); go.transform.localPosition = pos;
        Light l = go.GetComponent<Light>(); if (l == null) l = go.AddComponent<Light>();
        l.type = LightType.Point; l.color = c; l.range = range; l.intensity = intensity;
        l.shadows = LightShadows.None;
    }

    /// <summary>Orange takeoff strip on A's edge facing B, cyan landing strip on B's edge facing A.</summary>
    private static void Lips(Transform parent, Row a, Row b)
    {
        Vector3 d = new Vector3(b.X - a.X, 0f, b.Z - a.Z);
        bool alongX = Mathf.Abs(d.x) >= Mathf.Abs(d.z);
        const float t = 0.12f, inset = 0.10f;
        if (alongX)
        {
            D($"LipX_{a.Name}_Takeoff", parent,
              new Vector3(a.X + Mathf.Sign(d.x) * (a.SX * 0.5f - inset), a.TopY + t * 0.5f, a.Z),
              new Vector3(0.18f, t, a.SZ * 0.9f), MTakeoff);
            D($"LipX_{b.Name}_Land", parent,
              new Vector3(b.X - Mathf.Sign(d.x) * (b.SX * 0.5f - inset), b.TopY + t * 0.5f, b.Z),
              new Vector3(0.18f, t, b.SZ * 0.9f), MLand);
        }
        else
        {
            D($"LipX_{a.Name}_Takeoff", parent,
              new Vector3(a.X, a.TopY + t * 0.5f, a.Z + Mathf.Sign(d.z) * (a.SZ * 0.5f - inset)),
              new Vector3(a.SX * 0.9f, t, 0.18f), MTakeoff);
            D($"LipX_{b.Name}_Land", parent,
              new Vector3(b.X, b.TopY + t * 0.5f, b.Z - Mathf.Sign(d.z) * (b.SZ * 0.5f - inset)),
              new Vector3(b.SX * 0.9f, t, 0.18f), MLand);
        }
    }

    private static void MakeCheckpoint(Transform parent, string name, string label, Row deck, float hx, float hz)
    {
        Transform ex = parent.Find(name);
        GameObject go = ex != null ? ex.gameObject : new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(deck.X, deck.TopY + 1.2f, deck.Z);

        BoxCollider col = go.GetComponent<BoxCollider>();
        if (col == null) col = go.AddComponent<BoxCollider>();
        col.isTrigger = true; col.size = new Vector3(hx * 2f, 2.4f, hz * 2f);

        Transform rp = go.transform.Find("Respawn");
        GameObject rpGo = rp != null ? rp.gameObject : new GameObject("Respawn");
        rpGo.transform.SetParent(go.transform, false);
        rpGo.transform.localPosition = new Vector3(0f, -1.2f + 0.55f, 0f);

        CheckpointVolume cv = go.GetComponent<CheckpointVolume>();
        if (cv == null) cv = go.AddComponent<CheckpointVolume>();
        SerializedObject so = new SerializedObject(cv);
        so.FindProperty("respawnPoint").objectReferenceValue = rpGo.transform;
        so.FindProperty("checkpointName").stringValue = label;
        so.ApplyModifiedPropertiesWithoutUndo();

        D($"{name}_PostL", parent, new Vector3(deck.X - hx * 0.8f, deck.TopY + 1.2f, deck.Z), new Vector3(0.18f, 2.4f, 0.18f), MConcrete);
        D($"{name}_PostR", parent, new Vector3(deck.X + hx * 0.8f, deck.TopY + 1.2f, deck.Z), new Vector3(0.18f, 2.4f, 0.18f), MConcrete);
        D($"{name}_Top", parent, new Vector3(deck.X, deck.TopY + 2.4f, deck.Z), new Vector3(hx * 1.6f + 0.2f, 0.20f, 0.20f), MLand);
    }

    // ---------------------------------------------------------------- build

    [MenuItem("Tools/Parkour/E - Build Stage 7 + 8 Extension")]
    public static void Build()
    {
        // Building during play mode silently throws everything away on exit, so refuse outright.
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[Ext] ABORT - editor is in play mode. Exit play mode and run again.");
            return;
        }
        Scene s = SceneManager.GetActiveScene();
        if (s.path != ScenePath)
        {
            Debug.LogError($"[Ext] ABORT - active scene is '{s.path}', expected {ScenePath}.");
            return;
        }
        LoadPalette();

        Transform g7 = Group("STAGE_7_Transit");
        Transform g8 = Group("STAGE_8_Broadcast");
        Transform lip = Group("EDGE_LIPS_EXT");
        Transform cps = Group("CHECKPOINTS");            // existing course group; children added, none altered
        Transform env = Group("ENV_DISTRICT_EAST");

        // ---- Stage 7 platforms
        Pad(Get("T7_Span_A"), g7, MJump);
        Pad(Get("T7_Vent_B"), g7, MDeck);
        Pad(Get("T7_Beam_C"), g7, MPrec);
        Pad(Get("T7_Roof_D"), g7, MJump);
        Pad(Get("T7_Plat_E"), g7, MDeck);
        Pad(Get("T7_Span_F"), g7, MJump);
        Pad(Get("T7_Vent_G"), g7, MDeck);
        Pad(Get("T7_Beam_H"), g7, MPrec);
        Pad(Get("T7_Deck_I"), g7, MDeck);

        // ---- Stage 8: tower shaft (solid, like Finish_TowerBase), helix, summit
        Solid("T8_TowerShaft", g8, new Vector3(CX, SummitTop * 0.5f - 0.5f, CZ),
              new Vector3(CoreHalf * 2f, SummitTop - 1f, CoreHalf * 2f), MDark);
        for (int i = 1; i <= Rungs; i++) Pad(Get($"T8_Ledge_{i:00}"), g8, MPrec);
        Pad(Get("T8_MastShelf"), g8, MDeck);
        Pad(Get("T8_Summit"), g8, MDeck);

        // ---- edge lips across every new jump (collider-free so they cannot snag)
        Row prev = null;
        foreach (var r in Ext)
        {
            if (prev != null) Lips(lip, prev, r);
            prev = r;
        }
        // and the hand-off jump from the existing cap into Stage 7
        GameObject cap = GameObject.Find("Finish_TowerCap");
        if (cap != null)
        {
            Bounds cb = cap.GetComponent<Renderer>().bounds;
            // lives in the exempt CHECKPOINTS group: it sits on an existing course platform
            // inside the validator corridor, and is collider-free so it cannot affect anything
            D("LipX_FinishTowerCap_Takeoff", cps,
              new Vector3(cb.max.x - 0.6f, cb.max.y + 0.06f, cb.max.z - 1.2f), new Vector3(2.6f, 0.12f, 0.18f), MTakeoff);
        }

        // ---- checkpoints
        MakeCheckpoint(cps, "CP3_TransitDeck", "Transit Deck", Get("T7_Deck_I"), 3.4f, 2.6f);
        MakeCheckpoint(cps, "CP4_AntennaShelf", "Antenna Shelf", Get("T8_Ledge_05"), 1.2f, 0.8f);

        // ---- APPROVED cosmetic change: old cap becomes a transition, not a finish.
        // Only sharedMaterial changes; geometry, position and colliders are untouched.
        foreach (string n in new[] { "Finish_GlowPad", "Finish_Beacon", "Finish_Top" })
        {
            GameObject go = GameObject.Find(n);
            if (go == null) continue;
            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = MLand;      // cyan checkpoint language
        }
        // forward chevrons on the old cap pointing at Stage 7 (collider-free, in the exempt
        // CHECKPOINTS group so the corridor report stays clean)
        for (int i = 0; i < 3; i++)
        {
            D($"CP_Transit_Chevron_{i}", cps,
              new Vector3(3.2f + i * 1.5f, 22.07f, 94.0f + i * 1.5f), new Vector3(2.2f, 0.10f, 0.5f), MTakeoff);
        }
        D("CP_Transit_Arch", cps, new Vector3(0f, 23.6f, 97.5f), new Vector3(7.0f, 0.22f, 0.22f), MLand);

        // ---- T8_Summit final-goal dressing
        Row sum = Get("T8_Summit");
        D("T8_Goal_Pad", g8, new Vector3(CX, sum.TopY + 0.06f, CZ), new Vector3(6.5f, 0.12f, 6.5f), MGoal);
        D("T8_Goal_Beacon", g8, new Vector3(CX, sum.TopY + 2.6f, CZ), new Vector3(0.6f, 5.2f, 0.6f), MGoal);
        D("T8_Goal_ArchL", g8, new Vector3(CX - 4.2f, sum.TopY + 1.8f, CZ), new Vector3(0.3f, 3.6f, 0.3f), MConcrete);
        D("T8_Goal_ArchR", g8, new Vector3(CX + 4.2f, sum.TopY + 1.8f, CZ), new Vector3(0.3f, 3.6f, 0.3f), MConcrete);
        D("T8_Goal_ArchTop", g8, new Vector3(CX, sum.TopY + 3.6f, CZ), new Vector3(8.7f, 0.32f, 0.32f), MGoal);
        for (int i = 0; i < 4; i++)
            D($"T8_Goal_Ring_{i}", g8, new Vector3(CX, sum.TopY + 0.05f, CZ), new Vector3(9f - i * 1.6f, 0.06f, 9f - i * 1.6f),
              i % 2 == 0 ? MLand : MTakeoff);
        PointLight("T8_Goal_Light", g8, new Vector3(CX, sum.TopY + 6f, CZ), new Color(1f, 0.95f, 0.72f), 40f, 46f);

        // ================= ENV_DISTRICT_EAST - all collider-free
        // host rooftops beneath the Stage 7 platforms
        var hosts = new (string n, float x, float z, float w, float d, float roof)[]
        {
            // Roof_A is pushed east to x>=14 so it clears the validator corridor (x -13..13)
            ("DE_Roof_A", 20f, 110f, 12f, 14f, 20.0f), ("DE_Roof_B", 24f, 110f, 10f, 10f, 20.6f),
            ("DE_Roof_C", 33f, 110f, 14f, 10f, 21.2f), ("DE_Roof_D", 44f, 110f, 14f, 14f, 21.8f),
            ("DE_Roof_E", 44f, 120f, 12f, 12f, 22.2f), ("DE_Roof_F", 55f, 119f, 12f, 16f, 22.6f),
            ("DE_Roof_G", 60f, 128f, 12f, 12f, 23.2f), ("DE_Roof_H", 60f, 137f, 12f, 16f, 23.8f),
            ("DE_Roof_I", 60f, 145f, 16f, 16f, 24.4f),
        };
        foreach (var h in hosts)
            D(h.n, env, new Vector3(h.x, (h.roof - 4f) * 0.5f, h.z), new Vector3(h.w, h.roof + 4f, h.d), MDark);

        // rooftop clutter: vents, HVAC, ducts, antenna rigs
        int cl = 0;
        foreach (var h in hosts)
        {
            for (int k = 0; k < 3; k++)
            {
                float px = h.x + ((k - 1) * h.w * 0.3f);
                float pz = h.z + ((cl % 3) - 1) * h.d * 0.28f;
                float hh = 0.8f + (cl % 3) * 0.5f;
                D($"DE_Vent_{cl:00}_{k}", env, new Vector3(px, h.roof + hh * 0.5f, pz),
                  new Vector3(1.4f + (k * 0.6f), hh, 1.4f), MConcrete);
            }
            D($"DE_Duct_{cl:00}", env, new Vector3(h.x, h.roof + 0.35f, h.z + h.d * 0.34f),
              new Vector3(h.w * 0.7f, 0.7f, 1.0f), MDark);
            cl++;
        }

        // broadcast tower: shaft dressing + mast + dishes above the playable summit
        D("DE_Mast", env, new Vector3(CX, (SummitTop + MastTop) * 0.5f, CZ), new Vector3(1.6f, MastTop - SummitTop, 1.6f), MDark);
        D("DE_MastTip", env, new Vector3(CX, MastTop + 4f, CZ), new Vector3(0.5f, 8f, 0.5f), MNeonCyan);
        for (int i = 0; i < 5; i++)
        {
            float y = 42f + i * 7f;
            D($"DE_MastRing_{i}", env, new Vector3(CX, y, CZ), new Vector3(4.5f - i * 0.5f, 0.35f, 4.5f - i * 0.5f), MDark);
            D($"DE_MastLamp_{i}", env, new Vector3(CX, y + 0.4f, CZ), new Vector3(1.0f, 0.22f, 1.0f),
              i % 2 == 0 ? MNeonAmber : MNeonCyan);
        }
        for (int i = 0; i < 4; i++)
        {
            float a = Mathf.Deg2Rad * (45f + i * 90f);
            D($"DE_Dish_{i}", env, new Vector3(CX + 4.6f * Mathf.Cos(a), 39f + i * 2f, CZ + 4.6f * Mathf.Sin(a)),
              new Vector3(3.2f, 0.4f, 3.2f), MConcrete, PrimitiveType.Cylinder);
        }
        // tower support legs
        for (int i = 0; i < 4; i++)
        {
            float sx = (i % 2 == 0) ? -1f : 1f, sz = (i < 2) ? -1f : 1f;
            D($"DE_TowerLeg_{i}", env, new Vector3(CX + sx * 7.5f, 9f, CZ + sz * 7.5f), new Vector3(1.1f, 18f, 1.1f), MDark);
        }

        // district skyline fill: east + north, several heights, blocking long sightlines
        var fill = new (string n, float x, float z, float w, float d, float h)[]
        {
            ("DE_Blk_01", 36f, 96f, 14f, 14f, 26f), ("DE_Blk_02", 50f, 132f, 14f, 12f, 34f),
            ("DE_Blk_03", 72f, 118f, 18f, 18f, 30f), ("DE_Blk_04", 94f, 134f, 20f, 22f, 44f),
            ("DE_Blk_05", 48f, 152f, 14f, 14f, 38f), ("DE_Blk_06", 96f, 162f, 22f, 20f, 50f),
            ("DE_Blk_07", 34f, 162f, 18f, 18f, 28f), ("DE_Blk_08", 70f, 180f, 20f, 18f, 42f),
            ("DE_Blk_09", 22f, 142f, 14f, 16f, 24f), ("DE_Blk_10", 110f, 112f, 24f, 24f, 56f),
            ("DE_Blk_11", 48f, 92f, 14f, 14f, 22f), ("DE_Blk_12", 104f, 190f, 26f, 24f, 60f),
        };
        foreach (var b in fill)
            D(b.n, env, new Vector3(b.x, (b.h - 4f) * 0.5f, b.z), new Vector3(b.w, b.h + 4f, b.d), MDark);

        // windows on the district blocks
        int w = 0;
        foreach (var b in fill)
        {
            for (int r = 0; r < 4; r++)
            {
                float py = 6f + r * 8f;
                if (py > b.h - 3f) continue;
                D($"DE_Win_{w:000}", env, new Vector3(b.x - b.w * 0.5f - 0.06f, py, b.z + ((w % 3) - 1) * b.d * 0.25f),
                  new Vector3(0.1f, 2.4f, 4.6f), (w % 4 == 0) ? MWinBlue : MWinLit);
                w++;
            }
        }
        // sparse neon strips + signs
        for (int i = 0; i < 8; i++)
        {
            var b = fill[i];
            D($"DE_Neon_{i:00}", env, new Vector3(b.x - b.w * 0.5f - 0.09f, b.h - 8f, b.z - b.d * 0.34f),
              new Vector3(0.16f, 13f, 0.34f), i % 2 == 0 ? MNeonCyan : MNeonAmber);
        }
        for (int i = 0; i < 4; i++)
            D($"DE_Sign_{i:00}", env, new Vector3(fill[i + 4].x - fill[i + 4].w * 0.5f - 0.12f, fill[i + 4].h - 5f, fill[i + 4].z),
              new Vector3(0.22f, 5.5f, 8f), i % 2 == 0 ? MNeonAmber : MNeonCyan);

        // district lighting - dim, shadowless, never brighter than the route markers
        PointLight("DE_Light_A", env, new Vector3(20f, 25f, 116f), new Color(0.55f, 0.85f, 1f), 26f, 12f);
        PointLight("DE_Light_B", env, new Vector3(40f, 26f, 114f), new Color(1f, 0.72f, 0.42f), 26f, 12f);
        PointLight("DE_Light_C", env, new Vector3(56f, 28f, 121f), new Color(0.55f, 0.85f, 1f), 26f, 12f);
        PointLight("DE_Light_D", env, new Vector3(63f, 29f, 134f), new Color(0.55f, 0.85f, 1f), 24f, 14f);
        PointLight("DE_Light_E", env, new Vector3(CX, 31f, CZ), new Color(1f, 0.72f, 0.42f), 34f, 18f);

        // relocate the two decorative skyline pieces that sat where the district now is
        foreach (var mv in new (string n, Vector3 pos)[]
        { ("SK_Far_03", new Vector3(-96f, 34f, 150f)), ("SK_Far_05", new Vector3(-120f, 38f, 200f)) })
        {
            GameObject go = GameObject.Find(mv.n);
            if (go != null) go.transform.position = mv.pos;
        }

        // ---------------------------------------------------------------- assertions
        Bounds corridor = new Bounds(); corridor.SetMinMax(new Vector3(-13f, 0f, -9f), new Vector3(13f, 24f, 104f));
        int intr = 0, decorCols = 0;
        foreach (Transform grp in new[] { g7, g8, lip, env })
        {
            foreach (var mr in grp.GetComponentsInChildren<MeshRenderer>(true))
                if (corridor.Intersects(mr.bounds)) { Debug.LogWarning($"[Ext] corridor overlap: {mr.name}"); intr++; }
        }
        foreach (Transform grp in new[] { lip, env })
            decorCols += grp.GetComponentsInChildren<Collider>(true).Length;

        EditorSceneManager.MarkSceneDirty(s);
        EditorSceneManager.SaveScene(s, ScenePath);
        AssetDatabase.SaveAssets();

        Debug.Log($"[Ext] Stage 7 ({g7.childCount}) + Stage 8 ({g8.childCount}) + lips ({lip.childCount}) "
                + $"+ district ({env.childCount}) built. corridor overlaps={intr}, decorative colliders={decorCols}. Saved.");
    }
}
