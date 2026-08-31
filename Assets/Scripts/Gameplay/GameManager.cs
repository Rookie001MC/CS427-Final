using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Run state machine. Coordinates the timer, checkpoints, respawns and the player freeze; it
/// deliberately holds no timing, no scoring and no UI of its own - those live in their own
/// components and talk to this one through events.
/// </summary>
public sealed class GameManager : MonoBehaviour
{
    // Kept code-only so the recovery penalty is identical across every checkpoint course.
    private const int CheckpointRecoverySeconds = 3;

    [Header("Systems")]
    [SerializeField] private RunTimer runTimer;
    [SerializeField] private CheckpointManager checkpoints;
    [SerializeField] private RespawnManager respawn;
    [SerializeField] private PlayerFreezeController player;
    [SerializeField] private FallDetector fallDetector;

    [Tooltip("Optional. Kills the player for a fall too far to survive - Skybound City only.")]
    [SerializeField] private FallImpactDetector fallImpact;

    [Header("Countdown")]
    [SerializeField, Min(1)] private int countdownFrom = 3;
    [SerializeField, Min(0.05f)] private float countdownStepSeconds = 1f;

    public RunState State { get; private set; } = RunState.Idle;
    public int Deaths { get; private set; }
    public GameMode Mode => RunSession.ActiveMode;
    public RunModeRules Rules => RunModeRules.For(Mode);

    /// <summary>Reason string from the most recent accepted death.</summary>
    public string LastDeathReason { get; private set; } = string.Empty;

    public RunTimer Timer => runTimer;
    public CheckpointManager Checkpoints => checkpoints;

    /// <summary>
    /// Whether anything in the scene is going to offer the player the choice that No-Checkpoint
    /// Mode's death is supposed to end in.
    ///
    /// This exists because the mode was only half implemented. `Die` reads the rules correctly and
    /// takes the right branch, but the No-Checkpoint branch does not resolve the death - it stops
    /// the clock and waits for a view to call `RestartRun`. In a scene that carries a
    /// `GameplayUIController` that is exactly right. In one that does not - Skybound City, which is
    /// the level PLAY launches - there is nobody to ask, so the run sat in
    /// <see cref="RunState.Recovering"/> for ever and the mode had no effect a player could see.
    ///
    /// A rule the scene has to opt into is not a rule. The manager now knows whether the question
    /// can be asked, and honours the mode either way.
    /// </summary>
    public bool CanPresentDeathDecision => decisionResponders > 0;

    public event Action<RunState> StateChanged;

    /// <summary>Countdown label: "3", "2", "1", then "GO!".</summary>
    public event Action<string> CountdownTick;

    /// <summary>Whole seconds remaining before Checkpoint Mode respawns the player.</summary>
    public event Action<int> RecoveryCountdownTick;

    public event Action RunStarted;

    /// <summary>Payload is the new death count.</summary>
    public event Action<int> PlayerDied;

    /// <summary>Payload is the final run time in seconds.</summary>
    public event Action<float> RunFinished;

    private Coroutine countdownRoutine;
    private Coroutine recoveryRoutine;
    private int decisionResponders;

    /// <summary>
    /// Registers something that will present the No-Checkpoint death decision - in practice
    /// <see cref="GameplayUIController"/>, when it has a death overlay with a retry button on it.
    /// Counted rather than a flag so a view being disabled and re-enabled cannot lose it.
    /// </summary>
    public void AddDeathDecisionResponder() => decisionResponders++;

    public void RemoveDeathDecisionResponder()
        => decisionResponders = Mathf.Max(0, decisionResponders - 1);

    private void OnEnable()
    {
        KillZone.PlayerEntered += HandleKillZone;
        FinishLine.PlayerEntered += HandleFinishLine;

        if (fallDetector != null)
        {
            fallDetector.FellBelowThreshold += HandleFall;
        }

        if (fallImpact != null)
        {
            fallImpact.FatalImpact += HandleFallImpact;
        }
    }

    private void OnDisable()
    {
        KillZone.PlayerEntered -= HandleKillZone;
        FinishLine.PlayerEntered -= HandleFinishLine;

        if (fallDetector != null)
        {
            fallDetector.FellBelowThreshold -= HandleFall;
        }

        if (fallImpact != null)
        {
            fallImpact.FatalImpact -= HandleFallImpact;
        }

        CancelRoutine(ref countdownRoutine);
        CancelRoutine(ref recoveryRoutine);
    }

    private void Start()
    {
        RestartRun();
    }

    // ---------------------------------------------------------------- run lifecycle

    /// <summary>Full reset: back to LevelStart, clock, checkpoints and deaths all zeroed.</summary>
    public void RestartRun()
    {
        // Any pause must be lifted before the run restarts, or the countdown never ticks.
        Time.timeScale = 1f;

        CancelRoutine(ref countdownRoutine);
        CancelRoutine(ref recoveryRoutine);

        Deaths = 0;

        if (checkpoints != null)
        {
            checkpoints.ResetProgress();
        }

        if (runTimer != null)
        {
            runTimer.ResetTimer();
        }

        if (respawn != null)
        {
            respawn.RespawnAtStart();
        }

        if (fallDetector != null)
        {
            fallDetector.Rearm();
        }

        if (fallImpact != null)
        {
            fallImpact.Rearm();
        }

        countdownRoutine = StartCoroutine(CountdownThenRun());
    }

    private IEnumerator CountdownThenRun()
    {
        SetState(RunState.Countdown);

        // Held still, but the cursor stays locked so the countdown still feels in-game.
        if (player != null)
        {
            player.Freeze(true);
        }

        for (int n = countdownFrom; n >= 1; n--)
        {
            CountdownTick?.Invoke(n.ToString());
            yield return new WaitForSeconds(countdownStepSeconds);
        }

        CountdownTick?.Invoke("GO!");

        if (player != null)
        {
            player.Unfreeze();
        }

        if (runTimer != null)
        {
            runTimer.Begin();
        }

        SetState(RunState.Running);
        RunStarted?.Invoke();
        countdownRoutine = null;
    }

    // ---------------------------------------------------------------- death

    private void HandleFall()
    {
        // FallDetector disarms itself before raising. If the death is refused (paused, recovering,
        // finished), it must be re-armed here or the player stays silently unkillable.
        if (!Die("fell below the death plane") && fallDetector != null)
        {
            fallDetector.Rearm();
        }
    }

    /// <summary>
    /// A fall that landed too hard. Like <see cref="HandleFall"/>, the detector has already
    /// disarmed itself, so a refused death has to re-arm it or the player is silently unkillable
    /// by falling for the rest of the run.
    /// </summary>
    private void HandleFallImpact(float metres)
    {
        if (!Die($"fell {metres:F1} m") && fallImpact != null)
        {
            fallImpact.Rearm();
        }
    }

    private void HandleKillZone(KillZone zone) => Die($"entered {zone.ZoneName}");

    /// <summary>
    /// Kills the player. Returns false when the run was not in a state that can die, so callers
    /// that armed something on the way in can undo it.
    /// </summary>
    public bool Die(string reason)
    {
        if (State != RunState.Running)
        {
            return false;
        }

        Deaths++;
        LastDeathReason = reason;
        SetState(RunState.Recovering);

        bool awaitsDecision = Rules.DeathAction == DeathRecoveryAction.AwaitPlayerDecision;

        // The attempt is over either way in No-Checkpoint Mode; the only question is whether the
        // player gets to choose what happens next or the manager has to decide for them. The rule
        // the mode is written to - death resets the whole run: timer, progress and countdown - is
        // the manager's, so it holds in a scene with no death overlay too. Falling back to the
        // checkpoint respawn there is what made the two modes indistinguishable.
        bool asksThePlayer = awaitsDecision && CanPresentDeathDecision;

        if (runTimer != null && awaitsDecision)
        {
            runTimer.Stop();
        }

        if (player != null)
        {
            player.Freeze(!awaitsDecision);

            if (asksThePlayer)
            {
                player.ReleaseCursor();
            }
        }

        Debug.Log($"[Run] death #{Deaths} - {reason} ({Rules.DisplayName}).", this);
        PlayerDied?.Invoke(Deaths);

        if (!awaitsDecision)
        {
            recoveryRoutine = StartCoroutine(RecoverAfterDeath());
        }
        else if (!asksThePlayer)
        {
            recoveryRoutine = StartCoroutine(FailRunAfterDeath());
        }

        return true;
    }

    /// <summary>
    /// No-Checkpoint Mode with nobody to ask: the attempt ends and the whole run starts again,
    /// which is the mode's own rule rather than a fallback invented here. The pause is the same
    /// length as Checkpoint Mode's recovery so the death still reads as a beat rather than a
    /// teleport.
    /// </summary>
    private IEnumerator FailRunAfterDeath()
    {
        for (int remaining = CheckpointRecoverySeconds; remaining >= 1; remaining--)
        {
            RecoveryCountdownTick?.Invoke(remaining);
            yield return new WaitForSecondsRealtime(1f);
        }

        recoveryRoutine = null;
        RestartRun();
    }

    private IEnumerator RecoverAfterDeath()
    {
        for (int remaining = CheckpointRecoverySeconds; remaining >= 1; remaining--)
        {
            RecoveryCountdownTick?.Invoke(remaining);
            yield return new WaitForSecondsRealtime(1f);
        }

        recoveryRoutine = null;

        if (respawn != null) respawn.RespawnAtCheckpoint();
        if (fallDetector != null) fallDetector.Rearm();

        // Re-armed *after* the teleport, so the apex the player fell from is forgotten along with
        // the fall - otherwise landing at the anchor would be measured from where they died.
        if (fallImpact != null) fallImpact.Rearm();
        if (player != null) player.Unfreeze();
        SetState(RunState.Running);
    }

    // ---------------------------------------------------------------- finish

    private void HandleFinishLine(FinishLine line)
    {
        if (State != RunState.Running)
        {
            return;
        }

        if (line.RequireAllCheckpoints && checkpoints != null && !checkpoints.AllReached)
        {
            Debug.Log(
                $"[Run] finish blocked - {checkpoints.Reached}/{checkpoints.Total} checkpoints.",
                this);
            return;
        }

        Finish();
    }

    private void Finish()
    {
        float finalTime = runTimer != null ? runTimer.ElapsedSeconds : 0f;

        if (runTimer != null)
        {
            runTimer.Stop();
        }

        if (player != null)
        {
            player.Freeze(false);
            player.ReleaseCursor();
        }

        SetState(RunState.Finished);

        Debug.Log($"[Run] FINISHED in {RunTimer.Format(finalTime)} with {Deaths} deaths.", this);

        RunFinished?.Invoke(finalTime);
    }

    // ---------------------------------------------------------------- pause

    /// <summary>Toggles the pause state. Only meaningful while Running or Paused.</summary>
    public void SetPaused(bool paused)
    {
        if (paused)
        {
            if (State != RunState.Running)
            {
                return;
            }

            Time.timeScale = 0f;

            if (runTimer != null)
            {
                runTimer.Pause();
            }

            if (player != null)
            {
                player.Freeze(false);
                player.ReleaseCursor();
            }

            SetState(RunState.Paused);
            return;
        }

        if (State != RunState.Paused)
        {
            return;
        }

        Time.timeScale = 1f;

        if (player != null)
        {
            player.Unfreeze();
        }

        if (runTimer != null)
        {
            runTimer.Resume();
        }

        SetState(RunState.Running);
    }

    public void TogglePause() => SetPaused(State != RunState.Paused);

    // ---------------------------------------------------------------- plumbing

    private void SetState(RunState next)
    {
        if (State == next)
        {
            return;
        }

        State = next;
        StateChanged?.Invoke(next);
    }

    private void CancelRoutine(ref Coroutine routine)
    {
        if (routine == null)
        {
            return;
        }

        StopCoroutine(routine);
        routine = null;
    }

    private void OnDestroy()
    {
        CancelRoutine(ref countdownRoutine);
        CancelRoutine(ref recoveryRoutine);

        // Never leave a loading scene or a domain reload with a frozen clock.
        Time.timeScale = 1f;
    }
}
