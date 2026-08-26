using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Async scene load with a full-screen progress presentation.
///
/// Implemented as a persistent overlay rather than a dedicated Loading scene: it survives the
/// load via DontDestroyOnLoad, then destroys itself once the target scene is live. That keeps the
/// build to three scenes and means nothing of the menu can linger in gameplay - this object owns
/// no EventSystem and no raycaster, so it cannot duplicate either.
/// </summary>
public sealed class SceneLoader : MonoBehaviour
{
    [SerializeField] private CanvasGroup group;
    [SerializeField] private TMP_Text levelName;
    [SerializeField] private TMP_Text levelSubtitle;
    [SerializeField] private TMP_Text modeLabel;
    [SerializeField] private TMP_Text bestValue;
    [SerializeField] private TMP_Text statusLabel;
    [SerializeField] private TMP_Text percentLabel;
    [SerializeField] private TMP_Text tipLabel;
    [SerializeField] private RectTransform progressFill;
    [SerializeField] private RawImage preview;

    [SerializeField] private float fadeOut = 0.25f;

    private static SceneLoader active;

    /// <summary>True while a load is in flight, so menu input can be ignored.</summary>
    public static bool IsLoading => active != null;

    private void Awake()
    {
        // Hidden until a load starts; the object lives in the menu scene from the beginning.
        if (group != null)
        {
            group.alpha = 0f;
        }

        gameObject.SetActive(false);
    }

    /// <summary>Begins loading the scene named by <paramref name="level"/> in the selected mode.</summary>
    public void Load(LevelEntry level, GameMode mode)
    {
        // A second click while loading must not replace the run identity already in flight.
        if (active != null)
        {
            return;
        }

        if (level == null || string.IsNullOrWhiteSpace(level.SceneName) ||
            string.IsNullOrWhiteSpace(level.RecordKey))
        {
            RunSession.Clear();
            Debug.LogError("[Loader] Level entry requires a scene name and record key.", this);
            return;
        }

        if (!Application.CanStreamedLevelBeLoaded(level.SceneName))
        {
            RunSession.Clear();
            Debug.LogError($"[Loader] Scene '{level.SceneName}' is not available in the build.", this);
            return;
        }

        RunModeRules rules;
        try
        {
            rules = RunModeRules.For(mode);
        }
        catch (System.ArgumentOutOfRangeException exception)
        {
            RunSession.Clear();
            Debug.LogError($"[Loader] {exception.Message}", this);
            return;
        }

        // Selection is established only after every input has passed validation, but before the
        // overlay is detached and persisted into the gameplay scene.
        RunSession.Select(mode, level.RecordKey);
        Bind(level, mode, rules);

        active = this;
        gameObject.SetActive(true);
        transform.SetParent(null, true);
        DontDestroyOnLoad(gameObject);

        StartCoroutine(Run(level.SceneName));
    }

    private void Bind(LevelEntry level, GameMode mode, RunModeRules rules)
    {
        if (levelName != null)
        {
            levelName.text = level.DisplayName;
        }

        if (levelSubtitle != null)
        {
            levelSubtitle.text = level.Subtitle;
        }

        if (modeLabel != null)
        {
            modeLabel.text = rules.DisplayName;
        }

        if (tipLabel != null)
        {
            tipLabel.text = string.IsNullOrEmpty(level.Tip) ? string.Empty : "TIP: " + level.Tip;
        }

        if (preview != null)
        {
            preview.texture = level.Preview;
            preview.enabled = level.Preview != null;
        }

        if (bestValue != null)
        {
            bool has = RunStatsTracker.TryGetBest(level.RecordKey, mode, out float best);
            bestValue.text = has ? RunTimer.Format(best) : "--:--.--";
            bestValue.color = has ? UITheme.Cyan : UITheme.Dim;
        }

        SetProgress(0f);
        SetStatus("PREPARING");
    }

    private IEnumerator Run(string sceneName)
    {
        if (group != null)
        {
            group.alpha = 1f;
        }

        // A fresh gameplay scene must never inherit a paused clock from wherever we came from.
        Time.timeScale = 1f;

        AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);
        op.allowSceneActivation = false;

        SetStatus("STREAMING GEOMETRY");

        // Unity caps progress at 0.9 while activation is withheld, so rescale to a real 0..1.
        while (op.progress < 0.9f)
        {
            SetProgress(Mathf.Clamp01(op.progress / 0.9f));
            yield return null;
        }

        SetProgress(1f);
        SetStatus("ACTIVATING");
        op.allowSceneActivation = true;

        while (!op.isDone)
        {
            yield return null;
        }

        // One frame for the new scene's Awake/Start, so the countdown is already armed behind
        // the overlay and the player never sees an un-initialised frame.
        yield return null;

        SetStatus("READY");

        float elapsed = 0f;
        float from = group != null ? group.alpha : 1f;
        while (elapsed < fadeOut)
        {
            elapsed += Time.unscaledDeltaTime;
            if (group != null)
            {
                group.alpha = Mathf.Lerp(from, 0f, Mathf.Clamp01(elapsed / fadeOut));
            }

            yield return null;
        }

        active = null;
        Destroy(gameObject);
    }

    private void SetProgress(float t)
    {
        if (progressFill != null)
        {
            progressFill.anchorMax = new Vector2(Mathf.Clamp01(t), 1f);
            progressFill.offsetMax = new Vector2(0f, progressFill.offsetMax.y);
        }

        if (percentLabel != null)
        {
            percentLabel.text = Mathf.RoundToInt(Mathf.Clamp01(t) * 100f) + "%";
        }
    }

    private void SetStatus(string text)
    {
        if (statusLabel != null)
        {
            statusLabel.text = text;
        }
    }
}
