using System.Collections.Generic;

internal class Majson
{
    public string artist = "default";
    public string designer = "default";
    public string difficulty = "EZ";
    public int diffNum = 0;
    public string filePath = "";
    public string level = "1";
    public int songDetailStyle = 0;
    public List<SimaiTimingPoint> timingList = new();
    public string title = "default";
    public string wholeBpm = "";
    // Mid-chart visual overrides.
    public List<SvPoint> svTable = new();
    public List<SpeedChange> hsTable = new();
    public List<SpawnChange> spawnTable = new();
    public List<BounceChange> bounceTable = new();
    public List<ColorChange> colorTable = new();
    public List<SizeChange> sizeTable = new();
    public List<AlphaChange> alphaTable = new();
    public List<DisplayChange> displayTable = new();
    public List<SubtitleChange> subtitleTable = new();
    public List<EffectChange> effectTable = new();
    public List<MediaChange> mediaTable = new();
}

/// <summary>A timed note color override.</summary>
internal class ColorChange
{
    public double time;
    public string noteType = string.Empty;
    public string color = string.Empty;
    public float duration;
}

/// <summary>A timed note scale override.</summary>
internal class SizeChange
{
    public double time;
    public string noteType;
    public float scale;
    public float scaleX;
    public float scaleY;
}

internal class SpeedChange
{
    public double time;
    public string noteType;
    public float multiplier = 1f;
}

internal class SpawnChange
{
    public double time;
    public string noteType;
    public float radius = 1.225f;
    public bool reset;
}

internal class BounceChange
{
    public double time;
    public string noteType;
    public float duration;
    public bool reset;
}

/// <summary>A timed note opacity override.</summary>
internal class AlphaChange
{
    public double time;
    public string noteType;
    public float alpha;
}

public class DisplayChange
{
    public double time;
    public string property;
    public float target;
    public float duration;
}

public class SubtitleChange
{
    public double time;
    public string text;
    public float duration = -1f;
}

public class EffectChange
{
    public double time;
    public string effect;
    public float duration;
    public float intensity;
    // Negative attack selects the legacy envelope.
    public float attack = -1f;
    public float holdTime;
    public float release;
    public float paramA;
    public float paramB;
    public bool hasDirection;
    public string color;
    public bool stateful;
    public bool enabled;
    public float transition;
}

public class MediaChange
{
    public double time;
    public string kind;
    public bool enabled;
    public string path;
    public float transition;
    public int track;
    public double sourceOffset;
    public double duration;
    public bool timelineClip;
}

internal class SimaiTimingPoint
{
    public float currentBpm;
    public bool havePlayed;
    public float HSpeed = 1.0f;
    public string noteContent;
    public List<SimaiNote> noteList = new();
    public int rawTextPositionX;
    public int rawTextPositionY;
    public double time;
}

internal enum SimaiNoteType
{
    Tap,
    Slide,
    Hold,
    Touch,
    TouchHold
}

internal class SimaiNote
{
    public double holdTime = 0d;
    public bool isBreak = false;
    public bool isEx = false;
    public bool isFakeRotate = false;
    public bool isForceStar = false;
    public bool isHanabi = false;
    public bool isMonoHead = false;
    public bool isSlideMono = false;
    public bool isSlideBreak = false;
    public bool isSlideNoHead = false;
    public bool isTouchSlide = false;
    public bool isDZone = false;
    public bool isDZoneEnd = false;

    public string noteContent; //used for star explain
    public SimaiNoteType noteType;

    public double slideStartTime = 0d;
    public double slideTime = 0d;

    public int startPosition = 1; // Key position (1-8)
    public char touchArea = ' ';
    public int touchEndPosition = 1;
    public char touchEndArea = ' ';
    public char touchSlideShape = '-';
}

internal class EditRequestjson
{
    public string language = "en-US";
    public float audioSpeed;
    public float mediaAudioVolume = 1f;
    public float backgroundCover;
    public float innerBackgroundCover;
    public float outerBackgroundCover;
    public int backgroundFitMode;
    public bool showJudgeInfo;
    public bool showComboInfo;
    public bool showJudgeLine = true;
    public bool showJudgeText = true;
    public bool showJudgeArea = false;
    public EditorComboIndicator comboStatusType;
    public EditorPlayMethod editorPlayMethod;
    public EditorControlMethod control;
    public string jsonPath;
    public float noteSpeed;
    public float starSpeed;
    public long startAt;
    public float startTime;
    public float touchSpeed;
    public bool smoothSlideAnime;
    public string skin = "dx";
    public string tapSkin = "dx";
    public string holdSkin = "dx";
    public string starSkin = "dx";
    public bool pinkStar;
    public string standbyTheme = "dark";
    public string introBgTheme = "default";
    public bool previewFlow;
    public float previewTimelineTime;
    public bool showSongDetail = true;
    public bool showAllPerfect = true;
    public bool showGeneratedMark;
    public int viewDisplayFontPreset;
    public bool enableVisualChartEditor = true;
    public int songDetailStyle = 0;
    public float chartLength;
    public int recordFrameRate = 30;
    public string recordLayers = "";
    public string recordFileName = "out.mp4";
    public int recordWidth;
    public int recordHeight;
    public bool revealOutput = true;
    public string previewJson;
}

public enum EditorComboIndicator
{
    None,

    // List of viable indicators that won't be a static content.
    // ScoreBorder, AchievementMaxDown, ScoreDownDeluxe are static.
    Combo,
    ScoreClassic,
    AchievementClassic,
    AchievementDownClassic,
    AchievementDeluxe = 11,
    AchievementDownDeluxe,
    ScoreDeluxe,

    // Please prefix custom indicator with C
    CScoreDedeluxe = 101,
    CScoreDownDedeluxe,
    MAX
}

internal enum EditorControlMethod
{
    Start,
    Stop,
    OpStart,
    Pause,
    Continue,
    Record,
    SetDisplay,
    Preview
}

public enum EditorPlayMethod
{
    Classic, DJAuto, Random, Disabled
}
