using System;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-10000)]
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
    private EffectTrack hueEffects;
    private EffectTrack tintEffects;
    private EffectTrack moveEffects;
    private EffectTrack rotateEffects;
    private EffectTrack shakeEffects;
    private readonly Dictionary<EffectChange, Color> tintColors = new();
    private readonly List<RotationTarget> rotationTargets = new();
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
    private float gameplayRotationDegrees;
    private int rotationTargetRefreshFrame = -1;

    public bool IsPreparedForRecording => !enabled || material == null || !needsWarmup;

    public void Configure(List<EffectChange> effectEvents, AudioTimeProvider provider)
    {
        timeProvider = provider;
        ApplyGameplayRotation(0f);
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
        hueEffects = CreateTrack("Hue", true);
        tintEffects = CreateTrack("Tint", true);
        moveEffects = CreateTrack("Move", true);
        rotateEffects = CreateTrack("Rotate", true);
        shakeEffects = CreateTrack("Shake", true);
        tintColors.Clear();
        foreach (var item in effects)
        {
            if (item.effect != "Tint" || string.IsNullOrEmpty(item.color))
                continue;
            if (ColorUtility.TryParseHtmlString("#" + item.color, out var parsed))
                tintColors[item] = parsed;
        }
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

    private void Update()
    {
        EnsureRotationTargets();
        ResetRotationTargets();
    }

    private void LateUpdate()
    {
        if (timeProvider == null || rotateEffects == null)
        {
            gameplayRotationDegrees = 0f;
            return;
        }

        gameplayRotationDegrees = rotateEffects.Evaluate(timeProvider.AudioTime);
    }

    private void OnPreCull()
    {
        ApplyGameplayRotation(gameplayRotationDegrees);
    }

    public bool PrepareForRecording()
    {
        if (!enabled || material == null)
            return false;
        ReleaseTrail();
        trailWasActive = false;
        trailSeedStage = 0;
        needsWarmup = true;
        return true;
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
        var zoom = Mathf.Clamp(zoomEffects.Evaluate(time), 0f, 8f);
        var glitch = Mathf.Clamp01(glitchEffects.Evaluate(time));
        var tvNoise = Mathf.Clamp01(tvNoiseEffects.Evaluate(time));
        var hue = rotationDegreesToRadians(hueEffects.Evaluate(time));
        var tintAmount = Mathf.Clamp01(tintEffects.Evaluate(time, out var tintEvent));
        var tintColor = Color.black;
        if (tintAmount > 0.001f &&
            (tintEvent == null || !tintColors.TryGetValue(tintEvent, out tintColor)))
            tintAmount = 0f;
        moveEffects.EvaluateVector(time, out var offsetX, out var offsetY);
        var shake = shakeEffects.Evaluate(time, out var shakeEvent);
        if (shakeEvent != null && Mathf.Abs(shake) > 0.0001f)
        {
            // Strength 1 moves the frame by roughly five percent.
            var frequency = shakeEvent.paramA > 0f ? shakeEvent.paramA : 18f;
            var phase = time * frequency;
            if (shakeEvent.hasDirection)
            {
                var displacement = (Mathf.PerlinNoise(phase, 0.37f) - 0.5f) * 2f * shake * 0.05f;
                offsetX += Mathf.Cos(shakeEvent.paramB) * displacement;
                offsetY += Mathf.Sin(shakeEvent.paramB) * displacement;
            }
            else
            {
                offsetX += (Mathf.PerlinNoise(phase, 0.37f) - 0.5f) * 2f * shake * 0.05f;
                offsetY += (Mathf.PerlinNoise(0.71f, phase) - 0.5f) * 2f * shake * 0.05f;
            }
        }

        if (gaussian <= 0.001f && neon <= 0.001f && trail <= 0.001f && Mathf.Abs(flash) <= 0.001f &&
            brightness <= 0.001f && saturation <= 0.001f && contrast <= 0.001f &&
            rainbow <= 0.001f && vignette <= 0.001f &&
            zoom <= 0.001f && glitch <= 0.001f && tvNoise <= 0.001f &&
            Mathf.Abs(hue) <= 0.0001f && tintAmount <= 0.001f &&
            Mathf.Abs(offsetX) <= 0.0001f && Mathf.Abs(offsetY) <= 0.0001f)
        {
            trailWasActive = false;
            PrepareUpcomingTrail(source, time);
            if (!materialIsDefault)
            {
                SetMaterialDefaults();
                materialIsDefault = true;
            }
            Graphics.Blit(source, destination);
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
        material.SetFloat("_Hue", hue);
        material.SetColor("_TintColor",
            new Color(tintColor.r, tintColor.g, tintColor.b, tintAmount));
        material.SetFloat("_OffsetX", offsetX);
        material.SetFloat("_OffsetY", offsetY);
        material.SetFloat("_Rotate", 0f);
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
        material.SetFloat("_Hue", warmupValue);
        material.SetColor("_TintColor", new Color(0f, 0f, 0f, warmupValue));
        material.SetFloat("_OffsetX", warmupValue);
        material.SetFloat("_OffsetY", warmupValue);
        material.SetFloat("_Rotate", 0f);
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
        // Shader compilation does not depend on output dimensions. A fixed small
        // target avoids a second full-resolution render allocation before capture.
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
        material.SetFloat("_Hue", 0f);
        material.SetColor("_TintColor", new Color(0f, 0f, 0f, 0f));
        material.SetFloat("_OffsetX", 0f);
        material.SetFloat("_OffsetY", 0f);
        material.SetFloat("_Rotate", 0f);
        material.SetTexture("_HistoryTex", Texture2D.blackTexture);
        material.SetTexture("_HistoryTex2", Texture2D.blackTexture);
        material.SetTexture("_HistoryTex3", Texture2D.blackTexture);
    }

    private EffectTrack CreateTrack(string effect, bool triangularEnvelope)
        => new(effectsByType.TryGetValue(effect, out var result) ? result : null,
            triangularEnvelope);

    private static float rotationDegreesToRadians(float degrees)
        => degrees * Mathf.Deg2Rad;

    private void ApplyGameplayRotation(float degrees)
    {
        EnsureRotationTargets();
        gameplayRotationDegrees = degrees;
        var offset = Quaternion.Euler(0f, 0f, degrees);
        foreach (var target in rotationTargets)
        {
            if (target.Transform != null)
                target.Transform.localRotation = target.BaseRotation * offset;
        }
    }

    private void EnsureRotationTargets()
    {
        if (rotationTargets.Count > 0 &&
            !rotationTargets.Exists(target => target.Transform == null) &&
            Time.frameCount < rotationTargetRefreshFrame)
            return;

        RefreshRotationTargets();
        rotationTargetRefreshFrame = Time.frameCount + 30;
    }

    private void ResetRotationTargets()
    {
        foreach (var target in rotationTargets)
        {
            if (target.Transform != null)
                target.Transform.localRotation = target.BaseRotation;
        }
    }

    private void RefreshRotationTargets()
    {
        foreach (var target in rotationTargets)
        {
            if (target.Transform != null)
                target.Transform.localRotation = target.BaseRotation;
        }
        rotationTargets.Clear();

        AddRotationTarget(GameObject.Find("Notes")?.transform);
        AddRotationTarget(GameObject.Find("NoteEffects")?.transform);
        AddRotationTarget(GameObject.Find("FireworkEffect")?.transform);

        var outline = GameObject.Find("Outline") ?? GameObject.Find("DebugOutline");
        AddRotationTarget(outline?.transform);
        AddRotationTarget(GameObject.Find("JudgeAreaOverlay")?.transform);
    }

    private void AddRotationTarget(Transform target)
    {
        if (target == null)
            return;
        foreach (var existing in rotationTargets)
        {
            if (target == existing.Transform || target.IsChildOf(existing.Transform))
                return;
        }
        rotationTargets.Add(new RotationTarget(target, target.localRotation));
    }

    private sealed class RotationTarget
    {
        public readonly Transform Transform;
        public readonly Quaternion BaseRotation;

        public RotationTarget(Transform transform, Quaternion baseRotation)
        {
            Transform = transform;
            BaseRotation = baseRotation;
        }
    }

    private sealed class EffectTrack
    {
        private readonly List<EffectChange> allEvents;
        private readonly List<EffectChange> legacyEvents = new();
        private readonly List<StateSegment> stateSegments = new();
        private readonly bool triangularEnvelope;
        private int cursor;
        private int stateCursor;
        private int upcomingCursor;
        private float lastTime = float.MinValue;
        private float lastUpcomingTime = float.MinValue;

        public EffectTrack(List<EffectChange> events, bool triangularEnvelope)
        {
            allEvents = events;
            this.triangularEnvelope = triangularEnvelope;
            if (events == null)
                return;

            EffectChange activeSource = null;
            foreach (var item in events)
            {
                if (!item.stateful)
                {
                    legacyEvents.Add(item);
                    continue;
                }

                var startValue = stateSegments.Count == 0
                    ? 0f
                    : stateSegments[^1].ValueAt((float)item.time);
                var source = item.enabled
                    ? item
                    : activeSource ?? (stateSegments.Count > 0 ? stateSegments[^1].Source : null);
                var startParamA = stateSegments.Count == 0
                    ? 0f
                    : stateSegments[^1].ParamAAt((float)item.time);
                var startParamB = stateSegments.Count == 0
                    ? 0f
                    : stateSegments[^1].ParamBAt((float)item.time);
                stateSegments.Add(new StateSegment(item, startValue,
                    item.enabled ? item.intensity : 0f, source,
                    startParamA, startParamB,
                    item.enabled ? item.paramA : 0f,
                    item.enabled ? item.paramB : 0f));
                activeSource = item.enabled ? item : null;
            }
        }

        public float Evaluate(float time) => Evaluate(time, out _);

        public float Evaluate(float time, out EffectChange dominant)
        {
            dominant = null;
            if ((allEvents == null || allEvents.Count == 0))
                return 0f;

            if (time < lastTime)
            {
                cursor = 0;
                stateCursor = 0;
            }
            lastTime = time;

            while (cursor < legacyEvents.Count &&
                   legacyEvents[cursor].time + legacyEvents[cursor].duration < time)
                cursor++;

            var result = 0f;
            for (var i = cursor; i < legacyEvents.Count; i++)
            {
                var item = legacyEvents[i];
                if (item.time > time)
                    break;

                float envelope;
                if (item.attack >= 0f)
                {
                    // Legacy effects use an attack, hold, and release envelope.
                    var elapsed = time - (float)item.time;
                    if (elapsed < item.attack)
                        envelope = item.attack <= 0f ? 1f : elapsed / item.attack;
                    else if (elapsed <= item.attack + item.holdTime)
                        envelope = 1f;
                    else if (item.release > 0f)
                        envelope = Mathf.Clamp01(
                            1f - (elapsed - item.attack - item.holdTime) / item.release);
                    else
                        envelope = 0f;
                }
                else
                {
                    var progress = item.duration <= 0f
                        ? 1f
                        : (time - (float)item.time) / item.duration;
                    envelope = triangularEnvelope
                        ? 1f - Mathf.Abs(progress * 2f - 1f)
                        : Mathf.Clamp01(Mathf.Min(progress * 10f, (1f - progress) * 10f));
                }
                var value = item.intensity * envelope;
                if (Mathf.Abs(value) > Mathf.Abs(result))
                {
                    result = value;
                    dominant = item;
                }
            }

            while (stateCursor + 1 < stateSegments.Count &&
                   stateSegments[stateCursor + 1].Time <= time)
                stateCursor++;

            if (stateCursor < stateSegments.Count && stateSegments[stateCursor].Time <= time)
            {
                var segment = stateSegments[stateCursor];
                var stateValue = segment.ValueAt(time);
                if (Mathf.Abs(stateValue) > Mathf.Abs(result))
                {
                    result = stateValue;
                    dominant = Mathf.Abs(stateValue) > 0.0001f ? segment.Source : null;
                }
            }
            return result;
        }

        public void EvaluateVector(float time, out float x, out float y)
        {
            var amount = Evaluate(time, out var dominant);
            if (stateCursor < stateSegments.Count && stateSegments[stateCursor].Time <= time)
            {
                var segment = stateSegments[stateCursor];
                x = segment.ParamAAt(time);
                y = segment.ParamBAt(time);
                return;
            }

            x = dominant == null ? 0f : dominant.paramA * amount;
            y = dominant == null ? 0f : dominant.paramB * amount;
        }

        public bool IsUpcoming(float time, float leadTime)
        {
            if (allEvents == null || allEvents.Count == 0)
                return false;

            if (time < lastUpcomingTime)
                upcomingCursor = 0;
            lastUpcomingTime = time;
            while (upcomingCursor < allEvents.Count && allEvents[upcomingCursor].time < time)
                upcomingCursor++;
            if (upcomingCursor >= allEvents.Count)
                return false;

            var delta = (float)allEvents[upcomingCursor].time - time;
            return delta >= 0f && delta <= leadTime;
        }

        private sealed class StateSegment
        {
            private readonly float startValue;
            private readonly float targetValue;
            private readonly float startParamA;
            private readonly float startParamB;
            private readonly float targetParamA;
            private readonly float targetParamB;
            private readonly float transition;

            public StateSegment(EffectChange item, float startValue, float targetValue,
                EffectChange source, float startParamA, float startParamB,
                float targetParamA, float targetParamB)
            {
                Time = (float)item.time;
                this.startValue = startValue;
                this.targetValue = targetValue;
                this.startParamA = startParamA;
                this.startParamB = startParamB;
                this.targetParamA = targetParamA;
                this.targetParamB = targetParamB;
                transition = Mathf.Max(0f, item.transition);
                Source = source;
            }

            public float Time { get; }
            public EffectChange Source { get; }

            public float ValueAt(float time)
            {
                if (transition <= 0f)
                    return targetValue;
                return Mathf.Lerp(startValue, targetValue,
                    Mathf.Clamp01((time - Time) / transition));
            }

            public float ParamAAt(float time) => Interpolate(startParamA, targetParamA, time);
            public float ParamBAt(float time) => Interpolate(startParamB, targetParamB, time);

            private float Interpolate(float start, float target, float time)
            {
                if (transition <= 0f)
                    return target;
                return Mathf.Lerp(start, target,
                    Mathf.Clamp01((time - Time) / transition));
            }
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
        ApplyGameplayRotation(0f);
        ReleaseTrail();
        if (material != null)
            Destroy(material);
    }
}
