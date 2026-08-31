using UnityEngine;

/// <summary>
/// Which part of the game a level belongs to.
///
/// The menu needs this and the levels themselves do not, which is exactly why it lives on the
/// catalogue asset rather than in the menu: the two training maps and the main run are the same
/// kind of scene running the same systems, and the only thing that separates them is what the game
/// is telling the player they are for. A menu that hard-coded "the third one is the real one" would
/// be wrong the moment a fourth was added.
/// </summary>
public enum LevelTrack
{
    /// <summary>A practice course. Teaches a move set; not the run the game is about.</summary>
    Training = 0,

    /// <summary>The main run. There is one, and PLAY goes straight to it.</summary>
    MainRun = 1
}

/// <summary>
/// Shared metadata for one playable level.
///
/// This exists because <see cref="LevelInfo"/> is a scene component: the menu has to describe
/// levels it has not loaded, so the data cannot live only inside the gameplay scenes. LevelInfo
/// reads from this asset when one is assigned, keeping a single source of truth rather than two
/// parallel descriptions of the same level.
/// </summary>
[CreateAssetMenu(fileName = "LevelEntry", menuName = "Parkour/Level Entry")]
public sealed class LevelEntry : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Scene name exactly as it appears in Build Settings.")]
    [SerializeField] private string sceneName;

    [Tooltip("Ordinal shown as LEVEL 01, LEVEL 02, ...")]
    [SerializeField, Min(1)] private int levelNumber = 1;

    [SerializeField] private string displayName = "LEVEL";
    [SerializeField] private string subtitle = string.Empty;

    [Tooltip("Training course, or the main run PLAY launches. One level should be the main run.")]
    [SerializeField] private LevelTrack track = LevelTrack.Training;

    [Tooltip("Key session records are stored under. Defaults to the scene name.")]
    [SerializeField] private string recordKey = string.Empty;

    [Header("Presentation")]
    [Tooltip("Still used on the level card and the loading screen.")]
    [SerializeField] private Texture preview;

    [Tooltip("Shown on the loading screen while the scene streams in.")]
    [SerializeField, TextArea(2, 3)] private string tip = string.Empty;

    public string SceneName => sceneName;
    public LevelTrack Track => track;

    /// <summary>True for the one level PLAY launches.</summary>
    public bool IsMainRun => track == LevelTrack.MainRun;

    /// <summary>What the menu calls this level's track, in the menu's voice.</summary>
    public string TrackLabel => track == LevelTrack.MainRun ? "MAIN RUN" : "TRAINING";

    public int LevelNumber => levelNumber;
    public string DisplayName => displayName;
    public string Subtitle => subtitle;
    public string RecordKey => string.IsNullOrEmpty(recordKey) ? sceneName : recordKey;
    public Texture Preview => preview;
    public string Tip => tip;

    /// <summary>"LEVEL 01" style label.</summary>
    public string NumberLabel => $"LEVEL {levelNumber:00}";
}
