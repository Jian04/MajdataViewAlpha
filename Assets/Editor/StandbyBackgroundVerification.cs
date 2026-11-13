using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class StandbyBackgroundVerification
{
    public static void Run()
    {
        var shader = Resources.Load<Shader>("StandbyBackground");
        if (shader == null || !shader.isSupported) throw new Exception("Standby shader unavailable");
        var cameraObject = new GameObject("Standby color check");
        var camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true; camera.orthographicSize = 1; camera.transform.position = new Vector3(0, 0, -10);
        camera.cullingMask = 1 << 31; camera.enabled = false;
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        texture.SetPixels32(new[] { new Color32(30,30,30,255), new Color32(255,255,255,255),
            new Color32(30,30,30,255), new Color32(255,255,255,255) }); texture.filterMode = FilterMode.Point; texture.Apply();
        var sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * .5f, 1);
        var quad = new GameObject("Regions"); quad.layer = 31;
        var renderer = quad.AddComponent<SpriteRenderer>(); renderer.sprite = sprite;
        var material = new Material(shader); renderer.sharedMaterial = material;
        var target = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var pixels = new Texture2D(64, 64, TextureFormat.RGB24, false);
        var previous = RenderTexture.active; camera.targetTexture = target;
        foreach (var theme in new[] { "#23262C", "#ECEEF1" })
        {
            ColorUtility.TryParseHtmlString(theme, out var color);
            material.SetVector("_ThemeColor", QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color);
            material.SetFloat("_LightTheme", theme == "#ECEEF1" ? 1 : 0);
            camera.Render(); RenderTexture.active = target; pixels.ReadPixels(new Rect(0,0,64,64),0,0); pixels.Apply();
            var actual = pixels.GetPixel(12,32); var foreground = pixels.GetPixel(50,32);
            if (Mathf.Abs(actual.r-color.r) > .015f || Mathf.Abs(actual.g-color.g) > .015f || Mathf.Abs(actual.b-color.b) > .015f)
                throw new Exception("Theme color mismatch: " + theme + " actual=" + actual);
            if (Mathf.Abs(foreground.r - (theme == "#ECEEF1" ? 0 : 1)) > .015f)
                throw new Exception("Standby foreground lost contrast");
        }
        var regions = SpriteLoader.LoadSpriteFromFile(Path.Combine(Application.streamingAssetsPath, "Background", "StandbyRegions.png"));
        renderer.sprite = regions; camera.orthographicSize = regions.bounds.size.y / 2;
        camera.targetTexture = null; target.Release();
        target.width = target.height = 980; target.Create(); camera.targetTexture = target;
        UnityEngine.Object.DestroyImmediate(pixels); pixels = new Texture2D(980,980,TextureFormat.RGB24,false);
        foreach (var name in new[] { "dark", "light" })
        {
            ColorUtility.TryParseHtmlString(name == "dark" ? "#23262C" : "#ECEEF1", out var color);
            material.SetVector("_ThemeColor", QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color);
            material.SetFloat("_LightTheme", name == "light" ? 1 : 0);
            camera.Render(); RenderTexture.active = target; pixels.ReadPixels(new Rect(0,0,980,980),0,0); pixels.Apply();
            File.WriteAllBytes(Path.Combine(Application.dataPath,"../.codex-build/079-view-"+name+".png"),pixels.EncodeToPNG());
        }
        UnityEngine.Object.DestroyImmediate(regions.texture); UnityEngine.Object.DestroyImmediate(regions);
        RenderTexture.active = previous;
        UnityEngine.Object.DestroyImmediate(cameraObject); UnityEngine.Object.DestroyImmediate(quad);
        UnityEngine.Object.DestroyImmediate(material); UnityEngine.Object.DestroyImmediate(sprite); UnityEngine.Object.DestroyImmediate(texture);
        UnityEngine.Object.DestroyImmediate(pixels); target.Release(); UnityEngine.Object.DestroyImmediate(target);
        Debug.Log("STANDBY BACKGROUND PASS: exact dark/light theme pixels, retained foreground, one GPU pass.");
    }
    public static void Build() { Run(); AlphaCommandLineBuild.BuildView(); }
}
