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

        /// <summary>
        /// The main run, on its own, as a hero panel. It is a separate screen rather than the first
        /// card of a list because the game has one real run and two practice courses, and a list of
        /// three equal cards is a menu that says the opposite.
        /// </summary>
        MainRun,

        /// <summary>The practice courses, grouped and labelled as such.</summary>
        Training
    }

    [SerializeField] private UIPanel mainPanel;
    [SerializeField] private UIPanel mainRunPanel;
    [SerializeField] private UIPanel trainingPanel;

    public Screen Current { get; private set; } = Screen.Main;

    /// <summary>Raised after a screen change, so the controller can refresh that screen's data.</summary>
    public event System.Action<Screen> ScreenChanged;

    private void Awake()
    {
        // Every panel starts hidden; Show() drives the first transition so the fade always runs.
        Apply(mainPanel, false, true);
        Apply(mainRunPanel, false, true);
        Apply(trainingPanel, false, true);
    }

    public void Show(Screen screen, bool immediate = false)
    {
        Current = screen;

        Apply(mainPanel, screen == Screen.Main, immediate);
        Apply(mainRunPanel, screen == Screen.MainRun, immediate);
        Apply(trainingPanel, screen == Screen.Training, immediate);

        ScreenChanged?.Invoke(screen);
    }

    private static void Apply(UIPanel panel, bool visible, bool immediate)
    {
        if (panel == null)
        {
            return;
        }

        if (immediate)
        {
            panel.ApplyImmediate(visible);
        }
        else
        {
            panel.SetVisible(visible);
        }
    }

    /// <summary>Escape / Back behaviour: every other screen falls back to the main one.</summary>
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
