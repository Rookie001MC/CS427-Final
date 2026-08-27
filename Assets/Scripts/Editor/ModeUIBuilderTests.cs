using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

public sealed class ModeUIBuilderTests
{
    [Test]
    public void GameplayUIBuilder_CreatesModeAwareRecoveryUI()
    {
        DeathRecoveryView view = BuildRecoveryView();

        Assert.That(GameObject.Find("GameplayUI/DeathRecovery"), Is.Not.Null);
        Assert.That(GameObject.Find("GameplayUI/GameOver"), Is.Null);
        Assert.That(GameObject.Find("GameplayUI/DeathRecovery/Countdown"), Is.Not.Null);
        Assert.That(GameObject.Find("GameplayUI/DeathRecovery/Actions/RetryRun"), Is.Not.Null);
        Assert.That(GameObject.Find("GameplayUI/DeathRecovery/Actions/MainMenu"), Is.Not.Null);
        Assert.That(GameObject.Find("GameplayUI/HUD/Mode"), Is.Not.Null);
        Assert.That(GameObject.Find("GameplayUI/LevelComplete/Mode"), Is.Not.Null);

        var serialized = new SerializedObject(view);
        Assert.That(serialized.FindProperty("retryButton")?.objectReferenceValue, Is.Not.Null);
        Assert.That(serialized.FindProperty("mainMenuButton")?.objectReferenceValue, Is.Not.Null);

        UIPanel panel = GameObject.Find("GameplayUI/DeathRecovery").GetComponent<UIPanel>();
        var panelSerialized = new SerializedObject(panel);
        Assert.That(panelSerialized.FindProperty("interactable").boolValue, Is.True);
    }

    [Test]
    public void DeathRecoveryView_CheckpointModeShowsCountdownWithoutDecisions()
    {
        DeathRecoveryView view = BuildRecoveryViewWithoutPanel();

        view.Show(GameMode.Checkpoint, "fell", 2, 6);

        GameObject countdown = GameObject.Find("GameplayUI/DeathRecovery/Countdown");
        Assert.That(countdown, Is.Not.Null);
        Assert.That(countdown.activeSelf, Is.True);
        Assert.That(countdown.GetComponent<TMP_Text>().text, Is.EqualTo("RESPAWNING IN 3"));
        GameObject actions = GameObject.Find("GameplayUI/DeathRecovery/Actions");
        Assert.That(actions, Is.Not.Null);
        Assert.That(actions.activeSelf, Is.False);

        var setCountdown = typeof(DeathRecoveryView).GetMethod("SetCountdown");
        Assert.That(setCountdown, Is.Not.Null);
        setCountdown.Invoke(view, new object[] { 2 });
        Assert.That(countdown.GetComponent<TMP_Text>().text, Is.EqualTo("RESPAWNING IN 2"));
    }

    [Test]
    public void DeathRecoveryView_NoCheckpointModeShowsDecisionsWithoutCountdown()
    {
        DeathRecoveryView view = BuildRecoveryViewWithoutPanel();

        view.Show(GameMode.NoCheckpoint, "fell", 2, 6);

        GameObject countdown = GameObject.Find("GameplayUI/DeathRecovery/Countdown");
        GameObject actions = GameObject.Find("GameplayUI/DeathRecovery/Actions");
        Assert.That(countdown, Is.Not.Null);
        Assert.That(actions, Is.Not.Null);
        Assert.That(countdown.activeSelf, Is.False);
        Assert.That(actions.activeSelf, Is.True);
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

    private static DeathRecoveryView BuildRecoveryView()
    {
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(
            UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
            UnityEditor.SceneManagement.NewSceneMode.Single);
        new GameObject("GameManager", typeof(GameManager), typeof(LevelInfo));
        new GameObject("RunTimer", typeof(RunTimer));
        new GameObject("CheckpointManager", typeof(CheckpointManager));

        GameplayUIBuilder.Build();
        return GameObject.Find("GameplayUI/DeathRecovery").GetComponent<DeathRecoveryView>();
    }

    private static DeathRecoveryView BuildRecoveryViewWithoutPanel()
    {
        DeathRecoveryView view = BuildRecoveryView();
        var serialized = new SerializedObject(view);
        serialized.FindProperty("panel").objectReferenceValue = null;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return view;
    }
}
