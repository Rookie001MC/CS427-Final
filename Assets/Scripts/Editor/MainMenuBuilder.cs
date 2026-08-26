using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Builds Assets/Scenes/MainMenu.unity from the reference mockups in C:\Game_Final\UI
/// (Menu.png, Level_Select.png, LoadingScreen.png).
///
/// Same contract as GameplayUIBuilder: idempotent, menu-driven, and the single authority on the
/// layout so the scene can always be regenerated rather than hand-patched.
/// </summary>
public static class MainMenuBuilder
{
    private const string ScenePath = "Assets/Scenes/MainMenu.unity";
    private const string PreviewFolder = "Assets/UI/Previews/";

    private static UIFontSet fonts;

    [MenuItem("Tools/Parkour UI/Build Main Menu Scene")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[Menu] Exit play mode first.");
            return;
        }

        if (!UIFontCatalog.TryLoad(out fonts))
        {
            Debug.LogError("[Menu] UI font assets could not be loaded.");
            return;
        }

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        BuildCamera();
        BuildEventSystem();

        RectTransform root = BuildCanvas("MenuCanvas", 0, out _);

        List<LevelEntry> levels = LoadLevels();

        MenuVisualController visuals = root.gameObject.AddComponent<MenuVisualController>();
        MenuController controller = root.gameObject.AddComponent<MenuController>();

        MainRefs main = BuildMainPanel(root, out UIPanel mainPanel);
        SelectRefs select = BuildLevelSelectPanel(root, levels.Count, out UIPanel selectPanel);
        ModeSelectionView modeSelection = BuildModeSelectionModal(root);
        SceneLoader loader = BuildLoadingOverlay();

        SetRef(visuals, "mainPanel", mainPanel);
        SetRef(visuals, "levelSelectPanel", selectPanel);

        SetRef(controller, "visuals", visuals);
        SetRef(controller, "loader", loader);
        SetRef(controller, "modeSelection", modeSelection);
        SetRef(controller, "playButton", main.Play);
        SetRef(controller, "levelsButton", main.Levels);
        SetRef(controller, "statsButton", main.Stats);
        SetRef(controller, "quitButton", main.Quit);
        SetRef(controller, "currentZoneValue", main.CurrentZone);
        SetRef(controller, "backButton", select.Back);
        SetRef(controller, "clearedValue", select.Cleared);
        SetList(controller, "levels", levels.ConvertAll(l => (Object)l));
        SetList(controller, "cards", select.Cards.ConvertAll(c => (Object)c));

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.Refresh();
        Debug.Log($"[Menu] MainMenu built with {levels.Count} level(s) at {ScenePath}");
    }

    private static List<LevelEntry> LoadLevels()
    {
        List<LevelEntry> levels = new List<LevelEntry>();
        foreach (string guid in AssetDatabase.FindAssets("t:LevelEntry"))
        {
            LevelEntry e = AssetDatabase.LoadAssetAtPath<LevelEntry>(AssetDatabase.GUIDToAssetPath(guid));
            if (e != null)
            {
                levels.Add(e);
            }
        }

        levels.Sort((a, b) => a.LevelNumber.CompareTo(b.LevelNumber));
        return levels;
    }

    // ------------------------------------------------------------------ scene furniture

    private static void BuildCamera()
    {
        GameObject go = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        go.tag = "MainCamera";
        Camera cam = go.GetComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.02f, 0.025f, 0.03f, 1f);
        cam.cullingMask = 0;                       // nothing 3D to draw; the UI is overlay
        go.transform.position = new Vector3(0f, 1f, -10f);
    }

    private static void BuildEventSystem()
    {
        // Input System only: the legacy StandaloneInputModule would throw on first UI event.
        GameObject go = new GameObject("EventSystem", typeof(EventSystem));
        go.AddComponent<InputSystemUIInputModule>();
    }

    private static RectTransform BuildCanvas(string name, int sortingOrder, out Canvas canvas)
    {
        GameObject go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        CanvasScaler scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        return (RectTransform)go.transform;
    }

    // ------------------------------------------------------------------ main panel

    private struct MainRefs
    {
        public Button Play, Levels, Stats, Quit;
        public TMP_Text CurrentZone;
    }

    private static MainRefs BuildMainPanel(RectTransform root, out UIPanel panel)
    {
        RectTransform layer = Layer(root, "MainPanel", out panel);

        // ---- backdrop: the game's own skyline, dimmed, with the left column blacked out
        RawImage shot = Raw(layer, "Backdrop", PreviewFolder + "MenuBackdrop.png");
        Stretch((RectTransform)shot.transform);
        Image dim = Img(layer, "BackdropDim", new Color(0.02f, 0.025f, 0.035f, 0.55f));
        Stretch((RectTransform)dim.transform);

        Image column = Img(layer, "LeftColumn", new Color(0.027f, 0.031f, 0.039f, 1f));
        RectTransform columnRt = (RectTransform)column.transform;
        columnRt.anchorMin = new Vector2(0f, 0f);
        columnRt.anchorMax = new Vector2(0f, 1f);
        columnRt.pivot = new Vector2(0f, 0.5f);
        columnRt.anchoredPosition = Vector2.zero;
        columnRt.sizeDelta = new Vector2(810f, 0f);

        RawImage fade = Raw(layer, "ColumnFade", PreviewFolder + "FadeGradient.png");
        RectTransform fadeRt = (RectTransform)fade.transform;
        fadeRt.anchorMin = new Vector2(0f, 0f);
        fadeRt.anchorMax = new Vector2(0f, 1f);
        fadeRt.pivot = new Vector2(0f, 0.5f);
        fadeRt.anchoredPosition = new Vector2(810f, 0f);
        fadeRt.sizeDelta = new Vector2(260f, 0f);

        // ---- identity block
        TopLeft(Text(layer, "Eyebrow", "URBAN VELOCITY", 24f, UITheme.Cyan, TextAlignmentOptions.TopLeft, UITheme.EyebrowSpacing, fontRole: UIFontRole.Mono),
            64f, 92f, 700f, 30f);
        // Two lines, white over cyan, sized to sit inside the 810-wide left column: "SKYBOUND"
        // is eight caps where "VER" was three, so the point size drops to keep it off the edge.
        TopLeft(Text(layer, "TitleTop", "SKYBOUND", 100f, UITheme.White, TextAlignmentOptions.TopLeft, 4f, FontStyles.Bold, UIFontRole.Display),
            60f, 140f, 740f, 120f);
        TopLeft(Text(layer, "TitleBottom", "TRIALS", 100f, UITheme.CyanBright, TextAlignmentOptions.TopLeft, 4f, FontStyles.Bold, UIFontRole.Display),
            60f, 248f, 740f, 120f);

        Image rule = Img(layer, "Rule", new Color(UITheme.Cyan.r, UITheme.Cyan.g, UITheme.Cyan.b, 0.35f));
        TopLeft((RectTransform)rule.transform, 64f, 432f, 690f, 1f);

        TopLeft(Text(layer, "Tagline", "Run beyond your limit.", 26f, UITheme.Label, TextAlignmentOptions.TopLeft, 2f),
            64f, 456f, 700f, 34f);

        // ---- menu rows
        // Anchored to the bottom, not the top: on wider-than-16:9 viewports the canvas is shorter
        // than 1080 reference units, and a top-anchored stack clips its last row off-screen.
        MainRefs refs = new MainRefs();
        refs.Play = MenuRow(layer, "PlayRow", "PLAY", "CONTINUE RUN", 414f, UITheme.CyanBright);
        refs.Levels = MenuRow(layer, "LevelsRow", "LEVELS", "SELECT STAGE", 306f, UITheme.Cyan);
        refs.Stats = MenuRow(layer, "StatsRow", "STATS", "RUNNER PROFILE", 198f, UITheme.Cyan);
        refs.Quit = MenuRow(layer, "QuitRow", "QUIT", "EXIT TO DESKTOP", 90f, UITheme.Orange);

        // ---- current zone, bottom right over the photo
        RectTransform zone = Block(layer, "CurrentZone", new Vector2(1f, 0f), new Vector2(-64f, 64f), new Vector2(620f, 110f));
        zone.pivot = new Vector2(1f, 0f);
        TMP_Text zoneLabel = Text(zone, "Label", "CURRENT ZONE", 20f, UITheme.Cyan, TextAlignmentOptions.Right, UITheme.LabelSpacing, fontRole: UIFontRole.Mono);
        Anchor((RectTransform)zoneLabel.transform, new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(620f, 26f));
        refs.CurrentZone = Text(zone, "Value", "-", 52f, UITheme.White, TextAlignmentOptions.Right, 0f, FontStyles.Bold, UIFontRole.Display);
        Anchor((RectTransform)refs.CurrentZone.transform, new Vector2(1f, 1f), new Vector2(0f, -32f), new Vector2(620f, 66f));

        return refs;
    }

    /// <summary>Big label + small caption row with a left accent bar, as in the mockup.</summary>
    private static Button MenuRow(RectTransform parent, string name, string label, string caption, float bottom, Color accent)
    {
        RectTransform rt = Block(parent, name, new Vector2(0f, 0f), new Vector2(64f, bottom), new Vector2(700f, 100f));

        Image fill = Img(rt, "Fill", new Color(0.07f, 0.08f, 0.095f, 0.55f));
        Stretch((RectTransform)fill.transform);
        fill.raycastTarget = true;

        Image bar = Img(rt, "Accent", accent);
        Anchor((RectTransform)bar.transform, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(4f, 100f));

        Image edge = Img(rt, "Edge", new Color(1f, 1f, 1f, 0.05f));
        Anchor((RectTransform)edge.transform, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(700f, 1f));

        TMP_Text big = Text(rt, "Label", label, 60f, UITheme.White, TextAlignmentOptions.Left, 2f, FontStyles.Bold, UIFontRole.Display);
        Anchor((RectTransform)big.transform, new Vector2(0f, 0.5f), new Vector2(40f, 0f), new Vector2(400f, 74f));

        TMP_Text small = Text(rt, "Caption", caption, 20f, UITheme.Label, TextAlignmentOptions.Right, UITheme.LabelSpacing, fontRole: UIFontRole.Mono);
        Anchor((RectTransform)small.transform, new Vector2(1f, 0.5f), new Vector2(-32f, 0f), new Vector2(320f, 28f));

        Button button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = fill;
        button.transition = Selectable.Transition.None;

        MenuButtonVisual visual = rt.gameObject.AddComponent<MenuButtonVisual>();
        SetRef(visual, "background", fill);
        SetRef(visual, "border", bar);
        SetRef(visual, "label", big);
        SetValue(visual, "style", (int)MenuButtonVisual.Style.Outline);
        SetColor(visual, "accent", accent);

        return button;
    }

    // ------------------------------------------------------------------ level select

    private struct SelectRefs
    {
        public List<LevelCardView> Cards;
        public Button Back;
        public TMP_Text Cleared;
    }

    private static SelectRefs BuildLevelSelectPanel(RectTransform root, int levelCount, out UIPanel panel)
    {
        RectTransform layer = Layer(root, "LevelSelectPanel", out panel);

        Image bg = Img(layer, "Background", new Color(0.027f, 0.031f, 0.039f, 0.985f));
        Stretch((RectTransform)bg.transform);

        TopLeft(Text(layer, "Eyebrow", "STAGE SELECT", 24f, UITheme.Cyan, TextAlignmentOptions.TopLeft, UITheme.EyebrowSpacing, fontRole: UIFontRole.Mono),
            64f, 74f, 700f, 30f);
        TopLeft(Text(layer, "TitleTop", "CHOOSE YOUR", 88f, UITheme.White, TextAlignmentOptions.TopLeft, 3f, FontStyles.Bold, UIFontRole.Display),
            60f, 108f, 1200f, 106f);
        TopLeft(Text(layer, "TitleBottom", "DISTRICT", 88f, UITheme.CyanBright, TextAlignmentOptions.TopLeft, 3f, FontStyles.Bold, UIFontRole.Display),
            60f, 202f, 1200f, 106f);

        Image rule = Img(layer, "Rule", new Color(UITheme.Cyan.r, UITheme.Cyan.g, UITheme.Cyan.b, 0.30f));
        TopLeft((RectTransform)rule.transform, 64f, 330f, 1792f, 1f);

        SelectRefs refs = new SelectRefs { Cards = new List<LevelCardView>() };

        refs.Cleared = Text(layer, "Cleared", "0 / 0 CLEARED", 22f, UITheme.Label, TextAlignmentOptions.Right, UITheme.LabelSpacing, fontRole: UIFontRole.Mono);
        Anchor((RectTransform)refs.Cleared.transform, new Vector2(1f, 1f), new Vector2(-64f, -296f), new Vector2(600f, 28f));

        for (int i = 0; i < Mathf.Max(levelCount, 2); i++)
        {
            refs.Cards.Add(BuildCard(layer, i));
        }

        refs.Back = SmallButton(layer, "BackButton", "BACK", new Vector2(64f, 64f), new Vector2(220f, 62f));

        return refs;
    }

    private static LevelCardView BuildCard(RectTransform parent, int index)
    {
        const float cardW = 470f, cardH = 396f;
        RectTransform rt = Block(parent, $"LevelCard_{index + 1:00}", new Vector2(0f, 1f),
            new Vector2(64f + index * (cardW + 28f), -376f), new Vector2(cardW, cardH));

        Image border = Img(rt, "Border", UITheme.PanelBorder);
        Stretch((RectTransform)border.transform);

        Image fill = Img(rt, "Fill", new Color(0.075f, 0.085f, 0.10f, 0.98f));
        RectTransform fillRt = (RectTransform)fill.transform;
        Stretch(fillRt);
        fillRt.offsetMin = new Vector2(1f, 1f);
        fillRt.offsetMax = new Vector2(-1f, -1f);
        fill.raycastTarget = true;

        RawImage preview = Raw(rt, "Preview", null);
        Anchor((RectTransform)preview.transform, new Vector2(0.5f, 1f), new Vector2(0f, -1f), new Vector2(cardW - 2f, 176f));

        Image previewDim = Img(rt, "PreviewDim", new Color(0.02f, 0.025f, 0.03f, 0.25f));
        Anchor((RectTransform)previewDim.transform, new Vector2(0.5f, 1f), new Vector2(0f, -1f), new Vector2(cardW - 2f, 176f));

        TMP_Text idx = Text(rt, "Index", "00", 24f, new Color(1f, 1f, 1f, 0.55f), TextAlignmentOptions.TopLeft, 6f, FontStyles.Bold, UIFontRole.Mono);
        Anchor((RectTransform)idx.transform, new Vector2(0f, 1f), new Vector2(18f, -14f), new Vector2(120f, 30f));

        // Title gets the full card width: "INDUSTRIAL PARKOUR" at bold 32 overruns a 340 box and
        // collides with the rating marks, so the stars sit on their own row underneath instead.
        TMP_Text title = Text(rt, "Title", "LEVEL", 28f, UITheme.White, TextAlignmentOptions.TopLeft, 1f, FontStyles.Bold, UIFontRole.Display);
        Anchor((RectTransform)title.transform, new Vector2(0f, 1f), new Vector2(22f, -194f), new Vector2(cardW - 44f, 38f));

        TMP_Text sub = Text(rt, "Subtitle", "", 19f, UITheme.Label, TextAlignmentOptions.TopLeft, 2f, fontRole: UIFontRole.Mono);
        Anchor((RectTransform)sub.transform, new Vector2(0f, 1f), new Vector2(22f, -230f), new Vector2(cardW - 44f, 26f));

        List<Image> stars = new List<Image>();
        for (int i = 0; i < 3; i++)
        {
            Image s = Img(rt, $"Star_{i + 1}", new Color(UITheme.Cyan.r, UITheme.Cyan.g, UITheme.Cyan.b, 0.15f));
            RectTransform srt = (RectTransform)s.transform;
            Anchor(srt, new Vector2(1f, 1f), new Vector2(-24f - (2 - i) * 32f, -264f), new Vector2(16f, 16f));
            srt.localRotation = Quaternion.Euler(0f, 0f, 45f);
            stars.Add(s);
        }

        Image div = Img(rt, "Divider", new Color(1f, 1f, 1f, 0.07f));
        Anchor((RectTransform)div.transform, new Vector2(0.5f, 1f), new Vector2(0f, -286f), new Vector2(cardW - 44f, 1f));

        TMP_Text bestLabel = Text(rt, "BestLabel", "MODES CLEARED", 17f, UITheme.Label, TextAlignmentOptions.TopLeft, UITheme.LabelSpacing, fontRole: UIFontRole.Mono);
        Anchor((RectTransform)bestLabel.transform, new Vector2(0f, 1f), new Vector2(22f, -304f), new Vector2(220f, 22f));
        TMP_Text bestValue = Text(rt, "BestValue", "--:--.--", 28f, UITheme.Dim, TextAlignmentOptions.TopLeft, 0f, FontStyles.Bold, UIFontRole.Display);
        Anchor((RectTransform)bestValue.transform, new Vector2(0f, 1f), new Vector2(22f, -330f), new Vector2(240f, 38f));

        TMP_Text statusLabel = Text(rt, "StatusLabel", "STATUS", 17f, UITheme.Label, TextAlignmentOptions.Right, UITheme.LabelSpacing, fontRole: UIFontRole.Mono);
        Anchor((RectTransform)statusLabel.transform, new Vector2(1f, 1f), new Vector2(-22f, -304f), new Vector2(220f, 22f));
        TMP_Text statusValue = Text(rt, "StatusValue", "AVAILABLE", 22f, UITheme.Label, TextAlignmentOptions.Right, 2f, FontStyles.Bold, UIFontRole.Mono);
        Anchor((RectTransform)statusValue.transform, new Vector2(1f, 1f), new Vector2(-22f, -332f), new Vector2(240f, 32f));

        Button button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = fill;
        button.transition = Selectable.Transition.None;

        MenuButtonVisual visual = rt.gameObject.AddComponent<MenuButtonVisual>();
        SetRef(visual, "background", fill);
        SetRef(visual, "border", border);
        SetRef(visual, "label", title);
        SetValue(visual, "style", (int)MenuButtonVisual.Style.Outline);
        SetColor(visual, "accent", UITheme.Cyan);

        LevelCardView card = rt.gameObject.AddComponent<LevelCardView>();
        SetRef(card, "button", button);
        SetRef(card, "preview", preview);
        SetRef(card, "indexLabel", idx);
        SetRef(card, "title", title);
        SetRef(card, "subtitle", sub);
        SetRef(card, "bestValue", bestValue);
        SetRef(card, "statusValue", statusValue);
        SetList(card, "stars", stars.ConvertAll(s => (Object)s));

        return card;
    }

    private static Button SmallButton(RectTransform parent, string name, string caption, Vector2 fromBottomLeft, Vector2 size)
    {
        RectTransform rt = Block(parent, name, new Vector2(0f, 0f), fromBottomLeft, size);

        Image border = Img(rt, "Border", UITheme.PanelBorder);
        Stretch((RectTransform)border.transform);
        Image fill = Img(rt, "Fill", UITheme.ButtonIdle);
        RectTransform fillRt = (RectTransform)fill.transform;
        Stretch(fillRt);
        fillRt.offsetMin = new Vector2(1f, 1f);
        fillRt.offsetMax = new Vector2(-1f, -1f);
        fill.raycastTarget = true;

        TMP_Text label = Text(rt, "Label", caption, 26f, UITheme.White, TextAlignmentOptions.Center, 4f, FontStyles.Bold, UIFontRole.Display);
        Stretch((RectTransform)label.transform);

        Button button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = fill;
        button.transition = Selectable.Transition.None;

        MenuButtonVisual visual = rt.gameObject.AddComponent<MenuButtonVisual>();
        SetRef(visual, "background", fill);
        SetRef(visual, "border", border);
        SetRef(visual, "label", label);
        SetValue(visual, "style", (int)MenuButtonVisual.Style.Outline);
        SetColor(visual, "accent", UITheme.Cyan);

        return button;
    }

    // ------------------------------------------------------------------ mode selection

    private struct ModeChoiceRefs
    {
        public Button Button;
        public TMP_Text Best;
    }

    private static ModeSelectionView BuildModeSelectionModal(RectTransform root)
    {
        RectTransform layer = Layer(root, "ModeSelectionModal", out UIPanel modalPanel);

        Image scrim = Img(layer, "Scrim", UITheme.Scrim);
        Stretch((RectTransform)scrim.transform);
        scrim.raycastTarget = true;

        RectTransform content = Block(layer, "Panel", new Vector2(0.5f, 0.5f), Vector2.zero,
            new Vector2(1240f, 720f));

        Image panelBorder = Img(content, "Border", UITheme.PanelBorder);
        Stretch((RectTransform)panelBorder.transform);

        Image panelFill = Img(content, "Fill", UITheme.PanelFill);
        RectTransform panelFillRt = (RectTransform)panelFill.transform;
        Stretch(panelFillRt);
        panelFillRt.offsetMin = Vector2.one;
        panelFillRt.offsetMax = -Vector2.one;

        Image accent = Img(content, "Accent", UITheme.Cyan);
        TopLeft((RectTransform)accent.transform, 40f, 34f, 5f, 108f);

        TMP_Text eyebrow = Text(content, "Eyebrow", "SELECT RUN MODE", 20f, UITheme.Cyan,
            TextAlignmentOptions.TopLeft, UITheme.EyebrowSpacing, fontRole: UIFontRole.Mono);
        TopLeft(eyebrow, 64f, 30f, 620f, 28f);

        TMP_Text levelNumber = Text(content, "LevelNumber", "LEVEL 01", 19f, UITheme.Label,
            TextAlignmentOptions.TopLeft, UITheme.LabelSpacing, fontRole: UIFontRole.Mono);
        TopLeft(levelNumber, 64f, 72f, 440f, 26f);

        TMP_Text levelName = Text(content, "LevelName", "LEVEL", 52f, UITheme.White,
            TextAlignmentOptions.TopLeft, 2f, FontStyles.Bold, UIFontRole.Display);
        TopLeft(levelName, 62f, 96f, 800f, 62f);

        TMP_Text levelSubtitle = Text(content, "LevelSubtitle", string.Empty, 20f, UITheme.Label,
            TextAlignmentOptions.TopLeft, 2f, fontRole: UIFontRole.Mono);
        TopLeft(levelSubtitle, 64f, 160f, 900f, 28f);

        ModeChoiceRefs checkpoint = BuildModeChoice(content, "CheckpointMode", new Vector2(-296f, -36f),
            RunModeRules.For(GameMode.Checkpoint).DisplayName,
            "DEATH RESPAWNS YOU AT THE LATEST CHECKPOINT. THE TIMER CONTINUES.",
            UITheme.Cyan);

        ModeChoiceRefs noCheckpoint = BuildModeChoice(content, "NoCheckpointMode", new Vector2(296f, -36f),
            RunModeRules.For(GameMode.NoCheckpoint).DisplayName,
            "DEATH RESETS THE WHOLE RUN: TIMER, PROGRESS, AND COUNTDOWN.",
            UITheme.Cyan);

        Button back = SmallButton(content, "BackButton", "BACK", new Vector2(40f, 30f),
            new Vector2(220f, 60f));

        TMP_Text prompt = Text(content, "Prompt", "CHOOSE A RULESET TO BEGIN", 17f, UITheme.Dim,
            TextAlignmentOptions.Right, UITheme.LabelSpacing, fontRole: UIFontRole.Mono);
        Anchor((RectTransform)prompt.transform, new Vector2(1f, 0f), new Vector2(-40f, 47f),
            new Vector2(620f, 24f));

        ModeSelectionView view = layer.gameObject.AddComponent<ModeSelectionView>();
        SetRef(view, "panel", modalPanel);
        SetRef(view, "levelNumber", levelNumber);
        SetRef(view, "levelName", levelName);
        SetRef(view, "levelSubtitle", levelSubtitle);
        SetRef(view, "checkpointBest", checkpoint.Best);
        SetRef(view, "noCheckpointBest", noCheckpoint.Best);
        SetRef(view, "checkpointButton", checkpoint.Button);
        SetRef(view, "noCheckpointButton", noCheckpoint.Button);
        SetRef(view, "backButton", back);

        // Keep the originating screen visible until the controller explicitly opens the modal.
        modalPanel.ApplyImmediate(false);
        return view;
    }

    private static ModeChoiceRefs BuildModeChoice(RectTransform parent, string name, Vector2 position,
        string titleText, string ruleText, Color accent)
    {
        RectTransform rt = Block(parent, name, new Vector2(0.5f, 0.5f), position,
            new Vector2(568f, 360f));

        Image border = Img(rt, "Border", UITheme.PanelBorder);
        Stretch((RectTransform)border.transform);

        Image fill = Img(rt, "Fill", UITheme.ButtonIdle);
        RectTransform fillRt = (RectTransform)fill.transform;
        Stretch(fillRt);
        fillRt.offsetMin = Vector2.one;
        fillRt.offsetMax = -Vector2.one;
        fill.raycastTarget = true;

        Image edge = Img(rt, "Accent", accent);
        TopLeft((RectTransform)edge.transform, 0f, 0f, 5f, 360f);

        TMP_Text title = Text(rt, "Title", titleText, 32f, UITheme.White,
            TextAlignmentOptions.TopLeft, 2f, FontStyles.Bold, UIFontRole.Display);
        TopLeft(title, 30f, 28f, 500f, 42f);

        TMP_Text availability = Text(rt, "Availability", "AVAILABLE NOW", 16f, accent,
            TextAlignmentOptions.TopLeft, UITheme.LabelSpacing, fontRole: UIFontRole.Mono);
        TopLeft(availability, 30f, 78f, 500f, 22f);

        TMP_Text rules = Text(rt, "Rules", ruleText, 20f, UITheme.Label,
            TextAlignmentOptions.TopLeft, 1f, fontRole: UIFontRole.Mono);
        TopLeft(rules, 30f, 122f, 508f, 82f);
        rules.textWrappingMode = TextWrappingModes.Normal;
        rules.overflowMode = TextOverflowModes.Ellipsis;

        Image divider = Img(rt, "Divider", new Color(1f, 1f, 1f, 0.08f));
        TopLeft((RectTransform)divider.transform, 30f, 230f, 508f, 1f);

        TMP_Text bestLabel = Text(rt, "BestLabel", "PERSONAL BEST", 16f, UITheme.Label,
            TextAlignmentOptions.TopLeft, UITheme.LabelSpacing, fontRole: UIFontRole.Mono);
        TopLeft(bestLabel, 30f, 254f, 300f, 22f);

        TMP_Text best = Text(rt, "BestValue", "--:--.--", 38f, UITheme.Dim,
            TextAlignmentOptions.TopLeft, 0f, FontStyles.Bold, UIFontRole.Display);
        TopLeft(best, 30f, 282f, 300f, 48f);

        Button button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = fill;
        button.transition = Selectable.Transition.None;
        button.interactable = true;

        MenuButtonVisual visual = rt.gameObject.AddComponent<MenuButtonVisual>();
        SetRef(visual, "background", fill);
        SetRef(visual, "border", border);
        SetRef(visual, "label", title);
        SetValue(visual, "style", (int)MenuButtonVisual.Style.Outline);
        SetColor(visual, "accent", accent);

        return new ModeChoiceRefs { Button = button, Best = best };
    }

    // ------------------------------------------------------------------ loading overlay

    private static SceneLoader BuildLoadingOverlay()
    {
        // Its own canvas above everything, so it can survive into the gameplay scene while the
        // menu canvas is torn down with the menu.
        RectTransform layer = BuildCanvas("SceneLoader", 1000, out _);
        CanvasGroup group = layer.gameObject.AddComponent<CanvasGroup>();

        Image bg = Img(layer, "Background", new Color(0.02f, 0.024f, 0.03f, 1f));
        Stretch((RectTransform)bg.transform);
        bg.raycastTarget = true;                    // swallow clicks while loading

        RawImage shot = Raw(layer, "Backdrop", PreviewFolder + "MenuBackdrop.png");
        Stretch((RectTransform)shot.transform);
        shot.color = new Color(1f, 1f, 1f, 0.10f);

        // Linear-space blending keeps the backdrop brighter than the alpha suggests; this holds
        // it down to the near-black wash the reference uses so the copy stays the focus.
        Image wash = Img(layer, "BackdropWash", new Color(0.02f, 0.024f, 0.03f, 0.72f));
        Stretch((RectTransform)wash.transform);

        // brand mark
        Image tick = Img(layer, "BrandTick", UITheme.Cyan);
        TopLeft((RectTransform)tick.transform, 64f, 60f, 6f, 26f);
        TopLeft(Text(layer, "Brand", "VERTEX", 24f, UITheme.White, TextAlignmentOptions.TopLeft, 8f, FontStyles.Bold, UIFontRole.Display),
            82f, 58f, 400f, 30f);

        TopLeft(Text(layer, "Eyebrow", "LOADING STAGE", 24f, UITheme.Cyan, TextAlignmentOptions.TopLeft, UITheme.EyebrowSpacing, fontRole: UIFontRole.Mono),
            64f, 186f, 900f, 30f);

        TMP_Text name = Text(layer, "LevelName", "LEVEL", 104f, UITheme.White, TextAlignmentOptions.TopLeft, 3f, FontStyles.Bold, UIFontRole.Display);
        TopLeft(name, 60f, 226f, 1400f, 124f);

        TMP_Text sub = Text(layer, "Subtitle", "", 30f, UITheme.Label, TextAlignmentOptions.TopLeft, 4f, fontRole: UIFontRole.Mono);
        TopLeft(sub, 64f, 366f, 1200f, 40f);

        TMP_Text mode = Text(layer, "Mode", "CHECKPOINT MODE", 22f, UITheme.Cyan,
            TextAlignmentOptions.TopLeft, UITheme.LabelSpacing, fontRole: UIFontRole.Mono);
        TopLeft(mode, 64f, 418f, 700f, 30f);

        // small record strip
        TopLeft(Text(layer, "BestLabel", "YOUR BEST", 18f, UITheme.Label, TextAlignmentOptions.TopLeft, UITheme.LabelSpacing, fontRole: UIFontRole.Mono),
            64f, 458f, 300f, 24f);
        TMP_Text best = Text(layer, "BestValue", "--:--.--", 30f, UITheme.Dim, TextAlignmentOptions.TopLeft, 0f, FontStyles.Bold, UIFontRole.Display);
        TopLeft(best, 64f, 484f, 300f, 40f);

        RawImage preview = Raw(layer, "Preview", null);
        TopLeft((RectTransform)preview.transform, 64f, 546f, 760f, 300f);
        Image previewEdge = Img(layer, "PreviewEdge", new Color(1f, 1f, 1f, 0.08f));
        TopLeft((RectTransform)previewEdge.transform, 64f, 846f, 760f, 1f);

        // bottom progress strip
        TMP_Text status = Text(layer, "Status", "PREPARING", 20f, UITheme.Label, TextAlignmentOptions.Left, UITheme.LabelSpacing, fontRole: UIFontRole.Mono);
        Anchor((RectTransform)status.transform, new Vector2(0f, 0f), new Vector2(64f, 132f), new Vector2(900f, 26f));

        TMP_Text percent = Text(layer, "Percent", "0%", 20f, UITheme.Label, TextAlignmentOptions.Right, 2f, fontRole: UIFontRole.Mono);
        Anchor((RectTransform)percent.transform, new Vector2(1f, 0f), new Vector2(-64f, 132f), new Vector2(300f, 26f));

        RectTransform track = Block(layer, "ProgressTrack", new Vector2(0.5f, 0f), new Vector2(0f, 116f), new Vector2(1792f, 5f));
        Image trackImg = track.gameObject.AddComponent<Image>();
        trackImg.color = new Color(1f, 1f, 1f, 0.10f);
        trackImg.raycastTarget = false;

        GameObject fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fillGo.transform.SetParent(track, false);
        RectTransform fillRt = (RectTransform)fillGo.transform;
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = new Vector2(0f, 1f);
        fillRt.offsetMin = Vector2.zero;
        fillRt.offsetMax = Vector2.zero;
        Image fillImg = fillGo.GetComponent<Image>();
        fillImg.color = UITheme.CyanBright;
        fillImg.raycastTarget = false;

        TMP_Text tip = Text(layer, "Tip", "", 20f, UITheme.Dim, TextAlignmentOptions.Left, 1f, fontRole: UIFontRole.Mono);
        Anchor((RectTransform)tip.transform, new Vector2(0f, 0f), new Vector2(64f, 62f), new Vector2(1600f, 34f));

        SceneLoader loader = layer.gameObject.AddComponent<SceneLoader>();
        SetRef(loader, "group", group);
        SetRef(loader, "levelName", name);
        SetRef(loader, "levelSubtitle", sub);
        SetRef(loader, "modeLabel", mode);
        SetRef(loader, "bestValue", best);
        SetRef(loader, "statusLabel", status);
        SetRef(loader, "percentLabel", percent);
        SetRef(loader, "tipLabel", tip);
        SetRef(loader, "progressFill", fillRt);
        SetRef(loader, "preview", preview);
        return loader;
    }

    // ------------------------------------------------------------------ primitives

    private static RectTransform Layer(RectTransform parent, string name, out UIPanel panel)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
        RectTransform rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        Stretch(rt);
        panel = go.AddComponent<UIPanel>();
        SetRef(panel, "group", go.GetComponent<CanvasGroup>());
        SetValue(panel, "interactable", true);
        return rt;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void Anchor(RectTransform rt, Vector2 anchor, Vector2 position, Vector2 size)
    {
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
    }

    /// <summary>Places by distance from the canvas top-left, which is how the mockups read.</summary>
    private static void TopLeft(RectTransform rt, float x, float y, float w, float h)
        => Anchor(rt, new Vector2(0f, 1f), new Vector2(x, -y), new Vector2(w, h));

    private static void TopLeft(TMP_Text t, float x, float y, float w, float h)
        => TopLeft((RectTransform)t.transform, x, y, w, h);

    private static RectTransform Block(RectTransform parent, string name, Vector2 anchor, Vector2 position, Vector2 size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        Anchor(rt, anchor, position, size);
        return rt;
    }

    private static Image Img(RectTransform parent, string name, Color colour)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Image img = go.GetComponent<Image>();
        img.color = colour;
        img.raycastTarget = false;
        return img;
    }

    private static RawImage Raw(RectTransform parent, string name, string texturePath)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
        go.transform.SetParent(parent, false);
        RawImage raw = go.GetComponent<RawImage>();
        raw.raycastTarget = false;

        if (!string.IsNullOrEmpty(texturePath))
        {
            raw.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (raw.texture == null)
            {
                Debug.LogWarning($"[Menu] texture not found: {texturePath}");
            }
        }

        return raw;
    }

    private static TMP_Text Text(RectTransform parent, string name, string content, float size, Color colour,
        TextAlignmentOptions align, float spacing, FontStyles style = FontStyles.Normal,
        UIFontRole fontRole = UIFontRole.Body)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        t.font = fonts.Resolve(fontRole);
        t.text = content;
        t.fontSize = size;
        t.color = colour;
        t.alignment = align;
        t.characterSpacing = spacing;
        t.fontStyle = fontRole == UIFontRole.Display ? style & ~FontStyles.Bold : style;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Overflow;
        return t;
    }

    // ------------------------------------------------------------------ serialization

    private static void SetRef(Object target, string field, Object value)
    {
        SerializedObject so = new SerializedObject(target);
        SerializedProperty p = so.FindProperty(field);
        if (p == null)
        {
            Debug.LogError($"[Menu] '{field}' not found on {target.GetType().Name}");
            return;
        }

        p.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetValue(Object target, string field, object value)
    {
        SerializedObject so = new SerializedObject(target);
        SerializedProperty p = so.FindProperty(field);
        if (p == null)
        {
            return;
        }

        switch (value)
        {
            case bool b: p.boolValue = b; break;
            case int i: p.intValue = i; break;
            case float f: p.floatValue = f; break;
            case string s: p.stringValue = s; break;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetColor(Object target, string field, Color value)
    {
        SerializedObject so = new SerializedObject(target);
        SerializedProperty p = so.FindProperty(field);
        if (p != null)
        {
            p.colorValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void SetList(Object target, string field, List<Object> values)
    {
        SerializedObject so = new SerializedObject(target);
        SerializedProperty p = so.FindProperty(field);
        if (p == null)
        {
            Debug.LogError($"[Menu] '{field}' not found on {target.GetType().Name}");
            return;
        }

        p.arraySize = values.Count;
        for (int i = 0; i < values.Count; i++)
        {
            p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
