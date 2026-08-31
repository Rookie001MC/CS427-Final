using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

public enum UIFontRole
{
    Display,
    Body,
    Mono
}

public sealed class UIFontSet
{
    public TMP_FontAsset Display { get; }
    public TMP_FontAsset Body { get; }
    public TMP_FontAsset Mono { get; }

    public UIFontSet(TMP_FontAsset display, TMP_FontAsset body, TMP_FontAsset mono)
    {
        Display = display;
        Body = body;
        Mono = mono;
    }

    public TMP_FontAsset Resolve(UIFontRole role)
    {
        switch (role)
        {
            case UIFontRole.Display: return Display;
            case UIFontRole.Mono: return Mono;
            default: return Body;
        }
    }
}

/// <summary>
/// Creates and loads the three TMP font assets used by the Figma-derived UI.
///
/// Anton and Roboto Mono were measured against the reference mockups rather than guessed at:
/// Anton's advance-to-cap ratio is 0.513 against the reference's ~0.54 (Barlow Condensed Black,
/// the previous stand-in, sits at 0.662 - visibly too wide), and it shares the reference's
/// flat-topped A and squared bowls. Roboto Mono's 0.600em advance matches the reference mono
/// exactly, where Lekton's 0.500em ran a fifth too narrow. Both are OFL 1.1.
///
/// Note that Anton's cap height is 0.859em against Barlow's 0.700em, so every display size in
/// <see cref="UITheme"/> is 0.815x what it was; the rendered cap height is unchanged.
/// </summary>
public static class UIFontCatalog
{
    private const string AssetFolder = "Assets/TextMesh Pro/Resources/Fonts & Materials/";
    private const string FallbackPath = AssetFolder + "LiberationSans SDF.asset";

    /// <summary>
    /// Atlas parameters per role. Padding is what sets the SDF gradient range, and the shader
    /// derives _GradientScale from it, so a glyph blown up to 280pt from a 90pt sample runs out
    /// of gradient and softens. The display face carries the largest text in the game and gets
    /// the widest sample and padding to match.
    /// </summary>
    private readonly struct AtlasSpec
    {
        public readonly int SamplingPointSize;
        public readonly int Padding;
        public readonly int AtlasSize;

        /// <summary>Positive values thicken the rendered face. Fights a light stem weight.</summary>
        public readonly float FaceDilate;

        /// <summary>TMP's SDF edge sharpening. 0 is the default and reads soft on a UI canvas.</summary>
        public readonly float Sharpness;

        public AtlasSpec(int samplingPointSize, int padding, int atlasSize, float faceDilate, float sharpness)
        {
            SamplingPointSize = samplingPointSize;
            Padding = padding;
            AtlasSize = atlasSize;
            FaceDilate = faceDilate;
            Sharpness = sharpness;
        }
    }

    // Anton is a single-weight display face already at its ink ceiling; dilating it closes the
    // counters in O / P / R at the sizes the titles run at.
    private static readonly AtlasSpec DisplaySpec = new AtlasSpec(120, 14, 2048, 0f, 0.12f);

    // Roboto Mono Medium is a real 500 weight, so the labels need no dilation to stop reading
    // thin - that was a workaround for Lekton shipping only a 400.
    private static readonly AtlasSpec MonoSpec = new AtlasSpec(96, 11, 1024, 0f, 0.20f);

    // Inter ships only a 400 here and carries one line of running copy, so it keeps a hair of it.
    private static readonly AtlasSpec BodySpec = new AtlasSpec(96, 11, 1024, 0.04f, 0.20f);

    public static bool TryLoad(out UIFontSet fonts)
    {
        TMP_FontAsset fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FallbackPath);

        TMP_FontAsset display = LoadOrCreate(
            "Assets/TextMesh Pro/Fonts/Anton/Anton-Regular.ttf",
            AssetFolder + "Anton-Regular SDF.asset", DisplaySpec, fallback);
        TMP_FontAsset body = LoadOrCreate(
            "Assets/TextMesh Pro/Fonts/Inter/static/Inter_18pt-Regular.ttf",
            AssetFolder + "Inter_18pt-Regular SDF.asset", BodySpec, fallback);
        TMP_FontAsset mono = LoadOrCreate(
            "Assets/TextMesh Pro/Fonts/Roboto_Mono/RobotoMono-Medium.ttf",
            AssetFolder + "RobotoMono-Medium SDF.asset", MonoSpec, fallback);

        fonts = display != null && body != null && mono != null
            ? new UIFontSet(display, body, mono)
            : null;

        if (fonts != null)
        {
            AssetDatabase.SaveAssets();
        }

        return fonts != null;
    }

    private static TMP_FontAsset LoadOrCreate(string sourcePath, string assetPath, AtlasSpec spec,
        TMP_FontAsset fallback)
    {
        Font source = AssetDatabase.LoadAssetAtPath<Font>(sourcePath);
        if (source == null)
        {
            Debug.LogError($"[UI] Source font not found: {sourcePath}");
            return null;
        }

        TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
        if (existing != null)
        {
            if (existing.sourceFontFile != source)
            {
                Debug.LogError($"[UI] TMP font asset at '{assetPath}' uses the wrong source font.");
                return null;
            }

            // Self-healing: an asset baked under the old 90pt / 9px parameters would keep
            // rendering large text softly no matter what the builders ask for, and nothing in
            // the editor surfaces that. Rebake rather than silently accept the stale atlas.
            if (Mathf.RoundToInt(existing.faceInfo.pointSize) == spec.SamplingPointSize
                && existing.atlasPadding == spec.Padding)
            {
                Tune(existing, spec, fallback);
                return existing;
            }

            Debug.Log($"[UI] Rebaking '{assetPath}': was {existing.faceInfo.pointSize}pt / " +
                      $"{existing.atlasPadding}px padding, want {spec.SamplingPointSize}pt / {spec.Padding}px.");
            AssetDatabase.DeleteAsset(assetPath);
        }

        TMP_FontAsset created = TMP_FontAsset.CreateFontAsset(
            source, spec.SamplingPointSize, spec.Padding, GlyphRenderMode.SDFAA,
            spec.AtlasSize, spec.AtlasSize, AtlasPopulationMode.Dynamic, true);
        if (created == null)
        {
            Debug.LogError($"[UI] Failed to create TMP font asset from: {sourcePath}");
            return null;
        }

        AssetDatabase.CreateAsset(created, assetPath);
        AssetDatabase.AddObjectToAsset(created.atlasTextures[0], created);
        AssetDatabase.AddObjectToAsset(created.material, created);
        Tune(created, spec, fallback);
        return created;
    }

    /// <summary>
    /// Applies the sharpness / dilation the mockups need and wires the fallback chain. Split out
    /// so it runs on assets that were already baked at the right size, not just fresh ones.
    /// </summary>
    private static void Tune(TMP_FontAsset asset, AtlasSpec spec, TMP_FontAsset fallback)
    {
        Material material = asset.material;
        if (material != null)
        {
            if (material.HasProperty(ShaderUtilities.ID_Sharpness))
            {
                material.SetFloat(ShaderUtilities.ID_Sharpness, spec.Sharpness);
            }

            if (material.HasProperty(ShaderUtilities.ID_FaceDilate))
            {
                material.SetFloat(ShaderUtilities.ID_FaceDilate, spec.FaceDilate);
            }

            EditorUtility.SetDirty(material);
        }

        // Every glyph these screens use is present in all three faces (verified against the
        // string literals in the UI scripts and the LevelEntry assets), so this is insurance
        // for level names typed in later rather than a fix for a known gap.
        if (fallback != null && fallback != asset)
        {
            asset.fallbackFontAssetTable ??= new List<TMP_FontAsset>();
            if (!asset.fallbackFontAssetTable.Contains(fallback))
            {
                asset.fallbackFontAssetTable.Add(fallback);
            }
        }

        EditorUtility.SetDirty(asset);
    }
}
