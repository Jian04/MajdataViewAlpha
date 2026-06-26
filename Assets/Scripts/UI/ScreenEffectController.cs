using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Camera))]
public class ScreenEffectController : MonoBehaviour
{
    private readonly List<EffectChange> effects = new();
    private readonly Dictionary<string, List<EffectChange>> effectsByType = new();
    private EffectTrack gaussianEffects;
    private EffectTrack neonEffects;
    private EffectTrack trailEffects;
    private EffectTrack flashEffects;
    private EffectTrack brightnessEffects;
    private EffectTrack saturationEffects;
    private EffectTrack contrastEffects;
    private EffectTrack rainbowEffects;
    private EffectTrack vignetteEffects;
    private EffectTrack zoomEffects;
    private EffectTrack glitchEffects;
    private EffectTrack tvNoiseEffects;
    private AudioTimeProvider timeProvider;
    private Material material;
    private RenderTexture trailTexture;
    private RenderTexture trailTexture2;
    private RenderTexture trailTexture3;
    private int trailFrameCounter;
    private bool needsWarmup;
    private bool hasTrailEvents;
    private bool trailWasActive;
    private bool materialIsDefault;
    private int trailSeedStage;

    public void Configure(List<EffectChange> effectEvents, AudioTimeProvider provider)
    {
        timeProvider = provider;
        effects.Clear();
        effectsByType.Clear();
        if (effectEvents != null)
        {
            effects.AddRange(effectEvents);
            effects.Sort((left, right) => left.time.CompareTo(right.time));
            foreach (var item in effects)
            {
                if (!effectsByType.TryGetValue(item.effect, out var typeEvents))
                {
                    typeEvents = new List<EffectChange>();
                    effectsByType[item.effect] = typeEvents;
                }
                typeEvents.Add(item);
            }
        }
        gaussianEffects = CreateTrack("Gaussian", false);
        neonEffects = CreateTrack("Neon", false);
        trailEffects = CreateTrack("Trail", false);
        flashEffects = CreateTrack("Flash", true);
        brightnessEffects = CreateTrack("Brightness", false);
        saturationEffects = CreateTrack("Saturation", false);
        contrastEffects = CreateTrack("Contrast", false);
        rainbowEffects = CreateTrack("Rainbow", false);
        vignetteEffects = CreateTrack("Vignette", true);
        zoomEffects = CreateTrack("Zoom", true);
        glitchEffects = CreateTrack("Glitch", false);
        tvNoiseEffects = CreateTrack("TVNoise", false);
        enabled = effects.Count > 0;
        if (!enabled)
        {
            hasTrailEvents = false;
            needsWarmup = false;
            trailWasActive = false;
            materialIsDefault = true;
            trailSeedStage = 0;
            ReleaseTrail();
            return;
        }
        hasTrailEvents = effectsByType.ContainsKey("Trail");
        trailWasActive = false;
        materialIsDefault = false;
        trailSeedStage = 0;
        needsWarmup = true;

        var shader = Resources.Load<Shader>("AlphaScreenEffects");
        if (shader == null)
            shader = Shader.Find("Hidden/AlphaScreenEffects");
        if (shader != null && material == null)
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
    }

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (material == null || timeProvider == null)
        {
            trailWasActive = false;
            Graphics.Blit(source, destination);
            return;
        }

        if (needsWarmup)
        {
            if (hasTrailEvents)
                EnsureTrail(source);
            WarmupMaterial(source);
            Graphics.Blit(source, destination);
            needsWarmup = false;
            materialIsDefault = true;
            return;
        }

        var time = timeProvider.AudioTime;
        var gaussian = gaussianEffects.Evaluate(time);
        var neon = neonEffects.Evaluate(time);
        var trail = Mathf.Clamp01(trailEffects.Evaluate(time));
        var flash = Mathf.Clamp(flashEffects.Evaluate(time), -1f, 1f);
        var brightness = brightnessEffects.Evaluate(time);
        var saturation = Mathf.Clamp01(saturationEffects.Evaluate(time));
        var contrast = contrastEffects.Evaluate(time);
        var rainbow = Mathf.Clamp01(rainbowEffects.Evaluate(time));
        var vignette = Mathf.Clamp01(vignetteEffects.Evaluate(time));
        var zoom = Mathf.Clamp(zoomEffects.Evaluate(time), 0f, 2f);
        var glitch = Mathf.Clamp01(glitchEffects.Evaluate(time));
        var tvNoise = Mathf.Clamp01(tvNoiseEffects.Evaluate(time));

        if (gaussian <= 0.001f && neon <= 0.001f && trail <= 0.001f && Mathf.Abs(flash) <= 0.001f &&
            brightness <= 0.001f && saturation <= 0.001f && contrast <= 0.001f &&
            rainbow <= 0.001f && vignette <= 0.001f &&
            zoom <= 0.001f && glitch <= 0.001f && tvNoise <= 0.001f)
        {
            trailWasActive = false;
            PrepareUpcomingTrail(source, time);
            if (!materialIsDefault)
            {
                SetMaterialDefaults();
                materialIsDefault = true;
            }
            Graphics.Blit(source, destination, material);
            return;
        }

        material.SetFloat("_Blur", gaussian);
        material.SetFloat("_Neon", neon);
        material.SetFloat("_Fade", 0f);
        material.SetFloat("_Trail", trail);
        material.SetFloat("_Brightness", brightness);
        material.SetFloat("_Saturation", saturation);
        material.SetFloat("_Contrast", contrast);
        material.SetFloat("_Rainbow", rainbow);
        material.SetFloat("_EffectTime", time);
        material.SetFloat("_Flash", flash);
        material.SetFloat("_Vignette", vignette);
        material.SetFloat("_Zoom", zoom);
        material.SetFloat("_Glitch", glitch);
        material.SetFloat("_TVNoise", tvNoise);
        materialIsDefault = false;

        if (trail > 0.001f)
        {
            EnsureTrail(source);
            if (!trailWasActive)
                FinishTrailSeed(source);
            material.SetTexture("_HistoryTex", trailTexture);
            material.SetTexture("_HistoryTex2", trailTexture2);
            material.SetTexture("_HistoryTex3", trailTexture3);
            trailWasActive = true;
        }
        else
        {
            material.SetTexture("_HistoryTex", Texture2D.blackTexture);
            material.SetTexture("_HistoryTex2", Texture2D.blackTexture);
            material.SetTexture("_HistoryTex3", Texture2D.blackTexture);
            trailWasActive = false;
            PrepareUpcomingTrail(source, time);
        }

        Graphics.Blit(source, destination, material);

        if (trail > 0.001f)
        {
            trailFrameCounter++;
            if (trailFrameCounter >= 5)
            {
                Graphics.Blit(trailTexture2, trailTexture3);
                Graphics.Blit(trailTexture, trailTexture2);
                Graphics.Blit(source, trailTexture);
                trailFrameCounter = 0;
            }
        }

    }

    private void WarmupMaterial(RenderTexture source)
    {
        const float warmupValue = 0.01f;
        material.SetFloat("_Blur", warmupValue);
        material.SetFloat("_Neon", warmupValue);
        material.SetFloat("_Fade", 0f);
        material.SetFloat("_Trail", warmupValue);
        material.SetFloat("_Brightness", warmupValue);
        material.SetFloat("_Saturation", warmupValue);
        material.SetFloat("_Contrast", warmupValue);
        material.SetFloat("_Rainbow", warmupValue);
        material.SetFloat("_EffectTime", 0f);
        material.SetFloat("_Flash", warmupValue);
        material.SetFloat("_Vignette", warmupValue);
        material.SetFloat("_Zoom", warmupValue);
        material.SetFloat("_Glitch", warmupValue);
        material.SetFloat("_TVNoise", warmupValue);
        material.SetTexture("_HistoryTex", trailTexture != null
            ? trailTexture
            : Texture2D.blackTexture);
        material.SetTexture("_HistoryTex2", trailTexture2 != null
            ? trailTexture2
            : Texture2D.blackTexture);
        material.SetTexture("_HistoryTex3", trailTexture3 != null
            ? trailTexture3
            : Texture2D.blackTexture);

        var descriptor = source.descriptor;
        descriptor.width = 64;
        descriptor.height = 64;
        descriptor.depthBufferBits = 0;
        descriptor.msaaSamples = 1;
        var warmupTarget = RenderTexture.GetTemporary(descriptor);
        Graphics.Blit(source, warmupTarget, material);
        RenderTexture.ReleaseTemporary(warmupTarget);
        SetMaterialDefaults();
    }

    private void SetMaterialDefaults()
    {
        material.SetFloat("_Blur", 0f);
        material.SetFloat("_Neon", 0f);
        material.SetFloat("_Fade", 0f);
        material.SetFloat("_Trail", 0f);
        material.SetFloat("_Brightness", 0f);
        material.SetFloat("_Saturation", 0f);
        material.SetFloat("_Contrast", 0f);
        material.SetFloat("_Rainbow", 0f);
        material.SetFloat("_EffectTime", 0f);
        material.SetFloat("_Flash", 0f);
        material.SetFloat("_Vignette", 0f);
        material.SetFloat("_Zoom", 0f);
        material.SetFloat("_Glitch", 0f);
        material.SetFloat("_TVNoise", 0f);
        material.SetTexture("_HistoryTex", Texture2D.blackTexture);
        material.SetTexture("_HistoryTex2", Texture2D.blackTexture);
        material.SetTexture("_HistoryTex3", Texture2D.blackTexture);
    }

    private EffectTrack CreateTrack(string effect, bool triangularEnvelope)
        => new(effectsByType.TryGetValue(effect, out var result) ? result : null,
            triangularEnvelope);

    private sealed class EffectTrack
    {
        private readonly List<EffectChange> events;
        private readonly bool triangularEnvelope;
        private int cursor;
        private float lastTime = float.MinValue;

        public EffectTrack(List<EffectChange> events, bool triangularEnvelope)
        {
            this.events = events;
            this.triangularEnvelope = triangularEnvelope;
        }

        public float Evaluate(float time)
        {
            if (events == null || events.Count == 0)
                return 0f;

            if (time < lastTime)
                cursor = 0;
            lastTime = time;

            while (cursor < events.Count &&
                   events[cursor].time + events[cursor].duration < time)
                cursor++;

            var result = 0f;
            for (var i = cursor; i < events.Count; i++)
            {
                var item = events[i];
                if (item.time > time)
                    break;

                var progress = item.duration <= 0f
                    ? 1f
                    : (time - (float)item.time) / item.duration;
                var envelope = triangularEnvelope
                    ? 1f - Mathf.Abs(progress * 2f - 1f)
                    : Mathf.Clamp01(Mathf.Min(progress * 10f, (1f - progress) * 10f));
                var value = item.intensity * envelope;
                if (Mathf.Abs(value) > Mathf.Abs(result))
                    result = value;
            }
            return result;
        }

        public bool IsUpcoming(float time, float leadTime)
        {
            if (events == null || cursor >= events.Count)
                return false;

            var delta = (float)events[cursor].time - time;
            return delta >= 0f && delta <= leadTime;
        }
    }

    private void EnsureTrail(RenderTexture source)
    {
        if (trailTexture != null &&
            trailTexture.width == source.width &&
            trailTexture.height == source.height)
            return;

        ReleaseTrail();
        trailTexture = CreateTrailTexture(source);
        trailTexture2 = CreateTrailTexture(source);
        trailTexture3 = CreateTrailTexture(source);
        Graphics.Blit(Texture2D.blackTexture, trailTexture);
        Graphics.Blit(Texture2D.blackTexture, trailTexture2);
        Graphics.Blit(Texture2D.blackTexture, trailTexture3);
        trailFrameCounter = 0;
    }

    private void PrepareUpcomingTrail(RenderTexture source, float time)
    {
        if (!hasTrailEvents || !trailEffects.IsUpcoming(time, 0.5f))
        {
            trailSeedStage = 0;
            return;
        }

        EnsureTrail(source);
        switch (trailSeedStage)
        {
            case 0:
                Graphics.Blit(source, trailTexture);
                break;
            case 1:
                Graphics.Blit(source, trailTexture2);
                break;
            case 2:
                Graphics.Blit(source, trailTexture3);
                break;
        }
        if (trailSeedStage < 3)
            trailSeedStage++;
        trailFrameCounter = 0;
    }

    private void FinishTrailSeed(RenderTexture source)
    {
        if (trailSeedStage < 1)
            Graphics.Blit(source, trailTexture);
        if (trailSeedStage < 2)
            Graphics.Blit(source, trailTexture2);
        if (trailSeedStage < 3)
            Graphics.Blit(source, trailTexture3);
        trailSeedStage = 3;
        trailFrameCounter = 0;
    }

    private static RenderTexture CreateTrailTexture(RenderTexture source)
    {
        var texture = new RenderTexture(source.descriptor)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        texture.Create();
        return texture;
    }

    private void ReleaseTrail()
    {
        if (trailTexture == null)
            return;
        trailTexture.Release();
        Destroy(trailTexture);
        if (trailTexture2 != null)
        {
            trailTexture2.Release();
            Destroy(trailTexture2);
        }
        if (trailTexture3 != null)
        {
            trailTexture3.Release();
            Destroy(trailTexture3);
        }
        trailTexture = null;
        trailTexture2 = null;
        trailTexture3 = null;
        trailFrameCounter = 0;
    }

    private void OnDestroy()
    {
        ReleaseTrail();
        if (material != null)
            Destroy(material);
    }
}
