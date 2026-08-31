using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Builds Assets/Scenes/SkyboundCity.unity - the Phase 6B greybox of the Phase 6A city design.
///
/// The builder owns nothing. Every dimension comes from <see cref="CityDesign"/> and every box
/// comes from <see cref="CityPlan"/>; this class turns that plan into GameObjects and does the
/// scene furniture (lighting, fog, the player rig) that a plan cannot express. Keeping it that
/// thin is what lets the EditMode tests assert on the city without opening it.
///
/// Phase 6C added the traversal layer - fire escapes, risers, skybridges, the crane and the tower
/// spiral - and it arrives here almost for free: CityTraversal puts its geometry into the same four
/// plan lists, so this class only had to learn what the new piece kinds look like.
///
/// Phase 6D added the mission on top of it, and the same shape holds: CityObjectives puts the relay
/// plinths, the anchor pads and the tower gate into those same lists and the trigger volumes into a
/// fifth, and this class turns each volume into the components that give it behaviour. It also
/// stands up the run systems, because a mission needs something to be a run of - the same
/// GameManager, RunTimer, CheckpointManager and RespawnManager the other two levels use, with the
/// checkpoint route in Set mode so the five relays may be taken in any order.
///
/// Phase 6E is the art pass, and it arrives the same way one more time: `CityDressing` puts every
/// facade band, rooftop unit, sign, handrail, kerb and backdrop block into a fifth plan list, and
/// this class turns each into one <c>CityKit.Detail</c> call. Nothing in that list can be
/// collidable, which is why an art pass this large lands on a city whose traversal has already been
/// measured without re-measuring any of it. What 6E does add here that a plan cannot express is the
/// scene furniture that makes it read: a low sun, a procedural sky, dusk ambient, aerial fog, and a
/// mission HUD that looks like the rest of the game's UI instead of like a debug readout.
///
/// What the builder deliberately does NOT do, so it is clear the omissions are scope and not misses:
///   - no full gameplay UI: pause, countdown, death and complete panels   (Phase 6F)
///   - no occlusion or lighting bake                                      (Phase 6G)
///
/// The HUD it does build is the mission readout and nothing else - relay count, bearing, distance
/// and whether the tower is open - because those four are what an order-free objective set is
/// unplayable without. The scene is still a development scene you open directly.
///
/// Never opens or writes IndustrialParkour.unity, UIWorldDemo.unity or MainMenu.unity.
/// </summary>
public static class SkyboundCityBuilder
{
    public const string ScenePath = "Assets/Scenes/SkyboundCity.unity";

    /// <summary>The catalogue asset the menu and this scene both describe the level from.</summary>
    public const string LevelEntryPath = "Assets/Data/Level03_SkyboundCity.asset";

    private static Material mGround;
    private static Material mAvenue;
    private static Material mCenter;
    private static Material mResidential;
    private static Material mIndustrial;
    private static Material mCorporate;
    private static Material mOldQuarter;
    private static Material mLandmark;
    private static Material mCut;
    private static Material mDeck;
    private static Material mAscent;
    private static Material mCrane;
    private static Material mTowerAscent;
    private static Material mObjective;
    private static Material mObjectiveDone;
    private static Material mGate;

    [MenuItem("Tools/Skybound City/Build Greybox", priority = 0)]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[SkyboundCity] Exit play mode first.");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        EnsureMaterials();

        CityPlanResult plan = CityPlan.Generate();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        BuildLighting();
        BuildSlabs(plan);
        BuildBlocks(plan);
        BuildRamps(plan);
        BuildBuildings(plan);
        BuildScaffoldFrames(plan);
        BuildDistrictLabels();
        BuildTraversalLabels(plan);

        // Phase 6E before the mission, not after: the relay beacons it emits are part of a relay's
        // captured/uncaptured face, and BuildMission wires the status renderers by name.
        BuildDetails(plan);

        GameObject player = BuildPlayer();
        BuildMission(plan, player);

        Selection.activeGameObject = player;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        CityTraversalResult traversal = plan.Traversal;

        CityObjectivesResult mission = plan.Objectives;

        Debug.Log($"[SkyboundCity] Built {ScenePath}: {plan.Buildings.Count} buildings, " +
                  $"{plan.Slabs.Count} slabs, {plan.Blocks.Count} blocks, {plan.Ramps.Count} ramps, " +
                  $"{plan.Volumes.Count} volumes, {plan.ColliderCount} colliders. " +
                  $"Tallest {plan.TallestRoof:F1} m. " +
                  $"Traversal: {traversal.Links.Count} links ({traversal.InterDistrictLinkCount} " +
                  $"inter-district), {traversal.Ascents.Count} ascents. " +
                  $"Mission: {mission.Relays.Count} relays, {mission.Anchors.Count} respawn " +
                  $"anchors, fatal fall {CityDesign.FatalFallHeight:F1} m. " +
                  "Not registered in build settings - that is Phase 6F.");

        if (traversal.Problems.Count > 0)
        {
            Debug.LogWarning("[SkyboundCity] traversal problems:\n  " +
                             string.Join("\n  ", traversal.Problems));
        }

        if (mission.Problems.Count > 0)
        {
            Debug.LogWarning("[SkyboundCity] mission problems:\n  " +
                             string.Join("\n  ", mission.Problems));
        }
    }

    // ------------------------------------------------------------------ materials

    private static void EnsureMaterials()
    {
        detailMaterials.Clear();

        // PHASE 6E: the district massing now comes from CityDesign.Palette rather than from nine
        // hand-typed greys. That is the whole of the colour zoning at massing level - a facade, its
        // trim, its glass and its neon are all drawn from the same six-entry table, so a district
        // cannot end up with a cornice that does not belong to it.
        mGround = CityKit.Ensure("Mat_City_Ground", new Color(0.21f, 0.22f, 0.24f));
        mAvenue = CityKit.Ensure("Mat_City_Avenue", new Color(0.14f, 0.15f, 0.17f));
        mCenter = Massing("Center", DistrictGroup.CityCenter);
        mResidential = Massing("Residential", DistrictGroup.Residential);
        mIndustrial = Massing("Industrial", DistrictGroup.Industrial);
        mCorporate = Massing("Corporate", DistrictGroup.Corporate);
        mOldQuarter = Massing("OldQuarter", DistrictGroup.OldQuarter);
        mLandmark = Massing("Landmark", DistrictGroup.Landmark);
        mCut = CityKit.Ensure("Mat_City_Cut", new Color(0.17f, 0.18f, 0.19f));

        // Phase 6C. The traversal layer reads as a different material class from the massing on
        // purpose: a player has to be able to tell, from across an avenue, what is climbable.
        mDeck = CityKit.Ensure("Mat_City_Deck", new Color(0.72f, 0.70f, 0.64f), 0.20f);
        mAscent = CityKit.Ensure("Mat_City_Ascent", new Color(0.30f, 0.34f, 0.38f), 0.45f, 0.6f);
        mCrane = CityKit.Ensure("Mat_City_Crane", new Color(0.82f, 0.62f, 0.16f), 0.30f, 0.3f);
        mTowerAscent = CityKit.Ensure("Mat_City_TowerAscent", new Color(0.78f, 0.79f, 0.80f), 0.25f);

        // Phase 6D. The mission reads in two colours and only two: cyan is something to go to,
        // green is something done, and the gate is the one orange thing in the city.
        //
        // PHASE 6E made both of them emissive. A relay is meant to be findable from an avenue 25 m
        // below at dusk, and a matte cyan box is not; it also means the halo and the beacon 6E adds
        // to a relay keep glowing when ObjectiveRelay swaps their material, which a non-emissive
        // status material would have silently switched off.
        mObjective = CityKit.EnsureEmissive("Mat_City_Objective", new Color(0.04f, 0.16f, 0.19f),
            new Color(0.09f, 0.78f, 0.92f), 2.4f, 0.40f);
        mObjectiveDone = CityKit.EnsureEmissive("Mat_City_ObjectiveDone",
            new Color(0.05f, 0.18f, 0.11f), new Color(0.20f, 0.90f, 0.52f), 2.4f, 0.40f);
        mGate = CityKit.Ensure("Mat_City_Gate", new Color(0.85f, 0.35f, 0.14f), 0.25f);
    }

    private static Material Massing(string name, DistrictGroup group)
        => CityKit.Ensure($"Mat_City_{name}", CityDesign.Palette(group).Massing,
            group == DistrictGroup.Corporate ? 0.32f : 0.10f);

    // ------------------------------------------------------------------ Phase 6E: detail materials

    /// <summary>
    /// One material per (surface, district) pair that is actually used, created on demand.
    ///
    /// Four of the fourteen surfaces are district-tinted and ten are shared, which is why the key is
    /// a pair rather than a name: a shared surface collapses onto one entry however many districts
    /// ask for it, so the city ends up with about thirty materials rather than eighty-four, and
    /// static batching has thirty batches to work with rather than one per box.
    /// </summary>
    private static readonly Dictionary<int, Material> detailMaterials = new Dictionary<int, Material>();

    private static bool IsTinted(DetailSurface surface)
        => surface == DetailSurface.Trim || surface == DetailSurface.Panel
                                         || surface == DetailSurface.Glass
                                         || surface == DetailSurface.Neon;

    private static Material DetailMaterial(DetailSurface surface, DistrictGroup tint)
    {
        DistrictGroup key = IsTinted(surface) ? tint : DistrictGroup.Landmark;
        int hash = (int)surface * 16 + (int)key;

        if (detailMaterials.TryGetValue(hash, out Material cached))
        {
            return cached;
        }

        CityDesign.DistrictPalette palette = CityDesign.Palette(key);
        string suffix = IsTinted(surface) ? $"_{key}" : string.Empty;
        string name = $"Mat_Detail_{surface}{suffix}";
        Material material;

        switch (surface)
        {
            case DetailSurface.Trim:
                material = CityKit.Ensure(name, palette.Trim, 0.14f);
                break;

            case DetailSurface.Panel:
                material = CityKit.Ensure(name, palette.Panel, 0.10f);
                break;

            // Glazing is the one place the city is allowed to be shiny. It is what makes a Corporate
            // tower read as a tower rather than as a blue box, and it is why the sun sits low.
            case DetailSurface.Glass:
                material = CityKit.Ensure(name, palette.Glass, 0.88f, 0.35f);
                break;

            case DetailSurface.Neon:
                material = CityKit.EnsureEmissive(name, palette.Neon * 0.18f, palette.Neon, 2.6f);
                break;

            case DetailSurface.Concrete:
                material = CityKit.Ensure(name, new Color(0.54f, 0.54f, 0.52f), 0.08f);
                break;

            case DetailSurface.Metal:
                material = CityKit.Ensure(name, new Color(0.62f, 0.64f, 0.66f), 0.55f, 0.85f);
                break;

            case DetailSurface.MetalDark:
                material = CityKit.Ensure(name, new Color(0.19f, 0.20f, 0.22f), 0.42f, 0.70f);
                break;

            case DetailSurface.Machine:
                material = CityKit.Ensure(name, new Color(0.45f, 0.47f, 0.49f), 0.30f, 0.40f);
                break;

            case DetailSurface.Rust:
                material = CityKit.Ensure(name, new Color(0.43f, 0.27f, 0.19f), 0.12f, 0.15f);
                break;

            case DetailSurface.Paint:
                material = CityKit.Ensure(name, new Color(0.84f, 0.83f, 0.76f), 0.18f);
                break;

            case DetailSurface.Hazard:
                material = CityKit.EnsureEmissive(name, new Color(0.55f, 0.24f, 0.05f),
                    new Color(1f, 0.42f, 0.08f), 1.1f, 0.25f);
                break;

            // The route strip. Cyan on purpose: it is the colour ObjectiveRelay uses for an
            // uncaptured relay and the colour the HUD is drawn in, so "cyan means go there" is one
            // rule across the geometry, the objectives and the interface.
            case DetailSurface.Route:
                material = CityKit.EnsureEmissive(name, new Color(0.04f, 0.11f, 0.13f),
                    new Color(0.09f, 0.78f, 0.92f), 2.2f);
                break;

            case DetailSurface.Lamp:
                material = CityKit.EnsureEmissive(name, new Color(0.32f, 0.27f, 0.19f),
                    new Color(1f, 0.80f, 0.52f), 3.2f);
                break;

            // Never lit, never shiny, and a value close to the fog it sits in. A backdrop that
            // catches the sun reads as reachable, which is exactly what it is not.
            default:
                material = CityKit.Ensure(name, new Color(0.30f, 0.34f, 0.42f), 0.02f);
                break;
        }

        detailMaterials[hash] = material;
        return material;
    }

    private static Material MaterialFor(CityPieceKind kind)
    {
        switch (kind)
        {
            case CityPieceKind.Cut: return mCut;
            case CityPieceKind.Deck: return mDeck;
            case CityPieceKind.Ascent: return mAscent;
            case CityPieceKind.Crane: return mCrane;
            case CityPieceKind.TowerAscent: return mTowerAscent;
            case CityPieceKind.Objective: return mObjective;
            case CityPieceKind.Gate: return mGate;
            default: return mLandmark;
        }
    }

    private static Material MaterialFor(DistrictGroup group)
    {
        switch (group)
        {
            case DistrictGroup.CityCenter: return mCenter;
            case DistrictGroup.Residential: return mResidential;
            case DistrictGroup.Industrial: return mIndustrial;
            case DistrictGroup.Corporate: return mCorporate;
            case DistrictGroup.OldQuarter: return mOldQuarter;
            default: return mLandmark;
        }
    }

    // ------------------------------------------------------------------ scene furniture

    /// <summary>
    /// PHASE 6E. The single cheapest thing in this whole phase, and close to the most effective.
    ///
    /// The Phase 6B greybox lit the city from 46 degrees, which put roughly the same amount of light
    /// on every face of every box and is most of why a city of boxes read as boxes. A 24 degree sun
    /// throws a facade's own shadow across the avenue in front of it, separates the four sides of
    /// every building, and picks the cornices and floor bands out as lines instead of as tone.
    ///
    /// Three lights, and only one of them casts: a warm low key, a cool unshadowed fill from behind
    /// so the shaded faces are blue rather than black, and nothing else. The Phase 6A budget allows
    /// ten realtime lights; every lit window, sign, beacon and lamp head in the city is emissive
    /// geometry rather than a light, which is what keeps that number at two.
    /// </summary>
    private static void BuildLighting()
    {
        GameObject sun = new GameObject("Sun", typeof(Light));
        Light key = sun.GetComponent<Light>();
        key.type = LightType.Directional;
        key.intensity = CityDesign.SunIntensity;
        key.color = new Color(1f, 0.90f, 0.76f);
        key.shadows = LightShadows.Soft;
        key.shadowStrength = 0.82f;
        sun.transform.rotation = Quaternion.Euler(CityDesign.SunPitch, CityDesign.SunYaw, 0f);

        GameObject fillGo = new GameObject("Sky Fill", typeof(Light));
        Light fill = fillGo.GetComponent<Light>();
        fill.type = LightType.Directional;
        fill.intensity = 0.30f;
        fill.color = new Color(0.52f, 0.66f, 0.95f);
        fill.shadows = LightShadows.None;
        fillGo.transform.rotation = Quaternion.Euler(38f, CityDesign.SunYaw + 168f, 0f);

        RenderSettings.sun = key;
        RenderSettings.skybox = EnsureSky();

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.36f, 0.45f, 0.62f);
        RenderSettings.ambientEquatorColor = new Color(0.30f, 0.30f, 0.34f);
        RenderSettings.ambientGroundColor = new Color(0.12f, 0.11f, 0.12f);
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.reflectionIntensity = 1f;

        // Aerial perspective, and the thing that makes the raised far clip plane invisible. The fog
        // colour is pulled towards the sky's horizon rather than left neutral grey, because the
        // backdrop ring now sits inside it and a grey haze in front of a blue sky reads as a wall.
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.55f, 0.58f, 0.66f);
        RenderSettings.fogStartDistance = CityDesign.FogStart;
        RenderSettings.fogEndDistance = CityDesign.FogEnd;

        DynamicGI.UpdateEnvironment();
        BuildPostProcessing();
    }

    /// <summary>
    /// A post-processing volume local to this scene.
    ///
    /// It has to be local. The project's shared `DefaultVolumeProfile` carries a Bloom override with
    /// its intensity set to zero, which is right for Levels 1 and 2 and is not something Phase 6E is
    /// allowed to change - so every emissive surface this phase added (the signs, the crowns, the
    /// relay beacons, the lamp heads, the route strips) would render as a flat bright box and none
    /// of them would read as a light. A volume with its own profile, sitting only in this scene,
    /// overrides the default for this level and nothing else.
    ///
    /// The camera has to opt in as well: URP's <c>renderPostProcessing</c> defaults to off, and a
    /// volume nothing is looking through does nothing at all.
    /// </summary>
    private static void BuildPostProcessing()
    {
        VolumeProfile profile = EnsureProfile();

        if (profile == null)
        {
            return;
        }

        GameObject go = new GameObject("POST_PROCESSING");
        Volume volume = go.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 1f;
        volume.sharedProfile = profile;
    }

    private const string ProfilePath = "Assets/City/SkyboundCity_PostFX.asset";

    private static VolumeProfile EnsureProfile()
    {
        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);

        if (profile == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/City"))
            {
                AssetDatabase.CreateFolder("Assets", "City");
            }

            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
        }

        // Bloom, so the neon layer reads as light. The threshold sits above white on purpose:
        // CityKit.EnsureEmissive drives its emission past 1.0, and everything that is not emissive
        // stays under it, so a lit sign glows and a pale concrete cornice in full sun does not.
        Bloom bloom = Override<Bloom>(profile);
        bloom.threshold.Override(1.05f);
        bloom.intensity.Override(0.85f);
        bloom.scatter.Override(0.68f);

        // Neutral tonemapping, which is what stops a dusk sky from clipping to a flat band.
        Tonemapping tonemapping = Override<Tonemapping>(profile);
        tonemapping.mode.Override(TonemappingMode.Neutral);

        // A touch of cool in the shadows and warm in the highlights: the low sun is warm and the
        // sky fill is blue, and this is what keeps the two from averaging back out to grey.
        ColorAdjustments colour = Override<ColorAdjustments>(profile);
        colour.postExposure.Override(0.1f);
        colour.contrast.Override(8f);
        colour.saturation.Override(4f);

        Vignette vignette = Override<Vignette>(profile);
        vignette.intensity.Override(0.22f);
        vignette.smoothness.Override(0.4f);

        EditorUtility.SetDirty(profile);
        return profile;
    }

    /// <summary>
    /// Finds or adds one override on the profile, active. Rebuilding has to reuse the asset rather
    /// than replace it, or every rebuild would leave the scene pointing at a profile the previous
    /// one had already been saved against.
    /// </summary>
    private static T Override<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (!profile.TryGet(out T component))
        {
            component = profile.Add<T>(true);
        }

        component.active = true;
        return component;
    }

    /// <summary>
    /// A procedural sky, tinted to the same dusk the fog is. Unity's default skybox is a bright
    /// mid-blue that fights every one of the district palettes and washes the emissive layer out.
    /// </summary>
    private static Material EnsureSky()
    {
        const string path = CityKit.MaterialFolder + "/Mat_City_Sky.mat";
        Material sky = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (sky == null)
        {
            Shader shader = Shader.Find("Skybox/Procedural");

            if (shader == null)
            {
                Debug.LogWarning("[SkyboundCity] Skybox/Procedural is unavailable; the scene keeps " +
                                 "the default sky. Everything else in the art pass is unaffected.");
                return RenderSettings.skybox;
            }

            sky = new Material(shader) { name = "Mat_City_Sky" };
            AssetDatabase.CreateAsset(sky, path);
        }

        sky.SetFloat("_SunSize", 0.035f);
        sky.SetFloat("_SunSizeConvergence", 4f);
        sky.SetFloat("_AtmosphereThickness", 1.45f);
        sky.SetColor("_SkyTint", new Color(0.42f, 0.50f, 0.68f));
        sky.SetColor("_GroundColor", new Color(0.18f, 0.19f, 0.22f));
        sky.SetFloat("_Exposure", 1.15f);

        EditorUtility.SetDirty(sky);
        return sky;
    }

    private static GameObject BuildPlayer()
    {
        GameObject go = new GameObject("FPP_Player");
        go.transform.position = CityDesign.SpawnPosition;

        CharacterController cc = go.AddComponent<CharacterController>();
        cc.height = 2f;
        cc.radius = 0.35f;
        cc.center = new Vector3(0f, 1f, 0f);
        cc.slopeLimit = CityDesign.SlopeLimit;
        cc.stepOffset = 0.3f;
        cc.skinWidth = 0.04f;
        cc.minMoveDistance = 0.001f;

        GameObject cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        cam.tag = "MainCamera";
        cam.transform.SetParent(go.transform, false);
        cam.transform.localPosition = new Vector3(0f, 1.7f, 0f);

        Camera camera = cam.GetComponent<Camera>();
        camera.nearClipPlane = 0.1f;

        // PHASE 6E. Without this the scene's post-processing volume is inert: URP renders it only
        // for cameras that ask, and the default is not to. Antialiasing is on for the same reason
        // the sun was lowered - a city of hard vertical edges aliases badly at a distance, and the
        // backdrop ring is nothing but distant hard vertical edges.
        UniversalAdditionalCameraData urp = cam.AddComponent<UniversalAdditionalCameraData>();
        urp.renderPostProcessing = true;
        urp.antialiasing = AntialiasingMode.FastApproximateAntialiasing;

        // PHASE 6A RISK 3: 600 m clipped the tower from across the map.
        camera.farClipPlane = CityDesign.CameraFarClip;
        camera.fieldOfView = 70f;

        go.AddComponent<SlideAbility>();
        go.AddComponent<VaultDetector>();
        go.AddComponent<MantleDetector>();
        go.AddComponent<WallRunAbility>();
        go.AddComponent<ParkourCameraRig>();

        BasicFirstPersonController movement = go.AddComponent<BasicFirstPersonController>();
        SerializedObject so = new SerializedObject(movement);
        so.FindProperty("cameraPivot").objectReferenceValue = cam.transform;
        so.FindProperty("walkSpeed").floatValue = TraversalEnvelope.Default.Walk;
        so.FindProperty("sprintSpeed").floatValue = TraversalEnvelope.Default.Sprint;
        so.FindProperty("jumpHeight").floatValue = TraversalEnvelope.Default.JumpHeight;
        so.FindProperty("gravity").floatValue = TraversalEnvelope.Default.Gravity;

        // Phase 6D put a GameManager in the scene, so the controller's own fall reset is no longer
        // the recovery - it is the backstop underneath it. One storey below the death plane, so
        // FallDetector always raises a death the run counts before the controller silently
        // teleports the player home.
        so.FindProperty("fallResetHeight").floatValue = CityDesign.ControllerFallResetY;
        so.ApplyModifiedPropertiesWithoutUndo();

        go.AddComponent<PlayerFreezeController>();
        return go;
    }

    // ------------------------------------------------------------------ plan instantiation

    private static void BuildSlabs(CityPlanResult plan)
    {
        foreach (SlabPlan slab in plan.Slabs)
        {
            Transform parent = CityKit.Group(slab.GroupName);
            Material material = slab.Kind == CityPieceKind.Ground
                ? (IsAvenueSlab(slab) ? mAvenue : mGround)
                : MaterialFor(slab.Kind);

            CityKit.Slab(parent, slab.Name, slab.Footprint, slab.SurfaceY, slab.Thickness, material);
        }
    }

    /// <summary>Avenue and perimeter paving is darker, so the district grid reads from the air.</summary>
    private static bool IsAvenueSlab(SlabPlan slab)
        => slab.Name.Contains("Avenue") || slab.Name.Contains("Margin");

    private static void BuildBlocks(CityPlanResult plan)
    {
        foreach (BlockPlan block in plan.Blocks)
        {
            Transform parent = CityKit.Group(block.GroupName);
            Material material = MaterialFor(block.Kind);

            if (block.Collidable)
            {
                CityKit.Block(parent, block.Name, block.Footprint, block.BottomY, block.TopY,
                    material);
                continue;
            }

            float height = Mathf.Max(0.01f, block.TopY - block.BottomY);
            CityKit.Deco(parent, block.Name,
                new Vector3(block.Footprint.CentreX, block.BottomY + height * 0.5f,
                    block.Footprint.CentreZ),
                new Vector3(block.Footprint.Width, height, block.Footprint.Depth), material);
        }
    }

    private static void BuildRamps(CityPlanResult plan)
    {
        foreach (RampPlan ramp in plan.Ramps)
        {
            Transform parent = CityKit.Group(ramp.GroupName);
            Material material = ramp.GroupName == CityTraversal.TowerAscentGroup
                ? mTowerAscent
                : mCut;

            CityKit.Ramp(parent, ramp.Name, ramp.Centre, ramp.Size, ramp.PitchDegrees, material,
                ramp.YawDegrees);
        }
    }

    /// <summary>
    /// The scaffold uprights. Deco, so the frame reads as a frame without adding four colliders a
    /// player could catch on halfway up it - the working decks are the traversal, not the poles.
    /// </summary>
    private static void BuildScaffoldFrames(CityPlanResult plan)
    {
        Transform parent = CityKit.Group(CityTraversal.AscentGroup);

        foreach (AscentPlan ascent in plan.Traversal.Ascents)
        {
            if (ascent.Kind != AscentKind.Scaffold || ascent.Landings.Count == 0)
            {
                continue;
            }

            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float minZ = float.MaxValue;
            float maxZ = float.MinValue;

            foreach (CityRect landing in ascent.Landings)
            {
                minX = Mathf.Min(minX, landing.MinX);
                maxX = Mathf.Max(maxX, landing.MaxX);
                minZ = Mathf.Min(minZ, landing.MinZ);
                maxZ = Mathf.Max(maxZ, landing.MaxZ);
            }

            float[] xs = { minX + 0.1f, maxX - 0.1f };
            float[] zs = { minZ + 0.1f, maxZ - 0.1f };
            int index = 0;

            foreach (float x in xs)
            {
                foreach (float z in zs)
                {
                    index++;
                    CityKit.Pole(parent, $"{ascent.Name} Upright {index}".Replace(' ', '_'),
                        x, z, ascent.BaseY, ascent.TopY + 1.2f, 0.2f, mAscent);
                }
            }
        }
    }

    private static void BuildBuildings(CityPlanResult plan)
    {
        Dictionary<string, Transform> groups = new Dictionary<string, Transform>();

        foreach (BuildingPlan building in plan.Buildings)
        {
            if (!groups.TryGetValue(building.CellName, out Transform parent))
            {
                parent = CityKit.Group(building.CellName);
                groups[building.CellName] = parent;
            }

            CityKit.Block(parent, building.Name, building.Footprint, 0f, building.RoofY,
                MaterialFor(building.Group));
        }
    }

    // ------------------------------------------------------------------ Phase 6E: the art layer

    /// <summary>
    /// Every piece of Phase 6E art, as one loop.
    ///
    /// This method is short because the whole design of the phase is that it should be: the rules
    /// about what a facade band is, which roof edges may carry plant, which facade faces an avenue
    /// and where the backdrop ring sits all live in <see cref="CityDressing"/>, where the tests can
    /// reach them without opening a scene. What is left here is turning a box into a box.
    ///
    /// <c>CityKit.Detail</c> is the only call, and it forwards to <c>Deco</c>, so there is no path
    /// through this code that produces a collider. That is the Phase 6E invariant, and it is why
    /// none of the Phase 6B/6C/6D harnesses had to be re-run against a different city.
    /// </summary>
    private static void BuildDetails(CityPlanResult plan)
    {
        Dictionary<string, Transform> groups = new Dictionary<string, Transform>();

        foreach (DetailPlan detail in plan.Details)
        {
            if (!groups.TryGetValue(detail.GroupName, out Transform parent))
            {
                parent = DetailGroup(detail.GroupName);
                groups[detail.GroupName] = parent;
            }

            CityKit.Detail(parent, detail, DetailMaterial(detail.Surface, detail.Tint));
        }
    }

    private static Transform DetailGroup(string name)
        => name == CityDressing.GateDetailGroup
            ? CityKit.Group(name, CityKit.Group(CityObjectives.GateGroup))
            : CityKit.Group(name);

    private static void BuildDistrictLabels()
    {
        Transform parent = CityKit.Group("DISTRICT_MARKERS");

        foreach (DistrictCell cell in CityDesign.Cells)
        {
            CityRect bounds = cell.Bounds;
            CityKit.Label(parent, $"{cell.Name} ({cell.MinHeight:F1}-{cell.MaxHeight:F1} m)",
                new Vector3(bounds.CentreX, cell.MaxHeight + 8f, bounds.CentreZ));
        }

        CityKit.Label(parent, "PLAZA / START", new Vector3(0f, 6f, 0f));
        CityKit.Label(parent, "THE CUT", new Vector3(CityPlan.CutBounds().CentreX, 6f,
            CityPlan.CutBounds().CentreZ));
    }

    /// <summary>
    /// Scene-view names for the things Phase 6C built. The relays used to be labels here too;
    /// Phase 6D replaced them with real objects, which are their own labels.
    /// </summary>
    private static void BuildTraversalLabels(CityPlanResult plan)
    {
        Transform parent = CityKit.Group("TRAVERSAL_MARKERS");

        foreach (LinkPlan link in plan.Traversal.Links)
        {
            CityKit.Label(parent, link.Name, new Vector3(link.Deck.CentreX, link.DeckY + 2f,
                link.Deck.CentreZ));
        }

        CityKit.Label(parent, "SUMMIT",
            new Vector3(CityTraversal.ShaftFootprint.CentreX, CityDesign.TowerShaftTopY + 2f,
                CityTraversal.ShaftFootprint.CentreZ));
    }

    // ------------------------------------------------------------------ Phase 6D: the mission

    /// <summary>
    /// Turns the planned volumes into the objects that behave, and stands up the run systems that
    /// give them something to be part of.
    ///
    /// The wiring is all done here rather than by hand in the scene for the usual reason: the
    /// builder is the single source of truth, and a reference a person dragged in is a reference
    /// the next rebuild destroys.
    /// </summary>
    private static void BuildMission(CityPlanResult plan, GameObject player)
    {
        Transform objectives = CityKit.Group(CityObjectives.ObjectiveGroup);
        CityObjectivesResult mission = plan.Objectives;

        List<CheckpointVolume> route = new List<CheckpointVolume>();
        List<ObjectiveRelay> relays = new List<ObjectiveRelay>();
        GameObject finish = null;

        foreach (VolumePlan volume in plan.Volumes)
        {
            switch (volume.Kind)
            {
                case ObjectiveVolumeKind.Relay:
                    BuildRelay(objectives, volume, mission.Relay(volume.Owner), route, relays);
                    break;

                case ObjectiveVolumeKind.Anchor:
                    BuildAnchor(objectives, volume, Anchor(mission, volume.Owner));
                    break;

                default:
                    finish = BuildFinish(objectives, volume);
                    break;
            }
        }

        BuildSystems(plan, player, route, relays, finish);
    }

    private static AnchorObjective Anchor(CityObjectivesResult mission, string name)
    {
        foreach (AnchorObjective anchor in mission.Anchors)
        {
            if (anchor.Name == name)
            {
                return anchor;
            }
        }

        return null;
    }

    /// <summary>
    /// A trigger box standing on a surface, with a respawn point on that surface under it. The
    /// object itself is at the walking height, never at the box centre - the same rule
    /// <see cref="CityKit"/> holds for geometry.
    /// </summary>
    private static GameObject Trigger(Transform parent, in VolumePlan volume, float yaw)
    {
        GameObject go = new GameObject(volume.Name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(
            new Vector3(volume.Footprint.CentreX, volume.BottomY, volume.Footprint.CentreZ),
            Quaternion.Euler(0f, yaw, 0f));

        float height = Mathf.Max(0.01f, volume.TopY - volume.BottomY);

        BoxCollider box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.center = new Vector3(0f, height * 0.5f, 0f);
        box.size = new Vector3(volume.Footprint.Width, height, volume.Footprint.Depth);

        return go;
    }

    private static Transform RespawnPoint(GameObject owner)
    {
        GameObject point = new GameObject("Respawn");
        point.transform.SetParent(owner.transform, false);
        point.transform.localPosition = new Vector3(0f, CityDesign.RespawnLift, 0f);
        return point.transform;
    }

    private static void BuildRelay(Transform parent, in VolumePlan volume, RelayObjective site,
        List<CheckpointVolume> route, List<ObjectiveRelay> relays)
    {
        if (site == null)
        {
            Debug.LogWarning($"[SkyboundCity] {volume.Name} has no relay behind it.");
            return;
        }

        GameObject go = Trigger(parent, volume, site.Yaw);
        Transform respawn = RespawnPoint(go);

        CheckpointVolume checkpoint = go.AddComponent<CheckpointVolume>();
        SerializedObject cp = new SerializedObject(checkpoint);
        cp.FindProperty("respawnPoint").objectReferenceValue = respawn;
        cp.FindProperty("checkpointName").stringValue = $"{site.DisplayName} Relay";
        cp.FindProperty("logOnActivate").boolValue = true;
        cp.ApplyModifiedPropertiesWithoutUndo();

        RespawnAnchor anchor = go.AddComponent<RespawnAnchor>();
        SerializedObject an = new SerializedObject(anchor);
        an.FindProperty("anchorName").stringValue = $"{site.DisplayName} Relay";
        an.FindProperty("respawnPoint").objectReferenceValue = respawn;
        an.FindProperty("logOnActivate").boolValue = false;
        an.ApplyModifiedPropertiesWithoutUndo();

        ObjectiveRelay relay = go.AddComponent<ObjectiveRelay>();
        SerializedObject ro = new SerializedObject(relay);
        ro.FindProperty("relayId").stringValue = site.Name;
        ro.FindProperty("displayName").stringValue = site.DisplayName;
        ro.FindProperty("volume").objectReferenceValue = checkpoint;
        ro.FindProperty("idleMaterial").objectReferenceValue = mObjective;
        ro.FindProperty("capturedMaterial").objectReferenceValue = mObjectiveDone;

        // The plinth and the mast, which CityObjectives emitted as decoration under this group,
        // and the halo and beacon Phase 6E added under its own. All four turn from cyan to green
        // together: a relay that changed colour in two places out of four would read as broken.
        List<Renderer> faces = new List<Renderer>();
        AddRenderer(faces, parent, $"{site.Name}_Pad");
        AddRenderer(faces, parent, $"{site.Name}_Mast");

        Transform dressing = CityKit.Group(CityDressing.ObjectiveGroup);
        AddRenderer(faces, dressing, $"{site.Name}_Halo");
        AddRenderer(faces, dressing, $"{site.Name}_Beacon");

        SerializedProperty status = ro.FindProperty("statusRenderers");
        status.arraySize = faces.Count;

        for (int i = 0; i < faces.Count; i++)
        {
            status.GetArrayElementAtIndex(i).objectReferenceValue = faces[i];
        }

        ro.ApplyModifiedPropertiesWithoutUndo();

        route.Add(checkpoint);
        relays.Add(relay);
    }

    private static void AddRenderer(List<Renderer> into, Transform parent, string name)
    {
        Transform found = parent.Find(name);
        Renderer renderer = found != null ? found.GetComponent<Renderer>() : null;

        if (renderer != null)
        {
            into.Add(renderer);
        }
    }

    private static void BuildAnchor(Transform parent, in VolumePlan volume, AnchorObjective site)
    {
        if (site == null)
        {
            Debug.LogWarning($"[SkyboundCity] {volume.Name} has no anchor behind it.");
            return;
        }

        GameObject go = Trigger(parent, volume, site.Yaw);
        Transform respawn = RespawnPoint(go);

        RespawnAnchor anchor = go.AddComponent<RespawnAnchor>();
        SerializedObject an = new SerializedObject(anchor);
        an.FindProperty("anchorName").stringValue = site.Name;
        an.FindProperty("respawnPoint").objectReferenceValue = respawn;
        an.FindProperty("logOnActivate").boolValue = true;
        an.ApplyModifiedPropertiesWithoutUndo();
    }

    private static GameObject BuildFinish(Transform parent, in VolumePlan volume)
    {
        GameObject go = Trigger(parent, volume, 0f);

        FinishLine finish = go.AddComponent<FinishLine>();
        SerializedObject so = new SerializedObject(finish);

        // Both gates on the tower, and deliberately: the hoarding is what a player sees, and this
        // is what makes the rule true even if they find a way past it.
        so.FindProperty("requireAllCheckpoints").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();

        return go;
    }

    /// <summary>
    /// The run systems. Identical in kind to the ones Levels 1 and 2 carry - the level-specific
    /// part is three serialized values: the checkpoint route is a Set, the fall impact detector is
    /// present, and the fatal fall comes from the design.
    /// </summary>
    private static void BuildSystems(CityPlanResult plan, GameObject player,
        List<CheckpointVolume> route, List<ObjectiveRelay> relays, GameObject finish)
    {
        GameObject systems = new GameObject("MISSION");

        GameObject start = new GameObject("LevelStart");
        start.transform.SetParent(systems.transform, false);
        start.transform.SetPositionAndRotation(CityDesign.SpawnPosition, Quaternion.identity);

        RunTimer timer = systems.AddComponent<RunTimer>();
        CheckpointManager checkpoints = systems.AddComponent<CheckpointManager>();
        RespawnManager respawn = systems.AddComponent<RespawnManager>();
        FallDetector fall = systems.AddComponent<FallDetector>();
        FallImpactDetector impact = systems.AddComponent<FallImpactDetector>();
        ObjectiveTracker tracker = systems.AddComponent<ObjectiveTracker>();
        GameManager game = systems.AddComponent<GameManager>();

        // Who this level is, for the menu, the loading screen and the record store. It is the same
        // `LevelEntry` asset the menu's PLAY screen reads, so the level and the menu can never
        // disagree about its name or which records are its own - and `LevelInfo.RecordKey` falls
        // back to the scene name, so a missing asset degrades to correct-but-unnamed rather than to
        // records shared with another level.
        LevelInfo info = systems.AddComponent<LevelInfo>();
        LevelEntry entry = AssetDatabase.LoadAssetAtPath<LevelEntry>(LevelEntryPath);

        if (entry == null)
        {
            Debug.LogWarning($"[SkyboundCity] {LevelEntryPath} is missing, so the level will be " +
                             "named after its scene rather than after the catalogue.");
        }

        SerializedObject li = new SerializedObject(info);
        li.FindProperty("entry").objectReferenceValue = entry;
        li.FindProperty("displayName").stringValue = "SKYBOUND CITY";
        li.FindProperty("subtitle").stringValue = "Six Districts  -  The Main Run";
        li.FindProperty("recordKey").stringValue = "SkyboundCity";
        li.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject cm = new SerializedObject(checkpoints);
        SerializedProperty list = cm.FindProperty("checkpoints");
        list.arraySize = route.Count;

        for (int i = 0; i < route.Count; i++)
        {
            list.GetArrayElementAtIndex(i).objectReferenceValue = route[i];
        }

        // The one line that makes the mission order-free.
        cm.FindProperty("order").enumValueIndex = (int)CheckpointRouteOrder.Set;
        cm.FindProperty("runTimer").objectReferenceValue = timer;
        cm.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject rm = new SerializedObject(respawn);
        rm.FindProperty("player").objectReferenceValue = player.GetComponent<PlayerFreezeController>();
        rm.FindProperty("levelStart").objectReferenceValue = start.transform;
        rm.FindProperty("checkpoints").objectReferenceValue = checkpoints;
        rm.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject fd = new SerializedObject(fall);
        fd.FindProperty("target").objectReferenceValue = player.transform;
        fd.FindProperty("deathHeight").floatValue = CityDesign.DeathPlaneY;
        fd.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject fi = new SerializedObject(impact);
        fi.FindProperty("target").objectReferenceValue = player.GetComponent<CharacterController>();
        fi.FindProperty("fatalFallHeight").floatValue = CityDesign.FatalFallHeight;
        fi.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject ot = new SerializedObject(tracker);
        ot.FindProperty("checkpoints").objectReferenceValue = checkpoints;
        ot.FindProperty("game").objectReferenceValue = game;
        ot.FindProperty("relayRoot").objectReferenceValue =
            CityKit.Group(CityObjectives.ObjectiveGroup);
        ot.FindProperty("towerGate").objectReferenceValue =
            CityKit.Group(CityObjectives.GateGroup).gameObject;
        ot.FindProperty("summit").objectReferenceValue = finish != null ? finish.transform : null;
        ot.FindProperty("summitName").stringValue = CityObjectives.SummitName;

        SerializedProperty relayList = ot.FindProperty("relays");
        relayList.arraySize = relays.Count;

        for (int i = 0; i < relays.Count; i++)
        {
            relayList.GetArrayElementAtIndex(i).objectReferenceValue = relays[i];
        }

        ot.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject gm = new SerializedObject(game);
        gm.FindProperty("runTimer").objectReferenceValue = timer;
        gm.FindProperty("checkpoints").objectReferenceValue = checkpoints;
        gm.FindProperty("respawn").objectReferenceValue = respawn;
        gm.FindProperty("player").objectReferenceValue = player.GetComponent<PlayerFreezeController>();
        gm.FindProperty("fallDetector").objectReferenceValue = fall;
        gm.FindProperty("fallImpact").objectReferenceValue = impact;
        gm.ApplyModifiedPropertiesWithoutUndo();

        BuildMissionHud(tracker, player.transform);
        BuildRouteGuide(plan, tracker, player.transform);
    }

    // ------------------------------------------------------------------ route guidance

    /// <summary>
    /// The world-space trail to the current objective: a fixed pool of chevrons, a pillar of light
    /// over the target, and the navigation graph baked into the component that drives them.
    ///
    /// Not parented to <see cref="CityKit.WorldRoot"/>, and that is deliberate rather than
    /// convenient. WORLD is the city: everything under it is static, batched, occluded, lightmapped
    /// and counted by the Phase 6A budgets in the massing report - and every one of those is wrong
    /// for objects that move every frame. The guide is a heads-up display that happens to be drawn
    /// in world space, so it lives beside the HUD canvas rather than inside the city, and Harness B
    /// still measures the city rather than the interface.
    ///
    /// The markers carry no collider, like everything else this phase added.
    /// </summary>
    private static void BuildRouteGuide(CityPlanResult plan, ObjectiveTracker tracker,
        Transform playerBody)
    {
        CityNavigation.Result nav = CityNavigation.Build(plan);

        if (nav.Problems.Count > 0)
        {
            Debug.LogWarning("[SkyboundCity] route guidance problems:\n  " +
                             string.Join("\n  ", nav.Problems));
        }

        GameObject root = new GameObject("ROUTE_GUIDE");
        root.AddComponent<RouteGuide>();

        Transform[] markers = new Transform[CityDesign.GuideMarkerCount];

        for (int i = 0; i < markers.Length; i++)
        {
            markers[i] = BuildChevron(root.transform, i);
        }

        Transform[] actions = new Transform[CityDesign.GuideActionMarkerCount];

        for (int i = 0; i < actions.Length; i++)
        {
            actions[i] = BuildActionMarker(root.transform, i);
        }

        Transform beacon = BuildBeacon(root.transform);

        RouteGuide guide = root.GetComponent<RouteGuide>();
        SerializedObject rg = new SerializedObject(guide);
        rg.FindProperty("tracker").objectReferenceValue = tracker;
        rg.FindProperty("player").objectReferenceValue = playerBody;
        rg.FindProperty("beacon").objectReferenceValue = beacon;

        Fill(rg, "markers", markers.Length, (p, i) => p.objectReferenceValue = markers[i]);
        Fill(rg, "actionMarkers", actions.Length, (p, i) => p.objectReferenceValue = actions[i]);

        CityNavGraph graph = nav.Graph;

        Fill(rg, "nodeNames", graph.Nodes.Count,
            (p, i) => p.stringValue = graph.Nodes[i].Name);
        Fill(rg, "nodeKinds", graph.Nodes.Count,
            (p, i) => p.intValue = (int)graph.Nodes[i].Kind);
        Fill(rg, "nodePositions", graph.Nodes.Count,
            (p, i) => p.vector3Value = graph.Nodes[i].Position);
        Fill(rg, "nodeExtents", graph.Nodes.Count,
            (p, i) => p.vector3Value = graph.Nodes[i].Extent);

        Fill(rg, "linkFrom", graph.Links.Count, (p, i) => p.intValue = graph.Links[i].From);
        Fill(rg, "linkTo", graph.Links.Count, (p, i) => p.intValue = graph.Links[i].To);
        Fill(rg, "linkCost", graph.Links.Count, (p, i) => p.floatValue = graph.Links[i].Cost);
        Fill(rg, "linkExit", graph.Links.Count, (p, i) => p.vector3Value = graph.Links[i].Exit);
        Fill(rg, "linkTier", graph.Links.Count, (p, i) => p.intValue = (int)graph.Links[i].Tier);
        Fill(rg, "linkMove", graph.Links.Count, (p, i) => p.intValue = (int)graph.Links[i].Move);

        List<string> ids = new List<string>(nav.Targets.Keys);
        ids.Sort(System.StringComparer.Ordinal);

        Fill(rg, "targetIds", ids.Count, (p, i) => p.stringValue = ids[i]);
        Fill(rg, "targetNodes", ids.Count, (p, i) => p.stringValue = nav.Targets[ids[i]]);

        rg.ApplyModifiedPropertiesWithoutUndo();

        Debug.Log($"[SkyboundCity] Route guidance: {graph.Nodes.Count} nav nodes " +
                  $"({nav.StreetNodes} street, {nav.FootNodes} ways up, {nav.SurfaceNodes} " +
                  $"surfaces), {graph.Links.Count} links, {ids.Count} targets, " +
                  $"{markers.Length} chevrons, {actions.Length} action markers.");
    }

    private static void Fill(SerializedObject target, string property, int count,
        System.Action<SerializedProperty, int> set)
    {
        SerializedProperty array = target.FindProperty(property);
        array.arraySize = count;

        for (int i = 0; i < count; i++)
        {
            set(array.GetArrayElementAtIndex(i), i);
        }
    }

    /// <summary>
    /// One chevron: two bars meeting at a point, with the point at local +Z. Built rather than
    /// modelled so it stays in the same primitive vocabulary as the rest of the city, and so its
    /// arms can be sized from <see cref="CityDesign.GuideMarkerSize"/> like everything else here.
    /// </summary>
    private static Transform BuildChevron(Transform parent, int index)
    {
        GameObject go = new GameObject($"Chevron_{index:00}");
        go.transform.SetParent(parent, false);

        ChevronArms(go.transform, CityDesign.GuideMarkerSize, 0f,
            DetailMaterial(DetailSurface.Route, DistrictGroup.Landmark));

        go.SetActive(false);
        return go.transform;
    }

    /// <summary>
    /// Two bars meeting at a point, the point at local +Z.
    ///
    /// <b>The point has to be at +Z</b>, because +Z is where `RouteGuide` aims the marker: it calls
    /// `Quaternion.LookRotation` with the direction of travel, which puts the object's forward axis
    /// along the route. The first version of this built the arms the other way round - centred at
    /// -0.16 and splayed by +/-38 degrees, so they converged behind the origin and opened out in
    /// front of it - and every cyan chevron in the city was therefore an arrowhead aimed exactly
    /// backwards along the player's route. The fix is here in the geometry rather than a negated
    /// heading at the other end, because the heading was never wrong: `Breadcrumb.Forward` is the
    /// direction from this route point to the next one, and the tests, the action markers and the
    /// beacon all read it that way.
    /// </summary>
    private static void ChevronArms(Transform parent, float s, float lift, Material material)
    {
        for (int arm = 0; arm < 2; arm++)
        {
            float sign = arm == 0 ? -1f : 1f;

            GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = arm == 0 ? "Left" : "Right";
            bar.transform.SetParent(parent, false);
            bar.transform.localPosition = GuideChevron.ArmCentre(s, sign, lift);
            bar.transform.localRotation = Quaternion.Euler(0f, GuideChevron.ArmYaw(sign), 0f);
            bar.transform.localScale = GuideChevron.ArmScale(s);

            MeshRenderer renderer = bar.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            Object.DestroyImmediate(bar.GetComponent<Collider>());
        }
    }

    /// <summary>
    /// An upright marker for a transition: a post with a chevron on top, pointing the way the
    /// player leaves. Amber rather than cyan, because it means something different from the trail -
    /// the chevrons say "this way", and this says "and here is where you climb".
    ///
    /// The doc said "with a chevron on top" before there was one. The post and its rungs are
    /// symmetric about both horizontal axes, so the marker that stands at the foot of a fire escape
    /// or at the mouth of a skybridge said which spot but never which way - and `RouteGuide` aims
    /// it down the route just as carefully as it aims a ground chevron. It now carries the same
    /// arms, built by the same method, so the two cannot disagree about which way is forward.
    /// </summary>
    private static Transform BuildActionMarker(Transform parent, int index)
    {
        GameObject go = new GameObject($"Action_{index:00}");
        go.transform.SetParent(parent, false);

        float h = CityDesign.GuideActionMarkerHeight;
        float w = CityDesign.GuideActionMarkerWidth;
        Material post = DetailMaterial(DetailSurface.Hazard, DistrictGroup.Industrial);
        Material head = DetailMaterial(DetailSurface.Lamp, DistrictGroup.Landmark);

        Bar(go.transform, "Post", new Vector3(0f, h * 0.5f, 0f), new Vector3(w * 0.3f, h, w * 0.3f),
            Quaternion.identity, post);

        // Three rungs climbing the post, so it reads as "up" from any angle - which is what an
        // upright marker at the foot of a fire escape has to say before anything else.
        for (int rung = 0; rung < 3; rung++)
        {
            float y = h * (0.45f + rung * 0.2f);
            float scale = 1f - rung * 0.22f;

            Bar(go.transform, $"Rung{rung}", new Vector3(0f, y, 0f),
                new Vector3(w * scale, w * 0.16f, w * 0.16f), Quaternion.identity, head);
        }

        // And the heading, on top, where it is read against the skyline rather than against the
        // roof. Same arms as the trail's, so it points the same way the chevrons leading to it do.
        ChevronArms(go.transform, CityDesign.GuideMarkerSize, h, head);

        go.SetActive(false);
        return go.transform;
    }

    private static void Bar(Transform parent, string name, Vector3 position, Vector3 scale,
        Quaternion rotation, Material material)
    {
        GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bar.name = name;
        bar.transform.SetParent(parent, false);
        bar.transform.localPosition = position;
        bar.transform.localRotation = rotation;
        bar.transform.localScale = scale;

        MeshRenderer renderer = bar.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        Object.DestroyImmediate(bar.GetComponent<Collider>());
    }

    /// <summary>
    /// The pillar over the active objective. Tall enough to clear the tower, because the tower is
    /// the tallest thing that can ever stand between a player and a relay.
    /// </summary>
    private static Transform BuildBeacon(Transform parent)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Objective Beacon";
        go.transform.SetParent(parent, false);
        go.transform.localScale = new Vector3(CityDesign.GuideBeaconWidth,
            CityDesign.GuideBeaconHeight, CityDesign.GuideBeaconWidth);

        MeshRenderer renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = mObjective;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        Object.DestroyImmediate(go.GetComponent<Collider>());

        go.SetActive(false);
        return go.transform;
    }

    // ------------------------------------------------------------------ the mission HUD

    /// <summary>
    /// Relay count, target, bearing, distance, and whether the tower is open. Five numbers, which
    /// is what an order-free objective set is unplayable without and is also all of it - the pause,
    /// countdown, death and level-complete panels are Phase 6F's, built by `GameplayUIBuilder`
    /// against the same run systems this scene now carries.
    ///
    /// PHASE 6E rebuilt the presentation of those same five numbers and changed none of them. The
    /// Phase 6D version was four centred labels stacked down the middle of the screen over a plain
    /// square - correct, and unmistakably a debug readout. This is one instrument panel drawn to
    /// <see cref="UITheme"/>: the same fills, borders, type scale, tracking and three-family font
    /// set as the pause menu, the level-complete panel and the main menu, so the level does not look
    /// like it belongs to a different game from its own menus.
    ///
    /// Everything is laid out inside one frame rect in local coordinates, which is what makes the
    /// numbers below readable as a design rather than as a column of magic screen positions.
    /// </summary>
    private static void BuildMissionHud(ObjectiveTracker tracker, Transform playerBody)
    {
        if (!UIFontCatalog.TryLoad(out UIFontSet fonts))
        {
            Debug.LogWarning("[SkyboundCity] UI fonts unavailable; the mission HUD was skipped. " +
                             "The mission is still playable, just unguided.");
            return;
        }

        GameObject rootGo = new GameObject("MISSION_HUD", typeof(Canvas), typeof(CanvasScaler));
        Canvas canvas = rootGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = rootGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 1f;

        RectTransform root = (RectTransform)rootGo.transform;

        const float frameWidth = 660f;
        const float frameHeight = 152f;
        const float frameTop = 28f;

        RectTransform frame = Panel(root, "Frame", new Vector2(0.5f, 1f),
            new Vector2(0f, -(frameHeight * 0.5f + frameTop)),
            new Vector2(frameWidth, frameHeight), UITheme.PanelFill);

        // The accent rule down the left edge and the hairline under the frame. Both are lifted
        // straight from the level-complete panel, and they are what stop a translucent rectangle
        // from reading as a placeholder.
        Bar(frame, "Accent", new Vector2(-frameWidth * 0.5f + 2f, 0f), new Vector2(4f, frameHeight),
            UITheme.Cyan);
        Bar(frame, "Underline", new Vector2(0f, -frameHeight * 0.5f + 1f),
            new Vector2(frameWidth, 2f), UITheme.PanelBorder);

        RectTransform needle = BuildDial(frame, new Vector2(-244f, 0f), 112f);

        // --- the readout column -------------------------------------------------------
        const float columnLeft = -166f;

        TMP_Text eyebrow = Field(frame, "Eyebrow", "OBJECTIVE", UITheme.StatLabel, UITheme.Label,
            columnLeft, 46f, 250f, 34f, UIFontRole.Mono);
        eyebrow.characterSpacing = UITheme.EyebrowSpacing;

        TMP_Text target = Field(frame, "Target", "RELAY", 34f, UITheme.White,
            columnLeft, 6f, 300f, 58f, UIFontRole.Display);
        target.characterSpacing = UITheme.DisplaySpacing;

        TMP_Text distance = Field(frame, "Distance", "0 m", 28f, UITheme.Cyan,
            columnLeft, -42f, 250f, 44f, UIFontRole.Mono);
        distance.characterSpacing = UITheme.LabelSpacing;

        // --- the counter block --------------------------------------------------------
        Bar(frame, "Divider", new Vector2(150f, 0f), new Vector2(2f, 108f), UITheme.PanelBorder);

        TMP_Text counter = Centred(frame, "Counter", "0 / 5", UITheme.StatValueLarge, UITheme.White,
            new Vector2(240f, 16f), new Vector2(170f, 86f), UIFontRole.Display);
        counter.characterSpacing = UITheme.DisplaySpacing;

        TMP_Text relaysLabel = Centred(frame, "CounterLabel", "RELAYS", UITheme.StatLabel,
            UITheme.Label, new Vector2(240f, -30f), new Vector2(170f, 34f), UIFontRole.Mono);
        relaysLabel.characterSpacing = UITheme.EyebrowSpacing;

        List<Image> pips = new List<Image>();

        for (int i = 0; i < 5; i++)
        {
            RectTransform pip = Empty(frame, "Pip" + i, new Vector2(0.5f, 0.5f),
                new Vector2(240f + (i - 2) * 28f, -58f), new Vector2(22f, 5f));

            Image image = pip.gameObject.AddComponent<Image>();
            image.color = UITheme.PanelBorder;
            image.raycastTarget = false;
            pips.Add(image);
        }

        // --- the tower's state --------------------------------------------------------
        RectTransform statusPlate = Panel(root, "Status", new Vector2(0.5f, 1f),
            new Vector2(0f, -(frameHeight + frameTop + 26f)), new Vector2(380f, 40f),
            UITheme.PanelFillSoft);

        TMP_Text status = Centred(statusPlate, "StatusLabel", "TOWER LOCKED", UITheme.StatLabel,
            UITheme.Orange, Vector2.zero, new Vector2(360f, 34f), UIFontRole.Mono);
        status.characterSpacing = UITheme.EyebrowSpacing;

        ObjectiveCompass compass = rootGo.AddComponent<ObjectiveCompass>();
        SerializedObject oc = new SerializedObject(compass);
        oc.FindProperty("tracker").objectReferenceValue = tracker;
        oc.FindProperty("playerBody").objectReferenceValue = playerBody;
        oc.FindProperty("needle").objectReferenceValue = needle;
        oc.FindProperty("targetLabel").objectReferenceValue = target;
        oc.FindProperty("distanceLabel").objectReferenceValue = distance;
        oc.FindProperty("counterLabel").objectReferenceValue = counter;
        oc.FindProperty("statusLabel").objectReferenceValue = status;

        SerializedProperty pipList = oc.FindProperty("relayPips");
        pipList.arraySize = pips.Count;

        for (int i = 0; i < pips.Count; i++)
        {
            pipList.GetArrayElementAtIndex(i).objectReferenceValue = pips[i];
        }

        oc.ApplyModifiedPropertiesWithoutUndo();

        // --- local helpers -------------------------------------------------------------

        // A left-aligned field. The rect is placed by its left edge rather than its centre, which
        // is the only way a three-line column stays flush when the three lines are different sizes.
        TMP_Text Field(RectTransform parent, string name, string content, float size, Color colour,
            float left, float y, float width, float height, UIFontRole role)
        {
            RectTransform rect = Empty(parent, name, new Vector2(0.5f, 0.5f),
                new Vector2(left + width * 0.5f, y), new Vector2(width, height));

            return Text(rect, content, size, colour, role, TextAlignmentOptions.Left);
        }

        TMP_Text Centred(RectTransform parent, string name, string content, float size, Color colour,
            Vector2 offset, Vector2 box, UIFontRole role)
        {
            RectTransform rect = Empty(parent, name, new Vector2(0.5f, 0.5f), offset, box);
            return Text(rect, content, size, colour, role, TextAlignmentOptions.Center);
        }

        TMP_Text Text(RectTransform rect, string content, float size, Color colour, UIFontRole role,
            TextAlignmentOptions alignment)
        {
            TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = fonts.Resolve(role);
            text.text = content;
            text.fontSize = size;
            text.color = colour;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            text.overflowMode = TextOverflowModes.Overflow;
            return text;
        }
    }

    /// <summary>
    /// The compass dial: a plate, a hairline ring, four cardinal ticks and the needle.
    ///
    /// The ticks are what turn a rotating arrow into an instrument. Without them a needle 40 degrees
    /// off and a needle 140 degrees off look much the same at a glance, and a glance is the only
    /// kind of look this thing ever gets while the player is running.
    /// </summary>
    private static RectTransform BuildDial(RectTransform parent, Vector2 centre, float size)
    {
        RectTransform dial = Panel(parent, "Dial", new Vector2(0.5f, 0.5f), centre,
            new Vector2(size, size), UITheme.PanelFillSoft);

        float half = size * 0.5f;

        Bar(dial, "RingTop", new Vector2(0f, half - 1f), new Vector2(size, 2f), UITheme.PanelBorder);
        Bar(dial, "RingBottom", new Vector2(0f, -half + 1f), new Vector2(size, 2f),
            UITheme.PanelBorder);
        Bar(dial, "RingLeft", new Vector2(-half + 1f, 0f), new Vector2(2f, size),
            UITheme.PanelBorder);
        Bar(dial, "RingRight", new Vector2(half - 1f, 0f), new Vector2(2f, size),
            UITheme.PanelBorder);

        // Ahead is brighter than the other three: the needle points at the target, and the tick it
        // lines up with has to be the one that means "straight on".
        Bar(dial, "TickAhead", new Vector2(0f, half - 10f), new Vector2(3f, 12f), UITheme.Cyan);
        Bar(dial, "TickBehind", new Vector2(0f, -half + 10f), new Vector2(3f, 10f), UITheme.Dim);
        Bar(dial, "TickLeft", new Vector2(-half + 10f, 0f), new Vector2(10f, 3f), UITheme.Dim);
        Bar(dial, "TickRight", new Vector2(half - 10f, 0f), new Vector2(10f, 3f), UITheme.Dim);

        RectTransform needle = Empty(dial, "Needle", new Vector2(0.5f, 0.5f), Vector2.zero,
            new Vector2(48f, 96f));
        Bar(needle, "Tail", new Vector2(0f, -18f), new Vector2(5f, 30f), UITheme.Dim);
        Bar(needle, "Shaft", new Vector2(0f, 6f), new Vector2(6f, 44f), UITheme.Cyan);
        Bar(needle, "Head", new Vector2(0f, 30f), new Vector2(18f, 18f), UITheme.CyanBright, 45f);

        return needle;
    }

    private static RectTransform Empty(RectTransform parent, string name, Vector2 anchor,
        Vector2 offset, Vector2 size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = offset;
        return rect;
    }

    private static RectTransform Panel(RectTransform parent, string name, Vector2 anchor,
        Vector2 offset, Vector2 size, Color fill)
    {
        RectTransform rect = Empty(parent, name, anchor, offset, size);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = fill;
        image.raycastTarget = false;
        return rect;
    }

    private static void Bar(RectTransform parent, string name, Vector2 offset, Vector2 size,
        Color colour, float rotation = 0f)
    {
        RectTransform rect = Empty(parent, name, new Vector2(0.5f, 0.5f), offset, size);
        rect.localRotation = Quaternion.Euler(0f, 0f, rotation);

        Image image = rect.gameObject.AddComponent<Image>();
        image.color = colour;
        image.raycastTarget = false;
    }
}
