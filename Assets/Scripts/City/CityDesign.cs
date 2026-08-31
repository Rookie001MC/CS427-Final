using UnityEngine;

/// <summary>Which of the five districts (plus the landmark) a grid cell belongs to.</summary>
public enum DistrictGroup
{
    CityCenter,
    Residential,
    Industrial,
    Corporate,
    OldQuarter,
    Landmark
}

/// <summary>How the roofs inside one grid cell relate to each other.</summary>
public enum RoofClusterMode
{
    /// <summary>
    /// Roofs in the same lot row form a traversal cluster and must stay inside
    /// <see cref="CityDesign.RoofClusterTolerance"/> of one another.
    /// </summary>
    PerRow,

    /// <summary>
    /// Every building stands alone; roofs are reached by designed connections (skybridges, the
    /// tower ascent) rather than by jumping between them, so the cluster rule does not apply.
    /// </summary>
    Isolated
}

/// <summary>One cell of the 3 x 3 superblock grid.</summary>
public readonly struct DistrictCell
{
    public readonly string Name;
    public readonly DistrictGroup Group;

    /// <summary>0 = west, 1 = centre, 2 = east.</summary>
    public readonly int Column;

    /// <summary>0 = south, 1 = centre, 2 = north.</summary>
    public readonly int Row;

    /// <summary>Lot subdivision of the 180 m superblock, along X and along Z.</summary>
    public readonly int LotsX;
    public readonly int LotsZ;

    /// <summary>Width of the streets separating this cell's lots.</summary>
    public readonly float InternalStreetWidth;

    /// <summary>
    /// Inclusive storey range for this cell's massing. Multiply by
    /// <see cref="CityDesign.StoreyHeight"/> for metres.
    /// </summary>
    public readonly int MinStoreys;
    public readonly int MaxStoreys;

    public readonly RoofClusterMode ClusterMode;

    public DistrictCell(string name, DistrictGroup group, int column, int row,
        int lotsX, int lotsZ, float internalStreetWidth,
        int minStoreys, int maxStoreys, RoofClusterMode clusterMode)
    {
        Name = name;
        Group = group;
        Column = column;
        Row = row;
        LotsX = lotsX;
        LotsZ = lotsZ;
        InternalStreetWidth = internalStreetWidth;
        MinStoreys = minStoreys;
        MaxStoreys = maxStoreys;
        ClusterMode = clusterMode;
    }

    public CityRect Bounds => CityDesign.CellBounds(Column, Row);

    public float MinHeight => MinStoreys * CityDesign.StoreyHeight;

    public float MaxHeight => MaxStoreys * CityDesign.StoreyHeight;
}

/// <summary>
/// Every authored dimension of Skybound City, in one place.
///
/// The Phase 6A design report sized the city against a walk/sprint/jump controller; Phase 6A.5
/// then added the slide, vault, mantle and wall run and issued three corrections, all of which are
/// applied here and marked. Nothing in the builders may hard-code a dimension that belongs in this
/// file - that is what stops the geometry and the validators drifting apart.
///
/// Layout: a 3 x 3 arrangement of 180 m superblocks separated by 14 m avenues,
/// 3 x 180 + 2 x 14 = 568 m, centred in a 600 x 600 m collidable core, leaving a 16 m perimeter.
/// </summary>
public static class CityDesign
{
    // ------------------------------------------------------------------ plan

    /// <summary>Extent of the collidable, authored core. The backdrop beyond it is Phase 6E.</summary>
    public const float CoreExtent = 600f;

    public const float SuperblockSize = 180f;

    /// <summary>
    /// PHASE 6A.5 CHANGE 2: 12 m -> 14 m.
    ///
    /// 12 m was chosen to sit beyond the 9.24 m flat sprint maximum. It does not survive the new
    /// move set: dropping one storey onto the far side clears 13.63 m, so a 12 m avenue would be
    /// crossable from any building one floor taller than its neighbour, and district boundaries
    /// would stop meaning anything. 14 m restores the rule with 0.37 m to spare, while staying
    /// well inside the drop-assisted figure for a *deliberate* high-to-low crossing.
    ///
    /// Asserted against the live movement values by RouteTierValidator.
    /// </summary>
    public const float AvenueWidth = 14f;

    public const float SecondaryStreetWidth = 6.5f;

    public const float AlleyWidth = 4f;

    /// <summary>Open square at the centre of the City Center block. Never crossable at roof level.</summary>
    public const float PlazaSize = 40f;

    public const int GridSize = 3;

    /// <summary>Distance between the centres of adjacent superblocks.</summary>
    public const float CellPitch = SuperblockSize + AvenueWidth;

    /// <summary>3 x 180 + 2 x 14.</summary>
    public const float GridSpan = GridSize * SuperblockSize + (GridSize - 1) * AvenueWidth;

    /// <summary>Ring of open ground between the outer superblocks and the edge of the core.</summary>
    public const float PerimeterMargin = (CoreExtent - GridSpan) * 0.5f;

    // ------------------------------------------------------------------ elevation

    /// <summary>
    /// PHASE 6A.5 CHANGE 3: 3.5 m -> 3.6 m.
    ///
    /// The airborne mantle raised the absolute climb ceiling to 3.30 m
    /// (<see cref="TraversalEnvelope.MantleAssistedClimb"/>). At 3.5 m a storey cleared that by
    /// only 0.20 m, and the per-floor cornices planned for Phase 6E would have turned every facade
    /// into a ladder. 3.6 m restores a 0.30 m margin for a 3 % height increase.
    ///
    /// A storey must stay taller than the climb ceiling: vertical gain has to come from designed
    /// geometry, never from hopping up a wall.
    /// </summary>
    public const float StoreyHeight = 3.6f;

    /// <summary>
    /// PHASE 6A.5 CHANGE 1: +1.2 m -> +2.0 m.
    ///
    /// Adjacent roofs inside one traversal cluster must sit within this of each other. The mantle
    /// makes a 2.0 m step directly climbable, so rooflines no longer have to be flattened to a
    /// common height to stay linked - which is what lets the massing vary at all.
    /// </summary>
    public const float RoofClusterTolerance = 2f;

    /// <summary>Matches FallDetector.deathHeight. The controller's own reset sits below it.</summary>
    public const float DeathPlaneY = -12f;

    /// <summary>
    /// Where the controller's own fall reset sits, one storey under the death plane.
    ///
    /// Two things can notice a player below the world, and only one of them should act on it:
    /// FallDetector raises a death the run counts, and BasicFirstPersonController silently
    /// teleports the player back to their spawn. With both on <see cref="DeathPlaneY"/> which one
    /// wins is Update order. Putting the controller's lower makes it the backstop it is meant to
    /// be - it only ever fires for a player the run system somehow missed.
    /// </summary>
    public const float ControllerFallResetY = DeathPlaneY - StoreyHeight;

    public const float TowerTopY = 120f;

    /// <summary>Seven storeys.</summary>
    public const float TowerPodiumY = 7 * StoreyHeight;

    public const float TowerPodiumSize = 90f;

    public const float TowerShaftSize = 26f;

    public const float TowerShaftTopY = 105f;

    public const float TowerMastSize = 6f;

    /// <summary>"The Cut" - the sunken loading trench through the Old Quarter.</summary>
    public const float CutFloorY = -8f;

    public const float CutWidth = 10f;

    // ------------------------------------------------------------------ traversal (Phase 6C)

    /// <summary>
    /// Rise of one step in a fire escape, scaffold or roof riser.
    ///
    /// Sits 0.2 m inside <see cref="RouteTiers.MantleStepRise"/>, so every step in an ascent is a
    /// mantle the player definitely has rather than one they only just have. That is what makes an
    /// ascent uniformly ORANGE: it never needs a run-up, and it never needs a lucky frame.
    /// </summary>
    public const float AscentStepRise = 1.8f;

    /// <summary>Along the host facade.</summary>
    public const float AscentLandingWidth = 2.4f;

    /// <summary>
    /// Out from the host facade. Above the ORANGE minimum landing of 1.2 m with margin, and
    /// shallow enough that a landing hung over a 3.5 m Old Quarter street still leaves it open.
    /// </summary>
    public const float AscentLandingDepth = 1.6f;

    public const float AscentLandingThickness = 0.25f;

    /// <summary>
    /// Sideways offset of alternate landings. 2 x 1.3 m = 2.6 m between the centres of two
    /// consecutive 2.4 m landings, so they clear each other by 0.2 m and the stack reads as a
    /// zigzag rather than as a single column of shelves.
    /// </summary>
    public const float AscentZigzag = 1.3f;

    /// <summary>A scaffold is the same stack with a working deck instead of a landing.</summary>
    public const float ScaffoldLandingWidth = 4.2f;

    public const float ScaffoldLandingDepth = 2.4f;

    /// <summary>
    /// Skybridge deck width. Deliberately above the GREEN minimum landing depth of 3.0 m: walking
    /// onto a bridge from the roof it is flush with must never grade harder than the walk it is.
    /// </summary>
    public const float SkybridgeWidth = 3.2f;

    public const float SkybridgeThickness = 0.4f;

    /// <summary>
    /// How much of the two footprints must face each other across the deck. Below this the bridge
    /// is landing on a corner, and the roof it arrives at is not really the roof it was aimed at.
    /// </summary>
    public const float SkybridgeMinBearing = 8f;

    /// <summary>
    /// The crane jib is a walkway, not a bridge: 2.0 m is exactly the BLUE minimum landing depth,
    /// so stepping onto it is a graded jump and the Industrial crossing costs more than a stroll.
    /// </summary>
    public const float CraneDeckWidth = 2f;

    /// <summary>Two storeys of clearance above the higher of the two roofs the jib serves.</summary>
    public const float CraneJibRise = 2 * StoreyHeight;

    /// <summary>How far the jib cantilevers past each facade it crosses between.</summary>
    public const float CraneJibOverhang = 12f;

    public const float CraneMastSize = 5f;

    /// <summary>Mast above the jib: the cab and the apex.</summary>
    public const float CraneMastHeadroom = 9f;

    public const float CraneCounterJibLength = 16f;

    /// <summary>
    /// The podium wings: two low arms that carry the podium roof out to the edge of the landmark
    /// superblock, so the bridges from the City Center and the Old Quarter cross one 14 m avenue
    /// instead of the 59 m from the avenue to the podium face. Without them the tower is reached
    /// by two spans longer than any other in the city, for no design reason.
    /// </summary>
    public const float TowerWingLength = (SuperblockSize - TowerPodiumSize) * 0.5f;

    public const float TowerWingWidth = 40f;

    /// <summary>Corner landing of the spiral that climbs the shaft. Square.</summary>
    public const float TowerSpiralLandingSize = 4.5f;

    public const float TowerSpiralDeckWidth = 3.5f;

    public const float TowerSpiralThickness = 0.4f;

    /// <summary>
    /// Steepest the spiral may run. Well inside the controller's 50 degree slope limit, and inside
    /// the 24 degree step the walkability flood fill accepts, so the ascent is walked rather than
    /// mantled - 79.8 m of 1.8 m steps would be 45 mantles and nobody would climb it twice.
    /// </summary>
    public const float TowerSpiralMaxPitch = 22f;

    /// <summary>
    /// The player's CharacterController slope limit. The Cut ramp and the tower spiral are both
    /// authored against it, so it lives here rather than being typed into the builder twice.
    /// </summary>
    public const float SlopeLimit = 50f;

    /// <summary>
    /// Largest drop the rooftop network is allowed to count as a connection.
    ///
    /// <see cref="RouteTiers.Classify"/> puts no limit on a descent - a 40 m fall "reaches" the
    /// roof below it. Phase 6D adds `FallImpactDetector`, at which point it will not. Three storeys
    /// is what the roof graph counts now, so the redundancy this phase proves survives that.
    /// </summary>
    public const float SafeDropHeight = 3 * StoreyHeight;

    // ------------------------------------------------------------------ objectives (Phase 6D)

    /// <summary>
    /// Fall the player does not survive, measured from the highest point of the fall to the
    /// surface they land on.
    ///
    /// Derived rather than chosen: <see cref="SafeDropHeight"/> is what the Phase 6C roof graph
    /// counts as a connection, so a fatal fall has to be strictly above it or the redundancy that
    /// phase proved would evaporate the moment falling started to cost something. One storey of
    /// margin on top of it means a drop the network calls a route is never within 3.6 m of killing
    /// the player, and 14.4 m still kills a fall off any district roof but the shortest.
    ///
    /// It also stays above the Cut: the trench floor is 8 m down, so dropping into it remains the
    /// shortcut Phase 6B authored rather than a death.
    /// </summary>
    public const float FatalFallHeight = SafeDropHeight + StoreyHeight;

    /// <summary>Marker plinth under an objective relay. Decoration: it carries no collider.</summary>
    public const float RelayPadSize = 6f;

    public const float RelayPadRise = 0.15f;

    /// <summary>
    /// The relay's mast. Two storeys, so a relay reads from the avenue below and from the roofs
    /// across it - the compass says which way, and the mast says which roof.
    /// </summary>
    public const float RelayMastSize = 0.9f;

    public const float RelayMastHeight = 2 * StoreyHeight;

    /// <summary>How far above the roof a relay or anchor trigger reaches. A jumping player is caught.</summary>
    public const float ObjectiveTriggerHeight = 3f;

    /// <summary>
    /// How far above a surface a respawn point sits. The plaza spawn uses the same lift: a capsule
    /// placed exactly on a slab starts the frame intersecting it, and the CharacterController's
    /// first move then resolves that penetration somewhere neither the anchor nor the level chose.
    /// </summary>
    public const float RespawnLift = 0.2f;

    /// <summary>Marker pad under a respawn anchor. Decoration, like the relay plinth.</summary>
    public const float AnchorPadSize = 3f;

    public const float AnchorPadRise = 0.1f;

    /// <summary>
    /// How far in from the facade an anchor at the top of a fire escape stands. Far enough that
    /// respawning puts the player on the roof rather than on the last ledge of the stack.
    /// </summary>
    public const float AnchorInset = 2.5f;

    /// <summary>
    /// The hoarding across the foot of the tower spiral, removed when every relay is captured.
    ///
    /// Two storeys: <see cref="TraversalEnvelope.MantleAssistedClimb"/> is 3.30 m, so 7.2 m is more
    /// than double the highest thing a player can climb onto, with room for a wall jump on top of
    /// it. A gate that can be mantled is not a gate.
    /// </summary>
    public const float TowerGateHeight = 2 * StoreyHeight;

    public const float TowerGateThickness = 0.6f;

    /// <summary>Extra length on the gate's side wall, past where the spiral outruns the climb ceiling.</summary>
    public const float TowerGateMargin = 2f;

    /// <summary>
    /// The finish volume on the shaft roof. Inset from the roof edge so arriving over the summit
    /// slab crosses it, and a player who falls past the roof does not.
    /// </summary>
    public const float SummitFinishInset = 2f;

    public const float SummitFinishHeight = 6f;

    // ------------------------------------------------------------------ presentation

    /// <summary>
    /// PHASE 6A RISK 3: a 600 m clip plane cuts a 120 m tower seen from across the map. Fog closes
    /// first, so raising the plane costs nothing visually.
    /// </summary>
    public const float CameraFarClip = 1200f;

    public const float FogStart = 120f;

    /// <summary>
    /// PHASE 6E CHANGE: 450 m -> 700 m.
    ///
    /// 450 m closed exactly at the edge of the 600 m core, which was right while there was nothing
    /// beyond it. The backdrop ring runs from <see cref="BackdropInnerRadius"/> at 372 m out to
    /// <see cref="BackdropOuterRadius"/> at 510 m, and 450 m would have erased all of it.
    ///
    /// At 700 m the far side of the city sits at about a third of full fog, the nearest backdrop
    /// ring at 43 % and the outermost at two thirds - a skyline that recedes rather than one that
    /// either stops dead or is not there. It stays well inside <see cref="CameraFarClip"/>, which
    /// is the other thing fog has to do: close before the clip plane, or the clip plane is visible.
    /// </summary>
    public const float FogEnd = 700f;

    // ------------------------------------------------------------------ environment art (6E)

    /// <summary>
    /// Sun elevation and compass bearing. A low sun is what gives a city of boxes long shadows down
    /// its avenues; the Phase 6B greybox sat at 46 degrees, which lit every face almost equally and
    /// is the single biggest reason the massing read flat.
    /// </summary>
    public const float SunPitch = 24f;

    public const float SunYaw = 138f;

    public const float SunIntensity = 1.35f;

    // ---- facade dressing ----------------------------------------------------------
    //
    // A note on why every one of these is decoration rather than geometry: PHASE 6A.5 CHANGE 3
    // raised the storey to 3.6 m specifically so that per-floor cornices would not turn a facade
    // into a ladder. That margin is 0.30 m, which is thin. Making the whole 6E layer collider-free
    // removes the question entirely - a band a player cannot touch cannot be climbed, whatever its
    // rise - and it is what lets the Phase 6B walkability fill, the Phase 6C tier measurements and
    // the Phase 6D route probes all still read exactly what they read before.

    /// <summary>How far the ground-floor plinth stands proud of the facade above it.</summary>
    public const float PlinthProud = 0.35f;

    public const float PlinthHeight = 1.4f;

    /// <summary>The crown. Sits under the roof surface, never above it, so no roof grows a lip.</summary>
    public const float CorniceProud = 0.5f;

    public const float CorniceDepth = 0.7f;

    public const float FloorBandProud = 0.16f;

    public const float FloorBandHeight = 0.16f;

    /// <summary>
    /// Above this many storeys a facade bands every second floor instead of every floor. A 19
    /// storey Corporate tower with a band on each slab is 18 boxes for a line nobody can resolve
    /// from the street, and the renderer budget is finite.
    /// </summary>
    public const int MaxFacadeBands = 10;

    /// <summary>Solid corner the glazing stops short of, so a facade has structure at its edges.</summary>
    public const float FacadePierWidth = 1.5f;

    public const float GlassProud = 0.16f;

    public const float WindowSill = 0.5f;

    public const float WindowHead = 0.5f;

    /// <summary>
    /// Spacing of punched window bays on a masonry district's facade, and the widest a facade may
    /// be before it stops adding more of them.
    ///
    /// Both are as much a renderer budget as a design: 108 buildings times four facades means every
    /// extra bay costs the city about 430 boxes, and the Phase 6A ceiling is 3800. Wide bays at 9.5 m
    /// centres also survive the distance this city is mostly seen from - a 6.5 m rhythm on a
    /// residential block reads as noise from the far side of an avenue.
    /// </summary>
    public const float WindowBaySpacing = 9.5f;

    public const int WindowMaxBays = 3;

    public const float WindowStripWidth = 2.4f;

    /// <summary>Corner pier on a facade tall enough to need one. Corporate and the tower only.</summary>
    public const int PierStoreyMin = 10;

    // ---- rooftops -----------------------------------------------------------------

    /// <summary>
    /// How far in from the roof edge rooftop plant stands, and how deep the band it stands in is.
    /// </summary>
    public const float RoofPropInset = 2.4f;

    public const float RoofPropBandDepth = 3.4f;

    /// <summary>One prop per this much of a dead edge.</summary>
    public const float RoofPropSlot = 9f;

    /// <summary>A roof narrower than this has no room for plant that is not in the player's way.</summary>
    public const float RoofPropMinRoof = 12f;

    /// <summary>
    /// How far a neighbouring surface has to be before an edge counts as *dead* - one nothing can
    /// be jumped to or from, and therefore one a player never lands on.
    ///
    /// This is the rule the whole rooftop prop layer rests on. Props carry no collider, so they can
    /// never change what a roof measures, but a player running through an air-conditioning unit
    /// still looks wrong. Sitting above the flat sprint reach of 10.39 m means an edge is only
    /// dressed when no move in the game can arrive at it.
    /// </summary>
    public const float DeadEdgeReach = 13f;

    /// <summary>Clearance a prop keeps from a relay pad, an anchor, a bridge deck or an ascent.</summary>
    public const float PropClearance = 2.5f;

    // ---- traversal dressing -------------------------------------------------------

    public const float RailHeight = 1.05f;

    public const float RailThickness = 0.1f;

    /// <summary>
    /// The lit strip laid down the middle of every deck, landing and run the traversal layer
    /// authored. It carries no collider and no meaning the geometry does not already have - it is
    /// purely so that a bridge reads as a route from forty metres away rather than as a ledge.
    /// </summary>
    public const float RouteStripWidth = 0.55f;

    public const float RouteStripRise = 0.04f;

    // ---- signage ------------------------------------------------------------------

    public const float SignWidth = 1.7f;

    public const float SignDepth = 0.45f;

    public const float SignMaxHeight = 7.5f;

    /// <summary>A facade shorter than this carries no blade sign.</summary>
    public const int SignStoreyMin = 3;

    /// <summary>Above this many storeys a building wears a lit crown at its cornice.</summary>
    public const int CrownStoreyMin = 10;

    public const float CrownHeight = 0.4f;

    /// <summary>How close a facade has to sit to the edge of its superblock to face an avenue.</summary>
    public const float AvenueFacingTolerance = 2f;

    // ---- street furniture ---------------------------------------------------------

    public const float StreetLampSpacing = 48f;

    public const float StreetLampHeight = 6.5f;

    public const float KerbWidth = 0.5f;

    public const float KerbRise = 0.22f;

    /// <summary>Painted markings sit this far above the paving, which is enough to beat z-fighting.</summary>
    public const float PaintRise = 0.03f;

    // ---- the backdrop ring --------------------------------------------------------

    /// <summary>
    /// Where the city the player cannot reach begins.
    ///
    /// The core's half-extent is 300 m and a backdrop block is up to 62 m across, so the nearest
    /// ring has to clear 300 + its own half-diagonal (44 m) + the radial jitter (14 m) before any
    /// part of it can reach inside the paving. 372 m does, with 14 m to spare - and a block that
    /// crossed the perimeter would be geometry a player could run at and fall straight through.
    /// </summary>
    public const float BackdropInnerRadius = 372f;

    public const float BackdropRingStep = 46f;

    public const int BackdropRings = 4;

    public const int BackdropPerRing = 26;

    public const float BackdropMinHeight = 22f;

    public const float BackdropMaxHeight = 96f;

    public const float BackdropMinWidth = 26f;

    public const float BackdropMaxWidth = 62f;

    /// <summary>
    /// Outermost thing in the scene. Has to stay inside <see cref="CameraFarClip"/> with room, and
    /// it does: the far ring lands at 492 m and the far clip is 1200 m.
    /// </summary>
    public static float BackdropOuterRadius
        => BackdropInnerRadius + (BackdropRings - 1) * BackdropRingStep;

    // ---- route guidance -----------------------------------------------------------
    //
    // The Phase 6D compass points *at* the objective. In a city of solid blocks a bearing through a
    // building is worse than no bearing: it tells the player to go somewhere they cannot go and
    // gives them no idea which way round it. These size the world-space trail that answers the
    // other question - not where the relay is, but which way to run.

    /// <summary>
    /// Distance between breadcrumbs along a straight run.
    ///
    /// Chosen against the movement rather than by eye: the sprint is 7.5 m/s, so 7 m is a marker
    /// about every second - close enough to read as a line and far enough apart that a straight
    /// avenue does not become a wall of chevrons.
    /// </summary>
    public const float GuideBreadcrumbSpacing = 7f;

    /// <summary>
    /// How many markers exist at all. A hard pool: the guide never instantiates during play, it
    /// moves these and hides the rest.
    /// </summary>
    public const int GuideMarkerCount = 26;

    /// <summary>
    /// Upright markers that say what the *next move* is rather than merely which way it is.
    ///
    /// The chevrons answer "which way"; these answer "and then what" - one stands at the foot of
    /// the fire escape you have to climb, at the mouth of the skybridge you have to cross, at the
    /// roof edge you have to jump from. Four is enough: a player only ever needs the next couple of
    /// decisions, and more of them turns a route into a queue of instructions.
    /// </summary>
    public const int GuideActionMarkerCount = 4;

    public const float GuideActionMarkerHeight = 3.2f;

    public const float GuideActionMarkerWidth = 0.7f;

    /// <summary>
    /// How far down the route the trail is drawn. Past this it is not guidance, it is clutter in
    /// front of the thing the player is trying to look at.
    /// </summary>
    public const float GuideVisibleRange = 170f;

    /// <summary>
    /// How far off the route the player has to be before the arc reading is re-projected.
    ///
    /// Not "how far they may move": running the route is the normal case and must trigger nothing.
    /// And not a re-search either. Whether the route is still the player's route is a question
    /// about the graph - are they standing on a node it passes through - and this number only
    /// decides when the *reading* of how far along it they are has stopped being believable, which
    /// happens when something moves them without their running there.
    ///
    /// It is measured with the standing node's own footprint added, for the same reason
    /// <see cref="CityNavGraph.Score"/> measures to a surface rather than to its middle: the route
    /// is anchored at a node's centre, an Industrial roof node is 88 m across, and a player
    /// standing legitimately on its far corner is 51 m from the route that starts under their feet.
    /// Read bare, that says "off the route" on every frame the player spends on a roof.
    /// </summary>
    public const float GuideRecomputeDistance = 9f;

    /// <summary>
    /// The closest two chevrons may be laid before one of them is dropped.
    ///
    /// A resampled chevron and the corner marker for the same turn can land on top of each other -
    /// 47 of 862 across this city's routes, the closest 0.22 m apart. Two chevrons at the same
    /// height, on the same plane, pointing almost the same way is a z-fight, and a z-fight is a
    /// pair of surfaces swapping which one is in front as the camera turns. That is the flicker a
    /// player sees standing still at the top of a fire escape.
    ///
    /// One chevron measures about <see cref="GuideMarkerSize"/> across its arms, so twice that is
    /// the gap at which two of them stop sharing pixels. Comfortably under
    /// <see cref="GuideBreadcrumbSpacing"/>, so an evenly resampled run never loses a marker.
    /// </summary>
    public const float GuideMarkerClearGap = GuideMarkerSize * 2f;

    /// <summary>
    /// Step between stops on a street corridor, on top of the stop at every crossing.
    ///
    /// The player is snapped to the nearest node, and on a bare lattice the nearest node on a 190 m
    /// avenue can be 95 m behind them - which makes the first leg of the trail point back up the
    /// street they just ran down. 40 m keeps the trail starting in front of them without turning a
    /// two-hundred-node graph into a two-thousand-node one.
    /// </summary>
    public const float GuideLatticeStep = 40f;

    /// <summary>
    /// How much a metre of height counts for when deciding which node the player is standing on.
    ///
    /// Heavily weighted on purpose. A player on a 25 m roof is horizontally within a few metres of
    /// the pavement below them, and a guide that snapped them to the street would route them down a
    /// fire escape they are standing on top of.
    /// </summary>
    public const float GuideVerticalWeight = 6f;

    /// <summary>
    /// Height difference that costs nothing at all when snapping a player to a node.
    ///
    /// The weight above is applied only past this band, and the band is why. A CharacterController
    /// standing still on a roof does not have a constant y: gravity is integrated every frame and
    /// the ground contact resolves it, so the transform breathes by a centimetre or two. Multiplied
    /// by a weight of six that is a tenth of a metre of score jitter every frame, and where two
    /// nodes are close in score it is enough to make the snap chatter between them - which is a
    /// different route, and therefore a different trail, on alternating frames.
    ///
    /// Two metres also happens to be <see cref="RoofClusterTolerance"/>: the height inside which
    /// two roofs are one traversal surface anyway.
    /// </summary>
    public const float GuideSurfaceBand = 2f;

    /// <summary>
    /// How much better a rival node has to score before the guide changes its mind about where the
    /// player is standing.
    ///
    /// Hysteresis, and the reason it is needed rather than merely nice: 42 of 3075 sampled rooftop
    /// positions have a second node within 2 m of the winner's score, and the largest Corporate roof
    /// flips its nearest node 103 times over a 1 m walking grid. Without a margin the guide re-picks
    /// a start node on the boundary every time the player crosses it.
    /// </summary>
    public const float GuideSnapHysteresis = 6f;

    /// <summary>How far ahead of the player the nearest visible chevron sits.</summary>
    public const float GuideTrailLead = 2.5f;

    /// <summary>
    /// How far along the route the player's position is searched for, either side of where they
    /// were last frame.
    ///
    /// The trail is anchored to the route and shown by arc length, so "which chevrons are ahead" is
    /// a projection of the player onto the polyline. A global search for the closest point would
    /// jump wherever a route passes near itself - two legs of a switchback, an avenue crossed twice
    /// - so the search is windowed and cannot teleport.
    /// </summary>
    public const float GuideProjectionWindow = 30f;

    /// <summary>
    /// How much nearer a rival objective has to be before the mission switches to it.
    ///
    /// `ObjectiveTracker` points at the nearest uncaptured relay, and on the line where two of them
    /// are equidistant that flips every frame - 113 of 5041 sampled street positions sit inside 3 m
    /// of such a line. The compass label flickers between two district names and the route guide
    /// re-searches the whole city on alternating frames. The rule is unchanged; it is just no longer
    /// evaluated on a knife edge.
    /// </summary>
    public const float ObjectiveStickiness = 20f;

    /// <summary>Rise of a climb, as a multiple of its height, when costing a route.</summary>
    public const float GuideClimbWeight = 2.2f;

    /// <summary>
    /// Flat cost added to every move, in metres. Without it the search would happily chain six
    /// two-metre hops to save a metre of walking.
    /// </summary>
    public const float GuideMovePenalty = 3f;

    /// <summary>How far in from a roof edge a take-off marker sits, so it is on the roof.</summary>
    public const float GuideEdgeInset = 1.5f;

    /// <summary>How far out from a fire escape its pavement marker stands.</summary>
    public const float GuideFootStandoff = 1.6f;

    /// <summary>Margin a route keeps from a facade when deciding whether it is clear.</summary>
    public const float GuideClearance = 0.6f;

    public const float GuideMarkerSize = 1.15f;

    /// <summary>How far above the surface a marker floats. Clear of paint, under knee height.</summary>
    public const float GuideMarkerRise = 0.4f;

    /// <summary>
    /// The pillar of light over the active objective. Tall enough to clear the tallest thing that
    /// could stand between the player and it, which is the tower.
    /// </summary>
    public const float GuideBeaconHeight = TowerTopY;

    public const float GuideBeaconWidth = 1.5f;

    // ------------------------------------------------------------------ district palette

    /// <summary>
    /// What one district is made of. Five values, because five is what it takes to build a facade
    /// out of parts and still have the district readable as one place: the massing itself, the
    /// trim that bands it, the panel behind its windows, the glass in them, and the one saturated
    /// colour its signs are allowed to be.
    /// </summary>
    public readonly struct DistrictPalette
    {
        /// <summary>The massing block. What the district reads as in silhouette.</summary>
        public readonly Color Massing;

        /// <summary>Plinths, cornices, floor bands, parapets. Always lighter than the massing.</summary>
        public readonly Color Trim;

        /// <summary>The recessed field a punched window sits in.</summary>
        public readonly Color Panel;

        /// <summary>Glazing. Dark, because a lit window is a sign and this is not one.</summary>
        public readonly Color Glass;

        /// <summary>The district's one emissive colour. Signs, crowns, beacons, lamp heads.</summary>
        public readonly Color Neon;

        /// <summary>True where the district glazes in continuous bands rather than punched bays.</summary>
        public readonly bool CurtainWall;

        public DistrictPalette(Color massing, Color trim, Color panel, Color glass, Color neon,
            bool curtainWall)
        {
            Massing = massing;
            Trim = trim;
            Panel = panel;
            Glass = glass;
            Neon = neon;
            CurtainWall = curtainWall;
        }
    }

    /// <summary>
    /// The five districts and the landmark, as colour.
    ///
    /// Zoning is the point: a player standing on a roof has to be able to tell which district they
    /// are in without a map, and the Phase 6B greybox told them apart only by value, which fog
    /// erases at the exact distance the information is needed. Each district therefore owns a hue
    /// family and one saturated accent, and no two accents are within reach of each other on the
    /// wheel - cyan, amber, lime, electric blue, magenta.
    /// </summary>
    public static DistrictPalette Palette(DistrictGroup group)
    {
        switch (group)
        {
            // Cool poured concrete and cyan. The hub, and the colour the UI already uses.
            case DistrictGroup.CityCenter:
                return new DistrictPalette(
                    new Color(0.42f, 0.44f, 0.48f), new Color(0.62f, 0.64f, 0.68f),
                    new Color(0.26f, 0.28f, 0.32f), new Color(0.10f, 0.15f, 0.20f),
                    new Color(0.10f, 0.85f, 1.00f), curtainWall: true);

            // Warm brick and amber. Low, dense, domestic.
            case DistrictGroup.Residential:
                return new DistrictPalette(
                    new Color(0.50f, 0.41f, 0.34f), new Color(0.66f, 0.58f, 0.49f),
                    new Color(0.31f, 0.25f, 0.21f), new Color(0.12f, 0.12f, 0.14f),
                    new Color(1.00f, 0.62f, 0.20f), curtainWall: false);

            // Olive steel and hazard lime. The one district whose accent is also a warning colour.
            case DistrictGroup.Industrial:
                return new DistrictPalette(
                    new Color(0.40f, 0.38f, 0.32f), new Color(0.55f, 0.52f, 0.43f),
                    new Color(0.25f, 0.24f, 0.20f), new Color(0.11f, 0.13f, 0.12f),
                    new Color(0.78f, 0.95f, 0.25f), curtainWall: false);

            // Blue glass and electric blue. Tall, sheer, and the only district that is mostly window.
            case DistrictGroup.Corporate:
                return new DistrictPalette(
                    new Color(0.33f, 0.39f, 0.47f), new Color(0.70f, 0.74f, 0.80f),
                    new Color(0.18f, 0.22f, 0.28f), new Color(0.06f, 0.10f, 0.16f),
                    new Color(0.25f, 0.55f, 1.00f), curtainWall: true);

            // Red masonry and magenta. The oldest fabric in the city, and the loudest signage.
            case DistrictGroup.OldQuarter:
                return new DistrictPalette(
                    new Color(0.45f, 0.35f, 0.31f), new Color(0.60f, 0.50f, 0.44f),
                    new Color(0.28f, 0.21f, 0.19f), new Color(0.10f, 0.10f, 0.11f),
                    new Color(1.00f, 0.24f, 0.62f), curtainWall: false);

            // Pale precast and a cold white-cyan. The tower is the orientation anchor, so it is the
            // brightest thing in the city at every hour.
            default:
                return new DistrictPalette(
                    new Color(0.60f, 0.62f, 0.66f), new Color(0.80f, 0.82f, 0.86f),
                    new Color(0.30f, 0.33f, 0.38f), new Color(0.08f, 0.13f, 0.18f),
                    new Color(0.55f, 0.95f, 1.00f), curtainWall: true);
        }
    }

    // ------------------------------------------------------------------ grid maths

    /// <summary>Centre of a superblock. Column and row both run 0..2, low to high on X / Z.</summary>
    public static float CellCentre(int index) => (index - (GridSize - 1) * 0.5f) * CellPitch;

    public static CityRect CellBounds(int column, int row)
        => CityRect.FromCentre(CellCentre(column), CellCentre(row), SuperblockSize, SuperblockSize);

    public static CityRect CoreBounds => CityRect.FromCentre(0f, 0f, CoreExtent, CoreExtent);

    /// <summary>The plaza, at the centre of the City Center superblock.</summary>
    public static CityRect Plaza
        => CityRect.FromCentre(CellCentre(1), CellCentre(1), PlazaSize, PlazaSize);

    /// <summary>
    /// Distance from the city axis to the centreline of the street ringing the plaza.
    ///
    /// The plaza is enclosed by buildings on all four sides, so the only ways off it are the two
    /// secondary streets that border it - and those run the full width of the superblock. This is
    /// the exact coordinate the route harness walks to leave the start area, which is why the
    /// City Center lot split pins the plaza to the centre instead of jittering it.
    /// </summary>
    public static float PlazaRingStreet => PlazaSize * 0.5f + SecondaryStreetWidth * 0.5f;

    /// <summary>Centreline of an avenue. Sign picks the west/south (-) or east/north (+) one.</summary>
    public static float AvenueCentre(int sign)
        => sign * (SuperblockSize * 0.5f + AvenueWidth * 0.5f);

    /// <summary>Centreline of the open perimeter ring just inside the edge of the core.</summary>
    public static float PerimeterCentre(int sign)
        => sign * (CoreExtent * 0.5f - PerimeterMargin * 0.5f);

    /// <summary>Where the player starts: on the plaza, south edge, facing north up the city axis.</summary>
    public static Vector3 SpawnPosition => new Vector3(0f, RespawnLift, Plaza.MinZ - 6f);

    // ------------------------------------------------------------------ districts

    /// <summary>
    /// The nine cells, in the arrangement drawn in the Phase 6A report. Storey ranges are the
    /// report's metre bands quantised to <see cref="StoreyHeight"/>, always landing inside the
    /// band rather than rounding out of it.
    /// </summary>
    public static readonly DistrictCell[] Cells =
    {
        // --- north row (row 2) ---
        new DistrictCell("ResidentialNorth", DistrictGroup.Residential, 0, 2,
            5, 5, AlleyWidth, 4, 7, RoofClusterMode.PerRow),                 // 14.4 - 25.2 m
        new DistrictCell("IndustrialYards", DistrictGroup.Industrial, 1, 2,
            3, 2, 7f, 2, 5, RoofClusterMode.PerRow),                         //  7.2 - 18.0 m
        new DistrictCell("IndustrialConstruction", DistrictGroup.Industrial, 2, 2,
            3, 2, 7f, 2, 5, RoofClusterMode.PerRow),                         //  7.2 - 18.0 m

        // --- centre row (row 1) ---
        new DistrictCell("ResidentialWest", DistrictGroup.Residential, 0, 1,
            5, 5, AlleyWidth, 4, 7, RoofClusterMode.PerRow),                 // 14.4 - 25.2 m
        new DistrictCell("CityCenter", DistrictGroup.CityCenter, 1, 1,
            3, 3, SecondaryStreetWidth, 5, 9, RoofClusterMode.PerRow),       // 18.0 - 32.4 m
        new DistrictCell("CorporateCore", DistrictGroup.Corporate, 2, 1,
            2, 2, SecondaryStreetWidth, 13, 19, RoofClusterMode.Isolated),   // 46.8 - 68.4 m

        // --- south row (row 0) ---
        new DistrictCell("OldQuarter", DistrictGroup.OldQuarter, 0, 0,
            6, 6, 3.5f, 3, 5, RoofClusterMode.PerRow),                       // 10.8 - 18.0 m
        new DistrictCell("TowerPodium", DistrictGroup.Landmark, 1, 0,
            1, 1, SecondaryStreetWidth, 7, 7, RoofClusterMode.Isolated),     // podium 25.2 m
        new DistrictCell("CorporateSouth", DistrictGroup.Corporate, 2, 0,
            2, 2, SecondaryStreetWidth, 11, 17, RoofClusterMode.Isolated)    // 39.6 - 61.2 m
    };

    public static DistrictCell Cell(string name)
    {
        foreach (DistrictCell cell in Cells)
        {
            if (cell.Name == name)
            {
                return cell;
            }
        }

        throw new System.ArgumentException("No district cell named " + name, nameof(name));
    }
}
