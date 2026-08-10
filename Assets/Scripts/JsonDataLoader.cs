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
using Assets.Scripts;

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
    Coroutine noteParserTask = null;
    private int runtimeBindingReadyFrame = -1;
    private const float InitialPlaybackHorizon = 0.5f;
    private int reloadGeneration;
    public bool RuntimeBindingsReady => runtimeBindingReadyFrame < 0;
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
                BuildBounceTimeline(loadedData.bounceTable);
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
            // Playback can begin once the notes around the current timeline position
            // have bound their sensors. Continue streaming the rest of the chart after
            // that point instead of delaying audio until every later note exists.
            if (!previewOnly && runtimeBindingReadyFrame == int.MaxValue &&
                timing.time > ignoreOffset + InitialPlaybackHorizon)
            {
                runtimeBindingReadyFrame = Time.frameCount + 1;
            }

            // Keep loading incremental. A larger negative-time budget blocks
            // the main thread exactly when the cover transition is playing.
            if (sw.ElapsedMilliseconds >= 2)
            {
                yield return 0;
                sw.Restart();
            }
            try
            {
                if (timing.time < ignoreOffset)
                {
                    CountNoteCount(timing.noteList);
                    continue;
                }
                List<TouchDrop> members = new();
                for (var i = 0; i < timing.noteList.Count; i++)
                {
                    var note = timing.noteList[i];
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
                            var _NDCompo = PrepareNote<StarDrop>(GOnote);
                            _NDCompo.tapSpr = customSkin.Star;
                            _NDCompo.eachSpr = customSkin.Star_Each;
                            _NDCompo.breakSpr = customSkin.Star_Break;
                            _NDCompo.exSpr = customSkin.Star_Ex;
                            _NDCompo.tapLine = starLine;
                            _NDCompo.isFakeStarRotate = note.isFakeRotate;
                            _NDCompo.isFakeStar = true;
                            NDCompo = _NDCompo;
                        }
                        else
                        {
                            GOnote = Instantiate(tapPrefab, notes.transform);
                            NDCompo = PrepareNote<TapDrop>(GOnote);
                            // Custom note style
                            NDCompo.tapSpr = customSkin.Tap;
                            NDCompo.breakSpr = customSkin.Tap_Break;
                            NDCompo.eachSpr = customSkin.Tap_Each;
                            NDCompo.exSpr = customSkin.Tap_Ex;
                        }
                        AddJudgeNote(GOnote, note);
                        // Note layer order
                        NDCompo.noteSortOrder = noteSortOrder;
                        noteSortOrder -= NOTE_LAYER_COUNT[note.noteType];

                        NDCompo.BreakShine = BreakShine;

                        if (timing.noteList.Count > 1) NDCompo.isEach = true;
                        NDCompo.isBreak = note.isBreak;
                        NDCompo.isEX = note.isEx;
                        NDCompo.isFirework = note.isHanabi;
                        NDCompo.isDZone = note.isDZone;
                        NDCompo.time = (float)timing.time;
                        NDCompo.startPosition = note.startPosition;
                        var tapType = note.isForceStar ? "star" : "tap";
                        var tapIsEach = timing.noteList.Count > 1;
                        NDCompo.speed = noteSpeed * GetHSpeedAt(
                            tapType, timing.time, timing.HSpeed, note.isBreak, tapIsEach);
                        NDCompo.scrollType = ResolveSvType(tapType, note.isBreak, tapIsEach);
                        NDCompo.noteScrollPos = SvController.GetCumulativeScroll(
                            timing.time, NDCompo.scrollType);
                        NDCompo.spawnRadius = GetSpawnRadiusAt(
                            tapType, timing.time, note.isBreak, tapIsEach);
                        NDCompo.bounceDuration = GetBounceDurationAt(
                            tapType, timing.time, note.isBreak, tapIsEach);
                        var tapMat = note.isForceStar
                            ? GetStarMaterial(note.isBreak, timing.noteList.Count > 1, timing.time, note.isMonoHead)
                            : GetTapMaterial(note.isBreak, timing.noteList.Count > 1, timing.time, note.isMonoHead);
                        NDCompo.colorOverrideMaterial = tapMat;
                        if (tapMat != null) NDCompo.noteTintColor = tapMat.GetColor("_NoteColor");
                        var tapSize = GetSizeAt(tapType, timing.time, note.isBreak, tapIsEach);
                        NDCompo.noteScaleX = tapSize.x;
                        NDCompo.noteScaleY = tapSize.y;
                    }
                    else if (note.noteType == SimaiNoteType.Hold)
                    {
                        var GOnote = Instantiate(holdPrefab, notes.transform);
                        AddJudgeNote(GOnote, note);
                        var NDCompo = PrepareNote<HoldDrop>(GOnote);

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

                        NDCompo.HoldShine = HoldShine;
                        NDCompo.BreakShine = BreakShine;

                        if (timing.noteList.Count > 1) NDCompo.isEach = true;
                        NDCompo.time = (float)timing.time;
                        NDCompo.LastFor = (float)note.holdTime;
                        NDCompo.startPosition = note.startPosition;
                        var holdIsEach = timing.noteList.Count > 1;
                        NDCompo.speed = noteSpeed * GetHSpeedAt(
                            "hold", timing.time, timing.HSpeed, note.isBreak, holdIsEach);
                        NDCompo.scrollType = ResolveSvType("hold", note.isBreak, holdIsEach);
                        NDCompo.noteScrollPos = SvController.GetCumulativeScroll(
                            timing.time, NDCompo.scrollType);
                        NDCompo.spawnRadius = GetSpawnRadiusAt(
                            "hold", timing.time, note.isBreak, holdIsEach);
                        NDCompo.bounceDuration = GetBounceDurationAt(
                            "hold", timing.time, note.isBreak, holdIsEach);
                        NDCompo.isEX = note.isEx;
                        NDCompo.isBreak = note.isBreak;
                        NDCompo.isFirework = note.isHanabi;
                        NDCompo.isDZone = note.isDZone;
                        var holdMat = GetHoldMaterial(
                            note.isBreak, timing.noteList.Count > 1, timing.time, note.isMonoHead);
                        NDCompo.colorOverrideMaterial = holdMat;
                        if (holdMat != null) NDCompo.noteTintColor = holdMat.GetColor("_NoteColor");
                        var holdSize = GetSizeAt("hold", timing.time, note.isBreak, holdIsEach);
                        NDCompo.noteScaleX = holdSize.x;
                        NDCompo.noteScaleY = holdSize.y;
                    }
                    else if (note.noteType == SimaiNoteType.TouchHold)
                    {
                        var touchSensor = Assets.Scripts.TouchBase.GetSensor(note.touchArea, note.startPosition);
                        var GOnote = Instantiate(touchHoldPrefab, notes.transform);
                        AddJudgeTouch(GOnote, touchSensor);
                        var NDCompo = PrepareNote<TouchHoldDrop>(GOnote);

                        // Note layer order
                        NDCompo.noteSortOrder = noteSortOrder;
                        noteSortOrder -= NOTE_LAYER_COUNT[note.noteType];

                        NDCompo.touchArea = note.touchArea;
                        NDCompo.startPosition = note.startPosition;
                        NDCompo.time = (float)timing.time;
                        NDCompo.LastFor = (float)note.holdTime;
                        var touchHoldIsEach = timing.noteList.Count > 1;
                        NDCompo.speed = touchSpeed * GetHSpeedAt(
                            "touchhold", timing.time, timing.HSpeed, note.isBreak, touchHoldIsEach);
                        NDCompo.scrollType = ResolveSvType("touchhold", note.isBreak, touchHoldIsEach);
                        NDCompo.noteScrollPos = SvController.GetCumulativeScroll(
                            timing.time, NDCompo.scrollType);
                        NDCompo.isFirework = note.isHanabi;
                        NDCompo.isBreak = note.isBreak;
                        // Mine notes remain grayscale even when a color override is active.
                        var thMat = note.isMonoHead
                            ? CreateTintMaterial(null, GetAlphaAt("touchhold", timing.time), grayscale: true)
                            : CreateTintMaterial(GetColorAt("touchhold", timing.time), GetAlphaAt("touchhold", timing.time));
                        NDCompo.colorOverrideMaterial = thMat;
                        if (thMat != null && !note.isMonoHead) NDCompo.noteTintColor = thMat.GetColor("_NoteColor");
                        var touchHoldSize = GetSizeAt(
                            "touchhold", timing.time, note.isBreak, touchHoldIsEach);
                        NDCompo.noteScaleX = touchHoldSize.x;
                        NDCompo.noteScaleY = touchHoldSize.y;

                        // Break touch-holds use dedicated skin sprites instead of runtime tinting.
                        Array.Copy(note.isBreak ? customSkin.TouchHold_Break : customSkin.TouchHold, NDCompo.TouchHoldSprite, 5);
                        NDCompo.TouchPointSprite = note.isBreak ? customSkin.TouchPoint_Break : customSkin.TouchPoint;
                    }
                    else if (note.noteType == SimaiNoteType.Touch)
                    {
                        var GOnote = Instantiate(touchPrefab, notes.transform);
                        AddJudgeTouch(GOnote, TouchBase.GetSensor(note.touchArea, note.startPosition));
                        var NDCompo = PrepareNote<TouchDrop>(GOnote);

                        // Note layer order
                        NDCompo.noteSortOrder = noteSortOrder;
                        noteSortOrder -= NOTE_LAYER_COUNT[note.noteType];

                        NDCompo.time = (float)timing.time;
                        NDCompo.areaPosition = note.touchArea;
                        NDCompo.startPosition = note.startPosition;

                        // Break touches use dedicated skin sprites, including each notes.
                        NDCompo.fanNormalSprite = note.isBreak ? customSkin.Touch_Break : customSkin.Touch;
                        NDCompo.fanEachSprite = note.isBreak ? customSkin.Touch_Break : customSkin.Touch_Each;
                        NDCompo.pointNormalSprite = note.isBreak ? customSkin.TouchPoint_Break : customSkin.TouchPoint;
                        NDCompo.pointEachSprite = note.isBreak ? customSkin.TouchPoint_Break : customSkin.TouchPoint_Each;
                        NDCompo.justSprite = customSkin.TouchJust;
                        Array.Copy(customSkin.TouchBorder, NDCompo.multTouchNormalSprite, 2);
                        Array.Copy(customSkin.TouchBorder_Each, NDCompo.multTouchEachSprite, 2);

                        if (timing.noteList.Count > 1)
                        {
                            NDCompo.isEach = true;
                            members.Add(NDCompo);
                        }
                        var touchIsEach = timing.noteList.Count > 1;
                        NDCompo.speed = touchSpeed * GetHSpeedAt(
                            "touch", timing.time, timing.HSpeed, note.isBreak, touchIsEach);
                        NDCompo.scrollType = ResolveSvType("touch", note.isBreak, touchIsEach);
                        NDCompo.noteScrollPos = SvController.GetCumulativeScroll(
                            timing.time, NDCompo.scrollType);
                        NDCompo.isFirework = note.isHanabi;
                        NDCompo.isBreak = note.isBreak;
                        NDCompo.GroupInfo = null;
                        // Mine notes remain grayscale even when a color override is active.
                        NDCompo.colorOverrideMaterial = note.isMonoHead
                            ? CreateTintMaterial(null, GetAlphaAt("touch", timing.time), grayscale: true)
                            : CreateTintMaterial(GetColorAt("touch", timing.time), GetAlphaAt("touch", timing.time));
                        var touchSize = GetSizeAt("touch", timing.time, note.isBreak, touchIsEach);
                        NDCompo.noteScaleX = touchSize.x;
                        NDCompo.noteScaleY = touchSize.y;
                    }

                    else if (note.noteType == SimaiNoteType.Slide)
                    {
                        if (note.isTouchSlide)
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

                var eachNotes = timing.noteList.FindAll(o =>
                    o.noteType != SimaiNoteType.Touch && o.noteType != SimaiNoteType.TouchHold);
                if (eachNotes.Count > 1) // Multiple non-Touch notes
                {
                    var startPos = eachNotes[0].startPosition;
                    var endPos = eachNotes[1].startPosition;
                    endPos = endPos - startPos;
                    if (endPos == 0) continue;

                    var line = Instantiate(eachLine, notes.transform);
                    var lineDrop = line.GetComponent<EachLineDrop>();

                    lineDrop.time = (float)timing.time;
                    lineDrop.speed = noteSpeed * GetHSpeedAt(
                        "tap", timing.time, timing.HSpeed, isEach: true);
                    lineDrop.scrollType = ResolveSvType("tap", false, true);
                    lineDrop.noteScrollPos = SvController.GetCumulativeScroll(
                        timing.time, lineDrop.scrollType);
                    lineDrop.spawnRadius = GetSpawnRadiusAt(
                        "tap", timing.time, isEach: true);

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
                if (previewOnly)
                {
                    UnityEngine.Debug.LogWarning(e);
                    continue;
                }

                // Do not write parser errors to the View overlay. Syntax feedback
                // belongs in the editor; logging as an exception also triggers
                // Unity's Error Pause and deadlocks synchronous editor requests.
                UnityEngine.Debug.LogWarning(e);
            }
        }
        if (!previewOnly && runtimeBindingReadyFrame == int.MaxValue)
            runtimeBindingReadyFrame = Time.frameCount + 1;
        noteParserTask = null;
        yield break;
    }

    private T PrepareNote<T>(GameObject noteObject) where T : NoteDrop
    {
        var component = noteObject.GetComponent<T>();
        component.previewOnly = previewOnly;
        return component;
    }

    private void AddJudgeNote(GameObject noteObject, SimaiNote note)
    {
        if (previewOnly)
            return;
        var key = note.isDZone ? note.startPosition + 8 : note.startPosition;
        noteManager.AddNote(noteObject, noteIndex[key]++);
    }

    private void AddJudgeTouch(GameObject noteObject, SensorType sensorType)
    {
        if (previewOnly)
            return;
        noteManager.AddTouch(noteObject, touchIndex[sensorType]++);
    }

    public void LoadJson(string json, float ignoreOffset, bool previewOnly = false)
    {
        runtimeBindingReadyFrame = previewOnly ? -1 : int.MaxValue;
        ClearTintMaterialCache();
        jsonLoaderTask = Task.Run(() => JsonConvert.DeserializeObject<Majson>(json));
        State = NoteLoaderStatus.LodingJson;
        this.ignoreOffset = ignoreOffset;
        this.previewOnly = previewOnly;
    }

    public void LoadJsonImmediate(string json, float ignoreOffset, bool previewOnly = false)
    {
        runtimeBindingReadyFrame = -1;
        ClearTintMaterialCache();
        loadedData = JsonConvert.DeserializeObject<Majson>(json);
        this.ignoreOffset = ignoreOffset;
        this.previewOnly = previewOnly;
        if (loadedData == null || loadedData.timingList.Count == 0)
        {
            State = NoteLoaderStatus.Finished;
            return;
        }

        if (!previewOnly)
        {
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
        }

        SvController.Load(loadedData.svTable, 0d);
        BuildHSpeedTimeline(loadedData.hsTable);
        BuildSpawnTimeline(loadedData.spawnTable);
        BuildBounceTimeline(loadedData.bounceTable);
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
                if (note.isTouchSlide)
                {
                    if (!note.isSlideNoHead)
                    {
                        if (note.isBreak) ObjectCounter.breakSum++;
                        else if (note.touchArea == 'K') ObjectCounter.tapSum++;
                        else ObjectCounter.touchSum++;
                    }
                    if (note.isSlideBreak) ObjectCounter.breakSum++;
                    else ObjectCounter.slideSum++;
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
                        if (!note.isSlideNoHead) ObjectCounter.tapSum++;
                        if (note.isSlideBreak)
                            ObjectCounter.breakSum++;
                        else
                            ObjectCounter.slideSum++;
                    }
                }
                else
                {
                    if (note.noteType == SimaiNoteType.Slide)
                    {
                        if (!note.isSlideNoHead) ObjectCounter.breakSum++;
                        if (note.isSlideBreak)
                            ObjectCounter.breakSum++;
                        else
                            ObjectCounter.slideSum++;
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
            if (note.isTouchSlide)
            {
                if (!note.isSlideNoHead)
                {
                    if (note.isBreak) ObjectCounter.breakCount++;
                    else if (note.touchArea == 'K') ObjectCounter.tapCount++;
                    else ObjectCounter.touchCount++;
                }
                if (note.isSlideBreak) ObjectCounter.breakCount++;
                else ObjectCounter.slideCount++;
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
                    if (!note.isSlideNoHead) ObjectCounter.tapCount++;
                    if (note.isSlideBreak)
                        ObjectCounter.breakCount++;
                    else
                        ObjectCounter.slideCount++;
                }
            }
            else
            {
                if (note.noteType == SimaiNoteType.Slide)
                {
                    if (!note.isSlideNoHead) ObjectCounter.breakCount++;
                    if (note.isSlideBreak)
                        ObjectCounter.breakCount++;
                    else
                        ObjectCounter.slideCount++;
                }
                else
                {
                    ObjectCounter.breakCount++;
                }
            }
        }
    }

    private void InstantiateStarGroup(SimaiTimingPoint timing, SimaiNote note, int sort, double lastNoteTime)
    {
        int charIntParse(char c)
        {
            return c - '0';
        }

        var subSlide = new List<SimaiNote>();
        var subBarCount = new List<int>();
        var sumBarCount = 0;

        var noteContent = note.noteContent;
        var latestStartIndex = charIntParse(noteContent[0]); // Previous Slide endpoint and next Slide start
        var ptr = 1; // Current character

        var specTimeFlag = 0; // Whether this chain specifies total duration or each segment duration
        // 0=none read; 1=segment without duration; 2=segment with duration; 3=expected final duration read

        while (ptr < noteContent.Length)
            if (!char.IsNumber(noteContent[ptr]))
            {
                // Read a shape character
                var slideTypeChar = noteContent[ptr++].ToString();

                var slidePart = new SimaiNote();
                slidePart.noteType = SimaiNoteType.Slide;
                slidePart.startPosition = latestStartIndex;
                if (slideTypeChar == "V")
                {
                    // Turning Star
                    var middlePos = noteContent[ptr++];
                    var endPos = noteContent[ptr++];

                    slidePart.noteContent = latestStartIndex + slideTypeChar + middlePos + endPos;
                    latestStartIndex = charIntParse(endPos);
                }
                else
                {
                    // Other regular Stars
                    // Also check pp, qq, rp, and rq
                    if (ptr >= noteContent.Length)
                        throw new Exception("Slide缺少目标键\nSLIDE TARGET MISSING");
                    if (noteContent[ptr] == slideTypeChar[0])
                        slideTypeChar += noteContent[ptr++]; // pp or qq
                    else if (slideTypeChar == "r" && (noteContent[ptr] == 'p' || noteContent[ptr] == 'q'))
                        slideTypeChar += noteContent[ptr++]; // rp or rq
                    if (ptr >= noteContent.Length || !char.IsNumber(noteContent[ptr]))
                        throw new Exception("Slide缺少目标键\nSLIDE TARGET MISSING");
                    var endPos = noteContent[ptr++];

                    slidePart.noteContent = latestStartIndex + slideTypeChar + endPos;
                    latestStartIndex = charIntParse(endPos);
                }

                if (ptr < noteContent.Length && noteContent[ptr] == '[')
                {
                    // A duration is specified
                    if (specTimeFlag == 0)
                        // Nothing read previously
                        specTimeFlag = 2;
                    else if (specTimeFlag == 1)
                        // Prior segments had no durations; mark this as the expected final duration
                        specTimeFlag = 3;
                    else if (specTimeFlag == 3)
                        // Another duration after the expected final duration is invalid
                        throw new Exception("组合星星有错误\nSLIDE CHAIN ERROR");

                    while (ptr < noteContent.Length && noteContent[ptr] != ']')
                        slidePart.noteContent += noteContent[ptr++];
                    slidePart.noteContent += noteContent[ptr++];
                }
                else
                {
                    // No duration is specified
                    if (specTimeFlag == 0)
                        // Nothing read previously
                        specTimeFlag = 1;
                    else if (specTimeFlag == 2 || specTimeFlag == 3)
                        // Mixing segments with and without durations is invalid
                        throw new Exception("组合星星有错误\nSLIDE CHAIN ERROR");
                }

                string slideShape = detectShapeFromText(slidePart.noteContent);
                if (slideShape.StartsWith("-"))
                    slideShape = slideShape.Substring(1);
                if (slideShape.StartsWith("r"))
                    slideShape = slideShape.Substring(1);
                int slideIndex = SLIDE_PREFAB_MAP[slideShape];
                if (slideIndex < 0) slideIndex = -slideIndex;

                var barCount = slidePrefab[slideIndex].transform.childCount;
                subBarCount.Add(barCount);
                sumBarCount += barCount;

                subSlide.Add(slidePart);
            }
            else
            {
                // A number cannot appear here and indicates invalid syntax
                throw new Exception("组合星星有错误\nwSLIDE CHAIN ERROR");
            }

        for (var i = 0; i < subSlide.Count; i++)
        {
            var o = subSlide[i];
            // The parser stores D-zone ownership on the complete slide. Once the
            // slide is split into render/judge segments, retain it on the first and
            // last segment so a simple 1d-5d slide reaches SlideDrop unchanged.
            o.isDZone = i == 0 && note.isDZone;
            o.isDZoneEnd = i == subSlide.Count - 1 && note.isDZoneEnd;
            o.isBreak = note.isBreak;
            o.isEx = note.isEx;
            o.isHanabi = i == 0 && note.isHanabi; // Fireworks belong only to the Star head
            o.isMonoHead = i == 0 && note.isMonoHead;
            o.isSlideMono = note.isSlideMono;
            o.isSlideBreak = note.isSlideBreak;
            o.isSlideNoHead = true;
        }
        subSlide[0].isSlideNoHead = note.isSlideNoHead;

        if (specTimeFlag == 1 || specTimeFlag == 0)
            // Flag 1 at the end means no duration was specified
            throw new Exception("组合星星有错误\nwSLIDE CHAIN ERROR");
        // Flag 2 means per-segment syntax; flag 3 means total-duration syntax

        if (specTimeFlag == 3)
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

            // Local helper to obtain duration
            double getTimeFromBeats(string noteText, float currentBpm)
            {
                var startIndex = noteText.IndexOf('[');
                var overIndex = noteText.IndexOf(']');
                var innerString = noteText.Substring(startIndex + 1, overIndex - startIndex - 1);
                var timeOneBeat = 1d / (currentBpm / 60d);
                if (innerString.Count(o => o == '#') == 1)
                {
                    var times = innerString.Split('#');
                    if (times[1].Contains(':'))
                    {
                        innerString = times[1];
                        timeOneBeat = 1d / (double.Parse(times[0]) / 60d);
                    }
                    else
                    {
                        return double.Parse(times[1]);
                    }
                }

                if (innerString.Count(o => o == '#') == 2)
                {
                    var times = innerString.Split('#');
                    return double.Parse(times[2]);
                }

                var numbers = innerString.Split(':');
                var divide = int.Parse(numbers[0]);
                var count = int.Parse(numbers[1]);


                return timeOneBeat * 4d / divide * count;
            }

            double tempSlideTime = 0;
            for (var i = 0; i < subSlide.Count; i++)
            {
                subSlide[i].slideStartTime = note.slideStartTime + tempSlideTime;
                subSlide[i].slideTime = getTimeFromBeats(subSlide[i].noteContent, timing.currentBpm);
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

    private void InstantiateTouchSlide(
        SimaiTimingPoint timing,
        SimaiNote note,
        List<TouchDrop> touchMembers)
    {
        var isEach = timing.noteList.Count > 1;
        var slideObject = new GameObject(
            $"TouchSlide_{note.touchArea}{note.startPosition}{note.touchSlideShape}" +
            $"{note.touchEndArea}{note.touchEndPosition}");
        slideObject.transform.SetParent(notes.transform, false);
        var component = slideObject.AddComponent<TouchSlideDrop>();
        component.previewOnly = previewOnly;
        component.timeStart = (float)timing.time;
        component.time = (float)note.slideStartTime;
        component.duration = Mathf.Max(0.01f, (float)note.slideTime);
        component.speed = noteSpeed * GetHSpeedAt(
            "slide", timing.time, timing.HSpeed, false, isEach);
        component.starSpeed = starSpeed;
        component.startArea = note.touchArea;
        component.endArea = note.touchEndArea;
        component.startPosition = note.startPosition;
        component.endPosition = note.touchEndPosition;
        component.isDZone = note.isDZone;
        component.isDZoneEnd = note.isDZoneEnd;
        component.shape = note.touchSlideShape;
        component.pathExpression = note.noteContent;
        component.bodyBreak = note.isSlideBreak;
        component.pathSprite = note.isSlideBreak
            ? customSkin.Slide_Break
            : isEach ? customSkin.Slide_Each : customSkin.Slide;
        component.barTemplate =
            slidePrefab[SLIDE_PREFAB_MAP["line3"]].transform.GetChild(0).gameObject;
        component.pathMaterial = GetSlideMaterial(
            note.isSlideBreak, timing.time, note.isSlideMono);
        var slideSize = GetSizeAt("slide", timing.time, false, isEach);
        component.barScale = slideSize;

        component.star = Instantiate(star_slidePrefab, notes.transform);
        var starRenderer = component.star.GetComponent<SpriteRenderer>();
        starRenderer.sprite = note.isSlideBreak
            ? customSkin.Star_Break
            : isEach ? customSkin.Star_Each : customSkin.Star;
        component.starMaterial = component.pathMaterial;
        component.starScale = Vector2.one;
        component.star.SetActive(false);
        component.sortingOrder = slideLayer;
        slideLayer -= 24;

        if (note.isSlideNoHead)
            return;
        if (note.touchArea == 'K')
        {
            slideObject.SetActive(false);
            InstantiateTouchSlideKeyHead(timing, note, slideObject, isEach);
        }
        else
        {
            InstantiateTouchSlideHead(timing, note, touchMembers, isEach);
        }
    }

    private void InstantiateTouchSlideKeyHead(
        SimaiTimingPoint timing,
        SimaiNote note,
        GameObject slideObject,
        bool isEach)
    {
        var starObject = Instantiate(starPrefab, notes.transform);
        AddJudgeNote(starObject, note);
        var star = PrepareNote<StarDrop>(starObject);
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
        star.BreakShine = BreakShine;
        star.time = (float)timing.time;
        star.startPosition = note.startPosition;
        star.isDZone = note.isDZone;
        star.rotateSpeed = Mathf.Max(0.01f, (float)note.slideTime);
        star.isEach = isEach;
        star.isBreak = note.isBreak;
        star.isEX = note.isEx;
        star.isFirework = note.isHanabi;
        star.slide = slideObject;
        star.speed = noteSpeed * GetHSpeedAt(
            "star", timing.time, timing.HSpeed, note.isBreak, isEach);
        star.scrollType = ResolveSvType("star", note.isBreak, isEach);
        star.noteScrollPos = SvController.GetCumulativeScroll(timing.time, star.scrollType);
        star.spawnRadius = GetSpawnRadiusAt("star", timing.time, note.isBreak, isEach);
        star.bounceDuration = GetBounceDurationAt("star", timing.time, note.isBreak, isEach);
        star.colorOverrideMaterial = GetStarMaterial(
            note.isBreak, isEach, timing.time, note.isMonoHead);
        var size = GetSizeAt("star", timing.time, note.isBreak, isEach);
        star.noteScaleX = size.x;
        star.noteScaleY = size.y;
    }

    private void InstantiateTouchSlideHead(
        SimaiTimingPoint timing,
        SimaiNote note,
        List<TouchDrop> touchMembers,
        bool isEach)
    {
        var touchObject = Instantiate(touchPrefab, notes.transform);
        AddJudgeTouch(
            touchObject,
            TouchBase.GetSensor(note.touchArea, note.startPosition));
        var touch = PrepareNote<TouchDrop>(touchObject);
        touch.noteSortOrder = noteSortOrder;
        noteSortOrder -= NOTE_LAYER_COUNT[SimaiNoteType.Touch];
        touch.time = (float)timing.time;
        touch.areaPosition = note.touchArea;
        touch.startPosition = note.startPosition;
        touch.fanNormalSprite = note.isBreak ? customSkin.Touch_Break : customSkin.Touch;
        touch.fanEachSprite = note.isBreak ? customSkin.Touch_Break : customSkin.Touch_Each;
        touch.pointNormalSprite = note.isBreak ? customSkin.TouchPoint_Break : customSkin.TouchPoint;
        touch.pointEachSprite = note.isBreak ? customSkin.TouchPoint_Break : customSkin.TouchPoint_Each;
        touch.justSprite = customSkin.TouchJust;
        Array.Copy(customSkin.TouchBorder, touch.multTouchNormalSprite, 2);
        Array.Copy(customSkin.TouchBorder_Each, touch.multTouchEachSprite, 2);
        touch.isEach = isEach;
        touch.isBreak = note.isBreak;
        touch.isFirework = note.isHanabi;
        if (isEach)
            touchMembers.Add(touch);
        touch.speed = touchSpeed * GetHSpeedAt(
            "touch", timing.time, timing.HSpeed, note.isBreak, isEach);
        touch.scrollType = ResolveSvType("touch", note.isBreak, isEach);
        touch.noteScrollPos = SvController.GetCumulativeScroll(
            timing.time, touch.scrollType);
        touch.GroupInfo = null;
        touch.colorOverrideMaterial = note.isMonoHead
            ? CreateTintMaterial(null, GetAlphaAt("touch", timing.time), grayscale: true)
            : CreateTintMaterial(GetColorAt("touch", timing.time), GetAlphaAt("touch", timing.time));
        var touchSize = GetSizeAt("touch", timing.time, note.isBreak, isEach);
        touch.noteScaleX = touchSize.x;
        touch.noteScaleY = touchSize.y;
    }

    private GameObject InstantiateWifi(SimaiTimingPoint timing, SimaiNote note)
    {
        var str = note.noteContent.Substring(0, 3);
        var digits = str.Split('w');
        var startPos = int.Parse(digits[0]);
        var endPos = int.Parse(digits[1]);
        endPos = endPos - startPos;
        endPos = endPos < 0 ? endPos + 8 : endPos;
        endPos = endPos > 8 ? endPos - 8 : endPos;
        endPos++;

        var GOnote = Instantiate(starPrefab, notes.transform);
        var NDCompo = PrepareNote<StarDrop>(GOnote);
        AddJudgeNote(GOnote, note);


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

        NDCompo.BreakShine = BreakShine;

        NDCompo.rotateSpeed = (float)note.slideTime;
        NDCompo.isEX = note.isEx;
        NDCompo.isBreak = note.isBreak;
        NDCompo.isFirework = note.isHanabi;
        NDCompo.isDZone = note.isDZone;

        var slideWifi = Instantiate(slidePrefab[SLIDE_PREFAB_MAP["wifi"]], notes.transform);
        slideWifi.SetActive(false);
        NDCompo.slide = slideWifi;
        var WifiCompo = PrepareNote<WifiDrop>(slideWifi);

        WifiCompo.normalStar = customSkin.Star;
        WifiCompo.eachStar = customSkin.Star_Each;
        WifiCompo.breakStar = customSkin.Star_Break;
        WifiCompo.judgeBreakShine = JudgeBreakShine;
        WifiCompo.breakMaterial = breakMaterial;
        WifiCompo.slideShine = BreakShine;
        WifiCompo.areaStep = new List<int>(SLIDE_AREA_STEP_MAP["wifi"]);
        WifiCompo.judgeQueues = new(WIFISLIDE_JUDGE_QUEUE[startPos]);
        WifiCompo.slideConst = SLIDE_AREA_CONST["wifi"];
        WifiCompo.smoothSlideAnime = smoothSlideAnime;
        WifiCompo.starSpeed = starSpeed;
        WifiCompo.isDZone = note.isDZone;
        WifiCompo.isDZoneEnd = note.isDZoneEnd;

        Array.Copy(customSkin.Wifi, WifiCompo.normalSlide, 11);
        Array.Copy(customSkin.Wifi_Each, WifiCompo.eachSlide, 11);
        Array.Copy(customSkin.Wifi_Break, WifiCompo.breakSlide, 11);

        if (timing.noteList.Count > 1)
        {
            NDCompo.isEach = true;
            NDCompo.isDouble = false;
            if (timing.noteList.FindAll(
                    o => o.noteType == SimaiNoteType.Slide).Count
                > 1)
                WifiCompo.isEach = true;
            var count = timing.noteList.FindAll(
                o => o.noteType == SimaiNoteType.Slide &&
                     o.startPosition == note.startPosition).Count;
            if (count > 1) // Multiple notes share the same start
            {
                NDCompo.isDouble = true;
                if (count == timing.noteList.Count)
                    NDCompo.isEach = false;
                else
                    NDCompo.isEach = true;
            }
        }

        WifiCompo.isBreak = note.isSlideBreak;
        WifiCompo.colorOverrideMaterial = GetSlideMaterial(
            note.isSlideBreak, timing.time, note.isSlideMono);
        NDCompo.colorOverrideMaterial = GetStarMaterial(
            note.isBreak, timing.noteList.Count > 1, timing.time, note.isMonoHead);

        NDCompo.isNoHead = note.isSlideNoHead;
        NDCompo.time = (float)timing.time;
        NDCompo.startPosition = note.startPosition;
        NDCompo.speed = noteSpeed * GetHSpeedAt(
            "star", timing.time, timing.HSpeed, note.isBreak, NDCompo.isEach);
        NDCompo.scrollType = ResolveSvType("star", note.isBreak, NDCompo.isEach);
        NDCompo.noteScrollPos = SvController.GetCumulativeScroll(timing.time, NDCompo.scrollType);
        NDCompo.spawnRadius = GetSpawnRadiusAt(
            "star", timing.time, note.isBreak, NDCompo.isEach);
        NDCompo.bounceDuration = GetBounceDurationAt(
            "star", timing.time, note.isBreak, NDCompo.isEach);
        var wifiStarSize = GetSizeAt("star", timing.time, note.isBreak, NDCompo.isEach);
        NDCompo.noteScaleX = wifiStarSize.x;
        NDCompo.noteScaleY = wifiStarSize.y;

        WifiCompo.isJustR = detectJustType(note.noteContent, out endPos);
        WifiCompo.endPosition = endPos;
        WifiCompo.speed = noteSpeed * GetHSpeedAt(
            "slide", timing.time, timing.HSpeed, note.isSlideBreak, WifiCompo.isEach);
        WifiCompo.scrollType = ResolveSvType("slide", note.isSlideBreak, WifiCompo.isEach);
        var wifiSlideSize = GetSizeAt(
            "slide", timing.time, note.isSlideBreak, WifiCompo.isEach);
        WifiCompo.noteScaleX = wifiSlideSize.x;
        WifiCompo.noteScaleY = wifiSlideSize.y;
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
        var GOnote = Instantiate(starPrefab, notes.transform);
        var NDCompo = PrepareNote<StarDrop>(GOnote);
        if(!note.isSlideNoHead)
            AddJudgeNote(GOnote, note);
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

        NDCompo.BreakShine = BreakShine;

        NDCompo.rotateSpeed = (float)note.slideTime;
        NDCompo.isEX = note.isEx;
        NDCompo.isBreak = note.isBreak;
        NDCompo.isFirework = note.isHanabi;
        NDCompo.isDZone = note.isDZone;

        string slideShape = detectShapeFromText(note.noteContent);
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
        slide_star.GetComponent<SpriteRenderer>().sprite = customSkin.Star;
        slide_star.SetActive(false);
        slide.SetActive(false);
        NDCompo.slide = slide;
        var SliCompo = slide.AddComponent<SlideDrop>();
        SliCompo.previewOnly = previewOnly;
        SliCompo.isDZone = note.isDZone;
        SliCompo.isDZoneEnd = note.isDZoneEnd;

        SliCompo.slideType = slideShape;
        SliCompo.spriteNormal = customSkin.Slide;
        SliCompo.spriteEach = customSkin.Slide_Each;
        SliCompo.spriteBreak = customSkin.Slide_Break;
        SliCompo.slideShine = BreakShine;
        SliCompo.breakMaterial = breakMaterial;
        SliCompo.judgeBreakShine = JudgeBreakShine;
        SliCompo.areaStep = new List<int>(SLIDE_AREA_STEP_MAP[slideShape]);
        SliCompo.slideConst = SLIDE_AREA_CONST[slideShape];
        SliCompo.smoothSlideAnime = smoothSlideAnime;
        SliCompo.starSpeed = starSpeed;

        if (timing.noteList.Count > 1)
        {
            NDCompo.isEach = true;
            if (timing.noteList.FindAll(o => o.noteType == SimaiNoteType.Slide).Count > 1)
            {
                SliCompo.isEach = true;
                slide_star.GetComponent<SpriteRenderer>().sprite = customSkin.Star_Each;
            }

            var count = timing.noteList.FindAll(
                o => o.noteType == SimaiNoteType.Slide &&
                     o.startPosition == note.startPosition).Count;
            if (count > 1)
            {
                NDCompo.isDouble = true;
                if (count == timing.noteList.Count)
                    NDCompo.isEach = false;
                else
                    NDCompo.isEach = true;
            }
        }

        SliCompo.ConnectInfo = info;
        SliCompo.isBreak = note.isSlideBreak;
        if (note.isSlideBreak) slide_star.GetComponent<SpriteRenderer>().sprite = customSkin.Star_Break;

        NDCompo.isNoHead = note.isSlideNoHead;
        NDCompo.time = (float)timing.time;
        NDCompo.startPosition = note.startPosition;
        NDCompo.speed = noteSpeed * GetHSpeedAt(
            "star", timing.time, timing.HSpeed, note.isBreak, NDCompo.isEach);
        NDCompo.scrollType = ResolveSvType("star", note.isBreak, NDCompo.isEach);
        NDCompo.noteScrollPos = SvController.GetCumulativeScroll(timing.time, NDCompo.scrollType);
        NDCompo.spawnRadius = GetSpawnRadiusAt(
            "star", timing.time, note.isBreak, NDCompo.isEach);

        NDCompo.bounceDuration = GetBounceDurationAt(
            "star", timing.time, note.isBreak, NDCompo.isEach);
        SliCompo.isMirror = isMirror;
        SliCompo.isJustR = detectJustType(note.noteContent, out int endPos);
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
        SliCompo.speed = noteSpeed * GetHSpeedAt(
            "slide", timing.time, timing.HSpeed, note.isSlideBreak, SliCompo.isEach);
        SliCompo.scrollType = ResolveSvType("slide", note.isSlideBreak, SliCompo.isEach);
        var slideSize = GetSizeAt("slide", timing.time, note.isSlideBreak, SliCompo.isEach);
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
        var slideMat = GetSlideMaterial(note.isSlideBreak, timing.time, note.isSlideMono);
        SliCompo.colorOverrideMaterial = slideMat;
        if (slideMat != null) SliCompo.noteTintColor = slideMat.GetColor("_NoteColor");
        var starMat = GetStarMaterial(
            note.isBreak, timing.noteList.Count > 1, timing.time, note.isMonoHead);
        NDCompo.colorOverrideMaterial = starMat;
        if (starMat != null) NDCompo.noteTintColor = starMat.GetColor("_NoteColor");
        var starSize = GetSizeAt("star", timing.time, note.isBreak, NDCompo.isEach);
        NDCompo.noteScaleX = starSize.x;
        NDCompo.noteScaleY = starSize.y;
        //slideLayer += 5;
        return slide;
    }

    private bool detectJustType(string content, out int endPos)
    {
        // > < ^ V w
        if (content.Contains('>'))
        {
            var str = content.Substring(0, 3);
            var digits = str.Split('>');
            var startPos = int.Parse(digits[0]);
            endPos = int.Parse(digits[1]);
            if (isUpperHalf(startPos))
                return true;
            return false;
        }

        if (content.Contains('<'))
        {
            var str = content.Substring(0, 3);
            var digits = str.Split('<');
            var startPos = int.Parse(digits[0]);
            endPos = int.Parse(digits[1]);
            if (!isUpperHalf(startPos))
                return true;
            return false;
        }

        if (content.Contains('^'))
        {
            var str = content.Substring(0, 3);
            var digits = str.Split('^');
            var startPos = int.Parse(digits[0]);
            endPos = int.Parse(digits[1]);
            endPos = endPos - startPos;
            endPos = endPos < 0 ? endPos + 8 : endPos;
            endPos = endPos > 8 ? endPos - 8 : endPos;

            if (endPos < 4)
            {
                endPos = int.Parse(digits[1]);
                return true;
            }
            if (endPos > 4)
            {
                endPos = int.Parse(digits[1]);
                return false;
            }
        }
        else if (content.Contains('V'))
        {
            var str = content.Substring(0, 4);
            var digits = str.Split('V');
            endPos = int.Parse(digits[1][1].ToString());

            if (isRightHalf(endPos))
                return true;
            return false;
        }
        else if (content.Contains('w'))
        {
            var str = content.Substring(0, 3);
            endPos = int.Parse(str.Substring(2, 1));
            if (isUpperHalf(endPos))
                return true;
            return false;
        }
        else
        {
            //int endPos;
            if (content.Contains("qq") || content.Contains("pp") || content.Contains("rp") || content.Contains("rq"))
                endPos = int.Parse(content.Substring(3, 1));
            else
                endPos = int.Parse(content.Substring(2, 1));
            if (isRightHalf(endPos))
                return true;
            return false;
        }
        return true;
    }

    private string detectShapeFromText(string content)
    {
        int getRelativeEndPos(int startPos, int endPos)
        {
            endPos = endPos - startPos;
            endPos = endPos < 0 ? endPos + 8 : endPos;
            endPos = endPos > 8 ? endPos - 8 : endPos;
            return endPos + 1;
        }

        //print(content);
        if (content.Contains('-'))
        {
            // line
            var str = content.Substring(0, 3); //something like "8-6"
            var digits = str.Split('-');
            var startPos = int.Parse(digits[0]);
            var endPos = int.Parse(digits[1]);
            endPos = getRelativeEndPos(startPos, endPos);
            if (endPos < 3 || endPos > 7) throw new Exception("-星星至少隔开一键\n-スライドエラー");
            return "line" + endPos;
        }

        if (content.Contains('>'))
        {
            // Circle defaults to clockwise
            var str = content.Substring(0, 3);
            var digits = str.Split('>');
            var startPos = int.Parse(digits[0]);
            var endPos = int.Parse(digits[1]);
            endPos = getRelativeEndPos(startPos, endPos);
            if (isUpperHalf(startPos))
            {
                return "circle" + endPos;
            }

            endPos = MirrorKeys(endPos);
            return "-circle" + endPos; //Mirror
        }

        if (content.Contains('<'))
        {
            // Circle defaults to clockwise
            var str = content.Substring(0, 3);
            var digits = str.Split('<');
            var startPos = int.Parse(digits[0]);
            var endPos = int.Parse(digits[1]);
            endPos = getRelativeEndPos(startPos, endPos);
            if (!isUpperHalf(startPos))
            {
                return "circle" + endPos;
            }

            endPos = MirrorKeys(endPos);
            return "-circle" + endPos; //Mirror
        }

        if (content.Contains('^'))
        {
            var str = content.Substring(0, 3);
            var digits = str.Split('^');
            var startPos = int.Parse(digits[0]);
            var endPos = int.Parse(digits[1]);
            endPos = getRelativeEndPos(startPos, endPos);

            if (endPos == 1 || endPos == 5)
            {
                throw new Exception("^星星不合法\n^スライドエラー");
            }

            if (endPos < 5)
            {
                return "circle" + endPos;
            }
            if (endPos > 5)
            {
                return "-circle" + MirrorKeys(endPos);
            }
        }

        if (content.Contains('v'))
        {
            // v
            var str = content.Substring(0, 3);
            var digits = str.Split('v');
            var startPos = int.Parse(digits[0]);
            var endPos = int.Parse(digits[1]);
            endPos = getRelativeEndPos(startPos, endPos);
            if (endPos == 5) throw new Exception("v星星不合法\nvスライドエラー");
            return "v" + endPos;
        }

        if (content.Contains("rp"))
        {
            // rp: same arc geometry as (endPos pp startPos), traversed start->end (isReverse handles star direction)
            var str = content.Substring(0, 4);
            var digits = str.Split(new string[] { "rp" }, StringSplitOptions.None);
            var startPos = int.Parse(digits[0]);
            var endPos = int.Parse(digits[1]);
            endPos = getRelativeEndPos(endPos, startPos);
            return "rppqq" + endPos;
        }

        if (content.Contains("rq"))
        {
            // rq: same arc geometry as (endPos qq startPos), traversed start->end (isReverse handles star direction)
            var str = content.Substring(0, 4);
            var digits = str.Split(new string[] { "rq" }, StringSplitOptions.None);
            var startPos = int.Parse(digits[0]);
            var endPos = int.Parse(digits[1]);
            endPos = getRelativeEndPos(endPos, startPos);
            endPos = MirrorKeys(endPos);
            return "-rppqq" + endPos;
        }

        if (content.Contains("pp"))
        {
            // ppqq defaults to pp
            var str = content.Substring(0, 4);
            var digits = str.Split('p');
            var startPos = int.Parse(digits[0]);
            var endPos = int.Parse(digits[2]);
            endPos = getRelativeEndPos(startPos, endPos);
            return "ppqq" + endPos;
        }

        if (content.Contains("qq"))
        {
            // ppqq defaults to pp
            var str = content.Substring(0, 4);
            var digits = str.Split('q');
            var startPos = int.Parse(digits[0]);
            var endPos = int.Parse(digits[2]);
            endPos = getRelativeEndPos(startPos, endPos);
            endPos = MirrorKeys(endPos);
            return "-ppqq" + endPos;
        }

        if (content.Contains('p'))
        {
            // pq defaults to p
            var str = content.Substring(0, 3);
            var digits = str.Split('p');
            var startPos = int.Parse(digits[0]);
            var endPos = int.Parse(digits[1]);
            endPos = getRelativeEndPos(startPos, endPos);
            return "pq" + endPos;
        }

        if (content.Contains('q'))
        {
            // pq defaults to p
            var str = content.Substring(0, 3);
            var digits = str.Split('q');
            var startPos = int.Parse(digits[0]);
            var endPos = int.Parse(digits[1]);
            endPos = getRelativeEndPos(startPos, endPos);
            endPos = MirrorKeys(endPos);
            return "-pq" + endPos;
        }

        if (content.Contains('s'))
        {
            // s
            var str = content.Substring(0, 3);
            var digits = str.Split('s');
            var startPos = int.Parse(digits[0]);
            var endPos = int.Parse(digits[1]);
            endPos = getRelativeEndPos(startPos, endPos);
            if (endPos != 5) throw new Exception("s星星尾部错误\nsスライドエラー");
            return "s";
        }

        if (content.Contains('z'))
        {
            // Mirrored s
            var str = content.Substring(0, 3);
            var digits = str.Split('z');
            var startPos = int.Parse(digits[0]);
            var endPos = int.Parse(digits[1]);
            endPos = getRelativeEndPos(startPos, endPos);
            if (endPos != 5) throw new Exception("z星星尾部错误\nzスライドエラー");
            return "-s";
        }

        if (content.Contains('V'))
        {
            // L
            var str = content.Substring(0, 4);
            var digits = str.Split('V');
            var startPos = int.Parse(digits[0]);
            var turnPos = int.Parse(digits[1][0].ToString());
            var endPos = int.Parse(digits[1][1].ToString());

            turnPos = getRelativeEndPos(startPos, turnPos);
            endPos = getRelativeEndPos(startPos, endPos);
            if (turnPos == 7)
            {
                if (endPos < 2 || endPos > 5) throw new Exception("V星星终点不合法\nVスライドエラー");
                return "L" + endPos;
            }

            if (turnPos == 3)
            {
                if (endPos < 5) throw new Exception("V星星终点不合法\nVスライドエラー");
                return "-L" + MirrorKeys(endPos);
            }

            throw new Exception("V星星拐点只能隔开一键\nVスライドエラー");
        }

        if (content.Contains('w'))
        {
            // wifi
            var str = content.Substring(0, 3);
            var digits = str.Split('w');
            var startPos = int.Parse(digits[0]);
            var endPos = int.Parse(digits[1]);
            endPos = getRelativeEndPos(startPos, endPos);
            if (endPos != 5) throw new Exception("w星星尾部错误\nwスライドエラー");
            return "wifi";
        }

        return "";
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
            var shape = detectShapeFromText(content);
            var isMirror = shape.StartsWith("-", StringComparison.Ordinal);
            if (isMirror)
                shape = shape.Substring(1);
            var isReverse = shape.StartsWith("r", StringComparison.Ordinal);
            if (isReverse)
                shape = shape.Substring(1);
            if (!SLIDE_PREFAB_MAP.TryGetValue(shape, out var prefabIndex))
                return false;

            var startPosition = content[0] - '0';
            detectJustType(content, out var endPosition);
            var rotationPosition = isReverse ? endPosition : startPosition;
            var rotation = isMirror
                ? Quaternion.Euler(0f, 0f, -45f * rotationPosition)
                : Quaternion.Euler(0f, 0f, -45f * (rotationPosition - 1));
            var scale = isMirror ? new Vector3(-1f, 1f, 1f) : Vector3.one;
            var prefab = slidePrefab[prefabIndex].transform;
            var sensorRoot = GameObject.Find("Sensors")?.transform;
            if (sensorRoot == null)
                return false;

            Sensor lastSensor = null;
            for (var barIndex = 0; barIndex < prefab.childCount - 1; barIndex++)
            {
                var local = Vector3.Scale(prefab.GetChild(barIndex).localPosition, scale);
                var position = rotation * local;
                positions.Add(position);
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

            if (isReverse)
            {
                path.Reverse();
                positions.Reverse();
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

    private Dictionary<string, List<(double time, string color)>> _colorTimeline
        = new Dictionary<string, List<(double, string)>>();

    private void BuildColorTimeline(List<ColorChange> colorTable)
    {
        _colorTimeline.Clear();
        if (colorTable == null) return;
        foreach (var ev in colorTable)
        {
            var key = NormalizeNoteType(ev.noteType);
            if (!_colorTimeline.ContainsKey(key))
                _colorTimeline[key] = new List<(double, string)>();
            string colorValue = string.Equals(ev.color, "NULL", StringComparison.OrdinalIgnoreCase) ? null : ev.color;
            _colorTimeline[key].Add((ev.time, colorValue));
        }
        foreach (var kv in _colorTimeline)
            kv.Value.Sort((a, b) => a.time.CompareTo(b.time));
    }

    /// Returns the hex color string active for <paramref name="noteType"/> at <paramref name="time"/>,
    /// or null if no override is defined yet.
    private string GetColorAt(string noteType, double time)
    {
        if (!_colorTimeline.TryGetValue(NormalizeNoteType(noteType), out var list)) return null;
        var index = FindTimelineIndex(list, time);
        return index >= 0 ? list[index].color : null;
    }

    private Dictionary<string, List<(double time, Vector2 scale)>> _sizeTimeline = new();

    private void BuildSizeTimeline(List<SizeChange> sizeTable)
    {
        _sizeTimeline.Clear();
        if (sizeTable == null) return;
        foreach (var ev in sizeTable)
        {
            string key = NormalizeNoteType(ev.noteType);
            if (!_sizeTimeline.ContainsKey(key))
                _sizeTimeline[key] = new List<(double, Vector2)>();
            var x = ev.scaleX == 0f ? ev.scale : ev.scaleX;
            var y = ev.scaleY == 0f ? ev.scale : ev.scaleY;
            if (x == 0f) x = 1f;
            if (y == 0f) y = 1f;
            _sizeTimeline[key].Add((ev.time, new Vector2(x, y)));
        }
        foreach (var kv in _sizeTimeline)
            kv.Value.Sort((a, b) => a.time.CompareTo(b.time));
    }

    /// Returns the scale multiplier active for <paramref name="noteType"/> at
    /// <paramref name="time"/> (per-type over global, default 1.0).
    private Vector2 GetSizeAt(
        string noteType,
        double time,
        bool isBreak = false,
        bool isEach = false)
    {
        Vector2? Lookup(string key)
        {
            if (!_sizeTimeline.TryGetValue(key, out var list)) return null;
            var index = FindTimelineIndex(list, time);
            return index >= 0 ? list[index].scale : null;
        }
        var resolvedType = ResolveTimelineType(_sizeTimeline, NormalizeNoteType(noteType), isBreak, isEach);
        var typedScale = Lookup(resolvedType);
        if (typedScale.HasValue)
            return typedScale.Value;

        // A slide body is a path made from many sprites. Scaling it with the global
        // note size changes both sprite size and spacing, so only SIZE*slide=...
        // may alter it. The star head still follows the global/star setting.
        if (string.Equals(noteType, "slide", StringComparison.OrdinalIgnoreCase))
            return Vector2.one;

        return Lookup("") ?? Vector2.one;
    }

    private Dictionary<string, List<(double time, float multiplier)>> _hSpeedTimeline = new();

    private Dictionary<string, List<(double time, SpawnChange value)>> _spawnTimeline = new();

    private readonly Dictionary<string, List<(double time, BounceChange value)>> _bounceTimeline = new();

    private void BuildBounceTimeline(List<BounceChange> changes)
    {
        _bounceTimeline.Clear();
        if (changes == null)
            return;
        foreach (var change in changes)
        {
            var key = NormalizeNoteType(change.noteType);
            if (!_bounceTimeline.TryGetValue(key, out var values))
                _bounceTimeline[key] = values = new List<(double, BounceChange)>();
            values.Add((change.time, change));
        }
        foreach (var values in _bounceTimeline.Values)
            values.Sort((left, right) => left.time.CompareTo(right.time));
    }

    private float GetBounceDurationAt(string noteType, double time, bool isBreak, bool isEach)
    {
        var resolvedType = ResolveTimelineType(
            _bounceTimeline, NormalizeNoteType(noteType), isBreak, isEach);
        if (!_bounceTimeline.TryGetValue(resolvedType, out var values))
            return 0f;
        var index = FindTimelineIndex(values, time);
        if (index < 0 || values[index].value.reset)
            return 0f;
        return Math.Max(0f, values[index].value.duration);
    }

    private void BuildSpawnTimeline(List<SpawnChange> changes)
    {
        _spawnTimeline.Clear();
        if (changes == null) return;
        foreach (var change in changes)
        {
            var key = NormalizeNoteType(change.noteType);
            if (!_spawnTimeline.TryGetValue(key, out var list))
                _spawnTimeline[key] = list = new List<(double, SpawnChange)>();
            list.Add((change.time, change));
        }
        foreach (var list in _spawnTimeline.Values)
            list.Sort((left, right) => left.time.CompareTo(right.time));
    }

    private float GetSpawnRadiusAt(
        string noteType,
        double time,
        bool isBreak = false,
        bool isEach = false)
    {
        SpawnChange Lookup(string key)
        {
            if (!_spawnTimeline.TryGetValue(key, out var list)) return null;
            var index = FindTimelineIndex(list, time);
            return index >= 0 ? list[index].value : null;
        }

        float Resolve(SpawnChange change) =>
            change == null || change.reset ? NoteDrop.DefaultSpawnRadius : change.radius;

        if (isBreak)
        {
            var special = Lookup("break");
            if (special != null)
                return special.reset ? Resolve(Lookup("")) : special.radius;
        }
        if (isEach)
        {
            var special = Lookup("each");
            if (special != null)
                return special.reset ? Resolve(Lookup("")) : special.radius;
        }

        var typed = Lookup(NormalizeNoteType(noteType));
        if (typed != null)
            return typed.reset ? Resolve(Lookup("")) : typed.radius;
        return Resolve(Lookup(""));
    }

    private void BuildHSpeedTimeline(List<SpeedChange> changes)
    {
        _hSpeedTimeline.Clear();
        if (changes == null) return;
        foreach (var change in changes)
        {
            var key = NormalizeNoteType(change.noteType);
            if (!_hSpeedTimeline.TryGetValue(key, out var list))
                _hSpeedTimeline[key] = list = new List<(double, float)>();
            list.Add((change.time, change.multiplier));
        }
        foreach (var list in _hSpeedTimeline.Values)
            list.Sort((left, right) => left.time.CompareTo(right.time));
    }

    private float GetHSpeedAt(
        string noteType,
        double time,
        float fallback,
        bool isBreak = false,
        bool isEach = false)
    {
        var resolvedType = ResolveTimelineType(_hSpeedTimeline, NormalizeNoteType(noteType), isBreak, isEach);
        if (!_hSpeedTimeline.TryGetValue(resolvedType, out var list))
            return fallback;
        var index = FindTimelineIndex(list, time);
        return index >= 0 ? list[index].multiplier : fallback;
    }

    private static string ResolveTimelineType<T>(
        Dictionary<string, List<T>> timeline,
        string baseType,
        bool isBreak,
        bool isEach)
    {
        if (isBreak && timeline.ContainsKey("break"))
            return "break";
        if (isEach && timeline.ContainsKey("each"))
            return "each";
        return baseType;
    }

    private static string ResolveSvType(string baseType, bool isBreak, bool isEach)
    {
        if (isBreak && SvController.HasTypedCurve("break"))
            return "break";
        if (isEach && SvController.HasTypedCurve("each"))
            return "each";
        return baseType;
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

    private Dictionary<string, List<(double time, float alpha)>> _alphaTimeline = new();

    private void BuildAlphaTimeline(List<AlphaChange> alphaTable)
    {
        _alphaTimeline.Clear();
        if (alphaTable == null) return;
        foreach (var ev in alphaTable)
        {
            string key = NormalizeNoteType(ev.noteType);
            if (!_alphaTimeline.ContainsKey(key))
                _alphaTimeline[key] = new List<(double, float)>();
            _alphaTimeline[key].Add((ev.time, ev.alpha));
        }
        foreach (var kv in _alphaTimeline)
            kv.Value.Sort((a, b) => a.time.CompareTo(b.time));
    }

    private float GetAlphaAt(string noteType, double time)
    {
        float? Lookup(string key)
        {
            if (!_alphaTimeline.TryGetValue(key, out var list)) return null;
            var index = FindTimelineIndex(list, time);
            return index >= 0 ? list[index].alpha : null;
        }
        return Lookup(NormalizeNoteType(noteType)) ?? Lookup("") ?? 1f;
    }

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

    private Material GetTapMaterial(bool isBreak, bool isEach, double time, bool isMono = false)
    {
        if (isMono)
            return CreateTintMaterial(null,
                GetAlphaAt(isBreak ? "break" : isEach ? "each" : "tap", time),
                grayscale: true);
        if (isBreak)
            return CreateTintMaterial(GetColorAt("break", time), GetAlphaAt("break", time));
        if (isEach)
            return CreateTintMaterial(GetColorAt("each", time) ?? GetColorAt("tap", time),
                GetAlphaAt("each", time));
        return CreateTintMaterial(GetColorAt("tap", time), GetAlphaAt("tap", time));
    }

    private Material GetHoldMaterial(bool isBreak, bool isEach, double time, bool isMono = false)
    {
        if (isMono)
            return CreateTintMaterial(null,
                GetAlphaAt(isBreak ? "break" : isEach ? "each" : "hold", time),
                grayscale: true);
        if (isBreak)
            return CreateTintMaterial(GetColorAt("break", time) ?? GetColorAt("hold", time),
                GetAlphaAt("break", time));
        if (isEach)
            return CreateTintMaterial(GetColorAt("each", time) ?? GetColorAt("hold", time),
                GetAlphaAt("each", time));
        return CreateTintMaterial(GetColorAt("hold", time), GetAlphaAt("hold", time));
    }

    private Material GetSlideMaterial(bool isBreak, double time, bool isMono = false)
    {
        if (isMono)
            return CreateTintMaterial(null,
                GetAlphaAt(isBreak ? "break" : "slide", time),
                grayscale: true);
        if (isBreak)
            return CreateTintMaterial(GetColorAt("break", time) ?? GetColorAt("slide", time),
                GetAlphaAt("break", time));
        return CreateTintMaterial(GetColorAt("slide", time), GetAlphaAt("slide", time));
    }

    /// Star heads (slide star + forced-star taps). Uses "star" key, falls back to "tap".
    private Material GetStarMaterial(bool isBreak, bool isEach, double time, bool isMono = false)
    {
        if (isMono)
            return CreateTintMaterial(null,
                GetAlphaAt(isBreak ? "break" : isEach ? "each" : "star", time),
                grayscale: true);
        if (isBreak)
        {
            string c = GetColorAt("break", time) ?? GetColorAt("star", time) ?? GetColorAt("tap", time);
            return CreateTintMaterial(c, GetAlphaAt("break", time));
        }
        if (isEach)
            return CreateTintMaterial(
                GetColorAt("each", time) ?? GetColorAt("star", time) ?? GetColorAt("tap", time),
                GetAlphaAt("each", time));
        return CreateTintMaterial(
            GetColorAt("star", time) ?? GetColorAt("tap", time),
            GetAlphaAt("star", time));
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
