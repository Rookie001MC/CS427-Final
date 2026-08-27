using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Mode-aware death overlay. Checkpoint Mode presents an automatic recovery countdown;
/// No-Checkpoint Mode presents the two decisions that can follow a failed attempt.
/// </summary>
public sealed class DeathRecoveryView : MonoBehaviour
{
    [SerializeField] private UIPanel panel;
    [SerializeField] private TMP_Text eyebrow;
    [SerializeField] private TMP_Text headline;
    [SerializeField] private TMP_Text detail;
    [SerializeField] private TMP_Text reasonValue;
    [SerializeField] private TMP_Text countdownValue;
    [SerializeField] private GameObject decisionActions;
    [SerializeField] private Button retryButton;
    [SerializeField] private Button mainMenuButton;

    public Button RetryButton => retryButton;
    public Button MainMenuButton => mainMenuButton;

    public void Show(GameMode mode, string reason, int reached, int total)
    {
        RunModeRules rules = RunModeRules.For(mode);

        if (eyebrow != null)
        {
            eyebrow.text = rules.DisplayName;
        }

        if (headline != null)
        {
            headline.text = rules.DeathHeadline;
        }

        bool checkpointMode = mode == GameMode.Checkpoint;

        if (detail != null)
        {
            detail.text = checkpointMode
                ? $"RETURNING TO CHECKPOINT {reached} / {total}"
                : "THIS ATTEMPT HAS ENDED";
        }

        if (reasonValue != null)
        {
            reasonValue.text = string.IsNullOrWhiteSpace(reason)
                ? string.Empty
                : reason.ToUpperInvariant();
        }

        if (countdownValue != null)
        {
            countdownValue.gameObject.SetActive(checkpointMode);
        }

        if (decisionActions != null)
        {
            decisionActions.SetActive(!checkpointMode);
        }

        if (checkpointMode)
        {
            SetCountdown(3);
        }

        if (panel != null)
        {
            panel.SetVisible(true);
        }
    }

    public void SetCountdown(int seconds)
    {
        if (countdownValue != null)
        {
            countdownValue.text = $"RESPAWNING IN {Mathf.Max(0, seconds)}";
        }
    }

    public void Hide()
    {
        if (panel != null)
        {
            panel.SetVisible(false);
        }
    }
}
