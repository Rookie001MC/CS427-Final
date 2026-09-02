using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The modular part factory for Skybound City. Phase 6B needs only the massing primitives; the
/// facade, roof, vertical and street part groups from the Phase 6A report arrive in 6C and 6E and
/// belong in here beside them.
///
/// Two rules are enforced by the shape of this class rather than by discipline:
///
///   1. <b>Collider discipline.</b> There are exactly two ways to make geometry -
///      <see cref="Solid"/>, which keeps its collider, and <see cref="Deco"/>, which destroys it.
///      Neon District runs 761 renderers against 222 colliders because `CyberCityBuilder` held
///      that line; a city four times the size cannot afford to lose it.
///   2. <b>Surfaces are named by their walking height</b>, never by a box centre. Every jump the
///      route harnesses measure is measured off a roof or a deck, so an author who has to
///      subtract half a thickness in their head will eventually get it wrong.
///
/// Everything is marked static at build time. The Phase 6A inspection found zero static flags in
/// either shipped scene, which blocks static batching, occlusion culling and lightmapping - all of
/// which Phase 6G needs and none of which cost anything to enable now.
/// </summary>
public static class CityKit
{
    public const string MaterialFolder = "Assets/City/Materials";

    /// <summary>Root of everything the city builder creates.</summary>
    public const string WorldRoot = "WORLD";

    // ------------------------------------------------------------------ materials

    /// <summary>
    /// Finds or creates a flat URP/Lit material. Greybox is deliberately unlit-looking and
    /// untextured: the districts are told apart by value and hue only, which is what makes a
    /// silhouette test meaningful.
    /// </summary>
    public static Material Ensure(string name, Color baseColor, float smoothness = 0.08f,
        float metallic = 0f)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            material = new Material(shader) { name = name };
            EnsureFolder(MaterialFolder);
            AssetDatabase.CreateAsset(material, path);
        }

        material.SetColor("_BaseColor", baseColor);

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", baseColor);
        }

        material.SetFloat("_Smoothness", smoothness);
        material.SetFloat("_Metallic", metallic);
        material.DisableKeyword("_EMISSION");
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;

        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>
    /// Finds or creates a self-lit material. Phase 6E's signs, crowns, beacons, lamp heads and
    /// route strips are the only things in the city that emit, and they all come from here so that
    /// "what glows" is one list rather than a habit.
    ///
    /// The emissive colour is kept above 1.0 deliberately: URP's bloom keys off values past white,
    /// and a sign at exactly 1.0 reads as a light grey box in daylight rather than as a lit one.
    /// <c>EmissiveIsBlack</c> is cleared as well, or a baked lighting pass would ignore every one
    /// of them - which matters in Phase 6G and costs nothing now.
    /// </summary>
    public static Material EnsureEmissive(string name, Color baseColor, Color emission,
        float intensity, float smoothness = 0.5f)
    {
        Material material = Ensure(name, baseColor, smoothness);

        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", emission * intensity);
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

        EditorUtility.SetDirty(material);
        return material;
    }

    private static void EnsureFolder(string assetPath)
    {
        if (AssetDatabase.IsValidFolder(assetPath))
        {
            return;
        }

        string[] parts = assetPath.Split('/');
        string cursor = parts[0];

        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{cursor}/{parts[i]}";

            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(cursor, parts[i]);
            }

            cursor = next;
        }
    }

    // ------------------------------------------------------------------ hierarchy

    public static Transform Group(string name)
    {
        GameObject world = GameObject.Find(WorldRoot);

        if (world == null)
        {
            world = new GameObject(WorldRoot);
        }

        Transform existing = world.transform.Find(name);

        if (existing != null)
        {
            return existing;
        }

        GameObject group = new GameObject(name);
        group.transform.SetParent(world.transform, false);
        return group.transform;
    }

    /// <summary>
    /// A group nested inside another. Used by exactly one thing, and it is worth saying why rather
    /// than leaving it as a general facility: `ObjectiveTracker` opens the tower by deactivating the
    /// gate's group, so the Phase 6E chevrons and beacons on that gate have to hang underneath it or
    /// they would still be floating in the air over an opened spiral.
    /// </summary>
    public static Transform Group(string name, Transform parent)
    {
        Transform existing = parent.Find(name);

        if (existing != null)
        {
            return existing;
        }

        GameObject group = new GameObject(name);
        group.transform.SetParent(parent, false);
        return group.transform;
    }

    /// <summary>
    /// Everything the city builder emits is environment: it never moves, so it may be batched,
    /// occluded, lightmapped and reflected. Named rather than cast from ~0, so a future Unity
    /// version adding a flag does not silently change what this means.
    ///
    /// The two navigation flags are deliberately absent - they are deprecated in Unity 6, and the
    /// Phase 6A inspection concluded AI Navigation is not needed by this game at all.
    /// </summary>
    private const StaticEditorFlags EnvironmentStatic =
        StaticEditorFlags.ContributeGI |
        StaticEditorFlags.OccluderStatic |
        StaticEditorFlags.OccludeeStatic |
        StaticEditorFlags.BatchingStatic |
        StaticEditorFlags.ReflectionProbeStatic;

    // ------------------------------------------------------------------ primitives

    /// <summary>Collidable box. Use for anything the player may stand on, land on or hit.</summary>
    public static GameObject Solid(Transform parent, string name, Vector3 centre, Vector3 size,
        Material material)
        => Primitive(parent, name, centre, size, material, keepCollider: true);

    /// <summary>
    /// Decoration. The collider is destroyed, so it can never catch a falling player, block a
    /// parkour probe, or become an unintended shortcut.
    /// </summary>
    public static GameObject Deco(Transform parent, string name, Vector3 centre, Vector3 size,
        Material material)
        => Primitive(parent, name, centre, size, material, keepCollider: false);

    /// <summary>
    /// Decoration that is not axis-aligned: the crane's ties, the rails and route strips on the
    /// tower spiral's pitched runs, and the yawed blocks of the backdrop ring. Same rotation
    /// convention as <see cref="Ramp"/>, so a reader only has to learn it once.
    /// </summary>
    public static GameObject Deco(Transform parent, string name, Vector3 centre, Vector3 size,
        Material material, float pitchDegrees, float yawDegrees)
    {
        GameObject go = Primitive(parent, name, centre, size, material, keepCollider: false);
        go.transform.localRotation = Quaternion.Euler(pitchDegrees, yawDegrees, 0f);
        return go;
    }

    /// <summary>
    /// Phase 6E's one call. Named for what it is - a piece of art - because the rule that matters
    /// about it is the one in the name of the method it forwards to: it can never have a collider,
    /// so it can never change what the traversal harnesses measure.
    /// </summary>
    public static GameObject Detail(Transform parent, in DetailPlan detail, Material material)
        => detail.IsRotated
            ? Deco(parent, detail.Name, detail.Centre, detail.Size, material, detail.PitchDegrees,
                detail.YawDegrees)
            : Deco(parent, detail.Name, detail.Centre, detail.Size, material);

    private static GameObject Primitive(Transform parent, string name, Vector3 centre, Vector3 size,
        Material material, bool keepCollider)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = centre;
        go.transform.localScale = size;

        MeshRenderer renderer = go.GetComponent<MeshRenderer>();

        if (renderer != null && material != null)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        if (!keepCollider)
        {
            Collider collider = go.GetComponent<Collider>();

            if (collider != null)
            {
                Object.DestroyImmediate(collider);
            }
        }

        GameObjectUtility.SetStaticEditorFlags(go, EnvironmentStatic);
        return go;
    }

    // ------------------------------------------------------------------ city shapes

    /// <summary>
    /// A block standing on the ground plane. <paramref name="topY"/> is the roof surface and
    /// <paramref name="bottomY"/> its underside - both absolute, neither a centre.
    /// </summary>
    public static GameObject Block(Transform parent, string name, CityRect footprint,
        float bottomY, float topY, Material material)
    {
        float height = Mathf.Max(0.01f, topY - bottomY);
        return Solid(parent, name,
            new Vector3(footprint.CentreX, bottomY + height * 0.5f, footprint.CentreZ),
            new Vector3(footprint.Width, height, footprint.Depth), material);
    }

    /// <summary>
    /// A horizontal deck. <paramref name="surfaceY"/> is what the player walks on; the slab hangs
    /// below it.
    /// </summary>
    public static GameObject Slab(Transform parent, string name, CityRect footprint,
        float surfaceY, float thickness, Material material)
        => Solid(parent, name,
            new Vector3(footprint.CentreX, surfaceY - thickness * 0.5f, footprint.CentreZ),
            new Vector3(footprint.Width, thickness, footprint.Depth), material);

    /// <summary>
    /// A pitched deck. A positive pitch tips the +Z end down, matching
    /// <c>Quaternion.Euler(x, 0, 0)</c> - the same convention the movement sandbox uses.
    ///
    /// <paramref name="yawDegrees"/> turns the run without changing that reading: the box's local Z
    /// is always the direction of the run, so at yaw 90 a positive pitch tips its +X end down. That
    /// is what lets the tower spiral use one ramp primitive on all four faces of the shaft.
    /// </summary>
    public static GameObject Ramp(Transform parent, string name, Vector3 centre, Vector3 size,
        float pitchDegrees, Material material, float yawDegrees = 0f)
    {
        GameObject go = Solid(parent, name, centre, size, material);
        go.transform.localRotation = Quaternion.Euler(pitchDegrees, yawDegrees, 0f);
        return go;
    }

    /// <summary>The compact hierarchy contract returned for one conventional stair flight.</summary>
    public sealed class StairFlightBuildResult
    {
        public readonly GameObject Visual;
        public readonly GameObject WalkSurface;
        public readonly GameObject LandingBefore;
        public readonly GameObject LandingAfter;
        public readonly GameObject[] Guards;

        internal StairFlightBuildResult(GameObject visual, GameObject walkSurface,
            GameObject landingBefore, GameObject landingAfter, GameObject[] guards)
        {
            Visual = visual;
            WalkSurface = walkSurface;
            LandingBefore = landingBefore;
            LandingAfter = landingAfter;
            Guards = guards;
        }
    }

    /// <summary>
    /// Builds conventional visible stairs over one continuous collision ramp. The tread/riser
    /// silhouette is one mesh and one renderer regardless of step count; the only flight collider
    /// is the invisible inclined box. Passing the previous result's high landing into
    /// <paramref name="landingBefore"/> lets switchback flights share one turn landing.
    /// </summary>
    public static StairFlightBuildResult BuildWalkableStairs(Transform parent,
        in StairFlightPlan flight, Material visualMaterial, Material landingMaterial,
        GameObject landingBefore = null)
    {
        ValidateStairFlight(flight);

        Material slabMaterial = landingMaterial != null ? landingMaterial : visualMaterial;
        GameObject lowLanding = landingBefore != null
            ? landingBefore
            : Slab(parent, $"{flight.Name}_Landing_Before", flight.LandingBefore,
                flight.Start.y, CityDesign.AscentLandingThickness, slabMaterial);
        GameObject highLanding = Slab(parent, $"{flight.Name}_Landing_After",
            flight.LandingAfter, flight.End.y, CityDesign.AscentLandingThickness, slabMaterial);

        GameObject visual = new GameObject($"{flight.Name}_Visual",
            typeof(MeshFilter), typeof(MeshRenderer));
        visual.transform.SetParent(parent, false);
        visual.transform.position = flight.Start;
        visual.transform.rotation = Quaternion.Euler(0f, FlightYaw(flight.Direction), 0f);

        Mesh mesh = BuildStairMesh(flight);
        visual.GetComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = visual.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = visualMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        GameObjectUtility.SetStaticEditorFlags(visual, EnvironmentStatic);

        float slopeLength = Mathf.Sqrt(flight.HorizontalRun * flight.HorizontalRun
                                       + flight.Rise * flight.Rise);
        Quaternion slopeRotation = Quaternion.Euler(-flight.PitchDegrees,
            FlightYaw(flight.Direction), 0f);
        Vector3 normal = slopeRotation * Vector3.up;
        Vector3 surfaceMidpoint = (flight.Start + flight.End) * 0.5f;

        GameObject walkSurface = new GameObject($"{flight.Name}_WalkSurface");
        walkSurface.transform.SetParent(parent, false);
        walkSurface.transform.SetPositionAndRotation(
            surfaceMidpoint - normal * (CityDesign.StairCollisionSurfaceDepth * 0.5f),
            slopeRotation);
        BoxCollider walkCollider = walkSurface.AddComponent<BoxCollider>();
        walkCollider.size = new Vector3(flight.ClearWidth,
            CityDesign.StairCollisionSurfaceDepth, slopeLength);
        GameObjectUtility.SetStaticEditorFlags(walkSurface, EnvironmentStatic);

        const float railThickness = 0.10f;
        Vector3 right = Vector3.Cross(Vector3.up, flight.Direction).normalized;
        Vector3 railCentre = surfaceMidpoint
                             + Vector3.up * (CityDesign.StairGuardHeight
                                             - normal.y * railThickness * 0.5f);
        Vector3 railSize = new Vector3(railThickness, railThickness, slopeLength);
        float railOffset = flight.ClearWidth * 0.5f + railThickness * 0.5f;
        GameObject leftGuard = Deco(parent, $"{flight.Name}_Guard_Left",
            railCentre - right * railOffset, railSize, visualMaterial,
            -flight.PitchDegrees, FlightYaw(flight.Direction));
        GameObject rightGuard = Deco(parent, $"{flight.Name}_Guard_Right",
            railCentre + right * railOffset, railSize, visualMaterial,
            -flight.PitchDegrees, FlightYaw(flight.Direction));

        return new StairFlightBuildResult(visual, walkSurface, lowLanding, highLanding,
            new[] { leftGuard, rightGuard });
    }

    private static void ValidateStairFlight(in StairFlightPlan flight)
    {
        if (flight.StepCount <= 0 || flight.RiserHeight <= 0f
            || flight.RiserHeight > CityDesign.StairMaximumRiserHeight + 0.0001f)
        {
            throw new System.ArgumentException("A stair flight needs positive risers no higher than 0.20 m.",
                nameof(flight));
        }

        if (flight.TreadDepth < CityDesign.StairPreferredTreadDepth - 0.0001f)
        {
            throw new System.ArgumentException("A stair flight needs treads at least 0.30 m deep.",
                nameof(flight));
        }

        if (flight.ClearWidth < CityDesign.StairClearWidth - 0.0001f
            || flight.LandingBeforeDepth < CityDesign.StairTurnLandingDepth - 0.0001f
            || flight.LandingAfterDepth < CityDesign.StairTurnLandingDepth - 0.0001f)
        {
            throw new System.ArgumentException(
                "A stair flight and its turn landings need at least 1.80 m clear width.",
                nameof(flight));
        }

        if (flight.Direction.sqrMagnitude < 0.999f
            || flight.PitchDegrees > CityDesign.SlopeLimit + 0.0001f)
        {
            throw new System.ArgumentException("A stair flight needs a horizontal direction within the slope limit.",
                nameof(flight));
        }
    }

    private static float FlightYaw(Vector3 direction)
        => Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;

    private static Mesh BuildStairMesh(in StairFlightPlan flight)
    {
        List<Vector3> vertices = new List<Vector3>(flight.StepCount * 28 + 8);
        List<int> triangles = new List<int>(flight.StepCount * 42 + 12);
        float halfWidth = flight.ClearWidth * 0.5f;
        float depth = CityDesign.StairCollisionSurfaceDepth;

        for (int i = 0; i < flight.StepCount; i++)
        {
            float z0 = i * flight.TreadDepth;
            float z1 = (i + 1) * flight.TreadDepth;
            float top = (i + 1) * flight.RiserHeight;
            float riserBottom = i == 0 ? -depth : i * flight.RiserHeight;
            float bottom0 = i * flight.RiserHeight - depth;
            float bottom1 = (i + 1) * flight.RiserHeight - depth;

            AddQuad(vertices, triangles,
                new Vector3(-halfWidth, top, z0), new Vector3(-halfWidth, top, z1),
                new Vector3(halfWidth, top, z1), new Vector3(halfWidth, top, z0));
            AddQuad(vertices, triangles,
                new Vector3(-halfWidth, riserBottom, z0),
                new Vector3(-halfWidth, top, z0),
                new Vector3(halfWidth, top, z0),
                new Vector3(halfWidth, riserBottom, z0));
            AddQuad(vertices, triangles,
                new Vector3(-halfWidth, bottom0, z0),
                new Vector3(-halfWidth, bottom1, z1),
                new Vector3(-halfWidth, top, z1),
                new Vector3(-halfWidth, top, z0));
            AddQuad(vertices, triangles,
                new Vector3(halfWidth, bottom0, z0),
                new Vector3(halfWidth, top, z0),
                new Vector3(halfWidth, top, z1),
                new Vector3(halfWidth, bottom1, z1));
            AddQuad(vertices, triangles,
                new Vector3(-halfWidth, bottom0, z0),
                new Vector3(halfWidth, bottom0, z0),
                new Vector3(halfWidth, bottom1, z1),
                new Vector3(-halfWidth, bottom1, z1));
        }

        float run = flight.HorizontalRun;
        float rise = flight.Rise;
        AddQuad(vertices, triangles,
            new Vector3(-halfWidth, rise - depth, run),
            new Vector3(halfWidth, rise - depth, run),
            new Vector3(halfWidth, rise, run),
            new Vector3(-halfWidth, rise, run));

        Mesh mesh = new Mesh { name = $"{flight.Name}_CombinedTreads" };

        if (vertices.Count > ushort.MaxValue)
        {
            mesh.indexFormat = IndexFormat.UInt32;
        }

        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void AddQuad(List<Vector3> vertices, List<int> triangles,
        Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        int first = vertices.Count;
        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        vertices.Add(d);
        triangles.Add(first);
        triangles.Add(first + 1);
        triangles.Add(first + 2);
        triangles.Add(first);
        triangles.Add(first + 2);
        triangles.Add(first + 3);
    }

    /// <summary>
    /// A vertical pole. No collider: scaffold uprights and crane ties are silhouette, and a player
    /// must never be able to stand on one or have one block a parkour probe.
    /// </summary>
    public static GameObject Pole(Transform parent, string name, float x, float z, float bottomY,
        float topY, float thickness, Material material)
    {
        float height = Mathf.Max(0.01f, topY - bottomY);
        return Deco(parent, name, new Vector3(x, bottomY + height * 0.5f, z),
            new Vector3(thickness, height, thickness), material);
    }

    /// <summary>
    /// A scene-view-only marker: no renderer, no collider. Used to name districts and landmarks in
    /// the hierarchy so the greybox can be navigated without guessing which box is which.
    /// </summary>
    public static GameObject Label(Transform parent, string text, Vector3 position)
    {
        GameObject go = new GameObject($"# {text}");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        return go;
    }
}
