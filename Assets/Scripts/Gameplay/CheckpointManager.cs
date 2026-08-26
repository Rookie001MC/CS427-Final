using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns checkpoint ordering and split times for a run.
///
/// The route order is the serialized list order. If the list is left empty the manager collects
/// every <see cref="CheckpointVolume"/> under <see cref="checkpointRoot"/> and sorts them by name,
/// which matches the CP1..CPn naming the level builders produce.
/// </summary>
public sealed class CheckpointManager : MonoBehaviour
{
    [Header("Route")]
    [Tooltip("Route order. Leave empty to auto-collect from Checkpoint Root, sorted by name.")]
    [SerializeField] private List<CheckpointVolume> checkpoints = new List<CheckpointVolume>();

    [Tooltip("Fallback source when the list above is empty.")]
    [SerializeField] private Transform checkpointRoot;

    [Tooltip("When true a checkpoint only counts if it is the next one in the route.")]
    [SerializeField] private bool requireSequential = true;

    [Header("References")]
    [SerializeField] private RunTimer runTimer;

    private readonly List<float> cumulativeTimes = new List<float>();

    /// <summary>Total checkpoints on the route.</summary>
    public int Total => checkpoints.Count;

    /// <summary>How many have been crossed, 0..<see cref="Total"/>.</summary>
    public int Reached { get; private set; }

    public bool AllReached => Total > 0 && Reached >= Total;

    /// <summary>The most recently crossed checkpoint, or null if none yet.</summary>
    public CheckpointVolume Current => Reached > 0 ? checkpoints[Reached - 1] : null;

    /// <summary>Run time at each crossing, in route order.</summary>
    public IReadOnlyList<float> CumulativeTimes => cumulativeTimes;

    /// <summary>
    /// Raised when a checkpoint is legally crossed.
    /// Payload: volume, 1-based index, total, split since previous checkpoint, cumulative run time.
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
    /// Called by <see cref="CheckpointVolume"/> on entry. Returns false when the crossing is out
    /// of order, which leaves the gate armed for a later, legal crossing.
    /// </summary>
    internal bool TryActivate(CheckpointVolume volume)
    {
        int index = checkpoints.IndexOf(volume);
        if (index < 0)
        {
            Debug.LogWarning($"[Checkpoints] '{volume.name}' is not on the route list; ignoring.", volume);
            return false;
        }

        if (requireSequential ? index != Reached : index < Reached)
        {
            return false;
        }

        Reached = index + 1;

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
        Reached = 0;
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
