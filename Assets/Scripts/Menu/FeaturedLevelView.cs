using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The hero panel for the main run: one level, given the whole screen.
///
/// It is <see cref="LevelCardView"/>'s opposite number and deliberately not the same component. A
/// card is one of a row and has to stay comparable with its neighbours; this has no neighbours, so
/// the level's name is set in the display face at title size, the preview is full-bleed behind it,
/// and the only control is START RUN. That difference is the whole point of the screen - a player
/// arriving at the main menu should not have to read three equal-looking cards to work out which
/// one is the game.
///
/// Purely presentational, like the card: it renders a <see cref="LevelEntry"/> and reports the
/// click upward.
/// </summary>
public sealed class FeaturedLevelView : MonoBehaviour
{
    [SerializeField] private Button startButton;
    [SerializeField] private RawImage preview;
    [SerializeField] private TMP_Text trackLabel;
    [SerializeField] private TMP_Text numberLabel;
    [SerializeField] private TMP_Text title;
    [SerializeField] private TMP_Text subtitle;
    [SerializeField] private TMP_Text statusValue;
    [SerializeField] private TMP_Text clearedValue;
    [SerializeField] private TMP_Text tip;

    public LevelEntry Entry { get; private set; }

    /// <summary>Raised when START RUN is pressed, with the level it represents.</summary>
    public event System.Action<LevelEntry> Started;

    private void Awake()
    {
        if (startButton != null)
        {
            startButton.onClick.AddListener(HandleClick);
        }
    }

    private void OnDestroy()
    {
        if (startButton != null)
        {
            startButton.onClick.RemoveListener(HandleClick);
        }
    }

    private void HandleClick() => Started?.Invoke(Entry);

    public void Bind(LevelEntry entry)
    {
        Entry = entry;

        if (startButton != null)
        {
            startButton.interactable = entry != null;
        }

        if (entry == null)
        {
            // Nothing is hidden. A main run that failed to resolve has to be visible as a fault
            // rather than as an empty screen the player thinks is finished loading.
            if (title != null)
            {
                title.text = "NO MAIN RUN";
            }

            if (subtitle != null)
            {
                subtitle.text = "No level in the catalogue is marked as the main run.";
            }

            return;
        }

        if (trackLabel != null)
        {
            trackLabel.text = entry.TrackLabel;
        }

        if (numberLabel != null)
        {
            numberLabel.text = entry.NumberLabel;
        }

        if (title != null)
        {
            title.text = entry.DisplayName;
        }

        if (subtitle != null)
        {
            subtitle.text = entry.Subtitle;
        }

        if (tip != null)
        {
            tip.text = entry.Tip;
        }

        if (preview != null)
        {
            preview.texture = entry.Preview;
            preview.enabled = entry.Preview != null;
        }

        int completed = RunStatsTracker.CountCompletedModes(entry.RecordKey);
        Color colour = completed == 2 ? UITheme.Green : (completed == 1 ? UITheme.Cyan : UITheme.Label);

        if (clearedValue != null)
        {
            clearedValue.text = $"{completed} / 2";
            clearedValue.color = colour;
        }

        if (statusValue != null)
        {
            statusValue.text = completed == 2
                ? "CLEARED"
                : (completed == 1 ? "IN PROGRESS" : "NOT YET RUN");
            statusValue.color = colour;
        }
    }
}
