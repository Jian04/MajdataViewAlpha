using System;
using System.IO;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

public static class FontFileVerification
{
    public static void Run()
    {
        foreach (var path in Directory.GetFiles(RuntimeFontFiles.DirectoryPath))
            if (MajdataCore.FontFiles.IsFontFile(path)) CheckFont(path);
        var canvas = new GameObject("File font test", typeof(Canvas));
        var textObject = new GameObject("Legacy text", typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(canvas.transform, false);
        var source = textObject.GetComponent<Text>();
        source.text = "123 BPM";
        source.fontSize = 24;
        source.color = Color.cyan;
        var renderer = textObject.AddComponent<FileFontText>();
        renderer.Use(RuntimeFontFiles.Load("Allerta-Regular.ttf"));
        if (source.enabled || renderer.Measure("123 BPM") <= 0 ||
            textObject.GetComponentInChildren<TMPro.TextMeshProUGUI>().color != Color.cyan)
            throw new Exception("File-font renderer must preserve text, size, color and measurement");
        renderer.Use(null);
        if (!source.enabled || renderer.UsesFileFont)
            throw new Exception("Switching back must restore the existing renderer");
        UnityEngine.Object.DestroyImmediate(canvas);
        Debug.Log("FONT UI PASS: file font rendering, sizing, color and restore");
        var captionObject=new GameObject("Builtin caption font verification");
        var captions=captionObject.AddComponent<DisplayTimelineController>();
        var builtin=(TMPro.TMP_FontAsset)typeof(DisplayTimelineController).GetMethod("ResolveFileCaptionFont",
            System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(captions,new object[]{"NotoSansSC"});
        if(builtin==null || !builtin.TryAddCharacters("字幕")) throw new Exception("Built-in NotoSansSC must work without an external font file");
        UnityEngine.Object.DestroyImmediate(captionObject);
        captionObject=new GameObject("Second caption font verification");
        captions=captionObject.AddComponent<DisplayTimelineController>();
        var reused=typeof(DisplayTimelineController).GetMethod("ResolveFileCaptionFont",
            System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(captions,new object[]{"NotoSansSC"});
        if(!ReferenceEquals(builtin,reused)) throw new Exception("Repeated charts must reuse the built-in font atlas");
        UnityEngine.Object.DestroyImmediate(captionObject);
        Debug.Log("BUILTIN FONT PASS: NotoSansSC captions use the bundled font resource, no extra TTF required.");
    }

    private static void CheckFont(string path)
    {
        var font = RuntimeFontFiles.Load(path);
        FontEngine.LoadFontFace(path, 64);
        var expected = new System.Collections.Generic.Dictionary<char, float>();
        foreach (var character in "MiW013")
            if (FontEngine.TryGetGlyphWithUnicodeValue(character, GlyphLoadFlags.LOAD_DEFAULT, out var glyph))
                expected[character] = glyph.metrics.horizontalAdvance;
        FontEngine.UnloadFontFace();
        font.TryAddCharacters("ABCxyz0123MiW");
        foreach (var pair in expected)
            if (!font.characterLookupTable.TryGetValue(pair.Key, out var character) ||
                Math.Abs(character.glyph.metrics.horizontalAdvance - pair.Value) > 1.1f)
                throw new Exception("External font rendered a fallback face: " + path + " character=" + pair.Key);
        Debug.Log("FONT FILE PASS: " + Path.GetFileName(path) + " · " + font.faceInfo.familyName);
    }
}
