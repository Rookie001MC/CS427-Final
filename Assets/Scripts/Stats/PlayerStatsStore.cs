using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Read-only view of one level's career counters.</summary>
public readonly struct LevelStats
{
    public int Attempts { get; }
    public int Completions { get; }
    public int Checkpoints { get; }

    public LevelStats(int attempts, int completions, int checkpoints)
    {
        Attempts = attempts;
        Completions = completions;
        Checkpoints = checkpoints;
    }
}

/// <summary>
/// The authoritative persistent player statistics.
///
/// Built to the same contract as <see cref="RunRecordStore"/> - one versioned JSON document behind
/// an <see cref="IRunRecordPersistence"/> slot, validated on load, never throwing on a save it
/// does not recognise - and for the same reason: a statistics screen that crashes or resets on an
/// older save is worse than one that starts at zero.
///
/// It is a separate document from the run records rather than an extension of them, and that is
/// the point: the records file is the personal-best ledger and this is the career ledger. Merging
/// them would mean rewriting the record document on every death, and a corrupt career count would
/// take the personal bests with it. Two documents, one key each, and neither can erase the other.
///
/// Best times are not stored here at all. <see cref="RunRecordStore"/> owns them; the Player Stats
/// view reads both stores and never gets two answers to the same question.
/// </summary>
public sealed class PlayerStatsStore
{
    /// <summary>
    /// How many finished attempts the Recent Runs panel can draw from.
    ///
    /// Five is what the panel shows; the extra two are so that dropping one unreadable entry does
    /// not empty the list, and the cap exists at all so the save cannot grow without bound.
    /// </summary>
    public const int RecentRunCapacity = 7;

    private const int CurrentVersion = 1;

    private static PlayerStatsStore defaultStore;

    private readonly IRunRecordPersistence persistence;
    private PlayerStatsData data = NewPlayer();
    private bool dirty;

    public static PlayerStatsStore Default =>
        defaultStore ??= new PlayerStatsStore(new PlayerPrefsPlayerStatsPersistence());

    public PlayerStatsStore(IRunRecordPersistence persistence)
    {
        this.persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        LoadValidated();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetDefaultStore() => defaultStore = null;

    /// <summary>A save with nothing in it. Every number a new player sees comes from here.</summary>
    private static PlayerStatsData NewPlayer() => new PlayerStatsData { version = CurrentVersion };

    // ---------------------------------------------------------------- reads

    public int TotalRuns => data.totalRuns;
    public int CompletedRuns => data.completedRuns;
    public int FailedRuns => data.failedRuns;
    public int Deaths => data.deaths;
    public int CheckpointsReached => data.checkpointsReached;

    /// <summary>Metres of horizontal travel under the player's own control.</summary>
    public float DistanceMetres => data.distanceMetres;

    /// <summary>Fastest validated horizontal speed, m/s.</summary>
    public float MaxSpeed => data.maxSpeed;

    /// <summary>Seconds spent in <see cref="RunState.Running"/>, summed over every attempt.</summary>
    public float RunSeconds => data.runSeconds;

    /// <summary>True when nothing has ever been recorded, so the view can say so plainly.</summary>
    public bool IsEmpty => data.totalRuns == 0 && data.recentRuns.Count == 0
                           && HighestActionCount() == 0;

    public int GetAction(ParkourAction action) => action switch
    {
        ParkourAction.Jump => data.jumps,
        ParkourAction.Slide => data.slides,
        ParkourAction.Vault => data.vaults,
        ParkourAction.Mantle => data.mantles,
        ParkourAction.WallRun => data.wallRuns,
        ParkourAction.WallJump => data.wallJumps,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown action.")
    };

    /// <summary>
    /// The largest single action count, which is what the breakdown bars are drawn against.
    /// Zero for a new player, and the view must treat that as "no bars" rather than divide by it.
    /// </summary>
    public int HighestActionCount()
    {
        int highest = 0;

        for (int i = 0; i < PlayerStatsFormat.Actions.Length; i++)
        {
            int count = GetAction(PlayerStatsFormat.Actions[i]);
            if (count > highest)
            {
                highest = count;
            }
        }

        return highest;
    }

    /// <summary>Career counters for one level. All zeroes when the level has never been played.</summary>
    public LevelStats GetLevel(string levelKey)
    {
        LevelStatsData level = Find(levelKey);
        return level == null
            ? new LevelStats(0, 0, 0)
            : new LevelStats(level.attempts, level.completions, level.checkpoints);
    }

    /// <summary>Finished attempts, most recent first. Never null; empty for a new player.</summary>
    public IReadOnlyList<RunLogData> RecentRuns => data.recentRuns;

    // ---------------------------------------------------------------- run lifecycle

    /// <summary>
    /// A run actually started: the countdown finished and the player has control.
    ///
    /// Driven by <see cref="GameManager.RunStarted"/> and nothing else, which is what keeps
    /// opening a menu, loading the main menu, entering a level-selection screen and editor
    /// initialisation from counting as attempts - none of them raise it.
    /// </summary>
    public void RecordRunStarted(string levelKey, LevelTrack track)
    {
        LevelStatsData level = Resolve(levelKey, track);
        if (level == null)
        {
            return;
        }

        data.totalRuns++;
        level.attempts++;
        dirty = true;
        Flush();
    }

    /// <summary>
    /// The game reported the run as successfully completed.
    ///
    /// <paramref name="track"/> is the level's own, so a training course can never add to the
    /// main run's completions: the two are separate <see cref="LevelStatsData"/> rows and the
    /// view reads the main run's row by its own key.
    /// </summary>
    public void RecordRunFinished(string levelKey, string displayName, LevelTrack track,
        GameMode mode, float seconds, bool personalBest)
    {
        LevelStatsData level = Resolve(levelKey, track);
        if (level == null || !IsFiniteNonNegative(seconds))
        {
            return;
        }

        data.completedRuns++;
        level.completions++;
        Log(levelKey, displayName, track, mode, RunOutcome.Completed, seconds, personalBest);
        dirty = true;
        Flush();
    }

    /// <summary>
    /// The attempt ended without finishing, and the architecture can say so for certain.
    ///
    /// That is exactly one case: No-Checkpoint Mode, where a death ends the run by the mode's own
    /// rule. A death in Checkpoint Mode is not a failed run - the same attempt continues - and
    /// quitting to the menu mid-run is indistinguishable from alt-tabbing, so neither is counted.
    /// </summary>
    public void RecordRunFailed(string levelKey, string displayName, LevelTrack track,
        GameMode mode, float seconds)
    {
        LevelStatsData level = Resolve(levelKey, track);
        if (level == null || !IsFiniteNonNegative(seconds))
        {
            return;
        }

        data.failedRuns++;
        Log(levelKey, displayName, track, mode, RunOutcome.Failed, seconds, false);
        dirty = true;
        Flush();
    }

    // ---------------------------------------------------------------- events

    /// <summary>One completed action. Called once per action, never once per frame.</summary>
    public void RecordAction(ParkourAction action)
    {
        switch (action)
        {
            case ParkourAction.Jump: data.jumps++; break;
            case ParkourAction.Slide: data.slides++; break;
            case ParkourAction.Vault: data.vaults++; break;
            case ParkourAction.Mantle: data.mantles++; break;
            case ParkourAction.WallRun: data.wallRuns++; break;
            case ParkourAction.WallJump: data.wallJumps++; break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown action.");
        }

        // Deliberately not flushed: a run produces hundreds of these, and a disk write per jump
        // is the one thing this must never do. The run lifecycle events flush them.
        dirty = true;
    }

    public void RecordDeath()
    {
        data.deaths++;
        dirty = true;
        Flush();
    }

    /// <summary>
    /// One legal new checkpoint. Driven by <see cref="CheckpointManager.CheckpointReached"/>,
    /// which is raised only for a crossing that counts under the route's own rules, so a
    /// re-entered gate and a respawn add nothing.
    /// </summary>
    public void RecordCheckpoint(string levelKey, LevelTrack track)
    {
        data.checkpointsReached++;

        LevelStatsData level = Resolve(levelKey, track);
        if (level != null)
        {
            level.checkpoints++;
        }

        dirty = true;
        Flush();
    }

    /// <summary>
    /// Adds validated travel. The teleport rejection happens before this, in
    /// <see cref="MotionSampler"/>, because only the sampler knows what the previous frame was.
    /// </summary>
    public void AddDistance(float metres)
    {
        if (!IsFiniteNonNegative(metres) || metres <= 0f)
        {
            return;
        }

        data.distanceMetres += metres;
        dirty = true;
    }

    /// <summary>Raises the career peak if this sample is both plausible and faster.</summary>
    public void ReportSpeed(float metresPerSecond)
    {
        if (!MotionSampler.IsPlausibleSpeed(metresPerSecond) || metresPerSecond <= data.maxSpeed)
        {
            return;
        }

        data.maxSpeed = metresPerSecond;
        dirty = true;
    }

    /// <summary>Adds active gameplay time. Callers only pass frames spent actually running.</summary>
    public void AddRunTime(float seconds)
    {
        if (!IsFiniteNonNegative(seconds) || seconds <= 0f)
        {
            return;
        }

        data.runSeconds += seconds;
        dirty = true;
    }

    // ---------------------------------------------------------------- persistence

    /// <summary>Writes the document if anything has changed since the last write.</summary>
    public void Flush()
    {
        if (!dirty)
        {
            return;
        }

        try
        {
            data.version = CurrentVersion;
            persistence.Save(JsonUtility.ToJson(data));
            dirty = false;
        }
        catch (Exception exception)
        {
            // Left dirty so the next flush retries; the in-memory career is still correct.
            Debug.LogWarning($"[Stats] Could not persist player statistics: {exception.Message}");
        }
    }

    private void Log(string levelKey, string displayName, LevelTrack track, GameMode mode,
        RunOutcome outcome, float seconds, bool personalBest)
    {
        data.recentRuns.Insert(0, new RunLogData
        {
            levelKey = levelKey,
            displayName = string.IsNullOrWhiteSpace(displayName) ? levelKey : displayName,
            track = (int)track,
            mode = (int)mode,
            outcome = (int)outcome,
            seconds = seconds,
            utcTicks = DateTime.UtcNow.Ticks,
            personalBest = personalBest
        });

        while (data.recentRuns.Count > RecentRunCapacity)
        {
            data.recentRuns.RemoveAt(data.recentRuns.Count - 1);
        }
    }

    private LevelStatsData Find(string levelKey)
    {
        if (string.IsNullOrWhiteSpace(levelKey))
        {
            return null;
        }

        return data.levels.Find(level =>
            string.Equals(level.levelKey, levelKey, StringComparison.Ordinal));
    }

    /// <summary>
    /// The row for this level, created if it is new. Returns null for an unusable identity rather
    /// than throwing: a statistics recorder must never be the thing that stops a run.
    /// </summary>
    private LevelStatsData Resolve(string levelKey, LevelTrack track)
    {
        if (string.IsNullOrWhiteSpace(levelKey) || !IsKnownTrack((int)track))
        {
            Debug.LogWarning($"[Stats] Ignored an event for level key '{levelKey}' and track " +
                             $"{(int)track}.");
            return null;
        }

        LevelStatsData level = Find(levelKey);
        if (level == null)
        {
            level = new LevelStatsData { levelKey = levelKey, track = (int)track };
            data.levels.Add(level);
        }
        else
        {
            // The catalogue is allowed to move a level between tracks; the row follows it.
            level.track = (int)track;
        }

        return level;
    }

    private void LoadValidated()
    {
        string json;
        try
        {
            json = persistence.Load();
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Stats] Could not read player statistics: {exception.Message}");
            return;
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            // No save yet. Not a fault: this is a new player, and every value is already zero.
            return;
        }

        PlayerStatsData loaded;
        try
        {
            loaded = JsonUtility.FromJson<PlayerStatsData>(json);
        }
        catch (Exception)
        {
            loaded = null;
        }

        if (loaded == null || loaded.version != CurrentVersion)
        {
            Debug.LogWarning("[Stats] Ignored malformed or unsupported player statistics; " +
                             "starting from a zeroed career.");
            return;
        }

        data = Sanitise(loaded);
    }

    /// <summary>
    /// Brings a loaded document up to something every reader can trust: no negatives, no NaN, no
    /// null lists, no rows without an identity. A field the document omitted is already zero.
    /// </summary>
    private static PlayerStatsData Sanitise(PlayerStatsData loaded)
    {
        loaded.totalRuns = AtLeastZero(loaded.totalRuns);
        loaded.completedRuns = AtLeastZero(loaded.completedRuns);
        loaded.failedRuns = AtLeastZero(loaded.failedRuns);
        loaded.deaths = AtLeastZero(loaded.deaths);
        loaded.checkpointsReached = AtLeastZero(loaded.checkpointsReached);
        loaded.distanceMetres = AtLeastZero(loaded.distanceMetres);
        loaded.runSeconds = AtLeastZero(loaded.runSeconds);

        // A save that claims a physically impossible peak would make the whole screen a lie, so
        // the same ceiling the live sampler uses is applied on the way in.
        loaded.maxSpeed = MotionSampler.IsPlausibleSpeed(loaded.maxSpeed) ? loaded.maxSpeed : 0f;

        loaded.jumps = AtLeastZero(loaded.jumps);
        loaded.slides = AtLeastZero(loaded.slides);
        loaded.vaults = AtLeastZero(loaded.vaults);
        loaded.mantles = AtLeastZero(loaded.mantles);
        loaded.wallRuns = AtLeastZero(loaded.wallRuns);
        loaded.wallJumps = AtLeastZero(loaded.wallJumps);

        loaded.levels ??= new List<LevelStatsData>();
        loaded.levels.RemoveAll(level => level == null
                                         || string.IsNullOrWhiteSpace(level.levelKey)
                                         || !IsKnownTrack(level.track));

        for (int i = 0; i < loaded.levels.Count; i++)
        {
            LevelStatsData level = loaded.levels[i];
            level.attempts = AtLeastZero(level.attempts);
            level.completions = AtLeastZero(level.completions);
            level.checkpoints = AtLeastZero(level.checkpoints);
        }

        loaded.recentRuns ??= new List<RunLogData>();
        loaded.recentRuns.RemoveAll(run => run == null
                                           || string.IsNullOrWhiteSpace(run.levelKey)
                                           || !IsKnownTrack(run.track)
                                           || !IsKnownMode(run.mode)
                                           || !IsKnownOutcome(run.outcome)
                                           || !IsFiniteNonNegative(run.seconds));

        while (loaded.recentRuns.Count > RecentRunCapacity)
        {
            loaded.recentRuns.RemoveAt(loaded.recentRuns.Count - 1);
        }

        for (int i = 0; i < loaded.recentRuns.Count; i++)
        {
            RunLogData run = loaded.recentRuns[i];

            if (string.IsNullOrWhiteSpace(run.displayName))
            {
                run.displayName = run.levelKey;
            }

            if (run.utcTicks < 0L)
            {
                run.utcTicks = 0L;
            }
        }

        return loaded;
    }

    private static int AtLeastZero(int value) => value < 0 ? 0 : value;

    private static float AtLeastZero(float value) => IsFiniteNonNegative(value) ? value : 0f;

    private static bool IsKnownTrack(int track) =>
        track == (int)LevelTrack.Training || track == (int)LevelTrack.MainRun;

    private static bool IsKnownMode(int mode) =>
        mode == (int)GameMode.Checkpoint || mode == (int)GameMode.NoCheckpoint;

    private static bool IsKnownOutcome(int outcome) =>
        outcome == (int)RunOutcome.Completed || outcome == (int)RunOutcome.Failed;

    private static bool IsFiniteNonNegative(float value) =>
        value >= 0f && !float.IsNaN(value) && !float.IsInfinity(value);
}
