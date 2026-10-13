#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;
using UnityEditor;

public static class VignetteGradientVerification
{
    public static void Run()
    {
        var shader = Resources.Load<Shader>("AlphaScreenEffects");
        if (!shader) throw new Exception("Vignette shader missing");
        var material = new Material(shader);
        var white = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
        white.SetPixel(0, 0, Color.white); white.Apply();
        var pixels = new Texture2D(256, 256, TextureFormat.RGBA32, false, true);
        var source = RenderTexture.GetTemporary(256, 256, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        var target = RenderTexture.GetTemporary(256, 256, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        var previous = RenderTexture.active;
        void Draw()
        {
            Graphics.Blit(source, target, material);
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); pixels.Apply();
        }
        void Require(bool ok, string name) { if (!ok) throw new Exception(name); }
        try
        {
            Graphics.Blit(white, source);
            material.SetVector("_PlayfieldRect", new Vector4(0, 0, 1, 1));
            material.SetFloat("_Vignette", .5f);
            Draw();
            Require(pixels.GetPixel(170, 128).r > .95f && pixels.GetPixel(238, 128).r < .01f, "Old vignette hard edge changed");
            var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".codex-build", "VignettePreview"));
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "hard.png"), pixels.EncodeToPNG());
            material.SetFloat("_VignetteGradient", 1);
            Draw();
            var near = pixels.GetPixel(222, 128).r;
            var middle = pixels.GetPixel(238, 128).r;
            var far = pixels.GetPixel(250, 128).r;
            Require(near > middle && middle > far && near > .8f && middle > .1f && far > .01f, "Vignette does not gradually darken outside the circle");
            Require(pixels.GetPixel(170, 128).r > .95f && pixels.GetPixel(250, 250).r < .01f, "Soft vignette damaged the opening or failed to reach black");
            File.WriteAllBytes(Path.Combine(folder, "gradient.png"), pixels.EncodeToPNG());
            material.SetFloat("_Vignette", 1);
            Draw(); Require(pixels.GetPixel(128, 128).r < .01f, "Vignette 1 must close fully");
            material.SetFloat("_Vignette", 0);
            Draw(); Require(pixels.GetPixel(250, 250).r > .95f, "Disabled vignette changes pixels");
            material.SetFloat("_VignetteGradient", 0); material.SetFloat("_Vignette", .5f);
            Draw(); Require(pixels.GetPixel(238, 128).r < .01f, "Gradient does not return to hard mode");
            foreach (var message in ShaderUtil.GetShaderMessages(shader)) Require(message.severity.ToString() != "Error", message.message);
            Debug.Log($"VIGNETTE GRADIENT CHECK PASS: actual pixels, opening preserved, outward fade {near:F3}>{middle:F3}>{far:F3}, hard/reset/off/full-close compatibility.");
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(source); RenderTexture.ReleaseTemporary(target);
            UnityEngine.Object.DestroyImmediate(pixels); UnityEngine.Object.DestroyImmediate(white); UnityEngine.Object.DestroyImmediate(material);
        }
    }
}
#endif
