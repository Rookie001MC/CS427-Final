using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// The career ledger.
///
/// These are about the two claims the Player Stats screen makes that a screenshot cannot check:
/// that every figure on it came from something that actually happened, and that a new player is
/// shown zeroes rather than the mockup's numbers. So: a fresh store is empty, a round trip loses
/// nothing, an unreadable save degrades to empty instead of throwing, the run records are never
/// touched, and every counter moves exactly once per event.
/// </summary>
public sealed class PlayerStatsStoreTests
{
    private const string MainRunKey = "SkyboundCity";
    private const string TrainingKey = "IndustrialParkour";

    private sealed class MemorySlot : IRunRecordPersistence
    {
        public string Json = string.Empty;
        public int Saves;
        public bool ThrowOnSave;

        public string Load() => Json;

        public void Save(string json)
        {
            Saves++;

            if (ThrowOnSave)
            {
                throw new InvalidOperationException("save failed");
            }

            Json = json;
        }
    }

    // ------------------------------------------------------------------ data / save

    [Test]
    public void NewPlayer_StartsAtZeroWithNoHistory()
    {
        var store = new PlayerStatsStore(new MemorySlot());

        Assert.That(store.IsEmpty, Is.True);
        Assert.That(store.TotalRuns, Is.Zero);
        Assert.That(store.CompletedRuns, Is.Zero);
        Assert.That(store.FailedRuns, Is.Zero);
        Assert.That(store.Deaths, Is.Zero);
        Assert.That(store.CheckpointsReached, Is.Zero);
        Assert.That(store.DistanceMetres, Is.Zero);
        Assert.That(store.MaxSpeed, Is.Zero);
        Assert.That(store.RunSeconds, Is.Zero);
        Assert.That(store.HighestActionCount(), Is.Zero);
        Assert.That(store.RecentRuns, Is.Empty);

        foreach (ParkourAction action in PlayerStatsFormat.Actions)
        {
            Assert.That(store.GetAction(action), Is.Zero, $"{action} should start at zero.");
        }

        LevelStats level = store.GetLevel(MainRunKey);
        Assert.That(level.Attempts, Is.Zero);
        Assert.That(level.Completions, Is.Zero);
        Assert.That(level.Checkpoints, Is.Zero);

        // The screen a new player sees, spelled out.
        Assert.That(PlayerStatsFormat.Count(store.TotalRuns), Is.EqualTo("00"));
        Assert.That(PlayerStatsFormat.Speed(store.MaxSpeed), Is.EqualTo("0.0"));
        Assert.That(PlayerStatsFormat.Distance(store.DistanceMetres), Is.EqualTo("0.0"));
        Assert.That(PlayerStatsFormat.RunTime(store.RunSeconds), Is.EqualTo("00H 00M"));
        Assert.That(PlayerStatsFormat.Time(-1f), Is.EqualTo("--:--.--"));
    }

    [Test]
    public void SaveAndLoad_RoundTripsEveryField()
    {
        var slot = new MemorySlot();
        var first = new PlayerStatsStore(slot);

        first.RecordRunStarted(MainRunKey, LevelTrack.MainRun);
        first.RecordRunStarted(MainRunKey, LevelTrack.MainRun);
        first.RecordRunFinished(MainRunKey, "SKYBOUND CITY", LevelTrack.MainRun,
            GameMode.Checkpoint, 92.5f, true);
        first.RecordCheckpoint(MainRunKey, LevelTrack.MainRun);
        first.RecordCheckpoint(MainRunKey, LevelTrack.MainRun);
        first.RecordDeath();
        first.AddDistance(1234.5f);
        first.ReportSpeed(10.25f);
        first.AddRunTime(3725f);

        foreach (ParkourAction action in PlayerStatsFormat.Actions)
        {
            first.RecordAction(action);
        }

        first.RecordAction(ParkourAction.WallRun);
        first.Flush();

        var reloaded = new PlayerStatsStore(slot);

        Assert.That(reloaded.TotalRuns, Is.EqualTo(2));
        Assert.That(reloaded.CompletedRuns, Is.EqualTo(1));
        Assert.That(reloaded.Deaths, Is.EqualTo(1));
        Assert.That(reloaded.CheckpointsReached, Is.EqualTo(2));
        Assert.That(reloaded.DistanceMetres, Is.EqualTo(1234.5f).Within(0.01f));
        Assert.That(reloaded.MaxSpeed, Is.EqualTo(10.25f).Within(0.001f));
        Assert.That(reloaded.RunSeconds, Is.EqualTo(3725f).Within(0.01f));
        Assert.That(reloaded.GetAction(ParkourAction.WallRun), Is.EqualTo(2));
        Assert.That(reloaded.GetAction(ParkourAction.Mantle), Is.EqualTo(1));
        Assert.That(reloaded.HighestActionCount(), Is.EqualTo(2));

        LevelStats level = reloaded.GetLevel(MainRunKey);
        Assert.That(level.Attempts, Is.EqualTo(2));
        Assert.That(level.Completions, Is.EqualTo(1));
        Assert.That(level.Checkpoints, Is.EqualTo(2));

        Assert.That(reloaded.RecentRuns.Count, Is.EqualTo(1));
        Assert.That(reloaded.RecentRuns[0].displayName, Is.EqualTo("SKYBOUND CITY"));
        Assert.That(reloaded.RecentRuns[0].seconds, Is.EqualTo(92.5f).Within(0.01f));
        Assert.That(reloaded.RecentRuns[0].personalBest, Is.True);
        Assert.That(reloaded.RecentRuns[0].utcTicks, Is.GreaterThan(0L),
            "A logged run carries the real time it ended, never a fabricated date.");
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("{not json")]
    [TestCase("{\"version\":99,\"totalRuns\":147}")]
    [TestCase("{\"totalRuns\":147}")]
    public void UnreadableSave_LoadsAsANewPlayerWithoutThrowing(string json)
    {
        PlayerStatsStore store = null;

        Assert.DoesNotThrow(() => store = new PlayerStatsStore(new MemorySlot { Json = json }));
        Assert.That(store.IsEmpty, Is.True);
        Assert.That(store.TotalRuns, Is.Zero);

        // ...and the zeroed career is still usable, so a bad save is not a dead screen.
        store.RecordRunStarted(MainRunKey, LevelTrack.MainRun);
        Assert.That(store.TotalRuns, Is.EqualTo(1));
    }

    [Test]
    public void MissingFields_LoadAsZeroRatherThanFailingTheWholeDocument()
    {
        // A version 1 document written before a field existed: everything present is kept.
        const string json = "{\"version\":1,\"totalRuns\":4,\"jumps\":9}";
        var store = new PlayerStatsStore(new MemorySlot { Json = json });

        Assert.That(store.TotalRuns, Is.EqualTo(4));
        Assert.That(store.GetAction(ParkourAction.Jump), Is.EqualTo(9));
        Assert.That(store.CompletedRuns, Is.Zero);
        Assert.That(store.MaxSpeed, Is.Zero);
        Assert.That(store.RecentRuns, Is.Empty);
    }

    [Test]
    public void ImpossibleValues_AreClampedRatherThanShown()
    {
        const string json = "{\"version\":1,\"totalRuns\":-5,\"deaths\":-1," +
            "\"distanceMetres\":-40.0,\"maxSpeed\":900.0,\"runSeconds\":-3.0,\"wallRuns\":-2}";
        var store = new PlayerStatsStore(new MemorySlot { Json = json });

        Assert.That(store.TotalRuns, Is.Zero);
        Assert.That(store.Deaths, Is.Zero);
        Assert.That(store.DistanceMetres, Is.Zero);
        Assert.That(store.RunSeconds, Is.Zero);
        Assert.That(store.GetAction(ParkourAction.WallRun), Is.Zero);
        Assert.That(store.MaxSpeed, Is.Zero,
            "900 m/s is not a speed this game can produce, so it is not a peak it can display.");
    }

    [Test]
    public void RowsWithoutAnIdentity_AreDroppedAndTheRestSurvive()
    {
        const string json = "{\"version\":1," +
            "\"levels\":[{\"levelKey\":\"\",\"track\":1,\"attempts\":3}," +
            "{\"levelKey\":\"SkyboundCity\",\"track\":9,\"attempts\":4}," +
            "{\"levelKey\":\"SkyboundCity\",\"track\":1,\"attempts\":5,\"completions\":2}]," +
            "\"recentRuns\":[{\"levelKey\":\"\",\"track\":1,\"mode\":0,\"outcome\":0,\"seconds\":10.0}," +
            "{\"levelKey\":\"SkyboundCity\",\"track\":1,\"mode\":7,\"outcome\":0,\"seconds\":10.0}," +
            "{\"levelKey\":\"SkyboundCity\",\"track\":1,\"mode\":0,\"outcome\":0,\"seconds\":42.0}]}";

        var store = new PlayerStatsStore(new MemorySlot { Json = json });

        LevelStats level = store.GetLevel(MainRunKey);
        Assert.That(level.Attempts, Is.EqualTo(5), "The one usable row is the one that is kept.");
        Assert.That(level.Completions, Is.EqualTo(2));

        Assert.That(store.RecentRuns.Count, Is.EqualTo(1));
        Assert.That(store.RecentRuns[0].seconds, Is.EqualTo(42f).Within(0.01f));
    }

    [Test]
    public void PlayerStats_AndRunRecords_AreSeparateDocuments()
    {
        Assert.That(PlayerPrefsPlayerStatsPersistence.StorageKey,
            Is.Not.EqualTo(PlayerPrefsRunRecordPersistence.StorageKey),
            "Sharing a key would mean one ledger could overwrite the other.");

        var recordSlot = new MemorySlot();
        var records = new RunRecordStore(recordSlot);
        records.Commit(MainRunKey, GameMode.Checkpoint, 88f, new[] { 44f, 44f });
        string recordsAfterCommit = recordSlot.Json;

        var statsSlot = new MemorySlot();
        var stats = new PlayerStatsStore(statsSlot);
        stats.RecordRunStarted(MainRunKey, LevelTrack.MainRun);
        stats.RecordRunFinished(MainRunKey, "SKYBOUND CITY", LevelTrack.MainRun,
            GameMode.Checkpoint, 70f, true);
        stats.RecordDeath();
        stats.Flush();

        Assert.That(recordSlot.Json, Is.EqualTo(recordsAfterCommit),
            "Recording a career must not rewrite the personal-best ledger.");
        Assert.That(records.TryGetBest(MainRunKey, GameMode.Checkpoint, out float best), Is.True);
        Assert.That(best, Is.EqualTo(88f));
        Assert.That(records.GetBestSplit(MainRunKey, GameMode.Checkpoint, 1), Is.EqualTo(44f));
    }

    [Test]
    public void Actions_AreNeverWrittenToDiskOnTheirOwn()
    {
        var slot = new MemorySlot();
        var store = new PlayerStatsStore(slot);

        for (int i = 0; i < 500; i++)
        {
            store.RecordAction(ParkourAction.Jump);
            store.AddDistance(0.15f);
            store.AddRunTime(1f / 60f);
        }

        Assert.That(slot.Saves, Is.Zero,
            "A save per jump is the one thing a per-frame recorder must never do.");

        store.Flush();
        Assert.That(slot.Saves, Is.EqualTo(1));

        store.Flush();
        Assert.That(slot.Saves, Is.EqualTo(1), "Nothing changed, so nothing is rewritten.");
    }

    [Test]
    public void SaveFailure_KeepsTheCareerAndRetriesLater()
    {
        var slot = new MemorySlot { ThrowOnSave = true };
        var store = new PlayerStatsStore(slot);

        store.RecordDeath();
        Assert.That(store.Deaths, Is.EqualTo(1));

        slot.ThrowOnSave = false;
        store.Flush();
        Assert.That(slot.Json, Is.Not.Empty, "The failed write is retried, not forgotten.");
    }

    // ------------------------------------------------------------------ runs

    [Test]
    public void RunAttempt_IncrementsOncePerStartedRun()
    {
        var store = new PlayerStatsStore(new MemorySlot());

        store.RecordRunStarted(MainRunKey, LevelTrack.MainRun);

        Assert.That(store.TotalRuns, Is.EqualTo(1));
        Assert.That(store.GetLevel(MainRunKey).Attempts, Is.EqualTo(1));

        store.RecordRunStarted(MainRunKey, LevelTrack.MainRun);
        Assert.That(store.TotalRuns, Is.EqualTo(2));
    }

    [Test]
    public void RunAttempt_IgnoresAnEventWithNoLevelIdentity()
    {
        var store = new PlayerStatsStore(new MemorySlot());

        // The store warns when it refuses an event; that warning is the documented behaviour, so
        // it must not be allowed to fail the test.
        LogAssert.ignoreFailingMessages = true;
        store.RecordRunStarted(string.Empty, LevelTrack.MainRun);
        store.RecordRunStarted(null, LevelTrack.MainRun);
        store.RecordRunStarted(MainRunKey, (LevelTrack)42);
        LogAssert.ignoreFailingMessages = false;

        Assert.That(store.TotalRuns, Is.Zero,
            "An event that cannot say which level it belongs to is not an attempt.");
    }

    [Test]
    public void RunCompletion_IncrementsOncePerFinish()
    {
        var store = new PlayerStatsStore(new MemorySlot());

        store.RecordRunStarted(MainRunKey, LevelTrack.MainRun);
        store.RecordRunFinished(MainRunKey, "SKYBOUND CITY", LevelTrack.MainRun,
            GameMode.Checkpoint, 100f, false);

        Assert.That(store.CompletedRuns, Is.EqualTo(1));
        Assert.That(store.GetLevel(MainRunKey).Completions, Is.EqualTo(1));
        Assert.That(store.RecentRuns.Count, Is.EqualTo(1));
    }

    [Test]
    public void Restarting_AddsAnAttemptWithoutDuplicatingTheCompletedRun()
    {
        var store = new PlayerStatsStore(new MemorySlot());

        store.RecordRunStarted(MainRunKey, LevelTrack.MainRun);
        store.RecordRunFinished(MainRunKey, "SKYBOUND CITY", LevelTrack.MainRun,
            GameMode.Checkpoint, 100f, true);

        // REPLAY, R, or a No-Checkpoint death restarting the whole run.
        store.RecordRunStarted(MainRunKey, LevelTrack.MainRun);
        store.RecordRunStarted(MainRunKey, LevelTrack.MainRun);

        Assert.That(store.TotalRuns, Is.EqualTo(3));
        Assert.That(store.CompletedRuns, Is.EqualTo(1),
            "Restarting a finished run must not bank the finish again.");
        Assert.That(store.GetLevel(MainRunKey).Completions, Is.EqualTo(1));
        Assert.That(store.RecentRuns.Count, Is.EqualTo(1));
    }

    [Test]
    public void TrainingCompletion_IsNeverAMainRunCompletion()
    {
        var store = new PlayerStatsStore(new MemorySlot());

        store.RecordRunStarted(TrainingKey, LevelTrack.Training);
        store.RecordRunFinished(TrainingKey, "INDUSTRIAL PARKOUR", LevelTrack.Training,
            GameMode.Checkpoint, 40f, true);
        store.RecordRunStarted("UIWorldDemo", LevelTrack.Training);
        store.RecordRunFinished("UIWorldDemo", "NEON DISTRICT", LevelTrack.Training,
            GameMode.NoCheckpoint, 55f, true);
        store.RecordCheckpoint(TrainingKey, LevelTrack.Training);

        LevelStats mainRun = store.GetLevel(MainRunKey);
        Assert.That(mainRun.Attempts, Is.Zero);
        Assert.That(mainRun.Completions, Is.Zero,
            "Clearing a practice course is not clearing Skybound City.");
        Assert.That(mainRun.Checkpoints, Is.Zero);

        Assert.That(store.GetLevel(TrainingKey).Completions, Is.EqualTo(1));
        Assert.That(store.CompletedRuns, Is.EqualTo(2),
            "...but both are still real completions of the career.");
    }

    [Test]
    public void SkyboundCity_IsTheRunTheMainRunPanelCounts()
    {
        var store = new PlayerStatsStore(new MemorySlot());

        store.RecordRunStarted(MainRunKey, LevelTrack.MainRun);
        store.RecordRunFinished(MainRunKey, "SKYBOUND CITY", LevelTrack.MainRun,
            GameMode.NoCheckpoint, 210f, true);
        store.RecordCheckpoint(MainRunKey, LevelTrack.MainRun);
        store.RecordCheckpoint(MainRunKey, LevelTrack.MainRun);

        LevelStats mainRun = store.GetLevel(MainRunKey);
        Assert.That(mainRun.Attempts, Is.EqualTo(1));
        Assert.That(mainRun.Completions, Is.EqualTo(1));
        Assert.That(mainRun.Checkpoints, Is.EqualTo(2));
    }

    [Test]
    public void FailedRun_IsRecordedOnlyWhereTheModeCanProveIt()
    {
        var store = new PlayerStatsStore(new MemorySlot());

        // No-Checkpoint Mode: the death ended the attempt, which is a failure the architecture
        // can state. The recorder is the thing that decides this; the store just banks it.
        store.RecordRunStarted(MainRunKey, LevelTrack.MainRun);
        store.RecordDeath();
        store.RecordRunFailed(MainRunKey, "SKYBOUND CITY", LevelTrack.MainRun,
            GameMode.NoCheckpoint, 31.5f);

        Assert.That(store.FailedRuns, Is.EqualTo(1));
        Assert.That(store.CompletedRuns, Is.Zero);
        Assert.That(store.GetLevel(MainRunKey).Completions, Is.Zero,
            "A failed attempt must never touch the completion count.");
        Assert.That(store.RecentRuns[0].outcome, Is.EqualTo((int)RunOutcome.Failed));
        Assert.That(PlayerStatsFormat.RunStatus(store.RecentRuns[0]), Is.EqualTo("FAILED"));
    }

    [Test]
    public void RecentRuns_AreNewestFirstAndCapped()
    {
        var store = new PlayerStatsStore(new MemorySlot());

        for (int i = 0; i < PlayerStatsStore.RecentRunCapacity + 4; i++)
        {
            store.RecordRunStarted(MainRunKey, LevelTrack.MainRun);
            store.RecordRunFinished(MainRunKey, $"RUN {i:00}", LevelTrack.MainRun,
                GameMode.Checkpoint, 10f + i, false);
        }

        Assert.That(store.RecentRuns.Count, Is.EqualTo(PlayerStatsStore.RecentRunCapacity));
        Assert.That(store.RecentRuns[0].displayName, Is.EqualTo("RUN 10"),
            "The most recent finished attempt is the first one drawn.");
    }

    // ------------------------------------------------------------------ actions

    [Test]
    public void EachAction_CountsExactlyOncePerEvent()
    {
        var store = new PlayerStatsStore(new MemorySlot());

        foreach (ParkourAction action in PlayerStatsFormat.Actions)
        {
            store.RecordAction(action);

            Assert.That(store.GetAction(action), Is.EqualTo(1), $"{action} counted wrong.");

            foreach (ParkourAction other in PlayerStatsFormat.Actions)
            {
                if (other != action)
                {
                    Assert.That(store.GetAction(other), Is.LessThanOrEqualTo(1),
                        $"Recording {action} moved {other}.");
                }
            }
        }

        Assert.That(store.GetAction(ParkourAction.Jump), Is.EqualTo(1));
        Assert.That(store.GetAction(ParkourAction.WallJump), Is.EqualTo(1),
            "A wall jump is its own action and is never also a jump.");
    }

    [Test]
    public void Actions_CoverTheSixTheGameHas()
    {
        Assert.That(PlayerStatsFormat.Actions.Length, Is.EqualTo(6));
        Assert.That(PlayerStatsFormat.Actions,
            Is.Unique, "A repeated action would be drawn twice and normalised wrong.");

        foreach (ParkourAction action in PlayerStatsFormat.Actions)
        {
            Assert.That(PlayerStatsFormat.Label(action), Is.Not.Empty);
        }
    }

    [Test]
    public void UnknownAction_IsRejectedRatherThanCountedAsSomethingElse()
    {
        var store = new PlayerStatsStore(new MemorySlot());

        Assert.Throws<ArgumentOutOfRangeException>(() => store.RecordAction((ParkourAction)77));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.GetAction((ParkourAction)77));
    }

    // ------------------------------------------------------------------ gameplay

    [Test]
    public void Death_IncrementsOnceAndARespawnAddsNothing()
    {
        var store = new PlayerStatsStore(new MemorySlot());

        store.RecordRunStarted(MainRunKey, LevelTrack.MainRun);
        store.RecordDeath();

        Assert.That(store.Deaths, Is.EqualTo(1));

        // A respawn is a teleport and a state change. Neither is a death, and neither is wired to
        // one - the only path to the counter is GameManager.PlayerDied.
        var sampler = new MotionSampler();
        sampler.Discontinuity();
        sampler.TryAdvance(Vector3.zero, 1f / 60f, out _);

        Assert.That(store.Deaths, Is.EqualTo(1));
    }

    [Test]
    public void Checkpoint_IncrementsOncePerCrossingAndIsAttributedToItsLevel()
    {
        var store = new PlayerStatsStore(new MemorySlot());

        store.RecordCheckpoint(MainRunKey, LevelTrack.MainRun);
        store.RecordCheckpoint(MainRunKey, LevelTrack.MainRun);
        store.RecordCheckpoint(TrainingKey, LevelTrack.Training);

        Assert.That(store.CheckpointsReached, Is.EqualTo(3));
        Assert.That(store.GetLevel(MainRunKey).Checkpoints, Is.EqualTo(2));
        Assert.That(store.GetLevel(TrainingKey).Checkpoints, Is.EqualTo(1));
    }

    // ------------------------------------------------------------------ formatting

    [Test]
    public void BarFraction_IsRelativeAndNeverDividesByZero()
    {
        Assert.That(PlayerStatsFormat.BarFraction(0, 0), Is.Zero);
        Assert.That(PlayerStatsFormat.BarFraction(5, 0), Is.Zero);
        Assert.That(PlayerStatsFormat.BarFraction(0, 90), Is.Zero);
        Assert.That(PlayerStatsFormat.BarFraction(45, 90), Is.EqualTo(0.5f).Within(0.0001f));
        Assert.That(PlayerStatsFormat.BarFraction(90, 90), Is.EqualTo(1f));
        Assert.That(PlayerStatsFormat.BarFraction(120, 90), Is.EqualTo(1f),
            "A bar never runs past its own track.");
    }

    [Test]
    public void Formatting_NeverInventsAValue()
    {
        Assert.That(PlayerStatsFormat.Count(0), Is.EqualTo("00"));
        Assert.That(PlayerStatsFormat.Count(7), Is.EqualTo("07"));
        Assert.That(PlayerStatsFormat.Count(-3), Is.EqualTo("00"));
        Assert.That(PlayerStatsFormat.Count(147), Is.EqualTo("147"));

        Assert.That(PlayerStatsFormat.Speed(float.NaN), Is.EqualTo("0.0"));
        Assert.That(PlayerStatsFormat.Distance(38700f), Is.EqualTo("38.7"));
        Assert.That(PlayerStatsFormat.RunTime(3600f + 120f), Is.EqualTo("01H 02M"));
        Assert.That(PlayerStatsFormat.Time(float.NaN), Is.EqualTo(PlayerStatsFormat.NoTime));

        Assert.That(PlayerStatsFormat.Date(0L), Is.Empty,
            "An entry with no timestamp shows no date rather than a plausible one.");
        Assert.That(PlayerStatsFormat.Date(new DateTime(2026, 9, 3, 12, 0, 0,
            DateTimeKind.Utc).Ticks), Is.Not.Empty);

        Assert.That(PlayerStatsFormat.Track(LevelTrack.MainRun), Is.EqualTo("MAIN RUN"));
        Assert.That(PlayerStatsFormat.Track(LevelTrack.Training), Is.EqualTo("TRAINING"));
    }

    /// <summary>
    /// Every string this screen can draw fits inside a list row's box.
    ///
    /// The row is 533 units wide and split into fixed regions, so a metadata line that grew by
    /// three characters would silently sit on top of the status beside it. This is that bound
    /// stated in characters rather than measured in pixels, which is the part a layout test
    /// cannot see.
    /// </summary>
    [Test]
    public void RunRowStrings_StayWithinTheirColumns()
    {
        var run = new RunLogData
        {
            levelKey = MainRunKey,
            displayName = "SKYBOUND CITY",
            track = (int)LevelTrack.MainRun,
            mode = (int)GameMode.NoCheckpoint,
            outcome = (int)RunOutcome.Completed,
            seconds = 3599.99f,
            utcTicks = new DateTime(2026, 12, 31, 23, 0, 0, DateTimeKind.Utc).Ticks,
            personalBest = false
        };

        Assert.That(PlayerStatsFormat.RunMeta(run).Length, Is.LessThanOrEqualTo(20));
        Assert.That(PlayerStatsFormat.RunStatus(run).Length, Is.LessThanOrEqualTo(8));
        Assert.That(PlayerStatsFormat.Time(run.seconds).Length, Is.EqualTo(8));
        Assert.That(PlayerStatsFormat.Track((LevelTrack)run.track).Length,
            Is.LessThanOrEqualTo(8));

        run.utcTicks = 0L;
        Assert.That(PlayerStatsFormat.RunMeta(run), Is.EqualTo("NO-CP"));
    }
}
