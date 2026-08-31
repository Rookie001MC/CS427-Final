using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds Assets/Scenes/ParkourMovementTest.unity - a development sandbox for the Phase 6A.5
/// movement work. Not a player-facing level, deliberately not in the build settings, and it
/// contains no GameManager, no checkpoints and no UI: just the player rig and labelled geometry
/// for every mechanic, laid out as parallel lanes so one can be tested without walking the others.
///
/// Same contract as the other builders in this project: idempotent, menu-driven, and the single
/// authority on the layout. Never opens or writes IndustrialParkour.unity or UIWorldDemo.unity.
///
/// Lane spacing is 30m so a mistimed jump in one lane cannot land in the next.
/// </summary>
public static class ParkourMovementTestBuilder
{
    private const string ScenePath = "Assets/Scenes/ParkourMovementTest.unity";
    private const string MaterialFolder = "Assets/UIWorldDemo/Materials";

    // Derived from the real controller: sprint 9 m/s, jump 1.5 m, gravity -9 m/s^2.
    private const float LaneStep = 30f;

    private static Material mDeck, mTakeoff, mLanding, mWall, mDark, mAccent;

    [MenuItem("Tools/Parkour Movement/Build Movement Test Scene", priority = 0)]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[MoveTest] Exit play mode first.");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        LoadMaterials();

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        BuildLighting();
        BuildGround();

        GameObject player = BuildPlayer();

        LaneFlatJumps(0);
        LaneAscending(1);
        LaneDescending(2);
        LaneVault(3);
        LaneMantle(4);
        LaneSlide(5);
        LaneWallRun(6);
        LaneWallRunCombos(7);

        Selection.activeGameObject = player;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.Refresh();

        Debug.Log($"[MoveTest] Built {ScenePath}. 8 lanes; player at origin facing +Z.");
    }

    // ------------------------------------------------------------------ scene furniture

    private static void LoadMaterials()
    {
        mDeck = Load("Mat_Path_Deck") ?? Load("Mat_Concrete");
        mTakeoff = Load("Mat_Edge_Takeoff") ?? Load("Mat_AccentOrange");
        mLanding = Load("Mat_Edge_Land") ?? Load("Mat_Cyan");
        mWall = Load("Mat_Dark") ?? Load("Mat_Concrete");
        mDark = Load("Mat_Ind_Steel") ?? Load("Mat_Dark");
        mAccent = Load("Mat_RedAccent") ?? Load("Mat_AccentOrange");
    }

    private static Material Load(string n)
        => AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/{n}.mat");

    private static void BuildLighting()
    {
        GameObject sun = new GameObject("Directional Light", typeof(Light));
        Light light = sun.GetComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.15f;
        light.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(48f, 145f, 0f);

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.42f, 0.47f, 0.55f);
        RenderSettings.ambientEquatorColor = new Color(0.28f, 0.30f, 0.34f);
        RenderSettings.ambientGroundColor = new Color(0.14f, 0.15f, 0.17f);
        RenderSettings.fog = false;
    }

    private static void BuildGround()
    {
        Box("Ground", null, new Vector3(120f, -1f, 60f), new Vector3(400f, 2f, 260f), mDark);
    }

    private static GameObject BuildPlayer()
    {
        GameObject go = new GameObject("FPP_Player");
        go.transform.position = new Vector3(0f, 0.1f, -14f);

        CharacterController cc = go.AddComponent<CharacterController>();
        cc.height = 2f;
        cc.radius = 0.35f;
        cc.center = new Vector3(0f, 1f, 0f);
        cc.slopeLimit = 50f;
        cc.stepOffset = 0.3f;
        cc.skinWidth = 0.04f;
        cc.minMoveDistance = 0.001f;

        GameObject cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        cam.tag = "MainCamera";
        cam.transform.SetParent(go.transform, false);
        cam.transform.localPosition = new Vector3(0f, 1.7f, 0f);
        Camera camera = cam.GetComponent<Camera>();
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 600f;
        camera.fieldOfView = 70f;

        go.AddComponent<SlideAbility>();
        go.AddComponent<VaultDetector>();
        go.AddComponent<MantleDetector>();
        go.AddComponent<WallRunAbility>();
        go.AddComponent<ParkourCameraRig>();

        BasicFirstPersonController movement = go.AddComponent<BasicFirstPersonController>();
        SerializedObject so = new SerializedObject(movement);
        so.FindProperty("cameraPivot").objectReferenceValue = cam.transform;
        so.FindProperty("walkSpeed").floatValue = 6f;
        so.FindProperty("sprintSpeed").floatValue = 9f;
        so.FindProperty("jumpHeight").floatValue = 1.5f;
        so.FindProperty("gravity").floatValue = -9f;
        // Well below the deepest lane, so a missed jump resets instead of falling forever.
        so.FindProperty("fallResetHeight").floatValue = -40f;
        so.ApplyModifiedPropertiesWithoutUndo();

        go.AddComponent<PlayerFreezeController>();
        return go;
    }

    // ------------------------------------------------------------------ lanes

    /// <summary>Flat sprint gaps. 7.3m is the safe design figure, ~9.2m the practical ceiling.</summary>
    private static void LaneFlatJumps(int lane)
    {
        Transform root = Group($"Lane{lane}_FlatJumps");
        float x = lane * LaneStep;
        float z = 0f;

        Pad(root, "Flat_Start", x, 4f, z, 8f, 6f, mTakeoff);
        z += 8f;

        float[] gaps = { 4f, 5f, 6f, 7f, 8f, 9f, 10f };
        for (int i = 0; i < gaps.Length; i++)
        {
            z += gaps[i];
            Pad(root, $"Flat_{gaps[i]:0}m", x, 4f, z, 5f, 6f,
                gaps[i] >= 9.5f ? mAccent : mLanding);
            z += 5f;
            Label(root, $"gap {gaps[i]:0}m", new Vector3(x, 5.4f, z - 2.5f));
        }
    }

    /// <summary>Ascending jumps. Above +1.5m nothing is reachable - jumpHeight is the hard cap.</summary>
    private static void LaneAscending(int lane)
    {
        Transform root = Group($"Lane{lane}_Ascending");
        float x = lane * LaneStep;
        float z = 0f;

        Pad(root, "Asc_Start", x, 4f, z, 8f, 6f, mTakeoff);
        z += 8f;

        // (rise, gap) pairs sized just inside the analytic reach for each rise.
        (float rise, float gap)[] steps =
        {
            (0.5f, 5.5f), (0.5f, 7.5f),
            (1.0f, 4.5f), (1.0f, 6.5f),
            (1.5f, 3.0f), (1.5f, 5.0f)
        };

        float y = 4f;
        foreach ((float rise, float gap) in steps)
        {
            z += gap;
            y += rise;
            Pad(root, $"Asc_+{rise:0.0}_{gap:0.0}m", x, y, z, 5f, 6f, mLanding);
            Label(root, $"+{rise:0.0}m / {gap:0.0}m", new Vector3(x, y + 1.4f, z));
            z += 5f;
        }
    }

    /// <summary>Descending jumps. Drops buy airtime, so gaps widen sharply.</summary>
    private static void LaneDescending(int lane)
    {
        Transform root = Group($"Lane{lane}_Descending");
        float x = lane * LaneStep;
        float z = 0f;
        float y = 26f;

        Pad(root, "Desc_Start", x, y, z, 8f, 6f, mTakeoff);
        z += 8f;

        (float drop, float gap)[] steps = { (2f, 9f), (4f, 10.5f), (6f, 11.5f), (8f, 12.5f) };

        foreach ((float drop, float gap) in steps)
        {
            z += gap;
            y -= drop;
            Pad(root, $"Desc_-{drop:0}_{gap:0.0}m", x, y, z, 6f, 6f, mLanding);
            Label(root, $"-{drop:0}m / {gap:0.0}m", new Vector3(x, y + 1.4f, z));
            z += 6f;
        }

        // Access ramp so the lane can be entered on foot without flying.
        Ramp(root, "Desc_Ramp", new Vector3(x - 7f, 13f, -2f), new Vector3(4f, 0.5f, 56f), -25f, mDeck);
    }

    /// <summary>Vault band is 0.40-1.20m. The 1.40m block must refuse and read as a wall.</summary>
    private static void LaneVault(int lane)
    {
        Transform root = Group($"Lane{lane}_Vault");
        float x = lane * LaneStep;

        Pad(root, "Vault_Run", x, 0f, 6f, 26f, 8f, mDeck);

        float[] heights = { 0.4f, 0.6f, 0.8f, 1.0f, 1.2f, 1.4f };
        float z = -2f;

        foreach (float h in heights)
        {
            z += 7f;
            Box(root, $"Vault_{h:0.00}m", new Vector3(x, h * 0.5f, z), new Vector3(8f, h, 0.6f),
                h > 1.25f ? mAccent : mLanding);
            Label(root, $"vault {h:0.00}m" + (h > 1.25f ? " (must refuse)" : ""),
                new Vector3(x, h + 1.2f, z));
        }
    }

    /// <summary>Mantle band is 1.20-2.00m. The 2.20m ledge must refuse.</summary>
    private static void LaneMantle(int lane)
    {
        Transform root = Group($"Lane{lane}_Mantle");
        float x = lane * LaneStep;

        float[] heights = { 1.2f, 1.5f, 1.8f, 2.0f, 2.2f };
        float z = 0f;

        foreach (float h in heights)
        {
            Pad(root, $"Mantle_Run_{h:0.0}", x, 0f, z, 8f, 8f, mDeck);
            z += 6f;

            // A block deep enough to stand on top of, with clear air above.
            Box(root, $"Mantle_{h:0.00}m", new Vector3(x, h * 0.5f, z + 2f),
                new Vector3(8f, h, 4f), h > 2.05f ? mAccent : mLanding);
            Label(root, $"mantle {h:0.00}m" + (h > 2.05f ? " (must refuse)" : ""),
                new Vector3(x, h + 1.4f, z + 2f));
            z += 12f;
        }

        // Ceiling overhang above a valid ledge: the mantle must refuse this one for lack of
        // headroom, not because of the ledge height.
        Pad(root, "Mantle_Run_Blocked", x, 0f, z, 8f, 8f, mDeck);
        Box(root, "Mantle_Blocked_Ledge", new Vector3(x, 0.75f, z + 8f), new Vector3(8f, 1.5f, 4f), mLanding);
        Box(root, "Mantle_Blocked_Roof", new Vector3(x, 2.6f, z + 8f), new Vector3(8f, 0.4f, 4f), mAccent);
        Label(root, "mantle blocked by roof (must refuse)", new Vector3(x, 3.4f, z + 8f));
    }

    /// <summary>Slide clearance. Capsule drops to 1.0m, so 1.1m clears and 0.9m does not.</summary>
    private static void LaneSlide(int lane)
    {
        Transform root = Group($"Lane{lane}_Slide");
        float x = lane * LaneStep;

        Pad(root, "Slide_Run", x, 0f, 14f, 60f, 8f, mDeck);

        float[] clearances = { 1.6f, 1.3f, 1.1f, 0.9f };
        float z = -4f;

        foreach (float c in clearances)
        {
            z += 13f;
            // Portal: two posts and a lintel whose underside sits at the clearance height.
            Box(root, $"Slide_Lintel_{c:0.0}", new Vector3(x, c + 0.5f, z), new Vector3(8f, 1f, 1f),
                c < 1.0f ? mAccent : mLanding);
            Box(root, $"Slide_PostL_{c:0.0}", new Vector3(x - 3.5f, c * 0.5f, z), new Vector3(1f, c, 1f), mWall);
            Box(root, $"Slide_PostR_{c:0.0}", new Vector3(x + 3.5f, c * 0.5f, z), new Vector3(1f, c, 1f), mWall);
            Label(root, $"clearance {c:0.0}m" + (c < 1.0f ? " (must block)" : ""),
                new Vector3(x, c + 1.8f, z));
        }
    }

    /// <summary>Two parallel walls: run one on the left, one on the right.</summary>
    private static void LaneWallRun(int lane)
    {
        Transform root = Group($"Lane{lane}_WallRun");
        float x = lane * LaneStep;

        Pad(root, "WallRun_Approach", x, 0f, 0f, 10f, 14f, mDeck);

        // Right-hand wall, entered from a ledge so the player is airborne on arrival.
        Box(root, "WallRun_Right", new Vector3(x + 2.2f, 4f, 22f), new Vector3(0.8f, 8f, 22f), mWall);
        Label(root, "right wall run", new Vector3(x + 2.2f, 8.6f, 22f));

        // Left-hand wall further along.
        Box(root, "WallRun_Left", new Vector3(x - 2.2f, 4f, 50f), new Vector3(0.8f, 8f, 22f), mWall);
        Label(root, "left wall run", new Vector3(x - 2.2f, 8.6f, 50f));

        // Launch ledge: the run has to start in the air.
        Pad(root, "WallRun_Launch", x, 2.5f, 9f, 6f, 4f, mTakeoff);
        Ramp(root, "WallRun_Ramp", new Vector3(x, 1.25f, 4f), new Vector3(6f, 0.5f, 6f), -23f, mDeck);

        Pad(root, "WallRun_Catch", x, 2.5f, 66f, 8f, 8f, mLanding);
    }

    /// <summary>Wall-run into a jump across a gap, and wall-run into a mantle onto a high ledge.</summary>
    private static void LaneWallRunCombos(int lane)
    {
        Transform root = Group($"Lane{lane}_WallRunCombos");
        float x = lane * LaneStep;

        // --- combo 1: wall run -> wall jump across a gap that a flat jump cannot make.
        Pad(root, "Combo_Launch", x, 2.5f, 4f, 8f, 6f, mTakeoff);
        Ramp(root, "Combo_Ramp", new Vector3(x, 1.25f, -1f), new Vector3(6f, 0.5f, 6f), -23f, mDeck);
        Box(root, "Combo_WallA", new Vector3(x + 2.2f, 4f, 18f), new Vector3(0.8f, 8f, 16f), mWall);
        Pad(root, "Combo_Land_Jump", x - 6f, 2.5f, 34f, 8f, 8f, mLanding);
        Label(root, "wallrun -> wall jump", new Vector3(x, 9f, 18f));

        // --- combo 2: wall run -> mantle onto a ledge above the run height.
        Pad(root, "Combo2_Launch", x, 2.5f, 48f, 8f, 6f, mTakeoff);
        Box(root, "Combo2_Wall", new Vector3(x + 2.2f, 4f, 62f), new Vector3(0.8f, 8f, 16f), mWall);
        Box(root, "Combo2_Ledge", new Vector3(x - 1.5f, 1.9f, 74f), new Vector3(7f, 3.8f, 6f), mLanding);
        Label(root, "wallrun -> mantle", new Vector3(x, 9f, 62f));
    }

    // ------------------------------------------------------------------ primitives

    private static Transform Group(string name)
    {
        GameObject world = GameObject.Find("WORLD") ?? new GameObject("WORLD");
        Transform existing = world.transform.Find(name);
        if (existing != null)
        {
            return existing;
        }

        GameObject g = new GameObject(name);
        g.transform.SetParent(world.transform, false);
        return g.transform;
    }

    private static GameObject Box(Transform parent, string name, Vector3 centre, Vector3 size, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;

        if (parent != null)
        {
            go.transform.SetParent(parent, false);
        }

        go.transform.localPosition = centre;
        go.transform.localScale = size;

        MeshRenderer r = go.GetComponent<MeshRenderer>();
        if (r != null && mat != null)
        {
            r.sharedMaterial = mat;
        }

        return go;
    }

    private static GameObject Box(string name, Transform parent, Vector3 centre, Vector3 size, Material mat)
        => Box(parent, name, centre, size, mat);

    /// <summary>A platform whose named Y is its walking surface, not its centre.</summary>
    private static GameObject Pad(Transform parent, string name, float x, float surfaceY, float z,
        float depth, float width, Material mat)
    {
        const float thickness = 0.5f;
        return Box(parent, name, new Vector3(x, surfaceY - thickness * 0.5f, z),
            new Vector3(width, thickness, depth), mat);
    }

    private static GameObject Ramp(Transform parent, string name, Vector3 centre, Vector3 size,
        float pitchDegrees, Material mat)
    {
        GameObject go = Box(parent, name, centre, size, mat);
        go.transform.localRotation = Quaternion.Euler(pitchDegrees, 0f, 0f);
        return go;
    }

    /// <summary>
    /// A scene-view-only text marker. Carries no renderer and no collider, so it can never be
    /// stood on or block a probe.
    /// </summary>
    private static void Label(Transform parent, string text, Vector3 position)
    {
        GameObject go = new GameObject($"# {text}");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
    }
}
