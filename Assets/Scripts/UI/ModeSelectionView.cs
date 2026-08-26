using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Passive presentation for choosing the ruleset for a selected level. Scene loading remains the
/// responsibility of <see cref="MenuController"/>.
/// </summary>
public sealed class ModeSelectionView : MonoBehaviour
{
    [SerializeField] private UIPanel panel;
    [SerializeField] private TMP_Text levelNumber;
    [SerializeField] private TMP_Text levelName;
    [SerializeField] private TMP_Text levelSubtitle;
    [SerializeField] private TMP_Text checkpointBest;
    [SerializeField] private TMP_Text noCheckpointBest;
    [SerializeField] private Button checkpointButton;
    [SerializeField] private Button noCheckpointButton;
    [SerializeField] private Button backButton;

    public event Action<GameMode> ModeSelected;
    public event Action Cancelled;

    public bool IsVisible { get; private set; }

    private void Awake()
    {
        if (checkpointButton != null)
        {
            checkpointButton.onClick.AddListener(SelectCheckpoint);
        }

        if (noCheckpointButton != null)
        {
            noCheckpointButton.onClick.AddListener(SelectNoCheckpoint);
        }

        if (backButton != null)
        {
            backButton.onClick.AddListener(Cancel);
        }
    }

    private void OnDestroy()
    {
        if (checkpointButton != null)
        {
            checkpointButton.onClick.RemoveListener(SelectCheckpoint);
        }

        if (noCheckpointButton != null)
        {
            noCheckpointButton.onClick.RemoveListener(SelectNoCheckpoint);
        }

        if (backButton != null)
        {
            backButton.onClick.RemoveListener(Cancel);
        }
    }

    public void Show(LevelEntry level)
    {
        if (level == null)
        {
            throw new ArgumentNullException(nameof(level));
        }

        if (levelNumber != null)
        {
            levelNumber.text = level.NumberLabel;
        }

        if (levelName != null)
        {
            levelName.text = level.DisplayName;
        }

        if (levelSubtitle != null)
        {
            levelSubtitle.text = level.Subtitle;
        }

        BindBest(checkpointBest, level.RecordKey, GameMode.Checkpoint);
        BindBest(noCheckpointBest, level.RecordKey, GameMode.NoCheckpoint);

        IsVisible = true;
        if (panel != null)
        {
            panel.SetVisible(true);
        }
    }

    public void Hide()
    {
        IsVisible = false;
        if (panel != null)
        {
            panel.SetVisible(false);
        }
    }

    private static void BindBest(TMP_Text target, string recordKey, GameMode mode)
    {
        if (target == null)
        {
            return;
        }

        bool hasBest = RunStatsTracker.TryGetBest(recordKey, mode, out float best);
        target.text = hasBest ? RunTimer.Format(best) : "--:--.--";
        target.color = hasBest ? UITheme.Cyan : UITheme.Dim;
    }

    private void SelectCheckpoint() => ModeSelected?.Invoke(GameMode.Checkpoint);

    private void SelectNoCheckpoint() => ModeSelected?.Invoke(GameMode.NoCheckpoint);

    private void Cancel() => Cancelled?.Invoke();
}
