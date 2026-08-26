using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One level card on the Level Select screen. Purely presentational: it renders a
/// <see cref="LevelEntry"/> and reports clicks upward.
/// </summary>
public sealed class LevelCardView : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private RawImage preview;
    [SerializeField] private TMP_Text indexLabel;
    [SerializeField] private TMP_Text title;
    [SerializeField] private TMP_Text subtitle;
    [SerializeField] private TMP_Text bestValue;
    [SerializeField] private TMP_Text statusValue;
    [SerializeField] private List<Image> stars = new List<Image>();

    public LevelEntry Entry { get; private set; }

    /// <summary>Raised when the card is clicked, with the level it represents.</summary>
    public event System.Action<LevelEntry> Clicked;

    private void Awake()
    {
        if (button != null)
        {
            button.onClick.AddListener(HandleClick);
        }
    }

    private void OnDestroy()
    {
        if (button != null)
        {
            button.onClick.RemoveListener(HandleClick);
        }
    }

    private void HandleClick() => Clicked?.Invoke(Entry);

    public void Bind(LevelEntry entry)
    {
        Entry = entry;

        if (entry == null)
        {
            gameObject.SetActive(false);
            return;
        }

        gameObject.SetActive(true);

        if (indexLabel != null)
        {
            indexLabel.text = $"{entry.LevelNumber:00}";
        }

        if (title != null)
        {
            title.text = entry.DisplayName;
        }

        if (subtitle != null)
        {
            subtitle.text = entry.Subtitle;
        }

        if (preview != null)
        {
            preview.texture = entry.Preview;
            preview.enabled = entry.Preview != null;
        }

        int completed = RunStatsTracker.CountCompletedModes(entry.RecordKey);
        Color completionColor = completed == 2
            ? UITheme.Green
            : (completed == 1 ? UITheme.Cyan : UITheme.Label);

        if (bestValue != null)
        {
            bestValue.text = $"{completed} / 2";
            bestValue.color = completionColor;
        }

        if (statusValue != null)
        {
            statusValue.text = completed == 2
                ? "CLEARED"
                : (completed == 1 ? "IN PROGRESS" : "AVAILABLE");
            statusValue.color = completionColor;
        }

        // Difficulty markers stay unlit until at least one mode has a completed run.
        for (int i = 0; i < stars.Count; i++)
        {
            if (stars[i] != null)
            {
                stars[i].color = completed > 0
                    ? UITheme.Cyan
                    : new Color(UITheme.Cyan.r, UITheme.Cyan.g, UITheme.Cyan.b, 0.15f);
            }
        }
    }
}
