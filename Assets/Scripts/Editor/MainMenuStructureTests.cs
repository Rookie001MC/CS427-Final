using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The menu says what the game is.
///
/// Before this pass the main menu listed three levels as equals, which told the player that
/// Skybound City is the third of three maps. It is not - it is the game, and the other two are the
/// practice courses that teach it. These tests are about that claim rather than about pixels: the
/// catalogue names exactly one main run, PLAY goes to it and not to a list, the two training maps
/// are on a screen that says TRAINING, and nothing about how a level is actually launched changed.
/// </summary>
public sealed class MainMenuStructureTests
{
    [Test]
    public void Catalogue_NamesExactlyOneMainRunAndTwoTrainingCourses()
    {
        List<LevelEntry> levels = AllLevels();

        Assert.That(levels.Count, Is.EqualTo(3),
            "The catalogue should hold Industrial Parkour, Neon District and Skybound City.");

        List<LevelEntry> mainRuns = levels.FindAll(l => l.IsMainRun);
        List<LevelEntry> training = levels.FindAll(l => !l.IsMainRun);

        Assert.That(mainRuns.Count, Is.EqualTo(1),
            "PLAY launches the main run, so exactly one level must be marked as one.");
        Assert.That(mainRuns[0].SceneName, Is.EqualTo("SkyboundCity"));
        Assert.That(mainRuns[0].TrackLabel, Is.EqualTo("MAIN RUN"));

        Assert.That(training.Count, Is.EqualTo(2));

        foreach (LevelEntry level in training)
        {
            Assert.That(level.TrackLabel, Is.EqualTo("TRAINING"),
                $"{level.DisplayName} is a practice course and has to say so.");
        }

        // Every level still has to be launchable: a track is presentation, and presentation must
        // never be the reason a scene cannot be loaded.
        foreach (LevelEntry level in levels)
        {
            Assert.That(level.SceneName, Is.Not.Empty, $"{level.name} has no scene.");
            Assert.That(level.RecordKey, Is.Not.Empty, $"{level.name} has no record key.");
        }
    }

    [Test]
    public void EveryLevelInTheCatalogueIsInBuildSettings()
    {
        HashSet<string> built = new HashSet<string>();

        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
        {
            built.Add(System.IO.Path.GetFileNameWithoutExtension(scene.path));
        }

        foreach (LevelEntry level in AllLevels())
        {
            Assert.That(built.Contains(level.SceneName), Is.True,
                $"{level.DisplayName} points at '{level.SceneName}', which is not in Build " +
                "Settings, so the menu would fail to load it.");
        }
    }

    [Test]
    public void RecordKeysAreUniqueSoTwoLevelsCannotShareARun()
    {
        HashSet<string> keys = new HashSet<string>();

        foreach (LevelEntry level in AllLevels())
        {
            Assert.That(keys.Add(level.RecordKey), Is.True,
                $"{level.DisplayName} shares its record key with another level.");
        }
    }

    [Test]
    public void MainMenu_HasAMainRunScreenAndATrainingScreen()
    {
        MainMenuBuilder.Build();

        Assert.That(GameObject.Find("MenuCanvas/MainPanel"), Is.Not.Null);
        Assert.That(GameObject.Find("MenuCanvas/MainRunPanel"), Is.Not.Null,
            "PLAY has nowhere to go.");
        Assert.That(GameObject.Find("MenuCanvas/TrainingPanel"), Is.Not.Null);

        // The old undifferentiated screen is gone rather than hidden.
        Assert.That(GameObject.Find("MenuCanvas/LevelSelectPanel"), Is.Null);

        Assert.That(GameObject.Find("MenuCanvas/MainPanel/PlayRow"), Is.Not.Null);
        Assert.That(GameObject.Find("MenuCanvas/MainPanel/TrainingRow"), Is.Not.Null);
        Assert.That(GameObject.Find("MenuCanvas/MainPanel/QuitRow"), Is.Not.Null);
        Assert.That(GameObject.Find("MenuCanvas/MainPanel/LevelsRow"), Is.Null,
            "LEVELS presented the three maps as equals and has been replaced by TRAINING.");

        Assert.That(GameObject.Find("MenuCanvas/MainRunPanel/StartRunButton"), Is.Not.Null);
        Assert.That(GameObject.Find("MenuCanvas/MainRunPanel/BackButton"), Is.Not.Null);
    }

    [Test]
    public void MainMenu_PlayIsWiredToTheMainRunAndTrainingToTheCourses()
    {
        MainMenuBuilder.Build();

        MenuController controller = Object.FindFirstObjectByType<MenuController>();
        Assert.That(controller, Is.Not.Null);

        SerializedObject so = new SerializedObject(controller);

        foreach (string field in new[] { "visuals", "loader", "modeSelection", "playButton",
            "trainingButton", "quitButton", "featured", "mainRunBackButton", "backButton" })
        {
            Assert.That(so.FindProperty(field), Is.Not.Null, $"'{field}' is missing.");
            Assert.That(so.FindProperty(field).objectReferenceValue, Is.Not.Null,
                $"'{field}' was left unwired by the builder.");
        }

        // The catalogue goes in whole; which screen each level lands on is read off its own track.
        SerializedProperty levels = so.FindProperty("levels");
        Assert.That(levels.arraySize, Is.EqualTo(3));

        Assert.That(controller.MainRun, Is.Not.Null, "The menu found no main run.");
        Assert.That(controller.MainRun.SceneName, Is.EqualTo("SkyboundCity"));

        MenuVisualController visuals = Object.FindFirstObjectByType<MenuVisualController>();
        SerializedObject vs = new SerializedObject(visuals);

        foreach (string field in new[] { "mainPanel", "mainRunPanel", "trainingPanel" })
        {
            Assert.That(vs.FindProperty(field), Is.Not.Null, $"'{field}' is missing.");
            Assert.That(vs.FindProperty(field).objectReferenceValue, Is.Not.Null,
                $"'{field}' was left unwired, so that screen can never be shown.");
        }
    }

    [Test]
    public void MainMenu_TrainingCardsCarryTheTrainingLabelAndOnlyTheTrainingLevels()
    {
        MainMenuBuilder.Build();

        MenuController controller = Object.FindFirstObjectByType<MenuController>();
        SerializedProperty cards = new SerializedObject(controller).FindProperty("cards");

        Assert.That(cards.arraySize, Is.EqualTo(2),
            "Two practice courses, so two cards - the main run is not one of them.");

        for (int i = 0; i < cards.arraySize; i++)
        {
            LevelCardView card = (LevelCardView)cards.GetArrayElementAtIndex(i).objectReferenceValue;
            Assert.That(card, Is.Not.Null);

            SerializedProperty label = new SerializedObject(card).FindProperty("trackLabel");
            Assert.That(label, Is.Not.Null);
            Assert.That(label.objectReferenceValue, Is.Not.Null,
                $"Card {i} has no track badge, so nothing on it says it is a training map.");
        }
    }

    [Test]
    public void MainMenu_TheFeaturedPanelRendersTheMainRun()
    {
        MainMenuBuilder.Build();

        FeaturedLevelView view = Object.FindFirstObjectByType<FeaturedLevelView>();
        Assert.That(view, Is.Not.Null);

        SerializedObject so = new SerializedObject(view);

        foreach (string field in new[] { "startButton", "trackLabel", "numberLabel", "title",
            "subtitle", "statusValue", "clearedValue" })
        {
            Assert.That(so.FindProperty(field), Is.Not.Null, $"'{field}' is missing.");
            Assert.That(so.FindProperty(field).objectReferenceValue, Is.Not.Null,
                $"'{field}' was left unwired by the builder.");
        }

        LevelEntry mainRun = AllLevels().Find(l => l.IsMainRun);
        view.Bind(mainRun);

        Assert.That(view.Entry, Is.SameAs(mainRun));

        TMPro.TMP_Text title =
            (TMPro.TMP_Text)so.FindProperty("title").objectReferenceValue;
        Assert.That(title.text, Is.EqualTo(mainRun.DisplayName));

        TMPro.TMP_Text track =
            (TMPro.TMP_Text)so.FindProperty("trackLabel").objectReferenceValue;
        Assert.That(track.text, Is.EqualTo("MAIN RUN"));

        Button start = (Button)so.FindProperty("startButton").objectReferenceValue;
        Assert.That(start.interactable, Is.True);

        // A catalogue with no main run has to fail loudly on screen rather than silently.
        view.Bind(null);
        Assert.That(start.interactable, Is.False);
        Assert.That(title.text, Is.EqualTo("NO MAIN RUN"));
    }


    /// <summary>
    /// No two lines of running text on a menu screen overlap.
    ///
    /// The redesign laid three new screens out by hand and got several gaps wrong: a card's title
    /// box ended two units *below* where its subtitle began, the training screen's clear-count sat
    /// inside the paragraph above it, and the four main-menu rows were eight units apart. All of
    /// that is invisible to the typography audit, which asks whether a string fits its own box and
    /// never whether two boxes are the same piece of screen.
    ///
    /// Display-face headlines are excluded, and deliberately: a two-line wordmark at
    /// <see cref="UITheme.DisplayLineStep"/> is set solid on purpose, so its line boxes overlap by
    /// design. Their sizes are pinned by MainMenuBuilder_SizesDisplayHeadlinesToTheReference and
    /// their fit by NoUIString_OverrunsItsBox; this is about everything else.
    /// </summary>
    [Test]
    public void MainMenu_NoTwoLinesOfTextOverlap()
    {
        MainMenuBuilder.Build();
        Canvas.ForceUpdateCanvases();

        Assert.That(UIFontCatalog.TryLoad(out UIFontSet fonts), Is.True);
        TMPro.TMP_FontAsset display = fonts.Resolve(UIFontRole.Display);

        int compared = 0;

        foreach (string panel in new[] { "MenuCanvas/MainPanel", "MenuCanvas/MainRunPanel",
            "MenuCanvas/TrainingPanel" })
        {
            GameObject root = GameObject.Find(panel);
            Assert.That(root, Is.Not.Null, $"{panel} was not built.");

            List<TMPro.TMP_Text> texts = new List<TMPro.TMP_Text>();

            foreach (TMPro.TMP_Text text in root.GetComponentsInChildren<TMPro.TMP_Text>(true))
            {
                // A headline is allowed to be set solid; an empty box has no lines to collide.
                if (text.font == display || string.IsNullOrWhiteSpace(text.text))
                {
                    continue;
                }

                texts.Add(text);
            }

            Assert.That(texts.Count, Is.GreaterThan(3), $"{panel} has almost no text on it.");

            for (int a = 0; a < texts.Count; a++)
            {
                for (int b = a + 1; b < texts.Count; b++)
                {
                    // Two labels in the same row are side by side, which is fine; two stacked on
                    // the same column are not.
                    Rect first = CanvasRect(texts[a]);
                    Rect second = CanvasRect(texts[b]);
                    compared++;

                    bool sameColumn = first.xMax > second.xMin + 1f && second.xMax > first.xMin + 1f;
                    bool sameRow = first.yMax > second.yMin + 1f && second.yMax > first.yMin + 1f;

                    Assert.That(sameColumn && sameRow, Is.False,
                        $"{panel}: \"{Clip(texts[a].text)}\" and \"{Clip(texts[b].text)}\" " +
                        "occupy the same piece of screen.");
                }
            }
        }

        Assert.That(compared, Is.GreaterThan(50));
    }

    /// <summary>Every wrapped paragraph is leaded, so its lines do not sit on each other.</summary>
    [Test]
    public void MainMenu_WrappedTextIsLeaded()
    {
        MainMenuBuilder.Build();

        int checkedBlocks = 0;

        foreach (TMPro.TMP_Text text in Object.FindObjectsByType<TMPro.TMP_Text>(
            FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (text.textWrappingMode != TMPro.TextWrappingModes.Normal
                || string.IsNullOrWhiteSpace(text.text))
            {
                continue;
            }

            checkedBlocks++;
            Assert.That(text.lineSpacing, Is.GreaterThanOrEqualTo(UITheme.BodyLeading - 0.01f),
                $"\"{Clip(text.text)}\" wraps but is set solid.");
        }

        Assert.That(checkedBlocks, Is.GreaterThan(2),
            "No wrapped paragraphs found, so this proves nothing.");
    }

    /// <summary>A text object's box, in canvas units, with the canvas scale divided out.</summary>
    private static Rect CanvasRect(TMPro.TMP_Text text)
    {
        RectTransform rt = (RectTransform)text.transform;
        Vector3[] corners = new Vector3[4];
        rt.GetWorldCorners(corners);

        float scale = rt.lossyScale.x > 0.0001f ? rt.lossyScale.x : 1f;

        return Rect.MinMaxRect(corners[0].x / scale, corners[0].y / scale,
            corners[2].x / scale, corners[2].y / scale);
    }

    private static string Clip(string text)
        => text.Length <= 28 ? text : text.Substring(0, 28) + "...";

    private static List<LevelEntry> AllLevels()
    {
        List<LevelEntry> levels = new List<LevelEntry>();

        foreach (string guid in AssetDatabase.FindAssets("t:LevelEntry"))
        {
            LevelEntry entry =
                AssetDatabase.LoadAssetAtPath<LevelEntry>(AssetDatabase.GUIDToAssetPath(guid));

            if (entry != null)
            {
                levels.Add(entry);
            }
        }

        levels.Sort((a, b) => a.LevelNumber.CompareTo(b.LevelNumber));
        return levels;
    }
}
