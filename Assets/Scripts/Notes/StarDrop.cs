using Assets.Scripts.Notes;
using Assets.Scripts.Types;
using UnityEngine;
#nullable enable
public class StarDrop : TapBase
{
    public float rotateSpeed = 1f;

    public bool isDouble;
    public bool isNoHead;
    public bool isFakeStar = false;
    public bool isFakeStarRotate = false;

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
            if (isEX) exSpriteRender.color = exEffectTap;
            if (isEach)
            {
                lineSpriteRender.sprite = eachLine;
                spriteRenderer.sprite = eachSpr_Double;
                if (isEX) exSpriteRender.color = exEffectEach;
            }

            if (isBreak)
            {
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
            if (isEX) exSpriteRender.color = exEffectTap;
            if (isEach)
            {
                lineSpriteRender.sprite = eachLine;
                spriteRenderer.sprite = eachSpr;
                if (isEX) exSpriteRender.color = exEffectEach;
            }

            if (isBreak)
            {
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
            if (!previewOnly)
                BindJudgeInput(Check);
        }
        State = NoteStatus.Initialized;
    }
    // Update is called once per frame
    protected override void Update()
    {
        if ((!timeProvider.isStart && !timeProvider.IsPaused) || timeProvider.AudioTime < 0f)
        {
            tapLine.SetActive(false);
            spriteRenderer.forceRenderingOff = true;
            if (isEX) exSpriteRender.forceRenderingOff = true;
            if (!isFakeStar && slide != null)
                slide.SetActive(false);
            return;
        }

        var songSpeed = timeProvider.CurrentSpeed;
        var judgeOffset = timeProvider.AudioTime - time;
        if (!isFakeStar && slide != null && !slide.activeSelf &&
            timeProvider.AudioTime >= time + slideAppearanceStartOffset)
            slide.SetActive(true);

        if (IsBeforeBounceWindow(judgeOffset))
        {
            tapLine.SetActive(false);
            spriteRenderer.forceRenderingOff = true;
            if (isEX) exSpriteRender.forceRenderingOff = true;
            return;
        }

        var isBouncing = IsBounceActive(judgeOffset);
        var distance = isBouncing ? GetBounceDistance(judgeOffset) : GetSvDistance();
        var destScale = GetSpawnScale(distance);
        var hasLeftSpawn = HasLeftSpawnAtCurrentTime(noteScrollPos);

        if (isBouncing)
        {
            State = NoteStatus.Running;
            transform.position = getPositionFromDistance(distance);
            transform.localScale = new Vector3(
                noteScale * noteScaleX,
                noteScale * noteScaleY,
                1f);
            var bounceLineScale = Mathf.Clamp01(distance / 4.8f);
            if (!isNoHead)
                tapLine.SetActive(distance > 0.001f && distance <= 4.8f);
            tapLine.transform.localScale = new Vector3(
                bounceLineScale, bounceLineScale, 1f);
        }
        else switch (State)
        {
            case NoteStatus.Initialized:
                if (hasLeftSpawn)
                {
                    State = NoteStatus.Running;
                    goto case NoteStatus.Running;
                }
                if (destScale >= 0f)
                {
                    State = NoteStatus.Pending;
                    goto case NoteStatus.Pending;
                }
                else
                    transform.localScale = new Vector3(0, 0);
                return;
            case NoteStatus.Pending:
                {
                    if (hasLeftSpawn)
                    {
                        if (!isFakeStar && !slide.activeSelf)
                        {
                            slide.SetActive(true);
                            if (isNoHead)
                            {
                                Destroy(tapLine);
                                Destroy(gameObject);
                                return;
                            }
                        }
                        State = NoteStatus.Running;
                        goto case NoteStatus.Running;
                    }
                    var pendingScale = Mathf.Clamp01(destScale);
                    if (!isNoHead)
                        tapLine.SetActive(pendingScale > 0.3f);
                    transform.localScale = new Vector3(
                        pendingScale * noteScale * noteScaleX,
                        pendingScale * noteScale * noteScaleY,
                        1f);
                    transform.position = getPositionFromDistance(spawnRadius);
                    var lineScale = Mathf.Abs(spawnRadius / 4.8f);
                    tapLine.transform.localScale = new Vector3(lineScale, lineScale, 1f);
                }
                break;
            case NoteStatus.Running:
                {
                    transform.position = getPositionFromDistance(distance);
                    transform.localScale = new Vector3(
                        noteScale * noteScaleX,
                        noteScale * noteScaleY,
                        1f);
                    if (!isNoHead)
                    {
                        var absoluteDistance = Mathf.Abs(distance);
                        tapLine.SetActive(absoluteDistance > 0.001f && absoluteDistance <= 4.8f);
                    }
                    var lineScale = Mathf.Abs(distance / 4.8f);
                    tapLine.transform.localScale = new Vector3(lineScale, lineScale, 1f);
                }
                break;
        }
        if (!isNoHead)
            UpdateTapLineRotation(State == NoteStatus.Pending ? spawnRadius : distance);

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
            transform.Rotate(0f, 0f, -180f * Time.deltaTime * songSpeed / rotateSpeed);
        else if (isFakeStarRotate)
            transform.Rotate(0f, 0f, 400f * Time.deltaTime);  
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
