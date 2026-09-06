using Assets.Scripts.Types;
using System;
using System.Collections.Generic;
using UnityEngine;
#nullable enable
public class HoldDrop : NoteLongDrop
{
    /// <summary>The tail crosses its own spawn ring, so it remembers separately.</summary>
    private MajdataCore.SpawnCrossingMemo tailSpawnCrossingMemo;
    public bool isEX;
    public bool isBreak;
    public bool isFirework;
    public bool isMine;

    public Sprite tapSpr;
    public Sprite holdOnSpr;
    public Sprite holdOffSpr;
    public Sprite eachSpr;
    public Sprite eachHoldOnSpr;
    public Sprite exSpr;
    public Sprite breakSpr;
    public Sprite breakHoldOnSpr;

    public Sprite eachLine;
    public Sprite breakLine;

    public Sprite holdEachEnd;
    public Sprite holdBreakEnd;

    public RuntimeAnimatorController HoldShine;
    public RuntimeAnimatorController BreakShine;

    public GameObject tapLine;

    public Color exEffectTap;
    public Color exEffectEach;
    public Color exEffectBreak;
    private Animator animator;

    public Material breakMaterial;
    public Material colorOverrideMaterial;
    public float noteScale = 1f;
    public float noteScaleX = 1f;
    public float noteScaleY = 1f;
    private Vector2? liveScaleDefault;

    public override void ApplyLiveScale(Vector2? scale)
    {
        liveScaleDefault ??= new Vector2(noteScaleX, noteScaleY);
        var value = scale ?? liveScaleDefault.Value;
        noteScaleX = value.x;
        noteScaleY = value.y;
    }

    private SpriteRenderer exSpriteRender;
    private bool holdAnimStart;
    private SpriteRenderer holdEndRender;
    private SpriteRenderer lineSpriteRender;

    private SpriteRenderer spriteRenderer;
    private MaterialPropertyBlock brightnessProperties;
    private bool? holdOnOppositeSide;

    protected override IEnumerable<SpriteRenderer> GetLiveVisualRenderers()
    {
        foreach (var renderer in base.GetLiveVisualRenderers())
            if (renderer != null)
                yield return renderer;
        if (lineSpriteRender != null)
            yield return lineSpriteRender;
    }


    private void Awake()
    {
        HideSpriteUntilInitialized(transform);
        // That covers the note and its EX layer, but a hold also has an end cap on
        // child 1, shipped enabled, and Start does not switch it off until a frame
        // later. Until then it draws at the object's untouched transform, which is
        // the origin: a hold built mid-chart flashes its tail in the middle of the
        // playfield. Update recomputes this by 'enabled' every frame, so clearing
        // it the same way cannot leave the cap hidden for good.
        if (transform.childCount > 1 &&
            transform.GetChild(1).TryGetComponent<SpriteRenderer>(out var endCap))
            endCap.enabled = false;
    }

    private void Start()
    {
        var notes = noteManager != null ? noteManager.transform : GameObject.Find("Notes").transform;
        if (objectCounter == null) objectCounter = GameObject.Find("ObjectCounter").GetComponent<ObjectCounter>();
        if (noteManager == null) noteManager = notes.GetComponent<NoteManager>();
        holdEffect = Instantiate(holdEffect, notes);
        holdEffect.SetActive(false);

        tapLine = Instantiate(tapLine, notes);
        tapLine.SetActive(false);
        lineSpriteRender = tapLine.GetComponent<SpriteRenderer>();
        ApplyBaseRotation();

        exSpriteRender = transform.GetChild(0).GetComponent<SpriteRenderer>();

        if (timeProvider == null) timeProvider = GameObject.Find("AudioTimeProvider").GetComponent<AudioTimeProvider>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        brightnessProperties = new MaterialPropertyBlock();

        holdEndRender = transform.GetChild(1).GetComponent<SpriteRenderer>();

        spriteRenderer.sortingOrder += noteSortOrder;
        exSpriteRender.sortingOrder += noteSortOrder;
        holdEndRender.sortingOrder += noteSortOrder;

        spriteRenderer.sprite = tapSpr;
        exSpriteRender.sprite = exSpr;

        var anim = gameObject.AddComponent<Animator>();
        anim.enabled = false;
        animator = anim;

        if (isEX) exSpriteRender.color = exEffectTap;
        if (isEach)
        {
            spriteRenderer.sprite = eachSpr;
            if (!isMine)
            {
                lineSpriteRender.sprite = eachLine;
                holdEndRender.sprite = holdEachEnd;
            }
            if (isEX) exSpriteRender.color = exEffectEach;
        }

        if (isBreak)
        {
            spriteRenderer.sprite = breakSpr;
            if (!isMine)
            {
                lineSpriteRender.sprite = breakLine;
                holdEndRender.sprite = holdBreakEnd;
            }
            if (isEX) exSpriteRender.color = exEffectBreak;
            spriteRenderer.sharedMaterial = breakMaterial;
        }

        if (colorOverrideMaterial != null)
        {
            spriteRenderer.sharedMaterial = colorOverrideMaterial;
            holdEndRender.sharedMaterial  = colorOverrideMaterial;
            lineSpriteRender.sharedMaterial = colorOverrideMaterial;
        }
        if (isEX && colorOverrideMaterial != null &&
            colorOverrideMaterial.HasProperty("_NoteAlpha"))
        {
            var exColor = exSpriteRender.color;
            exColor.a *= colorOverrideMaterial.GetFloat("_NoteAlpha");
            exSpriteRender.color = exColor;
        }

        spriteRenderer.forceRenderingOff = true;
        exSpriteRender.forceRenderingOff = true;
        holdEndRender.enabled = false;

        sensor = GameObject.Find("Sensors")
                                   .transform.GetChild(SensorChildIndex)
                                   .GetComponent<Sensor>();
        manager = GameObject.Find("Sensors")
                                .GetComponent<SensorManager>();
        inputManager = GameObject.Find("Input")
                                 .GetComponent<InputManager>();
        sensorPos = (SensorType)SensorChildIndex;
        if (!JudgmentDisabled)
            BindJudgeInput(Check);
    }
    private void FixedUpdate()
    {
        if (JudgmentDisabled || JudgmentSuspended)
            return;
        var timing = GetJudgeTiming();
        var remainingTime = GetRemainingTime();

        if (remainingTime == 0 && isJudged) // Destroy after the Hold completes
        {
            Destroy(tapLine);
            Destroy(holdEffect);
            Destroy(gameObject);
        }
        else if(timing >= -0.01f)
        {
            // AutoPlay behavior
            switch (InputManager.Mode)
            {
                case AutoPlayMode.Enable:
                    if(!isJudged)
                        objectCounter.NextNote(JudgeQueueKey);
                    judgeResult = JudgeType.Perfect;
                    isJudged = true;
                    PlayHoldEffect();
                    return;
                case AutoPlayMode.DJAuto:
                    if (!isJudged)
                        inputManager.ClickSensor(sensorPos, true);
                    if (isJudged)
                        manager.SetSensorOn(sensor.Type, guid);
                    break;
                case AutoPlayMode.Random:
                    if (!isJudged)
                    {
                        objectCounter.NextNote(JudgeQueueKey);
                        judgeResult = (JudgeType)UnityEngine.Random.Range(1, 14);
                        isJudged = true;
                    }
                    PlayHoldEffect();
                    return;
                case AutoPlayMode.Disable:
                    manager.SetSensorOff(sensor.Type, guid);
                    break;
            }
        }

        if (isJudged) // Accumulate press duration after judging the head
        {
            if (timing <= 0.1f) // Ignore the first 6 frames
                return;
            else if (remainingTime <= 0.2f) // Ignore the last 12 frames
                return;
            else if (!timeProvider.isStart) // Ignore paused time
                return;
            var on = inputManager.CheckAreaStatus(sensorPos,SensorStatus.On);
            if (on)
                PlayHoldEffect();
            else
            {
                playerIdleTime += Time.fixedDeltaTime;
                StopHoldEffect();
            }
        }
        else if (timing > MissWindow && !isJudged) // Missed head
        {
            judgeDiff = 150;
            judgeResult = JudgeType.Miss;
            isJudged = true;
            objectCounter.NextNote(JudgeQueueKey);
        }
    }
    void Check(object sender, InputEventArgs arg)
    {
        if (JudgmentDisabled || JudgmentSuspended)
            return;
        if (arg.Type != sensor.Type)
            return;
        else if (isJudged || !noteManager.CanJudge(gameObject, JudgeQueueKey))
            return;
        else if (InputManager.Mode is AutoPlayMode.Enable or AutoPlayMode.Random)
            return;
        if (arg.IsClick)
        {
            if (InputManager.Mode != AutoPlayMode.DJAuto)
            {
                if (!inputManager.IsIdle(arg))
                    return;
                inputManager.SetBusy(arg);
            }
            Judge();
            if (isJudged)
            {
                inputManager.UnbindArea(Check, sensorPos);
                objectCounter.NextNote(JudgeQueueKey);
            }
        }
    }
    void Judge()
    {

        const int JUDGE_GOOD_AREA = 150;
        const int JUDGE_GREAT_AREA = 100;
        const int JUDGE_PERFECT_AREA = 50;

        const float JUDGE_SEG_PERFECT1 = 16.66667f;
        const float JUDGE_SEG_PERFECT2 = 33.33334f;
        const float JUDGE_SEG_GREAT1 = 66.66667f;
        const float JUDGE_SEG_GREAT2 = 83.33334f;

        if (isJudged)
            return;

        var timing = timeProvider.AudioTime - time;
        var isFast = timing < 0;
        var diff = MathF.Abs(timing * 1000);
        JudgeType result;
        if (diff > JUDGE_GOOD_AREA && isFast)
            return;
        else if (diff < JUDGE_SEG_PERFECT1)
            result = JudgeType.Perfect;
        else if (diff < JUDGE_SEG_PERFECT2)
            result = JudgeType.LatePerfect1;
        else if (diff < JUDGE_PERFECT_AREA)
            result = JudgeType.LatePerfect2;
        else if (diff < JUDGE_SEG_GREAT1)
            result = JudgeType.LateGreat;
        else if (diff < JUDGE_SEG_GREAT2)
            result = JudgeType.LateGreat1;
        else if (diff < JUDGE_GREAT_AREA)
            result = JudgeType.LateGreat;
        else if (diff < JUDGE_GOOD_AREA)
            result = JudgeType.LateGood;
        else
            result = JudgeType.Miss;

        if (result != JudgeType.Miss && isFast)
            result = 14 - result;
        if (result != JudgeType.Miss && isEX)
            result = JudgeType.Perfect;
        if (isFast)
            judgeDiff = 0;
        else
            judgeDiff = diff;

        judgeResult = result;
        isJudged = true;
        PlayHoldEffect();
    }
    // The animator may replace the hold material during Update.
    protected override void LateUpdate()
    {
        base.LateUpdate();
        if (!holdAnimStart || ReapplyLiveVisual(spriteRenderer))
            return;
        if (colorOverrideMaterial != null)
            spriteRenderer.sharedMaterial = colorOverrideMaterial;
    }

    // Update is called once per frame
    private void Update()
    {
        if ((!timeProvider.isStart && !timeProvider.IsPaused) || timeProvider.AudioTime < GameplayRevealTime)
        {
            spriteRenderer.forceRenderingOff = true;
            exSpriteRender.forceRenderingOff = true;
            holdEndRender.enabled = false;
            tapLine.SetActive(false);
            if (holdEffect != null)
                holdEffect.SetActive(false);
            return;
        }
        if (IsPausedTimelinePreview &&
            timeProvider.AudioTime > time + Mathf.Max(0f, LastFor))
        {
            spriteRenderer.forceRenderingOff = true;
            exSpriteRender.forceRenderingOff = true;
            holdEndRender.enabled = false;
            tapLine.SetActive(false);
            if (holdEffect != null)
                holdEffect.SetActive(false);
            return;
        }

        if (IsBeforeBounceWindow())
        {
            spriteRenderer.forceRenderingOff = true;
            exSpriteRender.forceRenderingOff = true;
            holdEndRender.enabled = false;
            tapLine.SetActive(false);
            return;
        }

        var isBouncing = IsBounceActive();
        var distance = isBouncing ? GetBounceDistance() : GetSvDistance();
        if (isBouncing)
        {
            State = NoteStatus.Running;
            spriteRenderer.forceRenderingOff = false;
            if (isEX)
                exSpriteRender.forceRenderingOff = false;

            var bounceTailScrollPosition =
                SvController.GetCumulativeScroll((double)time + LastFor, scrollType);
            var pathDirection = SvController.GetPathDirection(spawnRadius, destroyRadius);
            var fullBodyLength = pathDirection * speed * (float)(bounceTailScrollPosition -
                SvController.GetCumulativeScroll(time, scrollType));
            var visibleBodyLimit = Mathf.Abs(destroyRadius - spawnRadius);
            var bodyLength = Mathf.Sign(fullBodyLength) *
                             Mathf.Min(Mathf.Abs(fullBodyLength), visibleBodyLimit);
            var tailDistance = distance - bodyLength;
            var bodyCenter = (distance + tailDistance) * 0.5f;
            var bodySize = Mathf.Abs(distance - tailDistance) + 1.4f;
            holdEndRender.enabled = Mathf.Abs(bodyLength) > 0.001f;
            spriteRenderer.size = new Vector2(1.22f, bodySize);
            exSpriteRender.size = spriteRenderer.size;
            holdEndRender.transform.localPosition = new Vector3(0f, 0.6825f - bodySize / 2f);
            transform.position = getPositionFromDistance(bodyCenter);
            transform.localScale = new Vector3(
                noteScale * noteScaleX,
                noteScale * noteScaleY,
                1f);

            var absoluteBounceDistance = Mathf.Abs(distance);
            var bounceLineScale = absoluteBounceDistance / DefaultDestroyRadius;
            tapLine.SetActive(absoluteBounceDistance > 0.001f);
            tapLine.transform.localScale = new Vector3(
                bounceLineScale, bounceLineScale, 1f);
            UpdateVisualRotation(distance);
            return;
        }

        var headPresentation = GetSpawnPresentation(
            distance, noteScrollPos, ref spawnCrossingMemo);
        if (!headPresentation.Visible)
        {
            State = NoteStatus.Initialized;
            spriteRenderer.forceRenderingOff = true;
            exSpriteRender.forceRenderingOff = true;
            holdEndRender.enabled = false;
            tapLine.SetActive(false);
            return;
        }

        spriteRenderer.forceRenderingOff = false;
        if (isEX) exSpriteRender.forceRenderingOff = false;
        State = headPresentation.Running
            ? NoteStatus.Running
            : NoteStatus.Pending;

        spriteRenderer.size = new Vector2(1.22f, 1.4f);

        var holdTime = GetJudgeTiming() - LastFor;
        var holdDistance = SvController.GetVisualRadius(
            SvController.GetCumulativeScroll((double)time + LastFor, scrollType),
            speed,
            timeProvider.AudioTime,
            spawnRadius,
            destroyRadius,
            scrollType);
        var tailScrollPosition = SvController.GetCumulativeScroll(
            (double)time + LastFor, scrollType);
        var tailPresentation = GetSpawnPresentation(
            holdDistance, tailScrollPosition, ref tailSpawnCrossingMemo);
        if (holdTime >= 0)
        {
            tapLine.SetActive(false);
            tapLine.transform.localScale = new Vector3(1f, 1f, 1f);
            transform.position = getPositionFromDistance(destroyRadius);
            UpdateVisualRotation(destroyRadius);
            return;
        }


        // Judgement remains on the original key even when SV moves the note body
        // to the opposite side.
        holdEffect.transform.position = getPositionFromDistance(destroyRadius);

        if (isBreak &&
            !holdAnimStart && 
            !isJudged)
        {
            var extra = Math.Max(Mathf.Sin(timeProvider.GetFrame() * 0.17f) * 0.5f, 0);
            spriteRenderer.GetPropertyBlock(brightnessProperties);
            brightnessProperties.SetFloat("_Brightness", 0.95f + extra);
            spriteRenderer.SetPropertyBlock(brightnessProperties);
        }

        distance = headPresentation.Distance;
        var absoluteDistance = Mathf.Abs(distance);
        tapLine.SetActive(
            headPresentation.Running
                ? absoluteDistance > 0.001f
                : headPresentation.Scale > 0.3f);

        if (!headPresentation.Running)
        {
            transform.localScale = new Vector3(
                headPresentation.Scale * noteScale * noteScaleX,
                headPresentation.Scale * noteScale * noteScaleY,
                1f);
            spriteRenderer.size = new Vector2(1.22f, 1.42f);
            transform.position = getPositionFromDistance(spawnRadius);
            holdEndRender.enabled = false;
        }
        else
        {
            // The head reaches the judgement line by time, not by radial distance.
            // This lets negative SV move through the centre without being clamped.
            if (GetJudgeTiming() >= 0f)
                distance = destroyRadius;

            if (tailPresentation.Running)
            {
                holdEndRender.enabled = true;
                holdDistance = tailPresentation.Distance;
            }
            else
            {
                holdEndRender.enabled = false;
                holdDistance = spawnRadius;
            }

            var dis = (distance - holdDistance) / 2 + holdDistance;
            transform.position = getPositionFromDistance(dis);
            var size = Mathf.Abs(distance - holdDistance) + 1.4f;
            spriteRenderer.size = new Vector2(1.22f, size);
            holdEndRender.transform.localPosition = new Vector3(0f, 0.6825f - size / 2);
            transform.localScale = new Vector3(
                noteScale * noteScaleX,
                noteScale * noteScaleY,
                1f);
        }

        var lineScale = Mathf.Abs(distance / DefaultDestroyRadius);
        tapLine.transform.localScale = new Vector3(lineScale, lineScale, 1f);
        UpdateVisualRotation(
            headPresentation.Running ? distance : spawnRadius);
        exSpriteRender.size = spriteRenderer.size;
    }

    private void ApplyBaseRotation()
    {
        holdOnOppositeSide = null;
        UpdateVisualRotation(spawnRadius);
    }

    private void UpdateVisualRotation(float visualDistance)
    {
        var opposite = visualDistance < 0f;
        if (holdOnOppositeSide == opposite)
            return;
        holdOnOppositeSide = opposite;
        var dZoneOffset = isDZone ? 22.5f : 0f;
        var baseAngle = -22.5f + -45f * (startPosition - 1) + dZoneOffset;
        var reversePath = SvController.GetPathDirection(
            spawnRadius, destroyRadius) < 0f;
        transform.rotation = Quaternion.Euler(
            0f, 0f, baseAngle + (opposite != reversePath ? 180f : 0f));
        tapLine.transform.rotation = Quaternion.Euler(
            0f, 0f, baseAngle + (opposite ? 180f : 0f));
    }
    private void OnDestroy()
    {
        if (tapLine != null)
            Destroy(tapLine);
        if (inputManager != null)
            inputManager.UnbindArea(Check, sensorPos);
        if (manager != null && sensor != null)
            manager.SetSensorOff(sensor.Type, guid);
        if (JudgmentDisabled || HttpHandler.IsReloding)
            return;
        var realityHT = LastFor - 0.3f - (judgeDiff / 1000f);
        var percent = MathF.Min(1, (realityHT - playerIdleTime) / realityHT);
        JudgeType result = judgeResult;
        if(realityHT > 0)
        {
            if (percent >= 1f)
            {
                if(judgeResult == JudgeType.Miss)
                    result = JudgeType.LateGood;
                else if (MathF.Abs((int)judgeResult - 7) == 6)
                    result = (int)judgeResult < 7 ? JudgeType.LateGreat : JudgeType.FastGreat;
                else
                    result = judgeResult;
            }
            else if (percent >= 0.67f)
            {
                if (judgeResult == JudgeType.Miss)
                    result = JudgeType.LateGood;
                else if (MathF.Abs((int)judgeResult - 7) == 6)
                    result = (int)judgeResult < 7 ? JudgeType.LateGreat : JudgeType.FastGreat;
                else if (judgeResult == JudgeType.Perfect)
                    result = (int)judgeResult < 7 ? JudgeType.LatePerfect1 : JudgeType.FastPerfect1;
            }
            else if (percent >= 0.33f)
            {
                if (MathF.Abs((int)judgeResult - 7) >= 6)
                    result = (int)judgeResult < 7 ? JudgeType.LateGood : JudgeType.FastGood;
                else
                    result = (int)judgeResult < 7 ? JudgeType.LateGreat : JudgeType.FastGreat;
            }
            else if (percent >= 0.05f)
                result = (int)judgeResult < 7 ? JudgeType.LateGood : JudgeType.FastGood;
            else if (percent >= 0)
            {
                if (judgeResult == JudgeType.Miss)
                    result = JudgeType.Miss;
                else
                    result = (int)judgeResult < 7 ? JudgeType.LateGood : JudgeType.FastGood;
            }
        }

        switch (InputManager.Mode)
        {
            case AutoPlayMode.Enable:
                result = JudgeType.Perfect;
                break;
            case AutoPlayMode.Random:
                result = (JudgeType)UnityEngine.Random.Range(1, 14);
                break;
            case AutoPlayMode.DJAuto:
            case AutoPlayMode.Disable:
                break;
        }
        var effectManager = GameObject.Find("NoteEffects")?.GetComponent<NoteEffectManager>();
        if (effectManager != null &&
            (!isMine || NoteEffectManager.ShowMineHitFeedback))
        {
            effectManager.PlayEffect(
                JudgeQueueKey, destroyRadius, isBreak, result, noteTintColor);
            effectManager.PlayFastLate(JudgeQueueKey, destroyRadius, result);
        }
        if (isFirework && result != JudgeType.Miss)
        {
            var firework = GameObject.Find("FireworkEffect");
            var animator = firework?.GetComponent<Animator>();
            if (animator != null)
            {
                firework.transform.position = transform.position;
                animator.SetTrigger("Fire");
            }
        }
        objectCounter.ReportResult(this, result, isBreak);
        if (!isJudged)
            objectCounter.NextNote(JudgeQueueKey);

        manager.SetSensorOff(sensor.Type, guid);
    }
    protected override void PlayHoldEffect()
    {
        if (!isMine || NoteEffectManager.ShowMineHitFeedback)
            base.PlayHoldEffect();
        else if (holdEffect != null)
            holdEffect.SetActive(false);
        GameObject.Find("NoteEffects")?.GetComponent<NoteEffectManager>()?.ResetEffect(JudgeQueueKey);
        if (LastFor <= 0.3)
            return;
        else if (!holdAnimStart && GetJudgeTiming() >= 0.1f)// Ignore the first 6 and last 12 frames
        {
            holdAnimStart = true;
            animator.runtimeAnimatorController = HoldShine;
            animator.enabled = true;
            var sprRenderer = GetComponent<SpriteRenderer>();
            if (isBreak)
                sprRenderer.sprite = breakHoldOnSpr;
            else if (isEach)
                sprRenderer.sprite = eachHoldOnSpr;
            else
                sprRenderer.sprite = holdOnSpr;
            if (colorOverrideMaterial != null)
                sprRenderer.sharedMaterial = colorOverrideMaterial;
        }
    }
    protected override void StopHoldEffect()
    {
        base.StopHoldEffect();
        holdAnimStart = false;
        animator.runtimeAnimatorController = HoldShine;
        animator.enabled = false;
        var sprRenderer = GetComponent<SpriteRenderer>();
        sprRenderer.sprite = holdOffSpr;
        if (colorOverrideMaterial != null)
            sprRenderer.sharedMaterial = colorOverrideMaterial;
    }

}
