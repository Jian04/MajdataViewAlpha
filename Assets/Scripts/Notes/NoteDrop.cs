using Assets.Scripts.Types;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using MajdataCore;
#nullable enable
public class NoteDrop : MonoBehaviour
{
    /// <summary>
    /// A note is built part way through a frame, so Unity does not run its Start
    /// until the next one, and the frame in between still renders it. Every note
    /// prefab ships fully visible, so that frame is the whole chart appearing at
    /// once in its untouched prefab pose: the flash on leaving a paused preview.
    /// Awake runs inside Instantiate, early enough never to be drawn.
    ///
    /// Each note is hidden the same way its own Start hides it, so every renderer
    /// touched here is one that note is already known to reveal again. Reaching
    /// for a different switch would risk hiding something nothing turns back on:
    /// nothing, for one, ever clears forceRenderingOff on a touch note's fans.
    /// </summary>
    /// <summary>
    /// An image file from the chart's folder that replaces this one Note's skin,
    /// written as <c>1~[star.png]</c>. Empty leaves the prefab's own skin alone.
    /// </summary>
    public string customSkin = string.Empty;

    /// <summary>
    /// Resolves <see cref="customSkin"/> into a sprite drawn at the same size as
    /// <paramref name="model"/>, or null when this Note has no skin of its own.
    /// </summary>
    /// <remarks>
    /// Callers replace their own sprite fields with the result rather than assigning
    /// a renderer, because a Note picks between its normal, each, break and EX
    /// pictures as it runs; overwriting a renderer would be undone the moment it
    /// picked again.
    ///
    /// A skin that cannot be loaded is reported rather than ignored: a Note silently
    /// wearing the wrong picture is the kind of thing only noticed on stage. The Note
    /// keeps its default skin either way, so it stays playable.
    /// </remarks>
    protected Sprite? ResolveCustomSkin(Sprite? model)
    {
        if (string.IsNullOrEmpty(customSkin))
            return null;
        var sprite = NoteSkinLibrary.TryCreateSprite(customSkin, model, out var reason);
        if (sprite == null)
            ReportUnrenderable(reason);
        return sprite;
    }

    internal static void HideSpriteUntilInitialized(Transform note)
    {
        if (note.TryGetComponent<SpriteRenderer>(out var own))
            own.forceRenderingOff = true;
        if (note.childCount > 0 &&
            note.GetChild(0).TryGetComponent<SpriteRenderer>(out var ex))
            ex.forceRenderingOff = true;
    }

    /// <inheritdoc cref="HideSpriteUntilInitialized"/>
    /// <remarks>
    /// A slide prefab carries its whole path: Star_Circle_1 has 64 bar renderers,
    /// every one shipped opaque, which makes this the most visible part of the
    /// flash. Initialize clears them by alpha, so alpha is what is cleared here.
    ///
    /// The judge mark is the last child and Initialize deactivates it instead. It
    /// is left alone: deactivating a child before Unity has run its own Awake
    /// defers that Awake to activation, and one frame of one small mark is not
    /// worth changing another object's lifecycle.
    /// </remarks>
    internal static void HideSlideBarsUntilInitialized(Transform slide)
    {
        for (var i = 0; i < slide.childCount - 1; i++)
            if (slide.GetChild(i).TryGetComponent<SpriteRenderer>(out var bar))
                bar.color = new Color(1f, 1f, 1f, 0f);
    }

    /// <inheritdoc cref="HideSpriteUntilInitialized"/>
    internal static void HideFansUntilInitialized(GameObject[]? fans)
    {
        if (fans == null)
            return;
        foreach (var fan in fans)
            if (fan != null &&
                fan.TryGetComponent<SpriteRenderer>(out var renderer))
                renderer.color = new Color(1f, 1f, 1f, 0f);
    }

    public const float DefaultSpawnRadius = AlphaVisualTiming.DefaultSpawnRadius;
    public const float DefaultDestroyRadius = AlphaVisualTiming.DefaultDestroyRadius;
    protected const float SpawnScaleDistance = AlphaVisualTiming.SpawnScaleDistance;
    protected const float GameplayRevealTime = AlphaVisualTiming.GameplayRevealTime;

    public int startPosition;
    public float time;
    public int noteSortOrder;
    public float speed = 7;
    public bool isEach;
    public bool isEachInStream;
    public double noteScrollPos; // cumulative scroll at note's judge time
    public string scrollType;
    public bool previewOnly;
    public bool isFake;

    // Where this note came from in the chart text, so a note that builds but
    // cannot be drawn can be marked in the editor instead of just going missing.
    [System.NonSerialized] public JsonDataLoader renderReporter;
    [System.NonSerialized] public int sourceLine;
    [System.NonSerialized] public int sourceColumn;
    [System.NonSerialized] public double sourceTime;
    [System.NonSerialized] public string sourceContent = string.Empty;
    private bool reportedUnrenderable;

    /// <summary>
    /// Says out loud that this note exists but cannot be seen. Silence here is
    /// what makes a legal chart look like it lost a note for no reason.
    /// </summary>
    protected void ReportUnrenderable(string reason)
    {
        if (reportedUnrenderable || previewOnly || renderReporter == null)
            return;
        reportedUnrenderable = true;
        renderReporter.ReportUnrenderable(
            sourceLine, sourceColumn, sourceTime, sourceContent, reason);
    }
    /// <summary>
    /// How long a note nobody hit stays on screen past its judgement time.
    /// </summary>
    /// <remarks>
    /// A note is destroyed by the branch that calls it a miss, so this is also
    /// the whole visual difference between a note that was hit and one that was
    /// not: it keeps travelling outward for this long and then goes. A fake note
    /// is never judged, so its own lifetime has to be this same number, or it
    /// outlives a miss and walks off the playfield.
    /// </remarks>
    public const float MissWindow = 0.15f;
    protected bool JudgmentDisabled => previewOnly || isFake;
    protected bool JudgmentSuspended =>
        timeProvider == null || !timeProvider.isStart;
    protected bool IsPausedTimelinePreview =>
        previewOnly &&
        timeProvider != null &&
        timeProvider.IsPreview &&
        timeProvider.IsPaused;
    public bool isDZone;
    public float spawnRadius = DefaultSpawnRadius;
    public float destroyRadius = DefaultDestroyRadius;
    public SpawnVisualMode spawnMode = SpawnVisualMode.Rewind;
    public float bounceDuration;
    public double bounceStartTime;
    public float bounceHSpeedMultiplier = 1f;
    public float bounceDirection = 1f;

    private static Material liveVisualMaterial;
    private readonly Dictionary<SpriteRenderer, LiveRendererDefaults> liveRendererDefaults = new();

    protected AudioTimeProvider timeProvider;

    public NoteStatus State { get; protected set; } = NoteStatus.Start;
    protected SensorType sensorPos;
    protected Sensor sensor;
    protected SensorManager manager;
    protected InputManager inputManager;
    protected NoteManager noteManager;
    protected Guid guid = Guid.NewGuid();
    protected bool isJudged = false;
    protected JudgeType judgeResult;
    protected ObjectCounter objectCounter;

    internal void BindSceneContext(AudioTimeProvider clock, ObjectCounter counter, NoteManager registry)
    {
        timeProvider = clock;
        objectCounter = counter;
        noteManager = registry;
    }
    
    /// <summary>
    /// Gets the time from the current moment to the correct judgement frame
    /// </summary>
    /// <returns>
    /// Positive when the current time is after the correct judgement frame
    /// <para>Negative when the current time is before the correct judgement frame</para>
    /// </returns>
    protected float GetJudgeTiming() => timeProvider.AudioTime - time;

    private float lastObservedAudioTime = float.NaN;
    /// <summary>
    /// True on the frame the clock moved backwards, once per jump.
    /// </summary>
    /// <remarks>
    /// A ring note needs nothing here: its position and visibility are read
    /// from the clock every frame, so scrubbing backwards puts it back on its
    /// own. Touch and slide notes latch state on the way forward - a triggered
    /// head, a consumed bar, a registered sensor - and that state has to be
    /// undone when the timeline is dragged back before it, or the note keeps
    /// what it learned at a time that has not happened yet.
    /// </remarks>
    protected bool ClockMovedBackwards()
    {
        var now = timeProvider == null ? 0f : timeProvider.AudioTime;
        var rewound = !float.IsNaN(lastObservedAudioTime) &&
                      now + 0.001f < lastObservedAudioTime;
        lastObservedAudioTime = now;
        return rewound;
    }
    protected float GetTouchVisualTiming()
        => AlphaVisualTiming.GetTouchVisualTiming(
            noteScrollPos,
            SvController.GetCumulativeScroll(
                timeProvider.AudioTime, scrollType),
            speed);
    protected float GetSvDistance()
    {
        return SvController.GetVisualRadius(
            noteScrollPos,
            speed,
            timeProvider.AudioTime,
            spawnRadius,
            destroyRadius,
            scrollType);
    }
    protected float GetSpawnScale(float distance)
        => AlphaVisualTiming.GetSpawnScale(
            distance, spawnRadius, destroyRadius);
    public void ConfigureBounce(float hSpeedMultiplier)
    {
        bounceHSpeedMultiplier = hSpeedMultiplier;
        bounceDirection = SvController.GetBounceDirection(
            time, hSpeedMultiplier, scrollType);
        bounceStartTime = SvController.GetBounceStartTime(
            time, bounceDuration, hSpeedMultiplier, scrollType);
    }
    protected bool IsBeforeBounceWindow() =>
        bounceDuration > 0f &&
        timeProvider.AudioTime < time &&
        !IsBounceActive();
    protected bool IsBounceActive()
    {
        if (bounceDuration <= 0f ||
            timeProvider.AudioTime < bounceStartTime ||
            timeProvider.AudioTime >= time)
            return false;
        return spawnMode == SpawnVisualMode.Once ||
               GetBounceProgress(timeProvider.AudioTime) >=
               -(float)AlphaVisualTiming.Epsilon;
    }
    protected float GetBounceProgress(double currentTime) =>
        SvController.GetBounceProgress(
            time, bounceDuration, bounceHSpeedMultiplier, bounceDirection,
            currentTime, scrollType);
    protected float GetBounceDistance()
    {
        var progress = GetBounceProgress(timeProvider.AudioTime);
        var fromApex = progress * 2f - 1f;
        // Both ends of the bounce sit on the judgement ring; the excursion is
        // how far it leaves the ring in between, largest at the apex.
        var excursion = (destroyRadius - spawnRadius) * (1f - fromApex * fromApex);
        // A negative bounce turns the dip inward into a bulge outward. The
        // radius stays positive either way: getPositionFromDistance scales the
        // key's direction vector by it, so a negative radius does not reverse
        // the bounce, it moves the note to the key on the far side of the
        // playfield - a bounce on key 1 was landing on key 5.
        return bounceDirection < 0f
            ? destroyRadius + excursion
            : destroyRadius - excursion;
    }
    /// <summary>
    /// This note's own spawn ring. A note with a second ring - a hold's tail - owns
    /// a second memo, because one memo per ring is what keeps them from resetting
    /// each other every frame.
    /// </summary>
    protected SpawnCrossingMemo spawnCrossingMemo;
    protected SpawnPresentation GetSpawnPresentation(
        float distance,
        double targetScrollPos,
        ref SpawnCrossingMemo memo)
    {
        var isPastSpawnNow = SvController.IsPastSpawnNow(
            targetScrollPos,
            speed,
            timeProvider.AudioTime,
            spawnRadius,
            scrollType,
            destroyRadius);
        // Only SPAWNMODE=once cares whether the note was ever past the ring; the
        // default mode asks the note where it is right now. Working it out either
        // way meant every chart paid for a search of its whole elapsed scroll
        // curve, once per note per frame, for an answer it then threw away.
        var running = isPastSpawnNow ||
            (spawnMode == SpawnVisualMode.Once &&
             SvController.HasEverCrossedSpawn(
                 ref memo,
                 targetScrollPos,
                 speed,
                 timeProvider.AudioTime,
                 spawnRadius,
                 scrollType,
                 destroyRadius));
        var pendingScale = Mathf.Clamp01(GetSpawnScale(distance));
        // The radius has to be gated on the same condition that decides whether the
        // note is running. Keying it on "ever crossed" let a rewinding note keep
        // following the integrated radius past the spawn ring to the far side.
        return new SpawnPresentation(
            running,
            running || pendingScale > 0f,
            AlphaVisualTiming.GetSpawnPresentationRadius(
                distance, spawnRadius, running),
            running ? 1f : pendingScale);
    }
    protected float GetCurrentVisualDistance()
    {
        var distance = GetSvDistance();
        return State == NoteStatus.Pending ? spawnRadius : distance;
    }

    protected Vector3 getPositionFromDistance(float distance) => getPositionFromDistance(distance, VisualPosition);
    protected Vector3 getPositionFromDistance(float distance,float position)
    {
        return new Vector3(
            distance * Mathf.Cos((position * -2f + 5f) * 0.125f * Mathf.PI),
            distance * Mathf.Sin((position * -2f + 5f) * 0.125f * Mathf.PI));
    }

    // Dn sits between A(n-1) and An; D1 therefore wraps between A8 and A1.
    protected float VisualPosition => isDZone ? startPosition - 0.5f : startPosition;
    // Split judgement queues into 16 keys: A zone uses 1-8 and D zone uses 9-16
    protected int JudgeQueueKey => isDZone ? startPosition + 8 : startPosition;
    // Sensor child order matches the SensorType enum; D1 = 17
    protected int SensorChildIndex => isDZone
        ? (int)SensorType.D1 + startPosition - 1
        : startPosition - 1;
    // D zone has no physical buttons, so bind only sensors to avoid a missing Button exception
    protected void BindJudgeInput(EventHandler<InputEventArgs> checker)
    {
        if (isDZone)
            inputManager.BindSensor(checker, sensorPos);
        else
            inputManager.BindArea(checker, sensorPos);
    }

    protected Vector3 GetCurrentVisualPosition()
    {
        return getPositionFromDistance(GetCurrentVisualDistance());
    }

    // Touch judgement feedback belongs to the fixed sensor layout, not to the
    // ZOOM/MOVE transform applied to the Notes root.
    protected Vector3 GetFixedFeedbackPosition()
        => transform.parent != null && transform.parent.name == "Notes"
            ? transform.localPosition
            : transform.position;

    protected Quaternion GetFixedFeedbackRotation()
        => transform.parent != null && transform.parent.name == "Notes"
            ? transform.localRotation
            : transform.rotation;

    // Feedback objects hang under NoteEffects, which gameplay ZOOM/MOVE/ROTATE
    // transforms. Their coordinates come from the untransformed sensor layout,
    // so they must be mapped into that plane instead of written as world
    // positions, otherwise they stay behind while the notes move.
    protected static void PlaceInFeedbackPlane(
        Transform target, Transform plane, Vector3 planePosition)
    {
        if (target == null)
            return;
        target.position = plane != null
            ? plane.TransformPoint(planePosition)
            : planePosition;
    }

    protected static void PlaceInFeedbackPlane(
        Transform target, Transform plane, Vector3 planePosition,
        Quaternion planeRotation)
    {
        if (target == null)
            return;
        PlaceInFeedbackPlane(target, plane, planePosition);
        target.rotation = plane != null
            ? plane.rotation * planeRotation
            : planeRotation;
    }

    protected static void RotateInFeedbackPlane(
        Transform target, Transform plane, Quaternion planeRotation)
    {
        if (target == null)
            return;
        target.rotation = plane != null
            ? plane.rotation * planeRotation
            : planeRotation;
    }

    protected int GetCurrentVisualPositionIndex()
    {
        return GetSvDistance() < 0f ? (startPosition + 3) % 8 + 1 : startPosition;
    }

    protected Quaternion GetCurrentVisualRotation()
    {
        var position = GetCurrentVisualPositionIndex();
        return Quaternion.Euler(0, 0, -22.5f + -45f * (position - 1));
    }

    public int VisualStreamIndex
    {
        get
        {
            var separator = scrollType?.IndexOf('|') ?? -1;
            return separator > 0 && int.TryParse(scrollType.Substring(0, separator), out var stream)
                ? stream
                : 0;
        }
    }

    public string VisualNoteType => this switch
    {
        StarDrop => "star",
        HoldDrop => "hold",
        TapDrop => "tap",
        TouchHoldDrop => "touchhold",
        Assets.Scripts.TouchBase => "touch",
        SlideDrop => "slide",
        WifiDrop => "slide",
        TouchSlideDrop => "slide",
        TrajectoryCarrierDrop carrier => carrier.carrierVisualType,
        _ => string.Empty
    };

    public double VisualStateTime => this switch
    {
        SlideDrop slide => slide.timeStart,
        WifiDrop wifi => wifi.timeStart,
        TouchSlideDrop touchSlide => touchSlide.timeStart,
        _ => time
    };

    public bool MatchesLiveVisual(int stream, string requestedType)
    {
        if (VisualStreamIndex != stream)
            return false;

        var normalized = (requestedType ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
            return true;
        if (normalized == "mine")
            return IsVisualMine;
        if (normalized == "break")
            return IsVisualBreak;
        if (normalized == "each")
            return isEach;
        return !IsVisualBreak && !isEach && normalized == VisualNoteType;
    }

    public bool IsCurrentlyVisibleForLiveVisual()
    {
        if (!isActiveAndEnabled)
            return false;
        return GetLiveVisualRenderers().Any(renderer =>
            renderer != null && renderer.gameObject.activeInHierarchy &&
            !renderer.forceRenderingOff && renderer.color.a > 0.001f);
    }

    public bool WasVisibleAt(double eventTime)
    {
        var endTime = time + GetVisualLifetimeAfterJudge();
        if (eventTime > endTime + 0.000001d)
            return false;
        if (bounceDuration > 0f &&
            eventTime >= bounceStartTime &&
            eventTime < time)
            return spawnMode == SpawnVisualMode.Once ||
                   GetBounceProgress(eventTime) >=
                   -(float)AlphaVisualTiming.Epsilon;

        var distance = SvController.GetVisualRadius(
            noteScrollPos,
            speed,
            eventTime,
            spawnRadius,
            destroyRadius,
            scrollType);
        // Asked about a past event rather than now, but what is remembered are
        // facts about the curve, so the order the questions arrive in does not
        // matter: a time before a known crossing simply searches again.
        return (spawnMode == SpawnVisualMode.Once &&
                SvController.HasEverCrossedSpawn(
                    ref spawnCrossingMemo,
                    noteScrollPos, speed, eventTime,
                    spawnRadius, scrollType, destroyRadius)) ||
               GetSpawnScale(distance) >= 0f;
    }

    protected readonly struct SpawnPresentation
    {
        public SpawnPresentation(
            bool running,
            bool visible,
            float distance,
            float scale)
        {
            Running = running;
            Visible = visible;
            Distance = distance;
            Scale = scale;
        }

        public bool Running { get; }
        public bool Visible { get; }
        public float Distance { get; }
        public float Scale { get; }
    }

    private int appliedLiveVisualVersion;

    /// <summary>
    /// Every note asks, once a frame, whether the live look it is wearing is still
    /// the current one.
    /// </summary>
    /// <remarks>
    /// Deriving it here rather than being handed it is what makes it impossible to
    /// miss a note: a note born after a COLORV came due, or one that has just built
    /// new renderers, asks on its very next frame like everyone else.
    /// </remarks>
    protected virtual void LateUpdate()
    {
        var live = LiveNoteVisualController.Active;
        if (live == null || live.Version == appliedLiveVisualVersion)
            return;
        appliedLiveVisualVersion = live.Version;
        live.ApplyCurrent(this);
    }

    /// <summary>
    /// Says the renderers this note is wearing its live look on have changed, so
    /// the look has to be put on again.
    /// </summary>
    protected void InvalidateLiveVisual() => appliedLiveVisualVersion = 0;

    public void ApplyLiveColor(Color? color)
    {
        foreach (var renderer in GetLiveVisualRenderers())
        {
            if (renderer == null ||
                (!color.HasValue && !liveRendererDefaults.ContainsKey(renderer)))
                continue;
            var state = CaptureRendererDefaults(renderer);
            state.CurrentColor = color ?? state.Color;
            state.HasColorOverride = color.HasValue;
            liveRendererDefaults[renderer] = state;
            ApplyOrRestore(renderer, state);
        }
    }

    public void ApplyLiveAlpha(float? alpha)
    {
        foreach (var renderer in GetLiveVisualRenderers())
        {
            if (renderer == null ||
                (!alpha.HasValue && !liveRendererDefaults.ContainsKey(renderer)))
                continue;
            var state = CaptureRendererDefaults(renderer);
            state.CurrentAlpha = alpha ?? state.Alpha;
            state.HasAlphaOverride = alpha.HasValue;
            liveRendererDefaults[renderer] = state;
            ApplyOrRestore(renderer, state);
        }
    }

    /// <summary>
    /// A note wearing nothing live is put back in its own material, not left in the
    /// tint material with the tint set to its own colour.
    /// </summary>
    /// <remarks>
    /// The difference matters for anything whose material does more than colour:
    /// a break note's material is what makes it flash, so a note left in the tint
    /// material after a COLORV*NULL would come back the right colour and stop
    /// shining.
    /// </remarks>
    private void ApplyOrRestore(SpriteRenderer renderer, LiveRendererDefaults state)
    {
        if (state.HasColorOverride || state.HasAlphaOverride)
        {
            ApplyRendererVisual(
                renderer,
                state.CurrentColor,
                state.CurrentAlpha,
                GrayscaleFor(state));
            return;
        }
        renderer.sharedMaterial = state.Material;
        renderer.SetPropertyBlock(state.Properties);
    }

    public virtual void ApplyLiveScale(Vector2? scale)
    {
    }

    public virtual void ApplyLiveGuideStarColor(Color? color)
    {
    }

    public virtual void ApplyLiveGuideStarAlpha(float? alpha)
    {
    }

    public virtual void ApplyLiveGuideStarScale(Vector2? scale)
    {
    }

    public virtual string LiveGuideStarVisualType => "slidestar";

    protected virtual IEnumerable<SpriteRenderer> GetLiveVisualRenderers()
    {
        return GetComponentsInChildren<SpriteRenderer>(true);
    }

    protected bool ReapplyLiveVisual(SpriteRenderer renderer)
    {
        if (renderer == null ||
            !liveRendererDefaults.TryGetValue(renderer, out var state) ||
            (!state.HasColorOverride && !state.HasAlphaOverride))
            return false;

        ApplyRendererVisual(
            renderer,
            state.HasColorOverride ? state.CurrentColor : null,
            state.HasAlphaOverride ? state.CurrentAlpha : null,
            GrayscaleFor(state));
        return true;
    }

    protected void ApplyLiveRendererColor(SpriteRenderer renderer, Color? color)
    {
        if (renderer == null ||
            (!color.HasValue && !liveRendererDefaults.ContainsKey(renderer)))
            return;
        var state = CaptureRendererDefaults(renderer);
        state.CurrentColor = color ?? state.Color;
        state.HasColorOverride = color.HasValue;
        liveRendererDefaults[renderer] = state;
        ApplyRendererVisual(
            renderer,
            state.CurrentColor,
            state.CurrentAlpha,
            GrayscaleFor(state));
    }

    protected void ApplyLiveRendererAlpha(SpriteRenderer renderer, float? alpha)
    {
        if (renderer == null ||
            (!alpha.HasValue && !liveRendererDefaults.ContainsKey(renderer)))
            return;
        var state = CaptureRendererDefaults(renderer);
        state.CurrentAlpha = alpha ?? state.Alpha;
        state.HasAlphaOverride = alpha.HasValue;
        liveRendererDefaults[renderer] = state;
        ApplyRendererVisual(
            renderer,
            state.CurrentColor,
            state.CurrentAlpha,
            GrayscaleFor(state));
    }

    /// <summary>
    /// How grey this renderer is still drawn. Grey is only what a mine looks like until
    /// the chart names a colour for it: keeping the grey on top of a colour that was
    /// asked for would make COLOR*mine parse and then do nothing visible.
    /// </summary>
    private float GrayscaleFor(LiveRendererDefaults state) =>
        state.HasColorOverride && IsVisualMine ? 0f : state.Grayscale;

    public bool IsVisualMine => this switch
    {
        Assets.Scripts.Notes.TapBase tap => tap.isMine,
        HoldDrop hold => hold.isMine,
        Assets.Scripts.TouchBase touch => touch.isMine,
        TouchHoldDrop touchHold => touchHold.isMine,
        SlideDrop slide => slide.isMine,
        WifiDrop wifi => wifi.isMine,
        TouchSlideDrop touchSlide => touchSlide.bodyMine,
        TrajectoryCarrierDrop carrier => carrier.bodyMine,
        _ => false
    };

    public bool IsVisualBreak => this switch
    {
        Assets.Scripts.Notes.TapBase tap => tap.isBreak,
        HoldDrop hold => hold.isBreak,
        Assets.Scripts.TouchBase touch => touch.isBreak,
        TouchHoldDrop touchHold => touchHold.isBreak,
        SlideDrop slide => slide.isBreak,
        WifiDrop wifi => wifi.isBreak,
        TouchSlideDrop touchSlide => touchSlide.bodyBreak,
        TrajectoryCarrierDrop carrier => carrier.bodyBreak,
        _ => false
    };

    /// <summary>
    /// How long this note's own body lasts past its judgement time.
    /// </summary>
    internal float TailDuration => this switch
    {
        NoteLongDrop longNote => Math.Max(0f, longNote.LastFor),
        TouchSlideDrop touchSlide => Math.Max(0f, touchSlide.duration),
        _ => 0f
    };

    private float GetVisualLifetimeAfterJudge()
    {
        var tail = TailDuration;
        return tail > 0f ? tail : MissWindow;
    }

    private LiveRendererDefaults CaptureRendererDefaults(SpriteRenderer renderer)
    {
        if (liveRendererDefaults.TryGetValue(renderer, out var defaults))
            return defaults;

        var material = renderer.sharedMaterial;
        var properties = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(properties);
        defaults = new LiveRendererDefaults
        {
            Material = material,
            Properties = properties,
            Color = material != null && material.HasProperty("_NoteColor")
                ? material.GetColor("_NoteColor")
                : new Color(1f, 1f, 1f, 0f),
            Alpha = material != null && material.HasProperty("_NoteAlpha")
                ? material.GetFloat("_NoteAlpha")
                : 1f,
            Grayscale = material != null && material.HasProperty("_Grayscale")
                ? material.GetFloat("_Grayscale")
                : 0f
        };
        defaults.CurrentColor = defaults.Color;
        defaults.CurrentAlpha = defaults.Alpha;
        liveRendererDefaults[renderer] = defaults;
        return defaults;
    }

    private static void EnsureLiveVisualMaterial()
    {
        if (liveVisualMaterial != null)
            return;
        var shader = Shader.Find("Sprites/NoteColorTint");
        if (shader != null)
            liveVisualMaterial = new Material(shader) { name = "LiveNoteVisualMaterial" };
    }

    private void ApplyRendererVisual(
        SpriteRenderer renderer,
        Color? color,
        float? alpha,
        float grayscale)
    {
        EnsureLiveVisualMaterial();
        if (liveVisualMaterial == null)
            return;

        var properties = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(properties);
        if (color.HasValue)
            properties.SetColor("_NoteColor", color.Value);
        if (alpha.HasValue)
            properties.SetFloat("_NoteAlpha", Mathf.Clamp01(alpha.Value));
        properties.SetFloat("_Grayscale", grayscale);
        renderer.sharedMaterial = liveVisualMaterial;
        renderer.SetPropertyBlock(properties);
    }

    private struct LiveRendererDefaults
    {
        public Material Material;
        public MaterialPropertyBlock Properties;
        public Color Color;
        public float Alpha;
        public float Grayscale;
        public Color CurrentColor;
        public float CurrentAlpha;
        public bool HasColorOverride;
        public bool HasAlphaOverride;
    }
}

public class NoteLongDrop : NoteDrop
{
    public float LastFor = 1f;
    public GameObject holdEffect;
    public Color noteTintColor = Color.white;

    protected float playerIdleTime = 0;
    protected Stopwatch userHold = new();
    protected float judgeDiff = -1;

    protected bool isAutoTrigger = false;
    private ParticleSystemRenderer holdEffectRenderer;
    private MaterialPropertyBlock holdEffectProperties;

    /// <summary>
    /// Gets the Hold's remaining duration
    /// </summary>
    /// <returns>
    /// Remaining Hold duration
    /// </returns>
    protected float GetRemainingTime() => MathF.Max(LastFor - GetJudgeTiming(),0);


    protected virtual void PlayHoldEffect()
    {
        if (holdEffectRenderer == null)
            holdEffectRenderer = holdEffect.GetComponent<ParticleSystemRenderer>();
        holdEffectProperties ??= new MaterialPropertyBlock();

        Color baseColor;
        switch (judgeResult)
        {
            case JudgeType.LatePerfect2:
            case JudgeType.FastPerfect2:
            case JudgeType.LatePerfect1:
            case JudgeType.FastPerfect1:
            case JudgeType.Perfect:
                baseColor = new Color(1f, 0.93f, 0.61f); break; // Yellow
            case JudgeType.LateGreat:
            case JudgeType.LateGreat1:
            case JudgeType.LateGreat2:
            case JudgeType.FastGreat2:
            case JudgeType.FastGreat1:
            case JudgeType.FastGreat:
                baseColor = new Color(1f, 0.70f, 0.94f); break; // Pink
            case JudgeType.LateGood:
            case JudgeType.FastGood:
                baseColor = new Color(0.56f, 1f, 0.59f); break; // Green
            case JudgeType.Miss:
                baseColor = new Color(1f, 1f, 1f); break;       // White
            default:
                baseColor = new Color(1f, 0.93f, 0.61f); break;
        }
        // COLOR changes the note body only, not the hold judgement particle.
        holdEffectRenderer.GetPropertyBlock(holdEffectProperties);
        holdEffectProperties.SetColor("_Color", baseColor);
        holdEffectRenderer.SetPropertyBlock(holdEffectProperties);
        holdEffect.SetActive(true);
    }
    protected virtual void StopHoldEffect()
    {
        holdEffect.SetActive(false);
    }
}
