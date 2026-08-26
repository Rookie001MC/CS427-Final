using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The one route from gameplay back to the menu. Every exit runs through here so the timescale
/// and cursor are always restored the same way, no matter which panel the player left from.
/// </summary>
public static class MenuNavigation
{
    /// <summary>Scene name of the menu. Matches the entry in Build Settings.</summary>
    public const string MainMenuScene = "MainMenu";

    /// <summary>Returns to the menu on its main screen.</summary>
    public static void GoToMainMenu() => Leave(false);

    /// <summary>
    /// Returns to the menu with Level Select already open. Cheaper and simpler than a separate
    /// LevelSelect scene, and there is only one menu scene to keep consistent.
    /// </summary>
    public static void GoToLevelSelect() => Leave(true);

    private static void Leave(bool openLevelSelect)
    {
        // A scene change out of a paused game must never carry timeScale 0 across with it.
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        MenuController.OpenLevelSelectOnStart = openLevelSelect;
        SceneManager.LoadScene(MainMenuScene);
    }
}
