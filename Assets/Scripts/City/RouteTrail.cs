using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The route the player is being shown, and the rules that decide when it changes.
///
/// Lifted out of <see cref="RouteGuide"/> for the same reason <see cref="ObjectiveFocus"/> was
/// lifted out of `ObjectiveTracker`: the faults this code exists to prevent are only visible over a
/// run of frames, and a rule that lives in a `MonoBehaviour.Update` cannot be run over a thousand
/// frames in a test. Everything here is a pure function of the graph and a sequence of player
/// positions, so the state machine the player gets is literally the one the tests walk.
///
/// Three things it holds still, each of which flickered when it did not:
///
///   <b>Whether this is still the player's route.</b> The question is about the graph - are they
///   standing on a node the route passes through - and it was being asked as a distance. The route
///   is anchored at the centre of the node it starts from, an Industrial roof node is 88 m across,
///   and <see cref="CityDesign.GuideRecomputeDistance"/> is 9 m: a player standing anywhere on that
///   roof but its middle read as 17 to 51 m "off the route", so the guide ran a fresh Dijkstra and
///   re-anchored the whole trail on <b>every frame they spent on a rooftop</b> - 600 searches in
///   600 frames standing perfectly still. Asked of the graph instead, the answer is a discrete fact
///   that holds for as long as the player stays on the route.
///
///   <b>Where along the route they are.</b> A windowed projection that only moves forwards, and is
///   re-anchored - not re-searched - on the one case that needs it, which is something moving the
///   player without their running there.
///
///   <b>What a redundant search does.</b> Nothing. A search that finds the route already drawn
///   keeps the arc position and the markers exactly as they were, so the one remaining way to
///   provoke a search cannot itself be seen.
/// </summary>
public sealed class RouteTrail
{
    private readonly CityNavGraph graph;
    private readonly int budget;

    private readonly List<Vector3> polyline = new List<Vector3>();
    private readonly List<NavMove> moves = new List<NavMove>();
    private readonly List<Breadcrumb> crumbs = new List<Breadcrumb>();

    // The nodes the route passes through. A set rather than a list because the only question ever
    // asked of it is membership, and it is asked every frame.
    private readonly HashSet<int> routeNodes = new HashSet<int>();

    // Scratch, so a search that turns out to change nothing does not disturb what is drawn.
    private readonly List<Vector3> candidate = new List<Vector3>();
    private readonly List<NavMove> candidateMoves = new List<NavMove>();

    private Vector3 destination;

    // The arc position the markers were last laid from. Remembered because "the trail is
    // short" and "the route has no more to give" look identical from the outside, and a
    // player standing on the objective is in the second case on every frame of the rest of
    // the run.
    private float laidFrom = float.NaN;

    public RouteTrail(CityNavGraph graph, int budget)
    {
        this.graph = graph;
        this.budget = budget;
    }

    /// <summary>The objective the trail currently leads to, or empty.</summary>
    public string Target { get; private set; } = string.Empty;

    /// <summary>How far along the route the player has got, in metres.</summary>
    public float Along { get; private set; }

    /// <summary>False when there is no route to draw - no target, or nothing connects to it.</summary>
    public bool HasRoute { get; private set; }

    /// <summary>The graph node the player is standing on, held with hysteresis. -1 before the first frame.</summary>
    public int StandingOn { get; private set; } = -1;

    /// <summary>The route, as world points.</summary>
    public IReadOnlyList<Vector3> Polyline => polyline;

    /// <summary>Every marker the visible stretch of the route wants, in arc order.</summary>
    public IReadOnlyList<Breadcrumb> Crumbs => crumbs;

    /// <summary>How many graph searches have been run. Instrumentation, and a test's whole subject.</summary>
    public int Searches { get; private set; }

    /// <summary>How many times the markers have been laid out again.</summary>
    public int Lays { get; private set; }

    /// <summary>The length of the whole route, in metres.</summary>
    public float Length
    {
        get
        {
            float total = 0f;

            for (int i = 0; i < polyline.Count - 1; i++)
            {
                total += (polyline[i + 1] - polyline[i]).magnitude;
            }

            return total;
        }
    }

    /// <summary>
    /// One frame. Advance along the route, decide whether it is still the right one, lay out what
    /// is left of it. In that order, and the order matters.
    /// </summary>
    /// <param name="targetNode">The graph node the objective sits on, or -1 if it has none.</param>
    public void Step(Vector3 at, string targetId, int targetNode, Vector3 destination)
    {
        StandingOn = graph.NearestStable(at, StandingOn, CityDesign.GuideSnapHysteresis);

        if (HasRoute)
        {
            Along = CityNavigation.Advance(polyline, at, Along, CityDesign.GuideProjectionWindow);
        }

        if (NeedsSearch(at, targetId))
        {
            Search(at, targetId, targetNode, destination);
        }
        else
        {
            Reanchor(at);
        }

        Lay();
    }

    /// <summary>Nothing to draw: no objective, or the guide has been switched off.</summary>
    public void Clear()
    {
        polyline.Clear();
        moves.Clear();
        crumbs.Clear();
        routeNodes.Clear();
        HasRoute = false;
        Along = 0f;
        laidFrom = float.NaN;
        Target = string.Empty;
    }

    /// <summary>
    /// The markers the pools should be showing this frame: the chevrons on the route ahead of the
    /// player, and the upright markers on the transitions among them.
    ///
    /// Both lists are in arc order and both are windows onto the same laid-out trail, so the only
    /// way one of them changes is the player running past a marker or the route itself changing.
    /// </summary>
    public void Visible(int markerPool, int actionPool, List<Breadcrumb> chevrons,
        List<Breadcrumb> actions)
    {
        chevrons.Clear();
        actions.Clear();

        if (!HasRoute)
        {
            return;
        }

        float lead = Along + CityDesign.GuideTrailLead;

        for (int i = 0; i < crumbs.Count; i++)
        {
            if (chevrons.Count >= markerPool && actions.Count >= actionPool)
            {
                break;
            }

            Breadcrumb crumb = crumbs[i];

            if (crumb.Along < lead)
            {
                continue;
            }

            if (chevrons.Count < markerPool)
            {
                chevrons.Add(crumb);
            }

            // An upright marker wherever the route stops being a run. This is the difference
            // between "that way" and "climb this": the chevrons lead to the foot of the fire
            // escape, and one of these is standing on it.
            if (crumb.Move != NavMove.Walk && crumb.IsTransition && actions.Count < actionPool)
            {
                actions.Add(crumb);
            }
        }
    }

    /// <summary>
    /// Whether the route has to be found again.
    ///
    /// Running the route is the normal case and must trigger nothing; what matters is having left
    /// it. That is asked two ways, and neither is enough on its own - each covers exactly the other
    /// one's blind spot.
    ///
    ///   <b>The node under them is one the route passes through.</b> True of every square metre of
    ///   every roof, deck and street corridor the route runs along, however large it is. This is
    ///   the clause that was missing: the guide asked only the second one, and a distance from a
    ///   polyline anchored at the centre of an 88 m roof node says "off the route" for a player
    ///   standing legitimately on that roof. 600 searches in 600 frames of standing still.
    ///
    ///   <b>Or they are standing on the line it drew.</b> The route runs from one node's exit point
    ///   to the next one's, and a stretch of that can pass nearer to some third node than to either
    ///   of the ones it joins - so a player running the route exactly as drawn can be "standing on"
    ///   a node the route never lists. Without this clause that re-searched three times over a
    ///   285 m route, and a re-search re-anchors every marker at once.
    /// </summary>
    private bool NeedsSearch(Vector3 at, string targetId)
    {
        if (targetId != Target || !HasRoute)
        {
            return true;
        }

        // Run out of route - but only worth re-finding if there is still somewhere to go. A player
        // standing on the objective has reached the end of the route legitimately, and re-searching
        // it every frame would be a Dijkstra per frame for a trail with nothing left to draw.
        if (Along >= Length - CityDesign.GuideBreadcrumbSpacing
            && Horizontal(at - destination) > CityDesign.GuideRecomputeDistance)
        {
            return true;
        }

        if (StandingOn >= 0 && routeNodes.Contains(StandingOn))
        {
            return false;
        }

        return DistanceToRoute(at) > CityDesign.GuideRecomputeDistance;
    }

    /// <summary>
    /// Puts the arc reading back where the player is, without touching the route.
    ///
    /// For the one case the projection cannot handle on its own: something moved the player rather
    /// than the player running - a respawn, a fall reset. They are still on the route, so the route
    /// is still right; only how far along it they are has stopped being true, and
    /// <see cref="CityNavigation.Advance"/> will not walk backwards to find out.
    ///
    /// The threshold carries the standing node's own footprint, for the same reason
    /// <see cref="CityNavGraph.Score"/> measures to a surface rather than to its middle. Without
    /// that, this fires on every frame a player spends anywhere but the middle of a roof - which is
    /// the fault it is sitting next to rather than a second copy of it.
    /// </summary>
    private void Reanchor(Vector3 at)
    {
        if (!HasRoute)
        {
            return;
        }

        float slack = 0f;

        if (StandingOn >= 0)
        {
            Vector3 extent = graph.Nodes[StandingOn].Extent;
            slack = new Vector2(extent.x, extent.z).magnitude;
        }

        if (DistanceToRoute(at) <= CityDesign.GuideRecomputeDistance + slack)
        {
            return;
        }

        Along = CityNavigation.Advance(polyline, at, 0f, float.MaxValue);
    }

    /// <summary>
    /// Searches the graph and lays the route out.
    ///
    /// The polyline starts at the *node* the player is standing on rather than at the player, which
    /// is what anchors the chevrons to the city: two searches of the same route from two positions
    /// on the same roof produce the same markers in the same places, so a re-search is invisible.
    /// This goes one better and makes it free as well - a search that finds the route already drawn
    /// returns without touching anything, so there is no arc position to re-project and no trail to
    /// lay out again.
    /// </summary>
    private void Search(Vector3 at, string targetId, int targetNode, Vector3 destination)
    {
        Searches++;

        bool sameObjective = targetId == Target;

        Target = targetId;
        this.destination = destination;

        int from = StandingOn;

        if (targetNode < 0 || from < 0)
        {
            Clear();
            Target = targetId;
            return;
        }

        List<int> path = graph.Path(from, targetNode);

        if (path == null)
        {
            // No route is a real answer, not a bug to paper over: the trail goes away and the HUD
            // compass is left to say which way the objective is. Silently drawing a straight line
            // here would be the exact failure this component exists to fix.
            Clear();
            Target = targetId;
            return;
        }

        candidate.Clear();
        candidate.AddRange(graph.Waypoints(graph.Nodes[from].Position, path, destination,
            candidateMoves));

        if (sameObjective && HasRoute && Same(candidate, polyline))
        {
            return;
        }

        polyline.Clear();
        polyline.AddRange(candidate);
        moves.Clear();
        moves.AddRange(candidateMoves);

        routeNodes.Clear();
        routeNodes.Add(from);

        foreach (int link in path)
        {
            routeNodes.Add(graph.Links[link].To);
        }

        HasRoute = true;
        crumbs.Clear();
        laidFrom = float.NaN;

        // Re-projecting rather than resetting: if this was a re-search of the same objective the
        // player has not gone back to the beginning, and starting the arc at zero would make the
        // trail grow backwards behind them for one frame.
        Along = sameObjective
            ? CityNavigation.Advance(polyline, at, 0f, float.MaxValue)
            : CityNavigation.Advance(polyline, at, 0f, CityDesign.GuideProjectionWindow);
    }

    /// <summary>
    /// Lays the markers out over what is left of the route, and only when what is drawn has
    /// actually run short.
    ///
    /// The second half of that is not an optimisation. The old condition asked whether the trail
    /// reached half the visible range ahead of the player and nothing else, so a player within
    /// 85 m of the end of their route re-laid a trail that could not grow, every frame, for the
    /// rest of the run.
    /// </summary>
    private void Lay()
    {
        if (!HasRoute)
        {
            crumbs.Clear();
            laidFrom = float.NaN;
            return;
        }

        if (crumbs.Count > 0)
        {
            float drawn = crumbs[crumbs.Count - 1].Along;

            if (drawn >= Along + CityDesign.GuideVisibleRange * 0.5f
                || drawn >= Length - CityDesign.GuideBreadcrumbSpacing)
            {
                return;
            }
        }
        else if (laidFrom == Along)
        {
            // Laid from exactly here already, and the route had nothing to give. It still has
            // nothing to give: the answer is a function of the route and this number, and neither
            // has moved. A player who has arrived stands on the objective for the rest of the run,
            // and without this that is an empty list rebuilt sixty times a second, for ever.
            return;
        }

        Lays++;
        laidFrom = Along;
        CityNavigation.Breadcrumbs(polyline, moves, budget, Along, crumbs);
    }

    /// <summary>How far the player is from the point on the route they have got to.</summary>
    private float DistanceToRoute(Vector3 at)
    {
        float travelled = 0f;

        for (int i = 0; i < polyline.Count - 1; i++)
        {
            Vector3 from = polyline[i];
            Vector3 step = polyline[i + 1] - from;
            float length = step.magnitude;

            if (length < 0.001f)
            {
                continue;
            }

            if (Along <= travelled + length)
            {
                return (from + step * ((Along - travelled) / length) - at).magnitude;
            }

            travelled += length;
        }

        return polyline.Count > 0 ? (polyline[polyline.Count - 1] - at).magnitude : 0f;
    }

    private static bool Same(List<Vector3> a, List<Vector3> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Count; i++)
        {
            if ((a[i] - b[i]).sqrMagnitude > 0.0001f)
            {
                return false;
            }
        }

        return true;
    }

    private static float Horizontal(Vector3 delta)
    {
        delta.y = 0f;
        return delta.magnitude;
    }
}
