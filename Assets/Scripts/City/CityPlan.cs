using System.Collections.Generic;
using UnityEngine;

/// <summary>What a planned piece of the city is, and therefore how it must be built.</summary>
public enum CityPieceKind
{
    /// <summary>Walkable ground slab. Collidable.</summary>
    Ground,

    /// <summary>Massing block. Collidable walls and a collidable roof.</summary>
    Building,

    /// <summary>Landmark massing: the tower podium, shaft and mast.</summary>
    Landmark,

    /// <summary>Floor, walls, ramp and bridges of the sunken Cut. Collidable.</summary>
    Cut,

    /// <summary>Phase 6C: a skybridge deck. Collidable.</summary>
    Deck,

    /// <summary>Phase 6C: one ledge of a fire escape, scaffold, riser or link stair.</summary>
    Ascent,

    /// <summary>Phase 6C: the tower crane's jib, mast and counter-jib.</summary>
    Crane,

    /// <summary>Phase 6C: a run or a corner landing of the spiral up the tower shaft.</summary>
    TowerAscent,

    /// <summary>Phase 6D: a relay plinth, a relay mast or a respawn anchor pad. Decoration.</summary>
    Objective,

    /// <summary>Phase 6D: the hoarding across the foot of the tower spiral. Collidable.</summary>
    Gate
}

/// <summary>One planned massing block.</summary>
public readonly struct BuildingPlan
{
    public readonly string Name;
    public readonly string CellName;
    public readonly DistrictGroup Group;
    public readonly CityRect Footprint;

    /// <summary>Height of the roof surface above y = 0.</summary>
    public readonly float RoofY;

    public readonly int Storeys;

    /// <summary>Sub-storey variation applied on top of the cluster's storey height.</summary>
    public readonly float RoofOffset;

    /// <summary>
    /// Roofs sharing a cluster id form one traversal group and must satisfy
    /// <see cref="CityDesign.RoofClusterTolerance"/>. -1 means the building stands alone.
    /// </summary>
    public readonly int ClusterId;

    public readonly int LotColumn;
    public readonly int LotRow;

    public BuildingPlan(string name, string cellName, DistrictGroup group, CityRect footprint,
        float roofY, int storeys, float roofOffset, int clusterId, int lotColumn, int lotRow)
    {
        Name = name;
        CellName = cellName;
        Group = group;
        Footprint = footprint;
        RoofY = roofY;
        Storeys = storeys;
        RoofOffset = roofOffset;
        ClusterId = clusterId;
        LotColumn = lotColumn;
        LotRow = lotRow;
    }
}

/// <summary>One planned slab: ground, a Cut surface, or a bridge deck.</summary>
public readonly struct SlabPlan
{
    public readonly string Name;
    public readonly string GroupName;
    public readonly CityPieceKind Kind;
    public readonly CityRect Footprint;

    /// <summary>Walking surface, not the slab centre.</summary>
    public readonly float SurfaceY;

    public readonly float Thickness;

    public SlabPlan(string name, string groupName, CityPieceKind kind, CityRect footprint,
        float surfaceY, float thickness)
    {
        Name = name;
        GroupName = groupName;
        Kind = kind;
        Footprint = footprint;
        SurfaceY = surfaceY;
        Thickness = thickness;
    }
}

/// <summary>A solid block placed by absolute extents - the Cut's retaining walls and the tower.</summary>
public readonly struct BlockPlan
{
    public readonly string Name;
    public readonly string GroupName;
    public readonly CityPieceKind Kind;
    public readonly CityRect Footprint;
    public readonly float BottomY;
    public readonly float TopY;

    /// <summary>
    /// False for pure silhouette - the crane's counter-jib. The builder destroys its collider, so
    /// it can never catch a falling player or become an unintended shortcut.
    /// </summary>
    public readonly bool Collidable;

    public BlockPlan(string name, string groupName, CityPieceKind kind, CityRect footprint,
        float bottomY, float topY, bool collidable = true)
    {
        Name = name;
        GroupName = groupName;
        Kind = kind;
        Footprint = footprint;
        BottomY = bottomY;
        TopY = topY;
        Collidable = collidable;
    }
}

/// <summary>A sloped deck. Pitch is derived from the rise over the run, never authored blind.</summary>
public readonly struct RampPlan
{
    public readonly string Name;
    public readonly string GroupName;
    public readonly Vector3 Centre;
    public readonly Vector3 Size;
    public readonly float PitchDegrees;

    /// <summary>
    /// Which way the run points. The rotation is <c>Quaternion.Euler(pitch, yaw, 0)</c>, so at
    /// yaw 0 a positive pitch tips the +Z end down and at yaw 90 it tips the +X end down. The
    /// box's local Z is always the direction of the run, whatever the yaw.
    /// </summary>
    public readonly float YawDegrees;

    public RampPlan(string name, string groupName, Vector3 centre, Vector3 size, float pitchDegrees,
        float yawDegrees = 0f)
    {
        Name = name;
        GroupName = groupName;
        Centre = centre;
        Size = size;
        PitchDegrees = pitchDegrees;
        YawDegrees = yawDegrees;
    }
}

/// <summary>The complete greybox, as data. Nothing here has touched a scene yet.</summary>
public sealed class CityPlanResult
{
    public readonly List<BuildingPlan> Buildings = new List<BuildingPlan>();
    public readonly List<SlabPlan> Slabs = new List<SlabPlan>();
    public readonly List<BlockPlan> Blocks = new List<BlockPlan>();
    public readonly List<RampPlan> Ramps = new List<RampPlan>();

    /// <summary>Phase 6D: the trigger volumes. Never walked on, never in anything's way.</summary>
    public readonly List<VolumePlan> Volumes = new List<VolumePlan>();

    /// <summary>
    /// Phase 6E: the environment art. A separate list from the four above, and separate on purpose -
    /// a <see cref="BlockPlan"/> may be collidable and a <see cref="DetailPlan"/> may not, so
    /// keeping them apart is what makes "the art pass adds nothing solid" true by construction
    /// rather than by review. Nothing here is counted by <see cref="ColliderCount"/>, because there
    /// is nothing to count.
    /// </summary>
    public readonly List<DetailPlan> Details = new List<DetailPlan>();

    /// <summary>
    /// The Phase 6C traversal layer hung on this massing: the links, the ascents and the relays,
    /// as data. Its geometry is already in the four lists above - this is the network that geometry
    /// means. Null only if something built a plan without calling <see cref="CityPlan.Generate"/>.
    /// </summary>
    public CityTraversalResult Traversal;

    /// <summary>
    /// The Phase 6D mission hung on that traversal layer: the relays, the respawn anchors, the
    /// gate across the tower spiral and the summit finish. Its geometry is in the lists above.
    /// </summary>
    public CityObjectivesResult Objectives;

    /// <summary>
    /// The Phase 6E art layer hung on all three: facades, rooftops, signage, street furniture, the
    /// dressed traversal layer and the backdrop ring. Its geometry is in <see cref="Details"/>.
    /// </summary>
    public CityDressingResult Dressing;

    /// <summary>Every collidable object the builder will emit.</summary>
    public int ColliderCount
    {
        get
        {
            int blocks = 0;

            foreach (BlockPlan block in Blocks)
            {
                if (block.Collidable)
                {
                    blocks++;
                }
            }

            // Triggers are colliders too. They are counted because the Phase 6A budget is a
            // physics budget and the massing report measures the scene, not the plan - a number
            // here that quietly excluded triggers would stop matching what the report prints.
            return Buildings.Count + Slabs.Count + blocks + Ramps.Count + Volumes.Count;
        }
    }

    public IEnumerable<BuildingPlan> InCell(string cellName)
    {
        foreach (BuildingPlan b in Buildings)
        {
            if (b.CellName == cellName)
            {
                yield return b;
            }
        }
    }

    public float TallestRoof
    {
        get
        {
            float top = 0f;
            foreach (BlockPlan block in Blocks)
            {
                top = Mathf.Max(top, block.TopY);
            }

            foreach (BuildingPlan b in Buildings)
            {
                top = Mathf.Max(top, b.RoofY);
            }

            return top;
        }
    }
}

/// <summary>
/// Turns <see cref="CityDesign"/> into the concrete list of boxes that make up the Phase 6B
/// greybox.
///
/// This is a pure function of the design constants and a fixed seed: no scene, no UnityEditor, no
/// <c>System.Random</c> (whose sequence is not guaranteed stable across runtimes). That is the
/// point. Every claim the Phase 6B report makes about the city - heights inside their bands, the
/// roof cluster rule, avenue widths, footprint coverage - is asserted by the EditMode tests
/// against this plan, without needing the editor to have built anything.
///
/// The builder is a dumb consumer: it instantiates exactly what it is handed.
/// </summary>
public static class CityPlan
{
    /// <summary>Change this and the whole city relays out. Fixed so builds are reproducible.</summary>
    public const uint Seed = 0x5CB0D01u;

    private const float GroundThickness = 2f;
    private const float SlabThickness = 0.6f;

    /// <summary>
    /// Sub-storey roof variation. Deliberately capped below
    /// <see cref="CityDesign.RoofClusterTolerance"/>, so a cluster's roofs vary visibly while the
    /// worst pair inside it still sits 0.4 m inside the rule rather than exactly on it.
    /// </summary>
    private static readonly float[] RoofOffsets = { 0f, 0.8f, 1.6f };

    /// <summary>Fraction of its lot a corporate tower occupies; the rest becomes forecourt.</summary>
    private const float CorporateLotFill = 0.62f;

    // ------------------------------------------------------------------ rng

    /// <summary>
    /// xorshift32. Small, and - unlike <c>System.Random</c> - guaranteed to produce the same
    /// sequence in the editor, in a player and in a test runner on any runtime.
    /// </summary>
    private struct Rng
    {
        private uint state;

        public Rng(uint seed)
        {
            state = seed == 0u ? 0x9E3779B9u : seed;
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

        public T Pick<T>(T[] items) => items[(int)(NextUInt() % (uint)items.Length)];
    }

    /// <summary>FNV-1a, so a cell's layout depends only on its own name.</summary>
    private static uint HashSeed(string text)
    {
        uint hash = 2166136261u;

        foreach (char c in text)
        {
            hash ^= c;
            hash *= 16777619u;
        }

        return hash ^ Seed;
    }

    // ------------------------------------------------------------------ entry point

    public static CityPlanResult Generate()
    {
        CityPlanResult plan = new CityPlanResult();

        PlanGround(plan);
        PlanCut(plan);

        foreach (DistrictCell cell in CityDesign.Cells)
        {
            if (cell.Group == DistrictGroup.Landmark)
            {
                PlanTower(plan, cell);
            }
            else
            {
                PlanBlock(plan, cell);
            }
        }

        // Phase 6C hangs the traversal layer on the finished massing: the massing has to exist
        // before anything can work out which roof a bridge lands on.
        plan.Traversal = CityTraversal.Plan(plan);

        // Phase 6D hangs the mission on the traversal layer, for the same reason one step further
        // on: a relay stands on a roof, an anchor stands at the top of a fire escape, and the gate
        // stands at the foot of the spiral - none of which exist until the line above has run.
        plan.Objectives = CityObjectives.Plan(plan);

        // Phase 6E goes last, because every rule in it is a question about what is already there:
        // which roof edges nothing can reach, which facades face an avenue, where a bridge deck
        // runs and which pads must be kept clear. It adds only to Details, so nothing above this
        // line can be moved by it.
        plan.Dressing = CityDressing.Plan(plan);

        return plan;
    }

    // ------------------------------------------------------------------ ground

    /// <summary>
    /// The core is paved as a 7 x 7 patchwork rather than one slab: superblock, avenue and
    /// perimeter bands each get their own piece. Phase 6E needs that seam to colour-zone the
    /// districts, and Phase 6B needs it to leave a hole for the Cut.
    /// </summary>
    private static void PlanGround(CityPlanResult plan)
    {
        float[] edges = BandEdges();
        string[] labels = { "MarginW", "West", "AvenueW", "Centre", "AvenueE", "East", "MarginE" };
        string[] rowLabels = { "MarginS", "South", "AvenueS", "Middle", "AvenueN", "North", "MarginN" };

        CityRect cut = CutBounds();

        for (int xi = 0; xi < edges.Length - 1; xi++)
        {
            for (int zi = 0; zi < edges.Length - 1; zi++)
            {
                CityRect rect = new CityRect(edges[xi], edges[xi + 1], edges[zi], edges[zi + 1]);
                string name = $"Ground_{labels[xi]}_{rowLabels[zi]}";

                if (!rect.Overlaps(cut))
                {
                    plan.Slabs.Add(new SlabPlan(name, "GROUND", CityPieceKind.Ground, rect, 0f,
                        GroundThickness));
                    continue;
                }

                // The Cut runs north-south, so the slab it crosses is paved either side of it.
                plan.Slabs.Add(new SlabPlan($"{name}_A", "GROUND", CityPieceKind.Ground,
                    new CityRect(rect.MinX, cut.MinX, rect.MinZ, rect.MaxZ), 0f, GroundThickness));
                plan.Slabs.Add(new SlabPlan($"{name}_B", "GROUND", CityPieceKind.Ground,
                    new CityRect(cut.MaxX, rect.MaxX, rect.MinZ, rect.MaxZ), 0f, GroundThickness));
            }
        }
    }

    /// <summary>The 7 band boundaries across the core: margin, block, avenue, block, avenue, block, margin.</summary>
    private static float[] BandEdges()
    {
        float half = CityDesign.CoreExtent * 0.5f;
        float w = CityDesign.CellBounds(0, 0).MinX;
        float[] edges = new float[8];

        edges[0] = -half;
        edges[1] = w;
        edges[2] = CityDesign.CellBounds(0, 0).MaxX;
        edges[3] = CityDesign.CellBounds(1, 1).MinX;
        edges[4] = CityDesign.CellBounds(1, 1).MaxX;
        edges[5] = CityDesign.CellBounds(2, 2).MinX;
        edges[6] = CityDesign.CellBounds(2, 2).MaxX;
        edges[7] = half;
        return edges;
    }

    // ------------------------------------------------------------------ the Cut

    /// <summary>
    /// The sunken loading trench, running the full depth of the Old Quarter on the block's centre
    /// line. It is the map's only sub-street space.
    /// </summary>
    public static CityRect CutBounds()
    {
        CityRect quarter = CityDesign.Cell("OldQuarter").Bounds;
        return CityRect.FromCentre(quarter.CentreX, quarter.CentreZ,
            CityDesign.CutWidth, quarter.Depth);
    }

    private static void PlanCut(CityPlanResult plan)
    {
        CityRect cut = CutBounds();
        const string group = "THE_CUT";

        plan.Slabs.Add(new SlabPlan("Cut_Floor", group, CityPieceKind.Cut, cut,
            CityDesign.CutFloorY, GroundThickness));

        // Retaining walls, so the trench reads as cut into the block rather than drawn on it.
        const float wallThickness = 1.2f;
        plan.Blocks.Add(new BlockPlan("Cut_WallW", group, CityPieceKind.Cut,
            new CityRect(cut.MinX - wallThickness, cut.MinX, cut.MinZ, cut.MaxZ),
            CityDesign.CutFloorY, 0f));
        plan.Blocks.Add(new BlockPlan("Cut_WallE", group, CityPieceKind.Cut,
            new CityRect(cut.MaxX, cut.MaxX + wallThickness, cut.MinZ, cut.MaxZ),
            CityDesign.CutFloorY, 0f));

        // Two street-level crossings. Without them the trench bisects the Old Quarter and the
        // block can only be circled, which is not what a shortcut is supposed to do.
        float span = cut.Depth;
        for (int i = 0; i < 2; i++)
        {
            float z = cut.MinZ + span * (i == 0 ? 0.30f : 0.70f);
            plan.Slabs.Add(new SlabPlan($"Cut_Bridge_{i}", group, CityPieceKind.Cut,
                CityRect.FromCentre(cut.CentreX, z, CityDesign.CutWidth + 2.4f, 6f), 0f,
                SlabThickness));
        }

        // Access ramp at the north end. A 20 degree run: shallow enough that a sprinting player
        // keeps their footing, and well inside the controller's 50 degree slope limit.
        //
        // The pitch is negative because Quaternion.Euler(x,0,0) tips the +Z end *down*, and this
        // ramp's high end is its north (+Z) one.
        const float pitch = 20f;
        float rise = -CityDesign.CutFloorY;
        float run = rise / Mathf.Tan(pitch * Mathf.Deg2Rad);
        float rampLength = Mathf.Sqrt(rise * rise + run * run);
        plan.Ramps.Add(new RampPlan("Cut_Ramp_North", group,
            new Vector3(cut.CentreX, CityDesign.CutFloorY * 0.5f, cut.MaxZ - run * 0.5f),
            new Vector3(CityDesign.CutWidth - 1f, 0.6f, rampLength),
            -pitch));
    }

    // ------------------------------------------------------------------ superblocks

    private static void PlanBlock(CityPlanResult plan, DistrictCell cell)
    {
        Rng rng = new Rng(HashSeed(cell.Name));
        CityRect bounds = cell.Bounds;

        float[] xStart, xSize, zStart, zSize;

        if (cell.Group == DistrictGroup.CityCenter)
        {
            // The plaza is a fixed 40 m, so the centre lot is pinned and only the flanks vary.
            SplitWithFixedCentre(bounds.MinX, bounds.MaxX, cell.InternalStreetWidth,
                CityDesign.PlazaSize, out xStart, out xSize);
            SplitWithFixedCentre(bounds.MinZ, bounds.MaxZ, cell.InternalStreetWidth,
                CityDesign.PlazaSize, out zStart, out zSize);
        }
        else
        {
            Split(bounds.MinX, bounds.MaxX, cell.LotsX, cell.InternalStreetWidth, 0.22f, ref rng,
                out xStart, out xSize);
            Split(bounds.MinZ, bounds.MaxZ, cell.LotsZ, cell.InternalStreetWidth, 0.22f, ref rng,
                out zStart, out zSize);
        }

        CityRect cut = CutBounds();
        bool avoidCut = cell.Name == "OldQuarter";
        float lotFill = cell.Group == DistrictGroup.Corporate ? CorporateLotFill : 1f;

        bool perRow = cell.ClusterMode == RoofClusterMode.PerRow;

        for (int row = 0; row < zStart.Length; row++)
        {
            int rowStoreys = rng.RangeInt(cell.MinStoreys, cell.MaxStoreys);

            // A cluster is a *contiguous* run of roofs, not simply a lot row. The plaza and the
            // Cut each punch a lot out of their row, and the two halves left behind are 35-55 m
            // apart - nothing a player can hop. Treating them as one cluster would have the
            // validator measure a jump nobody can make and report the block as broken.
            int segment = 0;
            bool previousLotBuilt = false;

            for (int col = 0; col < xStart.Length; col++)
            {
                CityRect lot = new CityRect(xStart[col], xStart[col] + xSize[col],
                    zStart[row], zStart[row] + zSize[row]);

                CityRect footprint = lotFill >= 1f
                    ? lot
                    : CityRect.FromCentre(lot.CentreX, lot.CentreZ,
                        lot.Width * lotFill, lot.Depth * lotFill);

                bool isPlazaLot = cell.Group == DistrictGroup.CityCenter && col == 1 && row == 1;

                if (isPlazaLot || (avoidCut && footprint.Overlaps(cut)))
                {
                    if (previousLotBuilt)
                    {
                        segment++;
                    }

                    previousLotBuilt = false;
                    continue;
                }

                int clusterId = perRow ? ClusterId(cell, row, segment) : -1;

                int storeys = perRow ? rowStoreys : rng.RangeInt(cell.MinStoreys, cell.MaxStoreys);
                float baseY = storeys * CityDesign.StoreyHeight;

                // The storey range is quantised to sit inside the district's metre band, so the
                // sub-storey variation may not be allowed to push a roof back out of it. A cluster
                // already at its top storey simply reads flat.
                float offset = perRow ? rng.Pick(RoofOffsets) : 0f;
                if (baseY + offset > cell.MaxHeight)
                {
                    offset = 0f;
                }

                plan.Buildings.Add(new BuildingPlan(
                    $"{cell.Name}_B{col}{row}", cell.Name, cell.Group, footprint,
                    baseY + offset, storeys, offset, clusterId, col, row));

                previousLotBuilt = true;
            }
        }
    }

    /// <summary>
    /// Stable cluster id. Encodes the cell's index in the design table, the lot row, and which
    /// contiguous segment of that row the building belongs to.
    /// </summary>
    private static int ClusterId(DistrictCell cell, int row, int segment)
    {
        for (int i = 0; i < CityDesign.Cells.Length; i++)
        {
            if (CityDesign.Cells[i].Name == cell.Name)
            {
                return (i * 100) + (row * 10) + segment;
            }
        }

        return -1;
    }

    // ------------------------------------------------------------------ the landmark

    private static void PlanTower(CityPlanResult plan, DistrictCell cell)
    {
        CityRect bounds = cell.Bounds;
        const string group = "SKYBOUND_TOWER";

        CityRect podium = CityRect.FromCentre(bounds.CentreX, bounds.CentreZ,
            CityDesign.TowerPodiumSize, CityDesign.TowerPodiumSize);
        plan.Blocks.Add(new BlockPlan("Tower_Podium", group, CityPieceKind.Landmark, podium,
            0f, CityDesign.TowerPodiumY));

        CityRect shaft = CityRect.FromCentre(bounds.CentreX, bounds.CentreZ,
            CityDesign.TowerShaftSize, CityDesign.TowerShaftSize);
        plan.Blocks.Add(new BlockPlan("Tower_Shaft", group, CityPieceKind.Landmark, shaft,
            CityDesign.TowerPodiumY, CityDesign.TowerShaftTopY));

        CityRect mast = CityRect.FromCentre(bounds.CentreX, bounds.CentreZ,
            CityDesign.TowerMastSize, CityDesign.TowerMastSize);
        plan.Blocks.Add(new BlockPlan("Tower_Mast", group, CityPieceKind.Landmark, mast,
            CityDesign.TowerShaftTopY, CityDesign.TowerTopY));
    }

    // ------------------------------------------------------------------ lot subdivision

    /// <summary>
    /// Divides an axis into <paramref name="count"/> lots separated by streets of a fixed width.
    /// Lot sizes vary by <paramref name="variance"/> so a block does not read as graph paper,
    /// but the street widths never move - they are the thing the tier validator checks.
    /// </summary>
    private static void Split(float min, float max, int count, float street, float variance,
        ref Rng rng, out float[] starts, out float[] sizes)
    {
        starts = new float[count];
        sizes = new float[count];

        float buildable = (max - min) - (count - 1) * street;
        float[] weights = new float[count];
        float total = 0f;

        for (int i = 0; i < count; i++)
        {
            weights[i] = rng.Range(1f - variance, 1f + variance);
            total += weights[i];
        }

        float cursor = min;

        for (int i = 0; i < count; i++)
        {
            sizes[i] = buildable * weights[i] / total;
            starts[i] = cursor;
            cursor += sizes[i] + street;
        }
    }

    /// <summary>
    /// Three lots where the middle one is pinned to an exact size - the City Center plaza. The
    /// flanks share what is left, with a little variation.
    /// </summary>
    private static void SplitWithFixedCentre(float min, float max, float street, float centreSize,
        out float[] starts, out float[] sizes)
    {
        starts = new float[3];
        sizes = new float[3];

        // The flanks are deliberately equal rather than jittered: it is what makes the plaza land
        // exactly on CityDesign.Plaza, which the spawn point, the street centrelines the route
        // harness walks, and the tests all treat as a constant.
        float flanks = (max - min) - 2f * street - centreSize;

        sizes[0] = flanks * 0.5f;
        sizes[1] = centreSize;
        sizes[2] = flanks - sizes[0];

        starts[0] = min;
        starts[1] = starts[0] + sizes[0] + street;
        starts[2] = starts[1] + sizes[1] + street;
    }
}
