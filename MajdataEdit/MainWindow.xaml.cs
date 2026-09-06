using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Media;
using System.Text.RegularExpressions;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DiscordRPC.Logging;
using MajdataEdit.AutoSaveModule;
using MajdataEdit.Editor;
using Microsoft.Win32;
using Un4seen.Bass;
using Timer = System.Timers.Timer;

namespace MajdataEdit;

/// <summary>
///     Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly System.Windows.Threading.DispatcherTimer petTypingTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(850)
    };

    public MainWindow()
    {
        InitializeComponent();
        MediaTimelinePanel.ProjectChanged += MediaTimelinePanel_ProjectChanged;
        MediaTimelinePanel.MarkerRequested += MediaTimelinePanel_MarkerRequested;
        MediaTimelinePanel.PlayheadChanged += MediaTimelinePanel_PlayheadChanged;
        fumenEditor = new FumenEditorAdapter(FumenContent);
        FumenContent.TextArea.TextView.BackgroundRenderers.Add(
            new ColorSectionBackgroundRenderer(FumenContent));
        basicParseErrorRenderer = new BasicParseErrorRenderer(FumenContent);
        FumenContent.TextArea.TextView.BackgroundRenderers.Add(basicParseErrorRenderer);
        FumenContent.TextArea.TextView.LineTransformers.Add(new SimaiColorizer());
        FumenContent.TextArea.TextView.MouseHover += FumenContentTextView_MouseHover;
        FumenContent.TextArea.TextView.MouseHoverStopped += FumenContentTextView_MouseHoverStopped;
        FumenContent.TextArea.Caret.PositionChanged += FumenContent_SelectionChanged;
        FumenContent.LostKeyboardFocus += (_, _) => SyntaxCheck();
        FumenContent.Options.AllowToggleOverstrikeMode = false;
        AlphaCommandHints.Attach(FumenContent);
        petTypingTimer.Tick += (_, _) =>
        {
            petTypingTimer.Stop();
            if (basicParseErrorRenderer.HasErrors)
                PetStatusClient.Notify("error", "Chart has syntax errors");
            else
                PetStatusClient.Notify("idle", "Ready");
        };
        if (Environment.GetCommandLineArgs().Contains("--ForceSoftwareRender"))
        {
            MessageBox.Show(GetLocalizedString("SoftwareRenderMode"));
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        }
    }

    private ToolTip? basicParseErrorToolTip;
    private RecordVideoWindow? recordVideoWindow;

    private void FumenContentTextView_MouseHover(object sender, MouseEventArgs e)
    {
        var position = FumenContent.GetPositionFromPoint(e.GetPosition(FumenContent));
        if (position == null)
            return;

        var message = basicParseErrorRenderer.GetMessageForLine(position.Value.Line - 1);
        if (string.IsNullOrWhiteSpace(message))
            return;

        basicParseErrorToolTip = new ToolTip
        {
            Content = message,
            PlacementTarget = FumenContent,
            Padding = new Thickness(8, 5, 8, 5),
            BorderThickness = new Thickness(1)
        };
        basicParseErrorToolTip.SetResourceReference(Control.BackgroundProperty, "EditorBackground");
        basicParseErrorToolTip.SetResourceReference(Control.ForegroundProperty, "ButtonForeground");
        basicParseErrorToolTip.SetResourceReference(Control.BorderBrushProperty, "MenuSeparator");
        basicParseErrorToolTip.IsOpen = true;
        e.Handled = true;
    }

    private void FumenContentTextView_MouseHoverStopped(object sender, MouseEventArgs e)
    {
        if (basicParseErrorToolTip == null)
            return;

        basicParseErrorToolTip.IsOpen = false;
        basicParseErrorToolTip = null;
    }

    private static readonly System.Diagnostics.Stopwatch StartupStopwatch =
        System.Diagnostics.Stopwatch.StartNew();

    private static readonly bool StartupTraceEnabled =
        Environment.GetEnvironmentVariable("MAJDATA_STARTUP_TRACE") == "1";

    // Set MAJDATA_STARTUP_TRACE=1 to log phase timings to startup-trace.log when diagnosing slow release startup.
    private static void TraceStartup(string phase)
    {
        if (!StartupTraceEnabled)
            return;
        try
        {
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "startup-trace.log"),
                $"+{StartupStopwatch.ElapsedMilliseconds}ms {phase}\r\n");
        }
        catch
        {
            // Trace logging must never affect startup.
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        TraceStartup("Loaded begin");
        var viewWasAlreadyRunning =
            Process.GetProcessesByName("MajdataView").Length > 0 ||
            Process.GetProcessesByName("MajdataViewAlpha").Length > 0;
        CheckAndStartView();
        TraceStartup("CheckAndStartView done");
        StartVisualEditBridge();
        TraceStartup("StartVisualEditBridge done");

        TheWindow.Title = GetWindowsTitleString();

        SetWindowGoldenPosition();
        TraceStartup("SetWindowGoldenPosition done");
        // The launcher starts View before Edit. In that path CheckAndStartView
        // does not create its delayed alignment timer, so explicitly reuse the
        // same alignment path after the editor reaches its startup position.
        if (viewWasAlreadyRunning)
            ScheduleViewWindowAlignment(250);

        DCRPCclient.Logger = new ConsoleLogger { Level = LogLevel.Warning };
        _ = Task.Run(() =>
        {
            try
            {
                DCRPCclient.Initialize();
                TraceStartup("DiscordRPC done");
            }
            catch
            {
                // Discord presence is optional and must never block editor startup.
            }
        });
        TraceStartup("DiscordRPC queued");

        var handle = new WindowInteropHelper(this).Handle;
        Bass.BASS_Init(-1, 44100, BASSInit.BASS_DEVICE_CPSPEAKERS, handle);
        TraceStartup("Bass init done");
        InitWave();
        TraceStartup("InitWave done");

        ReadSoundEffect();
        TraceStartup("ReadSoundEffect done");
        ReadEditorSetting();
        VisualChartEditorCheck.IsChecked = editorSetting?.EnableVisualChartEditor ?? true;
        TraceStartup("ReadEditorSetting done");

        chartChangeTimer.Elapsed += ChartChangeTimer_Elapsed;
        chartChangeTimer.AutoReset = false;
        currentTimeRefreshTimer.Elapsed += CurrentTimeRefreshTimer_Elapsed;
        currentTimeRefreshTimer.Start();
        notePreviewTimer.Elapsed += NotePreviewTimer_Elapsed;
        notePreviewTimer.AutoReset = false;
        visualEffectRefreshTimer.Elapsed += VisualEffectRefreshTimer_Elapsed;
        waveStopMonitorTimer.Elapsed += WaveStopMonitorTimer_Elapsed;
        playbackSpeedHideTimer.Elapsed += PlbHideTimer_Elapsed;

        #region Abnormal termination handling

        TraceStartup("timers wired");
        var previousTerminationWasSafe = SafeTerminationDetector.Of().IsLastTerminationSafe();
        var previousEditPath = !previousTerminationWasSafe &&
                               File.Exists(SafeTerminationDetector.Of().RecordPath)
            ? File.ReadAllText(SafeTerminationDetector.Of().RecordPath).Trim()
            : string.Empty;
        // Clear the previous run marker before opening a chart. initFromFile then
        // creates the marker for this run, preserving crash recovery.
        SafeTerminationDetector.Of().RecordProgramClose();
        if (!previousTerminationWasSafe)
        {
            TraceStartup("recovery MessageBox shown");
            // Offer to open the recovery window after an abnormal exit.
            var result = MessageBox.Show(GetLocalizedString("AbnormalTerminationInformation"),
                GetLocalizedString("Attention"), MessageBoxButton.YesNo);
            if (result == MessageBoxResult.Yes)
            {
                if (previousEditPath.Length != 0)
                    // Try to open the chart from the abnormal exit before showing the recovery page.
                    try
                    {
                        initFromFile(previousEditPath);
                    }
                    catch (Exception error)
                    {
                        Console.WriteLine(error.StackTrace);
                    }

                Menu_AutosaveRecover_Click(new object(), new RoutedEventArgs());
            }
        }
        else
        {
            TryOpenLastChart();
        }

        #endregion

        TraceStartup("Loaded end");
        ContentRendered += (_, _) => TraceStartup("ContentRendered");
    }


    //start the view and wait for boot, then set window pos
    private void SetWindowPosTimer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        var setWindowPosTimer = (Timer)sender!;
        Dispatcher.Invoke(() => { InternalSwitchWindow(); });
        setWindowPosTimer.Stop();
        setWindowPosTimer.Dispose();
    }

    private void ScheduleViewWindowAlignment(double delayMilliseconds)
    {
        var setWindowPosTimer = new Timer(delayMilliseconds)
        {
            AutoReset = false
        };
        setWindowPosTimer.Elapsed += SetWindowPosTimer_Elapsed;
        setWindowPosTimer.Start();
    }

    //Window events
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!isSaved)
            if (!AskSave())
            {
                e.Cancel = true;
                return;
            }

        var viewProcesses = Process.GetProcessesByName("MajdataView");
        var closeView = false;
        if (viewProcesses.Length > 0)
        {
            var result = MessageBox.Show(
                GetLocalizedString("AskCloseView"),
                GetLocalizedString("Attention"),
                MessageBoxButton.YesNoCancel);
            if (result == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
                return;
            }
            closeView = result == MessageBoxResult.Yes;
        }

        var petProcesses = Process.GetProcessesByName("MajdataLauncher");
        var closePet = false;
        if (petProcesses.Length > 0)
        {
            var result = MessageBox.Show(
                GetLocalizedString("AskExitPet"),
                GetLocalizedString("Attention"),
                MessageBoxButton.YesNoCancel);
            if (result == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
                return;
            }
            closePet = result == MessageBoxResult.Yes;
        }

        // Everything that talks to View has to go quiet before View is killed,
        // otherwise a preview tick fires into a closed port and the user sees a
        // "connection refused" box on the way out.
        WebControl.IsShuttingDown = true;
        currentTimeRefreshTimer.Stop();
        StopVisualEditBridge();
        notePreviewTimer.Stop();
        visualEffectRefreshTimer.Stop();

        if (closeView)
            foreach (var view in viewProcesses)
                view.Kill();
        if (closePet)
            foreach (var pet in petProcesses)
                pet.Kill();

        soundSetting.Close();
        //if (bpmtap != null) { bpmtap.Close(); }
        //if (muriCheck != null) { muriCheck.Close(); }
        SaveSetting();

        Bass.BASS_ChannelStop(bgmStream);
        Bass.BASS_StreamFree(bgmStream);
        Bass.BASS_ChannelStop(answerStream);
        Bass.BASS_StreamFree(answerStream);
        Bass.BASS_ChannelStop(breakStream);
        Bass.BASS_StreamFree(breakStream);
        Bass.BASS_ChannelStop(judgeExStream);
        Bass.BASS_StreamFree(judgeExStream);
        Bass.BASS_ChannelStop(hanabiStream);
        Bass.BASS_StreamFree(hanabiStream);
        Bass.BASS_Stop();
        Bass.BASS_Free();

        // Normal exit
        SafeTerminationDetector.Of().RecordProgramClose();
    }

    //Window grid events
    private void Grid_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Handled)
            return;
        if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
            e.Data.GetData(DataFormats.FileDrop) is string[] files &&
            files.Any(MediaTimelineEditor.CanImportFile))
        {
            e.Effects = DragDropEffects.Copy;
            return;
        }
        e.Effects = DragDropEffects.Move;
    }

    private async void Grid_Drop(object sender, DragEventArgs e)
    {
        if (e.Handled)
            return;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) ||
            e.Data.GetData(DataFormats.FileDrop) is not string[] files)
            return;

        var mediaFiles = files.Where(MediaTimelineEditor.CanImportFile).ToArray();
        if (mediaFiles.Length > 0)
        {
            e.Handled = true;
            await OpenMediaTimelineAsync(mediaFiles);
            return;
        }

        var maidataPath = files.FirstOrDefault(path =>
            string.Equals(Path.GetFileName(path), "maidata.txt", StringComparison.OrdinalIgnoreCase));
        if (maidataPath == null)
            return;
        if (!isSaved && !AskSave())
            return;
        var fileInfo = new FileInfo(maidataPath);
        initFromFile(fileInfo.DirectoryName!);
    }

    private void FindClose_MouseDown(object sender, MouseButtonEventArgs e)
    {
        FindGrid.Visibility = Visibility.Collapsed;
        FumenContent.Focus();
    }

    #region MENU BARS

    private void Menu_New_Click(object sender, RoutedEventArgs e)
    {
        if (!isSaved)
            if (!AskSave())
                return;
        var openFileDialog = new OpenFileDialog
        {
            Filter = "track.mp3, track.ogg|track.mp3;track.ogg"
        };
        if ((bool)openFileDialog.ShowDialog()!)
        {
            var fileInfo = new FileInfo(openFileDialog.FileName);
            CreateNewFumen(fileInfo.DirectoryName!);
            initFromFile(fileInfo.DirectoryName!);
        }
    }

    private void Menu_Open_Click(object sender, RoutedEventArgs e)
    {
        if (!isSaved)
            if (!AskSave())
                return;
        var openFileDialog = new OpenFileDialog
        {
            Filter = "maidata.txt|maidata.txt"
        };
        if ((bool)openFileDialog.ShowDialog()!)
        {
            var fileInfo = new FileInfo(openFileDialog.FileName);
            initFromFile(fileInfo.DirectoryName!);
        }
    }

    private void Menu_Save_Click(object sender, RoutedEventArgs e)
    {
        SaveFumen(true);
        SystemSounds.Beep.Play();
    }

    private void Menu_ExportNoAlpha_Click(object sender, RoutedEventArgs e)
    {
        ExportNoAlphaFumen();
    }

    private void Menu_SaveAs_Click(object sender, RoutedEventArgs e)
    {
    }

    private void Menu_ExportRender_Click(object sender, RoutedEventArgs e)
    {
        if (recordVideoWindow is { IsLoaded: true })
        {
            recordVideoWindow.Activate();
            return;
        }
        var window = new RecordVideoWindow(this) { Owner = this };
        recordVideoWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(recordVideoWindow, window))
                recordVideoWindow = null;
        };
        // Do not disable the Edit owner: a modal owner transition makes some
        // transparent topmost desktop-pet windows flash black under DWM.
        window.Show();
    }

    private void MirrorLeftRight_MenuItem_Click(object? sender, RoutedEventArgs e)
    {
        fumenEditor.ReplaceSelection(Mirror.NoteMirrorHandle(fumenEditor.SelectedText, Mirror.HandleType.LRMirror));
    }

    private void MirrorUpDown_MenuItem_Click(object? sender, RoutedEventArgs e)
    {
        fumenEditor.ReplaceSelection(Mirror.NoteMirrorHandle(fumenEditor.SelectedText, Mirror.HandleType.UDMirror));
    }

    private void Mirror180_MenuItem_Click(object? sender, RoutedEventArgs e)
    {
        fumenEditor.ReplaceSelection(Mirror.NoteMirrorHandle(fumenEditor.SelectedText, Mirror.HandleType.HalfRotation));
    }

    private void Mirror45_MenuItem_Click(object? sender, RoutedEventArgs e)
    {
        fumenEditor.ReplaceSelection(Mirror.NoteMirrorHandle(fumenEditor.SelectedText, Mirror.HandleType.Rotation45));
    }

    private void MirrorCcw45_MenuItem_Click(object? sender, RoutedEventArgs e)
    {
        fumenEditor.ReplaceSelection(Mirror.NoteMirrorHandle(fumenEditor.SelectedText, Mirror.HandleType.CcwRotation45));
    }

    private void BPMtap_MenuItem_Click(object? sender, RoutedEventArgs e)
    {
        var tap = new BPMtap();
        tap.Owner = this;
        tap.Show();
    }

    private void ChartAssistant_MenuItem_Click(object? sender, RoutedEventArgs e)
    {
        var window = new ChartLibraryWindow { Owner = this };
        window.ShowDialog();
    }

    private void NoteDensity_MenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (selectedDifficulty < 0 || SimaiProcess.notelist == null || SimaiProcess.notelist.Count == 0)
        {
            MessageBox.Show(GetLocalizedString("NoChartForDensity"));
            return;
        }

        // Match CountTotalNotes categorization: Slide heads count as pink, Slide bodies as blue, and TouchHolds as Touch.
        var samples = new List<NoteDensitySample>();
        var maxTime = 0d;
        foreach (var tp in SimaiProcess.notelist)
        {
            maxTime = Math.Max(maxTime, tp.time);
            foreach (var n in tp.getNotes())
            {
                switch (n.noteType)
                {
                    case SimaiNoteType.Tap:
                    case SimaiNoteType.Hold:
                        samples.Add(new NoteDensitySample(tp.time, DensityCategory.TapFamily));
                        break;
                    case SimaiNoteType.Touch:
                    case SimaiNoteType.TouchHold:
                        samples.Add(new NoteDensitySample(tp.time, DensityCategory.Touch));
                        break;
                    case SimaiNoteType.Slide:
                        if (!n.isSlideNoHead)
                            samples.Add(new NoteDensitySample(tp.time, DensityCategory.TapFamily));
                        samples.Add(new NoteDensitySample(tp.time, DensityCategory.SlideBody));
                        break;
                }
            }
        }

        if (samples.Count == 0)
        {
            MessageBox.Show(GetLocalizedString("NoNotesForDensity"));
            return;
        }

        var length = Math.Max(songLength, maxTime);
        var window = new NoteDensityWindow(samples, GetDensityAudioEnvelope(), length, songLength,
            SimaiProcess.title ?? "")
        {
            Owner = this
        };
        window.Show();
    }

    private void FormatBrushAuto_MenuItem_Click(object? sender, RoutedEventArgs e)
        => ApplyBeatFormatBrush(null);

    private void FormatBrush8_MenuItem_Click(object? sender, RoutedEventArgs e)
        => ApplyBeatFormatBrush(8);

    private void FormatBrush12_MenuItem_Click(object? sender, RoutedEventArgs e)
        => ApplyBeatFormatBrush(12);

    private void FormatBrush16_MenuItem_Click(object? sender, RoutedEventArgs e)
        => ApplyBeatFormatBrush(16);

    private void FormatBrush24_MenuItem_Click(object? sender, RoutedEventArgs e)
        => ApplyBeatFormatBrush(24);

    private void FormatBrush32_MenuItem_Click(object? sender, RoutedEventArgs e)
        => ApplyBeatFormatBrush(32);

    private void FormatBrush384_MenuItem_Click(object? sender, RoutedEventArgs e)
        => ApplyBeatFormatBrush(384);

    private void ApplyBeatFormatBrush(int? targetBeat)
    {
        var text = fumenEditor.Text;
        if (!fumenEditor.HasSelection)
        {
            var wholeChart = BeatFormatBrush.Transform(text, targetBeat);
            if (!string.Equals(text, wholeChart, StringComparison.Ordinal))
                fumenEditor.Text = wholeChart;
            return;
        }

        var selection = fumenEditor.Selection;
        var original = text.Substring(
            Math.Clamp(selection.Start, 0, text.Length),
            Math.Clamp(selection.Length, 0, Math.Max(0, text.Length - selection.Start)));
        var transformed = BeatFormatBrush.TransformSelection(
            text, selection.Start, selection.Length, targetBeat);
        if (!string.Equals(original, transformed, StringComparison.Ordinal))
            fumenEditor.ReplaceSelection(transformed);
    }

    private void AutoOrganize16_MenuItem_Click(object? sender, RoutedEventArgs e)
    {
        var text = fumenEditor.Text;
        if (!ChartOrganizer.CanOrganize(text))
        {
            MessageBox.Show(GetLocalizedString("OrganizeNeedsDivision"), GetLocalizedString("AutoOrganize"));
            return;
        }

        var options = new OrganizeOptionsWindow { Owner = this };
        if (options.ShowDialog() != true || !options.AddMeasureComments.HasValue)
            return;

        var organized = ChartOrganizer.Organize(text, options.AddMeasureComments.Value);
        if (string.Equals(organized, text, StringComparison.Ordinal))
            return;

        fumenEditor.Text = organized;
    }

    private void SearchSelectedChartPattern_MenuItem_Click(object? sender, RoutedEventArgs e)
    {
        var query = fumenEditor.SelectedText.Trim();
        if (string.IsNullOrWhiteSpace(query))
            return;

        var window = new ChartLibraryWindow(query) { Owner = this };
        window.Show();
    }

    private void FumenContextMenu_Opened(object? sender, RoutedEventArgs e)
    {
        MergeNoteStreamsMenuItem.Visibility = NoteStreamMerger.CanMerge(
            fumenEditor.Text, FumenContent.CaretOffset)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void MergeNoteStreams_MenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (!NoteStreamMerger.TryBuildMerge(
                fumenEditor.Text,
                FumenContent.CaretOffset,
                out var start,
                out var length,
                out var replacement,
                out var error))
        {
            MessageBox.Show(error, GetLocalizedString("MergeNoteStreams"));
            return;
        }

        FumenContent.Document.BeginUpdate();
        try
        {
            FumenContent.Document.Replace(start, length, replacement);
            FumenContent.CaretOffset = start + replacement.Length;
        }
        finally
        {
            FumenContent.Document.EndUpdate();
        }
        FumenContent.Focus();
    }

    private async void MuriCheck_MenuItem_Click(object? sender, RoutedEventArgs e)
    {
        var chart = fumenEditor.Text;
        if (string.IsNullOrWhiteSpace(chart))
        {
            MessageBox.Show(GetLocalizedString("MuriChartEmpty"), GetLocalizedString("MuriCheckExternal"));
            return;
        }

        // Prefer bundled MaiMuriDX at tools\MaiMuriDX\lib, then fall back to desktop version 1.1.0.
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "tools", "MaiMuriDX", "lib"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "MaiMuriDX-1.1.0", "lib"),
        };
        var libDir = candidates.FirstOrDefault(d =>
            File.Exists(Path.Combine(d, "python.exe")) && File.Exists(Path.Combine(d, "cli.py")));
        if (libDir == null)
        {
            MessageBox.Show(string.Format(GetLocalizedString("MuriToolMissing"),
                    "\n" + string.Join("\n", candidates)),
                GetLocalizedString("MuriCheckExternal"));
            return;
        }

        var tempChart = Path.Combine(Path.GetTempPath(), "majedit_muri_chart.txt");
        var tempReport = Path.Combine(Path.GetTempPath(), "majedit_muri_report.txt");
        try
        {
            // cli.py reads UTF-8 and writes UTF-8 without BOM. The first offset shifts only absolute time, not muri relationships, so pass zero.
            await File.WriteAllTextAsync(tempChart, chart, new System.Text.UTF8Encoding(false));
            if (File.Exists(tempReport))
                File.Delete(tempReport);

            var psi = new ProcessStartInfo
            {
                FileName = Path.Combine(libDir, "python.exe"),
                WorkingDirectory = libDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add(Path.Combine(libDir, "cli.py"));
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add(tempChart);
            psi.ArgumentList.Add("-o");
            psi.ArgumentList.Add(tempReport);

            string stderr;
            using (var proc = Process.Start(psi)!)
            {
                var errTask = proc.StandardError.ReadToEndAsync();
                await proc.StandardOutput.ReadToEndAsync();
                stderr = await errTask;
                await proc.WaitForExitAsync();
            }

            var report = File.Exists(tempReport)
                ? await File.ReadAllTextAsync(tempReport, System.Text.Encoding.UTF8)
                : string.Empty;
            if (string.IsNullOrWhiteSpace(report))
                report = string.IsNullOrWhiteSpace(stderr)
                    ? GetLocalizedString("MuriNoOutput")
                    : string.Format(GetLocalizedString("MuriToolError"), "\n" + stderr);

            ShowMuriReport(report);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, GetLocalizedString("MuriCheckFailed"));
        }
        finally
        {
            try { if (File.Exists(tempChart)) File.Delete(tempChart); } catch { }
        }
    }

    private void ShowMuriReport(string report)
    {
        var box = new TextBox
        {
            Text = report,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            Padding = new Thickness(8),
        };
        new Window
        {
            Title = GetLocalizedString("MuriResultTitle"),
            Width = 760,
            Height = 580,
            Owner = this,
            Content = box,
        }.Show();
    }

    private async void AudioConvert44100_MenuItem_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            InitialDirectory = Directory.Exists(maidataDir) ? maidataDir : "",
            Filter = "Audio|*.wav;*.mp3;*.ogg;*.flac;*.m4a;*.aac|All files|*.*"
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            await RunMediaEditAsync(dialog.FileName,
                () => MediaTools.ConvertAudioTo44100Async(dialog.FileName));
            MessageBox.Show(GetLocalizedString("AudioConvertDone"), "MajdataEdit");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "ffmpeg failed");
        }
    }

    private async void MediaCutRange_MenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (!TryGetMediaTrimRange(out var range))
        {
            MessageBox.Show(GetLocalizedString("MediaMarkersRequired"), "MajdataEdit");
            return;
        }

        var dialog = new OpenFileDialog
        {
            InitialDirectory = Directory.Exists(maidataDir) ? maidataDir : "",
            Filter = "Media|*.mp4;*.mov;*.mkv;*.webm;*.wav;*.mp3;*.ogg;*.flac;*.m4a;*.aac|All files|*.*"
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            await RunMediaEditAsync(dialog.FileName,
                () => MediaTools.RemoveRangeAsync(dialog.FileName, range.Start, range.End));
            MessageBox.Show(GetLocalizedString("MediaCutDone"), "MajdataEdit");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "ffmpeg failed");
        }
    }

    private async void PrependFourBeats_MenuItem_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            InitialDirectory = Directory.Exists(maidataDir) ? maidataDir : "",
            Filter = "Media|*.mp4;*.mov;*.mkv;*.webm;*.wav;*.mp3;*.ogg;*.flac;*.m4a;*.aac|All files|*.*"
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var beatCount = MediaBeatCountDialog.ShowDialog(this);
            if (!beatCount.HasValue)
                return;
            var duration = GetBeatDuration(beatCount.Value);
            await RunMediaEditAsync(dialog.FileName,
                () => MediaTools.PrependBlankAsync(dialog.FileName, duration));
            MessageBox.Show(string.Format(
                CultureInfo.CurrentCulture,
                GetLocalizedString("PrependFourBeatsDone"),
                beatCount.Value,
                duration), "MajdataEdit");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "ffmpeg failed");
        }
    }

    private async void MediaTimeline_MenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (MediaTimelinePanel.Visibility == Visibility.Visible)
        {
            MediaTimelinePanel_CloseRequested(MediaTimelinePanel, EventArgs.Empty);
            return;
        }
        await OpenMediaTimelineAsync();
    }

    private async Task OpenMediaTimelineAsync(IEnumerable<string>? importFiles = null)
    {
        if (string.IsNullOrWhiteSpace(maidataDir) || !Directory.Exists(maidataDir))
        {
            MessageBox.Show(GetLocalizedString("NoMaidata_txt"), GetLocalizedString("Attention"));
            return;
        }

        if (isPlaying || lastEditorState == EditorControlMethod.Pause)
            ToggleStop();

        SimaiProcess.Serialize(GetRawFumenText(), GetRawFumenPosition());
        var projectEnd = MediaTimelineProject.LoadWorking(maidataDir).Clips
            .Select(clip => clip.TimelineEnd)
            .DefaultIfEmpty(songLength)
            .Max();
        BuildWaveBeatLines(Math.Max(Math.Max(songLength, projectEnd), 30d),
            out var strongBeats, out var weakBeats);
        double beatDuration;
        try
        {
            beatDuration = GetBeatDuration(1d);
        }
        catch
        {
            beatDuration = 0.5d;
        }

        await MediaTimelinePanel.ConfigureAsync(
            maidataDir,
            songLength,
            strongBeats,
            weakBeats,
            beatDuration,
            SimaiProcess.mediaTrimStart,
            SimaiProcess.mediaTrimEnd);
        MediaTimelinePanel.Visibility = Visibility.Visible;
        TimelineStopButton.Visibility = Visibility.Visible;
        TimelinePlayAndPauseButton.Visibility = Visibility.Visible;
        MediaTimelinePanel.Focus();
        if (importFiles != null)
            await MediaTimelinePanel.AddFilesAsync(importFiles);
    }

    private void MediaTimelinePanel_CloseRequested(object? sender, EventArgs e)
    {
        MediaTimelinePanel.Visibility = Visibility.Collapsed;
        TimelineStopButton.Visibility = Visibility.Collapsed;
        TimelinePlayAndPauseButton.Visibility = Visibility.Collapsed;
        FumenContent.Focus();
    }

    private void InsertMeasureTemplate_MenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag })
        {
            var values = tag.Split('|');
            if (values.Length == 2 && int.TryParse(values[1], out var measures))
                InsertMeasureTemplate(values[0], measures);
        }
    }

    private void AlphaSyntaxHelp_MenuItem_Click(object sender, RoutedEventArgs e)
    {
        new AlphaSyntaxHelp { Owner = this }.ShowDialog();
    }

    private void MenuItem_InfomationEdit_Click(object? sender, RoutedEventArgs e)
    {
        var before = BuildSongDetailInfoFingerprint();
        var infoWindow = new Infomation();
        SetSavedState(false);
        infoWindow.ShowDialog();
        TheWindow.Title = GetWindowsTitleString(SimaiProcess.title!);
        var after = BuildSongDetailInfoFingerprint();
        if (!string.Equals(before, after, StringComparison.Ordinal))
        {
            InvalidateSongDetailCache();
            SchedulePreBakeSongDetail(); // Re-bake the current difficulty cover after metadata changes.
        }
    }

    private void MenuItem_Majnet_Click(object? sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo() { FileName = "https://majdata.net", UseShellExecute = true });
        // Chart format in maidata.txt
    }

    private void MenuItem_GitHub_Click(object? sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo() { FileName = "https://github.com/LingFeng-bbben/MajdataView", UseShellExecute = true });
    }

    private void MenuItem_SoundSetting_Click(object? sender, RoutedEventArgs e)
    {
        soundSetting = new SoundSetting
        {
            Owner = this
        };
        soundSetting.ShowDialog();
        SaveSetting(); // Save current BASS channel volumes to majSetting.json when the volume window closes.
    }

    private void MuriCheck_Click_1(object? sender, RoutedEventArgs e)
    {
        var muriCheck = new MuriCheck
        {
            Owner = this
        };
        muriCheck.Show();
    }
    private void SyntaxCheckButton_Click(object sender, RoutedEventArgs e)
    {
        ShowErrorWindow();
    }
    void ShowErrorWindow()
    {
        var mcrWindow = new MuriCheckResult
        {
            Owner = this
        };
        // The same list the squiggles come from, so the window cannot show a chart
        // as clean while the editor is underlining it. Positions are already
        // zero-based, which is what SelectLineColumn expects.
        foreach (var error in latestParseErrors)
        {
            mcrWindow.errorPosition.Add(new ErrorInfo(error.PositionX, error.PositionY));
            var eRow = new ListBoxItem
            {
                Content = error.Message,
                Name = "rr" + mcrWindow.CheckResult_Listbox.Items.Count
            };
            eRow.AddHandler(PreviewMouseDoubleClickEvent,
                new MouseButtonEventHandler(mcrWindow.ListBoxItem_PreviewMouseDoubleClick));
            mcrWindow.CheckResult_Listbox.Items.Add(eRow);
        }
        mcrWindow.Show();
    }
    private void SyntaxCheckButton_Click(object sender, MouseButtonEventArgs e)
    {
        ShowErrorWindow();
    }
    private void MenuItem_EditorSetting_Click(object? sender, RoutedEventArgs e)
    {
        var esp = new EditorSettingPanel
        {
            Owner = this
        };
        esp.ShowDialog();
    }

    private void Menu_ResetViewWindow(object? sender, RoutedEventArgs e)
    {
        if (CheckAndStartView()) return;
        InternalSwitchWindow();
    }

    private void MenuFind_Click(object? sender, RoutedEventArgs e)
    {
        if (FindGrid.Visibility == Visibility.Collapsed)
        {
            FindGrid.Visibility = Visibility.Visible;
            InputText.Focus();
        }
        else
        {
            FindGrid.Visibility = Visibility.Collapsed;
        }
    }

    private void CheckUpdate_Click(object? sender, RoutedEventArgs e)
    {
        CheckUpdate();
    }

    private void Menu_AutosaveRecover_Click(object? sender, RoutedEventArgs e)
    {
        var asr = new AutoSaveRecover
        {
            Owner = this
        };
        asr.ShowDialog();
    }

    #endregion

    #region Keyboard shortcuts

    // Every shortcut below does its work in Executed, where work belongs. They
    // used to do it from CanExecute and never answer the question CanExecute was
    // asking, so WPF concluded the command could not run: it ran the handler,
    // then left the keystroke unclaimed for the editor to type out. That is where
    // the stray "s" from Ctrl+S came from, and an "f" from Ctrl+F with it.
    private void Shortcut_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = true;
    }

    private void PlayAndPause_Executed(object? sender, ExecutedRoutedEventArgs e) // Keyboard shortcut
    {
        TogglePlayAndStop();
    }

    private void StopPlaying_Executed(object? sender, ExecutedRoutedEventArgs e) // Keyboard shortcut
    {
        TogglePlayAndPause();
    }

    private void SaveFile_Command_Executed(object? sender, ExecutedRoutedEventArgs e)
    {
        SaveFumen(true);
        SystemSounds.Beep.Play();
    }

    private void SendToView_Executed(object? sender, ExecutedRoutedEventArgs e)
    {
        JumpToIntroAndPlay();
    }

    private void IncreasePlaybackSpeed_Executed(object? sender, ExecutedRoutedEventArgs e)
    {
        if (Bass.BASS_ChannelIsActive(bgmStream) == BASSActive.BASS_ACTIVE_PLAYING) return;
        var speed = GetPlaybackSpeed();
        Console.WriteLine(speed);
        speed += 0.25f;
        PlbSpdLabel.Content = speed * 100 + "%";
        SetPlaybackSpeed(speed);
        PlbSpdAdjGrid.Visibility = Visibility.Visible;
        playbackSpeedHideTimer.Stop();
        playbackSpeedHideTimer.Start();
    }

    private void DecreasePlaybackSpeed_Executed(object? sender, ExecutedRoutedEventArgs e)
    {
        if (Bass.BASS_ChannelIsActive(bgmStream) == BASSActive.BASS_ACTIVE_PLAYING) return;
        var speed = GetPlaybackSpeed();
        Console.WriteLine(speed);
        speed -= 0.25f;
        if (speed < 1e-6) return; // Interrupt if it's an epsilon or lower.
        PlbSpdLabel.Content = speed * 100 + "%";
        SetPlaybackSpeed(speed);
        PlbSpdAdjGrid.Visibility = Visibility.Visible;
        playbackSpeedHideTimer.Stop();
        playbackSpeedHideTimer.Start();
    }

    private readonly Timer playbackSpeedHideTimer = new(1000);

    private void PlbHideTimer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        Dispatcher.Invoke(() => { PlbSpdAdjGrid.Visibility = Visibility.Collapsed; });
        ((Timer)sender!).Stop();
    }

    private void FindCommand_Executed(object? sender, ExecutedRoutedEventArgs e)
    {
        if (FindGrid.Visibility == Visibility.Collapsed)
        {
            FindGrid.Visibility = Visibility.Visible;
            InputText.Focus();
        }
        else
        {
            FindGrid.Visibility = Visibility.Collapsed;
        }
    }

    private void MirrorLRCommand_Executed(object? sender, ExecutedRoutedEventArgs e)
    {
        MirrorLeftRight_MenuItem_Click(sender, null);
    }

    private void MirrorUDCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        MirrorUpDown_MenuItem_Click(sender, null);
    }

    private void Mirror180Command_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        Mirror180_MenuItem_Click(sender, null);
    }

    private void Mirror45Command_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        Mirror45_MenuItem_Click(sender, null);
    }

    private void MirrorCcw45Command_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        MirrorCcw45_MenuItem_Click(sender, null);
    }

    #endregion

    #region Left componients

    private void PlayAndPauseButton_Click(object sender, RoutedEventArgs e)
    {
        TogglePlayAndPause();
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleStop();
    }

    private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var i = LevelSelector.SelectedIndex;
        if (i < 0)
            return;
        selectedDifficulty = i;
        if (isLoading)
        {
            suppressLevelTextChange = true;
            try
            {
                LevelTextBox.Text = SimaiProcess.levels[selectedDifficulty];
            }
            finally
            {
                suppressLevelTextChange = false;
            }
            return;
        }
        ClearStoppedNotePreview();
        SetRawFumenText(SimaiProcess.fumens[i]);
        suppressLevelTextChange = true;
        try
        {
            LevelTextBox.Text = SimaiProcess.levels[selectedDifficulty];
        }
        finally
        {
            suppressLevelTextChange = false;
        }
        SetSavedState(true);
        chartChangeTimer.Stop();
        SimaiProcess.Serialize(GetRawFumenText());
        chartParsePending = false;
        DrawWave();
        SyntaxCheck(false);
        // A paused timeline preview keeps the previously loaded chart in View and
        // only receives Seek afterwards, so the difficulty swap has to force a
        // reload or View would keep showing the difficulty we just left.
        QueueNotePreview(chartChanged: true);
        SchedulePreBakeSongDetail(); // Pre-bake the selected difficulty cover after switching difficulty.
    }

    private void LevelTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (isLoading || suppressLevelTextChange)
            return;
        if (selectedDifficulty == -1)
            return;
        if (string.Equals(SimaiProcess.levels[selectedDifficulty], LevelTextBox.Text, StringComparison.Ordinal))
            return;
        SetSavedState(false);
        SimaiProcess.levels[selectedDifficulty] = LevelTextBox.Text;
        InvalidateSongDetailCache(selectedDifficulty);
    }

    private void OffsetTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (isLoading)
            return;
        // An offset being typed is not an offset of zero: dropping to zero here
        // moved the whole chart against its audio for as long as the box was
        // half-written.
        if (!SimaiProcess.TryReadOffset(OffsetTextBox.Text, out var offset))
        {
            SetSavedState(false);
            return;
        }
        if (Math.Abs(SimaiProcess.first - offset) < 0.000001f)
            return;
        SetSavedState(false);
        SimaiProcess.first = offset;
        SimaiProcess.Serialize(GetRawFumenText());
        DrawWave();
    }

    private void OffsetTextBox_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!SimaiProcess.TryReadOffset(OffsetTextBox.Text, out var offset))
            offset = SimaiProcess.first;
        offset += e.Delta > 0 ? 0.01f : -0.01f;
        OffsetTextBox.Text = offset.ToString(CultureInfo.InvariantCulture);
    }

    private void FollowPlayCheck_Click(object sender, RoutedEventArgs e)
    {
        FumenContent.Focus();
    }

    private void VisualChartEditorCheck_Click(object sender, RoutedEventArgs e)
    {
        if (editorSetting == null)
            return;

        editorSetting.EnableVisualChartEditor = VisualChartEditorCheck.IsChecked == true;
        SaveEditorSetting();
        SendDisplaySettings();
        FumenContent.Focus();
    }

    private void Op_Button_Click(object sender, RoutedEventArgs e)
    {
        TogglePlayAndStop(PlayMethod.Op);
    }

    private void SettingLabel_MouseUp(object sender, MouseButtonEventArgs e)
    {
        // A single click on Settings also opens the settings page.
        var esp = new EditorSettingPanel();
        esp.Owner = this;
        esp.ShowDialog();
    }

    #endregion

    #region RichTextbox events

    private void FumenContent_SelectionChanged(object? sender, EventArgs e)
    {
        if (chartParsePending)
            return;
        RefreshSyntaxValidationSlot();
        if (Bass.BASS_ChannelIsActive(bgmStream) == BASSActive.BASS_ACTIVE_PLAYING && (bool)FollowPlayCheck.IsChecked!)
            return;
        var time = GetCaretTimingTime();
        NoteNowText.Content = (Math.Abs(time) - Math.Floor(Math.Abs(time)))
            .ToString(".0000", System.Globalization.CultureInfo.InvariantCulture);

        // Change progress only for Ctrl plus left-click or an arrow key; other Ctrl combinations do not affect it.
        if (Keyboard.Modifiers == ModifierKeys.Control && (
                Mouse.LeftButton == MouseButtonState.Pressed ||
                Keyboard.IsKeyDown(Key.Left) ||
                Keyboard.IsKeyDown(Key.Right) ||
                Keyboard.IsKeyDown(Key.Up) ||
                Keyboard.IsKeyDown(Key.Down)
            ))
        {
            if (Bass.BASS_ChannelIsActive(bgmStream) == BASSActive.BASS_ACTIVE_PLAYING)
                TogglePause();
            SetBgmPosition(time);
        }

        //Console.WriteLine("SelectionChanged");
        SimaiProcess.ClearNoteListPlayedState();
        var ghostChanged = Math.Abs(ghostCusorPositionTime - time) > 0.0001d;
        ghostCusorPositionTime = (float)time;
        if (!isPlaying && ghostChanged)
            DrawWave();
        // Drag-selection is an editor operation, not a View preview request. Sending
        // the selected slide here renders a stray blue path while the mouse is down.
        if (FumenContent.SelectionLength == 0)
            QueueNotePreview();
    }

    private void FumenContent_TextChanged(object? sender, EventArgs e)
    {
        if (isLoading) return;
        PetStatusClient.Notify("running", "Charting...");
        petTypingTimer.Stop();
        petTypingTimer.Start();
        SetSavedState(false);
        if (GetRawFumenText() == "")
        {
            chartParsePending = false;
            ClearBasicParseErrors();
            return;
        }
        chartParsePending = true;
        QueueImmediateWaveRefresh();
        chartChangeTimer.Stop();
        chartChangeTimer.Start();
        QueueNotePreview(chartChanged: true);
    }


    private void FumenContent_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control &&
            e.Key is Key.Up or Key.Down)
        {
            var current = FumenContent.Document.GetLineByOffset(FumenContent.CaretOffset);
            var targetNumber = current.LineNumber + (e.Key == Key.Up ? -1 : 1);
            if (targetNumber >= 1 && targetNumber <= FumenContent.Document.LineCount)
            {
                var target = FumenContent.Document.GetLineByNumber(targetNumber);
                var column = FumenContent.CaretOffset - current.Offset;
                FumenContent.CaretOffset = target.Offset + Math.Min(column, target.Length);
                FumenContent.Select(FumenContent.CaretOffset, 0);
                FumenContent.ScrollToLine(targetNumber);
            }
            e.Handled = true;
            return;
        }

        // Toggle overwrite mode when Insert is pressed without modifiers.
        if (e.Key == Key.Insert && Keyboard.Modifiers == ModifierKeys.None)
        {
            SwitchFumenOverwriteMode();
            e.Handled = true;
        }
    }

    #endregion

    #region Wave displayer

    private void WaveViewZoomIn_Click(object sender, RoutedEventArgs e)
    {
        if (deltatime > 1)
            deltatime -= 1;
        DrawWave();
        FumenContent.Focus();
    }

    private void WaveViewZoomOut_Click(object sender, RoutedEventArgs e)
    {
        if (deltatime < 10)
            deltatime += 1;
        DrawWave();
        FumenContent.Focus();
    }

    private void MusicWave_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        ScrollWave(-e.Delta, syncCaret: true);
    }

    private void MusicWave_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (isPlaying)
            TogglePause();
        waveScrubActive = true;
        MusicWave.CaptureMouse();
        lastMousePointX = e.GetPosition(MusicWave).X;
    }

    private void MusicWave_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        waveScrubActive = false;
        MusicWave.ReleaseMouseCapture();
        MediaTimelinePanel.SyncPlayhead(GetTimelinePosition());
        if (GetTimelinePosition() >= 0d && GetTimelinePosition() <= songLength)
        {
            if (FollowPlayCheck.IsChecked == true)
                fumenEditor.Focus();
            SeekTextFromTime();
        }
    }

    private void MusicWave_LostMouseCapture(object sender, MouseEventArgs e)
    {
        waveScrubActive = false;
    }

    private void MusicWave_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            var delta = e.GetPosition(MusicWave).X - lastMousePointX;
            lastMousePointX = e.GetPosition(MusicWave).X;
            ScrollWave(-delta);
        }

        lastMousePointX = e.GetPosition(MusicWave).X;
    }

    private void MusicWave_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Keep the visible duration symmetric around the playhead. Width changes
        // therefore zoom the existing time window from either edge.
        QueueWaveResize();
    }


    #endregion

    
}
