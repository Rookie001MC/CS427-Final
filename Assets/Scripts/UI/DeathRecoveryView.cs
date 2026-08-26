using TMPro;
using UnityEngine;

/// <summary>
/// Brief non-interactive feedback while <see cref="GameManager"/> performs automatic recovery.
/// The view owns no recovery controls or run-state decisions.
/// </summary>
public sealed class DeathRecoveryView : MonoBehaviour
{
    [SerializeField] private UIPanel panel;
    [SerializeField] private TMP_Text eyebrow;
    [SerializeField] private TMP_Text headline;
    [SerializeField] private TMP_Text detail;
    [SerializeField] private TMP_Text reasonValue;

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

        if (detail != null)
        {
            detail.text = mode == GameMode.Checkpoint
                ? $"RETURNING TO CHECKPOINT {reached} / {total}"
                : "RETURNING TO LEVEL START";
        }

        if (reasonValue != null)
        {
            reasonValue.text = string.IsNullOrWhiteSpace(reason)
                ? string.Empty
                : reason.ToUpperInvariant();
        }

        if (panel != null)
        {
            panel.SetVisible(true);
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
