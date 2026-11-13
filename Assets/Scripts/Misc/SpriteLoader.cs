using System.IO;
using UnityEngine;
using System.Collections.Generic;
#nullable enable
public static class SpriteLoader
{
    private static readonly Dictionary<(string, Vector4), (long Ticks, long Length, Sprite Sprite)> cache = new();
    public static Sprite LoadSpriteFromFile(string path) => LoadSpriteFromFile(path, Vector4.zero);

    public static Sprite LoadSpriteFromFile(string path, Vector4 border)
    {
        if (!File.Exists(path))
            return Sprite.Create(new Texture2D(0, 0), new Rect(0, 0, 0, 0), new Vector2(0.5f, 0.5f));
        var file = new FileInfo(path);
        var key = (file.FullName.ToUpperInvariant(), border);
        if (cache.TryGetValue(key, out var stored) && stored.Sprite != null &&
            stored.Ticks == file.LastWriteTimeUtc.Ticks && stored.Length == file.Length) return stored.Sprite;
        var bytes = File.ReadAllBytes(path);
        var texture = new Texture2D(0, 0);
        texture.LoadImage(bytes);
        var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100, 1,
            border == Vector4.zero ? SpriteMeshType.Tight : SpriteMeshType.FullRect, border);
        cache[key] = (file.LastWriteTimeUtc.Ticks, file.Length, sprite);
        return sprite;
    }
}
