using System.Collections.Generic;
using System.IO;
using UnityEngine;
#nullable enable

/// <summary>
/// Loads the per-Note skins written as <c>~[star.png]</c> and hands out sprites for
/// them.
/// </summary>
/// <remarks>
/// Everything is cached on the decoded image, not on the sprite: one chart can point
/// hundreds of Notes at the same file and decoding a PNG per Note would stall the
/// build. Failures are cached too, so a misspelt name costs one look at the disk
/// instead of one per Note.
///
/// Paths are resolved against the chart's own folder and were already checked by
/// <see cref="MajdataCore.SlidePathParser.IsSkinPathUsable"/>, which rejects absolute
/// paths and anything climbing out with "..". The check is repeated here because this
/// class also serves charts that arrived as JSON without passing that parser.
/// </remarks>
public static class NoteSkinLibrary
{
    private static string chartFolder = string.Empty;
    private static readonly Dictionary<string, Texture2D?> textures = new();
    private static readonly Dictionary<(string, int), Sprite?> sprites = new();

    /// <summary>
    /// Points the library at the folder holding the chart being played. Switching
    /// charts drops everything loaded for the previous one.
    /// </summary>
    public static void SetChartFolder(string? folder)
    {
        var resolved = string.IsNullOrWhiteSpace(folder) ? string.Empty : folder!;
        if (resolved == chartFolder)
            return;
        chartFolder = resolved;
        Clear();
    }

    public static void Clear()
    {
        foreach (var sprite in sprites.Values)
            if (sprite != null)
                Object.Destroy(sprite);
        sprites.Clear();
        foreach (var texture in textures.Values)
            if (texture != null)
                Object.Destroy(texture);
        textures.Clear();
    }

    /// <summary>
    /// Builds a sprite for <paramref name="relativePath"/> sized to occupy exactly the
    /// same space on the playfield as <paramref name="model"/>, whatever the image's
    /// own pixel dimensions are. Returns null if the file cannot be used, and
    /// <paramref name="reason"/> then says why in a form fit to show the charter.
    /// </summary>
    public static Sprite? TryCreateSprite(
        string? relativePath,
        Sprite? model,
        out string reason)
    {
        reason = string.Empty;
        if (string.IsNullOrEmpty(relativePath))
            return null;

        if (!MajdataCore.SlidePathParser.IsSkinPathUsable(relativePath!, out var safe))
        {
            reason = $"skin '{relativePath}' is not a usable image path";
            return null;
        }

        // The model's world size is what the skin has to match, and a sprite with no
        // geometry cannot supply one, so fall back to Unity's default scale.
        var modelWidth = model != null && model.rect.width > 0f
            ? model.rect.width / model.pixelsPerUnit
            : 0f;

        var key = (safe, model == null ? 0 : model.GetInstanceID());
        if (sprites.TryGetValue(key, out var cachedSprite))
        {
            if (cachedSprite == null)
                reason = $"skin '{safe}' could not be loaded";
            return cachedSprite;
        }

        var texture = LoadTexture(safe, out reason);
        if (texture == null)
        {
            sprites[key] = null;
            return null;
        }

        var pixelsPerUnit = modelWidth > 0.0001f
            ? texture.width / modelWidth
            : 100f;
        var sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            pixelsPerUnit);
        sprite.name = safe;
        sprites[key] = sprite;
        return sprite;
    }

    private static Texture2D? LoadTexture(string safePath, out string reason)
    {
        reason = string.Empty;
        if (textures.TryGetValue(safePath, out var cached))
        {
            if (cached == null)
                reason = $"skin '{safePath}' could not be loaded";
            return cached;
        }

        if (string.IsNullOrEmpty(chartFolder))
        {
            textures[safePath] = null;
            reason = $"skin '{safePath}' has no chart folder to resolve against";
            return null;
        }

        var full = Path.Combine(chartFolder, safePath);
        if (!File.Exists(full))
        {
            textures[safePath] = null;
            reason = $"skin '{safePath}' was not found next to the chart";
            return null;
        }

        // LoadImage resizes the texture to the file, so the dimensions here are
        // placeholders; mipmaps are off because Notes are drawn near their native size.
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(File.ReadAllBytes(full)))
        {
            Object.Destroy(texture);
            textures[safePath] = null;
            reason = $"skin '{safePath}' is not a readable png/jpg";
            return null;
        }

        texture.wrapMode = TextureWrapMode.Clamp;
        textures[safePath] = texture;
        return texture;
    }
}
