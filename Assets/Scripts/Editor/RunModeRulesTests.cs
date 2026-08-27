using System;
using NUnit.Framework;

public sealed class RunModeRulesTests
{
    [Test]
    public void Checkpoint_ContinuesThroughLatestCheckpoint()
    {
        RunModeRules rules = RunModeRules.For(GameMode.Checkpoint);
        Assert.That(rules.DeathAction, Is.EqualTo(DeathRecoveryAction.RespawnAtCheckpoint));
        Assert.That(rules.DisplayName, Is.EqualTo("CHECKPOINT MODE"));
        Assert.That(rules.ProgressName, Is.EqualTo("CHECKPOINT"));
        Assert.That(rules.DeathHeadline, Is.EqualTo("RECOVERING"));
    }

    [Test]
    public void NoCheckpoint_WaitsForThePlayersDecision()
    {
        RunModeRules rules = RunModeRules.For(GameMode.NoCheckpoint);
        Assert.That(rules.DeathAction.ToString(), Is.EqualTo("AwaitPlayerDecision"));
        Assert.That(rules.DisplayName, Is.EqualTo("NO-CHECKPOINT MODE"));
        Assert.That(rules.ProgressName, Is.EqualTo("SPLIT"));
        Assert.That(rules.DeathHeadline, Is.EqualTo("RUN FAILED"));
    }

    [Test]
    public void UnknownMode_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RunModeRules.For((GameMode)99));
    }

    [Test]
    public void GameManager_ExposesRecoveryCountdownTicksForTheView()
    {
        var countdownEvent = typeof(GameManager).GetEvent("RecoveryCountdownTick");
        Assert.That(countdownEvent, Is.Not.Null);
        Assert.That(countdownEvent.EventHandlerType, Is.EqualTo(typeof(Action<int>)));
    }
}
