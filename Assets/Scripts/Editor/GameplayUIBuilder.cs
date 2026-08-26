using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Builds the gameplay UI for IndustrialParkour from the reference mockups in C:\Game_Final\UI.
///
/// Follows the same pattern as the level builders in Assets/UIWorldDemo/Editor: idempotent, run
/// from a menu item, and the single authority on the layout so the hierarchy can always be
/// regenerated instead of hand-patched. Touches nothing outside the GameplayUI root and the
/// EventSystem.
/// </summary>
public static class GameplayUIBuilder
{
    private const string RootName = "GameplayUI";

    private static UIFontSet fonts;

    // ------------------------------------------------------------------ entry point

    [MenuItem("Tools/Parkour UI/Build Gameplay UI")]
    public static void Build()
    {
        if (!UIFontCatalog.TryLoad(out fonts))
        {
            Debug.LogError("[UI] UI font assets could not be loaded. Aborting.");
            return;
        }

        GameObject existing = GameObject.Find(RootName);
        if (existing != null)
        {
            Object.DestroyImmediate(existing);
        }

        EnsureEventSystem();

        GameObject rootGo = new GameObject(RootName,
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

        Canvas canvas = rootGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = rootGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform root = (RectTransform)rootGo.transform;

        GameplayHUD hud = BuildHud(root);
        CountdownView countdown = BuildCountdown(root);
        CheckpointPopup popup = BuildCheckpointPopup(root);
        PauseMenuView pause = BuildPause(root);
        DeathRecoveryView deathRecovery = BuildDeathRecovery(root);
        LevelCompleteView complete = BuildLevelComplete(root);

        WireController(rootGo, hud, countdown, popup, pause, deathRecovery, complete);

        Selection.activeGameObject = rootGo;
        EditorUtility.SetDirty(rootGo);
        Debug.Log("[UI] Gameplay UI rebuilt.");
    }

    private static void EnsureEventSystem()
    {
        EventSystem[] found = Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None);

        // The project runs on activeInputHandler = 1 (Input System only), so the legacy
        // StandaloneInputModule would throw on the first UI event. Force the new module.
        for (int i = found.Length - 1; i >= 1; i--)
        {
            Object.DestroyImmediate(found[i].gameObject);
        }

        GameObject go = found.Length > 0
            ? found[0].gameObject
            : new GameObject("EventSystem", typeof(EventSystem));

        foreach (BaseInputModule stale in go.GetComponents<BaseInputModule>())
        {
            if (!(stale is InputSystemUIInputModule))
            {
                Object.DestroyImmediate(stale);
            }
        }

        if (go.GetComponent<InputSystemUIInputModule>() == null)
        {
            go.AddComponent<InputSystemUIInputModule>();
        }
    }

    // ------------------------------------------------------------------ HUD

    private static GameplayHUD BuildHud(RectTransform root)
    {
        RectTransform layer = Layer(root, "HUD", false, out UIPanel panel);

        TMP_Text mode = Centered(layer, "Mode", "CHECKPOINT MODE", 19f, UITheme.Cyan,
            500f, 900f, UITheme.EyebrowSpacing, fontRole: UIFontRole.Mono);

        // top-left: checkpoint progress
        RectTransform left = Block(layer, "CheckpointBlock", new Vector2(0f, 1f), new Vector2(48f, -44f), new Vector2(320f, 96f));
        HudPlate(left, new Vector2(0f, 1f), new Vector2(-18f, 12f), new Vector2(260f, 116f));
        TMP_Text cpLabel = Text(left, "Label", "CHECKPOINT", UITheme.StatLabel, UITheme.Label, TextAlignmentOptions.TopLeft, UITheme.LabelSpacing, fontRole: UIFontRole.Mono);
        Anchor((RectTransform)cpLabel.transform, new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(320f, 26f));
        TMP_Text cpValue = Text(left, "Value", "0 / 0", 52f, UITheme.White, TextAlignmentOptions.TopLeft, 0f, FontStyles.Bold, UIFontRole.Display);
        Anchor((RectTransform)cpValue.transform, new Vector2(0f, 1f), new Vector2(0f, -30f), new Vector2(320f, 62f));

        // top-right: clock + speed
        RectTransform right = Block(layer, "TimerBlock", new Vector2(1f, 1f), new Vector2(-48f, -44f), new Vector2(360f, 130f));
        HudPlate(right, new Vector2(1f, 1f), new Vector2(18f, 12f), new Vector2(330f, 150f));
        TMP_Text tLabel = Text(right, "Label", "TIME", UITheme.StatLabel, UITheme.Label, TextAlignmentOptions.TopRight, UITheme.LabelSpacing, fontRole: UIFontRole.Mono);
        Anchor((RectTransform)tLabel.transform, new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(360f, 26f));
        TMP_Text tValue = Text(right, "Value", "00:00.00", 58f, UITheme.Cyan, TextAlignmentOptions.TopRight, 0f, FontStyles.Bold, UIFontRole.Display);
        Anchor((RectTransform)tValue.transform, new Vector2(1f, 1f), new Vector2(0f, -30f), new Vector2(360f, 68f));
        TMP_Text speed = Text(right, "Speed", "0.0 m/s", 22f, UITheme.Dim, TextAlignmentOptions.TopRight, 4f, fontRole: UIFontRole.Mono);
        Anchor((RectTransform)speed.transform, new Vector2(1f, 1f), new Vector2(0f, -98f), new Vector2(360f, 28f));

        GameplayHUD hud = layer.gameObject.AddComponent<GameplayHUD>();
        SetRef(hud, "panel", panel);
        SetRef(hud, "modeValue", mode);
        SetRef(hud, "checkpointLabel", cpLabel);
        SetRef(hud, "checkpointValue", cpValue);
        SetRef(hud, "timerValue", tValue);
        SetRef(hud, "speedValue", speed);
        return hud;
    }

    // ------------------------------------------------------------------ countdown

    private static CountdownView BuildCountdown(RectTransform root)
    {
        RectTransform layer = Layer(root, "Countdown", false, out UIPanel panel);
        Scrim(layer, UITheme.ScrimLight);

        TMP_Text eyebrow = Centered(layer, "Eyebrow", "CHECKPOINT MODE  //  GET READY", UITheme.Eyebrow,
            UITheme.Cyan, 190f, 1200f, UITheme.EyebrowSpacing, fontRole: UIFontRole.Mono);
        TMP_Text numeral = Centered(layer, "Numeral", "3", UITheme.TitleHuge * 1.9f, UITheme.White, 10f, 900f, 0f, FontStyles.Bold, UIFontRole.Display);
        ((RectTransform)numeral.transform).sizeDelta = new Vector2(900f, 360f);

        RectTransform pipRow = Block(layer, "Pips", new Vector2(0.5f, 0.5f), new Vector2(0f, -190f), new Vector2(200f, 20f));
        List<Image> pips = new List<Image>();
        for (int i = 0; i < 3; i++)
        {
            Image dot = Img(pipRow, $"Pip_{i + 1}", new Color(UITheme.Cyan.r, UITheme.Cyan.g, UITheme.Cyan.b, 0.22f));
            Anchor((RectTransform)dot.transform, new Vector2(0.5f, 0.5f), new Vector2((i - 1) * 30f, 0f), new Vector2(12f, 12f));
            pips.Add(dot);
        }

        // Control hints, adapted to the moves this game actually has.
        RectTransform hints = Block(layer, "Controls", new Vector2(0.5f, 0f), new Vector2(0f, 86f), new Vector2(900f, 60f));
        string[] labels = { "MOVE", "JUMP", "SPRINT", "PAUSE" };
        string[] keys = { "W A S D", "SPACE", "SHIFT", "ESC" };
        for (int i = 0; i < labels.Length; i++)
        {
            float x = (i - 1.5f) * 200f;
            TMP_Text l = Text(hints, $"Label_{i}", labels[i], 18f, UITheme.Dim, TextAlignmentOptions.Center, 6f, fontRole: UIFontRole.Mono);
            Anchor((RectTransform)l.transform, new Vector2(0.5f, 0.5f), new Vector2(x, 16f), new Vector2(190f, 22f));
            TMP_Text k = Text(hints, $"Key_{i}", keys[i], 22f, UITheme.White, TextAlignmentOptions.Center, 2f, FontStyles.Bold, UIFontRole.Mono);
            Anchor((RectTransform)k.transform, new Vector2(0.5f, 0.5f), new Vector2(x, -10f), new Vector2(190f, 28f));
        }

        CountdownView view = layer.gameObject.AddComponent<CountdownView>();
        SetRef(view, "panel", panel);
        SetRef(view, "eyebrow", eyebrow);
        SetRef(view, "numeral", numeral);
        SetList(view, "pips", pips.ConvertAll(p => (Object)p));
        return view;
    }

    // ------------------------------------------------------------------ checkpoint popup

    private static CheckpointPopup BuildCheckpointPopup(RectTransform root)
    {
        RectTransform layer = Layer(root, "CheckpointPopup", false, out UIPanel panel);
        SetValue(panel, "riseDistance", 26f);

        // The reference sits this overlay on an almost black scene. This level is bright and the
        // popup must not dim gameplay, so it carries its own local plate instead of a scrim.
        Image plate = Img(layer, "Plate", new Color(0.03f, 0.035f, 0.045f, 0.72f));
        Anchor((RectTransform)plate.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, -10f), new Vector2(1040f, 480f));

        RectTransform banner = PanelBox(layer, "Banner", new Vector2(0.5f, 0.5f), new Vector2(0f, 190f), new Vector2(430f, 52f),
            new Color(0.04f, 0.11f, 0.13f, 0.95f),
            new Color(UITheme.Cyan.r, UITheme.Cyan.g, UITheme.Cyan.b, 0.55f));
        Image dot = Img(banner, "Dot", UITheme.Cyan);
        Anchor((RectTransform)dot.transform, new Vector2(0f, 0.5f), new Vector2(38f, 0f), new Vector2(10f, 10f));
        TMP_Text bannerText = Text(banner, "Text", "CHECKPOINT REACHED", 21f, UITheme.Cyan, TextAlignmentOptions.Center, UITheme.EyebrowSpacing, fontRole: UIFontRole.Mono);
        Anchor((RectTransform)bannerText.transform, new Vector2(0.5f, 0.5f), new Vector2(16f, 0f), new Vector2(400f, 30f));

        TMP_Text title = Centered(layer, "Title", "CHECKPOINT", 116f, UITheme.White, 88f, 1400f, 2f, FontStyles.Bold, UIFontRole.Display);
        TMP_Text counter = Centered(layer, "Counter", "0 / 0", 76f, UITheme.Cyan, -8f, 900f, 6f, FontStyles.Bold, UIFontRole.Display);

        RectTransform stats = Block(layer, "Stats", new Vector2(0.5f, 0.5f), new Vector2(0f, -135f), new Vector2(960f, 110f));
        StatColumn(stats, "Split", "SPLIT TIME", -300f, UITheme.Green, out _, out TMP_Text splitValue, 280f, 38f);
        StatColumn(stats, "Delta", "VS BEST", 0f, UITheme.Green, out TMP_Text deltaLabel, out TMP_Text deltaValue, 280f, 38f);
        StatColumn(stats, "Total", "TOTAL", 300f, UITheme.White, out _, out TMP_Text totalValue, 280f, 38f);
        Divider(stats, "Sep_1", new Vector2(-150f, -6f), new Vector2(1f, 66f));
        Divider(stats, "Sep_2", new Vector2(150f, -6f), new Vector2(1f, 66f));

        TMP_Text footer = Centered(layer, "Footer", "CHECKPOINT SECURED", 19f, UITheme.Dim, -218f, 900f, 8f, fontRole: UIFontRole.Mono);

        CheckpointPopup view = layer.gameObject.AddComponent<CheckpointPopup>();
        SetRef(view, "panel", panel);
        SetRef(view, "bannerText", bannerText);
        SetRef(view, "title", title);
        SetRef(view, "counter", counter);
        SetRef(view, "splitValue", splitValue);
        SetRef(view, "deltaValue", deltaValue);
        SetRef(view, "deltaLabel", deltaLabel);
        SetRef(view, "totalValue", totalValue);
        SetRef(view, "footer", footer);
        SetRef(view, "deltaColumn", deltaValue.transform.parent.gameObject);
        return view;
    }

    // ------------------------------------------------------------------ pause

    private static PauseMenuView BuildPause(RectTransform root)
    {
        RectTransform layer = Layer(root, "Pause", true, out UIPanel panel);
        Scrim(layer, UITheme.Scrim);

        TMP_Text mode = Centered(layer, "Mode", "CHECKPOINT MODE", 19f, UITheme.Cyan,
            385f, 900f, UITheme.EyebrowSpacing, fontRole: UIFontRole.Mono);
        Centered(layer, "Eyebrow", "GAME PAUSED", UITheme.Eyebrow, UITheme.Label, 335f, 900f, UITheme.EyebrowSpacing, fontRole: UIFontRole.Mono);
        Centered(layer, "Title", "PAUSE", UITheme.TitleLarge * 1.35f, UITheme.White, 235f, 1200f, 2f, FontStyles.Bold, UIFontRole.Display);
        Divider(layer, "Divider", new Vector2(0f, 150f), new Vector2(560f, 1f));

        RectTransform stats = Block(layer, "Stats", new Vector2(0.5f, 0.5f), new Vector2(0f, 78f), new Vector2(960f, 100f));
        StatColumn(stats, "Elapsed", "ELAPSED", -290f, UITheme.White, out _, out TMP_Text elapsed, 270f, 40f);
        StatColumn(stats, "Checkpoint", "CHECKPOINT", 0f, UITheme.White, out _, out TMP_Text cp, 270f, 40f);
        StatColumn(stats, "Best", "BEST TIME", 290f, UITheme.Cyan, out _, out TMP_Text best, 270f, 40f);
        Divider(stats, "Sep_1", new Vector2(-145f, -8f), new Vector2(1f, 64f));
        Divider(stats, "Sep_2", new Vector2(145f, -8f), new Vector2(1f, 64f));

        string[] names = { "Resume", "Restart", "LevelSelect", "MainMenu" };
        string[] captions = { "RESUME", "RESTART RUN", "LEVEL SELECT", "MAIN MENU" };
        Button[] buttons = new Button[4];
        for (int i = 0; i < 4; i++)
        {
            buttons[i] = Btn(layer, names[i], captions[i], new Vector2(0f, -30f - i * 94f), new Vector2(490f, 88f),
                MenuButtonVisual.Style.Outline, UITheme.Cyan, TextAlignmentOptions.Left);
        }

        // Filled at runtime from the scene's LevelInfo, never baked in here.
        TMP_Text footer = Centered(layer, "Footer", string.Empty, 18f, UITheme.Dim, -430f, 900f, 8f, fontRole: UIFontRole.Mono);

        PauseMenuView view = layer.gameObject.AddComponent<PauseMenuView>();
        SetRef(view, "footer", footer);
        SetRef(view, "panel", panel);
        SetRef(view, "modeValue", mode);
        SetRef(view, "elapsedValue", elapsed);
        SetRef(view, "checkpointValue", cp);
        SetRef(view, "bestValue", best);
        SetRef(view, "resumeButton", buttons[0]);
        SetRef(view, "restartButton", buttons[1]);
        SetRef(view, "levelSelectButton", buttons[2]);
        SetRef(view, "mainMenuButton", buttons[3]);
        return view;
    }

    // ------------------------------------------------------------------ death recovery

    private static DeathRecoveryView BuildDeathRecovery(RectTransform root)
    {
        RectTransform layer = Layer(root, "DeathRecovery", false, out UIPanel panel);
        Scrim(layer, UITheme.ScrimLight);

        RectTransform pill = PanelBox(layer, "ModePill", new Vector2(0.5f, 0.5f),
            new Vector2(0f, 250f), new Vector2(440f, 52f),
            new Color(0.025f, 0.10f, 0.12f, 0.94f),
            new Color(UITheme.Cyan.r, UITheme.Cyan.g, UITheme.Cyan.b, 0.55f));
        TMP_Text eyebrow = Text(pill, "Text", "CHECKPOINT MODE", 21f, UITheme.Cyan,
            TextAlignmentOptions.Center, UITheme.EyebrowSpacing, fontRole: UIFontRole.Mono);
        Anchor((RectTransform)eyebrow.transform, new Vector2(0.5f, 0.5f), Vector2.zero,
            new Vector2(410f, 30f));

        TMP_Text headline = Centered(layer, "Headline", "RECOVERING", 138f, UITheme.Orange,
            90f, 1500f, 4f, FontStyles.Bold, UIFontRole.Display);
        Divider(layer, "Divider", new Vector2(0f, -16f), new Vector2(660f, 1f));

        TMP_Text detail = Centered(layer, "Detail", "RETURNING TO CHECKPOINT 0 / 0", 27f,
            UITheme.White, -92f, 1200f, 4f, fontRole: UIFontRole.Mono);

        RectTransform reason = PanelBox(layer, "Reason", new Vector2(0.5f, 0.5f),
            new Vector2(0f, -205f), new Vector2(760f, 110f),
            UITheme.PanelFill, UITheme.PanelBorder);
        TMP_Text reasonLabel = Text(reason, "Label", "RECOVERY TRIGGER", 18f, UITheme.Orange,
            TextAlignmentOptions.TopLeft, UITheme.LabelSpacing, fontRole: UIFontRole.Mono);
        Anchor((RectTransform)reasonLabel.transform, new Vector2(0f, 1f),
            new Vector2(28f, -18f), new Vector2(700f, 24f));
        TMP_Text reasonValue = Text(reason, "Value", string.Empty, 22f, UITheme.Label,
            TextAlignmentOptions.TopLeft, 1f, fontRole: UIFontRole.Mono);
        Anchor((RectTransform)reasonValue.transform, new Vector2(0f, 1f),
            new Vector2(28f, -50f), new Vector2(700f, 36f));

        DeathRecoveryView view = layer.gameObject.AddComponent<DeathRecoveryView>();
        SetRef(view, "panel", panel);
        SetRef(view, "eyebrow", eyebrow);
        SetRef(view, "headline", headline);
        SetRef(view, "detail", detail);
        SetRef(view, "reasonValue", reasonValue);
        return view;
    }

    // ------------------------------------------------------------------ level complete

    private static LevelCompleteView BuildLevelComplete(RectTransform root)
    {
        RectTransform layer = Layer(root, "LevelComplete", true, out UIPanel panel);
        Scrim(layer, UITheme.Scrim);

        TMP_Text mode = Centered(layer, "Mode", "CHECKPOINT MODE", 19f, UITheme.Cyan,
            455f, 900f, UITheme.EyebrowSpacing, fontRole: UIFontRole.Mono);
        Centered(layer, "Eyebrow", "STAGE CLEARED", UITheme.Eyebrow, UITheme.Cyan, 405f, 900f, UITheme.EyebrowSpacing, fontRole: UIFontRole.Mono);
        // Placeholders only - LevelCompleteView.Bind writes the real strings from LevelInfo.
        TMP_Text name = Centered(layer, "StageName", "LEVEL", 104f, UITheme.White, 310f, 1600f, 2f, FontStyles.Bold, UIFontRole.Display);
        TMP_Text sub = Centered(layer, "Subtitle", string.Empty, 24f, UITheme.Label, 232f, 900f, 4f, fontRole: UIFontRole.Mono);

        RectTransform starRow = Block(layer, "Stars", new Vector2(0.5f, 0.5f), new Vector2(0f, 170f), new Vector2(300f, 50f));
        List<Image> stars = new List<Image>();
        for (int i = 0; i < 3; i++)
        {
            // Diamonds rather than stars: LiberationSans SDF has no star glyph, and a rotated
            // quad renders identically at every resolution without shipping a sprite.
            Image s = Img(starRow, $"Star_{i + 1}", UITheme.Cyan);
            RectTransform rt = (RectTransform)s.transform;
            Anchor(rt, new Vector2(0.5f, 0.5f), new Vector2((i - 1) * 62f, 0f), new Vector2(30f, 30f));
            rt.localRotation = Quaternion.Euler(0f, 0f, 45f);
            stars.Add(s);
        }

        string[] labels = { "FINISH TIME", "PERSONAL BEST", "CHECKPOINTS", "DEATHS", "MAX SPEED" };
        TMP_Text[] values = new TMP_Text[5];
        TMP_Text[] notes = new TMP_Text[5];
        for (int i = 0; i < 5; i++)
        {
            RectTransform row = PanelBox(layer, $"Row_{i}", new Vector2(0.5f, 0.5f),
                new Vector2(0f, 90f - i * 84f), new Vector2(700f, 80f),
                i % 2 == 0 ? UITheme.PanelFill : UITheme.PanelFillSoft, UITheme.PanelBorder);

            TMP_Text l = Text(row, "Label", labels[i], 20f, UITheme.Label, TextAlignmentOptions.TopLeft, 6f, fontRole: UIFontRole.Mono);
            Anchor((RectTransform)l.transform, new Vector2(0f, 1f), new Vector2(28f, -18f), new Vector2(420f, 24f));

            notes[i] = Text(row, "Note", "", 19f, UITheme.Green, TextAlignmentOptions.TopLeft, 1f, fontRole: UIFontRole.Mono);
            Anchor((RectTransform)notes[i].transform, new Vector2(0f, 1f), new Vector2(28f, -44f), new Vector2(420f, 24f));

            values[i] = Text(row, "Value", "-", 42f, UITheme.White, TextAlignmentOptions.Right, 0f, FontStyles.Bold, UIFontRole.Display);
            Anchor((RectTransform)values[i].transform, new Vector2(1f, 0.5f), new Vector2(-28f, 0f), new Vector2(320f, 56f));
        }

        Button replay = Btn(layer, "Replay", "REPLAY", new Vector2(-180f, -370f), new Vector2(330f, 78f),
            MenuButtonVisual.Style.Primary, UITheme.CyanBright, TextAlignmentOptions.Center);
        Button select = Btn(layer, "LevelSelect", "LEVEL SELECT", new Vector2(180f, -370f), new Vector2(330f, 78f),
            MenuButtonVisual.Style.Outline, UITheme.Cyan, TextAlignmentOptions.Center);
        Button menu = Btn(layer, "MainMenu", "MAIN MENU", new Vector2(0f, -456f), new Vector2(700f, 62f),
            MenuButtonVisual.Style.Ghost, UITheme.Cyan, TextAlignmentOptions.Center);

        LevelCompleteView view = layer.gameObject.AddComponent<LevelCompleteView>();
        SetRef(view, "panel", panel);
        SetRef(view, "modeValue", mode);
        SetRef(view, "stageName", name);
        SetRef(view, "stageSubtitle", sub);
        SetList(view, "stars", stars.ConvertAll(s => (Object)s));
        SetRef(view, "finishTimeValue", values[0]);
        SetRef(view, "finishTimeNote", notes[0]);
        SetRef(view, "personalBestValue", values[1]);
        SetRef(view, "personalBestNote", notes[1]);
        SetRef(view, "checkpointsValue", values[2]);
        SetRef(view, "checkpointsNote", notes[2]);
        SetRef(view, "deathsValue", values[3]);
        SetRef(view, "deathsNote", notes[3]);
        SetRef(view, "maxSpeedValue", values[4]);
        SetRef(view, "maxSpeedNote", notes[4]);
        SetRef(view, "replayButton", replay);
        SetRef(view, "levelSelectButton", select);
        SetRef(view, "mainMenuButton", menu);
        return view;
    }

    // ------------------------------------------------------------------ controller wiring

    private static void WireController(GameObject rootGo, GameplayHUD hud, CountdownView countdown,
        CheckpointPopup popup, PauseMenuView pause, DeathRecoveryView deathRecovery,
        LevelCompleteView complete)
    {
        GameManager game = Object.FindFirstObjectByType<GameManager>();
        RunTimer timer = Object.FindFirstObjectByType<RunTimer>();
        CheckpointManager checkpoints = Object.FindFirstObjectByType<CheckpointManager>();

        if (game == null)
        {
            Debug.LogError("[UI] No GameManager in the scene - UI will not bind.");
            return;
        }

        // Stats tracker and level identity live beside the other run systems, not on the UI.
        RunStatsTracker stats = Object.FindFirstObjectByType<RunStatsTracker>();
        if (stats == null)
        {
            stats = game.gameObject.AddComponent<RunStatsTracker>();
        }

        LevelInfo levelInfo = Object.FindFirstObjectByType<LevelInfo>();
        if (levelInfo == null)
        {
            levelInfo = game.gameObject.AddComponent<LevelInfo>();
            Debug.LogWarning($"[UI] No LevelInfo found - added one to '{game.gameObject.name}'. " +
                             "Set its Display Name and Subtitle for this scene.");
        }

        PlayerFreezeController player = Object.FindFirstObjectByType<PlayerFreezeController>();
        SetRef(stats, "playerController", player != null ? player.GetComponent<CharacterController>() : null);
        SetRef(stats, "checkpoints", checkpoints);
        SetRef(stats, "levelInfo", levelInfo);
        SetRef(hud, "stats", stats);

        GameplayUIController controller = rootGo.AddComponent<GameplayUIController>();
        SetRef(controller, "game", game);
        SetRef(controller, "runTimer", timer);
        SetRef(controller, "checkpoints", checkpoints);
        SetRef(controller, "stats", stats);
        SetRef(controller, "levelInfo", levelInfo);
        SetRef(controller, "hud", hud);
        SetRef(controller, "countdown", countdown);
        SetRef(controller, "checkpointPopup", popup);
        SetRef(controller, "pauseMenu", pause);
        SetRef(controller, "deathRecovery", deathRecovery);
        SetRef(controller, "levelComplete", complete);
    }

    // ------------------------------------------------------------------ primitives

    private static RectTransform Layer(RectTransform parent, string name, bool interactable, out UIPanel panel)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
        RectTransform rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        Stretch(rt);

        panel = go.AddComponent<UIPanel>();
        SetRef(panel, "group", go.GetComponent<CanvasGroup>());
        SetValue(panel, "interactable", interactable);
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

    private static RectTransform Block(RectTransform parent, string name, Vector2 anchor, Vector2 position, Vector2 size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        Anchor(rt, anchor, position, size);
        return rt;
    }

    /// <summary>
    /// Soft dark plate behind a HUD corner. The mockups put this HUD language on a near-black
    /// scene; this level has a bright sky, and the muted grey field labels are invisible against
    /// it without a backing. Kept low-alpha so it stays unobtrusive.
    /// </summary>
    private static Image HudPlate(RectTransform parent, Vector2 anchor, Vector2 offset, Vector2 size)
    {
        Image plate = Img(parent, "Plate", new Color(0.02f, 0.025f, 0.03f, 0.42f));
        Anchor((RectTransform)plate.transform, anchor, offset, size);
        plate.transform.SetAsFirstSibling();
        return plate;
    }

    private static Image Scrim(RectTransform parent, Color colour)
    {
        Image img = Img(parent, "Scrim", colour);
        Stretch((RectTransform)img.transform);
        return img;
    }

    private static Image Img(RectTransform parent, string name, Color colour)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Image img = go.GetComponent<Image>();
        img.color = colour;
        img.raycastTarget = false;      // decoration never eats clicks
        return img;
    }

    /// <summary>Bordered box: an outer border Image with an inset fill Image on top.</summary>
    private static RectTransform PanelBox(RectTransform parent, string name, Vector2 anchor, Vector2 position,
        Vector2 size, Color fill, Color border)
    {
        RectTransform rt = Block(parent, name, anchor, position, size);

        Image borderImg = Img(rt, "Border", border);
        Stretch((RectTransform)borderImg.transform);

        Image fillImg = Img(rt, "Fill", fill);
        RectTransform fillRt = (RectTransform)fillImg.transform;
        Stretch(fillRt);
        fillRt.offsetMin = new Vector2(1f, 1f);
        fillRt.offsetMax = new Vector2(-1f, -1f);

        return rt;
    }

    private static Image Divider(RectTransform parent, string name, Vector2 position, Vector2 size)
    {
        Image img = Img(parent, name, new Color(UITheme.Cyan.r, UITheme.Cyan.g, UITheme.Cyan.b, 0.28f));
        Anchor((RectTransform)img.transform, new Vector2(0.5f, 0.5f), position, size);
        return img;
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
        t.textWrappingMode = TextWrappingModes.Normal;
        t.overflowMode = TextOverflowModes.Overflow;
        return t;
    }

    private static TMP_Text Centered(RectTransform parent, string name, string content, float size, Color colour,
        float y, float width, float spacing, FontStyles style = FontStyles.Normal,
        UIFontRole fontRole = UIFontRole.Body)
    {
        TMP_Text t = Text(parent, name, content, size, colour, TextAlignmentOptions.Center, spacing, style, fontRole);
        Anchor((RectTransform)t.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, y), new Vector2(width, size * 1.35f));
        return t;
    }

    /// <summary>
    /// One label-over-value stat column. Width is explicit because the mockup's values
    /// ("01:56.67" at bold 40pt) are wider than a naive column and will collide with their
    /// neighbours if the column is sized to the label instead.
    /// </summary>
    private static void StatColumn(RectTransform parent, string name, string label, float x, Color valueColour,
        out TMP_Text labelText, out TMP_Text valueText, float width = 270f, float valueSize = UITheme.StatValue)
    {
        RectTransform col = Block(parent, name, new Vector2(0.5f, 0.5f), new Vector2(x, 0f), new Vector2(width, 100f));
        labelText = Text(col, "Label", label, UITheme.StatLabel, UITheme.Label, TextAlignmentOptions.Center, UITheme.LabelSpacing, fontRole: UIFontRole.Mono);
        Anchor((RectTransform)labelText.transform, new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(width, 26f));
        valueText = Text(col, "Value", "-", valueSize, valueColour, TextAlignmentOptions.Center, 0f, FontStyles.Bold, UIFontRole.Display);
        Anchor((RectTransform)valueText.transform, new Vector2(0.5f, 1f), new Vector2(0f, -34f), new Vector2(width, 60f));
    }

    private static Button Btn(RectTransform parent, string name, string caption, Vector2 position, Vector2 size,
        MenuButtonVisual.Style style, Color accent, TextAlignmentOptions align)
    {
        RectTransform rt = Block(parent, name, new Vector2(0.5f, 0.5f), position, size);

        Image border = Img(rt, "Border", UITheme.PanelBorder);
        Stretch((RectTransform)border.transform);

        Image fill = Img(rt, "Fill", UITheme.ButtonIdle);
        RectTransform fillRt = (RectTransform)fill.transform;
        Stretch(fillRt);
        fillRt.offsetMin = new Vector2(1f, 1f);
        fillRt.offsetMax = new Vector2(-1f, -1f);
        fill.raycastTarget = true;                       // the click surface

        TMP_Text label = Text(rt, "Label", caption, UITheme.ButtonLabel, UITheme.White, align, 4f, FontStyles.Bold, UIFontRole.Display);
        RectTransform labelRt = (RectTransform)label.transform;
        Stretch(labelRt);
        labelRt.offsetMin = new Vector2(align == TextAlignmentOptions.Left ? 36f : 12f, 0f);
        labelRt.offsetMax = new Vector2(-12f, 0f);

        Button button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = fill;
        button.transition = Selectable.Transition.None;   // MenuButtonVisual owns the feedback

        MenuButtonVisual visual = rt.gameObject.AddComponent<MenuButtonVisual>();
        SetRef(visual, "background", fill);
        SetRef(visual, "border", border);
        SetRef(visual, "label", label);
        SetValue(visual, "style", (int)style);
        SetColor(visual, "accent", accent);

        return button;
    }

    // ------------------------------------------------------------------ serialization helpers

    private static void SetRef(Object target, string field, Object value)
    {
        SerializedObject so = new SerializedObject(target);
        SerializedProperty p = so.FindProperty(field);
        if (p == null)
        {
            Debug.LogError($"[UI] '{field}' not found on {target.GetType().Name}");
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
            Debug.LogError($"[UI] '{field}' not found on {target.GetType().Name}");
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
            Debug.LogError($"[UI] '{field}' not found on {target.GetType().Name}");
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
