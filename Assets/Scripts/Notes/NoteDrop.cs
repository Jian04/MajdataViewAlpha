using Assets.Scripts.Types;
using System;
using System.Diagnostics;
using UnityEngine;
#nullable enable
public class NoteDrop : MonoBehaviour
{
    public const float DefaultSpawnRadius = 1.225f;
    protected const float SpawnScaleDistance = 2.5f;

    public int startPosition;
    public float time;
    public int noteSortOrder;
    public float speed = 7;
    public bool isEach;
    public double noteScrollPos; // cumulative scroll at note's judge time
    public string scrollType;
    public bool previewOnly;
    public bool isDZone;
    public float spawnRadius = DefaultSpawnRadius;
    public float bounceDuration;

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
    
    /// <summary>
    /// Gets the time from the current moment to the correct judgement frame
    /// </summary>
    /// <returns>
    /// Positive when the current time is after the correct judgement frame
    /// <para>Negative when the current time is before the correct judgement frame</para>
    /// </returns>
    protected float GetJudgeTiming() => timeProvider.AudioTime - time;
    protected float GetSvDistance()
        => 4.8f - speed * (float)(noteScrollPos -
            SvController.GetCumulativeScroll(timeProvider.AudioTime, scrollType));
    protected float GetSpawnScale(float distance)
        => (distance - spawnRadius + SpawnScaleDistance) / SpawnScaleDistance;
    protected bool IsBeforeBounceWindow(float judgeOffset) =>
        bounceDuration > 0f && judgeOffset < -bounceDuration;
    protected bool IsBounceActive(float judgeOffset) =>
        bounceDuration > 0f && judgeOffset < 0f && judgeOffset >= -bounceDuration;
    protected float GetBounceDistance(float judgeOffset)
    {
        var elapsed = Mathf.Clamp(judgeOffset + bounceDuration, 0f, bounceDuration);
        var halfDuration = bounceDuration * 0.5f;
        var acceleration = 8f * (4.8f - spawnRadius) /
                           (bounceDuration * bounceDuration);
        var fromApex = elapsed - halfDuration;
        return spawnRadius + 0.5f * acceleration * fromApex * fromApex;
    }
    protected bool HasLeftSpawnAtCurrentTime(double targetScrollPos)
        => SvController.HasReachedSpawnRadius(
            targetScrollPos,
            speed,
            timeProvider.AudioTime,
            spawnRadius,
            scrollType);
    protected float GetCurrentVisualDistance()
    {
        var distance = GetSvDistance();
        return State == NoteStatus.Pending && distance < spawnRadius
            ? spawnRadius
            : distance;
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

    protected int GetCurrentVisualPositionIndex()
    {
        return GetSvDistance() < 0f ? (startPosition + 3) % 8 + 1 : startPosition;
    }

    protected Quaternion GetCurrentVisualRotation()
    {
        var position = GetCurrentVisualPositionIndex();
        return Quaternion.Euler(0, 0, -22.5f + -45f * (position - 1));
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
