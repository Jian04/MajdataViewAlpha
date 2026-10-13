using System;
using System.IO;
using System.Reflection;
using UnityEngine;

public static class ShakeDirectionVerification
{
    private sealed class Fixture { public System.Collections.Generic.List<EffectChange> effectTable; }
    public static void Run()
    {
        var root = new GameObject("Shake direction verification");
        var source = RenderTexture.GetTemporary(256, 256, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        var target = RenderTexture.GetTemporary(256, 256, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        var texture = new Texture2D(256, 256, TextureFormat.RGBA32, false, true);
        var previous = RenderTexture.active;
        try
        {
            var clock = root.AddComponent<AudioTimeProvider>();
            var controller = root.AddComponent<ScreenEffectController>();
            var render = typeof(ScreenEffectController).GetMethod("RenderFiltered", BindingFlags.Instance | BindingFlags.NonPublic);
            for (var y = 0; y < 256; y++) for (var x = 0; x < 256; x++)
                texture.SetPixel(x, y, Math.Abs(x - 128) < 8 && Math.Abs(y - 128) < 8 ? Color.white : Color.black);
            texture.Apply(); Graphics.Blit(texture, source);
            foreach (var name in new[] { "shake-fixtures/instant-0.json", "shake-fixtures/instant-90.json", "oct12-megalovania/view.json" })
            {
                var path = Path.Combine(Application.dataPath, "..", ".codex-build", name);
                var chart = Newtonsoft.Json.JsonConvert.DeserializeObject<Fixture>(File.ReadAllText(path));
                controller.Configure(chart.effectTable, clock);
                render.Invoke(controller, new object[] { source, target }); // Shader warmup.
                var samples = new System.Collections.Generic.List<(float Time, int Angle)>();
                foreach (var item in chart.effectTable)
                {
                    if (item.effect != "Shake") continue;
                    var angle = Mathf.RoundToInt(item.paramB * Mathf.Rad2Deg);
                    if (!item.hasDirection || angle != 0 && angle != 90)
                        throw new Exception("Shake JSON direction lost");
                    foreach (var offset in new[] { .05f, .1f, .15f })
                        samples.Add(((float)item.time + offset, angle));
                }
                // Repeat backwards to cover timeline scrubbing between differently directed events.
                var reverse = new System.Collections.Generic.List<(float Time, int Angle)>(samples);
                reverse.Reverse(); samples.AddRange(reverse);
                var motionByAngle = new System.Collections.Generic.Dictionary<int, float>();
                foreach (var (time, angle) in samples)
                {
                    clock.SetPreviewTime(time);
                    render.Invoke(controller, new object[] { source, target });
                    RenderTexture.active = target;
                    texture.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); texture.Apply();
                    float weight = 0, px = 0, py = 0;
                    for (var y = 0; y < 256; y++) for (var x = 0; x < 256; x++)
                    {
                        var light = texture.GetPixel(x, y).r;
                        weight += light; px += x * light; py += y * light;
                    }
                    px = px / weight - 128; py = py / weight - 128;
                    if (Mathf.Abs(angle == 0 ? py : px) > .05f)
                        throw new Exception($"Shake {angle} axis incorrect at {time}: ({px}, {py})");
                    motionByAngle.TryGetValue(angle, out var largest);
                    motionByAngle[angle] = Mathf.Max(largest, Mathf.Abs(angle == 0 ? px : py));
                    Debug.Log($"SHAKE GPU PASS: {name}, Instant,1,10,{angle},0.2 at {time}: ({px}, {py}) pixels");
                }
                foreach (var pair in motionByAngle)
                    if (pair.Value < .1f) throw new Exception($"Shake {pair.Key} did not move the frame");
            }
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(source); RenderTexture.ReleaseTemporary(target);
            UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(root);
        }
    }
}
