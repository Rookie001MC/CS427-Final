using NUnit.Framework;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class UIFontBuilderTests
{
    [Test]
    public void MainMenuBuilder_AssignsFigmaFontFamilies()
    {
        MainMenuBuilder.Build();

        AssertSourceFont("MenuCanvas/MainPanel/TitleTop", "BarlowCondensed-Black");
        AssertSourceFont("MenuCanvas/MainPanel/Tagline", "Inter_18pt-Regular");
        AssertSourceFont("MenuCanvas/MainPanel/PlayRow/Caption", "Lekton-Regular");
    }

    [Test]
    public void GameplayUIBuilder_AssignsFigmaFontFamilies()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("GameManager", typeof(GameManager), typeof(LevelInfo));
        new GameObject("RunTimer", typeof(RunTimer));
        new GameObject("CheckpointManager", typeof(CheckpointManager));

        GameplayUIBuilder.Build();

        Assert.That(scene.isLoaded, Is.True);
        AssertSourceFont("GameplayUI/HUD/TimerBlock/Label", "Lekton-Regular");
        AssertSourceFont("GameplayUI/HUD/TimerBlock/Value", "BarlowCondensed-Black");
        AssertSourceFont("GameplayUI/GameOver/Cause/Tip", "Lekton-Regular");
    }

    private static void AssertSourceFont(string path, string expectedSourceFont)
    {
        GameObject go = GameObject.Find(path);
        if (go == null)
        {
            Assert.Fail($"Expected UI object '{path}' was not built.");
            return;
        }

        if (!go.TryGetComponent(out TMP_Text text) || text == null)
        {
            Assert.Fail($"Expected '{path}' to contain TMP text.");
            return;
        }

        TMP_FontAsset font = text.font;
        if (font == null || font.sourceFontFile == null)
        {
            Assert.Fail($"Expected the TMP asset on '{path}' to retain its source font reference.");
            return;
        }

        Assert.That(font.sourceFontFile.name, Is.EqualTo(expectedSourceFont),
            $"'{path}' uses the wrong semantic font family.");
    }
}
