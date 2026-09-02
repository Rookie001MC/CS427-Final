using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Derived run statistics that the HUD and end-of-run panels read: live speed, peak speed, and
/// the session's best finish time and per-checkpoint splits.
///
/// Records are keyed by <see cref="LevelInfo.RecordKey"/> and the active <see cref="GameMode"/>,
/// so the two modes retain independent persistent personal bests.
/// </summary>
public sealed class RunStatsTracker : MonoBehaviour
{
    [SerializeField] private CharacterController playerController;
    [SerializeField] private CheckpointManager checkpoints;
    [SerializeField] private LevelInfo levelInfo;

    [Tooltip("Samples above this are discarded so a bad traversal or teleport sample cannot " +
             "poison the run's peak speed.")]
    [SerializeField, Min(1f)] private float plausibleSpeedCeiling = 40f;

    private BasicFirstPersonController playerMovement;

    /// <summary>Horizontal speed in m/s. Vertical fall speed is excluded deliberately.</summary>
    public float CurrentSpeed { get; private set; }

    /// <summary>Peak horizontal speed reached in the current run.</summary>
    public float MaxSpeed { get; private set; }

    private string Key => levelInfo != null ? levelInfo.RecordKey : gameObject.scene.name;
    private GameMode Mode => RunSession.ActiveMode;
    private RunRecordStore Store => RunRecordStore.Default;

    /// <summary>Best finish time for this level and mode, or -1 when none has been set.</summary>
    public float BestTime =>
        Store.TryGetBest(Key, Mode, out float bestTime) ? bestTime : -1f;

    public bool HasBest => Store.TryGetBest(Key, Mode, out _);

    public static bool TryGetBest(string recordKey, GameMode mode, out float bestTime) =>
        RunRecordStore.Default.TryGetBest(recordKey, mode, out bestTime);

    public static int CountCompletedModes(string recordKey) =>
        RunRecordStore.Default.CountCompletedModes(recordKey);

    private void Update()
    {
        if (playerController == null)
        {
            return;
        }

        if (playerMovement == null)
        {
            playerMovement = playerController.GetComponent<BasicFirstPersonController>();
        }

        if (playerMovement == null)
        {
            return;
        }

        float speed = playerMovement.CurrentHorizontalSpeed;

        // Drop the teleport frame rather than letting it poison the run's peak.
        if (speed > plausibleSpeedCeiling)
        {
            return;
        }

        CurrentSpeed = speed;

        if (speed > MaxSpeed)
        {
            MaxSpeed = speed;
        }
    }

    /// <summary>Clears per-run values. Level records survive.</summary>
    public void ResetRun()
    {
        MaxSpeed = 0f;
        CurrentSpeed = 0f;
    }

    /// <summary>
    /// Best recorded split for a 1-based checkpoint index, or -1 when this checkpoint has never
    /// been completed in a finished run of this level.
    /// </summary>
    public float GetBestSplit(int oneBasedIndex)
    {
        return Store.GetBestSplit(Key, Mode, oneBasedIndex);
    }

    /// <summary>
    /// Commits a finished run. Splits are only promoted when the run as a whole was a personal
    /// best, so the comparison column always describes one coherent reference run.
    /// </summary>
    /// <returns>True when this run set a new best for this level.</returns>
    public bool CommitFinishedRun(float finishTime)
    {
        var sectionSplits = new List<float>();
        if (checkpoints != null)
        {
            IReadOnlyList<float> cumulative = checkpoints.CumulativeTimes;
            float previous = 0f;

            for (int i = 0; i < cumulative.Count; i++)
            {
                sectionSplits.Add(cumulative[i] - previous);
                previous = cumulative[i];
            }
        }

        return Store.Commit(Key, Mode, finishTime, sectionSplits);
    }
}
