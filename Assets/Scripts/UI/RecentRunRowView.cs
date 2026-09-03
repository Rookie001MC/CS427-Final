using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One row of the Recent Runs list.
///
/// Purely presentational, like <see cref="LevelCardView"/>: it renders a <see cref="RunLogData"/>
/// and knows nothing about where the data came from. A row with nothing to show hides its own
/// contents rather than drawing zeroes, because a run that did not happen must not look like a
/// run that did.
/// </summary>
public sealed class RecentRunRowView : MonoBehaviour
{
    [SerializeField] private Image accent;
    [SerializeField] private Image fill;
    [SerializeField] private TMP_Text title;
    [SerializeField] private TMP_Text trackLabel;
    [SerializeField] private TMP_Text meta;
    [SerializeField] private TMP_Text time;
    [SerializeField] private TMP_Text status;

    /// <summary>Draws one finished attempt. Pass null to leave the row empty.</summary>
    public void Bind(RunLogData run)
    {
        bool has = run != null;

        SetActive(has);

        if (!has)
        {
            return;
        }

        LevelTrack track = (LevelTrack)run.track;
        bool mainRun = track == LevelTrack.MainRun;
        bool failed = run.outcome == (int)RunOutcome.Failed;

        if (title != null)
        {
            title.text = run.displayName;
            title.color = UITheme.White;
        }

        if (trackLabel != null)
        {
            trackLabel.text = PlayerStatsFormat.Track(track);
            trackLabel.color = mainRun ? UITheme.CyanBright : UITheme.Orange;
        }

        if (meta != null)
        {
            meta.text = PlayerStatsFormat.RunMeta(run);
        }

        if (time != null)
        {
            time.text = PlayerStatsFormat.Time(run.seconds);
            time.color = failed ? UITheme.Label : UITheme.White;
        }

        if (status != null)
        {
            status.text = PlayerStatsFormat.RunStatus(run);
            status.color = failed
                ? UITheme.Orange
                : (run.personalBest ? UITheme.Green : UITheme.Cyan);
        }

        // The main run and a practice course have to be tellable apart at a glance, so the accent
        // carries the same cyan / orange split the main menu's rows use.
        if (accent != null)
        {
            accent.color = mainRun ? UITheme.CyanBright : UITheme.Orange;
        }

        if (fill != null)
        {
            fill.color = UITheme.PanelFillSoft;
        }
    }

    private void SetActive(bool visible)
    {
        if (accent != null) accent.enabled = visible;
        if (fill != null) fill.enabled = visible;
        if (title != null) title.enabled = visible;
        if (trackLabel != null) trackLabel.enabled = visible;
        if (meta != null) meta.enabled = visible;
        if (time != null) time.enabled = visible;
        if (status != null) status.enabled = visible;
    }
}
