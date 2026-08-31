using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What a piece of Phase 6E decoration is made of.
///
/// The first four are tinted per district by <see cref="CityDesign.Palette"/>; the rest are shared
/// across the whole city, because a handrail is a handrail wherever it is and giving each district
/// its own steel would be six materials for a difference nobody can see.
/// </summary>
public enum DetailSurface
{
    /// <summary>Plinths, cornices, floor bands, parapets, bulkheads. District-tinted.</summary>
    Trim,

    /// <summary>The recessed field a punched window sits in. District-tinted.</summary>
    Panel,

    /// <summary>Glazing. District-tinted.</summary>
    Glass,

    /// <summary>The district's one emissive colour: signs, crowns, beacons. District-tinted.</summary>
    Neon,

    /// <summary>Kerbs, planters, the perimeter wall.</summary>
    Concrete,

    /// <summary>Galvanised steel: rails, ducts, masts, lamp columns.</summary>
    Metal,

    /// <summary>Dark steel: trusses, caps, frames, the underside of things.</summary>
    MetalDark,

    /// <summary>Rooftop plant. Painted machine grey.</summary>
    Machine,

    /// <summary>Weathered iron: water tanks, industrial stacks.</summary>
    Rust,

    /// <summary>Road markings.</summary>
    Paint,

    /// <summary>Orange-and-black chevrons. The tower gate and the Cut's edge only.</summary>
    Hazard,

    /// <summary>
    /// The lit strip laid down every deck, landing and run the traversal layer authored. One
    /// colour, city-wide and district-blind on purpose: it means "this is a route", and a route
    /// that changed colour between districts would read as meaning something else.
    /// </summary>
    Route,

    /// <summary>Street lamp heads. Warm, and the only warm light in the city.</summary>
    Lamp,

    /// <summary>The unreachable city beyond the core. Flat, desaturated, never lit.</summary>
    Backdrop
}

/// <summary>
/// One box of Phase 6E decoration.
///
/// Deliberately not a <see cref="BlockPlan"/>: a block can be collidable and this can never be, and
/// keeping them in separate lists is what makes "Phase 6E adds nothing solid to the city" a property
/// of the type system rather than a promise. It also keeps <see cref="CityPlanResult.ColliderCount"/>
/// and the Phase 6B massing report reading exactly what they read before.
/// </summary>
public readonly struct DetailPlan
{
    public readonly string Name;
    public readonly string GroupName;
    public readonly DetailSurface Surface;

    /// <summary>Which district's palette tints this piece. Ignored by the shared surfaces.</summary>
    public readonly DistrictGroup Tint;

    public readonly Vector3 Centre;
    public readonly Vector3 Size;

    /// <summary>Rotation, in the same <c>Euler(pitch, yaw, 0)</c> convention <see cref="RampPlan"/> uses.</summary>
    public readonly float PitchDegrees;

    public readonly float YawDegrees;

    public DetailPlan(string name, string groupName, DetailSurface surface, DistrictGroup tint,
        Vector3 centre, Vector3 size, float pitchDegrees = 0f, float yawDegrees = 0f)
    {
        Name = name;
        GroupName = groupName;
        Surface = surface;
        Tint = tint;
        Centre = centre;
        Size = size;
        PitchDegrees = pitchDegrees;
        YawDegrees = yawDegrees;
    }

    public bool IsRotated => Mathf.Abs(PitchDegrees) > 0.001f || Mathf.Abs(YawDegrees) > 0.001f;

    /// <summary>Highest point the piece reaches, ignoring rotation. Used by the budget report.</summary>
    public float TopY => Centre.y + Size.y * 0.5f;

    /// <summary>The ground footprint of an unrotated piece, which every one that matters is.</summary>
    public CityRect Footprint => CityRect.FromCentre(Centre.x, Centre.z, Size.x, Size.z);
}

/// <summary>The Phase 6E art layer, as data, beside the city it dresses.</summary>
public sealed class CityDressingResult
{
    /// <summary>How many pieces each group carries. The renderer budget, itemised.</summary>
    public readonly Dictionary<string, int> PerGroup = new Dictionary<string, int>();

    /// <summary>Rooftops that were dressed, and rooftops that had no edge safe to dress.</summary>
    public int DressedRoofs;

    public int BareRoofs;

    /// <summary>Props that were planned and then dropped for standing too close to something.</summary>
    public int PropsRejected;

    public readonly List<string> Problems = new List<string>();

    public int Total
    {
        get
        {
            int total = 0;

            foreach (KeyValuePair<string, int> group in PerGroup)
            {
                total += group.Value;
            }

            return total;
        }
    }
}

/// <summary>
/// Phase 6E: the environment art, hung on the finished city the way every layer before it was.
///
/// The shape is the one Phase 6C and 6D established and for the same reasons. Every dimension is in
/// <see cref="CityDesign"/>, this file is a pure function of the plan and a fixed seed, and
/// `SkyboundCityBuilder` instantiates exactly what it is handed. What is new is a single invariant,
/// stronger than Phase 6D's:
///
///   <b>Phase 6E adds nothing collidable to the city at all.</b>
///
/// Not "nothing except one gate" - nothing. Every piece here goes into
/// <see cref="CityPlanResult.Details"/>, which the builder can only turn into <c>CityKit.Deco</c>,
/// and which <see cref="CityPlanResult.ColliderCount"/> does not count because there is nothing to
/// count. That is what lets an art pass this large land on a city whose traversal has already been
/// measured: the Phase 6B walkability fill, the Phase 6C tier measurements, the Phase 6D route
/// probes and all 120 mission orderings are arithmetic over the massing and the traversal layer, and
/// this file touches neither.
///
/// It costs one thing, and it is worth naming rather than hiding: a player can run through a rooftop
/// air-conditioning unit. The <see cref="DeadEdge"/> rule is the answer - plant only stands on roof
/// edges no move in the game can arrive at or leave from - and it is why roughly a third of the
/// city's roofs are left bare rather than dressed with something a runner would walk through.
/// </summary>
public static class CityDressing
{
    // ------------------------------------------------------------------ groups

    public const string FacadeGroup = "DETAIL_FACADES";
    public const string RoofGroup = "DETAIL_ROOFS";
    public const string SignGroup = "DETAIL_SIGNS";
    public const string TraversalGroup = "DETAIL_TRAVERSAL";
    public const string ObjectiveGroup = "DETAIL_OBJECTIVES";
    public const string TowerGroup = "DETAIL_TOWER";
    public const string StreetGroup = "DETAIL_STREET";
    public const string BackdropGroup = "BACKDROP";

    /// <summary>
    /// The chevrons and beacons on the tower gate. A group of its own rather than part of
    /// <see cref="TraversalGroup"/> because `ObjectiveTracker` hides the gate by deactivating
    /// <see cref="CityObjectives.GateGroup"/>, and dressing left in any other group would still be
    /// hanging in the air over an opened tower.
    /// </summary>
    public const string GateDetailGroup = "TOWER_GATE_DETAIL";

    /// <summary>
    /// Every group this layer emits into. All eight are siblings of the massing groups rather than
    /// children of them, and that is load bearing: the Phase 6B massing report measures a district's
    /// height band from the bounds of every renderer under that district's transform, so a plinth
    /// parented to <c>ResidentialNorth</c> would report the district as reaching 1.4 m and fail.
    /// </summary>
    public static readonly string[] Groups =
    {
        FacadeGroup, RoofGroup, SignGroup, TraversalGroup, ObjectiveGroup, TowerGroup,
        StreetGroup, GateDetailGroup, BackdropGroup
    };

    /// <summary>Seed for the art layer. Distinct from the plan's, so re-dressing cannot move a wall.</summary>
    public const uint Seed = 0x6E5A17u;

    // ------------------------------------------------------------------ rng

    /// <summary>
    /// xorshift32 again, for the reason <see cref="CityPlan"/> gives: <c>System.Random</c>'s
    /// sequence is not guaranteed stable across runtimes, and a city that dresses itself differently
    /// in a test runner than in the editor is a city whose art cannot be asserted on.
    /// </summary>
    private struct Rng
    {
        private uint state;

        public Rng(string key)
        {
            uint hash = 2166136261u;

            foreach (char c in key)
            {
                hash ^= c;
                hash *= 16777619u;
            }

            state = (hash ^ Seed) == 0u ? 0x9E3779B9u : hash ^ Seed;
        }

        public uint NextUInt()
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }

        public float Next01() => (NextUInt() >> 8) * (1f / 16777216f);

        public float Range(float min, float max) => min + (max - min) * Next01();

        public int RangeInt(int minInclusive, int maxInclusive)
            => minInclusive + (int)(NextUInt() % (uint)(maxInclusive - minInclusive + 1));

        public bool Chance(float probability) => Next01() < probability;
    }

    // ------------------------------------------------------------------ neighbourhood

    /// <summary>A surface something could be jumped to or from, for the dead-edge test.</summary>
    private readonly struct Neighbour
    {
        public readonly CityRect Rect;
        public readonly float Y;

        public Neighbour(CityRect rect, float y)
        {
            Rect = rect;
            Y = y;
        }
    }

    private sealed class Context
    {
        public readonly List<Neighbour> Neighbours = new List<Neighbour>();

        /// <summary>Footprints nothing decorative may stand on: pads, decks, ascents, the gate.</summary>
        public readonly List<CityRect> KeepOut = new List<CityRect>();

        public bool Blocked(in CityRect rect)
        {
            foreach (CityRect keep in KeepOut)
            {
                if (rect.Overlaps(keep))
                {
                    return true;
                }
            }

            return false;
        }
    }

    // ------------------------------------------------------------------ entry point

    /// <summary>
    /// Dresses a finished plan. Called last, after <see cref="CityTraversal"/> and
    /// <see cref="CityObjectives"/>, because every rule here is a question about what is already
    /// there - which roof edges are dead, which facades face an avenue, where a bridge deck runs.
    /// </summary>
    public static CityDressingResult Plan(CityPlanResult plan)
    {
        CityDressingResult result = new CityDressingResult();
        Context context = BuildContext(plan);

        foreach (BuildingPlan building in plan.Buildings)
        {
            DressFacade(plan, result, context, building);
            DressRoof(plan, result, context, building);
            DressSignage(plan, result, building);
        }

        DressTower(plan, result);
        DressTraversal(plan, result);
        DressObjectives(plan, result);
        DressStreets(plan, result);
        DressBackdrop(plan, result);

        return result;
    }

    /// <summary>
    /// Everything a prop has to keep away from, gathered once.
    ///
    /// The two lists answer different questions. <see cref="Context.Neighbours"/> is "could a player
    /// arrive here", which decides whether a roof edge may be dressed at all;
    /// <see cref="Context.KeepOut"/> is "is this exact square metre already spoken for", which
    /// rejects the individual prop.
    /// </summary>
    private static Context BuildContext(CityPlanResult plan)
    {
        Context context = new Context();

        foreach (BuildingPlan building in plan.Buildings)
        {
            context.Neighbours.Add(new Neighbour(building.Footprint, building.RoofY));
        }

        foreach (BlockPlan block in plan.Blocks)
        {
            if (block.Kind == CityPieceKind.Landmark || block.Kind == CityPieceKind.Crane)
            {
                context.Neighbours.Add(new Neighbour(block.Footprint, block.TopY));
            }

            if (block.Kind == CityPieceKind.Gate)
            {
                context.KeepOut.Add(block.Footprint.Inset(-CityDesign.PropClearance));
            }
        }

        if (plan.Traversal != null)
        {
            foreach (LinkPlan link in plan.Traversal.Links)
            {
                context.Neighbours.Add(new Neighbour(link.Deck, link.DeckY));
                context.KeepOut.Add(link.Deck.Inset(-CityDesign.PropClearance));
            }

            foreach (AscentPlan ascent in plan.Traversal.Ascents)
            {
                for (int i = 0; i < ascent.Landings.Count; i++)
                {
                    context.Neighbours.Add(new Neighbour(ascent.Landings[i], ascent.LandingY[i]));
                    context.KeepOut.Add(ascent.Landings[i].Inset(-CityDesign.PropClearance));
                }

                // The top of an ascent is where a player steps onto the roof, so the landing apron
                // in front of it has to stay clear even though the ascent itself is beside it.
                context.KeepOut.Add(ascent.TopFootprint.Inset(-CityDesign.PropClearance));
            }
        }

        if (plan.Objectives != null)
        {
            foreach (RelayObjective relay in plan.Objectives.Relays)
            {
                context.KeepOut.Add(relay.Trigger.Inset(-CityDesign.PropClearance));
            }

            foreach (AnchorObjective anchor in plan.Objectives.Anchors)
            {
                context.KeepOut.Add(anchor.Trigger.Inset(-CityDesign.PropClearance));
            }
        }

        return context;
    }

    // ------------------------------------------------------------------ emit helpers

    private static void Emit(CityPlanResult plan, CityDressingResult result, string name,
        string group, DetailSurface surface, DistrictGroup tint, Vector3 centre, Vector3 size,
        float pitch = 0f, float yaw = 0f)
    {
        plan.Details.Add(new DetailPlan(name, group, surface, tint, centre, size, pitch, yaw));
        result.PerGroup.TryGetValue(group, out int count);
        result.PerGroup[group] = count + 1;
    }

    /// <summary>A box named by the surface it sits between, never by a centre.</summary>
    private static void Slab(CityPlanResult plan, CityDressingResult result, string name,
        string group, DetailSurface surface, DistrictGroup tint, in CityRect footprint,
        float bottomY, float topY)
    {
        float height = Mathf.Max(0.02f, topY - bottomY);
        Emit(plan, result, name, group, surface, tint,
            new Vector3(footprint.CentreX, bottomY + height * 0.5f, footprint.CentreZ),
            new Vector3(footprint.Width, height, footprint.Depth));
    }

    // ------------------------------------------------------------------ facade geometry

    /// <summary>Coordinate of the facade plane: X for west/east, Z for south/north.</summary>
    private static float FacadePlane(in CityRect f, Facade side)
    {
        switch (side)
        {
            case Facade.West: return f.MinX;
            case Facade.East: return f.MaxX;
            case Facade.South: return f.MinZ;
            default: return f.MaxZ;
        }
    }

    /// <summary>+1 where the facade looks towards increasing X or Z, -1 where it looks back.</summary>
    private static float Outward(Facade side)
        => side == Facade.East || side == Facade.North ? 1f : -1f;

    private static bool AlongX(Facade side) => side == Facade.South || side == Facade.North;

    private static float AlongMin(in CityRect f, Facade side) => AlongX(side) ? f.MinX : f.MinZ;

    private static float AlongMax(in CityRect f, Facade side) => AlongX(side) ? f.MaxX : f.MaxZ;

    /// <summary>
    /// A strip standing on a facade, straddling the wall plane so it reads as proud from outside
    /// and never leaves a gap at the join. <paramref name="from"/> and <paramref name="to"/> run
    /// along the facade in world coordinates.
    /// </summary>
    private static CityRect FacadeStrip(in CityRect f, Facade side, float from, float to,
        float proud)
    {
        float plane = FacadePlane(f, side);
        float half = proud * 0.5f;

        return AlongX(side)
            ? new CityRect(from, to, plane - half, plane + half)
            : new CityRect(plane - half, plane + half, from, to);
    }

    private static readonly Facade[] AllFacades =
    {
        Facade.West, Facade.East, Facade.South, Facade.North
    };

    // ------------------------------------------------------------------ facades

    /// <summary>
    /// Plinth, cornice, floor bands and glazing. Four moves, and between them they are what turns a
    /// box into a building: a base it stands on, a crown it stops at, a horizontal rhythm that says
    /// how tall it is, and a window field that says which way is in.
    /// </summary>
    private static void DressFacade(CityPlanResult plan, CityDressingResult result, Context context,
        in BuildingPlan building)
    {
        CityRect f = building.Footprint;
        float roof = building.RoofY;
        DistrictGroup tint = building.Group;

        Slab(plan, result, $"{building.Name}_Plinth", FacadeGroup, DetailSurface.Trim, tint,
            f.Inset(-CityDesign.PlinthProud), 0f, CityDesign.PlinthHeight);

        // The cornice stops at the roof surface, never above it. A crown that stood proud of the
        // roof would be a lip on every roof edge in the city, and the roof edge is where every
        // Phase 6C jump is measured from.
        Slab(plan, result, $"{building.Name}_Cornice", FacadeGroup, DetailSurface.Trim, tint,
            f.Inset(-CityDesign.CorniceProud), roof - CityDesign.CorniceDepth, roof);

        DressFloorBands(plan, result, building);
        DressGlazing(plan, result, context, building);

        if (building.Storeys >= CityDesign.PierStoreyMin)
        {
            DressCornerPiers(plan, result, building);
        }
    }

    private static void DressFloorBands(CityPlanResult plan, CityDressingResult result,
        in BuildingPlan building)
    {
        int floors = building.Storeys - 1;

        if (floors <= 0)
        {
            return;
        }

        // Every floor up to the cap, then every second, then every third. A 19 storey tower gets
        // 9 bands rather than 18, and the rhythm still reads because the spacing stays even.
        int step = Mathf.CeilToInt(floors / (float)CityDesign.MaxFacadeBands);
        CityRect band = building.Footprint.Inset(-CityDesign.FloorBandProud);
        float half = CityDesign.FloorBandHeight * 0.5f;

        for (int i = step; i <= floors; i += step)
        {
            float y = i * CityDesign.StoreyHeight;

            if (y > building.RoofY - CityDesign.CorniceDepth - half)
            {
                break;
            }

            Slab(plan, result, $"{building.Name}_Band{i}", FacadeGroup, DetailSurface.Trim,
                building.Group, band, y - half, y + half);
        }
    }

    /// <summary>
    /// Windows, in one of two grammars. A curtain-walled district glazes each facade in a single
    /// field that the floor bands cross as spandrels; a masonry district punches bays into it. The
    /// difference is the fastest way to tell Corporate from the Old Quarter at any distance, which
    /// is exactly what the district zoning is for.
    /// </summary>
    /// <summary>
    /// Is this facade one anybody ever sees?
    ///
    /// A Residential block is five lots by five separated by 4 m alleys, so nine of its
    /// twenty-five buildings have neighbours on all four sides and most of the rest have them on
    /// three. A facade 4 m from the wall opposite is a party wall: it cannot be read, it cannot be
    /// photographed, and glazing it costs four boxes for nothing. Real dense fabric leaves those
    /// walls blank, so this does too - and it is what brought the art layer back inside the
    /// Phase 6A renderer budget without taking anything off a facade a player can actually look at.
    /// </summary>
    private static bool OpenFacade(Context context, in CityRect f, Facade side)
    {
        const float clear = 5f;

        CityRect band;

        switch (side)
        {
            case Facade.West: band = new CityRect(f.MinX - clear, f.MinX, f.MinZ, f.MaxZ); break;
            case Facade.East: band = new CityRect(f.MaxX, f.MaxX + clear, f.MinZ, f.MaxZ); break;
            case Facade.South: band = new CityRect(f.MinX, f.MaxX, f.MinZ - clear, f.MinZ); break;
            default: band = new CityRect(f.MinX, f.MaxX, f.MaxZ, f.MaxZ + clear); break;
        }

        foreach (Neighbour neighbour in context.Neighbours)
        {
            if (neighbour.Rect.Overlaps(band) && !neighbour.Rect.Overlaps(f.Inset(0.01f)))
            {
                return false;
            }
        }

        return true;
    }

    private static void DressGlazing(CityPlanResult plan, CityDressingResult result,
        Context context, in BuildingPlan building)
    {
        CityRect f = building.Footprint;
        CityDesign.DistrictPalette palette = CityDesign.Palette(building.Group);

        float bottom = CityDesign.PlinthHeight + CityDesign.WindowSill;
        float top = building.RoofY - CityDesign.CorniceDepth - CityDesign.WindowHead;

        if (top - bottom < 2f)
        {
            return;
        }

        foreach (Facade side in AllFacades)
        {
            float from = AlongMin(f, side) + CityDesign.FacadePierWidth;
            float to = AlongMax(f, side) - CityDesign.FacadePierWidth;

            if (to - from < CityDesign.WindowStripWidth)
            {
                continue;
            }

            if (palette.CurtainWall)
            {
                Slab(plan, result, $"{building.Name}_Glass{side}", FacadeGroup,
                    DetailSurface.Glass, building.Group,
                    FacadeStrip(f, side, from, to, CityDesign.GlassProud), bottom, top);
                continue;
            }

            bool open = OpenFacade(context, f, side);

            // A punched facade needs the recessed field behind the bays as well, or the bays read
            // as strips glued to a blank wall rather than as holes cut into one. A party wall gets
            // neither the field nor more than one bay.
            if (open)
            {
                Slab(plan, result, $"{building.Name}_Panel{side}", FacadeGroup, DetailSurface.Panel,
                    building.Group, FacadeStrip(f, side, from, to, CityDesign.GlassProud * 0.6f),
                    bottom, top);
            }

            int bays = open
                ? Mathf.Clamp(Mathf.FloorToInt((to - from) / CityDesign.WindowBaySpacing), 1,
                    CityDesign.WindowMaxBays)
                : 1;
            float pitch = (to - from) / bays;

            for (int i = 0; i < bays; i++)
            {
                float centre = from + pitch * (i + 0.5f);
                float half = CityDesign.WindowStripWidth * 0.5f;

                Slab(plan, result, $"{building.Name}_Bay{side}{i}", FacadeGroup,
                    DetailSurface.Glass, building.Group,
                    FacadeStrip(f, side, centre - half, centre + half, CityDesign.GlassProud),
                    bottom, top);
            }
        }
    }

    /// <summary>
    /// Corner piers, on the towers tall enough for a facade to need vertical structure as well as
    /// horizontal. Below <see cref="CityDesign.PierStoreyMin"/> the plinth and the cornice are
    /// close enough together to do the job on their own.
    /// </summary>
    private static void DressCornerPiers(CityPlanResult plan, CityDressingResult result,
        in BuildingPlan building)
    {
        CityRect f = building.Footprint;
        float proud = CityDesign.GlassProud;
        float w = CityDesign.FacadePierWidth;
        int index = 0;

        float[] xs = { f.MinX, f.MaxX };
        float[] zs = { f.MinZ, f.MaxZ };

        foreach (float x in xs)
        {
            foreach (float z in zs)
            {
                index++;
                float dx = x < f.CentreX ? w * 0.5f : -w * 0.5f;
                float dz = z < f.CentreZ ? w * 0.5f : -w * 0.5f;

                Slab(plan, result, $"{building.Name}_Pier{index}", FacadeGroup, DetailSurface.Trim,
                    building.Group,
                    CityRect.FromCentre(x + dx, z + dz, w + proud, w + proud),
                    CityDesign.PlinthHeight, building.RoofY - CityDesign.CorniceDepth);
            }
        }
    }

    // ------------------------------------------------------------------ signage

    /// <summary>
    /// A blade sign on the one facade that faces an avenue, and a lit crown on anything tall enough
    /// to be seen over its neighbours.
    ///
    /// "Faces an avenue" is answered structurally rather than by distance: a lot on the edge of its
    /// superblock is by construction on an avenue, the perimeter margin, or the plaza, because those
    /// are the only three things a superblock borders.
    /// </summary>
    private static void DressSignage(CityPlanResult plan, CityDressingResult result,
        in BuildingPlan building)
    {
        if (building.Storeys >= CityDesign.CrownStoreyMin)
        {
            Slab(plan, result, $"{building.Name}_Crown", SignGroup, DetailSurface.Neon,
                building.Group, building.Footprint.Inset(-CityDesign.CorniceProud - 0.05f),
                building.RoofY - CityDesign.CorniceDepth - CityDesign.CrownHeight,
                building.RoofY - CityDesign.CorniceDepth);
        }

        if (building.Storeys < CityDesign.SignStoreyMin)
        {
            return;
        }

        CityRect cell = CityDesign.Cell(building.CellName).Bounds;
        CityRect f = building.Footprint;
        Rng rng = new Rng($"sign:{building.Name}");

        List<Facade> facing = new List<Facade>();

        foreach (Facade side in AllFacades)
        {
            float gap = Mathf.Abs(FacadePlane(f, side) - FacadePlane(cell, side));

            if (gap <= CityDesign.AvenueFacingTolerance)
            {
                facing.Add(side);
            }
        }

        if (facing.Count == 0)
        {
            return;
        }

        Facade chosen = facing[rng.RangeInt(0, facing.Count - 1)];
        float height = Mathf.Min(CityDesign.SignMaxHeight,
            building.RoofY - CityDesign.PlinthHeight - CityDesign.CorniceDepth - 1f);

        if (height < 2.5f)
        {
            return;
        }

        float top = building.RoofY - CityDesign.CorniceDepth - 1f;
        float along = rng.Range(AlongMin(f, chosen) + 3f, AlongMax(f, chosen) - 3f);
        float plane = FacadePlane(f, chosen);
        float outward = Outward(chosen);

        // The blade hangs clear of the wall on a bracket, which is what stops it reading as a
        // painted stripe. Both are placed off the facade plane rather than off the box centre.
        float bladeOffset = CityDesign.SignDepth * 0.5f + 0.55f;

        CityRect blade = AlongX(chosen)
            ? CityRect.FromCentre(along, plane + outward * bladeOffset, CityDesign.SignWidth,
                CityDesign.SignDepth)
            : CityRect.FromCentre(plane + outward * bladeOffset, along, CityDesign.SignDepth,
                CityDesign.SignWidth);

        Slab(plan, result, $"{building.Name}_Sign", SignGroup, DetailSurface.Neon, building.Group,
            blade, top - height, top);

        CityRect bracket = AlongX(chosen)
            ? CityRect.FromCentre(along, plane + outward * 0.3f, 0.2f, 0.7f)
            : CityRect.FromCentre(plane + outward * 0.3f, along, 0.7f, 0.2f);

        Slab(plan, result, $"{building.Name}_SignArm", SignGroup, DetailSurface.Metal,
            building.Group, bracket, top - 0.35f, top - 0.15f);
    }

    // ------------------------------------------------------------------ rooftops

    /// <summary>
    /// Is this edge of this roof one no move in the game can arrive at or leave from?
    ///
    /// The band searched is <see cref="CityDesign.DeadEdgeReach"/> deep, which is above the flat
    /// sprint reach with margin, and anything standing at or above
    /// <c>roof - SafeDropHeight</c> inside it makes the edge live - including a roof *higher* than
    /// this one, because a player can drop onto this edge from there.
    /// </summary>
    private static bool DeadEdge(Context context, in CityRect roof, float roofY, Facade side)
    {
        float reach = CityDesign.DeadEdgeReach;

        CityRect band;

        switch (side)
        {
            case Facade.West: band = new CityRect(roof.MinX - reach, roof.MinX, roof.MinZ, roof.MaxZ); break;
            case Facade.East: band = new CityRect(roof.MaxX, roof.MaxX + reach, roof.MinZ, roof.MaxZ); break;
            case Facade.South: band = new CityRect(roof.MinX, roof.MaxX, roof.MinZ - reach, roof.MinZ); break;
            default: band = new CityRect(roof.MinX, roof.MaxX, roof.MaxZ, roof.MaxZ + reach); break;
        }

        foreach (Neighbour neighbour in context.Neighbours)
        {
            if (neighbour.Y < roofY - CityDesign.SafeDropHeight - 0.01f)
            {
                continue;
            }

            if (neighbour.Rect.Overlaps(band) && !neighbour.Rect.Overlaps(roof.Inset(0.01f)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The band of roof just inside one edge, where plant may stand.</summary>
    private static CityRect PropBand(in CityRect roof, Facade side)
    {
        float inset = CityDesign.RoofPropInset;
        float depth = CityDesign.RoofPropBandDepth;

        switch (side)
        {
            case Facade.West:
                return new CityRect(roof.MinX + inset, roof.MinX + inset + depth,
                    roof.MinZ + inset, roof.MaxZ - inset);
            case Facade.East:
                return new CityRect(roof.MaxX - inset - depth, roof.MaxX - inset,
                    roof.MinZ + inset, roof.MaxZ - inset);
            case Facade.South:
                return new CityRect(roof.MinX + inset, roof.MaxX - inset,
                    roof.MinZ + inset, roof.MinZ + inset + depth);
            default:
                return new CityRect(roof.MinX + inset, roof.MaxX - inset,
                    roof.MaxZ - inset - depth, roof.MaxZ - inset);
        }
    }

    private static void DressRoof(CityPlanResult plan, CityDressingResult result, Context context,
        in BuildingPlan building)
    {
        CityRect roof = building.Footprint;

        if (Mathf.Min(roof.Width, roof.Depth) < CityDesign.RoofPropMinRoof)
        {
            result.BareRoofs++;
            return;
        }

        List<Facade> dead = new List<Facade>();

        foreach (Facade side in AllFacades)
        {
            if (DeadEdge(context, roof, building.RoofY, side))
            {
                dead.Add(side);
            }
        }

        if (dead.Count == 0)
        {
            result.BareRoofs++;
            return;
        }

        Rng rng = new Rng($"roof:{building.Name}");
        int before = result.PerGroup.TryGetValue(RoofGroup, out int had) ? had : 0;

        // Two edges at most, however many are dead. A roof plated on all four sides stops reading
        // as a roof, and the budget is shared with 107 other buildings.
        int edges = Mathf.Min(dead.Count, 2);

        for (int e = 0; e < edges; e++)
        {
            Facade side = dead[(e + rng.RangeInt(0, dead.Count - 1)) % dead.Count];
            CityRect band = PropBand(roof, side);

            if (band.Width <= 0.5f || band.Depth <= 0.5f)
            {
                continue;
            }

            bool alongX = AlongX(side);
            float length = alongX ? band.Width : band.Depth;
            int slots = Mathf.Clamp(Mathf.FloorToInt(length / CityDesign.RoofPropSlot), 1, 3);
            float pitch = length / slots;
            float start = alongX ? band.MinX : band.MinZ;

            for (int i = 0; i < slots; i++)
            {
                float centre = start + pitch * (i + 0.5f);
                float x = alongX ? centre : band.CentreX;
                float z = alongX ? band.CentreZ : centre;

                Prop(plan, result, context, building, ref rng, x, z, i);
            }
        }

        DressBulkhead(plan, result, context, building, dead, ref rng);

        int after = result.PerGroup.TryGetValue(RoofGroup, out int now) ? now : 0;

        if (after > before)
        {
            result.DressedRoofs++;
        }
        else
        {
            result.BareRoofs++;
        }
    }

    /// <summary>
    /// One piece of rooftop plant, chosen by district so a roof says which district it is on. The
    /// archetypes are deliberately few and deliberately large - a roof read from an avenue 25 m
    /// below resolves a water tank and nothing smaller.
    /// </summary>
    private static void Prop(CityPlanResult plan, CityDressingResult result, Context context,
        in BuildingPlan building, ref Rng rng, float x, float z, int index)
    {
        float roofY = building.RoofY;
        string name = $"{building.Name}_Prop{index}";
        DistrictGroup tint = building.Group;
        int roll = rng.RangeInt(0, 5);

        switch (building.Group)
        {
            case DistrictGroup.Residential:
            case DistrictGroup.OldQuarter:
                if (roll < 3)
                {
                    // A water tank on a stand. The one silhouette that says "roofs people live under".
                    CityRect stand = CityRect.FromCentre(x, z, 2.6f, 2.6f);
                    CityRect tank = CityRect.FromCentre(x, z, 3.0f, 3.0f);

                    if (context.Blocked(tank))
                    {
                        result.PropsRejected++;
                        return;
                    }

                    Slab(plan, result, $"{name}_Stand", RoofGroup, DetailSurface.MetalDark, tint,
                        stand, roofY, roofY + 1.1f);
                    Slab(plan, result, $"{name}_Tank", RoofGroup, DetailSurface.Rust, tint,
                        tank, roofY + 1.1f, roofY + 3.5f);
                    return;
                }

                break;

            case DistrictGroup.Industrial:
                if (roll < 3)
                {
                    CityRect stack = CityRect.FromCentre(x, z, 1.5f, 1.5f);
                    CityRect cap = CityRect.FromCentre(x, z, 2.0f, 2.0f);

                    if (context.Blocked(cap))
                    {
                        result.PropsRejected++;
                        return;
                    }

                    Slab(plan, result, $"{name}_Stack", RoofGroup, DetailSurface.Rust, tint,
                        stack, roofY, roofY + 5.4f);
                    Slab(plan, result, $"{name}_StackCap", RoofGroup, DetailSurface.MetalDark, tint,
                        cap, roofY + 5.4f, roofY + 5.9f);
                    return;
                }

                break;

            case DistrictGroup.CityCenter:
            case DistrictGroup.Corporate:
                if (roll < 3)
                {
                    CityRect mast = CityRect.FromCentre(x, z, 0.32f, 0.32f);

                    if (context.Blocked(CityRect.FromCentre(x, z, 2.6f, 2.6f)))
                    {
                        result.PropsRejected++;
                        return;
                    }

                    float top = roofY + rng.Range(6f, 9.5f);

                    Slab(plan, result, $"{name}_Mast", RoofGroup, DetailSurface.Metal, tint,
                        mast, roofY, top);
                    Slab(plan, result, $"{name}_MastArm", RoofGroup, DetailSurface.Metal, tint,
                        CityRect.FromCentre(x, z, 2.4f, 0.2f), top - 1.6f, top - 1.4f);
                    Slab(plan, result, $"{name}_MastTip", RoofGroup, DetailSurface.Neon, tint,
                        CityRect.FromCentre(x, z, 0.5f, 0.5f), top, top + 0.5f);
                    return;
                }

                break;
        }

        if (roll == 5)
        {
            // A vent riser and its cowl.
            CityRect cowl = CityRect.FromCentre(x, z, 1.1f, 1.1f);

            if (context.Blocked(cowl))
            {
                result.PropsRejected++;
                return;
            }

            Slab(plan, result, $"{name}_Vent", RoofGroup, DetailSurface.Metal, tint,
                CityRect.FromCentre(x, z, 0.55f, 0.55f), roofY, roofY + 1.9f);
            Slab(plan, result, $"{name}_VentCowl", RoofGroup, DetailSurface.MetalDark, tint,
                cowl, roofY + 1.9f, roofY + 2.2f);
            return;
        }

        // The default everywhere: an air handling unit on its frame.
        CityRect unit = CityRect.FromCentre(x, z, 2.8f, 1.8f);

        if (context.Blocked(unit))
        {
            result.PropsRejected++;
            return;
        }

        Slab(plan, result, $"{name}_Plant", RoofGroup, DetailSurface.Machine, tint,
            unit, roofY + 0.25f, roofY + 1.5f);
        Slab(plan, result, $"{name}_PlantFrame", RoofGroup, DetailSurface.MetalDark, tint,
            unit.Inset(0.15f), roofY, roofY + 0.25f);
        Slab(plan, result, $"{name}_PlantGrille", RoofGroup, DetailSurface.MetalDark, tint,
            unit.Inset(0.35f), roofY + 1.5f, roofY + 1.62f);
    }

    /// <summary>
    /// The stair bulkhead - the little hut a real roof is reached through. Only where two dead
    /// edges meet, because a corner is where one always is and because two dead edges is the
    /// strongest evidence there is that nobody runs through that part of the roof.
    /// </summary>
    private static void DressBulkhead(CityPlanResult plan, CityDressingResult result,
        Context context, in BuildingPlan building, List<Facade> dead, ref Rng rng)
    {
        if (dead.Count < 2)
        {
            return;
        }

        bool west = dead.Contains(Facade.West);
        bool east = dead.Contains(Facade.East);
        bool south = dead.Contains(Facade.South);
        bool north = dead.Contains(Facade.North);

        if (!((west || east) && (south || north)))
        {
            return;
        }

        CityRect roof = building.Footprint;
        float inset = CityDesign.RoofPropInset + 1.8f;
        float x = west ? roof.MinX + inset : roof.MaxX - inset;
        float z = south ? roof.MinZ + inset : roof.MaxZ - inset;

        CityRect hut = CityRect.FromCentre(x, z, 3.4f, 2.8f);

        if (context.Blocked(hut) || !roof.Inset(1f).Contains(hut.MinX, hut.MinZ)
                                 || !roof.Inset(1f).Contains(hut.MaxX, hut.MaxZ))
        {
            result.PropsRejected++;
            return;
        }

        float top = building.RoofY + 2.5f;

        Slab(plan, result, $"{building.Name}_Bulkhead", RoofGroup, DetailSurface.Trim,
            building.Group, hut, building.RoofY, top);
        Slab(plan, result, $"{building.Name}_BulkheadCap", RoofGroup, DetailSurface.MetalDark,
            building.Group, hut.Inset(-0.25f), top, top + 0.25f);

        if (rng.Chance(0.5f))
        {
            Slab(plan, result, $"{building.Name}_BulkheadLamp", RoofGroup, DetailSurface.Lamp,
                building.Group, CityRect.FromCentre(x, z, 0.9f, 0.35f), top - 0.6f, top - 0.3f);
        }
    }

    // ------------------------------------------------------------------ the tower

    /// <summary>
    /// The landmark, which has to read as one from every square metre of the map: fins up the
    /// shaft to give it a vertical grain, banding on the podium so it is not a 90 m slab, a crown
    /// at the shaft roof, and an aircraft beacon on the mast.
    ///
    /// Kept out of the <c>SKYBOUND_TOWER</c> group deliberately - the Phase 6B massing report
    /// asserts that group's highest renderer is exactly <see cref="CityDesign.TowerTopY"/>, and a
    /// beacon parented into it would fail that by the height of the beacon.
    /// </summary>
    private static void DressTower(CityPlanResult plan, CityDressingResult result)
    {
        const DistrictGroup tint = DistrictGroup.Landmark;
        CityRect podium = CityTraversal.PodiumFootprint;
        CityRect shaft = CityTraversal.ShaftFootprint;

        Slab(plan, result, "Tower_PodiumPlinth", TowerGroup, DetailSurface.Trim, tint,
            podium.Inset(-CityDesign.PlinthProud), 0f, CityDesign.PlinthHeight * 1.6f);
        Slab(plan, result, "Tower_PodiumCornice", TowerGroup, DetailSurface.Trim, tint,
            podium.Inset(-CityDesign.CorniceProud), CityDesign.TowerPodiumY - 1.2f,
            CityDesign.TowerPodiumY);

        for (int i = 1; i < 7; i++)
        {
            float y = i * CityDesign.StoreyHeight;
            Slab(plan, result, $"Tower_PodiumBand{i}", TowerGroup, DetailSurface.Trim, tint,
                podium.Inset(-CityDesign.FloorBandProud), y - 0.12f, y + 0.12f);
        }

        // Glazing on all four podium faces, so the base is not a blank plinth 90 m wide.
        foreach (Facade side in AllFacades)
        {
            Slab(plan, result, $"Tower_PodiumGlass{side}", TowerGroup, DetailSurface.Glass, tint,
                FacadeStrip(podium, side, AlongMin(podium, side) + 6f,
                    AlongMax(podium, side) - 6f, CityDesign.GlassProud),
                CityDesign.PlinthHeight * 1.6f + 0.5f, CityDesign.TowerPodiumY - 1.8f);
        }

        // Four fins on the shaft corners. This is what gives an 80 m extrusion a direction.
        float[] xs = { shaft.MinX, shaft.MaxX };
        float[] zs = { shaft.MinZ, shaft.MaxZ };
        int fin = 0;

        foreach (float x in xs)
        {
            foreach (float z in zs)
            {
                fin++;
                float dx = x < shaft.CentreX ? 0.6f : -0.6f;
                float dz = z < shaft.CentreZ ? 0.6f : -0.6f;

                Slab(plan, result, $"Tower_Fin{fin}", TowerGroup, DetailSurface.Trim, tint,
                    CityRect.FromCentre(x + dx, z + dz, 2.2f, 2.2f),
                    CityDesign.TowerPodiumY, CityDesign.TowerShaftTopY);
            }
        }

        // A lit band every four storeys up the shaft: the tower is the compass, and at night it is
        // the only thing above the fog.
        int bands = Mathf.FloorToInt((CityDesign.TowerShaftTopY - CityDesign.TowerPodiumY)
                                     / (CityDesign.StoreyHeight * 4f));

        for (int i = 1; i <= bands; i++)
        {
            float y = CityDesign.TowerPodiumY + i * CityDesign.StoreyHeight * 4f;

            Slab(plan, result, $"Tower_ShaftBand{i}", TowerGroup, DetailSurface.Neon, tint,
                shaft.Inset(-0.35f), y - 0.18f, y + 0.18f);
        }

        Slab(plan, result, "Tower_Crown", TowerGroup, DetailSurface.Trim, tint,
            shaft.Inset(-1.4f), CityDesign.TowerShaftTopY - 1.6f, CityDesign.TowerShaftTopY);

        CityRect mast = CityRect.FromCentre(shaft.CentreX, shaft.CentreZ,
            CityDesign.TowerMastSize + 1.6f, CityDesign.TowerMastSize + 1.6f);

        Slab(plan, result, "Tower_MastCollar", TowerGroup, DetailSurface.MetalDark, tint,
            mast, CityDesign.TowerShaftTopY, CityDesign.TowerShaftTopY + 1.2f);

        // The beacon stops exactly at the design summit. Anything above it would make the tower
        // taller than the number every report in this project prints.
        Slab(plan, result, "Tower_Beacon", TowerGroup, DetailSurface.Neon, tint,
            CityRect.FromCentre(shaft.CentreX, shaft.CentreZ, CityDesign.TowerMastSize + 1f,
                CityDesign.TowerMastSize + 1f),
            CityDesign.TowerTopY - 1.4f, CityDesign.TowerTopY);

        // The two podium wings, banded so they read as part of the tower rather than as ledges.
        DressWing(plan, result, CityTraversal.WingNorthFootprint, "North");
        DressWing(plan, result, CityTraversal.WingWestFootprint, "West");
    }

    private static void DressWing(CityPlanResult plan, CityDressingResult result,
        in CityRect wing, string name)
    {
        Slab(plan, result, $"Tower_Wing{name}Plinth", TowerGroup, DetailSurface.Trim,
            DistrictGroup.Landmark, wing.Inset(-CityDesign.PlinthProud), 0f,
            CityDesign.PlinthHeight * 1.6f);
        Slab(plan, result, $"Tower_Wing{name}Cornice", TowerGroup, DetailSurface.Trim,
            DistrictGroup.Landmark, wing.Inset(-CityDesign.CorniceProud),
            CityDesign.TowerPodiumY - 1f, CityDesign.TowerPodiumY);

        for (int i = 2; i < 7; i += 2)
        {
            float y = i * CityDesign.StoreyHeight;
            Slab(plan, result, $"Tower_Wing{name}Band{i}", TowerGroup, DetailSurface.Trim,
                DistrictGroup.Landmark, wing.Inset(-CityDesign.FloorBandProud), y - 0.12f,
                y + 0.12f);
        }
    }

    // ------------------------------------------------------------------ traversal

    /// <summary>
    /// The Phase 6C layer, made legible.
    ///
    /// Everything here is the same two moves - a handrail on the outboard edge and a lit strip down
    /// the middle - applied to every deck, ledge and run in the city. That uniformity is the point:
    /// after five minutes a player reads "cyan strip" as "you can go this way", and the city stops
    /// needing to be explored twice.
    /// </summary>
    private static void DressTraversal(CityPlanResult plan, CityDressingResult result)
    {
        if (plan.Traversal == null)
        {
            return;
        }

        foreach (LinkPlan link in plan.Traversal.Links)
        {
            DressDeck(plan, result, link);
        }

        foreach (AscentPlan ascent in plan.Traversal.Ascents)
        {
            DressAscent(plan, result, ascent);
        }

        DressCrane(plan, result);
        DressSpiral(plan, result);
        DressGate(plan, result);
        DressCut(plan, result);
    }

    /// <summary>Rails both sides, posts, an under-truss, and the route strip.</summary>
    private static void DressDeck(CityPlanResult plan, CityDressingResult result, LinkPlan link)
    {
        CityRect deck = link.Deck;
        float y = link.DeckY;
        bool alongZ = deck.Depth >= deck.Width;
        float t = CityDesign.RailThickness;
        string name = link.Name.Replace(' ', '_');

        // The two long edges, whichever axis the deck runs along.
        CityRect railA = alongZ
            ? new CityRect(deck.MinX, deck.MinX + t, deck.MinZ, deck.MaxZ)
            : new CityRect(deck.MinX, deck.MaxX, deck.MinZ, deck.MinZ + t);
        CityRect railB = alongZ
            ? new CityRect(deck.MaxX - t, deck.MaxX, deck.MinZ, deck.MaxZ)
            : new CityRect(deck.MinX, deck.MaxX, deck.MaxZ - t, deck.MaxZ);

        Slab(plan, result, $"{name}_RailA", TraversalGroup, DetailSurface.Metal,
            DistrictGroup.Landmark, railA, y + CityDesign.RailHeight - t,
            y + CityDesign.RailHeight);
        Slab(plan, result, $"{name}_RailB", TraversalGroup, DetailSurface.Metal,
            DistrictGroup.Landmark, railB, y + CityDesign.RailHeight - t,
            y + CityDesign.RailHeight);

        float length = alongZ ? deck.Depth : deck.Width;
        float start = alongZ ? deck.MinZ : deck.MinX;

        for (int i = 1; i <= 3; i++)
        {
            float at = start + length * (i / 4f);

            for (int s = 0; s < 2; s++)
            {
                CityRect edge = s == 0 ? railA : railB;
                CityRect post = alongZ
                    ? CityRect.FromCentre(edge.CentreX, at, t * 1.4f, t * 1.4f)
                    : CityRect.FromCentre(at, edge.CentreZ, t * 1.4f, t * 1.4f);

                Slab(plan, result, $"{name}_Post{i}{s}", TraversalGroup, DetailSurface.Metal,
                    DistrictGroup.Landmark, post, y, y + CityDesign.RailHeight);
            }
        }

        // The truss under the deck. A 14 m span hanging on nothing is the single most obviously
        // unbuilt thing in a greybox.
        CityRect truss = alongZ
            ? CityRect.FromCentre(deck.CentreX, deck.CentreZ, 0.6f, deck.Depth * 0.98f)
            : CityRect.FromCentre(deck.CentreX, deck.CentreZ, deck.Width * 0.98f, 0.6f);

        Slab(plan, result, $"{name}_Truss", TraversalGroup, DetailSurface.MetalDark,
            DistrictGroup.Landmark, truss, y - CityDesign.SkybridgeThickness - 0.7f,
            y - CityDesign.SkybridgeThickness);

        RouteStrip(plan, result, $"{name}_Route", deck, y, alongZ);
    }

    /// <summary>The lit centreline. Its width is fixed; its length follows the surface.</summary>
    private static void RouteStrip(CityPlanResult plan, CityDressingResult result, string name,
        in CityRect surface, float surfaceY, bool alongZ)
    {
        float w = CityDesign.RouteStripWidth;

        CityRect strip = alongZ
            ? CityRect.FromCentre(surface.CentreX, surface.CentreZ, w, surface.Depth * 0.94f)
            : CityRect.FromCentre(surface.CentreX, surface.CentreZ, surface.Width * 0.94f, w);

        Slab(plan, result, name, TraversalGroup, DetailSurface.Route, DistrictGroup.Landmark,
            strip, surfaceY, surfaceY + CityDesign.RouteStripRise);
    }

    /// <summary>
    /// A rail on the outboard edge of every ledge, and a route strip at the two ends of the stack.
    ///
    /// "Outboard" is derived rather than authored: an ascent's ledges hang off the footprint it
    /// tops out on, so the direction away from that footprint's centre is the side with nothing
    /// behind it. That holds for a fire escape, a scaffold, a roof riser and a link stair alike,
    /// which is why none of them needed their own case.
    /// </summary>
    private static void DressAscent(CityPlanResult plan, CityDressingResult result,
        AscentPlan ascent)
    {
        if (ascent.IsRamped || ascent.Landings.Count == 0)
        {
            return;
        }

        string name = ascent.Name.Replace(' ', '_');
        float t = CityDesign.RailThickness;

        for (int i = 0; i < ascent.Landings.Count; i++)
        {
            CityRect ledge = ascent.Landings[i];
            float y = ascent.LandingY[i];

            float dx = ledge.CentreX - ascent.TopFootprint.CentreX;
            float dz = ledge.CentreZ - ascent.TopFootprint.CentreZ;
            bool acrossX = Mathf.Abs(dx) >= Mathf.Abs(dz);

            CityRect rail;

            if (acrossX)
            {
                rail = dx >= 0f
                    ? new CityRect(ledge.MaxX - t, ledge.MaxX, ledge.MinZ, ledge.MaxZ)
                    : new CityRect(ledge.MinX, ledge.MinX + t, ledge.MinZ, ledge.MaxZ);
            }
            else
            {
                rail = dz >= 0f
                    ? new CityRect(ledge.MinX, ledge.MaxX, ledge.MaxZ - t, ledge.MaxZ)
                    : new CityRect(ledge.MinX, ledge.MaxX, ledge.MinZ, ledge.MinZ + t);
            }

            Slab(plan, result, $"{name}_Rail{i}", TraversalGroup, DetailSurface.Metal,
                DistrictGroup.Landmark, rail, y + CityDesign.RailHeight - t,
                y + CityDesign.RailHeight);

            // The underside of a ledge, so a stack read from the street is a structure and not a
            // row of floating shelves. Only on the stacks that *are* read from the street: a riser
            // between two roof plateaus is looked down on, where an under-beam is invisible and
            // would be 130 renderers spent on nothing.
            if (ascent.FromStreet)
            {
                Slab(plan, result, $"{name}_Sole{i}", TraversalGroup, DetailSurface.MetalDark,
                    DistrictGroup.Landmark, ledge.Inset(0.25f),
                    y - CityDesign.AscentLandingThickness - 0.35f,
                    y - CityDesign.AscentLandingThickness);
            }
        }

        // A marker at the foot and at the head. Only two, because a strip on every ledge would
        // read as a ladder painted on a wall rather than as "the way up starts here".
        CityRect first = ascent.Landings[0];
        RouteStrip(plan, result, $"{name}_RouteFoot", first, ascent.LandingY[0],
            first.Depth >= first.Width);

        if (ascent.FromStreet)
        {
            CityRect mouth = CityRect.FromCentre(first.CentreX, first.CentreZ,
                first.Width * 0.8f, first.Depth * 0.8f);
            Slab(plan, result, $"{name}_RouteMouth", TraversalGroup, DetailSurface.Route,
                DistrictGroup.Landmark, mouth, 0f, CityDesign.PaintRise);
        }
    }

    /// <summary>The crane: a cab, a hook block and the ties that make a jib look carried.</summary>
    private static void DressCrane(CityPlanResult plan, CityDressingResult result)
    {
        LinkPlan crane = null;

        foreach (LinkPlan link in plan.Traversal.Links)
        {
            if (link.Kind == LinkKind.Crane)
            {
                crane = link;
                break;
            }
        }

        if (crane == null)
        {
            return;
        }

        // The jib is a *slab*, not a block - CityTraversal emits every deck that way, crane
        // included - so its height comes off the link rather than off a block called "Jib". There
        // is no such block, and looking for one was how the first draft of this method put the
        // crane's cab on the ground.
        float jibY = crane.DeckY;
        CityRect mast = CityRect.FromCentre(0f, 0f, 0f, 0f);
        float mastTop = 0f;
        bool found = false;

        foreach (BlockPlan block in plan.Blocks)
        {
            if (block.Kind == CityPieceKind.Crane && block.Name == "Crane_Mast")
            {
                mast = block.Footprint;
                mastTop = block.TopY;
                found = true;
                break;
            }
        }

        if (!found)
        {
            result.Problems.Add("no crane mast in the plan; the crane was left undressed");
            return;
        }

        // The mast stands at one end of the jib, so the direction from the deck's centre out to the
        // mast is the direction the cab hangs in - away from the walkway, never over it.
        bool alongX = crane.Deck.Width >= crane.Deck.Depth;
        float outX = alongX ? Mathf.Sign(mast.CentreX - crane.Deck.CentreX) : 0f;
        float outZ = alongX ? 0f : Mathf.Sign(mast.CentreZ - crane.Deck.CentreZ);

        float cabX = mast.CentreX + outX * (mast.Width * 0.5f + 1.5f);
        float cabZ = mast.CentreZ + outZ * (mast.Depth * 0.5f + 1.5f);

        Slab(plan, result, "Crane_Cab", TraversalGroup, DetailSurface.Machine,
            DistrictGroup.Industrial,
            CityRect.FromCentre(cabX, cabZ, alongX ? 2.8f : 3.2f, alongX ? 3.2f : 2.8f),
            jibY + 0.4f, jibY + 3.2f);

        Slab(plan, result, "Crane_CabGlass", TraversalGroup, DetailSurface.Glass,
            DistrictGroup.Industrial,
            CityRect.FromCentre(cabX + outX * 1.5f, cabZ + outZ * 1.5f,
                alongX ? 0.2f : 2.6f, alongX ? 2.6f : 0.2f),
            jibY + 1.4f, jibY + 2.9f);

        Slab(plan, result, "Crane_Apex", TraversalGroup, DetailSurface.Neon,
            DistrictGroup.Industrial,
            CityRect.FromCentre(mast.CentreX, mast.CentreZ, 1.3f, 1.3f),
            mastTop, mastTop + 0.9f);

        // Two ties from the apex out along the jib and out along the counter-jib. Rotated boxes,
        // which is the whole reason DetailPlan carries a rotation at all: a crane whose jib hangs
        // off nothing is the most obviously unbuilt object in a greybox.
        float reach = CityDesign.CraneCounterJibLength;
        float rise = mastTop - jibY;
        float length = Mathf.Sqrt(reach * reach + rise * rise);
        float pitch = Mathf.Atan2(rise, reach) * Mathf.Rad2Deg;
        float yaw = alongX ? 90f : 0f;

        for (int i = 0; i < 2; i++)
        {
            // At yaw 0 the box's local Z is world +Z and at yaw 90 it is world +X, and a positive
            // pitch always tips the local +Z end down - so the far, lower end of each tie decides
            // the sign, whichever axis the jib runs along.
            float sign = i == 0 ? 1f : -1f;
            float dx = alongX ? sign * reach * 0.5f : 0f;
            float dz = alongX ? 0f : sign * reach * 0.5f;

            Emit(plan, result, $"Crane_Tie{i}", TraversalGroup, DetailSurface.MetalDark,
                DistrictGroup.Industrial,
                new Vector3(mast.CentreX + dx, jibY + rise * 0.5f, mast.CentreZ + dz),
                new Vector3(0.24f, 0.24f, length),
                sign > 0f ? pitch : -pitch, yaw);
        }
    }

    /// <summary>
    /// The local right axis of a box rotated <c>Euler(pitch, yaw, 0)</c>. Pitch does not move it,
    /// because pitch turns about it.
    ///
    /// Written out in trigonometry rather than as <c>Quaternion.Euler(...) * Vector3.right</c> for
    /// the reason PHASE6.md gives under "Verifying without the editor": <c>Quaternion.Euler</c> is
    /// an engine ECall, so a plan that used it would stop being runnable outside Unity and the whole
    /// offline half of this project's verification would go with it.
    /// </summary>
    private static Vector3 LocalRight(float yawDegrees)
    {
        float y = yawDegrees * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(y), 0f, -Mathf.Sin(y));
    }

    /// <summary>The local up axis of the same box. This one pitch does move.</summary>
    private static Vector3 LocalUp(float pitchDegrees, float yawDegrees)
    {
        float p = pitchDegrees * Mathf.Deg2Rad;
        float y = yawDegrees * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(p) * Mathf.Sin(y), Mathf.Cos(p), Mathf.Sin(p) * Mathf.Cos(y));
    }

    /// <summary>
    /// Rails and a route strip on the tower spiral, matching each run's own rotation.
    ///
    /// Read off the emitted ramps rather than recomputed: the spiral's pitch and yaw are
    /// <see cref="CityTraversal"/>'s arithmetic, and deriving them a second time here is how the
    /// two would eventually disagree.
    /// </summary>
    private static void DressSpiral(CityPlanResult plan, CityDressingResult result)
    {
        float t = CityDesign.RailThickness;
        int index = 0;

        foreach (RampPlan ramp in plan.Ramps)
        {
            if (ramp.GroupName != CityTraversal.TowerAscentGroup)
            {
                continue;
            }

            index++;
            Vector3 right = LocalRight(ramp.YawDegrees);
            Vector3 up = LocalUp(ramp.PitchDegrees, ramp.YawDegrees);

            // The shaft is inboard, so the rail goes on whichever side faces away from it.
            Vector3 inboard = new Vector3(CityTraversal.ShaftFootprint.CentreX - ramp.Centre.x, 0f,
                CityTraversal.ShaftFootprint.CentreZ - ramp.Centre.z);
            float sign = Vector3.Dot(right, inboard) > 0f ? -1f : 1f;

            Vector3 railOffset = right * (sign * (ramp.Size.x * 0.5f - t))
                                 + up * (ramp.Size.y * 0.5f + CityDesign.RailHeight);

            Emit(plan, result, $"TowerSpiral_Rail{index}", TraversalGroup, DetailSurface.Metal,
                DistrictGroup.Landmark, ramp.Centre + railOffset,
                new Vector3(t, t, ramp.Size.z), ramp.PitchDegrees, ramp.YawDegrees);

            Vector3 stripOffset = up * (ramp.Size.y * 0.5f + CityDesign.RouteStripRise);

            Emit(plan, result, $"TowerSpiral_Route{index}", TraversalGroup, DetailSurface.Route,
                DistrictGroup.Landmark, ramp.Centre + stripOffset,
                new Vector3(CityDesign.RouteStripWidth, 0.06f, ramp.Size.z * 0.96f),
                ramp.PitchDegrees, ramp.YawDegrees);
        }

        int landing = 0;

        foreach (SlabPlan slab in plan.Slabs)
        {
            if (slab.GroupName != CityTraversal.TowerAscentGroup)
            {
                continue;
            }

            landing++;
            RouteStrip(plan, result, $"TowerSpiral_Landing{landing}", slab.Footprint.Inset(0.6f),
                slab.SurfaceY, slab.Footprint.Depth >= slab.Footprint.Width);
        }
    }

    /// <summary>Chevrons and a pair of beacons on the hoarding, so a locked tower looks locked.</summary>
    private static void DressGate(CityPlanResult plan, CityDressingResult result)
    {
        foreach (BlockPlan block in plan.Blocks)
        {
            if (block.Kind != CityPieceKind.Gate)
            {
                continue;
            }

            CityRect wall = block.Footprint;
            bool alongX = wall.Width >= wall.Depth;

            for (int i = 1; i <= 3; i++)
            {
                float y = block.BottomY + (block.TopY - block.BottomY) * (i / 4f);

                Slab(plan, result, $"{block.Name}_Chevron{i}",
                    GateDetailGroup, DetailSurface.Hazard,
                    DistrictGroup.Industrial, wall.Inset(-0.08f), y - 0.3f, y + 0.3f);
            }

            CityRect beacon = alongX
                ? CityRect.FromCentre(wall.CentreX, wall.CentreZ, 1.0f, wall.Depth + 0.3f)
                : CityRect.FromCentre(wall.CentreX, wall.CentreZ, wall.Width + 0.3f, 1.0f);

            Slab(plan, result, $"{block.Name}_Beacon", GateDetailGroup,
                DetailSurface.Neon, DistrictGroup.Industrial, beacon, block.TopY - 0.5f,
                block.TopY - 0.1f);
        }
    }

    /// <summary>The Cut: a hazard kerb along both lips and light down in the trench.</summary>
    private static void DressCut(CityPlanResult plan, CityDressingResult result)
    {
        CityRect cut = CityPlan.CutBounds();
        float w = 0.7f;

        Slab(plan, result, "Cut_KerbW", StreetGroup, DetailSurface.Hazard, DistrictGroup.OldQuarter,
            new CityRect(cut.MinX - 1.9f, cut.MinX - 1.9f + w, cut.MinZ, cut.MaxZ),
            0f, CityDesign.KerbRise);
        Slab(plan, result, "Cut_KerbE", StreetGroup, DetailSurface.Hazard, DistrictGroup.OldQuarter,
            new CityRect(cut.MaxX + 1.9f - w, cut.MaxX + 1.9f, cut.MinZ, cut.MaxZ),
            0f, CityDesign.KerbRise);

        for (int i = 0; i < 4; i++)
        {
            float z = cut.MinZ + cut.Depth * (i + 0.5f) / 4f;

            Slab(plan, result, $"Cut_Lamp{i}", StreetGroup, DetailSurface.Lamp,
                DistrictGroup.OldQuarter,
                CityRect.FromCentre(cut.MinX + 0.9f, z, 0.5f, 1.6f),
                CityDesign.CutFloorY + 3.2f, CityDesign.CutFloorY + 3.5f);
        }
    }

    // ------------------------------------------------------------------ objectives

    /// <summary>
    /// A halo on every relay pad and every anchor pad, and a beacon on top of each relay mast.
    ///
    /// The beacon is named <c>{relay}_Beacon</c> on purpose: `SkyboundCityBuilder` looks that name
    /// up and adds it to the relay's status renderers, so it turns from cyan to green with the
    /// plinth and the mast when the relay is captured. Nothing here knows that - it just has to
    /// keep the name.
    /// </summary>
    private static void DressObjectives(CityPlanResult plan, CityDressingResult result)
    {
        if (plan.Objectives == null)
        {
            return;
        }

        foreach (RelayObjective relay in plan.Objectives.Relays)
        {
            Slab(plan, result, $"{relay.Name}_Halo", ObjectiveGroup, DetailSurface.Route,
                relay.Group, relay.Pad.Inset(-1.4f), relay.RoofY,
                relay.RoofY + CityDesign.RouteStripRise);

            // Hugs the mast rather than ringing the pad. The pad is where the player has to stand
            // to capture the relay, and a 0.5 m collar across it would be something they walk
            // through at the one moment the level is asking them to look at it.
            Slab(plan, result, $"{relay.Name}_Collar", ObjectiveGroup, DetailSurface.MetalDark,
                relay.Group,
                CityRect.FromCentre(relay.Mast.CentreX, relay.Mast.CentreZ, 1.8f, 1.8f),
                relay.RoofY + CityDesign.RelayPadRise,
                relay.RoofY + CityDesign.RelayPadRise + 0.35f);

            float mastTop = relay.RoofY + CityDesign.RelayPadRise + CityDesign.RelayMastHeight;

            Slab(plan, result, $"{relay.Name}_Dish", ObjectiveGroup, DetailSurface.Metal,
                relay.Group,
                CityRect.FromCentre(relay.Mast.CentreX, relay.Mast.CentreZ, 2.6f, 0.25f),
                mastTop - 2.2f, mastTop - 0.6f);

            Slab(plan, result, $"{relay.Name}_Beacon", ObjectiveGroup, DetailSurface.Neon,
                relay.Group,
                CityRect.FromCentre(relay.Mast.CentreX, relay.Mast.CentreZ, 1.3f, 1.3f),
                mastTop, mastTop + 0.9f);
        }

        foreach (AnchorObjective anchor in plan.Objectives.Anchors)
        {
            if (anchor.Kind == AnchorKind.Relay)
            {
                continue;
            }

            Slab(plan, result, $"{anchor.Name}_Halo".Replace(' ', '_'), ObjectiveGroup,
                DetailSurface.Route, DistrictGroup.Landmark, anchor.Pad.Inset(-0.6f),
                anchor.SurfaceY, anchor.SurfaceY + CityDesign.RouteStripRise);
        }
    }

    // ------------------------------------------------------------------ streets

    /// <summary>
    /// The ground plane, which the Phase 6B greybox left as two flat greys.
    ///
    /// Kerbs and a centre line turn an avenue from a gap between blocks into a road; lamps give the
    /// street a rhythm and a scale; the plaza gets the only ornament in the city, because it is
    /// where every run starts and the first thing a player ever sees.
    /// </summary>
    private static void DressStreets(CityPlanResult plan, CityDressingResult result)
    {
        float half = CityDesign.GridSpan * 0.5f;

        for (int axis = 0; axis < 2; axis++)
        {
            for (int sign = -1; sign <= 1; sign += 2)
            {
                float centre = CityDesign.AvenueCentre(sign);
                string name = $"Avenue_{(axis == 0 ? "X" : "Z")}{(sign < 0 ? "W" : "E")}";

                DressAvenue(plan, result, axis == 0, centre, half, name);
            }
        }

        DressPlaza(plan, result);
        DressPerimeter(plan, result);
    }

    /// <summary>
    /// One avenue. <paramref name="alongZ"/> is true where the avenue runs north-south, which is
    /// the case for the two whose centreline is an X coordinate.
    /// </summary>
    private static void DressAvenue(CityPlanResult plan, CityDressingResult result, bool alongZ,
        float centre, float half, string name)
    {
        float kerb = CityDesign.AvenueWidth * 0.5f - CityDesign.KerbWidth;
        CityRect cut = CityPlan.CutBounds();

        for (int side = -1; side <= 1; side += 2)
        {
            float at = centre + side * kerb;

            CityRect rect = alongZ
                ? new CityRect(at, at + side * CityDesign.KerbWidth, -half, half)
                : new CityRect(-half, half, at, at + side * CityDesign.KerbWidth);

            Slab(plan, result, $"{name}_Kerb{(side < 0 ? "A" : "B")}", StreetGroup,
                DetailSurface.Concrete, DistrictGroup.CityCenter, rect, 0f, CityDesign.KerbRise);
        }

        CityRect line = alongZ
            ? CityRect.FromCentre(centre, 0f, 0.4f, CityDesign.GridSpan)
            : CityRect.FromCentre(0f, centre, CityDesign.GridSpan, 0.4f);

        Slab(plan, result, $"{name}_Line", StreetGroup, DetailSurface.Paint,
            DistrictGroup.CityCenter, line, 0f, CityDesign.PaintRise);

        int lamps = Mathf.Max(2, Mathf.FloorToInt(CityDesign.GridSpan / CityDesign.StreetLampSpacing));
        float pitch = CityDesign.GridSpan / lamps;

        for (int i = 0; i < lamps; i++)
        {
            float along = -half + pitch * (i + 0.5f);
            float side = (i % 2 == 0 ? 1f : -1f) * kerb;

            float x = alongZ ? centre + side : along;
            float z = alongZ ? along : centre + side;

            if (cut.Contains(x, z))
            {
                continue;
            }

            Slab(plan, result, $"{name}_LampPost{i}", StreetGroup, DetailSurface.Metal,
                DistrictGroup.CityCenter, CityRect.FromCentre(x, z, 0.26f, 0.26f), 0f,
                CityDesign.StreetLampHeight);
            Slab(plan, result, $"{name}_LampHead{i}", StreetGroup, DetailSurface.Lamp,
                DistrictGroup.CityCenter, CityRect.FromCentre(x, z, 1.5f, 0.55f),
                CityDesign.StreetLampHeight - 0.35f, CityDesign.StreetLampHeight - 0.05f);
        }
    }

    private static void DressPlaza(CityPlanResult plan, CityDressingResult result)
    {
        CityRect plaza = CityDesign.Plaza;

        Slab(plan, result, "Plaza_RingOuter", StreetGroup, DetailSurface.Route,
            DistrictGroup.CityCenter, plaza.Inset(3f), 0f, CityDesign.PaintRise);
        Slab(plan, result, "Plaza_RingInner", StreetGroup, DetailSurface.Paint,
            DistrictGroup.CityCenter, plaza.Inset(6f), 0f, CityDesign.PaintRise * 1.5f);

        // Four pylons at the corners of the plaza. They are the first thing in the player's eye
        // line at spawn, and the colour they are lit in is the colour every objective uses.
        float[] xs = { plaza.MinX + 3.5f, plaza.MaxX - 3.5f };
        float[] zs = { plaza.MinZ + 3.5f, plaza.MaxZ - 3.5f };
        int index = 0;

        foreach (float x in xs)
        {
            foreach (float z in zs)
            {
                index++;
                CityRect pylon = CityRect.FromCentre(x, z, 1.1f, 1.1f);

                Slab(plan, result, $"Plaza_Pylon{index}", StreetGroup, DetailSurface.Concrete,
                    DistrictGroup.CityCenter, pylon, 0f, 5.2f);
                Slab(plan, result, $"Plaza_PylonLamp{index}", StreetGroup, DetailSurface.Neon,
                    DistrictGroup.CityCenter, pylon.Inset(-0.18f), 5.2f, 5.9f);
            }
        }

        // Planters down the east and west sides, which is what stops a 40 m square reading as an
        // empty car park. Deliberately not the north and south sides: the player spawns on the
        // plaza's south edge facing north up the city axis, and the first thing they do in this
        // level is run straight up the middle of it. A planter with no collider standing on that
        // line would be the first thing they ran through.
        for (int i = 0; i < 3; i++)
        {
            float z = plaza.MinZ + plaza.Depth * (i + 1) / 4f;

            for (int side = 0; side < 2; side++)
            {
                float x = side == 0 ? plaza.MinX + 2f : plaza.MaxX - 2f;
                CityRect box = CityRect.FromCentre(x, z, 1.6f, 4.4f);

                Slab(plan, result, $"Plaza_Planter{i}{side}", StreetGroup, DetailSurface.Concrete,
                    DistrictGroup.CityCenter, box, 0f, 0.75f);
                Slab(plan, result, $"Plaza_PlanterSoil{i}{side}", StreetGroup, DetailSurface.Rust,
                    DistrictGroup.CityCenter, box.Inset(0.28f), 0.75f, 0.85f);
            }
        }
    }

    /// <summary>
    /// A parapet round the edge of the core. It is not a barrier - it has no collider, like
    /// everything else here - it is the line that stops the paving from simply ending in mid-air
    /// where the backdrop begins.
    /// </summary>
    private static void DressPerimeter(CityPlanResult plan, CityDressingResult result)
    {
        CityRect core = CityDesign.CoreBounds;
        const float thickness = 1.2f;
        const float height = 1.1f;

        Slab(plan, result, "Perimeter_W", StreetGroup, DetailSurface.Concrete,
            DistrictGroup.Landmark,
            new CityRect(core.MinX, core.MinX + thickness, core.MinZ, core.MaxZ), 0f, height);
        Slab(plan, result, "Perimeter_E", StreetGroup, DetailSurface.Concrete,
            DistrictGroup.Landmark,
            new CityRect(core.MaxX - thickness, core.MaxX, core.MinZ, core.MaxZ), 0f, height);
        Slab(plan, result, "Perimeter_S", StreetGroup, DetailSurface.Concrete,
            DistrictGroup.Landmark,
            new CityRect(core.MinX, core.MaxX, core.MinZ, core.MinZ + thickness), 0f, height);
        Slab(plan, result, "Perimeter_N", StreetGroup, DetailSurface.Concrete,
            DistrictGroup.Landmark,
            new CityRect(core.MinX, core.MaxX, core.MaxZ - thickness, core.MaxZ), 0f, height);
    }

    // ------------------------------------------------------------------ the backdrop

    /// <summary>
    /// The city the player cannot reach.
    ///
    /// Four rings of silhouette outside the core, yawed to face the middle so the ring does not
    /// read as a grid seen edge-on. It exists for one reason: from the tower summit the Phase 6B
    /// city ended at a hard line with sky under it, which made a 600 m map feel like a diorama. It
    /// is also the cheapest thing in this file per unit of effect - a hundred boxes that are never
    /// lit, never walked on and mostly dissolved by fog.
    ///
    /// A ring of ground goes under it for the same reason, laid as four slabs outside the core
    /// rather than one big one beneath it, so it can never z-fight with the paving.
    /// </summary>
    private static void DressBackdrop(CityPlanResult plan, CityDressingResult result)
    {
        Rng rng = new Rng("backdrop");
        float outer = CityDesign.BackdropOuterRadius + CityDesign.BackdropMaxWidth;
        float core = CityDesign.CoreExtent * 0.5f;

        // The ground ring: west, east, south, north bands filling everything between the core and
        // the far side of the outermost backdrop ring.
        Slab(plan, result, "Backdrop_GroundW", BackdropGroup, DetailSurface.Backdrop,
            DistrictGroup.Landmark, new CityRect(-outer, -core, -outer, outer), -0.6f, -0.1f);
        Slab(plan, result, "Backdrop_GroundE", BackdropGroup, DetailSurface.Backdrop,
            DistrictGroup.Landmark, new CityRect(core, outer, -outer, outer), -0.6f, -0.1f);
        Slab(plan, result, "Backdrop_GroundS", BackdropGroup, DetailSurface.Backdrop,
            DistrictGroup.Landmark, new CityRect(-core, core, -outer, -core), -0.6f, -0.1f);
        Slab(plan, result, "Backdrop_GroundN", BackdropGroup, DetailSurface.Backdrop,
            DistrictGroup.Landmark, new CityRect(-core, core, core, outer), -0.6f, -0.1f);

        for (int ring = 0; ring < CityDesign.BackdropRings; ring++)
        {
            float radius = CityDesign.BackdropInnerRadius + ring * CityDesign.BackdropRingStep;

            for (int i = 0; i < CityDesign.BackdropPerRing; i++)
            {
                // Half a slot of stagger between rings, so the blocks never line up radially.
                float angle = (i + (ring % 2) * 0.5f) / CityDesign.BackdropPerRing * 360f
                              + rng.Range(-3.5f, 3.5f);
                float r = radius + rng.Range(-14f, 14f);
                float radians = angle * Mathf.Deg2Rad;

                float x = Mathf.Sin(radians) * r;
                float z = Mathf.Cos(radians) * r;

                float width = rng.Range(CityDesign.BackdropMinWidth, CityDesign.BackdropMaxWidth);
                float depth = rng.Range(CityDesign.BackdropMinWidth, CityDesign.BackdropMaxWidth);

                // Nearer rings are shorter, so the skyline steps up and away rather than walling
                // the core in. The occasional spike keeps the line from reading as a ramp.
                float lift = ring / Mathf.Max(1f, CityDesign.BackdropRings - 1f);
                float height = Mathf.Lerp(CityDesign.BackdropMinHeight,
                    CityDesign.BackdropMaxHeight, lift * rng.Range(0.55f, 1f));

                if (rng.Chance(0.08f))
                {
                    height *= 1.6f;
                }

                Emit(plan, result, $"Backdrop_R{ring}_{i}", BackdropGroup, DetailSurface.Backdrop,
                    DistrictGroup.Landmark, new Vector3(x, height * 0.5f, z),
                    new Vector3(width, height, depth), 0f, angle);
            }
        }
    }
}
