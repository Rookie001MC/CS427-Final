using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public sealed class GameManagerRecoveryTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    private GameObject root;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        RunSession.Clear();
        yield return new EnterPlayMode();
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (root != null)
        {
            UnityEngine.Object.Destroy(root);
        }

        RunSession.Clear();
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator CheckpointDeath_AutomaticallyRecoversAfterThreeSecondPenalty()
    {
        Fixture fixture = BuildFixture(countdownStepSeconds: 0.01f);
        var recoveryTicks = new List<int>();
        EventInfo recoveryTickEvent = typeof(GameManager).GetEvent("RecoveryCountdownTick");
        Assert.That(recoveryTickEvent, Is.Not.Null);
        recoveryTickEvent.AddEventHandler(fixture.Game, (Action<int>)recoveryTicks.Add);

        yield return WaitForState(fixture.Game, RunState.Running);

        Assert.That(Activate(fixture.Checkpoints, fixture.Checkpoint), Is.True);
        yield return null;

        float timerBeforeDeath = fixture.Timer.ElapsedSeconds;
        fixture.Player.transform.position = new Vector3(0f, -6f, 0f);
        yield return WaitForState(fixture.Game, RunState.Recovering);

        Assert.That(GetPrivate<bool>(fixture.FallDetector, "armed"), Is.False);
        Assert.That(fixture.Game.Die("duplicate"), Is.False);

        float timerDuringRecovery = fixture.Timer.ElapsedSeconds;
        yield return new WaitForSecondsRealtime(2.75f);

        Assert.That(fixture.Game.State, Is.EqualTo(RunState.Recovering));
        Assert.That(fixture.Timer.ElapsedSeconds, Is.GreaterThan(timerDuringRecovery));
        Assert.That(recoveryTicks, Is.EqualTo(new[] { 3, 2, 1 }));

        yield return new WaitForSecondsRealtime(0.5f);

        Assert.That(fixture.Game.State, Is.EqualTo(RunState.Running));
        Assert.That(fixture.Game.Deaths, Is.EqualTo(1));
        Assert.That(fixture.Checkpoints.Reached, Is.EqualTo(1));
        Assert.That(fixture.Timer.ElapsedSeconds, Is.GreaterThan(timerBeforeDeath));
        Assert.That(fixture.Player.transform.position, Is.EqualTo(fixture.Checkpoint.transform.position));
        Assert.That(GetPrivate<bool>(fixture.FallDetector, "armed"), Is.True);
    }

    [UnityTest]
    public IEnumerator NoCheckpointDeath_StopsTimerAndWaitsForDecision()
    {
        RunSession.Select(GameMode.NoCheckpoint, "recovery-test");
        Fixture fixture = BuildFixture(countdownStepSeconds: 0.01f);

        // A scene with a death overlay in it - which is what "waits for a decision" presupposes.
        fixture.Game.AddDeathDecisionResponder();

        yield return WaitForState(fixture.Game, RunState.Running);

        Assert.That(Activate(fixture.Checkpoints, fixture.Checkpoint), Is.True);
        yield return null;
        Assert.That(fixture.Timer.ElapsedSeconds, Is.GreaterThan(0f));

        Assert.That(fixture.Game.Die("test death"), Is.True);
        float stoppedAt = fixture.Timer.ElapsedSeconds;

        Assert.That(fixture.Game.State, Is.EqualTo(RunState.Recovering));
        Assert.That(fixture.Game.Deaths, Is.EqualTo(1));
        Assert.That(fixture.Checkpoints.Reached, Is.EqualTo(1));
        Assert.That(fixture.Timer.IsRunning, Is.False);

        yield return new WaitForSecondsRealtime(0.75f);

        Assert.That(fixture.Game.State, Is.EqualTo(RunState.Recovering));
        Assert.That(fixture.Timer.ElapsedSeconds, Is.EqualTo(stoppedAt).Within(0.01f));
    }

    [UnityTest]
    public IEnumerator RetryAfterNoCheckpointDeath_ResetsRunAndStartsFreshCountdown()
    {
        RunSession.Select(GameMode.NoCheckpoint, "recovery-test");
        Fixture fixture = BuildFixture(countdownStepSeconds: 0.3f);
        yield return WaitForState(fixture.Game, RunState.Running);

        var restartedCountdownTicks = new List<string>();
        fixture.Game.CountdownTick += restartedCountdownTicks.Add;

        Assert.That(Activate(fixture.Checkpoints, fixture.Checkpoint), Is.True);
        yield return null;
        Assert.That(fixture.Game.Die("test death"), Is.True);

        fixture.Game.RestartRun();
        yield return null;

        Assert.That(fixture.Game.State, Is.EqualTo(RunState.Countdown));
        Assert.That(fixture.Game.Deaths, Is.Zero);
        Assert.That(fixture.Checkpoints.Reached, Is.Zero);
        Assert.That(fixture.Timer.ElapsedSeconds, Is.Zero);
        Assert.That(fixture.Timer.IsRunning, Is.False);
        Assert.That(fixture.Player.transform.position, Is.EqualTo(fixture.LevelStart.position));
        Assert.That(restartedCountdownTicks, Is.EqualTo(new[] { "1" }));
    }


    // ------------------------------------------------------------------ mode reaches the runtime

    /// <summary>
    /// The two modes produce different behaviour from the same death, in a scene that has no death
    /// overlay in it.
    ///
    /// This is the hole the bug lived in. `Die` read the rules correctly and took the
    /// No-Checkpoint branch, but that branch resolved nothing - it stopped the clock and waited for
    /// a view to call `RestartRun`. Skybound City, which is the level PLAY launches, carries the
    /// run systems but no `GameplayUIController`, so there was nobody to ask: the run sat in
    /// Recovering for ever and No-Checkpoint Mode had no effect a player could see. A rule a scene
    /// has to opt into is not a rule.
    /// </summary>
    [UnityTest]
    public IEnumerator CheckpointDeath_RespawnsWhereNoCheckpointDeathRestartsTheWholeRun()
    {
        RunSession.Select(GameMode.Checkpoint, "mode-flow-test");
        Fixture checkpointRun = BuildFixture(countdownStepSeconds: 0.01f);

        Assert.That(checkpointRun.Game.Mode, Is.EqualTo(GameMode.Checkpoint));
        Assert.That(checkpointRun.Game.CanPresentDeathDecision, Is.False,
            "This fixture has no death overlay, which is the case under test.");

        yield return WaitForState(checkpointRun.Game, RunState.Running);
        Assert.That(Activate(checkpointRun.Checkpoints, checkpointRun.Checkpoint), Is.True);
        Assert.That(checkpointRun.Game.Die("test death"), Is.True);

        yield return WaitForState(checkpointRun.Game, RunState.Running);

        // Checkpoint Mode: back at the checkpoint, progress and death count intact.
        Assert.That(checkpointRun.Game.Deaths, Is.EqualTo(1));
        Assert.That(checkpointRun.Checkpoints.Reached, Is.EqualTo(1));
        Assert.That(checkpointRun.Player.transform.position,
            Is.EqualTo(checkpointRun.Checkpoint.transform.position));

        UnityEngine.Object.DestroyImmediate(root);
        root = null;

        RunSession.Select(GameMode.NoCheckpoint, "mode-flow-test");
        Fixture failRun = BuildFixture(countdownStepSeconds: 0.01f);

        Assert.That(failRun.Game.Mode, Is.EqualTo(GameMode.NoCheckpoint));
        Assert.That(failRun.Game.CanPresentDeathDecision, Is.False);

        yield return WaitForState(failRun.Game, RunState.Running);
        Assert.That(Activate(failRun.Checkpoints, failRun.Checkpoint), Is.True);
        Assert.That(failRun.Checkpoints.Reached, Is.EqualTo(1));
        Assert.That(failRun.Game.Die("test death"), Is.True);

        Assert.That(failRun.Game.State, Is.EqualTo(RunState.Recovering));
        Assert.That(failRun.Timer.IsRunning, Is.False,
            "No-Checkpoint Mode stops the clock the moment the attempt ends.");

        // No-Checkpoint Mode with nobody to ask: the whole run goes back to the start.
        yield return WaitForState(failRun.Game, RunState.Running);

        Assert.That(failRun.Game.Deaths, Is.Zero,
            "The attempt ended, so the run restarted and the death count went with it.");
        Assert.That(failRun.Checkpoints.Reached, Is.Zero,
            "No-Checkpoint Mode kept the checkpoint the player had already crossed.");
        Assert.That(failRun.Player.transform.position, Is.EqualTo(failRun.LevelStart.position));
        Assert.That(failRun.Player.transform.position,
            Is.Not.EqualTo(failRun.Checkpoint.transform.position));
    }

    /// <summary>
    /// A scene that CAN ask still asks. The fix must not turn No-Checkpoint Mode into an automatic
    /// restart everywhere - Levels 1 and 2 present the decision, and that is the mode's real shape.
    /// </summary>
    [UnityTest]
    public IEnumerator NoCheckpointDeath_StillWaitsWhereTheSceneCanPresentTheDecision()
    {
        RunSession.Select(GameMode.NoCheckpoint, "mode-flow-test");
        Fixture fixture = BuildFixture(countdownStepSeconds: 0.01f);
        fixture.Game.AddDeathDecisionResponder();

        Assert.That(fixture.Game.CanPresentDeathDecision, Is.True);

        yield return WaitForState(fixture.Game, RunState.Running);
        Assert.That(Activate(fixture.Checkpoints, fixture.Checkpoint), Is.True);
        Assert.That(fixture.Game.Die("test death"), Is.True);

        // Well past the three seconds an unattended failure would have taken to restart.
        yield return new WaitForSecondsRealtime(4f);

        Assert.That(fixture.Game.State, Is.EqualTo(RunState.Recovering));
        Assert.That(fixture.Game.Deaths, Is.EqualTo(1));
        Assert.That(fixture.Checkpoints.Reached, Is.EqualTo(1));
    }

    /// <summary>
    /// The mode the menu chose is the mode the level runs in, across the scene load that separates
    /// them.
    ///
    /// `RunSession` is static and the loader writes it before `LoadSceneAsync`, so this is the
    /// claim that ties the two halves together: whatever the menu selected, a `GameManager` that
    /// wakes up in a freshly loaded scene reports it.
    /// </summary>
    [UnityTest]
    public IEnumerator TheSelectedModeSurvivesTheSceneLoad()
    {
        foreach (GameMode mode in new[] { GameMode.NoCheckpoint, GameMode.Checkpoint })
        {
            RunSession.Select(mode, "IndustrialParkour");

            SceneManager.LoadScene("IndustrialParkour");

            // One frame to activate, one for Awake/Start to have run.
            yield return null;
            yield return null;

            GameManager game = UnityEngine.Object.FindFirstObjectByType<GameManager>();
            Assert.That(game, Is.Not.Null, "IndustrialParkour has no GameManager.");
            Assert.That(RunSession.ActiveMode, Is.EqualTo(mode),
                "The scene load cleared or replaced the selected mode.");
            Assert.That(RunSession.ActiveRecordKey, Is.EqualTo("IndustrialParkour"));
            Assert.That(game.Mode, Is.EqualTo(mode),
                $"The level is running in {game.Mode} after the menu selected {mode}.");
            Assert.That(game.Rules.DeathAction, Is.EqualTo(mode == GameMode.Checkpoint
                ? DeathRecoveryAction.RespawnAtCheckpoint
                : DeathRecoveryAction.AwaitPlayerDecision));

            // And that scene does carry a death overlay, so it presents the decision rather than
            // restarting on the player's behalf.
            Assert.That(game.CanPresentDeathDecision, Is.True,
                "IndustrialParkour has a GameplayUIController and should be able to ask.");
        }
    }

    private Fixture BuildFixture(float countdownStepSeconds)
    {
        root = new GameObject("~GameManagerRecoveryTests");
        root.SetActive(false);

        GameObject systemsObject = new GameObject("Systems");
        systemsObject.transform.SetParent(root.transform);
        RunTimer timer = systemsObject.AddComponent<RunTimer>();
        CheckpointManager checkpoints = systemsObject.AddComponent<CheckpointManager>();
        FallDetector fallDetector = systemsObject.AddComponent<FallDetector>();
        RespawnManager respawn = systemsObject.AddComponent<RespawnManager>();
        GameManager game = systemsObject.AddComponent<GameManager>();

        GameObject playerObject = new GameObject("Player");
        playerObject.transform.SetParent(root.transform);
        PlayerFreezeController player = playerObject.AddComponent<PlayerFreezeController>();

        GameObject startObject = new GameObject("LevelStart");
        startObject.transform.SetParent(root.transform);
        startObject.transform.position = new Vector3(1f, 2f, 3f);

        GameObject checkpointObject = new GameObject("Checkpoint");
        checkpointObject.transform.SetParent(root.transform);
        checkpointObject.transform.position = new Vector3(7f, 8f, 9f);
        checkpointObject.AddComponent<BoxCollider>();
        CheckpointVolume checkpoint = checkpointObject.AddComponent<CheckpointVolume>();

        SetPrivate(checkpoints, "checkpoints", new List<CheckpointVolume> { checkpoint });
        SetPrivate(checkpoints, "runTimer", timer);

        SetPrivate(respawn, "player", player);
        SetPrivate(respawn, "levelStart", startObject.transform);
        SetPrivate(respawn, "checkpoints", checkpoints);

        SetPrivate(fallDetector, "target", player.transform);
        SetPrivate(fallDetector, "deathHeight", -5f);

        SetPrivate(game, "runTimer", timer);
        SetPrivate(game, "checkpoints", checkpoints);
        SetPrivate(game, "respawn", respawn);
        SetPrivate(game, "player", player);
        SetPrivate(game, "fallDetector", fallDetector);
        SetPrivate(game, "countdownFrom", 1);
        SetPrivate(game, "countdownStepSeconds", countdownStepSeconds);
        root.SetActive(true);

        return new Fixture
        {
            Game = game,
            Timer = timer,
            Checkpoints = checkpoints,
            Player = player,
            FallDetector = fallDetector,
            LevelStart = startObject.transform,
            Checkpoint = checkpoint
        };
    }

    /// <summary>
    /// Waits for a run state, bounded by the wall clock rather than by a frame count.
    ///
    /// A frame budget is the wrong unit here. The countdown this waits on is measured in seconds
    /// (WaitForSeconds), while play-mode test frames are driven as fast as the editor can tick
    /// them - the test runner explicitly removes the editor's frame throttle for a run. 180
    /// frames of an empty scene can therefore expire in well under the 0.3s countdown step
    /// RetryAfterNoCheckpointDeath configures, which is exactly how that test failed: it timed
    /// out in Countdown and never reached the death it was about to trigger. The two tests using
    /// a 0.01s step were fast enough to beat the budget and passed on the same code.
    /// </summary>
    private static IEnumerator WaitForState(GameManager game, RunState expected)
    {
        const float timeoutSeconds = 10f;
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;

        while (game.State != expected && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }

        Assert.That(game.State, Is.EqualTo(expected));
    }

    private static bool Activate(CheckpointManager checkpoints, CheckpointVolume checkpoint)
    {
        MethodInfo method = typeof(CheckpointManager).GetMethod("TryActivate", PrivateInstance);
        Assert.That(method, Is.Not.Null);
        return (bool)method.Invoke(checkpoints, new object[] { checkpoint });
    }

    private static void SetPrivate<T>(object target, string fieldName, T value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, PrivateInstance);
        Assert.That(field, Is.Not.Null, $"Missing private field '{fieldName}'.");
        field.SetValue(target, value);
    }

    private static T GetPrivate<T>(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(fieldName, PrivateInstance);
        Assert.That(field, Is.Not.Null, $"Missing private field '{fieldName}'.");
        return (T)field.GetValue(target);
    }

    private sealed class Fixture
    {
        public GameManager Game;
        public RunTimer Timer;
        public CheckpointManager Checkpoints;
        public PlayerFreezeController Player;
        public FallDetector FallDetector;
        public Transform LevelStart;
        public CheckpointVolume Checkpoint;
    }
}
