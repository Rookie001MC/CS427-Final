using System;
using NUnit.Framework;

public sealed class RunSessionTests
{
    [SetUp]
    public void SetUp() => RunSession.Clear();

    [TearDown]
    public void TearDown() => RunSession.Clear();

    [Test]
    public void Clear_DefaultsToCheckpointWithoutASelection()
    {
        Assert.That(RunSession.HasSelection, Is.False);
        Assert.That(RunSession.ActiveMode, Is.EqualTo(GameMode.Checkpoint));
        Assert.That(RunSession.ActiveRecordKey, Is.Empty);
    }

    [Test]
    public void Select_RetainsModeAndRecordKey()
    {
        RunSession.Select(GameMode.NoCheckpoint, "industrial");

        Assert.That(RunSession.HasSelection, Is.True);
        Assert.That(RunSession.ActiveMode, Is.EqualTo(GameMode.NoCheckpoint));
        Assert.That(RunSession.ActiveRecordKey, Is.EqualTo("industrial"));
    }

    [Test]
    public void Select_RejectsEmptyRecordKey()
    {
        Assert.Throws<ArgumentException>(() =>
            RunSession.Select(GameMode.Checkpoint, string.Empty));
    }

    [Test]
    public void Select_RejectsUnknownMode()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RunSession.Select((GameMode)99, "industrial"));
    }
}
