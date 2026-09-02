using NUnit.Framework;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;

public sealed class ModeUIBuilderTests
{
    [TearDown]
    public void TearDown()
    {
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(
            UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
            UnityEditor.SceneManagement.NewSceneMode.Single);
    }

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
    public void GameplayUIBuilder_ReservesTopCentreForMissionHud()
    {
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(
            UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
            UnityEditor.SceneManagement.NewSceneMode.Single);
        new GameObject("GameManager", typeof(GameManager), typeof(LevelInfo));
        new GameObject("RunTimer", typeof(RunTimer));
        new GameObject("CheckpointManager", typeof(CheckpointManager));
        GameObject missionHud = new GameObject("MISSION_HUD", typeof(Canvas));

        GameplayUIBuilder.Build();

        Assert.That(GameObject.Find("MISSION_HUD"), Is.SameAs(missionHud),
            "Building the shared HUD must preserve the level-owned objective instrument.");
        GameObject mode = GameObject.Find("GameplayUI/HUD/Mode");
        Assert.That(mode, Is.Not.Null);
        Assert.That(((RectTransform)mode.transform).anchoredPosition.y, Is.LessThanOrEqualTo(300f),
            "The run-mode readout must sit below the objective instrument's reserved top band.");
    }

    [Test]
    public void SkyboundScene_CarriesSharedRunUiAlongsideMissionInstrument()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
            SkyboundCityBuilder.ScenePath,
            UnityEditor.SceneManagement.OpenSceneMode.Single);

        GameObject missionHud = GameObject.Find("MISSION_HUD");
        GameObject sharedUi = GameObject.Find("GameplayUI");

        Assert.That(missionHud, Is.Not.Null,
            "Skybound must retain its distinct objective instrument.");
        Assert.That(sharedUi, Is.Not.Null,
            "Skybound must carry the shared run HUD and overlays.");
        GameplayUIController controller = sharedUi.GetComponent<GameplayUIController>();
        Assert.That(controller, Is.Not.Null,
            "The shared views must be bound to Skybound's run systems.");
        var serialized = new SerializedObject(controller);
        Assert.That(serialized.FindProperty("objectiveInstrument")?.objectReferenceValue,
            Is.SameAs(missionHud.GetComponent<Canvas>()),
            "Skybound's distinct objective instrument must follow shared overlay visibility.");
        Assert.That(Object.FindObjectsByType<RunStatsTracker>(FindObjectsSortMode.None),
            Has.Length.EqualTo(1), "Skybound must have exactly one run-stats source.");
    }

    [Test]
    public void SkyboundScene_SharedOverlaysRenderAboveMissionInstrument()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
            SkyboundCityBuilder.ScenePath,
            UnityEditor.SceneManagement.OpenSceneMode.Single);

        Canvas missionHud = GameObject.Find("MISSION_HUD").GetComponent<Canvas>();
        Canvas sharedUi = GameObject.Find("GameplayUI").GetComponent<Canvas>();

        Assert.That(sharedUi.sortingOrder, Is.GreaterThan(missionHud.sortingOrder),
            "Full-screen run overlays must cover the persistent objective instrument.");
    }

    [Test]
    public void GameplayUIController_HidesMissionInstrumentBehindFullScreenOverlays()
    {
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(
            UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
            UnityEditor.SceneManagement.NewSceneMode.Single);
        GameObject gameManager = new GameObject("GameManager", typeof(GameManager), typeof(LevelInfo));
        new GameObject("RunTimer", typeof(RunTimer));
        new GameObject("CheckpointManager", typeof(CheckpointManager));
        Canvas missionHud = new GameObject("MISSION_HUD", typeof(Canvas)).GetComponent<Canvas>();

        var levelInfo = new SerializedObject(gameManager.GetComponent<LevelInfo>());
        levelInfo.FindProperty("recordKey").stringValue = "hud-overlay-test";
        levelInfo.ApplyModifiedPropertiesWithoutUndo();

        GameplayUIBuilder.Build();

        GameplayUIController controller = Object.FindFirstObjectByType<GameplayUIController>();
        MethodInfo handleStateChanged = typeof(GameplayUIController).GetMethod(
            "HandleStateChanged", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(controller, Is.Not.Null);
        Assert.That(handleStateChanged, Is.Not.Null);

        handleStateChanged.Invoke(controller, new object[] { RunState.Paused });
        Assert.That(missionHud.enabled, Is.False, "Pause must fully clear the objective instrument.");

        handleStateChanged.Invoke(controller, new object[] { RunState.Recovering });
        Assert.That(missionHud.enabled, Is.False, "Recovery must fully clear the objective instrument.");

        handleStateChanged.Invoke(controller, new object[] { RunState.Finished });
        Assert.That(missionHud.enabled, Is.False, "Completion must fully clear the objective instrument.");

        handleStateChanged.Invoke(controller, new object[] { RunState.Countdown });
        Assert.That(missionHud.enabled, Is.False, "Countdown must fully clear the objective instrument.");

        handleStateChanged.Invoke(controller, new object[] { RunState.Running });
        Assert.That(missionHud.enabled, Is.True, "Active play must restore the objective instrument.");
    }

    [Test]
    public void UIRebuildAll_IncludesSkyboundGameplayScene()
    {
        Assert.That(UIRebuildAll.GameplayScenes, Does.Contain(SkyboundCityBuilder.ScenePath),
            "Rebuild All UI must refresh Skybound's shared run HUD and overlays.");
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
