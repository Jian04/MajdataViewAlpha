using Assets.Scripts.Types;
using Assets.Scripts.Notes;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using System.Threading.Tasks;
using System.Collections;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Assets.Scripts;
using MajdataCore;

public class JsonDataLoader : MonoBehaviour
{
    public float noteSpeed = 7f;
    public float starSpeed;
    public float touchSpeed = 7.5f;
    public bool smoothSlideAnime = false;
    public Sprite starEach;
    public GameObject tapPrefab;
    public GameObject holdPrefab;
    public GameObject starPrefab;
    public GameObject touchHoldPrefab;
    public GameObject touchPrefab;
    public GameObject eachLine;
    public GameObject starLine;
    public GameObject notes;
    public GameObject star_slidePrefab;
    public GameObject[] slidePrefab;
    public Material breakMaterial;
    public RuntimeAnimatorController BreakShine;
    public RuntimeAnimatorController JudgeBreakShine;
    public RuntimeAnimatorController HoldShine;

    public NoteLoaderStatus State { get; private set; } = NoteLoaderStatus.Idle;
    NoteManager noteManager;
    AudioTimeProvider timeProvider;
    Task<Majson> jsonLoaderTask = null;
    Majson loadedData = null;
    float ignoreOffset = 0;
    bool previewOnly = false;
    bool includeActiveSustainsAtOffset = false;
    Coroutine noteParserTask = null;
    private int runtimeBindingReadyFrame = -1;
    private int reloadGeneration;
    public bool RuntimeBindingsReady => runtimeBindingReadyFrame < 0;

    /// <summary>
    /// A beat the view could not build. Validation runs on the chart text, but the
    /// note itself is assembled here, so a beat can be legal and still fail to
    /// appear. Recording where it happened is what lets the editor mark it instead
    /// of leaving the note silently missing.
    /// </summary>
    public readonly struct DroppedBeat
    {
        public DroppedBeat(int line, int column, double time, string content, string reason)
        {
            Line = line;
            Column = column;
            Time = time;
            Content = content;
            Reason = reason;
        }

        public int Line { get; }
        public int Column { get; }
        public double Time { get; }
        public string Content { get; }
        public string Reason { get; }
    }

    private readonly List<DroppedBeat> droppedBeats = new();
    private readonly List<(GameObject Object, int NoteKey, SensorType Sensor, bool IsTouch)>
        beatJudgeRegistrations = new();
    public IReadOnlyList<DroppedBeat> DroppedBeats => droppedBeats;

    private int currentBeatLine;
    private int currentBeatColumn;
    private double currentBeatTime;
    private string currentBeatContent = string.Empty;

    /// <summary>
    /// Reported by a note that was built but cannot be drawn - degenerate
    /// geometry, no bars, a route that came back as NaN. Text validation cannot
    /// see any of this, so without a report the note is simply invisible and the
    /// chart looks correct. Deduplicated: this can be reached from Update.
    /// </summary>
    public void ReportUnrenderable(
        int line, int column, double time, string content, string reason)
    {
        foreach (var existing in droppedBeats)
            if (existing.Line == line &&
                existing.Column == column &&
                existing.Reason == reason)
                return;
        droppedBeats.Add(new DroppedBeat(line, column, time, content, reason));
        UnityEngine.Debug.LogWarning(
            $"[ChartLoader] Unrenderable note at t={time:F3} '{content}': {reason}");
    }
    Dictionary<int, int> noteIndex = new();
        Dictionary<SensorType, int> touchIndex = new();

    public Text diffText;
    public Text levelText;
    public Text titleText;
    public Text artistText;
    public Text designText;
    public RawImage cardImage;
    public Color[] diffColors = new Color[7];
    private CustomSkin customSkin;
    private SongDetailTemplateView songDetailTemplate;
    private RawImage songDetailJacket;

    private ObjectCounter ObjectCounter;

    private int slideLayer = -1;
    private int noteSortOrder = 0;

    private static readonly Dictionary<SimaiNoteType, int> NOTE_LAYER_COUNT = new Dictionary<SimaiNoteType, int>()
    {
        {SimaiNoteType.Tap, 2 },
        {SimaiNoteType.Hold, 3 },
        {SimaiNoteType.Slide, 2 },
        {SimaiNoteType.Touch, 7 },
        {SimaiNoteType.TouchHold, 6 },
    };
    private static readonly Dictionary<string, int> SLIDE_PREFAB_MAP = new Dictionary<string, int>()
    {
        {"line3", 0 },
        {"line4", 1 },
        {"line5", 2 },
        {"line6", 3 },
        {"line7", 4 },
        {"circle1", 5 },
        {"circle2", 6 },
        {"circle3", 7 },
        {"circle4", 8 },
        {"circle5", 9 },
        {"circle6", 10 },
        {"circle7", 11 },
        {"circle8", 12 },
        {"v1", 41 },
        {"v2", 13 },
        {"v3", 14 },
        {"v4", 15 },
        {"v6", 16 },
        {"v7", 17 },
        {"v8", 18 },
        {"ppqq1", 19 },
        {"ppqq2", 20 },
        {"ppqq3", 21 },
        {"ppqq4", 22 },
        {"ppqq5", 23 },
        {"ppqq6", 24 },
        {"ppqq7", 25 },
        {"ppqq8", 26 },
        {"pq1", 27 },
        {"pq2", 28 },
        {"pq3", 29 },
        {"pq4", 30 },
        {"pq5", 31 },
        {"pq6", 32 },
        {"pq7", 33 },
        {"pq8", 34 },
        {"s", 35 },
        {"wifi", 36 },
        {"L2", 37 },
        {"L3", 38 },
        {"L4", 39 },
        {"L5", 40 },
    };

    static readonly Dictionary<SensorType, SensorType[]> TOUCH_GROUPS = new()
    {
        { SensorType.A1, new SensorType[]{ SensorType.D1, SensorType.D2, SensorType.E1, SensorType.E2 } },
        { SensorType.A2, new SensorType[]{ SensorType.D2, SensorType.D3, SensorType.E2, SensorType.E3 } },
        { SensorType.A3, new SensorType[]{ SensorType.D3, SensorType.D4, SensorType.E3, SensorType.E4 } },
        { SensorType.A4, new SensorType[]{ SensorType.D4, SensorType.D5, SensorType.E4, SensorType.E5 } },
        { SensorType.A5, new SensorType[]{ SensorType.D5, SensorType.D6, SensorType.E5, SensorType.E6 } },
        { SensorType.A6, new SensorType[]{ SensorType.D6, SensorType.D7, SensorType.E6, SensorType.E7 } },
        { SensorType.A7, new SensorType[]{ SensorType.D7, SensorType.D8, SensorType.E7, SensorType.E8 } },
        { SensorType.A8, new SensorType[]{ SensorType.D8, SensorType.D1, SensorType.E8, SensorType.E1 } },

        { SensorType.D1, new SensorType[]{ SensorType.A1, SensorType.A8, SensorType.E1 } },
        { SensorType.D2, new SensorType[]{ SensorType.A2, SensorType.A1, SensorType.E2 } },
        { SensorType.D3, new SensorType[]{ SensorType.A3, SensorType.A2, SensorType.E3 } },
        { SensorType.D4, new SensorType[]{ SensorType.A4, SensorType.A3, SensorType.E4 } },
        { SensorType.D5, new SensorType[]{ SensorType.A5, SensorType.A4, SensorType.E5 } },
        { SensorType.D6, new SensorType[]{ SensorType.A6, SensorType.A5, SensorType.E6 } },
        { SensorType.D7, new SensorType[]{ SensorType.A7, SensorType.A6, SensorType.E7 } },
        { SensorType.D8, new SensorType[]{ SensorType.A8, SensorType.A7, SensorType.E8 } },

        { SensorType.E1, new SensorType[]{ SensorType.D1, SensorType.A1, SensorType.A8, SensorType.B1, SensorType.B8 } },
        { SensorType.E2, new SensorType[]{ SensorType.D2, SensorType.A2, SensorType.A1, SensorType.B2, SensorType.B1 } },
        { SensorType.E3, new SensorType[]{ SensorType.D3, SensorType.A3, SensorType.A2, SensorType.B3, SensorType.B2 } },
        { SensorType.E4, new SensorType[]{ SensorType.D4, SensorType.A4, SensorType.A3, SensorType.B4, SensorType.B3 } },
        { SensorType.E5, new SensorType[]{ SensorType.D5, SensorType.A5, SensorType.A4, SensorType.B5, SensorType.B4 } },
        { SensorType.E6, new SensorType[]{ SensorType.D6, SensorType.A6, SensorType.A5, SensorType.B6, SensorType.B5 } },
        { SensorType.E7, new SensorType[]{ SensorType.D7, SensorType.A7, SensorType.A6, SensorType.B7, SensorType.B6 } },
        { SensorType.E8, new SensorType[]{ SensorType.D8, SensorType.A8, SensorType.A7, SensorType.B8, SensorType.B7 } },

        { SensorType.B1, new SensorType[]{ SensorType.E1, SensorType.E2, SensorType.B8, SensorType.B2, SensorType.A1, SensorType.C } },
        { SensorType.B2, new SensorType[]{ SensorType.E2, SensorType.E3, SensorType.B1, SensorType.B3, SensorType.A2, SensorType.C } },
        { SensorType.B3, new SensorType[]{ SensorType.E3, SensorType.E4, SensorType.B2, SensorType.B4, SensorType.A3, SensorType.C } },
        { SensorType.B4, new SensorType[]{ SensorType.E4, SensorType.E5, SensorType.B3, SensorType.B5, SensorType.A4, SensorType.C } },
        { SensorType.B5, new SensorType[]{ SensorType.E5, SensorType.E6, SensorType.B4, SensorType.B6, SensorType.A5, SensorType.C } },
        { SensorType.B6, new SensorType[]{ SensorType.E6, SensorType.E7, SensorType.B5, SensorType.B7, SensorType.A6, SensorType.C } },
        { SensorType.B7, new SensorType[]{ SensorType.E7, SensorType.E8, SensorType.B6, SensorType.B8, SensorType.A7, SensorType.C } },
        { SensorType.B8, new SensorType[]{ SensorType.E8, SensorType.E1, SensorType.B7, SensorType.B1, SensorType.A8, SensorType.C } },

        { SensorType.C, new SensorType[]{ SensorType.B1, SensorType.B2, SensorType.B3, SensorType.B4, SensorType.B5, SensorType.B6, SensorType.B7, SensorType.B8} },
    };

    static Dictionary<string, float> SLIDE_AREA_CONST = new()
    {
        { "line3", 0.1919f},
        { "line4", 0.1793f},
        { "line5", 0.1629f},
        { "line6", 0.1793f},
        { "line7", 0.1919f},
        { "circle1", 0.7892f},
        { "circle2", 0.2326f},
        { "circle3", 0.1550f},
        { "circle4", 0.1163f},
        { "circle5", 0.0930f},
        { "circle6", 0.0775f},
        { "circle7", 0.0664f},
        { "circle8", 0.0490f},
        { "v1", 0.1629f},
        { "v2", 0.1629f},
        { "v3", 0.1629f},
        { "v4", 0.1629f},
        { "v5", 0.1629f},
        { "v6", 0.1629f},
        { "v7", 0.1629f},
        { "v8", 0.1629f},
        { "ppqq1", 0.1014f},
        { "ppqq2", 0.1204f},
        { "ppqq3", 0.1434f},
        { "ppqq4", 0.0697f},
        { "ppqq5", 0.0867f},
        { "ppqq6", 0.1026f},
        { "ppqq7", 0.1266f},
        { "ppqq8", 0.1413f},
        { "pq1", 0.1021f},
        { "pq2", 0.1144f},
        { "pq3", 0.1247f},
        { "pq4", 0.1436f},
        { "pq5", 0.1627f},
        { "pq6", 0.0752f},
        { "pq7", 0.0984f},
        { "pq8", 0.1126f},
        { "s", 0.1054f},
        { "wifi", 0.1829f},
        { "L2", 0.0948f},
        { "L3", 0.0711f},
        { "L4", 0.0948f},
        { "L5", 0.1186f},
    };

    private static readonly Dictionary<string, List<int>> SLIDE_AREA_STEP_MAP = new Dictionary<string, List<int>>()
    {
        {"line3", new List<int>(){ 0, 2, 8, 13 } },
        {"line4", new List<int>(){ 0, 3, 8, 12, 18 } },
        {"line5", new List<int>(){ 0, 3, 6, 11, 15, 19 } },
        {"line6", new List<int>(){ 0, 3, 8, 12, 18 } },
        {"line7", new List<int>(){ 0, 2, 8, 13 } },
        {"circle1", new List<int>(){ 0, 3, 11, 19, 27, 35, 43, 50, 58, 63 } },
        {"circle2", new List<int>(){ 0, 3, 7 } },
        {"circle3", new List<int>(){ 0, 3, 11, 15 } },
        {"circle4", new List<int>(){ 0, 3, 11, 19, 23 } },
        {"circle5", new List<int>(){ 0, 3, 11, 19, 27, 31 } },
        {"circle6", new List<int>(){ 0, 3, 11, 19, 27, 35, 39 } },
        {"circle7", new List<int>(){ 0, 3, 11, 19, 27, 35, 43, 47 } },
        {"circle8", new List<int>(){ 0, 3, 11, 19, 27, 35, 43, 50, 55 } },
        {"v1", new List<int>(){ 0, 3, 6, 11, 15, 19 } },
        {"v2", new List<int>(){ 0, 3, 6, 11, 15, 19 } },
        {"v3", new List<int>(){ 0, 3, 6, 11, 15, 19 } },
        {"v4", new List<int>(){ 0, 3, 6, 11, 15, 19 } },
        {"v6", new List<int>(){ 0, 3, 6, 11, 15, 19 } },
        {"v7", new List<int>(){ 0, 3, 6, 11, 15, 19 } },
        {"v8", new List<int>(){ 0, 3, 6, 11, 15, 19 } },
        {"ppqq1", new List<int>(){ 0, 3, 7, 13, 17, 26, 32, 35 } },
        {"ppqq2", new List<int>(){ 0, 3, 7, 12, 16, 25, 28 } },
        {"ppqq3", new List<int>(){ 0, 3, 6, 12, 15, 22 } },
        {"ppqq4", new List<int>(){ 0, 3, 7, 12, 16, 25, 29, 35, 40, 44, 49 } },
        {"ppqq5", new List<int>(){ 0, 3, 7, 12, 16, 25, 29, 35, 40, 44, 49 } },
        {"ppqq6", new List<int>(){ 0, 3, 7, 12, 16, 25, 28, 34, 38, 41, 48 } },
        {"ppqq7", new List<int>(){ 0, 3, 7, 13, 17, 27, 31, 37, 41, 46 } },
        {"ppqq8", new List<int>(){ 0, 3, 7, 12, 16, 25, 29, 35, 41 } },
        {"pq1", new List<int>(){ 0, 3, 8, 11, 14, 17, 21, 24, 27, 33 } },
        {"pq2", new List<int>(){ 0, 3, 8, 11, 14, 18, 21, 24, 30 } },
        {"pq3", new List<int>(){ 0, 3, 9, 12, 16, 19, 23, 27 } },
        {"pq4", new List<int>(){ 0, 3, 9, 13, 16, 20, 24 } },
        {"pq5", new List<int>(){ 0, 3, 9, 13, 17, 21 } },
        {"pq6", new List<int>(){ 0, 3, 8, 11, 15, 18, 21, 25, 28, 31, 35, 38, 42 } },
        {"pq7", new List<int>(){ 0, 3, 8, 12, 15, 18, 22, 25, 28, 32, 35, 39 } },
        {"pq8", new List<int>(){ 0, 3, 8, 11, 14, 17, 21, 24, 27, 30, 36 } },
        {"s", new List<int>(){ 0, 3, 8, 11, 17, 21, 24, 30 } },
        {"wifi", new List<int>(){ 0, 1, 4, 6, 11 } },
        {"L2", new List<int>(){ 0, 2, 7, 15, 21, 26, 32 } },
        {"L3", new List<int>(){ 0, 2, 8, 17, 20, 26, 29, 34 } },
        {"L4", new List<int>(){ 0, 2, 8, 17, 22, 26, 32 } },
        {"L5", new List<int>(){ 0, 2, 8, 16, 22, 28 } },
    };
    private static readonly Dictionary<int, List<List<JudgeArea>>> WIFISLIDE_JUDGE_QUEUE = new Dictionary<int, List<List<JudgeArea>>>()
    {
        { 1,
            new List<List<JudgeArea>>()
            {
                new List<JudgeArea>() // L
                {
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A1, false } },SLIDE_AREA_STEP_MAP["wifi"][0]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.B8, false } },SLIDE_AREA_STEP_MAP["wifi"][1]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.B7, false } },SLIDE_AREA_STEP_MAP["wifi"][2]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A6, true },{SensorType.D6, true }  },SLIDE_AREA_STEP_MAP["wifi"][3] ),
                },
                new List<JudgeArea>() // Center
                {
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A1, false } },SLIDE_AREA_STEP_MAP["wifi"][0]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.B1, false } },SLIDE_AREA_STEP_MAP["wifi"][1]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.C, false } },SLIDE_AREA_STEP_MAP["wifi"][2]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A5, true },{SensorType.B5, true }  },SLIDE_AREA_STEP_MAP["wifi"][3] ),
                },
                new List<JudgeArea>() // R
                {
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A1, false } },SLIDE_AREA_STEP_MAP["wifi"][0]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.B2, false } },SLIDE_AREA_STEP_MAP["wifi"][1]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.B3, false } },SLIDE_AREA_STEP_MAP["wifi"][2]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A4, true },{SensorType.D5, true }  },SLIDE_AREA_STEP_MAP["wifi"][3] ),
                }
            }
        },
        { 2,
            new List<List<JudgeArea>>()
            {
                new List<JudgeArea>() // L
                {
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A2, false } },SLIDE_AREA_STEP_MAP["wifi"][0]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.B1, false } },SLIDE_AREA_STEP_MAP["wifi"][1]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.B8, false } },SLIDE_AREA_STEP_MAP["wifi"][2]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A7, true },{SensorType.D7, true }  },SLIDE_AREA_STEP_MAP["wifi"][3] ),
                },
                new List<JudgeArea>() // Center
                {
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A2, false } },SLIDE_AREA_STEP_MAP["wifi"][0]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.B2, false } },SLIDE_AREA_STEP_MAP["wifi"][1]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.C, false } },SLIDE_AREA_STEP_MAP["wifi"][2]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A6, true },{SensorType.B6, true }  },SLIDE_AREA_STEP_MAP["wifi"][3] ),
                },
                new List<JudgeArea>() // R
                {
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A2, false } },SLIDE_AREA_STEP_MAP["wifi"][0]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.B3, false } },SLIDE_AREA_STEP_MAP["wifi"][1]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.B4, false } },SLIDE_AREA_STEP_MAP["wifi"][2]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A5, true },{SensorType.D6, true }  },SLIDE_AREA_STEP_MAP["wifi"][3] ),
                }
            }
        },
        { 3,
            new List<List<JudgeArea>>()
            {
                new List<JudgeArea>() // L
                {
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A3, false } },SLIDE_AREA_STEP_MAP["wifi"][0]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.B2, false } },SLIDE_AREA_STEP_MAP["wifi"][1]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.B1, false } },SLIDE_AREA_STEP_MAP["wifi"][2]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A8, true },{SensorType.D8, true }  },SLIDE_AREA_STEP_MAP["wifi"][3] ),
                },
                new List<JudgeArea>() // Center
                {
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A3, false } },SLIDE_AREA_STEP_MAP["wifi"][0]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.B3, false } },SLIDE_AREA_STEP_MAP["wifi"][1]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.C, false } },SLIDE_AREA_STEP_MAP["wifi"][2]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A7, true },{SensorType.B7, true }  },SLIDE_AREA_STEP_MAP["wifi"][3] ),
                },
                new List<JudgeArea>() // R
                {
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A3, false } },SLIDE_AREA_STEP_MAP["wifi"][0]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.B4, false } },SLIDE_AREA_STEP_MAP["wifi"][1]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.B5, false } },SLIDE_AREA_STEP_MAP["wifi"][2]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A6, true },{SensorType.D7, true }  },SLIDE_AREA_STEP_MAP["wifi"][3] ),
                }
            }
        },
        { 4,
            new List<List<JudgeArea>>()
            {
                new List<JudgeArea>() // L
                {
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A4, false } },SLIDE_AREA_STEP_MAP["wifi"][0]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.B3, false } },SLIDE_AREA_STEP_MAP["wifi"][1]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.B2, false } },SLIDE_AREA_STEP_MAP["wifi"][2]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A1, true },{SensorType.D1, true }  },SLIDE_AREA_STEP_MAP["wifi"][3] ),
                },
                new List<JudgeArea>() // Center
                {
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A4, false } },SLIDE_AREA_STEP_MAP["wifi"][0]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.B4, false } },SLIDE_AREA_STEP_MAP["wifi"][1]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.C, false } },SLIDE_AREA_STEP_MAP["wifi"][2]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A8, true },{SensorType.B8, true }  },SLIDE_AREA_STEP_MAP["wifi"][3] ),
                },
                new List<JudgeArea>() // R
                {
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A4, false } },SLIDE_AREA_STEP_MAP["wifi"][0]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.B5, false } },SLIDE_AREA_STEP_MAP["wifi"][1]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.B6, false } },SLIDE_AREA_STEP_MAP["wifi"][2]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A7, true },{SensorType.D8, true }  },SLIDE_AREA_STEP_MAP["wifi"][3] ),
                }
            }
        },
        { 5,
            new List<List<JudgeArea>>()
            {
                new List<JudgeArea>() // L
                {
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A5, false } },SLIDE_AREA_STEP_MAP["wifi"][0]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.B4, false } },SLIDE_AREA_STEP_MAP["wifi"][1]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.B3, false } },SLIDE_AREA_STEP_MAP["wifi"][2]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A2, true },{SensorType.D2, true }  },SLIDE_AREA_STEP_MAP["wifi"][3] ),
                },
                new List<JudgeArea>() // Center
                {
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A5, false } },SLIDE_AREA_STEP_MAP["wifi"][0]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.B5, false } },SLIDE_AREA_STEP_MAP["wifi"][1]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.C, false } },SLIDE_AREA_STEP_MAP["wifi"][2]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A1, true },{SensorType.B1, true }  },SLIDE_AREA_STEP_MAP["wifi"][3] ),
                },
                new List<JudgeArea>() // R
                {
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A5, false } },SLIDE_AREA_STEP_MAP["wifi"][0]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.B6, false } },SLIDE_AREA_STEP_MAP["wifi"][1]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.B7, false } },SLIDE_AREA_STEP_MAP["wifi"][2]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A8, true },{SensorType.D1, true }  },SLIDE_AREA_STEP_MAP["wifi"][3] ),
                }
            }
        },
        { 6,
            new List<List<JudgeArea>>()
            {
                new List<JudgeArea>() // L
                {
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A6, false } },SLIDE_AREA_STEP_MAP["wifi"][0]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.B5, false } },SLIDE_AREA_STEP_MAP["wifi"][1]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.B4, false } },SLIDE_AREA_STEP_MAP["wifi"][2]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A3, true },{SensorType.D3, true }  },SLIDE_AREA_STEP_MAP["wifi"][3] ),
                },
                new List<JudgeArea>() // Center
                {
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A6, false } },SLIDE_AREA_STEP_MAP["wifi"][0]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.B6, false } },SLIDE_AREA_STEP_MAP["wifi"][1]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.C, false } },SLIDE_AREA_STEP_MAP["wifi"][2]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A2, true },{SensorType.B2, true }  },SLIDE_AREA_STEP_MAP["wifi"][3] ),
                },
                new List<JudgeArea>() // R
                {
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A6, false } },SLIDE_AREA_STEP_MAP["wifi"][0]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.B7, false } },SLIDE_AREA_STEP_MAP["wifi"][1]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.B8, false } },SLIDE_AREA_STEP_MAP["wifi"][2]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A1, true },{SensorType.D2, true }  },SLIDE_AREA_STEP_MAP["wifi"][3] ),
                }
            }
        },
        { 7,
            new List<List<JudgeArea>>()
            {
                new List<JudgeArea>() // L
                {
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A7, false } },SLIDE_AREA_STEP_MAP["wifi"][0]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.B6, false } },SLIDE_AREA_STEP_MAP["wifi"][1]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.B5, false } },SLIDE_AREA_STEP_MAP["wifi"][2]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A4, true },{SensorType.D4, true }  },SLIDE_AREA_STEP_MAP["wifi"][3] ),
                },
                new List<JudgeArea>() // Center
                {
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A7, false } },SLIDE_AREA_STEP_MAP["wifi"][0]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.B7, false } },SLIDE_AREA_STEP_MAP["wifi"][1]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.C, false } },SLIDE_AREA_STEP_MAP["wifi"][2]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A3, true },{SensorType.B3, true }  },SLIDE_AREA_STEP_MAP["wifi"][3] ),
                },
                new List<JudgeArea>() // R
                {
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A7, false } },SLIDE_AREA_STEP_MAP["wifi"][0]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.B8, false } },SLIDE_AREA_STEP_MAP["wifi"][1]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.B1, false } },SLIDE_AREA_STEP_MAP["wifi"][2]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A2, true },{SensorType.D3, true }  },SLIDE_AREA_STEP_MAP["wifi"][3] ),
                }
            }
        },
        { 8,
            new List<List<JudgeArea>>()
            {
                new List<JudgeArea>() // L
                {
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A8, false } },SLIDE_AREA_STEP_MAP["wifi"][0]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.B7, false } },SLIDE_AREA_STEP_MAP["wifi"][1]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.B6, false } },SLIDE_AREA_STEP_MAP["wifi"][2]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A5, true },{SensorType.D5, true }  },SLIDE_AREA_STEP_MAP["wifi"][3] ),
                },
                new List<JudgeArea>() // Center
                {
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A8, false } },SLIDE_AREA_STEP_MAP["wifi"][0]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.B8, false } },SLIDE_AREA_STEP_MAP["wifi"][1]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.C, false } },SLIDE_AREA_STEP_MAP["wifi"][2]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A4, true },{SensorType.B4, true }  },SLIDE_AREA_STEP_MAP["wifi"][3] ),
                },
                new List<JudgeArea>() // R
                {
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A8, false } },SLIDE_AREA_STEP_MAP["wifi"][0]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.B1, false } },SLIDE_AREA_STEP_MAP["wifi"][1]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.B2, false } },SLIDE_AREA_STEP_MAP["wifi"][2]),
                    new JudgeArea(new Dictionary<SensorType, bool>(){ {SensorType.A3, true },{SensorType.D4, true }  },SLIDE_AREA_STEP_MAP["wifi"][3] ),
                }
            }
        }
    };
    // Start is called before the first frame update
    private void Start()
    {
        Application.targetFrameRate = 120;
        ObjectCounter = GameObject.Find("ObjectCounter").GetComponent<ObjectCounter>();
        customSkin = GameObject.Find("Outline").GetComponent<CustomSkin>();
        noteManager = GameObject.Find("Notes").GetComponent<NoteManager>();
        if (cardImage != null)
            songDetailTemplate = cardImage.GetComponent<SongDetailTemplateView>() ??
                                 cardImage.gameObject.AddComponent<SongDetailTemplateView>();
        songDetailJacket = GameObject.Find("Jacket")?.GetComponent<RawImage>();
    }

    // Update is called once per frame
    private void Update()
    {
        switch(State)
        {
            case NoteLoaderStatus.LodingJson:
                if (jsonLoaderTask is null || !jsonLoaderTask.IsCompleted)
                    return;
                if (jsonLoaderTask.IsCanceled || jsonLoaderTask.IsFaulted)
                {
                    var message = jsonLoaderTask.Exception?.GetBaseException().Message ??
                                  "Chart JSON loading was cancelled.";
                    UnityEngine.Debug.LogWarning("[ChartLoader] Ignored invalid chart JSON: " + message);
                    CompleteEmptyLoad();
                    return;
                }

                loadedData = jsonLoaderTask.Result;
                if (!previewOnly)
                {
                    ObjectCounter.ResetForChart();
                    if (diffText != null) diffText.text = loadedData.difficulty;
                    if (levelText != null) levelText.text = loadedData.level;
                    if (titleText != null) titleText.text = loadedData.title;
                    if (artistText != null) artistText.text = loadedData.artist;
                    if (designText != null) designText.text = loadedData.designer;
                    if (songDetailTemplate != null && cardImage != null &&
                        songDetailTemplate.IsMasterTemplate(loadedData) &&
                        songDetailTemplate.ApplyMaster(loadedData, cardImage, songDetailJacket,
                            diffText, levelText, titleText, artistText, designText))
                    {
                    }
                    else
                    {
                        songDetailTemplate?.ResetOriginal();
                        if (cardImage != null)
                            cardImage.color = diffColors[Mathf.Clamp(loadedData.diffNum, 0, diffColors.Length - 1)];
                    }

                    CountNoteSum(loadedData);
                    ObjectCounter.CompleteChartInitialization();
                }

                if (loadedData?.timingList == null || loadedData.timingList.Count == 0)
                {
                    CompleteEmptyLoad();
                    return;
                }
                var lastNoteTime = loadedData.timingList.Last().time;

                SvController.Load(loadedData.svTable, 0d);
                BuildHSpeedTimeline(loadedData.hsTable);
                BuildSpawnTimeline(loadedData.spawnTable);
                BuildSpawnModeTimeline(loadedData.spawnModeTable);
                BuildBounceTimeline(loadedData.bounceTable);
                BuildDestroyTimeline(loadedData.destroyTable);
                BuildColorTimeline(loadedData.colorTable);
                BuildSizeTimeline(loadedData.sizeTable);
                BuildAlphaTimeline(loadedData.alphaTable);

                noteParserTask = StartCoroutine(LoadNotes(loadedData.timingList, ignoreOffset, lastNoteTime));

                State = NoteLoaderStatus.ParsingNote;
                break;
            case NoteLoaderStatus.ParsingNote:
                if (noteParserTask == null)
                {
                    State = NoteLoaderStatus.Finished;
                    //noteManager.Refresh();
                    return;
                }
                break;
        }

    }

    private void CompleteEmptyLoad()
    {
        noteParserTask = null;
        runtimeBindingReadyFrame = -1;
        State = NoteLoaderStatus.Finished;
    }

    private void LateUpdate()
    {
        // Notes created during HttpHandler.Update bind their input in Start() on the
        // following frame. Publish readiness only after that frame's Start/Update pass.
        if (runtimeBindingReadyFrame >= 0 && Time.frameCount >= runtimeBindingReadyFrame)
            runtimeBindingReadyFrame = -1;
    }
    IEnumerator LoadNotes(IEnumerable<SimaiTimingPoint> timingList, float ignoreOffset, double lastNoteTime)
    {
        if (timeProvider == null)
            timeProvider = FindAnyObjectByType<AudioTimeProvider>();

        if (!previewOnly)
            noteManager.Refresh();
        noteIndex.Clear();
        touchIndex.Clear();
        for (int i = 1; i < 17; i++) // 1-8=A zone, 9-16=D zone
            noteIndex.Add(i, 0);
        for (int i = 0; i < 33; i++)
            touchIndex.Add((SensorType)i, 0);

        Stopwatch sw = new();
        sw.Start();
        foreach (var timing in timingList)
        {
            // Every note built below inherits this, so a note that turns out
            // unrenderable can name the beat it came from instead of failing
            // silently somewhere the editor cannot see.
            currentBeatLine = timing.rawTextPositionY;
            currentBeatColumn = timing.rawTextPositionX;
            currentBeatTime = timing.time;
            currentBeatContent = timing.notesContent ?? string.Empty;
            var timingIsEach = BeatIsEach(timing);
            var timingStateIsEach = timing.isEachInStream ?? timingIsEach;
            // Keep the editor responsive while preloading, but do not start playback
            // until every note path is built. Constructing D-zone routes during audio
            // playback causes a visible one-frame partial route and a main-thread hitch.
            //
            // Yielding this often stretches the build to roughly eight times its own
            // cost in wall-clock time, which is the wait before a paused preview
            // starts following the drag. Nothing is playing during a preview, so
            // there is no audio to protect and the author is sitting there waiting:
            // take most of the frame instead of a sliver of it.
            if (sw.ElapsedMilliseconds >= (previewOnly ? 12 : 2))
            {
                yield return 0;
                sw.Restart();
            }
            // A beat is built object by object, so a throw halfway through used to
            // leave everything built so far in the scene: a star head with no
            // slide to follow, drifting on its own and taking a miss. A beat that
            // fails has to leave nothing behind.
            var beatObjectCount = notes.transform.childCount;
            beatJudgeRegistrations.Clear();
            try
            {
                var beatNotes = timing.noteList;
                var restoringActiveSustain = false;
                if (timing.time < ignoreOffset)
                {
                    CountNoteCount(timing.noteList);
                    if (!includeActiveSustainsAtOffset)
                        continue;

                    beatNotes = timing.noteList.FindAll(note =>
                        RemainsVisibleAt(note, timing.time, ignoreOffset));
                    if (beatNotes.Count == 0)
                        continue;
                    restoringActiveSustain = true;
                }
                List<TouchDrop> members = new();
                for (var i = 0; i < beatNotes.Count; i++)
                {
                    var note = beatNotes[i];
                    NormalizeBorrowedTrajectory(timing, note);
                    if (note.noteType is SimaiNoteType.Tap or SimaiNoteType.Hold or SimaiNoteType.Slide &&
                        (note.startPosition < 1 || note.startPosition > 8))
                    {
                        if (!previewOnly)
                            UnityEngine.Debug.LogError(
                                $"Skipping malformed {note.noteType} at {timing.time:F3}s: " +
                                $"startPosition={note.startPosition}.");
                        continue;
                    }

                    if (note.noteType == SimaiNoteType.Tap)
                    {
                        GameObject GOnote = null;
                        TapBase NDCompo = null;
                        
                        if (note.isForceStar)
                        {
                            GOnote = Instantiate(starPrefab, notes.transform);
                            var _NDCompo = PrepareNote<StarDrop>(GOnote, note);
                            _NDCompo.tapSpr = customSkin.Star;
                            _NDCompo.eachSpr = customSkin.Star_Each;
                            _NDCompo.breakSpr = customSkin.Star_Break;
                            _NDCompo.exSpr = customSkin.Star_Ex;
                            if (note.isMineHead)
                            {
                                _NDCompo.eachSpr = customSkin.Star;
                                _NDCompo.breakSpr = customSkin.Star;
                            }
                            _NDCompo.tapLine = starLine;
                            _NDCompo.isFakeStarRotate = note.isFakeRotate;
                            _NDCompo.isFakeStar = true;
                            _NDCompo.isMine = note.isMineHead;
                            NDCompo = _NDCompo;
                        }
                        else
                        {
                            GOnote = Instantiate(tapPrefab, notes.transform);
                            NDCompo = PrepareNote<TapDrop>(GOnote, note);
                            // Custom note style
                            NDCompo.tapSpr = customSkin.Tap;
                            NDCompo.breakSpr = customSkin.Tap_Break;
                            NDCompo.eachSpr = customSkin.Tap_Each;
                            NDCompo.exSpr = customSkin.Tap_Ex;
                            if (note.isMineHead)
                            {
                                NDCompo.breakSpr = customSkin.Tap;
                                NDCompo.eachSpr = customSkin.Tap;
                            }
                        }
                        AddJudgeNote(GOnote, note);
                        // Note layer order
                        NDCompo.noteSortOrder = noteSortOrder;
                        noteSortOrder -= NOTE_LAYER_COUNT[note.noteType];

                        NDCompo.BreakShine = BreakShine;

                        if (timingIsEach) NDCompo.isEach = true;
                        NDCompo.isEachInStream = timingStateIsEach;
                        NDCompo.isBreak = note.isBreak;
                        NDCompo.isEX = note.isEx;
                        NDCompo.isFirework = note.isHanabi;
                        NDCompo.isMine = note.isMineHead;
                        NDCompo.isDZone = note.isDZone;
                        NDCompo.time = (float)timing.time;
                        NDCompo.startPosition = note.startPosition;
                        var tapType = note.isForceStar ? "star" : "tap";
                        var tapIsEach = timingStateIsEach;
                        var tapIsMine = note.isMineHead;
                        NDCompo.speed = noteSpeed * GetHSpeedAt(
                            tapType, timing.time, timing.HSpeed, note.isBreak, tapIsEach,
                            timing.streamIndex, tapIsMine);
                        NDCompo.scrollType = ResolveSvType(
                            tapType, note.isBreak, tapIsEach, timing.streamIndex, tapIsMine);
                        NDCompo.noteScrollPos = SvController.GetCumulativeScroll(
                            timing.time, NDCompo.scrollType);
                        NDCompo.spawnRadius = GetSpawnRadiusAt(
                            tapType, timing.time, note.isBreak, tapIsEach,
                            timing.streamIndex, tapIsMine);
                        NDCompo.spawnMode = GetSpawnModeAt(
                            tapType, timing.time, note.isBreak, tapIsEach,
                            timing.streamIndex, tapIsMine);
                        NDCompo.destroyRadius = GetDestroyRadiusAt(
                            tapType, timing.time, note.isBreak, tapIsEach,
                            timing.streamIndex, tapIsMine);
                        NDCompo.bounceDuration = GetBounceDurationAt(
                            tapType, timing.time, note.isBreak, tapIsEach,
                            timing.streamIndex, tapIsMine);
                        NDCompo.ConfigureBounce(NDCompo.speed / noteSpeed);
                        var tapMat = note.isForceStar
                            ? GetStarMaterial(note.isBreak, timingStateIsEach, timing.time, note.isMineHead, timing.streamIndex)
                            : GetTapMaterial(note.isBreak, timingStateIsEach, timing.time, note.isMineHead, timing.streamIndex);
                        NDCompo.colorOverrideMaterial = tapMat;
                        if (tapMat != null) NDCompo.noteTintColor = tapMat.GetColor("_NoteColor");
                        var tapSize = GetSizeAt(
                            tapType, timing.time, note.isBreak, tapIsEach,
                            timing.streamIndex, tapIsMine);
                        NDCompo.noteScaleX = tapSize.x;
                        NDCompo.noteScaleY = tapSize.y;
                    }
                    else if (note.noteType == SimaiNoteType.Hold)
                    {
                        var GOnote = Instantiate(holdPrefab, notes.transform);
                        AddJudgeNote(GOnote, note);
                        var NDCompo = PrepareNote<HoldDrop>(GOnote, note);

                        // Note layer order
                        NDCompo.noteSortOrder = noteSortOrder;
                        noteSortOrder -= NOTE_LAYER_COUNT[note.noteType];

                        NDCompo.tapSpr = customSkin.Hold;
                        NDCompo.holdOnSpr = customSkin.Hold_On;
                        NDCompo.holdOffSpr = customSkin.Hold_Off;
                        NDCompo.eachSpr = customSkin.Hold_Each;
                        NDCompo.eachHoldOnSpr = customSkin.Hold_Each_On;
                        NDCompo.exSpr = customSkin.Hold_Ex;
                        NDCompo.breakSpr = customSkin.Hold_Break;
                        NDCompo.breakHoldOnSpr = customSkin.Hold_Break_On;
                        if (note.isMineHead)
                        {
                            NDCompo.eachSpr = customSkin.Hold;
                            NDCompo.eachHoldOnSpr = customSkin.Hold_On;
                            NDCompo.breakSpr = customSkin.Hold;
                            NDCompo.breakHoldOnSpr = customSkin.Hold_On;
                        }

                        NDCompo.HoldShine = HoldShine;
                        NDCompo.BreakShine = BreakShine;

                        if (timingIsEach) NDCompo.isEach = true;
                        NDCompo.isEachInStream = timingStateIsEach;
                        NDCompo.time = (float)timing.time;
                        NDCompo.LastFor = (float)note.holdTime;
                        NDCompo.startPosition = note.startPosition;
                        var holdIsEach = timingStateIsEach;
                        var holdIsMine = note.isMineHead;
                        NDCompo.speed = noteSpeed * GetHSpeedAt(
                            "hold", timing.time, timing.HSpeed, note.isBreak, holdIsEach,
                            timing.streamIndex, holdIsMine);
                        NDCompo.scrollType = ResolveSvType(
                            "hold", note.isBreak, holdIsEach, timing.streamIndex, holdIsMine);
                        NDCompo.noteScrollPos = SvController.GetCumulativeScroll(
                            timing.time, NDCompo.scrollType);
                        NDCompo.spawnRadius = GetSpawnRadiusAt(
                            "hold", timing.time, note.isBreak, holdIsEach,
                            timing.streamIndex, holdIsMine);
                        NDCompo.spawnMode = GetSpawnModeAt(
                            "hold", timing.time, note.isBreak, holdIsEach,
                            timing.streamIndex, holdIsMine);
                        NDCompo.destroyRadius = GetDestroyRadiusAt(
                            "hold", timing.time, note.isBreak, holdIsEach,
                            timing.streamIndex, holdIsMine);
                        NDCompo.bounceDuration = GetBounceDurationAt(
                            "hold", timing.time, note.isBreak, holdIsEach,
                            timing.streamIndex, holdIsMine);
                        NDCompo.ConfigureBounce(NDCompo.speed / noteSpeed);
                        NDCompo.isEX = note.isEx;
                        NDCompo.isBreak = note.isBreak;
                        NDCompo.isFirework = note.isHanabi;
                        NDCompo.isMine = note.isMineHead;
                        NDCompo.isDZone = note.isDZone;
                        var holdMat = GetHoldMaterial(
                            note.isBreak, timingStateIsEach, timing.time, note.isMineHead, timing.streamIndex);
                        NDCompo.colorOverrideMaterial = holdMat;
                        if (holdMat != null) NDCompo.noteTintColor = holdMat.GetColor("_NoteColor");
                        var holdSize = GetSizeAt(
                            "hold", timing.time, note.isBreak, holdIsEach,
                            timing.streamIndex, holdIsMine);
                        NDCompo.noteScaleX = holdSize.x;
                        NDCompo.noteScaleY = holdSize.y;
                    }
                    else if (note.noteType == SimaiNoteType.TouchHold)
                    {
                        var touchSensor = Assets.Scripts.TouchBase.GetSensor(note.touchArea, note.startPosition);
                        var GOnote = Instantiate(touchHoldPrefab, notes.transform);
                        AddJudgeTouch(GOnote, touchSensor, note);
                        var NDCompo = PrepareNote<TouchHoldDrop>(GOnote, note);
                        NDCompo.isEachInStream = timingStateIsEach;

                        // Note layer order
                        NDCompo.noteSortOrder = noteSortOrder;
                        noteSortOrder -= NOTE_LAYER_COUNT[note.noteType];

                        NDCompo.touchArea = note.touchArea;
                        NDCompo.startPosition = note.startPosition;
                        NDCompo.time = (float)timing.time;
                        NDCompo.LastFor = (float)note.holdTime;
                        var touchHoldIsEach = timingStateIsEach;
                        var touchHoldIsMine = note.isMineHead;
                        NDCompo.speed = touchSpeed * GetHSpeedAt(
                            "touchhold", timing.time, timing.HSpeed, note.isBreak, touchHoldIsEach,
                            timing.streamIndex, touchHoldIsMine);
                        NDCompo.scrollType = ResolveSvType(
                            "touchhold", note.isBreak, touchHoldIsEach, timing.streamIndex,
                            touchHoldIsMine);
                        NDCompo.noteScrollPos = SvController.GetCumulativeScroll(
                            timing.time, NDCompo.scrollType);
                        NDCompo.isFirework = note.isHanabi;
                        NDCompo.isBreak = note.isBreak;
                        NDCompo.isMine = note.isMineHead;
                        // A mine stays grey unless the chart named a colour for mines.
                        var thMat = touchHoldIsMine
                            ? MineMaterial(
                                timing.time, timing.streamIndex,
                                GetAlphaAt("touchhold", timing.time, timing.streamIndex,
                                    note.isBreak, touchHoldIsEach, isMine: true))
                            : CreateTintMaterial(
                                GetColorAt("touchhold", timing.time, timing.streamIndex,
                                    note.isBreak, touchHoldIsEach),
                                GetAlphaAt("touchhold", timing.time, timing.streamIndex,
                                    note.isBreak, touchHoldIsEach));
                        NDCompo.colorOverrideMaterial = thMat;
                        if (thMat != null && !note.isMineHead) NDCompo.noteTintColor = thMat.GetColor("_NoteColor");
                        NDCompo.breakProgressMaterial = note.isBreak && !note.isMineHead
                            ? CreateTintMaterial("FF6538", 1f, 0f)
                            : null;
                        if (NDCompo.breakProgressMaterial != null &&
                            NDCompo.breakProgressMaterial.HasProperty("_Brightness"))
                            NDCompo.breakProgressMaterial.SetFloat("_Brightness", 1.12f);
                        NDCompo.breakProgressSprite = note.isBreak && !note.isMineHead
                            ? customSkin.TouchHold[4]
                            : null;
                        var touchHoldSize = GetSizeAt(
                            "touchhold", timing.time, note.isBreak, touchHoldIsEach,
                            timing.streamIndex, touchHoldIsMine);
                        NDCompo.noteScaleX = touchHoldSize.x;
                        NDCompo.noteScaleY = touchHoldSize.y;

                        Array.Copy(note.isMineHead
                            ? customSkin.TouchHold
                            : note.isBreak ? customSkin.TouchHold_Break : customSkin.TouchHold,
                            NDCompo.TouchHoldSprite, 5);
                        NDCompo.TouchPointSprite = note.isMineHead
                            ? customSkin.TouchPoint
                            : note.isBreak
                                ? customSkin.TouchPoint_Break
                                : touchHoldIsEach
                                    ? customSkin.TouchPoint_Each
                                    : customSkin.TouchPoint;
                    }
                    else if (note.noteType == SimaiNoteType.Touch)
                    {
                        var GOnote = Instantiate(touchPrefab, notes.transform);
                        AddJudgeTouch(GOnote, TouchBase.GetSensor(note.touchArea, note.startPosition), note);
                        var NDCompo = PrepareNote<TouchDrop>(GOnote, note);
                        NDCompo.isEachInStream = timingStateIsEach;

                        // Note layer order
                        NDCompo.noteSortOrder = noteSortOrder;
                        noteSortOrder -= NOTE_LAYER_COUNT[note.noteType];

                        NDCompo.time = (float)timing.time;
                        NDCompo.areaPosition = note.touchArea;
                        NDCompo.startPosition = note.startPosition;
                        // "~[distance]" moves where the Note is drawn; the sensor
                        // that judges it stays the one the area named.
                        NDCompo.customRadius = note.touchRadius;

                        // Break touches use dedicated skin sprites, including each notes.
                        NDCompo.fanNormalSprite = note.isBreak ? customSkin.Touch_Break : customSkin.Touch;
                        NDCompo.fanEachSprite = note.isBreak ? customSkin.Touch_Break : customSkin.Touch_Each;
                        NDCompo.pointNormalSprite = note.isBreak ? customSkin.TouchPoint_Break : customSkin.TouchPoint;
                        NDCompo.pointEachSprite = note.isBreak ? customSkin.TouchPoint_Break : customSkin.TouchPoint_Each;
                        if (note.isMineHead)
                        {
                            NDCompo.fanNormalSprite = customSkin.Touch;
                            NDCompo.fanEachSprite = customSkin.Touch;
                            NDCompo.pointNormalSprite = customSkin.TouchPoint;
                            NDCompo.pointEachSprite = customSkin.TouchPoint;
                        }
                        NDCompo.justSprite = customSkin.TouchJust;
                        Array.Copy(customSkin.TouchBorder, NDCompo.multTouchNormalSprite, 2);
                        Array.Copy(customSkin.TouchBorder_Each, NDCompo.multTouchEachSprite, 2);

                        if (timingIsEach)
                        {
                            NDCompo.isEach = true;
                            members.Add(NDCompo);
                        }
                        var touchIsEach = timingStateIsEach;
                        var touchIsMine = note.isMineHead;
                        NDCompo.speed = touchSpeed * GetHSpeedAt(
                            "touch", timing.time, timing.HSpeed, note.isBreak, touchIsEach,
                            timing.streamIndex, touchIsMine);
                        NDCompo.scrollType = ResolveSvType(
                            "touch", note.isBreak, touchIsEach, timing.streamIndex, touchIsMine);
                        NDCompo.noteScrollPos = SvController.GetCumulativeScroll(
                            timing.time, NDCompo.scrollType);
                        NDCompo.isFirework = note.isHanabi;
                        NDCompo.isBreak = note.isBreak;
                        NDCompo.isMine = note.isMineHead;
                        NDCompo.GroupInfo = null;
                        // A mine stays grey unless the chart named a colour for mines.
                        NDCompo.colorOverrideMaterial = touchIsMine
                            ? MineMaterial(
                                timing.time, timing.streamIndex,
                                GetAlphaAt("touch", timing.time, timing.streamIndex,
                                    note.isBreak, touchIsEach, isMine: true))
                            : CreateTintMaterial(
                                GetColorAt("touch", timing.time, timing.streamIndex,
                                    note.isBreak, touchIsEach),
                                GetAlphaAt("touch", timing.time, timing.streamIndex,
                                    note.isBreak, touchIsEach));
                        var touchSize = GetSizeAt(
                            "touch", timing.time, note.isBreak, touchIsEach,
                            timing.streamIndex, touchIsMine);
                        NDCompo.noteScaleX = touchSize.x;
                        NDCompo.noteScaleY = touchSize.y;
                    }

                    else if (note.noteType == SimaiNoteType.Slide)
                    {
                        if (note.isTrajectoryOnly)
                            InstantiateTrajectoryCarrier(timing, note);
                        else if (note.isTouchSlide)
                            InstantiateTouchSlide(timing, note, members);
                        else
                            InstantiateStarGroup(timing, note, i, lastNoteTime); // Star group
                    }
                }


                if (members.Count != 0)
                {
                    var sensorTypes = members.GroupBy(x => x.GetSensor())
                                             .Select(x => x.Key)
                                             .ToList();
                    List<List<SensorType>> sensorGroups = new();

                    while (sensorTypes.Count > 0)
                    {
                        var sensorType = sensorTypes[0];
                        var existsGroup = sensorGroups.FindAll(x => x.Contains(sensorType));
                        var groupMap = TOUCH_GROUPS[sensorType];
                        existsGroup.AddRange(sensorGroups.FindAll(x => x.Any(y => groupMap.Contains(y))));

                        var groupMembers = existsGroup.SelectMany(x => x)
                                                      .ToList();
                        var newMembers = sensorTypes.FindAll(x => groupMap.Contains(x));

                        groupMembers.AddRange(newMembers);
                        groupMembers.Add(sensorType);
                        var newGroup = groupMembers.GroupBy(x => x)
                                                   .Select(x => x.Key)
                                                   .ToList();

                        foreach (var newMember in newGroup)
                            sensorTypes.Remove(newMember);
                        foreach (var oldGroup in existsGroup)
                            sensorGroups.Remove(oldGroup);

                        sensorGroups.Add(newGroup);
                    }
                    List<TouchGroup> touchGroups = new();
                    var memberMapping = members.ToDictionary(x => x.GetSensor());
                    foreach (var group in sensorGroups)
                    {
                        touchGroups.Add(new TouchGroup()
                        {
                            Members = group.Select(x => memberMapping[x]).ToArray()
                        });
                    }
                    foreach (var member in members)
                        member.GroupInfo = touchGroups.Find(x => x.Members.Any(y => y == member));
                }

                var eachNotes = beatNotes.FindAll(o =>
                    o.noteType != SimaiNoteType.Touch && o.noteType != SimaiNoteType.TouchHold);
                if (!restoringActiveSustain && eachNotes.Count > 1) // Multiple non-Touch notes
                {
                    var startPos = eachNotes[0].startPosition;
                    var endPos = eachNotes[1].startPosition;
                    endPos = endPos - startPos;
                    if (endPos == 0) continue;

                    var line = Instantiate(eachLine, notes.transform);
                    var lineDrop = line.GetComponent<EachLineDrop>();

                    lineDrop.previewOnly = previewOnly;
                    lineDrop.time = (float)timing.time;
                    lineDrop.speed = noteSpeed * GetHSpeedAt(
                        "tap", timing.time, timing.HSpeed, isEach: true,
                        streamIndex: timing.streamIndex);
                    lineDrop.scrollType = ResolveSvType("tap", false, true, timing.streamIndex);
                    lineDrop.noteScrollPos = SvController.GetCumulativeScroll(
                        timing.time, lineDrop.scrollType);
                    lineDrop.spawnRadius = GetSpawnRadiusAt(
                        "tap", timing.time, isEach: true, streamIndex: timing.streamIndex);
                    lineDrop.spawnMode = GetSpawnModeAt(
                        "tap", timing.time, isEach: true, streamIndex: timing.streamIndex);
                    lineDrop.destroyRadius = GetDestroyRadiusAt(
                        "tap", timing.time, isBreak: false, isEach: true,
                        streamIndex: timing.streamIndex);
                    lineDrop.bounceDuration = GetBounceDurationAt(
                        "tap", timing.time, isBreak: false, isEach: true,
                        streamIndex: timing.streamIndex);
                    lineDrop.ConfigureBounce(lineDrop.speed / noteSpeed);

                    endPos = endPos < 0 ? endPos + 8 : endPos;
                    endPos = endPos > 8 ? endPos - 8 : endPos;
                    endPos++;

                    if (endPos > 4)
                    {
                        startPos = eachNotes[1].startPosition;
                        endPos = eachNotes[0].startPosition;
                        endPos = endPos - startPos;
                        endPos = endPos < 0 ? endPos + 8 : endPos;
                        endPos = endPos > 8 ? endPos - 8 : endPos;
                        endPos++;
                    }

                    lineDrop.startPosition = startPos;
                    lineDrop.curvLength = endPos - 1;
                }

            }
            catch (Exception e)
            {
                // The try covers the whole beat, so one note that cannot be built
                // takes every note sharing its beat. Name the beat: this message
                // is the only trace left behind. Syntax feedback belongs in the
                // editor, and logging as an error would trip Unity's Error Pause
                // and deadlock synchronous editor requests, so this stays a
                // warning - which in a built player means it reaches Player.log
                // and nothing else. A bare stack trace there leaves the note
                // simply absent, with nothing to search the chart for.
                RollBackBeatJudgeRegistrations();
                for (var i = notes.transform.childCount - 1; i >= beatObjectCount; i--)
                {
                    var partial = notes.transform.GetChild(i).gameObject;
                    // Nothing judged this note and nothing should score it on the
                    // way out; previewOnly is what its own OnDestroy reads to know
                    // it was never part of the played chart.
                    var drop = partial.GetComponent<NoteDrop>();
                    if (drop != null)
                        drop.previewOnly = true;
                    Destroy(partial);
                }
                UnityEngine.Debug.LogWarning(
                    $"[ChartLoader] Dropped beat at t={timing.time:F3} " +
                    $"'{timing.notesContent}'{(previewOnly ? " (preview)" : "")}: {e}");
                droppedBeats.Add(new DroppedBeat(
                    timing.rawTextPositionY,
                    timing.rawTextPositionX,
                    timing.time,
                    timing.notesContent ?? string.Empty,
                    e.Message));
                if (previewOnly)
                    continue;
            }
        }
        if (!previewOnly && runtimeBindingReadyFrame == int.MaxValue)
            runtimeBindingReadyFrame = Time.frameCount + 1;
        noteParserTask = null;
        yield break;
    }

    /// <summary>
    /// Whether this beat is an "each", the pairing that draws its notes yellow and
    /// selects the each variants of size, scroll type and spawn radius.
    /// </summary>
    /// <remarks>
    /// The rule itself is <see cref="EachRule"/>, shared with the editor's timeline,
    /// because the two used to decide this differently.
    /// </remarks>
    private static bool BeatIsEach(SimaiTimingPoint timing) =>
        EachRule.IsEach(
            timing.isEach,
            timing.noteList.Count(
                note => EachRule.CountsTowardEach(note.isSlideNoHead)));

    /// <summary>
    /// Whether this beat's slide trails are drawn yellow, which is a different question
    /// from <see cref="BeatIsEach"/>: a same-head pair is one struck head and two
    /// trails, so the head stays plain while both trails turn.
    /// </summary>
    private static bool BeatTrailsAreEach(SimaiTimingPoint timing) =>
        EachRule.TrailsAreEach(
            timing.noteList.Count(note => note.noteType == SimaiNoteType.Slide));

    /// <summary>
    /// How many of this beat's slides leave <paramref name="startPosition"/>. More than
    /// one means they share a head, which is drawn with the double star.
    /// </summary>
    private static int BeatSlidesFrom(SimaiTimingPoint timing, int startPosition) =>
        timing.noteList.Count(
            note => note.noteType == SimaiNoteType.Slide &&
                    note.startPosition == startPosition);

    private T PrepareNote<T>(
        GameObject noteObject,
        SimaiNote note,
        bool? fakeOverride = null) where T : NoteDrop
    {
        var isFake = fakeOverride ?? note.isFake;
        var component = noteObject.GetComponent<T>();
        component.BindSceneContext(timeProvider, ObjectCounter, noteManager);
        component.isFake = isFake;
        component.renderReporter = this;
        component.sourceLine = currentBeatLine;
        component.sourceColumn = currentBeatColumn;
        component.sourceTime = currentBeatTime;
        component.sourceContent = currentBeatContent;
        component.customSkin = note.noteSkin ?? string.Empty;
        // Fake notes disable judgement, but they are still live-chart objects and
        // must expire normally. Only timeline/standby previews are reversible.
        component.previewOnly = previewOnly;
        if (isFake)
            AttachFakeLifetime(noteObject);
        if (component is StarDrop star)
            star.usePinkStarExColor = customSkin.PinkStarEnabled;
        return component;
    }

    private void AddJudgeNote(GameObject noteObject, SimaiNote note, bool? fakeOverride = null)
    {
        if (previewOnly || (fakeOverride ?? note.isFake))
            return;
        var key = note.isDZone ? note.startPosition + 8 : note.startPosition;
        noteManager.AddNote(noteObject, noteIndex[key]++);
        beatJudgeRegistrations.Add((noteObject, key, default, false));
    }

    private void AddJudgeTouch(
        GameObject noteObject,
        SensorType sensorType,
        SimaiNote note,
        bool? fakeOverride = null)
    {
        if (previewOnly || (fakeOverride ?? note.isFake))
            return;
        noteManager.AddTouch(noteObject, touchIndex[sensorType]++);
        beatJudgeRegistrations.Add((noteObject, 0, sensorType, true));
    }

    /// <summary>
    /// Takes back the judgement slots a failed beat had already claimed.
    /// </summary>
    /// <remarks>
    /// Judgement runs in the order notes were handed out per key, and a note only
    /// advances that order by being judged. A note that was registered and then
    /// removed from the scene never judges, so leaving its slot behind would stall
    /// the key it sat on and quietly make every later note there unjudgeable.
    /// Slots are handed out in order, so undoing them in reverse restores the count.
    /// </remarks>
    private void RollBackBeatJudgeRegistrations()
    {
        for (var i = beatJudgeRegistrations.Count - 1; i >= 0; i--)
        {
            var (noteObject, key, sensorType, isTouch) = beatJudgeRegistrations[i];
            if (isTouch)
            {
                noteManager.touchOrder.Remove(noteObject);
                touchIndex[sensorType]--;
            }
            else
            {
                noteManager.noteOrder.Remove(noteObject);
                noteIndex[key]--;
            }
        }
        beatJudgeRegistrations.Clear();
    }

    private static void AttachFakeLifetime(GameObject noteObject)
    {
        if (noteObject.GetComponent<FakeNoteLifetime>() == null)
            noteObject.AddComponent<FakeNoteLifetime>();
    }

    public void LoadJson(
        string json,
        float ignoreOffset,
        bool previewOnly = false,
        bool preserveTintCache = false)
    {
        runtimeBindingReadyFrame = previewOnly ? -1 : int.MaxValue;
        droppedBeats.Clear();
        if (!preserveTintCache)
            ClearTintMaterialCache();
        jsonLoaderTask = Task.Run(() => JsonConvert.DeserializeObject<Majson>(json));
        State = NoteLoaderStatus.LodingJson;
        this.ignoreOffset = ignoreOffset;
        this.previewOnly = previewOnly;
        includeActiveSustainsAtOffset = false;
    }

    public void LoadJsonImmediate(
        string json,
        float ignoreOffset,
        bool previewOnly = false,
        bool preserveTintCache = false,
        bool includeActiveSustains = false)
    {
        runtimeBindingReadyFrame = -1;
        droppedBeats.Clear();
        if (!preserveTintCache)
            ClearTintMaterialCache();
        loadedData = JsonConvert.DeserializeObject<Majson>(json);
        this.ignoreOffset = ignoreOffset;
        this.previewOnly = previewOnly;
        includeActiveSustainsAtOffset = includeActiveSustains;
        if (loadedData == null || loadedData.timingList.Count == 0)
        {
            State = NoteLoaderStatus.Finished;
            return;
        }

        if (!previewOnly)
        {
            ObjectCounter.ResetForChart();
            if (diffText != null) diffText.text = loadedData.difficulty;
            if (levelText != null) levelText.text = loadedData.level;
            if (titleText != null) titleText.text = loadedData.title;
            if (artistText != null) artistText.text = loadedData.artist;
            if (designText != null) designText.text = loadedData.designer;
            if (songDetailTemplate != null && cardImage != null &&
                songDetailTemplate.IsMasterTemplate(loadedData) &&
                songDetailTemplate.ApplyMaster(loadedData, cardImage, songDetailJacket,
                    diffText, levelText, titleText, artistText, designText))
            {
            }
            else
            {
                songDetailTemplate?.ResetOriginal();
                if (cardImage != null)
                    cardImage.color = diffColors[Mathf.Clamp(loadedData.diffNum, 0, diffColors.Length - 1)];
            }

            CountNoteSum(loadedData);
            ObjectCounter.CompleteChartInitialization();
        }

        SvController.Load(loadedData.svTable, 0d);
        BuildHSpeedTimeline(loadedData.hsTable);
        BuildSpawnTimeline(loadedData.spawnTable);
        BuildSpawnModeTimeline(loadedData.spawnModeTable);
        BuildBounceTimeline(loadedData.bounceTable);
        BuildDestroyTimeline(loadedData.destroyTable);
        BuildColorTimeline(loadedData.colorTable);
        BuildSizeTimeline(loadedData.sizeTable);
        BuildAlphaTimeline(loadedData.alphaTable);

        var loader = LoadNotes(loadedData.timingList, ignoreOffset, loadedData.timingList.Last().time);
        while (loader.MoveNext())
        {
        }
        State = NoteLoaderStatus.Finished;
        if (!previewOnly)
            runtimeBindingReadyFrame = Time.frameCount + 1;
    }

    public void ClearLoadedNotes(bool immediate = false)
    {
        PreviewReplacementInProgress = false;
        UnityEngine.Debug.Log($"[MajdataView] ClearLoadedNotes(immediate={immediate}): clearing input bindings and judge queues.");
        if (noteParserTask != null)
        {
            StopCoroutine(noteParserTask);
            noteParserTask = null;
        }

        // Mark reload before removing old notes so OnDestroy cannot emit judgements.
        // Remove note objects first, then reset shared input state. This prevents an
        // old note from observing or mutating the reset performed for the new chart.
        HttpHandler.IsReloding = true;
        var generation = ++reloadGeneration;
        if (notes != null)
        {
            for (var i = notes.transform.childCount - 1; i >= 0; i--)
            {
                var child = notes.transform.GetChild(i).gameObject;
                child.SetActive(false);
                if (immediate)
                    DestroyImmediate(child);
                else
                    Destroy(child);
            }
        }
        noteManager?.Clear();
        (GameObject.Find("Input") ?? GameObject.Find("InputManager"))
            ?.GetComponent<InputManager>()
            ?.ResetInputState(true);
        GameObject.Find("Sensors")?.GetComponent<SensorManager>()?.ResetAllSensors();
        GameObject.Find("MultTouchHandler")?.GetComponent<MultTouchHandler>()?.clearSlots();
        slideLayer = -1;
        noteSortOrder = 0;
        State = NoteLoaderStatus.Idle;
        runtimeBindingReadyFrame = -1;
        StartCoroutine(ResetReloadFlagNextFrame(generation));
    }

    public void CancelPendingLoad()
    {
        if (noteParserTask != null)
        {
            StopCoroutine(noteParserTask);
            noteParserTask = null;
        }
        jsonLoaderTask = null;
        State = NoteLoaderStatus.Idle;
        runtimeBindingReadyFrame = -1;
    }

    public void ClearPreviewNotes()
    {
        if (notes == null)
            return;

        var previewObjects = new HashSet<GameObject>();
        foreach (var note in notes.GetComponentsInChildren<NoteDrop>(true))
            if (note != null && note.previewOnly)
                previewObjects.Add(note.gameObject);
        foreach (var line in notes.GetComponentsInChildren<EachLineDrop>(true))
            if (line != null && line.previewOnly)
                previewObjects.Add(line.gameObject);

        foreach (var previewObject in previewObjects)
        {
            if (previewObject == null)
                continue;
            previewObject.SetActive(false);
            Destroy(previewObject);
        }
    }

    public GameObject[] BeginPreviewReplacement()
    {
        CancelPendingLoad();
        HttpHandler.IsReloding = true;
        reloadGeneration++;
        PreviewReplacementInProgress = true;

        var previous = new List<GameObject>();
        if (notes != null)
            for (var i = 0; i < notes.transform.childCount; i++)
            {
                var previousObject = notes.transform.GetChild(i).gameObject;
                foreach (var note in
                         previousObject.GetComponentsInChildren<NoteDrop>(true))
                    note.previewOnly = true;
                foreach (var line in
                         previousObject.GetComponentsInChildren<EachLineDrop>(true))
                    line.previewOnly = true;
                foreach (var behaviour in
                         previousObject.GetComponentsInChildren<MonoBehaviour>(true))
                    behaviour.enabled = false;
                previous.Add(previousObject);
            }

        noteManager?.Clear();
        (GameObject.Find("Input") ?? GameObject.Find("InputManager"))
            ?.GetComponent<InputManager>()
            ?.ResetInputState(true);
        GameObject.Find("Sensors")?.GetComponent<SensorManager>()?.ResetAllSensors();
        GameObject.Find("MultTouchHandler")?.GetComponent<MultTouchHandler>()?.clearSlots();
        slideLayer = -1;
        noteSortOrder = 0;
        return previous.ToArray();
    }

    public void CompletePreviewReplacement(GameObject[] previous)
    {
        StartCoroutine(RetirePreviousPreviewAtEndOfFrame(
            previous, reloadGeneration));
    }

    private IEnumerator RetirePreviousPreviewAtEndOfFrame(
        GameObject[] previous,
        int generation)
    {
        yield return null;
        yield return new WaitForEndOfFrame();
        if (generation != reloadGeneration)
            yield break;
        if (previous != null)
            foreach (var previousObject in previous)
            {
                if (previousObject == null)
                    continue;
                previousObject.SetActive(false);
                Destroy(previousObject);
            }
        PreviewReplacementInProgress = false;
        HttpHandler.IsReloding = false;
    }

    public bool PreviewReplacementInProgress { get; private set; }

    private IEnumerator ResetReloadFlagNextFrame(int generation)
    {
        yield return null;
        if (generation == reloadGeneration)
            HttpHandler.IsReloding = false;
    }


    private void CountNoteSum(Majson json)
    {
        foreach (var timing in json.timingList)
            foreach (var note in timing.noteList)
            {
                if (note.isFake && note.noteType != SimaiNoteType.Slide)
                    continue;
                if (note.isTouchSlide)
                {
                    if (!note.isSlideNoHead && !note.isFakeHead)
                    {
                        if (note.isBreak) ObjectCounter.breakSum++;
                        else if (note.touchArea == 'K') ObjectCounter.tapSum++;
                        else ObjectCounter.touchSum++;
                    }
                    if (!note.isFakeSlide)
                    {
                        if (note.isSlideBreak) ObjectCounter.breakSum++;
                        else ObjectCounter.slideSum++;
                    }
                    continue;
                }
                if (!note.isBreak)
                {
                    if (note.noteType == SimaiNoteType.Tap) ObjectCounter.tapSum++;
                    if (note.noteType == SimaiNoteType.Hold) ObjectCounter.holdSum++;
                    if (note.noteType == SimaiNoteType.TouchHold) ObjectCounter.holdSum++;
                    if (note.noteType == SimaiNoteType.Touch) ObjectCounter.touchSum++;
                    if (note.noteType == SimaiNoteType.Slide)
                    {
                        if (!note.isSlideNoHead && !note.isFakeHead) ObjectCounter.tapSum++;
                        if (!note.isFakeSlide)
                        {
                            if (note.isSlideBreak)
                                ObjectCounter.breakSum++;
                            else
                                ObjectCounter.slideSum++;
                        }
                    }
                }
                else
                {
                    if (note.noteType == SimaiNoteType.Slide)
                    {
                        if (!note.isSlideNoHead && !note.isFakeHead) ObjectCounter.breakSum++;
                        if (!note.isFakeSlide)
                        {
                            if (note.isSlideBreak)
                                ObjectCounter.breakSum++;
                            else
                                ObjectCounter.slideSum++;
                        }
                    }
                    else
                    {
                        ObjectCounter.breakSum++;
                    }
                }
            }
    }

    private void CountNoteCount(List<SimaiNote> timing)
    {
        foreach (var note in timing)
        {
            if (note.isFake && note.noteType != SimaiNoteType.Slide)
                continue;
            if (note.isTouchSlide)
            {
                if (!note.isSlideNoHead && !note.isFakeHead)
                {
                    if (note.isBreak) ObjectCounter.breakCount++;
                    else if (note.touchArea == 'K') ObjectCounter.tapCount++;
                    else ObjectCounter.touchCount++;
                }
                if (!note.isFakeSlide)
                {
                    if (note.isSlideBreak) ObjectCounter.breakCount++;
                    else ObjectCounter.slideCount++;
                }
                continue;
            }
            if (!note.isBreak)
            {
                if (note.noteType == SimaiNoteType.Tap) ObjectCounter.tapCount++;
                if (note.noteType == SimaiNoteType.Hold) ObjectCounter.holdCount++;
                if (note.noteType == SimaiNoteType.TouchHold) ObjectCounter.holdCount++;
                if (note.noteType == SimaiNoteType.Touch) ObjectCounter.touchCount++;
                if (note.noteType == SimaiNoteType.Slide)
                {
                    if (!note.isSlideNoHead && !note.isFakeHead) ObjectCounter.tapCount++;
                    if (!note.isFakeSlide)
                    {
                        if (note.isSlideBreak)
                            ObjectCounter.breakCount++;
                        else
                            ObjectCounter.slideCount++;
                    }
                }
            }
            else
            {
                if (note.noteType == SimaiNoteType.Slide)
                {
                    if (!note.isSlideNoHead && !note.isFakeHead) ObjectCounter.breakCount++;
                    if (!note.isFakeSlide)
                    {
                        if (note.isSlideBreak)
                            ObjectCounter.breakCount++;
                        else
                            ObjectCounter.slideCount++;
                    }
                }
                else
                {
                    ObjectCounter.breakCount++;
                }
            }
        }
    }

    private static bool RemainsVisibleAt(
        SimaiNote note,
        double beatTime,
        double offset)
    {
        return note.noteType switch
        {
            SimaiNoteType.Hold or SimaiNoteType.TouchHold =>
                beatTime + Math.Max(0d, note.holdTime) > offset,
            SimaiNoteType.Slide =>
                note.slideStartTime + Math.Max(0d, note.slideTime) > offset,
            _ => false
        };
    }

    private List<SlidePathSegmentData> ResolveSlidePath(
        SimaiNote note,
        bool isConnectedSegment = false)
    {
        // A segment of a connected slide has already been parsed and validated as
        // part of its parent path, and these are the very objects that parse
        // produced. Sending it back through the whole-note validator asks it to
        // stand on its own, and under total-duration syntax every segment but the
        // last carries no duration - so "1-3-5[8:1]" was rejected on its first
        // segment for a duration the note does have. That threw halfway through
        // building, after the star head was already in the scene: the slide never
        // appeared and the orphaned head stayed behind with nothing to follow.
        if (isConnectedSegment && note.slidePath is { Count: > 0 })
            return note.slidePath;

        var serialized = note.slidePath;
        if (serialized != null && serialized.Count != 0)
        {
            if (!SlideSyntaxValidator.TryValidateSegments(
                    serialized, out var serializedError))
                throw new Exception(
                    string.IsNullOrEmpty(serializedError)
                        ? SlideSyntaxValidator.Diagnose(
                            "谱面里保存的星星路径不合法",
                            "STORED SLIDE PATH IS INVALID",
                            note.noteContent ?? string.Empty)
                        : serializedError);
            return serialized;
        }

        // Current editor snapshots already carry the shared AST. Re-parsing the
        // authored wrapper first breaks forms whose path is nested inside other
        // syntax, notably 1~[2-5[8:1]]; the wrapper is not itself a slide path.
        if (!string.IsNullOrEmpty(note.pathExpression))
            return ParseSlideExpression(
                note, note.pathExpression);

        if (string.IsNullOrEmpty(note.noteContent))
            throw new Exception(
                SlideSyntaxValidator.Diagnose(
                    "星星没有任何内容",
                    "SLIDE HAS NO CONTENT",
                    string.Empty));
        return ParseSlideExpression(
            note, note.noteContent);
    }

    private static void NormalizeBorrowedTrajectory(
        SimaiTimingPoint timing,
        SimaiNote note)
    {
        if (note.isTrajectoryOnly || string.IsNullOrWhiteSpace(note.pathExpression) ||
            !NoteExpressionParser.TryParse(
                note.pathExpression, out var expression, out _) ||
            expression.trajectory == null)
            return;

        var path = expression.trajectory;
        var end = path.segments[^1].end;
        note.isTrajectoryOnly = true;
        note.isFake = true;
        note.isFakeHead = true;
        note.isFakeSlide = true;
        note.isSlideNoHead = true;
        note.noteType = SimaiNoteType.Slide;
        note.trajectoryCarrierPosition = expression.position.position;
        note.trajectoryCarrierIsDZone = expression.position.isDZone;
        note.trajectoryCarrierType = expression.kind switch
        {
            NoteExpressionKind.Hold => SimaiNoteType.Hold,
            NoteExpressionKind.Touch => SimaiNoteType.Touch,
            NoteExpressionKind.TouchHold => SimaiNoteType.TouchHold,
            _ => SimaiNoteType.Tap
        };
        note.isBreak = expression.modifiers.HasHead(NoteModifierFlags.Break);
        note.isEx = expression.modifiers.HasAny(NoteModifierFlags.Ex);
        note.isHanabi = expression.modifiers.HasAny(NoteModifierFlags.Firework);
        note.isMineHead = expression.modifiers.HasHead(NoteModifierFlags.Mine);
        note.isForceStar = expression.modifiers.HasAny(NoteModifierFlags.ForceStar);
        note.isFakeRotate = expression.modifiers.HasAny(NoteModifierFlags.FakeRotate);
        note.slidePath = path.segments;
        note.startPosition = path.segments[0].start.position;
        note.touchEndPosition = end.position;
        note.isDZone = path.segments[0].start.isDZone;
        note.isDZoneEnd = end.isDZone;
        note.isTouchSlide = expression.isTouchPath;
        if (expression.isTouchPath)
        {
            note.touchArea = path.segments[0].start.area;
            note.touchEndArea = end.area;
            note.touchSlideShape = path.segments[0].shape[0];
        }

        double duration = 0d;
        foreach (var segment in path.segments)
            if (!string.IsNullOrEmpty(segment.duration) &&
                SlideSyntaxValidator.TryGetLengthSeconds(
                    segment.duration, timing.currentBpm, out var seconds))
                duration += seconds;
        if (duration > 0d)
            note.slideTime = duration;

        note.slideStartTime = timing.time;
    }

    private List<SlidePathSegmentData> ParseSlideExpression(
        SimaiNote note,
        string expression)
    {
        if (!SlidePathParser.TryParsePath(expression, out var path))
            throw new Exception(
                SlideSyntaxValidator.Diagnose(
                    "星星路径无法解析",
                    "SLIDE PATH CANNOT BE PARSED",
                    expression));
        if (!SlideSyntaxValidator.TryValidate(path, out var error))
            throw new Exception(
                string.IsNullOrEmpty(error)
                    ? SlideSyntaxValidator.Diagnose(
                        "星星路径不合法",
                        "SLIDE PATH IS INVALID",
                        expression)
                    : error);
        if (!NoteModifierParser.TryParse(
                expression, path.segments, out _))
            throw new Exception(
                SlideSyntaxValidator.Diagnose(
                    "修饰符位置错误：星星主体只能写「b」或「m」，其余修饰符要写在头部",
                    "INVALID SLIDE MODIFIER POSITION: THE BODY ALLOWS ONLY 'b' OR 'm'",
                    expression));
        note.slidePath = path.segments;
        return path.segments;
    }

    private void InstantiateStarGroup(SimaiTimingPoint timing, SimaiNote note, int sort, double lastNoteTime)
    {
        var subSlide = new List<SimaiNote>();
        var subBarCount = new List<int>();
        var sumBarCount = 0;
        var parsedSegments = ResolveSlidePath(note);

        var durationCount = 0;

        foreach (var segment in parsedSegments)
        {
            var slidePart = new SimaiNote
            {
                noteType = SimaiNoteType.Slide,
                startPosition = segment.startPosition,
                isDZone = segment.startIsDZone,
                isDZoneEnd = segment.endIsDZone,
                noteContent = segment.ToExpression(includeDZone: false),
                pathExpression = segment.ToExpression(includeDZone: true),
                slidePath = new List<SlidePathSegmentData> { segment }
            };

            if (!string.IsNullOrEmpty(segment.duration))
                durationCount++;

            var slideShape = DetectShape(segment);
            if (slideShape.StartsWith("-"))
                slideShape = slideShape.Substring(1);
            if (slideShape.StartsWith("r"))
                slideShape = slideShape.Substring(1);
            var slideIndex = Math.Abs(SLIDE_PREFAB_MAP[slideShape]);
            var barCount = slidePrefab[slideIndex].transform.childCount;
            subBarCount.Add(barCount);
            sumBarCount += barCount;
            subSlide.Add(slidePart);
        }

        for (var i = 0; i < subSlide.Count; i++)
        {
            var o = subSlide[i];
            o.isBreak = note.isBreak;
            o.isEx = note.isEx;
            o.isHanabi = i == 0 && note.isHanabi; // Fireworks belong only to the Star head
            o.isMineHead = i == 0 && note.isMineHead;
            o.isMineSlide = note.isMineSlide;
            o.isSlideBreak = note.isSlideBreak;
            o.isFakeHead = i == 0 && note.isFakeHead;
            o.isFakeSlide = note.isFakeSlide;
            o.isFake = o.isFakeHead && o.isFakeSlide;
            o.isSlideNoHead = true;
            o.suppressSlideGuideStarFade = note.suppressSlideGuideStarFade;
        }
        subSlide[0].isSlideNoHead = note.isSlideNoHead;

        if (durationCount != 1 && durationCount != parsedSegments.Count)
            throw new Exception(
                SlideSyntaxValidator.Diagnose(
                    $"组合星星只能写一个总时长，或每段各写一个时长（现在写了 {durationCount} 个，共 {parsedSegments.Count} 段）",
                    "CONNECTED SLIDE NEEDS ONE TOTAL DURATION OR ONE PER SEGMENT",
                    note.noteContent ?? string.Empty));

        if (durationCount == 1)
        {
            // Total-duration syntax uses slideTime for calculation
            var tempBarCount = 0;
            for (var i = 0; i < subSlide.Count; i++)
            {
                subSlide[i].slideStartTime = note.slideStartTime + (double)tempBarCount / sumBarCount * note.slideTime;
                subSlide[i].slideTime = (double)subBarCount[i] / sumBarCount * note.slideTime;
                tempBarCount += subBarCount[i];
            }
        }
        else
        {
            // Per-segment syntax
            double tempSlideTime = 0;
            for (var i = 0; i < subSlide.Count; i++)
            {
                var duration = parsedSegments[i].duration;
                if (!SlideSyntaxValidator.TryGetLengthSeconds(
                        duration, timing.currentBpm, out var slideTime))
                    throw new Exception(
                        SlideSyntaxValidator.Diagnose(
                            "星星时长写法错误",
                            "INVALID SLIDE DURATION",
                            duration));
                subSlide[i].slideStartTime = note.slideStartTime + tempSlideTime;
                subSlide[i].slideTime = slideTime;
                tempSlideTime += subSlide[i].slideTime;
            }
        }

        GameObject parent = null;
        List<SlideDrop> subSlides = new();
        float totalLen = (float)subSlide.Select(x => x.slideTime).Sum();
        float totalSlideLen = 0;
        for (var i = 0; i <= subSlide.Count - 1; i++)
        {
            bool isConn = subSlide.Count != 1;
            bool isGroupHead = i == 0;
            bool isGroupEnd = i == subSlide.Count - 1;
            if (note.noteContent.Contains('w')) //wifi
            {
                if (isConn)
                    throw new InvalidOperationException("不允许Wifi Slide作为Connection Slide的一部分");
                InstantiateWifi(timing, subSlide[i]);
            }
            else
            {
                ConnSlideInfo info = new ConnSlideInfo()
                {
                    TotalLength = totalLen,
                    IsGroupPart = isConn,
                    IsGroupPartHead = isGroupHead,
                    IsGroupPartEnd = isGroupEnd,
                    Parent = parent
                };
                parent = InstantiateStar(timing, subSlide[i], info);
                subSlides.Add(parent.GetComponent<SlideDrop>());
            }
        }
        subSlides.ForEach(s =>
        {
            s.Initialize();
            totalSlideLen += s.GetSlideLength();
        });
        subSlides.ForEach(s => s.ConnectInfo.TotalSlideLen = totalSlideLen);
    }

    private void InstantiateTrajectoryCarrier(
        SimaiTimingPoint timing,
        SimaiNote note)
    {
        var segments = ResolveSlidePath(note);
        var isEach = timing.isEachInStream ?? BeatIsEach(timing);
        var carrierObject = Instantiate(star_slidePrefab, notes.transform);
        carrierObject.name =
            $"TrajectoryCarrier_{note.pathExpression ?? note.noteContent}";
        carrierObject.AddComponent<TrajectoryCarrierDrop>();
        var carrier = PrepareNote<TrajectoryCarrierDrop>(
            carrierObject, note, fakeOverride: false);
        carrier.carrierVisualType = GetTrajectoryCarrierVisualType(note);
        carrier.bodyBreak = note.isBreak;
        carrier.bodyMine = note.isMineHead;
        carrier.isEach = isEach;
        carrier.isEachInStream = isEach;
        carrier.startPosition = note.startPosition;
        carrier.scrollType = ResolveSvType(
            "slide", note.isSlideBreak, isEach,
            timing.streamIndex, note.isMineSlide);
        var carrierScale = GetSizeAt(
            carrier.carrierVisualType,
            timing.time,
            note.isBreak,
            isEach,
            timing.streamIndex,
            note.isMineHead);
        carrier.Configure(
            this,
            segments,
            GetTrajectoryCarrierSprite(note, isEach),
            GetTrajectoryCarrierMaterial(
                note, isEach, timing.time, timing.streamIndex),
            carrierScale,
            (float)timing.time,
            (float)timing.time,
            (float)note.slideTime,
            slideLayer--);
    }

    private void InstantiateTouchSlide(
        SimaiTimingPoint timing,
        SimaiNote note,
        List<TouchDrop> touchMembers)
    {
        var parsedSegments = ResolveSlidePath(note);
        var isEach = BeatIsEach(timing);
        // The trail asks a different question than the head; see BeatTrailsAreEach.
        // Key slides have always drawn the two apart, touch slides used to tie both to
        // the head, which drew a same-head touch pair with a yellow head and, once the
        // head count was corrected, a plain trail.
        var trailIsEach = BeatTrailsAreEach(timing);
        var stateIsEach = timing.isEachInStream ?? isEach;
        var slideObject = new GameObject(
            $"TouchSlide_{note.touchArea}{note.startPosition}{note.touchSlideShape}" +
            $"{note.touchEndArea}{note.touchEndPosition}");
        slideObject.transform.SetParent(notes.transform, false);
        slideObject.AddComponent<TouchSlideDrop>();
        var component = PrepareNote<TouchSlideDrop>(
            slideObject, note, note.isFakeSlide);
        component.isEachInStream = stateIsEach;
        component.timeStart = (float)timing.time;
        component.time = (float)note.slideStartTime;
        component.duration = Mathf.Max(0.01f, (float)note.slideTime);
        component.speed = noteSpeed * ResolveSlideAppearanceSpeed(
            timing.time, timing.streamIndex);
        component.scrollType = ResolveSvType(
            "slide", note.isSlideBreak, stateIsEach, timing.streamIndex,
            note.isMineSlide);
        component.starSpeed = note.suppressSlideGuideStarFade ? 1f : starSpeed;
        component.startArea = note.touchArea;
        component.endArea = note.touchEndArea;
        component.startPosition = note.startPosition;
        component.endPosition = note.touchEndPosition;
        component.isDZone = note.isDZone;
        component.isDZoneEnd = note.isDZoneEnd;
        component.shape = note.touchSlideShape;
        component.pathExpression = note.pathExpression ?? note.noteContent;
        component.pathSegments = parsedSegments;
        component.bodyBreak = note.isSlideBreak;
        component.bodyMine = note.isMineSlide;
        component.suppressGuideStarFadeIn = note.suppressSlideGuideStarFade;
        component.pathSprite = note.isMineSlide
            ? customSkin.Slide
            : note.isSlideBreak
            ? customSkin.Slide_Break
            : trailIsEach ? customSkin.Slide_Each : customSkin.Slide;
        component.barTemplate =
            slidePrefab[SLIDE_PREFAB_MAP["line3"]].transform.GetChild(0).gameObject;
        var judgeSource = slidePrefab[SLIDE_PREFAB_MAP["line3"]].transform;
        component.judgeTemplate = judgeSource.GetChild(judgeSource.childCount - 1).gameObject;
        component.judgeBreakShine = JudgeBreakShine;
        component.pathMaterial = GetSlideMaterial(
            note.isSlideBreak, timing.time, note.isMineSlide, timing.streamIndex,
            stateIsEach);
        component.slideRouteSource = this;
        var slideSize = GetSizeAt(
            "slide", timing.time, false, stateIsEach, timing.streamIndex,
            note.isMineSlide);
        component.barScale = slideSize;

        component.star = Instantiate(star_slidePrefab, notes.transform);
        var starRenderer = component.star.GetComponent<SpriteRenderer>();
        starRenderer.sprite = note.isMineSlide
            ? customSkin.Star
            : note.isSlideBreak
            ? customSkin.Star_Break
            : trailIsEach ? customSkin.Star_Each : customSkin.Star;
        component.starMaterial = GetSlideStarMaterial(
            note.isSlideBreak, stateIsEach, timing.time, note.isMineSlide,
            timing.streamIndex);
        component.starScale = GetSizeAt(
            "slidestar",
            timing.time, note.isSlideBreak, stateIsEach,
            timing.streamIndex, note.isMineSlide);
        component.star.SetActive(false);
        component.sortingOrder = slideLayer;
        slideLayer -= 24;
        component.Initialize();

        if (note.isSlideNoHead)
            return;
        if (note.touchArea == 'K')
        {
            slideObject.SetActive(false);
            InstantiateTouchSlideKeyHead(
                timing, note, slideObject, isEach, stateIsEach);
        }
        else
        {
            InstantiateTouchSlideHead(
                timing, note, touchMembers, isEach, stateIsEach);
        }
    }

    private void InstantiateTouchSlideKeyHead(
        SimaiTimingPoint timing,
        SimaiNote note,
        GameObject slideObject,
        bool isEach,
        bool stateIsEach)
    {
        var starObject = Instantiate(starPrefab, notes.transform);
        AddJudgeNote(starObject, note, note.isFakeHead);
        var star = PrepareNote<StarDrop>(starObject, note, note.isFakeHead);
        star.isEachInStream = stateIsEach;
        star.noteSortOrder = noteSortOrder;
        noteSortOrder -= NOTE_LAYER_COUNT[SimaiNoteType.Slide];
        star.tapSpr = customSkin.Star;
        star.eachSpr = customSkin.Star_Each;
        star.breakSpr = customSkin.Star_Break;
        star.exSpr = customSkin.Star_Ex;
        star.tapSpr_Double = customSkin.Star_Double;
        star.eachSpr_Double = customSkin.Star_Each_Double;
        star.breakSpr_Double = customSkin.Star_Break_Double;
        star.exSpr_Double = customSkin.Star_Ex_Double;
        if (note.isMineHead)
        {
            star.eachSpr = customSkin.Star;
            star.breakSpr = customSkin.Star;
            star.eachSpr_Double = customSkin.Star_Double;
            star.breakSpr_Double = customSkin.Star_Double;
        }
        star.BreakShine = BreakShine;
        star.time = (float)timing.time;
        star.startPosition = note.startPosition;
        star.isDZone = note.isDZone;
        star.rotateSpeed = Mathf.Max(0.01f, (float)note.slideTime);
        star.isEach = isEach;
        star.isDouble = BeatSlidesFrom(timing, note.startPosition) > 1;
        star.isBreak = note.isBreak;
        star.isEX = note.isEx;
        star.isFirework = note.isHanabi;
        star.isMine = note.isMineHead;
        star.slide = slideObject;
        star.speed = noteSpeed * GetHSpeedAt(
            "star", timing.time, timing.HSpeed, note.isBreak, stateIsEach,
            timing.streamIndex, note.isMineHead);
        star.scrollType = ResolveSvType(
            "star", note.isBreak, stateIsEach, timing.streamIndex, note.isMineHead);
        star.noteScrollPos = SvController.GetCumulativeScroll(timing.time, star.scrollType);
        star.spawnRadius = GetSpawnRadiusAt(
            "star", timing.time, note.isBreak, stateIsEach, timing.streamIndex,
            note.isMineHead);
        star.spawnMode = GetSpawnModeAt(
            "star", timing.time, note.isBreak, stateIsEach, timing.streamIndex,
            note.isMineHead);
        star.destroyRadius = GetDestroyRadiusAt(
            "star", timing.time, note.isBreak, stateIsEach, timing.streamIndex,
            note.isMineHead);
        star.bounceDuration = GetBounceDurationAt(
            "star", timing.time, note.isBreak, stateIsEach, timing.streamIndex,
            note.isMineHead);
        star.ConfigureBounce(star.speed / noteSpeed);
        star.colorOverrideMaterial = GetStarMaterial(
            note.isBreak, stateIsEach, timing.time, note.isMineHead,
            timing.streamIndex);
        var size = GetSizeAt(
            "star", timing.time, note.isBreak, stateIsEach, timing.streamIndex,
            note.isMineHead);
        star.noteScaleX = size.x;
        star.noteScaleY = size.y;
    }

    private void InstantiateTouchSlideHead(
        SimaiTimingPoint timing,
        SimaiNote note,
        List<TouchDrop> touchMembers,
        bool isEach,
        bool stateIsEach)
    {
        var touchObject = Instantiate(touchPrefab, notes.transform);
        AddJudgeTouch(
            touchObject,
            TouchBase.GetSensor(note.touchArea, note.startPosition), note, note.isFakeHead);
        var touch = PrepareNote<TouchDrop>(touchObject, note, note.isFakeHead);
        touch.isEachInStream = stateIsEach;
        touch.noteSortOrder = noteSortOrder;
        noteSortOrder -= NOTE_LAYER_COUNT[SimaiNoteType.Touch];
        touch.time = (float)timing.time;
        touch.areaPosition = note.touchArea;
        touch.startPosition = note.startPosition;
        touch.customRadius = note.touchRadius;
        touch.fanNormalSprite = note.isBreak ? customSkin.Touch_Break : customSkin.Touch;
        touch.fanEachSprite = note.isBreak ? customSkin.Touch_Break : customSkin.Touch_Each;
        touch.pointNormalSprite = note.isBreak ? customSkin.TouchPoint_Break : customSkin.TouchPoint;
        touch.pointEachSprite = note.isBreak ? customSkin.TouchPoint_Break : customSkin.TouchPoint_Each;
        if (note.isMineHead)
        {
            touch.fanNormalSprite = customSkin.Touch;
            touch.fanEachSprite = customSkin.Touch;
            touch.pointNormalSprite = customSkin.TouchPoint;
            touch.pointEachSprite = customSkin.TouchPoint;
        }
        touch.justSprite = customSkin.TouchJust;
        Array.Copy(customSkin.TouchBorder, touch.multTouchNormalSprite, 2);
        Array.Copy(customSkin.TouchBorder_Each, touch.multTouchEachSprite, 2);
        touch.isEach = isEach;
        touch.isBreak = note.isBreak;
        touch.isFirework = note.isHanabi;
        touch.isMine = note.isMineHead;
        if (isEach)
            touchMembers.Add(touch);
        touch.speed = touchSpeed * GetHSpeedAt(
            "touch", timing.time, timing.HSpeed, note.isBreak, stateIsEach,
            timing.streamIndex, note.isMineHead);
        touch.scrollType = ResolveSvType(
            "touch", note.isBreak, stateIsEach, timing.streamIndex, note.isMineHead);
        touch.noteScrollPos = SvController.GetCumulativeScroll(
            timing.time, touch.scrollType);
        touch.GroupInfo = null;
        touch.colorOverrideMaterial = note.isMineHead
            ? MineMaterial(
                timing.time, timing.streamIndex,
                GetAlphaAt("touch", timing.time, timing.streamIndex,
                    note.isBreak, stateIsEach, isMine: true))
            : CreateTintMaterial(
                GetColorAt("touch", timing.time, timing.streamIndex,
                    note.isBreak, stateIsEach),
                GetAlphaAt("touch", timing.time, timing.streamIndex,
                    note.isBreak, stateIsEach));
        var touchSize = GetSizeAt(
            "touch", timing.time, note.isBreak, stateIsEach, timing.streamIndex,
            note.isMineHead);
        touch.noteScaleX = touchSize.x;
        touch.noteScaleY = touchSize.y;
    }

    private GameObject InstantiateWifi(SimaiTimingPoint timing, SimaiNote note)
    {
        var visualIsEach = BeatIsEach(timing);
        var stateIsEach = timing.isEachInStream ?? visualIsEach;
        var wifiSegment = ResolveSlidePath(note)[0];
        var startPos = wifiSegment.startPosition;

        var GOnote = Instantiate(starPrefab, notes.transform);
        var NDCompo = PrepareNote<StarDrop>(GOnote, note, note.isFakeHead);
        NDCompo.isEachInStream = stateIsEach;
        AddJudgeNote(GOnote, note, note.isFakeHead);


        // Note layer order
        NDCompo.noteSortOrder = noteSortOrder;
        noteSortOrder -= NOTE_LAYER_COUNT[note.noteType];

        NDCompo.tapSpr = customSkin.Star;
        NDCompo.eachSpr = customSkin.Star_Each;
        NDCompo.breakSpr = customSkin.Star_Break;
        NDCompo.exSpr = customSkin.Star_Ex;

        NDCompo.tapSpr_Double = customSkin.Star_Double;
        NDCompo.eachSpr_Double = customSkin.Star_Each_Double;
        NDCompo.breakSpr_Double = customSkin.Star_Break_Double;
        NDCompo.exSpr_Double = customSkin.Star_Ex_Double;
        if (note.isMineHead)
        {
            NDCompo.eachSpr = customSkin.Star;
            NDCompo.breakSpr = customSkin.Star;
            NDCompo.eachSpr_Double = customSkin.Star_Double;
            NDCompo.breakSpr_Double = customSkin.Star_Double;
        }

        NDCompo.BreakShine = BreakShine;

        // StarDrop divides by this every frame. A zero-length slide made it
        // divide by zero, and the infinity that comes back writes NaN into the
        // transform: the star stops travelling and stops retiring, so it sits on
        // the playfield. The touch-slide head has always clamped the same value.
        NDCompo.rotateSpeed = Mathf.Max(0.01f, (float)note.slideTime);
        NDCompo.isEX = note.isEx;
        NDCompo.isBreak = note.isBreak;
        NDCompo.isFirework = note.isHanabi;
        NDCompo.isMine = note.isMineHead;
        NDCompo.isDZone = note.isDZone;

        var slideWifi = Instantiate(slidePrefab[SLIDE_PREFAB_MAP["wifi"]], notes.transform);
        slideWifi.SetActive(false);
        NDCompo.slide = slideWifi;
        var WifiCompo = PrepareNote<WifiDrop>(slideWifi, note, note.isFakeSlide);
        WifiCompo.isEachInStream = stateIsEach;

        WifiCompo.normalStar = customSkin.Star;
        WifiCompo.eachStar = customSkin.Star_Each;
        WifiCompo.breakStar = customSkin.Star_Break;
        if (note.isMineSlide)
        {
            WifiCompo.eachStar = customSkin.Star;
            WifiCompo.breakStar = customSkin.Star;
        }
        WifiCompo.judgeBreakShine = JudgeBreakShine;
        WifiCompo.breakMaterial = breakMaterial;
        WifiCompo.slideShine = BreakShine;
        WifiCompo.areaStep = new List<int>(SLIDE_AREA_STEP_MAP["wifi"]);
        WifiCompo.judgeQueues = new(WIFISLIDE_JUDGE_QUEUE[startPos]);
        WifiCompo.slideConst = SLIDE_AREA_CONST["wifi"];
        WifiCompo.smoothSlideAnime = smoothSlideAnime;
        WifiCompo.starSpeed = note.suppressSlideGuideStarFade ? 1f : starSpeed;
        WifiCompo.isDZone = note.isDZone;
        WifiCompo.isDZoneEnd = note.isDZoneEnd;

        Array.Copy(customSkin.Wifi, WifiCompo.normalSlide, 11);
        Array.Copy(customSkin.Wifi_Each, WifiCompo.eachSlide, 11);
        Array.Copy(customSkin.Wifi_Break, WifiCompo.breakSlide, 11);
        if (note.isMineSlide)
        {
            Array.Copy(customSkin.Wifi, WifiCompo.eachSlide, 11);
            Array.Copy(customSkin.Wifi, WifiCompo.breakSlide, 11);
        }

        if (BeatTrailsAreEach(timing))
            WifiCompo.isEach = true;
        NDCompo.isEach = BeatIsEach(timing);
        NDCompo.isDouble = BeatSlidesFrom(timing, note.startPosition) > 1;

        WifiCompo.isBreak = note.isSlideBreak;
        WifiCompo.isMine = note.isMineSlide;
        WifiCompo.suppressGuideStarFadeIn = note.suppressSlideGuideStarFade;
        WifiCompo.colorOverrideMaterial = GetSlideMaterial(
            note.isSlideBreak, timing.time, note.isMineSlide, timing.streamIndex,
            stateIsEach);
        WifiCompo.guideStarMaterial = GetSlideStarMaterial(
            note.isSlideBreak, stateIsEach, timing.time, note.isMineSlide,
            timing.streamIndex);
        NDCompo.colorOverrideMaterial = GetStarMaterial(
            note.isBreak, stateIsEach, timing.time,
            note.isMineHead, timing.streamIndex);

        NDCompo.isNoHead = note.isSlideNoHead;
        NDCompo.time = (float)timing.time;
        NDCompo.startPosition = note.startPosition;
        NDCompo.speed = noteSpeed * GetHSpeedAt(
            "star", timing.time, timing.HSpeed, note.isBreak, stateIsEach,
            timing.streamIndex, note.isMineHead);
        NDCompo.scrollType = ResolveSvType(
            "star", note.isBreak, stateIsEach, timing.streamIndex, note.isMineHead);
        NDCompo.noteScrollPos = SvController.GetCumulativeScroll(timing.time, NDCompo.scrollType);
        NDCompo.spawnRadius = GetSpawnRadiusAt(
            "star", timing.time, note.isBreak, stateIsEach, timing.streamIndex,
            note.isMineHead);
        NDCompo.spawnMode = GetSpawnModeAt(
            "star", timing.time, note.isBreak, stateIsEach, timing.streamIndex,
            note.isMineHead);
        NDCompo.destroyRadius = GetDestroyRadiusAt(
            "star", timing.time, note.isBreak, stateIsEach, timing.streamIndex,
            note.isMineHead);
        NDCompo.bounceDuration = GetBounceDurationAt(
            "star", timing.time, note.isBreak, stateIsEach, timing.streamIndex,
            note.isMineHead);
        NDCompo.ConfigureBounce(NDCompo.speed / noteSpeed);
        var wifiStarSize = GetSizeAt(
            "star", timing.time, note.isBreak, stateIsEach, timing.streamIndex,
            note.isMineHead);
        NDCompo.noteScaleX = wifiStarSize.x;
        NDCompo.noteScaleY = wifiStarSize.y;

        WifiCompo.isJustR = detectJustType(wifiSegment, out var endPos);
        WifiCompo.endPosition = endPos;
        WifiCompo.speed = noteSpeed * ResolveSlideAppearanceSpeed(
            timing.time, timing.streamIndex);
        WifiCompo.scrollType = ResolveSvType(
            "slide", note.isSlideBreak, stateIsEach, timing.streamIndex,
            note.isMineSlide);
        var wifiSlideSize = GetSizeAt(
            "slide", timing.time, note.isSlideBreak, stateIsEach, timing.streamIndex,
            note.isMineSlide);
        WifiCompo.noteScaleX = wifiSlideSize.x;
        WifiCompo.noteScaleY = wifiSlideSize.y;
        var wifiGuideStarSize = GetSizeAt(
            "slidestar", timing.time, note.isSlideBreak, stateIsEach,
            timing.streamIndex, note.isMineSlide);
        WifiCompo.guideStarScaleX = wifiGuideStarSize.x;
        WifiCompo.guideStarScaleY = wifiGuideStarSize.y;
        WifiCompo.timeStart = (float)timing.time;
        WifiCompo.startPosition = note.startPosition;
        WifiCompo.time = (float)note.slideStartTime;
        WifiCompo.LastFor = (float)note.slideTime;
        WifiCompo.sortIndex = slideLayer;
        slideLayer -= SLIDE_AREA_STEP_MAP["wifi"].Last();
        //slideLayer += 5;

        return slideWifi;
    }

    private GameObject InstantiateStar(SimaiTimingPoint timing, SimaiNote note, ConnSlideInfo info)
    {
        var visualIsEach = BeatIsEach(timing);
        var stateIsEach = timing.isEachInStream ?? visualIsEach;
        var GOnote = Instantiate(starPrefab, notes.transform);
        var NDCompo = PrepareNote<StarDrop>(GOnote, note, note.isFakeHead);
        NDCompo.isEachInStream = stateIsEach;
        if(!note.isSlideNoHead)
            AddJudgeNote(GOnote, note, note.isFakeHead);
        // Note layer order
        NDCompo.noteSortOrder = noteSortOrder;
        noteSortOrder -= NOTE_LAYER_COUNT[note.noteType];

        NDCompo.tapSpr = customSkin.Star;
        NDCompo.eachSpr = customSkin.Star_Each;
        NDCompo.breakSpr = customSkin.Star_Break;
        NDCompo.exSpr = customSkin.Star_Ex;

        NDCompo.tapSpr_Double = customSkin.Star_Double;
        NDCompo.eachSpr_Double = customSkin.Star_Each_Double;
        NDCompo.breakSpr_Double = customSkin.Star_Break_Double;
        NDCompo.exSpr_Double = customSkin.Star_Ex_Double;
        if (note.isMineHead)
        {
            NDCompo.eachSpr = customSkin.Star;
            NDCompo.breakSpr = customSkin.Star;
            NDCompo.eachSpr_Double = customSkin.Star_Double;
            NDCompo.breakSpr_Double = customSkin.Star_Double;
        }

        NDCompo.BreakShine = BreakShine;

        // StarDrop divides by this every frame. A zero-length slide made it
        // divide by zero, and the infinity that comes back writes NaN into the
        // transform: the star stops travelling and stops retiring, so it sits on
        // the playfield. The touch-slide head has always clamped the same value.
        NDCompo.rotateSpeed = Mathf.Max(0.01f, (float)note.slideTime);
        NDCompo.isEX = note.isEx;
        NDCompo.isBreak = note.isBreak;
        NDCompo.isFirework = note.isHanabi;
        NDCompo.isMine = note.isMineHead;
        NDCompo.isDZone = note.isDZone;

        var parsedSegment = ResolveSlidePath(note, info.IsGroupPart)[0];
        var slideShape = DetectShape(parsedSegment);
        var isMirror = false;
        var isReverse = false;
        if (slideShape.StartsWith("-"))
        {
            isMirror = true;
            slideShape = slideShape.Substring(1);
        }
        if (slideShape.StartsWith("r"))
        {
            isReverse = true;
            slideShape = slideShape.Substring(1);
        }
        int slideIndex = SLIDE_PREFAB_MAP[slideShape];

        var slide = Instantiate(slidePrefab[slideIndex], notes.transform);
        var slide_star = Instantiate(star_slidePrefab, notes.transform);
        var slideStarRenderer = slide_star.GetComponent<SpriteRenderer>();
        slideStarRenderer.sprite = customSkin.Star;
        slide_star.SetActive(false);
        slide.SetActive(false);
        NDCompo.slide = slide;
        slide.AddComponent<SlideDrop>();
        var SliCompo = PrepareNote<SlideDrop>(
            slide, note, note.isFakeSlide);
        SliCompo.isEachInStream = stateIsEach;
        SliCompo.isDZone = note.isDZone;
        SliCompo.isDZoneEnd = note.isDZoneEnd;

        SliCompo.slideType = slideShape;
        SliCompo.pathExpression = note.pathExpression;
        SliCompo.pathSegment = parsedSegment;
        SliCompo.spriteNormal = customSkin.Slide;
        SliCompo.spriteEach = customSkin.Slide_Each;
        SliCompo.spriteBreak = customSkin.Slide_Break;
        if (note.isMineSlide)
        {
            SliCompo.spriteEach = customSkin.Slide;
            SliCompo.spriteBreak = customSkin.Slide;
        }
        SliCompo.slideShine = BreakShine;
        SliCompo.breakMaterial = breakMaterial;
        SliCompo.judgeBreakShine = JudgeBreakShine;
        SliCompo.areaStep = new List<int>(SLIDE_AREA_STEP_MAP[slideShape]);
        SliCompo.slideConst = SLIDE_AREA_CONST[slideShape];
        SliCompo.smoothSlideAnime = smoothSlideAnime;
        SliCompo.starSpeed = note.suppressSlideGuideStarFade ? 1f : starSpeed;

        if (BeatTrailsAreEach(timing))
        {
            SliCompo.isEach = true;
            slideStarRenderer.sprite = customSkin.Star_Each;
        }
        NDCompo.isEach = BeatIsEach(timing);
        NDCompo.isDouble = BeatSlidesFrom(timing, note.startPosition) > 1;

        SliCompo.ConnectInfo = info;
        SliCompo.isBreak = note.isSlideBreak;
        SliCompo.isMine = note.isMineSlide;
        SliCompo.suppressGuideStarFadeIn = note.suppressSlideGuideStarFade;
        if (note.isMineSlide)
            slideStarRenderer.sprite = customSkin.Star;
        else if (note.isSlideBreak)
            slideStarRenderer.sprite = customSkin.Star_Break;
        NDCompo.isNoHead = note.isSlideNoHead;
        NDCompo.time = (float)timing.time;
        NDCompo.startPosition = note.startPosition;
        NDCompo.speed = noteSpeed * GetHSpeedAt(
            "star", timing.time, timing.HSpeed, note.isBreak, stateIsEach,
            timing.streamIndex, note.isMineHead);
        NDCompo.scrollType = ResolveSvType(
            "star", note.isBreak, stateIsEach, timing.streamIndex, note.isMineHead);
        NDCompo.noteScrollPos = SvController.GetCumulativeScroll(timing.time, NDCompo.scrollType);
        NDCompo.spawnRadius = GetSpawnRadiusAt(
            "star", timing.time, note.isBreak, stateIsEach, timing.streamIndex,
            note.isMineHead);
        NDCompo.spawnMode = GetSpawnModeAt(
            "star", timing.time, note.isBreak, stateIsEach, timing.streamIndex,
            note.isMineHead);
        NDCompo.destroyRadius = GetDestroyRadiusAt(
            "star", timing.time, note.isBreak, stateIsEach, timing.streamIndex,
            note.isMineHead);

        NDCompo.bounceDuration = GetBounceDurationAt(
            "star", timing.time, note.isBreak, stateIsEach, timing.streamIndex,
            note.isMineHead);
        NDCompo.ConfigureBounce(NDCompo.speed / noteSpeed);
        SliCompo.isMirror = isMirror;
        SliCompo.isJustR = detectJustType(parsedSegment, out var endPos);
        SliCompo.endPosition = endPos;
        SliCompo.isReverse = isReverse;
        if (isReverse)
            SliCompo.rotationPosition = endPos;
        if (slideIndex - 26 > 0 && slideIndex - 26 <= 8)
        {
            // known slide sprite issue
            //    1 2 3 4 5 6 7 8
            // p  X X X X X X O O
            // q  X O O X X X X X
            var pqEndPos = slideIndex - 26;
            SliCompo.isSpecialFlip = isMirror == (pqEndPos == 7 || pqEndPos == 8);
        }
        else
        {
            SliCompo.isSpecialFlip = isMirror;
        }
        SliCompo.speed = noteSpeed * ResolveSlideAppearanceSpeed(
            timing.time, timing.streamIndex);
        SliCompo.scrollType = ResolveSvType(
            "slide", note.isSlideBreak, stateIsEach, timing.streamIndex,
            note.isMineSlide);
        var slideSize = GetSizeAt(
            "slide", timing.time, note.isSlideBreak, stateIsEach, timing.streamIndex,
            note.isMineSlide);
        SliCompo.noteScaleX = slideSize.x;
        SliCompo.noteScaleY = slideSize.y;
        SliCompo.timeStart = (float)timing.time;
        SliCompo.startPosition = note.startPosition;
        SliCompo.star_slide = slide_star;
        SliCompo.time = (float)note.slideStartTime;
        SliCompo.LastFor = (float)note.slideTime;
        //SliCompo.sortIndex = -7000 + (int)((lastNoteTime - timing.time) * -100) + sort * 5;
        SliCompo.sortIndex = slideLayer;
        slideLayer -= SLIDE_AREA_STEP_MAP[slideShape].Last();
        var slideMat = GetSlideMaterial(
            note.isSlideBreak, timing.time, note.isMineSlide, timing.streamIndex,
            stateIsEach);
        SliCompo.colorOverrideMaterial = slideMat;
        if (slideMat != null) SliCompo.noteTintColor = slideMat.GetColor("_NoteColor");
        SliCompo.guideStarMaterial = GetSlideStarMaterial(
            note.isSlideBreak, stateIsEach, timing.time, note.isMineSlide,
            timing.streamIndex);
        var guideStarSize = GetSizeAt(
            "slidestar",
            timing.time, note.isSlideBreak, stateIsEach,
            timing.streamIndex, note.isMineSlide);
        SliCompo.guideStarScaleX = guideStarSize.x;
        SliCompo.guideStarScaleY = guideStarSize.y;
        var starMat = GetStarMaterial(
            note.isBreak, stateIsEach, timing.time,
            note.isMineHead, timing.streamIndex);
        NDCompo.colorOverrideMaterial = starMat;
        if (starMat != null) NDCompo.noteTintColor = starMat.GetColor("_NoteColor");
        var starSize = GetSizeAt(
            "star", timing.time, note.isBreak, stateIsEach, timing.streamIndex,
            note.isMineHead);
        NDCompo.noteScaleX = starSize.x;
        NDCompo.noteScaleY = starSize.y;
        //slideLayer += 5;
        return slide;
    }

    private string GetTrajectoryCarrierVisualType(SimaiNote note)
    {
        if (note.isForceStar)
            return "star";
        return note.trajectoryCarrierType switch
        {
            SimaiNoteType.Hold => "hold",
            SimaiNoteType.Touch => "touch",
            SimaiNoteType.TouchHold => "touchhold",
            _ => "tap"
        };
    }

    private Sprite GetTrajectoryCarrierSprite(SimaiNote note, bool isEach)
    {
        if (note.isForceStar)
        {
            if (note.isBreak)
                return customSkin.Star_Break;
            return isEach ? customSkin.Star_Each : customSkin.Star;
        }
        if (note.trajectoryCarrierType == SimaiNoteType.Hold)
        {
            if (note.isBreak)
                return customSkin.Hold_Break;
            return isEach ? customSkin.Hold_Each : customSkin.Hold;
        }
        if (note.trajectoryCarrierType is
            SimaiNoteType.Touch or SimaiNoteType.TouchHold)
        {
            if (note.isBreak)
                return customSkin.Touch_Break;
            return isEach ? customSkin.Touch_Each : customSkin.Touch;
        }
        if (note.isBreak)
            return customSkin.Tap_Break;
        return isEach ? customSkin.Tap_Each : customSkin.Tap;
    }

    private Material GetTrajectoryCarrierMaterial(
        SimaiNote note,
        bool isEach,
        double time,
        int streamIndex)
    {
        var type = GetTrajectoryCarrierVisualType(note);
        if (type == "star")
            return GetStarMaterial(
                note.isBreak, isEach, time, note.isMineHead, streamIndex);
        if (type == "hold")
            return GetHoldMaterial(
                note.isBreak, isEach, time, note.isMineHead, streamIndex);
        if (type == "tap")
            return GetTapMaterial(
                note.isBreak, isEach, time, note.isMineHead, streamIndex);
        if (note.isMineHead)
            return MineMaterial(
                time, streamIndex,
                GetAlphaAt(type, time, streamIndex,
                    note.isBreak, isEach, isMine: true));
        return CreateTintMaterial(
            GetColorAt(type, time, streamIndex, note.isBreak, isEach),
            GetAlphaAt(type, time, streamIndex, note.isBreak, isEach));
    }

    // Guide-star facing comes from the parsed segment: reading the text again broke
    // on D-zone heads and on turns, because it assumed fixed character offsets.
    private bool detectJustType(SlidePathSegmentData segment, out int endPos)
    {
        SlideShapeResolver.TryGetJustDirection(segment, out endPos, out var isRight);
        return isRight;
    }

    private string DetectShape(SlidePathSegmentData segment)
    {
        if (!SlideShapeResolver.TryResolve(segment, out var prefabKey, out _, out var error))
            throw new Exception(error);
        return prefabKey;
    }

    public bool TryGetSlideRoute(
        string content,
        out List<SensorType> path,
        out List<Vector3> positions)
    {
        path = new List<SensorType>();
        positions = new List<Vector3>();
        try
        {
            if (!TryGetSlideVisualRoute(content, out positions))
                return false;
            var sensorRoot = GameObject.Find("Sensors")?.transform;
            if (sensorRoot == null)
                return false;

            Sensor lastSensor = null;
            foreach (var position in positions)
            {
                for (var sensorIndex = 0; sensorIndex < sensorRoot.childCount; sensorIndex++)
                {
                    var rect = sensorRoot.GetChild(sensorIndex).GetComponent<RectTransform>();
                    var sensor = rect?.GetComponent<Sensor>();
                    if (sensor == null || sensor.Group is SensorGroup.D or SensorGroup.E)
                        continue;
                    var radius = Math.Max(rect.rect.width * rect.lossyScale.x,
                                          rect.rect.height * rect.lossyScale.y) / 2f;
                    if ((position - rect.position).sqrMagnitude > radius * radius)
                        continue;
                    if (sensor != lastSensor)
                    {
                        path.Add(sensor.Type);
                        lastSensor = sensor;
                    }
                    break;
                }
            }

            return path.Count > 0;
        }
        catch
        {
            path.Clear();
            positions.Clear();
            return false;
        }
    }

    public bool TryGetSlideVisualRoute(string content, out List<Vector3> positions)
    {
        positions = new List<Vector3>();
        try
        {
            if (!SlidePathParser.TryParsePath(content, out var path) ||
                path.segments.Count != 1)
                return false;
            var parsed = path.segments[0];
            var shape = DetectShape(parsed);
            var isMirror = shape.StartsWith("-", StringComparison.Ordinal);
            if (isMirror)
                shape = shape.Substring(1);
            var isReverse = shape.StartsWith("r", StringComparison.Ordinal);
            if (isReverse)
                shape = shape.Substring(1);
            if (!SLIDE_PREFAB_MAP.TryGetValue(shape, out var prefabIndex))
                return false;

            var rotationPosition = isReverse
                ? parsed.endPosition
                : parsed.startPosition;
            var rotation = isMirror
                ? Quaternion.Euler(0f, 0f, -45f * rotationPosition)
                : Quaternion.Euler(0f, 0f, -45f * (rotationPosition - 1));
            var scale = isMirror ? new Vector3(-1f, 1f, 1f) : Vector3.one;
            var prefab = slidePrefab[prefabIndex].transform;
            for (var barIndex = 0; barIndex < prefab.childCount - 1; barIndex++)
            {
                var local = Vector3.Scale(prefab.GetChild(barIndex).localPosition, scale);
                positions.Add(rotation * local);
            }

            if (isReverse)
                positions.Reverse();
            return positions.Count > 0;
        }
        catch
        {
            positions.Clear();
            return false;
        }
    }

    private bool isUpperHalf(int key)
    {
        if (key == 7) return true;
        if (key == 8) return true;
        if (key == 1) return true;
        if (key == 2) return true;

        return false;
    }

    private bool isRightHalf(int key)
    {
        if (key == 1) return true;
        if (key == 2) return true;
        if (key == 3) return true;
        if (key == 4) return true;

        return false;
    }

    private static string StateKey(int streamIndex, string noteType) =>
        streamIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
        NormalizeNoteType(noteType);

    private static string VisualStateKey(int streamIndex, string noteType, bool live) =>
        StateKey(streamIndex, noteType) + (live ? ":live" : ":note");

    private readonly Dictionary<string, List<(double time, ColorChange value)>> _colorTimeline = new();

    private void BuildColorTimeline(List<ColorChange> colorTable)
    {
        _colorTimeline.Clear();
        if (colorTable == null) return;
        foreach (var ev in colorTable)
        {
            var key = VisualStateKey(ev.streamIndex, ev.noteType, ev.live);
            if (!_colorTimeline.ContainsKey(key))
                _colorTimeline[key] = new List<(double, ColorChange)>();
            _colorTimeline[key].Add((ev.time, ev));
        }
        foreach (var kv in _colorTimeline)
            kv.Value.Sort((a, b) => a.time != b.time
                ? a.time.CompareTo(b.time)
                : a.value.sourcePosition.CompareTo(b.value.sourcePosition));
    }

    /// Returns the hex color string active for <paramref name="noteType"/> at <paramref name="time"/>,
    /// or null if no override is defined yet.
    private string GetColorAt(
        string noteType,
        double noteTime,
        int streamIndex = 0,
        bool isBreak = false,
        bool isEach = false,
        double? liveTime = null,
        bool liveOnly = false)
    {
        ColorChange Lookup(string type, bool live, double time)
        {
            if (!_colorTimeline.TryGetValue(VisualStateKey(streamIndex, type, live), out var values))
                return null;
            var index = FindTimelineIndex(values, time);
            return index >= 0 ? values[index].value : null;
        }

        string ResolveTyped(bool live, double time)
        {
            // Mines are not here on purpose: they take their colour from the mine key
            // alone, through GetMineColorAt, so that colouring taps leaves the mines
            // among them alone.
            IEnumerable<string> Types()
            {
                if (isBreak) yield return "break";
                if (isEach) yield return "each";
                yield return noteType;
                if (string.Equals(noteType, "star", StringComparison.OrdinalIgnoreCase))
                    yield return "tap";
            }

            foreach (var type in Types().Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var value = Lookup(type, live, time);
                if (value != null && !string.Equals(value.color, "NULL", StringComparison.OrdinalIgnoreCase))
                    return value.color;
            }
            return null;
        }

        string ResolveGlobal(bool live, double time)
        {
            var value = Lookup(string.Empty, live, time);
            return value == null || string.Equals(value.color, "NULL", StringComparison.OrdinalIgnoreCase)
                ? null
                : value.color;
        }

        // The live answer on its own, for the override layer: what the note is
        // painted with when it is built already carries every non-live COLOR, so
        // repeating those here would let the override layer restate a value the
        // note already has - and, worse, keep restating it after a COLORV*NULL
        // asked for the note's own colour back.
        if (liveOnly)
            return liveTime.HasValue
                ? ResolveTyped(true, liveTime.Value) ??
                  ResolveGlobal(true, liveTime.Value)
                : null;
        return (liveTime.HasValue ? ResolveTyped(true, liveTime.Value) : null) ??
               ResolveTyped(false, noteTime) ??
               (liveTime.HasValue ? ResolveGlobal(true, liveTime.Value) : null) ??
               ResolveGlobal(false, noteTime);
    }

    private readonly Dictionary<string, List<(double time, SizeChange value)>> _sizeTimeline = new();

    private void BuildSizeTimeline(List<SizeChange> sizeTable)
    {
        _sizeTimeline.Clear();
        if (sizeTable == null) return;
        foreach (var ev in sizeTable)
        {
            string key = VisualStateKey(ev.streamIndex, ev.noteType, ev.live);
            if (!_sizeTimeline.ContainsKey(key))
                _sizeTimeline[key] = new List<(double, SizeChange)>();
            _sizeTimeline[key].Add((ev.time, ev));
        }
        foreach (var kv in _sizeTimeline)
            kv.Value.Sort((a, b) => a.time != b.time
                ? a.time.CompareTo(b.time)
                : a.value.sourcePosition.CompareTo(b.value.sourcePosition));
    }

    /// Returns the scale multiplier active for <paramref name="noteType"/> at
    /// <paramref name="time"/> (per-type over global, default 1.0).
    private Vector2? ResolveSizeAt(
        string noteType,
        double noteTime,
        bool isBreak = false,
        bool isEach = false,
        int streamIndex = 0,
        double? liveTime = null,
        bool isMine = false,
        bool liveOnly = false)
    {
        Vector2? Lookup(string type, bool live, double time)
        {
            if (!_sizeTimeline.TryGetValue(VisualStateKey(streamIndex, type, live), out var list))
                return null;
            var index = FindTimelineIndex(list, time);
            if (index < 0) return null;
            var value = list[index].value;
            if (value.reset) return null;
            var x = value.scaleX == 0f ? value.scale : value.scaleX;
            var y = value.scaleY == 0f ? value.scale : value.scaleY;
            return new Vector2(x == 0f ? 1f : x, y == 0f ? 1f : y);
        }
        Vector2? ResolveTyped(bool live, double time)
        {
            if (isMine)
            {
                var value = Lookup("mine", live, time);
                if (value.HasValue) return value;
            }
            if (isBreak)
            {
                var value = Lookup("break", live, time);
                if (value.HasValue) return value;
            }
            if (isEach)
            {
                var value = Lookup("each", live, time);
                if (value.HasValue) return value;
            }
            return Lookup(noteType, live, time);
        }

        var value = (liveTime.HasValue ? ResolveTyped(true, liveTime.Value) : null) ??
                    (liveOnly ? null : ResolveTyped(false, noteTime));
        if (value.HasValue)
            return value;

        // Global SIZE deliberately excludes Slide paths; SIZE*slide is explicit.
        if (string.Equals(noteType, "slide", StringComparison.OrdinalIgnoreCase))
            return null;

        return (liveTime.HasValue ? Lookup(string.Empty, true, liveTime.Value) : null) ??
               (liveOnly ? null : Lookup(string.Empty, false, noteTime));
    }

    private Vector2 GetSizeAt(
        string noteType,
        double time,
        bool isBreak = false,
        bool isEach = false,
        int streamIndex = 0,
        bool isMine = false) =>
        ResolveSizeAt(noteType, time, isBreak, isEach, streamIndex, isMine: isMine) ??
        Vector2.one;

    private readonly Dictionary<string, List<(double time, SpeedChange value)>> _hSpeedTimeline = new();

    private Dictionary<string, List<(double time, SpawnChange value)>> _spawnTimeline = new();
    private readonly Dictionary<string, List<(double time, SpawnModeChange value)>>
        _spawnModeTimeline = new();

    private readonly Dictionary<string, List<(double time, BounceChange value)>> _bounceTimeline = new();
    private readonly Dictionary<string, List<(double time, DestroyChange value)>> _destroyTimeline = new();

    private void BuildDestroyTimeline(List<DestroyChange> changes)
    {
        _destroyTimeline.Clear();
        if (changes == null)
            return;
        foreach (var change in changes)
        {
            var key = StateKey(change.streamIndex, change.noteType);
            if (!_destroyTimeline.TryGetValue(key, out var values))
                _destroyTimeline[key] = values = new List<(double, DestroyChange)>();
            values.Add((change.time, change));
        }
        foreach (var values in _destroyTimeline.Values)
            values.Sort((left, right) => left.time != right.time
                ? left.time.CompareTo(right.time)
                : left.value.sourcePosition.CompareTo(right.value.sourcePosition));
    }

    private float GetDestroyRadiusAt(
        string noteType,
        double time,
        bool isBreak,
        bool isEach,
        int streamIndex,
        bool isMine = false)
    {
        float? Lookup(string type)
        {
            if (!_destroyTimeline.TryGetValue(StateKey(streamIndex, type), out var values))
                return null;
            var index = FindTimelineIndex(values, time);
            if (index < 0 || values[index].value.reset)
                return null;
            return values[index].value.radius;
        }

        float? typed = null;
        if (isMine)
            typed = Lookup("mine");
        if (!typed.HasValue && isBreak)
            typed = Lookup("break");
        if (!typed.HasValue && isEach)
            typed = Lookup("each");
        return typed ?? Lookup(noteType) ?? Lookup(string.Empty) ??
               NoteDrop.DefaultDestroyRadius;
    }

    private float ResolveSlideAppearanceSpeed(double time, int streamIndex)
    {
        if (_hSpeedTimeline.TryGetValue(
                StateKey(streamIndex, "slide"), out var values))
        {
            var index = FindTimelineIndex(values, time);
            if (index >= 0 && !values[index].value.reset)
                return values[index].value.multiplier;
        }
        return 1f;
    }

    private void BuildBounceTimeline(List<BounceChange> changes)
    {
        _bounceTimeline.Clear();
        if (changes == null)
            return;
        foreach (var change in changes)
        {
            var key = StateKey(change.streamIndex, change.noteType);
            if (!_bounceTimeline.TryGetValue(key, out var values))
                _bounceTimeline[key] = values = new List<(double, BounceChange)>();
            values.Add((change.time, change));
        }
        foreach (var values in _bounceTimeline.Values)
            values.Sort((left, right) =>
            {
                var byTime = left.time.CompareTo(right.time);
                return byTime != 0
                    ? byTime
                    : left.value.sourcePosition.CompareTo(right.value.sourcePosition);
            });
    }

    private float GetBounceDurationAt(
        string noteType,
        double time,
        bool isBreak,
        bool isEach,
        int streamIndex = 0,
        bool isMine = false)
    {
        BounceChange Lookup(int stream, string type)
        {
            if (!_bounceTimeline.TryGetValue(StateKey(stream, type), out var values))
                return null;
            var index = FindTimelineIndex(values, time);
            return index >= 0 ? values[index].value : null;
        }
        float? Resolve(int stream)
        {
            float? Value(BounceChange value) =>
                value == null || value.reset ? null : Math.Max(0f, value.duration);
            if (isMine)
            {
                var value = Value(Lookup(stream, "mine"));
                if (value.HasValue) return value;
            }
            if (isBreak)
            {
                var value = Value(Lookup(stream, "break"));
                if (value.HasValue) return value;
            }
            if (isEach)
            {
                var value = Value(Lookup(stream, "each"));
                if (value.HasValue) return value;
            }
            return Value(Lookup(stream, noteType)) ?? Value(Lookup(stream, ""));
        }
        return Resolve(streamIndex) ?? 0f;
    }

    private void BuildSpawnTimeline(List<SpawnChange> changes)
    {
        _spawnTimeline.Clear();
        if (changes == null) return;
        foreach (var change in changes)
        {
            var key = StateKey(change.streamIndex, change.noteType);
            if (!_spawnTimeline.TryGetValue(key, out var list))
                _spawnTimeline[key] = list = new List<(double, SpawnChange)>();
            list.Add((change.time, change));
        }
        foreach (var list in _spawnTimeline.Values)
            list.Sort((left, right) =>
            {
                var byTime = left.time.CompareTo(right.time);
                return byTime != 0
                    ? byTime
                    : left.value.sourcePosition.CompareTo(right.value.sourcePosition);
            });
    }

    private float GetSpawnRadiusAt(
        string noteType,
        double time,
        bool isBreak = false,
        bool isEach = false,
        int streamIndex = 0,
        bool isMine = false)
    {
        SpawnChange Lookup(int stream, string type)
        {
            if (!_spawnTimeline.TryGetValue(StateKey(stream, type), out var list)) return null;
            var index = FindTimelineIndex(list, time);
            return index >= 0 ? list[index].value : null;
        }
        float? Resolve(int stream)
        {
            float? Value(SpawnChange value) => value == null || value.reset ? null : value.radius;
            if (isMine)
            {
                var value = Value(Lookup(stream, "mine"));
                if (value.HasValue) return value;
            }
            if (isBreak)
            {
                var value = Value(Lookup(stream, "break"));
                if (value.HasValue) return value;
            }
            if (isEach)
            {
                var value = Value(Lookup(stream, "each"));
                if (value.HasValue) return value;
            }
            return Value(Lookup(stream, noteType)) ?? Value(Lookup(stream, ""));
        }
        return Resolve(streamIndex) ?? NoteDrop.DefaultSpawnRadius;
    }

    private void BuildSpawnModeTimeline(List<SpawnModeChange> changes)
    {
        _spawnModeTimeline.Clear();
        if (changes == null)
            return;
        foreach (var change in changes)
        {
            var key = StateKey(change.streamIndex, change.noteType);
            if (!_spawnModeTimeline.TryGetValue(key, out var list))
                _spawnModeTimeline[key] =
                    list = new List<(double, SpawnModeChange)>();
            list.Add((change.time, change));
        }
        foreach (var list in _spawnModeTimeline.Values)
            list.Sort((left, right) => left.time != right.time
                ? left.time.CompareTo(right.time)
                : left.value.sourcePosition.CompareTo(right.value.sourcePosition));
    }

    private SpawnVisualMode GetSpawnModeAt(
        string noteType,
        double time,
        bool isBreak = false,
        bool isEach = false,
        int streamIndex = 0,
        bool isMine = false)
    {
        SpawnModeChange Lookup(string type)
        {
            if (!_spawnModeTimeline.TryGetValue(
                    StateKey(streamIndex, type), out var values))
                return null;
            var index = FindTimelineIndex(values, time);
            return index >= 0 ? values[index].value : null;
        }

        SpawnVisualMode? Value(SpawnModeChange value) =>
            value == null || value.reset ? null : value.mode;

        if (isMine && Value(Lookup("mine")) is { } mineMode)
            return mineMode;
        if (isBreak && Value(Lookup("break")) is { } breakMode)
            return breakMode;
        if (isEach && Value(Lookup("each")) is { } eachMode)
            return eachMode;
        return Value(Lookup(noteType)) ??
               Value(Lookup(string.Empty)) ??
               SpawnVisualMode.Rewind;
    }

    private void BuildHSpeedTimeline(List<SpeedChange> changes)
    {
        _hSpeedTimeline.Clear();
        if (changes == null) return;
        foreach (var change in changes)
        {
            var key = StateKey(change.streamIndex, change.noteType);
            if (!_hSpeedTimeline.TryGetValue(key, out var list))
                _hSpeedTimeline[key] = list = new List<(double, SpeedChange)>();
            list.Add((change.time, change));
        }
        foreach (var list in _hSpeedTimeline.Values)
            list.Sort((left, right) => left.time != right.time
                ? left.time.CompareTo(right.time)
                : left.value.sourcePosition.CompareTo(right.value.sourcePosition));
    }

    private float GetHSpeedAt(
        string noteType,
        double time,
        float fallback,
        bool isBreak = false,
        bool isEach = false,
        int streamIndex = 0,
        bool isMine = false)
    {
        float? Lookup(int stream, string type)
        {
            if (!_hSpeedTimeline.TryGetValue(StateKey(stream, type), out var values))
                return null;
            var index = FindTimelineIndex(values, time);
            if (index < 0 || values[index].value.reset) return null;
            return values[index].value.multiplier;
        }
        float? Resolve(int stream)
        {
            if (isMine)
            {
                var value = Lookup(stream, "mine");
                if (value.HasValue) return value;
            }
            if (isBreak)
            {
                var value = Lookup(stream, "break");
                if (value.HasValue) return value;
            }
            if (isEach)
            {
                var value = Lookup(stream, "each");
                if (value.HasValue) return value;
            }
            return Lookup(stream, noteType) ?? Lookup(stream, "");
        }
        return Resolve(streamIndex) ?? fallback;
    }

    private static string ResolveSvType(
        string baseType, bool isBreak, bool isEach, int streamIndex, bool isMine = false)
    {
        var mineType = SvController.MakeCurveKey(streamIndex, "mine");
        if (isMine && SvController.HasTypedCurve(mineType))
            return mineType;
        var breakType = SvController.MakeCurveKey(streamIndex, "break");
        if (isBreak && SvController.HasTypedCurve(breakType))
            return breakType;
        var eachType = SvController.MakeCurveKey(streamIndex, "each");
        if (isEach && SvController.HasTypedCurve(eachType))
            return eachType;
        return SvController.MakeCurveKey(streamIndex, baseType);
    }

    private static int FindTimelineIndex<T>(List<(double time, T value)> timeline, double time)
    {
        var low = 0;
        var high = timeline.Count - 1;
        var result = -1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            if (timeline[middle].time <= time)
            {
                result = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }
        return result;
    }

    private readonly Dictionary<string, List<(double time, AlphaChange value)>> _alphaTimeline = new();

    private void BuildAlphaTimeline(List<AlphaChange> alphaTable)
    {
        _alphaTimeline.Clear();
        if (alphaTable == null) return;
        foreach (var ev in alphaTable)
        {
            string key = VisualStateKey(ev.streamIndex, ev.noteType, ev.live);
            if (!_alphaTimeline.ContainsKey(key))
                _alphaTimeline[key] = new List<(double, AlphaChange)>();
            _alphaTimeline[key].Add((ev.time, ev));
        }
        foreach (var kv in _alphaTimeline)
            kv.Value.Sort((a, b) => a.time != b.time
                ? a.time.CompareTo(b.time)
                : a.value.sourcePosition.CompareTo(b.value.sourcePosition));
    }

    private float? ResolveAlphaAt(
        string noteType,
        double noteTime,
        int streamIndex = 0,
        bool isBreak = false,
        bool isEach = false,
        double? liveTime = null,
        bool isMine = false,
        bool liveOnly = false)
    {
        float? Lookup(string type, bool live, double time)
        {
            if (!_alphaTimeline.TryGetValue(VisualStateKey(streamIndex, type, live), out var list))
                return null;
            var index = FindTimelineIndex(list, time);
            if (index < 0 || list[index].value.reset) return null;
            return list[index].value.alpha;
        }

        float? ResolveTyped(bool live, double time)
        {
            if (isMine)
            {
                var value = Lookup("mine", live, time);
                if (value.HasValue) return value;
            }
            if (isBreak)
            {
                var value = Lookup("break", live, time);
                if (value.HasValue) return value;
            }
            if (isEach)
            {
                var value = Lookup("each", live, time);
                if (value.HasValue) return value;
            }
            return Lookup(noteType, live, time);
        }

        if (liveOnly)
            return liveTime.HasValue
                ? ResolveTyped(true, liveTime.Value) ??
                  Lookup(string.Empty, true, liveTime.Value)
                : null;
        return (liveTime.HasValue ? ResolveTyped(true, liveTime.Value) : null) ??
               ResolveTyped(false, noteTime) ??
               (liveTime.HasValue ? Lookup(string.Empty, true, liveTime.Value) : null) ??
               Lookup(string.Empty, false, noteTime);
    }

    private float GetAlphaAt(
        string noteType,
        double time,
        int streamIndex = 0,
        bool isBreak = false,
        bool isEach = false,
        bool isMine = false) =>
        ResolveAlphaAt(noteType, time, streamIndex, isBreak, isEach, isMine: isMine) ?? 1f;

    // A mine answers to a mine colour and to nothing else, live or not: it ignores the
    // note's own type, break, each and the global colour exactly as it always has, so
    // that a chart colouring taps does not accidentally colour the mines among them.
    // COLORV/SIZEV/ALPHAV are an override layer over what the note was painted
    // with, so these read the live commands and nothing else. Type precedence,
    // break/each, the star's fall back to tap and the mine's refusal to answer to
    // anything but "mine" are all the same rules COLOR/SIZE/ALPHA go through -
    // that is the point of asking the same resolver a different question.
    internal string ResolveLiveColor(NoteDrop note, double liveTime) =>
        note.IsVisualMine
            ? GetMineColorAt(note.VisualStateTime, note.VisualStreamIndex, liveTime,
                liveOnly: true)
            : GetColorAt(note.VisualNoteType, note.VisualStateTime, note.VisualStreamIndex,
                note.IsVisualBreak, note.isEachInStream, liveTime, liveOnly: true);

    internal Vector2? ResolveLiveSize(NoteDrop note, double liveTime) =>
        ResolveSizeAt(note.VisualNoteType, note.VisualStateTime, note.IsVisualBreak,
            note.isEachInStream, note.VisualStreamIndex, liveTime, note.IsVisualMine,
            liveOnly: true);

    internal float? ResolveLiveAlpha(NoteDrop note, double liveTime) =>
        ResolveAlphaAt(note.VisualNoteType, note.VisualStateTime, note.VisualStreamIndex,
            note.IsVisualBreak, note.isEachInStream, liveTime, note.IsVisualMine,
            liveOnly: true);

    internal string ResolveLiveGuideStarColor(NoteDrop note, double liveTime) =>
        note.IsVisualMine
            ? GetMineColorAt(note.VisualStateTime, note.VisualStreamIndex, liveTime,
                liveOnly: true)
            : GetColorAt(note.LiveGuideStarVisualType, note.VisualStateTime, note.VisualStreamIndex,
                note.IsVisualBreak, note.isEachInStream, liveTime, liveOnly: true);

    internal Vector2? ResolveLiveGuideStarSize(NoteDrop note, double liveTime) =>
        ResolveSizeAt(note.LiveGuideStarVisualType, note.VisualStateTime, note.IsVisualBreak,
            note.isEachInStream, note.VisualStreamIndex, liveTime, note.IsVisualMine,
            liveOnly: true);

    internal float? ResolveLiveGuideStarAlpha(NoteDrop note, double liveTime) =>
        ResolveAlphaAt(note.LiveGuideStarVisualType, note.VisualStateTime, note.VisualStreamIndex,
            note.IsVisualBreak, note.isEachInStream, liveTime, note.IsVisualMine,
            liveOnly: true);

    private static Shader _tintShader;
    private static bool renderingMaterialsWarmed;
    private readonly Dictionary<string, Material> _tintMaterialCache = new();

    public void WarmupRenderingMaterials()
    {
        if (renderingMaterialsWarmed)
            return;

        RenderTexture target = null;
        Material tintWarmup = null;
        Material breakWarmup = null;
        try
        {
            target = RenderTexture.GetTemporary(8, 8, 0, RenderTextureFormat.ARGB32);
            _tintShader ??= Shader.Find("Sprites/NoteColorTint");
            if (_tintShader != null)
            {
                tintWarmup = new Material(_tintShader) { hideFlags = HideFlags.HideAndDontSave };
                Graphics.Blit(Texture2D.whiteTexture, target, tintWarmup);
                tintWarmup.EnableKeyword("PIXELSNAP_ON");
                Graphics.Blit(Texture2D.whiteTexture, target, tintWarmup);
            }

            if (breakMaterial != null)
            {
                breakWarmup = new Material(breakMaterial) { hideFlags = HideFlags.HideAndDontSave };
                Graphics.Blit(Texture2D.whiteTexture, target, breakWarmup);
            }
            renderingMaterialsWarmed = true;
        }
        catch (System.Exception exception)
        {
            UnityEngine.Debug.LogWarning("[RenderWarmup] Material warmup failed: " + exception.Message);
        }
        finally
        {
            if (target != null)
                RenderTexture.ReleaseTemporary(target);
            if (tintWarmup != null)
                Destroy(tintWarmup);
            if (breakWarmup != null)
                Destroy(breakWarmup);
        }
    }

    private void ClearTintMaterialCache()
    {
        foreach (var material in _tintMaterialCache.Values)
        {
            if (material == null)
                continue;
            if (Application.isPlaying)
                Destroy(material);
            else
                DestroyImmediate(material);
        }
        _tintMaterialCache.Clear();
    }

    /// Creates a cached tint material while preserving the source texture shading.
    private Material CreateTintMaterial(
        string hex,
        float alpha = 1f,
        float srcHue = -1f,
        bool allowCache = true,
        bool grayscale = false,
        float tintCoverage = 0f)
    {
        if (string.IsNullOrEmpty(hex) && alpha >= 0.9999f && !grayscale) return null;
        if (_tintShader == null) _tintShader = Shader.Find("Sprites/NoteColorTint");
        if (_tintShader == null) { UnityEngine.Debug.LogWarning("[NoteColor] Shader 'Sprites/NoteColorTint' not found"); return null; }

        string cacheKey = null;
        if (allowCache)
        {
            cacheKey = $"{hex}|{alpha:0.####}|{srcHue:0.####}|{grayscale}|{tintCoverage:0.####}";
            if (_tintMaterialCache.TryGetValue(cacheKey, out var cached))
                return cached;
        }

        var mat = new Material(_tintShader);

        if (!string.IsNullOrEmpty(hex))
        {
            hex = hex.TrimStart('#');
            float hexAlpha = 1f;
            if (hex.Length == 8)
            {
                if (int.TryParse(hex.Substring(6, 2),
                        System.Globalization.NumberStyles.HexNumber, null, out int ai))
                    hexAlpha = ai / 255f;
                hex = hex.Substring(0, 6);
            }
            if (ColorUtility.TryParseHtmlString("#" + hex, out Color c))
            {
                c.a = 1f;
                mat.SetColor("_NoteColor", c);
            }
            alpha *= hexAlpha;
            if (srcHue >= 0f) mat.SetFloat("_SrcHue", srcHue);
        }

        if (alpha < 0.9999f) mat.SetFloat("_NoteAlpha", alpha);
        if (grayscale) mat.SetFloat("_Grayscale", 1f);
        if (tintCoverage > 0.0001f) mat.SetFloat("_TintCoverage", tintCoverage);
        if (allowCache)
            _tintMaterialCache[cacheKey] = mat;
        return mat;
    }

    private static string NormalizeNoteType(string noteType) =>
        string.IsNullOrWhiteSpace(noteType) ? "" : noteType.Trim().ToLowerInvariant();

    /// <summary>
    /// The colour a chart wrote for mines specifically, or null. A mine is grey by
    /// default, and only a colour aimed at mines may replace that grey: this asks for
    /// the one key and deliberately does not fall through to break, each, the note's
    /// own type or the global colour, all of which mines have always ignored.
    /// </summary>
    private string GetMineColorAt(
        double time,
        int streamIndex,
        double? liveTime = null,
        bool liveOnly = false)
    {
        string Lookup(bool live, double at)
        {
            if (!_colorTimeline.TryGetValue(
                    VisualStateKey(streamIndex, "mine", live), out var values))
                return null;
            var index = FindTimelineIndex(values, at);
            if (index < 0)
                return null;
            var color = values[index].value.color;
            return string.Equals(color, "NULL", StringComparison.OrdinalIgnoreCase)
                ? null
                : color;
        }
        if (liveOnly)
            return liveTime.HasValue ? Lookup(true, liveTime.Value) : null;
        return (liveTime.HasValue ? Lookup(true, liveTime.Value) : null) ??
               Lookup(false, time);
    }

    /// <summary>
    /// A mine's material: grey, unless the chart named a colour for mines, in which
    /// case that colour is drawn as written and the grey is dropped.
    /// </summary>
    private Material MineMaterial(double time, int streamIndex, float alpha)
    {
        var color = GetMineColorAt(time, streamIndex);
        return color == null
            ? CreateTintMaterial(null, alpha, grayscale: true)
            : CreateTintMaterial(color, alpha);
    }

    private Material GetTapMaterial(
        bool isBreak, bool isEach, double time, bool isMine = false,
        int streamIndex = 0)
    {
        if (isMine)
            return MineMaterial(time, streamIndex,
                GetAlphaAt("tap", time, streamIndex, isBreak, isEach, isMine: true));
        return CreateTintMaterial(
            GetColorAt("tap", time, streamIndex, isBreak, isEach),
            GetAlphaAt("tap", time, streamIndex, isBreak, isEach));
    }

    private Material GetHoldMaterial(
        bool isBreak, bool isEach, double time, bool isMine = false,
        int streamIndex = 0)
    {
        if (isMine)
            return MineMaterial(time, streamIndex,
                GetAlphaAt("hold", time, streamIndex, isBreak, isEach, isMine: true));
        return CreateTintMaterial(
            GetColorAt("hold", time, streamIndex, isBreak, isEach),
            GetAlphaAt("hold", time, streamIndex, isBreak, isEach));
    }

    private Material GetSlideMaterial(
        bool isBreak, double time, bool isMine = false, int streamIndex = 0,
        bool isEach = false)
    {
        if (isMine)
            return MineMaterial(time, streamIndex,
                GetAlphaAt("slide", time, streamIndex, isBreak, isEach, isMine: true));
        return CreateTintMaterial(
            GetColorAt("slide", time, streamIndex, isBreak, isEach),
            GetAlphaAt("slide", time, streamIndex, isBreak, isEach));
    }

    /// Star-shaped note visuals: Slide heads and forced-star taps.
    /// Uses the "star" key and falls back to "tap".
    private Material GetStarMaterial(
        bool isBreak, bool isEach, double time, bool isMine = false,
        int streamIndex = 0)
    {
        if (isMine)
            return MineMaterial(time, streamIndex,
                GetAlphaAt("star", time, streamIndex, isBreak, isEach, isMine: true));
        return CreateTintMaterial(
            GetColorAt("star", time, streamIndex, isBreak, isEach),
            GetAlphaAt("star", time, streamIndex, isBreak, isEach));
    }

    /// Moving guide-star visuals used by Slide, Wifi, and TouchSlide.
    private Material GetSlideStarMaterial(
        bool isBreak, bool isEach, double time, bool isMine = false,
        int streamIndex = 0)
    {
        if (isMine)
            return MineMaterial(time, streamIndex,
                GetAlphaAt(
                    "slidestar", time, streamIndex, isBreak, isEach, isMine: true));
        return CreateTintMaterial(
            GetColorAt(
                "slidestar", time, streamIndex, isBreak, isEach),
            GetAlphaAt(
                "slidestar", time, streamIndex, isBreak, isEach));
    }

    private int MirrorKeys(int key)
    {
        if (key == 1) return 1;
        if (key == 2) return 8;
        if (key == 3) return 7;
        if (key == 4) return 6;

        if (key == 5) return 5;
        if (key == 6) return 4;
        if (key == 7) return 3;
        if (key == 8) return 2;
        throw new Exception("Keys out of range: " + key);
    }
}
