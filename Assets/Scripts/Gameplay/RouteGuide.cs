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
///   for a thousand frames by a test: markers are laid once for the whole route and anchored at
///   whole spacings back from the objective, which of them are showing is a windowed projection of
///   the player onto the route's arc, the node under the player is chosen with hysteresis, and a
///   search only happens when the objective changes or the player has actually stepped off the
///   route.
///
///   What is left in this file is the view, and the view had the last of the flicker in it. A pool
///   slot was given whatever the trail's i-th visible marker was, so running past one chevron shifted
///   every marker behind it down a slot: 535 live objects teleported - up to 30 m, and turning
///   through up to 90 degrees - over a 400 m run in which only 36 markers genuinely came into view,
///   and the object at the far end blinked off and on again as the count wobbled. A marker is now a
///   thing with an identity - the square metre of city it stands on - and a slot is bound to one
///   until it leaves the window. A slot that keeps its marker is never re-aimed, never re-enabled,
///   and never moved except by the twelve centimetres of bob it is supposed to have.
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

    // Which pool object is standing on which marker. The whole of the persistent-marker model, and
    // in `City` rather than here so a test can walk it for ten thousand frames.
    private GuideMarkerPool chevronPool;
    private GuideMarkerPool actionPool;

    private readonly List<int> slots = new List<int>();
    private readonly List<bool> fresh = new List<bool>();
    private readonly List<int> release = new List<int>();

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

    /// <summary>
    /// How many times a pool object has been put on a marker it was not already on: switched on,
    /// aimed and moved. Instrumentation, and the number the flicker was measured in - it should
    /// equal the number of markers that have genuinely come into view, and nothing more.
    /// </summary>
    public int Rebinds => (chevronPool != null ? chevronPool.Rebinds : 0)
                          + (actionPool != null ? actionPool.Rebinds : 0);

    /// <summary>How many times a pool object has been switched on or off.</summary>
    public int Toggles => (chevronPool != null ? chevronPool.Toggles : 0)
                          + (actionPool != null ? actionPool.Toggles : 0);

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

        chevronPool = new GuideMarkerPool(markers.Length);
        actionPool = new GuideMarkerPool(actionMarkers.Length);
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
    /// Puts the pools on the markers the trail asked for, and leaves alone every object already
    /// standing on one.
    ///
    /// Which pool object draws which marker <b>is</b> stable, and has to be. The old version handed
    /// slot i the i-th visible marker, which is correct as a picture and wrong as a scene: running
    /// past the nearest chevron shifted every marker behind it down a slot, so twenty-odd live
    /// GameObjects were teleported and re-aimed on that frame. Nothing in a still frame shows it -
    /// the set of world positions was right either way - and everything in a moving one does,
    /// because a renderer that is moved discontinuously is a renderer with no motion vector, no
    /// temporal history and, at the end of the pool, a `SetActive` flip.
    ///
    /// So a marker is identified by the square metre it stands on, a slot holds one until the trail
    /// stops asking for it, and a slot that holds its marker is touched for the bob and nothing
    /// else.
    /// </summary>
    private void Draw()
    {
        Show(markers, chevronPool, chevrons, true);
        Show(actionMarkers, actionPool, actions, false);

        VisibleMarkers = chevrons.Count;
        VisibleActionMarkers = actions.Count;
    }

    /// <summary>Turns one frame of the pool's binding into transforms.</summary>
    private void Show(Transform[] pool, GuideMarkerPool binding, List<Breadcrumb> wanted, bool bob)
    {
        binding.Bind(wanted, slots, fresh, release);

        for (int i = 0; i < release.Count; i++)
        {
            Transform marker = pool[release[i]];

            if (marker != null && marker.gameObject.activeSelf)
            {
                marker.gameObject.SetActive(false);
            }
        }

        for (int i = 0; i < wanted.Count; i++)
        {
            if (slots[i] >= 0)
            {
                Place(pool[slots[i]], wanted[i], bob, fresh[i]);
            }
        }
    }

    /// <summary>The graph node the objective with this id stands on, or -1.</summary>
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
    private void Place(Transform marker, Breadcrumb crumb, bool bob, bool fresh)
    {
        if (marker == null)
        {
            return;
        }

        float lift = CityDesign.GuideMarkerRise;

        if (bob)
        {
            // Keyed to the marker's distance from the objective, so the pulse belongs to the spot on
            // the ground rather than to whichever pool object is drawing it - and so it does not
            // jump when a re-search changes how far the marker is from the *start* of the route,
            // which happens every time the player steps across a rooftop boundary.
            lift += Mathf.Sin(Time.time * 2.2f - crumb.Remaining * 0.35f) * 0.12f;
        }

        if (fresh)
        {
            // A marker's heading is a property of the route, so it is written once, when the object
            // arrives on it. Re-aiming a marker that has not moved is how a chevron came to swing
            // through 90 degrees on a frame the player ran past a different one.
            marker.SetPositionAndRotation(crumb.Position + Vector3.up * lift,
                Quaternion.LookRotation(crumb.Forward, Vector3.up));
        }
        else if (bob)
        {
            marker.position = crumb.Position + Vector3.up * lift;
        }

        if (!marker.gameObject.activeSelf)
        {
            marker.gameObject.SetActive(true);
        }
    }

    private void Hide(Transform[] pool, GuideMarkerPool binding)
    {
        binding?.Clear(release);

        for (int i = 0; i < pool.Length; i++)
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

        Hide(markers, chevronPool);
        Hide(actionMarkers, actionPool);

        if (beacon != null && beacon.gameObject.activeSelf)
        {
            beacon.gameObject.SetActive(false);
        }
    }
}
