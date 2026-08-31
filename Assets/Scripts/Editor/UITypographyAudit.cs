using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine.TextCore;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Static checks over every TMP object the builders produce.
///
/// The typography pass has failure modes that never raise a console error and are easy to miss
/// by eye at editor zoom: a label whose cap renders under 10 real pixels at 720p, a rect left at a
/// fractional scale (which resamples an SDF quad instead of re-typesetting it), a string wider
/// than the box that holds it, or a text object that silently fell back to LiberationSans.
/// This finds all four without entering play mode.
/// </summary>
public static class UITypographyAudit
{
    /// <summary>The lowest supported resolution. Everything must stay legible here.</summary>
    private const float MinScaleFactor = 720f / 1080f;

    /// <summary>
    /// Legibility is a function of rendered cap height, not point size, and the two are not
    /// interchangeable across these families: Anton's cap is 0.859 of its em where Roboto Mono's
    /// is 0.711, so 28pt of Anton and 28pt of Roboto Mono differ by a fifth on screen. Measuring
    /// caps is what lets one floor cover all three faces.
    /// </summary>
    private const float MinCapPixels = 10f;

    private static readonly string[] Fonts =
    {
        "Anton-Regular SDF",
        "Inter_18pt-Regular SDF",
        "RobotoMono-Medium SDF"
    };

    public sealed class Finding
    {
        public string Scene;
        public string Path;
        public string Problem;

        public override string ToString() => $"  [{Scene}] {Path}\n      {Problem}";
    }

    [MenuItem("Tools/Parkour UI/Audit UI Typography")]
    public static void RunFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        string reopen = SceneManager.GetActiveScene().path;
        List<Finding> findings = new List<Finding>();

        foreach (string scenePath in UIRebuildAll.AllUIScenes)
        {
            // SkyboundCity is on the list from Phase 6E and does not exist until it is built once.
            if (!System.IO.File.Exists(scenePath))
            {
                Debug.LogWarning($"[UI] Skipping missing scene: {scenePath}");
                continue;
            }

            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            findings.AddRange(AuditOpenScene());
        }

        if (!string.IsNullOrEmpty(reopen))
        {
            EditorSceneManager.OpenScene(reopen, OpenSceneMode.Single);
        }

        Report(findings);
    }

    public static void Report(List<Finding> findings)
    {
        if (findings.Count == 0)
        {
            Debug.Log("[UI] Typography audit passed: no undersized, scaled, clipped or " +
                      "wrong-font text in any UI scene.");
            return;
        }

        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"[UI] Typography audit found {findings.Count} issue(s):");
        foreach (Finding f in findings)
        {
            sb.AppendLine(f.ToString());
        }

        Debug.LogWarning(sb.ToString());
    }

    /// <summary>Audits whatever scene is currently open.</summary>
    public static List<Finding> AuditOpenScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        List<Finding> findings = new List<Finding>();

        foreach (TMP_Text text in Object.FindObjectsByType<TMP_Text>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            string path = Path(text.transform);

            // ---- font assignment
            if (text.font == null)
            {
                Add(findings, scene.name, path, "no font asset assigned.");
                continue;
            }

            if (System.Array.IndexOf(Fonts, text.font.name) < 0)
            {
                Add(findings, scene.name, path,
                    $"uses '{text.font.name}', which is outside the three-family UI set.");
            }

            // ---- transform scale
            Vector3 lossy = text.transform.lossyScale;
            if (!Approximately(lossy.x, 1f) || !Approximately(lossy.y, 1f))
            {
                Add(findings, scene.name, path,
                    $"lossy scale is ({lossy.x:0.###}, {lossy.y:0.###}), not 1. A scaled TMP " +
                    "object resamples its SDF quad and renders soft; change fontSize instead.");
            }

            // ---- legibility floor at 1280x720
            float smallest = text.enableAutoSizing ? text.fontSizeMin : text.fontSize;
            FaceInfo face = text.font.faceInfo;
            float capRatio = face.pointSize > 0f ? face.capLine / face.pointSize : 0.7f;
            float capPixels = smallest * capRatio * MinScaleFactor;
            if (capPixels < MinCapPixels)
            {
                Add(findings, scene.name, path,
                    $"{smallest:0.#}pt of {text.font.name} renders a {capPixels:0.#}px cap at " +
                    $"1280x720 (floor is {MinCapPixels}px).");
            }

            // ---- clipping
            if (string.IsNullOrEmpty(text.text))
            {
                continue;
            }

            RectTransform rt = (RectTransform)text.transform;
            Vector2 need = text.GetPreferredValues(text.text, rt.rect.width, 0f);

            if (text.textWrappingMode == TextWrappingModes.NoWrap && !text.enableAutoSizing
                && need.x > rt.rect.width + 1f)
            {
                Add(findings, scene.name, path,
                    $"\"{Trim(text.text)}\" needs {need.x:0}u of width in a {rt.rect.width:0}u box.");
            }

            if (!text.enableAutoSizing && need.y > rt.rect.height + 1f)
            {
                Add(findings, scene.name, path,
                    $"\"{Trim(text.text)}\" needs {need.y:0}u of height in a {rt.rect.height:0}u box.");
            }
        }

        return findings;
    }

    private static void Add(List<Finding> into, string scene, string path, string problem)
        => into.Add(new Finding { Scene = scene, Path = path, Problem = problem });

    private static bool Approximately(float value, float target) => Mathf.Abs(value - target) < 0.002f;

    private static string Trim(string s) => s.Length <= 42 ? s : s.Substring(0, 39) + "...";

    private static string Path(Transform t)
    {
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }

        return path;
    }
}
