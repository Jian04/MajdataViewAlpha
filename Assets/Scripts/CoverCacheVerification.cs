#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

public static class CoverCacheVerification
{
    public static void RenderSamplingComparison()
    {
        var folder = Path.GetFullPath(".codex-build/oct12-cover-sampling");
        Directory.CreateDirectory(Path.Combine(folder, "figure"));
        var path = Path.Combine(folder, "figure", "songdetail_master.png");
        File.Copy(Path.GetFullPath(".codex-build/oct11-cover-marquee.png"), path, true);
        var root = new GameObject("Sampling verification", typeof(RectTransform), typeof(RawImage));
        var baseline = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        var previous = RenderTexture.active;
        try
        {
            baseline.LoadImage(File.ReadAllBytes(path)); baseline.filterMode = FilterMode.Bilinear;
            baseline.wrapMode = TextureWrapMode.Clamp;
            var template = root.AddComponent<SongDetailTemplateView>();
            var card = root.GetComponent<RawImage>();
            template.ApplyMaster(new Majson { songDetailStyle = 1, diffNum = 4, filePath = Path.Combine(folder, "majdata.json") },
                card, null, null, null, null, null, null);
            var sampled = (Texture2D)card.texture;
            if (sampled.mipmapCount <= 1 || sampled.filterMode != FilterMode.Trilinear || sampled.mipMapBias != -.5f)
                throw new Exception("Cover minification still samples only four source texels");
            foreach (var height in new[] { 512, 768, 1024 })
            foreach (var bias in new[] { 0f, -.5f, -.75f })
            foreach (var image in new[] { baseline, sampled })
            {
                image.mipMapBias = bias;
                var target = RenderTexture.GetTemporary(Mathf.RoundToInt(height * 341f / 588), height, 0);
                var capture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
                try
                {
                    Graphics.Blit(image, target); RenderTexture.active = target;
                    capture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); capture.Apply();
                    File.WriteAllBytes(Path.Combine(folder, (image == baseline ? "before-" : "after-") + height + (bias == 0 ? "" : "-bias" + bias.ToString(System.Globalization.CultureInfo.InvariantCulture)) + ".png"), capture.EncodeToPNG());
                }
                finally { UnityEngine.Object.DestroyImmediate(capture); RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); }
            }
            sampled.mipMapBias = -.5f;
            Debug.Log("COVER SAMPLING PASS: real GPU minification comparison at 512/768/1024; cached card has trilinear mipmaps.");
        }
        finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(baseline); UnityEngine.Object.DestroyImmediate(root); }
    }
    public static void Run()
    {
        void Require(bool value, string message) { if (!value) throw new Exception(message); }
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".codex-build", "cover-view-test-" + Guid.NewGuid().ToString("N")));
        string figure = Path.Combine(folder, "figure");
        Directory.CreateDirectory(figure);
        var root = new GameObject("Cover verification", typeof(RectTransform), typeof(RawImage));
        try
        {
            var template = root.AddComponent<SongDetailTemplateView>();
            var card = root.GetComponent<RawImage>();
            var data = new Majson { songDetailStyle = 1, diffNum = 4, filePath = Path.Combine(folder, ".majdata", "majdata.json") };
            bool LoadFails()
            {
                try { template.ApplyMaster(data, card, null, null, null, null, null, null); return false; }
                catch (IOException) { return true; }
            }
            Require(LoadFails(), "missing official cover silently used the original card");
            string path = Path.Combine(figure, "songdetail_master.png");
            var texture = new Texture2D(2, 2);
            texture.SetPixels(new[] { Color.red, Color.red, Color.red, Color.red }); texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            Require(template.ApplyMaster(data, card, null, null, null, null, null, null) && card.texture != null,
                "legacy cover in figure could not load through .majdata path");
            var textFolder = Path.Combine(figure, "songdetail_master_text");
            Directory.CreateDirectory(textFolder);
            var strip = new Texture2D(800, 84);
            File.WriteAllBytes(Path.Combine(textFolder, "title.png"), strip.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(strip);
            Require(template.ApplyMaster(data, card, null, null, null, null, null, null), "scrolling cover could not load");
            var marquee = root.transform.Find("CoverTextMarquee0").GetComponent<RawImage>();
            Require(Mathf.Abs(marquee.uvRect.width - 602f / 800f) < 0.0001f,
                "cover marquee must crop at the padded row width instead of shrinking the text");
            var update = typeof(SongDetailTemplateView).GetMethod("UpdateCachedMarquees", BindingFlags.Instance | BindingFlags.NonPublic);
            update.Invoke(template, new object[] { 1.8f });
            Require(Mathf.Abs(marquee.uvRect.x - 32f / 400f) < 0.0001f, "long cover title did not scroll at the configured speed");
            update.Invoke(template, new object[] { 2.2f });
            Require(Mathf.Abs(marquee.uvRect.x + marquee.uvRect.width - 1f) < 0.0001f,
                "cover marquee did not show its complete right edge");
            template.ResetOriginal();
            Require(!marquee.gameObject.activeSelf, "original cover retained a scrolling official title");
            File.Delete(Path.Combine(textFolder, "title.png"));
            template.ApplyMaster(data, card, null, null, null, null, null, null);
            Require(!marquee.gameObject.activeSelf, "short cover retained the previous song's scrolling text");
            File.WriteAllText(path, "invalid PNG");
            Require(LoadFails(), "corrupt official cover silently used original card");

            var loaderObject = new GameObject("DataLoader");
            try
            {
                var loader = loaderObject.AddComponent<JsonDataLoader>();
                typeof(JsonDataLoader).GetProperty("LoadError").SetValue(loader, "封面读取失败");
                var handler = root.AddComponent<HttpHandler>();
                var clock = root.AddComponent<AudioTimeProvider>();
                foreach (var methodName in new[] { "CompleteAsyncStartWhenPlayable", "CompleteAsyncIntroWhenPlayable" })
                {
                    var method = typeof(HttpHandler).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
                    var arguments = new object[method.GetParameters().Length];
                    arguments[0] = loader; arguments[1] = clock;
                    for (int i = 2; i < arguments.Length; i++)
                        if (method.GetParameters()[i].ParameterType.IsValueType)
                            arguments[i] = Activator.CreateInstance(method.GetParameters()[i].ParameterType);
                    var coroutine = (IEnumerator)method.Invoke(handler, arguments);
                    Require(!coroutine.MoveNext(), "cover failure left start request waiting");
                    var response = (string)typeof(HttpHandler).GetField("responseBody", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(handler);
                    Require(response.Contains("\"ok\":false") && response.Contains("封面读取失败"),
                        "cover failure was not returned to Edit");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(loaderObject); }
            Debug.Log("COVER VIEW PASS: padded scrolling crop, complete right edge, original/short cover reset, missing/corrupt cover errors, legacy figure path, async start and intro errors returned to Edit.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
}
#endif
