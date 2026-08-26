using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Failure overlay. Uses the orange failure accent from the reference rather than the cyan run
/// accent, so death reads as a different mode at a glance.
/// </summary>
public sealed class GameOverView : MonoBehaviour
{
    [SerializeField] private UIPanel panel;

    [Header("Headline")]
    [SerializeField] private TMP_Text headlineTop;
    [SerializeField] private TMP_Text headlineBottom;

    [Header("Stats")]
    [SerializeField] private TMP_Text timeValue;
    [SerializeField] private TMP_Text checkpointValue;
    [SerializeField] private TMP_Text deathsValue;

    [Header("Cause")]
    [SerializeField] private TMP_Text causeHeadline;
    [SerializeField] private TMP_Text causeTip;

    [Header("Buttons")]
    [SerializeField] private Button tryAgainButton;
    [SerializeField] private Button restartButton;
    [SerializeField] private Button quitButton;

    public Button TryAgainButton => tryAgainButton;
    public Button RestartButton => restartButton;
    public Button QuitButton => quitButton;

    public void SetVisible(bool visible)
    {
        if (panel != null)
        {
            panel.SetVisible(visible);
        }
    }

    public void Bind(float elapsed, int reached, int total, int deaths, string reason)
    {
        bool fell = !string.IsNullOrEmpty(reason) && reason.Contains("death plane");

        if (headlineTop != null)
        {
            headlineTop.text = fell ? "FALL" : "RUN";
        }

        if (headlineBottom != null)
        {
            headlineBottom.text = fell ? "DETECTED" : "TERMINATED";
        }

        if (timeValue != null)
        {
            timeValue.text = RunTimer.Format(elapsed);
        }

        if (checkpointValue != null)
        {
            checkpointValue.text = $"{reached} / {total}";
        }

        if (deathsValue != null)
        {
            deathsValue.text = deaths.ToString();
        }

        if (causeHeadline != null)
        {
            causeHeadline.text = reached > 0
                ? $"Lost the route after checkpoint {reached}"
                : "Lost the route before the first checkpoint";
        }

        if (causeTip != null)
        {
            causeTip.text = reached > 0
                ? "TRY AGAIN resumes from your last checkpoint. The clock keeps running."
                : "TRY AGAIN returns you to the start line. The clock keeps running.";
        }
    }
}
