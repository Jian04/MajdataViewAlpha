using System;
using System.IO;
using UnityEngine;

public static class InheritedImageReferenceVerification
{
    public static void Run()
    {
        var args = Environment.GetCommandLineArgs();
        var folder = args[Array.IndexOf(args, "-alphaImageFixture") + 1];
        var output = Path.GetFullPath(".codex-build/oct13-image-reference");
        Directory.CreateDirectory(output);
        var model = SpriteLoader.LoadSpriteFromFile(Path.GetFullPath("Skin/dx/tap.png"));
        NoteSkinLibrary.SetChartFolder(folder);
        var root = new GameObject("View inherited image reference");
        var camera = root.AddComponent<Camera>();
        camera.orthographic = true; camera.orthographicSize = 4;
        camera.transform.position = new Vector3(0, 0, -10);
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
        camera.cullingMask = 1 << 31;
        var target = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        camera.targetTexture = target;
        var pixels = new Texture2D(256, 256, TextureFormat.RGBA32, false, true);
        var head = new GameObject("View custom Tap"); head.layer = 31;
        var renderer = head.AddComponent<SpriteRenderer>();
        var previous = RenderTexture.active;
        try
        {
            foreach (var file in new[] { "2.png", "4.png", "1.png", "3.png", "6.png", "8.png" })
            {
                renderer.sprite = NoteSkinLibrary.TryCreateSprite(file, model, out var reason);
                if (!renderer.sprite) throw new Exception(reason);
                head.transform.localScale = new Vector3(3, 3, 1);
                camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); pixels.Apply();
                File.WriteAllBytes(Path.Combine(output, file), pixels.EncodeToPNG());
                Debug.Log($"VIEW IMAGE REFERENCE PASS: {file}, sprite={renderer.sprite.bounds.size}, scale={head.transform.localScale}, rotation={head.transform.eulerAngles}");
            }
        }
        finally
        {
            RenderTexture.active = previous; camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(head); UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(pixels); UnityEngine.Object.DestroyImmediate(model.texture);
            UnityEngine.Object.DestroyImmediate(model); NoteSkinLibrary.Clear();
            target.Release(); UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
