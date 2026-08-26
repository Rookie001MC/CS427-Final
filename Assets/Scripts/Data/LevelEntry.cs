using UnityEngine;

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

    [Tooltip("Key session records are stored under. Defaults to the scene name.")]
    [SerializeField] private string recordKey = string.Empty;

    [Header("Presentation")]
    [Tooltip("Still used on the level card and the loading screen.")]
    [SerializeField] private Texture preview;

    [Tooltip("Shown on the loading screen while the scene streams in.")]
    [SerializeField, TextArea(2, 3)] private string tip = string.Empty;

    public string SceneName => sceneName;
    public int LevelNumber => levelNumber;
    public string DisplayName => displayName;
    public string Subtitle => subtitle;
    public string RecordKey => string.IsNullOrEmpty(recordKey) ? sceneName : recordKey;
    public Texture Preview => preview;
    public string Tip => tip;

    /// <summary>"LEVEL 01" style label.</summary>
    public string NumberLabel => $"LEVEL {levelNumber:00}";
}
