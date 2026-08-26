using System;
using UnityEngine;

public static class RunSession
{
    public static GameMode ActiveMode { get; private set; } = GameMode.Checkpoint;
    public static string ActiveRecordKey { get; private set; } = string.Empty;
    public static bool HasSelection { get; private set; }

    public static void Select(GameMode mode, string recordKey)
    {
        if (mode != GameMode.Checkpoint && mode != GameMode.NoCheckpoint)
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown game mode.");
        }

        if (string.IsNullOrWhiteSpace(recordKey))
        {
            throw new ArgumentException("A run selection requires a record key.", nameof(recordKey));
        }

        ActiveMode = mode;
        ActiveRecordKey = recordKey;
        HasSelection = true;
    }

    public static void Clear()
    {
        ActiveMode = GameMode.Checkpoint;
        ActiveRecordKey = string.Empty;
        HasSelection = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Clear();
}
