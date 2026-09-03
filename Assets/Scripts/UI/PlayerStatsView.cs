using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// The Player Stats screen.
///
/// Presentation only. It reads two stores - <see cref="PlayerStatsStore"/> for the career and
/// <see cref="RunRecordStore"/> for personal bests - and writes strings into the objects
/// <see cref="MainMenuBuilder"/> handed it. It never inspects a gameplay object, never searches
/// the scene, and never runs in Update: <see cref="Refresh"/> is called when the screen opens,
/// which is the only moment the numbers can have changed since it was last looked at.
///
/// Which level is the main run and which are the practice courses is read off each
/// <see cref="LevelEntry.Track"/>, exactly as <see cref="MenuController"/> does it, so moving a
/// level between tracks moves it on this screen too.
/// </summary>
public sealed class PlayerStatsView : MonoBehaviour
{
    [Header("Catalogue")]
    [Tooltip("The whole catalogue. The main run and the training records are read off Track.")]
    [SerializeField] private List<LevelEntry> levels = new List<LevelEntry>();

    [Header("Career")]
    [SerializeField] private TMP_Text totalRunsValue;
    [SerializeField] private TMP_Text completedRunsValue;
    [SerializeField] private TMP_Text maxSpeedValue;
    [SerializeField] private TMP_Text distanceValue;
    [SerializeField] private TMP_Text deathsValue;
    [SerializeField] private TMP_Text runTimeValue;
    [SerializeField] private TMP_Text failedRunsValue;
    [SerializeField] private TMP_Text checkpointsValue;

    [Header("Parkour breakdown")]
    [Tooltip("One per action, in PlayerStatsFormat.Actions order.")]
    [SerializeField] private List<TMP_Text> actionValues = new List<TMP_Text>();

    [Tooltip("Bar fills, in the same order. Width is driven by the anchor, never by a scale.")]
    [SerializeField] private List<RectTransform> actionBars = new List<RectTransform>();

    [Header("Recent runs")]
    [SerializeField] private List<RecentRunRowView> recentRows = new List<RecentRunRowView>();
    [SerializeField] private TMP_Text recentEmptyMessage;

    [Header("Main run")]
    [SerializeField] private TMP_Text mainRunName;
    [SerializeField] private TMP_Text mainRunAttempts;
    [SerializeField] private TMP_Text mainRunCompletions;
    [SerializeField] private TMP_Text mainRunBestTime;
    [SerializeField] private TMP_Text mainRunCheckpointBest;
    [SerializeField] private TMP_Text mainRunNoCheckpointBest;
    [SerializeField] private TMP_Text mainRunCheckpoints;

    [Header("Training records")]
    [SerializeField] private List<TMP_Text> trainingNames = new List<TMP_Text>();
    [SerializeField] private List<TMP_Text> trainingTimes = new List<TMP_Text>();

    /// <summary>The one level PLAY launches, or null if the catalogue names none.</summary>
    public LevelEntry MainRun
    {
        get
        {
            for (int i = 0; i < levels.Count; i++)
            {
                if (levels[i] != null && levels[i].IsMainRun)
                {
                    return levels[i];
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Rebuilds every readout from the persisted career.
    ///
    /// Safe to call at any time and cheap enough to call on every open: it walks six actions, five
    /// rows and one level, and allocates one small list of training entries.
    /// </summary>
    public void Refresh() => RefreshFrom(PlayerStatsStore.Default);

    /// <summary>
    /// Rebuilds every readout from a specific career document.
    ///
    /// Exists so the screen's geometry can be proved against a full career without a real save
    /// having to exist: the layout has to hold for four-digit counts and the longest level name
    /// in the catalogue, and that is not a thing a fresh install can demonstrate.
    /// </summary>
    public void RefreshFrom(PlayerStatsStore stats)
    {
        if (stats == null)
        {
            return;
        }

        RefreshCareer(stats);
        RefreshBreakdown(stats);
        RefreshRecentRuns(stats);
        RefreshMainRun(stats);
        RefreshTraining();
    }

    private void OnEnable() => Refresh();

    // ---------------------------------------------------------------- career

    private void RefreshCareer(PlayerStatsStore stats)
    {
        Set(totalRunsValue, PlayerStatsFormat.Count(stats.TotalRuns), stats.TotalRuns > 0);
        Set(completedRunsValue, PlayerStatsFormat.Count(stats.CompletedRuns),
            stats.CompletedRuns > 0);
        Set(maxSpeedValue, PlayerStatsFormat.Speed(stats.MaxSpeed), stats.MaxSpeed > 0f);
        Set(distanceValue, PlayerStatsFormat.Distance(stats.DistanceMetres),
            stats.DistanceMetres > 0f);
        Set(deathsValue, PlayerStatsFormat.Count(stats.Deaths), stats.Deaths > 0);
        Set(runTimeValue, PlayerStatsFormat.RunTime(stats.RunSeconds), stats.RunSeconds > 0f);
        Set(failedRunsValue, PlayerStatsFormat.Count(stats.FailedRuns), stats.FailedRuns > 0);
        Set(checkpointsValue, PlayerStatsFormat.Count(stats.CheckpointsReached),
            stats.CheckpointsReached > 0);
    }

    // ---------------------------------------------------------------- breakdown

    private void RefreshBreakdown(PlayerStatsStore stats)
    {
        int highest = stats.HighestActionCount();
        ParkourAction[] actions = PlayerStatsFormat.Actions;

        for (int i = 0; i < actions.Length; i++)
        {
            int count = stats.GetAction(actions[i]);

            if (i < actionValues.Count)
            {
                Set(actionValues[i], count.ToString(), count > 0);
            }

            if (i < actionBars.Count && actionBars[i] != null)
            {
                // Anchor-driven, like the loading bar: the fill is a fraction of its own track, so
                // it is correct at any resolution and no transform is ever scaled.
                float fraction = PlayerStatsFormat.BarFraction(count, highest);
                actionBars[i].anchorMin = new Vector2(0f, 0f);
                actionBars[i].anchorMax = new Vector2(fraction, 1f);
                actionBars[i].offsetMin = Vector2.zero;
                actionBars[i].offsetMax = Vector2.zero;
            }
        }
    }

    // ---------------------------------------------------------------- recent runs

    private void RefreshRecentRuns(PlayerStatsStore stats)
    {
        IReadOnlyList<RunLogData> runs = stats.RecentRuns;

        for (int i = 0; i < recentRows.Count; i++)
        {
            if (recentRows[i] != null)
            {
                recentRows[i].Bind(i < runs.Count ? runs[i] : null);
            }
        }

        if (recentEmptyMessage != null)
        {
            // Said in words rather than left blank: an empty panel reads as a screen that failed
            // to load, and this one is supposed to say that the career has not started yet.
            recentEmptyMessage.text = PlayerStatsFormat.NoRuns;
            recentEmptyMessage.enabled = runs.Count == 0;
        }
    }

    // ---------------------------------------------------------------- main run

    private void RefreshMainRun(PlayerStatsStore stats)
    {
        LevelEntry entry = MainRun;

        if (mainRunName != null)
        {
            mainRunName.text = entry != null ? entry.DisplayName : "NO MAIN RUN";
        }

        string key = entry != null ? entry.RecordKey : string.Empty;
        LevelStats level = string.IsNullOrEmpty(key)
            ? new LevelStats(0, 0, 0)
            : stats.GetLevel(key);

        Set(mainRunAttempts, PlayerStatsFormat.Count(level.Attempts), level.Attempts > 0);
        Set(mainRunCompletions, PlayerStatsFormat.Count(level.Completions), level.Completions > 0);
        Set(mainRunCheckpoints, PlayerStatsFormat.Count(level.Checkpoints), level.Checkpoints > 0);

        float checkpoint = BestFor(key, GameMode.Checkpoint);
        float noCheckpoint = BestFor(key, GameMode.NoCheckpoint);

        SetTime(mainRunCheckpointBest, checkpoint);
        SetTime(mainRunNoCheckpointBest, noCheckpoint);
        SetTime(mainRunBestTime, Faster(checkpoint, noCheckpoint));
    }

    // ---------------------------------------------------------------- training

    private void RefreshTraining()
    {
        // Built here rather than stored, for the same reason the menu builds it here: a level's
        // track lives in its own asset and this screen has to follow it.
        List<LevelEntry> training = new List<LevelEntry>();

        for (int i = 0; i < levels.Count; i++)
        {
            if (levels[i] != null && !levels[i].IsMainRun)
            {
                training.Add(levels[i]);
            }
        }

        for (int i = 0; i < trainingNames.Count; i++)
        {
            LevelEntry entry = i < training.Count ? training[i] : null;

            if (trainingNames[i] != null)
            {
                trainingNames[i].text = entry != null ? entry.DisplayName : string.Empty;
                trainingNames[i].enabled = entry != null;
            }

            if (i >= trainingTimes.Count || trainingTimes[i] == null)
            {
                continue;
            }

            if (entry == null)
            {
                trainingTimes[i].enabled = false;
                continue;
            }

            trainingTimes[i].enabled = true;
            SetTime(trainingTimes[i], Faster(
                BestFor(entry.RecordKey, GameMode.Checkpoint),
                BestFor(entry.RecordKey, GameMode.NoCheckpoint)));
        }
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>The personal best for a level and mode, or -1 when there is none.</summary>
    private static float BestFor(string recordKey, GameMode mode)
    {
        if (string.IsNullOrWhiteSpace(recordKey))
        {
            return -1f;
        }

        return RunRecordStore.Default.TryGetBest(recordKey, mode, out float best) ? best : -1f;
    }

    /// <summary>The better of two bests, either of which may be absent.</summary>
    private static float Faster(float a, float b)
    {
        if (a < 0f)
        {
            return b;
        }

        if (b < 0f)
        {
            return a;
        }

        return Mathf.Min(a, b);
    }

    /// <summary>
    /// A value and the colour that says whether it is real.
    ///
    /// Nothing is hidden and nothing is faked: a zero is written as a zero, in the muted label
    /// colour, so an untouched statistic reads as untouched rather than as a result.
    /// </summary>
    private static void Set(TMP_Text target, string value, bool earned)
    {
        if (target == null)
        {
            return;
        }

        target.text = value;
        target.color = earned ? UITheme.White : UITheme.Dim;
    }

    private static void SetTime(TMP_Text target, float seconds)
    {
        if (target == null)
        {
            return;
        }

        bool has = seconds >= 0f;
        target.text = has ? RunTimer.Format(seconds) : PlayerStatsFormat.NoTime;
        target.color = has ? UITheme.Cyan : UITheme.Dim;
    }
}
