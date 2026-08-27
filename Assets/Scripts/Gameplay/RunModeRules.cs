using System;

public enum DeathRecoveryAction
{
    RespawnAtCheckpoint,
    AwaitPlayerDecision
}

public readonly struct RunModeRules
{
    public GameMode Mode { get; }
    public DeathRecoveryAction DeathAction { get; }
    public string DisplayName { get; }
    public string ProgressName { get; }
    public string DeathHeadline { get; }

    private RunModeRules(GameMode mode, DeathRecoveryAction deathAction,
        string displayName, string progressName, string deathHeadline)
    {
        Mode = mode;
        DeathAction = deathAction;
        DisplayName = displayName;
        ProgressName = progressName;
        DeathHeadline = deathHeadline;
    }

    public static RunModeRules For(GameMode mode) => mode switch
    {
        GameMode.Checkpoint => new RunModeRules(mode,
            DeathRecoveryAction.RespawnAtCheckpoint,
            "CHECKPOINT MODE", "CHECKPOINT", "RECOVERING"),
        GameMode.NoCheckpoint => new RunModeRules(mode,
            DeathRecoveryAction.AwaitPlayerDecision,
            "NO-CHECKPOINT MODE", "SPLIT", "RUN FAILED"),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown game mode.")
    };
}
