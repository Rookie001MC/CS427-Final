using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Menu actions: what the buttons do, which level was chosen, and handing off to the loader.
/// Layout and transitions belong to <see cref="MenuVisualController"/>; per-button feedback
/// belongs to <see cref="MenuButtonVisual"/>.
/// </summary>
public sealed class MenuController : MonoBehaviour
{
    [Header("Levels")]
    [Tooltip("Catalogue order. Element 0 is what PLAY launches.")]
    [SerializeField] private List<LevelEntry> levels = new List<LevelEntry>();

    [Header("Wiring")]
    [SerializeField] private MenuVisualController visuals;
    [SerializeField] private SceneLoader loader;

    [Header("Main screen")]
    [SerializeField] private Button playButton;
    [SerializeField] private Button levelsButton;
    [SerializeField] private Button statsButton;
    [SerializeField] private Button quitButton;
    [SerializeField] private TMP_Text currentZoneValue;

    [Header("Level select")]
    [SerializeField] private List<LevelCardView> cards = new List<LevelCardView>();
    [SerializeField] private Button backButton;
    [SerializeField] private TMP_Text clearedValue;

    /// <summary>
    /// Set by <see cref="MenuNavigation"/> when gameplay returns straight to Level Select, so the
    /// menu opens on the right screen without needing a second scene.
    /// </summary>
    public static bool OpenLevelSelectOnStart;

    private void Awake()
    {
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void Start()
    {
        Bind(playButton, PlayDefault);
        Bind(levelsButton, () => visuals.Show(MenuVisualController.Screen.LevelSelect));
        Bind(backButton, () => visuals.Show(MenuVisualController.Screen.Main));
        Bind(quitButton, Quit);

        // Player Stats is Phase 5. Shown, but visibly inert rather than wired to nothing.
        if (statsButton != null)
        {
            statsButton.interactable = false;
        }

        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] == null)
            {
                continue;
            }

            cards[i].Clicked -= LaunchLevel;
            cards[i].Clicked += LaunchLevel;
            cards[i].Bind(i < levels.Count ? levels[i] : null);
        }

        if (visuals != null)
        {
            visuals.ScreenChanged += HandleScreenChanged;
        }

        RefreshLevelSelect();
        RefreshCurrentZone();

        bool startOnLevelSelect = OpenLevelSelectOnStart;
        OpenLevelSelectOnStart = false;

        visuals.Show(startOnLevelSelect
            ? MenuVisualController.Screen.LevelSelect
            : MenuVisualController.Screen.Main);
    }

    private void OnDestroy()
    {
        if (visuals != null)
        {
            visuals.ScreenChanged -= HandleScreenChanged;
        }

        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] != null)
            {
                cards[i].Clicked -= LaunchLevel;
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
        if (screen == MenuVisualController.Screen.LevelSelect)
        {
            RefreshLevelSelect();
        }
    }

    // ---------------------------------------------------------------- actions

    private void PlayDefault()
    {
        if (levels.Count == 0)
        {
            Debug.LogError("[Menu] No levels configured.", this);
            return;
        }

        LaunchLevel(levels[0]);
    }

    private void LaunchLevel(LevelEntry entry)
    {
        if (entry == null || SceneLoader.IsLoading)
        {
            return;
        }

        if (loader == null)
        {
            Debug.LogError("[Menu] No SceneLoader assigned.", this);
            return;
        }

        loader.Load(entry);
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

    private void RefreshLevelSelect()
    {
        int cleared = 0;
        for (int i = 0; i < levels.Count; i++)
        {
            if (levels[i] != null && RunStatsTracker.TryGetBest(
                levels[i].RecordKey, GameMode.Checkpoint, out _))
            {
                cleared++;
            }
        }

        if (clearedValue != null)
        {
            clearedValue.text = $"{cleared} / {levels.Count} CLEARED";
        }

        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] != null)
            {
                cards[i].Bind(i < levels.Count ? levels[i] : null);
            }
        }
    }

    private void RefreshCurrentZone()
    {
        if (currentZoneValue == null)
        {
            return;
        }

        currentZoneValue.text = levels.Count > 0 && levels[0] != null
            ? levels[0].DisplayName
            : string.Empty;
    }

    // ---------------------------------------------------------------- input

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || SceneLoader.IsLoading || !keyboard.escapeKey.wasPressedThisFrame)
        {
            return;
        }

        if (visuals != null)
        {
            visuals.TryGoBack();
        }
    }
}
