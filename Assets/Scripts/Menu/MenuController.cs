using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Menu actions: what the buttons do, which level was chosen, and handing off to the loader.
/// Layout and transitions belong to <see cref="MenuVisualController"/>; per-button feedback
/// belongs to <see cref="MenuButtonVisual"/>.
///
/// The catalogue is one list, and which screen a level appears on is read off its own
/// <see cref="LevelEntry.Track"/> rather than off its position in that list. The game has one real
/// run - Skybound City - and two practice courses, and a menu that presented three equal cards was
/// telling the player the opposite of what the game is. PLAY goes straight to the main run;
/// TRAINING is where the two courses live, labelled as such.
/// </summary>
public sealed class MenuController : MonoBehaviour
{
    [Header("Levels")]
    [Tooltip("The whole catalogue. Which screen each one appears on comes from its own Track.")]
    [SerializeField] private List<LevelEntry> levels = new List<LevelEntry>();

    [Header("Wiring")]
    [SerializeField] private MenuVisualController visuals;
    [SerializeField] private SceneLoader loader;
    [SerializeField] private ModeSelectionView modeSelection;

    [Header("Main screen")]
    [SerializeField] private Button playButton;
    [SerializeField] private Button trainingButton;
    [SerializeField] private Button statsButton;
    [SerializeField] private Button quitButton;
    [SerializeField] private TMP_Text currentZoneValue;

    [Header("Main run")]
    [SerializeField] private FeaturedLevelView featured;
    [SerializeField] private Button mainRunBackButton;

    [Header("Training")]
    [SerializeField] private List<LevelCardView> cards = new List<LevelCardView>();
    [SerializeField] private Button backButton;
    [SerializeField] private TMP_Text clearedValue;

    /// <summary>
    /// Set by <see cref="MenuNavigation"/> when gameplay returns straight to level browsing, so the
    /// menu opens on the right screen without needing a second scene. Which screen that is comes
    /// from the level the player just left, so a training map returns to TRAINING and the main run
    /// returns to its own panel.
    /// </summary>
    public static bool OpenLevelSelectOnStart;

    private LevelEntry pendingLevel;

    // The level the player has just come back from, read before `Awake` wipes the session. Without
    // this, "back to level select" always landed on TRAINING - including after the main run -
    // because the record key it is resolved from had already been cleared on the line above.
    private string returningFrom = string.Empty;

    /// <summary>The one level PLAY launches, or null if the catalogue names none.</summary>
    public LevelEntry MainRun
    {
        get
        {
            for (int i = 0; i < levels.Count; i++)
            {
                if (levels[i] != null && levels[i].IsMainRun)
                {
                    return levels[i];
                }
            }

            return null;
        }
    }

    private void Awake()
    {
        returningFrom = RunSession.ActiveRecordKey;
        RunSession.Clear();
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void Start()
    {
        Bind(playButton, ShowMainRun);
        Bind(trainingButton, ShowTraining);
        Bind(backButton, ShowMain);
        Bind(mainRunBackButton, ShowMain);
        Bind(quitButton, Quit);

        if (modeSelection != null)
        {
            modeSelection.ModeSelected -= ConfirmMode;
            modeSelection.ModeSelected += ConfirmMode;
            modeSelection.Cancelled -= CancelModeSelection;
            modeSelection.Cancelled += CancelModeSelection;
            modeSelection.Hide();
        }

        // Player Stats is Phase 5. Shown, but visibly inert rather than wired to nothing.
        if (statsButton != null)
        {
            statsButton.interactable = false;
        }

        if (featured != null)
        {
            featured.Started -= OpenModeSelection;
            featured.Started += OpenModeSelection;
        }

        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] == null)
            {
                continue;
            }

            cards[i].Clicked -= OpenModeSelection;
            cards[i].Clicked += OpenModeSelection;
        }

        if (visuals != null)
        {
            visuals.ScreenChanged += HandleScreenChanged;
        }

        RefreshTraining();
        RefreshMainRun();
        RefreshCurrentZone();

        bool returning = OpenLevelSelectOnStart;
        OpenLevelSelectOnStart = false;

        if (visuals != null)
        {
            visuals.Show(returning ? ScreenForLastRun() : MenuVisualController.Screen.Main);
        }
    }

    private void OnDestroy()
    {
        if (visuals != null)
        {
            visuals.ScreenChanged -= HandleScreenChanged;
        }

        if (modeSelection != null)
        {
            modeSelection.ModeSelected -= ConfirmMode;
            modeSelection.Cancelled -= CancelModeSelection;
        }

        if (featured != null)
        {
            featured.Started -= OpenModeSelection;
        }

        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] != null)
            {
                cards[i].Clicked -= OpenModeSelection;
            }
        }
    }

    private static void Bind(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null)
        {
            return;
        }

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(action);
    }

    private void HandleScreenChanged(MenuVisualController.Screen screen)
    {
        if (screen == MenuVisualController.Screen.Training)
        {
            RefreshTraining();
        }
        else if (screen == MenuVisualController.Screen.MainRun)
        {
            RefreshMainRun();
        }
    }

    /// <summary>
    /// Where "back to level select" from gameplay lands: the screen the level they just left lives
    /// on. Read from <see cref="RunSession"/> rather than remembered here, because the menu scene
    /// was unloaded while they played.
    /// </summary>
    private MenuVisualController.Screen ScreenForLastRun()
    {
        for (int i = 0; i < levels.Count; i++)
        {
            if (levels[i] != null && levels[i].RecordKey == returningFrom)
            {
                return levels[i].IsMainRun
                    ? MenuVisualController.Screen.MainRun
                    : MenuVisualController.Screen.Training;
            }
        }

        return MenuVisualController.Screen.Training;
    }

    // ---------------------------------------------------------------- actions

    private void OpenModeSelection(LevelEntry entry)
    {
        if (entry == null || SceneLoader.IsLoading)
        {
            return;
        }

        if (modeSelection == null)
        {
            Debug.LogError("[Menu] No mode selection view assigned.", this);
            return;
        }

        pendingLevel = entry;
        modeSelection.Show(entry);
    }

    private void ConfirmMode(GameMode mode)
    {
        if (pendingLevel == null || SceneLoader.IsLoading)
        {
            return;
        }

        if (loader == null)
        {
            Debug.LogError("[Menu] No SceneLoader assigned.", this);
            return;
        }

        LevelEntry level = pendingLevel;
        pendingLevel = null;
        modeSelection?.Hide();
        loader.Load(level, mode);
    }

    private void CancelModeSelection()
    {
        if (SceneLoader.IsLoading)
        {
            return;
        }

        modeSelection?.Hide();
        pendingLevel = null;
    }

    private void ShowMainRun() => Go(MenuVisualController.Screen.MainRun);

    private void ShowTraining() => Go(MenuVisualController.Screen.Training);

    private void ShowMain() => Go(MenuVisualController.Screen.Main);

    private void Go(MenuVisualController.Screen screen)
    {
        if (!SceneLoader.IsLoading && visuals != null)
        {
            visuals.Show(screen);
        }
    }

    private static void Quit()
    {
#if UNITY_EDITOR
        // Never actually close the editor - stopping play mode is the equivalent gesture.
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ---------------------------------------------------------------- data

    private void RefreshMainRun() => featured?.Bind(MainRun);

    private void RefreshTraining()
    {
        // The training courses, in catalogue order. Built here rather than stored so that a
        // level's track can be changed in its own asset and the menu follows.
        List<LevelEntry> training = new List<LevelEntry>();

        for (int i = 0; i < levels.Count; i++)
        {
            if (levels[i] != null && !levels[i].IsMainRun)
            {
                training.Add(levels[i]);
            }
        }

        int cleared = 0;

        for (int i = 0; i < training.Count; i++)
        {
            if (RunStatsTracker.CountCompletedModes(training[i].RecordKey) == 2)
            {
                cleared++;
            }
        }

        if (clearedValue != null)
        {
            clearedValue.text = $"{cleared} / {training.Count} COMPLETE";
        }

        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] != null)
            {
                cards[i].Bind(i < training.Count ? training[i] : null);
            }
        }
    }

    private void RefreshCurrentZone()
    {
        if (currentZoneValue == null)
        {
            return;
        }

        LevelEntry run = MainRun;
        currentZoneValue.text = run != null ? run.DisplayName : string.Empty;
    }

    // ---------------------------------------------------------------- input

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || SceneLoader.IsLoading || !keyboard.escapeKey.wasPressedThisFrame)
        {
            return;
        }

        if (modeSelection != null && modeSelection.IsVisible)
        {
            CancelModeSelection();
            return;
        }

        if (visuals != null)
        {
            visuals.TryGoBack();
        }
    }
}
