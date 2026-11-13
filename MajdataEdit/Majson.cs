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
    public List<BeatDisplayPoint> beatDisplayTable = new();
    public string title = "default";
    public string wholeBpm = "";
    public string utageLabel = "宴";
    public bool utageCoop;
    public bool hasAlphaEditorSyntax;
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
    public List<AngleChange> angleTable = new();
    public List<DisplayChange> displayTable = new();
    public List<SubtitleChange> subtitleTable = new();
    public List<EffectChange> effectTable = new();
    public List<MediaChange> mediaTable = new();
}

internal class SvPoint
{
    public NoteValueTransition? transition;
    public double time;
    public int sourcePosition;
    public int streamIndex;
    public float multiplier;
    public string? noteType;
    public bool reset;
}

internal class SpeedChange
{
    public NoteValueTransition? transition;
    public double time;
    public int sourcePosition;
    public int streamIndex;
    public string? noteType;
    public float multiplier = 1f;
    public bool reset;
}

internal class SpawnChange
{
    public NoteValueTransition? transition;
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
    public MajdataCore.NoteValueTransition? transition;
}

/// <summary>A timed note scale override.</summary>
internal class SizeChange
{
    public NoteValueTransition? transition;
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
    public NoteValueTransition? transition;
    public double time;
    public int sourcePosition;
    public int streamIndex;
    public string? noteType;
    public float alpha;
    public bool reset;
    public bool live;
}

internal class AngleChange
{
    public double time;
    public int sourcePosition;
    public int streamIndex;
    public string? noteType;
    public float angle;
    public bool live;
    public bool reset;
    public NoteValueTransition? transition;
}

internal class DisplayChange
{
    public double time;
    public string property = "";
    public float target;
    public float duration;
    public bool resetToDefault;
}

internal class SubtitleChange
{
    public double time;
    public string text = "";
    public float duration = -1f;
    // Screen fractions; origin selects both the coordinate origin and text alignment.
    public float x;
    public float y;
    public float size;
    public string font = "";
    public int index;
    public string style = "Fade";
    public float transition;
    public string color = "FFFFFF";
    public string origin = "TopLeft";
}

internal class EffectChange
{
    public bool rotatePV = false;
    public bool flipNotesOnly = true;
    public bool movePV = false;
    public bool gradient = true;
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
    public double opacity = 1d;
    public double brightness;
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
    public NoteValueTransition? transition;
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
    public string recordBatch = "";
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
    public int judgeLineStyle = 3;
    public bool showJudgeText = true;
    public bool showMineHitFeedback = true;
    public EditorComboIndicator comboStatusType;
    public EditorPlayMethod editorPlayMethod;
    public EditorControlMethod control;
    public string? jsonPath;
    public string chartDirectory = MainWindow.maidataDir;
    public float noteSpeed;
    public float starSpeed;
    public long startAt;
    public float startTime;
    public float touchSpeed;
    public bool smoothSlideAnime;
    public int starDisplayMode = 1;
    public string skin = "dx";
    public string tapSkin = "dx";
    public string holdSkin = "dx";
    public string starSkin = "dx";
    public string slideSkin;
    public bool pinkStar;
    public string standbyTheme = "dark";
    public string standbyBackgroundColor = "";
    public bool showPausedJudgeAreas = true;
    public string introBgTheme = "magical";
    public int songDetailStyle = 0;
    public bool previewFlow;
    public bool deferPlaybackStart;
    public float previewTimelineTime;
    public bool showSongDetail = true;
    public bool showAllPerfect = true;
    public bool showGeneratedMark;
    public int viewDisplayFontPreset;
    public string viewDisplayFontFile = "";
    public int viewTopLeftDisplayMode;
    public bool enableVisualChartEditor = true;
    public float chartLength;
    public float recordEndTime = -1f;
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
    Seek,
    RefreshAssets
}

//this setting is per maidata
internal class MajSetting
{
    public int lastEditDiff;
    public double lastEditTime;
}

//this setting is global
public class EditorSetting
{
    public bool DragPreview = true;
    public float backgroundCover = 0.6f;
    public float InnerBackgroundCover = -1f;
    public float OuterBackgroundCover = -1f;
    public int BackgroundFitMode;
    public bool ShowJudgeInfo = true;
    public bool ShowComboInfo = true;
    public int JudgeLineStyle = 3;
    public int JudgeLinePresetVersion = 1;
    public bool ShowJudgeText = true;
    public bool ShowMineHitFeedback = true;
    // Keeps the background inside the play circle, so raising the outer
    // brightness no longer uncovers the four corners of the picture while the
    // notes out there stay visible.
    public bool ClipBackgroundToRing;
    public bool ShowSongDetail = true;
    public bool ShowAllPerfect = true;
    public bool ShowGeneratedMark = true;
    public int ViewTopLeftDisplayMode;
    public int SongDetailStyle = 1;
    public bool DisableAutomaticSongDetailRefresh;
    public string RecordUtageLabel = "\u5bb4";
    public bool RecordUtageCoop;
    public string Skin = "dx";
    public string TapSkin = "dx";
    public string HoldSkin = "dx";
    public string StarSkin = "dx";
    public string SlideSkin = "dx";
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
    public bool EditorWordWrap = false;
    public bool ShowPausedJudgeAreas = true;
    public int EditorFontPreset = 1;
    public string EditorFontFile = "";
    public int ViewDisplayFontPreset;
    public string ViewDisplayFontFile = "";
    public int FontPresetVersion = 1;
    public bool EnableVisualChartEditor = true;
    public bool TextFollow = true;
    public int QuickControlDefaultsVersion = 1;
    public bool EditorLightTheme = false;
    public string EditorTheme = ThemeManager.DefaultTheme;
    public int BackgroundPresetVersion = 1;
    // Website decoration; "default" retains the original solid editor background.
    public string EditorBackgroundStyle = "magical";
    // View intro transition; "default" retains the original pink-purple background.
    public string ViewIntroStyle = "magical";
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
    public string FindKey = "Ctrl+f";
    public string SidebarKey = "Ctrl+Alt+b";
    public string MoveCaretUpKey = "Ctrl+Up";
    public string MoveCaretDownKey = "Ctrl+Down";
    public string OverwriteKey = "Insert";
    public string EditorZoomWheel = "Ctrl+Wheel";
    public string WaveZoomWheel = "Ctrl+Wheel";
    public string MediaDeleteKey = "Delete";
    public string MediaDeleteAlternateKey = "Back";
    public string MediaCopyKey = "Ctrl+c";
    public string MediaPasteKey = "Ctrl+v";
    public string MediaUndoKey = "Ctrl+z";
    public string MediaRedoKey = "Ctrl+y";
    public string MediaSplitKey = "s";
    public float touchSpeed = 7.5f;
    public bool SmoothSlideAnime = false;
    public int StarDisplayMode = 1;
    public string LastChartPath = "";
}
