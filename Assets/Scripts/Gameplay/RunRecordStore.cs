using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class RunRecordStore
{
    [Serializable]
    private sealed class SaveFile
    {
        public int version = 1;
        public List<RecordData> records = new List<RecordData>();
    }

    [Serializable]
    private sealed class RecordData
    {
        public string levelKey;
        public int mode;
        public float bestTime;
        public List<float> bestSplits = new List<float>();
    }

    private const int CurrentVersion = 1;
    private static RunRecordStore defaultStore;
    private readonly IRunRecordPersistence persistence;
    private readonly List<RecordData> records = new List<RecordData>();

    public static RunRecordStore Default =>
        defaultStore ??= new RunRecordStore(new PlayerPrefsRunRecordPersistence());

    public RunRecordStore(IRunRecordPersistence persistence)
    {
        this.persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        LoadValidated();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetDefaultStore() => defaultStore = null;

    public bool TryGetBest(string levelKey, GameMode mode, out float bestTime)
    {
        ValidateIdentity(levelKey, mode);
        RecordData record = Find(levelKey, mode);
        bestTime = record != null ? record.bestTime : -1f;
        return record != null;
    }

    public float GetBestSplit(string levelKey, GameMode mode, int oneBasedIndex)
    {
        ValidateIdentity(levelKey, mode);
        if (oneBasedIndex < 1)
            throw new ArgumentOutOfRangeException(nameof(oneBasedIndex));

        RecordData record = Find(levelKey, mode);
        int index = oneBasedIndex - 1;
        return record != null && index < record.bestSplits.Count
            ? record.bestSplits[index]
            : -1f;
    }

    public int CountCompletedModes(string levelKey)
    {
        if (string.IsNullOrWhiteSpace(levelKey))
            throw new ArgumentException("Record key cannot be empty.", nameof(levelKey));

        int count = 0;
        if (Find(levelKey, GameMode.Checkpoint) != null) count++;
        if (Find(levelKey, GameMode.NoCheckpoint) != null) count++;
        return count;
    }

    public bool Commit(string levelKey, GameMode mode, float finishTime,
        IReadOnlyList<float> sectionSplits)
    {
        ValidateIdentity(levelKey, mode);
        if (!IsFiniteNonNegative(finishTime))
            throw new ArgumentOutOfRangeException(nameof(finishTime));
        if (sectionSplits == null)
            throw new ArgumentNullException(nameof(sectionSplits));

        var splits = new List<float>(sectionSplits.Count);
        for (int i = 0; i < sectionSplits.Count; i++)
        {
            float split = sectionSplits[i];
            if (!IsFiniteNonNegative(split))
                throw new ArgumentOutOfRangeException(nameof(sectionSplits));
            splits.Add(split);
        }

        RecordData record = Find(levelKey, mode);
        if (record != null && finishTime >= record.bestTime)
            return false;

        if (record == null)
        {
            record = new RecordData { levelKey = levelKey, mode = (int)mode };
            records.Add(record);
        }

        record.bestTime = finishTime;
        record.bestSplits = splits;
        SaveCurrent();
        return true;
    }

    private RecordData Find(string levelKey, GameMode mode) =>
        records.Find(record => record.mode == (int)mode &&
            string.Equals(record.levelKey, levelKey, StringComparison.Ordinal));

    private void LoadValidated()
    {
        string json;
        try
        {
            json = persistence.Load();
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Records] Could not read save data: {exception.Message}");
            return;
        }

        if (string.IsNullOrWhiteSpace(json)) return;

        bool invalid = false;
        SaveFile file;
        try
        {
            file = JsonUtility.FromJson<SaveFile>(json);
        }
        catch (Exception)
        {
            file = null;
        }

        if (file == null || file.version != CurrentVersion || file.records == null)
        {
            Debug.LogWarning("[Records] Ignored malformed or unsupported save data.");
            return;
        }

        for (int i = 0; i < file.records.Count; i++)
        {
            RecordData candidate = file.records[i];
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.levelKey) ||
                !IsKnownMode(candidate.mode) || !IsFiniteNonNegative(candidate.bestTime))
            {
                invalid = true;
                continue;
            }

            candidate.bestSplits ??= new List<float>();
            if (candidate.bestSplits.Exists(split => !IsFiniteNonNegative(split)))
            {
                candidate.bestSplits.Clear();
                invalid = true;
            }

            GameMode mode = (GameMode)candidate.mode;
            RecordData existing = Find(candidate.levelKey, mode);
            if (existing == null)
            {
                records.Add(candidate);
            }
            else if (candidate.bestTime < existing.bestTime)
            {
                existing.bestTime = candidate.bestTime;
                existing.bestSplits = new List<float>(candidate.bestSplits);
                invalid = true;
            }
            else
            {
                invalid = true;
            }
        }

        if (invalid)
            Debug.LogWarning("[Records] Ignored invalid entries in save data.");
    }

    private void SaveCurrent()
    {
        try
        {
            var file = new SaveFile { version = CurrentVersion, records = records };
            persistence.Save(JsonUtility.ToJson(file));
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Records] Could not persist personal best: {exception.Message}");
        }
    }

    private static void ValidateIdentity(string levelKey, GameMode mode)
    {
        if (string.IsNullOrWhiteSpace(levelKey))
            throw new ArgumentException("Record key cannot be empty.", nameof(levelKey));
        if (mode != GameMode.Checkpoint && mode != GameMode.NoCheckpoint)
            throw new ArgumentOutOfRangeException(nameof(mode));
    }

    private static bool IsKnownMode(int mode) =>
        mode == (int)GameMode.Checkpoint || mode == (int)GameMode.NoCheckpoint;

    private static bool IsFiniteNonNegative(float value) =>
        value >= 0f && !float.IsNaN(value) && !float.IsInfinity(value);
}
