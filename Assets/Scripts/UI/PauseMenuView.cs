using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pause overlay. Buttons whose destination scenes do not exist yet (Level Select, Main Menu)
/// are shown but disabled, rather than wired to a scene load that would throw.
/// </summary>
public sealed class PauseMenuView : MonoBehaviour
{
    [SerializeField] private UIPanel panel;
    [SerializeField] private TMP_Text elapsedValue;
    [SerializeField] private TMP_Text checkpointValue;
    [SerializeField] private TMP_Text bestValue;
    [SerializeField] private TMP_Text footer;

    [Header("Buttons")]
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button restartButton;
    [SerializeField] private Button levelSelectButton;
    [SerializeField] private Button mainMenuButton;

    public Button ResumeButton => resumeButton;
    public Button RestartButton => restartButton;
    public Button LevelSelectButton => levelSelectButton;
    public Button MainMenuButton => mainMenuButton;

    public void SetVisible(bool visible)
    {
        if (panel != null)
        {
            panel.SetVisible(visible);
        }
    }

    public void Bind(float elapsed, int reached, int total, bool hasBest, float bestTime)
    {
        if (elapsedValue != null)
        {
            elapsedValue.text = RunTimer.Format(elapsed);
        }

        if (checkpointValue != null)
        {
            checkpointValue.text = $"{reached} / {total}";
        }

        if (bestValue == null)
        {
            return;
        }

        bestValue.text = hasBest ? RunTimer.Format(bestTime) : "--:--.--";
        bestValue.color = hasBest ? UITheme.Cyan : UITheme.Dim;
    }

    /// <summary>Sets the level name strip at the bottom of the overlay.</summary>
    public void SetLevelCaption(string caption)
    {
        if (footer != null)
        {
            footer.text = caption;
        }
    }
}
