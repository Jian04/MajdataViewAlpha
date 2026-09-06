using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Assets.Scripts.Types;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;

public class HttpHandler : MonoBehaviour
{
    private const int ProtocolVersion = 1;
    public static bool IsReloding { get; set; } = false;

    // True while a real (non-preview) chart is loaded for playback. A live chart
    // must never be disturbed by a note-preview request that arrives late — for
    // example the editor's debounced caret preview racing a play command. If a
    // preview slips through, the shared loader and clock are cleared for preview
    // content, so the live DJAuto chart can no longer advance correctly. The flag
    // rejects Preview instead of relying on cross-thread timing in the editor.
    private bool liveChartActive;
    private bool pausedTimelinePreviewActive;
    private bool playbackStartDeferred;

    private readonly HttpListener http = new();
    private readonly ManualResetEventSlim requestCompleted = new(true);
    private Task listen;
    private volatile string request = "";
    private volatile int responseStatusCode = 500;
    private string responseBody =
        "{\"ok\":false,\"protocolVersion\":1,\"error\":\"Request was not processed.\"}";
    private bool deferredStartCompletion;
    private int playbackActivationGeneration;
    private GameObject generatedMark;
    private GameObject currentTimeText;
    private bool showGeneratedMark;
    private int viewDisplayFontPreset;

    private void Start()
    {
        SceneManager.LoadScene(1);
        gameObject.AddComponent<VisualChartEditor>();
        http.AuthenticationSchemes = AuthenticationSchemes.Anonymous;
        http.Prefixes.Add("http://localhost:8013/");
        http.Start();
        listen = new Task(httpListen);
        listen.Start();
        print("server started");
    }

    private static T FindSceneComponent<T>(string objectName) where T : Component
    {
        var obj = GameObject.Find(objectName);
        return obj != null ? obj.GetComponent<T>() : null;
    }

    private void Update()
    {
        if (string.IsNullOrEmpty(request) || deferredStartCompletion) return;

        IsReloding = false;
        EditRequestjson data;
        try
        {
            data = JsonConvert.DeserializeObject<EditRequestjson>(request);
        }
        catch (JsonException exception)
        {
            Debug.LogWarning("[MajdataView] Ignored an invalid Edit request: " + exception.Message);
            CompleteRequest(
                false,
                "Invalid request JSON: " + exception.Message,
                400);
            return;
        }
        if (data == null)
        {
            Debug.LogError("[MajdataView] Ignored an empty or invalid Edit request.");
            CompleteRequest(false, "Request body is empty.", 400);
            return;
        }
        if (data.protocolVersion is not (0 or ProtocolVersion))
        {
            CompleteRequest(
                false,
                $"Unsupported protocol version {data.protocolVersion}; expected {ProtocolVersion}.",
                409);
            return;
        }
        NoteEffectManager.ShowMineHitFeedback = data.showMineHitFeedback;

        var loader = FindSceneComponent<JsonDataLoader>("DataLoader");
        var timeProvider = FindSceneComponent<AudioTimeProvider>("AudioTimeProvider");
        var bgManager = FindSceneComponent<BGManager>("Background");
        var customSkin = FindSceneComponent<CustomSkin>("Outline");
        var allPerfect = FindSceneComponent<PlayAllPerfect>("Notes");
        var screenRecorder = FindSceneComponent<ScreenRecorder>("ScreenRecorder");
        var multTouchHandler = FindSceneComponent<MultTouchHandler>("MultTouchHandler");
        var objectCounter = FindSceneComponent<ObjectCounter>("ObjectCounter");
        var input = FindSceneComponent<InputManager>("Input");
        var sensors = FindSceneComponent<SensorManager>("Sensors");
        var noteEffects = FindSceneComponent<NoteEffectManager>("NoteEffects");
        if (data.control is EditorControlMethod.Start or EditorControlMethod.OpStart or
            EditorControlMethod.Record or EditorControlMethod.Continue or
            EditorControlMethod.SetDisplay or EditorControlMethod.Preview or
            EditorControlMethod.TimelinePreview)
        {
            ViewLocalization.SetLanguage(data.language);
            showGeneratedMark = data.showGeneratedMark;
            viewDisplayFontPreset = data.viewDisplayFontPreset;
            var visualEditor = GetComponent<VisualChartEditor>();
            if (visualEditor != null)
                visualEditor.enabled = data.enableVisualChartEditor;
        }
        if (loader == null || timeProvider == null || bgManager == null || customSkin == null ||
            allPerfect == null || screenRecorder == null || multTouchHandler == null ||
            objectCounter == null || input == null || sensors == null || noteEffects == null)
        {
            // Scene reload is asynchronous. Do not consume Start/Preview requests until
            // the original View objects exist, otherwise notes can instantiate before
            // Input/Sensors and crash in TapDrop.Start().
            return;
        }
        var displayTimeline = loader.GetComponent<DisplayTimelineController>();
        if (displayTimeline == null)
            displayTimeline = loader.gameObject.AddComponent<DisplayTimelineController>();
        var mediaTimeline = loader.GetComponent<MediaTimelineController>();
        if (mediaTimeline == null)
            mediaTimeline = loader.gameObject.AddComponent<MediaTimelineController>();
        var mainCamera = Camera.main;
        var screenEffects = mainCamera != null ? mainCamera.GetComponent<ScreenEffectController>() : null;
        if (mainCamera != null && screenEffects == null)
            screenEffects = mainCamera.gameObject.AddComponent<ScreenEffectController>();

        if (data.control is EditorControlMethod.Start or EditorControlMethod.OpStart or
            EditorControlMethod.Record or EditorControlMethod.Continue or
            EditorControlMethod.SetDisplay or EditorControlMethod.Preview or
            EditorControlMethod.TimelinePreview)
        {
            bgManager.LoadStandbyTheme(data.standbyTheme);
            bgManager.SetIntroBgTheme(data.introBgTheme);
            bgManager.SetBackgroundClip(data.clipBackgroundToRing);
        }

        var deferResponse = false;
        string commandError = null;
        try
        {
        // A preview is allowed to arrive after Start when the editor is scrubbed and
        // Play is pressed immediately. Dropping that stale preview below is not enough:
        // changing the global mode first would silently disable DJAuto for the live chart.
        if (data.control != EditorControlMethod.Continue &&
            (data.control != EditorControlMethod.Preview || !liveChartActive))
            InputManager.Mode = (AutoPlayMode)(int)data.editorPlayMethod;

        switch(data.control)
        {
            case EditorControlMethod.Start:
                {
                    playbackActivationGeneration++;
                    playbackStartDeferred = data.deferPlaybackStart;
                    var replacePausedPreview = pausedTimelinePreviewActive;
                    MajdataPetClient.Trigger("running", "Playing chart...");
                    Debug.Log($"[MajdataView] Start request: t={data.startTime:F3}, mode={data.editorPlayMethod}");
                    liveChartActive = true;
                    pausedTimelinePreviewActive = false;
                    ApplyGeneratedMarkVisibility();
                    if (replacePausedPreview)
                    {
                        loader.CancelPendingLoad();
                        // pausedTimelinePreviewActive just went false, so the notes
                        // the paused preview built stop taking the preview branch
                        // and start animating against the playback clock. Retiring
                        // them once the new chart finished binding was too late:
                        // they fly in first as extra notes nothing in the chart
                        // asked for.
                        loader.ClearPreviewNotes();
                    }
                    else
                    {
                        loader.ClearLoadedNotes(true);
                    }
                    customSkin.LoadSkin(data.skin, data.tapSkin, data.holdSkin, data.starSkin, data.pinkStar);
                    loader.noteSpeed = (float)(107.25 / (71.4184491 * Mathf.Pow(data.noteSpeed + 0.9975f, -0.985558604f)));
                    loader.touchSpeed = data.touchSpeed;
                    loader.starSpeed = data.starSpeed;
                    loader.smoothSlideAnime = data.smoothSlideAnime;
                    objectCounter.ComboSetActive(data.comboStatusType);
                    objectCounter.SetSideDisplays(data.showJudgeInfo, data.showComboInfo);
                    objectCounter.SetDisplayFont(data.viewDisplayFontPreset);
                    var jsonText = File.ReadAllText(data.jsonPath);
                    // Keep DJAuto and the shared clock inert while the first playable
                    // slice binds. This prevents a stale preview or half-bound note from
                    // occupying one judge slot after an immediate scrub-and-play.
                    var requestedMode = (AutoPlayMode)(int)data.editorPlayMethod;
                    InputManager.Mode = AutoPlayMode.Disable;
                    timeProvider.SetStartTime(DateTime.MaxValue.Ticks, data.startTime, data.audioSpeed);
                    loader.LoadJson(
                        jsonText,
                        data.startTime,
                        previewOnly: false,
                        preserveTintCache: replacePausedPreview);
                    allPerfect.Configure(data.showAllPerfect);
                    allPerfect.enabled = true;
                    GameObject.Find("MultTouchHandler").GetComponent<MultTouchHandler>().clearSlots();

                    NoteSkinLibrary.SetChartFolder(
                        new FileInfo(data.jsonPath).DirectoryName);
                    bgManager.LoadBGFromPath(new FileInfo(data.jsonPath).DirectoryName, data.audioSpeed,
                        data.innerBackgroundCover, data.outerBackgroundCover, data.showSongDetail,
                        data.backgroundFitMode, !HasTimelineVideo(jsonText));
                    ConfigureDisplayTimeline(displayTimeline, screenEffects, mediaTimeline,
                        timeProvider, jsonText, data);
                    if (data.previewFlow && data.startTime >= getChartLength(jsonText))
                        allPerfect.PreviewNow();
                    deferredStartCompletion = true;
                    StartCoroutine(CompleteAsyncStartWhenPlayable(
                        loader, timeProvider, sensors, multTouchHandler, requestedMode,
                        bgManager, mediaTimeline, data.startTime, data.audioSpeed,
                        data.deferPlaybackStart, replacePausedPreview));
                    deferResponse = true;
                    //GameObject.Find("Notes").GetComponent<NoteManager>().Refresh();
                }
                break;
            case EditorControlMethod.OpStart:
                {
                    playbackActivationGeneration++;
                    playbackStartDeferred = false;
                    MajdataPetClient.Trigger("running", "Playing chart...");
                    liveChartActive = true;
                    pausedTimelinePreviewActive = false;
                    ApplyGeneratedMarkVisibility();
                    loader.ClearLoadedNotes(true);
                    customSkin.LoadSkin(data.skin, data.tapSkin, data.holdSkin, data.starSkin, data.pinkStar);
                    loader.noteSpeed = (float)(107.25 / (71.4184491 * Mathf.Pow(data.noteSpeed + 0.9975f, -0.985558604f)));
                    loader.touchSpeed = data.touchSpeed;
                    loader.starSpeed = data.starSpeed;
                    loader.smoothSlideAnime = data.smoothSlideAnime;
                    objectCounter.ComboSetActive(data.comboStatusType);
                    objectCounter.SetSideDisplays(data.showJudgeInfo, data.showComboInfo);
                    objectCounter.SetDisplayFont(data.viewDisplayFontPreset);
                    var jsonText = File.ReadAllText(data.jsonPath);
                    allPerfect.Configure(data.showAllPerfect);
                    allPerfect.enabled = true;
                    multTouchHandler.clearSlots();

                    NoteSkinLibrary.SetChartFolder(
                        new FileInfo(data.jsonPath).DirectoryName);
                    bgManager.LoadBGFromPath(new FileInfo(data.jsonPath).DirectoryName, data.audioSpeed,
                        data.innerBackgroundCover, data.outerBackgroundCover, data.showSongDetail,
                        data.backgroundFitMode, !HasTimelineVideo(jsonText));
                    ConfigureDisplayTimeline(displayTimeline, screenEffects, mediaTimeline,
                        timeProvider, jsonText, data);
                    if (data.previewFlow)
                    {
                        // Negative-time preview used to instantiate the whole chart on
                        // Unity's main thread. Load it through the normal async pipeline,
                        // then acknowledge Edit only when every runtime binding is ready.
                        var requestedMode = (AutoPlayMode)(int)data.editorPlayMethod;
                        InputManager.Mode = AutoPlayMode.Disable;
                        timeProvider.SetStartTime(DateTime.MaxValue.Ticks,
                            data.previewTimelineTime, data.audioSpeed);
                        loader.LoadJson(jsonText, data.startTime);
                        deferredStartCompletion = true;
                        StartCoroutine(CompleteAsyncIntroWhenPlayable(
                            loader, timeProvider, sensors, multTouchHandler, requestedMode,
                            bgManager, mediaTimeline, data.previewTimelineTime, data.audioSpeed,
                            data.showSongDetail));
                        deferResponse = true;
                    }
                    else
                    {
                        loader.LoadJsonImmediate(jsonText, data.startTime);
                        loader.WarmupRenderingMaterials();
                        timeProvider.SetStartTime(data.startAt, data.startTime, data.audioSpeed);
                        mediaTimeline.SetPlaybackActive(true);
                        if (data.showSongDetail)
                            bgManager.PlaySongDetail(data.previewTimelineTime, data.audioSpeed);
                        else
                            bgManager.HideSongDetail();
                    }
                    //GameObject.Find("Notes").GetComponent<NoteManager>().Refresh();
                }
                break;
            case EditorControlMethod.Record:
                {
                    playbackActivationGeneration++;
                    playbackStartDeferred = false;
                    MajdataPetClient.Trigger("running", "Recording chart...");
                    // Reserve the encoder before resize/configuration so duplicate Record or
                    // Stop requests cannot race the named-pipe startup.
                    if (!screenRecorder.PrepareRecording())
                    {
                        commandError =
                            "A recording is already starting, active, or finalizing.";
                        break;
                    }
                    try
                    {
                    liveChartActive = true;
                    pausedTimelinePreviewActive = false;
                    ApplyGeneratedMarkVisibility();
                    loader.ClearLoadedNotes(true);
                    customSkin.LoadSkin(data.skin, data.tapSkin, data.holdSkin, data.starSkin, data.pinkStar);
                    var maidataPath = new FileInfo(data.jsonPath).DirectoryName;
                    // A 30 FPS simulation step exceeds the CriticalPerfect window.
                    var recordFrameRate = data.recordFrameRate >= 120 ? 120 : 60;
                    loader.noteSpeed = (float)(107.25 / (71.4184491 * Mathf.Pow(data.noteSpeed + 0.9975f, -0.985558604f)));
                    loader.touchSpeed = data.touchSpeed;
                    loader.starSpeed = data.starSpeed;
                    loader.smoothSlideAnime = data.smoothSlideAnime;
                    objectCounter.ComboSetActive(data.comboStatusType);
                    objectCounter.SetSideDisplays(data.showJudgeInfo, data.showComboInfo);
                    objectCounter.SetDisplayFont(data.viewDisplayFontPreset);
                    var jsonText = File.ReadAllText(data.jsonPath);
                    loader.LoadJsonImmediate(jsonText, data.startTime);
                    timeProvider.SetStartTime(data.startAt, data.startTime, data.audioSpeed, true, recordFrameRate);
                    allPerfect.Configure(data.showAllPerfect);
                    allPerfect.enabled = true;
                    multTouchHandler.clearSlots();

                    // Use Edit's calculated final-note time so the audio mix and View stop on the same clock.
                    // AP's actual clip is 3.1166666 seconds; PlayAllPerfect replaces this fallback with
                    // a stop scheduled from the exact frame on which the animation is revealed.
                    const float allPerfectDuration = 3.1166666f;
                    var chartEnd = data.chartLength > 0f ? data.chartLength : getChartLength(jsonText);
                    screenRecorder.CutoffTime = chartEnd +
                        (data.showAllPerfect ? allPerfectDuration + 3f : 5f);
                    // Layered export is disabled until full-screen filters can be isolated correctly.
                    screenRecorder.RevealOutput = data.revealOutput;
                    screenRecorder.ShowSongDetail = data.showSongDetail;
                    screenRecorder.ScreenEffects = screenEffects;
                    screenRecorder.MediaTimeline = mediaTimeline;
                    screenRecorder.FrameRate = recordFrameRate;
                    screenRecorder.OutputFileName = "out.mp4";
                    // Mark PV and post effects unprepared before the recorder can observe
                    // their readiness, including the no-resize fast path.
                    NoteSkinLibrary.SetChartFolder(maidataPath);
                    bgManager.LoadBGFromPath(maidataPath, data.audioSpeed,
                        data.innerBackgroundCover, data.outerBackgroundCover, data.showSongDetail,
                        data.backgroundFitMode, !HasTimelineVideo(jsonText));
                    ConfigureDisplayTimeline(displayTimeline, screenEffects, mediaTimeline,
                        timeProvider, jsonText, data);
                    // Encoders require even dimensions; zero keeps the current resolution.
                    var resolutionRequested = false;
                    if (data.recordWidth > 0 && data.recordHeight > 0 &&
                        data.recordWidth % 2 == 0 && data.recordHeight % 2 == 0)
                    {
                        screenRecorder.WarnIfHighCaptureLoad(
                            data.recordWidth, data.recordHeight);
                        screenRecorder.RememberResolutionForRestore();
                        Screen.SetResolution(data.recordWidth, data.recordHeight, false);
                        resolutionRequested = true;
                    }
                    // SetResolution takes effect next frame. Starting immediately uses the old window size,
                    // which Edit may have made odd, and incorrectly reports an uneven resolution.
                    if (resolutionRequested)
                        StartCoroutine(StartRecordingAfterResize(
                            screenRecorder, data.recordWidth, data.recordHeight, maidataPath));
                    else
                        screenRecorder.StartRecording(maidataPath);

                    GameObject.Find("CanvasButtons")?.SetActive(false);
                    //GameObject.Find("Notes").GetComponent<NoteManager>().Refresh();
                    }
                    catch
                    {
                        screenRecorder.StopRecording();
                        throw;
                    }
                }
                break;
            case EditorControlMethod.Pause:
                playbackActivationGeneration++;
                playbackStartDeferred = false;
                pausedTimelinePreviewActive = false;
                MajdataPetClient.Trigger("waiting", "Playback paused");
                timeProvider.PausePlayback();
                displayTimeline.PausePlayback();
                mediaTimeline.PausePlayback();
                GameObject.Find("NoteEffects")?.GetComponent<NoteEffectManager>()?.ResetAllEffects();
                bgManager.PauseVideo();
                break;
            case EditorControlMethod.Stop:
                {
                    playbackActivationGeneration++;
                    MajdataPetClient.Trigger("idle", "Ready");
                    Debug.Log("[MajdataView] Stop request: clearing chart and input bindings.");
                    liveChartActive = false;
                    pausedTimelinePreviewActive = false;
                    playbackStartDeferred = false;
                    HideStandbyDisplays(objectCounter);
                    ApplyGeneratedMarkVisibility();
                    // Let an active capture close its pipe before any scene reload.
                    var wasRecording = screenRecorder.IsRecording;
                    screenRecorder.StopRecording();
                    displayTimeline.SetPlaybackActive(false);
                    mediaTimeline.SetPlaybackActive(false);
                    GameObject.Find("NoteEffects")?.GetComponent<NoteEffectManager>()?.ResetAllEffects();
                    if (!wasRecording)
                    {
                        timeProvider.ResetStartTime();
                        IsReloding = true;
                        // Match 4.4.0's non-blocking Stop acknowledgement. A following
                        // Start may queue immediately, but Update keeps it pending until
                        // the replacement scene has completed its initialization.
                        deferredStartCompletion = true;
                        deferResponse = true;
                        var previousSceneHandle = SceneManager.GetActiveScene().handle;
                        CompleteRequest();
                        SceneManager.LoadScene(1);
                        StartCoroutine(CompleteStopAfterSceneReload(previousSceneHandle));
                    }
                }
                break;
            case EditorControlMethod.Continue:
                {
                    var resumedTimelinePreview = pausedTimelinePreviewActive;
                    var startsDeferredChart = playbackStartDeferred;
                    playbackStartDeferred = false;
                    // Resuming a paused timeline preview only needs the preview's
                    // unjudgeable notes swapped for playable ones. Falling back to a
                    // full Start reloaded the skin, background and display timelines
                    // as well, which is the hitch and the cover flash seen when
                    // leaving a paused preview.
                    if (pausedTimelinePreviewActive)
                    {
                        if (string.IsNullOrWhiteSpace(data.jsonPath))
                        {
                            commandError =
                                "Continue from a timeline preview requires jsonPath.";
                            break;
                        }
                        loader.ClearLoadedNotes(true);
                        loader.noteSpeed = (float)(107.25 /
                            (71.4184491 * Mathf.Pow(
                                data.noteSpeed + 0.9975f,
                                -0.985558604f)));
                        loader.touchSpeed = data.touchSpeed;
                        loader.starSpeed = data.starSpeed;
                        loader.smoothSlideAnime = data.smoothSlideAnime;
                        loader.LoadJsonImmediate(
                            File.ReadAllText(data.jsonPath),
                            data.startTime,
                            previewOnly: false,
                            preserveTintCache: true,
                            includeActiveSustains: true);
                        loader.WarmupRenderingMaterials();
                        sensors?.ResetAllSensors();
                        multTouchHandler?.clearSlots();
                        noteEffects?.ResetAllEffects();
                        if (allPerfect != null)
                        {
                            allPerfect.Configure(data.showAllPerfect);
                            allPerfect.enabled = true;
                        }
                        pausedTimelinePreviewActive = false;
                    }

                    // Ordinary pause/resume keeps the live note graph intact. Match
                    // v0.4.2 here: changing input mode or scheduling a second visual
                    // activation makes held notes and already revealed slides cross
                    // their lifecycle boundary again and disappear for a frame.
                    if (!resumedTimelinePreview && !startsDeferredChart)
                    {
                        MajdataPetClient.Trigger(
                            "running", "Continuing chart...");
                        timeProvider.SetStartTime(
                            DateTime.Now.Ticks,
                            data.startTime,
                            data.audioSpeed);
                        bgManager.ContinueVideo(data.audioSpeed);
                        displayTimeline.SetPlaybackActive(true);
                        mediaTimeline.ContinuePlayback();
                        foreach (var wifi in
                                 FindObjectsByType<WifiDrop>(FindObjectsSortMode.None))
                            wifi.RefreshAfterResume();
                        break;
                    }

                    var activationGeneration =
                        ++playbackActivationGeneration;
                    var activationMode =
                        (AutoPlayMode)(int)data.editorPlayMethod;
                    var activationTicks =
                        data.startAt > 0
                            ? data.startAt
                            : DateTime.Now.Ticks;
                    MajdataPetClient.Trigger(
                        "running", "Continuing chart...");
                    timeProvider.SetStartTime(
                        activationTicks,
                        data.startTime,
                        data.audioSpeed,
                        keepVisibleWhileScheduled: true);
                    InputManager.Mode = AutoPlayMode.Disable;
                    StartCoroutine(CompleteContinueAt(
                        activationGeneration,
                        activationTicks,
                        activationMode,
                        bgManager,
                        displayTimeline,
                        mediaTimeline,
                        data.audioSpeed));
                }
                break;
            case EditorControlMethod.TimelinePreview:
                {
                    playbackActivationGeneration++;
                    MajdataPetClient.Trigger("review", "Previewing paused timeline");
                    liveChartActive = true;
                    pausedTimelinePreviewActive = true;
                    ApplyGeneratedMarkVisibility();
                    InputManager.Mode = AutoPlayMode.Disable;
                    timeProvider.SetPausedTimelineTime(data.startTime);
                    var previousPreview = loader.BeginPreviewReplacement();
                    try
                    {
                        customSkin.LoadSkin(
                            data.skin,
                            data.tapSkin,
                            data.holdSkin,
                            data.starSkin,
                            data.pinkStar);
                        loader.noteSpeed = (float)(107.25 /
                            (71.4184491 * Mathf.Pow(
                                data.noteSpeed + 0.9975f,
                                -0.985558604f)));
                        loader.touchSpeed = data.touchSpeed;
                        loader.starSpeed = data.starSpeed;
                        loader.smoothSlideAnime = data.smoothSlideAnime;
                        allPerfect.enabled = false;
                        multTouchHandler.clearSlots();
                        if (!string.IsNullOrWhiteSpace(data.jsonPath))
                        {
                            var jsonText = File.ReadAllText(data.jsonPath);
                            NoteSkinLibrary.SetChartFolder(
                                Path.GetDirectoryName(data.jsonPath));
                            // Keep the old rendered frame until the replacement notes
                            // have completed their first update.
                            loader.LoadJsonImmediate(
                                jsonText, -999f, true, preserveTintCache: true);
                            ConfigureDisplayTimeline(
                                displayTimeline,
                                screenEffects,
                                mediaTimeline,
                                timeProvider,
                                jsonText,
                                data);
                        }
                    }
                    finally
                    {
                        loader.CompletePreviewReplacement(previousPreview);
                    }
                    displayTimeline.SetPausedTimelineTime(data.startTime);
                    mediaTimeline.SetPausedTimelineTime(data.startTime);
                    noteEffects.ResetAllEffects();
                    bgManager.SetPausedTimelineTime(data.startTime);
                    deferredStartCompletion = true;
                    deferResponse = true;
                    StartCoroutine(CompleteTimelinePreviewWhenCommitted(loader));
                }
                break;
            case EditorControlMethod.Seek:
                {
                    if (!pausedTimelinePreviewActive)
                        break;
                    playbackActivationGeneration++;
                    InputManager.Mode = AutoPlayMode.Disable;
                    timeProvider.SetPausedTimelineTime(data.startTime);
                    displayTimeline.SetPausedTimelineTime(data.startTime);
                    mediaTimeline.SetPausedTimelineTime(data.startTime);
                    noteEffects.ResetAllEffects();
                    bgManager.SetPausedTimelineTime(data.startTime);
                }
                break;
            case EditorControlMethod.SetDisplay:
                MajdataPetClient.Trigger("review", "Refreshing display");
                customSkin.LoadSkin(data.skin, data.tapSkin, data.holdSkin, data.starSkin, data.pinkStar);
                if (liveChartActive)
                    objectCounter.SetSideDisplays(data.showJudgeInfo, data.showComboInfo);
                else
                    HideStandbyDisplays(objectCounter);
                objectCounter.SetDisplayFont(data.viewDisplayFontPreset);
                allPerfect.Configure(data.showAllPerfect);
                displayTimeline.SetImmediateDisplay(
                    data.showJudgeLine,
                    data.showJudgeInfo,
                    data.showComboInfo,
                    data.showJudgeText,
                    data.innerBackgroundCover,
                    data.outerBackgroundCover,
                    data.showJudgeArea);
                bgManager.SetBackgroundFitMode(data.backgroundFitMode);
                mediaTimeline.SetBackgroundFitMode(data.backgroundFitMode);
                ApplyGeneratedMarkVisibility();
                break;
            case EditorControlMethod.Preview:
                MajdataPetClient.Trigger("review", "Previewing note");
                // Preview only ever applies to the standby screen. If a real chart
                // is active, drop the request before touching anything — clearing
                // the loaded notes here would poison playback (see liveChartActive).
                if (liveChartActive)
                    break;
                Debug.Log("[MajdataView] Preview request: loading isolated preview notes.");
                customSkin.LoadSkin(data.skin, data.tapSkin, data.holdSkin, data.starSkin, data.pinkStar);
                allPerfect.enabled = false;
                var previewNotesRoot = GameObject.Find("Notes");
                if (previewNotesRoot != null)
                    previewNotesRoot.SetActive(false);
                try
                {
                    loader.ClearLoadedNotes(true);
                    if (!string.IsNullOrWhiteSpace(data.previewJson))
                    {
                        if (!string.IsNullOrWhiteSpace(data.jsonPath))
                            NoteSkinLibrary.SetChartFolder(
                                Path.GetDirectoryName(data.jsonPath));
                        InputManager.Mode = AutoPlayMode.Disable;
                        timeProvider.SetPreviewTime(0f);
                        loader.noteSpeed = (float)(107.25 /
                            (71.4184491 * Mathf.Pow(data.noteSpeed + 0.9975f, -0.985558604f)));
                        loader.touchSpeed = data.touchSpeed;
                        loader.starSpeed = data.starSpeed;
                        loader.smoothSlideAnime = data.smoothSlideAnime;
                        loader.LoadJsonImmediate(
                            data.previewJson, -999f, true, preserveTintCache: true);
                        displayTimeline.SetPlaybackActive(false);
                        mediaTimeline.SetPlaybackActive(false);
                        screenEffects?.Configure(null, timeProvider);
                    }
                    else
                    {
                        timeProvider.ResetStartTime();
                    }
                }
                finally
                {
                    if (previewNotesRoot != null)
                        previewNotesRoot.SetActive(true);
                }
                break;
        }
        }
        catch (Exception exception)
        {
            commandError = exception.Message;
            Debug.LogException(exception);
            playbackActivationGeneration++;
            liveChartActive = false;
            pausedTimelinePreviewActive = false;
            playbackStartDeferred = false;
            InputManager.Mode = AutoPlayMode.Disable;
            timeProvider.ResetStartTime();
            displayTimeline.SetPlaybackActive(false);
            mediaTimeline.SetPlaybackActive(false);
            if (screenRecorder.IsRecording)
                screenRecorder.StopRecording();
            ApplyGeneratedMarkVisibility();
        }
        finally
        {
            // Edit treats the HTTP response as completion of this command. Clearing
            // here also guarantees a malformed chart cannot wedge the HTTP listener.
            if (!deferResponse)
                CompleteRequest(
                    commandError == null,
                    commandError,
                    commandError == null ? 200 : 500);
        }
    }

    private IEnumerator CompleteContinueAt(
        int generation,
        long startAt,
        AutoPlayMode requestedMode,
        BGManager bgManager,
        DisplayTimelineController displayTimeline,
        MediaTimelineController mediaTimeline,
        float audioSpeed)
    {
        while (DateTime.Now.Ticks < startAt)
        {
            if (generation != playbackActivationGeneration ||
                !liveChartActive)
                yield break;
            yield return null;
        }
        if (generation != playbackActivationGeneration ||
            !liveChartActive)
            yield break;

        InputManager.Mode = requestedMode;
        bgManager?.ContinueVideo(audioSpeed);
        displayTimeline?.SetPlaybackActive(true);
        mediaTimeline?.ContinuePlayback();
        foreach (var wifi in
                 FindObjectsByType<WifiDrop>(FindObjectsSortMode.None))
            wifi.RefreshAfterResume();
    }

    private IEnumerator CompleteTimelinePreviewWhenCommitted(JsonDataLoader loader)
    {
        while (loader != null && loader.PreviewReplacementInProgress)
            yield return null;
        deferredStartCompletion = false;
        CompleteRequest();
    }

    private IEnumerator StartRecordingAfterResize(
        ScreenRecorder screenRecorder, int width, int height, string maidataPath)
    {
        // SetResolution takes effect next frame and the window manager may adjust it again.
        // Require the target size for two frames, or continue after 60 frames and let validation report errors.
        var stableFrames = 0;
        for (var i = 0; i < 60 && stableFrames < 2; i++)
        {
            yield return null;
            if (Screen.width == width && Screen.height == height)
                stableFrames++;
            else
                stableFrames = 0;
        }

        screenRecorder.StartRecording(maidataPath);
    }

    private IEnumerator CompleteStopAfterSceneReload(int previousSceneHandle)
    {
        while (!SceneManager.GetActiveScene().isLoaded ||
               SceneManager.GetActiveScene().handle == previousSceneHandle)
        {
            yield return null;
        }

        // Hide the new scene's default HUD before its first rendered frame. Waiting
        // for every Start method here caused a one-frame time/combo watermark flash.
        generatedMark = null;
        currentTimeText = null;
        HideStandbyDisplays(FindSceneComponent<ObjectCounter>("ObjectCounter"));
        ApplyGeneratedMarkVisibility();

        while (FindSceneComponent<JsonDataLoader>("DataLoader") == null ||
               FindSceneComponent<InputManager>("Input") == null ||
               FindSceneComponent<SensorManager>("Sensors") == null ||
               FindSceneComponent<MultTouchHandler>("MultTouchHandler") == null ||
               FindSceneComponent<NoteManager>("Notes") == null)
        {
            yield return null;
        }

        // GameObject.Find can see objects before their Start methods have populated
        // runtime collections. One frame turns scene presence into runtime readiness.
        yield return null;
        IsReloding = false;
        deferredStartCompletion = false;
    }

    private IEnumerator CompleteAsyncStartWhenPlayable(
        JsonDataLoader loader,
        AudioTimeProvider timeProvider,
        SensorManager sensorManager,
        MultTouchHandler multTouchHandler,
        AutoPlayMode requestedMode,
        BGManager bgManager,
        MediaTimelineController mediaTimeline,
        float startTime,
        float audioSpeed,
        bool deferPlaybackStart,
        bool replacePausedPreview)
    {
        while (loader != null && !loader.RuntimeBindingsReady)
            yield return null;

        loader?.WarmupRenderingMaterials();
        if (replacePausedPreview)
            loader?.ClearPreviewNotes();
        var videoWarmupDeadline = Time.realtimeSinceStartup + 15f;
        while (bgManager != null && !bgManager.IsPreparedForRecording &&
               Time.realtimeSinceStartup < videoWarmupDeadline)
            yield return null;
        var mediaWarmupDeadline = Time.realtimeSinceStartup + 15f;
        while (mediaTimeline != null && !mediaTimeline.IsPrepared &&
               Time.realtimeSinceStartup < mediaWarmupDeadline)
            yield return null;

        sensorManager?.ResetAllSensors();
        multTouchHandler?.clearSlots();
        if (deferPlaybackStart)
        {
            InputManager.Mode = AutoPlayMode.Disable;
            bgManager?.PauseVideo();
            mediaTimeline?.SetPlaybackActive(false);
        }
        else
        {
            InputManager.Mode = requestedMode;
            timeProvider.SetStartTime(DateTime.Now.Ticks, startTime, audioSpeed);
            mediaTimeline?.SetPlaybackActive(true);
        }
        deferredStartCompletion = false;
        CompleteRequest();
    }

    private IEnumerator CompleteAsyncIntroWhenPlayable(
        JsonDataLoader loader,
        AudioTimeProvider timeProvider,
        SensorManager sensorManager,
        MultTouchHandler multTouchHandler,
        AutoPlayMode requestedMode,
        BGManager bgManager,
        MediaTimelineController mediaTimeline,
        float timelineTime,
        float audioSpeed,
        bool showSongDetail)
    {
        while (loader != null && !loader.RuntimeBindingsReady)
            yield return null;

        loader?.WarmupRenderingMaterials();
        var videoWarmupDeadline = Time.realtimeSinceStartup + 15f;
        while (bgManager != null && !bgManager.IsPreparedForRecording &&
               Time.realtimeSinceStartup < videoWarmupDeadline)
            yield return null;
        var mediaWarmupDeadline = Time.realtimeSinceStartup + 15f;
        while (mediaTimeline != null && !mediaTimeline.IsPrepared &&
               Time.realtimeSinceStartup < mediaWarmupDeadline)
            yield return null;

        sensorManager?.ResetAllSensors();
        multTouchHandler?.clearSlots();
        InputManager.Mode = requestedMode;
        timeProvider.SetStartTime(DateTime.Now.Ticks, timelineTime, audioSpeed);
        mediaTimeline?.SetPlaybackActive(true);
        if (showSongDetail)
            bgManager?.PlaySongDetail(timelineTime, audioSpeed);
        else
            bgManager?.HideSongDetail();
        deferredStartCompletion = false;
        CompleteRequest();
    }

    private void ApplyGeneratedMarkVisibility()
    {
        if (generatedMark == null)
            generatedMark = GameObject.Find("TimeText (1)");
        if (currentTimeText == null)
            currentTimeText = GameObject.Find("TimeText");
        var visible = showGeneratedMark && liveChartActive;
        if (generatedMark != null)
            generatedMark.SetActive(visible);
        if (currentTimeText != null)
            currentTimeText.SetActive(visible);
        if (visible)
            FindSceneComponent<ObjectCounter>("ObjectCounter")?.SetDisplayFont(viewDisplayFontPreset);
    }

    private static void HideStandbyDisplays(ObjectCounter counter)
    {
        if (counter == null)
            return;
        counter.SetSideDisplays(false, false);
        counter.ComboSetActive((EditorComboIndicator)0);
    }

    private static void ConfigureDisplayTimeline(
        DisplayTimelineController controller,
        ScreenEffectController screenEffects,
        MediaTimelineController mediaTimeline,
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
            request.outerBackgroundCover,
            (int)request.comboStatusType,
            chart?.colorTable,
            request.showJudgeArea,
            request.showSongDetail);
        screenEffects?.Configure(chart?.effectTable, timeProvider, mediaTimeline);
        var liveNoteVisuals = controller.GetComponent<LiveNoteVisualController>() ??
                              controller.gameObject.AddComponent<LiveNoteVisualController>();
        liveNoteVisuals.Configure(
            chart?.colorTable,
            chart?.sizeTable,
            chart?.alphaTable,
            controller.GetComponent<JsonDataLoader>(),
            timeProvider);
        mediaTimeline?.Configure(
            chart?.mediaTable,
            Path.GetDirectoryName(request.jsonPath),
            timeProvider,
            request.control == EditorControlMethod.Record);
        mediaTimeline?.SetAudioVolume(request.mediaAudioVolume);
        mediaTimeline?.SetBackgroundFitMode(request.backgroundFitMode);
    }

    private static bool HasTimelineVideo(string jsonText)
    {
        var chart = JsonConvert.DeserializeObject<Majson>(jsonText);
        return chart?.mediaTable?.Exists(item =>
            item != null && item.timelineClip && item.kind == "pvOverlay") == true;
    }

    private void OnDestroy()
    {
        requestCompleted.Set();
        http.Stop();
        print("server stoped");
    }

    private void httpListen()
    {
        while (http.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = http.GetContext();
            }
            catch (HttpListenerException) when (!http.IsListening)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            try
            {
                using var reader =
                    new StreamReader(context.Request.InputStream);
                var data = reader.ReadToEnd();
                if (string.IsNullOrWhiteSpace(data))
                {
                    responseStatusCode = 400;
                    responseBody = JsonConvert.SerializeObject(new
                    {
                        ok = false,
                        protocolVersion = ProtocolVersion,
                        error = "Request body is empty."
                    });
                }
                else
                {
                    requestCompleted.Reset();
                    responseStatusCode = 500;
                    responseBody = JsonConvert.SerializeObject(new
                    {
                        ok = false,
                        protocolVersion = ProtocolVersion,
                        error = "Request was not processed."
                    });
                    request = data;
                    requestCompleted.Wait();
                }
                context.Response.StatusCode = responseStatusCode;
                context.Response.ContentType =
                    "application/json; charset=utf-8";
                using var stream =
                    new StreamWriter(context.Response.OutputStream);
                stream.Write(responseBody);
            }
            catch (Exception exception) when (
                exception is IOException or HttpListenerException or
                ObjectDisposedException)
            {
                Debug.LogWarning(
                    "[MajdataView] HTTP client disconnected: " +
                    exception.Message);
            }
            finally
            {
                try
                {
                    context.Response.Close();
                }
                catch
                {
                    // A disconnected client must not terminate the listener.
                }
            }
        }

        print("exit listen");
    }

    private void CompleteRequest(
        bool success = true,
        string error = null,
        int statusCode = 200)
    {
        responseStatusCode = statusCode;
        // Beats the loader could not build travel back on every response. The
        // chart text was legal or it would have been stopped in the editor, so
        // without this the note is just absent and there is nothing to look for.
        var loader = FindSceneComponent<JsonDataLoader>("DataLoader");
        var drops = loader == null
            ? Array.Empty<object>()
            : loader.DroppedBeats.Select(drop => (object)new
            {
                line = drop.Line,
                column = drop.Column,
                time = drop.Time,
                content = drop.Content,
                reason = drop.Reason
            }).ToArray();
        responseBody = JsonConvert.SerializeObject(new
        {
            ok = success,
            protocolVersion = ProtocolVersion,
            error,
            droppedBeats = drops
        });
        request = string.Empty;
        requestCompleted.Set();
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
