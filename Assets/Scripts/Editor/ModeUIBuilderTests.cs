using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class ModeUIBuilderTests
{
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
