using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>How a route's checkpoints relate to each other.</summary>
public enum CheckpointRouteOrder
{
    /// <summary>
    /// A course. Checkpoint n only counts once n-1 has been crossed, so a run has one shape and
    /// the split times mean something. Levels 1 and 2 are this.
    /// </summary>
    Sequential,

    /// <summary>
    /// A set of objectives. Every checkpoint counts the first time it is crossed, whatever order
    /// they are visited in, and the route is finished when the set is empty.
    ///
    /// Phase 6D's Skybound City is this: five relays across a 600 x 600 m city, where choosing the
    /// order to take them in is the level. A set is not a weakened sequence - the difference is who
    /// "have I reached this checkpoint" is asked of, and a set asks the checkpoint rather than the
    /// route.
    /// </summary>
    Set
}

/// <summary>
/// Owns checkpoint progress and split times for a run.
///
/// A route is either a sequence or a set - see <see cref="CheckpointRouteOrder"/>. Either way its
/// members are the serialized list; if that is left empty the manager collects every
/// <see cref="CheckpointVolume"/> under <see cref="checkpointRoot"/> and sorts them by name, which
/// matches the CP1..CPn naming the level builders produce.
/// </summary>
public sealed class CheckpointManager : MonoBehaviour
{
    [Header("Route")]
    [Tooltip("Route order. Leave empty to auto-collect from Checkpoint Root, sorted by name.")]
    [SerializeField] private List<CheckpointVolume> checkpoints = new List<CheckpointVolume>();

    [Tooltip("Fallback source when the list above is empty.")]
    [SerializeField] private Transform checkpointRoot;

    [Tooltip("Sequential: a checkpoint only counts if it is the next one in the route. " +
             "Set: every checkpoint counts the first time it is crossed, in any order.")]
    [SerializeField] private CheckpointRouteOrder order = CheckpointRouteOrder.Sequential;

    [Header("References")]
    [SerializeField] private RunTimer runTimer;

    private readonly List<float> cumulativeTimes = new List<float>();

    /// <summary>
    /// The checkpoints crossed so far, in the order they were crossed.
    ///
    /// Deliberately a list of the volumes themselves rather than a flag per route position. A
    /// parallel array has to be sized against the route, which means being sized at a moment when
    /// the route is already known - and <see cref="Awake"/> is not reliably that moment: a
    /// component added in code has its Awake run when it is added, before whatever assigns its
    /// route, and the array is then silently one size behind for the rest of its life. Holding the
    /// volumes removes the invariant instead of trying to maintain it.
    /// </summary>
    private readonly List<CheckpointVolume> crossed = new List<CheckpointVolume>();

    /// <summary>Total checkpoints on the route.</summary>
    public int Total => checkpoints.Count;

    /// <summary>How many have been crossed, 0..<see cref="Total"/>.</summary>
    public int Reached => crossed.Count;

    public bool AllReached => Total > 0 && Reached >= Total;

    /// <summary>Sequence or set.</summary>
    public CheckpointRouteOrder Order => order;

    /// <summary>
    /// The most recently crossed checkpoint, or null if none yet.
    ///
    /// Held rather than derived from <see cref="Reached"/>: in a set the count says how many have
    /// been crossed and nothing at all about which one was last, and the last one is exactly what
    /// a respawn has to return to.
    /// </summary>
    public CheckpointVolume Current { get; private set; }

    /// <summary>Has this member of the route been crossed? False for anything not on it.</summary>
    public bool IsReached(CheckpointVolume volume) => crossed.Contains(volume);

    /// <summary>Run time at each crossing, in route order.</summary>
    public IReadOnlyList<float> CumulativeTimes => cumulativeTimes;

    /// <summary>
    /// Raised when a checkpoint is legally crossed.
    /// Payload: volume, progress after the crossing, total, split since the previous crossing,
    /// cumulative run time. On a sequence that progress is also the checkpoint's 1-based position
    /// in the route; in a set it is only ever the count, which is what a set has instead.
    /// </summary>
    public event Action<CheckpointVolume, int, int, float, float> CheckpointReached;

    private void Awake()
    {
        CollectIfEmpty();

        for (int i = 0; i < checkpoints.Count; i++)
        {
            if (checkpoints[i] != null)
            {
                checkpoints[i].Bind(this, i + 1);
            }
        }
    }

    private void CollectIfEmpty()
    {
        if (checkpoints.Count > 0 || checkpointRoot == null)
        {
            return;
        }

        CheckpointVolume[] found = checkpointRoot.GetComponentsInChildren<CheckpointVolume>(true);
        Array.Sort(found, (a, b) => string.CompareOrdinal(a.name, b.name));
        checkpoints.AddRange(found);

        Debug.Log($"[Checkpoints] auto-collected {checkpoints.Count} from '{checkpointRoot.name}'.", this);
    }

    /// <summary>
    /// Called by <see cref="CheckpointVolume"/> on entry. Returns false when the crossing does not
    /// count - out of order on a sequence, or already crossed in a set - which leaves the gate
    /// armed for a later, legal crossing.
    /// </summary>
    internal bool TryActivate(CheckpointVolume volume)
    {
        int index = checkpoints.IndexOf(volume);
        if (index < 0)
        {
            Debug.LogWarning($"[Checkpoints] '{volume.name}' is not on the route list; ignoring.", volume);
            return false;
        }

        // A sequence asks the route - is this the next one - and a set asks the checkpoint - have
        // I already been here. That single line is the whole difference between the two modes.
        if (order == CheckpointRouteOrder.Sequential ? index != Reached : crossed.Contains(volume))
        {
            return false;
        }

        crossed.Add(volume);
        Current = volume;

        float cumulative = runTimer != null ? runTimer.ElapsedSeconds : 0f;
        float previous = cumulativeTimes.Count > 0 ? cumulativeTimes[cumulativeTimes.Count - 1] : 0f;
        float split = cumulative - previous;
        cumulativeTimes.Add(cumulative);

        CheckpointReached?.Invoke(volume, Reached, Total, split, cumulative);
        return true;
    }

    /// <summary>Clears progress and re-arms every gate. Used by RESTART RUN.</summary>
    public void ResetProgress()
    {
        Current = null;
        crossed.Clear();
        cumulativeTimes.Clear();

        for (int i = 0; i < checkpoints.Count; i++)
        {
            if (checkpoints[i] != null)
            {
                checkpoints[i].ResetActivation();
            }
        }
    }
}
