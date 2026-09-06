using System.Collections.Generic;
using MajdataCore;

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
    public string utageLabel = "宴";
    public bool utageCoop;
    // Mid-chart visual overrides.
    public List<SvPoint> svTable = new();
    public List<SpeedChange> hsTable = new();
    public List<SpawnChange> spawnTable = new();
    public List<SpawnModeChange> spawnModeTable = new();
    public List<BounceChange> bounceTable = new();
    public List<DestroyChange> destroyTable = new();
    public List<FakeChange> fakeTable = new();
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
    public int sourcePosition;
    public int streamIndex;
    public string noteType = string.Empty;
    public string color = string.Empty;
    public float duration;
    public bool live;
}

/// <summary>A timed note scale override.</summary>
internal class SizeChange
{
    public double time;
    public int sourcePosition;
    public int streamIndex;
    public string noteType;
    public float scale;
    public float scaleX;
    public float scaleY;
    public bool reset;
    public bool live;
}

internal class SpeedChange
{
    public double time;
    public int sourcePosition;
    public int streamIndex;
    public string noteType;
    public float multiplier = 1f;
    public bool reset;
}

internal class SpawnChange
{
    public double time;
    public int sourcePosition;
    public int streamIndex;
    public string noteType;
    public float radius = 1.225f;
    public bool reset;
}

internal class SpawnModeChange
{
    public double time;
    public int sourcePosition;
    public int streamIndex;
    public string noteType;
    public SpawnVisualMode mode = SpawnVisualMode.Rewind;
    public bool reset;
}

internal class BounceChange
{
    public double time;
    public int sourcePosition;
    public int streamIndex;
    public string noteType;
    public float duration;
    public bool reset;
}

internal class DestroyChange
{
    public double time;
    public int sourcePosition;
    public int streamIndex;
    public string noteType;
    public float radius = 4.8f;
    public bool reset;
}

internal class FakeChange
{
    public double time;
    public int sourcePosition;
    public int streamIndex;
    public string noteType;
    public bool enabled;
    public bool reset;
}

/// <summary>A timed note opacity override.</summary>
internal class AlphaChange
{
    public double time;
    public int sourcePosition;
    public int streamIndex;
    public string noteType;
    public float alpha;
    public bool reset;
    public bool live;
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
    // Left edge and top edge as a fraction of the screen, on top of the margin a
    // caption has always had, so a caption that asks for neither lands exactly
    // where captions have always landed. Zero point size means the same.
    public float x;
    public float y;
    public float size;
    public string font = "";
    public int index;
    public string style = "Fade";
    public float transition;
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
    // The editor writes this beat's source text as "notesContent". This side spelled it
    // "noteContent", so it never matched the wire format and was always null; nothing
    // read it, which is why the mismatch went unnoticed until a beat that failed to
    // build had to name itself back to the editor.
    public string notesContent;
    public List<SimaiNote> noteList = new();
    public int rawTextPositionX;
    public int rawTextPositionY;
    public int sourcePosition;
    public int streamIndex;
    public bool isEach;
    public bool? isEachInStream;
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
    public bool isMineHead = false;
    public bool isMineSlide = false;
    [Newtonsoft.Json.JsonProperty("isMonoHead")]
    private bool LegacyMineHead
    {
        get => isMineHead;
        set
        {
            if (value)
                isMineHead = true;
        }
    }
    [Newtonsoft.Json.JsonProperty("isSlideMono")]
    private bool LegacyMineSlide
    {
        get => isMineSlide;
        set
        {
            if (value)
                isMineSlide = true;
        }
    }
    public bool isSlideBreak = false;
    public bool isSlideNoHead = false;
    public bool suppressSlideGuideStarFade = false;
    public bool isTouchSlide = false;
    public bool isDZone = false;
    public bool isDZoneEnd = false;
    public bool isFake = false;
    public bool isFakeHead = false;
    public bool isFakeSlide = false;
    // "1~[5-7[8:1]]": this note is only borrowing the star trajectory. It draws no
    // arc, drops no head and is never judged - it is the travelling star and
    // nothing else.
    public bool isTrajectoryOnly = false;
    public SimaiNoteType trajectoryCarrierType = SimaiNoteType.Tap;
    public int trajectoryCarrierPosition = 1;
    public bool trajectoryCarrierIsDZone = false;

    public string noteContent; //used for star explain
    public string pathExpression;
    public List<SlidePathSegmentData> slidePath = new();
    public SimaiNoteType noteType;

    public double slideStartTime = 0d;
    public double slideTime = 0d;

    public int startPosition = 1; // Key position (1-8)
    public char touchArea = ' ';
    public int touchEndPosition = 1;
    public char touchEndArea = ' ';
    public char touchSlideShape = '-';
    // 0 keeps the Touch area's authored distance; a positive value draws the Note at
    // that distance along the same direction (see SlidePositionData.radius).
    public float touchRadius = 0f;
    // An image file, relative to the chart's folder, that replaces this one Note's
    // skin (see SlidePositionData.skin). Empty means the default skin.
    public string noteSkin = string.Empty;
}

internal class EditRequestjson
{
    public int protocolVersion = 1;
    public string language = "en-US";
    public float audioSpeed;
    public float mediaAudioVolume = 1f;
    public float backgroundCover;
    public float innerBackgroundCover;
    public float outerBackgroundCover;
    public int backgroundFitMode;
    public bool clipBackgroundToRing;
    public bool showJudgeInfo;
    public bool showComboInfo;
    public bool showJudgeLine = true;
    public bool showJudgeText = true;
    public bool showMineHitFeedback = true;
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
    public bool deferPlaybackStart;
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
    Preview,
    TimelinePreview,
    Seek
}

public enum EditorPlayMethod
{
    Classic, DJAuto, Random, Disabled
}
