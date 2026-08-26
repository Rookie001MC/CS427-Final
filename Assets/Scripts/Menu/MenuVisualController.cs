using UnityEngine;

/// <summary>
/// Owns which menu panel is on screen and the transition between them. Knows nothing about what
/// the buttons do - <see cref="MenuController"/> asks for a screen, this decides how it appears.
/// </summary>
public sealed class MenuVisualController : MonoBehaviour
{
    public enum Screen
    {
        Main,
        LevelSelect
    }

    [SerializeField] private UIPanel mainPanel;
    [SerializeField] private UIPanel levelSelectPanel;

    public Screen Current { get; private set; } = Screen.Main;

    /// <summary>Raised after a screen change, so the controller can refresh that screen's data.</summary>
    public event System.Action<Screen> ScreenChanged;

    private void Awake()
    {
        // Both panels start hidden; Show() drives the first transition so the fade always runs.
        if (mainPanel != null)
        {
            mainPanel.ApplyImmediate(false);
        }

        if (levelSelectPanel != null)
        {
            levelSelectPanel.ApplyImmediate(false);
        }
    }

    public void Show(Screen screen, bool immediate = false)
    {
        Current = screen;

        UIPanel target = screen == Screen.Main ? mainPanel : levelSelectPanel;
        UIPanel other = screen == Screen.Main ? levelSelectPanel : mainPanel;

        if (other != null)
        {
            if (immediate)
            {
                other.ApplyImmediate(false);
            }
            else
            {
                other.SetVisible(false);
            }
        }

        if (target != null)
        {
            if (immediate)
            {
                target.ApplyImmediate(true);
            }
            else
            {
                target.SetVisible(true);
            }
        }

        ScreenChanged?.Invoke(screen);
    }

    /// <summary>Escape / Back behaviour: Level Select falls back to the main screen.</summary>
    public bool TryGoBack()
    {
        if (Current == Screen.Main)
        {
            return false;
        }

        Show(Screen.Main);
        return true;
    }
}
