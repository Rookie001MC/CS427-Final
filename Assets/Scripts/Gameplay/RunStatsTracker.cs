using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Derived run statistics that the HUD and end-of-run panels read: live speed, peak speed, and
/// the session's best finish time and per-checkpoint splits.
///
/// Records are keyed by <see cref="LevelInfo.RecordKey"/>, so two levels loaded in the same
/// session never share a "personal best". They are held in memory only - this is not the save
/// system; Phase 5 replaces the store behind these accessors with persisted values.
/// </summary>
public sealed class RunStatsTracker : MonoBehaviour
{
    private sealed class LevelRecord
    {
        public float BestTime = -1f;
        public readonly List<float> BestSplits = new List<float>();
    }

    [SerializeField] private CharacterController playerController;
    [SerializeField] private CheckpointManager checkpoints;
    [SerializeField] private LevelInfo levelInfo;

    [Tooltip("Samples above this are discarded. CharacterController.velocity is derived from the " +
             "last Move delta, so a respawn teleport reports a huge bogus speed for one frame.")]
    [SerializeField, Min(1f)] private float plausibleSpeedCeiling = 40f;

    private static readonly Dictionary<string, LevelRecord> Records = new Dictionary<string, LevelRecord>();

    /// <summary>Horizontal speed in m/s. Vertical fall speed is excluded deliberately.</summary>
    public float CurrentSpeed { get; private set; }

    /// <summary>Peak horizontal speed reached in the current run.</summary>
    public float MaxSpeed { get; private set; }

    private string Key => levelInfo != null ? levelInfo.RecordKey : gameObject.scene.name;

    private LevelRecord Record
    {
        get
        {
            string key = Key;
            if (!Records.TryGetValue(key, out LevelRecord record))
            {
                record = new LevelRecord();
                Records[key] = record;
            }

            return record;
        }
    }

    /// <summary>Best finish time for this level this session, or -1 when none has been set.</summary>
    public float BestTime => Record.BestTime;

    public bool HasBest => Record.BestTime >= 0f;

    /// <summary>
    /// Reads a level's session best without needing its scene loaded, so the menu can show a
    /// record set earlier in the same session. Still memory-only - Phase 5 replaces the store.
    /// </summary>
    public static bool TryGetBest(string recordKey, out float bestTime)
    {
        bestTime = -1f;

        if (string.IsNullOrEmpty(recordKey) || !Records.TryGetValue(recordKey, out LevelRecord record))
        {
            return false;
        }

        bestTime = record.BestTime;
        return bestTime >= 0f;
    }

    private void Update()
    {
        if (playerController == null)
        {
            return;
        }

        Vector3 v = playerController.velocity;
        v.y = 0f;
        float speed = v.magnitude;

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
        List<float> splits = Record.BestSplits;
        int i = oneBasedIndex - 1;
        return i >= 0 && i < splits.Count ? splits[i] : -1f;
    }

    /// <summary>
    /// Commits a finished run. Splits are only promoted when the run as a whole was a personal
    /// best, so the comparison column always describes one coherent reference run.
    /// </summary>
    /// <returns>True when this run set a new best for this level.</returns>
    public bool CommitFinishedRun(float finishTime)
    {
        LevelRecord record = Record;

        if (record.BestTime >= 0f && finishTime >= record.BestTime)
        {
            return false;
        }

        record.BestTime = finishTime;
        record.BestSplits.Clear();

        if (checkpoints != null)
        {
            IReadOnlyList<float> cumulative = checkpoints.CumulativeTimes;
            float previous = 0f;

            for (int i = 0; i < cumulative.Count; i++)
            {
                record.BestSplits.Add(cumulative[i] - previous);
                previous = cumulative[i];
            }
        }

        return true;
    }
}
