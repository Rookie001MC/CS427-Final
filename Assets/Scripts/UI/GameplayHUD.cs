using TMPro;
using UnityEngine;

/// <summary>
/// The always-on run readout: checkpoint progress top-left, clock top-right, speed under the
/// clock. Deliberately sparse so it never competes with the parkour route for attention.
/// </summary>
public sealed class GameplayHUD : MonoBehaviour
{
    [SerializeField] private UIPanel panel;
    [SerializeField] private TMP_Text modeValue;
    [SerializeField] private TMP_Text checkpointLabel;
    [SerializeField] private TMP_Text checkpointValue;
    [SerializeField] private TMP_Text timerValue;
    [SerializeField] private TMP_Text speedValue;
    [SerializeField] private RunStatsTracker stats;

    public UIPanel Panel => panel;

    public void SetMode(GameMode mode)
    {
        RunModeRules rules = RunModeRules.For(mode);

        if (modeValue != null)
        {
            modeValue.text = rules.DisplayName;
        }

        if (checkpointLabel != null)
        {
            checkpointLabel.text = rules.ProgressName;
        }
    }

    public void SetVisible(bool visible)
    {
        if (panel != null)
        {
            panel.SetVisible(visible);
        }
    }

    public void SetCheckpoint(int reached, int total)
    {
        if (checkpointValue != null)
        {
            checkpointValue.text = $"{reached} / {total}";
        }
    }

    public void SetTime(float seconds)
    {
        if (timerValue != null)
        {
            timerValue.text = RunTimer.Format(seconds);
        }
    }

    private void Update()
    {
        if (speedValue == null || stats == null)
        {
            return;
        }

        speedValue.text = $"{stats.CurrentSpeed:0.0} m/s";
    }
}
