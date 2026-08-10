using Assets.Scripts.Interfaces;
using Assets.Scripts.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#nullable enable
public class WifiDrop : NoteLongDrop,IFlasher
{
    public bool isDZoneEnd;
    // Start is called before the first frame update
    public GameObject star_slidePrefab;

    public Sprite[] normalSlide = new Sprite[11];
    public Sprite[] eachSlide = new Sprite[11];
    public Sprite[] breakSlide = new Sprite[11];
    public Sprite normalStar;
    public Sprite eachStar;
    public Sprite breakStar;

    public RuntimeAnimatorController slideShine;
    public RuntimeAnimatorController judgeBreakShine;

    public bool isJustR;

    public float timeStart;
    public bool isBreak;
    public bool isGroupPart;
    public bool isGroupPartEnd;

    public int endPosition;
    public int sortIndex;

    public float fadeInTime;
    public float starSpeed;
    public float slideConst;
    float arriveTime = -1;
    public float fullFadeInTime;

    public Material breakMaterial;
    public Material colorOverrideMaterial;
    public float noteScaleX = 1f;
    public float noteScaleY = 1f;

    bool canShine = false;

    public List<int> areaStep = new List<int>();
    public bool smoothSlideAnime = false;

    private readonly List<SpriteRenderer> sbRender = new();

    private readonly List<GameObject> slideBars = new();
    private readonly Vector3[] SlidePositionEnd = new Vector3[3];

    private readonly SpriteRenderer[] spriteRenderer_star = new SpriteRenderer[3];
    private readonly GameObject[] star_slide = new GameObject[3];
    private GameObject slideOK;

    private Vector3 SlidePositionStart;

    private bool isDestroying = false;
    private float lastAudioTime = float.MinValue;

    bool isChecking = false;
    bool isFinished { get => _judgeQueues.All(x => x.Count == 0); }
    bool canCheck = false;
    bool isJudgeInputBound = false;
    Dictionary<GameObject, Guid> guids = new();
    SensorManager sManager;
    List<GameObject> sensors = new();
    List<SensorType> boundSensors = new();
    public List<List<JudgeArea>> _judgeQueues = new();
    public List<List<JudgeArea>> judgeQueues = new();
    public Dictionary<GameObject, List<Sensor>> triggerSensors = new();
    private List<List<JudgeArea>> judgeQueueTemplate = new();

    public float GetAppearanceStartOffset()
    {
        var fadeLeadScale = 1f - Mathf.Clamp(starSpeed, -1f, 1f);
        return (-3f / speed) * fadeLeadScale;
    }

    private void Start()
    {
        fadeInTime = GetAppearanceStartOffset();
        fullFadeInTime = Math.Min(fadeInTime + 0.2f, 0);
        var fadeAnimator = GetComponent<Animator>();
        if (fadeAnimator != null)
            fadeAnimator.enabled = false;
        objectCounter = GameObject.Find("ObjectCounter").GetComponent<ObjectCounter>();
        timeProvider = GameObject.Find("AudioTimeProvider").GetComponent<AudioTimeProvider>();
        var notes = GameObject.Find("Notes").transform;
        for (var i = 0; i < star_slide.Length; i++)
        {
            star_slide[i] = Instantiate(star_slidePrefab, notes);
            spriteRenderer_star[i] = star_slide[i].GetComponent<SpriteRenderer>();
            
            if (isBreak) spriteRenderer_star[i].sprite = breakStar;
            else if (isEach) spriteRenderer_star[i].sprite = eachStar;
            else spriteRenderer_star[i].sprite = normalStar;
            var visualStart = isDZone ? startPosition - 0.5f : startPosition;
            star_slide[i].transform.rotation = Quaternion.Euler(
                0, 0, -22.5f * (8 + i + 2 * (visualStart - 1f)));
            //SlidePositionEnd[i] = getPositionFromDistance(4.8f, i + 3 + startPosition);
            star_slide[i].SetActive(false);
        }

        if (isDZoneEnd)
        {
            var visualEnd = endPosition - 0.5f;
            SlidePositionEnd[0] = getPositionFromDistance(4.8f, visualEnd - 1f);
            SlidePositionEnd[1] = getPositionFromDistance(4.8f, visualEnd);
            SlidePositionEnd[2] = getPositionFromDistance(4.8f, visualEnd + 1f);
        }
        else
        {
            SlidePositionEnd[0] = GameObject.Find("NoteEffects").transform.GetChild(0).GetChild(endPosition - 2 < 0 ? 7 : endPosition - 2).position;
            SlidePositionEnd[1] = GameObject.Find("NoteEffects").transform.GetChild(0).GetChild(endPosition - 1).position;
            SlidePositionEnd[2] = GameObject.Find("NoteEffects").transform.GetChild(0).GetChild(endPosition >= 8 ? 0 : endPosition).position;
        }


        var wifiVisualStart = isDZone ? startPosition - 0.5f : startPosition;
        transform.rotation = Quaternion.Euler(0f, 0f, -45f * (wifiVisualStart - 1f));
        slideBars.Clear();
        for (var i = 0; i < transform.childCount - 1; i++) slideBars.Add(transform.GetChild(i).gameObject);
        slideOK = transform.GetChild(transform.childCount - 1).gameObject; //slideok is the last one
        if (isJustR)
        {
            slideOK.GetComponent<LoadJustSprite>().setR();
        }
        else
        {
            slideOK.GetComponent<LoadJustSprite>().setL();
            slideOK.transform.Rotate(new Vector3(0f, 0f, 180f));
        }

        if (isBreak)
            foreach(var star in star_slide)
            {
                var renderer = star.GetComponent<SpriteRenderer>();
                renderer.sharedMaterial = breakMaterial;
                var controller = star.AddComponent<BreakShineController>();
                controller.enabled = true;
                controller.parent = this;
            }

        slideOK.SetActive(false);
        slideOK.transform.SetParent(transform.parent);
        SlidePositionStart = getPositionFromDistance(4.8f);

        for (var i = 0; i < slideBars.Count; i++)
        {
            slideBars[i].transform.localScale = Vector3.Scale(
                slideBars[i].transform.localScale, new Vector3(noteScaleX, noteScaleY, 1f));
            var sr = slideBars[i].GetComponent<SpriteRenderer>();

            if (isBreak)
            {
                sr.sprite = breakSlide[i];
                sr.sharedMaterial = breakMaterial;
                var controller = slideBars[i].AddComponent<BreakShineController>();
                controller.parent = this;
                controller.enabled = true;
            }
            else if (isEach)
            {
                sr.sprite = eachSlide[i];
            }
            else
            {
                sr.sprite = normalSlide[i];
            }

            sbRender.Add(sr);
            sr.color = new Color(1f, 1f, 1f, 0f);
            sr.sortingOrder = sortIndex--;
            sr.sortingLayerName = "Slide";
        }
        var sManagerObj = GameObject.Find("Sensors");
        sManager = sManagerObj.GetComponent<SensorManager>();

        
        var count = sManagerObj.transform.childCount;
        
        for (int i = 0; i < count; i++)
            sensors.Add(sManagerObj.transform.GetChild(i).gameObject);
        triggerSensors.Clear();
        guids.Clear();
        foreach (var star in star_slide)
        {
            triggerSensors.Add(star, new());
            guids.Add(star, Guid.NewGuid());
        }
        judgeQueueTemplate = judgeQueues
            .Select(queue => new List<JudgeArea>(queue))
            .ToList();
        ResetJudgeState();
        // Match SlideDrop: color/alpha/grayscale override is applied last so
        // neither the default nor break material can replace it.
        if (colorOverrideMaterial != null)
        {
            foreach (var renderer in sbRender)
                renderer.sharedMaterial = colorOverrideMaterial;
            foreach (var renderer in spriteRenderer_star)
                renderer.sharedMaterial = colorOverrideMaterial;
        }
        //for(int i =0; i< 4; i++)
        //{
        //_judgeQueues.Add(new JudgeAreaGroup(new() { judgeQueues[0][i], judgeQueues[1][i], judgeQueues[2][i] }, judgeQueues[0][i].SlideIndex));
        //}
        //foreach(var sensor in sensors)
        //{
        //    var s = sensor.GetComponent<Sensor>();
        //    if (s != null)
        //        s.OnSensorStatusChange += Check;
        //}
        var allSensors = judgeQueues.SelectMany(x => x.SelectMany(y => y.GetSensorTypes()))
                                    .GroupBy(x => x)
                                    .Select(x => x.Key);
        inputManager = GameObject.Find("Input").GetComponent<InputManager>();
        boundSensors.AddRange(allSensors);
    }
    private void BindJudgeInputWhenReady()
    {
        if (previewOnly || isJudgeInputBound || !canCheck)
            return;

        foreach (var sensor in boundSensors)
            inputManager.BindSensor(Check, sensor);
        isJudgeInputBound = true;
    }
    private void FixedUpdate()
    {
        if (previewOnly)
            return;
        /// time      is when the Slide starts
        /// timeStart is when the Slide is fully visible but not yet moving
        /// LastFor   is the Slide duration
        var timing = timeProvider.AudioTime - time;
        var startTiming = timeProvider.AudioTime - timeStart;
        var forceJudgeTiming = time + LastFor + 0.6;

        if (startTiming >= -0.05f)
            canCheck = true;
        else if (timing > 0)
            Running();        

        BindJudgeInputWhenReady();

        if (isFinished)
        {
            HideBar(areaStep.LastOrDefault());
            Judge();
        }
        else if (timeProvider.AudioTime - forceJudgeTiming >= 0)
            TooLateJudge();
    }
    int GetLastIndex()
    {
        if(_judgeQueues.All(x => x.Count == 0))
            return areaStep.LastOrDefault();
        else
        {
            IEnumerable<int>[] queues = new IEnumerable<int>[]
            {
                _judgeQueues[0].Select(x => x.SlideIndex),
                _judgeQueues[1].Select(x => x.SlideIndex),
                _judgeQueues[2].Select(x => x.SlideIndex),
            };
            var _ = queues.SelectMany(x => x)
                          .GroupBy(x => x)
                          .Select(x => x.Key);
            return areaStep[areaStep.FindIndex(x => x == _.Min())];
        }
    }
    void TooLateJudge()
    {
        if (_judgeQueues.Count == 1)
            slideOK.GetComponent<LoadJustSprite>().setLateGd();
        else
            slideOK.GetComponent<LoadJustSprite>().setMiss();
        isJudged = true;
        DestroySelf();
    }
    public void Check(object sender, InputEventArgs arg) => CheckAll();
    void CheckAll()
    {
        if (previewOnly)
            return;
        if (isFinished || !canCheck)
            return;
        else if (isChecking)
            return;
        else if (InputManager.Mode is AutoPlayMode.Enable or AutoPlayMode.Random)
            return;
        isChecking = true;
        for (int i = 0; i < 3; i++)
        {
            var queue = _judgeQueues[i];
            Check(ref queue);
            _judgeQueues[i] = queue;
        }
        isChecking = false;
    }
    void Check(ref List<JudgeArea> judgeQueue)
    {
        if (judgeQueue.Count == 0)
            return;

        var first = judgeQueue.First();
        JudgeArea second = null;

        if (judgeQueue.Count >= 2)
            second = judgeQueue[1];
        var fType = first.GetSensorTypes();
        foreach (var t in fType)
        {
            var sensor = sManager.GetSensor(t);
            first.Judge(t, sensor.Status);
        }

        if (second is not null && (first.CanSkip || first.On))
        {
            var sType = second.GetSensorTypes();
            foreach (var t in sType)
            {
                var sensor = sManager.GetSensor(t);
                second.Judge(t, sensor.Status);
            }

            if (second.IsFinished)
            {
                //HideBar(first.SlideIndex);
                RemoveJudgeAreas(judgeQueue, 2);
                return;
            }
            else if (second.On)
            {
                //HideBar(first.SlideIndex);
                RemoveJudgeAreas(judgeQueue, 1);
                return;
            }
        }

        if (first.IsFinished)
        {
            //HideBar(first.SlideIndex);
            RemoveJudgeAreas(judgeQueue, 1);
            return;
        }
        if (!isFinished)
            HideBar(GetLastIndex());

    }
    void Judge()
    {
        var timing = timeProvider.AudioTime - time;
        var starTiming = timeStart + (time - timeStart) * 0.667;
        var pTime = LastFor / areaStep.Last();
        var judgeTime = time + pTime * (areaStep.LastOrDefault() - 2.1f);// Correct judgement frame
        var stayTime = (time + LastFor) - judgeTime; // Dwell time
        if (!isJudged)
        {
            arriveTime = timeProvider.AudioTime;
            var triggerTime = timeProvider.AudioTime;

            const float totalInterval = 1.2f; // Seconds
            const float nPInterval = 0.4666667f; // Base Perfect interval

            float extInterval = MathF.Min(stayTime / 4, 0.733333f);           // Extra Perfect interval
            float pInterval = MathF.Min(nPInterval + extInterval, totalInterval);// Total Perfect interval
            var ext = MathF.Max(extInterval - 0.4f, 0);
            float grInterval = MathF.Max(0.4f - extInterval, 0);        // Total Great interval
            float gdInterval = MathF.Max(0.3333334f - ext, 0); // Total Good interval

            var diff = judgeTime - triggerTime; // Positive is Fast; negative is Late
            bool isFast = false;
            JudgeType? judge = null;

            if (diff > 0)
                isFast = true;

            var p = pInterval / 2;
            var gr = grInterval / 2;
            var gd = gdInterval / 2;
            diff = MathF.Abs(diff);

            if (gr == 0)
            {
                if (diff >= p)
                    judge = isFast ? JudgeType.FastGood : JudgeType.LateGood;
                else
                    judge = JudgeType.Perfect;
            }
            else
            {
                if (diff >= gr + p || diff >= totalInterval / 2)
                    judge = isFast ? JudgeType.FastGood : JudgeType.LateGood;
                else if (diff >= p)
                    judge = isFast ? JudgeType.FastGreat : JudgeType.LateGreat;
                else
                    judge = JudgeType.Perfect;
            }

            judgeResult = (JudgeType)judge;
            SetJust();
            isJudged = true;
        }
        else if (arriveTime < starTiming && timeProvider.AudioTime >= starTiming + stayTime * 0.667)
            DestroySelf();
        else if (arriveTime >= starTiming && timeProvider.AudioTime >= arriveTime + stayTime * 0.667)
            DestroySelf();
    }
    void HideBar(int endIndex)
    {
        endIndex = Math.Min(endIndex, slideBars.Count - 1);
        for (int i = 0; i <= endIndex; i++)
            slideBars[i].SetActive(false);
    }
    void Running()
    {
        if (InputManager.Mode is AutoPlayMode.Enable or AutoPlayMode.Random or AutoPlayMode.Disable)
            return;
        foreach (var star in star_slide)
        {
            var starRadius = 0.763736616f;
            var starPos = star.transform.position;
            var oldList = new List<Sensor>(triggerSensors[star]);
            triggerSensors[star].Clear();
            foreach (var s in sensors.Select(x => x.GetComponent<RectTransform>()))
            {
                var sensor = s.GetComponent<Sensor>();
                if (sensor.Group == SensorGroup.E || sensor.Group == SensorGroup.D)
                    continue;

                var rCenter = s.position;
                var rWidth = s.rect.width * s.lossyScale.x;
                var rHeight = s.rect.height * s.lossyScale.y;

                var radius = Math.Max(rWidth, rHeight) / 2;

                if ((starPos - rCenter).sqrMagnitude <= (radius * radius + starRadius * starRadius))
                    triggerSensors[star].Add(sensor);
            }
            var untriggerSensors = oldList.Where(x => !triggerSensors[star].Contains(x));

            foreach (var s in untriggerSensors)
                sManager.SetSensorOff(s.Type, guids[star]);
            foreach (var s in triggerSensors[star])
                sManager.SetSensorOn(s.Type, guids[star]);
        }
    }
    // Update is called once per frame
    private void Update()
    {
        var audioTime = timeProvider.AudioTime;
        if (audioTime + 0.001f < lastAudioTime)
        {
            ResetJudgeState();
            RestoreVisualState();
        }
        lastAudioTime = audioTime;

        var startiming = timeProvider.AudioTime - timeStart;
        if (startiming <= 0f)
        {
            RestoreBars();
            var alpha = fadeInTime >= -0.0001f
                ? (startiming >= 0f ? 1f : 0f)
                : Mathf.InverseLerp(fadeInTime, 0f, startiming);
            setSlideBarAlpha(alpha);
            return;
        }

        setSlideBarAlpha(1f);
        foreach (var star in star_slide)
            star.SetActive(true);

        var timing = timeProvider.AudioTime - time;
        if (timing <= 0f)
        {
            canShine = true;
            float alpha;
            alpha = 1f - -timing / (time - timeStart);
            alpha = alpha > 1f ? 1f : alpha;
            alpha = alpha < 0f ? 0f : alpha;

            for (var i = 0; i < star_slide.Length; i++)
            {
                spriteRenderer_star[i].color = new Color(1, 1, 1, alpha);
                star_slide[i].transform.localScale = new Vector3(alpha + 0.5f, alpha + 0.5f, alpha + 0.5f);
                star_slide[i].transform.position = SlidePositionStart;
            }
        }
        else
        {
            UpdateStar();
            Running();
        }
        CheckAll();
    }
    void UpdateStar()
    {
        var process = SvController.GetTypedOnlyProgress(
            time, LastFor, timeProvider.AudioTime, "slide");

        if (process >= 1)
        {
            for (var i = 0; i < star_slide.Length; i++)
            {
                spriteRenderer_star[i].color = Color.white;
                star_slide[i].transform.position = SlidePositionEnd[i];
                star_slide[i].transform.localScale = new Vector3(1.5f, 1.5f, 1.5f);
            }
            switch (InputManager.Mode)
            {
                case AutoPlayMode.Enable:
                case AutoPlayMode.Random:
                    var barIndex = areaStep[(int)(process * (areaStep.Count - 1))];
                    HideBar(barIndex);
                    DestroySelf();
                    judgeQueues.Clear();
                    return;
            }
            if (isFinished && isJudged)
                DestroySelf();
        }
        else
        {
            for (var i = 0; i < star_slide.Length; i++)
            {
                spriteRenderer_star[i].color = Color.white;
                star_slide[i].transform.position =
                    (SlidePositionEnd[i] - SlidePositionStart) * process + SlidePositionStart; //TODO add some runhua
                star_slide[i].transform.localScale = new Vector3(1.5f, 1.5f, 1.5f);
            }
        }
        switch (InputManager.Mode)
        {
            case AutoPlayMode.Enable:
            case AutoPlayMode.Random:
                var barIndex = areaStep[(int)(process * (areaStep.Count - 1))];
                var removeCount = (int)(process * (judgeQueues.Count - 1));
                if (removeCount > 0)
                    judgeQueues.RemoveRange(0, Math.Min(removeCount, judgeQueues.Count));
                HideBar(barIndex);
                break;
        }
    }
    void SetJust()
    {
        switch (judgeResult)
        {
            case JudgeType.FastGreat2:
            case JudgeType.FastGreat1:
            case JudgeType.FastGreat:
                slideOK.GetComponent<LoadJustSprite>().setFastGr();
                break;
            case JudgeType.FastGood:
                slideOK.GetComponent<LoadJustSprite>().setFastGd();
                break;
            case JudgeType.LateGood:
                slideOK.GetComponent<LoadJustSprite>().setLateGd();
                break;
            case JudgeType.LateGreat1:
            case JudgeType.LateGreat2:
            case JudgeType.LateGreat:
                slideOK.GetComponent<LoadJustSprite>().setLateGr();
                break;

        }
    }

    private static void RemoveJudgeAreas(List<JudgeArea> queue, int count)
    {
        count = Math.Min(count, queue.Count);
        if (count > 0)
            queue.RemoveRange(0, count);
    }
    public bool CanShine() => canShine;

    public void RefreshAfterResume()
    {
        if (timeProvider.AudioTime + 0.001f < lastAudioTime)
            ResetJudgeState();
        RestoreVisualState();
        lastAudioTime = timeProvider.AudioTime;
    }

    private void ResetJudgeState()
    {
        _judgeQueues = judgeQueueTemplate
            .Select(queue => new List<JudgeArea>(queue))
            .ToList();
        judgeQueues = judgeQueueTemplate
            .Select(queue => new List<JudgeArea>(queue))
            .ToList();
        foreach (var queue in _judgeQueues)
            foreach (var area in queue)
                area.Reset();
        canCheck = false;
        isChecking = false;
        isJudged = false;
        arriveTime = -1f;
    }

    private void RestoreVisualState()
    {
        var now = timeProvider.AudioTime;
        if (now <= timeStart)
        {
            RestoreBars();
        }
        else
        {
            var process = Mathf.Clamp01((now - time) / LastFor);
            var hiddenEnd = process > 0f && areaStep.Count > 0
                ? areaStep[Mathf.Clamp((int)(process * (areaStep.Count - 1)), 0, areaStep.Count - 1)]
                : -1;
            for (var i = 0; i < slideBars.Count; i++)
                if (slideBars[i] != null)
                    slideBars[i].SetActive(i > hiddenEnd);
        }
        foreach (var star in star_slide)
            if (star != null)
                star.SetActive(now > timeStart);
    }

    private void RestoreBars()
    {
        foreach (var bar in slideBars)
            if (bar != null)
                bar.SetActive(true);
    }

    void DestroySelf()
    {
        foreach (GameObject obj in slideBars)
            obj.SetActive(false);

        for (var i = 0; i < star_slide.Length; i++)
            Destroy(star_slide[i]);
        Destroy(gameObject);
    }
    void OnDestroy()
    {
        if (isDestroying)
            return;
        isDestroying = true;
        if (isJudgeInputBound)
            foreach (var sensor in boundSensors)
                inputManager?.UnbindSensor(Check, sensor);

        if (previewOnly || HttpHandler.IsReloding)
            return;
        ClearTriggeredSensor();

        switch (InputManager.Mode)
        {
            case AutoPlayMode.Enable:
                judgeResult = JudgeType.Perfect;
                SetJust();
                break;
            case AutoPlayMode.Random:
                judgeResult = (JudgeType)UnityEngine.Random.Range(1, 14);
                SetJust();
                break;
        }
        if (objectCounter == null)
            return;
        objectCounter.ReportResult(this, judgeResult, isBreak);
        if (isBreak && judgeResult == JudgeType.Perfect && slideOK != null)
            slideOK.GetComponent<Animator>().runtimeAnimatorController = judgeBreakShine;
        if (slideOK != null)
            slideOK.SetActive(true);
    }
    void ClearTriggeredSensor()
    {
        foreach (var sensor in sensors)
        {
            if (sensor == null)
                continue;
            var s = sensor.GetComponent<Sensor>();
            if (s != null)
            {
                foreach (var id in guids.Values)
                    s.SetOff(id);
            }
        }
    }
    private void setSlideBarAlpha(float alpha)
    {
        foreach (var sr in sbRender)
        {
            var oldColor = sr.color;
            oldColor.a = alpha;
            sr.color = oldColor;
        }
    }
}
