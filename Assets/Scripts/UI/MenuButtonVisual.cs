using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Hover and press feedback for a menu button. Runs on unscaled time so it stays responsive
/// while paused, and drives colour on the button's own background Image rather than relying on
/// Selectable transitions, which cannot animate the label alongside the fill.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public sealed class MenuButtonVisual : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    public enum Style
    {
        /// <summary>Dark translucent bar with a border. Pause menu list.</summary>
        Outline,

        /// <summary>Solid accent fill with dark text. TRY AGAIN / REPLAY.</summary>
        Primary,

        /// <summary>Borderless, label only. LEVEL SELECT on Level Complete.</summary>
        Ghost
    }

    [SerializeField] private Image background;
    [SerializeField] private Image border;
    [SerializeField] private TMP_Text label;
    [SerializeField] private Style style = Style.Outline;

    [Tooltip("Accent used by Primary fill and by the hover edge of Outline/Ghost.")]
    [SerializeField] private Color accent = UITheme.Cyan;

    private Color fillIdle, fillHover, textIdle, textHover, borderIdle, borderHover;
    private bool hovered, pressed;

    private void Awake()
    {
        // Any localScale left over from an older build would resample this button's TMP label
        // instead of re-rendering it, which is exactly the softness this pass removes.
        transform.localScale = Vector3.one;
        BuildPalette();
        Apply(1f, true);
    }

    private void OnEnable()
    {
        hovered = false;
        pressed = false;
        Apply(1f, true);
    }

    private void BuildPalette()
    {
        switch (style)
        {
            case Style.Primary:
                fillIdle = accent;
                fillHover = Color.Lerp(accent, Color.white, 0.22f);
                textIdle = new Color32(0x08, 0x0A, 0x0C, 0xFF);
                textHover = textIdle;
                borderIdle = new Color(accent.r, accent.g, accent.b, 0f);
                borderHover = borderIdle;
                break;

            case Style.Ghost:
                fillIdle = new Color(0f, 0f, 0f, 0f);
                fillHover = new Color(accent.r, accent.g, accent.b, 0.10f);
                textIdle = UITheme.Dim;
                textHover = UITheme.White;
                borderIdle = new Color(UITheme.PanelBorder.r, UITheme.PanelBorder.g, UITheme.PanelBorder.b, 0.5f);
                borderHover = new Color(accent.r, accent.g, accent.b, 0.55f);
                break;

            default:
                fillIdle = UITheme.ButtonIdle;
                fillHover = UITheme.ButtonHover;
                textIdle = UITheme.White;
                textHover = Color.white;
                borderIdle = UITheme.PanelBorder;
                borderHover = new Color(accent.r, accent.g, accent.b, 0.75f);
                break;
        }
    }

    private void Update()
    {
        // Lerp toward the target every frame rather than coroutine-per-event: a fast mouse
        // sweeping across four buttons would otherwise leave stale coroutines fighting.
        float step = Time.unscaledDeltaTime / Mathf.Max(0.0001f, UITheme.ButtonFade);
        Apply(step, false);
    }

    /// <summary>A non-interactable button must not light up under the cursor.</summary>
    private bool Interactive
    {
        get
        {
            Selectable selectable = GetComponent<Selectable>();
            return selectable == null || selectable.IsInteractable();
        }
    }

    private void Apply(float step, bool immediate)
    {
        bool live = Interactive;
        float targetBlend = hovered && live ? 1f : 0f;
        float dim = live ? 1f : 0.4f;

        if (background != null)
        {
            Color target = Color.Lerp(fillIdle, fillHover, targetBlend);
            if (live && pressed)
            {
                target = Color.Lerp(target, Color.black, 0.25f);
            }

            target.a *= dim;
            background.color = immediate ? target : Color.Lerp(background.color, target, step);
        }

        if (border != null)
        {
            Color target = Color.Lerp(borderIdle, borderHover, targetBlend);
            target.a *= dim;
            border.color = immediate ? target : Color.Lerp(border.color, target, step);
        }

        if (label != null)
        {
            Color target = Color.Lerp(textIdle, textHover, targetBlend);
            target.a *= dim;
            label.color = immediate ? target : Color.Lerp(label.color, target, step);
        }

        // Hover and press read entirely through colour. The mockups show no scale response on
        // any button, and a fractional localScale on a rect that parents a TMP label is the
        // single most reliable way to make SDF text look blurry.
    }

    public void OnPointerEnter(PointerEventData eventData) => hovered = true;

    public void OnPointerExit(PointerEventData eventData)
    {
        hovered = false;
        pressed = false;
    }

    public void OnPointerDown(PointerEventData eventData) => pressed = true;

    public void OnPointerUp(PointerEventData eventData) => pressed = false;
}
