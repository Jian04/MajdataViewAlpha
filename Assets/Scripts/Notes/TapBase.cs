using Assets.Scripts.Types;
using System;
using System.Collections.Generic;
using UnityEngine;
#nullable enable
namespace Assets.Scripts.Notes
{
    public class TapBase: NoteDrop
    {
        public bool isBreak;
        public bool isEX;
        public bool isFirework;
        public bool isMine;
        bool isTriggered = false;

        public Sprite tapSpr;
        public Sprite eachSpr;
        public Sprite breakSpr;
        public Sprite exSpr;

        public Sprite eachLine;
        public Sprite breakLine;

        /// <summary>
        /// Redirects the pictures this Note can wear to the image named by
        /// <c>customSkin</c>, so a skinned Note keeps that image whether it is a
        /// plain, each or break Note. Does nothing when no skin was written.
        /// </summary>
        /// <remarks>
        /// Called from Start, not Awake: the loader fills <c>customSkin</c> in after
        /// the object exists, which is after Awake has already run.
        ///
        /// The EX sprite is deliberately left alone. It is not another picture of the
        /// Note, it is the halo drawn on a separate renderer underneath one; pointing
        /// it at the skin too would draw the image twice, once oversized. The each and
        /// break guide lines are left alone for the same reason: a line belongs to the
        /// pair of Notes it joins, not to either one of them.
        /// </remarks>
        protected void ApplyCustomSkinToSprites()
        {
            var skin = ResolveCustomSkin(tapSpr);
            if (skin == null)
                return;
            tapSpr = skin;
            eachSpr = skin;
            breakSpr = skin;
        }

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
        private Vector2? liveScaleDefault;

        protected SpriteRenderer exSpriteRender;
        protected SpriteRenderer lineSpriteRender;

        protected SpriteRenderer spriteRenderer;
        private MaterialPropertyBlock brightnessProperties;
        private bool? tapLineOnOppositeSide;

        protected override IEnumerable<SpriteRenderer> GetLiveVisualRenderers()
        {
            foreach (var renderer in base.GetLiveVisualRenderers())
                if (renderer != null)
                    yield return renderer;
            if (lineSpriteRender != null)
                yield return lineSpriteRender;
        }

        protected virtual void Awake() => HideSpriteUntilInitialized(transform);

        protected void ApplyExAlpha()
        {
            if (!isEX || colorOverrideMaterial == null ||
                !colorOverrideMaterial.HasProperty("_NoteAlpha"))
                return;

            var color = exSpriteRender.color;
            color.a *= colorOverrideMaterial.GetFloat("_NoteAlpha");
            exSpriteRender.color = color;
        }

        public override void ApplyLiveScale(Vector2? scale)
        {
            liveScaleDefault ??= new Vector2(noteScaleX, noteScaleY);
            var value = scale ?? liveScaleDefault.Value;
            noteScaleX = value.x;
            noteScaleY = value.y;
        }

        protected void PreLoad()
        {
            var notes = noteManager != null ? noteManager.transform : GameObject.Find("Notes").transform;
            if (noteManager == null) noteManager = notes.GetComponent<NoteManager>();
            tapLine = Instantiate(tapLine, notes);
            tapLine.SetActive(false);
            lineSpriteRender = tapLine.GetComponent<SpriteRenderer>();
            spriteRenderer = GetComponent<SpriteRenderer>();
            brightnessProperties = new MaterialPropertyBlock();
            exSpriteRender = transform.GetChild(0).GetComponent<SpriteRenderer>();
            if (timeProvider == null) timeProvider = GameObject.Find("AudioTimeProvider").GetComponent<AudioTimeProvider>();
            if (objectCounter == null) objectCounter = GameObject.Find("ObjectCounter").GetComponent<ObjectCounter>();

            spriteRenderer.sortingOrder += noteSortOrder;
            exSpriteRender.sortingOrder += noteSortOrder;

            // Both Tap and Star reach their sprite fields through here, and both do it
            // before assigning any of them to a renderer, which is what makes this the
            // one place a per-Note skin has to be swapped in.
            ApplyCustomSkinToSprites();

            
        }
        protected void FixedUpdate()
        {
            if (timeProvider == null || noteManager == null)
                return;

            if (JudgmentDisabled || JudgmentSuspended)
                return;

            BeforeFixedJudgment();

            var timing = GetJudgeTiming();
            if (!isJudged && timing > MissWindow)
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

        /// <summary>
        /// Lets a note prepare dependent visuals before this frame can judge and
        /// destroy it. The default is intentionally empty.
        /// </summary>
        protected virtual void BeforeFixedJudgment()
        {
        }
        // Update is called once per frame
        protected virtual void Update()
        {
            if ((!timeProvider.isStart && !timeProvider.IsPaused) || timeProvider.AudioTime < GameplayRevealTime)
            {
                tapLine.SetActive(false);
                spriteRenderer.forceRenderingOff = true;
                if (isEX) exSpriteRender.forceRenderingOff = true;
                return;
            }
            if (IsPausedTimelinePreview && timeProvider.AudioTime > time)
            {
                tapLine.SetActive(false);
                spriteRenderer.forceRenderingOff = true;
                if (isEX) exSpriteRender.forceRenderingOff = true;
                return;
            }

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
                tapLine.SetActive(absoluteDistance > 0.001f);
                tapLine.transform.localScale = new Vector3(bounceLineScale, bounceLineScale, 1f);
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

                State = presentation.Running
                    ? NoteStatus.Running
                    : NoteStatus.Pending;
                transform.position = getPositionFromDistance(presentation.Distance);
                transform.localScale = new Vector3(
                    presentation.Scale * noteScale * noteScaleX,
                    presentation.Scale * noteScale * noteScaleY,
                    1f);
                var absoluteDistance = Mathf.Abs(presentation.Distance);
                tapLine.SetActive(
                    presentation.Running
                        ? absoluteDistance > 0.001f
                        : presentation.Scale > 0.3f);
                var lineScale = absoluteDistance / DefaultDestroyRadius;
                tapLine.transform.localScale = new Vector3(
                    lineScale, lineScale, 1f);
            }
            UpdateTapLineRotation(
                isBouncing ? distance :
                State == NoteStatus.Pending ? spawnRadius : distance);

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
            if (JudgmentDisabled || JudgmentSuspended)
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
            if (JudgmentDisabled || HttpHandler.IsReloding)
                return;
            var effectManager = GameObject.Find("NoteEffects")?.GetComponent<NoteEffectManager>();
            if (effectManager != null &&
                (!isMine || NoteEffectManager.ShowMineHitFeedback))
            {
                effectManager.PlayEffect(
                    JudgeQueueKey, destroyRadius, isBreak, judgeResult, noteTintColor);
                effectManager.PlayFastLate(JudgeQueueKey, destroyRadius, judgeResult);
            }
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
