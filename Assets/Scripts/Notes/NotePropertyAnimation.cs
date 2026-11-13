using System.Collections.Generic;
using MajdataCore;
using UnityEngine;

// Restore the previous render pose before any note updates its own animation.
[DefaultExecutionOrder(-8000)]
public sealed class NotePropertyAnimation : MonoBehaviour
{
    private NoteDrop note;
    private JsonDataLoader loader;
    private AudioTimeProvider clock;
    private bool initialized;
    private bool targetsDirty = true;
    private double appearance;
    private float baseSpeed;
    private NoteValueTransition hs, spawn, destroy;
    private string slideFadeCurve;
    private double slideFadeOrigin, slideFadeDistance;
    private readonly List<Transform> bodyTargets = new();
    private readonly List<Transform> guideTargets = new();
    private readonly List<(Transform Target, Quaternion Rotation, Vector3 Position, bool Moved)> originalPoses = new();
    public bool HandlesSize { get; private set; }
    public bool HandlesAlpha { get; private set; }
    public bool HandlesColor { get; private set; }

    internal void Configure(NoteDrop source, JsonDataLoader dataLoader, AudioTimeProvider provider)
    { note = source; loader = dataLoader; clock = provider; }

    public void InvalidateAngleTargets() => targetsDirty = true;

    private void Start()
    {
        if (!initialized)
        {
            initialized = true;
            hs = loader.FindBirthHs(note);
            spawn = loader.FindBirthSpawn(note);
            destroy = loader.FindBirthDestroy(note);
            HandlesSize = loader.HasSizeAnimations;
            HandlesAlpha = loader.HasAlphaAnimations;
            HandlesColor = loader.HasColorAnimations;
            baseSpeed = note.VisualNoteType is "touch" or "touchhold" ? loader.touchSpeed : loader.noteSpeed;
            appearance = loader.GetAnimationAppearance(note);
            if (hs != null)
            {
                if (note.VisualNoteType == "slide")
                {
                    // HS*slide controls appearance, never the path's movement clock.
                    var originalSpeed = note.speed;
                    note.speed = baseSpeed;
                    var lead = note.VisualStateTime - loader.GetAnimationAppearance(note);
                    note.speed = originalSpeed;
                    slideFadeCurve = SvController.CreateBirthSpeedCurve(note.scrollType,note.VisualStateTime,
                        lead,hs,note.time+note.TailDuration,ref appearance,ignoreSv:true);
                    slideFadeOrigin = SvController.GetCumulativeScroll(appearance,slideFadeCurve);
                    slideFadeDistance = SvController.GetCumulativeScroll(note.VisualStateTime,slideFadeCurve)-slideFadeOrigin;
                    return;
                }
                var distance = note.VisualNoteType is "touch" or "touchhold"
                    ? AlphaVisualTiming.GetTouchMotionDuration(baseSpeed)
                    : (Mathf.Abs(note.destroyRadius-note.spawnRadius)+AlphaVisualTiming.SpawnScaleDistance)/baseSpeed;
                note.scrollType = SvController.CreateBirthSpeedCurve(note.scrollType,note.VisualStateTime,distance,hs,
                    note.time+note.TailDuration+NoteDrop.MissWindow,ref appearance);
                note.speed = baseSpeed;
                note.noteScrollPos = SvController.GetCumulativeScroll(note.time,note.scrollType);
            }
        }
    }

    internal float? SlideFadeProgress(double time)
    {
        if (slideFadeCurve == null) return null;
        if (time >= note.VisualStateTime) return 1f;
        if (time <= appearance || System.Math.Abs(slideFadeDistance)<.000001d) return 0f;
        return Mathf.Clamp01((float)((SvController.GetCumulativeScroll(time,slideFadeCurve)-slideFadeOrigin)/slideFadeDistance));
    }

    private void Update()
    {
        foreach (var state in originalPoses)
        {
            if (state.Target == null) continue;
            state.Target.localRotation = state.Rotation;
            if (state.Moved) state.Target.localPosition = state.Position;
        }
        originalPoses.Clear();
        var elapsed = clock.AudioTime - appearance;
        if (spawn != null) note.spawnRadius = spawn.Evaluate(elapsed);
        if (destroy != null) note.destroyRadius = destroy.Evaluate(elapsed);
        if (HandlesSize)
        {
            note.ApplyLiveScale(loader.AnimatedSize(note, clock.AudioTime, appearance, false));
            note.ApplyLiveGuideStarScale(loader.AnimatedSize(note, clock.AudioTime, appearance, true));
        }
        if (HandlesColor)
        {
            note.ApplyLiveColor(loader.AnimatedColor(note,clock.AudioTime,appearance,false));
            note.ApplyLiveGuideStarColor(loader.AnimatedColor(note,clock.AudioTime,appearance,true));
        }
        if (HandlesAlpha)
        {
            note.ApplyLiveAlpha(loader.AnimatedAlpha(note, clock.AudioTime, appearance, false));
            note.ApplyLiveGuideStarAlpha(loader.AnimatedAlpha(note, clock.AudioTime, appearance, true));
            if (note.TapLineRenderer != null)
                note.ApplyLiveTapLineAlpha(loader.ResolveTapLineAlpha(note, clock.AudioTime, appearanceTime: appearance));
        }
    }

    private void LateUpdate()
    {
        if (!loader.HasAngleAnimations) return;
        if (targetsDirty)
        {
            targetsDirty = false;
            bodyTargets.Clear(); guideTargets.Clear();
            note.CollectAngleTargets(bodyTargets, guideTargets);
        }
        ApplyAngle(bodyTargets, loader.AnimatedAngle(note, clock.AudioTime, appearance, false));
        ApplyAngle(guideTargets, loader.AnimatedAngle(note, clock.AudioTime, appearance, true));
    }

    private void ApplyAngle(List<Transform> targets, float degrees)
    {
        if (degrees == 0f) return;
        var offset = Quaternion.Euler(0f, 0f, degrees);
        foreach (var target in targets)
        {
            if (target == null) continue;
            var headOffset = target == note.transform ? note.AngleHeadOffset : 0f;
            var original = target.localRotation;
            originalPoses.Add((target, original, target.localPosition, headOffset != 0f));
            target.localRotation *= offset;
            if (headOffset != 0f)
            {
                var head = Vector3.up * (headOffset * target.localScale.y);
                target.localPosition += original * head - target.localRotation * head;
            }
        }
    }
}
