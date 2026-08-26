using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Visual layer for the countdown. Owns no timing: <see cref="GameManager"/> raises a tick per
/// step and this only animates what it is told, so the existing gate on movement and the run
/// clock is untouched.
/// </summary>
public sealed class CountdownView : MonoBehaviour
{
    [SerializeField] private UIPanel panel;
    [SerializeField] private TMP_Text eyebrow;
    [SerializeField] private TMP_Text numeral;
    [SerializeField] private List<Image> pips = new List<Image>();

    [SerializeField] private string goLabel = "GO!";

    private Coroutine punch;
    private int ticksSeen;

    /// <summary>Resets pip state for a fresh countdown.</summary>
    public void Begin(GameMode mode)
    {
        ticksSeen = 0;

        for (int i = 0; i < pips.Count; i++)
        {
            if (pips[i] != null)
            {
                pips[i].color = new Color(UITheme.Cyan.r, UITheme.Cyan.g, UITheme.Cyan.b, 0.22f);
            }
        }

        if (eyebrow != null)
        {
            eyebrow.text = RunModeRules.For(mode).DisplayName + "  //  GET READY";
            eyebrow.color = UITheme.Cyan;
        }

        if (panel != null)
        {
            panel.SetVisible(true);
        }
    }

    /// <summary>Handles one countdown label: "3", "2", "1" then "GO!".</summary>
    public void Tick(string label)
    {
        bool isGo = label == goLabel;

        if (numeral != null)
        {
            numeral.text = label;
            numeral.color = isGo ? UITheme.Cyan : UITheme.White;
            numeral.fontSize = isGo ? UITheme.TitleHuge * 1.15f : UITheme.TitleHuge * 1.9f;
        }

        if (isGo)
        {
            if (eyebrow != null)
            {
                eyebrow.text = string.Empty;
            }

            LightPip(pips.Count);
        }
        else
        {
            LightPip(++ticksSeen);
        }

        if (gameObject.activeInHierarchy)
        {
            if (punch != null)
            {
                StopCoroutine(punch);
            }

            punch = StartCoroutine(Punch(isGo));
        }
    }

    private void LightPip(int count)
    {
        for (int i = 0; i < pips.Count; i++)
        {
            if (pips[i] == null)
            {
                continue;
            }

            bool lit = i < count;
            pips[i].color = lit
                ? UITheme.Cyan
                : new Color(UITheme.Cyan.r, UITheme.Cyan.g, UITheme.Cyan.b, 0.22f);
        }
    }

    /// <summary>Scale-and-fade pop on each step. Unscaled so it survives a paused timescale.</summary>
    private IEnumerator Punch(bool isGo)
    {
        if (numeral == null)
        {
            yield break;
        }

        Transform t = numeral.transform;
        float duration = isGo ? 0.32f : 0.26f;
        float elapsed = 0f;

        Color full = numeral.color;
        Color start = new Color(full.r, full.g, full.b, 0f);

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(elapsed / duration);
            float ease = 1f - Mathf.Pow(1f - k, 3f);

            t.localScale = Vector3.one * Mathf.Lerp(isGo ? 0.55f : 1.55f, 1f, ease);
            numeral.color = Color.Lerp(start, full, Mathf.Clamp01(k * 3f));

            yield return null;
        }

        t.localScale = Vector3.one;
        numeral.color = full;
        punch = null;
    }

    /// <summary>Fades the whole overlay out once the run is under way.</summary>
    public void Finish()
    {
        if (gameObject.activeInHierarchy)
        {
            StartCoroutine(HideAfter(0.55f));
            return;
        }

        if (panel != null)
        {
            panel.SetVisible(false);
        }
    }

    private IEnumerator HideAfter(float delay)
    {
        yield return new WaitForSecondsRealtime(delay);

        if (panel != null)
        {
            panel.SetVisible(false);
        }
    }
}
