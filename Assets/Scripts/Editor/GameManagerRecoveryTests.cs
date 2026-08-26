using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
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
            Object.Destroy(root);
        }

        RunSession.Clear();
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator CheckpointRecovery_ContinuesTimerRespawnsLatestCheckpointAndRearmsFallDetection()
    {
        Fixture fixture = BuildFixture(countdownStepSeconds: 0.01f);
        yield return WaitForState(fixture.Game, RunState.Running);

        Assert.That(Activate(fixture.Checkpoints, fixture.Checkpoint), Is.True);
        yield return null;

        float timerBeforeDeath = fixture.Timer.ElapsedSeconds;
        fixture.Player.transform.position = new Vector3(0f, -6f, 0f);
        yield return WaitForState(fixture.Game, RunState.Recovering);

        Assert.That(GetPrivate<bool>(fixture.FallDetector, "armed"), Is.False);
        Assert.That(fixture.Game.Die("duplicate"), Is.False);

        yield return WaitForState(fixture.Game, RunState.Running);

        Assert.That(fixture.Game.Deaths, Is.EqualTo(1));
        Assert.That(fixture.Checkpoints.Reached, Is.EqualTo(1));
        Assert.That(fixture.Timer.ElapsedSeconds, Is.GreaterThan(timerBeforeDeath));
        Assert.That(fixture.Player.transform.position, Is.EqualTo(fixture.Checkpoint.transform.position));
        Assert.That(GetPrivate<bool>(fixture.FallDetector, "armed"), Is.True);
    }

    [UnityTest]
    public IEnumerator NoCheckpointRecovery_ResetsRunAndStartsFreshCountdown()
    {
        RunSession.Select(GameMode.NoCheckpoint, "recovery-test");
        Fixture fixture = BuildFixture(countdownStepSeconds: 0.3f);
        yield return WaitForState(fixture.Game, RunState.Running);

        var restartedCountdownTicks = new List<string>();
        fixture.Game.CountdownTick += restartedCountdownTicks.Add;

        Assert.That(Activate(fixture.Checkpoints, fixture.Checkpoint), Is.True);
        yield return null;
        Assert.That(fixture.Timer.ElapsedSeconds, Is.GreaterThan(0f));

        Assert.That(fixture.Game.Die("test death"), Is.True);
        Assert.That(fixture.Game.State, Is.EqualTo(RunState.Recovering));
        Assert.That(fixture.Game.Deaths, Is.EqualTo(1));
        Assert.That(fixture.Checkpoints.Reached, Is.EqualTo(1));

        yield return new WaitForSecondsRealtime(0.1f);

        Assert.That(fixture.Game.State, Is.EqualTo(RunState.Countdown));
        Assert.That(fixture.Game.Deaths, Is.Zero);
        Assert.That(fixture.Checkpoints.Reached, Is.Zero);
        Assert.That(fixture.Timer.ElapsedSeconds, Is.Zero);
        Assert.That(fixture.Timer.IsRunning, Is.False);
        Assert.That(fixture.Player.transform.position, Is.EqualTo(fixture.LevelStart.position));
        Assert.That(restartedCountdownTicks, Is.EqualTo(new[] { "1" }));
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
        SetPrivate(game, "deathRecoverySeconds", 0.05f);

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

    private static IEnumerator WaitForState(GameManager game, RunState expected)
    {
        const int maxFrames = 180;

        for (int frame = 0; frame < maxFrames && game.State != expected; frame++)
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
