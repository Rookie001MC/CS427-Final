using System.Collections.Generic;
using UnityEngine;

/// <summary>One directed move between two surfaces, and what it costs.</summary>
public readonly struct RoofEdge
{
    public readonly string To;
    public readonly RouteTier Tier;

    /// <summary>What carries the player along it: "jump", a link name, or an ascent name.</summary>
    public readonly string Via;

    public RoofEdge(string to, RouteTier tier, string via)
    {
        To = to;
        Tier = tier;
        Via = via;
    }
}

/// <summary>A way into the rooftop network from the pavement.</summary>
public readonly struct RoofEntry
{
    public readonly string Name;
    public readonly string Node;

    public RoofEntry(string name, string node)
    {
        Name = name;
        Node = node;
    }
}

/// <summary>
/// The rooftop network as a directed graph, and the questions Phase 6C has to answer about it.
///
/// Directed, because reachability is not symmetric: a 4 m alley with a 5 m rise on the far side is
/// a one-way drop, and the whole point of the city's vertical grammar is that height has to be
/// earned. Every claim this phase makes about how many ways there are onto a relay is a
/// reachability query on this graph, so the claim is measured rather than asserted.
///
/// Pure, and in the runtime assembly, so the EditMode tests can ask it the same questions
/// `RouteTierValidator` does without opening a scene.
/// </summary>
public sealed class RoofGraph
{
    private readonly Dictionary<string, List<RoofEdge>> edges =
        new Dictionary<string, List<RoofEdge>>();

    private readonly List<string> nodes = new List<string>();

    public IReadOnlyList<string> Nodes => nodes;

    public readonly List<RoofEntry> Entries = new List<RoofEntry>();

    public IReadOnlyList<RoofEdge> From(string node)
        => edges.TryGetValue(node, out List<RoofEdge> list) ? list : NoEdges;

    private static readonly List<RoofEdge> NoEdges = new List<RoofEdge>();

    // ------------------------------------------------------------------ construction

    /// <summary>
    /// Builds the graph from a finished plan.
    ///
    /// Three kinds of edge, and no others - anything the player can do that is not one of these is
    /// something the level did not design:
    ///   * a jump between two roofs, graded by <see cref="RouteTiers.Classify"/>;
    ///   * stepping on or off a link deck it is flush with;
    ///   * climbing an ascent, or walking back down it.
    /// </summary>
    public static RoofGraph Build(CityPlanResult plan)
    {
        CityTraversalResult traversal = plan.Traversal;
        RoofGraph graph = new RoofGraph();

        // Deterministic node order: the plan's own order, then the platforms, then the decks.
        List<string> roofs = new List<string>();

        foreach (BuildingPlan building in plan.Buildings)
        {
            roofs.Add(building.Name);
        }

        roofs.Add(CityTraversal.PodiumNode);
        roofs.Add(CityTraversal.WingNorthNode);
        roofs.Add(CityTraversal.WingWestNode);
        roofs.Add(CityTraversal.ShaftRoofNode);

        foreach (string roof in roofs)
        {
            graph.Add(roof);
        }

        foreach (LinkPlan link in traversal.Links)
        {
            graph.Add(link.DeckNode);
        }

        // --- jumps between roofs ------------------------------------------------------
        for (int i = 0; i < roofs.Count; i++)
        {
            for (int j = 0; j < roofs.Count; j++)
            {
                if (i == j)
                {
                    continue;
                }

                TraversalSurface a = traversal.Surfaces[roofs[i]];
                TraversalSurface b = traversal.Surfaces[roofs[j]];
                graph.TryJump(a, b, "jump");
            }
        }

        // --- link decks ---------------------------------------------------------------
        foreach (LinkPlan link in traversal.Links)
        {
            TraversalSurface deck = traversal.Surfaces[link.DeckNode];

            foreach (string end in link.FlushEnds())
            {
                TraversalSurface roof = traversal.Surfaces[end];
                graph.TryJump(roof, deck, link.Name);
                graph.TryJump(deck, roof, link.Name);
            }

            // Stepping off the taller end down onto the deck, where that is a drop the player can
            // take. Climbing back up is the link's stair, added below.
            graph.TryJump(traversal.Surfaces[link.FromNode], deck, link.Name);
            graph.TryJump(traversal.Surfaces[link.ToNode], deck, link.Name);
        }

        // --- ascents -------------------------------------------------------------------
        foreach (AscentPlan ascent in traversal.Ascents)
        {
            if (ascent.FromStreet)
            {
                graph.Entries.Add(new RoofEntry(ascent.Name, ascent.TopNode));
                continue;
            }

            RouteTier tier = WorstStep(ascent);
            graph.Connect(ascent.BottomNode, ascent.TopNode, tier, ascent.Name);

            // Every ascent is walkable downhill: a ledge stack is a stair, and a ramped spiral is
            // a ramp. Neither becomes harder for being taken the other way.
            graph.Connect(ascent.TopNode, ascent.BottomNode, RouteTier.Green, ascent.Name);
        }

        return graph;
    }

    /// <summary>
    /// The same graph with the pavement in it, which is the network a *mission* is played on.
    ///
    /// Phase 6C deliberately left the street out: its question was "how many separate ways up are
    /// there", and a graph where every roof connects to every other one through the ground would
    /// have answered it with "one". Phase 6D's question is the opposite - the player has to be able
    /// to visit five relays in any order they like, and climbing down a fire escape and walking two
    /// blocks is a legitimate way to do that. So the ways in become real edges here: up an ascent
    /// at the tier its worst step measures, and back down it as the walk down a stair always is.
    ///
    /// <see cref="Build"/> is untouched, so every Phase 6C measurement still means what it did.
    /// </summary>
    public static RoofGraph BuildWithStreet(CityPlanResult plan)
    {
        RoofGraph graph = Build(plan);
        graph.Add(StreetNode);

        foreach (AscentPlan ascent in plan.Traversal.Ascents)
        {
            if (!ascent.FromStreet)
            {
                continue;
            }

            graph.Connect(StreetNode, ascent.TopNode, WorstStep(ascent), ascent.Name);
            graph.Connect(ascent.TopNode, StreetNode, RouteTier.Green, ascent.Name);
        }

        return graph;
    }

    /// <summary>The pavement, as one node. Phase 6B proved the whole of it is walkable.</summary>
    public const string StreetNode = "STREET";

    /// <summary>The hardest step in an ascent, which is what the whole ascent grades at.</summary>
    public static RouteTier WorstStep(AscentPlan ascent)
    {
        if (ascent.IsRamped)
        {
            return RouteTier.Green;
        }

        RouteTier worst = RouteTier.Green;

        foreach (AscentStep step in ascent.Steps())
        {
            RouteTier tier = step.Tier;

            if (tier > worst)
            {
                worst = tier;
            }
        }

        return worst;
    }

    private void Add(string node)
    {
        if (edges.ContainsKey(node))
        {
            return;
        }

        edges[node] = new List<RoofEdge>();
        nodes.Add(node);
    }

    private void Connect(string from, string to, RouteTier tier, string via)
    {
        if (tier == RouteTier.Unreachable || !edges.ContainsKey(from) || !edges.ContainsKey(to))
        {
            return;
        }

        edges[from].Add(new RoofEdge(to, tier, via));
    }

    /// <summary>
    /// Adds the edge if the player could actually make it: inside the tier table, and not a drop
    /// past <see cref="CityDesign.SafeDropHeight"/>.
    /// </summary>
    private void TryJump(in TraversalSurface from, in TraversalSurface to, string via)
    {
        float rise = to.SurfaceY - from.SurfaceY;

        if (rise < -CityDesign.SafeDropHeight)
        {
            return;
        }

        float gap = from.Footprint.GapTo(to.Footprint);
        float landing = Mathf.Min(to.Footprint.Width, to.Footprint.Depth);
        Connect(from.Node, to.Node, RouteTiers.Classify(gap, rise, landing), via);
    }

    // ------------------------------------------------------------------ queries

    /// <summary>
    /// The route a player would actually take: the easiest one, and among equally easy ones the
    /// shortest.
    ///
    /// Plain breadth-first would return the fewest-move path, which is not the same thing and is
    /// usually worse - the shortest way between two Industrial roofs is a 9.9 m corner-to-corner
    /// diagonal that grades RED, when two BLUE hops along the street front get there just as well.
    /// So the search is run once per tier, easiest first, over the edges that tier allows, and the
    /// first tier that connects is the answer.
    /// </summary>
    public List<string> Path(string from, string to)
    {
        foreach (RouteTierSpec spec in RouteTiers.Table)
        {
            List<string> path = PathWithin(from, to, spec.Tier);

            if (path != null)
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>Breadth-first over the edges no harder than <paramref name="limit"/>.</summary>
    public List<string> PathWithin(string from, string to, RouteTier limit)
    {
        if (!edges.ContainsKey(from) || !edges.ContainsKey(to))
        {
            return null;
        }

        Dictionary<string, string> cameFrom = new Dictionary<string, string> { { from, null } };
        Queue<string> queue = new Queue<string>();
        queue.Enqueue(from);

        while (queue.Count > 0)
        {
            string at = queue.Dequeue();

            if (at == to)
            {
                List<string> path = new List<string>();

                for (string step = to; step != null; step = cameFrom[step])
                {
                    path.Add(step);
                }

                path.Reverse();
                return path;
            }

            foreach (RoofEdge edge in edges[at])
            {
                if (edge.Tier > limit || cameFrom.ContainsKey(edge.To))
                {
                    continue;
                }

                cameFrom[edge.To] = at;
                queue.Enqueue(edge.To);
            }
        }

        return null;
    }

    public bool CanReach(string from, string to) => Path(from, to) != null;

    /// <summary>The hardest edge along a path, which is the tier the path is graded at.</summary>
    public RouteTier WorstTier(IReadOnlyList<string> path)
    {
        RouteTier worst = RouteTier.Green;

        for (int i = 0; i < path.Count - 1; i++)
        {
            RouteTier best = RouteTier.Unreachable;

            foreach (RoofEdge edge in From(path[i]))
            {
                if (edge.To == path[i + 1] && edge.Tier < best)
                {
                    best = edge.Tier;
                }
            }

            if (best > worst)
            {
                worst = best;
            }
        }

        return worst;
    }

    /// <summary>
    /// The street entries a surface can be reached from - the "routes" the Phase 6C exit criterion
    /// counts. One per fire escape or scaffold, so two ways up the same stack are one route, and a
    /// bridge that only leads back where you came from is none.
    /// </summary>
    public List<string> AccessRoutes(string node)
    {
        List<string> found = new List<string>();

        foreach (RoofEntry entry in Entries)
        {
            if (entry.Node == node || CanReach(entry.Node, node))
            {
                found.Add(entry.Name);
            }
        }

        return found;
    }
}
