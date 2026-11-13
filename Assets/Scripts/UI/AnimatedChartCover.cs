using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using MajdataCore;
using UnityEngine;

internal sealed class AnimatedChartCover
{
    // Replays and seeks reuse decoded frames; no image decoding or allocation in Update.
    private static AnimatedChartCover cached;
    private static AnimatedChartCover cachedBackground;
    private string path;
    private long modified, length;
    private readonly List<Sprite> frames = new();
    private readonly List<double> ends = new();
    private uint loops;
    internal bool Ready { get; private set; }
    internal Sprite First => frames.Count == 0 ? null : frames[0];

    internal static AnimatedChartCover Open(string file, bool forBackground = false)
    {
        var info = new FileInfo(file);
        var previous = forBackground ? cachedBackground : cached;
        if (previous != null && previous.path == file && previous.modified == info.LastWriteTimeUtc.Ticks && previous.length == info.Length && previous.Ready)
            return previous;
        previous?.Release();
        var next = new AnimatedChartCover { path = file, modified = info.LastWriteTimeUtc.Ticks, length = info.Length };
        if (forBackground) cachedBackground = next;
        else cached = next;
        return next;
    }

    internal IEnumerator Prepare()
    {
        if (Ready) yield break;
        var image = ApngImage.Read(File.ReadAllBytes(path));
        if (image.Frames.Count == 0) throw new InvalidDataException("APNG 没有动画帧：" + path);
        loops = image.Loops;
        var canvas = new Color32[checked(image.Width * image.Height)];
        var previous = new Color32[canvas.Length];
        var elapsed = 0d;
        var budget = System.Diagnostics.Stopwatch.StartNew();
        foreach (var frame in image.Frames)
        {
            if (frame.Dispose == 2) Array.Copy(canvas, previous, canvas.Length);
            var patch = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!patch.LoadImage(frame.Png)) throw new InvalidDataException("无法解码 APNG 帧：" + path);
            var pixels = patch.GetPixels32();
            var bottom = image.Height - frame.Y - frame.Height;
            for (var y = 0; y < frame.Height; y++)
            for (var x = 0; x < frame.Width; x++)
            {
                var target = (bottom + y) * image.Width + frame.X + x;
                var source = pixels[y * frame.Width + x];
                canvas[target] = frame.Blend == 0 ? source : Over(source, canvas[target]);
            }
            UnityEngine.Object.Destroy(patch);
            var texture = new Texture2D(image.Width, image.Height, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            texture.SetPixels32(canvas); texture.Apply(false, true);
            frames.Add(Sprite.Create(texture, new Rect(0, 0, image.Width, image.Height), new Vector2(.5f, .5f)));
            elapsed += Math.Max(.001d, frame.Duration); ends.Add(elapsed);
            if (frame.Dispose == 1)
                for (var y = 0; y < frame.Height; y++) Array.Clear(canvas, (bottom + y) * image.Width + frame.X, frame.Width);
            else if (frame.Dispose == 2) Array.Copy(previous, canvas, canvas.Length);
            if (budget.ElapsedMilliseconds >= 6) { yield return null; budget.Restart(); }
        }
        Ready = true;
    }

    internal Sprite At(double time)
    {
        if (frames.Count == 0) return null;
        var duration = ends[^1];
        time = Math.Max(0d, time);
        if (loops != 0 && time >= duration * loops) return frames[^1];
        time %= duration;
        var lo = 0; var hi = ends.Count - 1;
        while (lo < hi) { var mid = (lo + hi) / 2; if (ends[mid] <= time) lo = mid + 1; else hi = mid; }
        return frames[lo];
    }

    private static Color32 Over(Color32 source, Color32 destination)
    {
        var a = source.a * 255 + destination.a * (255 - source.a);
        if (a == 0) return default;
        return new Color32(
            (byte)((source.r * source.a * 255 + destination.r * destination.a * (255 - source.a) + a / 2) / a),
            (byte)((source.g * source.a * 255 + destination.g * destination.a * (255 - source.a) + a / 2) / a),
            (byte)((source.b * source.a * 255 + destination.b * destination.a * (255 - source.a) + a / 2) / a),
            (byte)((a + 127) / 255));
    }

    private void Release()
    {
        foreach (var frame in frames) { UnityEngine.Object.Destroy(frame.texture); UnityEngine.Object.Destroy(frame); }
        frames.Clear(); ends.Clear(); Ready = false;
    }
}
