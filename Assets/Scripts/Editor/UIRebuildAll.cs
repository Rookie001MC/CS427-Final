using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One entry point that regenerates every UI surface in the project and audits the result.
///
/// The gameplay UI is per-scene: <see cref="GameplayUIBuilder"/> rebuilds the GameplayUI root of
/// whichever scene happens to be open, so running it by hand against the wrong scene drops a
/// second HUD into the main menu. This opens each gameplay scene in turn, rebuilds, saves, and
/// puts the editor back on the scene it started from.
/// </summary>
public static class UIRebuildAll
{
    private const string MainMenuScene = "Assets/Scenes/MainMenu.unity";

    /// <summary>Scenes that carry a GameplayUI root.</summary>
    public static readonly string[] GameplayScenes =
    {
        "Assets/Scenes/IndustrialParkour.unity",
        "Assets/Scenes/UIWorldDemo.unity",
        SkyboundCityBuilder.ScenePath
    };

    /// <summary>
    /// Every scene the typography audit has to cover.
    ///
    /// SkyboundCity carries both a shared GameplayUI root and its distinct mission instrument, so
    /// both surfaces are audited against the same typography floor. Missing scenes are skipped,
    /// which keeps this harmless before a generated scene exists.
    /// </summary>
    public static readonly string[] AllUIScenes =
    {
        "Assets/Scenes/IndustrialParkour.unity",
        "Assets/Scenes/UIWorldDemo.unity",
        SkyboundCityBuilder.ScenePath,
        MainMenuScene
    };

    [MenuItem("Tools/Parkour UI/Rebuild All UI", priority = 0)]
    public static void RebuildAll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[UI] Exit play mode before rebuilding the UI.");
            return;
        }

        // MainMenuBuilder replaces the open scene outright, so an unsaved edit anywhere would go
        // with it. Ask first rather than discarding the user's work silently.
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            Debug.Log("[UI] Rebuild cancelled.");
            return;
        }

        string reopen = SceneManager.GetActiveScene().path;

        foreach (string scenePath in GameplayScenes)
        {
            if (!System.IO.File.Exists(scenePath))
            {
                Debug.LogWarning($"[UI] Skipping missing scene: {scenePath}");
                continue;
            }

            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            GameplayUIBuilder.Build();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[UI] Rebuilt gameplay UI in {scenePath}");
        }

        MainMenuBuilder.Build();

        List<UITypographyAudit.Finding> findings = new List<UITypographyAudit.Finding>();
        foreach (string scenePath in AllUIScenes)
        {
            if (!System.IO.File.Exists(scenePath))
            {
                continue;
            }

            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            findings.AddRange(UITypographyAudit.AuditOpenScene());
        }

        if (!string.IsNullOrEmpty(reopen) && System.IO.File.Exists(reopen))
        {
            EditorSceneManager.OpenScene(reopen, OpenSceneMode.Single);
        }

        UITypographyAudit.Report(findings);
        Debug.Log("[UI] Rebuild All UI complete.");
    }
}
