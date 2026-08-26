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
    public static readonly Color Label = new Color32(0x7A, 0x82, 0x90, 0xFF);

    /// <summary>Dimmer still - hint lines, "continuing in..." text.</summary>
    public static readonly Color Dim = new Color32(0x4E, 0x55, 0x60, 0xFF);

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
    public const float TitleHuge = 132f;   // FALL / DETECTED / CHECKPOINT
    public const float TitleLarge = 96f;   // PAUSE, stage name
    public const float CounterLarge = 84f; // "3 / 6"
    public const float Eyebrow = 26f;      // GET READY, STAGE CLEARED
    public const float StatValue = 44f;
    public const float StatLabel = 20f;
    public const float Body = 24f;
    public const float ButtonLabel = 30f;

    /// <summary>Letter-spacing used on all small-caps labels, matching the mockups.</summary>
    public const float LabelSpacing = 12f;
    public const float EyebrowSpacing = 16f;

    // ---- motion -------------------------------------------------------------------
    /// <summary>Panel fade duration. All UI motion runs on unscaled time.</summary>
    public const float PanelFade = 0.20f;

    /// <summary>Button hover / press response.</summary>
    public const float ButtonFade = 0.12f;
}
