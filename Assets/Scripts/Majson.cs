using System.Collections.Generic;

internal class Majson
{
    public string artist = "default";
    public string designer = "default";
    public string difficulty = "EZ";
    public int diffNum = 0;
    public string level = "1";
    public List<SimaiTimingPoint> timingList = new();
    public string title = "default";
    // ALPHA: true SV — global scroll velocity table
    public List<SvPoint> svTable = new();
    // ALPHA: mid-chart note color change events (from <COLOR*...> tokens)
    public List<ColorChange> colorTable = new();
    // ALPHA: mid-chart note size change events (from <SIZE*x> tokens)
    public List<SizeChange> sizeTable = new();
    // ALPHA: mid-chart note alpha change events (from <ALPHA*x> tokens)
    public List<AlphaChange> alphaTable = new();
    public List<DisplayChange> displayTable = new();
    public List<SubtitleChange> subtitleTable = new();
    public List<EffectChange> effectTable = new();
}

/// <summary>
/// ALPHA: A single note-color-change event from &lt;COLOR*RRGGBB&gt; or &lt;COLOR*key=RRGGBB,...&gt; syntax.
/// noteType is one of: tap | each | hold | slide | star | break | touch | touchhold
/// color is a 6-digit hex string "FF8800" or 8-digit "FF880080" (last 2 bytes = opacity).
/// </summary>
internal class ColorChange
{
    public double time;
    public string noteType;
    public string color;    // "RRGGBB" or "RRGGBBAA"
}

/// <summary>
/// ALPHA: A note-size-change event from &lt;SIZE*x&gt; syntax.
/// scale: multiplier applied to all note types from this time onward (default 1.0).
/// </summary>
internal class SizeChange
{
    public double time;
    public float scale;
}

/// <summary>
/// ALPHA: A note-alpha-change event from &lt;ALPHA*x&gt; or &lt;ALPHA*key=x,...&gt; syntax.
/// noteType: null = global (all types); or one of tap|each|hold|slide|star|break|touch|touchhold.
/// alpha: opacity multiplier (0.0=transparent, 1.0=opaque).
/// </summary>
internal class AlphaChange
{
    public double time;
    public string noteType; // null = global
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

    public string noteContent; //used for star explain
    public SimaiNoteType noteType;

    public double slideStartTime = 0d;
    public double slideTime = 0d;

    public int startPosition = 1; //键位（1-8）
    public char touchArea = ' ';
}

internal class EditRequestjson
{
    public float audioSpeed;
    public float backgroundCover;
    public float innerBackgroundCover;
    public float outerBackgroundCover;
    public bool showJudgeInfo;
    public bool showComboInfo;
    public bool showJudgeLine = true;
    public bool showJudgeText = true;
    public EditorComboIndicator comboStatusType;
    public EditorPlayMethod editorPlayMethod;
    public EditorControlMethod control;
    public string jsonPath;
    public float noteSpeed;
    public long startAt;
    public float startTime;
    public float touchSpeed;
    public bool smoothSlideAnime;
    public float chartLength;
    public int recordFrameRate = 30;
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
    SetDisplay
}

public enum EditorPlayMethod
{
    Classic, DJAuto, Random, Disabled
}
