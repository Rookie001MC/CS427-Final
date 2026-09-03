using System;
using System.Collections.Generic;

/// <summary>
/// One discrete parkour action the player can perform.
///
/// An enum rather than six counters passed around by name, because every consumer - the
/// controller that raises it, the recorder that counts it, the store that persists it and the
/// view that draws the bar - has to agree on the same six, and a misspelled string would be a
/// silently missing statistic.
/// </summary>
public enum ParkourAction
{
    Jump = 0,
    Slide = 1,
    Vault = 2,
    Mantle = 3,
    WallRun = 4,
    WallJump = 5
}

/// <summary>
/// How a run ended.
///
/// Only two values, and deliberately so: the game reports a completion (the finish line accepted)
/// and No-Checkpoint Mode's own rule ends an attempt on death. Nothing else in the current
/// architecture can distinguish "abandoned" from "still playing", so nothing else is invented
/// here - see <see cref="PlayerStatsStore.RecordRunFailed"/>.
/// </summary>
public enum RunOutcome
{
    Completed = 0,
    Failed = 1
}

/// <summary>
/// Per-level career counters.
///
/// Best times are deliberately absent: <see cref="RunRecordStore"/> already owns them, per level
/// and per mode, and a second copy would be a second answer to "what is my personal best". This
/// holds only what that store has no place for - how many times the level was started, how many
/// of those finished, and how many checkpoints were reached in it.
/// </summary>
[Serializable]
public sealed class LevelStatsData
{
    public string levelKey;

    /// <summary>Serialized <see cref="LevelTrack"/>. Training and the main run never mix.</summary>
    public int track = -1;

    public int attempts;
    public int completions;
    public int checkpoints;
}

/// <summary>
/// One finished attempt, as the Recent Runs panel reads it.
///
/// The timestamp is the real UTC tick count at the moment the run ended. Nothing about a run is
/// back-dated or synthesised: a save with no entries renders as NO RUNS RECORDED rather than as
/// plausible-looking history.
/// </summary>
[Serializable]
public sealed class RunLogData
{
    public string levelKey;
    public string displayName;
    public int track = -1;

    /// <summary>Serialized <see cref="GameMode"/>.</summary>
    public int mode = -1;

    /// <summary>Serialized <see cref="RunOutcome"/>.</summary>
    public int outcome = -1;

    /// <summary>Run time in seconds. For a failed attempt, how long it lasted.</summary>
    public float seconds = -1f;

    /// <summary>DateTime.UtcNow.Ticks when the run ended. 0 when unknown.</summary>
    public long utcTicks;

    /// <summary>True when this run set a new personal best for its level and mode.</summary>
    public bool personalBest;
}

/// <summary>
/// The one authoritative persistent statistics record.
///
/// Plain serializable data with no behaviour, so <see cref="PlayerStatsStore"/> owns every rule
/// about how it is changed and validated and this stays something JsonUtility can round-trip.
/// Every field starts at zero, which is exactly the state a new player is supposed to see.
/// </summary>
[Serializable]
public sealed class PlayerStatsData
{
    /// <summary>-1 so a JSON document that omits it is rejected rather than read as version 0.</summary>
    public int version = -1;

    // ---- career ----------------------------------------------------------------
    public int totalRuns;
    public int completedRuns;
    public int failedRuns;
    public int deaths;
    public int checkpointsReached;
    public float distanceMetres;
    public float maxSpeed;
    public float runSeconds;

    // ---- parkour actions -------------------------------------------------------
    public int jumps;
    public int slides;
    public int vaults;
    public int mantles;
    public int wallRuns;
    public int wallJumps;

    public List<LevelStatsData> levels = new List<LevelStatsData>();
    public List<RunLogData> recentRuns = new List<RunLogData>();
}
