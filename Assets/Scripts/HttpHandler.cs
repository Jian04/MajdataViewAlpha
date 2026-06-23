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
                    timeProvider.SetStartTime(data.startAt, data.startTime, data.audioSpeed);
                    loader.noteSpeed = (float)(107.25 / (71.4184491 * Mathf.Pow(data.noteSpeed + 0.9975f, -0.985558604f)));
                    loader.touchSpeed = data.touchSpeed;
                    loader.smoothSlideAnime = data.smoothSlideAnime;
                    objectCounter.ComboSetActive(data.comboStatusType);
                    objectCounter.SetSideDisplays(data.showJudgeInfo, data.showComboInfo);
                    var jsonText = File.ReadAllText(data.jsonPath);
                    loader.LoadJson(jsonText, data.startTime);
                    GameObject.Find("Notes").GetComponent<PlayAllPerfect>().enabled = false;
                    GameObject.Find("MultTouchHandler").GetComponent<MultTouchHandler>().clearSlots();

                    bgManager.LoadBGFromPath(new FileInfo(data.jsonPath).DirectoryName, data.audioSpeed,
                        data.innerBackgroundCover, data.outerBackgroundCover);
                    ConfigureDisplayTimeline(displayTimeline, screenEffects, timeProvider, jsonText, data);
                    //GameObject.Find("Notes").GetComponent<NoteManager>().Refresh();
                }
                break;
            case EditorControlMethod.OpStart:
                {
                    timeProvider.SetStartTime(data.startAt, data.startTime, data.audioSpeed);
                    loader.noteSpeed = (float)(107.25 / (71.4184491 * Mathf.Pow(data.noteSpeed + 0.9975f, -0.985558604f)));
                    loader.touchSpeed = data.touchSpeed;
                    loader.smoothSlideAnime = data.smoothSlideAnime;
                    objectCounter.ComboSetActive(data.comboStatusType);
                    objectCounter.SetSideDisplays(data.showJudgeInfo, data.showComboInfo);
                    var jsonText = File.ReadAllText(data.jsonPath);
                    loader.LoadJson(jsonText, data.startTime);
                    GameObject.Find("MultTouchHandler").GetComponent<MultTouchHandler>().clearSlots();

                    bgManager.LoadBGFromPath(new FileInfo(data.jsonPath).DirectoryName, data.audioSpeed,
                        data.innerBackgroundCover, data.outerBackgroundCover);
                    ConfigureDisplayTimeline(displayTimeline, screenEffects, timeProvider, jsonText, data);
                    bgManager.PlaySongDetail();
                    //GameObject.Find("Notes").GetComponent<NoteManager>().Refresh();
                }
                break;
            case EditorControlMethod.Record:
                {
                    var maidataPath = new FileInfo(data.jsonPath).DirectoryName;
                    var recordFrameRate = data.recordFrameRate == 60 ? 60 : 30;
                    timeProvider.SetStartTime(data.startAt, data.startTime, data.audioSpeed, true, recordFrameRate);
                    loader.noteSpeed = (float)(107.25 / (71.4184491 * Mathf.Pow(data.noteSpeed + 0.9975f, -0.985558604f)));
                    loader.touchSpeed = data.touchSpeed;
                    loader.smoothSlideAnime = data.smoothSlideAnime;
                    objectCounter.ComboSetActive(data.comboStatusType);
                    objectCounter.SetSideDisplays(data.showJudgeInfo, data.showComboInfo);
                    var jsonText = File.ReadAllText(data.jsonPath);
                    loader.LoadJson(jsonText, data.startTime);
                    multTouchHandler.clearSlots();

                    var cl = Math.Max(data.chartLength, getChartLength(jsonText));
                    // All Perfect performs the normal stop. This fallback only
                    // covers a failed animation sequence, not an extra video tail.
                    screenRecorder.CutoffTime = cl + 4f;
                    screenRecorder.FrameRate = recordFrameRate;
                    screenRecorder.StartRecording(maidataPath);

                    bgManager.LoadBGFromPath(maidataPath, data.audioSpeed,
                        data.innerBackgroundCover, data.outerBackgroundCover);
                    ConfigureDisplayTimeline(displayTimeline, screenEffects, timeProvider, jsonText, data);
                    bgManager.PlaySongDetail();
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
                screenRecorder.StopRecording();
                displayTimeline.SetPlaybackActive(false);
                GameObject.Find("NoteEffects")?.GetComponent<NoteEffectManager>()?.ResetAllEffects();
                timeProvider.ResetStartTime();
                IsReloding = true;
                SceneManager.LoadScene(1);
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
                displayTimeline.SetImmediateDisplay(
                    data.showJudgeLine,
                    data.showJudgeInfo,
                    data.showComboInfo,
                    data.showJudgeText,
                    data.innerBackgroundCover,
                    data.outerBackgroundCover);
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
