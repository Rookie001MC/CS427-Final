using UnityEngine;

/// <summary>
/// Single source of truth for the gameplay UI's visual language, taken from the reference
/// mockups in C:\Game_Final\UI. Kept as plain static data so both the runtime views and the
/// editor builder read exactly the same values.
/// </summary>
public static class UITheme
{
    // ---- palette ------------------------------------------------------------------
    /// <summary>Primary accent. Checkpoint counters, stage-cleared eyebrow, HUD timer.</summary>
    public static readonly Color Cyan = new Color32(0x17, 0xD4, 0xF0, 0xFF);

    /// <summary>Saturated cyan reserved for solid primary buttons (REPLAY).</summary>
    public static readonly Color CyanBright = new Color32(0x00, 0xE5, 0xFF, 0xFF);

    /// <summary>Failure accent. Game Over only.</summary>
    public static readonly Color Orange = new Color32(0xFF, 0x6B, 0x28, 0xFF);

    /// <summary>Positive delta / "all cleared" confirmations.</summary>
    public static readonly Color Green = new Color32(0x33, 0xDD, 0x7F, 0xFF);

    public static readonly Color White = new Color32(0xF0, 0xF2, 0xF5, 0xFF);

    /// <summary>Muted small-caps field labels.</summary>
    public static readonly Color Label = new Color32(0x8B, 0x94, 0xA3, 0xFF);

    /// <summary>
    /// Dimmer still - hint lines, "continuing in..." text. Lifted from the mockups' #4E5560:
    /// that value is legible in a browser on a calibrated monitor, but at 1280x720 the mono
    /// face is only ~16 real pixels tall and the strokes disappear into the scrim.
    /// </summary>
    public static readonly Color Dim = new Color32(0x6B, 0x74, 0x82, 0xFF);

    public static readonly Color PanelFill = new Color32(0x14, 0x16, 0x1A, 0xF0);
    public static readonly Color PanelFillSoft = new Color32(0x1A, 0x1D, 0x21, 0xD9);
    public static readonly Color PanelBorder = new Color32(0x2A, 0x2E, 0x35, 0xFF);

    /// <summary>Full-screen dim behind modal panels.</summary>
    public static readonly Color Scrim = new Color32(0x07, 0x08, 0x0A, 0xDB);

    /// <summary>Lighter dim used during the countdown, so the level stays readable.</summary>
    public static readonly Color ScrimLight = new Color32(0x07, 0x08, 0x0A, 0x8C);

    public static readonly Color ButtonIdle = new Color32(0x17, 0x1A, 0x1F, 0xE6);
    public static readonly Color ButtonHover = new Color32(0x23, 0x28, 0x30, 0xF2);

    // ---- type scale ---------------------------------------------------------------
    //
    // All sizes are in canvas reference units at 1920x1080. They were derived by measuring ink
    // cap-heights in the reference PNGs (which render at ~1998px wide, so ref px * 0.961 gives
    // canvas units) and dividing by Anton's cap ratio of 0.859.
    //
    // Two deliberate departures from a literal transcription of the mockups:
    //
    //  * Display headlines land ~8% under their measured reference cap height, which leaves room
    //    for the longest level name the project ships without auto-sizing having to engage.
    //
    //  * Mono labels sit ABOVE their measured reference size, on a floor of 22. The mockups are
    //    browser screenshots read at desk distance; this UI is read at 1280x720, where the
    //    CanvasScaler multiplies by 0.667. The floor keeps every cap at >=10 real pixels,
    //    which is what UITypographyAudit enforces - see MinimumCapPixels there.

    // Display - Anton. Never carries FontStyles.Bold: it is a single heavy cut already at its
    // ink ceiling, and TMP's faux-bold smears an SDF that has no headroom left.
    //
    // These numbers read small next to a normal UI scale because Anton's cap height is 0.859em -
    // an unusually large share of the em. A 96pt Anton cap matches a 118pt Barlow Condensed cap.
    /// <summary>Countdown "3 / 2 / 1". The single largest glyph in the game.</summary>
    public const float DisplayCountdown = 228f;

    /// <summary>Countdown "GO!". Shorter string, so it reads at a smaller size.</summary>
    public const float DisplayGo = 137f;

    /// <summary>Main-menu wordmark lines: SKYBOUND over TRIALS.</summary>
    public const float TitleHero = 140f;

    /// <summary>CHECKPOINT, RECOVERING, RUN FAILED.</summary>
    public const float TitleHuge = 114f;

    /// <summary>PAUSE.</summary>
    public const float TitlePause = 106f;

    /// <summary>Stage names on Level Complete and the loading screen.</summary>
    public const float TitleLarge = 96f;

    /// <summary>CHOOSE YOUR / DISTRICT on level select.</summary>
    public const float TitleMedium = 91f;

    /// <summary>PLAY / LEVELS / STATS / QUIT rows.</summary>
    public const float MenuRow = 81f;

    /// <summary>"3 / 6" checkpoint counter under the popup title.</summary>
    public const float CounterLarge = 78f;

    /// <summary>Level name inside the run-mode modal.</summary>
    public const float HeadingSmall = 49f;

    /// <summary>HUD clock.</summary>
    public const float TimerValue = 52f;

    /// <summary>HUD checkpoint counter, current-zone value, personal-best figures.</summary>
    public const float StatValueLarge = 46f;

    /// <summary>Stat readouts inside panels and popup columns.</summary>
    public const float StatValue = 37f;

    /// <summary>Level-card titles.</summary>
    public const float CardTitle = 28f;

    public const float ButtonLabel = 28f;
    public const float ButtonLabelSmall = 24f;

    // Mono - RobotoMono-Medium. Every label, eyebrow, caption and subtitle in the mockups.
    // A real 500 weight, which is what stops these reading thin at 1280x720.
    /// <summary>GET READY, STAGE CLEARED, URBAN VELOCITY, LOADING STAGE.</summary>
    public const float Eyebrow = 28f;

    /// <summary>Small-caps field labels: ELAPSED, FINISH TIME, TIME, CHECKPOINT.</summary>
    public const float StatLabel = 24f;

    /// <summary>Tighter label variant for the two-column strip inside a level card.</summary>
    public const float LabelSmall = 22f;

    /// <summary>Row captions: CONTINUE RUN, SELECT STAGE, RUNNER PROFILE.</summary>
    public const float Caption = 24f;

    /// <summary>Running prose. The tagline, rule descriptions, failure tips.</summary>
    public const float Body = 26f;

    /// <summary>Stage subtitles: "Industrial Zone - Stage 01".</summary>
    public const float Subtitle = 30f;

    /// <summary>The loading screen sets its subtitle far larger than any other screen does.</summary>
    public const float SubtitleLarge = 42f;

    /// <summary>Control-hint captions under the countdown.</summary>
    public const float Hint = 24f;

    /// <summary>Key names under those captions.</summary>
    public const float KeyCap = 28f;

    /// <summary>
    /// The smallest point size any mono label may use: 22pt of Roboto Mono is a 15.6-unit cap,
    /// or 10.4 real pixels at 1280x720. Display text is checked on rendered cap height instead,
    /// since Anton's cap ratio makes its point sizes incomparable - see
    /// <see cref="UITypographyAudit"/>, which enforces both.
    /// </summary>
    public const float MinimumSize = 22f;

    // ---- tracking -----------------------------------------------------------------
    // TMP characterSpacing is expressed in hundredths of an em.

    /// <summary>
    /// Letter-spacing on small-caps field labels. Pulled back from 14 because Roboto Mono's
    /// 0.600em advance is already a fifth wider than the Lekton it replaced, so the same numeric
    /// tracking read looser than the mockups and inflated every label box.
    /// </summary>
    public const float LabelSpacing = 11f;

    /// <summary>Wide tracking on eyebrows, matching the mockups' ~0.30em of optical space.</summary>
    public const float EyebrowSpacing = 20f;

    /// <summary>Anton is tight by design; caps need a touch of air.</summary>
    public const float DisplaySpacing = 2f;

    // ---- line spacing -------------------------------------------------------------
    /// <summary>
    /// Step between stacked display lines (SKYBOUND/TRIALS, CHOOSE YOUR/DISTRICT), as a multiple
    /// of point size. The mockups leave a gap of about 0.14 of a cap height between the bottom of
    /// one line's caps and the top of the next; at Anton's 0.859em cap that lands here. Far
    /// tighter than the face's own 1.505em line box, which is why the two lines are separate
    /// objects at explicit offsets rather than one wrapped string.
    /// </summary>
    public const float DisplayLineStep = 0.98f;

    // ---- motion -------------------------------------------------------------------
    /// <summary>Panel fade duration. All UI motion runs on unscaled time.</summary>
    public const float PanelFade = 0.20f;

    /// <summary>Button hover / press response.</summary>
    public const float ButtonFade = 0.12f;
}
