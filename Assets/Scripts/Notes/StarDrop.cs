using Assets.Scripts.Notes;
using Assets.Scripts.Types;
using UnityEngine;
#nullable enable
public class StarDrop : TapBase
{
    private static readonly Color PinkStarExColor = new(1f, 0.6745283f, 0.8829237f, 1f);

    public float rotateSpeed = 1f;

    public bool isDouble;
    public bool isNoHead;
    public bool isFakeStar = false;
    public bool isFakeStarRotate = false;
    public bool usePinkStarExColor;

    public Sprite tapSpr_Double;
    public Sprite eachSpr_Double;
    public Sprite breakSpr_Double;
    public Sprite exSpr_Double;

    public GameObject slide;
    private float slideAppearanceStartOffset;
    private void Start()
    {
        PreLoad();

        if (!isFakeStar && slide != null)
        {
            if (slide.TryGetComponent<SlideDrop>(out var slideDrop))
                slideAppearanceStartOffset = slideDrop.GetAppearanceStartOffset();
            else if (slide.TryGetComponent<WifiDrop>(out var wifiDrop))
                slideAppearanceStartOffset = wifiDrop.GetAppearanceStartOffset();
            else if (slide.TryGetComponent<TouchSlideDrop>(out var touchSlideDrop))
                slideAppearanceStartOffset = touchSlideDrop.GetAppearanceStartOffset();
        }

        if (isDouble)
        {
            exSpriteRender.sprite = exSpr_Double;
            spriteRenderer.sprite = tapSpr_Double;
            if (isEX) exSpriteRender.color = usePinkStarExColor ? PinkStarExColor : exEffectTap;
            if (isEach)
            {
                if (!isMine)
                    lineSpriteRender.sprite = eachLine;
                spriteRenderer.sprite = eachSpr_Double;
                if (isEX) exSpriteRender.color = exEffectEach;
            }

            if (isBreak)
            {
                if (!isMine)
                    lineSpriteRender.sprite = breakLine;
                spriteRenderer.sprite = breakSpr_Double;
                if (isEX) exSpriteRender.color = exEffectBreak;
                spriteRenderer.sharedMaterial = breakMaterial;
            }
        }
        else
        {
            exSpriteRender.sprite = exSpr;
            spriteRenderer.sprite = tapSpr;
            if (isEX) exSpriteRender.color = usePinkStarExColor ? PinkStarExColor : exEffectTap;
            if (isEach)
            {
                if (!isMine)
                    lineSpriteRender.sprite = eachLine;
                spriteRenderer.sprite = eachSpr;
                if (isEX) exSpriteRender.color = exEffectEach;
            }

            if (isBreak)
            {
                if (!isMine)
                    lineSpriteRender.sprite = breakLine;
                spriteRenderer.sprite = breakSpr;
                if (isEX) exSpriteRender.color = exEffectBreak;
                spriteRenderer.sharedMaterial = breakMaterial;
            }
        }

        // ALPHA: apply color override to star circle and guide arc.
        if (colorOverrideMaterial != null)
        {
            spriteRenderer.sharedMaterial = colorOverrideMaterial;
            lineSpriteRender.sharedMaterial = colorOverrideMaterial;
        }
        ApplyExAlpha();

        spriteRenderer.forceRenderingOff = true;
        exSpriteRender.forceRenderingOff = true;

        if(!isNoHead)
        {
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
        State = NoteStatus.Initialized;
    }
    // Update is called once per frame
    protected override void Update()
    {
        if (ClockMovedBackwards() &&
            !isFakeStar &&
            slide != null &&
            timeProvider.AudioTime < time + slideAppearanceStartOffset)
            slide.SetActive(false);

        if ((!timeProvider.isStart && !timeProvider.IsPaused) || timeProvider.AudioTime < GameplayRevealTime)
        {
            tapLine.SetActive(false);
            spriteRenderer.forceRenderingOff = true;
            if (isEX) exSpriteRender.forceRenderingOff = true;
            if (!isFakeStar && slide != null)
                slide.SetActive(false);
            return;
        }
        if (IsPausedTimelinePreview && timeProvider.AudioTime > time)
        {
            tapLine.SetActive(false);
            spriteRenderer.forceRenderingOff = true;
            if (isEX) exSpriteRender.forceRenderingOff = true;
            if (!isFakeStar && slide != null)
                slide.SetActive(true);
            return;
        }

        var songSpeed = timeProvider.CurrentSpeed;
        ActivateSlideWhenDue();

        if (IsBeforeBounceWindow())
        {
            tapLine.SetActive(false);
            spriteRenderer.forceRenderingOff = true;
            if (isEX) exSpriteRender.forceRenderingOff = true;
            return;
        }

        var isBouncing = IsBounceActive();
        var distance = isBouncing ? GetBounceDistance() : GetSvDistance();

        if (isBouncing)
        {
            State = NoteStatus.Running;
            transform.position = getPositionFromDistance(distance);
            transform.localScale = new Vector3(
                noteScale * noteScaleX,
                noteScale * noteScaleY,
                1f);
            var absoluteDistance = Mathf.Abs(distance);
            var bounceLineScale = absoluteDistance / DefaultDestroyRadius;
            if (!isNoHead)
                tapLine.SetActive(absoluteDistance > 0.001f);
            tapLine.transform.localScale = new Vector3(
                bounceLineScale, bounceLineScale, 1f);
        }
        else
        {
            var presentation = GetSpawnPresentation(
                distance, noteScrollPos, ref spawnCrossingMemo);
            if (!presentation.Visible)
            {
                State = NoteStatus.Initialized;
                transform.localScale = Vector3.zero;
                tapLine.SetActive(false);
                spriteRenderer.forceRenderingOff = true;
                if (isEX)
                    exSpriteRender.forceRenderingOff = true;
                return;
            }

            if (!JudgmentDisabled &&
                presentation.Running &&
                isNoHead &&
                !isFakeStar)
            {
                // This star is the only thing that switches the slide on, at
                // "time + slideAppearanceStartOffset" above. A no-head star is
                // already invisible, so it stays alive - silently - until that
                // handover happens; leaving early took the whole slide with it.
                if (slide == null || slide.activeSelf)
                {
                    Destroy(tapLine);
                    Destroy(gameObject);
                }
                else
                {
                    transform.localScale = Vector3.zero;
                    tapLine.SetActive(false);
                    spriteRenderer.forceRenderingOff = true;
                    if (isEX)
                        exSpriteRender.forceRenderingOff = true;
                }
                return;
            }

            State = presentation.Running
                ? NoteStatus.Running
                : NoteStatus.Pending;
            transform.position = getPositionFromDistance(presentation.Distance);
            transform.localScale = new Vector3(
                presentation.Scale * noteScale * noteScaleX,
                presentation.Scale * noteScale * noteScaleY,
                1f);
            var absoluteDistance = Mathf.Abs(presentation.Distance);
            if (!isNoHead)
                tapLine.SetActive(
                    presentation.Running
                        ? absoluteDistance > 0.001f
                        : presentation.Scale > 0.3f);
            var lineScale = absoluteDistance / DefaultDestroyRadius;
            tapLine.transform.localScale = new Vector3(
                lineScale, lineScale, 1f);
        }
        if (!isNoHead)
            UpdateTapLineRotation(
                isBouncing ? distance :
                State == NoteStatus.Pending ? spawnRadius : distance);

        if (isNoHead)
        {
            spriteRenderer.forceRenderingOff = true;
            if (isEX) exSpriteRender.forceRenderingOff = true;
        }
        else
        {
            spriteRenderer.forceRenderingOff = false;
            if (isEX) exSpriteRender.forceRenderingOff = false;
        }

        if (timeProvider.isStart && !isFakeStar)
            transform.Rotate(
                0f, 0f,
                -180f * Time.deltaTime * songSpeed / Mathf.Max(0.01f, rotateSpeed));
        else if (isFakeStarRotate)
            transform.Rotate(0f, 0f, 400f * Time.deltaTime);  
    }

    protected override void BeforeFixedJudgment() => ActivateSlideWhenDue();

    private void ActivateSlideWhenDue()
    {
        if (isFakeStar || slide == null || slide.activeSelf ||
            timeProvider == null || !timeProvider.isStart)
            return;

        if (timeProvider.AudioTime >= time + slideAppearanceStartOffset)
            slide.SetActive(true);
    }

    protected override void OnDestroy()
    {
        if (!isNoHead || isFakeStar)
        {
            base.OnDestroy();
            return;
        }

        if (tapLine != null)
            Destroy(tapLine);
    }
}
