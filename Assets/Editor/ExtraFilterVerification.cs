using System;
using System.IO;
using System.Linq;
using System.Reflection;
using MajdataCore;
using UnityEditor;
using UnityEngine;

public static class ExtraFilterVerification
{
    public static void BuildViewFixes()
    {
        Run();
        ViewFixVerification.Run();
        AlphaCommandLineBuild.BuildView();
    }
    public static void BuildCheckedView()
    {
        Run();
        VignetteGradientVerification.Run();
        NoiseZoneVerification.Run();
        TimelineRestoreVerification.Run();
        AlphaCommandLineBuild.BuildView();
    }
    public static void Run()
    {
        var shader = Resources.Load<Shader>("AlphaScreenEffects");
        if (!shader) throw new Exception("Missing filter shader");
        var material = new Material(shader);
        var fixture = new Texture2D(256, 256, TextureFormat.RGBA32, false, true) { wrapMode = TextureWrapMode.Clamp };
        var pixels = new Texture2D(256, 256, TextureFormat.RGBA32, false, true);
        var source = RenderTexture.GetTemporary(256, 256, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        var target = RenderTexture.GetTemporary(256, 256, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        var previous = RenderTexture.active;
        var controllerRoot = new GameObject("New filters controller fixture");
        void Require(bool ok, string name) { if (!ok) throw new Exception(name); }
        Color[] Draw()
        {
            Graphics.Blit(source, target, material);
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); pixels.Apply();
            return pixels.GetPixels();
        }
        float Difference(Color[] a, Color[] b) => a.Zip(b, (x, y) => Mathf.Abs(x.r-y.r) + Mathf.Abs(x.g-y.g) + Mathf.Abs(x.b-y.b)).Average();
        try
        {
            for (var y = 0; y < 256; y++) for (var x = 0; x < 256; x++)
            {
                var checker = ((x / 24 + y / 24) & 1) == 0 ? .13f : .5f;
                var color = new Color(x / 300f, checker, y / 300f, 1);
                var radius = Vector2.Distance(new Vector2(x, y), new Vector2(128, 128));
                if (radius > 93 && radius < 96 || Math.Abs(x - 128) < 2 || Math.Abs(y - 128) < 2) color = Color.white;
                fixture.SetPixel(x, y, color);
            }
            fixture.Apply(); Graphics.Blit(fixture, source);
            var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".codex-build", "FilterPreview"));
            Directory.CreateDirectory(folder);
            var baseline = Draw(); File.WriteAllBytes(Path.Combine(folder, "original.png"), pixels.EncodeToPNG());
            foreach (var filter in AlphaExtraFilters.All)
            {
                material.SetFloat(filter.Uniform, filter.Name == "BLOOM" ? 2 : .8f);
                material.SetFloat("_RipplePhase", .55f);
                material.SetFloat("_ShatterPhase", .2f);
                var result = Draw();
                var difference = Difference(result, baseline);
                Require(difference > .0005f, "Filter did not affect actual pixels: " + filter.Name + " difference=" + difference);
                Require(result.All(c => !float.IsNaN(c.r) && !float.IsInfinity(c.r)), "Invalid pixels: " + filter.Name);
                File.WriteAllBytes(Path.Combine(folder, filter.Name + ".png"), pixels.EncodeToPNG());
                material.SetFloat(filter.Uniform, 0);
                Require(Difference(Draw(), baseline) < .001f, "Filter failed to restore at zero: " + filter.Name);
                Debug.Log($"FILTER PIXEL PASS: {filter.Name}, difference={difference:F5}");
            }
            material.SetFloat("_LensDistort", -.8f);
            Require(Difference(Draw(), baseline) > .001f, "Negative lens distortion missing");
            material.SetFloat("_LensDistort", 0);
            material.SetFloat("_Kaleidoscope",1);
            material.SetFloat("_KaleidoscopeAngle",0);
            var zeroAngle=Draw();
            material.SetFloat("_KaleidoscopeAngle",45*Mathf.Deg2Rad);
            Require(Difference(Draw(),zeroAngle)>.01f,"Kaleidoscope degrees do not rotate mirrored sectors");
            material.SetFloat("_Kaleidoscope",0); material.SetFloat("_KaleidoscopeAngle",0);
            material.SetFloat("_Shatter", .8f);
            var first = Draw(); material.SetFloat("_EffectTime", 20);
            Require(Difference(Draw(), first) < .001f, "Glass shards drift randomly when seeking");
            material.SetFloat("_ShatterPhase", .01f);
            Require(Difference(Draw(), first) > .005f, "Glass has no impact motion");
            foreach (var phase in new[] { 0f, .025f, .05f, .1f, .2f })
            {
                material.SetFloat("_ShatterPhase", phase); Draw();
                File.WriteAllBytes(Path.Combine(folder, $"SHATTER_IMPACT_{phase:F3}.png"), pixels.EncodeToPNG());
            }
            material.SetFloat("_ShatterPhase", .2f);
            Require(Difference(Draw(), first) < .001f, "Glass impact does not reproduce after seeking");
            material.SetFloat("_Shatter", 0);
            foreach (var filter in AlphaExtraFilters.All) material.SetFloat(filter.Uniform, .3f);
            Require(Difference(Draw(), baseline) > .001f, "Combined new filters missing");
            foreach (var filter in AlphaExtraFilters.All) material.SetFloat(filter.Uniform, 0f);
            material.SetFloat("_RadialBlur", .8f); material.SetFloat("_Bloom", 2f);
            material.SetFloat("_Saturation", 1f);
            Require(Draw().All(c => Mathf.Abs(c.r - c.g) < .005f && Mathf.Abs(c.g - c.b) < .005f),
                "Sampling filters overwrote the existing monochrome filter");
            // Check material recovery through the real controller, not a duplicate reset helper.
            var controller = controllerRoot.AddComponent<ScreenEffectController>();
            typeof(ScreenEffectController).GetField("material", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, material);
            typeof(ScreenEffectController).GetMethod("SetMaterialDefaults", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
            Require(Difference(Draw(), baseline) < .001f, "Controller left residual filters after reset");
            var events = AlphaExtraFilters.All.Select(f => new EffectChange { effect = f.Canonical, stateful = true, enabled = true, intensity = .6f, transition = .4f }).ToList();
            events.Add(new EffectChange { effect = "Vignette", intensity = .5f });
            controller.Configure(events, null);
            var tracks = (System.Collections.IList)typeof(ScreenEffectController).GetField("extraEffects", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
            Require(tracks.Count == 10, "New events were not connected to controller tracks");
            foreach (var track in tracks)
            {
                var evaluate = track.GetType().GetMethod("Evaluate", new[] { typeof(float) });
                var value = (float)evaluate.Invoke(track, new object[] { .5f });
                Require(Mathf.Abs(value - .6f) < .001f, "Track strength changed");
                Require((float)evaluate.Invoke(track, new object[] { -1f }) == 0f, "Seek before event retained a filter");
                Require(Mathf.Abs((float)evaluate.Invoke(track, new object[] { .5f }) - value) < .001f, "Track did not recover after seeking");
            }
            var provider = controllerRoot.AddComponent<AudioTimeProvider>();
            var render = typeof(ScreenEffectController).GetMethod("OnRenderImage", BindingFlags.Instance | BindingFlags.NonPublic);
            Color[] RenderController()
            {
                render.Invoke(controller, new object[] { source, target });
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); pixels.Apply();
                return pixels.GetPixels();
            }
            foreach (var filter in AlphaExtraFilters.All)
            {
                controller.Configure(new System.Collections.Generic.List<EffectChange> {
                    new EffectChange { effect = filter.Canonical, stateful = true, enabled = true, intensity = .6f }
                }, provider);
                provider.AudioTime = .55f;
                RenderController(); // First frame warms up the real material.
                Require(Difference(RenderController(), baseline) > .0005f, "Controller failed to render " + filter.Name);
                provider.AudioTime = -1f;
                Require(Difference(RenderController(), baseline) < .001f, "Controller seek failed to clear " + filter.Name);
                provider.AudioTime = .55f;
                Require(Difference(RenderController(), baseline) > .0005f, "Controller seek failed to recover " + filter.Name);
            }
            controller.Configure(new System.Collections.Generic.List<EffectChange> {
                new(){time=0,effect="Kaleidoscope",stateful=true,enabled=true,intensity=0},
                new(){time=1,effect="Kaleidoscope",stateful=true,enabled=true,intensity=90,transition=2},
                new(){time=4,effect="Kaleidoscope",stateful=true,enabled=false,transition=.5f}
            },provider);
            provider.AudioTime=.5f; RenderController();
            Require(Difference(RenderController(),baseline)>.001f,"Zero-degree kaleidoscope is incorrectly disabled");
            provider.AudioTime=2;
            RenderController();
            Require(Mathf.Abs(material.GetFloat("_KaleidoscopeAngle")-45*Mathf.Deg2Rad)<.01f,"Kaleidoscope does not interpolate toward target degrees");
            provider.AudioTime=5;
            Require(Difference(RenderController(),baseline)<.001f,"Kaleidoscope False did not restore the image");
            Debug.Log("KALEIDOSCOPE ANGLE PASS: real GPU degrees, zero-angle enable, 45-degree midpoint, False and seek reset.");
            foreach (var message in ShaderUtil.GetShaderMessages(shader)) Require(message.severity.ToString() != "Error", message.message);
            // Controller owns the material once assigned.
            typeof(ScreenEffectController).GetField("material", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, null);
            Debug.Log("EXTRA FILTER CHECK PASS: 10 real GPU filters, zero/reset, negative lens, deterministic glass, combination, controller tracks and seeking.");
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(source); RenderTexture.ReleaseTemporary(target);
            UnityEngine.Object.DestroyImmediate(controllerRoot);
            UnityEngine.Object.DestroyImmediate(material); UnityEngine.Object.DestroyImmediate(fixture); UnityEngine.Object.DestroyImmediate(pixels);
        }
    }
}
