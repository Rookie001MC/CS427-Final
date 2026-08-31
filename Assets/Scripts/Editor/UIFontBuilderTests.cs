using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class UIFontBuilderTests
{
    [Test]
    public void MainMenuBuilder_AssignsFigmaFontFamilies()
    {
        MainMenuBuilder.Build();

        AssertSourceFont("MenuCanvas/MainPanel/TitleTop", "Anton-Regular");
        AssertSourceFont("MenuCanvas/MainPanel/Tagline", "Inter_18pt-Regular");
        AssertSourceFont("MenuCanvas/MainPanel/PlayRow/Caption", "RobotoMono-Medium");
    }

    [Test]
    public void GameplayUIBuilder_AssignsFigmaFontFamilies()
    {
        BuildGameplayScene();

        AssertSourceFont("GameplayUI/HUD/TimerBlock/Label", "RobotoMono-Medium");
        AssertSourceFont("GameplayUI/HUD/TimerBlock/Value", "Anton-Regular");
        AssertSourceFont("GameplayUI/DeathRecovery/Detail", "RobotoMono-Medium");
    }

    /// <summary>
    /// The display headlines were the single biggest departure from the mockups: measured against
    /// the reference PNGs they were running at 50-60% of their intended cap height. These pin the
    /// sizes that closed that gap, so a later edit cannot quietly shrink them again.
    ///
    /// The point numbers look small only because Anton's cap is 0.859 of its em - TitleHero at
    /// 140pt is a 120-unit cap, against the ~139 units measured off Menu.png.
    /// </summary>
    [Test]
    public void MainMenuBuilder_SizesDisplayHeadlinesToTheReference()
    {
        MainMenuBuilder.Build();

        Assert.That(Find("MenuCanvas/MainPanel/TitleTop").fontSize, Is.EqualTo(UITheme.TitleHero),
            "The wordmark carries the menu and is the largest type on it.");
        Assert.That(Find("MenuCanvas/MainPanel/PlayRow/Label").fontSize, Is.EqualTo(UITheme.MenuRow));
        Assert.That(Find("MenuCanvas/TrainingPanel/TitleTop").fontSize, Is.EqualTo(UITheme.TitleMedium));

        // The main run's name is the largest headline on any screen but the wordmark, and it is
        // auto-sized so a longer level name can shrink rather than clip - so the number pinned here
        // is the ceiling it starts from.
        Assert.That(Find("MenuCanvas/MainRunPanel/Title").fontSizeMax, Is.EqualTo(UITheme.TitleHuge),
            "The main run's name carries the PLAY screen.");
    }

    [Test]
    public void GameplayUIBuilder_SizesDisplayHeadlinesToTheReference()
    {
        BuildGameplayScene();

        Assert.That(Find("GameplayUI/CheckpointPopup/Title").fontSize, Is.EqualTo(UITheme.TitleHuge));
        Assert.That(Find("GameplayUI/Pause/Title").fontSize, Is.EqualTo(UITheme.TitlePause));
        Assert.That(Find("GameplayUI/Countdown/Numeral").fontSize, Is.EqualTo(UITheme.DisplayCountdown));
    }

    /// <summary>
    /// The mockups' own small labels sit near 18pt, but they are browser screenshots read at desk
    /// distance. At 1280x720 the CanvasScaler multiplies by 0.667, so an 18pt label renders a cap
    /// under 9 real pixels and the stems break up. The audit floors rendered cap height at 10px.
    /// </summary>
    [Test]
    public void EveryUIString_ClearsTheLegibilityFloorAt720p()
    {
        AssertNoFindings(f => f.Problem.Contains("cap at"));
    }

    /// <summary>
    /// A fractional localScale stretches an already-rendered SDF quad rather than re-typesetting,
    /// which is the most common cause of blurry TMP text. Nothing the builders emit may carry one.
    /// </summary>
    [Test]
    public void NoUIText_IsRenderedThroughATransformScale()
    {
        AssertNoFindings(f => f.Problem.Contains("lossy scale"));
    }

    [Test]
    public void NoUIString_OverrunsItsBox()
    {
        AssertNoFindings(f => f.Problem.Contains("needs"));
    }

    [Test]
    public void EveryUIString_UsesOneOfTheThreeUIFamilies()
    {
        AssertNoFindings(f => f.Problem.Contains("outside the three-family"));
    }

    /// <summary>Buttons must survive the typography pass as click targets.</summary>
    [Test]
    public void MainMenuBuilder_KeepsEveryButtonClickable()
    {
        MainMenuBuilder.Build();

        Button[] buttons = Object.FindObjectsByType<Button>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        Assert.That(buttons, Is.Not.Empty);

        foreach (Button button in buttons)
        {
            Assert.That(button.targetGraphic, Is.Not.Null,
                $"'{button.name}' has no target graphic, so nothing receives its raycasts.");
            Assert.That(button.targetGraphic.raycastTarget, Is.True,
                $"'{button.name}' has a target graphic with raycastTarget off.");

            RectTransform rt = (RectTransform)button.transform;
            Assert.That(rt.rect.width, Is.GreaterThan(0f), $"'{button.name}' has no width.");
            Assert.That(rt.rect.height, Is.GreaterThan(0f), $"'{button.name}' has no height.");
            Assert.That(button.transform.lossyScale.x, Is.EqualTo(1f).Within(0.002f),
                $"'{button.name}' is scaled, which would blur its label.");
        }
    }

    // ------------------------------------------------------------------ helpers

    private static void BuildGameplayScene()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("GameManager", typeof(GameManager), typeof(LevelInfo));
        new GameObject("RunTimer", typeof(RunTimer));
        new GameObject("CheckpointManager", typeof(CheckpointManager));

        GameplayUIBuilder.Build();
    }

    /// <summary>
    /// Runs the audit over both freshly built surfaces and asserts nothing of the given kind came
    /// back. Building rather than opening the saved scenes keeps the test honest about what the
    /// builders currently emit, not about whatever is checked in.
    /// </summary>
    private static void AssertNoFindings(System.Func<UITypographyAudit.Finding, bool> kind)
    {
        List<UITypographyAudit.Finding> findings = new List<UITypographyAudit.Finding>();

        BuildGameplayScene();
        findings.AddRange(UITypographyAudit.AuditOpenScene());

        MainMenuBuilder.Build();
        findings.AddRange(UITypographyAudit.AuditOpenScene());

        List<UITypographyAudit.Finding> matched = findings.Where(kind).ToList();
        if (matched.Count == 0)
        {
            return;
        }

        Assert.Fail(string.Join("\n", matched.Select(f => f.ToString())));
    }

    private static TMP_Text Find(string path)
    {
        GameObject go = GameObject.Find(path);
        Assert.That(go, Is.Not.Null, $"Expected UI object '{path}' was not built.");
        Assert.That(go.TryGetComponent(out TMP_Text text), Is.True,
            $"Expected '{path}' to contain TMP text.");
        return text;
    }

    private static void AssertSourceFont(string path, string expectedSourceFont)
    {
        TMP_Text text = Find(path);
        TMP_FontAsset font = text.font;

        Assert.That(font, Is.Not.Null, $"'{path}' has no font asset.");
        Assert.That(font.sourceFontFile, Is.Not.Null,
            $"Expected the TMP asset on '{path}' to retain its source font reference.");
        Assert.That(font.sourceFontFile.name, Is.EqualTo(expectedSourceFont),
            $"'{path}' uses the wrong semantic font family.");
    }
}
