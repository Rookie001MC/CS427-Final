using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Brief non-blocking overlay shown when a sequential checkpoint is crossed. Never touches
/// Time.timeScale and never blocks raycasts - the run continues underneath it.
/// </summary>
public sealed class CheckpointPopup : MonoBehaviour
{
    [SerializeField] private UIPanel panel;
    [SerializeField] private TMP_Text bannerText;
    [SerializeField] private TMP_Text title;
    [SerializeField] private TMP_Text counter;
    [SerializeField] private TMP_Text splitValue;
    [SerializeField] private TMP_Text deltaValue;
    [SerializeField] private TMP_Text deltaLabel;
    [SerializeField] private TMP_Text totalValue;
    [SerializeField] private TMP_Text footer;
    [SerializeField] private GameObject deltaColumn;

    [SerializeField, Min(0.5f)] private float holdSeconds = 1.8f;

    private Coroutine hideRoutine;

    /// <summary>
    /// Shows the popup for one crossing. <paramref name="bestSplit"/> is negative when no
    /// previous split exists for this checkpoint, in which case the comparison column is hidden
    /// rather than invented.
    /// </summary>
    public void Show(int index, int total, float split, float cumulative, float bestSplit, GameMode mode)
    {
        RunModeRules rules = RunModeRules.For(mode);
        bool savesProgress = mode == GameMode.Checkpoint;

        if (bannerText != null)
        {
            bannerText.text = savesProgress ? "CHECKPOINT REACHED" : "SECTION CLEARED";
        }

        if (title != null)
        {
            title.text = rules.ProgressName;
        }

        if (counter != null)
        {
            counter.text = $"{index} / {total}";
        }

        if (splitValue != null)
        {
            splitValue.text = RunTimer.Format(split);
            splitValue.color = UITheme.Green;
        }

        if (totalValue != null)
        {
            totalValue.text = RunTimer.Format(cumulative);
        }

        bool hasComparison = bestSplit >= 0f;

        if (deltaColumn != null)
        {
            deltaColumn.SetActive(hasComparison);
        }

        if (hasComparison && deltaValue != null)
        {
            float delta = split - bestSplit;
            deltaValue.text = RunTimer.FormatDelta(delta);
            deltaValue.color = delta <= 0f ? UITheme.Green : UITheme.Orange;

            if (deltaLabel != null)
            {
                deltaLabel.text = "VS BEST";
            }
        }

        if (footer != null)
        {
            footer.text = savesProgress
                ? $"CHECKPOINT {index} / {total} SECURED"
                : $"SPLIT {index} / {total} RECORDED";
        }

        if (panel != null)
        {
            panel.SetVisible(true);
        }

        if (!gameObject.activeInHierarchy)
        {
            return;
        }

        if (hideRoutine != null)
        {
            StopCoroutine(hideRoutine);
        }

        hideRoutine = StartCoroutine(HideAfterHold());
    }

    private IEnumerator HideAfterHold()
    {
        yield return new WaitForSecondsRealtime(holdSeconds);

        if (panel != null)
        {
            panel.SetVisible(false);
        }

        hideRoutine = null;
    }

    /// <summary>Hides immediately - used when a run ends while the popup is still up.</summary>
    public void HideNow()
    {
        if (hideRoutine != null)
        {
            StopCoroutine(hideRoutine);
            hideRoutine = null;
        }

        if (panel != null)
        {
            panel.SetVisible(false);
        }
    }
}
