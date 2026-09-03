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

        LevelEntry mainRun = levels.Find(l => l != null && l.IsMainRun);
        int trainingCount = levels.FindAll(l => l != null && !l.IsMainRun).Count;

        if (mainRun == null)
        {
            Debug.LogWarning("[Menu] No LevelEntry is marked as the main run: the PLAY screen " +
                             "will build, but it will have nothing to launch.");
        }

        MainRefs main = BuildMainPanel(root, mainRun, out UIPanel mainPanel);
        HeroRefs hero = BuildMainRunPanel(root, mainRun, out UIPanel heroPanel);
        SelectRefs select = BuildTrainingPanel(root, trainingCount, out UIPanel trainingPanel);
        StatsRefs stats = BuildStatsPanel(root, levels, out UIPanel statsPanel);
        ModeSelectionView modeSelection = BuildModeSelectionModal(root);
        SceneLoader loader = BuildLoadingOverlay();

        SetRef(visuals, "mainPanel", mainPanel);
        SetRef(visuals, "mainRunPanel", heroPanel);
        SetRef(visuals, "trainingPanel", trainingPanel);
        SetRef(visuals, "statsPanel", statsPanel);

        SetRef(controller, "visuals", visuals);
        SetRef(controller, "loader", loader);
        SetRef(controller, "modeSelection", modeSelection);
        SetRef(controller, "playButton", main.Play);
        SetRef(controller, "trainingButton", main.Training);
        SetRef(controller, "statsButton", main.Stats);
        SetRef(controller, "quitButton", main.Quit);
        SetRef(controller, "currentZoneValue", main.CurrentZone);
        SetRef(controller, "featured", hero.View);
        SetRef(controller, "mainRunBackButton", hero.Back);
        SetRef(controller, "backButton", select.Back);
        SetRef(controller, "clearedValue", select.Cleared);
        SetRef(controller, "stats", stats.View);
        SetRef(controller, "statsBackButton", stats.Back);
        SetList(controller, "levels", levels.ConvertAll(l => (Object)l));
        SetList(controller, "cards", select.Cards.ConvertAll(c => (Object)c));

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.Refresh();
        Debug.Log($"[Menu] MainMenu built: main run " +
                  $"{(mainRun != null ? mainRun.DisplayName : "<none>")}, " +
                  $"{trainingCount} training course(s), {levels.Count} level(s) at {ScenePath}");
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
        // Match on height, not the 0.5 blend. At 16:9 (1920x1080, 1600x900, 1280x720) the two
        // are identical, but height-matching guarantees the canvas is always exactly 1080
        // reference units tall, so a wider-than-16:9 viewport can never shrink type or push a
        // bottom-anchored stack off screen.
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 1f;

        return (RectTransform)go.transform;
    }

    // ------------------------------------------------------------------ main panel

    private struct MainRefs
    {
        public Button Play, Training, Stats, Quit;
        public TMP_Text CurrentZone;
    }

    private struct HeroRefs
    {
        public FeaturedLevelView View;
        public Button Back;
    }

    private static MainRefs BuildMainPanel(RectTransform root, LevelEntry mainRun,
        out UIPanel panel)
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
        TopLeft(Text(layer, "Eyebrow", "URBAN VELOCITY", UITheme.Eyebrow, UITheme.Cyan, TextAlignmentOptions.TopLeft, UITheme.EyebrowSpacing, fontRole: UIFontRole.Mono),
            64f, 64f, 900f, 40f);

        // Two lines, white over cyan, inside the 810-wide left column. The mockup's wordmark is
        // 145px of cap height on a 1998px-wide frame - roughly 196pt in canvas units for the
        // three-letter "VER". "SKYBOUND" is eight caps, so it takes the largest size that still
        // clears the column: 8 chars x (0.462em advance + 0.02em tracking) x 172 = 663 of 750.
        TopLeft(Text(layer, "TitleTop", "SKYBOUND", UITheme.TitleHero, UITheme.White, TextAlignmentOptions.TopLeft, UITheme.DisplaySpacing, FontStyles.Bold, UIFontRole.Display),
            60f, 106f, 760f, 220f);
        TopLeft(Text(layer, "TitleBottom", "TRIALS", UITheme.TitleHero, UITheme.CyanBright, TextAlignmentOptions.TopLeft, UITheme.DisplaySpacing, FontStyles.Bold, UIFontRole.Display),
            60f, 106f + UITheme.TitleHero * UITheme.DisplayLineStep, 760f, 220f);

        // The wordmark ends at 106 + 137 + a 120-unit cap = 363. The rule sits a section below it
        // and the tagline a heading-gap under that, which leaves the four menu rows a clear
        // 130 units of air instead of butting into the tagline's descenders.
        Image rule = Img(layer, "Rule", new Color(UITheme.Cyan.r, UITheme.Cyan.g, UITheme.Cyan.b, 0.35f));
        TopLeft((RectTransform)rule.transform, 64f, 408f, 690f, 1f);

        TMP_Text tagline = Text(layer, "Tagline", "Run beyond your limit.", UITheme.Body, UITheme.Label, TextAlignmentOptions.TopLeft, 2f);
        TopLeft(tagline, 64f, 408f + UITheme.HeadingGap, 700f, 44f);

        // ---- menu rows
        // Anchored to the bottom, not the top: on wider-than-16:9 viewports the canvas is shorter
        // than 1080 reference units, and a top-anchored stack clips its last row off-screen.
        // PLAY names the main run in its caption and TRAINING says what it is, so the split
        // between the game and its practice courses is legible before anything is clicked.
        MainRefs refs = new MainRefs();
        // 112-tall rows on a 132 pitch: a 20-unit gutter, where they used to sit 8 apart and read
        // as one block of stacked type with hairlines through it.
        const float rowPitch = 112f + UITheme.RowGutter;
        const float firstRow = 72f;

        refs.Play = MenuRow(layer, "PlayRow", "PLAY",
            mainRun != null ? mainRun.DisplayName : "MAIN RUN", firstRow + rowPitch * 3f,
            UITheme.CyanBright, true);
        refs.Training = MenuRow(layer, "TrainingRow", "TRAINING", "PRACTICE COURSES",
            firstRow + rowPitch * 2f, UITheme.Orange);
        refs.Stats = MenuRow(layer, "StatsRow", "STATS", "RUNNER PROFILE",
            firstRow + rowPitch, UITheme.Cyan);
        refs.Quit = MenuRow(layer, "QuitRow", "QUIT", "EXIT TO DESKTOP", firstRow, UITheme.Orange);

        // ---- current zone, bottom right over the photo
        RectTransform zone = Block(layer, "CurrentZone", new Vector2(1f, 0f), new Vector2(-64f, 72f), new Vector2(760f, 140f));
        zone.pivot = new Vector2(1f, 0f);
        TMP_Text zoneLabel = Text(zone, "Label", "CURRENT ZONE", UITheme.StatLabel, UITheme.Cyan, TextAlignmentOptions.Right, UITheme.LabelSpacing, fontRole: UIFontRole.Mono);
        Anchor((RectTransform)zoneLabel.transform, new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(760f, 34f));
        refs.CurrentZone = Text(zone, "Value", "-", UITheme.StatValueLarge, UITheme.White, TextAlignmentOptions.Right, 0f, FontStyles.Bold, UIFontRole.Display);
        Anchor((RectTransform)refs.CurrentZone.transform, new Vector2(1f, 1f), new Vector2(0f, -46f), new Vector2(760f, 84f));
        AutoSize(refs.CurrentZone, UITheme.StatValueLarge * 0.62f, UITheme.StatValueLarge);

        return refs;
    }

    /// <summary>Big label + small caption row with a left accent bar, as in the mockup.</summary>
    private static Button MenuRow(RectTransform parent, string name, string label, string caption,
        float bottom, Color accent, bool primary = false)
    {
        // 112 tall, not 100: the mockup sets these labels at ~103pt of cap height and a 100-unit
        // row would clip the descender-free caps against the accent bar's own edge.
        const float rowH = 112f;
        RectTransform rt = Block(parent, name, new Vector2(0f, 0f), new Vector2(64f, bottom), new Vector2(700f, rowH));

        Image fill = Img(rt, "Fill", new Color(0.07f, 0.08f, 0.095f, 0.55f));
        Stretch((RectTransform)fill.transform);
        fill.raycastTarget = true;

        // The row that launches the game gets a standing cyan bar and a cyan wash. Both are drawn
        // *behind* the pieces `MenuButtonVisual` animates and are not referenced by it, because it
        // rebuilds the fill and border colours from its own palette on Awake - a hand-set colour on
        // either of those is a colour that lasts until the first frame.
        if (primary)
        {
            Image wash = Img(rt, "PrimaryWash", new Color(UITheme.Cyan.r, UITheme.Cyan.g,
                UITheme.Cyan.b, 0.10f));
            Stretch((RectTransform)wash.transform);

            Image standing = Img(rt, "PrimaryBar", UITheme.CyanBright);
            Anchor((RectTransform)standing.transform, new Vector2(0f, 0.5f), Vector2.zero,
                new Vector2(8f, rowH));
        }

        Image bar = Img(rt, "Accent", accent);
        Anchor((RectTransform)bar.transform, new Vector2(0f, 0.5f), Vector2.zero,
            new Vector2(4f, rowH));

        Image edge = Img(rt, "Edge", new Color(1f, 1f, 1f, 0.05f));
        Anchor((RectTransform)edge.transform, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(700f, 1f));

        TMP_Text big = Text(rt, "Label", label, UITheme.MenuRow, UITheme.White, TextAlignmentOptions.Left, UITheme.DisplaySpacing, FontStyles.Bold, UIFontRole.Display);
        Anchor((RectTransform)big.transform, new Vector2(0f, 0.5f), new Vector2(40f, 0f), new Vector2(380f, 128f));

        TMP_Text small = Text(rt, "Caption", caption, UITheme.Caption,
            primary ? UITheme.CyanBright : UITheme.Label, TextAlignmentOptions.Right,
            UITheme.LabelSpacing, fontRole: UIFontRole.Mono);
        Anchor((RectTransform)small.transform, new Vector2(1f, 0.5f), new Vector2(-32f, 0f),
            new Vector2(320f, 34f));
        AutoSize(small, UITheme.MinimumSize, UITheme.Caption);

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

    // ------------------------------------------------------------------ main run

    /// <summary>
    /// The main run, given a whole screen.
    ///
    /// This is the answer to "the menu presents three equivalent levels": it does not present them
    /// at all any more. PLAY opens this, one level fills the frame at title size against its own
    /// skyline, and the only control on it is START RUN. The two practice courses are a screen of
    /// their own, reached by a row that says TRAINING.
    ///
    /// Built from the same primitives, palette and type scale as everything else here, so it reads
    /// as the same game: dark ground, one cyan accent, condensed display caps for the name, mono
    /// for the labels, and the same outline hover the rest of the menu uses.
    /// </summary>
    private static HeroRefs BuildMainRunPanel(RectTransform root, LevelEntry mainRun,
        out UIPanel panel)
    {
        RectTransform layer = Layer(root, "MainRunPanel", out panel);

        Image bg = Img(layer, "Background", new Color(0.024f, 0.028f, 0.035f, 1f));
        Stretch((RectTransform)bg.transform);

        // The city itself behind the copy, washed back far enough for 24pt mono to stay legible on
        // top of it. The level's own preview when it has one, the menu backdrop when it does not.
        RawImage shot = Raw(layer, "Backdrop", PreviewFolder + "MenuBackdrop.png");
        Stretch((RectTransform)shot.transform);
        shot.color = new Color(1f, 1f, 1f, 0.55f);

        Image wash = Img(layer, "BackdropWash", new Color(0.02f, 0.025f, 0.032f, 0.62f));
        Stretch((RectTransform)wash.transform);

        // A left column so the copy always sits on solid ground whatever the photo is doing.
        Image column = Img(layer, "LeftColumn", new Color(0.027f, 0.031f, 0.039f, 0.94f));
        RectTransform columnRt = (RectTransform)column.transform;
        columnRt.anchorMin = new Vector2(0f, 0f);
        columnRt.anchorMax = new Vector2(0f, 1f);
        columnRt.pivot = new Vector2(0f, 0.5f);
        columnRt.anchoredPosition = Vector2.zero;
        columnRt.sizeDelta = new Vector2(1080f, 0f);

        RawImage fade = Raw(layer, "ColumnFade", PreviewFolder + "FadeGradient.png");
        RectTransform fadeRt = (RectTransform)fade.transform;
        fadeRt.anchorMin = new Vector2(0f, 0f);
        fadeRt.anchorMax = new Vector2(0f, 1f);
        fadeRt.pivot = new Vector2(0f, 0.5f);
        fadeRt.anchoredPosition = new Vector2(1080f, 0f);
        fadeRt.sizeDelta = new Vector2(300f, 0f);

        // ---- the badge that says what this screen is
        Image badge = Img(layer, "TrackBadge", UITheme.CyanBright);
        TopLeft((RectTransform)badge.transform, 64f, 96f, 6f, 32f);

        TMP_Text track = Text(layer, "TrackLabel", "MAIN RUN", UITheme.Eyebrow, UITheme.CyanBright,
            TextAlignmentOptions.TopLeft, UITheme.EyebrowSpacing, fontRole: UIFontRole.Mono);
        TopLeft(track, 86f, 88f, 700f, 40f);

        TMP_Text number = Text(layer, "LevelNumber", "LEVEL 03", UITheme.LabelSmall, UITheme.Label,
            TextAlignmentOptions.TopLeft, UITheme.LabelSpacing, fontRole: UIFontRole.Mono);
        TopLeft(number, 86f, 140f, 500f, 34f);

        // ---- the name, at hero size. Two lines so a long one never has to shrink to nothing.
        TMP_Text title = Text(layer, "Title", "SKYBOUND CITY", UITheme.TitleHuge, UITheme.White,
            TextAlignmentOptions.TopLeft, UITheme.DisplaySpacing, FontStyles.Bold,
            UIFontRole.Display);
        TopLeft(title, 60f, 190f, 1000f, 200f);
        Prose(title);
        AutoSize(title, UITheme.TitleMedium * 0.6f, UITheme.TitleHuge);

        // Everything below the name is spaced on the same two gaps: a section between blocks, a
        // heading gap between a label and the thing it labels. The screen had them all at 26 to 50
        // units of a mixture, which is what read as lines running into each other.
        const float ruleY = 400f;
        const float subtitleY = ruleY + UITheme.HeadingGap;
        const float pitchY = subtitleY + 48f + UITheme.SectionGap;
        const float statsY = pitchY + 92f + UITheme.SectionGap;
        const float tipY = statsY + 96f + UITheme.SectionGap;

        Image rule = Img(layer, "Rule", new Color(UITheme.Cyan.r, UITheme.Cyan.g, UITheme.Cyan.b, 0.35f));
        TopLeft((RectTransform)rule.transform, 64f, ruleY, 950f, 1f);

        TMP_Text subtitle = Text(layer, "Subtitle", "", UITheme.Subtitle, UITheme.Label,
            TextAlignmentOptions.TopLeft, 3f, fontRole: UIFontRole.Mono);
        TopLeft(subtitle, 64f, subtitleY, 960f, 48f);
        AutoSize(subtitle, UITheme.MinimumSize, UITheme.Subtitle);

        TMP_Text pitch = Text(layer, "Pitch",
            "The full run. Five relays across six districts, taken in any order, then the tower.",
            UITheme.Body, UITheme.White, TextAlignmentOptions.TopLeft, 1f);
        TopLeft(pitch, 64f, pitchY, 900f, 92f);
        Prose(pitch);

        // ---- the record strip
        // A label and its value are one unit, so the gap inside the pair is the heading gap and
        // the gap to the next pair is a section. 72, not 62, on the value: one line of Anton at
        // StatValueLarge measures 69 units tall.
        TopLeft(Text(layer, "ClearedLabel", "MODES CLEARED", UITheme.LabelSmall, UITheme.Label,
                TextAlignmentOptions.TopLeft, UITheme.LabelSpacing, fontRole: UIFontRole.Mono),
            64f, statsY, 300f, 32f);
        TMP_Text cleared = Text(layer, "ClearedValue", "0 / 2", UITheme.StatValueLarge, UITheme.Label,
            TextAlignmentOptions.TopLeft, 0f, FontStyles.Bold, UIFontRole.Display);
        TopLeft(cleared, 64f, statsY + UITheme.HeadingGap, 300f, 76f);

        TopLeft(Text(layer, "StatusLabel", "STATUS", UITheme.LabelSmall, UITheme.Label,
                TextAlignmentOptions.TopLeft, UITheme.LabelSpacing, fontRole: UIFontRole.Mono),
            396f, statsY, 400f, 32f);
        TMP_Text status = Text(layer, "StatusValue", "NOT YET RUN", UITheme.StatValue, UITheme.Label,
            TextAlignmentOptions.TopLeft, 2f, FontStyles.Bold, UIFontRole.Mono);
        TopLeft(status, 396f, statsY + UITheme.HeadingGap + 6f, 480f, 60f);
        AutoSize(status, UITheme.MinimumSize, UITheme.StatValue);

        TMP_Text tip = Text(layer, "Tip", "", UITheme.StatLabel, UITheme.Dim,
            TextAlignmentOptions.TopLeft, 1f, fontRole: UIFontRole.Mono);
        TopLeft(tip, 64f, tipY, 940f, 84f);
        Prose(tip);

        // ---- the preview, out on the photo side, so the screen has a subject as well as copy
        RectTransform frame = Block(layer, "PreviewFrame", new Vector2(1f, 0.5f),
            new Vector2(-72f, 40f), new Vector2(700f, 400f));
        Image frameBorder = Img(frame, "Border", UITheme.PanelBorder);
        Stretch((RectTransform)frameBorder.transform);
        Image frameFill = Img(frame, "Fill", new Color(0.05f, 0.06f, 0.07f, 0.55f));
        RectTransform frameFillRt = (RectTransform)frameFill.transform;
        Stretch(frameFillRt);
        frameFillRt.offsetMin = Vector2.one;
        frameFillRt.offsetMax = -Vector2.one;

        RawImage preview = Raw(frame, "Preview", null);
        Stretch((RectTransform)preview.transform);

        Image frameAccent = Img(frame, "Accent", UITheme.CyanBright);
        Anchor((RectTransform)frameAccent.transform, new Vector2(0f, 1f), Vector2.zero,
            new Vector2(96f, 4f));

        // ---- the one control
        HeroRefs refs = new HeroRefs();
        Button start = PrimaryButton(layer, "StartRunButton", "START RUN",
            new Vector2(64f, 72f), new Vector2(520f, 104f));

        refs.Back = SmallButton(layer, "BackButton", "BACK",
            new Vector2(64f + 520f + UITheme.RowGutter, 72f), new Vector2(240f, 104f));

        FeaturedLevelView view = layer.gameObject.AddComponent<FeaturedLevelView>();
        SetRef(view, "startButton", start);
        SetRef(view, "preview", preview);
        SetRef(view, "trackLabel", track);
        SetRef(view, "numberLabel", number);
        SetRef(view, "title", title);
        SetRef(view, "subtitle", subtitle);
        SetRef(view, "statusValue", status);
        SetRef(view, "clearedValue", cleared);
        SetRef(view, "tip", tip);
        refs.View = view;

        // Built from the asset so the scene reads correctly in the editor before it is ever run.
        if (mainRun != null)
        {
            title.text = mainRun.DisplayName;
            subtitle.text = mainRun.Subtitle;
            number.text = mainRun.NumberLabel;
            track.text = mainRun.TrackLabel;
            tip.text = mainRun.Tip;
            preview.texture = mainRun.Preview;
            preview.enabled = mainRun.Preview != null;
        }
        else
        {
            preview.enabled = false;
        }

        return refs;
    }

    /// <summary>The one filled, cyan-on-dark call to action in the game. Used once.</summary>
    private static Button PrimaryButton(RectTransform parent, string name, string caption,
        Vector2 fromBottomLeft, Vector2 size)
    {
        RectTransform rt = Block(parent, name, new Vector2(0f, 0f), fromBottomLeft, size);

        Image border = Img(rt, "Border", UITheme.CyanBright);
        Stretch((RectTransform)border.transform);

        // Solid accent with dark text: `MenuButtonVisual.Style.Primary`, which is the game's
        // existing call-to-action treatment - the same one TRY AGAIN and REPLAY wear.
        Image fill = Img(rt, "Fill", UITheme.CyanBright);
        RectTransform fillRt = (RectTransform)fill.transform;
        Stretch(fillRt);
        fillRt.offsetMin = new Vector2(2f, 2f);
        fillRt.offsetMax = new Vector2(-2f, -2f);
        fill.raycastTarget = true;

        TMP_Text label = Text(rt, "Label", caption, UITheme.ButtonLabel, new Color32(8, 10, 12, 255),
            TextAlignmentOptions.Center, 6f, FontStyles.Bold, UIFontRole.Display);
        label.textWrappingMode = TextWrappingModes.NoWrap;
        Stretch((RectTransform)label.transform);

        Button button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = fill;
        button.transition = Selectable.Transition.None;

        MenuButtonVisual visual = rt.gameObject.AddComponent<MenuButtonVisual>();
        SetRef(visual, "background", fill);
        SetRef(visual, "border", border);
        SetRef(visual, "label", label);
        SetValue(visual, "style", (int)MenuButtonVisual.Style.Primary);
        SetColor(visual, "accent", UITheme.CyanBright);

        return button;
    }

    // ------------------------------------------------------------------ training

    private struct SelectRefs
    {
        public List<LevelCardView> Cards;
        public Button Back;
        public TMP_Text Cleared;
    }

    /// <summary>
    /// The practice courses, grouped and labelled so they cannot be mistaken for the game.
    ///
    /// Same grid and the same card as before - the two maps are unchanged and so is what happens
    /// when one is clicked - but the screen around them now says what they are for, and each card
    /// carries a TRAINING badge of its own so the label survives a screenshot.
    /// </summary>
    private static SelectRefs BuildTrainingPanel(RectTransform root, int levelCount,
        out UIPanel panel)
    {
        RectTransform layer = Layer(root, "TrainingPanel", out panel);

        Image bg = Img(layer, "Background", new Color(0.027f, 0.031f, 0.039f, 0.985f));
        Stretch((RectTransform)bg.transform);

        TopLeft(Text(layer, "Eyebrow", "TRAINING", UITheme.Eyebrow, UITheme.Orange, TextAlignmentOptions.TopLeft, UITheme.EyebrowSpacing, fontRole: UIFontRole.Mono),
            64f, 62f, 900f, 40f);
        TopLeft(Text(layer, "TitleTop", "PRACTICE", UITheme.TitleMedium, UITheme.White, TextAlignmentOptions.TopLeft, UITheme.DisplaySpacing, FontStyles.Bold, UIFontRole.Display),
            60f, 106f, 1400f, 144f);
        TopLeft(Text(layer, "TitleBottom", "COURSES", UITheme.TitleMedium, UITheme.Orange, TextAlignmentOptions.TopLeft, UITheme.DisplaySpacing, FontStyles.Bold, UIFontRole.Display),
            60f, 106f + UITheme.TitleMedium * UITheme.DisplayLineStep, 1400f, 144f);

        // The two display lines end at 106 + 89 + a 78-unit cap = 273. A section below that, not
        // the 25 units the note used to sit at.
        TMP_Text note = Text(layer, "Note",
            "Learn the moves here. These are tutorial maps - the run the game is about is Skybound City.",
            UITheme.Body, UITheme.Label, TextAlignmentOptions.TopLeft, 1f);
        TopLeft(note, 64f, 273f + UITheme.SectionGap, 1520f, 84f);
        Prose(note);

        Image rule = Img(layer, "Rule", new Color(UITheme.Cyan.r, UITheme.Cyan.g, UITheme.Cyan.b, 0.30f));
        TopLeft((RectTransform)rule.transform, 64f, 273f + UITheme.SectionGap + 84f + 12f, 1792f, 1f);

        SelectRefs refs = new SelectRefs { Cards = new List<LevelCardView>() };

        refs.Cleared = Text(layer, "Cleared", "0 / 0 COMPLETE", UITheme.StatLabel, UITheme.Label, TextAlignmentOptions.Right, UITheme.LabelSpacing, fontRole: UIFontRole.Mono);
        Anchor((RectTransform)refs.Cleared.transform, new Vector2(1f, 1f), new Vector2(-64f, -422f), new Vector2(600f, 36f));

        for (int i = 0; i < Mathf.Max(levelCount, 2); i++)
        {
            refs.Cards.Add(BuildCard(layer, i));
        }

        refs.Back = SmallButton(layer, "BackButton", "BACK", new Vector2(64f, 64f), new Vector2(240f, 70f));

        return refs;
    }

    private static LevelCardView BuildCard(RectTransform parent, int index)
    {
        // 500 tall on a 570-wide card, laid on the same two gaps as the rest of the menu. At 430
        // the title box ended two units *below* where the subtitle began, and every other pair was
        // 18 to 22 units apart for type 22 to 28 points tall - which is the collision the screens
        // showed. Nothing here got smaller; the card got taller and the gaps got real.
        const float cardW = 570f, cardH = 484f;
        const float previewH = 176f;
        const float pad = 24f;
        RectTransform rt = Block(parent, $"LevelCard_{index + 1:00}", new Vector2(0f, 1f),
            new Vector2(64f + index * (cardW + 40f), -432f), new Vector2(cardW, cardH));

        Image border = Img(rt, "Border", UITheme.PanelBorder);
        Stretch((RectTransform)border.transform);

        Image fill = Img(rt, "Fill", new Color(0.075f, 0.085f, 0.10f, 0.98f));
        RectTransform fillRt = (RectTransform)fill.transform;
        Stretch(fillRt);
        fillRt.offsetMin = new Vector2(1f, 1f);
        fillRt.offsetMax = new Vector2(-1f, -1f);
        fill.raycastTarget = true;

        RawImage preview = Raw(rt, "Preview", null);
        Anchor((RectTransform)preview.transform, new Vector2(0.5f, 1f), new Vector2(0f, -1f), new Vector2(cardW - 2f, previewH));

        Image previewDim = Img(rt, "PreviewDim", new Color(0.02f, 0.025f, 0.03f, 0.25f));
        Anchor((RectTransform)previewDim.transform, new Vector2(0.5f, 1f), new Vector2(0f, -1f), new Vector2(cardW - 2f, previewH));

        TMP_Text idx = Text(rt, "Index", "00", UITheme.StatLabel, new Color(1f, 1f, 1f, 0.55f), TextAlignmentOptions.TopLeft, 6f, FontStyles.Bold, UIFontRole.Mono);
        Anchor((RectTransform)idx.transform, new Vector2(0f, 1f), new Vector2(pad, -16f), new Vector2(140f, 36f));

        // The badge is on the card, not only on the screen around it: a card that has been
        // screenshotted, or one seen for a second on the way past, still says what it is.
        Image badgeBar = Img(rt, "TrackBar", UITheme.Orange);
        Anchor((RectTransform)badgeBar.transform, new Vector2(1f, 1f), new Vector2(-pad, -18f),
            new Vector2(4f, 30f));

        TMP_Text trackLabel = Text(rt, "TrackLabel", "TRAINING", UITheme.LabelSmall, UITheme.Orange,
            TextAlignmentOptions.Right, UITheme.LabelSpacing, FontStyles.Bold, UIFontRole.Mono);
        Anchor((RectTransform)trackLabel.transform, new Vector2(1f, 1f), new Vector2(-pad - 14f, -16f),
            new Vector2(240f, 34f));

        // Title gets the full card width: "INDUSTRIAL PARKOUR" at bold 34 overruns a 340 box and
        // collides with the rating marks, so the stars sit on their own row underneath instead.
        // Top-down from the bottom of the preview, on the menu's two gaps.
        float titleY = previewH + UITheme.HeadingGap;
        float subtitleY = titleY + 48f + 14f;
        float starsY = subtitleY + 36f + UITheme.HeadingGap;
        float dividerY = starsY + 28f;
        float labelY = dividerY + UITheme.HeadingGap;
        float valueY = labelY + 34f;

        TMP_Text title = Text(rt, "Title", "LEVEL", UITheme.CardTitle, UITheme.White, TextAlignmentOptions.TopLeft, 1f, FontStyles.Bold, UIFontRole.Display);
        Anchor((RectTransform)title.transform, new Vector2(0f, 1f), new Vector2(pad, -titleY), new Vector2(cardW - pad * 2f, 48f));
        AutoSize(title, UITheme.CardTitle * 0.72f, UITheme.CardTitle);

        TMP_Text sub = Text(rt, "Subtitle", "", UITheme.StatLabel, UITheme.Label, TextAlignmentOptions.TopLeft, 2f, fontRole: UIFontRole.Mono);
        Anchor((RectTransform)sub.transform, new Vector2(0f, 1f), new Vector2(pad, -subtitleY), new Vector2(cardW - pad * 2f, 36f));
        AutoSize(sub, UITheme.MinimumSize, UITheme.StatLabel);

        List<Image> stars = new List<Image>();
        for (int i = 0; i < 3; i++)
        {
            Image s = Img(rt, $"Star_{i + 1}", new Color(UITheme.Cyan.r, UITheme.Cyan.g, UITheme.Cyan.b, 0.15f));
            RectTransform srt = (RectTransform)s.transform;
            Anchor(srt, new Vector2(1f, 1f), new Vector2(-pad - (2 - i) * 32f, -starsY), new Vector2(16f, 16f));
            srt.localRotation = Quaternion.Euler(0f, 0f, 45f);
            stars.Add(s);
        }

        Image div = Img(rt, "Divider", new Color(1f, 1f, 1f, 0.07f));
        Anchor((RectTransform)div.transform, new Vector2(0.5f, 1f), new Vector2(0f, -dividerY), new Vector2(cardW - pad * 2f, 1f));

        TMP_Text bestLabel = Text(rt, "BestLabel", "MODES CLEARED", UITheme.LabelSmall, UITheme.Label, TextAlignmentOptions.TopLeft, UITheme.LabelSpacing, fontRole: UIFontRole.Mono);
        Anchor((RectTransform)bestLabel.transform, new Vector2(0f, 1f), new Vector2(pad, -labelY), new Vector2(250f, 32f));
        TMP_Text bestValue = Text(rt, "BestValue", "--:--.--", UITheme.CardTitle, UITheme.Dim, TextAlignmentOptions.TopLeft, 0f, FontStyles.Bold, UIFontRole.Display);
        Anchor((RectTransform)bestValue.transform, new Vector2(0f, 1f), new Vector2(pad, -valueY), new Vector2(250f, 50f));

        TMP_Text statusLabel = Text(rt, "StatusLabel", "STATUS", UITheme.LabelSmall, UITheme.Label, TextAlignmentOptions.Right, UITheme.LabelSpacing, fontRole: UIFontRole.Mono);
        Anchor((RectTransform)statusLabel.transform, new Vector2(1f, 1f), new Vector2(-pad, -labelY), new Vector2(250f, 32f));
        TMP_Text statusValue = Text(rt, "StatusValue", "AVAILABLE", UITheme.StatLabel, UITheme.Label, TextAlignmentOptions.Right, 2f, FontStyles.Bold, UIFontRole.Mono);
        Anchor((RectTransform)statusValue.transform, new Vector2(1f, 1f), new Vector2(-pad, -valueY - 4f), new Vector2(250f, 44f));
        AutoSize(statusValue, UITheme.MinimumSize, UITheme.StatLabel);

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
        SetRef(card, "trackLabel", trackLabel);
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

        TMP_Text label = Text(rt, "Label", caption, UITheme.ButtonLabelSmall, UITheme.White, TextAlignmentOptions.Center, 4f, FontStyles.Bold, UIFontRole.Display);
        label.textWrappingMode = TextWrappingModes.NoWrap;
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
            new Vector2(1320f, 780f));

        Image panelBorder = Img(content, "Border", UITheme.PanelBorder);
        Stretch((RectTransform)panelBorder.transform);

        Image panelFill = Img(content, "Fill", UITheme.PanelFill);
        RectTransform panelFillRt = (RectTransform)panelFill.transform;
        Stretch(panelFillRt);
        panelFillRt.offsetMin = Vector2.one;
        panelFillRt.offsetMax = -Vector2.one;

        Image accent = Img(content, "Accent", UITheme.Cyan);
        TopLeft((RectTransform)accent.transform, 40f, 32f, 5f, 128f);

        TMP_Text eyebrow = Text(content, "Eyebrow", "SELECT RUN MODE", UITheme.StatLabel, UITheme.Cyan,
            TextAlignmentOptions.TopLeft, UITheme.EyebrowSpacing, fontRole: UIFontRole.Mono);
        TopLeft(eyebrow, 64f, 28f, 700f, 34f);

        TMP_Text levelNumber = Text(content, "LevelNumber", "LEVEL 01", UITheme.LabelSmall, UITheme.Label,
            TextAlignmentOptions.TopLeft, UITheme.LabelSpacing, fontRole: UIFontRole.Mono);
        TopLeft(levelNumber, 64f, 70f, 480f, 30f);

        TMP_Text levelName = Text(content, "LevelName", "LEVEL", UITheme.HeadingSmall, UITheme.White,
            TextAlignmentOptions.TopLeft, UITheme.DisplaySpacing, FontStyles.Bold, UIFontRole.Display);
        TopLeft(levelName, 62f, 96f, 1000f, 80f);
        AutoSize(levelName, UITheme.HeadingSmall * 0.65f, UITheme.HeadingSmall);

        TMP_Text levelSubtitle = Text(content, "LevelSubtitle", string.Empty, UITheme.StatLabel, UITheme.Label,
            TextAlignmentOptions.TopLeft, 2f, fontRole: UIFontRole.Mono);
        TopLeft(levelSubtitle, 64f, 174f, 1100f, 34f);
        AutoSize(levelSubtitle, UITheme.MinimumSize, UITheme.StatLabel);

        ModeChoiceRefs checkpoint = BuildModeChoice(content, "CheckpointMode", new Vector2(-316f, -42f),
            RunModeRules.For(GameMode.Checkpoint).DisplayName,
            "DEATH RESPAWNS YOU AT THE LATEST CHECKPOINT. THE TIMER CONTINUES.",
            UITheme.Cyan);

        ModeChoiceRefs noCheckpoint = BuildModeChoice(content, "NoCheckpointMode", new Vector2(316f, -42f),
            RunModeRules.For(GameMode.NoCheckpoint).DisplayName,
            "DEATH RESETS THE WHOLE RUN: TIMER, PROGRESS, AND COUNTDOWN.",
            UITheme.Cyan);

        Button back = SmallButton(content, "BackButton", "BACK", new Vector2(40f, 30f),
            new Vector2(240f, 68f));

        TMP_Text prompt = Text(content, "Prompt", "CHOOSE A RULESET TO BEGIN", UITheme.LabelSmall, UITheme.Dim,
            TextAlignmentOptions.Right, UITheme.LabelSpacing, fontRole: UIFontRole.Mono);
        Anchor((RectTransform)prompt.transform, new Vector2(1f, 0f), new Vector2(-40f, 50f),
            new Vector2(760f, 30f));

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
            new Vector2(608f, 400f));

        Image border = Img(rt, "Border", UITheme.PanelBorder);
        Stretch((RectTransform)border.transform);

        Image fill = Img(rt, "Fill", UITheme.ButtonIdle);
        RectTransform fillRt = (RectTransform)fill.transform;
        Stretch(fillRt);
        fillRt.offsetMin = Vector2.one;
        fillRt.offsetMax = -Vector2.one;
        fill.raycastTarget = true;

        Image edge = Img(rt, "Accent", accent);
        TopLeft((RectTransform)edge.transform, 0f, 0f, 5f, 400f);

        TMP_Text title = Text(rt, "Title", titleText, UITheme.CardTitle, UITheme.White,
            TextAlignmentOptions.TopLeft, UITheme.DisplaySpacing, FontStyles.Bold, UIFontRole.Display);
        TopLeft(title, 30f, 26f, 548f, 46f);
        AutoSize(title, UITheme.CardTitle * 0.72f, UITheme.CardTitle);

        TMP_Text availability = Text(rt, "Availability", "AVAILABLE NOW", UITheme.LabelSmall, accent,
            TextAlignmentOptions.TopLeft, UITheme.LabelSpacing, fontRole: UIFontRole.Mono);
        TopLeft(availability, 30f, 80f, 548f, 31f);

        // The only wrapped prose in the menu. Two lines of 24pt mono need 74 units of leading,
        // so the block is 96 tall and the divider under it moves down to match.
        TMP_Text rules = Text(rt, "Rules", ruleText, UITheme.StatLabel, UITheme.Label,
            TextAlignmentOptions.TopLeft, 1f, fontRole: UIFontRole.Mono);
        TopLeft(rules, 30f, 128f, 548f, 116f);
        Prose(rules);
        rules.overflowMode = TextOverflowModes.Ellipsis;

        Image divider = Img(rt, "Divider", new Color(1f, 1f, 1f, 0.08f));
        TopLeft((RectTransform)divider.transform, 30f, 266f, 548f, 1f);

        TMP_Text bestLabel = Text(rt, "BestLabel", "PERSONAL BEST", UITheme.LabelSmall, UITheme.Label,
            TextAlignmentOptions.TopLeft, UITheme.LabelSpacing, fontRole: UIFontRole.Mono);
        TopLeft(bestLabel, 30f, 266f + UITheme.HeadingGap, 340f, 32f);

        TMP_Text best = Text(rt, "BestValue", "--:--.--", UITheme.StatValue, UITheme.Dim,
            TextAlignmentOptions.TopLeft, 0f, FontStyles.Bold, UIFontRole.Display);
        TopLeft(best, 30f, 266f + UITheme.HeadingGap + 34f, 340f, 62f);

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

    // ------------------------------------------------------------------ player stats

    private struct StatsRefs
    {
        public PlayerStatsView View;
        public Button Back;
    }

    /// <summary>
    /// The runner profile: the whole persisted career on one screen.
    ///
    /// Laid out from Player_Stats.png - three equal columns on a 36-unit gutter, a brand mark and
    /// an eyebrow over a two-tone display title, one cyan hairline under the header, dark bordered
    /// panels, and a left column of stat plates over the action breakdown. Two things about that
    /// reference are deliberately not copied.
    ///
    /// The first is its type sizes. The mockup is a 2005 x 1186 browser screenshot read at desk
    /// distance; its panel headings measure about 16pt of mono in canvas units, which renders a
    /// 7.6-pixel cap at 1280x720 and fails the floor <see cref="UITypographyAudit"/> enforces. So
    /// the reference sets the structure and <see cref="UITheme"/> sets the sizes, exactly as the
    /// rest of this menu already does. The consequence is that the same information needs more
    /// room, which is why the action breakdown sits under Recent Runs - in the half of the centre
    /// column the reference leaves empty - rather than beneath the stat plates.
    ///
    /// The second is its content. Every VERTEX mark is SKYBOUND TRIALS, the invented runner class
    /// and combo score are gone, the leaderboard ranks are gone because the game has no
    /// leaderboard, and the achievements column is replaced by the main run's real record - the
    /// project has no achievement system and inventing one to fill a panel would be worse than an
    /// honest one.
    /// </summary>
    private static StatsRefs BuildStatsPanel(RectTransform root, List<LevelEntry> levels,
        out UIPanel panel)
    {
        // ---- the grid the whole screen is measured from
        const float margin = 64f;
        const float columnW = 573f;
        const float gutter = 36f;
        const float leftX = margin;
        const float centreX = margin + columnW + gutter;          // 673
        const float rightX = centreX + columnW + gutter;          // 1282
        const float contentTop = 326f;

        RectTransform layer = Layer(root, "StatsPanel", out panel);

        Image bg = Img(layer, "Background", new Color(0.024f, 0.028f, 0.035f, 1f));
        Stretch((RectTransform)bg.transform);

        StatsRefs refs = new StatsRefs();

        // ---- header --------------------------------------------------------------
        // The reference's top bar is a mockup navigator for a page of screens, not a game
        // control, so it is not recreated: the brand stays where it was, and the one thing the
        // player actually needs up there - the way back - takes the right-hand end of the row.
        Image tick = Img(layer, "BrandTick", UITheme.CyanBright);
        TopLeft((RectTransform)tick.transform, margin, 44f, 6f, 30f);

        TopLeft(Text(layer, "Brand", "SKYBOUND TRIALS", UITheme.ButtonLabel, UITheme.White,
                TextAlignmentOptions.TopLeft, 8f, FontStyles.Bold, UIFontRole.Display),
            84f, 38f, 480f, 48f);

        TopLeft(Text(layer, "Eyebrow", "RUNNER PROFILE", UITheme.StatLabel, UITheme.Cyan,
                TextAlignmentOptions.TopLeft, UITheme.EyebrowSpacing, fontRole: UIFontRole.Mono),
            margin, 100f, 700f, 35f);

        // One text object, not two: a display headline split across two objects has to be placed
        // by hand from an advance-width estimate, and the word gap is then wrong the moment the
        // face changes. Rich text lets TMP set the line and colour the second word.
        // The box is the full content width rather than the line's measured width: the colour tag
        // is markup, and a preferred-width measurement that counted it as ink would read as a
        // clipped headline in the typography audit.
        string statsInCyan = $"PLAYER <color=#{ColorUtility.ToHtmlStringRGB(UITheme.CyanBright)}>" +
                             "STATS</color>";
        TopLeft(Text(layer, "Title", statsInCyan, UITheme.TitleMedium, UITheme.White,
                TextAlignmentOptions.TopLeft, UITheme.DisplaySpacing, FontStyles.Bold,
                UIFontRole.Display),
            60f, 132f, 1792f, 146f);

        Image rule = Img(layer, "Rule",
            new Color(UITheme.Cyan.r, UITheme.Cyan.g, UITheme.Cyan.b, 0.35f));
        TopLeft((RectTransform)rule.transform, margin, 298f, 1792f, 1f);

        refs.Back = SmallButton(layer, "StatsBackButton", "BACK", Vector2.zero,
            new Vector2(200f, 56f));
        Anchor((RectTransform)refs.Back.transform, new Vector2(1f, 1f), new Vector2(-margin, -34f),
            new Vector2(200f, 56f));

        // ---- left column: identity, career plates, career footer -----------------
        RectTransform identity = StatsCard(layer, "IdentityCard", leftX, contentTop, columnW, 140f);

        // The reference brands this card VERTEX and gives the player an invented "Runner Class".
        // There are no accounts in this game, so the emblem is the game's own initials and the
        // card says what the screen is instead of inventing a rank to fill the line.
        Image emblem = Img(identity, "Emblem", UITheme.CyanBright);
        TopLeft((RectTransform)emblem.transform, 24f, 28f, 84f, 84f);

        TMP_Text emblemMark = Text((RectTransform)emblem.transform, "Mark", "ST", UITheme.StatValue,
            new Color32(8, 10, 12, 255), TextAlignmentOptions.Center, 4f, FontStyles.Bold,
            UIFontRole.Display);
        Stretch((RectTransform)emblemMark.transform);

        TopLeft(Text(identity, "Name", "SKYBOUND TRIALS", UITheme.ButtonLabel, UITheme.White,
                TextAlignmentOptions.TopLeft, 4f, FontStyles.Bold, UIFontRole.Display),
            128f, 32f, 400f, 48f);

        TopLeft(Text(identity, "Role", "RUNNER PROFILE", UITheme.LabelSmall, UITheme.Label,
                TextAlignmentOptions.TopLeft, UITheme.LabelSpacing, fontRole: UIFontRole.Mono),
            128f, 84f, 400f, 32f);

        // Six plates on the reference's two-column grid, one row deeper: the four it shows plus
        // the two career figures a parkour career actually turns on.
        const float plateW = 278f;
        const float plateH = 134f;
        const float plateGutterX = 17f;
        const float plateGutterY = 14f;
        const float platesTop = contentTop + 140f + 16f;           // 482

        TMP_Text totalRuns = StatPlate(layer, "TotalRunsCard", "TOTAL RUNS", "00", string.Empty,
            leftX, platesTop, plateW, plateH);
        TMP_Text completedRuns = StatPlate(layer, "CompletedRunsCard", "COMPLETED RUNS", "00",
            string.Empty, leftX + plateW + plateGutterX, platesTop, plateW, plateH);

        const float platesRow2 = platesTop + plateH + plateGutterY;
        TMP_Text maxSpeed = StatPlate(layer, "MaxSpeedCard", "MAX SPEED", "0.0", "M/S",
            leftX, platesRow2, plateW, plateH);
        TMP_Text distance = StatPlate(layer, "DistanceCard", "DISTANCE", "0.0", "KM",
            leftX + plateW + plateGutterX, platesRow2, plateW, plateH);

        const float platesRow3 = platesRow2 + plateH + plateGutterY;
        TMP_Text deaths = StatPlate(layer, "DeathsCard", "DEATHS", "00", string.Empty,
            leftX, platesRow3, plateW, plateH);
        TMP_Text runTime = StatPlate(layer, "RunTimeCard", "RUN TIME", "00H 00M", string.Empty,
            leftX + plateW + plateGutterX, platesRow3, plateW, plateH);

        RectTransform footer = StatsCard(layer, "CareerFooter", leftX, 924f, columnW, 108f);
        TMP_Text failedRuns = MiniStat(footer, "Failed", "FAILED RUNS", 24f);
        TMP_Text checkpointsHit = MiniStat(footer, "Checkpoints", "CHECKPOINTS", 296f);

        // ---- centre column: recent runs, then the action breakdown ---------------
        RectTransform recent = StatsPanelBox(layer, "RecentRunsPanel", centreX, contentTop,
            columnW, 471f, "RECENT RUNS", string.Empty, out _);

        List<RecentRunRowView> rows = new List<RecentRunRowView>();
        for (int i = 0; i < 4; i++)
        {
            rows.Add(RecentRunRow(recent, i, 69f + i * 98f));
        }

        TMP_Text noRuns = Text(recent, "EmptyMessage", PlayerStatsFormat.NoRuns, UITheme.StatLabel,
            UITheme.Dim, TextAlignmentOptions.Center, UITheme.EyebrowSpacing,
            fontRole: UIFontRole.Mono);
        TopLeft(noRuns, 24f, 200f, 525f, 40f);

        RectTransform breakdown = StatsPanelBox(layer, "ParkourBreakdownPanel", centreX, 821f,
            columnW, 207f, "PARKOUR BREAKDOWN", "ACTION COUNTS", out _);

        List<TMP_Text> actionValues = new List<TMP_Text>();
        List<RectTransform> actionBars = new List<RectTransform>();

        for (int i = 0; i < PlayerStatsFormat.Actions.Length; i++)
        {
            ParkourAction action = PlayerStatsFormat.Actions[i];
            float cellX = i < 3 ? 20f : 297f;
            float cellY = 61f + (i % 3) * 44f;

            actionValues.Add(ActionBarRow(breakdown, action, cellX, cellY, 256f,
                out RectTransform fill));
            actionBars.Add(fill);
        }

        // ---- right column: the main run's record, then the training records ------
        LevelEntry mainRun = levels.Find(l => l != null && l.IsMainRun);

        // Not "MainRunPanel": the menu already has a screen by that name, and two objects with
        // one path is how a find-by-path test starts passing for the wrong reason.
        RectTransform main = StatsPanelBox(layer, "MainRunRecordPanel", rightX, contentTop, columnW,
            450f, "MAIN RUN", mainRun != null ? mainRun.DisplayName : "NO MAIN RUN",
            out TMP_Text mainRunName);

        TMP_Text attempts = RecordRow(main, "Attempts", "ATTEMPTS", "00", 69f, true);
        TMP_Text completions = RecordRow(main, "Completions", "COMPLETIONS", "00", 127f, true);
        TMP_Text bestTime = RecordRow(main, "BestTime", "BEST TIME", PlayerStatsFormat.NoTime,
            185f, true);
        TMP_Text checkpointBest = RecordRow(main, "CheckpointBest", "CHECKPOINT BEST",
            PlayerStatsFormat.NoTime, 243f, true);
        TMP_Text noCheckpointBest = RecordRow(main, "NoCheckpointBest", "NO-CHECKPOINT BEST",
            PlayerStatsFormat.NoTime, 301f, true);
        TMP_Text checkpointsReached = RecordRow(main, "CheckpointsReached", "CHECKPOINTS REACHED",
            "00", 359f, false);

        RectTransform training = StatsPanelBox(layer, "TrainingRecordsPanel", rightX, 800f,
            columnW, 232f, "TRAINING RECORDS", string.Empty, out _);

        List<TMP_Text> trainingNames = new List<TMP_Text>();
        List<TMP_Text> trainingTimes = new List<TMP_Text>();

        for (int i = 0; i < 2; i++)
        {
            trainingNames.Add(TrainingRow(training, i, 69f + i * 78f, i == 0,
                out TMP_Text time));
            trainingTimes.Add(time);
        }

        // ---- wiring --------------------------------------------------------------
        PlayerStatsView view = layer.gameObject.AddComponent<PlayerStatsView>();
        SetList(view, "levels", levels.ConvertAll(l => (Object)l));

        SetRef(view, "totalRunsValue", totalRuns);
        SetRef(view, "completedRunsValue", completedRuns);
        SetRef(view, "maxSpeedValue", maxSpeed);
        SetRef(view, "distanceValue", distance);
        SetRef(view, "deathsValue", deaths);
        SetRef(view, "runTimeValue", runTime);
        SetRef(view, "failedRunsValue", failedRuns);
        SetRef(view, "checkpointsValue", checkpointsHit);

        SetList(view, "actionValues", actionValues.ConvertAll(t => (Object)t));
        SetList(view, "actionBars", actionBars.ConvertAll(t => (Object)t));

        SetList(view, "recentRows", rows.ConvertAll(r => (Object)r));
        SetRef(view, "recentEmptyMessage", noRuns);

        SetRef(view, "mainRunName", mainRunName);
        SetRef(view, "mainRunAttempts", attempts);
        SetRef(view, "mainRunCompletions", completions);
        SetRef(view, "mainRunBestTime", bestTime);
        SetRef(view, "mainRunCheckpointBest", checkpointBest);
        SetRef(view, "mainRunNoCheckpointBest", noCheckpointBest);
        SetRef(view, "mainRunCheckpoints", checkpointsReached);

        SetList(view, "trainingNames", trainingNames.ConvertAll(t => (Object)t));
        SetList(view, "trainingTimes", trainingTimes.ConvertAll(t => (Object)t));

        refs.View = view;

        // The builder deliberately does not bind the screen to the live save.
        //
        // A generated scene has to be a function of the project, not of whoever last ran the
        // game: baking the machine's own career into MainMenu.unity would make two rebuilds
        // produce two different scenes and put one developer's numbers in another's diff. What is
        // committed is therefore the state a new player is supposed to see - zeroes, dashes and
        // NO RUNS RECORDED - and PlayerStatsView fills in the real career when the screen opens.
        return refs;
    }

    /// <summary>A bordered dark card. The screen's only container primitive.</summary>
    private static RectTransform StatsCard(RectTransform parent, string name, float x, float y,
        float w, float h)
    {
        RectTransform rt = Block(parent, name, new Vector2(0f, 1f), new Vector2(x, -y),
            new Vector2(w, h));

        Image border = Img(rt, "Border", UITheme.PanelBorder);
        Stretch((RectTransform)border.transform);

        Image fill = Img(rt, "Fill", UITheme.PanelFillSoft);
        RectTransform fillRt = (RectTransform)fill.transform;
        Stretch(fillRt);
        fillRt.offsetMin = Vector2.one;
        fillRt.offsetMax = -Vector2.one;

        return rt;
    }

    /// <summary>
    /// A bordered panel with a heading and an optional right-aligned caption, as every panel in
    /// the reference carries. Returns the content rect; the caption comes back for binding.
    /// </summary>
    private static RectTransform StatsPanelBox(RectTransform parent, string name, float x, float y,
        float w, float h, string heading, string caption, out TMP_Text captionText)
    {
        RectTransform rt = Block(parent, name, new Vector2(0f, 1f), new Vector2(x, -y),
            new Vector2(w, h));

        Image border = Img(rt, "Border", UITheme.PanelBorder);
        Stretch((RectTransform)border.transform);

        Image fill = Img(rt, "Fill", UITheme.PanelFill);
        RectTransform fillRt = (RectTransform)fill.transform;
        Stretch(fillRt);
        fillRt.offsetMin = Vector2.one;
        fillRt.offsetMax = -Vector2.one;

        TopLeft(Text(rt, "Heading", heading, UITheme.StatLabel, UITheme.Cyan,
                TextAlignmentOptions.TopLeft, UITheme.LabelSpacing, fontRole: UIFontRole.Mono),
            22f, 18f, 300f, 35f);

        // The caption carries a level name, so it gets the width a longer one would need rather
        // than the width the current catalogue happens to want.
        captionText = Text(rt, "Caption", caption, UITheme.LabelSmall, UITheme.Dim,
            TextAlignmentOptions.Right, 4f, fontRole: UIFontRole.Mono);
        TopLeft(captionText, 332f, 20f, w - 332f - 22f, 32f);

        return rt;
    }

    /// <summary>
    /// One of the reference's stat plates: a small-caps label over a display figure, with the
    /// unit set beside it rather than inside it so the number stays the plate's subject.
    /// </summary>
    private static TMP_Text StatPlate(RectTransform parent, string name, string label,
        string value, string unit, float x, float y, float w, float h)
    {
        RectTransform rt = StatsCard(parent, name, x, y, w, h);

        TopLeft(Text(rt, "Label", label, UITheme.LabelSmall, UITheme.Label,
                TextAlignmentOptions.TopLeft, UITheme.LabelSpacing, fontRole: UIFontRole.Mono),
            20f, 18f, 238f, 32f);

        TMP_Text figure = Text(rt, "Value", value, UITheme.StatValue, UITheme.White,
            TextAlignmentOptions.TopLeft, 0f, FontStyles.Bold, UIFontRole.Display);
        TopLeft(figure, 20f, 56f, 170f, 63f);

        // A career count can reach five figures; the plate does not grow, so the figure is the one
        // thing on this screen allowed to set itself smaller rather than run over its own unit.
        AutoSize(figure, UITheme.CardTitle * 0.8f, UITheme.StatValue);

        if (!string.IsNullOrEmpty(unit))
        {
            TopLeft(Text(rt, "Unit", unit, UITheme.LabelSmall, UITheme.Label,
                    TextAlignmentOptions.Left, UITheme.LabelSpacing, fontRole: UIFontRole.Mono),
                196f, 76f, 60f, 32f);
        }

        return figure;
    }

    /// <summary>A label-over-figure pair inside the career footer card.</summary>
    private static TMP_Text MiniStat(RectTransform parent, string name, string label, float x)
    {
        TopLeft(Text(parent, name + "Label", label, UITheme.LabelSmall, UITheme.Label,
                TextAlignmentOptions.TopLeft, UITheme.LabelSpacing, fontRole: UIFontRole.Mono),
            x, 16f, 250f, 32f);

        TMP_Text value = Text(parent, name + "Value", "00", UITheme.ButtonLabel, UITheme.White,
            TextAlignmentOptions.TopLeft, 0f, FontStyles.Bold, UIFontRole.Display);
        TopLeft(value, x, 54f, 250f, 48f);
        AutoSize(value, UITheme.MinimumSize, UITheme.ButtonLabel);

        return value;
    }

    /// <summary>
    /// One Recent Runs row: a track-coloured accent, the level, its ruleset and date, the time it
    /// took and what became of it. The reference's leaderboard rank is not here - the game has no
    /// leaderboard, so a "#4" would be a number about nothing.
    /// </summary>
    private static RecentRunRowView RecentRunRow(RectTransform parent, int index, float y)
    {
        const float rowW = 533f;
        const float rowH = 90f;

        RectTransform rt = Block(parent, $"RecentRun_{index + 1:00}", new Vector2(0f, 1f),
            new Vector2(20f, -y), new Vector2(rowW, rowH));

        Image fill = Img(rt, "Fill", UITheme.PanelFillSoft);
        Stretch((RectTransform)fill.transform);

        Image accent = Img(rt, "Accent", UITheme.CyanBright);
        Anchor((RectTransform)accent.transform, new Vector2(0f, 0.5f), Vector2.zero,
            new Vector2(4f, rowH));

        TMP_Text title = Text(rt, "Title", string.Empty, UITheme.ButtonLabel, UITheme.White,
            TextAlignmentOptions.TopLeft, 1f, FontStyles.Bold, UIFontRole.Display);
        TopLeft(title, 20f, 6f, 250f, 48f);
        AutoSize(title, UITheme.MinimumSize, UITheme.ButtonLabel);

        TMP_Text track = Text(rt, "Track", string.Empty, UITheme.LabelSmall, UITheme.Orange,
            TextAlignmentOptions.Right, 4f, fontRole: UIFontRole.Mono);
        TopLeft(track, 272f, 12f, 128f, 32f);

        TMP_Text time = Text(rt, "Time", string.Empty, UITheme.ButtonLabel, UITheme.White,
            TextAlignmentOptions.Right, 0f, FontStyles.Bold, UIFontRole.Display);
        TopLeft(time, 406f, 6f, 127f, 48f);

        TMP_Text meta = Text(rt, "Meta", string.Empty, UITheme.LabelSmall, UITheme.Label,
            TextAlignmentOptions.TopLeft, 2f, fontRole: UIFontRole.Mono);
        TopLeft(meta, 20f, 56f, 290f, 32f);

        TMP_Text status = Text(rt, "Status", string.Empty, UITheme.LabelSmall, UITheme.Cyan,
            TextAlignmentOptions.Right, 2f, FontStyles.Bold, UIFontRole.Mono);
        TopLeft(status, 320f, 56f, 213f, 32f);

        RecentRunRowView row = rt.gameObject.AddComponent<RecentRunRowView>();
        SetRef(row, "accent", accent);
        SetRef(row, "fill", fill);
        SetRef(row, "title", title);
        SetRef(row, "trackLabel", track);
        SetRef(row, "meta", meta);
        SetRef(row, "time", time);
        SetRef(row, "status", status);

        return row;
    }

    /// <summary>
    /// One action's row in the breakdown: name, count, and a bar measured against the player's
    /// own highest count.
    ///
    /// The reference draws these as skill scores out of a hundred. Nothing in this game produces
    /// such a score, so the number is the raw count - the panel's caption says so - and the bar is
    /// explicitly relative. A bar that implied a 0-100 rating would be the one invented statistic
    /// on the screen.
    /// </summary>
    private static TMP_Text ActionBarRow(RectTransform parent, ParkourAction action, float x,
        float y, float w, out RectTransform barFill)
    {
        RectTransform rt = Block(parent, $"Action_{action}", new Vector2(0f, 1f),
            new Vector2(x, -y), new Vector2(w, 42f));

        TopLeft(Text(rt, "Label", PlayerStatsFormat.Label(action), UITheme.LabelSmall,
                UITheme.Label, TextAlignmentOptions.Left, UITheme.LabelSpacing,
                fontRole: UIFontRole.Mono),
            0f, 0f, 170f, 32f);

        // Not bolded: Roboto Mono Medium is already a 500 weight, and TMP's faux bold adds
        // 0.07em of advance per glyph - enough to push a five-figure count out of its box.
        TMP_Text value = Text(rt, "Value", "0", UITheme.StatLabel, UITheme.Cyan,
            TextAlignmentOptions.Right, 0f, fontRole: UIFontRole.Mono);
        TopLeft(value, 180f, 0f, 76f, 33f);

        Image track = Img(rt, "BarTrack", new Color(1f, 1f, 1f, 0.10f));
        TopLeft((RectTransform)track.transform, 0f, 36f, w, 5f);

        Image fill = Img((RectTransform)track.transform, "BarFill", UITheme.CyanBright);
        barFill = (RectTransform)fill.transform;
        barFill.anchorMin = Vector2.zero;
        barFill.anchorMax = new Vector2(0f, 1f);
        barFill.pivot = new Vector2(0f, 0.5f);
        barFill.offsetMin = Vector2.zero;
        barFill.offsetMax = Vector2.zero;

        return value;
    }

    /// <summary>A label / value line inside the main run's record panel.</summary>
    private static TMP_Text RecordRow(RectTransform parent, string name, string label,
        string value, float y, bool divider)
    {
        RectTransform rt = Block(parent, name + "Row", new Vector2(0f, 1f),
            new Vector2(24f, -y), new Vector2(525f, 52f));

        TopLeft(Text(rt, "Label", label, UITheme.LabelSmall, UITheme.Label,
                TextAlignmentOptions.Left, UITheme.LabelSpacing, fontRole: UIFontRole.Mono),
            0f, 10f, 330f, 32f);

        TMP_Text figure = Text(rt, "Value", value, UITheme.ButtonLabel, UITheme.White,
            TextAlignmentOptions.Right, 0f, FontStyles.Bold, UIFontRole.Display);
        TopLeft(figure, 336f, 2f, 189f, 48f);

        if (divider)
        {
            Image line = Img(rt, "Divider", new Color(1f, 1f, 1f, 0.07f));
            TopLeft((RectTransform)line.transform, 0f, 52f, 525f, 1f);
        }

        return figure;
    }

    /// <summary>One training course and its best time across both rulesets.</summary>
    private static TMP_Text TrainingRow(RectTransform parent, int index, float y, bool divider,
        out TMP_Text time)
    {
        RectTransform rt = Block(parent, $"TrainingRow_{index + 1:00}", new Vector2(0f, 1f),
            new Vector2(24f, -y), new Vector2(525f, 70f));

        Image bar = Img(rt, "Accent", UITheme.Orange);
        Anchor((RectTransform)bar.transform, new Vector2(0f, 0.5f), Vector2.zero,
            new Vector2(3f, 40f));

        TMP_Text name = Text(rt, "Name", string.Empty, UITheme.ButtonLabel, UITheme.White,
            TextAlignmentOptions.Left, 1f, FontStyles.Bold, UIFontRole.Display);
        TopLeft(name, 18f, 10f, 300f, 48f);
        AutoSize(name, UITheme.MinimumSize, UITheme.ButtonLabel);

        time = Text(rt, "Time", PlayerStatsFormat.NoTime, UITheme.ButtonLabel, UITheme.Dim,
            TextAlignmentOptions.Right, 0f, FontStyles.Bold, UIFontRole.Display);
        TopLeft(time, 336f, 10f, 189f, 48f);

        if (divider)
        {
            Image line = Img(rt, "Divider", new Color(1f, 1f, 1f, 0.07f));
            TopLeft((RectTransform)line.transform, 0f, 70f, 525f, 1f);
        }

        return name;
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
        TopLeft((RectTransform)tick.transform, 64f, 58f, 6f, 32f);
        // The reference mockups brand this corner VERTEX. The game is Skybound Trials, and the
        // loading screen is the one place the wordmark appears outside the main menu.
        TopLeft(Text(layer, "Brand", "SKYBOUND TRIALS", UITheme.Eyebrow, UITheme.White, TextAlignmentOptions.TopLeft, 8f, FontStyles.Bold, UIFontRole.Display),
            84f, 54f, 520f, 44f);

        TopLeft(Text(layer, "Eyebrow", "LOADING STAGE", UITheme.Eyebrow, UITheme.Cyan, TextAlignmentOptions.TopLeft, UITheme.EyebrowSpacing, fontRole: UIFontRole.Mono),
            64f, 182f, 1100f, 40f);

        TMP_Text name = Text(layer, "LevelName", "LEVEL", UITheme.TitleLarge, UITheme.White, TextAlignmentOptions.TopLeft, UITheme.DisplaySpacing, FontStyles.Bold, UIFontRole.Display);
        TopLeft(name, 60f, 222f, 1680f, 152f);
        AutoSize(name, UITheme.TitleLarge * 0.65f, UITheme.TitleLarge);

        // The mockup runs this line at ~52pt - by far the largest subtitle in the game, and the
        // main reason the loading screen reads as a title card rather than a progress bar.
        TMP_Text sub = Text(layer, "Subtitle", "", UITheme.SubtitleLarge, UITheme.Label, TextAlignmentOptions.TopLeft, 4f, fontRole: UIFontRole.Mono);
        TopLeft(sub, 64f, 372f, 1500f, 58f);
        AutoSize(sub, UITheme.Subtitle, UITheme.SubtitleLarge);

        TMP_Text mode = Text(layer, "Mode", "CHECKPOINT MODE", UITheme.StatLabel, UITheme.Cyan,
            TextAlignmentOptions.TopLeft, UITheme.LabelSpacing, fontRole: UIFontRole.Mono);
        TopLeft(mode, 64f, 442f, 900f, 34f);

        // small record strip
        TopLeft(Text(layer, "BestLabel", "YOUR BEST", UITheme.LabelSmall, UITheme.Label, TextAlignmentOptions.TopLeft, UITheme.LabelSpacing, fontRole: UIFontRole.Mono),
            64f, 486f, 340f, 31f);
        TMP_Text best = Text(layer, "BestValue", "--:--.--", UITheme.StatValue, UITheme.Dim, TextAlignmentOptions.TopLeft, 0f, FontStyles.Bold, UIFontRole.Display);
        TopLeft(best, 64f, 516f, 340f, 60f);

        RawImage preview = Raw(layer, "Preview", null);
        TopLeft((RectTransform)preview.transform, 64f, 588f, 760f, 300f);
        Image previewEdge = Img(layer, "PreviewEdge", new Color(1f, 1f, 1f, 0.08f));
        TopLeft((RectTransform)previewEdge.transform, 64f, 888f, 760f, 1f);

        // bottom progress strip
        TMP_Text status = Text(layer, "Status", "PREPARING", UITheme.StatLabel, UITheme.Label, TextAlignmentOptions.Left, UITheme.LabelSpacing, fontRole: UIFontRole.Mono);
        Anchor((RectTransform)status.transform, new Vector2(0f, 0f), new Vector2(64f, 134f), new Vector2(1100f, 34f));

        TMP_Text percent = Text(layer, "Percent", "0%", UITheme.StatLabel, UITheme.Label, TextAlignmentOptions.Right, 2f, fontRole: UIFontRole.Mono);
        Anchor((RectTransform)percent.transform, new Vector2(1f, 0f), new Vector2(-64f, 134f), new Vector2(300f, 34f));

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

        TMP_Text tip = Text(layer, "Tip", "", UITheme.StatLabel, UITheme.Dim, TextAlignmentOptions.Left, 1f, fontRole: UIFontRole.Mono);
        Anchor((RectTransform)tip.transform, new Vector2(0f, 0f), new Vector2(64f, 58f), new Vector2(1700f, 40f));

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

        // TMP renders from a signed distance field, so a transform scale resamples an existing
        // quad instead of re-typesetting. Everything here stays at 1 and changes fontSize.
        go.transform.localScale = Vector3.one;
        return t;
    }

    /// <summary>
    /// Lets one text object shrink, but never grow, to fit its box. Used only where the string
    /// comes from a LevelEntry (names, subtitles, current zone) and a longer entry added later
    /// would otherwise clip. Fixed copy keeps a fixed size so the hierarchy stays predictable.
    /// </summary>
    /// <summary>
    /// Wrapped prose: leading, and a box tall enough for the lines that leading produces.
    ///
    /// Every block of running text in this menu goes through here, so "the lines are too close
    /// together" is one number in <see cref="UITheme"/> rather than a judgement repeated at each
    /// call site.
    /// </summary>
    private static void Prose(TMP_Text t)
    {
        t.textWrappingMode = TextWrappingModes.Normal;
        t.lineSpacing = UITheme.BodyLeading;
        t.paragraphSpacing = UITheme.BodyLeading;
    }

    private static void AutoSize(TMP_Text t, float min, float max)
    {
        t.enableAutoSizing = true;
        t.fontSizeMin = min;
        t.fontSizeMax = max;
        t.fontSize = max;
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
