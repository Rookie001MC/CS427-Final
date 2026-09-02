using System.Collections.Generic;
using UnityEngine;

/// <summary>What a navigation node stands on.</summary>
public enum NavNodeKind
{
    /// <summary>A point on the street network: an avenue, the perimeter ring, the plaza ring.</summary>
    Street,

    /// <summary>The pavement at the foot of one of the thirteen ways up off the street.</summary>
    AscentFoot,

    /// <summary>A roof, a bridge deck, the podium, a wing or the shaft roof.</summary>
    Surface
}

/// <summary>One place the guidance can route through.</summary>
public readonly struct NavNode
{
    public readonly string Name;
    public readonly NavNodeKind Kind;

    /// <summary>Standing height, not a box centre - the same rule the rest of the city keeps.</summary>
    public readonly Vector3 Position;

    /// <summary>
    /// Half the surface this node stands for, on X and Z. Zero for a point on a street.
    ///
    /// It exists so that "which node is the player on" can be answered by asking whether they are
    /// *on* it rather than how far they are from the middle of it. A Corporate roof is 55 m across
    /// and its node sits at the centre, so a player standing near its edge is genuinely closer to
    /// the centre of the roof next door - and a guide that believed that re-routed them off the
    /// building they were standing on, 52 times over one walk.
    /// </summary>
    public readonly Vector3 Extent;

    public NavNode(string name, NavNodeKind kind, Vector3 position, Vector3 extent)
    {
        Name = name;
        Kind = kind;
        Position = position;
        Extent = extent;
    }
}

/// <summary>What the player actually has to do to take a link.</summary>
public enum NavMove
{
    /// <summary>Along a street, a roof or a deck. Nothing to do but run.</summary>
    Walk,

    /// <summary>Up a fire escape, a scaffold, a riser, a link stair or the tower spiral.</summary>
    Climb,

    /// <summary>Back down one of the same.</summary>
    Descend,

    /// <summary>A gap. The route has been graded, but the player still has to leave the ground.</summary>
    Jump,

    /// <summary>On or off a skybridge deck or the crane jib.</summary>
    Cross
}

/// <summary>
/// One directed move between two nav nodes.
///
/// <see cref="Exit"/> is what makes the breadcrumb trail follow the city rather than cut across it.
/// A node's position is the middle of a roof, but the player does not run to the middle of a roof
/// and then teleport - they run to the edge that faces where they are going and jump from there. So
/// every link carries the point on its *source* where the player leaves it, and the guidance draws
/// the polyline through those rather than through the node centres.
/// </summary>
public readonly struct NavLink
{
    public readonly int From;
    public readonly int To;

    /// <summary>Metres, weighted by how hard the move is. Never the raw distance.</summary>
    public readonly float Cost;

    public readonly Vector3 Exit;

    public readonly RouteTier Tier;

    /// <summary>
    /// What the player does to take it. Carried rather than inferred, because the guidance has to
    /// be able to say "climb here" at the foot of a fire escape and "cross here" at the mouth of a
    /// skybridge, and a cost and a tier cannot tell those apart.
    /// </summary>
    public readonly NavMove Move;

    public NavLink(int from, int to, float cost, Vector3 exit, RouteTier tier, NavMove move)
    {
        From = from;
        To = to;
        Cost = cost;
        Exit = exit;
        Tier = tier;
        Move = move;
    }
}

/// <summary>
/// One chevron on the ground: where it is, which way it points, how far along the route it sits,
/// and what the player is about to have to do.
///
/// <see cref="Along"/> is the field that makes the trail stop flickering. The markers are laid out
/// against the route's own arc length rather than against the player, so they hold still in the
/// world while the player moves past them - and which of them are showing is a projection of the
/// player onto that same arc, which is continuous, instead of a per-frame distance test with a hard
/// threshold to oscillate across.
///
/// <see cref="Remaining"/> is the field that makes it stop flickering when the <i>route</i>
/// changes, which is the harder half and the one that was still wrong. A marker's arc position is
/// measured from the start of the route, and the start of the route is the node the player is
/// standing on - so stepping from one rooftop node to the next re-anchored every marker in the
/// city, and 114 of 116 markers on a stretch of route that both searches agreed about landed in a
/// different place. Measured backwards from the objective instead, a stretch of route shared by two
/// searches is the same distance from the end in both, so it gets the same markers in the same
/// square metres however the player came to be on it.
/// </summary>
public readonly struct Breadcrumb
{
    public readonly Vector3 Position;

    /// <summary>Along the route, taken from the polyline rather than from the player's facing.</summary>
    public readonly Vector3 Forward;

    /// <summary>Metres from the start of the route.</summary>
    public readonly float Along;

    /// <summary>
    /// Metres from here to the objective, along the route. The marker's identity: invariant under
    /// a re-search that keeps this stretch of the route, which <see cref="Along"/> is not.
    /// </summary>
    public readonly float Remaining;

    /// <summary>What the player has to do next, at or just after this point.</summary>
    public readonly NavMove Move;

    /// <summary>True where this marker sits exactly on a transition rather than on a straight run.</summary>
    public readonly bool IsTransition;

    public Breadcrumb(Vector3 position, Vector3 forward, float along, NavMove move,
        bool isTransition, float remaining = 0f)
    {
        Position = position;
        Forward = forward;
        Along = along;
        Move = move;
        IsTransition = isTransition;
        Remaining = remaining;
    }
}

/// <summary>
/// The navigable city, as a graph, with a shortest-path search over it.
///
/// Built once by <see cref="CityNavigation"/> from the plan, serialized into the scene by
/// `SkyboundCityBuilder`, and rebuilt from those arrays at runtime by `RouteGuide`. One type for
/// both sides so the search the tests exercise is literally the search the player gets.
/// </summary>
public sealed class CityNavGraph
{
    public readonly List<NavNode> Nodes = new List<NavNode>();
    public readonly List<NavLink> Links = new List<NavLink>();

    private readonly Dictionary<string, int> index = new Dictionary<string, int>();

    /// <summary>Adjacency, as a list of link indices per node. Built on demand and cached.</summary>
    private List<int>[] adjacency;

    public int Add(string name, NavNodeKind kind, Vector3 position, Vector3 extent = default)
    {
        if (index.TryGetValue(name, out int existing))
        {
            return existing;
        }

        int id = Nodes.Count;
        Nodes.Add(new NavNode(name, kind, position, extent));
        index[name] = id;
        adjacency = null;
        return id;
    }

    public void Connect(int from, int to, float cost, Vector3 exit, RouteTier tier, NavMove move)
    {
        if (from < 0 || to < 0 || from == to)
        {
            return;
        }

        Links.Add(new NavLink(from, to, Mathf.Max(0.01f, cost), exit, tier, move));
        adjacency = null;
    }

    /// <summary>Both ways, at the same cost, each leaving from its own end.</summary>
    public void ConnectBoth(int a, int b, float cost, RouteTier tier)
    {
        Connect(a, b, cost, Nodes[a].Position, tier, NavMove.Walk);
        Connect(b, a, cost, Nodes[b].Position, tier, NavMove.Walk);
    }

    public int IndexOf(string name) => index.TryGetValue(name, out int id) ? id : -1;

    public bool Has(string name) => index.ContainsKey(name);

    /// <summary>Rebuilds the graph a builder baked into a scene. Same type, same search.</summary>
    public static CityNavGraph FromArrays(string[] names, int[] kinds, Vector3[] positions,
        Vector3[] extents, int[] from, int[] to, float[] cost, Vector3[] exit, int[] tier,
        int[] move)
    {
        CityNavGraph graph = new CityNavGraph();

        for (int i = 0; i < names.Length; i++)
        {
            graph.Add(names[i], (NavNodeKind)kinds[i], positions[i],
                i < extents.Length ? extents[i] : Vector3.zero);
        }

        for (int i = 0; i < from.Length; i++)
        {
            graph.Connect(from[i], to[i], cost[i], exit[i], (RouteTier)tier[i], (NavMove)move[i]);
        }

        return graph;
    }

    private void BuildAdjacency()
    {
        adjacency = new List<int>[Nodes.Count];

        for (int i = 0; i < adjacency.Length; i++)
        {
            adjacency[i] = new List<int>();
        }

        for (int i = 0; i < Links.Count; i++)
        {
            adjacency[Links[i].From].Add(i);
        }
    }

    public IReadOnlyList<int> LinksFrom(int node)
    {
        if (adjacency == null)
        {
            BuildAdjacency();
        }

        return adjacency[node];
    }

    // ------------------------------------------------------------------ search

    /// <summary>
    /// Cheapest path, as a list of link indices. Empty when the two are already the same node,
    /// null when there is no way through.
    ///
    /// Dijkstra rather than the breadth-first search <see cref="RoofGraph.Path"/> uses, and for a
    /// different question: that one answers "is this reachable, and by the easiest grade", which is
    /// what a *validator* asks. This one answers "which way should the player actually go", where a
    /// short awkward hop and a long stroll are genuinely comparable and the tier weighting in
    /// <see cref="CityNavigation.TierWeight"/> is what compares them.
    ///
    /// The graph is about two hundred nodes, so a linear scan for the next node is faster than a
    /// heap and has no allocation behind it. This runs when the objective changes, not per frame.
    /// </summary>
    public List<int> Path(int from, int to)
    {
        if (from < 0 || to < 0 || from >= Nodes.Count || to >= Nodes.Count)
        {
            return null;
        }

        if (from == to)
        {
            return new List<int>();
        }

        int count = Nodes.Count;
        float[] best = new float[count];
        int[] cameFrom = new int[count];
        bool[] done = new bool[count];

        for (int i = 0; i < count; i++)
        {
            best[i] = float.MaxValue;
            cameFrom[i] = -1;
        }

        best[from] = 0f;

        while (true)
        {
            int at = -1;
            float lowest = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                if (!done[i] && best[i] < lowest)
                {
                    lowest = best[i];
                    at = i;
                }
            }

            if (at < 0)
            {
                return null;
            }

            if (at == to)
            {
                break;
            }

            done[at] = true;

            foreach (int link in LinksFrom(at))
            {
                NavLink edge = Links[link];
                float through = best[at] + edge.Cost;

                if (through < best[edge.To])
                {
                    best[edge.To] = through;
                    cameFrom[edge.To] = link;
                }
            }
        }

        List<int> path = new List<int>();

        for (int node = to; node != from;)
        {
            int link = cameFrom[node];

            if (link < 0)
            {
                return null;
            }

            path.Add(link);
            node = Links[link].From;
        }

        path.Reverse();
        return path;
    }

    /// <summary>
    /// How badly a node fits a player standing at <paramref name="at"/>. Lower is better.
    ///
    /// Height is weighted far more heavily than distance, because a player on a 25 m roof is
    /// horizontally within a few metres of the street below them and a guide that snapped them to
    /// the pavement would route them down a fire escape they are standing on top of - but only
    /// *past* <see cref="CityDesign.GuideSurfaceBand"/>. Inside the band height costs nothing at
    /// all, which is what stops a CharacterController's centimetre of idle breathing from moving
    /// the score at all, let alone enough to change the answer.
    /// </summary>
    public float Score(int node, Vector3 at)
    {
        NavNode n = Nodes[node];
        Vector3 delta = n.Position - at;

        // Distance to the *surface*, not to its middle. Anywhere on a roof scores zero for that
        // roof, whatever its size, so a player standing on a building is never told they are
        // standing on the one next door.
        float dx = Mathf.Max(0f, Mathf.Abs(delta.x) - n.Extent.x);
        float dz = Mathf.Max(0f, Mathf.Abs(delta.z) - n.Extent.z);
        float horizontal = Mathf.Sqrt(dx * dx + dz * dz);

        float vertical = Mathf.Max(0f, Mathf.Abs(delta.y) - CityDesign.GuideSurfaceBand);
        return horizontal + vertical * CityDesign.GuideVerticalWeight;
    }

    /// <summary>The node a player standing at <paramref name="at"/> is on.</summary>
    public int Nearest(Vector3 at)
    {
        int best = -1;
        float bestScore = float.MaxValue;

        for (int i = 0; i < Nodes.Count; i++)
        {
            float score = Score(i, at);

            if (score < bestScore)
            {
                bestScore = score;
                best = i;
            }
        }

        return best;
    }

    /// <summary>
    /// The same answer, but it does not change its mind for nothing.
    ///
    /// <paramref name="previous"/> is kept unless something scores better than it by
    /// <paramref name="hysteresis"/>. Without that margin a player walking the boundary between two
    /// nodes re-snaps every time they cross it - the largest Corporate roof flips 103 times over a
    /// 1 m grid - and every flip is a different start node, a different route, and a trail that
    /// jumps. The margin costs nothing in accuracy: the two candidates are, by construction, within
    /// a few metres of each other.
    /// </summary>
    public int NearestStable(Vector3 at, int previous, float hysteresis)
    {
        int best = Nearest(at);

        if (previous < 0 || previous >= Nodes.Count || best < 0)
        {
            return best;
        }

        return Score(best, at) < Score(previous, at) - hysteresis ? best : previous;
    }

    /// <summary>
    /// The polyline a path draws through the city: where it starts, the point the player leaves
    /// each node from, and where they are going.
    ///
    /// <paramref name="moves"/> is filled in step with the points, so a caller knows what the
    /// player has to do to get from each one to the next. It is optional only because the
    /// validators and the tests do not always want it.
    /// </summary>
    public List<Vector3> Waypoints(Vector3 from, List<int> path, Vector3 target,
        List<NavMove> moves = null)
    {
        List<Vector3> points = new List<Vector3> { from };
        moves?.Clear();
        moves?.Add(NavMove.Walk);

        if (path != null)
        {
            foreach (int link in path)
            {
                Vector3 exit = Links[link].Exit;

                if ((exit - points[points.Count - 1]).sqrMagnitude > 0.25f)
                {
                    points.Add(exit);
                    moves?.Add(Links[link].Move);
                }
                else if (moves != null && moves.Count > 0)
                {
                    // Two links leaving from within half a metre of each other: the second one is
                    // the move that matters, so it replaces rather than being dropped.
                    moves[moves.Count - 1] = Links[link].Move;
                }
            }
        }

        if ((target - points[points.Count - 1]).sqrMagnitude > 0.25f)
        {
            points.Add(target);
            moves?.Add(NavMove.Walk);
        }

        return points;
    }

    /// <summary>The hardest move on a path, which is the grade the whole route reads at.</summary>
    public RouteTier WorstTier(List<int> path)
    {
        RouteTier worst = RouteTier.Green;

        if (path == null)
        {
            return worst;
        }

        foreach (int link in path)
        {
            if (Links[link].Tier > worst)
            {
                worst = Links[link].Tier;
            }
        }

        return worst;
    }

    public float Length(List<int> path)
    {
        float total = 0f;

        if (path == null)
        {
            return 0f;
        }

        foreach (int link in path)
        {
            total += (Nodes[Links[link].To].Position - Nodes[Links[link].From].Position).magnitude;
        }

        return total;
    }
}

/// <summary>
/// Builds the graph the route guidance walks, from the finished plan.
///
/// The problem it exists to solve: the Phase 6D compass points *at* the objective, and in a city of
/// solid blocks a bearing through a building is worse than no bearing at all - it tells the player
/// to go somewhere they cannot go and gives them no idea which way round. What is needed instead is
/// the route, in the world, on the ground.
///
/// Three layers, and they are the three the city already has:
///
///   <b>The street corridors.</b> The four avenue centrelines, the perimeter ring and the two plaza
///   ring streets, as lines, intersected into a lattice. That is not a simplification of the street
///   network - it is the part of it that goes anywhere. Phase 6C's
///   `Ascents_FromTheStreetNeverBlockAnythingNarrowerThanAnAlley` already guarantees that every way
///   up off the street is on a facade facing an avenue, the perimeter or an open forecourt, so the
///   corridors reach all thirteen of them by construction.
///
///   <b>The ways up.</b> Each of the thirteen street ascents gets a node on the pavement at its
///   foot, joined to the corridor by the shortest connector that does not cross a building.
///
///   <b>The rooftops.</b> `RoofGraph` unchanged - the same directed graph, with the same tier
///   grading and the same refusal to count a drop the fall rule would kill the player for.
///
/// Pure, deterministic and free of `UnityEditor`, like every other file in this folder, so the
/// route the player is shown is the route the EditMode tests measure.
/// </summary>
public static class CityNavigation
{
    /// <summary>Prefix on a corridor node's name, so it can never collide with a surface's.</summary>
    public const string StreetPrefix = "NAV_S";

    /// <summary>Prefix on the pavement node at the foot of a way up.</summary>
    public const string FootPrefix = "NAV_F:";

    /// <summary>
    /// How much more a metre of a given grade costs than a metre of walking.
    ///
    /// This is what stops the guidance from routing a player over a RED corner-to-corner diagonal
    /// because it saved eight metres. It is the same judgement `RoofGraph.Path` makes by searching
    /// tier by tier; expressed as a weight because a shortest-path search has to compare a hard
    /// short move against an easy long one rather than rule one of them out.
    /// </summary>
    public static float TierWeight(RouteTier tier)
    {
        switch (tier)
        {
            case RouteTier.Green: return 1f;
            case RouteTier.Blue: return 1.4f;
            case RouteTier.Orange: return 2.1f;
            default: return 4f;
        }
    }

    /// <summary>One line of the street corridor lattice.</summary>
    private readonly struct Corridor
    {
        /// <summary>True where the line runs north-south, i.e. its X is fixed.</summary>
        public readonly bool AlongZ;

        public readonly float Fixed;
        public readonly float Min;
        public readonly float Max;

        public Corridor(bool alongZ, float fixedCoordinate, float min, float max)
        {
            AlongZ = alongZ;
            Fixed = fixedCoordinate;
            Min = min;
            Max = max;
        }

        public Vector3 At(float along)
            => AlongZ ? new Vector3(Fixed, 0f, along) : new Vector3(along, 0f, Fixed);
    }

    public sealed class Result
    {
        public CityNavGraph Graph;

        /// <summary>Relay id (and the summit) to the node the guidance ends on.</summary>
        public readonly Dictionary<string, string> Targets = new Dictionary<string, string>();

        public readonly List<string> Problems = new List<string>();

        /// <summary>
        /// What carries the player along each link, by name - "Center West Escape", "Old Quarter
        /// Span", or just "street". Parallel with <c>Graph.Links</c> and deliberately *not*
        /// serialized into the scene: the player never needs it, and the reports and the tests
        /// that describe a route in words do.
        /// </summary>
        public readonly List<string> Via = new List<string>();

        public int StreetNodes;
        public int FootNodes;
        public int SurfaceNodes;
    }

    // ------------------------------------------------------------------ entry point

    public static Result Build(CityPlanResult plan)
    {
        Result result = new Result { Graph = new CityNavGraph() };

        List<Corridor> corridors = Corridors();
        BuildStreetLattice(plan, result, corridors);
        BuildSurfaces(plan, result);
        BuildWaysUp(plan, result, corridors);
        BuildTargets(plan, result);

        return result;
    }

    // ------------------------------------------------------------------ the street corridors

    /// <summary>
    /// The lines the guidance is allowed to walk down.
    ///
    /// Every one of them is a street the Phase 6B walkability flood fill already proved, and none
    /// of them crosses the Cut: the trench runs north-south at x ≈ -194 between z = -284 and
    /// z = -104, and no corridor line passes through that box. The clearance test below checks it
    /// anyway rather than relying on the arithmetic staying true.
    /// </summary>
    private static List<Corridor> Corridors()
    {
        List<Corridor> lines = new List<Corridor>();

        float perimeter = CityDesign.PerimeterCentre(1);
        float avenue = CityDesign.AvenueCentre(1);
        float ring = CityDesign.PlazaRingStreet;
        CityRect centre = CityDesign.Cell("CityCenter").Bounds;

        // The perimeter ring and the two avenues, each running the full width of the core.
        foreach (int sign in new[] { -1, 1 })
        {
            lines.Add(new Corridor(true, sign * perimeter, -perimeter, perimeter));
            lines.Add(new Corridor(false, sign * perimeter, -perimeter, perimeter));
            lines.Add(new Corridor(true, sign * avenue, -perimeter, perimeter));
            lines.Add(new Corridor(false, sign * avenue, -perimeter, perimeter));
        }

        // The plaza's two ring streets, extended out to the avenue centrelines at either end.
        // The plaza is enclosed on all four sides and these are the only ways off it, which is why
        // `CityPlan.SplitWithFixedCentre` pins the centre lot instead of jittering it - and it is
        // also why the run leaving the spawn has somewhere to go on the very first frame.
        foreach (int sign in new[] { -1, 1 })
        {
            lines.Add(new Corridor(true, sign * ring, -avenue, avenue));
            lines.Add(new Corridor(false, sign * ring, -avenue, avenue));

            // The two secondary streets that bound the City Center's outer lot rows, which is how
            // the plaza ring reaches the avenue without doubling back round the whole block.
            _ = centre;
        }

        return lines;
    }

    private static void BuildStreetLattice(CityPlanResult plan, Result result,
        List<Corridor> corridors)
    {
        CityNavGraph graph = result.Graph;

        // Every intersection of a north-south line with an east-west one that both lines reach.
        List<List<float>> alongs = new List<List<float>>();

        for (int i = 0; i < corridors.Count; i++)
        {
            alongs.Add(new List<float>());
        }

        // Stops along each line at a fixed step as well as at every crossing.
        //
        // Not decoration: `CityNavGraph.Nearest` snaps the player to a node, and on a bare lattice
        // the nearest node on a 190 m stretch of avenue can be 95 m behind them - which makes the
        // first leg of the trail point backwards up the street they just ran down. A stop every
        // 40 m is what keeps the trail starting in front of the player.
        for (int i = 0; i < corridors.Count; i++)
        {
            float span = corridors[i].Max - corridors[i].Min;
            int steps = Mathf.Max(1, Mathf.RoundToInt(span / CityDesign.GuideLatticeStep));

            for (int s = 0; s <= steps; s++)
            {
                alongs[i].Add(corridors[i].Min + span * s / steps);
            }
        }

        for (int i = 0; i < corridors.Count; i++)
        {
            for (int j = 0; j < corridors.Count; j++)
            {
                if (corridors[i].AlongZ == corridors[j].AlongZ)
                {
                    continue;
                }

                float alongI = corridors[j].Fixed;
                float alongJ = corridors[i].Fixed;

                if (alongI < corridors[i].Min - 0.01f || alongI > corridors[i].Max + 0.01f)
                {
                    continue;
                }

                if (alongJ < corridors[j].Min - 0.01f || alongJ > corridors[j].Max + 0.01f)
                {
                    continue;
                }

                alongs[i].Add(alongI);
            }
        }

        for (int i = 0; i < corridors.Count; i++)
        {
            List<float> stops = alongs[i];
            stops.Sort();

            int previous = -1;
            float previousAlong = 0f;

            foreach (float along in stops)
            {
                Vector3 at = corridors[i].At(along);

                if (Blocked(plan, at))
                {
                    previous = -1;
                    continue;
                }

                int node = graph.Add(NameFor(at), NavNodeKind.Street, at);

                if (previous >= 0 && !BlockedSegment(plan, corridors[i].At(previousAlong), at))
                {
                    Street(result, graph, previous, node, Mathf.Abs(along - previousAlong));
                }

                previous = node;
                previousAlong = along;
            }
        }

        foreach (NavNode node in graph.Nodes)
        {
            if (node.Kind == NavNodeKind.Street)
            {
                result.StreetNodes++;
            }
        }

        if (result.StreetNodes < 16)
        {
            result.Problems.Add($"only {result.StreetNodes} street nodes; the corridor lattice " +
                                "did not form");
        }
    }

    /// <summary>Named by position, rounded, so the same intersection is only ever one node.</summary>
    private static string NameFor(Vector3 at)
        => $"{StreetPrefix}{Mathf.RoundToInt(at.x)}_{Mathf.RoundToInt(at.z)}";

    // ------------------------------------------------------------------ the rooftops

    /// <summary>
    /// The Phase 6C roof graph, unchanged, lifted into the nav graph.
    ///
    /// Nothing is re-derived here: the edges, the tiers and the refusal to count a fatal drop are
    /// all `RoofGraph`'s, so the guidance can never suggest a move the traversal layer does not
    /// believe in. All this adds is a position for each node and a cost for each edge.
    /// </summary>
    private static void BuildSurfaces(CityPlanResult plan, Result result)
    {
        CityNavGraph graph = result.Graph;
        RoofGraph roofs = RoofGraph.Build(plan);
        CityTraversalResult traversal = plan.Traversal;

        foreach (string node in roofs.Nodes)
        {
            if (!traversal.Surfaces.TryGetValue(node, out TraversalSurface surface))
            {
                result.Problems.Add($"{node} is in the roof graph but has no surface");
                continue;
            }

            graph.Add(node, NavNodeKind.Surface, surface.Centre,
                new Vector3(surface.Footprint.Width * 0.5f, 0f, surface.Footprint.Depth * 0.5f));
            result.SurfaceNodes++;
        }

        foreach (string node in roofs.Nodes)
        {
            int from = graph.IndexOf(node);

            if (from < 0)
            {
                continue;
            }

            TraversalSurface a = traversal.Surfaces[node];

            foreach (RoofEdge edge in roofs.From(node))
            {
                int to = graph.IndexOf(edge.To);

                if (to < 0)
                {
                    continue;
                }

                TraversalSurface b = traversal.Surfaces[edge.To];

                // Where the player leaves this surface: the point on it closest to where they are
                // going, pulled in from the lip so the marker sits on the roof and not over the
                // drop. A roof smaller than twice the inset keeps its centre instead.
                Vector3 exit = EdgePoint(a, b.Centre);

                float distance = (b.Centre - a.Centre).magnitude;
                float cost = (distance + CityDesign.GuideMovePenalty) * TierWeight(edge.Tier);

                Record(result, graph, from, to, cost, exit, edge.Tier,
                    MoveFor(plan, edge, a.SurfaceY, b.SurfaceY), edge.Via);
            }
        }
    }

    /// <summary>
    /// Connects two nodes and records what carries the player along it, in words.
    ///
    /// Every link in the graph goes through here or through <c>ConnectBoth</c>, so <c>Result.Via</c>
    /// stays exactly parallel with <c>Graph.Links</c> - which is what lets a route be described in
    /// English rather than as a list of node ids.
    /// </summary>
    private static void Record(Result result, CityNavGraph graph, int from, int to, float cost,
        Vector3 exit, RouteTier tier, NavMove move, string via)
    {
        int before = graph.Links.Count;
        graph.Connect(from, to, cost, exit, tier, move);

        for (int i = before; i < graph.Links.Count; i++)
        {
            result.Via.Add(via);
        }
    }

    /// <summary>
    /// What a rooftop edge actually is, from what the roof graph says carries the player along it.
    ///
    /// `RoofGraph` already distinguishes them and throws the distinction away at the boundary: an
    /// edge's <c>Via</c> is the literal string "jump", or the name of the link whose deck it steps
    /// onto, or the name of the ascent it climbs. That is exactly the three answers the guidance
    /// needs, so it is read back rather than re-derived from the geometry.
    /// </summary>
    private static NavMove MoveFor(CityPlanResult plan, in RoofEdge edge, float fromY, float toY)
    {
        if (edge.Via == "jump")
        {
            return NavMove.Jump;
        }

        if (plan.Traversal.Link(edge.Via) != null)
        {
            return NavMove.Cross;
        }

        if (CityTraversal.Ascent(plan.Traversal, edge.Via) != null)
        {
            return toY > fromY + 0.01f ? NavMove.Climb : NavMove.Descend;
        }

        return NavMove.Jump;
    }

    /// <summary>
    /// The point on a surface nearest a target, inset from the edge. This is the whole of why the
    /// breadcrumb trail bends round corners instead of cutting them.
    /// </summary>
    public static Vector3 EdgePoint(in TraversalSurface surface, Vector3 towards)
    {
        CityRect f = surface.Footprint;
        float inset = CityDesign.GuideEdgeInset;

        float minX = f.MinX + inset;
        float maxX = f.MaxX - inset;
        float minZ = f.MinZ + inset;
        float maxZ = f.MaxZ - inset;

        float x = minX <= maxX ? Mathf.Clamp(towards.x, minX, maxX) : f.CentreX;
        float z = minZ <= maxZ ? Mathf.Clamp(towards.z, minZ, maxZ) : f.CentreZ;

        return new Vector3(x, surface.SurfaceY, z);
    }

    // ------------------------------------------------------------------ the ways up

    private static void BuildWaysUp(CityPlanResult plan, Result result, List<Corridor> corridors)
    {
        CityNavGraph graph = result.Graph;

        foreach (AscentPlan ascent in plan.Traversal.StreetAscents())
        {
            if (ascent.Landings.Count == 0)
            {
                result.Problems.Add($"{ascent.Name} has no ledges to stand under");
                continue;
            }

            // The pavement in front of the stack, not under it: standing under a fire escape is
            // standing inside the geometry, and a marker there would be swallowed by it.
            CityRect first = ascent.Landings[0];
            Vector3 away = new Vector3(first.CentreX - ascent.TopFootprint.CentreX, 0f,
                first.CentreZ - ascent.TopFootprint.CentreZ);

            away = away.sqrMagnitude < 0.0001f ? Vector3.forward : away.normalized;

            Vector3 foot = new Vector3(first.CentreX, 0f, first.CentreZ)
                           + away * CityDesign.GuideFootStandoff;

            int footNode = graph.Add(FootPrefix + ascent.Name, NavNodeKind.AscentFoot, foot,
                new Vector3(CityDesign.GuideFootStandoff, 0f, CityDesign.GuideFootStandoff));
            result.FootNodes++;

            int top = graph.IndexOf(ascent.TopNode);

            if (top < 0)
            {
                result.Problems.Add($"{ascent.Name} tops out on {ascent.TopNode}, which is not a " +
                                    "surface the nav graph carries");
                continue;
            }

            RouteTier tier = RoofGraph.WorstStep(ascent);
            float climb = ascent.Rise * CityDesign.GuideClimbWeight;

            Record(result, graph, footNode, top,
                (climb + CityDesign.GuideMovePenalty) * TierWeight(tier), foot, tier,
                NavMove.Climb, ascent.Name);

            // And back down, which is always a walk down a stair whatever it cost to come up.
            Record(result, graph, top, footNode, climb + CityDesign.GuideMovePenalty,
                EdgePoint(plan.Traversal.Surfaces[ascent.TopNode], foot), RouteTier.Green,
                NavMove.Descend, ascent.Name);

            JoinToCorridor(plan, result, corridors, footNode, ascent.Name);
        }
    }

    /// <summary>
    /// Joins a pavement node to the corridor lattice by the shortest connector that does not cross
    /// a building.
    ///
    /// Every candidate is a perpendicular onto one of the corridor lines, which keeps the connector
    /// axis-aligned and therefore down a street rather than diagonally across a forecourt. A foot
    /// that cannot reach any line is a real problem and is reported rather than wired up anyway -
    /// guidance that walks the player through a wall is worse than none.
    /// </summary>
    private static void JoinToCorridor(CityPlanResult plan, Result result, List<Corridor> corridors,
        int footNode, string owner)
    {
        CityNavGraph graph = result.Graph;
        Vector3 foot = graph.Nodes[footNode].Position;

        float bestDistance = float.MaxValue;
        Vector3 bestPoint = Vector3.zero;
        bool found = false;

        foreach (Corridor corridor in corridors)
        {
            float along = corridor.AlongZ ? foot.z : foot.x;

            if (along < corridor.Min || along > corridor.Max)
            {
                continue;
            }

            Vector3 point = corridor.At(along);
            float distance = (point - foot).magnitude;

            if (distance >= bestDistance || Blocked(plan, point) || BlockedSegment(plan, foot, point))
            {
                continue;
            }

            bestDistance = distance;
            bestPoint = point;
            found = true;
        }

        if (!found)
        {
            result.Problems.Add($"{owner}: no corridor is reachable from its foot without crossing " +
                                "a building");
            return;
        }

        int node = graph.Add(NameFor(bestPoint), NavNodeKind.Street, bestPoint);

        // The projected point usually lands between two lattice nodes, so it has to be spliced into
        // the line rather than left dangling: joined to the nearest node on each side of it along
        // the same corridor.
        SpliceIntoLattice(plan, result, graph, node, bestPoint);
        Street(result, graph, footNode, node, bestDistance);
    }

    /// <summary>
    /// Joins a point dropped onto a corridor to the nodes already on that corridor, on both sides.
    /// Only ever along the shared axis, so nothing here can invent a diagonal through a block.
    /// </summary>
    /// <summary>A two-way walk along a street, recorded as one.</summary>
    private static void Street(Result result, CityNavGraph graph, int a, int b, float cost)
    {
        int before = graph.Links.Count;
        graph.ConnectBoth(a, b, cost, RouteTier.Green);

        for (int i = before; i < graph.Links.Count; i++)
        {
            result.Via.Add("street");
        }
    }

    private static void SpliceIntoLattice(CityPlanResult plan, Result result, CityNavGraph graph,
        int node, Vector3 at)
    {
        for (int axis = 0; axis < 2; axis++)
        {
            bool alongZ = axis == 0;

            int lower = -1;
            int higher = -1;
            float lowerGap = float.MaxValue;
            float higherGap = float.MaxValue;

            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                if (i == node || graph.Nodes[i].Kind != NavNodeKind.Street)
                {
                    continue;
                }

                Vector3 other = graph.Nodes[i].Position;
                float across = alongZ ? Mathf.Abs(other.x - at.x) : Mathf.Abs(other.z - at.z);

                if (across > 0.5f)
                {
                    continue;
                }

                float delta = alongZ ? other.z - at.z : other.x - at.x;

                if (Mathf.Abs(delta) < 0.5f || BlockedSegment(plan, at, other))
                {
                    continue;
                }

                if (delta < 0f && -delta < lowerGap)
                {
                    lowerGap = -delta;
                    lower = i;
                }
                else if (delta > 0f && delta < higherGap)
                {
                    higherGap = delta;
                    higher = i;
                }
            }

            if (lower >= 0)
            {
                Street(result, graph, node, lower, lowerGap);
            }

            if (higher >= 0)
            {
                Street(result, graph, node, higher, higherGap);
            }
        }
    }

    // ------------------------------------------------------------------ targets

    private static void BuildTargets(CityPlanResult plan, Result result)
    {
        foreach (RelayObjective relay in plan.Objectives.Relays)
        {
            if (!result.Graph.Has(relay.Node))
            {
                result.Problems.Add($"{relay.Name} stands on {relay.Node}, which the nav graph " +
                                    "does not carry");
                continue;
            }

            result.Targets[relay.Name] = relay.Node;
        }

        if (result.Graph.Has(CityTraversal.ShaftRoofNode))
        {
            result.Targets[CityObjectives.SummitName] = CityTraversal.ShaftRoofNode;
        }
        else
        {
            result.Problems.Add("the summit is not a node in the nav graph");
        }
    }

    // ------------------------------------------------------------------ describing a route

    /// <summary>
    /// A route, in English.
    ///
    /// It exists because "follow the chevrons" is not an answer to "how am I supposed to get there",
    /// and because a route that cannot be written down as a sequence of moves a player could
    /// perform is a route that has not really been checked. Every line names the surface, the
    /// direction, the distance, the height change and - where the move is not a run - the piece of
    /// Phase 6C geometry that carries it, by the name it has in `CityTraversal`.
    /// </summary>
    public static List<string> Describe(CityPlanResult plan, Result nav, List<int> path)
    {
        List<string> lines = new List<string>();

        if (path == null)
        {
            lines.Add("no route");
            return lines;
        }

        CityNavGraph graph = nav.Graph;

        if (path.Count == 0)
        {
            lines.Add("already there");
            return lines;
        }

        lines.Add("START: " + Place(plan, graph, graph.Links[path[0]].From));

        for (int i = 0; i < path.Count; i++)
        {
            NavLink link = graph.Links[path[i]];
            string via = path[i] < nav.Via.Count ? nav.Via[path[i]] : "?";

            Vector3 from = graph.Nodes[link.From].Position;
            Vector3 to = graph.Nodes[link.To].Position;
            Vector3 delta = to - from;
            float flat = new Vector2(delta.x, delta.z).magnitude;
            string heading = Compass(delta);
            string destination = Place(plan, graph, link.To);

            switch (link.Move)
            {
                case NavMove.Climb:
                    lines.Add($"  CLIMB  {Ascent(plan, via)} on the {heading} side "
                              + $"(+{delta.y:F1} m) -> {destination}");
                    break;

                case NavMove.Descend:
                    lines.Add($"  DOWN   {Ascent(plan, via)} ({delta.y:F1} m) -> {destination}");
                    break;

                case NavMove.Cross:
                    lines.Add($"  CROSS  {via} - {Deck(plan, via)} - heading {heading} "
                              + $"({flat:F0} m, {delta.y:+0.0;-0.0;0.0} m) -> {destination}");
                    break;

                case NavMove.Jump:
                    lines.Add($"  JUMP   {heading} across a {Gap(plan, graph, link):F1} m gap "
                              + $"({delta.y:+0.0;-0.0;0.0} m, {link.Tier}) -> {destination}");
                    break;

                default:
                    lines.Add($"  RUN    {flat:F0} m {heading} -> {destination}");
                    break;
            }
        }

        return lines;
    }

    /// <summary>Clear distance between the two surfaces a jump crosses, or 0 on the ground.</summary>
    private static float Gap(CityPlanResult plan, CityNavGraph graph, in NavLink link)
    {
        string from = graph.Nodes[link.From].Name;
        string to = graph.Nodes[link.To].Name;

        return plan.Traversal.Surfaces.TryGetValue(from, out TraversalSurface a)
               && plan.Traversal.Surfaces.TryGetValue(to, out TraversalSurface b)
            ? a.Footprint.GapTo(b.Footprint)
            : 0f;
    }

    private static string Ascent(CityPlanResult plan, string name)
    {
        AscentPlan ascent = CityTraversal.Ascent(plan.Traversal, name);

        if (ascent == null)
        {
            return name;
        }

        string kind;

        switch (ascent.Kind)
        {
            case AscentKind.FireEscape: kind = "fire escape"; break;
            case AscentKind.Scaffold: kind = "scaffold"; break;
            case AscentKind.Riser: kind = "roof riser"; break;
            case AscentKind.LinkStair: kind = "link stair"; break;
            default: kind = "tower spiral"; break;
        }

        if (ascent.Style == AscentTraversalStyle.Ramp)
        {
            return $"the {name} ({kind}, {ascent.Rise:F0} m at " +
                   $"{ascent.PitchDegrees:F0} degrees)";
        }

        string flights = ascent.Flights.Count == 1 ? "stair flight" : "stair flights";
        return $"the {name} ({kind}, {ascent.Flights.Count} {flights}, " +
               $"{ascent.StepCount} steps with {ascent.StepRise:F2} m risers)";
    }

    private static string Deck(CityPlanResult plan, string name)
    {
        LinkPlan link = plan.Traversal.Link(name);

        if (link == null)
        {
            return "deck";
        }

        return link.Kind == LinkKind.Crane
            ? $"the crane jib, {link.DeckWidth:F1} m wide at {link.DeckY:F0} m"
            : $"a {link.DeckWidth:F1} m skybridge at {link.DeckY:F0} m";
    }

    /// <summary>Where a node is, in words a player could act on.</summary>
    public static string Place(CityPlanResult plan, CityNavGraph graph, int node)
    {
        NavNode n = graph.Nodes[node];

        if (n.Kind == NavNodeKind.Street)
        {
            return $"street level at ({n.Position.x:F0}, {n.Position.z:F0})";
        }

        if (n.Kind == NavNodeKind.AscentFoot)
        {
            return $"the pavement at the foot of {n.Name.Substring(FootPrefix.Length)}";
        }

        foreach (BuildingPlan building in plan.Buildings)
        {
            if (building.Name != n.Name)
            {
                continue;
            }

            DistrictCell cell = CityDesign.Cell(building.CellName);

            return $"the {building.RoofY:F1} m roof of {Readable(cell.Name)} "
                   + $"lot [{building.LotColumn},{building.LotRow}] "
                   + $"({building.Storeys} storeys, at x {building.Footprint.CentreX:F0}, "
                   + $"z {building.Footprint.CentreZ:F0})";
        }

        if (n.Name.StartsWith("Deck_"))
        {
            return $"the {n.Name.Substring(5).Replace('_', ' ')} deck at {n.Position.y:F0} m";
        }

        switch (n.Name)
        {
            case CityTraversal.PodiumNode: return "the tower podium roof at 25.2 m";
            case CityTraversal.WingNorthNode: return "the tower's north podium wing";
            case CityTraversal.WingWestNode: return "the tower's west podium wing";
            case CityTraversal.ShaftRoofNode: return "the tower shaft roof at 105 m - THE SUMMIT";
            default: return n.Name;
        }
    }

    private static string Readable(string cellName)
    {
        switch (cellName)
        {
            case "ResidentialNorth": return "Residential North";
            case "ResidentialWest": return "Residential West";
            case "IndustrialYards": return "the Industrial Yards";
            case "IndustrialConstruction": return "the Industrial Construction site";
            case "CityCenter": return "the City Center";
            case "CorporateCore": return "the Corporate Core";
            case "CorporateSouth": return "Corporate South";
            case "OldQuarter": return "the Old Quarter";
            default: return cellName;
        }
    }

    /// <summary>Eight-point compass bearing of a horizontal delta.</summary>
    public static string Compass(Vector3 delta)
    {
        float angle = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;

        if (angle < 0f)
        {
            angle += 360f;
        }

        string[] points = { "north", "north-east", "east", "south-east", "south", "south-west",
            "west", "north-west" };

        return points[Mathf.RoundToInt(angle / 45f) % 8];
    }

    // ------------------------------------------------------------------ clearance

    /// <summary>Is this point inside something a player cannot stand in?</summary>
    public static bool Blocked(CityPlanResult plan, Vector3 at)
    {
        foreach (BuildingPlan building in plan.Buildings)
        {
            if (building.Footprint.Inset(-CityDesign.GuideClearance).Contains(at.x, at.z))
            {
                return true;
            }
        }

        foreach (BlockPlan block in plan.Blocks)
        {
            if (block.Collidable && block.Kind == CityPieceKind.Landmark
                                && block.Footprint.Inset(-CityDesign.GuideClearance)
                                    .Contains(at.x, at.z))
            {
                return true;
            }
        }

        return CityPlan.CutBounds().Contains(at.x, at.z);
    }

    /// <summary>
    /// Does a street-level segment pass through anything solid?
    ///
    /// This is the test that makes the corridor lattice honest. Without it the guidance would be a
    /// straight line drawn on a graph that happened to have street-shaped node names.
    /// </summary>
    public static bool BlockedSegment(CityPlanResult plan, Vector3 a, Vector3 b)
    {
        foreach (BuildingPlan building in plan.Buildings)
        {
            if (SegmentHitsRect(a, b, building.Footprint.Inset(-CityDesign.GuideClearance)))
            {
                return true;
            }
        }

        foreach (BlockPlan block in plan.Blocks)
        {
            if (!block.Collidable || block.Kind != CityPieceKind.Landmark)
            {
                continue;
            }

            if (SegmentHitsRect(a, b, block.Footprint.Inset(-CityDesign.GuideClearance)))
            {
                return true;
            }
        }

        // The Cut is crossed at its two bridges and nowhere else.
        CityRect cut = CityPlan.CutBounds();

        if (!SegmentHitsRect(a, b, cut))
        {
            return false;
        }

        foreach (SlabPlan slab in plan.Slabs)
        {
            if (slab.GroupName == "THE_CUT" && slab.Name.Contains("Bridge")
                                            && SegmentInsideRect(a, b, slab.Footprint))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Slab method, in 2D. A segment that starts or ends inside the rect counts as a hit.</summary>
    public static bool SegmentHitsRect(Vector3 a, Vector3 b, in CityRect rect)
    {
        if (rect.Contains(a.x, a.z) || rect.Contains(b.x, b.z))
        {
            return true;
        }

        float dx = b.x - a.x;
        float dz = b.z - a.z;
        float enter = 0f;
        float exit = 1f;

        if (!Slab(a.x, dx, rect.MinX, rect.MaxX, ref enter, ref exit))
        {
            return false;
        }

        return Slab(a.z, dz, rect.MinZ, rect.MaxZ, ref enter, ref exit);
    }

    private static bool Slab(float origin, float delta, float min, float max, ref float enter,
        ref float exit)
    {
        if (Mathf.Abs(delta) < 0.0001f)
        {
            return origin >= min && origin <= max;
        }

        float t0 = (min - origin) / delta;
        float t1 = (max - origin) / delta;

        if (t0 > t1)
        {
            (t0, t1) = (t1, t0);
        }

        enter = Mathf.Max(enter, t0);
        exit = Mathf.Min(exit, t1);
        return enter <= exit;
    }

    private static bool SegmentInsideRect(Vector3 a, Vector3 b, in CityRect rect)
        => rect.Contains(a.x, a.z) && rect.Contains(b.x, b.z);

    // ------------------------------------------------------------------ breadcrumbs

    /// <summary>
    /// Turns a route polyline into the markers that get placed in the world.
    ///
    /// Four rules, and each of them exists because leaving it out produced a visible fault.
    ///
    ///   <b>Every marker is anchored to the route, not to the player.</b> Its position is a fixed
    ///   arc length along the polyline, so as long as the route is the same the chevrons occupy the
    ///   same square metres of the city frame after frame. Resampling from wherever the player
    ///   happened to be standing made the whole trail slide by up to a spacing every time the route
    ///   was searched again, which is 7 m of everything moving at once.
    ///
    ///   <b>Every corner of the polyline is kept</b>, whatever the spacing says, because a turn a
    ///   player cannot see coming is a turn they miss, and an even resample slides the marker past
    ///   the junction.
    ///
    ///   <b>Every marker knows what the player has to do next.</b> The move carried by the segment
    ///   it sits on is what lets the guide put an upright marker at the foot of the fire escape
    ///   rather than another arrow pointing at it.
    ///
    ///   <b>No two markers share a patch of ground.</b> The first three rules pull against each
    ///   other: a corner is kept whatever the spacing says, and a resampled marker may already be
    ///   standing on it. 47 of this city's 862 chevrons landed within 1.5 m of the one before, the
    ///   closest 0.22 m apart - two flat chevrons at the same height, on the same plane, pointing
    ///   almost the same way. That is a z-fight, and a z-fight is two surfaces swapping which one
    ///   is in front as the camera turns, which is what a player standing still at the top of a
    ///   fire escape was watching flicker. Where two would collide the transition wins, because it
    ///   is the one carrying the next move.
    ///
    /// The whole route is laid first and the window applied afterwards, which is what makes the
    /// fourth rule safe: which markers survive a collision is decided by the route alone, so it
    /// cannot depend on where the player had got to when the trail was last laid. Resolving it
    /// against the markers already emitted would have made the trail's contents a function of
    /// <paramref name="fromAlong"/>, and the whole point of the first rule is that they are not.
    ///
    /// The trail is cut off at <see cref="CityDesign.GuideVisibleRange"/> from
    /// <paramref name="fromAlong"/>: past that it is not guidance, it is clutter in front of the
    /// thing the player is trying to look at.
    /// </summary>
    public static List<Breadcrumb> Breadcrumbs(List<Vector3> polyline, List<NavMove> moves,
        int maxMarkers, float fromAlong = 0f)
    {
        List<Breadcrumb> markers = new List<Breadcrumb>();
        Breadcrumbs(polyline, moves, maxMarkers, fromAlong, markers);
        return markers;
    }

    /// <summary>
    /// The same, filling a list the caller owns.
    ///
    /// The guide re-lays its trail as the player runs, and a route that allocated a fresh list
    /// every time it did would put the guidance in the collector's path for no reason.
    /// </summary>
    public static void Breadcrumbs(List<Vector3> polyline, List<NavMove> moves, int maxMarkers,
        float fromAlong, List<Breadcrumb> into)
    {
        into.Clear();

        if (polyline == null || polyline.Count < 2 || maxMarkers <= 0)
        {
            return;
        }

        float ceiling = fromAlong + CityDesign.GuideVisibleRange;

        foreach (Breadcrumb crumb in Lay(polyline, moves))
        {
            if (crumb.Along < fromAlong || crumb.Along > ceiling)
            {
                continue;
            }

            if (into.Count >= maxMarkers)
            {
                break;
            }

            into.Add(crumb);
        }
    }

    /// <summary>
    /// Every marker the whole route wants, in arc order, with collisions resolved and no window
    /// applied at all, filling a list the caller owns.
    ///
    /// This is what <see cref="RouteTrail"/> holds. The windowed <see cref="Breadcrumbs"/> above
    /// is a view of it, and the guide used to re-derive that view as the player ran - which meant
    /// the set of markers in existence was a function of where the player had got to, and a marker
    /// was therefore a thing that came into being rather than a thing that was already there. Laid
    /// once per route, a marker is an object with a lifetime, and the pool can bind to it.
    /// </summary>
    public static void LayRoute(List<Vector3> polyline, List<NavMove> moves, List<Breadcrumb> into)
    {
        into.Clear();

        if (polyline == null || polyline.Count < 2)
        {
            return;
        }

        into.AddRange(Lay(polyline, moves));
    }

    /// <summary>
    /// Every marker the whole route wants, in arc order, with collisions already resolved.
    ///
    /// Independent of the player and of the visible window, which is the property the trail's
    /// stability rests on: two views of the same route are the same markers, filtered differently.
    ///
    /// The resample is phased so that markers land at whole multiples of the spacing measured
    /// <b>backwards from the objective</b> rather than forwards from the start. That is not a
    /// cosmetic choice. The start of the route is the node the player is standing on, so it moves
    /// whenever they step across a rooftop boundary - and with a forward phase every marker in the
    /// city then slid by up to a spacing, including the ones out on a stretch of route that both
    /// searches completely agreed about. Measured from the end, a shared stretch is the same
    /// distance from the objective in both searches, so it gets the same markers in the same square
    /// metres and a re-search is invisible everywhere except where the route really did change.
    /// </summary>
    private static List<Breadcrumb> Lay(List<Vector3> polyline, List<NavMove> moves)
    {
        List<Breadcrumb> laid = new List<Breadcrumb>();
        float spacing = CityDesign.GuideBreadcrumbSpacing;
        float travelled = 0f;

        float total = 0f;

        for (int i = 0; i < polyline.Count - 1; i++)
        {
            total += (polyline[i + 1] - polyline[i]).magnitude;
        }

        // The phase that puts every marker a whole number of spacings back from the objective.
        float phase = total - Mathf.Floor(total / spacing) * spacing;

        if (phase <= 0.001f)
        {
            phase = spacing;
        }

        // The last one is a whole spacing short of the objective, not on it. Two reasons, and the
        // second is the one that bit: a chevron laid at the destination stands inside the beacon on
        // the relay pad the player is running at, and - because it sits exactly at the end of the
        // arc - whether the resample emitted it at all came down to a few ULPs of drift in the
        // accumulator. Two routes to the same relay accumulate that drift differently, so one drew
        // a marker there and the other did not, and Mono and RyuJIT disagreed about which. It is
        // the only marker in the city whose existence was a rounding question.
        float ceiling = total - spacing * 0.5f;

        // Counted rather than accumulated, so a 600 m route's last marker is still exactly a
        // multiple of the spacing from the objective instead of ninety additions away from one.
        int tick = 0;
        float nextAt = phase;

        // The last horizontal heading the route actually had. A climb goes straight up, and a
        // marker whose heading came out vertical used to fall back to whatever rotation the pool
        // object drawing it happened to be left in - so one spot on the ground faced a different
        // way depending on which chevron was currently standing there.
        Vector3 heading = Flat(polyline[1] - polyline[0], Vector3.forward);

        for (int i = 0; i < polyline.Count - 1; i++)
        {
            Vector3 from = polyline[i];
            Vector3 to = polyline[i + 1];
            Vector3 step = to - from;
            float length = step.magnitude;

            if (length < 0.001f)
            {
                continue;
            }

            heading = Flat(step, heading);

            // The move that gets the player off the *end* of this segment, which is what a marker
            // standing on it has to announce.
            NavMove move = moves != null && i + 1 < moves.Count ? moves[i + 1] : NavMove.Walk;

            while (nextAt <= travelled + length && nextAt < ceiling)
            {
                // The counter advances whether or not the marker survives. That is the whole of
                // what anchors the trail: a marker's arc position is a multiple of the spacing
                // measured from the start of the *route*, so the window the player sees can slide
                // without any of them moving. The first version advanced this counter only for
                // markers it emitted and reset it at every corner, which meant the same route drew
                // its chevrons in different places depending on where the search had begun - all
                // twenty-six of them sliding up to 7 m at once, every time the route was re-found.
                laid.Add(new Breadcrumb(Vector3.Lerp(from, to, (nextAt - travelled) / length),
                    heading, nextAt, move, false, total - nextAt));

                tick++;
                nextAt = phase + tick * spacing;
            }

            travelled += length;

            if (i + 1 >= polyline.Count - 1)
            {
                continue;
            }

            // A vertex of the route. Two reasons to put a marker on one, and they are different:
            //
            //   * it is a turn, and a turn a player cannot see coming is a turn they miss. Skipped
            //     when a resampled marker has already landed close enough to serve.
            //   * the player has to *do* something there - climb, jump, cross. That one is never
            //     skipped, whatever else is nearby, because it is the only thing on the trail that
            //     says what the next action is rather than merely which way it is.
            // Asked as a distance from the objective, which is the axis the resample is phased on.
            // Asked about the distance from the *start* instead, this answered differently for two
            // routes that agreed about this corner - and asked as `travelled - phase` it was the
            // same number arrived at by subtracting two large ones, so it answered differently
            // again between runtimes.
            float fromEnd = total - travelled;
            bool nearAMultiple = Mathf.Abs(fromEnd - Mathf.Round(fromEnd / spacing) * spacing)
                                 < spacing * 0.4f;

            if (move == NavMove.Walk && nearAMultiple)
            {
                continue;
            }

            laid.Add(new Breadcrumb(to, Flat(polyline[i + 2] - to, heading), travelled, move,
                true, total - travelled));
        }

        return Resolve(laid);
    }

    /// <summary>
    /// Drops any marker that would be standing on its neighbour, working <b>backwards from the
    /// objective</b>.
    ///
    /// The rule itself is unchanged and is worth restating: two markers within
    /// <see cref="CityDesign.GuideMarkerClearGap"/> of each other share pixels, which is a z-fight,
    /// which is two surfaces swapping which one is in front as the camera turns. Where two collide
    /// the transition survives - it is the one carrying the next move, and an upright marker stands
    /// on it - and a plain resampled chevron gives way, because the run it is measuring out is
    /// drawn either side of it anyway.
    ///
    /// The <i>direction</i> is the fix. Resolved forwards, a marker's survival depends on the
    /// marker before it, so it depends transitively on the whole head of the route - and the head
    /// of the route is whatever node the player happened to be standing on when the search ran.
    /// Two routes that agree completely about a stretch of city could therefore disagree about one
    /// marker on it, right at the junction where they merge, and the disagreement could propagate
    /// forwards from there. It was a borderline `>=` on a float, so it also decided differently
    /// under Mono and under RyuJIT, which is why it passed offline and failed in the editor.
    ///
    /// Resolved from the objective end, a marker's survival depends only on markers between it and
    /// the objective - which two merged routes share by construction - so a shared stretch is laid
    /// identically whichever search produced it, exactly and on every runtime.
    /// </summary>
    private static List<Breadcrumb> Resolve(List<Breadcrumb> laid)
    {
        List<Breadcrumb> kept = new List<Breadcrumb>();

        for (int i = laid.Count - 1; i >= 0; i--)
        {
            Breadcrumb crumb = laid[i];

            if (kept.Count == 0)
            {
                kept.Add(crumb);
                continue;
            }

            Breadcrumb nearer = kept[kept.Count - 1];

            if ((nearer.Position - crumb.Position).magnitude >= CityDesign.GuideMarkerClearGap)
            {
                kept.Add(crumb);
                continue;
            }

            // They collide. The transition wins; if both are, the one nearer the objective is the
            // one the player reaches second and needs to still be there, so it stays.
            if (crumb.IsTransition && !nearer.IsTransition)
            {
                kept[kept.Count - 1] = crumb;
            }
        }

        kept.Reverse();
        return kept;
    }

    /// <summary>
    /// The horizontal part of a direction, or <paramref name="fallback"/> where there is not one.
    ///
    /// A chevron lies flat on the ground and can only say which way round it is. Asking it to point
    /// up a fire escape gives it no heading at all, and the answer to that has to come from the
    /// route - the leg the player arrived on - rather than from whichever pool object is drawing
    /// it.
    /// </summary>
    private static Vector3 Flat(Vector3 direction, Vector3 fallback)
    {
        direction.y = 0f;
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : fallback;
    }

    /// <summary>
    /// How far along a polyline a player at <paramref name="at"/> has got.
    ///
    /// Searched only inside a window around <paramref name="fromAlong"/>, and that is the point: a
    /// global closest-point search jumps wherever a route passes near itself - two legs of a
    /// switchback, an avenue crossed twice on the way out and back - and a jump in the arc position
    /// is the entire visible trail changing which of its markers are showing, in one frame. Windowed
    /// it cannot teleport, so the reading is continuous and so is the trail.
    /// </summary>
    public static float Advance(List<Vector3> polyline, Vector3 at, float fromAlong, float window)
    {
        if (polyline == null || polyline.Count < 2)
        {
            return fromAlong;
        }

        float travelled = 0f;
        float best = fromAlong;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < polyline.Count - 1; i++)
        {
            Vector3 from = polyline[i];
            Vector3 step = polyline[i + 1] - from;
            float length = step.magnitude;

            if (length < 0.001f)
            {
                continue;
            }

            float segmentStart = travelled;
            travelled += length;

            if (travelled < fromAlong - window || segmentStart > fromAlong + window)
            {
                continue;
            }

            float t = Mathf.Clamp01(Vector3.Dot(at - from, step) / (length * length));
            Vector3 closest = from + step * t;
            float distance = (closest - at).sqrMagnitude;

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = segmentStart + length * t;
            }
        }

        // Never backwards. A player who steps back off a ledge has not un-run the route, and a
        // trail that grew back towards them would read as the guidance changing its mind.
        return Mathf.Max(fromAlong, best);
    }
}

/// <summary>
/// Which objective the mission is pointing at, as a pure rule.
///
/// Lifted out of <see cref="ObjectiveTracker"/> so it can be measured without a scene, because the
/// fault it fixes is only visible over a walk: "the nearest uncaptured relay" is the right rule and
/// evaluating it fresh every frame is the wrong way to apply it. On the line where two relays are
/// equidistant - and 113 of 5041 sampled street positions sit within 3 m of such a line - the
/// answer alternates frame by frame, the HUD flashes between two district names, and the route
/// guide re-searches the whole city each time.
///
/// The rule is unchanged. It is just no longer asked to choose between two things that are the same
/// distance away: whatever it chose last time stays chosen until something is clearly nearer.
/// </summary>
public static class ObjectiveFocus
{
    /// <summary>
    /// The index of the objective to point at, or -1 if there are none left.
    ///
    /// <paramref name="current"/> is what was chosen last time, or -1 the first time. It is kept
    /// unless it has been taken, or another one is <paramref name="stickiness"/> metres nearer.
    /// </summary>
    public static int Choose(IReadOnlyList<Vector3> positions, IReadOnlyList<bool> available,
        Vector3 from, int current, float stickiness)
    {
        int best = -1;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < positions.Count; i++)
        {
            if (i >= available.Count || !available[i])
            {
                continue;
            }

            Vector3 delta = positions[i] - from;
            delta.y = 0f;
            float distance = delta.magnitude;

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        if (best < 0)
        {
            return -1;
        }

        bool currentStillValid = current >= 0 && current < positions.Count
                                 && current < available.Count && available[current];

        if (!currentStillValid)
        {
            return best;
        }

        Vector3 held = positions[current] - from;
        held.y = 0f;

        return bestDistance < held.magnitude - stickiness ? best : current;
    }
}
