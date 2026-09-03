using System;

/// <summary>
/// How every statistic is written on screen.
///
/// Formatting lives next to the data rather than in the view because the empty state is part of
/// the format: a career with no runs in it has to read as "--:--.--" and "0.0", not as a blank or
/// a zero that could be mistaken for a real result. Keeping that decision in one place is what
/// stops half the screen inventing its own placeholder.
///
/// Pure and engine-free, so the strings the screen shows can be asserted without a canvas.
/// </summary>
public static class PlayerStatsFormat
{
    /// <summary>
    /// The six parkour actions, in the order the breakdown draws them. Also the order the store
    /// scans when it looks for the highest count, so the bars and the normaliser cannot disagree.
    /// </summary>
    public static readonly ParkourAction[] Actions =
    {
        ParkourAction.Jump,
        ParkourAction.Slide,
        ParkourAction.Vault,
        ParkourAction.Mantle,
        ParkourAction.WallRun,
        ParkourAction.WallJump
    };

    /// <summary>Shown wherever a time has never been set.</summary>
    public const string NoTime = "--:--.--";

    /// <summary>Shown in place of the Recent Runs list when nothing has been recorded.</summary>
    public const string NoRuns = "NO RUNS RECORDED";

    public static string Label(ParkourAction action) => action switch
    {
        ParkourAction.Jump => "JUMPS",
        ParkourAction.Slide => "SLIDES",
        ParkourAction.Vault => "VAULTS",
        ParkourAction.Mantle => "MANTLES",
        ParkourAction.WallRun => "WALL RUNS",
        ParkourAction.WallJump => "WALL JUMPS",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown action.")
    };

    /// <summary>
    /// A career count. Two digits minimum, because the reference sets these figures as fixed-width
    /// plates and a lone "0" under a label reads as a missing value rather than a real zero.
    /// Thousands are grouped once they exist.
    /// </summary>
    public static string Count(int value)
    {
        if (value < 0)
        {
            value = 0;
        }

        return value < 100 ? value.ToString("00") : value.ToString("N0");
    }

    /// <summary>Speed in m/s to one decimal. The unit is drawn separately, as in the reference.</summary>
    public static string Speed(float metresPerSecond)
        => (IsUsable(metresPerSecond) ? metresPerSecond : 0f).ToString("0.0");

    /// <summary>Travel in kilometres to one decimal. Under 100 m still reads as 0.0.</summary>
    public static string Distance(float metres)
        => ((IsUsable(metres) ? metres : 0f) / 1000f).ToString("0.0");

    /// <summary>
    /// Active gameplay time as "00H 00M". Hours are not wrapped at 24: this is a career total,
    /// and a player with 30 hours in it should be told so.
    /// </summary>
    public static string RunTime(float seconds)
    {
        if (!IsUsable(seconds))
        {
            seconds = 0f;
        }

        int totalMinutes = (int)(seconds / 60f);
        return $"{totalMinutes / 60:00}H {totalMinutes % 60:00}M";
    }

    /// <summary>A finish time, or <see cref="NoTime"/> when there is none.</summary>
    public static string Time(float seconds)
        => IsUsable(seconds) ? RunTimer.Format(seconds) : NoTime;

    /// <summary>
    /// The real local date a run ended, as YYYY-MM-DD. Empty when the entry carries no timestamp,
    /// which is the only honest answer - a date is never invented to fill the column.
    /// </summary>
    public static string Date(long utcTicks)
    {
        if (utcTicks <= 0L || utcTicks > DateTime.MaxValue.Ticks)
        {
            return string.Empty;
        }

        return new DateTime(utcTicks, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd");
    }

    /// <summary>"TRAINING" or "MAIN RUN", in the menu's own voice.</summary>
    public static string Track(LevelTrack track)
        => track == LevelTrack.MainRun ? "MAIN RUN" : "TRAINING";

    /// <summary>The ruleset a run was played under, short enough for a list row.</summary>
    public static string Mode(GameMode mode)
        => mode == GameMode.NoCheckpoint ? "NO-CP" : "CP";

    /// <summary>
    /// The one line of metadata under a run's name: which ruleset, and when.
    ///
    /// The track is not in here - it has its own label on the row above, and its own accent
    /// colour - so this line stays short enough to sit beside the run's status without the two
    /// running into each other. A run with no timestamp says only the ruleset rather than padding
    /// the column with an invented date.
    /// </summary>
    public static string RunMeta(RunLogData run)
    {
        if (run == null)
        {
            return string.Empty;
        }

        string mode = Mode((GameMode)run.mode);
        string date = Date(run.utcTicks);

        return string.IsNullOrEmpty(date) ? mode : $"{mode}  -  {date}";
    }

    /// <summary>What became of a run: a personal best, a plain finish, or a failed attempt.</summary>
    public static string RunStatus(RunLogData run)
    {
        if (run == null)
        {
            return string.Empty;
        }

        if (run.outcome == (int)RunOutcome.Failed)
        {
            return "FAILED";
        }

        return run.personalBest ? "PB" : "COMPLETE";
    }

    /// <summary>
    /// The 0..1 length of an action's bar, measured against the player's highest action count.
    ///
    /// Relative, and labelled as such on screen, because there is no scale on which "84 wall
    /// runs" is a score out of a hundred. With nothing recorded the answer is 0, never a divide.
    /// </summary>
    public static float BarFraction(int count, int highest)
    {
        if (count <= 0 || highest <= 0)
        {
            return 0f;
        }

        return count >= highest ? 1f : (float)count / highest;
    }

    private static bool IsUsable(float value)
        => value >= 0f && !float.IsNaN(value) && !float.IsInfinity(value);
}
