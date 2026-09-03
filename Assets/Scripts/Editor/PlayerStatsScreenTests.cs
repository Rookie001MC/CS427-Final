using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.UI;

/// <summary>
/// The Player Stats screen, as the generated menu produces it.
///
/// Three claims are checked here. STATS opens a panel of this menu and BACK closes it, without
/// disturbing what PLAY and TRAINING do. The screen is wired to the real stores, so every figure
/// on it is one the career can actually produce. And its geometry holds with a full career in it -
/// four-digit counts, the longest level name in the catalogue, a run in every row - at both
/// supported resolutions, with nothing overlapping and nothing off screen.
///
/// The layout half of that is deliberately checked against a *populated* career. A fresh save
/// draws almost nothing, so a layout proved on one is a layout proved on an empty screen.
/// </summary>
public sealed class PlayerStatsScreenTests
{
    /// <summary>The lowest supported resolution, as a fraction of the 1080-unit canvas.</summary>
    private const float LowestScale = 720f / 1080f;

    /// <summary>The same floor <see cref="UITypographyAudit"/> enforces, in real pixels.</summary>
    private const float MinCapPixels = 10f;

    // ------------------------------------------------------------------ structure

    [Test]
    public void MainMenu_HasAPlayerStatsScreen()
    {
        MainMenuBuilder.Build();

        Assert.That(GameObject.Find("MenuCanvas/StatsPanel"), Is.Not.Null,
            "STATS has nowhere to go.");
        Assert.That(GameObject.Find("MenuCanvas/StatsPanel/StatsBackButton"), Is.Not.Null);
        Assert.That(GameObject.Find("MenuCanvas/StatsPanel/IdentityCard"), Is.Not.Null);
        Assert.That(GameObject.Find("MenuCanvas/StatsPanel/RecentRunsPanel"), Is.Not.Null);
        Assert.That(GameObject.Find("MenuCanvas/StatsPanel/ParkourBreakdownPanel"), Is.Not.Null);
        Assert.That(GameObject.Find("MenuCanvas/StatsPanel/MainRunRecordPanel"), Is.Not.Null);
        Assert.That(GameObject.Find("MenuCanvas/StatsPanel/TrainingRecordsPanel"), Is.Not.Null);

        // The panels the earlier phases built are untouched.
        Assert.That(GameObject.Find("MenuCanvas/MainPanel"), Is.Not.Null);
        Assert.That(GameObject.Find("MenuCanvas/MainRunPanel"), Is.Not.Null);
        Assert.That(GameObject.Find("MenuCanvas/TrainingPanel"), Is.Not.Null);
    }

    [Test]
    public void MainMenu_StatsIsWiredAndTheOtherRowsStillDoWhatTheyDid()
    {
        MainMenuBuilder.Build();

        MenuController controller = Object.FindFirstObjectByType<MenuController>();
        Assert.That(controller, Is.Not.Null);

        SerializedObject so = new SerializedObject(controller);

        foreach (string field in new[] { "statsButton", "statsBackButton", "stats" })
        {
            Assert.That(so.FindProperty(field), Is.Not.Null, $"'{field}' is missing.");
            Assert.That(so.FindProperty(field).objectReferenceValue, Is.Not.Null,
                $"'{field}' was left unwired, so STATS would do nothing.");
        }

        Button statsButton = (Button)so.FindProperty("statsButton").objectReferenceValue;
        Assert.That(statsButton.interactable, Is.True,
            "STATS is a working row now, not a placeholder.");
        Assert.That(statsButton.onClick.GetPersistentEventCount(), Is.Zero,
            "The menu binds its rows in code, as it does for every other row.");

        // PLAY and TRAINING are unchanged: still one main run, still two practice courses.
        Assert.That(controller.MainRun, Is.Not.Null);
        Assert.That(controller.MainRun.SceneName, Is.EqualTo("SkyboundCity"));
        Assert.That(new SerializedObject(controller).FindProperty("cards").arraySize,
            Is.EqualTo(2));

        MenuVisualController visuals = Object.FindFirstObjectByType<MenuVisualController>();
        SerializedObject vs = new SerializedObject(visuals);

        foreach (string field in new[] { "mainPanel", "mainRunPanel", "trainingPanel",
            "statsPanel" })
        {
            Assert.That(vs.FindProperty(field), Is.Not.Null, $"'{field}' is missing.");
            Assert.That(vs.FindProperty(field).objectReferenceValue, Is.Not.Null,
                $"'{field}' was left unwired, so that screen can never be shown.");
        }
    }

    [Test]
    public void MenuScreens_ShowStatsAndComeBackToMain()
    {
        MainMenuBuilder.Build();

        MenuVisualController visuals = Object.FindFirstObjectByType<MenuVisualController>();
        SerializedObject vs = new SerializedObject(visuals);

        UIPanel mainPanel = (UIPanel)vs.FindProperty("mainPanel").objectReferenceValue;
        UIPanel statsPanel = (UIPanel)vs.FindProperty("statsPanel").objectReferenceValue;

        visuals.Show(MenuVisualController.Screen.Main, true);
        Assert.That(visuals.Current, Is.EqualTo(MenuVisualController.Screen.Main));
        Assert.That(statsPanel.IsVisible, Is.False);

        visuals.Show(MenuVisualController.Screen.Stats, true);
        Assert.That(visuals.Current, Is.EqualTo(MenuVisualController.Screen.Stats));
        Assert.That(statsPanel.IsVisible, Is.True, "STATS has to actually open the panel.");
        Assert.That(mainPanel.IsVisible, Is.False,
            "It is a screen of the menu, not an overlay on top of it.");

        // BACK, and Escape, both route through TryGoBack.
        Assert.That(visuals.TryGoBack(), Is.True);
        Assert.That(visuals.Current, Is.EqualTo(MenuVisualController.Screen.Main));

        visuals.Show(MenuVisualController.Screen.Main, true);
        Assert.That(visuals.TryGoBack(), Is.False,
            "Main is the root; back from it is not this menu's business.");
    }

    [Test]
    public void StatsScreen_LoadsNoSceneOfItsOwn()
    {
        MainMenuBuilder.Build();

        // One canvas for the menu and one for the loading overlay, as before. A stats screen that
        // needed a scene would have shown up as a third.
        Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        Assert.That(canvases.Length, Is.EqualTo(2));

        HashSet<string> scenes = new HashSet<string>();
        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
        {
            scenes.Add(System.IO.Path.GetFileNameWithoutExtension(scene.path));
        }

        Assert.That(scenes.Contains("PlayerStats"), Is.False,
            "The stats screen is a menu panel; there is no scene to add.");
    }

    // ------------------------------------------------------------------ data

    [Test]
    public void StatsScreen_IsFullyWiredToTheStores()
    {
        MainMenuBuilder.Build();

        PlayerStatsView view = Object.FindFirstObjectByType<PlayerStatsView>();
        Assert.That(view, Is.Not.Null);

        SerializedObject so = new SerializedObject(view);

        foreach (string field in new[] { "totalRunsValue", "completedRunsValue", "maxSpeedValue",
            "distanceValue", "deathsValue", "runTimeValue", "failedRunsValue", "checkpointsValue",
            "recentEmptyMessage", "mainRunName", "mainRunAttempts", "mainRunCompletions",
            "mainRunBestTime", "mainRunCheckpointBest", "mainRunNoCheckpointBest",
            "mainRunCheckpoints" })
        {
            Assert.That(so.FindProperty(field), Is.Not.Null, $"'{field}' is missing.");
            Assert.That(so.FindProperty(field).objectReferenceValue, Is.Not.Null,
                $"'{field}' was left unwired, so that figure would never be drawn.");
        }

        Assert.That(so.FindProperty("actionValues").arraySize,
            Is.EqualTo(PlayerStatsFormat.Actions.Length));
        Assert.That(so.FindProperty("actionBars").arraySize,
            Is.EqualTo(PlayerStatsFormat.Actions.Length));
        Assert.That(so.FindProperty("recentRows").arraySize, Is.EqualTo(4));
        Assert.That(so.FindProperty("trainingNames").arraySize, Is.EqualTo(2));
        Assert.That(so.FindProperty("trainingTimes").arraySize, Is.EqualTo(2));
        Assert.That(so.FindProperty("levels").arraySize, Is.EqualTo(3),
            "The whole catalogue goes in; the screen reads each level's own track.");

        Assert.That(view.MainRun, Is.Not.Null);
        Assert.That(view.MainRun.SceneName, Is.EqualTo("SkyboundCity"));
    }

    [Test]
    public void EmptyCareer_ReadsAsZeroesAndNoRunsRecorded()
    {
        MainMenuBuilder.Build();

        PlayerStatsView view = Object.FindFirstObjectByType<PlayerStatsView>();
        view.RefreshFrom(new PlayerStatsStore(new MemorySlot()));

        SerializedObject so = new SerializedObject(view);

        Assert.That(Value(so, "totalRunsValue"), Is.EqualTo("00"));
        Assert.That(Value(so, "completedRunsValue"), Is.EqualTo("00"));
        Assert.That(Value(so, "maxSpeedValue"), Is.EqualTo("0.0"));
        Assert.That(Value(so, "distanceValue"), Is.EqualTo("0.0"));
        Assert.That(Value(so, "deathsValue"), Is.EqualTo("00"));
        Assert.That(Value(so, "runTimeValue"), Is.EqualTo("00H 00M"));
        Assert.That(Value(so, "mainRunAttempts"), Is.EqualTo("00"));
        Assert.That(Value(so, "mainRunCompletions"), Is.EqualTo("00"));
        Assert.That(Value(so, "mainRunBestTime"), Is.EqualTo(PlayerStatsFormat.NoTime));
        Assert.That(Value(so, "mainRunCheckpointBest"), Is.EqualTo(PlayerStatsFormat.NoTime));
        Assert.That(Value(so, "mainRunNoCheckpointBest"), Is.EqualTo(PlayerStatsFormat.NoTime));

        TMP_Text empty = (TMP_Text)so.FindProperty("recentEmptyMessage").objectReferenceValue;
        Assert.That(empty.enabled, Is.True);
        Assert.That(empty.text, Is.EqualTo(PlayerStatsFormat.NoRuns));

        // No row draws anything, so nothing on screen can be mistaken for a run.
        foreach (RecentRunRowView row in Object.FindObjectsByType<RecentRunRowView>(
            FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            foreach (TMP_Text text in row.GetComponentsInChildren<TMP_Text>(true))
            {
                Assert.That(text.enabled, Is.False, "An empty row must draw nothing.");
            }
        }

        // Every bar is empty rather than full: a new player has no highest action count to be
        // measured against.
        SerializedProperty bars = so.FindProperty("actionBars");
        for (int i = 0; i < bars.arraySize; i++)
        {
            RectTransform fill = (RectTransform)bars.GetArrayElementAtIndex(i)
                .objectReferenceValue;
            Assert.That(fill.anchorMax.x, Is.Zero, "A zero count draws no bar.");
        }
    }

    [Test]
    public void PopulatedCareer_DrawsTheRealFiguresAndDistinguishesTheTracks()
    {
        MainMenuBuilder.Build();

        PlayerStatsView view = Object.FindFirstObjectByType<PlayerStatsView>();
        view.RefreshFrom(FullCareer());

        SerializedObject so = new SerializedObject(view);

        Assert.That(Value(so, "totalRunsValue"), Is.EqualTo("14"));
        Assert.That(Value(so, "completedRunsValue"), Is.EqualTo("03"));
        Assert.That(Value(so, "deathsValue"), Is.EqualTo("07"));
        Assert.That(Value(so, "mainRunAttempts"), Is.EqualTo("12"));
        Assert.That(Value(so, "mainRunCompletions"), Is.EqualTo("01"),
            "Two training finishes must not appear as main run completions.");

        TMP_Text empty = (TMP_Text)so.FindProperty("recentEmptyMessage").objectReferenceValue;
        Assert.That(empty.enabled, Is.False, "There are runs, so nothing says there are none.");

        // The main run and a practice course are tellable apart on the row itself, not only by
        // the colour of the bar beside it.
        List<string> tracks = new List<string>();
        foreach (RecentRunRowView row in Object.FindObjectsByType<RecentRunRowView>(
            FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            TMP_Text track = (TMP_Text)new SerializedObject(row)
                .FindProperty("trackLabel").objectReferenceValue;

            if (track.enabled && !string.IsNullOrEmpty(track.text))
            {
                tracks.Add(track.text);
            }
        }

        Assert.That(tracks, Contains.Item("MAIN RUN"));
        Assert.That(tracks, Contains.Item("TRAINING"));

        // The action bars are relative to the player's own highest count, and the highest one is
        // full while a smaller one is not.
        SerializedProperty bars = so.FindProperty("actionBars");
        float widest = 0f;
        float narrowest = 1f;

        for (int i = 0; i < bars.arraySize; i++)
        {
            RectTransform fill = (RectTransform)bars.GetArrayElementAtIndex(i)
                .objectReferenceValue;
            widest = Mathf.Max(widest, fill.anchorMax.x);
            narrowest = Mathf.Min(narrowest, fill.anchorMax.x);
        }

        Assert.That(widest, Is.EqualTo(1f).Within(0.0001f));
        Assert.That(narrowest, Is.LessThan(1f));
    }

    /// <summary>
    /// The generated scene is a function of the project, not of the machine that generated it.
    ///
    /// The screen reads a save, so the obvious thing for the builder to do is bind it - and that
    /// would put whoever last played the game into MainMenu.unity, and make two rebuilds produce
    /// two different scenes. What is committed is the zero state; the real career arrives when the
    /// screen opens.
    /// </summary>
    [Test]
    public void GeneratedScreen_IsDeterministicAndCommitsTheZeroState()
    {
        MainMenuBuilder.Build();
        List<string> first = StatsStrings();

        MainMenuBuilder.Build();
        List<string> second = StatsStrings();

        Assert.That(second, Is.EqualTo(first),
            "Two rebuilds produced two different scenes.");

        SerializedObject so = new SerializedObject(Object.FindFirstObjectByType<PlayerStatsView>());

        Assert.That(Value(so, "totalRunsValue"), Is.EqualTo("00"));
        Assert.That(Value(so, "mainRunBestTime"), Is.EqualTo(PlayerStatsFormat.NoTime));
        Assert.That(Value(so, "mainRunCheckpointBest"), Is.EqualTo(PlayerStatsFormat.NoTime));
        Assert.That(Value(so, "mainRunNoCheckpointBest"), Is.EqualTo(PlayerStatsFormat.NoTime));

        TMP_Text empty = (TMP_Text)so.FindProperty("recentEmptyMessage").objectReferenceValue;
        Assert.That(empty.text, Is.EqualTo(PlayerStatsFormat.NoRuns));

        foreach (RecentRunRowView row in Object.FindObjectsByType<RecentRunRowView>(
            FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            foreach (TMP_Text text in row.GetComponentsInChildren<TMP_Text>(true))
            {
                Assert.That(text.text, Is.Empty,
                    "A committed scene must carry nobody's run history.");
            }
        }
    }

    // ------------------------------------------------------------------ typography

    /// <summary>
    /// No two lines of text on the stats screen occupy the same piece of screen, with a full
    /// career in it.
    ///
    /// Same rule and the same exclusions as MainMenu_NoTwoLinesOfTextOverlap: two labels side by
    /// side in a row are fine, two stacked in a column are not, and a display headline is allowed
    /// to be set solid. This is the check the recent-runs row was designed against - its metadata
    /// line and its status sit on the same baseline in adjacent regions, so a metadata string that
    /// grew would land on top of the status.
    /// </summary>
    [Test]
    public void StatsScreen_HasNoOverlappingText()
    {
        MainMenuBuilder.Build();
        Object.FindFirstObjectByType<PlayerStatsView>().RefreshFrom(FullCareer());
        Canvas.ForceUpdateCanvases();

        Assert.That(UIFontCatalog.TryLoad(out UIFontSet fonts), Is.True);
        TMP_FontAsset display = fonts.Resolve(UIFontRole.Display);

        List<TMP_Text> texts = Drawn(display);
        Assert.That(texts.Count, Is.GreaterThan(30),
            "Almost nothing is being drawn, so this proves nothing.");

        int compared = 0;

        for (int a = 0; a < texts.Count; a++)
        {
            for (int b = a + 1; b < texts.Count; b++)
            {
                Rect first = CanvasRect(texts[a]);
                Rect second = CanvasRect(texts[b]);
                compared++;

                bool sameColumn = first.xMax > second.xMin + 1f && second.xMax > first.xMin + 1f;
                bool sameRow = first.yMax > second.yMin + 1f && second.yMax > first.yMin + 1f;

                Assert.That(sameColumn && sameRow, Is.False,
                    $"StatsPanel: \"{Clip(texts[a].text)}\" ({Path(texts[a])}) and " +
                    $"\"{Clip(texts[b].text)}\" ({Path(texts[b])}) occupy the same piece of screen.");
            }
        }

        Assert.That(compared, Is.GreaterThan(400));
    }

    /// <summary>Every string on the screen fits the box it was given, at its full career length.</summary>
    [Test]
    public void StatsScreen_HasNoClippedText()
    {
        MainMenuBuilder.Build();
        Object.FindFirstObjectByType<PlayerStatsView>().RefreshFrom(FullCareer());
        Canvas.ForceUpdateCanvases();

        Assert.That(UIFontCatalog.TryLoad(out UIFontSet fonts), Is.True);

        int checked_ = 0;

        foreach (TMP_Text text in AllStatsText())
        {
            if (!text.enabled || string.IsNullOrEmpty(text.text))
            {
                continue;
            }

            RectTransform rt = (RectTransform)text.transform;
            Vector2 need = text.GetPreferredValues(text.text, rt.rect.width, 0f);
            checked_++;

            if (text.textWrappingMode == TextWrappingModes.NoWrap && !text.enableAutoSizing)
            {
                Assert.That(need.x, Is.LessThanOrEqualTo(rt.rect.width + 1f),
                    $"{Path(text)}: \"{Clip(text.text)}\" needs {need.x:0}u of width in a " +
                    $"{rt.rect.width:0}u box.");
            }

            if (!text.enableAutoSizing)
            {
                Assert.That(need.y, Is.LessThanOrEqualTo(rt.rect.height + 1f),
                    $"{Path(text)}: \"{Clip(text.text)}\" needs {need.y:0}u of height in a " +
                    $"{rt.rect.height:0}u box.");
            }
        }

        Assert.That(checked_, Is.GreaterThan(30));
    }

    /// <summary>
    /// The screen holds at 1920x1080 and at 1280x720.
    ///
    /// Those are the same layout, and that is the point of the assertion: the canvas matches on
    /// height against a 1920x1080 reference, so 1280x720 is the identical arrangement at 0.667
    /// scale and there is no second layout to get wrong. What the lower resolution can still break
    /// is legibility, so every label is measured against the project's 10-pixel cap floor there.
    /// </summary>
    [Test]
    public void StatsScreen_HoldsAtBothSupportedResolutions()
    {
        MainMenuBuilder.Build();
        Object.FindFirstObjectByType<PlayerStatsView>().RefreshFrom(FullCareer());
        Canvas.ForceUpdateCanvases();

        CanvasScaler scaler = Object.FindFirstObjectByType<CanvasScaler>();
        Assert.That(scaler.uiScaleMode, Is.EqualTo(CanvasScaler.ScaleMode.ScaleWithScreenSize));
        Assert.That(scaler.referenceResolution, Is.EqualTo(new Vector2(1920f, 1080f)));
        Assert.That(scaler.screenMatchMode,
            Is.EqualTo(CanvasScaler.ScreenMatchMode.MatchWidthOrHeight));
        Assert.That(scaler.matchWidthOrHeight, Is.EqualTo(1f),
            "Matching on height is what makes 1280x720 the same layout rather than a new one.");

        int measured = 0;

        foreach (TMP_Text text in AllStatsText())
        {
            if (!text.enabled || string.IsNullOrEmpty(text.text))
            {
                continue;
            }

            measured++;

            float smallest = text.enableAutoSizing ? text.fontSizeMin : text.fontSize;
            FaceInfo face = text.font.faceInfo;
            float capRatio = face.pointSize > 0f ? face.capLine / face.pointSize : 0.7f;

            Assert.That(smallest * capRatio * LowestScale,
                Is.GreaterThanOrEqualTo(MinCapPixels),
                $"{Path(text)} renders an illegible cap at 1280x720.");

            Vector3 lossy = text.transform.lossyScale;
            Assert.That(lossy.x, Is.EqualTo(1f).Within(0.002f),
                $"{Path(text)} is transform-scaled; change fontSize instead.");
            Assert.That(lossy.y, Is.EqualTo(1f).Within(0.002f));
        }

        Assert.That(measured, Is.GreaterThan(30));

        // Nothing runs off either edge of the canvas, at either resolution, because at both the
        // canvas is exactly 1920 x 1080 reference units.
        foreach (RectTransform rt in StatsRoot().GetComponentsInChildren<RectTransform>(true))
        {
            if (rt == StatsRoot())
            {
                continue;
            }

            Rect box = CanvasRect(rt);

            // Full-bleed elements (the background, the header rule) are anchored to the canvas
            // and legitimately touch its edges; only content is required to sit inside.
            if (box.width >= 1790f)
            {
                continue;
            }

            Assert.That(box.xMin, Is.GreaterThanOrEqualTo(-1f), $"{Path(rt)} is off the left edge.");
            Assert.That(box.xMax, Is.LessThanOrEqualTo(1921f), $"{Path(rt)} is off the right edge.");
            Assert.That(box.yMin, Is.GreaterThanOrEqualTo(-1f),
                $"{Path(rt)} is off the bottom edge.");
            Assert.That(box.yMax, Is.LessThanOrEqualTo(1081f), $"{Path(rt)} is off the top edge.");
        }
    }

    /// <summary>
    /// The reference's heading sizes come from <see cref="UITheme"/>, not from a number typed into
    /// the builder, so the whole menu still moves together when the scale is retuned.
    /// </summary>
    [Test]
    public void StatsScreen_UsesTheProjectTypeScale()
    {
        MainMenuBuilder.Build();

        TMP_Text title = Find("MenuCanvas/StatsPanel/Title");
        Assert.That(title.fontSize, Is.EqualTo(UITheme.TitleMedium),
            "PLAYER STATS is a screen title and is set at the screen-title size.");

        Assert.That(Find("MenuCanvas/StatsPanel/Eyebrow").fontSize, Is.EqualTo(UITheme.StatLabel));
        Assert.That(Find("MenuCanvas/StatsPanel/Brand").fontSize, Is.EqualTo(UITheme.ButtonLabel));
        Assert.That(Find("MenuCanvas/StatsPanel/RecentRunsPanel/Heading").fontSize,
            Is.EqualTo(UITheme.StatLabel));
        Assert.That(Find("MenuCanvas/StatsPanel/TotalRunsCard/Label").fontSize,
            Is.EqualTo(UITheme.LabelSmall));

        foreach (TMP_Text text in AllStatsText())
        {
            float smallest = text.enableAutoSizing ? text.fontSizeMin : text.fontSize;
            Assert.That(smallest, Is.GreaterThanOrEqualTo(UITheme.MinimumSize),
                $"{Path(text)} is set below the project's minimum size.");
        }
    }

    // ------------------------------------------------------------------ branding

    /// <summary>
    /// The reference mockups brand every screen VERTEX. The game is Skybound Trials, and no
    /// string anywhere in the generated menu may say otherwise.
    /// </summary>
    [Test]
    public void GeneratedMenu_SaysSkyboundTrialsAndNeverVertex()
    {
        MainMenuBuilder.Build();

        int brandMarks = 0;

        foreach (TMP_Text text in Object.FindObjectsByType<TMP_Text>(
            FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Assert.That(text.text.ToUpperInvariant(), Does.Not.Contain("VERTEX"),
                $"{Path(text)} still carries the reference's branding.");

            if (text.text == "SKYBOUND TRIALS")
            {
                brandMarks++;
            }
        }

        Assert.That(brandMarks, Is.GreaterThanOrEqualTo(3),
            "The stats screen's brand mark and identity card, and the loading screen's mark.");

        Assert.That(Find("MenuCanvas/StatsPanel/Brand").text, Is.EqualTo("SKYBOUND TRIALS"));
        Assert.That(Find("MenuCanvas/StatsPanel/IdentityCard/Name").text,
            Is.EqualTo("SKYBOUND TRIALS"));
        Assert.That(Find("MenuCanvas/StatsPanel/IdentityCard/Role").text,
            Is.EqualTo("RUNNER PROFILE"));
        Assert.That(Find("SceneLoader/Brand").text, Is.EqualTo("SKYBOUND TRIALS"));
    }

    // ------------------------------------------------------------------ helpers

    private sealed class MemorySlot : IRunRecordPersistence
    {
        private string json = string.Empty;
        public string Load() => json;
        public void Save(string value) => json = value;
    }

    /// <summary>
    /// A career with something in every field: twelve main run attempts and one completion, two
    /// training finishes, a failed attempt, four-figure action counts and a long distance. This is
    /// the state the layout has to survive, and a real save cannot be relied on to be in it.
    /// </summary>
    private static PlayerStatsStore FullCareer()
    {
        var store = new PlayerStatsStore(new MemorySlot());

        for (int i = 0; i < 12; i++)
        {
            store.RecordRunStarted("SkyboundCity", LevelTrack.MainRun);
        }

        store.RecordRunStarted("IndustrialParkour", LevelTrack.Training);
        store.RecordRunStarted("UIWorldDemo", LevelTrack.Training);

        store.RecordRunFailed("SkyboundCity", "SKYBOUND CITY", LevelTrack.MainRun,
            GameMode.NoCheckpoint, 214.55f);
        store.RecordRunFinished("UIWorldDemo", "NEON DISTRICT", LevelTrack.Training,
            GameMode.Checkpoint, 95.5f, false);
        store.RecordRunFinished("IndustrialParkour", "INDUSTRIAL PARKOUR", LevelTrack.Training,
            GameMode.NoCheckpoint, 61.25f, true);
        store.RecordRunFinished("SkyboundCity", "SKYBOUND CITY", LevelTrack.MainRun,
            GameMode.Checkpoint, 3599.99f, true);

        for (int i = 0; i < 7; i++)
        {
            store.RecordDeath();
        }

        for (int i = 0; i < 38; i++)
        {
            store.RecordCheckpoint("SkyboundCity", LevelTrack.MainRun);
        }

        int[] counts = { 1284, 316, 208, 97, 154, 63 };
        for (int i = 0; i < PlayerStatsFormat.Actions.Length; i++)
        {
            for (int n = 0; n < counts[i]; n++)
            {
                store.RecordAction(PlayerStatsFormat.Actions[i]);
            }
        }

        store.AddDistance(38712.5f);
        store.ReportSpeed(11.4f);
        store.AddRunTime(3600f * 27f + 60f * 43f);

        return store;
    }

    private static RectTransform StatsRoot()
    {
        GameObject panel = GameObject.Find("MenuCanvas/StatsPanel");
        Assert.That(panel, Is.Not.Null, "StatsPanel was not built.");
        return (RectTransform)panel.transform;
    }

    private static IEnumerable<TMP_Text> AllStatsText()
        => StatsRoot().GetComponentsInChildren<TMP_Text>(true);

    /// <summary>Everything actually being drawn, minus the display headlines set solid on purpose.</summary>
    private static List<TMP_Text> Drawn(TMP_FontAsset display)
    {
        List<TMP_Text> texts = new List<TMP_Text>();

        foreach (TMP_Text text in AllStatsText())
        {
            if (!text.enabled || text.font == display || string.IsNullOrWhiteSpace(text.text))
            {
                continue;
            }

            texts.Add(text);
        }

        return texts;
    }

    private static TMP_Text Find(string path)
    {
        GameObject go = GameObject.Find(path);
        Assert.That(go, Is.Not.Null, $"{path} was not built.");

        TMP_Text text = go.GetComponent<TMP_Text>();
        Assert.That(text, Is.Not.Null, $"{path} carries no text.");
        return text;
    }

    private static string Value(SerializedObject view, string field)
    {
        SerializedProperty property = view.FindProperty(field);
        Assert.That(property, Is.Not.Null, $"'{field}' is missing.");

        TMP_Text text = (TMP_Text)property.objectReferenceValue;
        Assert.That(text, Is.Not.Null, $"'{field}' was left unwired.");
        return text.text;
    }

    /// <summary>A rect in canvas units, with the canvas scale divided out.</summary>
    private static Rect CanvasRect(Component component)
    {
        RectTransform rt = (RectTransform)component.transform;
        Vector3[] corners = new Vector3[4];
        rt.GetWorldCorners(corners);

        float scale = rt.lossyScale.x > 0.0001f ? rt.lossyScale.x : 1f;

        return Rect.MinMaxRect(corners[0].x / scale, corners[0].y / scale,
            corners[2].x / scale, corners[2].y / scale);
    }

    /// <summary>Every string on the stats screen, in hierarchy order.</summary>
    private static List<string> StatsStrings()
    {
        List<string> strings = new List<string>();

        foreach (TMP_Text text in AllStatsText())
        {
            strings.Add($"{Path(text)}={text.text}");
        }

        return strings;
    }

    private static string Clip(string text)
        => text.Length <= 28 ? text : text.Substring(0, 28) + "...";

    private static string Path(Component component)
    {
        Transform t = component.transform;
        string path = t.name;

        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }

        return path;
    }
}
