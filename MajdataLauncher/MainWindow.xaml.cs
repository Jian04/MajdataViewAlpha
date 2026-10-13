using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MajdataLauncher.Models;

namespace MajdataLauncher;

public partial class MainWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private readonly LauncherService launcher = new();
    private readonly DispatcherTimer idleTimer = new() { Interval = TimeSpan.FromSeconds(8) };
    private readonly DispatcherTimer speechTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer expressionTimer = new();
    private readonly DispatcherTimer modeTimer = new();
    private readonly Random random = new();
    private static readonly string[] IdleMessages =
    {
        LauncherLocalization.Text("IdleWaveform"),
        LauncherLocalization.Text("IdleContextMenu"),
        LauncherLocalization.Text("IdleView"),
        LauncherLocalization.Text("IdleUndo"),
        LauncherLocalization.Text("IdleStar"),
        LauncherLocalization.Text("IdleSave")
    };
    private PetAnimator? animator;
    private PetControlServer? petControlServer;
    private CancellationTokenSource? launchCancellation;
    private bool launching;
    private bool editorActive;
    private bool syntaxErrors;
    private bool previewActive;
    private PetMode displayedMode = (PetMode)(-1);
    private enum PetMode { Initial, Editing, SyntaxError, Preview }
    private readonly DateTime[] nextModeGesture = new DateTime[4];
    private string currentStatus = string.Empty;
    private string currentSpeech = string.Empty;
    private DateTime lastSpeechAt;

    public MainWindow()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) Dispatcher.BeginInvoke(new Action(KeepPetTopmost), DispatcherPriority.Loaded);
        };
        LaunchMenuItem.Header = L("LaunchEditor");
        OpenFolderMenuItem.Header = L("OpenLauncherFolder");
        ExitMenuItem.Header = L("ExitPet");
        UpdateStatusLayout(L("Ready"));
        idleTimer.Tick += IdleTimer_Tick;
        expressionTimer.Tick += (_, _) => {
            expressionTimer.Stop();
            if (animator?.IsGesturePlaying == true) { expressionTimer.Interval = TimeSpan.FromSeconds(1); expressionTimer.Start(); }
            else PlayModeGesture();
        };
        modeTimer.Tick += (_, _) => { modeTimer.Stop(); ApplyPetMode(); };
        speechTimer.Tick += (_, _) =>
        {
            speechTimer.Stop();
            SpeechBubbleHost.Visibility = Visibility.Collapsed;
        };
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var style = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, style | WsExNoActivate | WsExToolWindow);
        PositionAboveTaskbar();
    }

    private void KeepPetTopmost()
    {
        if (!IsVisible) return;
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        // Establish the native topmost band without activating or moving the pet.
        if (!SetWindowPos(handle, new IntPtr(-1), 0, 0, 0, 0, 0x0013))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        animator = new PetAnimator(PetPreviousImage, PetImage);
        var petReady = LoadPet();
        if (petReady) PlayStartupAppearance();
        StartPetControlServer();
        StartSessionDiscovery();
        StartDockListener();
        if (petReady)
            Present(L("Ready"), L("LaunchHint"));
        idleTimer.Start();
    }

    private bool LoadPet()
    {
        var petRoot = Path.Combine(AppContext.BaseDirectory, "Pets", launcher.Settings.Pet);
        LoadPetMotion(petRoot);
        var manifestPath = Path.Combine(petRoot, "pet.json");
        var manifest = File.Exists(manifestPath)
            ? JsonSerializer.Deserialize<PetManifest>(File.ReadAllText(manifestPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new PetManifest()
            : new PetManifest();
        var atlasPath = Path.Combine(petRoot, manifest.SpritesheetPath);
        if (manifest.SpriteVersionNumber == 2 && animator!.LoadAtlas(atlasPath))
        {
            PresentStatus(L("Ready"));
            return true;
        }
        Present(L("AssetError"), L("PetAtlasError"));
        return false;
    }

    private void SwitchPet(string petId)
    {
        if (string.IsNullOrWhiteSpace(petId) ||
            !string.Equals(petId, Path.GetFileName(petId), StringComparison.Ordinal) ||
            petId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return;
        var previousPet = launcher.Settings.Pet;
        if (string.Equals(previousPet, petId, StringComparison.OrdinalIgnoreCase))
            return;
        launcher.Settings.Pet = petId;
        if (!LoadPet())
        {
            launcher.Settings.Pet = previousPet;
            LoadPet();
        }
        displayedMode = (PetMode)(-1);
        ApplyPetMode(true);
    }

    private async void Launch_Click(object sender, RoutedEventArgs e)
    {
        if (launching)
            return;
        launching = true;
        launchCancellation = new CancellationTokenSource();
        try
        {
            var progress = new Progress<string>(_ => { });
            await launcher.LaunchAsync(progress, launchCancellation.Token);
            ApplyPetMode(true);
        }
        catch (OperationCanceledException)
        {
            ApplyPetMode(true);
        }
        catch (Exception exception)
        {
            Present(L("LaunchFailed"), exception.Message);
            animator?.Play(PetAnimation.Failed, false, () => animator.Play(PetAnimation.Idle, true));
        }
        finally
        {
            launching = false;
        }
    }

    private void Pet_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || animator?.HasAtlas != true)
            return;
        if (petDragStartScreen is Point start && GetCursorPos(out var released))
            petDragMoved |= Math.Abs(released.X - start.X) + Math.Abs(released.Y - start.Y) > 12;
        petDragStartScreen = null;
        if (docking || startupAppearancePlaying || petDragMoved)
            return;
        PulsePetPress();
        if (DateTime.UtcNow - lastClickExpression < TimeSpan.FromSeconds(petMotion.ClickExpressionCooldownSeconds))
            return;
        lastClickExpression = DateTime.UtcNow;
        expressionTimer.Stop();
        animator.Play(PetAnimation.Waving, false, ResumeAmbientAnimation);
        if (SpeechBubbleHost.Visibility != Visibility.Visible)
            Present(CurrentPetStatus(), random.Next(3) switch
        {
            0 => L("ReadyToChart"),
            1 => L("RememberSave"),
            _ => L("CheckBeforeExport")
        });
    }

    private void Pet_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        // The context menu opens automatically; right-click is not an editor state.
    }

    private void StartPetControlServer()
    {
        try
        {
            petControlServer = new PetControlServer(launcher.Settings.PetControlPort, request =>
                Dispatcher.Invoke(() => HandlePetControlRequest(request)));
            petControlServer.Start();
            PresentStatus(L("Ready"));
        }
        catch (Exception exception)
        {
            Present(L("ConnectionError"), L("PetConnectionUnavailable", exception.Message));
        }
    }

    private void HandlePetControlRequest(PetControlRequest request)
    {
        var action = request.Action.Trim().ToLowerInvariant();
        if (action == "skin")
        {
            SwitchPet(request.Message);
            return;
        }
        switch (action)
        {
            case "editing":
            case "running":
            case "working":
            case "chart-agent":
                editorActive = true;
                break;
            case "syntax-error":
            case "error":
                editorActive = true;
                syntaxErrors = true;
                break;
            case "syntax-ok":
                editorActive = true;
                syntaxErrors = false;
                break;
            case "preview":
                editorActive = true;
                previewActive = true;
                break;
            case "idle":
            case "stop":
            case "waiting":
                editorActive = true;
                previewActive = false;
                break;
        }
        // Report errors immediately; debounce correction so a parser error/ok/error
        // burst does not restart the same error expression.
        modeTimer.Stop();
        if (DesiredPetMode() == displayedMode) return;
        if (DesiredPetMode() == PetMode.SyntaxError)
        {
            ApplyPetMode();
            return;
        }
        modeTimer.Interval = TimeSpan.FromMilliseconds(action == "syntax-ok" ? 650 : 250);
        modeTimer.Start();
    }

    private PetMode DesiredPetMode() => previewActive ? PetMode.Preview : syntaxErrors ? PetMode.SyntaxError :
        editorActive ? PetMode.Editing : PetMode.Initial;

    private void ApplyPetMode(bool force = false)
    {
        if (docking || startupAppearancePlaying || petDragActive || animator?.HasAtlas != true)
            return;
        var mode = DesiredPetMode();
        if (!force && displayedMode == mode)
            return;
        bool modeChanged = displayedMode != mode;
        displayedMode = mode;
        if (modeChanged) PresentStatus(CurrentPetStatus());
        else Present(CurrentPetStatus());
        expressionTimer.Stop();
        switch (mode)
        {
            case PetMode.Initial:
                if (!animator.IsGesturePlaying) animator.Play(PetAnimation.Idle, true);
                break;
            case PetMode.Editing:
                PlayModeGesture();
                break;
            case PetMode.SyntaxError:
                // An error is feedback for this edit, not an ambient idle action.
                if (modeChanged)
                    animator.Play(PetAnimation.Failed, false, ResumeAmbientAnimation);
                break;
            case PetMode.Preview:
                PlayModeGesture();
                break;
        }
    }

    private void PlayModeGesture()
    {
        if(docking || startupAppearancePlaying || petDragActive || animator?.HasAtlas!=true) return;
        var mode=displayedMode;
        if(mode==PetMode.Initial || mode==PetMode.SyntaxError || (int)mode<0) return;
        if (animator.IsGesturePlaying || DateTime.UtcNow < nextModeGesture[(int)mode]) {
            ScheduleModeGesture();
            return;
        }
        var animation=mode==PetMode.SyntaxError ? PetAnimation.Failed :
            mode==PetMode.Preview ? PetAnimation.Review : PetAnimation.Running;
        // Reserve the cooldown immediately too: a dock/drag can deliberately
        // replace this clip, but must not make its next status event replay it.
        nextModeGesture[(int)mode] = DateTime.UtcNow.AddSeconds(GestureCooldown(mode));
        animator.Play(animation,false,() => {
            nextModeGesture[(int)mode] = DateTime.UtcNow.AddSeconds(GestureCooldown(mode));
            if(docking || startupAppearancePlaying || petDragActive) return;
            animator.Play(PetAnimation.Idle,true);
            ScheduleModeGesture();
        });
    }

    private void ResumeAmbientAnimation()
    {
        if (docking || startupAppearancePlaying || petDragActive || animator?.HasAtlas != true) return;
        if (animator?.IsGesturePlaying == true) { ScheduleModeGesture(); return; }
        animator.Play(PetAnimation.Idle, true);
        ScheduleModeGesture();
    }

    private void ScheduleModeGesture()
    {
        expressionTimer.Stop();
        if (displayedMode == PetMode.Initial || displayedMode == PetMode.SyntaxError || (int)displayedMode < 0) return;
        var remaining = nextModeGesture[(int)displayedMode] - DateTime.UtcNow;
        expressionTimer.Interval = animator?.IsGesturePlaying == true
            ? TimeSpan.FromSeconds(1) : remaining > TimeSpan.Zero ? remaining : TimeSpan.FromMilliseconds(1);
        expressionTimer.Start();
    }

    private int GestureCooldown(PetMode mode) => mode == PetMode.Preview ? random.Next(18, 30) : random.Next(20, 36);

    private void Present(string status, string? speech = null)
    {
        if (!string.Equals(currentStatus, status, StringComparison.Ordinal))
        {
            currentStatus = status;
            UpdateStatusLayout(status);
        }

        if (string.IsNullOrWhiteSpace(speech))
            return;

        var now = DateTime.UtcNow;
        if (string.Equals(currentSpeech, speech, StringComparison.Ordinal) &&
            now - lastSpeechAt < TimeSpan.FromSeconds(2))
            return;

        currentSpeech = speech;
        lastSpeechAt = now;
        SpeechText.Text = speech;
        SpeechBubbleHost.Visibility = Visibility.Visible;
        speechTimer.Stop();
        speechTimer.Start();
    }

    private void UpdateStatusLayout(string status)
    {
        const double normalMaxWidth = 126d;
        const double windowInnerWidth = 178d;
        StatusText.Text = status;
        StatusText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var desiredWidth = Math.Ceiling(StatusText.DesiredSize.Width) +
                           StatusBorder.Padding.Left + StatusBorder.Padding.Right;
        StatusBorder.MaxWidth = desiredWidth > normalMaxWidth
            ? Math.Min(desiredWidth, windowInnerWidth)
            : normalMaxWidth;
    }

    private void PresentStatus(string status)
    {
        speechTimer.Stop();
        SpeechBubbleHost.Visibility = Visibility.Collapsed;
        currentSpeech = string.Empty;
        Present(status);
    }

    private string CurrentPetStatus() => displayedMode switch
    {
        PetMode.Editing => L("Charting"),
        PetMode.SyntaxError => L("SyntaxError"),
        PetMode.Preview => L("Previewing"),
        _ => L("Ready")
    };

    private static string L(string key, params object[] args) => LauncherLocalization.Text(key, args);

    private static string LocalizeStatusMessage(string? message) => message switch
    {
        "Playing chart..." or "正在播放谱面……" => L("PlayingChart"),
        "Continuing chart..." or "继续播放谱面……" => L("ContinuingChart"),
        "Playback paused" or "播放已暂停" => L("PlaybackPaused"),
        "Recording chart..." or "正在录制视频……" => L("RecordingChart"),
        "Previewing note" or "正在预览音符" => L("PreviewingNote"),
        "Refreshing display" or "正在刷新显示设置" => L("RefreshingDisplay"),
        "Ready" or "准备就绪" => L("Ready"),
        "View is awake" or "View 已启动" => L("ViewAwake"),
        "Waiting for your cue..." or "正在等待下一步操作……" => L("WaitingCue"),
        "Chart has syntax errors" or "谱面里有需要处理的语法错误" => L("ChartHasSyntaxErrors"),
        "Charting..." or "正在写谱…" => L("ChartingMessage"),
        "Last action undone" or "已撤销上一步操作" => L("ActionUndone"),
        _ => message ?? string.Empty
    };

    private void IdleTimer_Tick(object? sender, EventArgs e)
    {
        if (launching || docking || startupAppearancePlaying || petDragActive ||
            displayedMode != PetMode.Initial || animator?.HasAtlas != true || animator.IsGesturePlaying)
            return;
        if (random.Next(3) == 0)
            Present(CurrentPetStatus(), IdleMessages[random.Next(IdleMessages.Length)]);
        switch (random.Next(10))
        {
            case 0:
                animator.Play(PetAnimation.Waving, false, () => animator.Play(PetAnimation.Idle, true));
                break;
            case 1:
                animator.Play(PetAnimation.Jumping, false, () => animator.Play(PetAnimation.Idle, true));
                break;
            case 2:
                animator.Play(PetAnimation.Review, false, () => animator.Play(PetAnimation.Idle, true));
                break;
            case 3:
            case 4:
                animator.ShowLookDirection(random.Next(16)*22.5,random.Next(700,1400));
                break;
            default:
                animator.Play(PetAnimation.Idle, true);
                break;
        }
    }

    private void PositionAboveTaskbar()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 20;
        Top = area.Bottom - Height;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;
        BeginPetDrag(false);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = AppContext.BaseDirectory,
            UseShellExecute = true
        });
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closed(object? sender, EventArgs e)
    {
        StopSessionDiscovery();
        launchCancellation?.Cancel();
        launchCancellation?.Dispose();
        idleTimer.Stop();
        expressionTimer.Stop();
        modeTimer.Stop();
        motionTimer.Stop();
        StopPetDrag();
        speechTimer.Stop();
        petControlServer?.Dispose();
        animator?.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
