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
    private readonly DispatcherTimer externalStateTimer = new();
    private readonly DispatcherTimer speechTimer = new() { Interval = TimeSpan.FromSeconds(5) };
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
    private bool externalStateActive;
    private int externalStatePriority;
    private DateTime externalStateLockUntil;
    private PetControlRequest? deferredPetRequest;
    private string currentStatus = string.Empty;
    private string currentSpeech = string.Empty;
    private DateTime lastSpeechAt;

    public MainWindow()
    {
        InitializeComponent();
        LaunchMenuItem.Header = L("LaunchEditor");
        OpenFolderMenuItem.Header = L("OpenLauncherFolder");
        ExitMenuItem.Header = L("ExitPet");
        UpdateStatusLayout(L("Ready"));
        idleTimer.Tick += IdleTimer_Tick;
        externalStateTimer.Tick += ExternalStateTimer_Tick;
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

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        animator = new PetAnimator(PetPreviousImage, PetImage);
        var petReady = LoadPet();
        if (petReady)
            animator.Play(PetAnimation.Jumping, false, () => animator.Play(PetAnimation.Idle, true));
        StartPetControlServer();
        if (petReady)
            Present(L("Ready"), L("LaunchHint"));
        idleTimer.Start();
    }

    private bool LoadPet()
    {
        var petRoot = Path.Combine(AppContext.BaseDirectory, "Pets", launcher.Settings.Pet);
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

    private async void Launch_Click(object sender, RoutedEventArgs e)
    {
        if (launching)
            return;
        launching = true;
        launchCancellation = new CancellationTokenSource();
        animator?.Play(PetAnimation.Running, true);
        try
        {
            var progress = new Progress<string>(status => PresentStatus(status));
            await launcher.LaunchAsync(progress, launchCancellation.Token);
            PresentStatus(L("Ready"));
            animator?.Play(PetAnimation.Waving, false, () => animator.Play(PetAnimation.Idle, true));
        }
        catch (OperationCanceledException)
        {
            PresentStatus(L("Ready"));
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
        animator.Play(PetAnimation.Waving, false, () => animator.Play(PetAnimation.Idle, true));
        Present(L("Ready"), random.Next(3) switch
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
        if (animator?.HasAtlas != true)
            return;

        var action = request.Action.Trim().ToLowerInvariant();
        var priority = GetPetStatePriority(action);
        if (DateTime.UtcNow < externalStateLockUntil && priority < externalStatePriority)
        {
            deferredPetRequest = request;
            ScheduleDeferredPetState();
            return;
        }

        deferredPetRequest = null;
        externalStatePriority = priority;
        externalStateLockUntil = action switch
        {
            // The failed row needs about 1.9 s to reach its seated frame. Keep that
            // final frame visible for another four seconds before accepting a lower
            // priority state.
            "failed" or "error" => DateTime.UtcNow.AddMilliseconds(5500),
            "running" or "working" or "chart-agent" => DateTime.UtcNow.AddMilliseconds(900),
            "review" or "organize" or "star-combo" => DateTime.UtcNow.AddMilliseconds(500),
            _ => DateTime.UtcNow
        };

        var message = LocalizeStatusMessage(request.Message);
        externalStateActive = action is not ("idle" or "wave" or "success" or "jump" or "launch");

        switch (action)
        {
            case "idle":
                PresentStatus(L("Ready"));
                animator.Play(PetAnimation.Idle, true);
                break;
            case "running":
            case "working":
            case "chart-agent":
                var runningMessage = string.IsNullOrWhiteSpace(message) ? L("OrganizingIdeas") : message;
                var sourceMessage = request.Message ?? string.Empty;
                var runningStatus = sourceMessage.Contains("Recording", StringComparison.OrdinalIgnoreCase) ||
                                    sourceMessage.Contains("录制", StringComparison.Ordinal)
                    ? L("Recording")
                    : sourceMessage.Contains("Playing", StringComparison.OrdinalIgnoreCase) ||
                      sourceMessage.Contains("播放", StringComparison.Ordinal)
                        ? L("Playing")
                        : L("Charting");
                PresentStatus(runningStatus);
                animator.Play(PetAnimation.Running, true);
                break;
            case "review":
            case "organize":
                PresentStatus(L("Reviewing"));
                animator.Play(PetAnimation.Review, true);
                break;
            case "waiting":
            case "ask":
                PresentStatus(L("Waiting"));
                animator.Play(PetAnimation.Waiting, true);
                break;
            case "failed":
            case "error":
                Present(L("SyntaxError"), string.IsNullOrWhiteSpace(message) ? L("NeedsAttention") : message);
                animator.Play(PetAnimation.Failed, false, () => animator.Play(PetAnimation.Idle, true));
                break;
            case "wave":
            case "success":
                Present(L("Completed"), string.IsNullOrWhiteSpace(message) ? L("Complete") : message);
                animator.Play(PetAnimation.Waving, false, () => animator.Play(PetAnimation.Idle, true));
                break;
            case "jump":
            case "launch":
                animator.Play(PetAnimation.Jumping, false, () => animator.Play(PetAnimation.Idle, true));
                break;
            case "left":
                animator.Play(PetAnimation.RunningLeft, false, () => animator.Play(PetAnimation.Idle, true));
                break;
            case "right":
                animator.Play(PetAnimation.RunningRight, false, () => animator.Play(PetAnimation.Idle, true));
                break;
            case "look":
                animator.ShowLookDirection(request.Angle ?? 0, 900, () => animator.Play(PetAnimation.Idle, true));
                break;
            case "star-combo":
                Present(L("Reviewing"), string.IsNullOrWhiteSpace(message) ? L("CheckingStarCombo") : message);
                animator.Play(PetAnimation.Review, false, () => animator.Play(PetAnimation.Idle, true));
                break;
        }
    }

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

    private static int GetPetStatePriority(string action) => action switch
    {
        "failed" or "error" => 100,
        "running" or "working" or "chart-agent" => 60,
        "review" or "organize" or "star-combo" => 50,
        "waiting" or "ask" => 40,
        "left" or "right" or "look" => 30,
        _ => 0
    };

    private void ScheduleDeferredPetState()
    {
        var remaining = externalStateLockUntil - DateTime.UtcNow;
        externalStateTimer.Stop();
        externalStateTimer.Interval = remaining > TimeSpan.FromMilliseconds(50)
            ? remaining
            : TimeSpan.FromMilliseconds(50);
        externalStateTimer.Start();
    }

    private void ExternalStateTimer_Tick(object? sender, EventArgs e)
    {
        externalStateTimer.Stop();
        var request = deferredPetRequest;
        deferredPetRequest = null;
        if (request != null)
            HandlePetControlRequest(request);
    }

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
        if (launching || externalStateActive || animator?.HasAtlas != true)
            return;
        if (random.Next(3) == 0)
            Present(L("Ready"), IdleMessages[random.Next(IdleMessages.Length)]);
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
        if (e.ClickCount >= 2)
        {
            Launch_Click(this, new RoutedEventArgs());
            return;
        }
        DragMove();
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
        launchCancellation?.Cancel();
        launchCancellation?.Dispose();
        idleTimer.Stop();
        externalStateTimer.Stop();
        speechTimer.Stop();
        petControlServer?.Dispose();
        animator?.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
