using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds Level 2 - "Abandoned Industrial Facility" - into Assets/Scenes/IndustrialParkour.unity.
///
/// Every platform position in <see cref="Course"/> was validated offline against the same reach
/// formula the reachability validator uses (launch 5.196 m/s, footing 0.4 m), so the layout is
/// known-good before a single object exists. Steps are separate menu items so the scene can be
/// saved after each stage.
///
/// This builder never opens or writes Assets/Scenes/UIWorldDemo.unity.
/// </summary>
public static class IndustrialLevelBuilder
{
    public const string ScenePath = "Assets/Scenes/IndustrialParkour.unity";
    private const string MaterialFolder = "Assets/UIWorldDemo/Materials";

    // ---------------------------------------------------------------- course table

    public sealed class Row
    {
        public readonly string Name;
        public readonly float X, TopY, Z, SX, Thick, SZ;
        public readonly int Stage;
        public readonly string Note;
        public Row(string n, float x, float top, float z, float sx, float th, float sz, int st, string note)
        { Name = n; X = x; TopY = top; Z = z; SX = sx; Thick = th; SZ = sz; Stage = st; Note = note; }
    }

    private static readonly List<Row> Course = new List<Row>();
    private static void Plat(string n, float x, float top, float z, float sx, float th, float sz, int st, string note)
        => Course.Add(new Row(n, x, top, z, sx, th, sz, st, note));

    static IndustrialLevelBuilder()
    {
        // ---- Stage 1  Loading Bay
        Plat("Bay_Dock", 0.00f, 0.50f, 0.00f, 16.00f, 1.00f, 10.00f, 1, "SPAWN");
        Plat("Bay_Crate_01", -2.00f, 0.80f, 9.00f, 4.50f, 0.80f, 4.00f, 1, "low crate");
        Plat("Bay_Crate_02", 2.00f, 1.10f, 15.20f, 4.50f, 0.80f, 4.00f, 1, "low crate");
        Plat("Bay_Cont_A", -1.50f, 1.60f, 21.00f, 6.00f, 0.80f, 2.80f, 1, "container");
        Plat("Bay_Cont_B", 2.00f, 2.10f, 26.40f, 6.00f, 0.80f, 2.80f, 1, "container");
        Plat("Bay_Gantry", 0.50f, 2.60f, 32.65f, 10.00f, 1.00f, 4.50f, 1, "CP1");
        // ---- Stage 2  Conveyor Hall
        Plat("Conv_01", -8.20f, 3.10f, 34.15f, 5.00f, 0.60f, 2.40f, 2, "belt");
        Plat("Conv_02", -16.00f, 3.60f, 36.75f, 5.00f, 0.60f, 2.40f, 2, "belt");
        Plat("Conv_03", -23.80f, 4.10f, 34.15f, 5.00f, 0.60f, 2.40f, 2, "belt");
        Plat("Conv_04", -31.60f, 4.60f, 36.75f, 5.00f, 0.60f, 2.40f, 2, "belt");
        Plat("Hall_Deck", -38.60f, 5.10f, 35.25f, 7.00f, 1.00f, 6.00f, 2, "CP2");
        // ---- Stage 3  Boiler Room
        Plat("Boil_P1", -37.10f, 5.95f, 41.35f, 3.00f, 0.80f, 3.00f, 3, "maintenance");
        Plat("Boil_P2", -32.50f, 6.80f, 46.25f, 3.00f, 0.80f, 3.00f, 3, "maintenance");
        Plat("Boil_P3", -37.10f, 7.65f, 51.15f, 3.00f, 0.80f, 3.00f, 3, "maintenance");
        Plat("Boil_P4", -32.50f, 8.50f, 56.05f, 3.00f, 0.80f, 3.00f, 3, "maintenance");
        Plat("Boil_PipeRun", -34.70f, 9.35f, 62.95f, 1.60f, 0.60f, 7.00f, 3, "pipe run");
        Plat("Boil_P5", -31.30f, 10.20f, 69.75f, 2.80f, 0.80f, 2.80f, 3, "maintenance");
        Plat("Boil_P6", -35.70f, 11.05f, 74.45f, 2.80f, 0.80f, 2.80f, 3, "maintenance");
        Plat("Boil_Deck", -33.50f, 11.90f, 79.75f, 8.00f, 1.00f, 5.00f, 3, "CP3");
        // ---- Stage 4  Scaffold Zone
        Plat("Scaf_B1", -25.20f, 12.60f, 80.55f, 2.20f, 0.60f, 2.20f, 4, "node");
        Plat("Scaf_Beam_A", -17.30f, 13.30f, 79.35f, 7.00f, 0.60f, 1.40f, 4, "beam");
        Plat("Scaf_B2", -9.40f, 14.00f, 80.55f, 2.20f, 0.60f, 2.20f, 4, "node");
        Plat("Scaf_Hang_1", -3.70f, 14.70f, 79.15f, 2.40f, 0.60f, 2.40f, 4, "suspended");
        Plat("Scaf_Beam_B", 1.60f, 15.40f, 80.55f, 1.40f, 0.60f, 6.00f, 4, "beam");
        Plat("Scaf_B3", 6.90f, 16.10f, 79.35f, 2.20f, 0.60f, 2.20f, 4, "node");
        Plat("Scaf_Deck", 13.50f, 16.80f, 80.55f, 7.00f, 1.00f, 5.00f, 4, "CP4");
        // ---- Stage 5  Crane Yard
        Plat("Crane_Arm_A", 24.50f, 17.40f, 80.55f, 7.00f, 0.80f, 2.60f, 5, "crane arm");
        Plat("Crane_Hook_1", 27.00f, 18.00f, 70.95f, 3.00f, 0.80f, 3.00f, 5, "hanging");
        Plat("Crane_Arm_B", 29.50f, 18.60f, 59.15f, 2.60f, 0.80f, 7.00f, 5, "crane arm");
        Plat("Crane_Hook_2", 32.00f, 19.20f, 47.25f, 3.00f, 0.80f, 3.00f, 5, "hanging");
        Plat("Crane_Arm_C", 35.20f, 19.80f, 37.70f, 2.60f, 0.80f, 7.00f, 5, "crane arm");
        Plat("Crane_Deck", 34.50f, 20.40f, 26.25f, 7.00f, 1.00f, 6.00f, 5, "CP5");
        // ---- Stage 6  Reactor Tower (helix, 10 rungs)
        Plat("Rx_Cat01", 34.50f, 21.40f, 32.45f, 2.00f, 0.60f, 3.00f, 6, "catwalk");
        Plat("Rx_Cat02", 32.67f, 22.40f, 38.09f, 2.00f, 0.60f, 3.00f, 6, "catwalk");
        Plat("Rx_Cat03", 27.87f, 23.40f, 41.58f, 3.00f, 0.60f, 2.00f, 6, "catwalk");
        Plat("Rx_Cat04", 21.93f, 24.40f, 41.58f, 3.00f, 0.60f, 2.00f, 6, "catwalk");
        Plat("Rx_Cat05", 17.13f, 25.40f, 38.09f, 2.00f, 0.60f, 3.00f, 6, "catwalk CP6");
        Plat("Rx_Cat06", 15.30f, 26.40f, 32.45f, 2.00f, 0.60f, 3.00f, 6, "catwalk");
        Plat("Rx_Cat07", 17.13f, 27.40f, 26.81f, 2.00f, 0.60f, 3.00f, 6, "catwalk");
        Plat("Rx_Cat08", 21.93f, 28.40f, 23.32f, 3.00f, 0.60f, 2.00f, 6, "catwalk");
        Plat("Rx_Cat09", 27.87f, 29.40f, 23.32f, 3.00f, 0.60f, 2.00f, 6, "catwalk");
        Plat("Rx_Cat10", 32.67f, 30.40f, 26.81f, 2.00f, 0.60f, 3.00f, 6, "catwalk");
        Plat("Rx_Shelf", 35.70f, 31.40f, 32.45f, 4.50f, 0.80f, 4.50f, 6, "shelf");
        Plat("Reactor_Cap", 24.90f, 32.40f, 32.45f, 12.00f, 1.00f, 12.00f, 6, "GOAL");
    }

    /// <summary>Route order = table order. Sprint legs are the crane-yard hops.</summary>
    public static readonly string[] SprintFrom =
    {
        "Scaf_Deck", "Crane_Arm_A", "Crane_Hook_1", "Crane_Arm_B", "Crane_Hook_2", "Crane_Arm_C"
    };

    public const float CoreX = 24.90f, CoreZ = 32.45f, CoreHalf = 6.0f;
    public const float SpawnY = 0.55f, SpawnZ = -4.0f;

    // ---------------------------------------------------------------- palette

    private static Material MConcrete, MSteel, MRust, MDark, MGrate, MWarn, MFurnace, MTakeoff, MLand, MGoal;

    private static Material Ensure(string name, Color baseColor, Color emission, float smooth, float metal)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            // Never use ?? with UnityEngine.Object: it bypasses the overloaded == and can yield
            // a fake-null. Explicit == checks throughout this file for the same reason.
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

    private static void LoadPalette()
    {
        MConcrete = Ensure("Mat_Ind_Concrete", new Color(0.30f, 0.29f, 0.27f), Color.black, 0.12f, 0f);
        MSteel = Ensure("Mat_Ind_Steel", new Color(0.38f, 0.40f, 0.43f), Color.black, 0.45f, 0.65f);
        MRust = Ensure("Mat_Ind_Rust", new Color(0.42f, 0.22f, 0.12f), Color.black, 0.22f, 0.35f);
        MDark = Ensure("Mat_Ind_Dark", new Color(0.09f, 0.09f, 0.10f), Color.black, 0.10f, 0.2f);
        MGrate = Ensure("Mat_Ind_Grate", new Color(0.16f, 0.17f, 0.18f), Color.black, 0.55f, 0.8f);
        MWarn = Ensure("Mat_Ind_Warning", new Color(0.85f, 0.62f, 0.05f), new Color(0.55f, 0.32f, 0.02f), 0.35f, 0.1f);
        MFurnace = Ensure("Mat_Ind_Furnace", new Color(0.95f, 0.35f, 0.10f), new Color(4.2f, 1.10f, 0.15f), 0.4f, 0f);
        // Route legibility reuses Level 1's proven takeoff/landing/goal language.
        MTakeoff = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/Mat_Edge_Takeoff.mat");
        if (MTakeoff == null)
            MTakeoff = Ensure("Mat_Edge_Takeoff", new Color(0.9f, 0.34f, 0.06f), new Color(3.2f, 0.85f, 0.12f), 0.5f, 0f);
        MLand = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/Mat_Edge_Land.mat");
        if (MLand == null)
            MLand = Ensure("Mat_Edge_Land", new Color(0.1f, 0.7f, 0.85f), new Color(0.2f, 2.4f, 3.2f), 0.5f, 0f);
        MGoal = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/Mat_Goal_Glow.mat");
        if (MGoal == null)
            MGoal = Ensure("Mat_Goal_Glow", new Color(0.85f, 0.9f, 0.6f), new Color(2.6f, 2.9f, 1.4f), 0.5f, 0f);
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

    /// <summary>
    /// Decorative prop: same as <see cref="Box"/> but with the collider stripped. Markers, stripes,
    /// cables and brackets must never be able to snag the player capsule or occupy a stance.
    /// </summary>
    private static GameObject Decor(string name, Transform parent, Vector3 centre, Vector3 size, Material mat,
        PrimitiveType type = PrimitiveType.Cube)
    {
        GameObject go = Box(name, parent, centre, size, mat, type);
        Collider c = go.GetComponent<Collider>();
        if (c != null) Object.DestroyImmediate(c);
        return go;
    }

    private static GameObject Box(string name, Transform parent, Vector3 centre, Vector3 size, Material mat,
        PrimitiveType type = PrimitiveType.Cube)
    {
        Transform existing = parent.Find(name);
        GameObject go = existing != null ? existing.gameObject : GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = centre;
        go.transform.localScale = size;
        MeshRenderer r = go.GetComponent<MeshRenderer>();
        if (r != null && mat != null) r.sharedMaterial = mat;
        return go;
    }

    /// <summary>Platform from the validated table: `top` is the walking surface height.</summary>
    private static GameObject Pad(Row row, Transform parent, Material mat)
        => Box(row.Name, parent, new Vector3(row.X, row.TopY - row.Thick * 0.5f, row.Z),
               new Vector3(row.SX, row.Thick, row.SZ), mat);

    private static Row Get(string name) => Course.Find(r => r.Name == name);

    /// <summary>Orange takeoff strip on A's edge facing B, cyan landing strip on B's edge facing A.</summary>
    private static void RouteLips(Transform parent, int stage)
    {
        for (int i = 0; i < Course.Count - 1; i++)
        {
            Row a = Course[i], b = Course[i + 1];
            if (b.Stage != stage) continue;
            Vector3 d = new Vector3(b.X - a.X, 0f, b.Z - a.Z);
            bool alongX = Mathf.Abs(d.x) >= Mathf.Abs(d.z);
            const float t = 0.12f, w = 0.10f;

            if (alongX)
            {
                float ax = a.X + Mathf.Sign(d.x) * (a.SX * 0.5f - w);
                Decor($"Lip_{a.Name}_Takeoff", parent, new Vector3(ax, a.TopY + t * 0.5f, a.Z),
                    new Vector3(0.18f, t, a.SZ * 0.9f), MTakeoff);
                float bx = b.X - Mathf.Sign(d.x) * (b.SX * 0.5f - w);
                Decor($"Lip_{b.Name}_Land", parent, new Vector3(bx, b.TopY + t * 0.5f, b.Z),
                    new Vector3(0.18f, t, b.SZ * 0.9f), MLand);
            }
            else
            {
                float az = a.Z + Mathf.Sign(d.z) * (a.SZ * 0.5f - w);
                Decor($"Lip_{a.Name}_Takeoff", parent, new Vector3(a.X, a.TopY + t * 0.5f, az),
                    new Vector3(a.SX * 0.9f, t, 0.18f), MTakeoff);
                float bz = b.Z - Mathf.Sign(d.z) * (b.SZ * 0.5f - w);
                Decor($"Lip_{b.Name}_Land", parent, new Vector3(b.X, b.TopY + t * 0.5f, bz),
                    new Vector3(b.SX * 0.9f, t, 0.18f), MLand);
            }
        }
    }

    private static void PointLight(string name, Transform parent, Vector3 pos, Color c, float range, float intensity)
    {
        Transform ex = parent.Find(name);
        GameObject go = ex != null ? ex.gameObject : new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        Light l = go.GetComponent<Light>();
        if (l == null) l = go.AddComponent<Light>();
        l.type = LightType.Point; l.color = c; l.range = range; l.intensity = intensity;
        l.shadows = LightShadows.None;
    }

    private static void Save()
    {
        Scene s = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(s);
        EditorSceneManager.SaveScene(s, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Industrial] saved {ScenePath}");
    }

    private static bool GuardScene()
    {
        Scene s = SceneManager.GetActiveScene();
        if (s.path == "Assets/Scenes/UIWorldDemo.unity")
        {
            Debug.LogError("[Industrial] ABORT - UIWorldDemo is the active scene. Run step 0 first.");
            return false;
        }
        return true;
    }

    // ---------------------------------------------------------------- step 0: scene + player

    [MenuItem("Tools/Industrial/0 - New Scene + Player")]
    public static void Step0()
    {
        if (SceneManager.GetActiveScene().isDirty &&
            !EditorUtility.DisplayDialog("Unsaved changes",
                "The active scene has unsaved changes. Discard them and create IndustrialParkour?",
                "Discard and continue", "Cancel"))
        {
            return;
        }

        Scene s = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        LoadPalette();

        GameObject world = new GameObject("WORLD");
        world.transform.position = Vector3.zero;

        // ---- player: same components and values as Level 1, nothing retuned
        GameObject player = new GameObject("FPP_Player");
        player.transform.position = new Vector3(0f, SpawnY, SpawnZ);
        CharacterController cc = player.AddComponent<CharacterController>();
        cc.height = 2f; cc.radius = 0.35f; cc.center = new Vector3(0f, 1f, 0f);
        cc.slopeLimit = 50f; cc.stepOffset = 0.30f; cc.skinWidth = 0.04f; cc.minMoveDistance = 0.001f;

        GameObject cam = new GameObject("Main Camera");
        cam.transform.SetParent(player.transform, false);
        cam.transform.localPosition = new Vector3(0f, 1.7f, 0f);
        cam.tag = "MainCamera";
        Camera c = cam.AddComponent<Camera>();
        c.nearClipPlane = 0.1f; c.farClipPlane = 600f; c.fieldOfView = 70f;
        cam.AddComponent<AudioListener>();

        BasicFirstPersonController fpp = player.AddComponent<BasicFirstPersonController>();
        SerializedObject so = new SerializedObject(fpp);
        so.FindProperty("cameraPivot").objectReferenceValue = cam.transform;
        so.ApplyModifiedPropertiesWithoutUndo();   // walkSpeed/sprintSpeed/jumpHeight/gravity keep script defaults

        EditorSceneManager.SaveScene(s, ScenePath);
        AssetDatabase.Refresh();
        Debug.Log($"[Industrial] step 0 done - scene created at {ScenePath}, player at (0,{SpawnY},{SpawnZ})");
    }

    // ---------------------------------------------------------------- stages

    [MenuItem("Tools/Industrial/1 - Stage 1 Loading Bay")]
    public static void Step1()
    {
        if (!GuardScene()) return;
        LoadPalette();
        Transform g = Group("STAGE_1_LoadingBay");

        Pad(Get("Bay_Dock"), g, MConcrete);
        Pad(Get("Bay_Crate_01"), g, MRust);
        Pad(Get("Bay_Crate_02"), g, MRust);
        Pad(Get("Bay_Cont_A"), g, MRust);
        Pad(Get("Bay_Cont_B"), g, MRust);
        Pad(Get("Bay_Gantry"), g, MSteel);

        // dock furniture: back wall, side rails, warning stripe, dead crates (non-route dressing)
        Box("Bay_BackWall", g, new Vector3(0f, 3.0f, -6.0f), new Vector3(16f, 5f, 1f), MConcrete);
        Box("Bay_RailL", g, new Vector3(-7.9f, 1.1f, 0f), new Vector3(0.25f, 1.2f, 10f), MSteel);
        Box("Bay_RailR", g, new Vector3(7.9f, 1.1f, 0f), new Vector3(0.25f, 1.2f, 10f), MSteel);
        Decor("Bay_WarnStripe", g, new Vector3(0f, 0.56f, 3.6f), new Vector3(11f, 0.12f, 0.5f), MWarn);
        Box("Bay_DeadCrate_A", g, new Vector3(-6.2f, 0.9f, 6.5f), new Vector3(1.8f, 1.8f, 1.8f), MRust);
        Box("Bay_DeadCrate_B", g, new Vector3(6.4f, 0.7f, 4.2f), new Vector3(1.4f, 1.4f, 1.4f), MRust);
        Box("Bay_DoorFrame_L", g, new Vector3(-8.4f, 3.2f, 30f), new Vector3(0.6f, 6.4f, 0.6f), MSteel);
        Box("Bay_DoorFrame_R", g, new Vector3(8.4f, 3.2f, 30f), new Vector3(0.6f, 6.4f, 0.6f), MSteel);
        Decor("Bay_DoorHeader", g, new Vector3(0f, 6.2f, 30f), new Vector3(17f, 0.7f, 0.6f), MWarn);

        RouteLips(g, 1);
        Save();
    }

    [MenuItem("Tools/Industrial/2 - Stage 2 Conveyor Hall")]
    public static void Step2()
    {
        if (!GuardScene()) return;
        LoadPalette();
        Transform g = Group("STAGE_2_ConveyorHall");

        for (int i = 1; i <= 4; i++) Pad(Get($"Conv_{i:00}"), g, MSteel);
        Pad(Get("Hall_Deck"), g, MConcrete);

        // belt rollers along each conveyor + overhead pipes well clear of the jump arc
        for (int i = 1; i <= 4; i++)
        {
            Row r = Get($"Conv_{i:00}");
            for (int k = -2; k <= 2; k++)
            {
                Decor($"Roller_{i:00}_{k + 2}", g,
                    new Vector3(r.X + k * 0.95f, r.TopY + 0.06f, r.Z),
                    new Vector3(0.18f, 0.12f, r.SZ * 0.86f), MRust);
            }
            Decor($"Belt_Rail_{i:00}_L", g, new Vector3(r.X, r.TopY - 0.05f, r.Z - r.SZ * 0.5f + 0.08f),
                new Vector3(r.SX, 0.30f, 0.16f), MRust);
            Decor($"Belt_Rail_{i:00}_R", g, new Vector3(r.X, r.TopY - 0.05f, r.Z + r.SZ * 0.5f - 0.08f),
                new Vector3(r.SX, 0.30f, 0.16f), MRust);
        }

        // overhead pipe bank: lowest pipe at y 9.5, > 3.6 m above the highest belt surface
        for (int i = 0; i < 4; i++)
        {
            Box($"Hall_Pipe_{i:00}", g, new Vector3(-20f, 9.5f + i * 0.75f, 30.5f + i * 2.2f),
                new Vector3(44f, 0.55f, 0.55f), MRust);
        }
        Box("Hall_Wall_N", g, new Vector3(-17.5f, 6.0f, 43.0f), new Vector3(25f, 12f, 0.8f), MConcrete);
        Box("Hall_Wall_S", g, new Vector3(-17.5f, 6.0f, 27.5f), new Vector3(25f, 12f, 0.8f), MConcrete);
        Box("Hall_Vent_A", g, new Vector3(-12f, 8.5f, 27.9f), new Vector3(3.2f, 3.2f, 0.5f), MGrate);
        Box("Hall_Vent_B", g, new Vector3(-28f, 8.5f, 42.6f), new Vector3(3.2f, 3.2f, 0.5f), MGrate);

        RouteLips(g, 2);
        Save();
    }

    [MenuItem("Tools/Industrial/3 - Stage 3 Boiler Room")]
    public static void Step3()
    {
        if (!GuardScene()) return;
        LoadPalette();
        Transform g = Group("STAGE_3_BoilerRoom");

        foreach (string n in new[] { "Boil_P1", "Boil_P2", "Boil_P3", "Boil_P4", "Boil_P5", "Boil_P6" })
            Pad(Get(n), g, MGrate);
        Pad(Get("Boil_PipeRun"), g, MRust);
        Pad(Get("Boil_Deck"), g, MConcrete);

        // two boiler drums the switchback climbs between
        Box("Boiler_Drum_A", g, new Vector3(-40.5f, 7.0f, 46.0f), new Vector3(6.5f, 14f, 6.5f), MRust,
            PrimitiveType.Cylinder);
        Box("Boiler_Drum_B", g, new Vector3(-28.5f, 8.0f, 62.0f), new Vector3(6.5f, 16f, 6.5f), MRust,
            PrimitiveType.Cylinder);
        Box("Boiler_Drum_C", g, new Vector3(-40.0f, 9.0f, 72.0f), new Vector3(5.5f, 18f, 5.5f), MRust,
            PrimitiveType.Cylinder);

        // furnace mouths - the orange light source of the room
        Decor("Furnace_Mouth_A", g, new Vector3(-40.5f, 2.2f, 46.0f), new Vector3(3.0f, 2.2f, 0.4f), MFurnace);
        Decor("Furnace_Mouth_B", g, new Vector3(-28.5f, 2.2f, 62.0f), new Vector3(3.0f, 2.2f, 0.4f), MFurnace);

        // steam vents (visual only, thin and set beside the route)
        Decor("Steam_Vent_A", g, new Vector3(-30.0f, 6.6f, 44.0f), new Vector3(0.9f, 0.9f, 0.9f), MGrate);
        Decor("Steam_Vent_B", g, new Vector3(-39.5f, 9.0f, 58.0f), new Vector3(0.9f, 0.9f, 0.9f), MGrate);
        Decor("Steam_Vent_C", g, new Vector3(-29.5f, 11.4f, 74.0f), new Vector3(0.9f, 0.9f, 0.9f), MGrate);

        // support columns under each maintenance platform
        foreach (string n in new[] { "Boil_P1", "Boil_P2", "Boil_P3", "Boil_P4", "Boil_P5", "Boil_P6" })
        {
            Row r = Get(n);
            Box($"Strut_{n}", g, new Vector3(r.X, (r.TopY - r.Thick) * 0.5f, r.Z),
                new Vector3(0.30f, r.TopY - r.Thick, 0.30f), MSteel);
        }
        RouteLips(g, 3);
        Save();
    }

    [MenuItem("Tools/Industrial/4 - Stage 4 Scaffold Zone")]
    public static void Step4()
    {
        if (!GuardScene()) return;
        LoadPalette();
        Transform g = Group("STAGE_4_ScaffoldZone");

        foreach (string n in new[] { "Scaf_B1", "Scaf_B2", "Scaf_B3" }) Pad(Get(n), g, MGrate);
        foreach (string n in new[] { "Scaf_Beam_A", "Scaf_Beam_B" }) Pad(Get(n), g, MSteel);
        Pad(Get("Scaf_Hang_1"), g, MGrate);
        Pad(Get("Scaf_Deck"), g, MConcrete);

        // scaffold poles under the nodes, hanger cables above the suspended platform
        foreach (string n in new[] { "Scaf_B1", "Scaf_B2", "Scaf_B3" })
        {
            Row r = Get(n);
            Box($"Pole_{n}_A", g, new Vector3(r.X - 0.85f, r.TopY - 2.2f, r.Z - 0.85f), new Vector3(0.16f, 4.4f, 0.16f), MSteel);
            Box($"Pole_{n}_B", g, new Vector3(r.X + 0.85f, r.TopY - 2.2f, r.Z + 0.85f), new Vector3(0.16f, 4.4f, 0.16f), MSteel);
        }
        Row h = Get("Scaf_Hang_1");
        Box("Hang_Cable_L", g, new Vector3(h.X - 1.0f, h.TopY + 3.0f, h.Z), new Vector3(0.09f, 6.0f, 0.09f), MSteel);
        Box("Hang_Cable_R", g, new Vector3(h.X + 1.0f, h.TopY + 3.0f, h.Z), new Vector3(0.09f, 6.0f, 0.09f), MSteel);
        Box("Hang_Rail", g, new Vector3(h.X, h.TopY + 6.0f, h.Z), new Vector3(4.0f, 0.30f, 0.30f), MSteel);

        // warning stripes on the two narrow beams so the 1.4 m width reads at speed
        foreach (string n in new[] { "Scaf_Beam_A", "Scaf_Beam_B" })
        {
            Row r = Get(n);
            Decor($"Warn_{n}", g, new Vector3(r.X, r.TopY + 0.07f, r.Z),
                new Vector3(r.SX * 0.96f, 0.10f, r.SZ * 0.30f), MWarn);
        }
        RouteLips(g, 4);
        Save();
    }

    [MenuItem("Tools/Industrial/5 - Stage 5 Crane Yard")]
    public static void Step5()
    {
        if (!GuardScene()) return;
        LoadPalette();
        Transform g = Group("STAGE_5_CraneYard");

        foreach (string n in new[] { "Crane_Arm_A", "Crane_Arm_B", "Crane_Arm_C" }) Pad(Get(n), g, MRust);
        foreach (string n in new[] { "Crane_Hook_1", "Crane_Hook_2" }) Pad(Get(n), g, MGrate);
        Pad(Get("Crane_Deck"), g, MConcrete);

        // crane masts + hoist cables
        Box("Crane_Mast_A", g, new Vector3(24.5f, 9.0f, 84.0f), new Vector3(1.4f, 18f, 1.4f), MRust);
        Box("Crane_Mast_B", g, new Vector3(33.0f, 9.5f, 59.15f), new Vector3(1.4f, 19f, 1.4f), MRust);
        Box("Crane_Mast_C", g, new Vector3(39.2f, 10.0f, 37.70f), new Vector3(1.4f, 20f, 1.4f), MRust);
        foreach (string n in new[] { "Crane_Hook_1", "Crane_Hook_2" })
        {
            Row r = Get(n);
            Decor($"Hoist_{n}", g, new Vector3(r.X, r.TopY + 4.0f, r.Z), new Vector3(0.12f, 8.0f, 0.12f), MSteel);
            Decor($"Block_{n}", g, new Vector3(r.X, r.TopY + 8.2f, r.Z), new Vector3(1.2f, 0.6f, 1.2f), MRust);
        }

        // sprint lane markers: the crane-yard takeoffs need sprint speed, so flag them
        foreach (string n in SprintFrom)
        {
            Row r = Get(n);
            Decor($"SprintLane_{n}", g, new Vector3(r.X, r.TopY + 0.07f, r.Z),
                new Vector3(r.SX * 0.35f, 0.10f, r.SZ * 0.35f), MWarn);
        }
        RouteLips(g, 5);
        Save();
    }

    [MenuItem("Tools/Industrial/6 - Stage 6 Reactor Tower")]
    public static void Step6()
    {
        if (!GuardScene()) return;
        LoadPalette();
        Transform g = Group("STAGE_6_ReactorTower");

        // the stack the helix wraps
        Box("Reactor_Stack", g, new Vector3(CoreX, 16.0f, CoreZ), new Vector3(12f, 32f, 12f), MConcrete);
        Decor("Reactor_Band_A", g, new Vector3(CoreX, 22.0f, CoreZ), new Vector3(12.6f, 0.6f, 12.6f), MRust);
        Decor("Reactor_Band_B", g, new Vector3(CoreX, 28.0f, CoreZ), new Vector3(12.6f, 0.6f, 12.6f), MRust);

        for (int i = 1; i <= 10; i++) Pad(Get($"Rx_Cat{i:00}"), g, MGrate);
        Pad(Get("Rx_Shelf"), g, MGrate);
        Pad(Get("Reactor_Cap"), g, MConcrete);

        // bracket under each catwalk rung, tying it to the stack
        for (int i = 1; i <= 10; i++)
        {
            Row r = Get($"Rx_Cat{i:00}");
            Vector3 toCore = new Vector3(CoreX - r.X, 0f, CoreZ - r.Z);
            Vector3 mid = new Vector3(r.X, r.TopY - 0.45f, r.Z) + toCore.normalized * 1.4f;
            Decor($"Bracket_{i:00}", g, mid, new Vector3(
                Mathf.Abs(toCore.normalized.x) > 0.5f ? 3.0f : 0.22f, 0.22f,
                Mathf.Abs(toCore.normalized.z) > 0.5f ? 3.0f : 0.22f), MSteel);
        }

        // goal dressing
        Decor("Finish_GlowPad", g, new Vector3(CoreX, 32.46f, CoreZ), new Vector3(6f, 0.12f, 5f), MGoal);
        Decor("Finish_Beacon", g, new Vector3(CoreX, 34.4f, CoreZ), new Vector3(0.5f, 3.5f, 0.5f), MGoal);
        Decor("Furnace_Core_Glow", g, new Vector3(CoreX, 3.0f, CoreZ + 6.05f), new Vector3(5f, 4f, 0.3f), MFurnace);

        RouteLips(g, 6);
        Save();
    }

    // ---------------------------------------------------------------- step 7: environment + lighting

    [MenuItem("Tools/Industrial/7 - Environment and Lighting")]
    public static void Step7()
    {
        if (!GuardScene()) return;
        LoadPalette();
        Transform g = Group("ENVIRONMENT");

        Box("Ground", g, new Vector3(0f, -18f, 40f), new Vector3(400f, 0.5f, 400f), MDark);

        // factory skyline: sheds, silos, stacks - warehouse silhouettes, not city towers
        var sheds = new (string n, Vector3 p, Vector3 s)[]
        {
            ("Shed_W1", new Vector3(-70f, 6f, 20f), new Vector3(26f, 12f, 30f)),
            ("Shed_W2", new Vector3(-66f, 8f, 66f), new Vector3(22f, 16f, 26f)),
            ("Shed_E1", new Vector3(64f, 7f, 20f), new Vector3(24f, 14f, 28f)),
            ("Shed_E2", new Vector3(68f, 9f, 68f), new Vector3(26f, 18f, 24f)),
            ("Shed_N1", new Vector3(-10f, 7f, 118f), new Vector3(40f, 14f, 22f)),
            ("Shed_N2", new Vector3(40f, 9f, 124f), new Vector3(34f, 18f, 22f)),
            ("Shed_S1", new Vector3(-14f, 6f, -46f), new Vector3(36f, 12f, 20f)),
            ("Shed_S2", new Vector3(34f, 8f, -52f), new Vector3(30f, 16f, 20f)),
        };
        foreach (var s in sheds) Box(s.n, g, s.p, s.s, MDark);

        var silos = new (string n, Vector3 p, float r, float h)[]
        {
            ("Silo_A", new Vector3(-58f, 14f, 96f), 7f, 28f),
            ("Silo_B", new Vector3(-46f, 12f, 104f), 6f, 24f),
            ("Silo_C", new Vector3(56f, 15f, 98f), 7f, 30f),
            ("Stack_A", new Vector3(-78f, 22f, 48f), 4f, 44f),
            ("Stack_B", new Vector3(76f, 26f, 52f), 4.5f, 52f),
        };
        foreach (var s in silos)
            Box(s.n, g, s.p, new Vector3(s.r * 2f, s.h * 0.5f, s.r * 2f), MDark, PrimitiveType.Cylinder);

        // ---- lighting
        Transform lg = Group("ENV_LIGHTS");
        GameObject sun = GameObject.Find("Directional Light");
        if (sun == null) sun = new GameObject("Directional Light");
        Light sl = sun.GetComponent<Light>();
        if (sl == null) sl = sun.AddComponent<Light>();
        sl.type = LightType.Directional;
        sl.color = new Color(0.55f, 0.42f, 0.34f);      // low, dirty industrial dusk
        sl.intensity = 0.55f;
        sl.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(18f, 214f, 0f);

        Color furnace = new Color(1.00f, 0.42f, 0.14f);
        Color nav = new Color(0.55f, 0.90f, 1.00f);
        PointLight("Light_Furnace_A", lg, new Vector3(-40.5f, 3.5f, 46f), furnace, 30f, 34f);
        PointLight("Light_Furnace_B", lg, new Vector3(-28.5f, 3.5f, 62f), furnace, 30f, 34f);
        PointLight("Light_Furnace_Core", lg, new Vector3(CoreX, 4.5f, CoreZ + 8f), furnace, 40f, 46f);
        PointLight("Light_Bay", lg, new Vector3(0f, 6.5f, 16f), new Color(1f, 0.86f, 0.66f), 26f, 16f);
        PointLight("Light_Hall", lg, new Vector3(-20f, 8f, 36f), new Color(1f, 0.82f, 0.60f), 30f, 18f);
        PointLight("Light_CraneYard", lg, new Vector3(30f, 24f, 58f), new Color(1f, 0.80f, 0.58f), 34f, 20f);
        PointLight("Light_CP1", lg, new Vector3(0.5f, 5.6f, 32.65f), nav, 22f, 20f);
        PointLight("Light_CP2", lg, new Vector3(-38.6f, 8.1f, 35.25f), nav, 22f, 20f);
        PointLight("Light_CP3", lg, new Vector3(-33.5f, 14.9f, 79.75f), nav, 22f, 20f);
        PointLight("Light_CP4", lg, new Vector3(13.5f, 19.8f, 80.55f), nav, 22f, 20f);
        PointLight("Light_CP5", lg, new Vector3(34.5f, 23.4f, 26.25f), nav, 22f, 20f);
        PointLight("Light_CP6", lg, new Vector3(17.13f, 28.4f, 38.09f), nav, 20f, 18f);
        PointLight("Light_Goal", lg, new Vector3(CoreX, 36f, CoreZ), new Color(1f, 0.95f, 0.72f), 36f, 44f);

        // warm, dark, enclosed factory air
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.10f, 0.08f, 0.07f);
        RenderSettings.fogStartDistance = 30f;
        RenderSettings.fogEndDistance = 180f;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.17f, 0.15f, 0.15f);
        RenderSettings.ambientEquatorColor = new Color(0.13f, 0.10f, 0.09f);
        RenderSettings.ambientGroundColor = new Color(0.05f, 0.04f, 0.04f);

        Save();
    }

    // ---------------------------------------------------------------- step 8: checkpoints

    private static void MakeCheckpoint(Transform parent, string name, string label, Row deck, float halfX, float halfZ)
    {
        Transform ex = parent.Find(name);
        GameObject go = ex != null ? ex.gameObject : new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(deck.X, deck.TopY + 1.2f, deck.Z);

        BoxCollider col = go.GetComponent<BoxCollider>();
        if (col == null) col = go.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.size = new Vector3(halfX * 2f, 2.4f, halfZ * 2f);

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

        // physical gate posts so the checkpoint reads in-world
        Box($"{name}_PostL", parent, new Vector3(deck.X - halfX * 0.8f, deck.TopY + 1.2f, deck.Z),
            new Vector3(0.18f, 2.4f, 0.18f), MSteel);
        Box($"{name}_PostR", parent, new Vector3(deck.X + halfX * 0.8f, deck.TopY + 1.2f, deck.Z),
            new Vector3(0.18f, 2.4f, 0.18f), MSteel);
        Box($"{name}_Top", parent, new Vector3(deck.X, deck.TopY + 2.4f, deck.Z),
            new Vector3(halfX * 1.6f + 0.2f, 0.20f, 0.20f), MLand);
    }

    [MenuItem("Tools/Industrial/8 - Checkpoints")]
    public static void Step8()
    {
        if (!GuardScene()) return;
        LoadPalette();
        Transform g = Group("CHECKPOINTS");

        MakeCheckpoint(g, "CP1_LoadingGantry", "Loading Gantry", Get("Bay_Gantry"), 4.0f, 2.0f);
        MakeCheckpoint(g, "CP2_HallEnd", "Conveyor Hall End", Get("Hall_Deck"), 3.0f, 2.6f);
        MakeCheckpoint(g, "CP3_BoilerHead", "Boiler Head", Get("Boil_Deck"), 3.4f, 2.2f);
        MakeCheckpoint(g, "CP4_ScaffoldHead", "Scaffold Head", Get("Scaf_Deck"), 3.0f, 2.2f);
        MakeCheckpoint(g, "CP5_CraneCab", "Crane Cab Deck", Get("Crane_Deck"), 3.0f, 2.6f);
        MakeCheckpoint(g, "CP6_MidHelix", "Reactor Mid-Helix", Get("Rx_Cat05"), 0.9f, 1.4f);

        Save();
    }

    [MenuItem("Tools/Industrial/9 - Build All")]
    public static void BuildAll()
    {
        Step0(); Step1(); Step2(); Step3(); Step4(); Step5(); Step6(); Step7(); Step8();
        Debug.Log("[Industrial] full build complete");
    }

    public static IReadOnlyList<Row> Rows => Course;
}
