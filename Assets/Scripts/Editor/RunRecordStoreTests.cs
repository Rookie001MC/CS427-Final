using System;
using NUnit.Framework;

public sealed class RunRecordStoreTests
{
    private sealed class MemoryPersistence : IRunRecordPersistence
    {
        public string Json = string.Empty;
        public bool ThrowOnSave;
        public string Load() => Json;
        public void Save(string json)
        {
            if (ThrowOnSave) throw new InvalidOperationException("save failed");
            Json = json;
        }
    }

    [Test]
    public void Commit_SeparatesModesForTheSameLevel()
    {
        var persistence = new MemoryPersistence();
        var store = new RunRecordStore(persistence);

        Assert.That(store.Commit("industrial", GameMode.Checkpoint, 20f,
            new[] { 8f, 12f }), Is.True);
        Assert.That(store.Commit("industrial", GameMode.NoCheckpoint, 15f,
            new[] { 6f, 9f }), Is.True);

        Assert.That(store.TryGetBest("industrial", GameMode.Checkpoint, out float checkpoint), Is.True);
        Assert.That(store.TryGetBest("industrial", GameMode.NoCheckpoint, out float noCheckpoint), Is.True);
        Assert.That(checkpoint, Is.EqualTo(20f));
        Assert.That(noCheckpoint, Is.EqualTo(15f));
        Assert.That(store.CountCompletedModes("industrial"), Is.EqualTo(2));
    }

    [Test]
    public void Commit_OnlyReplacesTimeAndSplitsTogetherWhenFaster()
    {
        var store = new RunRecordStore(new MemoryPersistence());
        Assert.That(store.Commit("neon", GameMode.Checkpoint, 30f,
            new[] { 10f, 20f }), Is.True);
        Assert.That(store.Commit("neon", GameMode.Checkpoint, 31f,
            new[] { 1f, 30f }), Is.False);
        Assert.That(store.GetBestSplit("neon", GameMode.Checkpoint, 1), Is.EqualTo(10f));
        Assert.That(store.Commit("neon", GameMode.Checkpoint, 25f,
            new[] { 9f, 16f }), Is.True);
        Assert.That(store.GetBestSplit("neon", GameMode.Checkpoint, 1), Is.EqualTo(9f));
        Assert.That(store.GetBestSplit("neon", GameMode.Checkpoint, 2), Is.EqualTo(16f));
    }

    [Test]
    public void Constructor_RoundTripsVersionOneJson()
    {
        var persistence = new MemoryPersistence();
        var first = new RunRecordStore(persistence);
        first.Commit("industrial", GameMode.NoCheckpoint, 18.5f, new[] { 7f, 11.5f });

        var reloaded = new RunRecordStore(persistence);
        Assert.That(reloaded.TryGetBest("industrial", GameMode.NoCheckpoint, out float best), Is.True);
        Assert.That(best, Is.EqualTo(18.5f));
        Assert.That(reloaded.GetBestSplit("industrial", GameMode.NoCheckpoint, 2), Is.EqualTo(11.5f));
    }

    [TestCase("")]
    [TestCase("{not json")]
    [TestCase("{\"version\":99,\"records\":[]}")]
    public void Constructor_InvalidSaveStartsEmpty(string json)
    {
        var store = new RunRecordStore(new MemoryPersistence { Json = json });
        Assert.That(store.CountCompletedModes("industrial"), Is.Zero);
    }

    [Test]
    public void Constructor_InvalidSplitsKeepsValidOverallBest()
    {
        const string json = "{\"version\":1,\"records\":[{\"levelKey\":\"industrial\",\"mode\":0," +
            "\"bestTime\":12.0,\"bestSplits\":[5.0,-1.0]}]}";
        var store = new RunRecordStore(new MemoryPersistence { Json = json });

        Assert.That(store.TryGetBest("industrial", GameMode.Checkpoint, out float best), Is.True);
        Assert.That(best, Is.EqualTo(12f));
        Assert.That(store.GetBestSplit("industrial", GameMode.Checkpoint, 1), Is.EqualTo(-1f));
    }

    [Test]
    public void Commit_SaveFailureRetainsTheInMemoryBest()
    {
        var store = new RunRecordStore(new MemoryPersistence { ThrowOnSave = true });
        Assert.That(store.Commit("industrial", GameMode.Checkpoint, 12f, new[] { 12f }), Is.True);
        Assert.That(store.TryGetBest("industrial", GameMode.Checkpoint, out float best), Is.True);
        Assert.That(best, Is.EqualTo(12f));
    }

    [Test]
    public void Commit_SeparatesDifferentLevelsInTheSameMode()
    {
        var store = new RunRecordStore(new MemoryPersistence());
        store.Commit("industrial", GameMode.Checkpoint, 12f, new[] { 12f });
        store.Commit("neon", GameMode.Checkpoint, 24f, new[] { 24f });

        Assert.That(store.TryGetBest("industrial", GameMode.Checkpoint, out float industrial), Is.True);
        Assert.That(store.TryGetBest("neon", GameMode.Checkpoint, out float neon), Is.True);
        Assert.That(industrial, Is.EqualTo(12f));
        Assert.That(neon, Is.EqualTo(24f));
    }

    [Test]
    public void Commit_RejectsInvalidIdentityAndTimes()
    {
        var store = new RunRecordStore(new MemoryPersistence());
        Assert.Throws<ArgumentException>(() =>
            store.Commit(string.Empty, GameMode.Checkpoint, 10f, Array.Empty<float>()));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            store.Commit("industrial", (GameMode)99, 10f, Array.Empty<float>()));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            store.Commit("industrial", GameMode.Checkpoint, -1f, Array.Empty<float>()));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            store.Commit("industrial", GameMode.Checkpoint, float.NaN, Array.Empty<float>()));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            store.Commit("industrial", GameMode.Checkpoint, float.PositiveInfinity, Array.Empty<float>()));
    }

    [Test]
    public void Commit_RejectsInvalidSplitsWithoutWritingARecord()
    {
        var store = new RunRecordStore(new MemoryPersistence());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            store.Commit("industrial", GameMode.Checkpoint, 10f, new[] { 4f, -1f }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            store.Commit("industrial", GameMode.Checkpoint, 10f, new[] { float.NaN }));
        Assert.That(store.TryGetBest("industrial", GameMode.Checkpoint, out _), Is.False);
    }

    [Test]
    public void Commit_PersistsVersionOneJsonWithRecordsForBothModes()
    {
        var persistence = new MemoryPersistence();
        var store = new RunRecordStore(persistence);

        store.Commit("industrial", GameMode.Checkpoint, 20f, new[] { 8f, 12f });
        store.Commit("industrial", GameMode.NoCheckpoint, 15f, new[] { 6f, 9f });

        StringAssert.Contains("\"version\":1", persistence.Json);
        StringAssert.Contains("\"mode\":0", persistence.Json);
        StringAssert.Contains("\"mode\":1", persistence.Json);
        Assert.That(CountOccurrences(persistence.Json, "\"levelKey\":\"industrial\""), Is.EqualTo(2));
    }

    private static int CountOccurrences(string value, string fragment)
    {
        int count = 0;
        int index = 0;
        while ((index = value.IndexOf(fragment, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += fragment.Length;
        }

        return count;
    }
}
