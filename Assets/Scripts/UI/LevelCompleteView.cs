using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// End-of-run summary. Star rating is derived from deaths rather than from a persisted score,
/// so it stays honest until the save system lands in Phase 5.
/// </summary>
public sealed class LevelCompleteView : MonoBehaviour
{
    [SerializeField] private UIPanel panel;

    [Header("Header")]
    [SerializeField] private TMP_Text modeValue;
    [SerializeField] private TMP_Text stageName;
    [SerializeField] private TMP_Text stageSubtitle;
    [SerializeField] private List<Image> stars = new List<Image>();

    [Header("Rows")]
    [SerializeField] private TMP_Text finishTimeValue;
    [SerializeField] private TMP_Text finishTimeNote;
    [SerializeField] private TMP_Text personalBestValue;
    [SerializeField] private TMP_Text personalBestNote;
    [SerializeField] private TMP_Text checkpointsValue;
    [SerializeField] private TMP_Text checkpointsNote;
    [SerializeField] private TMP_Text deathsValue;
    [SerializeField] private TMP_Text deathsNote;
    [SerializeField] private TMP_Text maxSpeedValue;
    [SerializeField] private TMP_Text maxSpeedNote;

    [Header("Buttons")]
    [SerializeField] private Button replayButton;
    [SerializeField] private Button levelSelectButton;
    [SerializeField] private Button mainMenuButton;

    public Button ReplayButton => replayButton;
    public Button LevelSelectButton => levelSelectButton;
    public Button MainMenuButton => mainMenuButton;

    public void SetVisible(bool visible)
    {
        if (panel != null)
        {
            panel.SetVisible(visible);
        }
    }

    public void Bind(float finishTime, bool isNewBest, bool hasBest, float bestTime,
        int reached, int total, int deaths, float maxSpeed, string levelName, string levelSubtitle,
        GameMode mode)
    {
        if (modeValue != null)
        {
            modeValue.text = RunModeRules.For(mode).DisplayName;
        }

        if (stageName != null && !string.IsNullOrEmpty(levelName))
        {
            stageName.text = levelName;
        }

        if (stageSubtitle != null)
        {
            stageSubtitle.text = levelSubtitle ?? string.Empty;
        }

        int rating = deaths == 0 ? 3 : (deaths <= 2 ? 2 : 1);
        for (int i = 0; i < stars.Count; i++)
        {
            if (stars[i] == null)
            {
                continue;
            }

            stars[i].color = i < rating
                ? UITheme.Cyan
                : new Color(UITheme.Cyan.r, UITheme.Cyan.g, UITheme.Cyan.b, 0.15f);
        }

        if (finishTimeValue != null)
        {
            finishTimeValue.text = RunTimer.Format(finishTime);
            finishTimeValue.color = UITheme.Cyan;
        }

        if (finishTimeNote != null)
        {
            finishTimeNote.text = isNewBest ? "Personal Best!" : string.Empty;
            finishTimeNote.color = UITheme.Green;
        }

        if (personalBestValue != null)
        {
            personalBestValue.text = hasBest ? RunTimer.Format(bestTime) : "--:--.--";
            personalBestValue.color = hasBest ? UITheme.White : UITheme.Dim;
        }

        if (personalBestNote != null)
        {
            personalBestNote.text = mode == GameMode.Checkpoint
                ? "LOCAL BEST • CHECKPOINT"
                : "LOCAL BEST • NO CHECKPOINT";
            personalBestNote.color = UITheme.Dim;
        }

        if (checkpointsValue != null)
        {
            checkpointsValue.text = $"{reached} / {total}";
        }

        if (checkpointsNote != null)
        {
            bool all = total > 0 && reached >= total;
            checkpointsNote.text = all ? "All cleared" : "Partial";
            checkpointsNote.color = all ? UITheme.Green : UITheme.Dim;
        }

        if (deathsValue != null)
        {
            deathsValue.text = deaths.ToString();
        }

        if (deathsNote != null)
        {
            deathsNote.text = deaths == 0 ? "Flawless run" : (deaths == 1 ? "One reset" : $"{deaths} resets");
            deathsNote.color = deaths == 0 ? UITheme.Green : UITheme.Dim;
        }

        if (maxSpeedValue != null)
        {
            maxSpeedValue.text = $"{maxSpeed:0.0} m/s";
        }

        if (maxSpeedNote != null)
        {
            maxSpeedNote.text = "Peak horizontal";
            maxSpeedNote.color = UITheme.Dim;
        }
    }
}
