using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The single seam between the run systems and the gameplay UI. Views hold no game logic and
/// GameManager holds no view references; this listens to one and drives the other.
/// </summary>
public sealed class GameplayUIController : MonoBehaviour
{
    [Header("Systems")]
    [SerializeField] private GameManager game;
    [SerializeField] private RunTimer runTimer;
    [SerializeField] private CheckpointManager checkpoints;
    [SerializeField] private RunStatsTracker stats;
    [SerializeField] private LevelInfo levelInfo;

    [Header("Views")]
    [SerializeField] private GameplayHUD hud;
    [SerializeField] private CountdownView countdown;
    [SerializeField] private CheckpointPopup checkpointPopup;
    [SerializeField] private PauseMenuView pauseMenu;
    [SerializeField] private GameOverView gameOver;
    [SerializeField] private LevelCompleteView levelComplete;

    private void OnEnable()
    {
        if (game != null)
        {
            game.StateChanged += HandleStateChanged;
            game.CountdownTick += HandleCountdownTick;
            game.PlayerDied += HandlePlayerDied;
            game.RunFinished += HandleRunFinished;
        }

        if (runTimer != null)
        {
            runTimer.Ticked += HandleTimerTicked;
        }

        if (checkpoints != null)
        {
            checkpoints.CheckpointReached += HandleCheckpointReached;
        }

        WireButtons();
    }

    private void OnDisable()
    {
        if (game != null)
        {
            game.StateChanged -= HandleStateChanged;
            game.CountdownTick -= HandleCountdownTick;
            game.PlayerDied -= HandlePlayerDied;
            game.RunFinished -= HandleRunFinished;
        }

        if (runTimer != null)
        {
            runTimer.Ticked -= HandleTimerTicked;
        }

        if (checkpoints != null)
        {
            checkpoints.CheckpointReached -= HandleCheckpointReached;
        }
    }

    private void Start()
    {
        RefreshCheckpointReadout();
        HandleTimerTicked(0f);

        if (pauseMenu != null)
        {
            pauseMenu.SetLevelCaption(BuildLevelCaption());
        }
    }

    /// <summary>"NAME  -  SUBTITLE", upper-cased, from the scene's LevelInfo.</summary>
    private string BuildLevelCaption()
    {
        if (levelInfo == null)
        {
            return gameObject.scene.name.ToUpperInvariant();
        }

        string caption = levelInfo.DisplayName;
        if (!string.IsNullOrEmpty(levelInfo.Subtitle))
        {
            caption += "  -  " + levelInfo.Subtitle;
        }

        return caption.ToUpperInvariant();
    }

    // ---------------------------------------------------------------- buttons

    private void WireButtons()
    {
        if (pauseMenu != null)
        {
            Bind(pauseMenu.ResumeButton, () => game.SetPaused(false));
            Bind(pauseMenu.RestartButton, RestartRun);
            Bind(pauseMenu.LevelSelectButton, MenuNavigation.GoToLevelSelect);
            Bind(pauseMenu.MainMenuButton, MenuNavigation.GoToMainMenu);
        }

        if (gameOver != null)
        {
            Bind(gameOver.RestartButton, RestartRun);

            // QUIT abandons the run rather than the application: the reference's Game Over is a
            // mid-run screen, and quitting to desktop from it would be a trap.
            Bind(gameOver.QuitButton, MenuNavigation.GoToMainMenu);
        }

        if (levelComplete != null)
        {
            Bind(levelComplete.ReplayButton, RestartRun);
            Bind(levelComplete.LevelSelectButton, MenuNavigation.GoToLevelSelect);
            Bind(levelComplete.MainMenuButton, MenuNavigation.GoToMainMenu);
        }
    }

    private static void Bind(UnityEngine.UI.Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null)
        {
            return;
        }

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(action);
    }

    private void RestartRun()
    {
        if (stats != null)
        {
            stats.ResetRun();
        }

        game.RestartRun();
    }

    // ---------------------------------------------------------------- input

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || game == null || !keyboard.escapeKey.wasPressedThisFrame)
        {
            return;
        }

        // Escape only toggles pause while a run is actively running or paused.
        if (game.State == RunState.Running)
        {
            game.SetPaused(true);
        }
        else if (game.State == RunState.Paused)
        {
            game.SetPaused(false);
        }
    }

    // ---------------------------------------------------------------- run events

    private void HandleStateChanged(RunState state)
    {
        if (hud != null)
        {
            hud.SetVisible(state == RunState.Running || state == RunState.Countdown);
        }

        if (pauseMenu != null)
        {
            bool show = state == RunState.Paused;
            if (show)
            {
                pauseMenu.Bind(
                    runTimer != null ? runTimer.ElapsedSeconds : 0f,
                    checkpoints != null ? checkpoints.Reached : 0,
                    checkpoints != null ? checkpoints.Total : 0,
                    stats != null && stats.HasBest,
                    stats != null ? stats.BestTime : -1f);
            }

            pauseMenu.SetVisible(show);
        }

        if (state == RunState.Countdown)
        {
            if (stats != null)
            {
                stats.ResetRun();
            }

            if (countdown != null)
            {
                countdown.Begin();
            }

            if (gameOver != null)
            {
                gameOver.SetVisible(false);
            }

            if (levelComplete != null)
            {
                levelComplete.SetVisible(false);
            }

            if (checkpointPopup != null)
            {
                checkpointPopup.HideNow();
            }

            RefreshCheckpointReadout();
        }

        if (state == RunState.Running && countdown != null)
        {
            countdown.Finish();
        }

        if (state != RunState.Recovering && gameOver != null)
        {
            gameOver.SetVisible(false);
        }
    }

    private void HandleCountdownTick(string label)
    {
        if (countdown != null)
        {
            countdown.Tick(label);
        }
    }

    private void HandleTimerTicked(float seconds)
    {
        if (hud != null)
        {
            hud.SetTime(seconds);
        }
    }

    private void HandleCheckpointReached(CheckpointVolume volume, int index, int total, float split, float cumulative)
    {
        RefreshCheckpointReadout();

        if (checkpointPopup != null)
        {
            float best = stats != null ? stats.GetBestSplit(index) : -1f;
            checkpointPopup.Show(index, total, split, cumulative, best);
        }
    }

    private void HandlePlayerDied(int deaths)
    {
        if (checkpointPopup != null)
        {
            checkpointPopup.HideNow();
        }

    }

    private void HandleRunFinished(float finishTime)
    {
        if (checkpointPopup != null)
        {
            checkpointPopup.HideNow();
        }

        bool isBest = stats != null && stats.CommitFinishedRun(finishTime);

        if (levelComplete == null)
        {
            return;
        }

        levelComplete.Bind(
            finishTime,
            isBest,
            stats != null && stats.HasBest,
            stats != null ? stats.BestTime : -1f,
            checkpoints != null ? checkpoints.Reached : 0,
            checkpoints != null ? checkpoints.Total : 0,
            game != null ? game.Deaths : 0,
            stats != null ? stats.MaxSpeed : 0f,
            levelInfo != null ? levelInfo.DisplayName : gameObject.scene.name.ToUpperInvariant(),
            levelInfo != null ? levelInfo.Subtitle : string.Empty);

        levelComplete.SetVisible(true);
    }

    private void RefreshCheckpointReadout()
    {
        if (hud != null && checkpoints != null)
        {
            hud.SetCheckpoint(checkpoints.Reached, checkpoints.Total);
        }
    }
}
