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
    [Header("Systems")]
    [SerializeField] private RunTimer runTimer;
    [SerializeField] private CheckpointManager checkpoints;
    [SerializeField] private RespawnManager respawn;
    [SerializeField] private PlayerFreezeController player;
    [SerializeField] private FallDetector fallDetector;

    [Header("Countdown")]
    [SerializeField, Min(1)] private int countdownFrom = 3;
    [SerializeField, Min(0.05f)] private float countdownStepSeconds = 1f;

    [Header("Behaviour")]
    [SerializeField, Range(0.05f, 0.95f)] private float deathRecoverySeconds = 0.45f;

    public RunState State { get; private set; } = RunState.Idle;
    public int Deaths { get; private set; }
    public GameMode Mode => RunSession.ActiveMode;
    public RunModeRules Rules => RunModeRules.For(Mode);

    /// <summary>Reason string from the most recent accepted death.</summary>
    public string LastDeathReason { get; private set; } = string.Empty;

    public RunTimer Timer => runTimer;
    public CheckpointManager Checkpoints => checkpoints;

    public event Action<RunState> StateChanged;

    /// <summary>Countdown label: "3", "2", "1", then "GO!".</summary>
    public event Action<string> CountdownTick;

    public event Action RunStarted;

    /// <summary>Payload is the new death count.</summary>
    public event Action<int> PlayerDied;

    /// <summary>Payload is the final run time in seconds.</summary>
    public event Action<float> RunFinished;

    private Coroutine countdownRoutine;
    private Coroutine recoveryRoutine;

    private void OnEnable()
    {
        KillZone.PlayerEntered += HandleKillZone;
        FinishLine.PlayerEntered += HandleFinishLine;

        if (fallDetector != null)
        {
            fallDetector.FellBelowThreshold += HandleFall;
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

        if (player != null)
        {
            player.Freeze(true);
        }

        Debug.Log($"[Run] death #{Deaths} - {reason}.", this);
        PlayerDied?.Invoke(Deaths);
        recoveryRoutine = StartCoroutine(RecoverAfterDeath());
        return true;
    }

    private IEnumerator RecoverAfterDeath()
    {
        yield return new WaitForSecondsRealtime(deathRecoverySeconds);
        recoveryRoutine = null;

        if (Rules.DeathAction == DeathRecoveryAction.RestartRun)
        {
            RestartRun();
            yield break;
        }

        if (respawn != null) respawn.RespawnAtCheckpoint();
        if (fallDetector != null) fallDetector.Rearm();
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
