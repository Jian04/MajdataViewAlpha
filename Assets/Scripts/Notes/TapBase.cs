using Assets.Scripts.Types;
using System;
using UnityEngine;
#nullable enable
namespace Assets.Scripts.Notes
{
    public class TapBase: NoteDrop
    {
        public bool isBreak;
        public bool isEX;
        public bool isFirework;
        bool isTriggered = false;

        public Sprite tapSpr;
        public Sprite eachSpr;
        public Sprite breakSpr;
        public Sprite exSpr;

        public Sprite eachLine;
        public Sprite breakLine;

        public RuntimeAnimatorController BreakShine;

        public GameObject tapLine;

        public Color exEffectTap;
        public Color exEffectEach;
        public Color exEffectBreak;

        public Material breakMaterial;
        public Material colorOverrideMaterial;
        public Color noteTintColor = Color.white;
        public float noteScale = 1f;
        public float noteScaleX = 1f;
        public float noteScaleY = 1f;

        protected SpriteRenderer exSpriteRender;
        protected SpriteRenderer lineSpriteRender;

        protected SpriteRenderer spriteRenderer;
        private MaterialPropertyBlock brightnessProperties;
        private bool? tapLineOnOppositeSide;

        protected void ApplyExAlpha()
        {
            if (!isEX || colorOverrideMaterial == null ||
                !colorOverrideMaterial.HasProperty("_NoteAlpha"))
                return;

            var color = exSpriteRender.color;
            color.a *= colorOverrideMaterial.GetFloat("_NoteAlpha");
            exSpriteRender.color = color;
        }

        protected void PreLoad()
        {
            var notes = GameObject.Find("Notes").transform;
            noteManager = notes.GetComponent<NoteManager>();
            tapLine = Instantiate(tapLine, notes);
            tapLine.SetActive(false);
            lineSpriteRender = tapLine.GetComponent<SpriteRenderer>();
            spriteRenderer = GetComponent<SpriteRenderer>();
            brightnessProperties = new MaterialPropertyBlock();
            exSpriteRender = transform.GetChild(0).GetComponent<SpriteRenderer>();
            timeProvider = GameObject.Find("AudioTimeProvider").GetComponent<AudioTimeProvider>();
            objectCounter = GameObject.Find("ObjectCounter").GetComponent<ObjectCounter>();

            spriteRenderer.sortingOrder += noteSortOrder;
            exSpriteRender.sortingOrder += noteSortOrder;

            
        }
        protected void FixedUpdate()
        {
            if (timeProvider == null || noteManager == null)
                return;
            if (previewOnly)
                return;

            var timing = GetJudgeTiming();
            if (!isJudged && timing > 0.15f)
            {
                judgeResult = JudgeType.Miss;
                isJudged = true;
                Destroy(tapLine);
                Destroy(gameObject);
            }
            else if (isJudged)
            {
                Destroy(tapLine);
                Destroy(gameObject);
            }
            else if (timing >= -0.01f)
            {
                switch(InputManager.Mode)
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
                        if (inputManager == null)
                            return;
                        if (isTriggered)
                            return;
                        inputManager.ClickSensor(sensorPos, true);
                        // A queue predecessor can survive until end-of-frame. Retry
                        // until this note actually judges instead of consuming its
                        // only DJAuto attempt.
                        isTriggered = isJudged;
                        break;
                }
            }

        }
        // Update is called once per frame
        protected virtual void Update()
        {
            if ((!timeProvider.isStart && !timeProvider.IsPaused) || timeProvider.AudioTime < 0f)
            {
                tapLine.SetActive(false);
                spriteRenderer.forceRenderingOff = true;
                if (isEX) exSpriteRender.forceRenderingOff = true;
                return;
            }

            var judgeOffset = timeProvider.AudioTime - time;
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
                tapLine.SetActive(distance > 0.001f && distance <= 4.8f);
                tapLine.transform.localScale = new Vector3(bounceLineScale, bounceLineScale, 1f);
            }
            else switch(State)
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
                            State = NoteStatus.Running;
                            goto case NoteStatus.Running;
                        }
                        var pendingScale = Mathf.Clamp01(destScale);
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
                        var absoluteDistance = Mathf.Abs(distance);
                        tapLine.SetActive(absoluteDistance > 0.001f && absoluteDistance <= 4.8f);
                        var lineScale = Mathf.Abs(distance / 4.8f);
                        tapLine.transform.localScale = new Vector3(lineScale, lineScale, 1f);
                    }
                    break;
            }
            UpdateTapLineRotation(State == NoteStatus.Pending ? spawnRadius : distance);

            spriteRenderer.forceRenderingOff = false;
            if (isEX) exSpriteRender.forceRenderingOff = false;
            if (isBreak)
            {
                var extra = Math.Max(Mathf.Sin(timeProvider.GetFrame() * 0.17f) * 0.5f, 0);
                spriteRenderer.GetPropertyBlock(brightnessProperties);
                brightnessProperties.SetFloat("_Brightness", 0.95f + extra);
                spriteRenderer.SetPropertyBlock(brightnessProperties);
            }
        }

        protected void UpdateTapLineRotation(float visualDistance)
        {
            var opposite = visualDistance < 0f;
            if (tapLineOnOppositeSide == opposite)
                return;

            tapLineOnOppositeSide = opposite;
            var dZoneOffset = isDZone ? 22.5f : 0f;
            tapLine.transform.rotation = Quaternion.Euler(
                0, 0, -22.5f + -45f * (startPosition - 1) + dZoneOffset +
                      (opposite ? 180f : 0f));
        }
        protected void Check(object sender, InputEventArgs arg)
        {
            if (previewOnly)
                return;
            if (this == null || !isActiveAndEnabled || sensor == null || inputManager == null || noteManager == null)
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
                    Destroy(tapLine);
                    Destroy(gameObject);
                }
            }
        }
        protected void Judge()
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

            judgeResult = result;
            isJudged = true;
        }
        protected virtual void OnDestroy()
        {
            if (tapLine != null)
                Destroy(tapLine);
            if (inputManager != null)
                inputManager.UnbindArea(Check, sensorPos);
            if (previewOnly || HttpHandler.IsReloding)
                return;
            var effectManager = GameObject.Find("NoteEffects")?.GetComponent<NoteEffectManager>();
            if (effectManager == null) return;
            effectManager.PlayEffect(JudgeQueueKey, isBreak, judgeResult, noteTintColor);
            effectManager.PlayFastLate(JudgeQueueKey, judgeResult);
            if (isFirework && judgeResult != JudgeType.Miss)
            {
                var firework = GameObject.Find("FireworkEffect");
                var animator = firework?.GetComponent<Animator>();
                if (animator != null)
                {
                    firework.transform.position = transform.position;
                    animator.SetTrigger("Fire");
                }
            }
            objectCounter.NextNote(JudgeQueueKey);
            objectCounter.ReportResult(this, judgeResult,isBreak);
        }
    }
}
