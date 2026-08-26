using System.Collections;
using UnityEngine;

/// <summary>
/// Fades a CanvasGroup in and out on <b>unscaled</b> time, so panels still animate while the
/// game is paused with Time.timeScale = 0.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public sealed class UIPanel : MonoBehaviour
{
    [SerializeField] private CanvasGroup group;
    [SerializeField] private float fadeDuration = UITheme.PanelFade;

    [Tooltip("Blocks clicks and shows the cursor while visible.")]
    [SerializeField] private bool interactable;

    [Tooltip("Slides up slightly while fading in.")]
    [SerializeField] private float riseDistance;

    private RectTransform rect;
    private Vector2 baseAnchoredPosition;
    private Coroutine routine;

    public bool IsVisible { get; private set; }

    private void Awake()
    {
        if (group == null)
        {
            group = GetComponent<CanvasGroup>();
        }

        rect = (RectTransform)transform;
        baseAnchoredPosition = rect.anchoredPosition;
        ApplyImmediate(false);
    }

    /// <summary>Shows or hides with the standard fade.</summary>
    public void SetVisible(bool visible)
    {
        if (IsVisible == visible)
        {
            return;
        }

        IsVisible = visible;

        if (!gameObject.activeInHierarchy)
        {
            ApplyImmediate(visible);
            return;
        }

        if (routine != null)
        {
            StopCoroutine(routine);
        }

        routine = StartCoroutine(Fade(visible));
    }

    /// <summary>Snaps to a state with no animation.</summary>
    public void ApplyImmediate(bool visible)
    {
        IsVisible = visible;

        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }

        group.alpha = visible ? 1f : 0f;
        group.blocksRaycasts = visible && interactable;
        group.interactable = visible && interactable;

        if (rect != null)
        {
            rect.anchoredPosition = baseAnchoredPosition;
        }
    }

    private IEnumerator Fade(bool visible)
    {
        float from = group.alpha;
        float to = visible ? 1f : 0f;
        float elapsed = 0f;

        // Raycasts go live at the start of a show and die at the start of a hide, so a panel on
        // its way out never eats a click meant for whatever is underneath it.
        group.blocksRaycasts = visible && interactable;
        group.interactable = visible && interactable;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / fadeDuration);
            t = t * t * (3f - 2f * t);                      // smoothstep

            group.alpha = Mathf.Lerp(from, to, t);

            if (rect != null && riseDistance != 0f)
            {
                float offset = Mathf.Lerp(visible ? riseDistance : 0f, visible ? 0f : riseDistance, t);
                rect.anchoredPosition = baseAnchoredPosition + new Vector2(0f, offset);
            }

            yield return null;
        }

        group.alpha = to;

        if (rect != null)
        {
            rect.anchoredPosition = baseAnchoredPosition;
        }

        routine = null;
    }
}
