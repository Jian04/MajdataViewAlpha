using System;
using System.IO;
using System.Linq;
using MajdataCore;
using UnityEngine;

public static class FilterPreviewAtlases
{
    const int Tile = 176, Columns = 4, Rows = 3;

    public static void BuildView()
    {
        Generate();
        Carrier081Verification.Build();
    }

    public static void BuildShatter()
    {
        Generate("SHATTER");
        Carrier081Verification.Build();
    }

    public static void BuildNativePreviews()
    {
        Generate();
        ExtraFilterVerification.Run();
        AlphaCommandLineBuild.BuildView();
    }

    public static void Generate() => Generate(null);

    public static void ExportGlassDemo()
    {
        Generate();
        var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../.codex-build/128-glass-demo"));
        Directory.CreateDirectory(folder);
        var scene = MakeScene(512);
        var material = new Material(Resources.Load<Shader>("AlphaScreenEffects"));
        var target = RenderTexture.GetTemporary(512, 512, 0);
        var pixels = new Texture2D(512, 512, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            material.EnableKeyword("ALPHA_SHATTER");
            foreach (var strength in new[] { .2f, .85f })
            {
                var sample = Path.Combine(folder, strength < .3f ? "cracks" : "shards");
                Directory.CreateDirectory(sample);
                material.SetFloat("_Shatter", strength);
                for (var frame = 0; frame < 72; frame++)
                {
                    material.SetFloat("_ShatterPhase", Math.Max(0, frame - 12) / 30f);
                    AlphaGlassMesh.Blit(scene, target, material);
                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, 512, 512), 0, 0); pixels.Apply();
                    File.WriteAllBytes(Path.Combine(sample, $"{frame:D3}.png"), pixels.EncodeToPNG());
                }
            }
        }
        finally
        {
            RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target);
            UnityEngine.Object.DestroyImmediate(scene); UnityEngine.Object.DestroyImmediate(material);
            UnityEngine.Object.DestroyImmediate(pixels);
        }
        Debug.Log("GLASS DEMO EXPORTED: " + folder);
    }

    public static void BuildImpactPreview()
    {
        ExportGlassDemo();
        ExtraFilterVerification.Run();
        AlphaCommandLineBuild.BuildView();
    }

    static void Generate(string name)
    {
        var directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../MajdataEdit/Image/FilterPreviews"));
        Directory.CreateDirectory(directory);
        var scene = MakeScene();
        var atlas = new Texture2D(Tile * Columns, Tile * Rows, TextureFormat.RGB24, false);
        var material = new Material(Resources.Load<Shader>("AlphaScreenEffects"));
        var target = RenderTexture.GetTemporary(Tile, Tile, 0);
        var history = RenderTexture.GetTemporary(Tile, Tile, 0);
        var previous = RenderTexture.active;
        try
        {
            foreach (var filter in AlphaCommandGrammar.Commands.Where(item => item.kind == AlphaCommandKind.Effect))
            {
                if (name != null && filter.name != name) continue;
                for (var frame = 0; frame < Columns * Rows; frame++)
                {
                    var phase = Math.Max(0, frame - 2) / 9f;
                    var amount = frame < 2 ? 0 : frame < 4 ? (frame - 1) * .5f : 1;
                    SetSample(material, filter, phase, amount);
                    if (filter.name == "TRAIL")
                    {
                        Graphics.Blit(scene, history, Vector2.one, new Vector2(phase * .15f, -.11f));
                        material.SetTexture("_HistoryTex", history);
                        material.SetTexture("_HistoryTex2", history);
                        material.SetTexture("_HistoryTex3", history);
                    }
                    AlphaGlassMesh.Blit(scene, target, material);
                    RenderTexture.active = target;
                    atlas.ReadPixels(new Rect(0, 0, Tile, Tile), frame % Columns * Tile,
                        (Rows - 1 - frame / Columns) * Tile, false);
                }
                atlas.Apply();
                File.WriteAllBytes(Path.Combine(directory, filter.name + ".jpg"), atlas.EncodeToJPG(82));
                SetSample(material, filter, 0, 0);
            }
            Debug.Log("FILTER PREVIEW ATLASES GENERATED: " + directory);
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target); RenderTexture.ReleaseTemporary(history);
            UnityEngine.Object.DestroyImmediate(scene); UnityEngine.Object.DestroyImmediate(atlas);
            UnityEngine.Object.DestroyImmediate(material);
        }
    }

    static void SetSample(Material material, AlphaCommandDescriptor filter, float phase, float amount)
    {
        switch (filter.name)
        {
            case "GAUSSIAN": material.SetFloat("_Blur", .8f * amount); break;
            case "FADE": material.SetFloat("_Flash", -.7f * amount); break;
            case "FLASH": material.SetFloat("_Flash", .52f * amount * (1 - phase)); break;
            case "TRAIL": material.SetFloat("_Trail", .9f * amount); break;
            case "VIGNETTE":
                material.SetFloat("_Vignette", .55f * amount);
                material.SetFloat("_VignetteGradient", 1);
                break;
            case "RIPPLE":
                material.SetFloat("_Ripple", .9f * amount);
                material.SetFloat("_RipplePhase", phase * 1.18f + .15f);
                break;
            case "SHATTER":
                material.SetFloat("_Shatter", amount > 0 ? .85f : 0);
                material.SetFloat("_ShatterPhase", phase * .405f);
                if (amount > 0) material.EnableKeyword("ALPHA_SHATTER");
                else material.DisableKeyword("ALPHA_SHATTER");
                break;
            case "MOVE":
                material.SetFloat("_OffsetX", .15f * amount);
                material.SetFloat("_OffsetY", .06f * amount);
                break;
            case "SHAKE":
                material.SetFloat("_OffsetX", Mathf.Sin(phase * 29) * .036f * amount);
                material.SetFloat("_OffsetY", Mathf.Cos(phase * 19) * .021f * amount);
                break;
            case "FLIP":
                material.SetVector("_FlipAxis", amount > 0 ? new Vector4(.7071068f,.7071068f,0,0) : Vector4.zero);
                break;
            case "TINT": material.SetColor("_TintColor", new Color(.71f, .36f, .90f, .57f * amount)); break;
            case "KALEIDOSCOPE":
                material.SetFloat("_Kaleidoscope", amount);
                material.SetFloat("_KaleidoscopeAngle", phase * .8f);
                break;
            default:
                var strength = filter.name switch
                {
                    "ROTATE" => .53f, "HUE" => 2.1f, "ZOOM" => .43f,
                    "BLOOM" => 2.1f, "BRIGHTNESS" => .4f, _ => .82f
                };
                material.SetFloat("_" + filter.Canonical, strength * amount);
                break;
        }
        material.SetFloat("_EffectTime", phase);
    }

    static Texture2D MakeScene(int size = Tile)
    {
        var textures = new System.Collections.Generic.List<Texture2D>();
        Texture2D Load(string relative)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            textures.Add(texture);
            if (!texture.LoadImage(File.ReadAllBytes(Path.Combine(Application.streamingAssetsPath, relative))))
                throw new InvalidOperationException("Cannot read filter preview asset: " + relative);
            return texture;
        }
        var previous = RenderTexture.active;
        var target = RenderTexture.GetTemporary(size, size, 0);
        try
        {
            var background = Load("Background/Default_Background_Dark.png");
            var outline = Load("Skin/sd/outline.png");
            var tap = Load("Skin/sd/tap.png");
            var each = Load("Skin/sd/tap_each.png");
            var star = Load("Skin/sd/star.png");
            RenderTexture.active = target;
            GL.Clear(true, true, new Color(.13f,.14f,.16f,1));
            GL.PushMatrix();
            try
            {
                GL.LoadPixelMatrix(0, Tile, Tile, 0);
                Graphics.DrawTexture(new Rect(0, 0, Tile, Tile), background);
                Graphics.DrawTexture(new Rect(10, 10, Tile - 20, Tile - 20), outline);
                Graphics.DrawTexture(new Rect(29, 35, 27, 27), tap);
                Graphics.DrawTexture(new Rect(119, 111, 27, 27), tap);
                Graphics.DrawTexture(new Rect(105, 33, 25, 25), each);
                Graphics.DrawTexture(new Rect(62, 110, 28, 28), star);
            }
            finally { GL.PopMatrix(); }
            var image = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            image.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            image.Apply();
            return image;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            foreach (var texture in textures) UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
