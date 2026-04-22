using Assets.Scripts.Types;
using System;
using System.Diagnostics;
using UnityEngine;
#nullable enable
public class NoteDrop : MonoBehaviour
{
    public int startPosition;
    public float time;
    public int noteSortOrder;
    public float speed = 7;
    public bool isEach;
    public double noteScrollPos; // cumulative scroll at note's judge time

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
    /// ��ȡ��ǰʱ�̾�������֡��ʱ�䳤��
    /// </summary>
    /// <returns>
    /// ��ǰʱ��������֡�󷽣����Ϊ����
    /// <para>��ǰʱ��������֡ǰ�������Ϊ����</para>
    /// </returns>
    protected float GetJudgeTiming() => timeProvider.AudioTime - time;
    protected float GetSvDistance()
        => 4.8f - speed * (float)(noteScrollPos - SvController.GetCumulativeScroll(timeProvider.AudioTime));

    protected Vector3 getPositionFromDistance(float distance) => getPositionFromDistance(distance, startPosition);
    protected Vector3 getPositionFromDistance(float distance,int position)
    {
        return new Vector3(
            distance * Mathf.Cos((position * -2f + 5f) * 0.125f * Mathf.PI),
            distance * Mathf.Sin((position * -2f + 5f) * 0.125f * Mathf.PI));
    }
}

public class NoteLongDrop : NoteDrop
{
    public float LastFor = 1f;
    public GameObject holdEffect;
    // ALPHA: tint color for hold-press particle effect (white = no tint)
    public Color noteTintColor = Color.white;

    protected float playerIdleTime = 0;
    protected Stopwatch userHold = new();
    protected float judgeDiff = -1;

    protected bool isAutoTrigger = false;

    /// <summary>
    /// ����Hold��ʣ�೤��
    /// </summary>
    /// <returns>
    /// Holdʣ�೤��
    /// </returns>
    protected float GetRemainingTime() => MathF.Max(LastFor - GetJudgeTiming(),0);


    protected virtual void PlayHoldEffect()
    {
        var material = holdEffect.GetComponent<ParticleSystemRenderer>().material;
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
        // ALPHA: blend hold particle color toward noteTintColor
        material.SetColor("_Color", noteTintColor == Color.white
            ? baseColor
            : Color.Lerp(baseColor, new Color(noteTintColor.r, noteTintColor.g, noteTintColor.b, baseColor.a), 0.6f));
        holdEffect.SetActive(true);
    }
    protected virtual void StopHoldEffect()
    {
        holdEffect.SetActive(false);
    }
}