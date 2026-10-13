using System;
using UnityEngine;

public static class StarDisplayModeVerification
{
    public static void BuildCheckedView()
    {
        Run();
        AlphaCommandLineBuild.BuildView();
    }

    public static void Run()
    {
        var root = new GameObject("Star order verification");
        var texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, Color.white); texture.Apply();
        var sprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.one * .5f, 1);
        var target = new RenderTexture(16, 16, 24);
        var pixels = new Texture2D(16, 16);
        var previousTarget = RenderTexture.active;
        try
        {
            var camera = root.AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = .5f;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black; camera.targetTexture = target;
            camera.cullingMask = 1 << 31;
            var blue = Create(root, sprite, Color.blue, 0);
            var red = Create(root, sprite, Color.red, 0);
            StarDisplayOrder.Register(blue.gameObject, -1);
            StarDisplayOrder.Register(red.gameObject, -25);
            StarDisplayOrder.SetMode(0);
            ExpectPixel(camera, target, pixels, Color.red, "Legacy guide overlap");
            StarDisplayOrder.SetMode(1);
            ExpectPixel(camera, target, pixels, Color.blue, "Modern guide overlap");
            if (blue.sortingOrder != -1 || red.sortingOrder != -25)
                throw new Exception("Modern guide source order changed");
            UnityEngine.Object.DestroyImmediate(blue.gameObject);
            UnityEngine.Object.DestroyImmediate(red.gameObject);

            blue = Create(root, sprite, Color.blue, 0);
            red = Create(root, sprite, Color.red, -4);
            var ex = Create(red.gameObject, sprite, Color.clear, -3);
            ex.enabled = false;
            StarDisplayOrder.Register(blue.gameObject, 0, true);
            StarDisplayOrder.Register(red.gameObject, -4, true);
            StarDisplayOrder.SetMode(0);
            ExpectPixel(camera, target, pixels, Color.red, "Legacy head overlap");
            if (ex.sortingOrder - red.sortingOrder != 1) throw new Exception("EX offset changed");
            StarDisplayOrder.SetMode(1);
            ExpectPixel(camera, target, pixels, Color.blue, "Modern head overlap");
            if (blue.sortingOrder != 0 || red.sortingOrder != -4 || ex.sortingOrder != -3)
                throw new Exception("Original head layers were not restored");
            Debug.Log("STAR DISPLAY MODE PASS: live blue/red overlap in both modes, guide/head layers and EX offset.");
        }
        finally
        {
            RenderTexture.active = previousTarget;
            root.GetComponent<Camera>().targetTexture = null;
            UnityEngine.Object.DestroyImmediate(root);
            StarDisplayOrder.SetMode(1);
            UnityEngine.Object.DestroyImmediate(sprite);
            UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(pixels);
            target.Release(); UnityEngine.Object.DestroyImmediate(target);
        }
    }

    private static SpriteRenderer Create(GameObject parent, Sprite sprite, Color color, int order)
    {
        var item = new GameObject("Star fixture"); item.transform.SetParent(parent.transform, false);
        item.layer = 31;
        // The camera is attached to the fixture root; keep stars at world origin.
        item.transform.position = Vector3.zero;
        var renderer = item.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite; renderer.color = color; renderer.sortingOrder = order;
        return renderer;
    }

    private static void ExpectPixel(Camera camera, RenderTexture target, Texture2D pixels, Color expected, string name)
    {
        camera.Render(); RenderTexture.active = target;
        pixels.ReadPixels(new Rect(0, 0, 16, 16), 0, 0); pixels.Apply();
        var pixel = pixels.GetPixel(8, 8);
        if (Mathf.Abs(pixel.r - expected.r) > .05f || Mathf.Abs(pixel.b - expected.b) > .05f)
            throw new Exception(name + ": " + pixel);
    }
}
