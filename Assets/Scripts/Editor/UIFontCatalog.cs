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

/// <summary>Creates and loads the three TMP font assets used by the Figma-derived UI.</summary>
public static class UIFontCatalog
{
    private const string AssetFolder = "Assets/TextMesh Pro/Resources/Fonts & Materials/";

    public static bool TryLoad(out UIFontSet fonts)
    {
        TMP_FontAsset display = LoadOrCreate(
            "Assets/TextMesh Pro/Fonts/Barlow_Condensed/BarlowCondensed-Black.ttf",
            AssetFolder + "BarlowCondensed-Black SDF.asset");
        TMP_FontAsset body = LoadOrCreate(
            "Assets/TextMesh Pro/Fonts/Inter/static/Inter_18pt-Regular.ttf",
            AssetFolder + "Inter_18pt-Regular SDF.asset");
        TMP_FontAsset mono = LoadOrCreate(
            "Assets/TextMesh Pro/Fonts/Lekton/Lekton-Regular.ttf",
            AssetFolder + "Lekton-Regular SDF.asset");

        fonts = display != null && body != null && mono != null
            ? new UIFontSet(display, body, mono)
            : null;

        if (fonts != null)
        {
            AssetDatabase.SaveAssets();
        }

        return fonts != null;
    }

    private static TMP_FontAsset LoadOrCreate(string sourcePath, string assetPath)
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

            return existing;
        }

        TMP_FontAsset created = TMP_FontAsset.CreateFontAsset(
            source, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024,
            AtlasPopulationMode.Dynamic, true);
        if (created == null)
        {
            Debug.LogError($"[UI] Failed to create TMP font asset from: {sourcePath}");
            return null;
        }

        AssetDatabase.CreateAsset(created, assetPath);
        AssetDatabase.AddObjectToAsset(created.atlasTextures[0], created);
        AssetDatabase.AddObjectToAsset(created.material, created);
        EditorUtility.SetDirty(created);
        return created;
    }
}
