using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The mission: five relays in any order, then the tower.
///
/// This is the seam between the run systems and the objective layer. <see cref="CheckpointManager"/>
/// in <see cref="CheckpointRouteOrder.Set"/> mode owns *whether* a relay is captured - it counts the
/// crossings, gates the finish and survives RESTART RUN like it does on every other level. This
/// class owns what that means for the level: which relay a counted crossing was, what the compass
/// should be pointing at next, and whether the tower is still shut.
///
/// It holds no state the checkpoint manager already holds, which is what keeps the two from
/// disagreeing. <see cref="Captured"/> is the manager's count.
///
/// Order-freedom is not implemented here. It is a property of the set - nothing in this file, in
/// <see cref="ObjectiveRelay"/> or in <see cref="CityObjectives"/> says which relay is first - and
/// what makes it true of the *city* rather than of the code is that every relay can be reached from
/// every other one, which `CityObjectives.CanCompleteInAnyOrder` measures against the roof graph.
///
/// Its wiring is re-checked rather than assumed, and the class runs outside play mode. The tracker
/// is the one piece of the mission whose whole correctness sits in a lifecycle callback: miss the
/// subscription below and no relay ever lights, the hoarding never comes down and the tower never
/// opens - with no error, because nothing was ever asked to happen. Both of the ways this object is
/// actually built assign its references *after* the component exists. `SkyboundCityBuilder` adds it
/// and then fills the serialized fields; the Phase 6D tests add it under a deactivated root and
/// then activate the root. Neither is a moment a once-only OnEnable is guaranteed to see, and
/// outside play mode it is not seen at all. So the subscription is made idempotent and re-checked
/// wherever the tracker is used - the same fix, for the same reason, as the route list in
/// <see cref="CheckpointManager"/>: remove the invariant instead of trying to maintain it.
/// </summary>
[ExecuteAlways]
public sealed class ObjectiveTracker : MonoBehaviour
{
    [Header("Systems")]
    [SerializeField] private CheckpointManager checkpoints;

    [Tooltip("Optional. Used only to put the mission back to the start when a run restarts.")]
    [SerializeField] private GameManager game;

    [Header("Objectives")]
    [Tooltip("The relays. Leave empty to collect every ObjectiveRelay under Relay Root.")]
    [SerializeField] private List<ObjectiveRelay> relays = new List<ObjectiveRelay>();

    [SerializeField] private Transform relayRoot;

    [Header("The tower")]
    [Tooltip("Hoarding across the foot of the tower spiral. Deactivated when the set is complete.")]
    [SerializeField] private GameObject towerGate;

    [Tooltip("Where the compass points once every relay is captured.")]
    [SerializeField] private Transform summit;

    [SerializeField] private string summitName = "Skybound Tower";

    /// <summary>How many relays there are. Five, one per district.</summary>
    public int Total => checkpoints != null ? checkpoints.Total : relays.Count;

    /// <summary>How many have been captured, in whatever order they were taken.</summary>
    public int Captured => checkpoints != null ? checkpoints.Reached : 0;

    public bool AllCaptured => Total > 0 && Captured >= Total;

    /// <summary>False while the gate across the spiral is still up.</summary>
    public bool TowerUnlocked { get; private set; }

    public string SummitName => summitName;

    public IReadOnlyList<ObjectiveRelay> Relays => relays;

    /// <summary>Payload: the relay captured, the new count, the total.</summary>
    public event Action<ObjectiveRelay, int, int> RelayCaptured;

    /// <summary>Raised when the last relay is captured and the tower opens.</summary>
    public event Action TowerUnlockedChanged;

    // What this tracker is attached to, as opposed to what it is pointed at. The two differ for
    // exactly as long as it takes someone to notice, which is what EnsureSubscribed is.
    private CheckpointManager subscribedCheckpoints;
    private GameManager subscribedGame;

    // Which relay the mission is currently pointing at, and the scratch lists ObjectiveFocus is
    // asked with. Kept as fields rather than allocated per call because TryGetTarget is asked
    // every frame by both the compass and the route guide.
    private int focused = -1;
    private readonly List<Vector3> focusPositions = new List<Vector3>();
    private readonly List<bool> focusAvailable = new List<bool>();

    private void Awake()
    {
        CollectIfEmpty();
        EnsureSubscribed();

        // From the count rather than from a constant: Awake is not always the start of a run. Under
        // [ExecuteAlways] it is also every domain reload, and a gate that slammed shut on a
        // recompile would contradict progress the checkpoint manager is still holding.
        ApplyGate(AllCaptured);
        WarnAboutWiring();
    }

    private void OnEnable() => EnsureSubscribed();

    private void OnDisable() => Unsubscribe();

    /// <summary>
    /// Attaches to what this tracker is pointed at now, detaching from whatever it was attached to
    /// before. Idempotent and cheap - once the two agree it does nothing at all - so it is safe to
    /// call from anywhere, which is the point: "the references were assigned before OnEnable ran"
    /// is exactly the assumption that does not hold here.
    /// </summary>
    private void EnsureSubscribed()
    {
        if (subscribedCheckpoints != checkpoints)
        {
            if (subscribedCheckpoints != null)
            {
                subscribedCheckpoints.CheckpointReached -= HandleCheckpointReached;
            }

            subscribedCheckpoints = checkpoints;

            if (subscribedCheckpoints != null)
            {
                subscribedCheckpoints.CheckpointReached += HandleCheckpointReached;
            }
        }

        if (subscribedGame != game)
        {
            if (subscribedGame != null)
            {
                subscribedGame.StateChanged -= HandleStateChanged;
            }

            subscribedGame = game;

            if (subscribedGame != null)
            {
                subscribedGame.StateChanged += HandleStateChanged;
            }
        }
    }

    private void Unsubscribe()
    {
        if (subscribedCheckpoints != null)
        {
            subscribedCheckpoints.CheckpointReached -= HandleCheckpointReached;
        }

        if (subscribedGame != null)
        {
            subscribedGame.StateChanged -= HandleStateChanged;
        }

        subscribedCheckpoints = null;
        subscribedGame = null;
    }

    /// <summary>
    /// The tracker reads its totals off the checkpoint route, so the route has to be the relay set
    /// and nothing else. Anything else is a wiring mistake worth saying out loud.
    /// </summary>
    private void WarnAboutWiring()
    {
        if (checkpoints == null)
        {
            return;
        }

        if (checkpoints.Total != relays.Count)
        {
            Debug.LogWarning($"[Objectives] {relays.Count} relay(s) but {checkpoints.Total} " +
                             "checkpoint(s) on the route; the mission count will not agree.", this);
        }

        if (checkpoints.Order != CheckpointRouteOrder.Set)
        {
            Debug.LogWarning("[Objectives] the checkpoint route is Sequential, so the relays can " +
                             "only be taken in list order.", this);
        }
    }

    private void CollectIfEmpty()
    {
        if (relays.Count > 0 || relayRoot == null)
        {
            return;
        }

        ObjectiveRelay[] found = relayRoot.GetComponentsInChildren<ObjectiveRelay>(true);
        Array.Sort(found, (a, b) => string.CompareOrdinal(a.RelayId, b.RelayId));
        relays.AddRange(found);

        Debug.Log($"[Objectives] auto-collected {relays.Count} relays from '{relayRoot.name}'.", this);
    }

    // ---------------------------------------------------------------- capture

    private void HandleCheckpointReached(CheckpointVolume volume, int reached, int total,
        float split, float cumulative)
    {
        ObjectiveRelay relay = Find(volume);

        if (relay == null)
        {
            // A checkpoint that is not a relay is not this class's business.
            return;
        }

        relay.MarkCaptured();
        RelayCaptured?.Invoke(relay, reached, total);

        Debug.Log($"[Objectives] {relay.DisplayName} relay captured - {reached}/{total}.", relay);

        if (reached >= total)
        {
            ApplyGate(true);
        }
    }

    private ObjectiveRelay Find(CheckpointVolume volume)
    {
        foreach (ObjectiveRelay relay in relays)
        {
            if (relay != null && relay.Volume == volume)
            {
                return relay;
            }
        }

        return null;
    }

    private void ApplyGate(bool unlocked)
    {
        TowerUnlocked = unlocked;

        if (towerGate != null)
        {
            towerGate.SetActive(!unlocked);
        }

        TowerUnlockedChanged?.Invoke();
    }

    // ---------------------------------------------------------------- the run

    private void HandleStateChanged(RunState state)
    {
        // The manager clears its own progress on a restart; the relays and the gate are this
        // class's to put back, and Countdown is the one state a fresh run always passes through.
        if (state != RunState.Countdown)
        {
            return;
        }

        ResetObjectives();
    }

    /// <summary>Puts every relay back to uncaptured and the gate back up.</summary>
    public void ResetObjectives()
    {
        EnsureSubscribed();

        foreach (ObjectiveRelay relay in relays)
        {
            if (relay != null)
            {
                relay.ResetRelay();
            }
        }

        // A restart puts the mission back to nothing chosen, or the held target would survive into
        // a run that has not started yet and the first thing the compass said would be stale.
        focused = -1;

        ApplyGate(false);
    }

    // ---------------------------------------------------------------- the compass

    /// <summary>
    /// What the player should be heading for from where they are standing: the nearest relay they
    /// have not captured, or the summit once there are none left.
    ///
    /// Nearest rather than next, because there is no next - the set has no order, and a compass
    /// that picked one would be inventing the very thing this level is built not to have.
    /// </summary>
    public bool TryGetTarget(Vector3 from, out Vector3 position, out string label, out bool isSummit)
        => TryGetTarget(from, out position, out label, out isSummit, out _);

    /// <summary>
    /// The same answer, plus the target's stable id - a relay's <see cref="ObjectiveRelay.RelayId"/>
    /// or <see cref="SummitName"/>.
    ///
    /// The display name is for a player to read and is not an identity: two relays could be renamed
    /// to the same words without anything breaking, and `RouteGuide` has to know when the objective
    /// has actually changed so it can re-search the city rather than re-search it every frame.
    /// </summary>
    public bool TryGetTarget(Vector3 from, out Vector3 position, out string label, out bool isSummit,
        out string id)
    {
        // The compass asks this every frame, so it is also the cheapest place to notice that the
        // tracker is pointed at a route it never got attached to.
        EnsureSubscribed();

        // The rule is "the nearest uncaptured relay", and it is applied with hysteresis rather
        // than evaluated fresh. On the line where two relays are equidistant the bare rule
        // alternates frame by frame - 113 of 5041 sampled street positions sit within 3 m of such a
        // line - which made the compass flash between two district names and made the route guide
        // re-search the whole city on alternating frames. `ObjectiveFocus` keeps whatever was
        // chosen last until something is clearly nearer, so the answer is the same answer, held.
        focusPositions.Clear();
        focusAvailable.Clear();

        foreach (ObjectiveRelay relay in relays)
        {
            focusPositions.Add(relay != null ? relay.Position : from);
            focusAvailable.Add(relay != null && !relay.Captured);
        }

        focused = ObjectiveFocus.Choose(focusPositions, focusAvailable, from, focused,
            CityDesign.ObjectiveStickiness);

        ObjectiveRelay best = focused >= 0 && focused < relays.Count ? relays[focused] : null;

        if (best != null)
        {
            position = best.Position;
            label = best.DisplayName;
            id = best.RelayId;
            isSummit = false;
            return true;
        }

        isSummit = true;
        label = summitName;
        id = summitName;

        if (summit != null)
        {
            position = summit.position;
            return true;
        }

        position = from;
        return false;
    }

    private static float Horizontal(Vector3 delta)
    {
        delta.y = 0f;
        return delta.magnitude;
    }
}
