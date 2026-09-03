using UnityEngine;

/// <summary>
/// Per-scene identity for a parkour level. Everything that used to be baked into the UI scripts
/// as Industrial-Parkour-specific text lives here instead, so the same systems drive any scene.
///
/// When <see cref="entry"/> is assigned it wins: the level is then described in exactly one place
/// (the shared <see cref="LevelEntry"/> asset the menu also reads), and the inline strings below
/// act only as a fallback for scenes that have no catalogue entry.
/// </summary>
public sealed class LevelInfo : MonoBehaviour
{
    [Tooltip("Shared catalogue asset. When set, its values override the inline fields below.")]
    [SerializeField] private LevelEntry entry;

    [Tooltip("Fallback headline used when no Level Entry is assigned.")]
    [SerializeField] private string displayName = "LEVEL";

    [Tooltip("Fallback subtitle used when no Level Entry is assigned.")]
    [SerializeField] private string subtitle = string.Empty;

    [Tooltip("Fallback record key. Defaults to the scene name.")]
    [SerializeField] private string recordKey = string.Empty;

    public LevelEntry Entry => entry;

    public string DisplayName
    {
        get
        {
            if (entry != null && !string.IsNullOrEmpty(entry.DisplayName))
            {
                return entry.DisplayName;
            }

            return string.IsNullOrEmpty(displayName) ? gameObject.scene.name : displayName;
        }
    }

    public string Subtitle => entry != null ? entry.Subtitle : subtitle;

    /// <summary>
    /// Which part of the game this level belongs to.
    ///
    /// Read straight off the catalogue asset, so a level's track is stated in exactly one place -
    /// the same place the menu reads it from. A scene with no entry assigned is reported as
    /// Training, which is the safe answer: the one thing that must never happen is a practice
    /// course being counted as a completion of the main run.
    /// </summary>
    public LevelTrack Track => entry != null ? entry.Track : LevelTrack.Training;

    /// <summary>Never empty: falls back to the scene name so two levels can never share records.</summary>
    public string RecordKey
    {
        get
        {
            if (entry != null && !string.IsNullOrEmpty(entry.RecordKey))
            {
                return entry.RecordKey;
            }

            return string.IsNullOrEmpty(recordKey) ? gameObject.scene.name : recordKey;
        }
    }
}
