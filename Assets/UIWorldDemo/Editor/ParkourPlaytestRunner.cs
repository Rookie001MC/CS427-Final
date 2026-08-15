using UnityEditor;
using UnityEngine;

/// <summary>
/// Enters play mode and injects the ParkourAutoPilot harness. The harness object is created at
/// runtime only, so the scene on disk is never modified by a play-test.
/// </summary>
[InitializeOnLoad]
public static class ParkourPlaytestRunner
{
    private const string ArmedKey = "ParkourPlaytest.Armed";

    static ParkourPlaytestRunner()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    [MenuItem("Tools/Parkour/T - Play-test Route")]
    public static void RunPlaytest()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Playtest] Already in play mode.");
            return;
        }

        // An unfocused Unity Editor does not tick play mode unless Run In Background is set,
        // which would leave the harness coroutine frozen in playmode_transition forever.
        if (!PlayerSettings.runInBackground)
        {
            PlayerSettings.runInBackground = true;
            Debug.Log("[Playtest] Enabled PlayerSettings.runInBackground so play mode ticks while unfocused.");
        }

        SessionState.SetBool(ArmedKey, true);
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode)
        {
            return;
        }

        if (!SessionState.GetBool(ArmedKey, false))
        {
            return;
        }

        SessionState.SetBool(ArmedKey, false);

        GameObject host = new GameObject("~ParkourAutoPilot");
        host.AddComponent<ParkourAutoPilot>();
        Debug.Log("[Playtest] AutoPilot injected.");
    }
}
