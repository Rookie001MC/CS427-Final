using UnityEngine;

public interface IRunRecordPersistence
{
    string Load();
    void Save(string json);
}

public sealed class PlayerPrefsRunRecordPersistence : IRunRecordPersistence
{
    public const string StorageKey = "SkyboundTrials.RunRecords.v1";

    public string Load() => PlayerPrefs.GetString(StorageKey, string.Empty);

    public void Save(string json)
    {
        PlayerPrefs.SetString(StorageKey, json);
        PlayerPrefs.Save();
    }
}
