using UnityEngine;

/// <summary>
/// The career document's storage slot.
///
/// One PlayerPrefs key holding one JSON document, exactly as
/// <see cref="PlayerPrefsRunRecordPersistence"/> does for the personal-best ledger - and it
/// reuses that file's <see cref="IRunRecordPersistence"/> interface rather than declaring a
/// second identical one. The interface is a string slot with a load and a save; the two records
/// differ in what they contain, not in how they are stored, and a parallel abstraction would
/// have to be kept in step for no benefit.
///
/// A separate key, not a separate mechanism: the whole career is one value, so there is no bag of
/// unrelated keys to keep consistent, and an unreadable career can never damage the run records.
/// </summary>
public sealed class PlayerPrefsPlayerStatsPersistence : IRunRecordPersistence
{
    public const string StorageKey = "SkyboundTrials.PlayerStats.v1";

    public string Load() => PlayerPrefs.GetString(StorageKey, string.Empty);

    public void Save(string json)
    {
        PlayerPrefs.SetString(StorageKey, json);
        PlayerPrefs.Save();
    }
}
