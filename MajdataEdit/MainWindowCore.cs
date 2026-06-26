using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Timers;
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
    public Timer chartChangeTimer = new(1000); // 谱面变更延迟解析]\
    private readonly Timer currentTimeRefreshTimer = new(100);
    private readonly Timer notePreviewTimer = new(120);

    public DiscordRpcClient DCRPCclient = new("1068882546932326481");

    private float deltatime = 4f;
    public EditorSetting? editorSetting;

    private bool fumenOverwriteMode; //谱面文本覆盖模式
    private float ghostCusorPositionTime;
    private bool isDrawing;
    private bool isLoading;
    private bool isReplaceConformed;
    private bool chartParsePending;
    private bool suppressLevelTextChange;
    private bool immediateWaveRefreshQueued;
    private object? timelineDisplaySource;
    private object? timelineEffectSource;
    private object? timelineSubtitleSource;
    private readonly List<TimelineOverlayItem> timelineOverlayCache = new();
    // Small lead-in for a fresh Normal play: send the chart to the View first, wait
    // this long so it can load, then start the BGM and the View clock from the same
    // instant. Without it the BGM started before the View finished loading, startAt
    // went stale by the send+load time, and the View fast-forwarded past (and missed)
    // the first notes — the "scrub back then play drops notes" bug.
    private const double PlaybackLeadIn = 0.2d;
    private double? flowTimelineCursor;
    private bool flowPreviewActive;
    private DateTime flowPreviewStartedAt;
    private double flowPreviewStartTime;
    private int flowPreviewGeneration;
    private int notePreviewGeneration;
    private string? lastNotePreviewKey;
    private const double RecordingIntroDuration = 5d;
    private const double AllPerfectDuration = 4d;

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
        // 这玩意用于其他窗口来滚动Scroll 因为涉及到好多变量都是private的
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

        var audioPath = path + "/track" + (useOgg ? ".ogg" : ".mp3");
        var dataPath = path + "/maidata.txt";
        if (!File.Exists(audioPath))
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
        VolumnSetting.IsEnabled = true;
        MenuMuriCheck.IsEnabled = true;
        Menu_ExportRender.IsEnabled = true;
        Menu_ExportRender60.IsEnabled = true;
        SyntaxCheckButton.IsEnabled = true;
        AutoSaveManager.Of().SetAutoSaveEnable(true);
        SetSavedState(true);
        SyntaxCheck();
    }

    internal async void SyntaxCheck()
    {
        await Task.CompletedTask;
    }

    private double GetTimeFromParsedPosition(int line, int column)
    {
        if (SimaiProcess.timinglist.Count == 0)
            return GetTimelinePosition();

        var low = 0;
        var high = SimaiProcess.timinglist.Count - 1;
        var best = 0;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            var point = SimaiProcess.timinglist[middle];
            var beforeCaret = point.rawTextPositionY < line ||
                              point.rawTextPositionY == line && point.rawTextPositionX <= column;
            if (beforeCaret)
            {
                best = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }
        return SimaiProcess.timinglist[best].time;
    }

    void SetErrCount<T>(T eCount) => Dispatcher.Invoke(() => ErrCount.Content = $"{eCount}");
    private void ReadWaveFromFile()
    {
        var useOgg = File.Exists(maidataDir + "/track.ogg");
        var bgmDecode = Bass.BASS_StreamCreateFile(maidataDir + "/track" + (useOgg ? ".ogg" : ".mp3"), 0L, 0L, BASSFlag.BASS_STREAM_DECODE);
        try
        {
            songLength = Bass.BASS_ChannelBytes2Seconds(bgmDecode,
                Bass.BASS_ChannelGetLength(bgmDecode, BASSMode.BASS_POS_BYTE));
/*                int sampleNumber = (int)((songLength * 1000) / (0.02f * 1000));
                wavedBs = new float[sampleNumber];
                for (int i = 0; i < sampleNumber; i++)
                {
                    wavedBs[i] = Bass.BASS_ChannelGetLevels(bgmDecode, 0.02f, BASSLevel.BASS_LEVEL_MONO)[0];
                }*/
            Bass.BASS_StreamFree(bgmDecode);
            var bgmSample = Bass.BASS_SampleLoad(maidataDir + "/track" + (useOgg ? ".ogg" : ".mp3"), 0, 0, 1, BASSFlag.BASS_DEFAULT);
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
        catch (Exception e)
        {
            MessageBox.Show("mp3/ogg解码失败。\nMP3/OGG Decode fail.\n" + e.Message + Bass.BASS_ErrorGetCode());
            Bass.BASS_StreamFree(bgmDecode);
            Process.Start("https://github.com/LingFeng-bbben/MajdataEdit/issues/26");
        }
    }

    private void SetSavedState(bool state)
    {
        if (state)
        {
            isSaved = true;
            LevelSelector.IsEnabled = true;
            TheWindow.Title = GetWindowsTitleString(SimaiProcess.title!);
        }
        else
        {
            isSaved = false;
            LevelSelector.IsEnabled = false;
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
            return true;
        }

        if (result == MessageBoxResult.Cancel) return false;
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
            SetSavedState(true);
        }
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

        SaveSetting(); // 覆盖旧版本setting
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
                "&first=0\n");
    }

    private void CreateEditorSetting()
    {
        editorSetting = new EditorSetting
        {
            RenderMode =
            RenderOptions.ProcessRenderMode == RenderMode.SoftwareOnly ? 1 : 0 // 使用命令行指定强制软件渲染时，同步修改配置值
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
        editorSetting = JsonConvert.DeserializeObject<EditorSetting>(json)!;
        if (editorSetting.InnerBackgroundCover < 0f)
            editorSetting.InnerBackgroundCover = editorSetting.backgroundCover;
        if (editorSetting.OuterBackgroundCover < 0f)
            editorSetting.OuterBackgroundCover = editorSetting.backgroundCover;

        if (RenderOptions.ProcessRenderMode != RenderMode.SoftwareOnly)
            //如果没有通过命令行预先指定渲染模式，则使用设置项的渲染模式
            RenderOptions.ProcessRenderMode =
                editorSetting.RenderMode == 0 ? RenderMode.Default : RenderMode.SoftwareOnly;
        else
            //如果通过命令行指定了使用软件渲染模式，则覆盖设置项
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

        ViewerSpeed.Content = editorSetting.playSpeed.ToString("F1"); // 转化为形如"7.0", "9.5"这样的速度
        ViewerTouchSpeed.Content = editorSetting.touchSpeed.ToString("F1");

        chartChangeTimer.Interval = editorSetting.ChartRefreshDelay; // 设置更新延迟

        SaveEditorSetting(); // 覆盖旧版本setting
    }

    public void SaveEditorSetting()
    {
        File.WriteAllText(editorSettingFilename, JsonConvert.SerializeObject(editorSetting, Formatting.Indented));
    }

    internal void ApplyEditorAppearance()
    {
        if (editorSetting == null)
            return;

        var theme = ThemeManager.LoadThemeByName(editorSetting.EditorTheme);
        ThemeManager.ApplyApplicationResources(theme);
        FumenContent.FontWeight = FontWeights.Normal;
        FumenContent.FontFamily = editorSetting.EditorFontPreset switch
        {
            0 => new System.Windows.Media.FontFamily("Consolas"),
            1 => new System.Windows.Media.FontFamily("Cascadia Code"),
            2 => new System.Windows.Media.FontFamily("Cascadia Mono"),
            3 => new System.Windows.Media.FontFamily("Microsoft YaHei UI"),
            4 => new System.Windows.Media.FontFamily("Noto Sans SC, Microsoft YaHei UI"),
            5 => new System.Windows.Media.FontFamily("NSimSun, SimSun"),
            6 => new System.Windows.Media.FontFamily("DengXian, Microsoft YaHei UI"),
            7 => new System.Windows.Media.FontFamily("Noto Serif SC, SimSun"),
            8 => new System.Windows.Media.FontFamily("Global Monospace, Consolas"),
            _ => new System.Windows.Media.FontFamily("Cascadia Code, Consolas")
        };
        ThemeManager.ApplyEditor(FumenContent, theme);
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

    // 谱面变更延迟解析
    private void ChartChangeTimer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        Console.WriteLine("TextChanged");
        QueueImmediateWaveRefresh();
        SyntaxCheck();
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
        string? requestJson = null;
        var generation = notePreviewGeneration;
        Dispatcher.Invoke(() =>
        {
            if (generation == notePreviewGeneration &&
                !isLoading && !isPlaying && lastEditorState == EditorControlMethod.Stop)
                requestJson = BuildNotePreviewRequestJson();
        });

        if (!string.IsNullOrEmpty(requestJson) &&
            generation == notePreviewGeneration &&
            !isLoading && !isPlaying && lastEditorState == EditorControlMethod.Stop)
            WebControl.RequestPOST("http://localhost:8013/", requestJson);
    }

    private string? BuildNotePreviewRequestJson()
    {
        var group = NotePreviewModule.ExtractNoteGroupAtCaret(GetRawFumenText(), (int)GetRawFumenPosition());
        var previewNotes = NotePreviewModule.ExpandPreview(group);
        var previewKey = string.Join("/", previewNotes);
        if (string.Equals(previewKey, lastNotePreviewKey, StringComparison.Ordinal))
            return null;

        lastNotePreviewKey = previewKey;
        var request = new EditRequestjson
        {
            control = EditorControlMethod.Preview,
            noteSpeed = editorSetting?.playSpeed ?? 7f,
            touchSpeed = editorSetting?.touchSpeed ?? 7.5f,
            smoothSlideAnime = editorSetting?.SmoothSlideAnime ?? false,
            skin = editorSetting?.Skin ?? "dx",
            songDetailStyle = editorSetting?.SongDetailStyle ?? 0,
            editorPlayMethod = EditorPlayMethod.Disabled,
            previewJson = BuildNotePreviewMajsonJson(previewNotes)
        };
        return JsonConvert.SerializeObject(request);
    }

    private string? BuildNotePreviewMajsonJson(List<string> previewNotes)
    {
        if (previewNotes == null || previewNotes.Count == 0)
            return null;

        var content = string.Join("/", previewNotes);
        // Put preview notes slightly after the preview clock. Touch/hold notes
        // at exactly 0s can be consumed before the user sees them.
        var timing = new SimaiTimingPoint(0.01d, 0, 0, content, 120f);
        timing.noteList = timing.getNotes();
        if (timing.noteList.Count == 0)
        {
            var validBranches = previewNotes
                .SelectMany(note => note.Split('/'))
                .Where(IsPreviewBranchParseable)
                .Distinct()
                .ToList();
            if (validBranches.Count == 0)
                return null;

            content = string.Join("/", validBranches);
            timing = new SimaiTimingPoint(0.01d, 0, 0, content, 120f);
            timing.noteList = timing.getNotes();
            if (timing.noteList.Count == 0)
                return null;
        }

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
        majson.songDetailStyle = editorSetting?.SongDetailStyle ?? 0;
        majson.timingList.Add(timing);
        return JsonConvert.SerializeObject(majson);
    }

    private static bool IsPreviewBranchParseable(string branch)
    {
        if (string.IsNullOrWhiteSpace(branch))
            return false;
        var timing = new SimaiTimingPoint(0.01d, 0, 0, branch, 120f);
        return timing.getNotes().Count > 0;
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
        // Keep the original editor coordinate system. DrawWave, the center
        // cursor and mouse scrolling were all designed around window width.
        var width = Math.Max(1, (int)Width - 2);
        var height = Math.Max(1, (int)MusicWave.Height);
        WaveBitmap = new WriteableBitmap(width, height, 72, 72, PixelFormats.Pbgra32, null);
        MusicWave.Source = WaveBitmap;
    }

    private void DrawWave()
    {
        if (isDrawing) return;
        if (WaveBitmap == null) return;

        Dispatcher.Invoke(() =>
        {
            isDrawing = true;
            var width = WaveBitmap.PixelWidth;
            var height = WaveBitmap.PixelHeight;

            if (waveRaws[0] == null)
            {
                isDrawing = false;
                return;
            }

            WaveBitmap.Lock();
            try
            {

            //the process starts
            var backBitmap = new Bitmap(width, height, WaveBitmap.BackBufferStride,
                PixelFormat.Format32bppArgb, WaveBitmap.BackBuffer);
            var graphics = Graphics.FromImage(backBitmap);
            var currentTime = GetTimelinePosition();

            graphics.Clear(Color.FromArgb(100, 0, 0, 0));
            var resample = (int)deltatime - 1;
            if (resample > 1 && resample <= 3) resample = 1;
            if (resample > 3) resample = 2;
            var waveLevels = waveRaws[resample];

            var step = songLength / waveLevels.Length;
            var startindex = (int)((currentTime - deltatime) / step);
            var stopindex = (int)((currentTime + deltatime) / step);
            var linewidth = backBitmap.Width / (float)(stopindex - startindex);
            var pen = new Pen(Color.Green, linewidth);
            var points = new List<PointF>();
            if (startindex < 0)
            {
                var zeroX = (0 - startindex) * linewidth;
                graphics.DrawLine(pen, 0f, height / 2f, Math.Min(width, zeroX), height / 2f);
            }
            for (var i = startindex; i < stopindex; i = i + 1)
            {
                if (i < 0) continue;
                if (i >= waveLevels.Length - 1) break;

                var x = (i - startindex) * linewidth;
                var y = waveLevels[i] / 65535f * height + height / 2;

                points.Add(new PointF(x, y));
            }

            if (points.Count >= 2)
                graphics.DrawLines(pen, points.ToArray());

            //Draw Bpm lines
            var lastbpm = -1f;
            var bpmChangeTimes = new List<double>(); //在什么时间变成什么值
            var bpmChangeValues = new List<float>();
            bpmChangeTimes.Clear();
            bpmChangeValues.Clear();
            foreach (var timing in SimaiProcess.timinglist)
                if (timing.currentBpm != lastbpm)
                {
                    bpmChangeTimes.Add(timing.time);
                    bpmChangeValues.Add(timing.currentBpm);
                    lastbpm = timing.currentBpm;
                }

            bpmChangeTimes.Add(Bass.BASS_ChannelBytes2Seconds(bgmStream, Bass.BASS_ChannelGetLength(bgmStream)));

            double time = SimaiProcess.first;
            var signature = 4; //预留拍号
            var currentBeat = 1;
            var timePerBeat = 0d;
            pen = new Pen(Color.Yellow, 1);
            var strongBeat = new List<double>();
            var weakBeat = new List<double>();
            for (var i = 1; i < bpmChangeTimes.Count; i++)
            {
                while (time - bpmChangeTimes[i] < -0.05) //在那个时间之前都是之前的bpm
                {
                    if (currentBeat > signature) currentBeat = 1;
                    timePerBeat = 1d / (bpmChangeValues[i - 1] / 60d);
                    if (currentBeat == 1)
                        strongBeat.Add(time);
                    else
                        weakBeat.Add(time);
                    currentBeat++;
                    time += timePerBeat;
                }

                time = bpmChangeTimes[i];
                currentBeat = 1;
            }

            foreach (var btime in strongBeat)
            {
                if (btime - currentTime > deltatime) continue;
                var x = ((float)(btime / step) - startindex) * linewidth;
                graphics.DrawLine(pen, x, 0, x, 75);
            }

            foreach (var btime in weakBeat)
            {
                if (btime - currentTime > deltatime) continue;
                var x = ((float)(btime / step) - startindex) * linewidth;
                graphics.DrawLine(pen, x, 0, x, 15);
            }

            //Draw timing lines
            pen = new Pen(Color.White, 1);
            foreach (var note in SimaiProcess.timinglist)
            {
                if (note == null) break;
                if (note.time - currentTime > deltatime) continue;
                var x = ((float)(note.time / step) - startindex) * linewidth;
                graphics.DrawLine(pen, x, 60, x, 75);
            }

            //Draw notes                    
            using var starFont = new Font("Consolas", 12, System.Drawing.FontStyle.Bold);
            using var breakStarBrush = new SolidBrush(Color.OrangeRed);
            using var eachStarBrush = new SolidBrush(Color.Gold);
            using var normalStarBrush = new SolidBrush(Color.DeepSkyBlue);
            foreach (var note in SimaiProcess.notelist)
            {
                if (note == null) break;
                if (note.time - currentTime > deltatime) continue;
                var notes = note.getNotes();
                var isEach = notes.Count(o => !o.isSlideNoHead) > 1;

                var x = ((float)(note.time / step) - startindex) * linewidth;

                foreach (var noteD in notes)
                {
                    var y = noteD.startPosition * 6.875f + 8f; //与键位有关

                    if (noteD.isHanabi)
                    {
                        var xDeltaHanabi = (float)(1f / step) * linewidth; //Hanabi is 1s due to frame analyze
                        var rectangleF = new RectangleF(x, 0, xDeltaHanabi, 75);
                        if (noteD.noteType == SimaiNoteType.TouchHold)
                            rectangleF.X += (float)(noteD.holdTime / step) * linewidth;
                        var gradientBrush = new LinearGradientBrush(
                            rectangleF,
                            Color.FromArgb(100, 255, 0, 0),
                            Color.FromArgb(0, 255, 0, 0),
                            LinearGradientMode.Horizontal
                        );
                        graphics.FillRectangle(gradientBrush, rectangleF);
                    }

                    if (noteD.noteType == SimaiNoteType.Tap)
                    {
                        if (noteD.isForceStar)
                        {
                            pen.Width = 3;
                            if (noteD.isBreak)
                                pen.Color = Color.OrangeRed;
                            else if (isEach)
                                pen.Color = Color.Gold;
                            else
                                pen.Color = Color.DeepSkyBlue;
                            var brush = noteD.isBreak
                                ? breakStarBrush
                                : isEach ? eachStarBrush : normalStarBrush;
                            graphics.DrawString("*", starFont, brush, new PointF(x - 7f, y - 7f));
                        }
                        else
                        {
                            pen.Width = 2;
                            if (noteD.isBreak)
                                pen.Color = Color.OrangeRed;
                            else if (isEach)
                                pen.Color = Color.Gold;
                            else
                                pen.Color = Color.LightPink;
                            graphics.DrawEllipse(pen, x - 2.5f, y - 2.5f, 5f, 5f);
                        }
                    }

                    if (noteD.noteType == SimaiNoteType.Touch)
                    {
                        pen.Width = 2;
                        pen.Color = isEach ? Color.Gold : Color.DeepSkyBlue;
                        graphics.DrawRectangle(pen, x - 2.5f, y - 2.5f, 5f, 5f);
                    }

                    if (noteD.noteType == SimaiNoteType.Hold)
                    {
                        pen.Width = 3;
                        if (noteD.isBreak)
                            pen.Color = Color.OrangeRed;
                        else if (isEach)
                            pen.Color = Color.Gold;
                        else
                            pen.Color = Color.LightPink;

                        var xRight = x + (float)(noteD.holdTime / step) * linewidth;

                        // Zero-duration holds are short holds, not unbounded holds.
                        if (!float.IsFinite(xRight)) xRight = x;
                        if (xRight - x < 1f) xRight = x + 5;
                        graphics.DrawLine(pen, x, y, xRight, y);

                    }

                    if (noteD.noteType == SimaiNoteType.TouchHold)
                    {
                        pen.Width = 3;
                        var xDelta = (float)(noteD.holdTime / step) * linewidth / 4f;
                        //Console.WriteLine("HoldPixel"+ xDelta);
                        if (!float.IsFinite(xDelta)) xDelta = 0f;
                        if (xDelta < 1f) xDelta = 1;

                        pen.Color = Color.FromArgb(200, 255, 75, 0);
                        graphics.DrawLine(pen, x, y, x + xDelta * 4f, y);
                        pen.Color = Color.FromArgb(200, 255, 241, 0);
                        graphics.DrawLine(pen, x, y, x + xDelta * 3f, y);
                        pen.Color = Color.FromArgb(200, 2, 165, 89);
                        graphics.DrawLine(pen, x, y, x + xDelta * 2f, y);
                        pen.Color = Color.FromArgb(200, 0, 140, 254);
                        graphics.DrawLine(pen, x, y, x + xDelta, y);
                    }

                    if (noteD.noteType == SimaiNoteType.Slide)
                    {
                        pen.Width = 3;
                        if (!noteD.isSlideNoHead)
                        {
                            if (noteD.isBreak)
                                pen.Color = Color.OrangeRed;
                            else if (isEach)
                                pen.Color = Color.Gold;
                            else
                                pen.Color = Color.DeepSkyBlue;
                            var brush = noteD.isBreak
                                ? breakStarBrush
                                : isEach ? eachStarBrush : normalStarBrush;
                            graphics.DrawString("*", starFont, brush, new PointF(x - 7f, y - 7f));
                        }

                        if (noteD.isSlideBreak)
                            pen.Color = Color.OrangeRed;
                        else if (notes.Count(o => o.noteType == SimaiNoteType.Slide) >= 2)
                            pen.Color = Color.Gold;
                        else
                            pen.Color = Color.SkyBlue;
                        pen.DashStyle = DashStyle.Dot;
                        var xSlide = (float)(noteD.slideStartTime / step - startindex) * linewidth;
                        var xSlideRight = (float)(noteD.slideTime / step) * linewidth + xSlide;

                        if (!float.IsNormal(xSlideRight)) xSlideRight = ushort.MaxValue;
                        if (!float.IsNormal(xSlide)) xSlide = ushort.MaxValue;

                        graphics.DrawLine(pen, xSlide, y, xSlideRight, y);
                        pen.DashStyle = DashStyle.Solid;
                    }
                }
            }

            DrawRecordingFlowBackground(graphics, currentTime, deltatime, step, startindex, linewidth, height);
            DrawTimelineOverlay(graphics, currentTime, deltatime, step, startindex, linewidth, height);

            if (playStartTime - currentTime <= deltatime)
            {
                //Draw play Start time
                pen = new Pen(Color.Red, 5);
                var x1 = (float)(playStartTime / step - startindex) * linewidth;
                PointF[] tranglePoints = { new(x1 - 2, 0), new(x1 + 2, 0), new(x1, 3.46f) };
                graphics.DrawPolygon(pen, tranglePoints);
            }

            if (ghostCusorPositionTime - currentTime <= deltatime)
            {
                //Draw ghost cusor
                pen = new Pen(Color.Orange, 5);
                var x2 = (float)(ghostCusorPositionTime / step - startindex) * linewidth;
                PointF[] tranglePoints2 = { new(x2 - 2, 0), new(x2 + 2, 0), new(x2, 3.46f) };
                graphics.DrawPolygon(pen, tranglePoints2);
            }

            graphics.Flush();
            graphics.Dispose();
            backBitmap.Dispose();

            //MusicWave.Width = waveLevels.Length * zoominPower;
            WaveBitmap.AddDirtyRect(new Int32Rect(0, 0, WaveBitmap.PixelWidth, WaveBitmap.PixelHeight));
            }
            finally
            {
                WaveBitmap.Unlock();
                isDrawing = false;
            }
        });
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
            var endTime = item.Time + Math.Max(0d, item.Duration);
            if (item.Time > currentTime + visibleRange || endTime < currentTime - visibleRange)
                continue;

            var x = (float)(item.Time / step - startIndex) * lineWidth;
            var drawDuration = double.IsPositiveInfinity(item.Duration)
                ? Math.Max(visibleRange * 2d, currentTime + visibleRange - item.Time)
                : Math.Max(0d, item.Duration);
            var durationWidth = (float)(drawDuration / step) * lineWidth;
            var label = TrimTimelineLabel(item.Label);
            var textWidth = graphics.MeasureString(label, labelFont).Width + 5f;
            var width = Math.Max(7f, Math.Max(durationWidth, textWidth));
            var y = item.Lane * 13f;
            var rectangle = new RectangleF(x, y, width, 12f);
            using var gradient = new LinearGradientBrush(
                rectangle,
                Color.FromArgb(155, item.Color),
                Color.FromArgb(0, item.Color),
                LinearGradientMode.Horizontal);
            using var labelBrush = new SolidBrush(Color.FromArgb(225, 235, 214, 255));
            graphics.FillRectangle(gradient, rectangle);
            graphics.DrawString(label, labelFont, labelBrush,
                new PointF(x + 2f, y));
        }
    }

    private void RefreshTimelineOverlayCache()
    {
        if (ReferenceEquals(timelineDisplaySource, SimaiProcess.displayTable) &&
            ReferenceEquals(timelineEffectSource, SimaiProcess.effectTable) &&
            ReferenceEquals(timelineSubtitleSource, SimaiProcess.subtitleTable))
            return;

        timelineDisplaySource = SimaiProcess.displayTable;
        timelineEffectSource = SimaiProcess.effectTable;
        timelineSubtitleSource = SimaiProcess.subtitleTable;
        timelineOverlayCache.Clear();

        timelineOverlayCache.AddRange(SimaiProcess.displayTable.Select(item =>
            new TimelineOverlayItem(item.time, Math.Max(0d, item.duration),
                $"{item.property}:{item.target:0.##}", Color.FromArgb(182, 92, 255))));
        timelineOverlayCache.AddRange(SimaiProcess.effectTable.Select(item =>
            new TimelineOverlayItem(item.time, Math.Max(0d, item.duration),
                $"{item.effect}:{item.intensity:0.##}", Color.FromArgb(182, 92, 255))));

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
        DrawFlowBackground(graphics, -RecordingIntroDuration, -1d, "录制加载",
            Color.FromArgb(85, 160, 245), currentTime, visibleRange, step, startIndex, lineWidth, height);
        DrawFlowBackground(graphics, -1d, 0d, "转场",
            Color.FromArgb(70, 210, 175), currentTime, visibleRange, step, startIndex, lineWidth, height);
        var allPerfectStart = GetAllPerfectStartTime();
        if (allPerfectStart >= 0d)
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
        });
    }

    private void ScrollWave(double delta)
    {
        CancelNotePreview();
        if (isPlaying)
            StopPlaybackForScrub();
        delta = delta * deltatime / (Width / 2d);
        var time = GetTimelinePosition();
        SetTimelinePosition(time + delta);
        SimaiProcess.ClearNoteListPlayedState();
        if (GetTimelinePosition() >= 0d && GetTimelinePosition() <= songLength)
            SeekTextFromTime();
        Task.Run(() => DrawWave());
    }

    private void StopPlaybackForScrub()
    {
        TogglePause();
    }

    private double GetTimelinePosition()
    {
        if (flowPreviewActive)
        {
            var elapsed = (DateTime.Now - flowPreviewStartedAt).TotalSeconds * GetPlaybackSpeed();
            return flowPreviewStartTime + elapsed;
        }

        return flowTimelineCursor ??
               Bass.BASS_ChannelBytes2Seconds(bgmStream, Bass.BASS_ChannelGetPosition(bgmStream));
    }

    private void SetTimelinePosition(double time, bool keepFlowCursor = false)
    {
        CancelNotePreview();
        var allPerfectEnd = Math.Max(songLength, GetAllPerfectStartTime() + AllPerfectDuration);
        time = Math.Clamp(time, -RecordingIntroDuration, allPerfectEnd);
        flowPreviewActive = false;
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

        // Build up the fully-qualified name of the key

        var assemblyName = Assembly.GetExecutingAssembly().GetName().Name;
        var fullKey = assemblyName + ":" + resourceFileName + ":" + key;
        var locExtension = new LocExtension(fullKey);
        locExtension.ResolveLocalizedValue(out string? localizedString);

        // Add a space to the end, if requested
        if (addSpaceAfter) localizedString += " ";

        return localizedString ?? key;
    }

    private void TogglePlay(PlayMethod playMethod = PlayMethod.Normal)
    {
        if (Op_Button.IsEnabled == false) return;
        CancelNotePreview();

        if (lastEditorState == EditorControlMethod.Start || playMethod != PlayMethod.Normal)
            if (!sendRequestStop())
                return;

        FumenContent.Focus();
        SaveFumen();
        if (CheckAndStartView()) return;
        Op_Button.IsEnabled = false;
        isPlaying = true;
        isPlan2Stop = false;
        PlayAndPauseButton.Content = "  ▌▌ ";
        var CusorTime = SimaiProcess.Serialize(GetRawFumenText(), GetRawFumenPosition()); //scan first

        //TODO: Moeying改一下你的generateSoundEffect然后把下面这行删了
        var isOpIncluded = playMethod == PlayMethod.Normal ? false : true;

        var startAt = DateTime.Now;
        switch (playMethod)
        {
            case PlayMethod.Record:
            case PlayMethod.Record60:
                Bass.BASS_ChannelSetPosition(bgmStream, 0);
                //TODO: i18n
                MessageBox.Show(GetLocalizedString("AskRender"), GetLocalizedString("Attention"));
                generateSoundEffectList(0.0, isOpIncluded);
                var task = new Task(() => renderSoundEffect(5d));
                try
                {
                    task.Start();
                    task.Wait();
                }
                catch (AggregateException)
                {
                    MessageBox.Show(task.Exception!.InnerException!.Message + "\n" +
                                    task.Exception.InnerException.StackTrace);
                    return;
                }

                startAt = DateTime.Now.AddSeconds(5d);
                if (!sendRequestRun(startAt, playMethod)) return;
                InternalSwitchWindow(false);
                break;
            case PlayMethod.Op:
                generateSoundEffectList(0.0, isOpIncluded);
                InternalSwitchWindow(false);
                Bass.BASS_ChannelSetPosition(bgmStream, 0);
                startAt = DateTime.Now.AddSeconds(5d);
                Bass.BASS_ChannelPlay(trackStartStream, true);
                Task.Run(() =>
                {
                    if (!sendRequestRun(startAt, playMethod)) return;
                    while (DateTime.Now.Ticks < startAt.Ticks)
                        if (lastEditorState != EditorControlMethod.Start)
                            return;
                    Dispatcher.Invoke(() =>
                    {
                        playStartTime =
                            Bass.BASS_ChannelBytes2Seconds(bgmStream, Bass.BASS_ChannelGetPosition(bgmStream));
                        SimaiProcess.ClearNoteListPlayedState();
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

                if (lastEditorState == EditorControlMethod.Pause)
                {
                    // Resume in place: notes are still on the field, nothing reloads, so
                    // there is no send+load gap — start the BGM now and tell the View to
                    // continue.
                    SimaiProcess.ClearNoteListPlayedState();
                    StartSELoop();
                    waveStopMonitorTimer.Start();
                    visualEffectRefreshTimer.Start();
                    startAt = DateTime.Now;
                    Bass.BASS_ChannelPlay(bgmStream, false);
                    Task.Run(() => sendRequestContinue(startAt));
                }
                else
                {
                    // Fresh play (incl. after a scrub): send the chart FIRST so the View
                    // loads during the lead-in, then start the BGM exactly at startAt — the
                    // same shared future instant the View clocks from. Pin startTime to the
                    // scrub position so neither side reads a moving BGM cursor.
                    var bgmStartPos = playStartTime;
                    startAt = DateTime.Now.AddSeconds(PlaybackLeadIn);
                    Task.Run(() =>
                    {
                        if (!sendRequestRun(startAt, playMethod, (float)bgmStartPos)) return;
                        while (DateTime.Now.Ticks < startAt.Ticks)
                            if (lastEditorState != EditorControlMethod.Start) return;
                        Dispatcher.Invoke(() =>
                        {
                            if (!isPlaying) return;
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

    private void TogglePause()
    {
        CancelNotePreview();
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
        Bass.BASS_ChannelStop(bgmStream);
        Bass.BASS_ChannelStop(trackStartStream);
        Bass.BASS_ChannelStop(allperfectStream);
        Bass.BASS_ChannelStop(fanfareStream);
        Bass.BASS_ChannelStop(holdRiserStream);
        //soundEffectTimer.Stop();
        waveStopMonitorTimer.Stop();
        visualEffectRefreshTimer.Stop();
        sendRequestPause();
        DrawWave();
    }

    private void ToggleStop()
    {
        CancelNotePreview();
        flowPreviewActive = false;
        flowPreviewGeneration++;
        Op_Button.IsEnabled = true;
        isPlaying = false;
        isPlan2Stop = false;

        FumenContent.Focus();
        PlayAndPauseButton.Content = "▶";
        Bass.BASS_ChannelStop(bgmStream);
        Bass.BASS_ChannelStop(trackStartStream);
        Bass.BASS_ChannelStop(allperfectStream);
        Bass.BASS_ChannelStop(fanfareStream);
        Bass.BASS_ChannelStop(holdRiserStream);
        //soundEffectTimer.Stop();
        waveStopMonitorTimer.Stop();
        visualEffectRefreshTimer.Stop();
        sendRequestStop();
        SetTimelinePosition(playStartTime);
        DrawWave();
    }

    private void StartFlowPreview(double startTime)
    {
        playStartTime = startTime;
        flowTimelineCursor = null;
        flowPreviewActive = true;
        flowPreviewStartTime = startTime;
        flowPreviewStartedAt = DateTime.Now;
        var generation = ++flowPreviewGeneration;
        var playbackSpeed = GetPlaybackSpeed();
        var leadInSeconds = startTime < 0d ? -startTime / playbackSpeed : 0d;
        var playbackStart = DateTime.Now.AddSeconds(leadInSeconds);
        var viewStartTime = startTime < 0d ? 0f : (float)startTime;

        generateSoundEffectList(Math.Max(0d, startTime), true);
        visualEffectRefreshTimer.Start();
        if (startTime < 0d)
        {
            var introPosition = Math.Clamp(RecordingIntroDuration + startTime, 0d, RecordingIntroDuration);
            Bass.BASS_ChannelSetPosition(trackStartStream, introPosition);
            Bass.BASS_ChannelPlay(trackStartStream, false);
        }
        if (startTime >= GetAllPerfectStartTime())
        {
            if (editorSetting!.ShowAllPerfect)
            {
                Bass.BASS_ChannelPlay(allperfectStream, true);
                Bass.BASS_ChannelPlay(fanfareStream, true);
            }
        }
        else if (startTime >= 0d && startTime <= songLength)
        {
            Bass.BASS_ChannelSetPosition(bgmStream, startTime);
            SimaiProcess.ClearNoteListPlayedState();
            StartSELoop();
            Bass.BASS_ChannelPlay(bgmStream, false);
        }

        var requestPlayMethod = startTime < 0d ? PlayMethod.Op : PlayMethod.Normal;
        Task.Run(() =>
        {
            if (!sendRequestRun(playbackStart, requestPlayMethod, viewStartTime, true))
            {
                Dispatcher.Invoke(() =>
                {
                    if (generation != flowPreviewGeneration)
                        return;
                    flowPreviewActive = false;
                    isPlaying = false;
                    Op_Button.IsEnabled = true;
                    PlayAndPauseButton.Content = "▶";
                    visualEffectRefreshTimer.Stop();
                    DrawWave();
                });
            }
        });

        Task.Run(async () =>
        {
            if (startTime < 0d)
            {
                var delay = playbackStart - DateTime.Now;
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay);
                if (!flowPreviewActive || generation != flowPreviewGeneration)
                    return;
                Dispatcher.Invoke(() =>
                {
                    flowPreviewStartTime = 0d;
                    flowPreviewStartedAt = DateTime.Now;
                    flowTimelineCursor = null;
                    Bass.BASS_ChannelSetPosition(bgmStream, 0d);
                    SimaiProcess.ClearNoteListPlayedState();
                    StartSELoop();
                    Bass.BASS_ChannelPlay(bgmStream, false);
                });
            }

            var previewEnd = Math.Max(songLength, GetAllPerfectStartTime() + AllPerfectDuration);
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
        if (isPlaying)
            TogglePause();
        else
        {
            if (lastEditorState != EditorControlMethod.Pause && 
                editorSetting!.SyntaxCheckLevel == 2 && 
                SyntaxChecker.GetErrorCount() != 0)
            {
                ShowErrorWindow();
                return;
            }
            TogglePlay(playMethod);
        }
            
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
        if (lastEditorState == EditorControlMethod.Pause) sendRequestStop();
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

    private bool sendRequestContinue(DateTime StartAt)
    {
        var request = new EditRequestjson
        {
            control = EditorControlMethod.Continue,
            startAt = StartAt.Ticks,
            startTime = (float)Bass.BASS_ChannelBytes2Seconds(bgmStream, Bass.BASS_ChannelGetPosition(bgmStream)),
            audioSpeed = GetPlaybackSpeed(),
            showJudgeLine = editorSetting.ShowJudgeLine,
            showJudgeText = editorSetting.ShowJudgeText,
            showAllPerfect = editorSetting.ShowAllPerfect,
            skin = editorSetting.Skin,
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
        if (!isPlaying || editorSetting == null)
            return;

        var request = new EditRequestjson
        {
            control = EditorControlMethod.SetDisplay,
            showJudgeInfo = editorSetting.ShowJudgeInfo,
            showComboInfo = editorSetting.ShowComboInfo,
            showJudgeLine = editorSetting.ShowJudgeLine,
            showJudgeText = editorSetting.ShowJudgeText,
            innerBackgroundCover = editorSetting.InnerBackgroundCover,
            outerBackgroundCover = editorSetting.OuterBackgroundCover,
            showAllPerfect = editorSetting.ShowAllPerfect,
            skin = editorSetting.Skin,
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
        var jsonStruct = new Majson();
        foreach (var note in SimaiProcess.notelist)
        {
            note.noteList = note.getNotes();
            jsonStruct.timingList.Add(note);
        }

        jsonStruct.title = SimaiProcess.title!;
        jsonStruct.artist = SimaiProcess.artist!;
        jsonStruct.level = SimaiProcess.levels[selectedDifficulty];
        jsonStruct.designer = SimaiProcess.GetDesignerText(selectedDifficulty);
        jsonStruct.difficulty = SimaiProcess.GetDifficultyText(selectedDifficulty);
        jsonStruct.diffNum = selectedDifficulty;
        jsonStruct.songDetailStyle = editorSetting?.SongDetailStyle ?? 0;
        jsonStruct.wholeBpm = SimaiProcess.GetWholeBpmText();
        jsonStruct.svTable    = SimaiProcess.svTable;    // ALPHA: true SV
        jsonStruct.colorTable = SimaiProcess.colorTable; // ALPHA: note color
        jsonStruct.sizeTable  = SimaiProcess.sizeTable;  // ALPHA: note size
        jsonStruct.alphaTable = SimaiProcess.alphaTable; // ALPHA: note alpha
        jsonStruct.displayTable = SimaiProcess.displayTable; // ALPHA: display transitions
        jsonStruct.subtitleTable = SimaiProcess.subtitleTable; // ALPHA: timed subtitles
        jsonStruct.effectTable = SimaiProcess.effectTable; // ALPHA: screen effects

        var path = maidataDir + "/majdata.json";
        jsonStruct.filePath = path;
        var json = JsonConvert.SerializeObject(jsonStruct);
        File.WriteAllText(path, json);

        var request = new EditRequestjson();
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
            // 将maimaiDX速度换算为View中的单位速度 MajSpeed = 107.25 / (71.4184491 * (MaiSpeed + 0.9975) ^ -0.985558604)
            request.noteSpeed = editorSetting!.playSpeed;
            request.touchSpeed = editorSetting!.touchSpeed;
            request.backgroundCover = editorSetting!.backgroundCover;
            request.innerBackgroundCover = editorSetting.InnerBackgroundCover;
            request.outerBackgroundCover = editorSetting.OuterBackgroundCover;
            request.showJudgeInfo = editorSetting.ShowJudgeInfo;
            request.showComboInfo = editorSetting.ShowComboInfo;
            request.showJudgeLine = editorSetting.ShowJudgeLine;
            request.showJudgeText = editorSetting.ShowJudgeText;
            request.skin = editorSetting.Skin;
            request.songDetailStyle = editorSetting.SongDetailStyle;
            request.previewFlow = previewFlow;
            request.previewTimelineTime = previewFlow
                ? (float)flowPreviewStartTime
                : request.startTime;
            request.showAllPerfect = editorSetting.ShowAllPerfect;
            request.comboStatusType = editorSetting!.comboStatusType;
            request.audioSpeed = GetPlaybackSpeed();
            request.smoothSlideAnime = editorSetting!.SmoothSlideAnime;
            request.editorPlayMethod = editorSetting.editorPlayMethod;
            request.chartLength = chartLen;
            request.recordFrameRate = playMethod == PlayMethod.Record60 ? 60 : 30;
        });

        if (editorSetting?.SongDetailStyle == 1)
            EnsureSongDetailCache(jsonStruct);

        json = JsonConvert.SerializeObject(request);
        var response = WebControl.RequestPOST("http://localhost:8013/", json);
        if (response == "ERROR")
        {
            MessageBox.Show(GetLocalizedString("PortClear"));
            return false;
        }
        lastEditorState = EditorControlMethod.Start;
        return true;
    }

    // 让封面缓存失效:只删签名文件,保留 PNG,避免 View 端瞬间退回实时拼 UI。
    private void InvalidateSongDetailCache(params int[] difficulties)
    {
        if (string.IsNullOrWhiteSpace(maidataDir))
            return;
        try
        {
            if (difficulties == null || difficulties.Length == 0)
                difficulties = new[] { 4, 5 };

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
            // 删除失败不影响功能,大不了下次仍沿用旧缓存。
        }
    }

    private static string? GetSongDetailCacheStem(int difficulty)
    {
        return difficulty switch
        {
            4 => "songdetail_master",
            5 => "songdetail_remaster",
            _ => null
        };
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
                // 元数据只用于判断缓存是否需要刷新,取不到就忽略。
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

    private void EnsureSongDetailCache(Majson majson)
    {
        if (majson.songDetailStyle != 1 || string.IsNullOrWhiteSpace(maidataDir))
            return;

        try
        {
            var templateDir = FindProjectAssetPath("Assets/Resources/SongDetailTemplates/dx");
            var fontDir = FindProjectAssetPath("Assets/Resources/Fonts");
            if (templateDir == null || fontDir == null)
                return;

            var isReMaster = majson.diffNum == 5;
            if (majson.diffNum != 4 && !isReMaster)
                return;
            var basePath = Path.Combine(templateDir, isReMaster ? "DxReMasterBase.png" : "DxBase.png");
            var overlayPath = Path.Combine(templateDir, isReMaster ? "DxReMasterOverlay.png" : "DxOverlay.png");
            var coverPath = FindChartImage("Cover") ?? FindChartImage("bg");
            if (!File.Exists(basePath) || !File.Exists(overlayPath) || coverPath == null)
                return;

            // 只有当谱面信息(标题/曲师/谱师/等级/BPM)或封面文件变化时才重烤,
            // 否则直接沿用已有 PNG。避免每次播放都重新合成、卡顿。
            var cacheStem = GetSongDetailCacheStem(majson.diffNum);
            if (cacheStem == null)
                return;
            var outputPath = Path.Combine(maidataDir, cacheStem + ".png");
            var signaturePath = Path.Combine(maidataDir, cacheStem + ".sig");
            var dxMaxScore = CountTotalNotes(majson) * 3;
            var signature = BuildSongDetailSignature(majson, coverPath, dxMaxScore);
            if (File.Exists(outputPath) && File.Exists(signaturePath) &&
                string.Equals(File.ReadAllText(signaturePath), signature, StringComparison.Ordinal))
                return;

            using var canvas = new Bitmap(341, 588, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(canvas);
            graphics.CompositingMode = CompositingMode.SourceOver;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            graphics.Clear(Color.Transparent);

            using (var baseImage = System.Drawing.Image.FromFile(basePath))
                graphics.DrawImage(baseImage, 0, 0, 341, 588);
            DrawCover(graphics, coverPath, new Rectangle(30, 78, 283, 282));
            using (var overlayImage = System.Drawing.Image.FromFile(overlayPath))
                graphics.DrawImage(overlayImage, 0, 0, 341, 588);

            using var titleFonts = new PrivateFontCollection();
            AddFontIfExists(titleFonts, Path.Combine(fontDir, "MicrosoftYaHei-Bold.ttc"));
            AddFontIfExists(titleFonts, Path.Combine(fontDir, "NotoSansSC-VF.ttf"));
            using var smallFonts = new PrivateFontCollection();
            AddFontIfExists(smallFonts, Path.Combine(fontDir, "Aileron-Regular.otf"));
            using var levelFonts = new PrivateFontCollection();
            AddFontIfExists(levelFonts, Path.Combine(fontDir, "Allerta-Regular.ttf"));

            DrawFitText(graphics, majson.title, titleFonts, new RectangleF(10, 407, 321, 45),
                22f, 10f, Color.White, StringAlignment.Center, true);
            DrawFitText(graphics, majson.artist, titleFonts, new RectangleF(10, 452, 321, 38),
                16f, 8f, Color.FromArgb(235, 241, 255), StringAlignment.Center, true);
            DrawFitText(graphics, majson.designer, smallFonts, new RectangleF(11, 558, 204, 24),
                17f, 9f, Color.FromArgb(28, 34, 62), StringAlignment.Near, false);
            DrawFitText(graphics, GetBpmTextForCache(majson), smallFonts, new RectangleF(226, 558, 94, 24),
                16f, 8f, Color.FromArgb(28, 34, 62), StringAlignment.Far, false);

            DrawLevelTextInBox(
                graphics,
                CleanLevelForCache(majson.level),
                levelFonts,
                new RectangleF(221f, 354f, 113f, 46f),
                isReMaster);

            // DXSCORE 右侧、星级上方:白色 "物量*3/ 物量*3"(左大右小,底部对齐),与谱师同字体。
            DrawDxScore(graphics, dxMaxScore, smallFonts);

            canvas.Save(outputPath, ImageFormat.Png);
            File.WriteAllText(signaturePath, signature);
        }
        catch (Exception e)
        {
            Console.WriteLine("Song detail cache failed: " + e.Message);
        }
    }

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

    private static void DrawLevelTextInBox(
        Graphics graphics,
        string text,
        PrivateFontCollection fonts,
        RectangleF box,
        bool isReMaster = false)
    {
        text = string.IsNullOrWhiteSpace(text) ? "" : text.Trim();
        if (text.Length == 0)
            return;

        var family = fonts.Families.Length > 0 ? fonts.Families[0] : System.Drawing.FontFamily.GenericSansSerif;
        var (number, hasPlus) = SplitLevelForCache(text);
        if (string.IsNullOrEmpty(number))
            number = text;

        using var glyphFormat = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near };
        using var group = new GraphicsPath();

        // "LV" 前缀已经烤进 DxOverlay 模板,这里只画难度数字和 "+",不要再画 LV(会和模板重叠)。
        // 数字左缘紧贴模板里的 LV 右侧(box.Left+43 ≈ x264),垂直居中到 box 中线(≈ y377)。
        var digitsLeft = box.Left + 43f;
        var centerY = box.Top + box.Height * 0.5f;

        using var digits = new GraphicsPath();
        digits.AddString(number, family, (int)System.Drawing.FontStyle.Regular, 50f, PointF.Empty, glyphFormat);
        var db = digits.GetBounds();
        if (db.Width <= 0f || db.Height <= 0f)
            return;
        OffsetPath(digits, digitsLeft - db.Left, centerY - (db.Top + db.Height * 0.5f));
        db = digits.GetBounds();
        group.AddPath(digits, false);

        // "+" superscript: small, just right of the digits and top-aligned near their top.
        if (hasPlus)
        {
            using var plus = new GraphicsPath();
            plus.AddString("+", family, (int)System.Drawing.FontStyle.Regular, 25f, PointF.Empty, glyphFormat);
            var plb = plus.GetBounds();
            OffsetPath(plus, (db.Right + 4f) - plb.Left, (db.Top - 2f) - plb.Top);
            group.AddPath(plus, false);
        }

        using var outline = new Pen(isReMaster ? Color.White : Color.FromArgb(78, 54, 111), 5f)
        {
            LineJoin = LineJoin.Round
        };
        using var fill = new LinearGradientBrush(
            group.GetBounds(),
            isReMaster ? Color.FromArgb(170, 50, 225) : Color.White,
            isReMaster ? Color.FromArgb(44, 8, 102) : Color.FromArgb(220, 224, 236),
            LinearGradientMode.Vertical);
        graphics.DrawPath(outline, group);
        graphics.FillPath(fill, group);
    }

    // DXSCORE 标签右侧那块空白(星级正上方)显示 dx 满分:左边 "物量*3/" 较大、
    // 右边 "物量*3" 较小,"/" 紧贴左侧数字、右侧数字前空一格,两段视觉底部对齐。
    // 用 GraphicsPath 取字形真实下沿(GetBounds().Bottom)对齐,而非 rect,确保不同字号底齐。
    private static void DrawDxScore(Graphics graphics, int dxMaxScore, PrivateFontCollection fonts)
    {
        if (dxMaxScore <= 0)
            return;

        var family = fonts.Families.Length > 0 ? fonts.Families[0] : System.Drawing.FontFamily.GenericSansSerif;
        const float startX = 120f;     // 左缘紧贴 DXSCORE 标签右侧(绿字右缘约 x109)
        const float baselineY = 540f;  // 与 DXSCORE 文字底部对齐
        const float gap = 6f;          // "/" 与右侧数字之间的一个空格
        const float leftSize = 18f;    // 左侧数字(与谱师字号相当,略醒目)
        const float rightSize = 17f;   // 右侧仅比左侧小一点;末位落在第五颗星正上方、AP 徽章之前

        using var fmt = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Near
        };

        var value = dxMaxScore.ToString(CultureInfo.InvariantCulture);

        using var leftPath = new GraphicsPath();
        leftPath.AddString(value + "/", family, (int)System.Drawing.FontStyle.Regular, leftSize, PointF.Empty, fmt);
        var lb = leftPath.GetBounds();
        if (lb.Width <= 0f || lb.Height <= 0f)
            return;
        OffsetPath(leftPath, startX - lb.Left, baselineY - lb.Bottom);
        lb = leftPath.GetBounds();

        using var rightPath = new GraphicsPath();
        rightPath.AddString(value, family, (int)System.Drawing.FontStyle.Regular, rightSize, PointF.Empty, fmt);
        var rb = rightPath.GetBounds();
        OffsetPath(rightPath, (lb.Right + gap) - rb.Left, baselineY - rb.Bottom);

        using var brush = new SolidBrush(Color.White);
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
        foreach (var root in EnumerateSearchRoots())
        {
            var candidate = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
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

    // 渲染逻辑变化时手动 +1,使旧缓存的签名失效、强制重烤一次。
    private const string SongDetailCacheVersion = "v8";

    // 与 View 端 JsonDataLoader.CountNoteSum 同口径统计谱面总物量:
    // 带头 slide = star 头(1) + slide 体(1);其余每个 note(含 break)各计 1。
    // dx 满分 = 总物量 * 3,正是卡面 DXSCORE 右侧显示并纳入签名的值。
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
                        total++; // star 头
                    total++;     // slide 体(break 与否都计 1)
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
            // 取不到封面元数据就只按文本签名,不影响功能。
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
            // 物量*3 纳入签名:开始播放/录制前若与缓存里的 dxscore 对不上,签名变化即触发重烤。
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
        if (Process.GetProcessesByName("MajdataView").Length == 0 && Process.GetProcessesByName("Unity").Length == 0)
        {
            var viewPath = FindMajdataViewExecutable();
            if (viewPath == null)
            {
                MessageBox.Show(
                    "找不到 MajdataView.exe。\n请把 MajdataView.exe 放在 MajdataEdit.exe 同目录，或使用完整发布包运行。",
                    GetLocalizedString("Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return true;
            }

            var viewProcess = Process.Start(new ProcessStartInfo
            {
                FileName = viewPath,
                WorkingDirectory = Path.GetDirectoryName(viewPath) ?? Environment.CurrentDirectory,
                UseShellExecute = true
            });
            var setWindowPosTimer = new Timer(2000)
            {
                AutoReset = false
            };
            setWindowPosTimer.Elapsed += SetWindowPosTimer_Elapsed;
            setWindowPosTimer.Start();
            return true;
        }

        return false;
    }

    private static string? FindMajdataViewExecutable()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(Environment.CurrentDirectory, "MajdataView.exe"),
            Path.Combine(baseDirectory, "MajdataView.exe"),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "..", "MajdataView.exe")),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "..", "release", "MaiChartAssistant", "MajdataView.exe")),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "..", "..", "release", "MaiChartAssistant", "MajdataView.exe"))
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private string GetViewerWorkingDirectory()
    {
        return Environment.CurrentDirectory + "/MajdataView_Data/StreamingAssets";
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
        ShowWindow(windowPtr, 5); //还原窗口
        SwitchToThisWindow(windowPtr, true);
        //SwitchToThisWindow(thisWindow, true);
        if (moveToPlace) InternalMoveWindow();
    }

    private void InternalMoveWindow()
    {
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
        // 属于你的独享黄金位置
        var ScreenWidth = SystemParameters.PrimaryScreenWidth;
        var ScreenHeight = SystemParameters.PrimaryScreenHeight;

        Left = (ScreenWidth - Width + Height) / 2 - 10;
        Top = (ScreenHeight - Height) / 2;
    }

    private void SwitchFumenOverwriteMode()
    {
        fumenOverwriteMode = !fumenOverwriteMode;
        FumenContent.TextArea.OverstrikeMode = fumenOverwriteMode;

        //修改提示弹窗可见性
        OverrideModeTipsPopup.Visibility = fumenOverwriteMode ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CheckUpdate(bool onStart = false)
    {
        if (UpdateCheckLock) return;
        UpdateCheckLock = true;

        #region 子函数

        SemVersion oldVersionCompatible(string versionString)
        {
            var result = SemVersion.Parse("v0.0.0", SemVersionStyles.Any);
            try
            {
                // 尝试解析版本号，解析失败说明是旧版本格式
                result = SemVersion.Parse(versionString, SemVersionStyles.Any);
            }
            catch (FormatException)
            {
                if (versionString.Contains("Back2Root"))
                {
                    // back to root特别版本
                    result = SemVersion.Parse("v0.0.0", SemVersionStyles.Any);
                }
                else if (versionString.Contains("Early Access"))
                {
                    // EA版本
                    result = SemVersion.Parse("v0.0.1", SemVersionStyles.Any);
                }
                else if (versionString.Contains("Alpha"))
                {
                    // 旧版本格式 Alpha<MainVersion>.<SubVersion>[.<ModifiedVersion>]
                    // 从4.0开始，结束于6.4
                    // 在原版本号基础上增加 0. 主版本前缀，并增加 -alpha 后缀
                    var startPos = versionString.IndexOfAny("0123456789".ToArray());
                    versionString = "0." + versionString[startPos..];
                    if (versionString.Count(c => { return c == '.'; }) > 2)
                        versionString = versionString[..versionString.LastIndexOf('.')];
                    versionString += "-alpha";
                    result = SemVersion.Parse(versionString, SemVersionStyles.Any);
                }
                else if (versionString.Contains("Beta"))
                {
                    // 旧版本格式 Beta<MainVersion>.<SubVersion>[.<ModifiedVersion>]
                    // 从1.0开始，结束于3.1。后续的语义化版本号继承该版本号进度，从4.0开始
                    // 增加 -beta 后缀
                    var startPos = versionString.IndexOfAny("0123456789".ToArray());
                    versionString = versionString[startPos..];
                    if (versionString.Contains(' '))
                        versionString = versionString[..versionString.IndexOf(' ')];
                    versionString += "-beta";
                    result = SemVersion.Parse(versionString, SemVersionStyles.Any);
                }
                else
                {
                    // 其他无法识别的版本，均设置为v0.0.1-unknown
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
                // 版本不同，需要更新
                var msgboxText = string.Format(GetLocalizedString("NewVersionDetected"), latestVersionString,
                    MAJDATA_VERSION_STRING);
                if (onStart) msgboxText += "\n\n" + GetLocalizedString("AutoUpdateCheckTip");

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
                // 没有新版本，可以不用更新
                if (!onStart) MessageBox.Show(GetLocalizedString("NoNewVersion"), GetLocalizedString("CheckUpdate"));
            }
        }

        #endregion

        // 检查是否需要更新软件

        try
        {
            requestHandler(
                WebControl.RequestGETAsync("http://api.github.com/repos/LingFeng-bbben/MajdataView/releases/latest"));
        } catch {
            // 网络请求失败
            if (!onStart) MessageBox.Show(GetLocalizedString("RequestFail"), GetLocalizedString("CheckUpdate"));
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
        Record,
        Record60
    }
}
