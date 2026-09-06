using Assets.Scripts;
using Assets.Scripts.Types;
using System;
using MajdataCore;
using Unity.Burst.Intrinsics;
using UnityEngine;
#nullable enable
public class TouchDrop : TouchBase
{
    public GameObject justEffect;

    public GameObject multTouchEffect2;
    public GameObject multTouchEffect3;

    public Sprite fanNormalSprite;
    public Sprite fanEachSprite;

    public Sprite pointNormalSprite;
    public Sprite pointEachSprite;

    public Sprite justSprite;

    public Sprite[] multTouchNormalSprite = new Sprite[2];
    public Sprite[] multTouchEachSprite = new Sprite[2];

    public GameObject[] fans;

    // Set from "E1~[4.8]": the Note keeps E1's direction and sensor but is drawn at
    // this distance instead of the area's own. 0 means the area's own distance.
    public float customRadius;

    private readonly SpriteRenderer[] fansSprite = new SpriteRenderer[7];
    private float displayDuration;

    private GameObject firework;
    private Animator fireworkEffect;
    private bool isStarted;
    private bool isTriggered;
    private bool judgeFinalized;
    private int layer;
    private float moveDuration;
    private MultTouchHandler multTouchHandler;
    private NoteEffectManager noteEffectManager;

    private float wholeDuration;

    // Start clears the fans one frame too late to stop them being drawn opaque;
    // see NoteDrop.HideSpriteUntilInitialized. Only the alpha is touched, matching
    // what Start does, because nothing here ever clears forceRenderingOff.
    private void Awake() => HideFansUntilInitialized(fans);

    // Start is called before the first frame update
    void Start()
    {
        wholeDuration = AlphaVisualTiming.GetTouchMotionDuration(speed);
        moveDuration = 0.8f * wholeDuration;
        displayDuration = 0.2f * wholeDuration;

        var notes = GameObject.Find("Notes").transform;
        noteManager = notes.GetComponent<NoteManager>();
        timeProvider = GameObject.Find("AudioTimeProvider").GetComponent<AudioTimeProvider>();
        multTouchHandler = GameObject.Find("MultTouchHandler").GetComponent<MultTouchHandler>();
        objectCounter = GameObject.Find("ObjectCounter").GetComponent<ObjectCounter>();
        noteEffectManager = GameObject.Find("NoteEffects").GetComponent<NoteEffectManager>();
        firework = GameObject.Find("FireworkEffect");
        fireworkEffect = firework.GetComponent<Animator>();

        for (var i = 0; i < 7; i++)
        {
            fansSprite[i] = fans[i].GetComponent<SpriteRenderer>();
            fansSprite[i].sortingOrder += noteSortOrder;
        }

        if (isEach)
        {
            SetfanSprite(fanEachSprite);
            fansSprite[4].sprite = pointEachSprite;
            fansSprite[5].sprite = multTouchEachSprite[0];
            fansSprite[6].sprite = multTouchEachSprite[1];
        }
        else
        {
            SetfanSprite(fanNormalSprite);
            fansSprite[4].sprite = pointNormalSprite;
            fansSprite[5].sprite = multTouchNormalSprite[0];
            fansSprite[6].sprite = multTouchNormalSprite[1];
        }

        justEffect.GetComponent<SpriteRenderer>().sprite = justSprite;

        // Apply COLOR to note sprites only. justEffect is a judgement effect.
        if (colorOverrideMaterial != null)
        {
            for (var fi = 0; fi < 7; fi++)
                fansSprite[fi].sharedMaterial = colorOverrideMaterial;
        }

        transform.position = GetAreaPos(startPosition, areaPosition);
        transform.localScale = Vector3.Scale(transform.localScale,
            new Vector3(noteScale * noteScaleX, noteScale * noteScaleY, 1f));
        // The JUST ring follows the same feedback plane as judgement text so
        // gameplay ZOOM/MOVE/ROTATE transforms every hit visual together.
        justEffect.transform.SetParent(noteEffectManager.transform, false);
        PlaceInFeedbackPlane(
            justEffect.transform,
            noteEffectManager.transform,
            GetFixedFeedbackPosition(),
            GetFixedFeedbackRotation());
        justEffect.SetActive(false);
        SetfanColor(new Color(1f, 1f, 1f, 0f));
        sensor = GameObject.Find("Sensors")
                                   .transform.GetChild((int)GetSensor())
                                   .GetComponent<Sensor>();
        manager = GameObject.Find("Sensors")
                                .GetComponent<SensorManager>();
        inputManager = GameObject.Find("Input")
                                 .GetComponent<InputManager>();
        var customSkin = GameObject.Find("Outline").GetComponent<CustomSkin>();
        judgeText = customSkin.JudgeText;
        judgeTextBreak = customSkin.JudgeText_Break;
        if (!JudgmentDisabled)
            inputManager.BindSensor(Check, GetSensor());
    }
    void Check(object sender,InputEventArgs arg)
    {
        if (JudgmentDisabled || JudgmentSuspended)
            return;
        var type = GetSensor();
        if (arg.Type != type)
            return;
        else if (isJudged || !noteManager.CanJudge(gameObject, type))
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
                FinalizeJudge();
                Destroy(gameObject);
            }
        }
    }
    private void FixedUpdate()
    {
        if (JudgmentDisabled || JudgmentSuspended)
            return;
        var timing = GetJudgeTiming();
        if (!isJudged && timing <= 0.316667f)
        {
            if (GroupInfo is not null)
            {
                if (GroupInfo.Percent > 0.5f && GroupInfo.JudgeResult != null)
                {
                    isJudged = true;
                    judgeResult = (JudgeType)GroupInfo.JudgeResult;
                    FinalizeJudge();
                    Destroy(gameObject);
                }
            }
        }
        else if (!isJudged)
        {
            judgeResult = JudgeType.Miss;
            isJudged = true;
            FinalizeJudge();
            Destroy(gameObject);
        }
        else if (isJudged)
        {
            FinalizeJudge();
            Destroy(gameObject);
        }

        if (GetJudgeTiming() >= 0)
        {
            switch (InputManager.Mode)
            {
                case AutoPlayMode.Enable:
                    judgeResult = JudgeType.Perfect;
                    isJudged = true;
                    break;
                case AutoPlayMode.Random:
                    judgeResult = (JudgeType)UnityEngine.Random.Range(1, 14);
                    isJudged = true;
                    break;
                case AutoPlayMode.DJAuto:
                    if (isTriggered)
                        return;
                    inputManager.ClickSensor(GetSensor(), true);
                    isTriggered = isJudged;
                    break;
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

        judgeResult = result;
        isJudged = true;
    }
    /// <summary>
    /// Puts the note back to how it looked before it ever appeared.
    /// </summary>
    /// <remarks>
    /// Outside its own appearance window nothing here writes the fan colour, so
    /// a note the timeline was dragged back past would otherwise keep the fans
    /// it had faded in, and keep its multi-touch slot with them.
    /// </remarks>
    private void RewindVisualState()
    {
        isStarted = false;
        if (multTouchHandler != null)
            multTouchHandler.cancelTouch(this);
        SetfanColor(Color.clear);
        if (justEffect != null)
            justEffect.SetActive(false);
    }
    // Update is called once per frame
    private void Update()
    {
        if (ClockMovedBackwards())
            RewindVisualState();
        if (IsPausedTimelinePreview && timeProvider.AudioTime > time)
        {
            if (isStarted)
            {
                isStarted = false;
                if (multTouchHandler != null)
                    multTouchHandler.cancelTouch(this);
            }
            SetfanColor(Color.clear);
            justEffect.SetActive(false);
            return;
        }
        // SV changes visual expansion only. Judgement and DJAuto remain tied to
        // the real audio clock through GetJudgeTiming().
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
        var pow = -Mathf.Exp(8 * (timing * 0.4f / moveDuration) - 0.85f) + 0.42f;
        var distance = Mathf.Clamp(pow, 0f, 0.4f);

        if (isMine && !NoteEffectManager.ShowMineHitFeedback)
            justEffect.SetActive(false);
        else if (timing > -0.02f)
            justEffect.SetActive(true);

        if (timing >= 0)
        {
            SetfanColor(Color.white);
            var _pow = -Mathf.Exp(- 0.85f) + 0.42f;
            var _distance = Mathf.Clamp(_pow, 0f, 0.4f);
            for (var i = 0; i < 4; i++)
            {
                fans[i].transform.localPosition = LeafPosition(i, _distance);
            }
            return;
        }

        if (-timing <= wholeDuration && -timing > moveDuration)
        {
            if (!isStarted)
            {
                isStarted = true;
                if (!JudgmentDisabled)
                    multTouchHandler.registerTouch(this);
            }

            SetfanColor(new Color(1f, 1f, 1f, Mathf.Clamp((wholeDuration + timing) / displayDuration, 0f, 1f)));
        }
        else if (-timing < moveDuration)
        {
            if (!isStarted)
            {
                isStarted = true;
                if (!JudgmentDisabled)
                    multTouchHandler.registerTouch(this);
            }

            SetfanColor(Color.white);
        }

        if (float.IsNaN(distance)) distance = 0f;
        for (var i = 0; i < 4; i++)
        {
            fans[i].transform.localPosition = LeafPosition(i, distance);
        }
    }
    private void OnDestroy()
    {
        if (justEffect != null)
            Destroy(justEffect);
        if (inputManager != null)
            inputManager.UnbindSensor(Check, GetSensor());
        if (multTouchHandler != null)
            multTouchHandler.cancelTouch(this);
        if (JudgmentDisabled || HttpHandler.IsReloding || !gameObject.scene.isLoaded)
            return;

        FinalizeJudge();

        if (isFirework && judgeResult != JudgeType.Miss)
        {
            fireworkEffect.SetTrigger("Fire");
            PlaceInFeedbackPlane(
                firework.transform,
                firework.transform.parent,
                GetFixedFeedbackPosition());
        }

        // Effect instantiation is comparatively expensive. Keep judge state
        // advancement ahead of it so a dense touch sweep cannot be blocked.
        PlayJudgeEffect();
    }

    private void FinalizeJudge()
    {
        if (judgeFinalized)
            return;

        judgeFinalized = true;
        if (GroupInfo is not null && judgeResult != JudgeType.Miss)
            GroupInfo.JudgeResult = judgeResult;
        objectCounter.ReportResult(this, judgeResult, isBreak);
        objectCounter.NextTouch(sensor.Type);
    }
    void PlayJudgeEffect()
    {
        if (judgeEffect == null || noteEffectManager == null)
            return;
        if (isMine && !NoteEffectManager.ShowMineHitFeedback)
            return;
        var feedbackRotation = GetFixedFeedbackRotation();
        var plane = noteEffectManager.transform;
        var obj = Instantiate(judgeEffect, plane);
        var _obj = Instantiate(judgeEffect, plane);
        PlaceInFeedbackPlane(obj.transform, plane, Vector3.zero, feedbackRotation);
        PlaceInFeedbackPlane(_obj.transform, plane, Vector3.zero, feedbackRotation);
        var judgeObj = obj.transform.GetChild(0);
        var flObj = _obj.transform.GetChild(0);

        if (sensor.Group != SensorGroup.C)
        {
            PlaceInFeedbackPlane(judgeObj, plane, GetPosition(-0.46f));
            PlaceInFeedbackPlane(flObj, plane, GetPosition(-0.92f));
        }
        else
        {
            PlaceInFeedbackPlane(judgeObj, plane, new Vector3(0, -0.6f, 0));
            PlaceInFeedbackPlane(flObj, plane, new Vector3(0, -1.08f, 0));
        }
        RotateInFeedbackPlane(judgeObj.GetChild(0), plane, GetRoation());
        if (judgeObj.childCount > 1)
            RotateInFeedbackPlane(judgeObj.GetChild(1), plane, GetRoation());
        RotateInFeedbackPlane(flObj.GetChild(0), plane, GetRoation());
        var anim = obj.GetComponent<Animator>();
        var flAnim = _obj.GetComponent<Animator>();
        switch(judgeResult)
        {
            case JudgeType.LateGood:
            case JudgeType.FastGood:
                judgeObj.GetChild(0).gameObject.GetComponent<SpriteRenderer>().sprite = judgeText[1];
                break;
            case JudgeType.LateGreat:
            case JudgeType.LateGreat1:
            case JudgeType.LateGreat2:
            case JudgeType.FastGreat2:
            case JudgeType.FastGreat1:
            case JudgeType.FastGreat:
                judgeObj.GetChild(0).gameObject.GetComponent<SpriteRenderer>().sprite = judgeText[2];
                break;
            case JudgeType.LatePerfect2:
            case JudgeType.FastPerfect2:
            case JudgeType.LatePerfect1:
            case JudgeType.FastPerfect1:
                judgeObj.GetChild(0).gameObject.GetComponent<SpriteRenderer>().sprite = judgeText[3];
                break;
            case JudgeType.Perfect:
                judgeObj.GetChild(0).gameObject.GetComponent<SpriteRenderer>().sprite = judgeText[4];
                break;
            case JudgeType.Miss:
                judgeObj.GetChild(0).gameObject.GetComponent<SpriteRenderer>().sprite = judgeText[0];
                break;
            default:
                break;
        }
        // ALPHA: Break Touch uses the same two-layer judgement text as Break Tap.
        // child0 remains normal text; child1 uses the skin's glowing Break layer,
        // lit and flashed by JudgeBreak. Non-Break or non-CriticalPerfect uses the original Touch animation.
        if (judgeObj.childCount > 1)
        {
            var breakRenderer = judgeObj.GetChild(1).GetComponent<SpriteRenderer>();
            breakRenderer.sprite = judgeTextBreak;
            NoteEffectManager.ApplyJudgeTextAlpha(breakRenderer);
        }
        NoteEffectManager.ApplyJudgeTextAlpha(judgeObj.GetChild(0).GetComponent<SpriteRenderer>());
        if(judgeResult != JudgeType.Miss)
        {
            var hit = Instantiate(tapEffect, plane);
            PlaceInFeedbackPlane(
                hit.transform, plane, GetFixedFeedbackPosition(), feedbackRotation);
        }

        noteEffectManager.PlayFastLate(_obj,flAnim,judgeResult);

        if (isBreak && judgeResult == JudgeType.Perfect)
        {
            anim.SetTrigger("break");
            // JudgeBreak lacks TouchJudge's ifDestroy curve, so explicitly destroy the
            // instantiated judgement object after 0.27s to prevent it from remaining forever.
            Destroy(obj, 1f);
        }
        else
        {
            anim.SetTrigger("touch");
        }
    }
    /// <summary>
    /// Gets a coordinate at the specified distance from the current coordinate
    /// <para>Direction: origin</para>
    /// </summary>
    /// <param name="magnitude"></param>
    /// <param name="distance"></param>
    /// <returns></returns>
    Vector3 GetPosition(float distance)
    {
        var feedbackPosition = GetFixedFeedbackPosition();
        var d = feedbackPosition.magnitude;
        if (d <= Mathf.Epsilon)
            return feedbackPosition;
        var ratio = MathF.Max(0, d + distance) / d;
        return feedbackPosition * ratio;
    }
    public void setLayer(int newLayer)
    {
        layer = newLayer;
        if (layer == 1)
        {
            multTouchEffect2.SetActive(true);
            multTouchEffect3.SetActive(false);
        }
        else if (layer == 2)
        {
            multTouchEffect2.SetActive(false);
            multTouchEffect3.SetActive(true);
        }
        else
        {
            multTouchEffect2.SetActive(false);
            multTouchEffect3.SetActive(false);
        }
    }
    public void layerDown()
    {
        setLayer(layer - 1);
    }

    private Vector3 LeafPosition(int index, float distance) =>
        (0.226f + distance) * GetAngle(index);
    private Vector3 GetAngle(int index)
    {
        var angle = index * (Mathf.PI / 2);
        return new Vector3(Mathf.Sin(angle), Mathf.Cos(angle));
    }

    private Vector3 GetAreaPos(int index, char area)
    {
        /// <summary>
        /// AreaDistance: 
        /// C:   0
        /// E:   3.1
        /// B:   2.21
        /// A,D: 4.8
        /// </summary>
        if (area == 'C') return Vector3.zero;

        // A/B share one set of directions and D/E another, so a custom distance only
        // has to replace the multiplier to stay on the area's own line.
        float distance;
        float angle;
        if (area == 'A' || area == 'B')
        {
            angle = -index * (Mathf.PI / 4) + Mathf.PI * 5 / 8;
            distance = area == 'B' ? 2.3f : 4.1f;
        }
        else if (area == 'D' || area == 'E')
        {
            angle = -index * (Mathf.PI / 4) + Mathf.PI * 6 / 8;
            distance = area == 'E' ? 3.0f : 4.1f;
        }
        else
        {
            return Vector3.zero;
        }

        if (customRadius > 0f)
            distance = customRadius;
        return new Vector3(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
    }

    private void SetfanColor(Color color)
    {
        foreach (var fan in fansSprite) fan.color = color;
    }

    private void SetfanSprite(Sprite sprite)
    {
        for (var i = 0; i < 4; i++) fansSprite[i].sprite = sprite;
    }
}
