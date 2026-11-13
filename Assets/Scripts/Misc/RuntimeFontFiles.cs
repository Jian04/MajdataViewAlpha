using System;
using System.Collections.Generic;
using System.IO;
using MajdataCore;
using TMPro;
using UnityEngine.TextCore.LowLevel;

public static class RuntimeFontFiles
{
    private static readonly Dictionary<string, (long Stamp, TMP_FontAsset Font)> fonts = new(StringComparer.OrdinalIgnoreCase);
    public static string DirectoryPath => FontFiles.DirectoryForApplication(
        Path.GetDirectoryName(UnityEngine.Application.dataPath));

    public static TMP_FontAsset Load(string requested, string chartDirectory = null)
    {
        var path = FontFiles.Resolve(requested, DirectoryPath, chartDirectory);
        if (!File.Exists(path)) throw new FileNotFoundException("找不到字体文件：" + path, path);
        var stamp = File.GetLastWriteTimeUtc(path).Ticks;
        if (fonts.TryGetValue(path, out var cached) && cached.Stamp == stamp && cached.Font != null)
            return cached.Font;
        var copy = FontFiles.CreateLoadCopy(path);
        var font = TMP_FontAsset.CreateFontAsset(copy, 0, 64, 6, GlyphRenderMode.SDFAA, 2048, 2048);
        if (font == null) throw new InvalidDataException("无法加载字体文件：" + path);
        fonts[path] = (stamp, font);
        return font;
    }
}
