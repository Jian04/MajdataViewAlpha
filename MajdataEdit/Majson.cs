using MajdataCore;

namespace MajdataEdit;

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
    // ALPHA: true SV table
    public List<SvPoint> svTable = new();
    public List<SpeedChange> hsTable = new();
    public List<SpawnChange> spawnTable = new();
    public List<SpawnModeChange> spawnModeTable = new();
    public List<BounceChange> bounceTable = new();
    public List<DestroyChange> destroyTable = new();
    public List<FakeChange> fakeTable = new();
    // ALPHA: mid-chart note color change events
    public List<ColorChange> colorTable = new();
    // ALPHA: mid-chart note size change events
    public List<SizeChange> sizeTable = new();
    // ALPHA: mid-chart note alpha change events (from <ALPHA*x> tokens)
    public List<AlphaChange> alphaTable = new();
    public List<DisplayChange> displayTable = new();
    public List<SubtitleChange> subtitleTable = new();
    public List<EffectChange> effectTable = new();
    public List<MediaChange> mediaTable = new();
}

internal class SvPoint
{
    public double time;
    public int sourcePosition;
    public int streamIndex;
    public float multiplier;
    public string? noteType;
    public bool reset;
}

internal class SpeedChange
{
    public double time;
    public int sourcePosition;
    public int streamIndex;
    public string? noteType;
    public float multiplier = 1f;
    public bool reset;
}

internal class SpawnChange
{
    public double time;
    public int sourcePosition;
    public int streamIndex;
    public string? noteType;
    public float radius = 1.225f;
    public bool reset;
}

internal class SpawnModeChange
{
    public double time;
    public int sourcePosition;
    public int streamIndex;
    public string? noteType;
    public SpawnVisualMode mode = SpawnVisualMode.Rewind;
    public bool reset;
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
    public string? noteType;
    public float scale;
    public float scaleX;
    public float scaleY;
    public bool reset;
    public bool live;
}

/// <summary>A timed note opacity override.</summary>
internal class AlphaChange
{
    public double time;
    public int sourcePosition;
    public int streamIndex;
    public string? noteType;
    public float alpha;
    public bool reset;
    public bool live;
}

internal class DisplayChange
{
    public double time;
    public string property = "";
    public float target;
    public float duration;
}

internal class SubtitleChange
{
    public double time;
    public string text = "";
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

internal class EffectChange
{
    public double time;
    public string effect = "";
    public float duration;
    public float intensity;
    // Negative attack selects the legacy envelope.
    public float attack = -1f;
    public float holdTime;
    public float release;
    public float paramA;
    public float paramB;
    public bool hasDirection;
    public string? color;
    public bool stateful;
    public bool enabled;
    public float transition;
}

internal class MediaChange
{
    public double time;
    public string kind = "";
    public bool enabled;
    public string path = "";
    public float transition;
    public int track;
    public double sourceOffset;
    public double duration;
    public bool timelineClip;
}

internal class BounceChange
{
    public double time;
    public int sourcePosition;
    public int streamIndex;
    public string? noteType;
    public float duration;
    public bool reset;
}

internal class DestroyChange
{
    public double time;
    public int sourcePosition;
    public int streamIndex;
    public string? noteType;
    public float radius = 4.8f;
    public bool reset;
}

internal class FakeChange
{
    public double time;
    public int sourcePosition;
    public int streamIndex;
    public string? noteType;
    public bool enabled;
    public bool reset;
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
    // ALPHA: Judgment-area overlay toggle. Forced off whenever showJudgeLine is off (see MainWindowCore).
    public bool showJudgeArea = false;
    public EditorComboIndicator comboStatusType;
    public EditorPlayMethod editorPlayMethod;
    public EditorControlMethod control;
    public string? jsonPath;
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
    public int songDetailStyle = 0;
    public bool previewFlow;
    public bool deferPlaybackStart;
    public float previewTimelineTime;
    public bool showSongDetail = true;
    public bool showAllPerfect = true;
    public bool showGeneratedMark;
    public int viewDisplayFontPreset;
    public bool enableVisualChartEditor = true;
    public float chartLength;
    public int recordFrameRate = 60;
    public string recordLayers = "";
    public string recordFileName = "out.mp4";
    public int recordWidth;
    public int recordHeight;
    public bool revealOutput = true;
    public string? previewJson;
}

public enum EditorPlayMethod
{
    Classic,DJAuto,Random,Disabled
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

//this setting is per maidata
internal class MajSetting
{
    public float Answer_Level = 0.7f;

    public float BGM_Level = 0.7f;
    public float Break_Level = 0.7f;
    public float Break_Slide_Level = 0.7f;
    public float Ex_Level = 0.7f;
    public float Hanabi_Level = 0.7f;
    public float Judge_Level = 0.7f;
    public float Mine_Level = 0.7f;
    public int lastEditDiff;
    public double lastEditTime;
    public float Slide_Level = 0.7f;
    public float Touch_Level = 0.7f;
}

//this setting is global
public class EditorSetting
{
    public float backgroundCover = 0.6f;
    public float InnerBackgroundCover = -1f;
    public float OuterBackgroundCover = -1f;
    public int BackgroundFitMode;
    public bool ShowJudgeInfo = true;
    public bool ShowComboInfo = true;
    public bool ShowJudgeLine = true;
    public bool ShowJudgeText = true;
    public bool ShowMineHitFeedback = true;
    // Keeps the background inside the play circle, so raising the outer
    // brightness no longer uncovers the four corners of the picture while the
    // notes out there stay visible.
    public bool ClipBackgroundToRing;
    public bool ShowJudgeArea = true;
    public bool ShowSongDetail = true;
    public bool ShowAllPerfect = true;
    public bool ShowGeneratedMark = true;
    public int SongDetailStyle = 1;
    public string RecordUtageLabel = "\u5bb4";
    public bool RecordUtageCoop;
    public string Skin = "dx";
    public string TapSkin = "dx";
    public string HoldSkin = "dx";
    public string StarSkin = "dx";
    public bool PinkStar;
    public int ChartRefreshDelay = 1000;
    public EditorComboIndicator comboStatusType = 0;
    public EditorPlayMethod editorPlayMethod;
    public string DecreasePlaybackSpeedKey = "Ctrl+o";
    public float Default_Answer_Level = 0.7f;
    public float Default_BGM_Level = 0.7f;
    public float Default_Break_Level = 0.7f;
    public float Default_Break_Slide_Level = 0.7f;
    public float Default_Ex_Level = 0.7f;
    public float Default_Hanabi_Level = 0.7f;
    public float Default_Judge_Level = 0.7f;
    public float Default_Mine_Level = 0.7f;
    public float Default_Slide_Level = 0.7f;
    public float Default_Touch_Level = 0.7f;
    public float DefaultSlideAccuracy = 0.2f;
    public float FontSize = 13;
    public int EditorFontPreset = 1;
    public int ViewDisplayFontPreset;
    public int FontPresetVersion = 1;
    public bool EnableVisualChartEditor = true;
    public bool EditorLightTheme = false;
    public string EditorTheme = ThemeManager.DefaultTheme;
    // Editor window decoration: "default" (theme solid color), "circleplus" (Japanese site animation), or "circle" (international site animation).
    public string EditorBackgroundStyle = "default";
    // View intro transition background: "default" (original pink-purple), "circleplus", or "circle".
    public string ViewIntroStyle = "circleplus";
    public string IncreasePlaybackSpeedKey = "Ctrl+p";
    public string Language = "en-US";
    public string Mirror180Key = "Ctrl+l";
    public string Mirror45Key = "Ctrl+OemSemicolon";
    public string MirrorCcw45Key = "Ctrl+OemQuotes";
    public string MirrorLeftRightKey = "Ctrl+j";
    public string MirrorUpDownKey = "Ctrl+k";
    public string PlayPauseKey = "Ctrl+Shift+c";
    public float playSpeed = 7.5f;
    public float starSpeed = 0f;
    public string PlayStopKey = "Ctrl+Shift+x";
    public int RenderMode = 0; // 0 = hardware rendering (default), 1 = software rendering
    public int SyntaxCheckLevel = 1; // 0 = disabled, 1 = warning (default), 2 = enabled
    public string SaveKey = "Ctrl+s";
    public string SendViewerKey = "Ctrl+Shift+z";
    public float touchSpeed = 7.5f;
    public bool SmoothSlideAnime = false;
    public string LastChartPath = "";
}
