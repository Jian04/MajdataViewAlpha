using System;
using System.Linq;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PseudoAlphaPlaybackVerification
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static object Call(object target, string name, params object[] args) =>
        target.GetType().GetMethod(name, Flags).Invoke(target, args);
    public static void Build() { Run(); AlphaCommandLineBuild.BuildView(); }
    public static void Run()
    {
        var json = File.ReadAllText(".codex-build/pseudo-alpha-screenshot/view.json");
        foreach (var alpha in new[] { 0f, 1f })
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Server.unity");
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
            var loader = UnityEngine.Object.FindAnyObjectByType<JsonDataLoader>();
            Call(loader, "Start");
            var skin = GameObject.Find("Outline").GetComponent<CustomSkin>();
            skin.LoadSkin("dx");
            Call(GameObject.Find("MultTouchHandler").GetComponent<MultTouchHandler>(), "Start");
            var clock = GameObject.Find("AudioTimeProvider").GetComponent<AudioTimeProvider>();
            typeof(JsonDataLoader).GetField("timeProvider", Flags).SetValue(loader, clock);
            var data = JObject.Parse(json);
            data["alphaTable"][0]["alpha"] = alpha;
            loader.LoadJsonImmediate(data.ToString(), 0, true);
            var touches = GameObject.Find("Notes").GetComponentsInChildren<TouchDrop>(true);
            if (touches.Length != 8) throw new Exception("Screenshot pseudo-each did not load all 8 Touch notes");
            foreach (var touch in touches)
            {
                Call(touch, "Start");
                foreach (var renderer in touch.GetComponentsInChildren<SpriteRenderer>(true))
                    renderer.gameObject.layer = 31;
            }
            var cameraObject = new GameObject("Pseudo Touch capture");
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 6;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            var target = RenderTexture.GetTemporary(384, 384, 0, RenderTextureFormat.ARGB32);
            camera.targetTexture = target;
            var pixels = new Texture2D(384, 384, TextureFormat.RGBA32, false);
            long visible = 0;
            var captured = false;
            for (var frame = 0; frame < 120; frame++)
            {
                clock.AudioTime = .5f + frame / 60f;
                foreach (var touch in touches)
                {
                    Call(touch, "Update");
                    // Also exercise the white frames used by repeated Touch notes.
                    touch.setLayer(frame % 3);
                }
                camera.Render();
                var previous = RenderTexture.active; RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 384, 384), 0, 0); pixels.Apply();
                RenderTexture.active = previous;
                long frameAlpha = 0;
                foreach (var pixel in pixels.GetPixels32()) frameAlpha += pixel.a;
                visible += frameAlpha;
                if (alpha == 0 && frameAlpha > 0 && !captured)
                {
                    captured = true;
                    File.WriteAllBytes(".codex-build/pseudo-alpha-residual.png", pixels.EncodeToPNG());
                    foreach (var renderer in UnityEngine.Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
                    {
                        if (renderer.gameObject.layer != 31 || !renderer.gameObject.activeInHierarchy) continue;
                        var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
                        var mat = renderer.sharedMaterial;
                        Debug.Log($"RESIDUAL {clock.AudioTime}: {renderer.transform.parent?.name}/{renderer.name}; shader={mat?.shader.name}, matAlpha={(mat!=null && mat.HasProperty("_NoteAlpha") ? mat.GetFloat("_NoteAlpha") : -1)}, blockAlpha={(block.HasFloat("_NoteAlpha") ? block.GetFloat("_NoteAlpha") : -1)}, vertex={renderer.color.a}");
                    }
                    foreach (var renderer in touches.SelectMany(t => t.GetComponentsInChildren<SpriteRenderer>(true))) renderer.enabled = false;
                    camera.Render();
                    previous = RenderTexture.active; RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, 384, 384), 0, 0); pixels.Apply();
                    RenderTexture.active = previous;
                    long emptyAlpha = 0; foreach (var pixel in pixels.GetPixels32()) emptyAlpha += pixel.a;
                    Debug.Log($"RESIDUAL EMPTY CONTROL: {emptyAlpha}; frame={frameAlpha}");
                    foreach (var renderer in touches.SelectMany(t => t.GetComponentsInChildren<SpriteRenderer>(true))) renderer.enabled = true;
                }
            }
            if (alpha == 0 && visible != 0) throw new Exception($"ALPHA*0 screenshot Touch still rendered: {visible}");
            if (alpha == 1 && visible == 0) throw new Exception("Opaque control did not render; transparency test is inconclusive");
            Debug.Log($"PSEUDO ALPHA PLAYBACK GPU: alpha={alpha}, {touches.Length} real Touch prefabs, 120 frames, pixel alpha sum={visible}");
            camera.targetTexture = null; RenderTexture.ReleaseTemporary(target);
            UnityEngine.Object.DestroyImmediate(pixels); UnityEngine.Object.DestroyImmediate(cameraObject);
            foreach (var touch in touches)
                if (touch.justEffect != null) UnityEngine.Object.DestroyImmediate(touch.justEffect);
        }
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Debug.Log("PSEUDO ALPHA PLAYBACK GPU PASSED");
    }
}
