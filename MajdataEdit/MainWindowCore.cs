using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Timers;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using DiscordRPC;
using MajdataEdit.AutoSaveModule;
using MajdataEdit.Editor;
using MajdataEdit.SyntaxModule;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Semver;
using Un4seen.Bass;
using Un4seen.Bass.AddOn.Fx;
using WPFLocalizeExtension.Engine;
using WPFLocalizeExtension.Extensions;
using Brush = System.Drawing.Brush;
using Color = System.Drawing.Color;
using DashStyle = System.Drawing.Drawing2D.DashStyle;
using LinearGradientBrush = System.Drawing.Drawing2D.LinearGradientBrush;
using Pen = System.Drawing.Pen;
using PixelFormat = System.Drawing.Imaging.PixelFormat;
using Timer = System.Timers.Timer;

namespace MajdataEdit;

public partial class MainWindow : Window
{
    private const string majSettingFilename = "majSetting.json";
    private const string editorSettingFilename = "EditorSetting.json";
    public static readonly string MAJDATA_VERSION_STRING = $"v{Assembly.GetExecutingAssembly().GetName().Version!.ToString(3)}";
    public static readonly SemVersion MAJDATA_VERSION = SemVersion.Parse(MAJDATA_VERSION_STRING, SemVersionStyles.Any);

    public static string maidataDir = "";

    //float[] wavedBs;
    private readonly short[][] waveRaws = new short[3][];
    private short[]? densityAudioEnvelopeSource;
    private float[] densityAudioEnvelope = Array.Empty<float>();
    public Timer chartChangeTimer = new(1000); // Delayed chart-change parsing
    private readonly Timer currentTimeRefreshTimer = new(100);
    private readonly Timer notePreviewTimer = new(120);
    private readonly HttpListener visualEditListener = new();
    private CancellationTokenSource? visualEditCancellation;

    public DiscordRpcClient DCRPCclient = new("1068882546932326481");

    private float deltatime = 4f;
    public EditorSetting? editorSetting;

    private bool fumenOverwriteMode; // Chart text overwrite mode
    private float ghostCusorPositionTime;
    private bool mediaToolRunning;
    private bool isDrawing;
    private bool waveScrubActive;
    private bool isLoading;
    private bool isReplaceConformed;
    private bool chartParsePending;
    private BasicParseErrorRenderer basicParseErrorRenderer = null!;
    private bool suppressLevelTextChange;
    private bool immediateWaveRefreshQueued;
    private Task<string?>? timelineAudioBuildTask;
    private string? timelineAudioSourcePath;
    private string? loadedTrackPath;
    private int timelineAudioBuildGeneration;
    private object? timelineDisplaySource;
    private object? timelineEffectSource;
    private object? timelineSubtitleSource;
    private object? timelineMediaSource;
    private readonly List<TimelineOverlayItem> timelineOverlayCache = new();
    private int waveRedrawQueued;
    private int waveResizeQueued;
    private object? cachedWaveTimingList;
    private object? cachedWaveMeterList;
    private object? cachedWaveNoteList;
    private double cachedWaveMaxVisualDuration;
    private double cachedWaveSongEnd = double.NaN;
    private List<double> cachedStrongBeats = new();
    private List<double> cachedWeakBeats = new();
    // Serialize preview, playback, pause, and stop requests so View observes editor order.
    private Task<bool>? pendingPlaySend;
    // A waveform seek invalidates the View's current judge queues. The Stop request
    // is sent immediately; a subsequent Play waits for this exact request before Run.
    private Task<bool>? pendingScrubStop;
    // Preview requests use the same View endpoint as playback. Keep the in-flight send
    // as a barrier so Stop and Start can never overtake a late preview request.
    private Task<bool>? pendingNotePreviewSend;
    // Invalidates delayed control requests after a newer user action.
    private int viewControlGeneration;
    // Treat an in-flight pause as paused when deciding whether a seek needs a full stop.
    private bool pausePending;
    // Options for the current RecordVideoWindow capture pass.
    internal RecordVideoOptions? pendingRecordOptions;
    private double? flowTimelineCursor;
    private bool flowPreviewActive;
    private bool flowPreviewAwaitingView;
    private DateTime flowPreviewStartedAt;
    private double flowPreviewStartTime;
    private int flowPreviewGeneration;
    private int notePreviewGeneration;
    private string? lastNotePreviewKey;
    private int lastSyntaxValidationSlotStart = -1;
    private const double RecordingIntroDuration = 5d;
    // Assets/Animation/Enter.anim is 3.1166666 seconds long.
    private const double AllPerfectDuration = 3.1166666d;

    private bool isSaved = true;
    private EditorControlMethod lastEditorState = EditorControlMethod.Stop;
    private FumenEditorAdapter fumenEditor = null!;
    private EditorSelection? lastFindPosition;

    private double lastMousePointX; //Used for drag scroll

    private int selectedDifficulty = -1;
    private double songLength;

    private SoundSetting soundSetting = new();
    private bool UpdateCheckLock;


    //*UI DRAWING
    private readonly Timer visualEffectRefreshTimer = new(33);

    private WriteableBitmap? WaveBitmap;

    //*TEXTBOX CONTROL
    private string GetRawFumenText()
    {
        return fumenEditor.Text;
    }

    private void SetRawFumenText(string content)
    {
        isLoading = true;
        fumenEditor.Text = content ?? string.Empty;
        isLoading = false;
    }

    private long GetRawFumenPosition()
    {
        return fumenEditor.CaretOffset;
    }

    private void SeekTextFromTime()
    {
        //Console.WriteLine("SeekText");
        var time = Bass.BASS_ChannelBytes2Seconds(bgmStream, Bass.BASS_ChannelGetPosition(bgmStream));
        var timingList = new List<SimaiTimingPoint>();
        timingList.AddRange(SimaiProcess.timinglist);
        var noteList = SimaiProcess.notelist;
        if (SimaiProcess.timinglist.Count <= 0) return;
        timingList.Sort((x, y) => Math.Abs(time - x.time).CompareTo(Math.Abs(time - y.time)));
        var theNote = timingList[0];
        timingList.Clear();
        timingList.AddRange(SimaiProcess.timinglist);
        var indexOfTheNote = timingList.IndexOf(theNote);
        fumenEditor.SelectLineColumn(theNote.rawTextPositionY, theNote.rawTextPositionX);
    }

    private void SeekTextFromIndex(int noteGroupIndex)
    {
        if (SimaiProcess.notelist.Count > noteGroupIndex + 1 && noteGroupIndex >= 0)
        {
            var theNote = SimaiProcess.notelist[noteGroupIndex];
            fumenEditor.SelectLineColumn(theNote.rawTextPositionY, theNote.rawTextPositionX);
        }
    }

    public void ScrollToFumenContentSelection(int positionX, int positionY)
    {
        // Allows other windows to scroll this view without exposing its many private fields.
        fumenEditor.Focus();
        fumenEditor.SelectLineColumn(positionY, positionX);
        Focus();

        if (Bass.BASS_ChannelIsActive(bgmStream) == BASSActive.BASS_ACTIVE_PLAYING && (bool)FollowPlayCheck.IsChecked!)
            return;
        var time = SimaiProcess.Serialize(GetRawFumenText(), GetRawFumenPosition());
        SetBgmPosition(time);
        //Console.WriteLine("SelectionChanged");
        SimaiProcess.ClearNoteListPlayedState();
        ghostCusorPositionTime = (float)time;
    }

    //*FIND AND REPLACE
    private void Find_icon_MouseDown(object? sender, MouseButtonEventArgs e)
    {
        FindAndScroll();
    }

    private void Replace_icon_MouseDown(object? sender, MouseButtonEventArgs e)
    {
        if (!isReplaceConformed)
        {
            FindAndScroll();
            return;
        }

        if (lastFindPosition == fumenEditor.Selection)
        {
            fumenEditor.ReplaceSelection(ReplaceText.Text);
            FindAndScroll();
        }
        else
        {
            isReplaceConformed = false;
        }
    }

    public void FindAndScroll()
    {
        var position = fumenEditor.FindNext(InputText.Text);
        if (position < 0)
        {
            isReplaceConformed = false;
            return;
        }

        fumenEditor.Select(position, InputText.Text.Length);
        lastFindPosition = fumenEditor.Selection;
        fumenEditor.Focus();
        isReplaceConformed = true;
    }

    //*FILE CONTROL
    private void initFromFile(string path) //file name should not be included in path
    {
        if (soundSetting != null) soundSetting.Close();
        if (editorSetting == null) ReadEditorSetting();

        var useOgg = File.Exists(path + "/track.ogg");

        var originalAudioPath = path + "/track" + (useOgg ? ".ogg" : ".mp3");
        var dataPath = path + "/maidata.txt";
        if (!File.Exists(originalAudioPath))
        {
            MessageBox.Show(GetLocalizedString("NoTrack"), GetLocalizedString("Error"));
            return;
        }

        if (!File.Exists(dataPath))
        {
            MessageBox.Show(GetLocalizedString("NoMaidata_txt"), GetLocalizedString("Error"));
            return;
        }

        maidataDir = path;
        timelineAudioSourcePath = MediaTools.FindCachedTimelineAudio(path);
        var audioPath = timelineAudioSourcePath ?? originalAudioPath;
        SafeTerminationDetector.Of().ChangePath(maidataDir);
        SetRawFumenText("");
        if (bgmStream != -1024)
        {
            Bass.BASS_ChannelStop(bgmStream);
            Bass.BASS_StreamFree(bgmStream);
        }

        //soundSetting.Close();
        var decodeStream = Bass.BASS_StreamCreateFile(audioPath, 0L, 0L, BASSFlag.BASS_STREAM_DECODE | BASSFlag.BASS_STREAM_PRESCAN);
        bgmStream = BassFx.BASS_FX_TempoCreate(decodeStream, BASSFlag.BASS_FX_FREESOURCE);
        loadedTrackPath = audioPath;
        //Bass.BASS_StreamCreateFile(audioPath, 0L, 0L, BASSFlag.BASS_SAMPLE_FLOAT);

        Bass.BASS_ChannelSetAttribute(bgmStream, BASSAttribute.BASS_ATTRIB_VOL, editorSetting!.Default_BGM_Level);
        Bass.BASS_ChannelSetAttribute(trackStartStream, BASSAttribute.BASS_ATTRIB_VOL, editorSetting!.Default_BGM_Level);
        Bass.BASS_ChannelSetAttribute(allperfectStream, BASSAttribute.BASS_ATTRIB_VOL, editorSetting!.Default_BGM_Level);
        Bass.BASS_ChannelSetAttribute(fanfareStream, BASSAttribute.BASS_ATTRIB_VOL, editorSetting!.Default_BGM_Level);
        Bass.BASS_ChannelSetAttribute(clockStream, BASSAttribute.BASS_ATTRIB_VOL, editorSetting!.Default_BGM_Level);
        Bass.BASS_ChannelSetAttribute(answerStream, BASSAttribute.BASS_ATTRIB_VOL, editorSetting!.Default_Answer_Level);
        Bass.BASS_ChannelSetAttribute(judgeStream, BASSAttribute.BASS_ATTRIB_VOL, editorSetting!.Default_Judge_Level);
        Bass.BASS_ChannelSetAttribute(judgeBreakStream, BASSAttribute.BASS_ATTRIB_VOL,
            editorSetting!.Default_Break_Level);
        Bass.BASS_ChannelSetAttribute(judgeBreakSlideStream, BASSAttribute.BASS_ATTRIB_VOL,
            editorSetting!.Default_Break_Slide_Level);
        Bass.BASS_ChannelSetAttribute(slideStream, BASSAttribute.BASS_ATTRIB_VOL, editorSetting!.Default_Slide_Level);
        Bass.BASS_ChannelSetAttribute(breakSlideStartStream, BASSAttribute.BASS_ATTRIB_VOL,
            editorSetting!.Default_Slide_Level);
        Bass.BASS_ChannelSetAttribute(breakStream, BASSAttribute.BASS_ATTRIB_VOL, editorSetting!.Default_Break_Level);
        Bass.BASS_ChannelSetAttribute(breakSlideStream, BASSAttribute.BASS_ATTRIB_VOL,
            editorSetting!.Default_Break_Slide_Level);
        Bass.BASS_ChannelSetAttribute(judgeExStream, BASSAttribute.BASS_ATTRIB_VOL, editorSetting!.Default_Ex_Level);
        Bass.BASS_ChannelSetAttribute(touchStream, BASSAttribute.BASS_ATTRIB_VOL, editorSetting!.Default_Touch_Level);
        Bass.BASS_ChannelSetAttribute(hanabiStream, BASSAttribute.BASS_ATTRIB_VOL, editorSetting!.Default_Hanabi_Level);
        Bass.BASS_ChannelSetAttribute(holdRiserStream, BASSAttribute.BASS_ATTRIB_VOL,
            editorSetting!.Default_Hanabi_Level);
        var info = Bass.BASS_ChannelGetInfo(bgmStream);
        if (info.freq != 44100) MessageBox.Show(GetLocalizedString("Warn44100Hz"), GetLocalizedString("Attention"));
        ReadWaveFromFile();
        SimaiProcess.ClearData();

        if (!SimaiProcess.ReadData(dataPath)) return;


        LevelSelector.SelectedItem = LevelSelector.Items[0];
        ReadSetting();
        chartParsePending = true;
        SetRawFumenText(SimaiProcess.fumens[selectedDifficulty]);
        SimaiProcess.Serialize(GetRawFumenText());
        SeekTextFromTime();
        chartParsePending = false;
        FumenContent.Focus();
        DrawWave();

        OffsetTextBox.Text = SimaiProcess.first.ToString();

        Cover.Visibility = Visibility.Collapsed;
        MenuEdit.IsEnabled = true;
        Menu_ExportNoAlpha.IsEnabled = true;
        VolumnSetting.IsEnabled = true;
        MenuMuriCheck.IsEnabled = true;
        Menu_ExportRender.IsEnabled = true;
        SyntaxCheckButton.IsEnabled = true;
        AutoSaveManager.Of().SetAutoSaveEnable(true);
        SetSavedState(true);
        SyntaxCheck(false);
        SchedulePreBakeSongDetail();
        editorSetting!.LastChartPath = maidataDir;
        SaveEditorSetting();
        QueueMediaTimelineWaveformRefreshFromDisk();
    }

    private void TryOpenLastChart()
    {
        var path = editorSetting?.LastChartPath;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;
        if (!File.Exists(Path.Combine(path, "maidata.txt")) ||
            (!File.Exists(Path.Combine(path, "track.ogg")) &&
             !File.Exists(Path.Combine(path, "track.mp3"))))
            return;

        try
        {
            initFromFile(path);
        }
        catch (Exception error)
        {
            Console.WriteLine(error);
        }
    }

        // Validate after the deferred chart parse so diagnostics use current line offsets.
    internal void SyntaxCheck(bool suppressActiveSlot = true)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (isLoading || string.IsNullOrEmpty(GetRawFumenText()))
            {
                ClearBasicParseErrors();
                return;
            }

            if (chartParsePending)
            {
                SimaiProcess.Serialize(GetRawFumenText());
                chartParsePending = false;
            }

            var errors = ValidateTimingsForView(SimaiProcess.notelist);
            errors.AddRange(SimaiProcess.ValidateAlphaCommands(GetRawFumenText())
                .Select(error => new BasicParseError(error.PositionX, error.PositionY, error.Message)));
            errors = errors
                .GroupBy(error => error.PositionY)
                .Select(group => group.First())
                .ToList();
            if (suppressActiveSlot)
                errors = SuppressActiveTimingSlotErrors(errors);
            if (errors.Count > 0)
                SetBasicParseErrors(errors);
            else
                ClearBasicParseErrors();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    void SetErrCount<T>(T eCount) => Dispatcher.Invoke(() => ErrCount.Content = $"{eCount}");

    private sealed record BasicParseError(int PositionX, int PositionY, string Message);

    private List<BasicParseError> SuppressActiveTimingSlotErrors(List<BasicParseError> errors)
    {
        if (errors.Count == 0 || !FumenContent.IsKeyboardFocusWithin)
            return errors;

        var source = GetRawFumenText();
        var caret = Math.Clamp((int)GetRawFumenPosition(), 0, source.Length);
        var (slotStart, slotEnd) = GetTimingSlotBounds(source, caret);
        return errors.Where(error =>
        {
            var offset = GetTextOffset(source, error.PositionY, error.PositionX);
            return offset < slotStart || offset >= slotEnd;
        }).ToList();
    }

    private void RefreshSyntaxValidationSlot()
    {
        if (isPlaying || !FumenContent.IsKeyboardFocusWithin)
            return;

        var source = GetRawFumenText();
        var caret = Math.Clamp((int)GetRawFumenPosition(), 0, source.Length);
        var (slotStart, _) = GetTimingSlotBounds(source, caret);
        if (slotStart == lastSyntaxValidationSlotStart)
            return;

        lastSyntaxValidationSlotStart = slotStart;
        SyntaxCheck();
    }

    private static (int Start, int End) GetTimingSlotBounds(string source, int caret)
    {
        var commas = FindTimingCommas(source);
        var start = 0;
        var end = source.Length;
        foreach (var comma in commas)
        {
            if (comma < caret)
                start = Math.Max(start, comma + 1);
            else
                end = Math.Min(end, comma);
        }
        return (start, end);
    }

    private static int GetTextOffset(string source, int line, int column)
    {
        var offset = 0;
        for (var currentLine = 0; currentLine < line && offset < source.Length; currentLine++)
        {
            var newline = source.IndexOf('\n', offset);
            if (newline < 0)
                return source.Length;
            offset = newline + 1;
        }

        var lineEnd = source.IndexOf('\n', offset);
        if (lineEnd < 0)
            lineEnd = source.Length;
        return Math.Clamp(offset + Math.Max(0, column), offset, lineEnd);
    }

    private void SetBasicParseErrors(IEnumerable<BasicParseError> errors)
    {
        var errorList = errors.ToList();
        Dispatcher.Invoke(() =>
        {
            basicParseErrorRenderer.SetErrors(errorList.Select(e => (e.PositionY, e.Message)));
            ErrCount.Visibility = errorList.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ErrCount_Label.Visibility = errorList.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ErrCount.Content = errorList.Count.ToString(CultureInfo.InvariantCulture);
            if (errorList.Count > 0)
                PetStatusClient.Notify("error",
                    string.Format(GetLocalizedString("FoundSyntaxErrors"), errorList.Count));
        });
    }

    private void ClearBasicParseErrors()
    {
        Dispatcher.Invoke(() =>
        {
            basicParseErrorRenderer.Clear();
            ErrCount.Visibility = Visibility.Collapsed;
            ErrCount_Label.Visibility = Visibility.Collapsed;
            ErrCount.Content = "0";
        });
    }
    private void ReadWaveFromFile()
    {
        densityAudioEnvelopeSource = null;
        densityAudioEnvelope = Array.Empty<float>();
        var useOgg = File.Exists(maidataDir + "/track.ogg");
        var bgmDecode = Bass.BASS_StreamCreateFile(maidataDir + "/track" + (useOgg ? ".ogg" : ".mp3"), 0L, 0L, BASSFlag.BASS_STREAM_DECODE);
        try
        {
            songLength = Bass.BASS_ChannelBytes2Seconds(bgmDecode,
                Bass.BASS_ChannelGetLength(bgmDecode, BASSMode.BASS_POS_BYTE));
            waveformDisplayLength = songLength;
/*                int sampleNumber = (int)((songLength * 1000) / (0.02f * 1000));
                wavedBs = new float[sampleNumber];
                for (int i = 0; i < sampleNumber; i++)
                {
                    wavedBs[i] = Bass.BASS_ChannelGetLevels(bgmDecode, 0.02f, BASSLevel.BASS_LEVEL_MONO)[0];
                }*/
            Bass.BASS_StreamFree(bgmDecode);
            var bgmSample = Bass.BASS_SampleLoad(maidataDir + "/track" + (useOgg ? ".ogg" : ".mp3"), 0, 0, 1, BASSFlag.BASS_DEFAULT);
            try
            {
                var bgmInfo = Bass.BASS_SampleGetInfo(bgmSample);
                var freq = bgmInfo.freq;
                var sampleCount = (long)(songLength * freq * 2);
                var bgmRAW = new short[sampleCount];
                Bass.BASS_SampleGetData(bgmSample, bgmRAW);

                waveRaws[0] = new short[sampleCount / 20 + 1];
                for (var i = 0; i < sampleCount; i = i + 20) waveRaws[0][i / 20] = bgmRAW[i];
                waveRaws[1] = new short[sampleCount / 50 + 1];
                for (var i = 0; i < sampleCount; i = i + 50) waveRaws[1][i / 50] = bgmRAW[i];
                waveRaws[2] = new short[sampleCount / 100 + 1];
                for (var i = 0; i < sampleCount; i = i + 100) waveRaws[2][i / 100] = bgmRAW[i];
            }
            finally
            {
                if (bgmSample != 0)
                    Bass.BASS_SampleFree(bgmSample);
            }
        }
        catch (Exception e)
        {
            MessageBox.Show(string.Format(
                GetLocalizedString("AudioDecodeFailed"),
                e.Message + Bass.BASS_ErrorGetCode()));
            Bass.BASS_StreamFree(bgmDecode);
            Process.Start("https://github.com/LingFeng-bbben/MajdataEdit/issues/26");
        }
    }

    private void SetSavedState(bool state)
    {
        if (state && MediaTimelinePanel.HasPendingChanges)
            state = false;
        LevelSelector.Opacity = state ? 1d : 0.68d;
        UnsavedDifficultyFrame.Visibility = state ? Visibility.Collapsed : Visibility.Visible;
        if (state)
        {
            isSaved = true;
            LevelSelector.IsEnabled = true;
            LevelSelector.ToolTip = null;
            TheWindow.Title = GetWindowsTitleString(SimaiProcess.title!);
        }
        else
        {
            isSaved = false;
            LevelSelector.IsEnabled = false;
            LevelSelector.ToolTip = GetLocalizedString("Unsaved");
            TheWindow.Title = GetWindowsTitleString(GetLocalizedString("Unsaved") + SimaiProcess.title!);
            AutoSaveManager.Of().SetFileChanged();
        }
    }

    /// <summary>
    ///     Ask the user and save fumen.
    /// </summary>
    /// <returns>Return false if user cancel the action</returns>
    private bool AskSave()
    {
        var result = MessageBox.Show(GetLocalizedString("AskSave"), GetLocalizedString("Warning"),
            MessageBoxButton.YesNoCancel);
        if (result == MessageBoxResult.Yes)
        {
            SaveFumen(true);
            return isSaved;
        }

        if (result == MessageBoxResult.Cancel) return false;
        MediaTimelinePanel.DiscardPendingChanges();
        return true;
    }

    private void SaveFumen(bool writeToDisk = false)
    {
        if (selectedDifficulty == -1) return;
        SimaiProcess.fumens[selectedDifficulty] = GetRawFumenText();
        SimaiProcess.first = float.Parse(OffsetTextBox.Text);
        if (maidataDir == "")
        {
            var saveDialog = new SaveFileDialog
            {
                Filter = "maidata.txt|maidata.txt",
                OverwritePrompt = true
            };
            if ((bool)saveDialog.ShowDialog()!) maidataDir = new FileInfo(saveDialog.FileName).DirectoryName!;
        }

        SimaiProcess.SaveData(maidataDir + "/maidata.bak.txt");
        SaveSetting();
        if (writeToDisk)
        {
            SimaiProcess.SaveData(maidataDir + "/maidata.txt");
            if (!MediaTimelinePanel.CommitPendingChanges())
                return;
            SetSavedState(true);
        }
    }

    private void ExportNoAlphaFumen()
    {
        if (selectedDifficulty == -1)
            return;

        var source = GetRawFumenText();
        if (string.IsNullOrWhiteSpace(source))
        {
            MessageBox.Show(GetLocalizedString("EmptyChart"), GetLocalizedString("Attention"));
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "maidata.txt|*.txt|Text file|*.txt|All files|*.*",
            FileName = "maidata_no_effect.txt",
            OverwritePrompt = true,
            InitialDirectory = string.IsNullOrWhiteSpace(maidataDir) ? null : maidataDir
        };
        if (dialog.ShowDialog() != true)
            return;

        var cleaned = StripAlphaOnlySyntax(source);
        File.WriteAllText(dialog.FileName, cleaned, Encoding.UTF8);
    }

    private static string StripAlphaOnlySyntax(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            if (IsEditorBackgroundColorLine(trimmed))
            {
                lines[i] = "";
                continue;
            }

            var commentStart = line.IndexOf("||", StringComparison.Ordinal);
            if (commentStart >= 0)
                line = line.Substring(0, commentStart);

            lines[i] = RemoveAlphaAngleCommands(line);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static bool IsEditorBackgroundColorLine(string trimmed)
    {
        if (trimmed.Length == 0)
            return false;

        if (trimmed[0] == '@')
            return true;
        if (trimmed[0] != '&')
            return false;

        var value = trimmed.Substring(1);
        if (value.Equals("NULL", StringComparison.OrdinalIgnoreCase))
            return true;
        if (value.Length != 6)
            return false;
        return value.All(Uri.IsHexDigit);
    }

    private static string RemoveAlphaAngleCommands(string line)
    {
        if (string.IsNullOrEmpty(line))
            return line;

        var builder = new StringBuilder(line.Length);
        for (var i = 0; i < line.Length;)
        {
            if (line[i] == '<' && IsAlphaCommandStart(line, i, out var close))
            {
                i = close + 1;
                continue;
            }

            builder.Append(line[i]);
            i++;
        }

        return builder.ToString();
    }

    private static bool IsAlphaCommandStart(string text, int openIndex, out int closeIndex)
    {
        return AlphaCommandBoundary.TryGetCommand(text, openIndex, out closeIndex);
    }

    private void SaveSetting()
    {
        if (maidataDir == "") return;
        var setting = new MajSetting
        {
            lastEditDiff = selectedDifficulty,
            lastEditTime = Bass.BASS_ChannelBytes2Seconds(bgmStream, Bass.BASS_ChannelGetPosition(bgmStream))
        };
        Bass.BASS_ChannelGetAttribute(bgmStream, BASSAttribute.BASS_ATTRIB_VOL, ref setting.BGM_Level);
        Bass.BASS_ChannelGetAttribute(answerStream, BASSAttribute.BASS_ATTRIB_VOL, ref setting.Answer_Level);
        Bass.BASS_ChannelGetAttribute(judgeStream, BASSAttribute.BASS_ATTRIB_VOL, ref setting.Judge_Level);
        Bass.BASS_ChannelGetAttribute(judgeBreakStream, BASSAttribute.BASS_ATTRIB_VOL, ref setting.Break_Level);
        Bass.BASS_ChannelGetAttribute(breakSlideStream, BASSAttribute.BASS_ATTRIB_VOL, ref setting.Break_Slide_Level);
        Bass.BASS_ChannelGetAttribute(judgeExStream, BASSAttribute.BASS_ATTRIB_VOL, ref setting.Ex_Level);
        Bass.BASS_ChannelGetAttribute(touchStream, BASSAttribute.BASS_ATTRIB_VOL, ref setting.Touch_Level);
        Bass.BASS_ChannelGetAttribute(slideStream, BASSAttribute.BASS_ATTRIB_VOL, ref setting.Slide_Level);
        Bass.BASS_ChannelGetAttribute(hanabiStream, BASSAttribute.BASS_ATTRIB_VOL, ref setting.Hanabi_Level);
        var json = JsonConvert.SerializeObject(setting);
        File.WriteAllText(maidataDir + "/" + majSettingFilename, json);
    }

    private void ReadSetting()
    {
        var path = maidataDir + "/" + majSettingFilename;
        if (!File.Exists(path)) return;
        var setting = JsonConvert.DeserializeObject<MajSetting>(File.ReadAllText(path));
        LevelSelector.SelectedIndex = setting!.lastEditDiff;
        selectedDifficulty = setting.lastEditDiff;
        SetBgmPosition(setting.lastEditTime);
        Bass.BASS_ChannelSetAttribute(bgmStream, BASSAttribute.BASS_ATTRIB_VOL, setting.BGM_Level);
        Bass.BASS_ChannelSetAttribute(trackStartStream, BASSAttribute.BASS_ATTRIB_VOL, setting.BGM_Level);
        Bass.BASS_ChannelSetAttribute(allperfectStream, BASSAttribute.BASS_ATTRIB_VOL, setting.BGM_Level);
        Bass.BASS_ChannelSetAttribute(fanfareStream, BASSAttribute.BASS_ATTRIB_VOL, setting.BGM_Level);
        Bass.BASS_ChannelSetAttribute(clockStream, BASSAttribute.BASS_ATTRIB_VOL, setting.BGM_Level);
        Bass.BASS_ChannelSetAttribute(answerStream, BASSAttribute.BASS_ATTRIB_VOL, setting.Answer_Level);
        Bass.BASS_ChannelSetAttribute(judgeStream, BASSAttribute.BASS_ATTRIB_VOL, setting.Judge_Level);
        Bass.BASS_ChannelSetAttribute(judgeBreakStream, BASSAttribute.BASS_ATTRIB_VOL, setting.Break_Level);
        Bass.BASS_ChannelSetAttribute(judgeBreakSlideStream, BASSAttribute.BASS_ATTRIB_VOL, setting.Break_Slide_Level);
        Bass.BASS_ChannelSetAttribute(slideStream, BASSAttribute.BASS_ATTRIB_VOL, setting.Slide_Level);
        Bass.BASS_ChannelSetAttribute(breakSlideStartStream, BASSAttribute.BASS_ATTRIB_VOL, setting.Slide_Level);
        Bass.BASS_ChannelSetAttribute(breakStream, BASSAttribute.BASS_ATTRIB_VOL, setting.Break_Level);
        Bass.BASS_ChannelSetAttribute(breakSlideStream, BASSAttribute.BASS_ATTRIB_VOL, setting.Break_Slide_Level);
        Bass.BASS_ChannelSetAttribute(judgeExStream, BASSAttribute.BASS_ATTRIB_VOL, setting.Ex_Level);
        Bass.BASS_ChannelSetAttribute(touchStream, BASSAttribute.BASS_ATTRIB_VOL, setting.Touch_Level);
        Bass.BASS_ChannelSetAttribute(hanabiStream, BASSAttribute.BASS_ATTRIB_VOL, setting.Hanabi_Level);
        Bass.BASS_ChannelSetAttribute(holdRiserStream, BASSAttribute.BASS_ATTRIB_VOL, setting.Hanabi_Level);

        SaveSetting(); // Overwrite settings from older versions.
    }

    private void CreateNewFumen(string path)
    {
        if (File.Exists(path + "/maidata.txt"))
            MessageBox.Show(GetLocalizedString("MaidataExist"));
        else
            File.WriteAllText(path + "/maidata.txt",
                "&title=" + GetLocalizedString("SetTitle") + "\n" +
                "&artist=" + GetLocalizedString("SetArtist") + "\n" +
                "&des=" + GetLocalizedString("SetDes") + "\n" +
                "&first=0\n" +
                "|*\n" + GetLocalizedString("NewChartHint") + "\n*|\n");
    }

    private void CreateEditorSetting()
    {
        editorSetting = new EditorSetting
        {
            RenderMode =
            RenderOptions.ProcessRenderMode == RenderMode.SoftwareOnly ? 1 : 0 // Keep the setting in sync when software rendering is forced via the command line.
        };

        File.WriteAllText(editorSettingFilename, JsonConvert.SerializeObject(editorSetting, Formatting.Indented));

        var esp = new EditorSettingPanel(true)
        {
            Owner = this
        };
        esp.ShowDialog();
    }

    private void ReadEditorSetting()
    {
        if (!File.Exists(editorSettingFilename)) CreateEditorSetting();
        var json = File.ReadAllText(editorSettingFilename);
        var storedSettings = JObject.Parse(json);
        editorSetting = JsonConvert.DeserializeObject<EditorSetting>(json)!;
        if (!storedSettings.ContainsKey(nameof(EditorSetting.FontPresetVersion)))
        {
            editorSetting.ViewDisplayFontPreset = editorSetting.ViewDisplayFontPreset switch
            {
                1 => 9,
                2 => 10,
                3 => 4,
                4 => 3,
                _ => 0
            };
            editorSetting.FontPresetVersion = 1;
        }
        editorSetting.Skin = string.IsNullOrWhiteSpace(editorSetting.Skin) ? "dx" : editorSetting.Skin;
        if (!storedSettings.ContainsKey(nameof(EditorSetting.TapSkin)))
            editorSetting.TapSkin = editorSetting.Skin;
        if (!storedSettings.ContainsKey(nameof(EditorSetting.HoldSkin)))
            editorSetting.HoldSkin = editorSetting.Skin;
        if (!storedSettings.ContainsKey(nameof(EditorSetting.StarSkin)))
            editorSetting.StarSkin = editorSetting.Skin;
        editorSetting.TapSkin = string.IsNullOrWhiteSpace(editorSetting.TapSkin) ? editorSetting.Skin : editorSetting.TapSkin;
        editorSetting.HoldSkin = string.IsNullOrWhiteSpace(editorSetting.HoldSkin) ? editorSetting.Skin : editorSetting.HoldSkin;
        editorSetting.StarSkin = string.IsNullOrWhiteSpace(editorSetting.StarSkin) ? editorSetting.Skin : editorSetting.StarSkin;
        const string legacyPinkSuffix = "-pink";
        if (editorSetting.StarSkin.EndsWith(legacyPinkSuffix, StringComparison.OrdinalIgnoreCase))
        {
            editorSetting.StarSkin = editorSetting.StarSkin[..^legacyPinkSuffix.Length];
            editorSetting.PinkStar = true;
        }
        if (editorSetting.InnerBackgroundCover < 0f)
            editorSetting.InnerBackgroundCover = editorSetting.backgroundCover;
        if (editorSetting.OuterBackgroundCover < 0f)
            editorSetting.OuterBackgroundCover = editorSetting.backgroundCover;

        if (RenderOptions.ProcessRenderMode != RenderMode.SoftwareOnly)
            // Use the configured render mode unless the command line specified one.
            RenderOptions.ProcessRenderMode =
                editorSetting.RenderMode == 0 ? RenderMode.Default : RenderMode.SoftwareOnly;
        else
            // Override the setting when software rendering was specified via the command line.
            editorSetting.RenderMode = 1;

        LocalizeDictionary.Instance.Culture = new CultureInfo(editorSetting.Language);
        AddGesture(editorSetting.PlayPauseKey, "PlayAndPause");
        AddGesture(editorSetting.PlayStopKey, "StopPlaying");
        AddGesture(editorSetting.SaveKey, "SaveFile");
        AddGesture(editorSetting.SendViewerKey, "SendToView");
        AddGesture(editorSetting.IncreasePlaybackSpeedKey, "IncreasePlaybackSpeed");
        AddGesture(editorSetting.DecreasePlaybackSpeedKey, "DecreasePlaybackSpeed");
        AddGesture("Ctrl+f", "Find");
        AddGesture(editorSetting.MirrorLeftRightKey, "MirrorLR");
        AddGesture(editorSetting.MirrorUpDownKey, "MirrorUD");
        AddGesture(editorSetting.Mirror180Key, "Mirror180");
        AddGesture(editorSetting.Mirror45Key, "Mirror45");
        AddGesture(editorSetting.MirrorCcw45Key, "MirrorCcw45");
        FumenContent.FontSize = editorSetting.FontSize;
        ApplyEditorAppearance();

        ViewerSpeed.Content = editorSetting.playSpeed.ToString("F1"); // Format the speed as "7.0", "9.5", etc.
        ViewerTouchSpeed.Content = editorSetting.touchSpeed.ToString("F1");

        chartChangeTimer.Interval = editorSetting.ChartRefreshDelay; // Set the refresh delay.

        SaveEditorSetting(); // Overwrite settings from older versions.
    }

    public void SaveEditorSetting()
    {
        File.WriteAllText(editorSettingFilename, JsonConvert.SerializeObject(editorSetting, Formatting.Indented));
    }

    private static bool IsLightEditorTheme(string? themeName) =>
        string.Equals(themeName, "light", StringComparison.OrdinalIgnoreCase);

    internal void ApplyEditorAppearance()
    {
        if (editorSetting == null)
            return;

        var theme = ThemeManager.LoadThemeByName(editorSetting.EditorTheme);
        ThemeManager.ApplyApplicationResources(theme);
        EditorDecorationBg.Apply(DecorationBgHost, editorSetting.EditorBackgroundStyle);
        FumenContent.FontWeight = FontWeights.Normal;
        FumenContent.FontFamily = editorSetting.EditorFontPreset switch
        {
            0 => new System.Windows.Media.FontFamily("Consolas"),
            1 => new System.Windows.Media.FontFamily("Cascadia Mono, JetBrains Mono, Cascadia Code, Consolas"),
            2 => new System.Windows.Media.FontFamily("Cascadia Code, Consolas"),
            3 => new System.Windows.Media.FontFamily("Microsoft YaHei UI"),
            4 => new System.Windows.Media.FontFamily("Noto Sans SC, Microsoft YaHei UI"),
            5 => new System.Windows.Media.FontFamily("NSimSun, SimSun"),
            6 => new System.Windows.Media.FontFamily("DengXian, Microsoft YaHei UI"),
            7 => new System.Windows.Media.FontFamily("Noto Serif SC, SimSun"),
            8 => new System.Windows.Media.FontFamily("Global Monospace, Consolas"),
            9 => LoadBundledEditorFont("Aileron-Regular.otf", "Aileron", "Segoe UI"),
            10 => LoadBundledEditorFont("Allerta-Regular.ttf", "Allerta", "Segoe UI"),
            _ => new System.Windows.Media.FontFamily("Cascadia Mono, JetBrains Mono, Cascadia Code, Consolas")
        };
        FumenContent.FontSize = Math.Clamp(editorSetting.FontSize, 8f, 32f);
        var textView = FumenContent.TextArea.TextView;
        textView.ClearValue(TextBlock.LineHeightProperty);
        textView.ClearValue(TextBlock.LineStackingStrategyProperty);
        textView.UpdateLayout();
        TextBlock.SetLineHeight(textView, textView.DefaultLineHeight * 1.10d);
        TextBlock.SetLineStackingStrategy(textView, LineStackingStrategy.BlockLineHeight);
        ThemeManager.ApplyEditor(FumenContent, theme);
    }

    private static System.Windows.Media.FontFamily LoadBundledEditorFont(
        string fileName,
        string familyName,
        string fallback)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Resources", "Fonts");
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
            return new System.Windows.Media.FontFamily(fallback);

        var directoryUri = new Uri(directory + Path.DirectorySeparatorChar, UriKind.Absolute);
        return new System.Windows.Media.FontFamily(directoryUri, $"./#{familyName}");
    }

    internal void PreviewEditorTheme(EditorTheme theme)
    {
        ThemeManager.ApplyApplicationResources(theme);
        ThemeManager.ApplyEditor(FumenContent, theme);
        DrawWave();
    }

    private void AddGesture(string keyGusture, string command)
    {
        var gesture = (InputGesture) new KeyGestureConverter().ConvertFromString(keyGusture)!;
        var inputBinding = new InputBinding((ICommand)FumenContent.Resources[command], gesture);
        FumenContent.InputBindings.Add(inputBinding);
    }

    // This update very freqently to Draw FFT wave.
    private void VisualEffectRefreshTimer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        try
        {
            DrawFFT();
            DrawWave();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }

    // Delayed chart-change parsing
    private void ChartChangeTimer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        Console.WriteLine("TextChanged");
        QueueImmediateWaveRefresh();
    }

    private void QueueImmediateWaveRefresh()
    {
        if (immediateWaveRefreshQueued)
            return;

        immediateWaveRefreshQueued = true;
        Dispatcher.InvokeAsync(() =>
        {
            immediateWaveRefreshQueued = false;
            if (isLoading || string.IsNullOrEmpty(GetRawFumenText()))
                return;
            ghostCusorPositionTime = (float)SimaiProcess.Serialize(
                GetRawFumenText(), GetRawFumenPosition());
            chartParsePending = false;
            DrawWave();
            SyntaxCheck();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void QueueNotePreview()
    {
        if (isLoading || isPlaying || lastEditorState != EditorControlMethod.Stop)
            return;

        notePreviewGeneration++;
        notePreviewTimer.Stop();
        notePreviewTimer.Start();
    }

    private void CancelNotePreview()
    {
        notePreviewGeneration++;
        notePreviewTimer.Stop();
        lastNotePreviewKey = null;
    }

    private void NotePreviewTimer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        var previousPreview = pendingNotePreviewSend;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        pendingNotePreviewSend = completion.Task;
        var generation = notePreviewGeneration;
        try
        {
            if (previousPreview != null)
                previousPreview.GetAwaiter().GetResult();

            string? requestJson = null;
            Dispatcher.Invoke(() =>
            {
                if (generation == notePreviewGeneration &&
                    !isLoading && !isPlaying && lastEditorState == EditorControlMethod.Stop)
                    requestJson = BuildNotePreviewRequestJson();
            });

            if (string.IsNullOrEmpty(requestJson) || generation != notePreviewGeneration ||
                isLoading || isPlaying || lastEditorState != EditorControlMethod.Stop)
            {
                completion.TrySetResult(true);
                return;
            }

            completion.TrySetResult(WebControl.RequestPOST("http://localhost:8013/", requestJson) != "ERROR");
        }
        catch
        {
            completion.TrySetResult(false);
        }
    }

    private string? BuildNotePreviewRequestJson()
    {
        var group = NotePreviewModule.ExtractNoteGroupAtCaret(GetRawFumenText(), (int)GetRawFumenPosition());
        var previewTimings = NotePreviewModule.ExpandPreviewTimings(group);
        var previewKey = string.Join("`", previewTimings.Select(timing => string.Join("/", timing)));
        if (string.Equals(previewKey, lastNotePreviewKey, StringComparison.Ordinal))
            return null;

        lastNotePreviewKey = previewKey;
        var request = new EditRequestjson
        {
            control = EditorControlMethod.Preview,
            language = editorSetting?.Language ?? "en-US",
            noteSpeed = editorSetting?.playSpeed ?? 7f,
            touchSpeed = editorSetting?.touchSpeed ?? 7.5f,
            starSpeed = editorSetting?.starSpeed ?? 0f,
            smoothSlideAnime = editorSetting?.SmoothSlideAnime ?? false,
            skin = editorSetting?.Skin ?? "dx",
            tapSkin = editorSetting?.TapSkin ?? editorSetting?.Skin ?? "dx",
            holdSkin = editorSetting?.HoldSkin ?? editorSetting?.Skin ?? "dx",
            starSkin = editorSetting?.StarSkin ?? editorSetting?.Skin ?? "dx",
            pinkStar = editorSetting?.PinkStar ?? false,
            standbyTheme = IsLightEditorTheme(editorSetting?.EditorTheme) ? "light" : "dark",
            introBgTheme = editorSetting?.ViewIntroStyle ?? "circleplus",
            backgroundFitMode = editorSetting?.BackgroundFitMode ?? 0,
            songDetailStyle = editorSetting?.SongDetailStyle ?? 1,
            showGeneratedMark = editorSetting?.ShowGeneratedMark ?? false,
            viewDisplayFontPreset = editorSetting?.ViewDisplayFontPreset ?? 0,
            enableVisualChartEditor = editorSetting?.EnableVisualChartEditor ?? true,
            editorPlayMethod = EditorPlayMethod.Disabled,
            previewJson = BuildNotePreviewMajsonJson(previewTimings)
        };
        return JsonConvert.SerializeObject(request);
    }

    private string? BuildNotePreviewMajsonJson(List<List<string>> previewTimings)
    {
        if (previewTimings == null || previewTimings.Count == 0)
            return null;

        var majson = new Majson
        {
            title = SimaiProcess.title ?? "",
            artist = SimaiProcess.artist ?? "",
            designer = selectedDifficulty >= 0 ? SimaiProcess.GetDesignerText(selectedDifficulty) : "",
            difficulty = selectedDifficulty >= 0 ? SimaiProcess.GetDifficultyText(selectedDifficulty) : "",
            diffNum = Math.Max(0, selectedDifficulty),
            level = selectedDifficulty >= 0 && selectedDifficulty < SimaiProcess.levels.Length
                ? SimaiProcess.levels[selectedDifficulty]
                : "1",
            wholeBpm = SimaiProcess.GetWholeBpmText()
        };
        majson.songDetailStyle = editorSetting?.SongDetailStyle ?? 1;
        for (var index = 0; index < previewTimings.Count; index++)
        {
            var branches = previewTimings[index]
                .SelectMany(note => note.Split('/'))
                .Where(IsPreviewBranchParseable)
                .Distinct()
                .ToList();
            if (branches.Count == 0)
                continue;

            // Pseudo-each uses the same 1/128-note interval as full chart serialization.
            var time = 0.001d + index * (1.875d / 120d);
            var timing = new SimaiTimingPoint(time, 0, 0, string.Join("/", branches), 120f);
            timing.noteList = timing.getNotes();
            if (IsPreviewNoteListValid(timing.noteList))
                majson.timingList.Add(timing);
        }
        if (majson.timingList.Count == 0)
            return null;
        return JsonConvert.SerializeObject(majson);
    }

    private static bool IsPreviewBranchParseable(string branch)
    {
        if (string.IsNullOrWhiteSpace(branch))
            return false;
        var timing = new SimaiTimingPoint(0.01d, 0, 0, branch, 120f);
        return IsPreviewNoteListValid(timing.getNotes());
    }

    private static bool IsPreviewNoteListValid(IReadOnlyCollection<SimaiNote> notes)
    {
        if (notes.Count == 0)
            return false;

        return notes.All(note =>
            note.noteType is SimaiNoteType.Touch or SimaiNoteType.TouchHold ||
            note.startPosition is >= 1 and <= 8);
    }

    private void DrawFFT()
    {
        Dispatcher.InvokeAsync(() =>
        {
            //Scroll WaveView
            var currentTime = GetTimelinePosition();
            //MusicWave.Margin = new Thickness(-currentTime / sampleTime * zoominPower, Margin.Left, MusicWave.Margin.Right, Margin.Bottom);
            //MusicWaveCusor.Margin = new Thickness(-currentTime / sampleTime * zoominPower, Margin.Left, MusicWave.Margin.Right, Margin.Bottom);

            var writableBitmap = new WriteableBitmap(255, 255, 72, 72, PixelFormats.Pbgra32, null);
            FFTImage.Source = writableBitmap;
            writableBitmap.Lock();
            var backBitmap = new Bitmap(255, 255, writableBitmap.BackBufferStride,
                PixelFormat.Format32bppArgb, writableBitmap.BackBuffer);

            var graphics = Graphics.FromImage(backBitmap);
            graphics.Clear(Color.Transparent);

            var fft = new float[1024];
            Bass.BASS_ChannelGetData(bgmStream, fft, (int)BASSData.BASS_DATA_FFT1024);
            var points = new PointF[1024];
            for (var i = 0; i < fft.Length; i++)
                points[i] = new PointF((float)Math.Log10(i + 1) * 100f, 240 - fft[i] * 256); //semilog

            graphics.DrawCurve(new Pen(Color.LightSkyBlue, 1), points);


            //no please
            /*
            var isSuccess = new Visuals().CreateSpectrumWave(bgmStream, graphics, new System.Drawing.Rectangle(0, 0, 255, 255),
                System.Drawing.Color.White, System.Drawing.Color.Red,
                System.Drawing.Color.Black, 1,
                false, false, false);
            Console.WriteLine(isSuccess);
            */
            graphics.Flush();
            graphics.Dispose();
            backBitmap.Dispose();

            writableBitmap.AddDirtyRect(new Int32Rect(0, 0, 255, 255));
            writableBitmap.Unlock();
        });
    }

    private void InitWave()
    {
        // Match the bitmap to the control in DIPs. The old 72-DPI bitmap was always
        // resampled by WPF (96/72), which blurred both the wave and timing lines.
        var width = Math.Max(1, (int)Math.Round(
            MusicWave.ActualWidth > 1d ? MusicWave.ActualWidth : Width - 2d));
        var height = Math.Max(1, (int)Math.Round(
            MusicWave.ActualHeight > 1d ? MusicWave.ActualHeight : MusicWave.Height));
        if (WaveBitmap != null && WaveBitmap.PixelWidth == width && WaveBitmap.PixelHeight == height)
            return;
        WaveBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Pbgra32, null);
        MusicWave.Source = WaveBitmap;
    }

    private void QueueWaveResize()
    {
        if (Interlocked.CompareExchange(ref waveResizeQueued, 1, 0) != 0)
            return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            var retry = false;
            try
            {
                if (isDrawing)
                {
                    retry = true;
                    return;
                }
                InitWave();
                DrawWaveCore();
            }
            finally
            {
                Interlocked.Exchange(ref waveResizeQueued, 0);
                if (retry)
                    QueueWaveResize();
            }
        }), System.Windows.Threading.DispatcherPriority.Render);
    }

    private void DrawWave()
    {
        if (WaveBitmap == null)
            return;

        if (Interlocked.CompareExchange(ref waveRedrawQueued, 1, 0) != 0)
            return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                DrawWaveCore();
            }
            finally
            {
                Interlocked.Exchange(ref waveRedrawQueued, 0);
            }
        }), System.Windows.Threading.DispatcherPriority.Render);
    }

    private void DrawWaveCore()
    {
        if (isDrawing || WaveBitmap == null || waveRaws[0] == null)
            return;

        isDrawing = true;
        var width = WaveBitmap.PixelWidth;
        var height = WaveBitmap.PixelHeight;

        WaveBitmap.Lock();
        try
        {
            using var backBitmap = new Bitmap(width, height, WaveBitmap.BackBufferStride,
                PixelFormat.Format32bppArgb, WaveBitmap.BackBuffer);
            using var graphics = Graphics.FromImage(backBitmap);
            graphics.SmoothingMode = SmoothingMode.None;
            graphics.PixelOffsetMode = PixelOffsetMode.Default;
            var currentTime = GetTimelinePosition();

            var waveBackground = WaveThemeColor(
                ThemeManager.CurrentTheme.waveBackground,
                Color.FromArgb(255, 6, 7, 9));
            graphics.Clear(waveBackground);
            var resample = (int)deltatime - 1;
            if (resample > 1 && resample <= 3) resample = 1;
            if (resample > 3) resample = 2;
            var waveLevels = waveRaws[resample];

            var displayLength = double.IsFinite(waveformDisplayLength) && waveformDisplayLength > 0d
                ? waveformDisplayLength
                : songLength;
            var step = displayLength / waveLevels.Length;
            if (!double.IsFinite(step) || step <= 0d)
                return;
            var startindex = (int)((currentTime - deltatime) / step);
            var stopindex = (int)((currentTime + deltatime) / step);
            var linewidth = backBitmap.Width / (float)Math.Max(1, stopindex - startindex);
            var isLightTheme = string.Equals(ThemeManager.CurrentTheme.name, "light",
                StringComparison.OrdinalIgnoreCase);
            using var wavePen = new Pen(
                isLightTheme ? Color.FromArgb(225, 96, 184, 137) : Color.FromArgb(225, 32, 178, 92),
                Math.Clamp(linewidth, 1f, 1.6f));
            if (startindex < 0)
            {
                var zeroX = (0 - startindex) * linewidth;
                graphics.DrawLine(wavePen, 0f, height / 2f, Math.Min(width, zeroX), height / 2f);
            }

            PointF? previousPoint = null;
            for (var i = startindex; i < stopindex; i = i + 1)
            {
                if (i < 0) continue;
                if (i >= waveLevels.Length - 1) break;

                var x = (i - startindex) * linewidth;
                var y = waveLevels[i] / 65535f * height + height / 2;
                var point = new PointF(x, y);
                if (previousPoint.HasValue)
                    graphics.DrawLine(wavePen, previousPoint.Value, point);
                previousPoint = point;
            }

            // Cache the full-song grid; playback redraws only project cached times to pixels.
            var songEnd = Bass.BASS_ChannelBytes2Seconds(bgmStream, Bass.BASS_ChannelGetLength(bgmStream));
            EnsureWaveBeatCache(songEnd);
            using var strongBeatPen = new Pen(
                isLightTheme ? Color.FromArgb(255, 190, 142, 28) : Color.FromArgb(255, 255, 220, 45),
                1.25f);
            using var weakBeatPen = new Pen(
                isLightTheme ? Color.FromArgb(220, 158, 116, 28) : Color.FromArgb(220, 255, 220, 45),
                1f);

            foreach (var btime in cachedStrongBeats)
            {
                if (Math.Abs(btime - currentTime) > deltatime) continue;
                var x = ((float)(btime / step) - startindex) * linewidth;
                graphics.DrawLine(strongBeatPen, x, 0, x, height);
            }

            foreach (var btime in cachedWeakBeats)
            {
                if (Math.Abs(btime - currentTime) > deltatime) continue;
                var x = ((float)(btime / step) - startindex) * linewidth;
                graphics.DrawLine(weakBeatPen, x, 0, x, Math.Min(15, height));
            }

            using var timingPen = new Pen(
                isLightTheme ? Color.FromArgb(235, 230, 237, 245) : Color.FromArgb(245, 255, 255, 255),
                1f);
            var timingStart = FindFirstWaveItemAtOrAfter(
                SimaiProcess.timinglist, currentTime - deltatime);
            for (var timingIndex = timingStart;
                 timingIndex < SimaiProcess.timinglist.Count;
                 timingIndex++)
            {
                var note = SimaiProcess.timinglist[timingIndex];
                if (note == null) break;
                if (note.time > currentTime + deltatime) break;
                var x = ((float)(note.time / step) - startindex) * linewidth;
                graphics.DrawLine(timingPen, x, Math.Max(0, height - 15), x, height);
            }

            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var normalStarColor = Color.FromArgb(40, 196, 255);
            EnsureWaveNoteRangeCache();
            var noteStart = FindFirstWaveItemAtOrAfter(
                SimaiProcess.notelist,
                currentTime - deltatime - cachedWaveMaxVisualDuration);
            for (var noteIndex = noteStart;
                 noteIndex < SimaiProcess.notelist.Count;
                 noteIndex++)
            {
                var note = SimaiProcess.notelist[noteIndex];
                if (note == null) break;
                if (note.time > currentTime + deltatime) break;
                var notes = note.noteList.Count > 0 ? note.noteList : note.getNotes();
                var visualEndTime = note.time;
                foreach (var visibleNote in notes)
                {
                    visualEndTime = visibleNote.noteType switch
                    {
                        SimaiNoteType.Hold or SimaiNoteType.TouchHold =>
                            Math.Max(visualEndTime, note.time + visibleNote.holdTime),
                        SimaiNoteType.Slide =>
                            Math.Max(visualEndTime, visibleNote.slideStartTime + visibleNote.slideTime),
                        _ => visualEndTime
                    };

                    if (!visibleNote.isHanabi)
                        continue;
                    var fireworkStart = visibleNote.noteType == SimaiNoteType.TouchHold
                        ? note.time + visibleNote.holdTime
                        : note.time;
                    visualEndTime = Math.Max(visualEndTime, fireworkStart + 1d);
                }
                if (visualEndTime - currentTime < -deltatime) continue;
                var isEach = notes.Count(o => !o.isSlideNoHead) > 1;
                var slideCount = notes.Count(o => o.noteType == SimaiNoteType.Slide);

                var x = ((float)(note.time / step) - startindex) * linewidth;

                foreach (var noteD in notes)
                {
                    var visualPosition = noteD.isDZone
                        ? noteD.startPosition - 0.5f
                        : noteD.startPosition;
                    var y = visualPosition * 6.875f + 8f;

                    if (noteD.isHanabi)
                    {
                        var xDeltaHanabi = (float)(1f / step) * linewidth; //Hanabi is 1s due to frame analyze
                        var rectangleF = new RectangleF(x, 0, xDeltaHanabi, 75);
                        if (noteD.noteType == SimaiNoteType.TouchHold)
                            rectangleF.X += (float)(noteD.holdTime / step) * linewidth;
                        using var gradientBrush = new LinearGradientBrush(
                            rectangleF,
                            Color.FromArgb(100, 255, 0, 0),
                            Color.FromArgb(0, 255, 0, 0),
                            LinearGradientMode.Horizontal
                        );
                        graphics.FillRectangle(gradientBrush, rectangleF);
                    }

                    if (noteD.noteType == SimaiNoteType.Tap)
                    {
                        var color = WaveNoteRenderColor(
                            noteD.isBreak, isEach, noteD.isMonoHead, Color.FromArgb(255, 95, 176));
                        if (noteD.isForceStar)
                            DrawWaveStar(graphics, x, y, 4.5f,
                                WaveNoteRenderColor(
                                    noteD.isBreak, isEach, noteD.isMonoHead, normalStarColor), 0f);
                        else
                            DrawWaveRing(graphics, x, y, 3f, color);
                    }

                    if (noteD.noteType == SimaiNoteType.Touch)
                    {
                        DrawWaveDiamond(graphics, x, y, 3.4f,
                            WaveNoteRenderColor(
                                noteD.isBreak, isEach, noteD.isMonoHead, Color.FromArgb(40, 196, 255)));
                    }

                    if (noteD.noteType == SimaiNoteType.Hold)
                    {
                        var color = WaveNoteRenderColor(
                            noteD.isBreak, isEach, noteD.isMonoHead, Color.FromArgb(255, 95, 176));
                        var xRight = x + (float)(noteD.holdTime / step) * linewidth;
                        if (!float.IsFinite(xRight)) xRight = x;
                        if (xRight - x < 2f) xRight = x + 2f;
                        DrawWaveHold(graphics, x, xRight, y, color);
                    }

                    if (noteD.noteType == SimaiNoteType.TouchHold)
                    {
                        var xDelta = (float)(noteD.holdTime / step) * linewidth / 4f;
                        if (!float.IsFinite(xDelta)) xDelta = 0f;
                        if (xDelta < 1f) xDelta = 1;
                        Color? specialColor = noteD.isMonoHead
                            ? WaveMineColor
                            : noteD.isBreak
                                ? Color.OrangeRed
                                : null;
                        DrawWaveTouchHold(graphics, x, y, xDelta, specialColor);
                    }

                    if (noteD.noteType == SimaiNoteType.Slide)
                    {
                        var xSlide = (float)(noteD.slideStartTime / step - startindex) * linewidth;
                        var xSlideRight = (float)(noteD.slideTime / step) * linewidth + xSlide;
                        if (!float.IsFinite(xSlideRight) || !float.IsFinite(xSlide))
                            continue;

                        var slideColor = noteD.isSlideMono
                            ? WaveMineColor
                            : noteD.isSlideBreak
                            ? Color.OrangeRed
                            : slideCount >= 2 ? Color.Gold : Color.FromArgb(40, 196, 255);
                        if (noteD.isTouchSlide)
                        {
                            if (!noteD.isSlideNoHead)
                            {
                                var headColor = WaveNoteRenderColor(
                                    noteD.isBreak,
                                    isEach,
                                    noteD.isMonoHead,
                                    noteD.touchArea == 'K'
                                        ? normalStarColor
                                        : Color.FromArgb(40, 196, 255));
                                if (noteD.touchArea == 'K')
                                    DrawWaveStar(graphics, x, y, 4.5f, headColor, 0f);
                                else
                                    DrawWaveDiamond(graphics, x, y, 3.4f, headColor);
                            }
                        }
                        var endPosition = noteD.isTouchSlide
                            ? noteD.touchEndPosition
                            : WaveSlideEndPosition(noteD);
                        var endVisualPosition = noteD.isDZoneEnd ? endPosition - 0.5f : endPosition;
                        var yEnd = endVisualPosition * 6.875f + 8f;
                        var angle = MathF.Atan2(yEnd - y, xSlideRight - xSlide);
                        if (!noteD.isSlideNoHead && !noteD.isTouchSlide)
                        {
                            var headColor = WaveNoteRenderColor(
                                noteD.isBreak, isEach, noteD.isMonoHead, normalStarColor);
                            DrawWaveStar(graphics, x, y, 4.5f, headColor, angle);
                        }
                        DrawWaveSlide(graphics, xSlide, y, xSlideRight, yEnd, slideColor);
                    }
                }
            }

            var markerIndex = FindFirstWaveItemAtOrAfter(
                SimaiProcess.notelist, ghostCusorPositionTime - 0.0005d);
            if (markerIndex < SimaiProcess.notelist.Count)
                DrawScrollSpawnMarker(
                    graphics,
                    SimaiProcess.notelist[markerIndex],
                    currentTime,
                    step,
                    startindex,
                    linewidth);

            DrawRecordingFlowBackground(graphics, currentTime, deltatime, step, startindex, linewidth, height);
            DrawTimelineOverlay(graphics, currentTime, deltatime, step, startindex, linewidth, height);

            if (playStartTime - currentTime <= deltatime)
            {
                using var markerPen = new Pen(Color.Red, 3);
                var x1 = (float)(playStartTime / step - startindex) * linewidth;
                PointF[] tranglePoints = { new(x1 - 2, 0), new(x1 + 2, 0), new(x1, 3.46f) };
                graphics.DrawPolygon(markerPen, tranglePoints);
            }

            if (ghostCusorPositionTime - currentTime <= deltatime)
            {
                using var ghostPen = new Pen(Color.Orange, 3);
                var x2 = (float)(ghostCusorPositionTime / step - startindex) * linewidth;
                PointF[] tranglePoints2 = { new(x2 - 2, 0), new(x2 + 2, 0), new(x2, 3.46f) };
                graphics.DrawPolygon(ghostPen, tranglePoints2);
            }

            DrawMediaTrimMarker(graphics, SimaiProcess.mediaTrimStart, "START", Color.DeepSkyBlue,
                currentTime, step, startindex, linewidth, height);
            DrawMediaTrimMarker(graphics, SimaiProcess.mediaTrimEnd, "END", Color.Magenta,
                currentTime, step, startindex, linewidth, height);

            graphics.Flush();
            WaveBitmap.AddDirtyRect(new Int32Rect(0, 0, WaveBitmap.PixelWidth, WaveBitmap.PixelHeight));
        }
        finally
        {
            WaveBitmap.Unlock();
            isDrawing = false;
        }
    }

    private void DrawMediaTrimMarker(
        Graphics graphics,
        double? markerTime,
        string label,
        Color color,
        double currentTime,
        double step,
        int startIndex,
        float lineWidth,
        int height)
    {
        if (!markerTime.HasValue || Math.Abs(markerTime.Value - currentTime) > deltatime)
            return;

        var x = (float)(markerTime.Value / step - startIndex) * lineWidth;
        if (x < 0f || x > WaveBitmap!.PixelWidth)
            return;

        using var pen = new Pen(color, 2f);
        using var brush = new SolidBrush(color);
        using var font = new Font("Segoe UI", 7f, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
        graphics.DrawLine(pen, x, 0f, x, height);
        graphics.FillPolygon(brush, new[]
        {
            new PointF(x - 4f, 0f),
            new PointF(x + 4f, 0f),
            new PointF(x, 6f)
        });
        graphics.DrawString(label, font, brush, x + 3f, 4f);
    }

    private void EnsureWaveBeatCache(double songEnd)
    {
        if (ReferenceEquals(cachedWaveTimingList, SimaiProcess.timinglist) &&
            ReferenceEquals(cachedWaveMeterList, SimaiProcess.meterTable) &&
            Math.Abs(cachedWaveSongEnd - songEnd) < 0.001d)
            return;

        BuildWaveBeatLines(songEnd, out cachedStrongBeats, out cachedWeakBeats);
        cachedWaveTimingList = SimaiProcess.timinglist;
        cachedWaveMeterList = SimaiProcess.meterTable;
        cachedWaveSongEnd = songEnd;
    }

    private void EnsureWaveNoteRangeCache()
    {
        if (ReferenceEquals(cachedWaveNoteList, SimaiProcess.notelist))
            return;

        var maxDuration = 0d;
        foreach (var timing in SimaiProcess.notelist)
        {
            var notes = timing.noteList.Count > 0 ? timing.noteList : timing.getNotes();
            foreach (var note in notes)
            {
                var endTime = note.noteType switch
                {
                    SimaiNoteType.Hold or SimaiNoteType.TouchHold => timing.time + note.holdTime,
                    SimaiNoteType.Slide => note.slideStartTime + note.slideTime,
                    _ => timing.time
                };
                if (note.isHanabi)
                {
                    var fireworkStart = note.noteType == SimaiNoteType.TouchHold
                        ? timing.time + note.holdTime
                        : timing.time;
                    endTime = Math.Max(endTime, fireworkStart + 1d);
                }
                maxDuration = Math.Max(maxDuration, endTime - timing.time);
            }
        }

        cachedWaveNoteList = SimaiProcess.notelist;
        cachedWaveMaxVisualDuration = Math.Max(0d, maxDuration);
    }

    private static int FindFirstWaveItemAtOrAfter(List<SimaiTimingPoint> source, double time)
    {
        var low = 0;
        var high = source.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (source[middle].time < time)
                low = middle + 1;
            else
                high = middle;
        }
        return low;
    }

    private static Color WaveNoteColor(bool isBreak, bool isEach, Color normal) =>
        isBreak ? Color.OrangeRed : isEach ? Color.Gold : normal;

    private static readonly Color WaveMineColor = Color.FromArgb(225, 170, 170, 170);

    private static Color WaveNoteRenderColor(bool isBreak, bool isEach, bool isMine, Color normal) =>
        isMine ? WaveMineColor : WaveNoteColor(isBreak, isEach, normal);

    private static Color DarkWaveColor(Color color) =>
        Color.FromArgb(color.A, color.R * 11 / 20, color.G * 11 / 20, color.B * 11 / 20);

    private static Color WaveThemeColor(string value, Color fallback)
    {
        try
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value);
            return Color.FromArgb(color.A, color.R, color.G, color.B);
        }
        catch
        {
            return fallback;
        }
    }

    private static void DrawWaveRing(Graphics graphics, float x, float y, float radius, Color color)
    {
        using var outline = new Pen(Color.FromArgb(230, 12, 12, 16), 3.4f);
        using var body = new Pen(color, 2.5f);
        using var highlight = new Pen(Color.FromArgb(145, 255, 255, 255), 0.8f);
        graphics.DrawEllipse(outline, x - radius, y - radius, radius * 2f, radius * 2f);
        graphics.DrawEllipse(body, x - radius, y - radius, radius * 2f, radius * 2f);
        graphics.DrawArc(highlight, x - radius + 0.45f, y - radius + 0.45f,
            radius * 2f - 0.9f, radius * 2f - 0.9f, 205f, 105f);
    }

    private static void DrawWaveDiamond(Graphics graphics, float x, float y, float radius, Color color)
    {
        var points = new[]
        {
            new PointF(x, y - radius), new PointF(x + radius, y),
            new PointF(x, y + radius), new PointF(x - radius, y)
        };
        using var outline = new Pen(Color.FromArgb(230, 12, 12, 16), 3f) { LineJoin = LineJoin.Round };
        using var body = new Pen(color, 2.2f) { LineJoin = LineJoin.Round };
        graphics.DrawPolygon(outline, points);
        graphics.DrawPolygon(body, points);
    }

    private static void DrawWaveStar(
        Graphics graphics,
        float x,
        float y,
        float radius,
        Color color,
        float rotation)
    {
        var points = new PointF[10];
        for (var i = 0; i < points.Length; i++)
        {
            var angle = rotation - MathF.PI / 2f + i * MathF.PI / 5f;
            var pointRadius = i % 2 == 0 ? radius : radius * 0.46f;
            points[i] = new PointF(
                x + MathF.Cos(angle) * pointRadius,
                y + MathF.Sin(angle) * pointRadius);
        }
        using var outline = new Pen(Color.FromArgb(230, 12, 12, 16), 2.55f) { LineJoin = LineJoin.Round };
        using var body = new Pen(color, 1.95f) { LineJoin = LineJoin.Round };
        graphics.DrawPolygon(outline, points);
        graphics.DrawPolygon(body, points);
    }

    private static void DrawWaveHold(Graphics graphics, float x0, float x1, float y, Color color)
    {
        var length = Math.Max(5f, x1 - x0);
        x1 = x0 + length;
        var tip = Math.Clamp(length * 0.2f, 1.1f, 3.1f);
        const float radius = 3.35f;
        var outer = new[]
        {
            new PointF(x0, y), new PointF(x0 + tip, y - radius),
            new PointF(x1 - tip, y - radius), new PointF(x1, y),
            new PointF(x1 - tip, y + radius), new PointF(x0 + tip, y + radius)
        };
        const float innerRadius = 1f;
        var innerTip = Math.Min(tip, Math.Max(0.7f, tip * 0.72f));
        var inner = new[]
        {
            new PointF(x0 + innerTip, y - innerRadius),
            new PointF(x1 - innerTip, y - innerRadius),
            new PointF(x1 - 0.35f, y),
            new PointF(x1 - innerTip, y + innerRadius),
            new PointF(x0 + innerTip, y + innerRadius),
            new PointF(x0 + 0.35f, y)
        };

        using var band = new GraphicsPath(FillMode.Alternate);
        band.AddPolygon(outer);
        band.AddPolygon(inner);
        using var hollowBrush = new SolidBrush(Color.FromArgb(42, color));
        graphics.FillPolygon(hollowBrush, inner);
        using var bodyBrush = new SolidBrush(color);
        graphics.FillPath(bodyBrush, band);
        using var border = new Pen(Color.FromArgb(145, 12, 12, 16), 0.38f) { LineJoin = LineJoin.Round };
        using var highlight = new Pen(Color.FromArgb(115, 255, 255, 255), 0.7f);
        graphics.DrawPolygon(border, outer);
        graphics.DrawLine(highlight, x0 + tip, y - radius + 0.65f,
            Math.Max(x0 + tip, x1 - tip), y - radius + 0.65f);
    }

    private static void DrawWaveTouchHold(
        Graphics graphics,
        float x,
        float y,
        float quarter,
        Color? specialColor)
    {
        var colors = specialColor.HasValue
            ? new[]
            {
                specialColor.Value, specialColor.Value, specialColor.Value, specialColor.Value
            }
            : new[]
        {
            Color.FromArgb(220, 255, 75, 0), Color.FromArgb(220, 255, 241, 0),
            Color.FromArgb(220, 2, 165, 89), Color.FromArgb(220, 0, 140, 254)
        };
        for (var i = 0; i < colors.Length; i++)
        {
            using var outline = new Pen(DarkWaveColor(colors[i]), 2.7f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            using var body = new Pen(colors[i], 2.25f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            var end = x + quarter * (4 - i);
            graphics.DrawLine(outline, x, y, end, y);
            graphics.DrawLine(body, x, y, end, y);
        }
        DrawWaveDiamond(
            graphics,
            x,
            y,
            3.4f,
            specialColor ?? Color.FromArgb(40, 196, 255));
    }

    private static void DrawWaveSlide(Graphics graphics, float x0, float y0, float x1, float y1, Color color)
    {
        var dx = x1 - x0;
        var dy = y1 - y0;
        var length = MathF.Sqrt(dx * dx + dy * dy);
        if (length < 0.5f)
            return;

        var fx = dx / length;
        var fy = dy / length;
        var px = -fy;
        var py = fx;
        var spacing = 6f;
        using var outline = new Pen(Color.FromArgb(230, 12, 12, 16), 2.8f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        using var body = new Pen(color, 2.15f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };

        for (var distance = 1.5f; distance <= length; distance += spacing)
        {
            var cx = x0 + fx * distance;
            var cy = y0 + fy * distance;
            var backX = cx - fx * 3.5f;
            var backY = cy - fy * 3.5f;
            var left = new PointF(backX + px * 2.4f, backY + py * 2.4f);
            var right = new PointF(backX - px * 2.4f, backY - py * 2.4f);
            var tipPoint = new PointF(cx, cy);
            graphics.DrawLine(outline, left, tipPoint);
            graphics.DrawLine(outline, right, tipPoint);
            graphics.DrawLine(body, left, tipPoint);
            graphics.DrawLine(body, right, tipPoint);
        }
    }

    private static int WaveSlideEndPosition(SimaiNote note)
    {
        if (string.IsNullOrEmpty(note.noteContent))
            return note.startPosition;

        var end = -1;
        var wifi = false;
        var depth = 0;
        foreach (var ch in note.noteContent)
        {
            if (ch == '[')
            {
                depth++;
                continue;
            }
            if (ch == ']')
            {
                depth = Math.Max(0, depth - 1);
                continue;
            }
            if (depth > 0)
                continue;
            if (ch is >= '1' and <= '8')
                end = ch - '0';
            else if (ch == 'w')
                wifi = true;
        }

        if (wifi)
            return (note.startPosition + 3) % 8 + 1;
        return end > 0 ? end : note.startPosition;
    }

    private static void BuildWaveBeatLines(double songEnd, out List<double> strongBeats, out List<double> weakBeats)
    {
        strongBeats = new List<double>();
        weakBeats = new List<double>();
        if (songEnd <= SimaiProcess.first)
            return;

        var bpmEvents = new List<SimaiTimingPoint>();
        var lastBpm = float.NaN;
        foreach (var point in SimaiProcess.timinglist.OrderBy(point => point.time))
        {
            if (point.currentBpm <= 0 || Math.Abs(point.currentBpm - lastBpm) < 0.0001f)
                continue;
            bpmEvents.Add(point);
            lastBpm = point.currentBpm;
        }
        var meterEvents = SimaiProcess.meterTable
            .OrderBy(change => change.time)
            .ToList();

        var bpm = bpmEvents.FirstOrDefault()?.currentBpm ?? 120f;
        var numerator = 4;
        var denominator = 4;
        var bpmIndex = 0;
        var meterIndex = 0;
        var beatInMeasure = 1;
        var time = (double)SimaiProcess.first;
        var segmentStart = time;
        long segmentBeatIndex = 0;
        const double epsilon = 0.0001;

        while (time <= songEnd + epsilon)
        {
            var changed = false;
            while (bpmIndex < bpmEvents.Count && bpmEvents[bpmIndex].time <= time + epsilon)
            {
                bpm = bpmEvents[bpmIndex].currentBpm;
                bpmIndex++;
                changed = true;
            }

            while (meterIndex < meterEvents.Count && meterEvents[meterIndex].time <= time + epsilon)
            {
                numerator = meterEvents[meterIndex].numerator;
                denominator = meterEvents[meterIndex].denominator;
                meterIndex++;
                changed = true;
            }

            if (changed)
            {
                beatInMeasure = 1;
                segmentStart = time;
                segmentBeatIndex = 0;
            }

            if (bpm <= 0 || numerator <= 0 || denominator <= 0)
                break;

            if (beatInMeasure == 1)
                strongBeats.Add(Math.Round(time, 9, MidpointRounding.AwayFromZero));
            else
                weakBeats.Add(Math.Round(time, 9, MidpointRounding.AwayFromZero));

            var beatDuration = 60d / bpm * 4d / denominator;
            segmentBeatIndex++;
            var nextBeat = segmentStart + segmentBeatIndex * beatDuration;
            var nextBpmTime = bpmIndex < bpmEvents.Count ? bpmEvents[bpmIndex].time : double.PositiveInfinity;
            var nextMeterTime = meterIndex < meterEvents.Count ? meterEvents[meterIndex].time : double.PositiveInfinity;
            var nextChange = Math.Min(nextBpmTime, nextMeterTime);

            if (nextChange > time + epsilon && nextChange < nextBeat - epsilon)
            {
                time = nextChange;
                beatInMeasure = 1;
                segmentStart = time;
                segmentBeatIndex = 0;
                continue;
            }

            time = nextBeat;
            beatInMeasure = beatInMeasure >= numerator ? 1 : beatInMeasure + 1;
        }
    }

    private void InsertMeasureTemplate(string meter, int measureCount)
    {
        var nextMeasure = ChartOrganizer.GetMeasureNumberAt(GetRawFumenText(), fumenEditor.CaretOffset);
        measureCount = Math.Max(1, measureCount);
        var meterParts = meter.Split('/');
        var beatsPerMeasure = meterParts.Length == 2 && int.TryParse(meterParts[0], out var parsedBeats)
            ? Math.Max(1, parsedBeats)
            : 4;
        var slots = beatsPerMeasure * 4;
        var lineBuilder = new StringBuilder("{16}");
        for (var index = 0; index < slots; index++)
        {
            lineBuilder.Append(',');
            if ((index + 1) % 4 == 0 && index + 1 < slots)
                lineBuilder.Append(' ');
        }
        var line = lineBuilder.ToString();
        var measureLabel = measureCount == 1
            ? nextMeasure.ToString(CultureInfo.InvariantCulture)
            : $"{nextMeasure}-{nextMeasure + measureCount - 1}";
        var meterCommand = string.Equals(meter, "4/4", StringComparison.Ordinal) ? string.Empty : $"@{meter}\n";
        var template = $"\n{meterCommand}||{string.Format(GetLocalizedString("MeasureComment"), measureLabel)}\n" +
                       string.Join("\n", Enumerable.Repeat(line, measureCount)) + "\n";
        fumenEditor.ReplaceSelection(template);
        SetSavedState(false);
    }

    private sealed class VisualEditMessage
    {
        public string note = "";
        public string action = "note";
        public int slideStart;
    }

    internal void StartVisualEditBridge()
    {
        if (visualEditListener.IsListening)
            return;
        try
        {
            visualEditListener.Prefixes.Add("http://127.0.0.1:8014/");
            visualEditListener.Start();
            visualEditCancellation = new CancellationTokenSource();
            _ = Task.Run(ReceiveVisualEditMessages);
            LogVisualEdit("bridge started");
        }
        catch (HttpListenerException exception)
        {
            // If the port is occupied, such as by another Edit instance, note generation fails silently; leave a diagnostic trail.
            LogVisualEditError(exception);
        }
    }

    internal void StopVisualEditBridge()
    {
        visualEditCancellation?.Cancel();
        visualEditCancellation?.Dispose();
        visualEditCancellation = null;
        if (visualEditListener.IsListening)
            visualEditListener.Stop();
    }

    private async Task ReceiveVisualEditMessages()
    {
        while (visualEditListener.IsListening && visualEditCancellation?.IsCancellationRequested == false)
        {
            HttpListenerContext? context = null;
            try
            {
                context = await visualEditListener.GetContextAsync();
                using var reader = new StreamReader(context.Request.InputStream);
                var body = await reader.ReadToEndAsync();
                LogVisualEdit("received: " + body);
                var message = JsonConvert.DeserializeObject<VisualEditMessage>(body);
                if (message != null &&
                    (!string.IsNullOrWhiteSpace(message.note) ||
                     string.Equals(message.action, "undo", StringComparison.OrdinalIgnoreCase)))
                    // BeginInvoke returns 200 immediately, so a busy UI thread cannot trigger View's 1-second timeout.
                    // Handler exceptions must not escape here because they would terminate the entire receive loop,
                    // causing every subsequent click to disappear (the root cause of clicks in View producing no notes).
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            HandleVisualEditMessage(message);
                        }
                        catch (Exception exception)
                        {
                            LogVisualEditError(exception);
                        }
                    }));
                context.Response.StatusCode = 200;
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception exception)
            {
                // Skip only the malformed or failed message; keep the receive loop alive.
                LogVisualEditError(exception);
            }
            finally
            {
                context?.Response.Close();
            }
        }
    }

    private static void LogVisualEditError(Exception exception) => LogVisualEdit(exception.ToString());

    // Diagnostic log for click-to-generate-note flow, showing whether requests arrived and why they were rejected.
    private static void LogVisualEdit(string text)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "visualedit-error.log");
            if (File.Exists(path) && new FileInfo(path).Length > 1_000_000)
                File.Delete(path);
            File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss.fff}] {text}\r\n");
        }
        catch
        {
            // Logging failures must not affect editing.
        }
    }

    private void HandleVisualEditMessage(VisualEditMessage message)
    {
        if (string.Equals(message.action, "undo", StringComparison.OrdinalIgnoreCase))
        {
            if (!isLoading && lastEditorState == EditorControlMethod.Stop)
            {
                if (fumenEditor.Undo())
                {
                    fumenEditor.Focus();
                    SetSavedState(false);
                    PetStatusClient.Notify("review", "Last action undone");
                }
            }
            return;
        }

        InsertVisualNote(message);
    }

    private void InsertVisualNote(VisualEditMessage message)
    {
        var note = message.note;
        if (isLoading || lastEditorState != EditorControlMethod.Stop || string.IsNullOrWhiteSpace(note))
        {
            LogVisualEdit($"insert skipped: isLoading={isLoading} state={lastEditorState} note='{note}'");
            return;
        }
        note = note.Trim().Trim('/');
        if (note.Length == 0)
            return;

        var selection = fumenEditor.Selection;
        var source = GetRawFumenText();
        var caret = Math.Clamp(selection.Start, 0, source.Length);
        var timingCommas = FindTimingCommas(source);

        // A control-only line is not a timing slot. Insert the generated note on
        // a new line after it instead of merging the command text into the note.
        if (TryGetControlLineInsertion(source, caret, out var controlInsertion))
        {
            var prefix = controlInsertion > 0 && source[controlInsertion - 1] != '\n' ? "\n" : string.Empty;
            var inserted = prefix + note.Trim().TrimStart('/') + ",";
            fumenEditor.Select(controlInsertion, 0);
            fumenEditor.ReplaceSelection(inserted);
            fumenEditor.Select(controlInsertion + inserted.Length - 1, 0);
            RestoreEditorFocusAfterVisualInsert();
            SetSavedState(false);
            return;
        }

        // A caret immediately after a timing comma means "create the next slot".
        // This must not scan through following Alpha commands for another comma.
        if (caret > 0 && timingCommas.Contains(caret - 1))
        {
            var inserted = note.Trim().TrimStart('/') + ",";
            fumenEditor.Select(caret, selection.Length);
            fumenEditor.ReplaceSelection(inserted);
            fumenEditor.Select(caret + inserted.Length - 1, 0);
            RestoreEditorFocusAfterVisualInsert();
            SetSavedState(false);
            return;
        }

        var previousComma = timingCommas.Where(index => index < caret).DefaultIfEmpty(-1).Max();
        var slotStart = previousComma + 1;
        var slotEnd = timingCommas.Where(index => index >= caret).DefaultIfEmpty(source.Length).Min();

        var noteStart = slotStart;
        while (noteStart < slotEnd && char.IsWhiteSpace(source[noteStart]))
            noteStart++;
        if (noteStart < slotEnd && source[noteStart] == '{')
        {
            var markerEnd = source.IndexOf('}', noteStart + 1);
            if (markerEnd >= noteStart && markerEnd < slotEnd)
                noteStart = markerEnd + 1;
        }
        while (noteStart < slotEnd && char.IsWhiteSpace(source[noteStart]))
            noteStart++;

        var current = source.Substring(noteStart, Math.Max(0, slotEnd - noteStart)).Trim();
        var combined = MergeVisualNote(current, note, message.action, message.slideStart).Trim('/');
        var hasFollowingComma = slotEnd < source.Length && source[slotEnd] == ',';
        fumenEditor.Select(noteStart, slotEnd - noteStart);
        fumenEditor.ReplaceSelection(combined + (hasFollowingComma ? string.Empty : ","));
        fumenEditor.Select(noteStart + combined.Length, 0);
        RestoreEditorFocusAfterVisualInsert();
        SetSavedState(false);
    }

    private void RestoreEditorFocusAfterVisualInsert()
    {
        Activate();
        FumenContent.Focus();
        Keyboard.Focus(FumenContent.TextArea);
    }

    private static bool TryGetControlLineInsertion(string text, int caret, out int insertion)
    {
        insertion = -1;
        var lineStart = caret <= 0 ? 0 : text.LastIndexOf('\n', Math.Min(caret - 1, text.Length - 1)) + 1;
        var lineEnd = text.IndexOf('\n', caret);
        if (lineEnd < 0)
            lineEnd = text.Length;

        var line = text.Substring(lineStart, lineEnd - lineStart).Trim();
        var isControl = line.Length == 0 ||
                        line.StartsWith("&", StringComparison.Ordinal) ||
                        line.StartsWith("||", StringComparison.Ordinal) ||
                        line.StartsWith("|*", StringComparison.Ordinal) ||
                        (line.StartsWith("<", StringComparison.Ordinal) &&
                         IsAlphaCommandStart(line, 0, out var commandEnd) &&
                         string.IsNullOrWhiteSpace(line[(commandEnd + 1)..]));
        if (!isControl)
            return false;

        insertion = lineEnd < text.Length ? lineEnd + 1 : lineEnd;
        return true;
    }

    private static HashSet<int> FindTimingCommas(string text)
    {
        var result = new HashSet<int>();
        var squareDepth = 0;
        var roundDepth = 0;
        var inBlockComment = false;
        var inLineComment = false;

        for (var index = 0; index < text.Length; index++)
        {
            if (inBlockComment)
            {
                if (index + 1 < text.Length && text[index] == '*' && text[index + 1] == '|')
                {
                    inBlockComment = false;
                    index++;
                }
                continue;
            }

            if (inLineComment)
            {
                if (text[index] == '\n')
                    inLineComment = false;
                continue;
            }

            if (index + 1 < text.Length && text[index] == '|' && text[index + 1] == '*')
            {
                inBlockComment = true;
                index++;
                continue;
            }
            if (index + 1 < text.Length && text[index] == '|' && text[index + 1] == '|')
            {
                inLineComment = true;
                index++;
                continue;
            }
            if (text[index] == '<' && IsAlphaCommandStart(text, index, out var alphaEnd))
            {
                index = alphaEnd;
                continue;
            }

            switch (text[index])
            {
                case '[': squareDepth++; break;
                case ']': squareDepth = Math.Max(0, squareDepth - 1); break;
                case '(': roundDepth++; break;
                case ')': roundDepth = Math.Max(0, roundDepth - 1); break;
                case ',' when squareDepth == 0 && roundDepth == 0:
                    result.Add(index);
                    break;
            }
        }
        return result;
    }

    private static string MergeVisualNote(
        string current,
        string incoming,
        string action = "note",
        int slideStart = 0)
    {
        current = current.Trim().Trim('/');
        incoming = incoming.Trim().TrimStart('/');
        if (string.IsNullOrWhiteSpace(current))
            return incoming;

        var notes = current.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (incoming.Length == 1 && incoming[0] is >= '1' and <= '8')
        {
            for (var slideIndex = 0; slideIndex < notes.Count; slideIndex++)
            {
                if (!IsVisualSlide(notes[slideIndex]))
                    continue;
                if (TrySplitConnectedVisualSlide(notes[slideIndex], incoming[0], out var first, out var second))
                {
                    notes[slideIndex] = first;
                    notes.Insert(slideIndex + 1, second);
                    return string.Join('/', notes);
                }
            }

            if (action == "slideHead")
            {
                if (TryCycleDisconnectedVisualSlide(notes, incoming[0]))
                    return string.Join('/', notes);

                var slideIndex = notes.FindIndex(item =>
                    IsVisualSlide(item) && item[0] == incoming[0]);
                if (slideIndex >= 0)
                {
                    ToggleVisualSlideHead(notes, slideIndex);
                    return string.Join('/', notes);
                }
            }
        }

        if (action == "slidePath" && IsVisualTouch(incoming) &&
            ToggleVisualSlidePath(notes, incoming, slideStart))
        {
            return string.Join('/', notes);
        }

        if (IsVisualSlide(incoming))
        {
            var slideIndex = notes.FindIndex(item =>
                IsVisualSlide(item) && item[0] == incoming[0]);
            if (slideIndex >= 0)
            {
                var incomingBranch = incoming.Substring(1);
                var incomingPath = SplitVisualSlide(incoming).Path.Substring(1);
                var branchExists = notes[slideIndex]
                    .Split('*')
                    .Skip(1)
                    .Select(branch => SplitVisualSlide(branch).Path)
                    .Contains(incomingPath, StringComparer.Ordinal);
                if (!branchExists)
                    notes[slideIndex] += "*" + incomingBranch;
                return string.Join('/', notes);
            }

            var connectionIndex = notes.FindIndex(item =>
                IsVisualSlide(item) && GetVisualSlideEnd(item) == incoming[0]);
            if (connectionIndex >= 0)
            {
                notes[connectionIndex] += incoming.Substring(1);
                return string.Join('/', notes);
            }
        }

        var index = notes.FindIndex(item => IsVisualVariantOf(item, incoming));
        if (index >= 0)
            notes[index] = NextVisualVariant(notes[index], incoming);
        else
            notes.Add(incoming);
        return string.Join('/', notes);
    }

    private static void ToggleVisualSlideHead(List<string> notes, int slideIndex)
    {
        var slide = notes[slideIndex];
        if (slide.Length > 1 && slide[1] == 'b')
            notes[slideIndex] = slide.Remove(1, 1);
        else
            notes[slideIndex] = slide.Insert(1, "b");
    }

    private static bool ToggleVisualSlidePath(
        List<string> notes,
        string touch,
        int slideStart)
    {
        var slideIndex = notes.FindIndex(item =>
            IsVisualSlide(item) && (slideStart == 0 || item[0] - '0' == slideStart));
        if (slideIndex < 0)
            return false;

        var touchIndex = notes.FindIndex(item =>
            string.Equals(item, touch, StringComparison.OrdinalIgnoreCase));
        var slide = notes[slideIndex];
        var isBreak = HasVisualSlideBodyBreak(slide);

        if (touchIndex < 0 && !isBreak)
        {
            notes[slideIndex] = slide + "b";
            return true;
        }

        if (touchIndex < 0)
        {
            notes[slideIndex] = slide[..^1];
            notes.Add(touch);
            return true;
        }

        if (!isBreak)
        {
            notes[slideIndex] = slide + "b";
            return true;
        }

        notes[slideIndex] = slide[..^1];
        notes.RemoveAt(touchIndex);
        return true;
    }

    private static bool HasVisualSlideBodyBreak(string slide)
    {
        return slide.Length > 0 && slide[^1] == 'b';
    }

    private static bool TryCycleDisconnectedVisualSlide(List<string> notes, char key)
    {
        var previousIndex = notes.FindIndex(item =>
            IsVisualSlide(item) && GetVisualSlideEnd(item) == key && item[0] != key);
        if (previousIndex < 0)
            return false;

        var nextIndex = notes.FindIndex(item =>
            IsVisualSlide(item) && item[0] == key);
        if (nextIndex < 0 || nextIndex == previousIndex)
            return false;

        var next = notes[nextIndex];
        if (next.Length > 1 && next[1] == 'b')
        {
            next = next.Remove(1, 1);
            notes[previousIndex] += next[1..];
            notes.RemoveAt(nextIndex);
        }
        else
        {
            notes[nextIndex] = next.Insert(1, "b");
        }
        return true;
    }

    private static bool TrySplitConnectedVisualSlide(
        string token,
        char key,
        out string first,
        out string second)
    {
        first = string.Empty;
        second = string.Empty;
        if (token.Contains('*'))
            return false;

        var bracketDepth = 0;
        for (var index = 1; index < token.Length; index++)
        {
            if (token[index] == '[') { bracketDepth++; continue; }
            if (token[index] == ']') { bracketDepth = Math.Max(0, bracketDepth - 1); continue; }
            if (bracketDepth != 0 || token[index] != key)
                continue;

            var nextOperator = index + 1;
            while (nextOperator < token.Length)
            {
                if (token[nextOperator] == '[')
                {
                    nextOperator = token.IndexOf(']', nextOperator + 1);
                    if (nextOperator < 0)
                        return false;
                    nextOperator++;
                    continue;
                }
                if (IsVisualSlideOperator(token[nextOperator]))
                    break;
                nextOperator++;
            }
            if (nextOperator >= token.Length)
                continue;

            first = token.Substring(0, nextOperator);
            second = key + token.Substring(nextOperator);
            return true;
        }
        return false;
    }

    private static char GetVisualSlideEnd(string token)
    {
        var bracketDepth = 0;
        for (var index = token.Length - 1; index >= 0; index--)
        {
            if (token[index] == ']') { bracketDepth++; continue; }
            if (token[index] == '[') { bracketDepth = Math.Max(0, bracketDepth - 1); continue; }
            if (bracketDepth == 0 && token[index] is >= '1' and <= '8')
                return token[index];
        }
        return '\0';
    }

    private static bool IsVisualSlideOperator(char value) =>
        value is '-' or '<' or '>' or '^' or 'v' or 'p' or 'q' or 'r' or 's' or 'z' or 'V' or 'w';

    private static (string Path, string Duration) SplitVisualSlide(string token)
    {
        var durationStart = token.LastIndexOf('[');
        if (durationStart < 0 || !token.EndsWith(']'))
            return (token, string.Empty);
        return (token[..durationStart], token[durationStart..]);
    }

    private static bool IsVisualSlide(string token)
    {
        if (token.Length < 3 || token[0] is < '1' or > '8')
            return false;
        return token.IndexOfAny(new[] { '-', '<', '>', '^', 'v', 'p', 'q', 's', 'z', 'V', 'w' }, 1) >= 0;
    }

    private static bool IsVisualVariantOf(string existing, string incoming)
    {
        if (existing == incoming)
            return true;
        if (IsVisualTouch(incoming))
            return IsVisualTouchHold(existing, incoming);
        if (incoming.Length != 1 || incoming[0] is < '1' or > '8')
            return false;
        return existing == incoming + "b" ||
               existing == incoming + "h[8:1]" ||
               existing == incoming + "hb[8:1]";
    }

    private static string NextVisualVariant(string existing, string incoming)
    {
        if (IsVisualTouch(incoming))
            return IsVisualTouchHold(existing, incoming) ? incoming : incoming + "h[8:1]";
        if (incoming.Length != 1 || incoming[0] is < '1' or > '8')
            return existing;
        if (existing == incoming)
            return incoming + "h[8:1]";
        if (existing == incoming + "h[8:1]")
            return incoming + "b";
        if (existing == incoming + "b")
            return incoming + "hb[8:1]";
        return incoming;
    }

    private static bool IsVisualTouch(string token)
    {
        if (string.Equals(token, "C", StringComparison.OrdinalIgnoreCase))
            return true;
        return token.Length == 2 && token[0] is 'A' or 'B' or 'D' or 'E' && token[1] is >= '1' and <= '8';
    }

    private static bool IsVisualTouchHold(string existing, string touch)
    {
        var prefix = touch + "h[";
        return existing.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
               existing.EndsWith(']');
    }

    private void DrawScrollSpawnMarker(
        Graphics graphics,
        SimaiTimingPoint timing,
        double currentTime,
        double step,
        int startIndex,
        float lineWidth)
    {
        // Only annotate the timing point under the editor cursor. Rendering every
        // marker would obscure dense charts and make waveform redraws needlessly costly.
        if (Math.Abs(timing.time - ghostCusorPositionTime) > 0.0005d)
            return;

        var notes = timing.getNotes();
        if (notes.Count == 0)
            return;

        var selected = notes[0];
        var isEach = notes.Count(note => !note.isSlideNoHead) > 1;
        var spawnRadius = GetSpawnRadiusAt(timing.time, selected, isEach);
        var scrollType = ResolveWaveScrollType(selected, isEach);
        var hSpeed = GetWaveHSpeedAt(timing.time, selected, isEach, timing.HSpeed);
        var hasScrollModifier = Math.Abs(hSpeed - 1f) > 0.0001f ||
            HasWaveSvCurve(scrollType) ||
            Math.Abs(spawnRadius - 1.225f) > 0.0001f;
        if (!hasScrollModifier)
            return;

        var spawnTime = FindScrollSpawnTime(
            timing.time, hSpeed, selected.noteType, spawnRadius, scrollType);
        if (!spawnTime.HasValue || spawnTime.Value < currentTime - deltatime ||
            spawnTime.Value > currentTime + deltatime)
            return;

        var x = ((float)(spawnTime.Value / step) - startIndex) * lineWidth;
        var visualPosition = selected.isDZone ? selected.startPosition - 0.5f : selected.startPosition;
        var y = visualPosition * 6.875f + 8f;

        switch (selected.noteType)
        {
            case SimaiNoteType.Touch:
                DrawWaveDiamond(graphics, x, y, 3.4f, WaveMineColor);
                break;
            case SimaiNoteType.Hold:
                DrawWaveHold(graphics, x - 3f, x + 3f, y, WaveMineColor);
                break;
            case SimaiNoteType.TouchHold:
                DrawWaveTouchHold(graphics, x, y, 1.5f, WaveMineColor);
                break;
            case SimaiNoteType.Slide:
                if (selected.isTouchSlide && selected.touchArea != 'K')
                    DrawWaveDiamond(graphics, x, y, 3.4f, WaveMineColor);
                else
                    DrawWaveStar(graphics, x, y, 4.5f, WaveMineColor, 0f);
                break;
            default:
                if (selected.isForceStar)
                    DrawWaveStar(graphics, x, y, 4.5f, WaveMineColor, 0f);
                else
                    DrawWaveRing(graphics, x, y, 3f, WaveMineColor);
                break;
        }
    }

    private static float GetSpawnRadiusAt(
        double time,
        SimaiNote note,
        bool isEach)
    {
        SpawnChange? Lookup(string? noteType)
        {
            return SimaiProcess.spawnTable
                .Where(item => string.Equals(item.noteType, noteType,
                    StringComparison.OrdinalIgnoreCase) && item.time <= time)
                .OrderBy(item => item.time)
                .LastOrDefault();
        }

        float Resolve(SpawnChange? change) =>
            change == null || change.reset ? 1.225f : change.radius;

        if (note.isBreak)
        {
            var special = Lookup("break");
            if (special != null)
                return special.reset ? Resolve(Lookup(null)) : special.radius;
        }
        if (isEach)
        {
            var special = Lookup("each");
            if (special != null)
                return special.reset ? Resolve(Lookup(null)) : special.radius;
        }

        var baseType = note.noteType switch
        {
            SimaiNoteType.Tap => note.isForceStar ? "star" : "tap",
            SimaiNoteType.Hold => "hold",
            SimaiNoteType.Slide => note.isTouchSlide && note.touchArea != 'K' ? "" : "star",
            _ => ""
        };
        if (baseType.Length > 0)
        {
            var typed = Lookup(baseType);
            if (typed != null)
                return typed.reset ? Resolve(Lookup(null)) : typed.radius;
        }
        return Resolve(Lookup(null));
    }

    private static string WaveBaseNoteType(SimaiNote note)
    {
        return note.noteType switch
        {
            SimaiNoteType.Tap => note.isForceStar ? "star" : "tap",
            SimaiNoteType.Hold => "hold",
            SimaiNoteType.Touch => "touch",
            SimaiNoteType.TouchHold => "touchhold",
            SimaiNoteType.Slide => note.isTouchSlide && note.touchArea != 'K' ? "touch" : "star",
            _ => string.Empty
        };
    }

    private static string ResolveWaveScrollType(SimaiNote note, bool isEach)
    {
        bool HasType(string type) => SimaiProcess.svTable.Any(point =>
            string.Equals(point.noteType, type, StringComparison.OrdinalIgnoreCase));

        if (note.isBreak && HasType("break"))
            return "break";
        if (isEach && HasType("each"))
            return "each";
        return WaveBaseNoteType(note);
    }

    private static bool HasWaveSvCurve(string noteType)
    {
        return SimaiProcess.svTable.Any(point =>
            string.IsNullOrWhiteSpace(point.noteType) ||
            string.Equals(point.noteType, noteType, StringComparison.OrdinalIgnoreCase));
    }

    private static float GetWaveHSpeedAt(
        double time,
        SimaiNote note,
        bool isEach,
        float fallback)
    {
        var baseType = WaveBaseNoteType(note);
        string ResolveType()
        {
            bool HasType(string type) => SimaiProcess.hsTable.Any(point =>
                string.Equals(point.noteType, type, StringComparison.OrdinalIgnoreCase));
            if (note.isBreak && HasType("break"))
                return "break";
            if (isEach && HasType("each"))
                return "each";
            return baseType;
        }

        var resolvedType = ResolveType();
        return SimaiProcess.hsTable
            .Where(point => string.Equals(point.noteType, resolvedType,
                StringComparison.OrdinalIgnoreCase) && point.time <= time)
            .OrderBy(point => point.time)
            .Select(point => (float?)point.multiplier)
            .LastOrDefault() ?? fallback;
    }

    private static List<(double Time, float Multiplier)> BuildWaveSvCurve(string noteType)
    {
        var ordered = SimaiProcess.svTable.OrderBy(point => point.time).ToList();
        var globalAtZero = 1f;
        foreach (var point in ordered)
        {
            if (point.time > 0d)
                break;
            if (string.IsNullOrWhiteSpace(point.noteType))
                globalAtZero = point.multiplier;
        }

        float? typeOverride = null;
        if (!string.IsNullOrWhiteSpace(noteType))
        {
            foreach (var point in ordered)
            {
                if (point.time > 0d)
                    break;
                if (string.Equals(point.noteType, noteType, StringComparison.OrdinalIgnoreCase))
                    typeOverride = point.reset ? null : point.multiplier;
            }
        }

        var effective = typeOverride ?? globalAtZero;
        var curve = new List<(double Time, float Multiplier)> { (0d, effective) };
        foreach (var point in ordered)
        {
            if (point.time <= 0d ||
                (!string.IsNullOrWhiteSpace(point.noteType) &&
                 !string.Equals(point.noteType, noteType, StringComparison.OrdinalIgnoreCase)))
                continue;

            if (string.IsNullOrWhiteSpace(point.noteType))
            {
                globalAtZero = point.multiplier;
                if (!typeOverride.HasValue)
                    effective = globalAtZero;
            }
            else
            {
                typeOverride = point.reset ? null : point.multiplier;
                effective = typeOverride ?? globalAtZero;
            }

            if (Math.Abs(curve[^1].Time - point.time) < 0.000001d)
                curve[^1] = (point.time, effective);
            else
                curve.Add((point.time, effective));
        }
        return curve;
    }

    private static double WaveCumulativeAt(
        IReadOnlyList<(double Time, float Multiplier)> curve,
        double time)
    {
        var cumulative = 0d;
        if (time <= 0d)
            return curve[0].Multiplier * time;

        for (var index = 0; index < curve.Count; index++)
        {
            var start = curve[index].Time;
            if (start >= time)
                break;
            var end = index + 1 < curve.Count
                ? Math.Min(time, curve[index + 1].Time)
                : time;
            if (end > start)
                cumulative += curve[index].Multiplier * (end - start);
            if (end >= time)
                break;
        }
        return cumulative;
    }

    private double? FindScrollSpawnTime(
        double noteTime,
        float hSpeed,
        SimaiNoteType type,
        float spawnRadius,
        string scrollType)
    {
        if (editorSetting == null || hSpeed <= 0f)
            return null;

        var maiSpeed = type is SimaiNoteType.Touch or SimaiNoteType.TouchHold
            ? editorSetting.touchSpeed
            : editorSetting.playSpeed;
        var speed = (float)(107.25 / (71.4184491 * Math.Pow(maiSpeed + 0.9975f, -0.985558604))) * hSpeed;
        if (speed <= 0f || !float.IsFinite(speed))
            return null;

        var curve = BuildWaveSvCurve(scrollType);
        var firstVisibleScroll = WaveCumulativeAt(curve, noteTime) -
            (7.3d - spawnRadius) / speed;

        if (noteTime <= 0d)
            return curve[0].Multiplier > 0f
                ? firstVisibleScroll / curve[0].Multiplier
                : null;

        if (firstVisibleScroll <= 0d && curve[0].Multiplier > 0f)
            return firstVisibleScroll / curve[0].Multiplier;

        var cumulative = 0d;
        for (var index = 0; index < curve.Count; index++)
        {
            var start = curve[index].Time;
            if (start > noteTime)
                break;
            var end = index + 1 < curve.Count
                ? Math.Min(noteTime, curve[index + 1].Time)
                : noteTime;
            var multiplier = curve[index].Multiplier;
            var endCumulative = cumulative + multiplier * Math.Max(0d, end - start);
            if (multiplier > 0f && cumulative < firstVisibleScroll &&
                endCumulative >= firstVisibleScroll)
                return start + (firstVisibleScroll - cumulative) / multiplier;
            if (cumulative >= firstVisibleScroll)
                return start;
            cumulative = endCumulative;
            if (end >= noteTime)
                break;
        }
        return cumulative >= firstVisibleScroll ? noteTime : null;
    }

    private void DrawTimelineOverlay(
        Graphics graphics,
        double currentTime,
        double visibleRange,
        double step,
        int startIndex,
        float lineWidth,
        int height)
    {
        if (step <= 0d || lineWidth <= 0f)
            return;

        RefreshTimelineOverlayCache();
        using var labelFont = new Font("Cascadia Mono", 6.5f, System.Drawing.FontStyle.Regular);
        foreach (var item in timelineOverlayCache)
        {
            if (item.Time > currentTime + visibleRange)
                continue;

            var x = (float)(item.Time / step - startIndex) * lineWidth;
            var drawDuration = double.IsPositiveInfinity(item.Duration)
                ? Math.Max(visibleRange * 2d, currentTime + visibleRange - item.Time)
                : Math.Max(0d, item.Duration);
            var durationWidth = (float)(drawDuration / step) * lineWidth;
            var label = TrimTimelineLabel(item.Label);
            var textWidth = graphics.MeasureString(label, labelFont).Width + 5f;
            var width = drawDuration > 0.0001d ? Math.Max(2f, durationWidth) : 7f;
            var cullWidth = Math.Max(width, textWidth);
            var visualEndTime = item.Time + cullWidth / lineWidth * step;
            if (visualEndTime < currentTime - visibleRange)
                continue;
            var y = item.Lane * 13f;
            var rectangle = new RectangleF(x, y, width, 12f);
            using var gradient = new LinearGradientBrush(
                rectangle,
                Color.FromArgb(155, item.Color),
                Color.FromArgb(42, item.Color),
                LinearGradientMode.Horizontal);
            using var labelBrush = new SolidBrush(Color.FromArgb(235, 245, 238, 255));
            graphics.FillRectangle(gradient, rectangle);
            graphics.DrawString(label, labelFont, labelBrush,
                new PointF(x + 2f, y));
        }
    }

    private void RefreshTimelineOverlayCache()
    {
        if (ReferenceEquals(timelineDisplaySource, SimaiProcess.displayTable) &&
            ReferenceEquals(timelineEffectSource, SimaiProcess.effectTable) &&
            ReferenceEquals(timelineSubtitleSource, SimaiProcess.subtitleTable) &&
            ReferenceEquals(timelineMediaSource, SimaiProcess.mediaTable))
            return;

        timelineDisplaySource = SimaiProcess.displayTable;
        timelineEffectSource = SimaiProcess.effectTable;
        timelineSubtitleSource = SimaiProcess.subtitleTable;
        timelineMediaSource = SimaiProcess.mediaTable;
        timelineOverlayCache.Clear();

        timelineOverlayCache.AddRange(SimaiProcess.displayTable.Select(item =>
            new TimelineOverlayItem(item.time, Math.Max(0d, item.duration),
                FormatDisplayOverlayLabel(item), Color.FromArgb(182, 92, 255))));
        timelineOverlayCache.AddRange(SimaiProcess.effectTable.Select(item =>
            new TimelineOverlayItem(item.time, Math.Max(0d, item.duration),
                FormatEffectOverlayLabel(item), Color.FromArgb(182, 92, 255))));

        var subtitles = SimaiProcess.subtitleTable;
        for (var i = 0; i < subtitles.Count; i++)
        {
            var item = subtitles[i];
            var duration = item.duration >= 0f
                ? item.duration
                : i + 1 < subtitles.Count
                    ? Math.Max(0d, subtitles[i + 1].time - item.time)
                    : double.PositiveInfinity;
            timelineOverlayCache.Add(new TimelineOverlayItem(item.time, duration,
                $"TEXT:{TrimTimelineLabel(item.text)}", Color.FromArgb(182, 92, 255)));
        }

        timelineOverlayCache.AddRange(SimaiProcess.mediaTable.Select(item =>
            new TimelineOverlayItem(item.time, Math.Max(0d, item.transition),
                FormatMediaOverlayLabel(item), Color.FromArgb(36, 188, 194))));

        timelineOverlayCache.Sort((left, right) => left.Time.CompareTo(right.Time));
        var laneEnds = Enumerable.Repeat(double.NegativeInfinity, 5).ToArray();
        var overflowLane = 0;
        foreach (var item in timelineOverlayCache)
        {
            var lane = -1;
            for (var candidate = 0; candidate < laneEnds.Length; candidate++)
            {
                if (laneEnds[candidate] <= item.Time + 0.0001d)
                {
                    lane = candidate;
                    break;
                }
            }

            if (lane < 0)
                lane = overflowLane++ % laneEnds.Length;
            item.Lane = lane;
            laneEnds[lane] = item.Time + Math.Max(0d, item.Duration);
        }
    }

    private static string FormatDisplayOverlayLabel(DisplayChange item)
    {
        if (item.property.StartsWith("Show", StringComparison.Ordinal))
            return $"{item.property} {(item.target >= 0.5f ? "True" : "False")}";
        return $"{item.property} {item.target:0.##}";
    }

    private static string FormatEffectOverlayLabel(EffectChange item)
    {
        if (!item.stateful)
            return $"{item.effect} {item.intensity:0.##}";
        if (!item.enabled)
            return $"{item.effect} False";
        return item.effect switch
        {
            "Move" => $"Move True ({item.paramA:0.##},{item.paramB:0.##})",
            "Tint" => $"Tint True #{item.color} {item.intensity:0.##}",
            "Shake" when item.hasDirection =>
                $"Shake True {item.intensity:0.##}@{item.paramA:0.##}Hz ∠{item.paramB * 180f / MathF.PI:0.#}°",
            "Shake" => $"Shake True {item.intensity:0.##}@{item.paramA:0.##}Hz",
            _ => $"{item.effect} True {item.intensity:0.##}"
        };
    }

    private static string FormatMediaOverlayLabel(MediaChange item)
    {
        var name = item.kind == "pvOverlay" ? "PV" : "AUDIO";
        return item.enabled
            ? $"{name}:{TrimTimelineLabel(Path.GetFileName(item.path))}"
            : $"{name}:OFF";
    }

    private sealed class TimelineOverlayItem
    {
        public TimelineOverlayItem(double time, double duration, string label, Color color)
        {
            Time = time;
            Duration = duration;
            Label = label;
            Color = color;
        }

        public double Time { get; }
        public double Duration { get; }
        public string Label { get; }
        public Color Color { get; }
        public int Lane { get; set; }
    }

    private void DrawRecordingFlowBackground(
        Graphics graphics,
        double currentTime,
        double visibleRange,
        double step,
        int startIndex,
        float lineWidth,
        int height)
    {
        if (editorSetting?.ShowSongDetail == true)
        {
            DrawFlowBackground(graphics, -RecordingIntroDuration, -1d, GetLocalizedString("RecordingLoadLabel"),
                Color.FromArgb(85, 160, 245), currentTime, visibleRange, step, startIndex, lineWidth, height);
            DrawFlowBackground(graphics, -1d, 0d, GetLocalizedString("TransitionLabel"),
                Color.FromArgb(70, 210, 175), currentTime, visibleRange, step, startIndex, lineWidth, height);
        }
        var allPerfectStart = GetAllPerfectStartTime();
        if (editorSetting?.ShowAllPerfect == true && allPerfectStart >= 0d)
            DrawFlowBackground(graphics, allPerfectStart, allPerfectStart + AllPerfectDuration, "ALL PERFECT",
                Color.FromArgb(235, 95, 190), currentTime, visibleRange, step, startIndex, lineWidth, height);
    }

    private static void DrawFlowBackground(
        Graphics graphics,
        double start,
        double end,
        string label,
        Color color,
        double currentTime,
        double visibleRange,
        double step,
        int startIndex,
        float lineWidth,
        int height)
    {
        if (start > currentTime + visibleRange || end < currentTime - visibleRange)
            return;

        var x = (float)(start / step - startIndex) * lineWidth;
        var width = Math.Max(4f, (float)((end - start) / step) * lineWidth);
        var rectangle = new RectangleF(x, 0f, width, height);
        using var gradient = new LinearGradientBrush(
            rectangle,
            Color.FromArgb(105, color),
            Color.FromArgb(0, color),
            LinearGradientMode.Horizontal);
        using var labelFont = new Font("Cascadia Mono", 6.5f, System.Drawing.FontStyle.Regular);
        using var labelBrush = new SolidBrush(Color.FromArgb(225, 245, 248, 255));
        graphics.FillRectangle(gradient, rectangle);
        graphics.DrawString(label, labelFont, labelBrush, new PointF(x + 3f, 1f));
    }

    private static string TrimTimelineLabel(string text)
    {
        const int maxLength = 28;
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text ?? "";
        return text.Substring(0, maxLength - 3) + "...";
    }

    internal void RefreshWaveNoteSkin()
    {
        var current = GetTimelinePosition();
        var clamped = Math.Clamp(current, GetTimelineMinimum(), GetTimelineMaximum());
        if (Math.Abs(current - clamped) > 0.0001d)
            SetTimelinePosition(clamped);
        DrawWave();
    }

    // This update less frequently. set the time text.
    private void CurrentTimeRefreshTimer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        UpdateTimeDisplay();
    }

    private void UpdateTimeDisplay()
    {
        var currentPlayTime = GetTimelinePosition();
        var absolute = Math.Abs(currentPlayTime);
        var minute = (int)absolute / 60;
        var second = (int)(absolute - 60 * minute);
        var fraction = absolute - Math.Floor(absolute);
        Dispatcher.Invoke(() =>
        {
            TimeLabel.Content = $"{minute}:{second:00}";
            NoteNowText.Content = fraction.ToString(".0000", System.Globalization.CultureInfo.InvariantCulture);
            if (MediaTimelinePanel.Visibility == Visibility.Visible && !waveScrubActive)
                MediaTimelinePanel.SyncPlayhead(currentPlayTime);
        });
    }

    private void ScrollWave(double delta)
    {
        CancelNotePreview();
        if (isPlaying || ((lastEditorState == EditorControlMethod.Pause || pausePending) &&
                          (pendingScrubStop == null || pendingScrubStop.IsCompleted)))
            StopPlaybackForScrub();
        delta = delta * deltatime / (Width / 2d);
        var time = GetTimelinePosition();
        SetTimelinePosition(time + delta);
        SimaiProcess.ClearNoteListPlayedState();
        if (GetTimelinePosition() >= 0d && GetTimelinePosition() <= songLength)
            SeekTextFromTime();
        DrawWave();
    }

    private bool TryGetMediaTrimRange(out MediaRange range)
    {
        range = default;
        SimaiProcess.Serialize(GetRawFumenText(), GetRawFumenPosition());
        if (!SimaiProcess.mediaTrimStart.HasValue || !SimaiProcess.mediaTrimEnd.HasValue)
            return false;

        var start = Math.Min(SimaiProcess.mediaTrimStart.Value, SimaiProcess.mediaTrimEnd.Value);
        var end = Math.Max(SimaiProcess.mediaTrimStart.Value, SimaiProcess.mediaTrimEnd.Value);
        if (end - start < 0.001d)
            return false;

        range = new MediaRange(start, end);
        return true;
    }

    private double GetBeatDuration(double beatCount)
    {
        if (!double.IsFinite(beatCount) || beatCount <= 0d)
            throw new InvalidOperationException(GetLocalizedString("InvalidBeatCount"));
        var bpmText = SimaiProcess.GetWholeBpmText();
        var bpm = double.TryParse(bpmText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : SimaiProcess.timinglist.FirstOrDefault(point => point.currentBpm > 0f)?.currentBpm ?? 0d;
        if (!double.IsFinite(bpm) || bpm <= 0d)
            throw new InvalidOperationException(GetLocalizedString("NoValidBpm"));
        return beatCount * 60d / bpm;
    }

    private async Task RunMediaEditAsync(string filePath, Func<Task> editOperation)
    {
        if (mediaToolRunning)
            throw new InvalidOperationException(GetLocalizedString("MediaToolBusy"));

        mediaToolRunning = true;
        var currentTrackPath = GetCurrentTrackPath();
        var hasOpenChart = currentTrackPath != null;
        var currentPosition = hasOpenChart
            ? Math.Clamp(GetTimelinePosition(), 0d, Math.Max(0d, songLength))
            : 0d;
        var editsCurrentTrack = currentTrackPath != null &&
                                string.Equals(Path.GetFullPath(filePath), Path.GetFullPath(currentTrackPath),
                                    StringComparison.OrdinalIgnoreCase);
        var editsCurrentChartMedia = !string.IsNullOrWhiteSpace(maidataDir) &&
                                     string.Equals(Path.GetDirectoryName(Path.GetFullPath(filePath)),
                                         Path.GetFullPath(maidataDir).TrimEnd(Path.DirectorySeparatorChar),
                                         StringComparison.OrdinalIgnoreCase);
        var volume = editorSetting?.Default_BGM_Level ?? 1f;
        var tempo = 0f;
        if (bgmStream > 0)
        {
            Bass.BASS_ChannelGetAttribute(bgmStream, BASSAttribute.BASS_ATTRIB_VOL, ref volume);
            Bass.BASS_ChannelGetAttribute(bgmStream, BASSAttribute.BASS_ATTRIB_TEMPO, ref tempo);
        }

        try
        {
            if (editsCurrentChartMedia)
            {
                CancelNotePreview();
                if (pendingNotePreviewSend != null)
                    await Task.WhenAny(pendingNotePreviewSend, Task.Delay(1500));
                if (pendingPlaySend != null)
                    await Task.WhenAny(pendingPlaySend, Task.Delay(3000));

                // Stop only after older requests have drained. The Stop response is a
                // barrier: View acknowledges it after the reloaded scene releases media.
                ToggleStop();
                await Task.Delay(100);
            }

            if (editsCurrentTrack && bgmStream > 0)
            {
                Bass.BASS_ChannelStop(bgmStream);
                Bass.BASS_StreamFree(bgmStream);
                bgmStream = -1024;
            }

            await editOperation();
        }
        finally
        {
            try
            {
                if (editsCurrentTrack)
                    ReloadCurrentTrack(currentPosition, volume, tempo);
            }
            finally
            {
                mediaToolRunning = false;
            }
        }
    }

    private string? GetCurrentTrackPath()
    {
        if (!string.IsNullOrWhiteSpace(timelineAudioSourcePath) && File.Exists(timelineAudioSourcePath))
            return timelineAudioSourcePath;
        return GetOriginalTrackPath();
    }

    private string? GetOriginalTrackPath()
    {
        if (string.IsNullOrWhiteSpace(maidataDir))
            return null;
        var ogg = Path.Combine(maidataDir, "track.ogg");
        if (File.Exists(ogg))
            return ogg;
        var mp3 = Path.Combine(maidataDir, "track.mp3");
        return File.Exists(mp3) ? mp3 : null;
    }

    private void ReloadCurrentTrack(double position, float volume, float tempo)
    {
        var audioPath = GetCurrentTrackPath();
        if (audioPath == null || !File.Exists(audioPath))
            return;

        var decodeStream = Bass.BASS_StreamCreateFile(audioPath, 0L, 0L,
            BASSFlag.BASS_STREAM_DECODE | BASSFlag.BASS_STREAM_PRESCAN);
        if (decodeStream == 0)
            throw new InvalidOperationException(string.Format(
                GetLocalizedString("AudioDecodeFailed"), Bass.BASS_ErrorGetCode()));

        bgmStream = BassFx.BASS_FX_TempoCreate(decodeStream, BASSFlag.BASS_FX_FREESOURCE);
        if (bgmStream == 0)
        {
            Bass.BASS_StreamFree(decodeStream);
            throw new InvalidOperationException(string.Format(
                GetLocalizedString("AudioDecodeFailed"), Bass.BASS_ErrorGetCode()));
        }

        Bass.BASS_ChannelSetAttribute(bgmStream, BASSAttribute.BASS_ATTRIB_VOL, volume);
        Bass.BASS_ChannelSetAttribute(bgmStream, BASSAttribute.BASS_ATTRIB_TEMPO, tempo);
        loadedTrackPath = audioPath;
        ReadWaveFromFile();
        cachedWaveTimingList = null;
        cachedWaveMeterList = null;
        cachedWaveSongEnd = double.NaN;
        flowTimelineCursor = null;
        flowPreviewActive = false;
        Bass.BASS_ChannelSetPosition(bgmStream, Math.Clamp(position, 0d, songLength));
        DrawWave();
    }

    private void StopPlaybackForScrub()
    {
        // Capture both tasks before publishing the new stop. Reading pendingPlaySend
        // inside the worker creates a cycle when a new Play replaces it first:
        // Stop waits for Play while Play waits for Stop.
        var previousStop = pendingScrubStop;
        var playToDrain = pendingPlaySend;
        var previewToDrain = pendingNotePreviewSend;

        viewControlGeneration++;
        pausePending = false;
        flowPreviewActive = false;
        flowPreviewGeneration++;
        Op_Button.IsEnabled = true;
        isPlaying = false;
        isPlan2Stop = false;
        FumenContent.Focus();
        PlayAndPauseButton.Content = "▶";
        TimelinePlayAndPauseButton.Content = "▶";
        Bass.BASS_ChannelStop(bgmStream);
        Bass.BASS_ChannelStop(trackStartStream);
        Bass.BASS_ChannelStop(clockStream);
        Bass.BASS_ChannelStop(allperfectStream);
        Bass.BASS_ChannelStop(fanfareStream);
        Bass.BASS_ChannelStop(holdRiserStream);
        waveStopMonitorTimer.Stop();
        visualEffectRefreshTimer.Stop();

        // Every scrub queues a barrier. Do not return early when an older barrier is
        // active: a Play may already be queued behind it and must be stopped again.
        pendingScrubStop = Task.Run(() => SendScrubStop(previousStop, playToDrain, previewToDrain));
    }

    private double GetTimelinePosition()
    {
        if (flowPreviewActive)
        {
            if (flowPreviewAwaitingView)
                return flowPreviewStartTime;
            var elapsed = (DateTime.Now - flowPreviewStartedAt).TotalSeconds * GetPlaybackSpeed();
            return flowPreviewStartTime + elapsed;
        }

        return flowTimelineCursor ??
               Bass.BASS_ChannelBytes2Seconds(bgmStream, Bass.BASS_ChannelGetPosition(bgmStream));
    }

    private void SetTimelinePosition(double time, bool keepFlowCursor = false)
    {
        CancelNotePreview();
        time = Math.Clamp(time, GetTimelineMinimum(), GetTimelineMaximum());
        flowPreviewActive = false;
        flowPreviewAwaitingView = false;
        flowPreviewGeneration++;

        if (!keepFlowCursor && time >= 0d && time <= songLength)
        {
            flowTimelineCursor = null;
            SetBgmPosition(time);
        }
        else
        {
            flowTimelineCursor = time;
            if (time < 0d)
                Bass.BASS_ChannelSetPosition(bgmStream, 0d);
            else
                Bass.BASS_ChannelSetPosition(bgmStream, songLength);
        }
    }

    public static string GetLocalizedString(string key, string resourceFileName = "Langs", bool addSpaceAfter = false)
    {
        var assemblyName = Assembly.GetExecutingAssembly().GetName().Name;
        var culture = LocalizeDictionary.Instance.Culture ?? CultureInfo.CurrentUICulture;
        string? localizedString = null;

        try
        {
            var baseName = $"{assemblyName}.{resourceFileName}.{resourceFileName}";
            localizedString = new System.Resources.ResourceManager(
                    baseName,
                    Assembly.GetExecutingAssembly())
                .GetString(key, culture);
        }
        catch (System.Resources.MissingManifestResourceException)
        {
            // Keep the key as a readable fallback instead of exposing a resource URI.
        }

        localizedString ??= key;
        return addSpaceAfter ? localizedString + " " : localizedString;
    }

    private void TogglePlay(PlayMethod playMethod = PlayMethod.Normal)
    {
        if (Op_Button.IsEnabled == false) return;
        CancelNotePreview();
        EnsureTimelineAudioReady();
        var previewToDrain = pendingNotePreviewSend;
        var scrubStopToDrain = pendingScrubStop;
            viewControlGeneration++;
        pausePending = false;
        // Ignore a delayed BGM callback after a newer transport action.
        var playGeneration = viewControlGeneration;

        if ((lastEditorState == EditorControlMethod.Start || playMethod != PlayMethod.Normal) &&
            (pendingScrubStop == null || pendingScrubStop.IsCompleted))
            if (!sendRequestStop())
                return;

        FumenContent.Focus();
        SaveFumen();
        if (CheckAndStartView()) return;
        var CusorTime = SimaiProcess.Serialize(GetRawFumenText(), GetRawFumenPosition()); //scan first
        if (!ValidateMediaFiles())
            return;
        Op_Button.IsEnabled = false;
        isPlaying = true;
        isPlan2Stop = false;
        PlayAndPauseButton.Content = "  ▌▌ ";
        TimelinePlayAndPauseButton.Content = " ▌▌";

        // TODO: Moeying, update generateSoundEffect and remove the following line.
        var isOpIncluded = playMethod == PlayMethod.Normal ? false : true;

        var startAt = DateTime.Now;
        switch (playMethod)
        {
            case PlayMethod.Record60:
            case PlayMethod.Record120:
                Bass.BASS_ChannelSetPosition(bgmStream, 0);
                //TODO: i18n
            // RecordVideoWindow already confirms multi-pass exports.
                if (pendingRecordOptions == null)
                    MessageBox.Show(GetLocalizedString("AskRender"), GetLocalizedString("Attention"));
                generateSoundEffectList(0.0, isOpIncluded);
                var recordingIntroDuration = editorSetting?.ShowSongDetail == true
                    ? RecordingIntroDuration
                    : 0d;
                var task = new Task(() => renderSoundEffect(recordingIntroDuration));
                try
                {
                    task.Start();
                    task.Wait();
                }
                catch (AggregateException)
                {
                    MessageBox.Show(task.Exception!.InnerException!.Message + "\n" +
                                    task.Exception.InnerException.StackTrace);
                    FinishRecordRun();
                    return;
                }

                startAt = DateTime.Now.AddSeconds(recordingIntroDuration);
                if (!sendRequestRun(startAt, playMethod))
                {
                    RestoreFailedPlaybackStart(playGeneration);
                    return;
                }
                InternalSwitchWindow(false);
                break;
            case PlayMethod.Op:
                generateSoundEffectList(0.0, isOpIncluded);
                InternalSwitchWindow(false);
                Bass.BASS_ChannelSetPosition(bgmStream, 0);
                startAt = DateTime.Now.AddSeconds(5d);
                Bass.BASS_ChannelPlay(trackStartStream, true);
                var opStartAt = startAt;
                var opSend = Task.Run(() => sendRequestRun(opStartAt, playMethod));
                pendingPlaySend = opSend;
                Task.Run(() =>
                {
                    if (!opSend.Result)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            if (playGeneration == viewControlGeneration)
                                RestoreFailedPlaybackStart(playGeneration);
                        });
                        return;
                    }
                    while (DateTime.Now.Ticks < opStartAt.Ticks)
                        if (lastEditorState != EditorControlMethod.Start ||
                            playGeneration != viewControlGeneration ||
                            !isPlaying)
                            return;
                    Dispatcher.Invoke(() =>
                    {
                        if (!isPlaying || playGeneration != viewControlGeneration)
                            return;
                        playStartTime =
                            Bass.BASS_ChannelBytes2Seconds(bgmStream, Bass.BASS_ChannelGetPosition(bgmStream));
                        SimaiProcess.ClearNoteListPlayedState();
                        Bass.BASS_ChannelStop(trackStartStream);
                        StartSELoop();
                        //soundEffectTimer.Start();
                        waveStopMonitorTimer.Start();
                        visualEffectRefreshTimer.Start();
                        Bass.BASS_ChannelPlay(bgmStream, false);
                    });
                });
                break;
            case PlayMethod.Normal:
                if (flowTimelineCursor.HasValue)
                {
                    StartFlowPreview(flowTimelineCursor.Value);
                    break;
                }

                playStartTime = Bass.BASS_ChannelBytes2Seconds(bgmStream, Bass.BASS_ChannelGetPosition(bgmStream));
                generateSoundEffectList(playStartTime, isOpIncluded);

                if (lastEditorState == EditorControlMethod.Pause &&
                    (pendingScrubStop == null || pendingScrubStop.IsCompleted))
                {
                    SimaiProcess.ClearNoteListPlayedState();
                    startAt = DateTime.Now;
                    var continueAt = startAt;
                    var continueSend = Task.Run(() => sendRequestContinue(continueAt));
                    pendingPlaySend = continueSend;
                    Task.Run(() =>
                    {
                        if (!continueSend.Result)
                        {
                            Dispatcher.Invoke(() => RestoreFailedPlaybackStart(playGeneration));
                            return;
                        }
                        while (DateTime.Now.Ticks < continueAt.Ticks)
                            if (lastEditorState != EditorControlMethod.Start) return;
                        Dispatcher.Invoke(() =>
                        {
                            if (!isPlaying || playGeneration != viewControlGeneration) return;
                            StartSELoop();
                            waveStopMonitorTimer.Start();
                            visualEffectRefreshTimer.Start();
                            Bass.BASS_ChannelPlay(bgmStream, false);
                        });
                    });
                }
                else
                {
                    // Fresh play (incl. after a scrub): send the chart FIRST, then start the
                    // BGM at startAt. Pin startTime to the scrub position so neither side
                    // reads a moving BGM cursor.
                    var bgmStartPos = playStartTime;
                    var runAt = DateTime.MinValue;
                    var runSend = Task.Run(async () =>
                    {
                        var actualStart = await SendRunAfterScrubStop(
                            playMethod, (float)bgmStartPos, previewToDrain, scrubStopToDrain);
                        if (!actualStart.HasValue)
                            return false;

                        runAt = actualStart.Value;
                        return true;
                    });
                    pendingPlaySend = runSend;
                    Task.Run(() =>
                    {
                        if (!runSend.Result)
                        {
                            Dispatcher.Invoke(() => RestoreFailedPlaybackStart(playGeneration));
                            return;
                        }
                        while (DateTime.Now.Ticks < runAt.Ticks)
                            if (lastEditorState != EditorControlMethod.Start) return;
                        Dispatcher.Invoke(() =>
                        {
                            if (!isPlaying || playGeneration != viewControlGeneration) return;
                            Bass.BASS_ChannelSetPosition(bgmStream, bgmStartPos);
                            SimaiProcess.ClearNoteListPlayedState();
                            StartSELoop();
                            waveStopMonitorTimer.Start();
                            visualEffectRefreshTimer.Start();
                            Bass.BASS_ChannelPlay(bgmStream, false);
                        });
                    });
                }
                break;
        }

        ghostCusorPositionTime = (float)CusorTime;
        DrawWave();
    }

    private void RestoreFailedPlaybackStart(int generation)
    {
        if (generation != viewControlGeneration)
            return;

        isPlaying = false;
        isPlan2Stop = false;
        pausePending = false;
        Op_Button.IsEnabled = true;
        PlayAndPauseButton.Content = "▶";
        TimelinePlayAndPauseButton.Content = "▶";
        Bass.BASS_ChannelStop(trackStartStream);
        Bass.BASS_ChannelStop(bgmStream);
        waveStopMonitorTimer.Stop();
        visualEffectRefreshTimer.Stop();
        DrawWave();
    }

    private bool ValidateMediaFiles()
    {
        var root = Path.GetFullPath(maidataDir);
        var rootWithSeparator = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                                Path.DirectorySeparatorChar;
        foreach (var media in GetEffectiveMediaTable().Where(item => item.enabled))
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(Path.Combine(
                    root, media.path.Replace('/', Path.DirectorySeparatorChar)));
            }
            catch
            {
                fullPath = "";
            }

            if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(fullPath))
            {
                MessageBox.Show(
                    string.Format(GetLocalizedString("MediaFileMissing"), media.path),
                    GetLocalizedString("Attention"));
                return false;
            }
        }
        return true;
    }

    /// <summary>Starts one capture pass with RecordVideoWindow options.</summary>
    internal void StartRecordRun(RecordVideoOptions options)
    {
        // The previous recording, especially a failed one, may leave Op_Button disabled and isPlaying true.
        // TogglePlay then returns immediately, leaving View unresponsive from the second layered export onward.
        FinishRecordRun();
        pendingRecordOptions = options;
        TogglePlayAndPause(options.FrameRate >= 120 ? PlayMethod.Record120 : PlayMethod.Record60);
    }

    /// <summary>Resets transfer state after a recording run, successful or not, so the next run or playback can start.</summary>
    internal void FinishRecordRun()
    {
        Op_Button.IsEnabled = true;
        isPlaying = false;
        isPlan2Stop = false;
        PlayAndPauseButton.Content = "▶";
        TimelinePlayAndPauseButton.Content = "▶";
    }

    internal void CancelRecordRun()
    {
        try
        {
            sendRequestStop();
        }
        finally
        {
            FinishRecordRun();
        }
    }

    /// <summary>Returns the selected chart length for capture timeout estimation.</summary>
    internal double GetChartLengthForRecord()
    {
        double len = 0;
        foreach (var tp in SimaiProcess.notelist)
        {
            len = Math.Max(len, tp.time);
            foreach (var note in tp.getNotes())
                len = Math.Max(len, note.noteType == SimaiNoteType.Slide
                    ? note.slideStartTime + note.slideTime
                    : tp.time + note.holdTime);
        }
        return len;
    }

    internal (int songDetailStyle, bool showSongDetail, bool showAllPerfect, string introStyle)
        GetRecordDisplayDefaults()
    {
        if (editorSetting == null)
            ReadEditorSetting();

        return (
            editorSetting?.SongDetailStyle ?? 0,
            editorSetting?.ShowSongDetail ?? true,
            editorSetting?.ShowAllPerfect ?? true,
            editorSetting?.ViewIntroStyle ?? "default");
    }

    internal void ApplyRecordDisplaySettings(
        int songDetailStyle,
        bool showSongDetail,
        bool showAllPerfect,
        string introStyle)
    {
        if (editorSetting == null)
            ReadEditorSetting();

        if (editorSetting == null)
            return;

        var normalizedStyle = Math.Clamp(songDetailStyle, 0, 1);
        var normalizedIntroStyle = introStyle?.ToLowerInvariant() switch
        {
            "circleplus" => "circleplus",
            "circle" => "circle",
            _ => "default"
        };
        if (editorSetting.SongDetailStyle == normalizedStyle &&
            editorSetting.ShowSongDetail == showSongDetail &&
            editorSetting.ShowAllPerfect == showAllPerfect &&
            string.Equals(editorSetting.ViewIntroStyle, normalizedIntroStyle,
                StringComparison.OrdinalIgnoreCase))
            return;

        editorSetting.SongDetailStyle = normalizedStyle;
        editorSetting.ShowSongDetail = showSongDetail;
        editorSetting.ShowAllPerfect = showAllPerfect;
        editorSetting.ViewIntroStyle = normalizedIntroStyle;
        SaveEditorSetting();
        DrawWave();
    }

    private double GetTimelineMinimum() =>
        editorSetting?.ShowSongDetail == true ? -RecordingIntroDuration : 0d;

    private double GetTimelineMaximum()
    {
        var mediaEnd = double.IsFinite(waveformDisplayLength) && waveformDisplayLength > 0d
            ? Math.Max(songLength, waveformDisplayLength)
            : songLength;
        if (editorSetting?.ShowAllPerfect != true)
            return mediaEnd;
        return Math.Max(mediaEnd, GetAllPerfectStartTime() + AllPerfectDuration);
    }

    private IReadOnlyList<float> GetDensityAudioEnvelope()
    {
        var source = waveRaws[0];
        if (source == null || source.Length == 0)
            return Array.Empty<float>();
        if (ReferenceEquals(source, densityAudioEnvelopeSource))
            return densityAudioEnvelope;

        var pointCount = Math.Min(2048, source.Length);
        var peaks = new float[pointCount];
        for (var point = 0; point < pointCount; point++)
        {
            var start = (int)((long)point * source.Length / pointCount);
            var end = Math.Max(start + 1, (int)((long)(point + 1) * source.Length / pointCount));
            var peak = 0;
            for (var index = start; index < end && index < source.Length; index++)
                peak = Math.Max(peak, Math.Abs((int)source[index]));
            peaks[point] = peak;
        }

        var sorted = (float[])peaks.Clone();
        Array.Sort(sorted);
        var floor = sorted[Math.Clamp((int)(sorted.Length * 0.08f), 0, sorted.Length - 1)];
        var ceiling = sorted[Math.Clamp((int)(sorted.Length * 0.98f), 0, sorted.Length - 1)];
        var range = Math.Max(1f, ceiling - floor);
        var normalizedLevels = new float[peaks.Length];
        for (var i = 0; i < peaks.Length; i++)
            normalizedLevels[i] = Math.Clamp((peaks[i] - floor) / range, 0f, 1f);

        const int filterRadius = 4;
        for (var i = 0; i < peaks.Length; i++)
        {
            var weighted = 0f;
            var weightSum = 0f;
            for (var offset = -filterRadius; offset <= filterRadius; offset++)
            {
                var sourceIndex = Math.Clamp(i + offset, 0, normalizedLevels.Length - 1);
                var weight = filterRadius + 1 - Math.Abs(offset);
                weighted += normalizedLevels[sourceIndex] * weight;
                weightSum += weight;
            }
            peaks[i] = weighted / weightSum;
        }

        densityAudioEnvelopeSource = source;
        densityAudioEnvelope = peaks;
        return densityAudioEnvelope;
    }

    internal (string title, string artist, string designer, string level, string bpm, string clock, string difficulty)
        GetRecordSongInfo()
    {
        var difficulty = selectedDifficulty >= 0
            ? SimaiProcess.GetDifficultyText(selectedDifficulty)
            : "UNKNOWN";
        var level = selectedDifficulty >= 0 && selectedDifficulty < SimaiProcess.levels.Length
            ? SimaiProcess.levels[selectedDifficulty] ?? ""
            : "";
        return (
            SimaiProcess.title ?? "",
            SimaiProcess.artist ?? "",
            SimaiProcess.GetDesignerText(Math.Max(0, selectedDifficulty)),
            level,
            SimaiProcess.GetWholeBpmText(),
            SimaiProcess.GetClockCountText(),
            difficulty);
    }

    internal void ApplyRecordSongInfo(
        string title,
        string artist,
        string designer,
        string level,
        string bpm,
        string clock)
    {
        title = title.Trim();
        artist = artist.Trim();
        designer = designer.Trim();
        level = level.Trim();
        bpm = bpm.Trim();
        clock = clock.Trim();
        var currentLevel = selectedDifficulty >= 0 && selectedDifficulty < SimaiProcess.levels.Length
            ? SimaiProcess.levels[selectedDifficulty]?.Trim() ?? ""
            : "";
        if (string.Equals(SimaiProcess.title?.Trim(), title, StringComparison.Ordinal) &&
            string.Equals(SimaiProcess.artist?.Trim(), artist, StringComparison.Ordinal) &&
            string.Equals(SimaiProcess.GetDesignerText(Math.Max(0, selectedDifficulty)), designer,
                StringComparison.Ordinal) &&
            string.Equals(currentLevel, level, StringComparison.Ordinal) &&
            string.Equals(SimaiProcess.GetWholeBpmText(), bpm, StringComparison.Ordinal) &&
            string.Equals(SimaiProcess.GetClockCountText(), clock, StringComparison.Ordinal))
            return;

        SimaiProcess.title = title;
        SimaiProcess.artist = artist;
        SimaiProcess.designer = designer;
        SimaiProcess.wholeBpm = bpm;
        SimaiProcess.clockCount = clock;
        if (selectedDifficulty >= 0 && selectedDifficulty < SimaiProcess.levels.Length)
        {
            SimaiProcess.levels[selectedDifficulty] = level;
            suppressLevelTextChange = true;
            LevelTextBox.Text = SimaiProcess.levels[selectedDifficulty];
            suppressLevelTextChange = false;
            InvalidateSongDetailCache(selectedDifficulty);
        }
        TheWindow.Title = GetWindowsTitleString(SimaiProcess.title);
        SetSavedState(false);
        SchedulePreBakeSongDetail();
    }

    private void TogglePause()
    {
        CancelNotePreview();
        var generation = ++viewControlGeneration;
        pausePending = true;
        if (flowPreviewActive)
        {
            var pausedFlowTime = GetTimelinePosition();
            flowPreviewActive = false;
            flowPreviewGeneration++;
            flowTimelineCursor = pausedFlowTime;
        }
        Op_Button.IsEnabled = true;
        isPlaying = false;
        isPlan2Stop = false;

        FumenContent.Focus();
        PlayAndPauseButton.Content = "▶";
        TimelinePlayAndPauseButton.Content = "▶";
        Bass.BASS_ChannelStop(bgmStream);
        Bass.BASS_ChannelStop(trackStartStream);
        Bass.BASS_ChannelStop(clockStream);
        Bass.BASS_ChannelStop(allperfectStream);
        Bass.BASS_ChannelStop(fanfareStream);
        Bass.BASS_ChannelStop(holdRiserStream);
        //soundEffectTimer.Stop();
        waveStopMonitorTimer.Stop();
        visualEffectRefreshTimer.Stop();
        SendControlOrdered(generation, sendRequestPause);
        DrawWave();
    }

    /// <summary>
    /// Sends Pause or Stop after the pending playback request unless superseded.
    /// </summary>
    private void SendControlOrdered(int generation, Func<bool> send)
    {
        var pending = pendingPlaySend;
        if (pending == null || pending.IsCompleted)
        {
            send();
            return;
        }

        Task.Run(() =>
        {
            try
            {
                pending.Wait(3000);
            }
            catch
            {
                // A failed predecessor must not suppress the latest control request.
            }
            Dispatcher.Invoke(() =>
            {
                if (generation == viewControlGeneration)
                    send();
            });
        });
    }

    private void ToggleStop()
    {
        CancelNotePreview();
        var generation = ++viewControlGeneration;
        pausePending = false;
        flowPreviewActive = false;
        flowPreviewGeneration++;
        Op_Button.IsEnabled = true;
        isPlaying = false;
        isPlan2Stop = false;

        FumenContent.Focus();
        PlayAndPauseButton.Content = "▶";
        TimelinePlayAndPauseButton.Content = "▶";
        Bass.BASS_ChannelStop(bgmStream);
        Bass.BASS_ChannelStop(trackStartStream);
        Bass.BASS_ChannelStop(clockStream);
        Bass.BASS_ChannelStop(allperfectStream);
        Bass.BASS_ChannelStop(fanfareStream);
        Bass.BASS_ChannelStop(holdRiserStream);
        //soundEffectTimer.Stop();
        waveStopMonitorTimer.Stop();
        visualEffectRefreshTimer.Stop();
        SendControlOrdered(generation, sendRequestStop);
        SetTimelinePosition(playStartTime);
        DrawWave();
    }

    private void StartFlowPreview(double startTime)
    {
        startTime = Math.Clamp(startTime, GetTimelineMinimum(), GetTimelineMaximum());
        playStartTime = startTime;
        flowTimelineCursor = null;
        flowPreviewActive = true;
        flowPreviewAwaitingView = true;
        flowPreviewStartTime = startTime;
        var generation = ++flowPreviewGeneration;
        var playbackSpeed = GetPlaybackSpeed();
        var leadInSeconds = startTime < 0d ? -startTime / playbackSpeed : 0d;
        var viewStartTime = startTime < 0d ? 0f : (float)startTime;

        generateSoundEffectList(Math.Max(0d, startTime), true);

        var requestPlayMethod = startTime < 0d ? PlayMethod.Op : PlayMethod.Normal;
        var flowSend = Task.Run(() => sendRequestRun(DateTime.Now, requestPlayMethod, viewStartTime, true));
        pendingPlaySend = flowSend;
        Task.Run(async () =>
        {
            bool sent;
            try
            {
                sent = await flowSend.ConfigureAwait(false);
            }
            catch
            {
                sent = false;
            }

            var canStart = false;
            Dispatcher.Invoke(() =>
            {
                if (!sent || !flowPreviewActive || generation != flowPreviewGeneration)
                {
                    if (generation == flowPreviewGeneration)
                    {
                        flowPreviewActive = false;
                        flowPreviewAwaitingView = false;
                        isPlaying = false;
                        Op_Button.IsEnabled = true;
                        PlayAndPauseButton.Content = "▶";
                        TimelinePlayAndPauseButton.Content = "▶";
                        visualEffectRefreshTimer.Stop();
                        DrawWave();
                    }
                    return;
                }

                flowPreviewStartedAt = DateTime.Now;
                flowPreviewAwaitingView = false;
                visualEffectRefreshTimer.Start();
                if (startTime < 0d)
                {
                    var introPosition = Math.Clamp(
                        RecordingIntroDuration + startTime, 0d, RecordingIntroDuration);
                    Bass.BASS_ChannelSetPosition(trackStartStream, introPosition);
                    Bass.BASS_ChannelPlay(trackStartStream, false);
                }
                else if (startTime >= GetAllPerfectStartTime())
                {
                    if (editorSetting!.ShowAllPerfect)
                    {
                        Bass.BASS_ChannelPlay(allperfectStream, true);
                        Bass.BASS_ChannelPlay(fanfareStream, true);
                    }
                }
                else if (startTime <= songLength)
                {
                    Bass.BASS_ChannelSetPosition(bgmStream, startTime);
                    SimaiProcess.ClearNoteListPlayedState();
                    StartSELoop();
                    Bass.BASS_ChannelPlay(bgmStream, false);
                }
                canStart = true;
            });
            if (!canStart)
                return;

            if (startTime < 0d)
            {
                if (leadInSeconds > 0d)
                    await Task.Delay(TimeSpan.FromSeconds(leadInSeconds)).ConfigureAwait(false);
                if (!flowPreviewActive || generation != flowPreviewGeneration)
                    return;
                Dispatcher.Invoke(() =>
                {
                    flowPreviewStartTime = 0d;
                    flowPreviewStartedAt = DateTime.Now;
                    flowTimelineCursor = null;
                    Bass.BASS_ChannelSetPosition(bgmStream, 0d);
                    SimaiProcess.ClearNoteListPlayedState();
                    Bass.BASS_ChannelStop(trackStartStream);
                    StartSELoop();
                    Bass.BASS_ChannelPlay(bgmStream, false);
                });
            }

            var previewEnd = GetTimelineMaximum();
            var remaining = (previewEnd - Math.Max(startTime, 0d)) / playbackSpeed;
            if (remaining > 0d)
                await Task.Delay(TimeSpan.FromSeconds(remaining));
            if (!flowPreviewActive || generation != flowPreviewGeneration)
                return;
            Dispatcher.Invoke(ToggleStop);
        });
    }

    private void TogglePlayAndPause(PlayMethod playMethod = PlayMethod.Normal)
    {
        // Recording and OP preview are one-shot actions, not play/pause toggles.
        if (isPlaying && playMethod == PlayMethod.Normal)
        {
            TogglePause();
            return;
        }
        if (lastEditorState != EditorControlMethod.Pause &&
            editorSetting!.SyntaxCheckLevel == 2 &&
            SyntaxChecker.GetErrorCount() != 0)
        {
            ShowErrorWindow();
            return;
        }
        TogglePlay(playMethod);
    }

    private void TogglePlayAndStop(PlayMethod playMethod = PlayMethod.Normal)
    {
        if (editorSetting!.SyntaxCheckLevel == 2 && SyntaxChecker.GetErrorCount() != 0)
        {
            ShowErrorWindow();
            return;
        }
        if (isPlaying)
            ToggleStop();
        else
            TogglePlay(playMethod);
    }

    private void SetPlaybackSpeed(float speed)
    {
        var scale = (speed - 1) * 100f;
        Bass.BASS_ChannelSetAttribute(bgmStream, BASSAttribute.BASS_ATTRIB_TEMPO, scale);
    }

    private float GetPlaybackSpeed()
    {
        var speed = 0f;
        Bass.BASS_ChannelGetAttribute(bgmStream, BASSAttribute.BASS_ATTRIB_TEMPO, ref speed);
        return speed / 100f + 1f;
    }

    private void SetBgmPosition(double time)
    {
        flowTimelineCursor = null;
        flowPreviewActive = false;
        flowPreviewGeneration++;
        // Seeking while paused invalidates View judge queues and requires an ordered stop.
        if ((lastEditorState == EditorControlMethod.Pause || pausePending) &&
            (pendingScrubStop == null || pendingScrubStop.IsCompleted))
            StopPlaybackForScrub();
        Bass.BASS_ChannelSetPosition(bgmStream, time);
    }


    //*VIEW COMMUNICATION
    private bool sendRequestStop()
    {
        var requestStop = new EditRequestjson
        {
            control = EditorControlMethod.Stop
        };
        var json = JsonConvert.SerializeObject(requestStop);
        var response = WebControl.RequestPOST("http://localhost:8013/", json);
        if (response == "ERROR")
        {
            MessageBox.Show(GetLocalizedString("PortClear"));
            return false;
        }

        lastEditorState = EditorControlMethod.Stop;
        return true;
    }

    private bool sendRequestPause()
    {
        var requestStop = new EditRequestjson
        {
            control = EditorControlMethod.Pause
        };
        var json = JsonConvert.SerializeObject(requestStop);
        var response = WebControl.RequestPOST("http://localhost:8013/", json);
        if (response == "ERROR")
        {
            MessageBox.Show(GetLocalizedString("PortClear"));
            return false;
        }

        lastEditorState = EditorControlMethod.Pause;
        return true;
    }

    private async Task<DateTime?> SendRunAfterScrubStop(
        PlayMethod playMethod,
        float startTime,
        Task<bool>? previewToDrain,
        Task<bool>? scrubStopToDrain)
    {
        if (previewToDrain != null)
        {
            try
            {
                await previewToDrain.ConfigureAwait(false);
            }
            catch
            {
                return null;
            }
        }

        if (scrubStopToDrain != null)
        {
            try
            {
                if (!await scrubStopToDrain.ConfigureAwait(false))
                    return null;
            }
            catch
            {
                return null;
            }
            if (ReferenceEquals(pendingScrubStop, scrubStopToDrain))
                pendingScrubStop = null;
        }

        // Generate the shared clock anchor only after every stale preview and stop
        // request has completed. This keeps the timestamp fresh without a fixed delay.
        var startAt = DateTime.Now;
        return sendRequestRun(startAt, playMethod, startTime) ? startAt : null;
    }

    private async Task<bool> SendScrubStop(
        Task<bool>? previousStop,
        Task<bool>? playToDrain,
        Task<bool>? previewToDrain)
    {
        if (previewToDrain != null)
        {
            try
            {
                await previewToDrain.ConfigureAwait(false);
            }
            catch
            {
                return false;
            }
        }

        if (previousStop != null)
        {
            try
            {
                if (!await previousStop.ConfigureAwait(false))
                    return false;
            }
            catch
            {
                return false;
            }
        }

        if (playToDrain != null && !ReferenceEquals(playToDrain, previousStop))
        {
            try
            {
                if (!await playToDrain.ConfigureAwait(false))
                    return false;
            }
            catch
            {
                return false;
            }
        }

        return sendRequestStop();
    }

    private bool sendRequestContinue(DateTime StartAt)
    {
        var request = new EditRequestjson
        {
            control = EditorControlMethod.Continue,
            language = editorSetting!.Language,
            startAt = StartAt.Ticks,
            startTime = (float)Bass.BASS_ChannelBytes2Seconds(bgmStream, Bass.BASS_ChannelGetPosition(bgmStream)),
            audioSpeed = GetPlaybackSpeed(),
            mediaAudioVolume = editorSetting.Default_BGM_Level,
            showJudgeLine = editorSetting.ShowJudgeLine,
            showJudgeText = editorSetting.ShowJudgeText,
            // Hiding the judgment line always hides the judgment area.
            showJudgeArea = editorSetting.ShowJudgeArea && editorSetting.ShowJudgeLine,
            showSongDetail = editorSetting.ShowSongDetail,
            showAllPerfect = editorSetting.ShowAllPerfect,
            showGeneratedMark = editorSetting.ShowGeneratedMark,
            viewDisplayFontPreset = editorSetting.ViewDisplayFontPreset,
            enableVisualChartEditor = editorSetting.EnableVisualChartEditor,
            skin = editorSetting.Skin,
            tapSkin = editorSetting.TapSkin,
            holdSkin = editorSetting.HoldSkin,
            starSkin = editorSetting.StarSkin,
            pinkStar = editorSetting.PinkStar,
            standbyTheme = IsLightEditorTheme(editorSetting.EditorTheme) ? "light" : "dark",
            introBgTheme = editorSetting.ViewIntroStyle,
            backgroundFitMode = editorSetting.BackgroundFitMode,
            songDetailStyle = editorSetting.SongDetailStyle,
            editorPlayMethod = editorSetting.editorPlayMethod
        };
        var json = JsonConvert.SerializeObject(request);
        var response = WebControl.RequestPOST("http://localhost:8013/", json);
        if (response == "ERROR")
        {
            MessageBox.Show(GetLocalizedString("PortClear"));
            return false;
        }

        lastEditorState = EditorControlMethod.Start;
        return true;
    }

    internal void SendDisplaySettings()
    {
        if (editorSetting == null)
            return;

        var request = new EditRequestjson
        {
            control = EditorControlMethod.SetDisplay,
            language = editorSetting.Language,
            showJudgeInfo = editorSetting.ShowJudgeInfo,
            showComboInfo = editorSetting.ShowComboInfo,
            showJudgeLine = editorSetting.ShowJudgeLine,
            showJudgeText = editorSetting.ShowJudgeText,
            // Hiding the judgment line always hides the judgment area.
            showJudgeArea = editorSetting.ShowJudgeArea && editorSetting.ShowJudgeLine,
            showSongDetail = editorSetting.ShowSongDetail,
            innerBackgroundCover = editorSetting.InnerBackgroundCover,
            outerBackgroundCover = editorSetting.OuterBackgroundCover,
            showAllPerfect = editorSetting.ShowAllPerfect,
            showGeneratedMark = editorSetting.ShowGeneratedMark,
            viewDisplayFontPreset = editorSetting.ViewDisplayFontPreset,
            enableVisualChartEditor = editorSetting.EnableVisualChartEditor,
            skin = editorSetting.Skin,
            tapSkin = editorSetting.TapSkin,
            holdSkin = editorSetting.HoldSkin,
            starSkin = editorSetting.StarSkin,
            pinkStar = editorSetting.PinkStar,
            standbyTheme = IsLightEditorTheme(editorSetting.EditorTheme) ? "light" : "dark",
            introBgTheme = editorSetting.ViewIntroStyle,
            backgroundFitMode = editorSetting.BackgroundFitMode,
            songDetailStyle = editorSetting.SongDetailStyle
        };
        WebControl.RequestPOST("http://localhost:8013/",
            JsonConvert.SerializeObject(request));
    }

    private bool sendRequestRun(
        DateTime StartAt,
        PlayMethod playMethod,
        float? startTimeOverride = null,
        bool previewFlow = false)
    {
        var jsonStruct = BuildSongDetailMajson();
        var basicErrors = ValidateMajsonForView(jsonStruct);
        if (basicErrors.Count > 0)
            SetBasicParseErrors(basicErrors);
        else
            ClearBasicParseErrors();

        var path = maidataDir + "/majdata.json";
        jsonStruct.filePath = path;
        var json = JsonConvert.SerializeObject(jsonStruct);
        File.WriteAllText(path, json);

        var request = new EditRequestjson
        {
            language = editorSetting?.Language ?? "en-US"
        };
        if (playMethod == PlayMethod.Op)
            request.control = EditorControlMethod.OpStart;
        else if (playMethod == PlayMethod.Normal)
            request.control = EditorControlMethod.Start;
        else
            request.control = EditorControlMethod.Record;

        float chartLen = 0f;
        foreach (var tp in jsonStruct.timingList)
        {
            chartLen = Math.Max(chartLen, (float)tp.time);
            foreach (var note in tp.noteList)
            {
                if (note.noteType == SimaiNoteType.Slide)
                    chartLen = Math.Max(chartLen, (float)(note.slideStartTime + note.slideTime));
                else
                    chartLen = Math.Max(chartLen, (float)(tp.time + note.holdTime));
            }
        }

        Dispatcher.Invoke(() =>
        {
            request.jsonPath = path;
            request.startAt = StartAt.Ticks;
            request.startTime = startTimeOverride ??
                (float)Bass.BASS_ChannelBytes2Seconds(bgmStream, Bass.BASS_ChannelGetPosition(bgmStream));
            // request.playSpeed = float.Parse(ViewerSpeed.Text);
            // Convert maimai DX speed to View units: MajSpeed = 107.25 / (71.4184491 * (MaiSpeed + 0.9975) ^ -0.985558604)
            request.noteSpeed = editorSetting!.playSpeed;
            request.touchSpeed = editorSetting!.touchSpeed;
            request.starSpeed = editorSetting!.starSpeed;
            request.backgroundCover = editorSetting!.backgroundCover;
            request.innerBackgroundCover = editorSetting.InnerBackgroundCover;
            request.outerBackgroundCover = editorSetting.OuterBackgroundCover;
            request.backgroundFitMode = editorSetting.BackgroundFitMode;
            request.showJudgeInfo = editorSetting.ShowJudgeInfo;
            request.showComboInfo = editorSetting.ShowComboInfo;
            request.showJudgeLine = editorSetting.ShowJudgeLine;
            request.showJudgeText = editorSetting.ShowJudgeText;
            // Hiding the judgment line always hides the judgment area.
            request.showJudgeArea = editorSetting.ShowJudgeArea && editorSetting.ShowJudgeLine;
            request.skin = editorSetting.Skin;
            request.tapSkin = editorSetting.TapSkin;
            request.holdSkin = editorSetting.HoldSkin;
            request.starSkin = editorSetting.StarSkin;
            request.pinkStar = editorSetting.PinkStar;
            request.standbyTheme = IsLightEditorTheme(editorSetting.EditorTheme) ? "light" : "dark";
            request.introBgTheme = editorSetting.ViewIntroStyle;
            request.songDetailStyle = editorSetting.SongDetailStyle;
            request.previewFlow = previewFlow;
            request.previewTimelineTime = previewFlow
                ? (float)flowPreviewStartTime
                : request.startTime;
            request.showSongDetail = editorSetting.ShowSongDetail;
            request.showAllPerfect = editorSetting.ShowAllPerfect;
            request.showGeneratedMark = editorSetting.ShowGeneratedMark;
            request.viewDisplayFontPreset = editorSetting.ViewDisplayFontPreset;
            request.enableVisualChartEditor = editorSetting.EnableVisualChartEditor;
            request.comboStatusType = editorSetting!.comboStatusType;
            request.audioSpeed = GetPlaybackSpeed();
            request.mediaAudioVolume = editorSetting.Default_BGM_Level;
            request.smoothSlideAnime = editorSetting!.SmoothSlideAnime;
            request.editorPlayMethod = editorSetting.editorPlayMethod;
            request.chartLength = chartLen;
            request.recordFrameRate = playMethod == PlayMethod.Record120 ? 120 : 60;
            // Layered export is temporarily disabled; every recording is one composite out.mp4.
            if (request.control == EditorControlMethod.Record && pendingRecordOptions != null)
            {
                request.recordFrameRate = pendingRecordOptions.FrameRate >= 120 ? 120 : 60;
                request.recordLayers = "";
                request.recordFileName = "out.mp4";
                request.recordWidth = pendingRecordOptions.Width;
                request.recordHeight = pendingRecordOptions.Height;
                request.revealOutput = pendingRecordOptions.RevealOutput;
            }
        });

        if (editorSetting?.SongDetailStyle == 1)
        {
            var preBake = songDetailBakeTask;
            if (preBake != null && !preBake.IsCompleted)
                preBake.GetAwaiter().GetResult();
            lock (songDetailBakeLock)
            {
                EnsureSongDetailCache(jsonStruct);
            }
        }

        json = JsonConvert.SerializeObject(request);
        var response = WebControl.RequestPOST("http://localhost:8013/", json);
        if (response == "ERROR")
        {
            MessageBox.Show(GetLocalizedString("PortClear"));
            return false;
        }
        lastEditorState = request.control == EditorControlMethod.Record
            ? EditorControlMethod.Record
            : EditorControlMethod.Start;
        return true;
    }

    // Build a Majson snapshot from the Edit-side SimaiProcess state.
    // Keep this local to MajdataEdit so Alpha stays on the original Edit/View split.
    private Majson BuildSongDetailMajson()
    {
        var jsonStruct = new Majson();
        foreach (var note in SimaiProcess.notelist)
        {
            note.noteList = note.getNotes();
            jsonStruct.timingList.Add(note);
        }

        jsonStruct.title = SimaiProcess.title!;
        jsonStruct.artist = SimaiProcess.artist!;
        jsonStruct.level = selectedDifficulty >= 0 ? SimaiProcess.levels[selectedDifficulty] : "";
        jsonStruct.designer = selectedDifficulty >= 0 ? SimaiProcess.GetDesignerText(selectedDifficulty) : "";
        jsonStruct.difficulty = selectedDifficulty >= 0 ? SimaiProcess.GetDifficultyText(selectedDifficulty) : "";
        jsonStruct.diffNum = selectedDifficulty;
        jsonStruct.songDetailStyle = editorSetting?.SongDetailStyle ?? 1;
        jsonStruct.wholeBpm = SimaiProcess.GetWholeBpmText();
        jsonStruct.svTable = SimaiProcess.svTable;
        jsonStruct.hsTable = SimaiProcess.hsTable;
        jsonStruct.spawnTable = SimaiProcess.spawnTable;
        jsonStruct.bounceTable = SimaiProcess.bounceTable;
        jsonStruct.colorTable = SimaiProcess.colorTable;
        jsonStruct.sizeTable = SimaiProcess.sizeTable;
        jsonStruct.alphaTable = SimaiProcess.alphaTable;
        jsonStruct.displayTable = SimaiProcess.displayTable;
        jsonStruct.subtitleTable = SimaiProcess.subtitleTable;
        jsonStruct.effectTable = SimaiProcess.effectTable;
        jsonStruct.mediaTable = GetEffectiveMediaTable()
            .Where(item => item.kind != "audio" || !item.timelineClip)
            .ToList();
        return jsonStruct;
    }

    private static List<BasicParseError> ValidateMajsonForView(Majson majson)
        => ValidateTimingsForView(majson.timingList);

    // Mirror View's per-timing parse errors in Edit diagnostics.
    private static List<BasicParseError> ValidateTimingsForView(IEnumerable<SimaiTimingPoint> timingList)
    {
        var errors = new List<BasicParseError>();
        var flaggedLines = new HashSet<int>();
        foreach (var timing in timingList)
        {
            void AddError(string message)
            {
                if (flaggedLines.Add(timing.rawTextPositionY))
                    errors.Add(new BasicParseError(
                        timing.rawTextPositionX,
                        timing.rawTextPositionY,
                        string.Format(
                            GetLocalizedString("ValidationIssueAtLine"),
                            timing.rawTextPositionY + 1,
                            LocalizeViewValidationMessage(message).Replace('\n', ' '))));
            }

            var notes = timing.getNotes();
            if (!string.IsNullOrWhiteSpace(timing.notesContent) && notes.Count == 0)
            {
                AddError(timing.noteParseError ?? GetLocalizedString("ChartStatementInvalid"));
                continue;
            }

            foreach (var note in notes)
            {
                if (note.startPosition is < 1 or > 8 || ContainsInvalidKey(note.noteContent))
                {
                    AddError(GetLocalizedString("ChartKeyRange"));
                    break;
                }
                if (note.noteType == SimaiNoteType.Slide && note.slideTime <= 0d)
                {
                    AddError(GetLocalizedString("SlideDurationInvalid"));
                    break;
                }
                if (note.noteType == SimaiNoteType.Slide && !note.isTouchSlide)
                {
                    try
                    {
                        ValidateSlideForView(timing, note);
                    }
                    catch (Exception error)
                    {
                        AddError(error.Message);
                        break;
                    }
                }
            }

        }

        return errors;
    }

    private static string LocalizeViewValidationMessage(string message)
    {
        if (message.Contains("Slide缺少目标键", StringComparison.Ordinal))
            return GetLocalizedString("SlideTargetMissing");
        if (message.Contains("组合星星有错误", StringComparison.Ordinal))
            return GetLocalizedString("SlideChainInvalid");
        if (message.StartsWith("不存在的Slide形状:", StringComparison.Ordinal))
            return string.Format(
                GetLocalizedString("SlideShapeUnknown"),
                message[(message.IndexOf(':') + 1)..].Trim());
        if (message.Contains("不允许Wifi Slide", StringComparison.Ordinal))
            return GetLocalizedString("WifiConnectionSlideUnsupported");
        if (message.Contains("-星星至少隔开一键", StringComparison.Ordinal))
            return GetLocalizedString("LineSlideGap");
        if (message.Contains("V星星拐点只能隔开一键", StringComparison.Ordinal))
            return GetLocalizedString("VSlideTurnInvalid");
        if (message.Contains("星星不合法", StringComparison.Ordinal) ||
            message.Contains("星星尾部错误", StringComparison.Ordinal) ||
            message.Contains("星星终点不合法", StringComparison.Ordinal))
            return string.Format(
                GetLocalizedString("SlideShapeInvalid"),
                message.Length > 0 ? message[0].ToString() : "?");
        return message;
    }

    private static bool ContainsInvalidKey(string? noteContent)
    {
        if (string.IsNullOrEmpty(noteContent))
            return false;

        var inDuration = false;
        foreach (var character in noteContent)
        {
            if (character == '[')
            {
                inDuration = true;
                continue;
            }
            if (character == ']')
            {
                inDuration = false;
                continue;
            }
            if (!inDuration && character is '0' or '9')
                return true;
        }

        return false;
    }

    // Validate every slide through the same chained path used by View.
    private static void ValidateSlideForView(SimaiTimingPoint timing, SimaiNote note)
    {
        var content = note.noteContent ?? "";
        if (content.Length == 0 || !char.IsNumber(content[0]))
            throw new Exception("Slide缺少目标键");

        ValidateSlideChainForView(timing, note);
    }

    private static void ValidateSlideChainForView(SimaiTimingPoint timing, SimaiNote note)
    {
        static int CharIntParse(char c) => c - '0';
        static double GetTimeFromBeats(string noteText, float currentBpm)
        {
            var startIndex = noteText.IndexOf('[');
            var overIndex = noteText.IndexOf(']');
            if (startIndex < 0 || overIndex <= startIndex)
                throw new Exception("组合星星有错误");

            var innerString = noteText.Substring(startIndex + 1, overIndex - startIndex - 1);
            var timeOneBeat = 1d / (currentBpm / 60d);
            if (innerString.Count(o => o == '#') == 1)
            {
                var times = innerString.Split('#');
                if (times[1].Contains(':'))
                {
                    innerString = times[1];
                    timeOneBeat = 1d / (double.Parse(times[0], CultureInfo.InvariantCulture) / 60d);
                }
                else
                {
                    return double.Parse(times[1], CultureInfo.InvariantCulture);
                }
            }

            if (innerString.Count(o => o == '#') == 2)
            {
                var times = innerString.Split('#');
                return double.Parse(times[2], CultureInfo.InvariantCulture);
            }

            var numbers = innerString.Split(':');
            var divide = int.Parse(numbers[0], CultureInfo.InvariantCulture);
            var count = int.Parse(numbers[1], CultureInfo.InvariantCulture);
            return timeOneBeat * 4d / divide * count;
        }

        var noteContent = note.noteContent ?? "";
        var subSlide = new List<string>();
        var latestStartIndex = CharIntParse(noteContent[0]);
        var ptr = 1;
        var specTimeFlag = 0;

        while (ptr < noteContent.Length)
        {
            if (char.IsNumber(noteContent[ptr]))
                throw new Exception("组合星星有错误");

            var slideTypeChar = noteContent[ptr++].ToString();
            string slidePart;
            if (slideTypeChar == "V")
            {
                if (ptr + 1 >= noteContent.Length)
                    throw new Exception("Slide缺少目标键");
                var middlePos = noteContent[ptr++];
                var endPos = noteContent[ptr++];
                slidePart = latestStartIndex + slideTypeChar + middlePos + endPos;
                latestStartIndex = CharIntParse(endPos);
            }
            else
            {
                if (ptr >= noteContent.Length)
                    throw new Exception("Slide缺少目标键");
                if (noteContent[ptr] == slideTypeChar[0])
                    slideTypeChar += noteContent[ptr++];
                else if (slideTypeChar == "r" && (noteContent[ptr] == 'p' || noteContent[ptr] == 'q'))
                    slideTypeChar += noteContent[ptr++];
                if (ptr >= noteContent.Length || !char.IsNumber(noteContent[ptr]))
                    throw new Exception("Slide缺少目标键");
                var endPos = noteContent[ptr++];
                slidePart = latestStartIndex + slideTypeChar + endPos;
                latestStartIndex = CharIntParse(endPos);
            }

            if (ptr < noteContent.Length && noteContent[ptr] == '[')
            {
                if (specTimeFlag == 0)
                    specTimeFlag = 2;
                else if (specTimeFlag == 1)
                    specTimeFlag = 3;
                else if (specTimeFlag == 3)
                    throw new Exception("组合星星有错误");

                while (ptr < noteContent.Length && noteContent[ptr] != ']')
                    slidePart += noteContent[ptr++];
                if (ptr >= noteContent.Length)
                    throw new Exception("组合星星有错误");
                slidePart += noteContent[ptr++];
            }
            else
            {
                if (specTimeFlag == 0)
                    specTimeFlag = 1;
                else if (specTimeFlag == 2 || specTimeFlag == 3)
                    throw new Exception("组合星星有错误");
            }

            // Reject shapes absent from View's prefab map.
            var slideShape = ValidateSlideShapeForView(slidePart);
            if (slideShape.StartsWith("-", StringComparison.Ordinal))
                slideShape = slideShape[1..];
            if (slideShape.StartsWith("r", StringComparison.Ordinal))
                slideShape = slideShape[1..];
            if (!ViewSlideShapes.Contains(slideShape))
                throw new Exception("不存在的Slide形状: " + slideShape);

            subSlide.Add(slidePart);
        }

        if (specTimeFlag == 1 || specTimeFlag == 0)
            throw new Exception("组合星星有错误");

        // View does not support Wifi as a connection-slide segment.
        if (noteContent.Contains('w') && subSlide.Count != 1)
            throw new Exception("不允许Wifi Slide作为Connection Slide的一部分");

        if (specTimeFlag != 3)
        {
            foreach (var slide in subSlide)
                _ = GetTimeFromBeats(slide, timing.currentBpm);
        }
    }

    // Valid normalized shape names from View's slide prefab map.
    private static readonly HashSet<string> ViewSlideShapes = new()
    {
        "line3", "line4", "line5", "line6", "line7",
        "circle1", "circle2", "circle3", "circle4", "circle5", "circle6", "circle7", "circle8",
        "v1", "v2", "v3", "v4", "v6", "v7", "v8",
        "ppqq1", "ppqq2", "ppqq3", "ppqq4", "ppqq5", "ppqq6", "ppqq7", "ppqq8",
        "pq1", "pq2", "pq3", "pq4", "pq5", "pq6", "pq7", "pq8",
        "s", "wifi", "L2", "L3", "L4", "L5"
    };

    private static string ValidateSlideShapeForView(string content)
    {
        static int RelativeEnd(int startPos, int endPos)
        {
            endPos -= startPos;
            if (endPos < 0) endPos += 8;
            if (endPos > 8) endPos -= 8;
            return endPos + 1;
        }

        static int MirrorKeys(int key) => key switch
        {
            1 => 1, 2 => 8, 3 => 7, 4 => 6, 5 => 5, 6 => 4, 7 => 3, 8 => 2,
            _ => throw new Exception("Keys out of range: " + key)
        };

        if (content.Contains('-'))
        {
            var str = content.Substring(0, Math.Min(3, content.Length));
            var digits = str.Split('-');
            if (digits.Length < 2) throw new Exception("Slide缺少目标键");
            var endPos = RelativeEnd(int.Parse(digits[0]), int.Parse(digits[1]));
            if (endPos < 3 || endPos > 7) throw new Exception("-星星至少隔开一键");
            return "line" + endPos;
        }

        if (content.Contains('>'))
        {
            var digits = content.Substring(0, Math.Min(3, content.Length)).Split('>');
            if (digits.Length < 2) throw new Exception("Slide缺少目标键");
            var startPos = int.Parse(digits[0]);
            var endPos = RelativeEnd(startPos, int.Parse(digits[1]));
            return IsUpperHalfForView(startPos) ? "circle" + endPos : "-circle" + MirrorKeys(endPos);
        }

        if (content.Contains('<'))
        {
            var digits = content.Substring(0, Math.Min(3, content.Length)).Split('<');
            if (digits.Length < 2) throw new Exception("Slide缺少目标键");
            var startPos = int.Parse(digits[0]);
            var endPos = RelativeEnd(startPos, int.Parse(digits[1]));
            return !IsUpperHalfForView(startPos) ? "circle" + endPos : "-circle" + MirrorKeys(endPos);
        }

        if (content.Contains('^'))
        {
            var digits = content.Substring(0, Math.Min(3, content.Length)).Split('^');
            if (digits.Length < 2) throw new Exception("Slide缺少目标键");
            var endPos = RelativeEnd(int.Parse(digits[0]), int.Parse(digits[1]));
            if (endPos is 1 or 5) throw new Exception("^星星不合法");
            return endPos < 5 ? "circle" + endPos : "-circle" + MirrorKeys(endPos);
        }

        if (content.Contains('v'))
        {
            var digits = content.Substring(0, Math.Min(3, content.Length)).Split('v');
            if (digits.Length < 2) throw new Exception("Slide缺少目标键");
            var endPos = RelativeEnd(int.Parse(digits[0]), int.Parse(digits[1]));
            if (endPos == 5) throw new Exception("v星星不合法");
            return "v" + endPos;
        }

        if (content.Contains("rp"))
        {
            var digits = content.Substring(0, Math.Min(4, content.Length)).Split(new[] { "rp" }, StringSplitOptions.None);
            if (digits.Length < 2) throw new Exception("Slide缺少目标键");
            return "rppqq" + RelativeEnd(int.Parse(digits[1]), int.Parse(digits[0]));
        }

        if (content.Contains("rq"))
        {
            var digits = content.Substring(0, Math.Min(4, content.Length)).Split(new[] { "rq" }, StringSplitOptions.None);
            if (digits.Length < 2) throw new Exception("Slide缺少目标键");
            return "-rppqq" + MirrorKeys(RelativeEnd(int.Parse(digits[1]), int.Parse(digits[0])));
        }

        if (content.Contains("pp"))
        {
            var digits = content.Substring(0, Math.Min(4, content.Length)).Split('p');
            if (digits.Length < 3) throw new Exception("Slide缺少目标键");
            return "ppqq" + RelativeEnd(int.Parse(digits[0]), int.Parse(digits[2]));
        }

        if (content.Contains("qq"))
        {
            var digits = content.Substring(0, Math.Min(4, content.Length)).Split('q');
            if (digits.Length < 3) throw new Exception("Slide缺少目标键");
            return "-ppqq" + MirrorKeys(RelativeEnd(int.Parse(digits[0]), int.Parse(digits[2])));
        }

        if (content.Contains('p'))
        {
            var digits = content.Substring(0, Math.Min(3, content.Length)).Split('p');
            if (digits.Length < 2) throw new Exception("Slide缺少目标键");
            return "pq" + RelativeEnd(int.Parse(digits[0]), int.Parse(digits[1]));
        }

        if (content.Contains('q'))
        {
            var digits = content.Substring(0, Math.Min(3, content.Length)).Split('q');
            if (digits.Length < 2) throw new Exception("Slide缺少目标键");
            return "-pq" + MirrorKeys(RelativeEnd(int.Parse(digits[0]), int.Parse(digits[1])));
        }

        if (content.Contains('s'))
        {
            var digits = content.Substring(0, Math.Min(3, content.Length)).Split('s');
            if (digits.Length < 2) throw new Exception("Slide缺少目标键");
            if (RelativeEnd(int.Parse(digits[0]), int.Parse(digits[1])) != 5)
                throw new Exception("s星星尾部错误");
            return "s";
        }

        if (content.Contains('z'))
        {
            var digits = content.Substring(0, Math.Min(3, content.Length)).Split('z');
            if (digits.Length < 2) throw new Exception("Slide缺少目标键");
            if (RelativeEnd(int.Parse(digits[0]), int.Parse(digits[1])) != 5)
                throw new Exception("z星星尾部错误");
            return "-s";
        }

        if (content.Contains('V'))
        {
            if (content.Length < 4) throw new Exception("Slide缺少目标键");
            var digits = content.Substring(0, 4).Split('V');
            var startPos = int.Parse(digits[0]);
            var turnPos = RelativeEnd(startPos, int.Parse(digits[1][0].ToString()));
            var endPos = RelativeEnd(startPos, int.Parse(digits[1][1].ToString()));
            if (turnPos == 7)
            {
                if (endPos < 2 || endPos > 5) throw new Exception("V星星终点不合法");
                return "L" + endPos;
            }

            if (turnPos == 3)
            {
                if (endPos < 5) throw new Exception("V星星终点不合法");
                return "-L" + MirrorKeys(endPos);
            }

            throw new Exception("V星星拐点只能隔开一键");
        }

        if (content.Contains('w'))
        {
            var digits = content.Substring(0, Math.Min(3, content.Length)).Split('w');
            if (digits.Length < 2) throw new Exception("Slide缺少目标键");
            if (RelativeEnd(int.Parse(digits[0]), int.Parse(digits[1])) != 5)
                throw new Exception("w星星尾部错误");
            return "wifi";
        }

        throw new Exception("Slide缺少目标键");
    }

    private static bool IsUpperHalfForView(int key) => key is 7 or 8 or 1 or 2;

    // Serialize card baking so rapid difficulty changes cannot write the same PNG concurrently.
    private readonly object songDetailBakeLock = new();
    private Task? songDetailBakeTask;

    // Pre-bake card PNGs after metadata changes to avoid first-play frame spikes.
    private void SchedulePreBakeSongDetail()
    {
        if (selectedDifficulty < 0 || selectedDifficulty > 6)
            return;
        if (string.IsNullOrWhiteSpace(maidataDir))
            return;

        Majson majson;
        try
        {
            majson = BuildSongDetailMajson();
            // Bake the selected Master/Re:Master card during chart loading even
            // when the original card style is currently selected. Switching styles
            // later must not make the first playback render a cache on demand.
            majson.songDetailStyle = 1;
        }
        catch
        {
            return;
        }

        songDetailBakeTask = Task.Run(() =>
        {
            lock (songDetailBakeLock)
            {
                EnsureSongDetailCache(majson);
            }
        });
    }

    internal void PreBakeSongDetail() => SchedulePreBakeSongDetail();

        // Invalidate the signature while retaining the last usable PNG.
    private void InvalidateSongDetailCache(params int[] difficulties)
    {
        if (string.IsNullOrWhiteSpace(maidataDir))
            return;
        try
        {
            if (difficulties == null || difficulties.Length == 0)
                difficulties = new[] { 0, 1, 2, 3, 4, 5, 6 };

            foreach (var diff in difficulties.Distinct())
            {
                var stem = GetSongDetailCacheStem(diff);
                if (stem == null)
                    continue;

                var path = Path.Combine(maidataDir, stem + ".sig");
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
        catch
        {
            // The previous cache remains usable if invalidation fails.
        }
    }

    private static string? GetSongDetailCacheStem(int difficulty)
    {
        return difficulty switch
        {
            0 => "songdetail_easy",
            1 => "songdetail_basic",
            2 => "songdetail_advanced",
            3 => "songdetail_expert",
            4 => "songdetail_master",
            5 => "songdetail_remaster",
            6 => "songdetail_original",
            _ => null
        };
    }

    // Difficulty-specific asset suffix and level glyph atlas.
    private static (string suffix, string digitSheet) GetSongDetailPartNames(int difficulty)
    {
        return difficulty switch
        {
            1 => ("BSC", "UI_NUM_MLevel_01"),
            2 => ("ADV", "UI_NUM_MLevel_02"),
            3 => ("EXP", "UI_NUM_MLevel_03"),
            4 => ("MST", "UI_NUM_MLevel_04"),
            5 => ("MST_Re", "UI_NUM_MLevel_05"),
            6 => ("UTG", "UI_NUM_MLevel_10"),
            _ => ("DMY", "UI_NUM_MLevelDAMMY")
        };
    }

    // Touch, touch-hold, and Wifi notes identify DX charts.
    // Standard charts mirror the header tab and use the standard-mode badge.
    private static bool IsDxChart(Majson majson)
    {
        foreach (var timing in majson.timingList)
        {
            if (timing.noteList == null)
                continue;
            foreach (var note in timing.noteList)
            {
                if (note.noteType is SimaiNoteType.Touch or SimaiNoteType.TouchHold)
                    return true;
                if (note.isTouchSlide)
                    return true;
                if (note.noteType == SimaiNoteType.Slide &&
                    note.noteContent != null && note.noteContent.Contains('w'))
                    return true;
            }
        }
        return false;
    }

    private string BuildSongDetailInfoFingerprint()
    {
        var coverPath = FindChartImage("Cover") ?? FindChartImage("bg") ?? "";
        long coverTicks = 0L, coverLength = 0L;
        if (!string.IsNullOrWhiteSpace(coverPath))
        {
            try
            {
                var info = new FileInfo(coverPath);
                coverTicks = info.LastWriteTimeUtc.Ticks;
                coverLength = info.Length;
            }
            catch
            {
                // Metadata is used only to decide whether to refresh the cache; ignore lookup failures.
            }
        }

        return string.Join("\u0001", new[]
        {
            SimaiProcess.title ?? "",
            SimaiProcess.artist ?? "",
            SimaiProcess.designer ?? "",
            SimaiProcess.wholeBpm ?? "",
            coverPath,
            coverTicks.ToString(CultureInfo.InvariantCulture),
            coverLength.ToString(CultureInfo.InvariantCulture)
        });
    }

    // Bake a base PNG below the jacket and an overlay PNG above it.
    private void EnsureSongDetailCache(Majson majson)
    {
        if (majson.songDetailStyle != 1 || string.IsNullOrWhiteSpace(maidataDir))
            return;

        try
        {
            var partsDir = FindProjectAssetPath("Assets/SongDetailParts");
            var fontDir = FindProjectAssetPath("Assets/Resources/Fonts");
            var cacheStem = GetSongDetailCacheStem(majson.diffNum);
            if (partsDir == null || fontDir == null || cacheStem == null)
                return;

            var (suffix, digitSheetName) = GetSongDetailPartNames(majson.diffNum);
            var bodyPath = Path.Combine(partsDir, $"UI_TST_MBase_{suffix}.png");
            var tabPath = Path.Combine(partsDir, $"UI_TST_MBase_{suffix}_Tab.png");
            var lvPlatePath = Path.Combine(partsDir, $"UI_TST_MBase_LV_{suffix}.png");
            var digitSheetPath = Path.Combine(partsDir, digitSheetName + ".png");
            var infoOverlayPath = Path.Combine(partsDir, "InfoOverlay.png");
            if (!File.Exists(bodyPath) || !File.Exists(tabPath) || !File.Exists(lvPlatePath) ||
                !File.Exists(digitSheetPath) || !File.Exists(infoOverlayPath))
                return;

            // Reuse the cache until visible metadata or chart mode changes.
            var outputPath = Path.Combine(maidataDir, cacheStem + ".png");
            var overlayOutPath = Path.Combine(maidataDir, cacheStem + "_overlay.png");
            var fullOutPath = Path.Combine(maidataDir, cacheStem + "_full.png");
            var signaturePath = Path.Combine(maidataDir, cacheStem + ".sig");
            var dxMaxScore = CountTotalNotes(majson) * 3;
            var isDx = IsDxChart(majson);
            var isUtage = majson.diffNum == 6;
            var coverPath = FindChartImage("Cover") ?? FindChartImage("bg") ?? "";
            var signature = BuildSongDetailSignature(majson, coverPath, dxMaxScore) + (isDx ? "|dx" : "|std");
            if (File.Exists(outputPath) && File.Exists(overlayOutPath) && File.Exists(signaturePath) &&
                (!File.Exists(coverPath) || File.Exists(fullOutPath)) &&
                string.Equals(File.ReadAllText(signaturePath), signature, StringComparison.Ordinal))
                return;

            // Fit the complete source images inside the cache canvas. Do not crop their
            // transparent margins: the outer purple border lives in those edge pixels.
            const float bodyScaleX = 341f / 420f;
            const float bodyScaleY = 588f / 718f;

            using (var canvas = new Bitmap(341, 588, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            using (var graphics = Graphics.FromImage(canvas))
            {
                ConfigureCardGraphics(graphics);

                using (var body = System.Drawing.Image.FromFile(bodyPath))
                    graphics.DrawImage(body, new RectangleF(
                        0f, 75f * bodyScaleY, 420f * bodyScaleX, 636f * bodyScaleY));

                // ORIGINAL cards omit the header tab but retain the mode badge.
                if (!isUtage)
                    using (var tab = System.Drawing.Image.FromFile(tabPath))
                    {
                        if (!isDx)
                            tab.RotateFlip(RotateFlipType.RotateNoneFlipX);
                        // Keep the complete tab source as well; only its vertical placement
                        // changes. The mode icon is rendered independently below.
                        graphics.DrawImage(tab, new RectangleF(
                            0f, 17f, 420f * bodyScaleX, 96f * bodyScaleY));
                    }

                // Place the mode badge on the tab and mirror its anchor for standard charts.
                var iconName = isUtage ? "UI_TST_Infoicon_Utage.png"
                    : isDx ? "UI_TST_Infoicon_DeluxeMode.png" : "UI_TST_Infoicon_StandardMode.png";
                var iconPath = Path.Combine(partsDir, iconName);
                if (File.Exists(iconPath))
                    using (var icon = System.Drawing.Image.FromFile(iconPath))
                    {
                        var iconH = 50f;
                        var iconW = icon.Width * iconH / icon.Height;
                        const float bumpCenterX = 89.3f;
                        var iconCenterX = isUtage ? bumpCenterX
                            : isDx ? bumpCenterX + 2f : 341f - bumpCenterX - 2f;
                        graphics.DrawImage(icon,
                            new RectangleF(iconCenterX - iconW / 2f, 20f, iconW, iconH));
                    }

                // The information overlay follows the 341x588 source layout.
                using (var info = System.Drawing.Image.FromFile(infoOverlayPath))
                    graphics.DrawImage(info, new RectangleF(13f, 28f, 317f, 548f));

                using var titleFonts = new PrivateFontCollection();
                AddFontIfExists(titleFonts, Path.Combine(fontDir, "MicrosoftYaHei-Bold.ttc"));
                AddFontIfExists(titleFonts, Path.Combine(fontDir, "NotoSansSC-VF.ttf"));
                using var smallFonts = new PrivateFontCollection();
                AddFontIfExists(smallFonts, Path.Combine(fontDir, "Aileron-Regular.otf"));

                if (isUtage)
                    DrawCondensedText(graphics, "宴", titleFonts, new RectangleF(14, 20, 151, 48),
                        21f, Color.White, StringAlignment.Center, true);

                // Bake the title into the same image as DXSCORE and the other labels.
                // A live Unity Text layer used to use different font metrics and could
                // disappear after scene reload, so the preview and View never matched.
                DrawCondensedText(graphics, majson.title, titleFonts, new RectangleF(8, 403, 325, 42),
                    22f, Color.White, StringAlignment.Center, true);
                DrawCondensedText(graphics, majson.artist, titleFonts, new RectangleF(8, 443, 325, 38),
                    17f, Color.FromArgb(235, 241, 255), StringAlignment.Center, false);
                var designerText = string.IsNullOrWhiteSpace(majson.designer) ? "-" : majson.designer;
                DrawFitText(graphics, designerText, smallFonts, new RectangleF(19, 548, 200, 26),
                    18f, 10f, Color.FromArgb(28, 34, 62), StringAlignment.Near, false);
                // Draw the BPM label and value because the extracted overlay omits them.
                DrawFitText(graphics, "BPM " + GetBpmTextForCache(majson), smallFonts,
                    new RectangleF(190, 548, 130, 26),
                    17f, 9f, Color.FromArgb(28, 34, 62), StringAlignment.Far, false);

                // Draw current/max DXSCORE above the rating row with a shared baseline.
                DrawDxScore(graphics, dxMaxScore, smallFonts);

                SavePngAtomically(canvas, outputPath);
            }

            // Keep the level plate and glyphs above the jacket.
            using (var overlayCanvas = new Bitmap(341, 588, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            using (var overlayGraphics = Graphics.FromImage(overlayCanvas))
            {
                ConfigureCardGraphics(overlayGraphics);
                using (var plate = System.Drawing.Image.FromFile(lvPlatePath))
                    overlayGraphics.DrawImage(plate, new RectangleF(193f, 321f, 134f, 83.4f));
                DrawLevelFromSheet(overlayGraphics, digitSheetPath,
                    CleanLevelForCache(majson.level), new RectangleF(202f, 348f, 128f, 53f));
                SavePngAtomically(overlayCanvas, overlayOutPath);
            }

            // Also bake a flattened preview using the same jacket geometry as View.
            if (File.Exists(coverPath))
                using (var full = new Bitmap(341, 588, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                using (var fullGraphics = Graphics.FromImage(full))
                {
                    ConfigureCardGraphics(fullGraphics);
                    using (var baseImage = System.Drawing.Image.FromFile(outputPath))
                        fullGraphics.DrawImage(baseImage, new RectangleF(0f, 0f, 341f, 588f));
                    DrawCover(fullGraphics, coverPath, new Rectangle(41, 96, 260, 262));
                    using (var overlayImage = System.Drawing.Image.FromFile(overlayOutPath))
                        fullGraphics.DrawImage(overlayImage, new RectangleF(0f, 0f, 341f, 588f));
                    SavePngAtomically(full, fullOutPath);
                }

            WriteTextAtomically(signaturePath, signature);
        }
        catch (Exception e)
        {
            Console.WriteLine("Song detail cache failed: " + e.Message);
        }
    }

    private static void SavePngAtomically(Bitmap bitmap, string destination)
    {
        var temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            bitmap.Save(temporary, ImageFormat.Png);
            File.Move(temporary, destination, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static void WriteTextAtomically(string destination, string content)
    {
        var temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, content);
            File.Move(temporary, destination, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    // Center-crop the jacket to fill its window.
    private static void DrawCover(Graphics graphics, string coverPath, Rectangle target)
    {
        using var cover = System.Drawing.Image.FromFile(coverPath);
        var scale = Math.Max(target.Width / (float)cover.Width, target.Height / (float)cover.Height);
        var source = new RectangleF(
            (cover.Width - target.Width / scale) / 2f,
            (cover.Height - target.Height / scale) / 2f,
            target.Width / scale,
            target.Height / scale);
        graphics.DrawImage(cover, target, source.X, source.Y, source.Width, source.Height, GraphicsUnit.Pixel);
    }

    private static void ConfigureCardGraphics(Graphics graphics)
    {
        graphics.CompositingMode = CompositingMode.SourceOver;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.Clear(Color.Transparent);
    }

    // Draw level text from the difficulty atlas and scale the row to its plate.
    private static void DrawLevelFromSheet(Graphics graphics, string sheetPath, string level, RectangleF box)
    {
        level = string.IsNullOrWhiteSpace(level) ? "" : level.Trim();
        if (level.Length == 0)
            return;
        var (number, hasPlus) = SplitLevelForCache(level);
        if (string.IsNullOrEmpty(number))
            number = level;
        var display = number + (hasPlus ? "+" : "");

        static int CellOf(char c) => c switch
        {
            >= '0' and <= '9' => c - '0',
            '+' => 10,
            '-' => 11,
            ',' => 12,
            '.' => 13,
            _ => -1
        };
        const int lvCell = 14;
        const float cellW = 48f, cellH = 60f;
        const float glyphH = 52.2f;

        using var sheet = new Bitmap(sheetPath);
        var questionPath = Path.Combine(Path.GetDirectoryName(sheetPath) ?? "", "UI_NUM_MLevel_10_Question.png");
        using var question = File.Exists(questionPath) ? new Bitmap(questionPath) : null;
        const float scale = glyphH / cellH;

        static Rectangle FindVisibleBounds(Bitmap image, Rectangle area)
        {
            var minX = area.Right;
            var minY = area.Bottom;
            var maxX = area.Left - 1;
            var maxY = area.Top - 1;
            for (var y = area.Top; y < area.Bottom; y++)
            for (var x = area.Left; x < area.Right; x++)
            {
                if (image.GetPixel(x, y).A == 0)
                    continue;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
            return maxX >= minX && maxY >= minY
                ? Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1)
                : Rectangle.Empty;
        }

        Rectangle CellBounds(int cell) => FindVisibleBounds(sheet,
            new Rectangle(cell % 4 * (int)cellW, cell / 4 * (int)cellH, (int)cellW, (int)cellH));

        var referenceCells = new[] { lvCell, 1, 4, 10 };
        var referenceBounds = referenceCells.Select(CellBounds).ToArray();
        const float spacing = -7f;
        var referenceWidth = referenceBounds.Sum(bound => bound.Width * scale) + spacing * 3f;
        // Keep the visual center from the previous layout while reducing glyph size.
        var baseX = box.Right - referenceWidth + 2.25f;
        var slots = new float[4];
        slots[0] = baseX + 7f;
        slots[1] = baseX + referenceBounds[0].Width * scale + spacing + 7f;
        slots[2] = baseX + (referenceBounds[0].Width + referenceBounds[1].Width) * scale + spacing * 2f + 7f;
        slots[3] = baseX + (referenceBounds[0].Width + referenceBounds[1].Width + referenceBounds[2].Width) * scale + spacing * 3f;

        void DrawCell(int cell, float drawX, float yOffset = 0f)
        {
            var bound = CellBounds(cell);
            if (bound.IsEmpty)
                return;
            var width = bound.Width * scale;
            var height = bound.Height * scale;
            var cellTop = cell / 4 * (int)cellH;
            var fullCellTop = box.Top + 2f + (box.Height - glyphH) * 0.5f;
            graphics.DrawImage(sheet,
                new RectangleF(drawX, fullCellTop + (bound.Top - cellTop) * scale + yOffset, width, height),
                bound, GraphicsUnit.Pixel);
        }

        void DrawQuestion(float drawX)
        {
            if (question == null)
                return;
            var area = new Rectangle(0, 0, question.Width, question.Height);
            var bound = FindVisibleBounds(question, area);
            if (bound.IsEmpty)
                return;
            var fullCellTop = box.Top + 2f + (box.Height - glyphH) * 0.5f;
            var digitBottom = fullCellTop + (referenceBounds[2].Bottom - (int)cellH) * scale;
            var questionHeight = referenceBounds[2].Height * scale * 0.7f;
            var questionScale = questionHeight / bound.Height;
            var fullQuestionWidth = bound.Width * scale;
            var questionWidth = bound.Width * questionScale;
            graphics.DrawImage(question,
                new RectangleF(drawX + (fullQuestionWidth - questionWidth) * 0.5f,
                    digitBottom - questionHeight, questionWidth, questionHeight),
                bound, GraphicsUnit.Pixel);
        }

        DrawCell(lvCell, slots[0]);
        var digits = display.Where(char.IsDigit).Take(2).ToArray();
        if (digits.Length == 1)
        {
            var bound = CellBounds(CellOf(digits[0]));
            var firstCenter = slots[1] + referenceBounds[1].Width * scale * 0.5f;
            var secondCenter = slots[2] + referenceBounds[2].Width * scale * 0.5f;
            var singleCenter = (firstCenter + secondCenter) * 0.5f;
            DrawCell(CellOf(digits[0]), singleCenter - bound.Width * scale * 0.5f + 4f);
        }
        else if (digits.Length >= 2)
        {
            DrawCell(CellOf(digits[0]), slots[1]);
            DrawCell(CellOf(digits[1]), slots[2]);
        }

        var hasDisplayPlus = display.Contains('+');
        var hasQuestion = display.Contains('?');
        if (hasDisplayPlus)
            DrawCell(10, hasQuestion ? slots[3] + 2f : slots[3] + 5f, -5f);
        if (hasQuestion)
        {
            DrawQuestion(slots[3] + 5f);
        }
    }

    private static void DrawFitText(
        Graphics graphics,
        string text,
        PrivateFontCollection fonts,
        RectangleF rect,
        float maxSize,
        float minSize,
        Color color,
        StringAlignment alignment,
        bool bold)
    {
        text ??= "";

        // Tight single-line measurement. GenericTypographic strips the wide side-bearing
        // padding the default StringFormat adds, and NoWrap keeps the text on one line.
        // The previous code measured against rect.Size WITH StringTrimming.EllipsisCharacter,
        // which reports the *truncated* width as fitting — so the shrink loop never kicked in
        // and long designer names were drawn full-size with an ellipsis ("...").
        using var measureFormat = (StringFormat)StringFormat.GenericTypographic.Clone();
        measureFormat.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
        using var drawFormat = (StringFormat)StringFormat.GenericTypographic.Clone();
        drawFormat.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.NoClip;
        drawFormat.Alignment = alignment;
        drawFormat.LineAlignment = StringAlignment.Center;

        var family = fonts.Families.Length > 0 ? fonts.Families[0] : System.Drawing.FontFamily.GenericSansSerif;
        var style = bold ? System.Drawing.FontStyle.Bold : System.Drawing.FontStyle.Regular;
        var chosen = minSize;
        for (var size = maxSize; size >= minSize; size -= 0.5f)
        {
            using var probe = new System.Drawing.Font(family, size, style, GraphicsUnit.Pixel);
            var measured = graphics.MeasureString(text, probe, PointF.Empty, measureFormat);
            if (measured.Width <= rect.Width)
            {
                chosen = size;
                break;
            }
        }

        using (var font = new System.Drawing.Font(family, chosen, style, GraphicsUnit.Pixel))
        using (var brush = new SolidBrush(color))
            graphics.DrawString(text, font, brush, rect, drawFormat);
    }

    // Keep titles visually large like the arcade card while fitting long names into one line.
    // GDI+ font-size fitting alone made long CJK titles much smaller than the reference card;
    // horizontal compression preserves the requested height without clipping the text.
    private static void DrawCondensedText(
        Graphics graphics,
        string text,
        PrivateFontCollection fonts,
        RectangleF rect,
        float fontSize,
        Color color,
        StringAlignment alignment,
        bool bold)
    {
        text ??= "";
        if (text.Length == 0)
            return;

        var family = fonts.Families.Length > 0 ? fonts.Families[0] : System.Drawing.FontFamily.GenericSansSerif;
        var style = bold ? System.Drawing.FontStyle.Bold : System.Drawing.FontStyle.Regular;
        using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.NoClip | StringFormatFlags.MeasureTrailingSpaces;
        format.Alignment = alignment;
        format.LineAlignment = StringAlignment.Center;
        using var font = new System.Drawing.Font(family, fontSize, style, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(color);
        var measured = graphics.MeasureString(text, font, PointF.Empty, format);
        var scaleX = measured.Width > rect.Width && measured.Width > 0f ? rect.Width / measured.Width : 1f;

        var state = graphics.Save();
        graphics.TranslateTransform(rect.Left, rect.Top);
        graphics.ScaleTransform(scaleX, 1f);
        graphics.DrawString(text, font, brush,
            new RectangleF(0f, 0f, rect.Width / scaleX, rect.Height), format);
        graphics.Restore(state);
    }

    private static void DrawDxScore(Graphics graphics, int dxMaxScore, PrivateFontCollection fonts)
    {
        if (dxMaxScore <= 0)
            return;

        var family = fonts.Families.Length > 0 ? fonts.Families[0] : System.Drawing.FontFamily.GenericSansSerif;
        var value = dxMaxScore.ToString(CultureInfo.InvariantCulture);
        const float startX = 122f;
        const float baselineY = 531.5f;
        const float gap = 6f;
        const float leftSize = 18f;
        const float rightSize = 17f;
        using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.Alignment = StringAlignment.Near;
        format.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.NoClip;
        using var brush = new SolidBrush(Color.White);
        using var leftPath = new GraphicsPath();
        leftPath.AddString(value + "/", family, (int)System.Drawing.FontStyle.Regular,
            leftSize, PointF.Empty, format);
        var leftBounds = leftPath.GetBounds();
        OffsetPath(leftPath, startX - leftBounds.Left, baselineY - leftBounds.Bottom);
        leftBounds = leftPath.GetBounds();

        using var rightPath = new GraphicsPath();
        rightPath.AddString(value, family, (int)System.Drawing.FontStyle.Regular,
            rightSize, PointF.Empty, format);
        var rightBounds = rightPath.GetBounds();
        OffsetPath(rightPath, leftBounds.Right + gap - rightBounds.Left,
            baselineY - rightBounds.Bottom);

        graphics.FillPath(brush, leftPath);
        graphics.FillPath(brush, rightPath);
    }

    private static void OffsetPath(GraphicsPath path, float dx, float dy)
    {
        using var m = new System.Drawing.Drawing2D.Matrix();
        m.Translate(dx, dy);
        path.Transform(m);
    }

    private static void AddFontIfExists(PrivateFontCollection collection, string path)
    {
        if (File.Exists(path))
            collection.AddFontFile(path);
    }

    private string FindChartImage(string basename)
    {
        foreach (var ext in new[] { ".png", ".jpg", ".jpeg" })
        {
            var path = Path.Combine(maidataDir, basename + ext);
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    private static string FindProjectAssetPath(string relativePath)
    {
        var outputRelativePath = relativePath.StartsWith("Assets/Resources/", StringComparison.Ordinal)
            ? "Resources/" + relativePath.Substring("Assets/Resources/".Length)
            : relativePath.StartsWith("Assets/", StringComparison.Ordinal)
                ? relativePath.Substring("Assets/".Length)
                : relativePath;

        foreach (var root in EnumerateSearchRoots())
        {
            var candidate = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(candidate))
                return candidate;
            candidate = Path.Combine(root, outputRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(candidate))
                return candidate;
        }
        return null;
    }

    private static IEnumerable<string> EnumerateSearchRoots()
    {
        var current = new DirectoryInfo(Environment.CurrentDirectory);
        while (current != null)
        {
            yield return current.FullName;
            current = current.Parent;
        }

        current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            yield return current.FullName;
            current = current.Parent;
        }
    }

    private static string CleanLevelForCache(string level)
    {
        if (string.IsNullOrWhiteSpace(level))
            return "";
        level = level.Trim();
        if (level.StartsWith("Lv", StringComparison.OrdinalIgnoreCase))
            level = level.Substring(2).Trim();
        return level;
    }

    private static (string number, bool hasPlus) SplitLevelForCache(string level)
    {
        if (string.IsNullOrWhiteSpace(level))
            return ("", false);
        var trimmed = level.Trim();
        if (trimmed.EndsWith("+", StringComparison.Ordinal))
            return (trimmed.Substring(0, trimmed.Length - 1).TrimEnd(), true);
        // Decimal constant (e.g. "14.9") → maimai display: integer part, with a "+"
        // when the fractional part is >= 0.6 (14.6→"14+", 14.5→"14").
        if (trimmed.Contains('.') &&
            float.TryParse(trimmed, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            var intPart = (int)Math.Floor(value);
            return (intPart.ToString(System.Globalization.CultureInfo.InvariantCulture),
                value - intPart >= 0.595f);
        }
        return (trimmed, false);
    }

    private static string GetBpmTextForCache(Majson majson)
    {
        return string.IsNullOrWhiteSpace(majson.wholeBpm) ? "-" : majson.wholeBpm.Trim();
    }

    // Increment when card rendering changes to invalidate old signatures.
    private const string SongDetailCacheVersion = "v35";

    // Match View note counting; max DXSCORE is total notes multiplied by three.
    private static int CountTotalNotes(Majson majson)
    {
        var total = 0;
        foreach (var timing in majson.timingList)
        {
            if (timing.noteList == null)
                continue;
            foreach (var note in timing.noteList)
            {
                if (note.noteType == SimaiNoteType.Slide)
                {
                    if (!note.isSlideNoHead)
                            total++;
                            total++;
                }
                else
                {
                    total++;
                }
            }
        }
        return total;
    }

    private static string BuildSongDetailSignature(Majson majson, string coverPath, int dxMaxScore)
    {
        long coverTicks = 0L, coverLength = 0L;
        try
        {
            var info = new FileInfo(coverPath);
            coverTicks = info.LastWriteTimeUtc.Ticks;
            coverLength = info.Length;
        }
        catch
        {
            // Text metadata alone still provides a valid cache signature.
        }

        return string.Join("", new[]
        {
            SongDetailCacheVersion,
            majson.songDetailStyle.ToString(),
            majson.diffNum.ToString(),
            majson.title ?? "",
            majson.artist ?? "",
            majson.designer ?? "",
            majson.level ?? "",
            GetBpmTextForCache(majson),
            coverPath ?? "",
            coverTicks.ToString(),
            coverLength.ToString(),
        // Include max DXSCORE so note-count changes trigger a rebake.
            dxMaxScore.ToString()
        });
    }

    [DllImport("user32.dll")]
    public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll", EntryPoint = "MoveWindow")]
    public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hwnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool SwitchToThisWindow(IntPtr hWnd, bool fAltTab);

    private bool CheckAndStartView()
    {
        if (Process.GetProcessesByName("MajdataView").Length == 0)
        {
            var unityRunning = Process.GetProcessesByName("Unity").Length > 0;
            // Unity counts as View only while actually playing with View's port 8013 open.
            // If only the Unity Editor is open, the release package should still start its bundled View.
            if (unityRunning && IsViewHttpPortOpen())
                return false;

            var viewPath = FindMajdataViewExecutable();
            if (viewPath == null)
            {
                if (unityRunning)
                    return false; // Development environment: no release View; wait for Unity to enter Play mode.

                MessageBox.Show(
                    GetLocalizedString("ViewExecutableMissing"),
                    GetLocalizedString("Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return true;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = viewPath,
                WorkingDirectory = Path.GetDirectoryName(viewPath) ?? Environment.CurrentDirectory,
                UseShellExecute = true
            });
            ScheduleViewWindowAlignment(2000);
            return true;
        }

        return false;
    }

    private static bool IsViewHttpPortOpen()
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            return client.ConnectAsync("127.0.0.1", 8013).Wait(250);
        }
        catch
        {
            return false;
        }
    }

    private static string? FindMajdataViewExecutable()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(Environment.CurrentDirectory, "MajdataView.exe"),
            Path.Combine(baseDirectory, "MajdataView.exe"),
            // Release layout: App\MajdataEdit and App\MajdataView are siblings.
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "MajdataView", "MajdataView.exe")),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "..", "MajdataView.exe")),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "..", "release", "MaiChartAssistant", "MajdataView.exe")),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "..", "..", "release", "MaiChartAssistant", "MajdataView.exe"))
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private string GetViewerWorkingDirectory()
    {
        var legacy = Path.Combine(Environment.CurrentDirectory, "MajdataView_Data", "StreamingAssets");
        if (Directory.Exists(legacy))
            return legacy;

        // Release layout: View is in App\MajdataView; use its executable to locate StreamingAssets.
        var viewPath = FindMajdataViewExecutable();
        if (viewPath != null)
        {
            var fromExe = Path.Combine(Path.GetDirectoryName(viewPath)!,
                "MajdataView_Data", "StreamingAssets");
            if (Directory.Exists(fromExe))
                return fromExe;
        }

        return legacy;
        /*string tempPath = "";
        Process baseProc;
        Process[] viewProcs;
        viewProcs = Process.GetProcessesByName("MajdataView");
        // Prioritize Majdata First
        if (viewProcs.Length > 0)
        {
            baseProc = viewProcs.First();
            string pwd;
            pwd = baseProc.StartInfo.WorkingDirectory.TrimEnd('/');
            if (pwd.Length == 0) pwd = ".";
            tempPath = pwd + "/MajdataView_Data/StreamingAssets";
        }
        else
        {
            viewProcs = Process.GetProcessesByName("Unity");
        }
        if (viewProcs.Length <= 0)
            throw new Exception("Unable to find MajdataView instance!");

        return (tempPath.Length == 0) ?
            Environment.CurrentDirectory + "/SFX" :
            tempPath;*/
    }

    private void InternalSwitchWindow(bool moveToPlace = true)
    {
        var windowPtr = FindWindow(null, "MajdataView");
        //var thisWindow = FindWindow(null, this.Title);
        ShowWindow(windowPtr, 5); // Restore the window.
        SwitchToThisWindow(windowPtr, true);
        //SwitchToThisWindow(thisWindow, true);
        if (moveToPlace) InternalMoveWindow();
    }

    private void InternalMoveWindow()
    {
        // Never resize View while recording; changing frame dimensions breaks the ffmpeg pipe.
        if (lastEditorState == EditorControlMethod.Record)
            return;

        var windowPtr = FindWindow(null, "MajdataView");
        var source = PresentationSource.FromVisual(this);

        double dpiX = 1, dpiY = 1;
        if (source != null)
        {
            dpiX = 96.0 * source.CompositionTarget.TransformToDevice.M11;
            dpiY = 96.0 * source.CompositionTarget.TransformToDevice.M22;
        }

        //Console.WriteLine(dpiX+" "+dpiY);
        dpiX /= 96d;
        dpiY /= 96d;

        var Height = this.Height * dpiY;
        var Left = this.Left * dpiX;
        var Top = this.Top * dpiY;
        MoveWindow(windowPtr,
            (int)(Left - Height + 20),
            (int)Top,
            (int)Height - 20,
            (int)Height, true);
    }

    private void SetWindowGoldenPosition()
    {
        // Reserved ideal position.
        var ScreenWidth = SystemParameters.PrimaryScreenWidth;
        var ScreenHeight = SystemParameters.PrimaryScreenHeight;

        Left = (ScreenWidth - Width + Height) / 2 - 10;
        Top = (ScreenHeight - Height) / 2;
    }

    private void SwitchFumenOverwriteMode()
    {
        fumenOverwriteMode = !fumenOverwriteMode;
        FumenContent.TextArea.OverstrikeMode = fumenOverwriteMode;

        // Update the prompt popup visibility.
        OverrideModeTipsPopup.Visibility = fumenOverwriteMode ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CheckUpdate()
    {
        if (UpdateCheckLock) return;
        UpdateCheckLock = true;

        #region Local helpers

        SemVersion oldVersionCompatible(string versionString)
        {
            var result = SemVersion.Parse("v0.0.0", SemVersionStyles.Any);
            try
            {
                // Try to parse the version; failure indicates a legacy format.
                result = SemVersion.Parse(versionString, SemVersionStyles.Any);
            }
            catch (FormatException)
            {
                if (versionString.Contains("Back2Root"))
                {
                    // Special back-to-root version.
                    result = SemVersion.Parse("v0.0.0", SemVersionStyles.Any);
                }
                else if (versionString.Contains("Early Access"))
                {
                    // EA version.
                    result = SemVersion.Parse("v0.0.1", SemVersionStyles.Any);
                }
                else if (versionString.Contains("Alpha"))
                {
                    // Legacy format: Alpha<MainVersion>.<SubVersion>[.<ModifiedVersion>]
                    // Ranges from 4.0 through 6.4.
                    // Prefix the original major version with 0. and append -alpha.
                    var startPos = versionString.IndexOfAny("0123456789".ToArray());
                    versionString = "0." + versionString[startPos..];
                    if (versionString.Count(c => { return c == '.'; }) > 2)
                        versionString = versionString[..versionString.LastIndexOf('.')];
                    versionString += "-alpha";
                    result = SemVersion.Parse(versionString, SemVersionStyles.Any);
                }
                else if (versionString.Contains("Beta"))
                {
                    // Legacy format: Beta<MainVersion>.<SubVersion>[.<ModifiedVersion>]
                    // Ranges from 1.0 through 3.1; later semantic versions continue from 4.0.
                    // Append the -beta suffix.
                    var startPos = versionString.IndexOfAny("0123456789".ToArray());
                    versionString = versionString[startPos..];
                    if (versionString.Contains(' '))
                        versionString = versionString[..versionString.IndexOf(' ')];
                    versionString += "-beta";
                    result = SemVersion.Parse(versionString, SemVersionStyles.Any);
                }
                else
                {
                    // Map all other unrecognized versions to v0.0.1-unknown.
                    result = SemVersion.Parse("v0.0.1-unknown", SemVersionStyles.Any);
                }
            }

            return result;
        }

        void requestHandler(string response)
        {
            UpdateCheckLock = false;

            var resJson = JsonConvert.DeserializeObject<JObject>(response)!;
            var latestVersionString = resJson["tag_name"]!.ToString();
            var releaseUrl = resJson["html_url"]!.ToString();

            var latestVersion = oldVersionCompatible(latestVersionString);

            if (latestVersion.ComparePrecedenceTo(MAJDATA_VERSION) > 0)
            {
                // A version mismatch requires an update.
                var msgboxText = string.Format(GetLocalizedString("NewVersionDetected"), latestVersionString,
                    MAJDATA_VERSION_STRING);

                var result = MessageBox.Show(
                    msgboxText,
                    GetLocalizedString("CheckUpdate"),
                    MessageBoxButton.YesNo);
                switch (result)
                {
                    case MessageBoxResult.Yes:
                        var startInfo = new ProcessStartInfo(releaseUrl)
                        {
                            UseShellExecute = true
                        };
                        Process.Start(startInfo);
                        break;
                    case MessageBoxResult.No:
                        break;
                }
            }
            else
            {
                // No newer version is available; no update is needed.
                MessageBox.Show(GetLocalizedString("NoNewVersion"), GetLocalizedString("CheckUpdate"));
            }
        }

        #endregion

        // Check whether the application needs an update.

        try
        {
            requestHandler(
                WebControl.RequestGETAsync("http://api.github.com/repos/LingFeng-bbben/MajdataView/releases/latest"));
        } catch {
            // The network request failed.
            MessageBox.Show(GetLocalizedString("RequestFail"), GetLocalizedString("CheckUpdate"));
        }
    }

    public string GetWindowsTitleString()
    {
        return "MajdataEditAlpha";
    }

    public string GetWindowsTitleString(string info)
    {
        try
        {
            var details = "Editing: " + SimaiProcess.title;
            if (details.Length > 50)
                details = details[..50];
            DCRPCclient.SetPresence(new RichPresence
            {
                Details = details,
                State = "With note count of " + SimaiProcess.notelist.Count,
                Assets = new Assets
                {
                    LargeImageKey = "salt",
                    LargeImageText = "Majdata",
                    SmallImageKey = "None"
                }
            });
        }
        catch
        {
        }

        return GetWindowsTitleString() + " - " + info;
    }

    public void OpenFile(string path)
    {
        initFromFile(path);
    }


    //*PLAY CONTROL

    private enum PlayMethod
    {
        Normal,
        Op,
        Record60,
        Record120
    }
}
