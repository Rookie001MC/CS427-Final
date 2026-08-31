using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The world-space route to the current objective, drawn on the ground the player has to run over.
///
/// The Phase 6D compass answers "where is it". In a 600 x 600 m city of solid blocks that is not
/// enough on its own: a bearing straight through a superblock tells the player to go somewhere they
/// cannot go and gives them no idea which way round it. This answers the other two questions - which
/// way to run, and what to do when they get there - by searching the city's own navigation graph,
/// laying a trail of chevrons along the route it finds, and standing an upright marker at every
/// point where the route stops being a run and becomes a climb, a jump or a crossing.
///
/// Three properties are worth stating, because each of them is a thing this deliberately is not:
///
///   <b>It is not a line to the objective.</b> Every leg is an edge of <see cref="CityNavGraph"/>,
///   which is the street corridors, the thirteen ways up off the pavement, and Phase 6C's
///   `RoofGraph` exactly as the traversal layer built it. The guide cannot suggest a move the city
///   does not have, because it has no way to express one.
///
///   <b>It owns no mission state.</b> It reads <see cref="ObjectiveTracker.TryGetTarget"/> and
///   nothing else. Which relay is next, whether the tower is open and what counts as captured are
///   all still the tracker's, so a guide that failed to run would cost the player directions and
///   nothing more.
///
///   <b>It holds still.</b> The first version of this component recomputed everything it drew from
///   the player's position every frame, and flickered. All of the state that used to live here now
///   lives in <see cref="RouteTrail"/>, which is a pure object over the graph and so can be walked
///   for a thousand frames by a test: markers are anchored to the route's own arc length, which of
///   them are showing is a windowed projection of the player onto that same arc, the node under the
///   player is chosen with hysteresis, and a search only happens when the objective changes or the
///   player has actually stepped off the route. What is left in this file is the view - a fixed
///   pool of transforms, pointed at whatever the trail says is visible.
/// </summary>
public sealed class RouteGuide : MonoBehaviour
{
    [Header("Systems")]
    [SerializeField] private ObjectiveTracker tracker;

    [Tooltip("The player's body. The route is searched from wherever this is standing.")]
    [SerializeField] private Transform player;

    [Header("Views")]
    [Tooltip("The chevron pool. Fixed size; the guide shows and hides these, never creates them.")]
    [SerializeField] private Transform[] markers = new Transform[0];

    [Tooltip("Upright markers that stand where the route stops being a run. Also a fixed pool.")]
    [SerializeField] private Transform[] actionMarkers = new Transform[0];

    [Tooltip("The pillar of light over the active objective.")]
    [SerializeField] private Transform beacon;

    [Header("The navigation graph")]
    [Tooltip("Baked by SkyboundCityBuilder from CityNavigation. Never edited by hand.")]
    [SerializeField] private string[] nodeNames = new string[0];

    [SerializeField] private int[] nodeKinds = new int[0];
    [SerializeField] private Vector3[] nodePositions = new Vector3[0];
    [SerializeField] private Vector3[] nodeExtents = new Vector3[0];
    [SerializeField] private int[] linkFrom = new int[0];
    [SerializeField] private int[] linkTo = new int[0];
    [SerializeField] private float[] linkCost = new float[0];
    [SerializeField] private Vector3[] linkExit = new Vector3[0];
    [SerializeField] private int[] linkTier = new int[0];
    [SerializeField] private int[] linkMove = new int[0];

    [Header("Targets")]
    [Tooltip("Objective ids, parallel with the node each one ends on.")]
    [SerializeField] private string[] targetIds = new string[0];

    [SerializeField] private string[] targetNodes = new string[0];

    private CityNavGraph graph;

    // The route and where along it the player is. Both survive from frame to frame: that is the
    // whole of why the trail is stable, and none of it is this component's to reason about.
    private RouteTrail trail;

    // What the pools should be showing this frame. Fields rather than locals so drawing the trail
    // allocates nothing.
    private readonly List<Breadcrumb> chevrons = new List<Breadcrumb>();
    private readonly List<Breadcrumb> actions = new List<Breadcrumb>();

    /// <summary>How many chevrons are currently showing. Zero when there is no route.</summary>
    public int VisibleMarkers { get; private set; }

    /// <summary>How many upright markers are currently showing.</summary>
    public int VisibleActionMarkers { get; private set; }

    /// <summary>The objective the trail currently leads to.</summary>
    public string ActiveTarget => trail != null ? trail.Target : string.Empty;

    /// <summary>The route, as world points. Exposed so a harness can measure what a player sees.</summary>
    public IReadOnlyList<Breadcrumb> Trail
        => trail != null ? trail.Crumbs : System.Array.Empty<Breadcrumb>();

    /// <summary>How far along the route the player has got, in metres.</summary>
    public float Progress => trail != null ? trail.Along : 0f;

    /// <summary>How many graph searches the guide has run. Instrumentation for the harnesses.</summary>
    public int Searches => trail != null ? trail.Searches : 0;

    // What the builder baked in, so Harness E can check the scene against the plan rather than
    // taking the builder's word for it. A guide wired to an empty graph draws nothing and reports
    // no error, which is exactly the failure that needs to be loud.

    public int NodeCount => nodeNames.Length;

    public int LinkCount => linkFrom.Length;

    public int TargetCount => targetIds.Length;

    public int MarkerCount => markers.Length;

    public int ActionMarkerCount => actionMarkers.Length;

    public bool IsWired => tracker != null && player != null && beacon != null
                           && markers.Length > 0 && actionMarkers.Length > 0
                           && nodeNames.Length > 0;

    /// <summary>
    /// Searches the baked graph without touching the scene. Used by the objective validator to
    /// prove that the graph the player's guide is holding really does connect the spawn to every
    /// objective, rather than that some other copy of it does.
    /// </summary>
    public List<Vector3> RouteFrom(Vector3 at, string targetId, Vector3 destination)
    {
        EnsureGraph();

        if (graph == null)
        {
            return null;
        }

        int to = TargetNode(targetId);
        int from = graph.Nearest(at);

        if (to < 0 || from < 0)
        {
            return null;
        }

        List<int> path = graph.Path(from, to);
        return path == null ? null : graph.Waypoints(graph.Nodes[from].Position, path, destination);
    }

    private void Awake() => EnsureGraph();

    private void EnsureGraph()
    {
        if (graph != null || nodeNames.Length == 0)
        {
            return;
        }

        graph = CityNavGraph.FromArrays(nodeNames, nodeKinds, nodePositions, nodeExtents,
            linkFrom, linkTo, linkCost, linkExit, linkTier, linkMove);

        // Room for the pool plus a spare pool's worth, so the far end of the drawn window is laid
        // before the near end is consumed and the trail never runs out mid-stride.
        trail = new RouteTrail(graph, markers.Length + CityDesign.GuideMarkerCount);
    }

    /// <summary>
    /// Per frame: ask the tracker where the player is going, hand that and their position to
    /// <see cref="RouteTrail"/>, and point the pools at whatever it says is visible.
    ///
    /// There is no decision left in this method, which is the point of it. Every judgement the
    /// guide makes - is this still the route, how far along it are they, which markers does the
    /// route want and where - is a pure function over in `RouteTrail`, where a test can run it for
    /// a thousand frames and count what moved.
    /// </summary>
    private void Update()
    {
        if (tracker == null || player == null)
        {
            return;
        }

        EnsureGraph();

        if (graph == null || trail == null)
        {
            return;
        }

        Vector3 at = player.position;

        if (!tracker.TryGetTarget(at, out Vector3 destination, out _, out _, out string id))
        {
            Clear();
            return;
        }

        PlaceBeacon(destination);

        trail.Step(at, id, TargetNode(id), destination);
        trail.Visible(markers.Length, actionMarkers.Length, chevrons, actions);

        Draw();
    }

    /// <summary>
    /// Puts the pools on the markers the trail asked for.
    ///
    /// Which pool object draws which marker is not stable and does not need to be - as the player
    /// runs past the nearest chevron every marker behind it shifts down a slot - because a slot is
    /// given its whole transform every frame. What must be stable, and is, is the set of world
    /// positions: the same route asks for the same square metres of the city, whoever is drawing
    /// them.
    /// </summary>
    private void Draw()
    {
        for (int i = 0; i < chevrons.Count; i++)
        {
            Place(markers[i], chevrons[i], true);
        }

        for (int i = 0; i < actions.Count; i++)
        {
            Place(actionMarkers[i], actions[i], false);
        }

        Hide(markers, chevrons.Count);
        Hide(actionMarkers, actions.Count);

        VisibleMarkers = chevrons.Count;
        VisibleActionMarkers = actions.Count;
    }

    private int TargetNode(string id)
    {
        for (int i = 0; i < targetIds.Length && i < targetNodes.Length; i++)
        {
            if (targetIds[i] == id)
            {
                return graph.IndexOf(targetNodes[i]);
            }
        }

        return -1;
    }

    /// <summary>
    /// Stands one pool object on one marker.
    ///
    /// The heading comes from the breadcrumb and nothing else. It used to fall back to the pool
    /// object's own rotation where the route had no horizontal direction to give - up a fire escape
    /// - which made a fixed spot on the ground face whichever way the last marker drawn by that
    /// slot had faced. <see cref="CityNavigation.Breadcrumbs"/> now carries the last real heading
    /// forward instead, so there is nothing left to fall back to.
    /// </summary>
    private void Place(Transform marker, Breadcrumb crumb, bool bob)
    {
        if (marker == null)
        {
            return;
        }

        float lift = CityDesign.GuideMarkerRise;

        if (bob)
        {
            // Keyed to the marker's place on the route, so the pulse belongs to the spot on the
            // ground rather than to whichever pool object is currently drawing it.
            lift += Mathf.Sin(Time.time * 2.2f - crumb.Along * 0.35f) * 0.12f;
        }

        marker.SetPositionAndRotation(crumb.Position + Vector3.up * lift,
            Quaternion.LookRotation(crumb.Forward, Vector3.up));

        if (!marker.gameObject.activeSelf)
        {
            marker.gameObject.SetActive(true);
        }
    }

    private static void Hide(Transform[] pool, int from)
    {
        for (int i = from; i < pool.Length; i++)
        {
            if (pool[i] != null && pool[i].gameObject.activeSelf)
            {
                pool[i].gameObject.SetActive(false);
            }
        }
    }

    private void PlaceBeacon(Vector3 destination)
    {
        if (beacon == null)
        {
            return;
        }

        beacon.position = destination + Vector3.up * (CityDesign.GuideBeaconHeight * 0.5f);

        if (!beacon.gameObject.activeSelf)
        {
            beacon.gameObject.SetActive(true);
        }
    }

    private void Clear()
    {
        trail?.Clear();
        chevrons.Clear();
        actions.Clear();
        VisibleMarkers = 0;
        VisibleActionMarkers = 0;

        Hide(markers, 0);
        Hide(actionMarkers, 0);

        if (beacon != null && beacon.gameObject.activeSelf)
        {
            beacon.gameObject.SetActive(false);
        }
    }
}
