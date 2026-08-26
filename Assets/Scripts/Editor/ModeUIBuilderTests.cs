using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class ModeUIBuilderTests
{
    [Test]
    public void GameplayUIBuilder_CreatesModeAwareRecoveryUI()
    {
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(
            UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
            UnityEditor.SceneManagement.NewSceneMode.Single);
        new GameObject("GameManager", typeof(GameManager), typeof(LevelInfo));
        new GameObject("RunTimer", typeof(RunTimer));
        new GameObject("CheckpointManager", typeof(CheckpointManager));

        GameplayUIBuilder.Build();

        Assert.That(GameObject.Find("GameplayUI/DeathRecovery"), Is.Not.Null);
        Assert.That(GameObject.Find("GameplayUI/GameOver"), Is.Null);
        Assert.That(GameObject.Find("GameplayUI/HUD/Mode"), Is.Not.Null);
        Assert.That(GameObject.Find("GameplayUI/LevelComplete/Mode"), Is.Not.Null);
    }

    [Test]
    public void MainMenuBuilder_CreatesAndWiresModeModal()
    {
        MainMenuBuilder.Build();

        GameObject modalObject = GameObject.Find("MenuCanvas/ModeSelectionModal");
        Assert.That(modalObject, Is.Not.Null);
        Assert.That(modalObject.GetComponent<ModeSelectionView>(), Is.Not.Null);
        Assert.That(GameObject.Find("MenuCanvas/ModeSelectionModal/Panel/CheckpointMode"), Is.Not.Null);
        Assert.That(GameObject.Find("MenuCanvas/ModeSelectionModal/Panel/NoCheckpointMode"), Is.Not.Null);

        MenuController controller = Object.FindFirstObjectByType<MenuController>();
        var serialized = new SerializedObject(controller);
        Assert.That(serialized.FindProperty("modeSelection").objectReferenceValue, Is.Not.Null);

        SceneLoader loader = Object.FindFirstObjectByType<SceneLoader>();
        var loaderSerialized = new SerializedObject(loader);
        Assert.That(loaderSerialized.FindProperty("modeLabel").objectReferenceValue, Is.Not.Null);
    }
}
