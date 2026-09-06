using Assets.Scripts.Types;
using System;
using MajdataCore;
using UnityEngine;
using UnityEngine.Rendering;
#nullable enable
public class TouchHoldDrop : NoteLongDrop
{
    public bool isFirework;
    public char touchArea = 'C';
    public bool isBreak;
    public bool isMine;
    public Material colorOverrideMaterial;
    public Material breakProgressMaterial;
    public Sprite breakProgressSprite;
    public float noteScale = 1f;
    public float noteScaleX = 1f;
    public float noteScaleY = 1f;
    private Vector2? liveScaleDefault;
    private Vector3 authoredCenterPointScale;
    private bool centerPointScaleCaptured;

    public override void ApplyLiveScale(Vector2? scale)
    {
        liveScaleDefault ??= new Vector2(noteScaleX, noteScaleY);
        var previous = new Vector2(noteScaleX, noteScaleY);
        var value = scale ?? liveScaleDefault.Value;
        noteScaleX = value.x;
        noteScaleY = value.y;
        if (previous.x != 0f && previous.y != 0f)
            transform.localScale = Vector3.Scale(
                transform.localScale,
                new Vector3(value.x / previous.x, value.y / previous.y, 1f));
        KeepCenterPointSize();
    }
    public GameObject tapEffect;
    public GameObject judgeEffect;

    public Sprite touchHoldBoard;
    public Sprite touchHoldBoard_Miss;
    public SpriteRenderer boarder;
    public Sprite[] TouchHoldSprite = new Sprite[5];
    public Sprite TouchPointSprite;

    public GameObject[] fans;
    public SpriteMask mask;
    private readonly SpriteRenderer[] fansSprite = new SpriteRenderer[6];
    private float displayDuration;

    private GameObject firework;
    private Animator fireworkEffect;
    private float moveDuration;

    private float wholeDuration;
    private NoteEffectManager noteEffectManager;

    Sprite[] judgeText;
    Sprite judgeTextBreak;

    // See NoteDrop.HideSpriteUntilInitialized: Start clears the fans a frame after
    // they have already been drawn opaque.
    private void Awake() => HideFansUntilInitialized(fans);

    // Start is called before the first frame update
    private void Start()
    {
        wholeDuration = AlphaVisualTiming.GetTouchMotionDuration(speed);
        moveDuration = 0.8f * wholeDuration;
        displayDuration = 0.2f * wholeDuration;

        if (objectCounter == null) objectCounter = GameObject.Find("ObjectCounter").GetComponent<ObjectCounter>();
        noteEffectManager = GameObject.Find("NoteEffects").GetComponent<NoteEffectManager>();
        var notes = noteManager != null ? noteManager.transform : GameObject.Find("Notes").transform;
        if (noteManager == null) noteManager = notes.GetComponent<NoteManager>();
        var originalHoldEffect = holdEffect;
        // Keep particles in the same transformed feedback plane as judgement
        // text so ZOOM/MOVE/ROTATE remain spatially consistent.
        holdEffect = Instantiate(holdEffect, noteEffectManager.transform);
        holdEffect.SetActive(false);
        originalHoldEffect.SetActive(false);
        foreach (var r in holdEffect.GetComponentsInChildren<ParticleSystemRenderer>(true))
            r.maskInteraction = SpriteMaskInteraction.None;
        foreach (var r in holdEffect.GetComponentsInChildren<SpriteRenderer>(true))
            r.maskInteraction = SpriteMaskInteraction.None;

        if (timeProvider == null) timeProvider = GameObject.Find("AudioTimeProvider").GetComponent<AudioTimeProvider>();

        firework = GameObject.Find("FireworkEffect");
        fireworkEffect = firework.GetComponent<Animator>();

        for (var i = 0; i < 6; i++)
            fansSprite[i] = fans[i].GetComponent<SpriteRenderer>();
        var sortingGroup = GetComponent<SortingGroup>() ??
                           gameObject.AddComponent<SortingGroup>();
        sortingGroup.sortingLayerID = fansSprite[0].sortingLayerID;
        sortingGroup.sortingOrder = noteSortOrder;
        mask.isCustomRangeActive = true;
        mask.backSortingLayerID = fansSprite[0].sortingLayerID;
        mask.frontSortingLayerID = fansSprite[0].sortingLayerID;
        mask.backSortingOrder = 0;
        mask.frontSortingOrder = 5;

        for (var i = 0; i < 4; i++) fansSprite[i].sprite = TouchHoldSprite[i];
        fansSprite[5].sprite = TouchHoldSprite[4]; // TouchHold Border
        fansSprite[4].sprite = TouchPointSprite;
        authoredCenterPointScale = fans[4].transform.localScale;
        centerPointScaleCaptured = true;

        SetfanColor(new Color(1f, 1f, 1f, 0f));
        mask.enabled = false;
        mask.gameObject.SetActive(false);

        if (colorOverrideMaterial != null)
            for (var fi = 0; fi < 6; fi++)
                fansSprite[fi].sharedMaterial = colorOverrideMaterial;
        if (isBreak && !isMine && breakProgressMaterial != null)
        {
            fansSprite[5].sprite = breakProgressSprite != null
                ? breakProgressSprite
                : TouchHoldSprite[4];
            fansSprite[5].sharedMaterial = breakProgressMaterial;
        }

        var sensorsRoot = GameObject.Find("Sensors");
        var touchSensor = Assets.Scripts.TouchBase.GetSensor(touchArea, startPosition);
        sensor = sensorsRoot.transform.GetChild((int)touchSensor).GetComponent<Sensor>();
        manager = sensorsRoot.GetComponent<SensorManager>();
        inputManager = GameObject.Find("Input").GetComponent<InputManager>();
        if (touchArea != 'C')
        {
            transform.position = GetAreaPos(startPosition, touchArea);
        }
        transform.localScale = Vector3.Scale(transform.localScale,
            new Vector3(noteScale * noteScaleX, noteScale * noteScaleY, 1f));
        KeepCenterPointSize();
        var customSkin = GameObject.Find("Outline").GetComponent<CustomSkin>();
        judgeText = customSkin.JudgeText;
        judgeTextBreak = customSkin.JudgeText_Break;
        if (!JudgmentDisabled)
            inputManager.BindSensor(Check, touchSensor);
    }
    void Check(object sender, InputEventArgs arg)
    {
        if (JudgmentDisabled || JudgmentSuspended)
            return;
        if (isJudged || !noteManager.CanJudge(gameObject, sensor.Type))
            return;
        else if (InputManager.Mode is AutoPlayMode.Enable or AutoPlayMode.Random)
            return;
        else if (arg.IsClick)
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
                inputManager.UnbindSensor(Check, sensor.Type);
                objectCounter.NextTouch(sensor.Type);
            }
        }
    }
    void Judge()
    {

        const float JUDGE_GOOD_AREA = 316.667f;
        const int JUDGE_GREAT_AREA = 250;
        const int JUDGE_PERFECT_AREA = 200;

        const float JUDGE_SEG_PERFECT = 150f;

        if (isJudged)
            return;

        var timing = timeProvider.AudioTime - time;
        var isFast = timing < 0;
        var diff = MathF.Abs(timing * 1000);
        JudgeType result;
        if (diff > JUDGE_SEG_PERFECT && isFast)
            return;
        else if (diff < JUDGE_SEG_PERFECT)
            result = JudgeType.Perfect;
        else if (diff < JUDGE_PERFECT_AREA)
            result = JudgeType.LatePerfect2;
        else if (diff < JUDGE_GREAT_AREA)
            result = JudgeType.LateGreat;
        else if (diff < JUDGE_GOOD_AREA)
            result = JudgeType.LateGood;
        else
            result = JudgeType.Miss;
        if (isFast)
            judgeDiff = 0;
        else
            judgeDiff = diff;

        judgeResult = result;
        isJudged = true;
        PlayHoldEffect();
    }
    private void FixedUpdate()
    {
        if (JudgmentDisabled || JudgmentSuspended)
            return;
        var remainingTime = GetRemainingTime();
        var timing = GetJudgeTiming();
        var holdTime = timing - LastFor;

        if (remainingTime == 0 && isJudged)
        {
            Destroy(holdEffect);
            Destroy(gameObject);
        }
        else if (timing >= -0.01f)
        {
            // AutoPlay behavior
            switch (InputManager.Mode)
            {
                case AutoPlayMode.Enable:
                    if (!isJudged)
                        objectCounter.NextTouch(sensor.Type);
                    judgeResult = JudgeType.Perfect;
                    isJudged = true;
                    PlayHoldEffect();
                    return;
                case AutoPlayMode.DJAuto:
                    if (!isJudged)
                        inputManager.ClickSensor(sensor.Type, true);
                    if (isJudged)
                        manager.SetSensorOn(sensor.Type, guid);
                    break;
                case AutoPlayMode.Random:
                    if (!isJudged)
                    {
                        objectCounter.NextTouch(sensor.Type);
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

        if (isJudged)
        {
            if (timing <= 0.25f) // Ignore the first 15 frames
                return;
            else if (remainingTime <= 0.2f) // Ignore the last 12 frames
                return;
            else if (!timeProvider.isStart) // Ignore paused time
                return;

            var on = inputManager.CheckSensorStatus(sensor.Type, SensorStatus.On);
            if (on)
                PlayHoldEffect();
            else
            {
                playerIdleTime += Time.fixedDeltaTime;
                StopHoldEffect();
            }
        }
        else if (timing > 0.316667f)
        {
            judgeDiff = 316.667f;
            judgeResult = JudgeType.Miss;
            inputManager.UnbindSensor(Check, sensor.Type);
            isJudged = true;
            objectCounter.NextTouch(sensor.Type);
        }
    }
    /// <summary>
    /// Puts the note back to how it looked before it ever appeared, for the same
    /// reason as <see cref="TouchDrop"/>: outside its window nothing rewrites
    /// the fans, the mask or the hold effect.
    /// </summary>
    private void RewindVisualState()
    {
        SetfanColor(Color.clear);
        fans[5].SetActive(false);
        mask.enabled = false;
        mask.gameObject.SetActive(false);
        if (holdEffect != null)
            holdEffect.SetActive(false);
    }
    // Update is called once per frame
    private void Update()
    {
        if (ClockMovedBackwards())
            RewindVisualState();
        if (IsPausedTimelinePreview &&
            timeProvider.AudioTime > time + Mathf.Max(0f, LastFor))
        {
            SetfanColor(Color.clear);
            fans[5].SetActive(false);
            mask.enabled = false;
            mask.gameObject.SetActive(false);
            if (holdEffect != null)
                holdEffect.SetActive(false);
            return;
        }
        // Keep hold judgement duration on the audio clock, while allowing the
        // Touch head's appearance to follow global or typed Touch SV.
        var timing = GetTouchVisualTiming();
        // Scroll sits on whichever side of the note the SV integral puts it: a
        // negative integral brings a Touch in from the far side, where this timing
        // reads positive. That side used to be read as "already landed", so such a
        // Touch stayed shut for its whole approach and then snapped. It is mirrored
        // instead while the note is still ahead of the audio clock, so the petals
        // open out of the centre and close back into it either way round. Past the
        // note's own moment the far side still reads as landed, so a Touch that was
        // missed does not puff back open on its way out.
        if (timeProvider.AudioTime < time)
            timing = -Mathf.Abs(timing);
        var judgeTiming = GetJudgeTiming();
        var pow = -Mathf.Exp(8 * (timing * 0.4f / moveDuration) - 0.85f) + 0.42f;
        var distance = Mathf.Clamp(pow, 0f, 0.4f);

        if (-timing <= wholeDuration && -timing > moveDuration)
        {
            SetfanColor(new Color(1f, 1f, 1f, Mathf.Clamp((wholeDuration + timing) / displayDuration, 0f, 1f)));
            fans[5].SetActive(false);
            mask.enabled = false;
            mask.gameObject.SetActive(false);
        }
        else if (-timing < moveDuration)
        {
            fans[5].SetActive(true);
            mask.gameObject.SetActive(true);
            mask.enabled = true;
            SetfanColor(Color.white);
            mask.alphaCutoff = Mathf.Clamp(
                0.91f * (1 - (LastFor - judgeTiming) / LastFor),
                0f,
                1f);
        }

        if (float.IsNaN(distance)) distance = 0f;

        for (var i = 0; i < 4; i++)
            fans[i].transform.localPosition = LeafPosition(i, distance);
    }
    private void OnDestroy()
    {
        if (inputManager != null && sensor != null)
            inputManager.UnbindSensor(Check, sensor.Type);
        if (manager != null && sensor != null)
            manager.SetSensorOff(sensor.Type, guid);
        if (holdEffect != null && gameObject.scene.isLoaded)
            Destroy(holdEffect);
        if (JudgmentDisabled || HttpHandler.IsReloding || !gameObject.scene.isLoaded)
            return;
        var realityHT = LastFor - 0.45f - (judgeDiff / 1000f);
        var percent = MathF.Min(1, (realityHT - playerIdleTime) / realityHT);
        JudgeType result = judgeResult;
        if (realityHT > 0)
        {
            if (percent >= 1f)
            {
                if (judgeResult == JudgeType.Miss)
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

        objectCounter.ReportResult(this, result, isBreak);
        if (!isJudged)
            objectCounter.NextTouch(sensor.Type);
        if (isFirework && result != JudgeType.Miss)
        {
            fireworkEffect.SetTrigger("Fire");
            PlaceInFeedbackPlane(
                firework.transform,
                firework.transform.parent,
                GetFixedFeedbackPosition());
        }
        PlayJudgeEffect(result);
    }

    protected override void PlayHoldEffect()
    {
        PlaceInFeedbackPlane(
            holdEffect.transform,
            noteEffectManager != null ? noteEffectManager.transform : null,
            GetFixedFeedbackPosition(),
            GetFixedFeedbackRotation());
        if (!isMine || NoteEffectManager.ShowMineHitFeedback)
            base.PlayHoldEffect();
        else
            holdEffect.SetActive(false);
        boarder.sprite = touchHoldBoard;
    }
    void PlayJudgeEffect(JudgeType judgeResult)
    {
        if (judgeEffect == null || noteEffectManager == null)
            return;
        if (isMine && !NoteEffectManager.ShowMineHitFeedback)
            return;
        var feedbackPosition = GetFixedFeedbackPosition();
        var feedbackRotation = GetFixedFeedbackRotation();
        var plane = noteEffectManager.transform;
        var obj = Instantiate(judgeEffect, plane);
        var _obj = Instantiate(judgeEffect, plane);
        PlaceInFeedbackPlane(obj.transform, plane, Vector3.zero, feedbackRotation);
        PlaceInFeedbackPlane(_obj.transform, plane, Vector3.zero, feedbackRotation);
        var judgeObj = obj.transform.GetChild(0);
        var flObj = _obj.transform.GetChild(0);

        var judgeRotation = GetJudgeRotation();
        if (sensor.Group != SensorGroup.C)
        {
            PlaceInFeedbackPlane(judgeObj, plane, GetJudgePosition(-0.46f));
            PlaceInFeedbackPlane(flObj, plane, GetJudgePosition(-0.92f));
        }
        else
        {
            PlaceInFeedbackPlane(judgeObj, plane, new Vector3(0, -0.6f, 0));
            PlaceInFeedbackPlane(flObj, plane, new Vector3(0, -1.08f, 0));
        }
        RotateInFeedbackPlane(flObj.GetChild(0), plane, judgeRotation);
        RotateInFeedbackPlane(judgeObj.GetChild(0), plane, judgeRotation);
        if (judgeObj.childCount > 1)
            RotateInFeedbackPlane(judgeObj.GetChild(1), plane, judgeRotation);
        var anim = obj.GetComponent<Animator>();

        var effects = noteEffectManager.gameObject;
        var flAnim = _obj.GetComponent<Animator>();
        if (effects == null)
        {
            Destroy(obj);
            Destroy(_obj);
            return;
        }
        GameObject effect;
        switch (judgeResult)
        {
            case JudgeType.LateGood:
            case JudgeType.FastGood:
                judgeObj.GetChild(0).gameObject.GetComponent<SpriteRenderer>().sprite = judgeText[1];
                effect = Instantiate(
                    effects.transform.GetChild(3).GetChild(0), plane).gameObject;
                PlaceInFeedbackPlane(
                    effect.transform, plane, feedbackPosition, feedbackRotation);
                effect.SetActive(true);
                break;
            case JudgeType.LateGreat:
            case JudgeType.LateGreat1:
            case JudgeType.LateGreat2:
            case JudgeType.FastGreat2:
            case JudgeType.FastGreat1:
            case JudgeType.FastGreat:
                judgeObj.GetChild(0).gameObject.GetComponent<SpriteRenderer>().sprite = judgeText[2];
                effect = Instantiate(
                    effects.transform.GetChild(2).GetChild(0), plane).gameObject;
                PlaceInFeedbackPlane(
                    effect.transform, plane, feedbackPosition, feedbackRotation);
                effect.SetActive(true);
                effect.gameObject.GetComponent<Animator>().SetTrigger("great");
                break;
            case JudgeType.LatePerfect2:
            case JudgeType.FastPerfect2:
            case JudgeType.LatePerfect1:
            case JudgeType.FastPerfect1:
                judgeObj.GetChild(0).gameObject.GetComponent<SpriteRenderer>().sprite = judgeText[3];
                if (tapEffect != null)
                    PlaceInFeedbackPlane(
                        Instantiate(tapEffect, plane).transform, plane,
                        feedbackPosition, feedbackRotation);
                break;
            case JudgeType.Perfect:
                judgeObj.GetChild(0).gameObject.GetComponent<SpriteRenderer>().sprite = judgeText[4];
                if (tapEffect != null)
                    PlaceInFeedbackPlane(
                        Instantiate(tapEffect, plane).transform, plane,
                        feedbackPosition, feedbackRotation);
                break;
            case JudgeType.Miss:
                judgeObj.GetChild(0).gameObject.GetComponent<SpriteRenderer>().sprite = judgeText[0];
                break;
            default:
                break;
        }
        if (judgeObj.childCount > 1)
        {
            var breakRenderer = judgeObj.GetChild(1).GetComponent<SpriteRenderer>();
            breakRenderer.sprite = judgeTextBreak;
            NoteEffectManager.ApplyJudgeTextAlpha(breakRenderer);
        }
        NoteEffectManager.ApplyJudgeTextAlpha(judgeObj.GetChild(0).GetComponent<SpriteRenderer>());
        noteEffectManager.PlayFastLate(_obj, flAnim, judgeResult);
        if (isBreak && judgeResult == JudgeType.Perfect)
        {
            anim.SetTrigger("break");
            // Like TouchDrop, JudgeBreak has no ifDestroy curve, so destroy it in code
            Destroy(obj, 1f);
        }
        else
        {
            anim.SetTrigger("touch");
        }
    }
    protected override void StopHoldEffect()
    {
        base.StopHoldEffect();
        boarder.sprite = touchHoldBoard_Miss;
    }
    private Vector3 LeafPosition(int index, float distance) =>
        (0.226f + distance) * GetAngle(index);

    private void KeepCenterPointSize()
    {
        if (!centerPointScaleCaptured || fans == null || fans.Length <= 4 ||
            fans[4] == null)
            return;
        var scaleX = Mathf.Max(
            0.0001f, Mathf.Abs(noteScale * noteScaleX));
        var scaleY = Mathf.Max(
            0.0001f, Mathf.Abs(noteScale * noteScaleY));
        fans[4].transform.localScale = new Vector3(
            authoredCenterPointScale.x / scaleX,
            authoredCenterPointScale.y / scaleY,
            authoredCenterPointScale.z);
    }
    private Vector3 GetAngle(int index)
    {
        var angle = Mathf.PI / 4 + index * (Mathf.PI / 2);
        return new Vector3(Mathf.Sin(angle), Mathf.Cos(angle));
    }
    private Quaternion GetJudgeRotation()
    {
        if (sensor.Group == SensorGroup.C)
            return Quaternion.identity;

        var direction = -GetFixedFeedbackPosition();
        var degrees = 180f + Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg;
        return Quaternion.Euler(0f, 0f, -degrees);
    }
    private Vector3 GetJudgePosition(float offset)
    {
        var feedbackPosition = GetFixedFeedbackPosition();
        var radius = feedbackPosition.magnitude;
        if (radius <= Mathf.Epsilon)
            return feedbackPosition;
        return feedbackPosition * (Mathf.Max(0f, radius + offset) / radius);
    }
    private static Vector3 GetAreaPos(int index, char area)
    {
        if (area == 'C') return Vector3.zero;
        if (area == 'B')
        {
            var angle = -index * (Mathf.PI / 4) + Mathf.PI * 5 / 8;
            return new Vector3(Mathf.Cos(angle), Mathf.Sin(angle)) * 2.3f;
        }
        if (area == 'A')
        {
            var angle = -index * (Mathf.PI / 4) + Mathf.PI * 5 / 8;
            return new Vector3(Mathf.Cos(angle), Mathf.Sin(angle)) * 4.1f;
        }
        if (area == 'E')
        {
            var angle = -index * (Mathf.PI / 4) + Mathf.PI * 6 / 8;
            return new Vector3(Mathf.Cos(angle), Mathf.Sin(angle)) * 3.0f;
        }
        if (area == 'D')
        {
            var angle = -index * (Mathf.PI / 4) + Mathf.PI * 6 / 8;
            return new Vector3(Mathf.Cos(angle), Mathf.Sin(angle)) * 4.1f;
        }
        return Vector3.zero;
    }

    private void SetfanColor(Color color)
    {
        for (var i = 0; i < fansSprite.Length; i++)
            fansSprite[i].color = color;
    }
}
