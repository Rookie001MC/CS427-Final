using System.Collections.Generic;
using UnityEngine;

/// <summary>Which face of a footprint a structure is hung on.</summary>
public enum Facade
{
    West,
    East,
    South,
    North
}

/// <summary>What kind of thing carries the player across an avenue.</summary>
public enum LinkKind
{
    /// <summary>A flat deck at the lower of the two roofs. Walked, so GREEN.</summary>
    Skybridge,

    /// <summary>
    /// The tower crane's jib, above both roofs and only as wide as the BLUE landing minimum. It is
    /// climbed to from both sides, which is what makes the Industrial crossing cost something.
    /// </summary>
    Crane
}

/// <summary>Why an ascent exists. All four are the same stack of ledges; only the reading differs.</summary>
public enum AscentKind
{
    /// <summary>Street to roof, on a facade.</summary>
    FireEscape,

    /// <summary>Street to roof, on the Industrial Construction site. Wider decks.</summary>
    Scaffold,

    /// <summary>Roof to roof, where the step between two plateaus is past a mantle.</summary>
    Riser,

    /// <summary>Deck to roof, or roof to deck: the piece a link needs at its taller end.</summary>
    LinkStair,

    /// <summary>The ramped spiral up the tower shaft. Walked, not mantled.</summary>
    TowerSpiral
}

/// <summary>What a <see cref="SurfaceRef"/> points at.</summary>
public enum SurfaceKind
{
    /// <summary>The pavement. Only ever the bottom of an ascent.</summary>
    Street,

    /// <summary>One lot of one district cell, by its lot indices.</summary>
    Lot,

    /// <summary>A named landmark surface: the podium, a wing, the shaft roof.</summary>
    Platform
}

/// <summary>
/// A reference to a walkable surface, resolved against the plan rather than typed as coordinates.
///
/// Lot indices are structural - they come from <see cref="DistrictCell.LotsX"/> and
/// <see cref="DistrictCell.LotsZ"/> - so they survive a change to the seed, which the roof heights
/// they select do not. That is the whole reason the traversal layer is authored this way instead of
/// as a list of positions.
/// </summary>
public readonly struct SurfaceRef
{
    public readonly SurfaceKind Kind;

    /// <summary>Cell name for a lot, platform name for a platform.</summary>
    public readonly string Name;

    public readonly int Column;
    public readonly int Row;

    private SurfaceRef(SurfaceKind kind, string name, int column, int row)
    {
        Kind = kind;
        Name = name;
        Column = column;
        Row = row;
    }

    public static SurfaceRef Street => new SurfaceRef(SurfaceKind.Street, "STREET", 0, 0);

    public static SurfaceRef Lot(string cellName, int column, int row)
        => new SurfaceRef(SurfaceKind.Lot, cellName, column, row);

    public static SurfaceRef Platform(string platformName)
        => new SurfaceRef(SurfaceKind.Platform, platformName, 0, 0);

    public override string ToString()
        => Kind == SurfaceKind.Lot ? $"{Name}[{Column},{Row}]" : Name;
}

/// <summary>A resolved surface: where it is, how high it is, and what to call it in the graph.</summary>
public readonly struct TraversalSurface
{
    public readonly string Node;
    public readonly CityRect Footprint;
    public readonly float SurfaceY;
    public readonly bool IsStreet;

    public TraversalSurface(string node, CityRect footprint, float surfaceY, bool isStreet = false)
    {
        Node = node;
        Footprint = footprint;
        SurfaceY = surfaceY;
        IsStreet = isStreet;
    }

    public Vector3 Centre => new Vector3(Footprint.CentreX, SurfaceY, Footprint.CentreZ);
}

/// <summary>One authored crossing between two roofs.</summary>
public readonly struct DistrictLink
{
    public readonly string Name;
    public readonly SurfaceRef From;
    public readonly SurfaceRef To;
    public readonly LinkKind Kind;

    /// <summary>
    /// True when the two ends belong to different <see cref="DistrictGroup"/>s. Six of these is
    /// the Phase 6C exit criterion; the rest knit a district's own roofs together.
    /// </summary>
    public readonly bool InterDistrict;

    /// <summary>What the author claims the crossing grades at. The validator measures it.</summary>
    public readonly RouteTier Tier;

    public DistrictLink(string name, SurfaceRef from, SurfaceRef to, LinkKind kind,
        bool interDistrict, RouteTier tier)
    {
        Name = name;
        From = from;
        To = to;
        Kind = kind;
        InterDistrict = interDistrict;
        Tier = tier;
    }
}

/// <summary>One authored stack of ledges.</summary>
public readonly struct AscentSite
{
    public readonly string Name;
    public readonly AscentKind Kind;

    /// <summary>The surface the stack reaches.</summary>
    public readonly SurfaceRef Top;

    /// <summary>Which facade of <see cref="Top"/> it is hung on.</summary>
    public readonly Facade Side;

    /// <summary>Offset along that facade from the footprint's centre.</summary>
    public readonly float CrossOffset;

    /// <summary>Where it starts. <see cref="SurfaceRef.Street"/> makes it a way in from the city.</summary>
    public readonly SurfaceRef Bottom;

    public AscentSite(string name, AscentKind kind, SurfaceRef top, Facade side, float crossOffset,
        SurfaceRef bottom)
    {
        Name = name;
        Kind = kind;
        Top = top;
        Side = side;
        CrossOffset = crossOffset;
        Bottom = bottom;
    }
}

/// <summary>
/// A named journey across the rooftops: which way in, and which relay it ends on.
///
/// Only the ends are authored. The path between them is whatever <see cref="RoofGraph"/> finds,
/// which is the point - a hand-drawn rooftop path would be a claim about geometry that nothing
/// checks, and would rot the first time a roof height moved. What *is* a claim is the tier: no
/// route across the roofs may measure harder than what is declared here.
/// </summary>
public readonly struct RoofRouteSite
{
    public readonly string Name;

    /// <summary>Name of the street ascent this starts on.</summary>
    public readonly string Entry;

    /// <summary>A relay name, or a surface node for a journey that does not end on a relay.</summary>
    public readonly string Target;

    public readonly RouteTier Tier;

    public RoofRouteSite(string name, string entry, string target, RouteTier tier)
    {
        Name = name;
        Entry = entry;
        Target = target;
        Tier = tier;
    }
}

/// <summary>Where a Phase 6D objective relay will stand.</summary>
public readonly struct RelaySite
{
    public readonly string Name;
    public readonly SurfaceRef Host;

    public RelaySite(string name, SurfaceRef host)
    {
        Name = name;
        Host = host;
    }
}

/// <summary>One measured step of an ascent, ready to be graded.</summary>
public readonly struct AscentStep
{
    public readonly float Gap;
    public readonly float Rise;
    public readonly float LandingDepth;

    public AscentStep(float gap, float rise, float landingDepth)
    {
        Gap = gap;
        Rise = rise;
        LandingDepth = landingDepth;
    }

    public RouteTier Tier => RouteTiers.Classify(Gap, Rise, LandingDepth);
}

/// <summary>A resolved ascent: the ledges, and every step between them.</summary>
public sealed class AscentPlan
{
    public string Name;
    public AscentKind Kind;
    public string BottomNode;
    public string TopNode;
    public bool FromStreet;
    public float BaseY;
    public float TopY;
    public float StepRise;

    /// <summary>Number of moves from the bottom surface to the top one.</summary>
    public int StepCount;

    /// <summary>The intermediate ledges. One fewer than <see cref="StepCount"/>.</summary>
    public readonly List<CityRect> Landings = new List<CityRect>();

    public readonly List<float> LandingY = new List<float>();

    public CityRect BottomFootprint;
    public CityRect TopFootprint;

    /// <summary>Set for <see cref="AscentKind.TowerSpiral"/>, which is walked instead of mantled.</summary>
    public bool IsRamped;

    public float PitchDegrees;

    /// <summary>
    /// The spiral's last corner landing sits diagonally off the shaft's corner and so meets its
    /// roof at a single point, which is not a step. This is the slab that turns that corner into a
    /// shared edge. Only a <see cref="AscentKind.TowerSpiral"/> has one.
    /// </summary>
    public CityRect SummitFootprint;

    /// <summary>The last corner landing of a ramped ascent, which the summit slab adjoins.</summary>
    public CityRect FinalLanding;

    /// <summary>
    /// The corner a ramped ascent is entered at, on the surface it starts from. Phase 6D's tower
    /// gate stands here: it is the one place the spiral can be stepped onto, because every later
    /// run is a storey or more above whatever is under it.
    /// </summary>
    public CityRect FootLanding;

    /// <summary>Which axis the first run of a ramped ascent travels along.</summary>
    public bool FootRunAlongZ;

    /// <summary>Footprint of the first run's deck, which the gate has to shut off.</summary>
    public CityRect FootRun;

    public float Rise => TopY - BaseY;

    /// <summary>
    /// Every move the player makes climbing this, as a gap / rise / landing triple. A ramped ascent
    /// has no steps - it is graded on its pitch instead.
    /// </summary>
    public IEnumerable<AscentStep> Steps()
    {
        if (IsRamped)
        {
            yield break;
        }

        for (int i = 0; i < StepCount; i++)
        {
            CityRect from = i == 0 ? BottomFootprint : Landings[i - 1];
            bool last = i == StepCount - 1;
            CityRect to = last ? TopFootprint : Landings[i];
            float toY = last ? TopY : LandingY[i];
            float fromY = i == 0 ? BaseY : LandingY[i - 1];

            // The bottom of a street ascent is the pavement under the stack, so the player is
            // already standing beneath the first ledge and the gap is zero by construction.
            float gap = FromStreet && i == 0 ? 0f : from.GapTo(to);
            float landing = Mathf.Min(to.Width, to.Depth);

            yield return new AscentStep(gap, toY - fromY, landing);
        }
    }
}

/// <summary>A resolved crossing: the deck, the two ends, and the stair the taller end needs.</summary>
public sealed class LinkPlan
{
    public string Name;
    public LinkKind Kind;
    public bool InterDistrict;
    public RouteTier Tier;

    public string DeckNode;
    public CityRect Deck;
    public float DeckY;

    public string FromNode;
    public string ToNode;
    public CityRect FromFootprint;
    public CityRect ToFootprint;
    public float FromY;
    public float ToY;

    /// <summary>Clear distance between the two facades the deck spans.</summary>
    public float Span;

    /// <summary>How much of the two footprints face each other across the deck.</summary>
    public float Bearing;

    /// <summary>The stairs this link needs: one for a skybridge, two for the crane.</summary>
    public readonly List<AscentPlan> Stairs = new List<AscentPlan>();

    public float DeckWidth => Mathf.Min(Deck.Width, Deck.Depth);

    /// <summary>Nodes the deck is flush with, i.e. steppable onto without climbing anything.</summary>
    public IEnumerable<string> FlushEnds()
    {
        if (Mathf.Abs(FromY - DeckY) < 0.01f)
        {
            yield return FromNode;
        }

        if (Mathf.Abs(ToY - DeckY) < 0.01f)
        {
            yield return ToNode;
        }
    }
}

/// <summary>A resolved relay position.</summary>
public sealed class RelayPlan
{
    public string Name;
    public string Node;
    public string CellName;
    public DistrictGroup Group;
    public CityRect Footprint;
    public float SurfaceY;

    public Vector3 Position => new Vector3(Footprint.CentreX, SurfaceY, Footprint.CentreZ);
}

/// <summary>The traversal layer, as data, beside the massing it is hung on.</summary>
public sealed class CityTraversalResult
{
    public readonly List<LinkPlan> Links = new List<LinkPlan>();
    public readonly List<AscentPlan> Ascents = new List<AscentPlan>();
    public readonly List<RelayPlan> Relays = new List<RelayPlan>();

    /// <summary>Every standable surface the roof graph has a node for.</summary>
    public readonly Dictionary<string, TraversalSurface> Surfaces =
        new Dictionary<string, TraversalSurface>();

    /// <summary>
    /// Anything that could not be resolved. Collected rather than thrown so the validator can
    /// report all of them at once instead of dying on the first.
    /// </summary>
    public readonly List<string> Problems = new List<string>();

    public int InterDistrictLinkCount
    {
        get
        {
            int count = 0;

            foreach (LinkPlan link in Links)
            {
                if (link.InterDistrict)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>The ascents that start on the pavement - the ways into the rooftop network.</summary>
    public IEnumerable<AscentPlan> StreetAscents()
    {
        foreach (AscentPlan ascent in Ascents)
        {
            if (ascent.FromStreet)
            {
                yield return ascent;
            }
        }
    }

    public LinkPlan Link(string name)
    {
        foreach (LinkPlan link in Links)
        {
            if (link.Name == name)
            {
                return link;
            }
        }

        return null;
    }

    public RelayPlan Relay(string name)
    {
        foreach (RelayPlan relay in Relays)
        {
            if (relay.Name == name)
            {
                return relay;
            }
        }

        return null;
    }
}

/// <summary>
/// Phase 6C: the layer that makes the Phase 6B massing climbable.
///
/// Phase 6B proved the player can walk the whole 600 x 600 m at street level. Nothing in it can be
/// climbed: a storey is deliberately taller than the mantle ceiling, and an avenue is deliberately
/// wider than a drop-assisted sprint jump, so without designed geometry the roofs are scenery. This
/// file is that geometry, and - like everything else in the city - it is data first and boxes
/// second:
///
///   <b>Ascents</b>    a stack of ledges, one <see cref="CityDesign.AscentStepRise"/> apart, hung on
///                     a facade. Read as a fire escape from the street, a scaffold on the
///                     construction site, a riser between two roof plateaus, or the stair a link
///                     needs at its taller end. Every step is one mantle, so every step is ORANGE.
///   <b>Links</b>      a deck across an avenue, at the lower of the two roofs so one end is always
///                     flush. Nine of them, six between different districts.
///   <b>The crane</b>  the same idea, above both roofs and only as wide as a BLUE landing.
///   <b>The spiral</b> a ramped ascent of the tower shaft. 79.8 m of mantles would be 45 steps;
///                     this is eight walked runs instead.
///
/// The endpoints are authored as lot indices, not coordinates, so the network survives a change to
/// the seed. What it does *not* survive silently is a change that makes a step unclimbable - that
/// is what `RouteTierValidator` measures, and what the Phase 6C exit criterion is.
/// </summary>
public static class CityTraversal
{
    // ------------------------------------------------------------------ node names

    public const string PodiumNode = "Tower_Podium";
    public const string WingNorthNode = "Tower_WingNorth";
    public const string WingWestNode = "Tower_WingWest";
    public const string ShaftRoofNode = "Tower_ShaftRoof";

    public const string LinkGroup = "SKYBRIDGES";
    public const string AscentGroup = "ASCENTS";
    public const string CraneGroup = "CRANE";
    public const string TowerAscentGroup = "TOWER_ASCENT";
    public const string TowerGroup = "SKYBOUND_TOWER";

    // ------------------------------------------------------------------ the authored network

    /// <summary>
    /// The six inter-district links, and the crossings each district needs inside itself.
    ///
    /// The six are the Phase 6C exit criterion, and they are these six because they are the set
    /// that touches all six district groups: the City Center is the hub with four spokes, and the
    /// Old Quarter - the one group the hub does not border - is tied in twice, to the Residential
    /// block and to the tower. Nothing else in the table crosses a district boundary.
    ///
    /// The intra-district entries are not decoration. Corporate lots are
    /// <see cref="RoofClusterMode.Isolated"/> and sit 39 m apart, the Cut splits the Old Quarter's
    /// roofs into two halves 36 m apart, and an avenue runs through the middle of both the
    /// Residential and the Industrial districts. Without them a district would be several
    /// disconnected roofs rather than one place.
    /// </summary>
    public static readonly DistrictLink[] Links =
    {
        // --- the six inter-district links ---------------------------------------------
        new DistrictLink("Center-Residential Span",
            SurfaceRef.Lot("CityCenter", 0, 1), SurfaceRef.Lot("ResidentialWest", 4, 2),
            LinkKind.Skybridge, true, RouteTier.Green),
        new DistrictLink("Center-Industrial Span",
            SurfaceRef.Lot("CityCenter", 1, 2), SurfaceRef.Lot("IndustrialYards", 1, 0),
            LinkKind.Skybridge, true, RouteTier.Green),
        new DistrictLink("Center-Corporate Span",
            SurfaceRef.Lot("CityCenter", 2, 2), SurfaceRef.Lot("CorporateCore", 0, 1),
            LinkKind.Skybridge, true, RouteTier.Green),
        new DistrictLink("Center-Tower Span",
            SurfaceRef.Lot("CityCenter", 1, 0), SurfaceRef.Platform(WingNorthNode),
            LinkKind.Skybridge, true, RouteTier.Green),
        new DistrictLink("Quarter-Residential Span",
            SurfaceRef.Lot("OldQuarter", 1, 5), SurfaceRef.Lot("ResidentialWest", 1, 0),
            LinkKind.Skybridge, true, RouteTier.Green),
        new DistrictLink("Quarter-Tower Span",
            SurfaceRef.Lot("OldQuarter", 5, 3), SurfaceRef.Platform(WingWestNode),
            LinkKind.Skybridge, true, RouteTier.Green),

        // --- inside a district ---------------------------------------------------------
        new DistrictLink("Residential Ladder",
            SurfaceRef.Lot("ResidentialWest", 2, 4), SurfaceRef.Lot("ResidentialNorth", 2, 0),
            LinkKind.Skybridge, false, RouteTier.Green),
        new DistrictLink("Yard Crane",
            SurfaceRef.Lot("IndustrialYards", 2, 0), SurfaceRef.Lot("IndustrialConstruction", 0, 0),
            LinkKind.Crane, false, RouteTier.Blue),
        new DistrictLink("Cut Span",
            SurfaceRef.Lot("OldQuarter", 2, 3), SurfaceRef.Lot("OldQuarter", 4, 3),
            LinkKind.Skybridge, false, RouteTier.Green),
        new DistrictLink("Corporate Spine",
            SurfaceRef.Lot("CorporateCore", 0, 0), SurfaceRef.Lot("CorporateSouth", 0, 1),
            LinkKind.Skybridge, false, RouteTier.Green),
        new DistrictLink("Corporate Core North",
            SurfaceRef.Lot("CorporateCore", 0, 0), SurfaceRef.Lot("CorporateCore", 0, 1),
            LinkKind.Skybridge, false, RouteTier.Green),
        new DistrictLink("Corporate Core East",
            SurfaceRef.Lot("CorporateCore", 0, 0), SurfaceRef.Lot("CorporateCore", 1, 0),
            LinkKind.Skybridge, false, RouteTier.Green),
        new DistrictLink("Corporate Core Cross",
            SurfaceRef.Lot("CorporateCore", 0, 1), SurfaceRef.Lot("CorporateCore", 1, 1),
            LinkKind.Skybridge, false, RouteTier.Green),
        new DistrictLink("Corporate South West",
            SurfaceRef.Lot("CorporateSouth", 0, 1), SurfaceRef.Lot("CorporateSouth", 0, 0),
            LinkKind.Skybridge, false, RouteTier.Green),
        new DistrictLink("Corporate South East",
            SurfaceRef.Lot("CorporateSouth", 0, 0), SurfaceRef.Lot("CorporateSouth", 1, 0),
            LinkKind.Skybridge, false, RouteTier.Green),
        new DistrictLink("Corporate South Cross",
            SurfaceRef.Lot("CorporateSouth", 1, 0), SurfaceRef.Lot("CorporateSouth", 1, 1),
            LinkKind.Skybridge, false, RouteTier.Green)
    };

    /// <summary>
    /// Fire escapes and scaffolds are the ways in from the street; risers are the ways between one
    /// roof plateau and the next.
    ///
    /// Every street ascent is on a facade that faces an avenue, the perimeter or an open forecourt.
    /// That is not aesthetics: the lowest ledge is 1.8 m up, which is below the player's standing
    /// height, so a stack hung over one of the Old Quarter's 3.5 m streets would take that street
    /// out of the walkable network Phase 6B proved.
    /// </summary>
    public static readonly AscentSite[] Ascents =
    {
        // --- ways in from the street ---------------------------------------------------
        new AscentSite("Center West Escape", AscentKind.FireEscape,
            SurfaceRef.Lot("CityCenter", 0, 2), Facade.West, 0f, SurfaceRef.Street),
        new AscentSite("Center East Escape", AscentKind.FireEscape,
            SurfaceRef.Lot("CityCenter", 2, 2), Facade.East, 0f, SurfaceRef.Street),
        new AscentSite("Center South Escape", AscentKind.FireEscape,
            SurfaceRef.Lot("CityCenter", 1, 0), Facade.South, 0f, SurfaceRef.Street),
        new AscentSite("Residential West Escape", AscentKind.FireEscape,
            SurfaceRef.Lot("ResidentialWest", 0, 2), Facade.West, 0f, SurfaceRef.Street),
        new AscentSite("Residential South Escape", AscentKind.FireEscape,
            SurfaceRef.Lot("ResidentialWest", 2, 0), Facade.South, 0f, SurfaceRef.Street),
        new AscentSite("Residential North Escape", AscentKind.FireEscape,
            SurfaceRef.Lot("ResidentialNorth", 2, 4), Facade.North, 0f, SurfaceRef.Street),
        new AscentSite("Yards West Escape", AscentKind.FireEscape,
            SurfaceRef.Lot("IndustrialYards", 0, 0), Facade.West, 0f, SurfaceRef.Street),
        new AscentSite("Yards North Escape", AscentKind.FireEscape,
            SurfaceRef.Lot("IndustrialYards", 1, 1), Facade.North, 0f, SurfaceRef.Street),
        new AscentSite("Construction Scaffold", AscentKind.Scaffold,
            SurfaceRef.Lot("IndustrialConstruction", 0, 0), Facade.West, -30f, SurfaceRef.Street),
        new AscentSite("Construction East Escape", AscentKind.FireEscape,
            SurfaceRef.Lot("IndustrialConstruction", 2, 1), Facade.East, 0f, SurfaceRef.Street),
        new AscentSite("Quarter West Escape", AscentKind.FireEscape,
            SurfaceRef.Lot("OldQuarter", 0, 3), Facade.West, 0f, SurfaceRef.Street),
        new AscentSite("Quarter East Escape", AscentKind.FireEscape,
            SurfaceRef.Lot("OldQuarter", 5, 0), Facade.East, 0f, SurfaceRef.Street),
        new AscentSite("Podium East Stair", AscentKind.FireEscape,
            SurfaceRef.Platform(PodiumNode), Facade.East, 0f, SurfaceRef.Street),

        // --- between roof plateaus -----------------------------------------------------
        new AscentSite("Center West Riser", AscentKind.Riser,
            SurfaceRef.Lot("CityCenter", 0, 1), Facade.South, 0f,
            SurfaceRef.Lot("CityCenter", 0, 0)),
        new AscentSite("Center West Upper Riser", AscentKind.Riser,
            SurfaceRef.Lot("CityCenter", 0, 2), Facade.South, 0f,
            SurfaceRef.Lot("CityCenter", 0, 1)),
        new AscentSite("Center East Riser", AscentKind.Riser,
            SurfaceRef.Lot("CityCenter", 2, 1), Facade.South, 0f,
            SurfaceRef.Lot("CityCenter", 2, 0)),
        new AscentSite("Center East Upper Riser", AscentKind.Riser,
            SurfaceRef.Lot("CityCenter", 2, 2), Facade.South, 0f,
            SurfaceRef.Lot("CityCenter", 2, 1)),
        new AscentSite("Residential Riser", AscentKind.Riser,
            SurfaceRef.Lot("ResidentialWest", 2, 1), Facade.South, 0f,
            SurfaceRef.Lot("ResidentialWest", 2, 0)),
        new AscentSite("Residential Upper Riser", AscentKind.Riser,
            SurfaceRef.Lot("ResidentialWest", 2, 2), Facade.South, 0f,
            SurfaceRef.Lot("ResidentialWest", 2, 1)),
        new AscentSite("Residential North Lower Riser", AscentKind.Riser,
            SurfaceRef.Lot("ResidentialNorth", 2, 0), Facade.North, 0f,
            SurfaceRef.Lot("ResidentialNorth", 2, 1)),
        new AscentSite("Residential North Riser", AscentKind.Riser,
            SurfaceRef.Lot("ResidentialNorth", 2, 3), Facade.South, 0f,
            SurfaceRef.Lot("ResidentialNorth", 2, 2)),
        new AscentSite("Residential North Upper Riser", AscentKind.Riser,
            SurfaceRef.Lot("ResidentialNorth", 2, 4), Facade.South, 0f,
            SurfaceRef.Lot("ResidentialNorth", 2, 3)),
        new AscentSite("Yards Riser", AscentKind.Riser,
            SurfaceRef.Lot("IndustrialYards", 1, 1), Facade.South, 0f,
            SurfaceRef.Lot("IndustrialYards", 1, 0)),
        // The Old Quarter's lot rows run north with the index, so its two risers are hung on the
        // north facade of the row below - the opposite side to every other riser in the table.
        new AscentSite("Quarter West Riser", AscentKind.Riser,
            SurfaceRef.Lot("OldQuarter", 2, 4), Facade.North, 0f,
            SurfaceRef.Lot("OldQuarter", 2, 5)),
        new AscentSite("Quarter East Riser", AscentKind.Riser,
            SurfaceRef.Lot("OldQuarter", 4, 4), Facade.North, 0f,
            SurfaceRef.Lot("OldQuarter", 4, 5))
    };

    /// <summary>
    /// Where Phase 6D's objective relays will stand: one per district, none on the landmark - the
    /// tower is what the relays unlock, so it cannot also be one of them.
    ///
    /// Hosts are named as lots so the relay lands on a specific roof rather than wherever the seed
    /// happens to put the tallest one.
    /// </summary>
    public static readonly RelaySite[] Relays =
    {
        new RelaySite("Relay_CityCenter", SurfaceRef.Lot("CityCenter", 1, 2)),
        new RelaySite("Relay_Residential", SurfaceRef.Lot("ResidentialWest", 2, 2)),
        new RelaySite("Relay_Industrial", SurfaceRef.Lot("IndustrialConstruction", 1, 0)),
        new RelaySite("Relay_Corporate", SurfaceRef.Lot("CorporateCore", 0, 1)),
        new RelaySite("Relay_OldQuarter", SurfaceRef.Lot("OldQuarter", 2, 3))
    };

    /// <summary>
    /// Three ways onto each relay, and the climb to the summit.
    ///
    /// Three is the Phase 6C exit criterion, and these are the three that read as genuinely
    /// different approaches rather than three doors into the same stairwell. The Corporate relay
    /// has no street ascent of its own - its towers start at 46.8 m and a fire escape up one would
    /// be twenty-eight mantles - so all three of its routes arrive over a bridge, which is exactly
    /// the thing the Corporate district is supposed to feel like.
    ///
    /// Every one of them is declared ORANGE, because every ascent in the city is a stack of
    /// mantles and a mantle is ORANGE. A rooftop route that measures RED is therefore a design
    /// error, not a hard route, and the validator says so.
    /// </summary>
    public static readonly RoofRouteSite[] RoofRoutes =
    {
        new RoofRouteSite("City Center Relay via West Escape", "Center West Escape",
            "Relay_CityCenter", RouteTier.Orange),
        new RoofRouteSite("City Center Relay via East Escape", "Center East Escape",
            "Relay_CityCenter", RouteTier.Orange),
        new RoofRouteSite("City Center Relay via South Escape", "Center South Escape",
            "Relay_CityCenter", RouteTier.Orange),

        new RoofRouteSite("Residential Relay via West Escape", "Residential West Escape",
            "Relay_Residential", RouteTier.Orange),
        new RoofRouteSite("Residential Relay via South Escape", "Residential South Escape",
            "Relay_Residential", RouteTier.Orange),
        new RoofRouteSite("Residential Relay via the Ladder", "Residential North Escape",
            "Relay_Residential", RouteTier.Orange),

        new RoofRouteSite("Industrial Relay via the Scaffold", "Construction Scaffold",
            "Relay_Industrial", RouteTier.Orange),
        new RoofRouteSite("Industrial Relay via East Escape", "Construction East Escape",
            "Relay_Industrial", RouteTier.Orange),
        new RoofRouteSite("Industrial Relay via the Crane", "Yards West Escape",
            "Relay_Industrial", RouteTier.Orange),

        new RoofRouteSite("Corporate Relay via Center East", "Center East Escape",
            "Relay_Corporate", RouteTier.Orange),
        new RoofRouteSite("Corporate Relay via Center South", "Center South Escape",
            "Relay_Corporate", RouteTier.Orange),
        new RoofRouteSite("Corporate Relay via Residential", "Residential West Escape",
            "Relay_Corporate", RouteTier.Orange),

        new RoofRouteSite("Old Quarter Relay via West Escape", "Quarter West Escape",
            "Relay_OldQuarter", RouteTier.Orange),
        new RoofRouteSite("Old Quarter Relay via East Escape", "Quarter East Escape",
            "Relay_OldQuarter", RouteTier.Orange),
        new RoofRouteSite("Old Quarter Relay via Residential", "Residential South Escape",
            "Relay_OldQuarter", RouteTier.Orange),

        // Not a relay: the summit is what the relays unlock in Phase 6D.
        new RoofRouteSite("Tower Ascent", "Podium East Stair", ShaftRoofNode, RouteTier.Orange)
    };

    // ------------------------------------------------------------------ landmark platforms

    /// <summary>Resolves a <see cref="RoofRouteSite.Target"/>, which may be a relay or a node.</summary>
    public static string TargetNode(CityTraversalResult traversal, string target)
    {
        RelayPlan relay = traversal.Relay(target);
        return relay != null ? relay.Node : target;
    }

    /// <summary>The street ascent a rooftop route starts on, or null if it is not authored.</summary>
    public static AscentPlan Ascent(CityTraversalResult traversal, string name)
    {
        foreach (AscentPlan ascent in traversal.Ascents)
        {
            if (ascent.Name == name)
            {
                return ascent;
            }
        }

        return null;
    }

    public static CityRect PodiumFootprint
    {
        get
        {
            CityRect cell = CityDesign.Cell("TowerPodium").Bounds;
            return CityRect.FromCentre(cell.CentreX, cell.CentreZ,
                CityDesign.TowerPodiumSize, CityDesign.TowerPodiumSize);
        }
    }

    public static CityRect ShaftFootprint
    {
        get
        {
            CityRect cell = CityDesign.Cell("TowerPodium").Bounds;
            return CityRect.FromCentre(cell.CentreX, cell.CentreZ,
                CityDesign.TowerShaftSize, CityDesign.TowerShaftSize);
        }
    }

    /// <summary>The arm that carries the podium roof north to the avenue.</summary>
    public static CityRect WingNorthFootprint
    {
        get
        {
            CityRect cell = CityDesign.Cell("TowerPodium").Bounds;
            CityRect podium = PodiumFootprint;
            return new CityRect(cell.CentreX - CityDesign.TowerWingWidth * 0.5f,
                cell.CentreX + CityDesign.TowerWingWidth * 0.5f, podium.MaxZ, cell.MaxZ);
        }
    }

    /// <summary>The arm that carries it west to the avenue the Old Quarter faces.</summary>
    public static CityRect WingWestFootprint
    {
        get
        {
            CityRect cell = CityDesign.Cell("TowerPodium").Bounds;
            CityRect podium = PodiumFootprint;
            return new CityRect(cell.MinX, podium.MinX,
                cell.CentreZ - CityDesign.TowerWingWidth * 0.5f,
                cell.CentreZ + CityDesign.TowerWingWidth * 0.5f);
        }
    }

    // ------------------------------------------------------------------ entry point

    /// <summary>
    /// Hangs the traversal layer on a finished massing plan, adding its geometry to that plan and
    /// returning the network as data. Pure, like everything else here.
    /// </summary>
    public static CityTraversalResult Plan(CityPlanResult plan)
    {
        CityTraversalResult result = new CityTraversalResult();

        PlanWings(plan, result);
        RegisterRoofs(plan, result);
        PlanLinks(plan, result);
        PlanAscents(plan, result);
        PlanTowerSpiral(plan, result);
        PlanRelays(plan, result);

        return result;
    }

    // ------------------------------------------------------------------ the podium wings

    private static void PlanWings(CityPlanResult plan, CityTraversalResult result)
    {
        plan.Blocks.Add(new BlockPlan(WingNorthNode, TowerGroup, CityPieceKind.Landmark,
            WingNorthFootprint, 0f, CityDesign.TowerPodiumY));
        plan.Blocks.Add(new BlockPlan(WingWestNode, TowerGroup, CityPieceKind.Landmark,
            WingWestFootprint, 0f, CityDesign.TowerPodiumY));
    }

    private static void RegisterRoofs(CityPlanResult plan, CityTraversalResult result)
    {
        foreach (BuildingPlan building in plan.Buildings)
        {
            result.Surfaces[building.Name] =
                new TraversalSurface(building.Name, building.Footprint, building.RoofY);
        }

        result.Surfaces[PodiumNode] =
            new TraversalSurface(PodiumNode, PodiumFootprint, CityDesign.TowerPodiumY);
        result.Surfaces[WingNorthNode] =
            new TraversalSurface(WingNorthNode, WingNorthFootprint, CityDesign.TowerPodiumY);
        result.Surfaces[WingWestNode] =
            new TraversalSurface(WingWestNode, WingWestFootprint, CityDesign.TowerPodiumY);
        result.Surfaces[ShaftRoofNode] =
            new TraversalSurface(ShaftRoofNode, ShaftFootprint, CityDesign.TowerShaftTopY);
    }

    // ------------------------------------------------------------------ resolution

    /// <summary>
    /// Turns an authored reference into the surface it names. Failures are recorded rather than
    /// thrown: a lot that the plaza or the Cut has removed is an authoring mistake worth reporting
    /// next to the fifteen links that were fine.
    /// </summary>
    public static bool TryResolve(CityPlanResult plan, CityTraversalResult result, SurfaceRef reference,
        out TraversalSurface surface)
    {
        surface = default;

        switch (reference.Kind)
        {
            case SurfaceKind.Street:
                return false;

            case SurfaceKind.Platform:
                if (result.Surfaces.TryGetValue(reference.Name, out surface))
                {
                    return true;
                }

                result.Problems.Add($"no platform named {reference.Name}");
                return false;

            default:
                foreach (BuildingPlan building in plan.InCell(reference.Name))
                {
                    if (building.LotColumn != reference.Column || building.LotRow != reference.Row)
                    {
                        continue;
                    }

                    surface = new TraversalSurface(building.Name, building.Footprint, building.RoofY);
                    return true;
                }

                result.Problems.Add($"no building at {reference}");
                return false;
        }
    }

    // ------------------------------------------------------------------ links

    private static void PlanLinks(CityPlanResult plan, CityTraversalResult result)
    {
        foreach (DistrictLink authored in Links)
        {
            if (!TryResolve(plan, result, authored.From, out TraversalSurface from) ||
                !TryResolve(plan, result, authored.To, out TraversalSurface to))
            {
                result.Problems.Add($"{authored.Name}: an end did not resolve");
                continue;
            }

            LinkPlan link = BuildLink(authored, from, to);

            if (link == null)
            {
                result.Problems.Add($"{authored.Name}: {from.Node} and {to.Node} do not face each " +
                                    "other across a street");
                continue;
            }

            result.Links.Add(link);
            result.Surfaces[link.DeckNode] =
                new TraversalSurface(link.DeckNode, link.Deck, link.DeckY);

            plan.Slabs.Add(new SlabPlan(link.DeckNode,
                link.Kind == LinkKind.Crane ? CraneGroup : LinkGroup,
                link.Kind == LinkKind.Crane ? CityPieceKind.Crane : CityPieceKind.Deck,
                link.Deck, link.DeckY, CityDesign.SkybridgeThickness));

            foreach (AscentPlan stair in link.Stairs)
            {
                result.Ascents.Add(stair);
                EmitAscent(plan, stair, link.Kind == LinkKind.Crane ? CraneGroup : LinkGroup);
            }

            if (link.Kind == LinkKind.Crane)
            {
                EmitCraneRig(plan, link, to);
            }
        }
    }

    private static LinkPlan BuildLink(DistrictLink authored, TraversalSurface from,
        TraversalSurface to)
    {
        float dx = Mathf.Max(0f, Mathf.Max(from.Footprint.MinX - to.Footprint.MaxX,
            to.Footprint.MinX - from.Footprint.MaxX));
        float dz = Mathf.Max(0f, Mathf.Max(from.Footprint.MinZ - to.Footprint.MaxZ,
            to.Footprint.MinZ - from.Footprint.MaxZ));

        bool alongX = dx >= dz;
        float span = alongX ? dx : dz;

        if (span <= 0.01f)
        {
            return null;
        }

        // The deck sits at the lower roof, so one end is always flush and only the other needs a
        // stair. The crane is the exception: its jib clears both roofs, and is climbed to twice.
        bool crane = authored.Kind == LinkKind.Crane;
        float deckY = crane
            ? Mathf.Max(from.SurfaceY, to.SurfaceY) + CityDesign.CraneJibRise
            : Mathf.Min(from.SurfaceY, to.SurfaceY);

        float deckWidth = crane ? CityDesign.CraneDeckWidth : CityDesign.SkybridgeWidth;
        float overhang = crane ? CityDesign.CraneJibOverhang : 0f;

        LinkPlan link = new LinkPlan
        {
            Name = authored.Name,
            Kind = authored.Kind,
            InterDistrict = authored.InterDistrict,
            Tier = authored.Tier,
            DeckNode = "Deck_" + authored.Name.Replace(' ', '_'),
            DeckY = deckY,
            FromNode = from.Node,
            ToNode = to.Node,
            FromFootprint = from.Footprint,
            ToFootprint = to.Footprint,
            FromY = from.SurfaceY,
            ToY = to.SurfaceY,
            Span = span
        };

        if (alongX)
        {
            bool fromIsWest = from.Footprint.MaxX <= to.Footprint.MinX;
            float westFace = fromIsWest ? from.Footprint.MaxX : to.Footprint.MaxX;
            float eastFace = fromIsWest ? to.Footprint.MinX : from.Footprint.MinX;

            float lo = Mathf.Max(from.Footprint.MinZ, to.Footprint.MinZ);
            float hi = Mathf.Min(from.Footprint.MaxZ, to.Footprint.MaxZ);
            link.Bearing = hi - lo;

            float cross = (lo + hi) * 0.5f;
            link.Deck = new CityRect(westFace - overhang, eastFace + overhang,
                cross - deckWidth * 0.5f, cross + deckWidth * 0.5f);

            AddStairs(link, from, to, cross,
                fromIsWest ? Facade.East : Facade.West,
                fromIsWest ? Facade.West : Facade.East, crane);
        }
        else
        {
            bool fromIsSouth = from.Footprint.MaxZ <= to.Footprint.MinZ;
            float southFace = fromIsSouth ? from.Footprint.MaxZ : to.Footprint.MaxZ;
            float northFace = fromIsSouth ? to.Footprint.MinZ : from.Footprint.MinZ;

            float lo = Mathf.Max(from.Footprint.MinX, to.Footprint.MinX);
            float hi = Mathf.Min(from.Footprint.MaxX, to.Footprint.MaxX);
            link.Bearing = hi - lo;

            float cross = (lo + hi) * 0.5f;
            link.Deck = new CityRect(cross - deckWidth * 0.5f, cross + deckWidth * 0.5f,
                southFace - overhang, northFace + overhang);

            AddStairs(link, from, to, cross,
                fromIsSouth ? Facade.North : Facade.South,
                fromIsSouth ? Facade.South : Facade.North, crane);
        }

        return link;
    }

    /// <summary>
    /// The stair a link needs. A skybridge needs one, at whichever end stands above the deck; the
    /// crane needs one at each end, because its jib stands above both.
    /// </summary>
    private static void AddStairs(LinkPlan link, TraversalSurface from, TraversalSurface to,
        float cross, Facade fromSide, Facade toSide, bool crane)
    {
        if (crane)
        {
            link.Stairs.Add(Stack($"{link.Name} (from stair)", AscentKind.LinkStair,
                from.Node, link.DeckNode, from.Footprint, from.SurfaceY, link.Deck, link.DeckY,
                from.Footprint, fromSide, cross, false));
            link.Stairs.Add(Stack($"{link.Name} (to stair)", AscentKind.LinkStair,
                to.Node, link.DeckNode, to.Footprint, to.SurfaceY, link.Deck, link.DeckY,
                to.Footprint, toSide, cross, false));
            return;
        }

        bool fromIsHigher = from.SurfaceY > to.SurfaceY + 0.01f;
        bool toIsHigher = to.SurfaceY > from.SurfaceY + 0.01f;

        if (!fromIsHigher && !toIsHigher)
        {
            return;
        }

        TraversalSurface high = fromIsHigher ? from : to;
        Facade side = fromIsHigher ? fromSide : toSide;

        link.Stairs.Add(Stack($"{link.Name} (stair)", AscentKind.LinkStair,
            link.DeckNode, high.Node, link.Deck, link.DeckY, high.Footprint, high.SurfaceY,
            high.Footprint, side, cross, false));
    }

    /// <summary>The mast, the cab and the counter-jib. None of it is on the traversal path.</summary>
    private static void EmitCraneRig(CityPlanResult plan, LinkPlan link, TraversalSurface mastEnd)
    {
        bool alongX = link.Deck.Width > link.Deck.Depth;
        float mastX, mastZ;

        if (alongX)
        {
            bool mastIsEast = mastEnd.Footprint.CentreX > link.Deck.CentreX;
            mastX = mastIsEast ? link.Deck.MaxX : link.Deck.MinX;
            mastZ = link.Deck.CentreZ;
        }
        else
        {
            bool mastIsNorth = mastEnd.Footprint.CentreZ > link.Deck.CentreZ;
            mastZ = mastIsNorth ? link.Deck.MaxZ : link.Deck.MinZ;
            mastX = link.Deck.CentreX;
        }

        CityRect mast = CityRect.FromCentre(mastX, mastZ,
            CityDesign.CraneMastSize, CityDesign.CraneMastSize);

        plan.Blocks.Add(new BlockPlan("Crane_Mast", CraneGroup, CityPieceKind.Crane, mast,
            mastEnd.SurfaceY, link.DeckY + CityDesign.CraneMastHeadroom));

        // The counter-jib balances the silhouette and is deliberately not walkable: it has no
        // collider, so it can never become an unintended shortcut off the back of the crane.
        float counterY = link.DeckY + CityDesign.CraneJibRise * 0.5f;
        CityRect counter;

        if (alongX)
        {
            bool mastIsEast = mastEnd.Footprint.CentreX > link.Deck.CentreX;
            counter = mastIsEast
                ? new CityRect(mast.MaxX, mast.MaxX + CityDesign.CraneCounterJibLength,
                    mastZ - CityDesign.CraneDeckWidth * 0.5f, mastZ + CityDesign.CraneDeckWidth * 0.5f)
                : new CityRect(mast.MinX - CityDesign.CraneCounterJibLength, mast.MinX,
                    mastZ - CityDesign.CraneDeckWidth * 0.5f, mastZ + CityDesign.CraneDeckWidth * 0.5f);
        }
        else
        {
            bool mastIsNorth = mastEnd.Footprint.CentreZ > link.Deck.CentreZ;
            counter = mastIsNorth
                ? new CityRect(mastX - CityDesign.CraneDeckWidth * 0.5f,
                    mastX + CityDesign.CraneDeckWidth * 0.5f,
                    mast.MaxZ, mast.MaxZ + CityDesign.CraneCounterJibLength)
                : new CityRect(mastX - CityDesign.CraneDeckWidth * 0.5f,
                    mastX + CityDesign.CraneDeckWidth * 0.5f,
                    mast.MinZ - CityDesign.CraneCounterJibLength, mast.MinZ);
        }

        plan.Blocks.Add(new BlockPlan("Crane_CounterJib", CraneGroup, CityPieceKind.Crane, counter,
            counterY, counterY + CityDesign.SkybridgeThickness, collidable: false));
    }

    // ------------------------------------------------------------------ ascents

    private static void PlanAscents(CityPlanResult plan, CityTraversalResult result)
    {
        foreach (AscentSite site in Ascents)
        {
            if (!TryResolve(plan, result, site.Top, out TraversalSurface top))
            {
                result.Problems.Add($"{site.Name}: top {site.Top} did not resolve");
                continue;
            }

            bool fromStreet = site.Bottom.Kind == SurfaceKind.Street;
            TraversalSurface bottom;

            if (fromStreet)
            {
                bottom = new TraversalSurface("STREET", top.Footprint, 0f, true);
            }
            else if (!TryResolve(plan, result, site.Bottom, out bottom))
            {
                result.Problems.Add($"{site.Name}: bottom {site.Bottom} did not resolve");
                continue;
            }

            float cross = CrossCentre(top.Footprint, site.Side) + site.CrossOffset;
            bool scaffold = site.Kind == AscentKind.Scaffold;

            AscentPlan ascent = Stack(site.Name, site.Kind, bottom.Node, top.Node,
                bottom.Footprint, bottom.SurfaceY, top.Footprint, top.SurfaceY,
                top.Footprint, site.Side, cross, scaffold);

            ascent.FromStreet = fromStreet;

            if (ascent.StepCount <= 0)
            {
                result.Problems.Add($"{site.Name}: nothing to climb ({bottom.SurfaceY:F2} m to " +
                                    $"{top.SurfaceY:F2} m)");
                continue;
            }

            result.Ascents.Add(ascent);
            EmitAscent(plan, ascent, AscentGroup);
        }
    }

    /// <summary>Centre of the facade a stack hangs on, along the axis it runs.</summary>
    private static float CrossCentre(CityRect host, Facade side)
        => side == Facade.West || side == Facade.East ? host.CentreZ : host.CentreX;

    /// <summary>
    /// Builds a stack of ledges from <paramref name="baseY"/> to <paramref name="topY"/>, hung on
    /// one facade of <paramref name="host"/>.
    ///
    /// The step count is the smallest that keeps every rise inside
    /// <see cref="CityDesign.AscentStepRise"/>, and the rise is then shared out evenly - so a
    /// 2.0 m climb is two 1.0 m steps rather than a 1.8 m one and a 0.2 m stub.
    /// </summary>
    private static AscentPlan Stack(string name, AscentKind kind, string bottomNode, string topNode,
        CityRect bottomFootprint, float baseY, CityRect topFootprint, float topY,
        CityRect host, Facade side, float cross, bool scaffold)
    {
        float rise = topY - baseY;
        int steps = Mathf.Max(0, Mathf.CeilToInt(rise / CityDesign.AscentStepRise - 0.0001f));

        AscentPlan ascent = new AscentPlan
        {
            Name = name,
            Kind = kind,
            BottomNode = bottomNode,
            TopNode = topNode,
            BaseY = baseY,
            TopY = topY,
            StepCount = steps,
            StepRise = steps > 0 ? rise / steps : 0f,
            BottomFootprint = bottomFootprint,
            TopFootprint = topFootprint
        };

        float width = scaffold ? CityDesign.ScaffoldLandingWidth : CityDesign.AscentLandingWidth;
        float depth = scaffold ? CityDesign.ScaffoldLandingDepth : CityDesign.AscentLandingDepth;

        for (int k = 1; k < steps; k++)
        {
            float centre = cross + (k % 2 == 1 ? CityDesign.AscentZigzag : -CityDesign.AscentZigzag);
            ascent.Landings.Add(Landing(host, side, centre, width, depth));
            ascent.LandingY.Add(baseY + ascent.StepRise * k);
        }

        return ascent;
    }

    private static CityRect Landing(CityRect host, Facade side, float cross, float width, float depth)
    {
        float half = width * 0.5f;

        switch (side)
        {
            case Facade.West:
                return new CityRect(host.MinX - depth, host.MinX, cross - half, cross + half);
            case Facade.East:
                return new CityRect(host.MaxX, host.MaxX + depth, cross - half, cross + half);
            case Facade.South:
                return new CityRect(cross - half, cross + half, host.MinZ - depth, host.MinZ);
            default:
                return new CityRect(cross - half, cross + half, host.MaxZ, host.MaxZ + depth);
        }
    }

    private static void EmitAscent(CityPlanResult plan, AscentPlan ascent, string group)
    {
        for (int i = 0; i < ascent.Landings.Count; i++)
        {
            plan.Slabs.Add(new SlabPlan($"{ascent.Name} L{i + 1}".Replace(' ', '_'), group,
                CityPieceKind.Ascent, ascent.Landings[i], ascent.LandingY[i],
                CityDesign.AscentLandingThickness));
        }
    }

    // ------------------------------------------------------------------ the tower spiral

    /// <summary>
    /// The way up the shaft. 79.8 m at <see cref="CityDesign.AscentStepRise"/> would be 45 mantles,
    /// which nobody would climb twice, so this is ramped instead: whole runs along each face of the
    /// shaft with a landing at every corner, at the shallowest pitch that fits in a whole number of
    /// runs.
    /// </summary>
    private static void PlanTowerSpiral(CityPlanResult plan, CityTraversalResult result)
    {
        CityRect shaft = ShaftFootprint;
        float cx = shaft.CentreX;
        float cz = shaft.CentreZ;

        float radius = CityDesign.TowerShaftSize * 0.5f + CityDesign.TowerSpiralLandingSize * 0.5f;
        float baseY = CityDesign.TowerPodiumY;
        float topY = CityDesign.TowerShaftTopY;
        float rise = topY - baseY;

        // Clear horizontal run between the two corner landings a run connects.
        float run = 2f * radius - CityDesign.TowerSpiralLandingSize;
        float maxRisePerRun = run * Mathf.Tan(CityDesign.TowerSpiralMaxPitch * Mathf.Deg2Rad);
        int runs = Mathf.Max(1, Mathf.CeilToInt(rise / maxRisePerRun));
        float risePerRun = rise / runs;
        float pitch = Mathf.Atan2(risePerRun, run) * Mathf.Rad2Deg;
        float slope = Mathf.Sqrt(run * run + risePerRun * risePerRun);

        // (dx, dz) of the four corner stations, clockwise seen from above.
        float[] stationX = { radius, radius, -radius, -radius };
        float[] stationZ = { radius, -radius, -radius, radius };

        for (int k = 0; k < runs; k++)
        {
            int from = k % 4;
            int to = (k + 1) % 4;
            float lowY = baseY + risePerRun * k;
            float highY = lowY + risePerRun;
            float midY = (lowY + highY) * 0.5f;

            bool alongZ = Mathf.Approximately(stationX[from], stationX[to]);
            float yaw = alongZ ? 0f : 90f;

            // Quaternion.Euler(pitch, yaw, 0) tips the run's +Z end down at yaw 0, and its +X end
            // down at yaw 90 - the same convention the Cut ramp uses, which is why the sign is
            // negative whenever the high end is the positive one.
            float ascending = alongZ
                ? Mathf.Sign(stationZ[to] - stationZ[from])
                : Mathf.Sign(stationX[to] - stationX[from]);
            float pitchDegrees = -pitch * ascending;

            float centreX = cx + (alongZ ? stationX[from] : 0f);
            float centreZ = cz + (alongZ ? 0f : stationZ[from]);

            // The deck's walking surface is half a thickness above the box's centre line, measured
            // along the slope normal.
            float centreY = midY - CityDesign.TowerSpiralThickness * 0.5f
                / Mathf.Cos(pitch * Mathf.Deg2Rad);

            plan.Ramps.Add(new RampPlan($"Tower_Spiral_Run{k}", TowerAscentGroup,
                new Vector3(centreX, centreY, centreZ),
                new Vector3(CityDesign.TowerSpiralDeckWidth, CityDesign.TowerSpiralThickness, slope),
                pitchDegrees, yaw));

            plan.Slabs.Add(new SlabPlan($"Tower_Spiral_Landing{k}", TowerAscentGroup,
                CityPieceKind.TowerAscent,
                CityRect.FromCentre(cx + stationX[to], cz + stationZ[to],
                    CityDesign.TowerSpiralLandingSize, CityDesign.TowerSpiralLandingSize),
                highY, CityDesign.TowerSpiralThickness));
        }

        // The first run is the only one a player can step onto: every later one starts a whole
        // run's rise above the roof below it. Which axis it travels along is what decides where
        // Phase 6D's gate stands, so it is recorded rather than re-derived there.
        bool footAlongZ = Mathf.Approximately(stationX[0], stationX[1]);

        // Every corner landing sits diagonally outboard of the shaft's corner, so the last one
        // meets the shaft roof at a point rather than along an edge. This fills that corner in:
        // it shares one full landing width with the landing and another with the roof.
        int lastStation = runs % 4;
        float half = CityDesign.TowerShaftSize * 0.5f;
        float size = CityDesign.TowerSpiralLandingSize;
        bool eastSide = stationX[lastStation] > 0f;

        CityRect summit = new CityRect(
            cx + (eastSide ? half - size : -half),
            cx + (eastSide ? half : size - half),
            cz + stationZ[lastStation] - size * 0.5f,
            cz + stationZ[lastStation] + size * 0.5f);

        plan.Slabs.Add(new SlabPlan("Tower_Spiral_Summit", TowerAscentGroup,
            CityPieceKind.TowerAscent, summit, topY, CityDesign.TowerSpiralThickness));

        result.Ascents.Add(new AscentPlan
        {
            Name = "Tower Spiral",
            Kind = AscentKind.TowerSpiral,
            BottomNode = PodiumNode,
            TopNode = ShaftRoofNode,
            BaseY = baseY,
            TopY = topY,
            StepCount = runs,
            StepRise = risePerRun,
            BottomFootprint = PodiumFootprint,
            TopFootprint = shaft,
            IsRamped = true,
            PitchDegrees = pitch,
            SummitFootprint = summit,
            FinalLanding = CityRect.FromCentre(cx + stationX[lastStation], cz + stationZ[lastStation],
                size, size),
            FootLanding = CityRect.FromCentre(cx + stationX[0], cz + stationZ[0], size, size),
            FootRunAlongZ = footAlongZ,
            FootRun = footAlongZ
                ? new CityRect(cx + stationX[0] - CityDesign.TowerSpiralDeckWidth * 0.5f,
                    cx + stationX[0] + CityDesign.TowerSpiralDeckWidth * 0.5f,
                    cz + Mathf.Min(stationZ[0], stationZ[1]), cz + Mathf.Max(stationZ[0], stationZ[1]))
                : new CityRect(cx + Mathf.Min(stationX[0], stationX[1]),
                    cx + Mathf.Max(stationX[0], stationX[1]),
                    cz + stationZ[0] - CityDesign.TowerSpiralDeckWidth * 0.5f,
                    cz + stationZ[0] + CityDesign.TowerSpiralDeckWidth * 0.5f)
        });
    }

    // ------------------------------------------------------------------ relays

    private static void PlanRelays(CityPlanResult plan, CityTraversalResult result)
    {
        foreach (RelaySite site in Relays)
        {
            if (!TryResolve(plan, result, site.Host, out TraversalSurface host))
            {
                result.Problems.Add($"{site.Name}: host {site.Host} did not resolve");
                continue;
            }

            result.Relays.Add(new RelayPlan
            {
                Name = site.Name,
                Node = host.Node,
                CellName = site.Host.Name,
                Group = CityDesign.Cell(site.Host.Name).Group,
                Footprint = host.Footprint,
                SurfaceY = host.SurfaceY
            });
        }
    }
}
