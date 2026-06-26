using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Assets.Scripts.Types;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;

public class HttpHandler : MonoBehaviour
{
    public static bool IsReloding { get; set; } = false;

    // True while a real (non-preview) chart is loaded for playback. A live chart
    // must never be disturbed by a note-preview request that arrives late — for
    // example the editor's debounced caret preview racing a play command. If a
    // preview slips through, its inert (previewOnly) notes occupy the judge queue
    // and never advance it, so every real note misses. The flag lets the Preview
    // command reject itself instead of relying on cross-thread timing in the editor.
    private bool liveChartActive;

    private readonly HttpListener http = new();
    private Task listen;
    private string request = "";

    private void Start()
    {
        SceneManager.LoadScene(1);
        http.AuthenticationSchemes = AuthenticationSchemes.Anonymous;
        http.Prefixes.Add("http://localhost:8013/");
        http.Start();
        listen = new Task(httpListen);
        listen.Start();
        print("server started");
    }

    private void Update()
    {
        if (string.IsNullOrEmpty(request)) return;

        IsReloding = false;
        var data = JsonConvert.DeserializeObject<EditRequestjson>(request);
        request = string.Empty;

        var loader = GameObject.Find("DataLoader").GetComponent<JsonDataLoader>();
        var timeProvider = GameObject.Find("AudioTimeProvider").GetComponent<AudioTimeProvider>();
        var bgManager = GameObject.Find("Background").GetComponent<BGManager>();
        var customSkin = GameObject.Find("Outline").GetComponent<CustomSkin>();
        var allPerfect = GameObject.Find("Notes").GetComponent<PlayAllPerfect>();
        var screenRecorder = GameObject.Find("ScreenRecorder").GetComponent<ScreenRecorder>();
        var multTouchHandler = GameObject.Find("MultTouchHandler").GetComponent<MultTouchHandler>();
        var objectCounter = GameObject.Find("ObjectCounter").GetComponent<ObjectCounter>();
        var displayTimeline = loader.GetComponent<DisplayTimelineController>();
        if (displayTimeline == null)
            displayTimeline = loader.gameObject.AddComponent<DisplayTimelineController>();
        var mainCamera = Camera.main;
        var screenEffects = mainCamera != null ? mainCamera.GetComponent<ScreenEffectController>() : null;
        if (mainCamera != null && screenEffects == null)
            screenEffects = mainCamera.gameObject.AddComponent<ScreenEffectController>();

        InputManager.Mode = (AutoPlayMode)(int)data.editorPlayMethod;

        switch(data.control)
        {
            case EditorControlMethod.Start:
                {
                    liveChartActive = true;
                    loader.ClearLoadedNotes(true);
                    customSkin.LoadSkin(data.skin);
                    loader.noteSpeed = (float)(107.25 / (71.4184491 * Mathf.Pow(data.noteSpeed + 0.9975f, -0.985558604f)));
                    loader.touchSpeed = data.touchSpeed;
                    loader.smoothSlideAnime = data.smoothSlideAnime;
                    objectCounter.ComboSetActive(data.comboStatusType);
                    objectCounter.SetSideDisplays(data.showJudgeInfo, data.showComboInfo);
                    var jsonText = File.ReadAllText(data.jsonPath);
                    // Set the time base BEFORE loading notes, then load asynchronously —
                    // exactly like upstream MajdataView. Normal play starts the BGM on the
                    // editor immediately, so the View must not block. The synchronous
                    // LoadJsonImmediate instantiated the whole chart first and only then
                    // called SetStartTime, so by the time the clock started, startAt was
                    // already stale by the load duration; AudioTime then fast-forwarded past
                    // the first notes and they were judged Miss the instant they loaded
                    // (the "scrub then play drops notes" bug). Async load keeps startAt
                    // honest and streams notes in over the next few frames.
                    timeProvider.SetStartTime(data.startAt, data.startTime, data.audioSpeed);
                    loader.LoadJson(jsonText, data.startTime);
                    allPerfect.Configure(data.showAllPerfect);
                    allPerfect.enabled = true;
                    GameObject.Find("MultTouchHandler").GetComponent<MultTouchHandler>().clearSlots();

                    bgManager.LoadBGFromPath(new FileInfo(data.jsonPath).DirectoryName, data.audioSpeed,
                        data.innerBackgroundCover, data.outerBackgroundCover);
                    ConfigureDisplayTimeline(displayTimeline, screenEffects, timeProvider, jsonText, data);
                    if (data.previewFlow && data.startTime >= getChartLength(jsonText))
                        allPerfect.PreviewNow();
                    //GameObject.Find("Notes").GetComponent<NoteManager>().Refresh();
                }
                break;
            case EditorControlMethod.OpStart:
                {
                    liveChartActive = true;
                    loader.ClearLoadedNotes(true);
                    customSkin.LoadSkin(data.skin);
                    loader.noteSpeed = (float)(107.25 / (71.4184491 * Mathf.Pow(data.noteSpeed + 0.9975f, -0.985558604f)));
                    loader.touchSpeed = data.touchSpeed;
                    loader.smoothSlideAnime = data.smoothSlideAnime;
                    objectCounter.ComboSetActive(data.comboStatusType);
                    objectCounter.SetSideDisplays(data.showJudgeInfo, data.showComboInfo);
                    var jsonText = File.ReadAllText(data.jsonPath);
                    loader.LoadJsonImmediate(jsonText, data.startTime);
                    timeProvider.SetStartTime(data.startAt, data.startTime, data.audioSpeed);
                    allPerfect.Configure(data.showAllPerfect);
                    allPerfect.enabled = true;
                    GameObject.Find("MultTouchHandler").GetComponent<MultTouchHandler>().clearSlots();

                    bgManager.LoadBGFromPath(new FileInfo(data.jsonPath).DirectoryName, data.audioSpeed,
                        data.innerBackgroundCover, data.outerBackgroundCover);
                    ConfigureDisplayTimeline(displayTimeline, screenEffects, timeProvider, jsonText, data);
                    bgManager.PlaySongDetail(data.previewTimelineTime, data.audioSpeed);
                    //GameObject.Find("Notes").GetComponent<NoteManager>().Refresh();
                }
                break;
            case EditorControlMethod.Record:
                {
                    // 已在录制中:忽略重复的录制请求。否则下面的 ClearLoadedNotes/LoadJsonImmediate
                    // 会清空并重载谱面,打断正在写入视频管道的录制协程,导致 Pipe is broken。
                    if (screenRecorder.IsRecording)
                        break;
                    liveChartActive = true;
                    loader.ClearLoadedNotes(true);
                    customSkin.LoadSkin(data.skin);
                    var maidataPath = new FileInfo(data.jsonPath).DirectoryName;
                    var recordFrameRate = data.recordFrameRate == 60 ? 60 : 30;
                    loader.noteSpeed = (float)(107.25 / (71.4184491 * Mathf.Pow(data.noteSpeed + 0.9975f, -0.985558604f)));
                    loader.touchSpeed = data.touchSpeed;
                    loader.smoothSlideAnime = data.smoothSlideAnime;
                    objectCounter.ComboSetActive(data.comboStatusType);
                    objectCounter.SetSideDisplays(data.showJudgeInfo, data.showComboInfo);
                    var jsonText = File.ReadAllText(data.jsonPath);
                    loader.LoadJsonImmediate(jsonText, data.startTime);
                    timeProvider.SetStartTime(data.startAt, data.startTime, data.audioSpeed, true, recordFrameRate);
                    allPerfect.Configure(data.showAllPerfect);
                    allPerfect.enabled = true;
                    multTouchHandler.clearSlots();

                    // 录制自动停止的两条路径,取先到者(都置 isRecording=false → 协程收尾出视频):
                    //  1) AP 演出开启时:由 DestroySelf(演出动画放完→ifStopRecording→StopRecording)
                    //     驱动,时机最准。此时 CutoffTime 设较大兜底(谱末+30s),仅防演出因故未触发
                    //     导致录制永不停,正常情况下 DestroySelf 会先停、不会砍掉演出动画。
                    //  2) AP 演出关闭时:演出根本不播、DestroySelf 永不触发,必须靠 CutoffTime 兜底
                    //     ——谱末+5s 自动停止并出视频。
                    screenRecorder.CutoffTime = getChartLength(jsonText) + (data.showAllPerfect ? 30f : 5f);
                    screenRecorder.FrameRate = recordFrameRate;
                    screenRecorder.StartRecording(maidataPath);

                    bgManager.LoadBGFromPath(maidataPath, data.audioSpeed,
                        data.innerBackgroundCover, data.outerBackgroundCover);
                    ConfigureDisplayTimeline(displayTimeline, screenEffects, timeProvider, jsonText, data);
                    bgManager.PlaySongDetail(-5f, data.audioSpeed);
                    GameObject.Find("CanvasButtons").SetActive(false);
                    //GameObject.Find("Notes").GetComponent<NoteManager>().Refresh();
                }
                break;
            case EditorControlMethod.Pause:
                timeProvider.isStart = false;
                displayTimeline.SetPlaybackActive(false);
                GameObject.Find("NoteEffects")?.GetComponent<NoteEffectManager>()?.ResetAllEffects();
                bgManager.PauseVideo();
                break;
            case EditorControlMethod.Stop:
                {
                    liveChartActive = false;
                    // 录制进行中(已在写帧)时,绝不能立即重载场景:那会销毁正在收尾 ffmpeg 的录制
                    // 协程,使 out.mp4 出不来(recording failed)。改为只置停止标志,让协程自然退出
                    // 写帧循环 → 关闭管道 → ffmpeg 编码完成 → 生成视频(协程末尾自行清理状态)。
                    // 仅在非录制时才重载到待机场景。
                    var wasRecording = screenRecorder.IsRecording;
                    screenRecorder.StopRecording();
                    displayTimeline.SetPlaybackActive(false);
                    GameObject.Find("NoteEffects")?.GetComponent<NoteEffectManager>()?.ResetAllEffects();
                    if (!wasRecording)
                    {
                        timeProvider.ResetStartTime();
                        IsReloding = true;
                        SceneManager.LoadScene(1);
                    }
                }
                break;
            case EditorControlMethod.Continue:
                timeProvider.SetStartTime(data.startAt, data.startTime, data.audioSpeed);
                foreach (var wifi in FindObjectsByType<WifiDrop>(FindObjectsSortMode.None))
                    wifi.RefreshAfterResume();
                displayTimeline.SetPlaybackActive(true);
                bgManager.ContinueVideo(data.audioSpeed);
                break;
            case EditorControlMethod.SetDisplay:
                objectCounter.SetSideDisplays(data.showJudgeInfo, data.showComboInfo);
                allPerfect.Configure(data.showAllPerfect);
                displayTimeline.SetImmediateDisplay(
                    data.showJudgeLine,
                    data.showJudgeInfo,
                    data.showComboInfo,
                    data.showJudgeText,
                    data.innerBackgroundCover,
                    data.outerBackgroundCover);
                break;
            case EditorControlMethod.Preview:
                // Preview only ever applies to the standby screen. If a real chart
                // is active, drop the request before touching anything — clearing
                // the loaded notes here would poison playback (see liveChartActive).
                if (liveChartActive)
                    break;
                customSkin.LoadSkin(data.skin);
                allPerfect.enabled = false;
                loader.ClearLoadedNotes(true);
                if (!string.IsNullOrWhiteSpace(data.previewJson))
                {
                    InputManager.Mode = AutoPlayMode.Disable;
                    timeProvider.SetPreviewTime(0f);
                    loader.noteSpeed = (float)(107.25 /
                        (71.4184491 * Mathf.Pow(data.noteSpeed + 0.9975f, -0.985558604f)));
                    loader.touchSpeed = data.touchSpeed;
                    loader.smoothSlideAnime = data.smoothSlideAnime;
                    loader.LoadJson(data.previewJson, -999f, true);
                    displayTimeline.SetPlaybackActive(false);
                    screenEffects?.Configure(null, timeProvider);
                }
                else
                {
                    timeProvider.ResetStartTime();
                }
                break;
        }
    }

    private static void ConfigureDisplayTimeline(
        DisplayTimelineController controller,
        ScreenEffectController screenEffects,
        AudioTimeProvider timeProvider,
        string jsonText,
        EditRequestjson request)
    {
        var chart = JsonConvert.DeserializeObject<Majson>(jsonText);
        controller.Configure(
            chart?.displayTable,
            chart?.subtitleTable,
            request.showJudgeLine,
            request.showJudgeInfo,
            request.showComboInfo,
            request.showJudgeText,
            request.innerBackgroundCover,
            request.outerBackgroundCover);
        screenEffects?.Configure(chart?.effectTable, timeProvider);
    }

    private void OnDestroy()
    {
        http.Stop();
        print("server stoped");
    }

    private void httpListen()
    {
        while (http.IsListening)
        {
            var context = http.GetContext();
            var reader = new StreamReader(context.Request.InputStream);
            var data = reader.ReadToEnd();
            request = data;
            while (request != "") ;
            context.Response.StatusCode = 200;
            var stream = new StreamWriter(context.Response.OutputStream);
            stream.WriteLine("Hello!!!");
            stream.Close();
            context.Response.Close();
        }

        print("exit listen");
    }

    private float getChartLength(string jsonText)
    {
        var majson = JsonConvert.DeserializeObject<Majson>(jsonText);
        if (majson == null)
            return 0f;

        var length = 0f;
        foreach (var timing in majson.timingList)
        {
            length = Math.Max(length, (float)timing.time);
            foreach (var note in timing.noteList)
            {
                if (note.noteType == SimaiNoteType.Slide)
                    length = Math.Max(length, (float)(note.slideStartTime + note.slideTime));
                else
                    length = Math.Max(length, (float)(timing.time + note.holdTime));
            }
        }

        return length;
    }
}
