using Assets.Scripts.Types;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

public class ScreenRecorder : MonoBehaviour
{
    public float CutoffTime;
    public int FrameRate = 30;
    // Per-pass output and temporary layer masking are restored after capture.
    public string OutputFileName = "out.mp4";
    public bool RevealOutput = true;
    public bool ShowSongDetail = true;
    public ScreenEffectController ScreenEffects;
    public MediaTimelineController MediaTimeline;
    public GameObject APObj;
    JsonDataLoader loader;
    ObjectCounter counter;
    AudioTimeProvider timeProvider;
    BGManager bgManager;

    private bool isStarting;
    private bool isRecording;
    private bool captureActive;
    private bool highLoadWarningShown;
    private float scheduledStopAt = -1f;
    private string recordFailureMessage;
    private bool shouldRestoreResolution;
    private Process encoderProcess;
    private int restoreWidth;
    private int restoreHeight;
    private bool restoreFullscreen;
    private static readonly WaitForEndOfFrame WaitForFrameEnd = new();
    public bool IsRecording => isStarting || isRecording || captureActive;

    // Start is called before the first frame update
    private void Start()
    {
        loader = FindAnyObjectByType<JsonDataLoader>();
        counter = FindAnyObjectByType<ObjectCounter>();
        timeProvider = FindAnyObjectByType<AudioTimeProvider>();
        bgManager = FindAnyObjectByType<BGManager>();
    }

    // Update is called once per frame
    private void Update()
    {
        if(isRecording)
        {
            if (loader == null)
                loader = FindAnyObjectByType<JsonDataLoader>();
            if (loader == null)
                return;

            if (loader.State is not (NoteLoaderStatus.Idle or NoteLoaderStatus.Finished))
                return;

            // Once AP is revealed, its animation-relative deadline is authoritative.
            // Otherwise use the chart-relative cutoff supplied with the Record request.
            var stopAt = scheduledStopAt > 0f ? scheduledStopAt : CutoffTime;
            if (stopAt > 0f && timeProvider != null && timeProvider.AudioTime >= stopAt)
                isRecording = false;
        }
    }

    public bool PrepareRecording()
    {
        if (isStarting || isRecording || captureActive)
            return false;
        isStarting = true;
        highLoadWarningShown = false;
        return true;
    }

    public void StartRecording(string maidata_path)
    {
        if (!isStarting || isRecording)
            return;
        if (string.IsNullOrWhiteSpace(maidata_path))
        {
            AppendError(ViewLocalization.Text("RecordingNoChart"));
            CleanupRecordingState();
            return;
        }

        recordFailureMessage = null;
        scheduledStopAt = -1f;
        captureActive = true;
        StartCoroutine(CaptureScreen(maidata_path));
    }

    public void StopRecording()
    {
        print("stop recording");
        var cancelledStartup = isStarting && !isRecording;
        var wasRecording = isRecording;
        isStarting = false;
        isRecording = false;
        if (timeProvider != null)
            timeProvider.isStart = false;
        if (cancelledStartup)
        {
            if (captureActive && encoderProcess != null)
                TryTerminateEncoder(encoderProcess);
            else
                CleanupRecordingState();
        }
        else if (captureActive && !wasRecording && encoderProcess != null)
            TryTerminateEncoder(encoderProcess);
    }

    /// <summary>Stops after the recording clock advances by seconds, used after the AP presentation.</summary>
    public void StopAfter(float seconds)
    {
        if (isRecording && timeProvider != null)
            scheduledStopAt = timeProvider.AudioTime + seconds;
    }

    public void RememberResolutionForRestore()
    {
        shouldRestoreResolution = true;
        restoreWidth = Screen.width;
        restoreHeight = Screen.height;
        restoreFullscreen = Screen.fullScreen;
    }

    private IEnumerator CaptureScreen(string maidata_path)
    {
        if (!isStarting)
        {
            CleanupRecordingState();
            yield break;
        }
        if (Screen.width % 2 != 0 || Screen.height % 2 != 0)
        {
            SetError(ViewLocalization.Text("RecordingEvenResolution", Screen.width, Screen.height));
            CleanupRecordingState();
            yield break;
        }
        WarnIfHighCaptureLoad(Screen.width, Screen.height);

        var outputName = string.IsNullOrWhiteSpace(OutputFileName) ? "out.mp4" : OutputFileName;
        var captureRect = default(Rect);
        Texture2D texture = null;
        byte[] data = null;
        NamedPipeServerStream pipeServer;
        ProcessStartInfo startinfo;
        string arguments;
        try
        {
            const string wavpath = "out.wav";
            var outputfile = outputName;
            if (File.Exists(maidata_path + "\\" + outputName))
                File.Delete(maidata_path + "\\" + outputName);
            var audioTempoFilter = BuildAudioTempoFilter(timeProvider?.CurrentSpeed ?? 1f);
            arguments = string.Format(
                CultureInfo.InvariantCulture,
                File.ReadAllText(Application.streamingAssetsPath + "\\ffarguments.txt").Trim(),
                Screen.width, Screen.height,
                wavpath, outputfile,
                int.MaxValue,
                FrameRate,
                audioTempoFilter
            );
            startinfo = new ProcessStartInfo(Application.streamingAssetsPath + "\\ffmpeg.exe", arguments);
            startinfo.UseShellExecute = false;
            startinfo.CreateNoWindow = true;
            startinfo.WorkingDirectory = maidata_path;
            startinfo.EnvironmentVariables.Add("FFREPORT", "file=out.log:level=24");
            pipeServer = new NamedPipeServerStream("majdataRec", PipeDirection.Out);
        }
        catch (System.Exception ex)
        {
            AppendError(ViewLocalization.Text("FfmpegStartFailed", ex.Message));
            CleanupRecordingState();
            yield break;
        }

        using (pipeServer)
        {
            print(arguments);
            Process p;
            try
            {
                p = Process.Start(startinfo);
            }
            catch (System.Exception ex)
            {
                AppendError(ViewLocalization.Text("FfmpegStartFailed", ex.Message));
                CleanupRecordingState();
                yield break;
            }

            if (p == null)
            {
                AppendError(ViewLocalization.Text("FfmpegStartFailed", string.Empty));
                CleanupRecordingState();
                yield break;
            }
            encoderProcess = p;

            Task waitForConnection = pipeServer.WaitForConnectionAsync();
            while (!waitForConnection.IsCompleted)
            {
                if (!isStarting)
                {
                    TryTerminateEncoder(p);
                    CleanupRecordingState();
                    yield break;
                }
                if (p.HasExited)
                {
                    AppendError(ViewLocalization.Text("FfmpegPipeExit", p.ExitCode));
                    CleanupRecordingState();
                    yield break;
                }

                yield return null;
            }

            if (waitForConnection.IsFaulted)
            {
                AppendError(ViewLocalization.Text("NamedPipeFailed"));
                TryTerminateEncoder(p);
                CleanupRecordingState();
                yield break;
            }

            if (!isStarting)
            {
                TryTerminateEncoder(p);
                CleanupRecordingState();
                yield break;
            }

            var videoWarmupDeadline = Time.realtimeSinceStartup + 15f;
            while (bgManager != null && !bgManager.IsPreparedForRecording &&
                   Time.realtimeSinceStartup < videoWarmupDeadline)
            {
                if (!isStarting)
                {
                    TryTerminateEncoder(p);
                    CleanupRecordingState();
                    yield break;
                }
                yield return null;
            }
            if (bgManager != null && !bgManager.IsPreparedForRecording)
            {
                AppendError(ViewLocalization.Text("PvPrewarmFailed"));
                TryTerminateEncoder(p);
                CleanupRecordingState();
                yield break;
            }
            var mediaWarmupDeadline = Time.realtimeSinceStartup + 15f;
            while (MediaTimeline != null && !MediaTimeline.IsPrepared &&
                   Time.realtimeSinceStartup < mediaWarmupDeadline)
            {
                if (!isStarting)
                {
                    TryTerminateEncoder(p);
                    CleanupRecordingState();
                    yield break;
                }
                yield return null;
            }
            if (MediaTimeline != null && !MediaTimeline.IsPrepared)
                UnityEngine.Debug.LogWarning("[MediaTimeline] Media did not finish preparing before capture.");
            if (MediaTimeline != null && !string.IsNullOrEmpty(MediaTimeline.PreparationError))
            {
                AppendError(ViewLocalization.Text(
                    "MediaPrewarmFailed", MediaTimeline.PreparationError));
                TryTerminateEncoder(p);
                CleanupRecordingState();
                yield break;
            }

            // Warm only materials used by the chart and screen effects. Global
            // Shader.WarmupAllShaders can compile unrelated variants and cause a
            // large CPU/VRAM spike immediately before high-resolution capture.
            loader?.WarmupRenderingMaterials();
            ScreenEffects?.PrepareForRecording();
            // Drain material compilation and full-resolution post-effect allocation before
            // starting AudioTime or sending the first frame to ffmpeg.
            yield return WaitForFrameEnd;
            if (!isStarting)
            {
                TryTerminateEncoder(p);
                CleanupRecordingState();
                yield break;
            }
            var effectWarmupDeadline = Time.realtimeSinceStartup + 5f;
            while (ScreenEffects != null && !ScreenEffects.IsPreparedForRecording &&
                   Time.realtimeSinceStartup < effectWarmupDeadline)
            {
                yield return WaitForFrameEnd;
            }
            if (!isStarting)
            {
                TryTerminateEncoder(p);
                CleanupRecordingState();
                yield break;
            }
            if (ScreenEffects != null && !ScreenEffects.IsPreparedForRecording)
                UnityEngine.Debug.LogWarning("[RenderWarmup] Screen effects did not finish before capture.");

            captureRect = new Rect(0, 0, Screen.width, Screen.height);
            // The final composite is opaque. RGB24 cuts capture memory and pipe
            // bandwidth by 25% compared with the previous unused alpha channel.
            texture = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            data = new byte[Screen.width * Screen.height * 3];
            timeProvider?.BeginRecordingCapture(ShowSongDetail ? 5f : 0f);
            MediaTimeline?.SetPlaybackActive(true);
            if (ShowSongDetail)
                bgManager?.PlaySongDetail(-5f, timeProvider?.CurrentSpeed ?? 1f);
            else
                bgManager?.HideSongDetail();
            isStarting = false;
            isRecording = true;
            using (var bw = new BinaryWriter(pipeServer))
            {
                do
                {
                    yield return WaitForFrameEnd;
                    // A window resize changes frame byte count from ffmpeg's expected value and
                    // breaks the pipe. Stop cleanly with a readable reason as soon as it changes.
                    if (Screen.width != (int)captureRect.width || Screen.height != (int)captureRect.height)
                    {
                        recordFailureMessage = ViewLocalization.Text(
                            "RecordingResize",
                            (int)captureRect.width,
                            (int)captureRect.height,
                            Screen.width,
                            Screen.height);
                        AppendError(recordFailureMessage);
                        isRecording = false;
                        break;
                    }
                    try
                    {
                        texture.ReadPixels(captureRect, 0, 0, false);
                        texture.GetRawTextureData<byte>().CopyTo(data);

                        bw.Write(data, 0, data.Length);
                    }
                    catch (System.Exception ex)
                    {
                        if (ex is System.IO.IOException && p.HasExited && p.ExitCode == 0)
                        {
                            // A normal encoder shutdown can race the final capture frame.
                            isRecording = false;
                        }
                        else
                        {
                            recordFailureMessage = ViewLocalization.Text(
                                "RecordingFailedAt",
                                timeProvider?.AudioTime ?? 0f,
                                ex.GetType().Name,
                                ex.Message);
                            AppendError(recordFailureMessage);
                            UnityEngine.Debug.LogException(ex);
                            isRecording = false;
                        }
                    }
                } while (
                    pipeServer.IsConnected &&
                    isRecording &&
                    !p.HasExited
                );
            }

            var finalizeDeadline = Time.realtimeSinceStartup + 15f;
            while (!p.HasExited &&
                   Time.realtimeSinceStartup < finalizeDeadline)
                yield return null;
            if (!p.HasExited)
            {
                recordFailureMessage =
                    ViewLocalization.Text("FfmpegFinalizeTimeout");
                AppendError(recordFailureMessage);
                TryTerminateEncoder(p);
                var terminateDeadline = Time.realtimeSinceStartup + 2f;
                while (!p.HasExited &&
                       Time.realtimeSinceStartup < terminateDeadline)
                    yield return null;
            }

            if (!string.IsNullOrEmpty(recordFailureMessage))
            {
                AppendError(ViewLocalization.Text("RecordingAborted"));
            }
            else if (p.HasExited &&
                     File.Exists(maidata_path + "/" + outputName) &&
                     p.ExitCode == 0)
            {
                AppendStatus(ViewLocalization.Text(
                    "RecordingSuccess",
                    maidata_path + "\\" + outputName,
                    p.ExitCode));
                if (RevealOutput)
                    Process.Start("explorer", "/select,\"" + maidata_path + "\\" + outputName + "\"");
            }
            else
            {
                AppendError(ViewLocalization.Text(
                    "FfmpegExited",
                    p.HasExited ? p.ExitCode : -1));
            }
            encoderProcess = null;
            p.Dispose();
        }

        Destroy(texture);

        CleanupRecordingState();
    }

    private static bool IsCaptureLoadSafe(int width, int height, int frameRate)
    {
        var maxLongSide = frameRate >= 120 ? 1920 : 2560;
        var maxShortSide = frameRate >= 120 ? 1080 : 1440;
        return Mathf.Max(width, height) <= maxLongSide &&
               Mathf.Min(width, height) <= maxShortSide;
    }

    public void WarnIfHighCaptureLoad(int width, int height)
    {
        if (highLoadWarningShown || IsCaptureLoadSafe(width, height, FrameRate))
            return;
        highLoadWarningShown = true;
        AppendStatus(ViewLocalization.Text(
            "RecordingResolutionHighWarning", width, height, FrameRate));
    }

    private static string BuildAudioTempoFilter(float speed)
    {
        var remaining = Mathf.Clamp(speed, 0.01f, 100f);
        // Avoid passing already mixed clock transients through FFmpeg's overlap-add
        // tempo processor at unity speed. This keeps 1x exports sample-transparent.
        if (Mathf.Approximately(remaining, 1f))
            return "anull";

        var stages = new List<string>();
        while (remaining < 0.5f)
        {
            stages.Add("atempo=0.5");
            remaining /= 0.5f;
        }
        while (remaining > 2f)
        {
            stages.Add("atempo=2");
            remaining /= 2f;
        }
        if (stages.Count == 0 || !Mathf.Approximately(remaining, 1f))
            stages.Add("atempo=" + remaining.ToString("0.######", CultureInfo.InvariantCulture));
        return string.Join(",", stages);
    }

    private static void TryTerminateEncoder(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill();
        }
        catch
        {
            // The process may have exited between HasExited and Kill.
        }
    }

    private static void SetError(string message)
    {
        var errorText = GameObject.Find("ErrText")?.GetComponent<Text>();
        if (errorText != null)
            errorText.text = message;
        else
            UnityEngine.Debug.LogError(message);
    }

    private static void AppendStatus(string message)
    {
        var errorText = GameObject.Find("ErrText")?.GetComponent<Text>();
        if (errorText != null)
            errorText.text += message;
        else
            UnityEngine.Debug.Log(message);
    }

    private static void AppendError(string message)
    {
        var errorText = GameObject.Find("ErrText")?.GetComponent<Text>();
        if (errorText != null)
            errorText.text += message;
        else
            UnityEngine.Debug.LogError(message);
    }

    private void CleanupRecordingState()
    {
        isStarting = false;
        isRecording = false;
        captureActive = false;
        var process = encoderProcess;
        encoderProcess = null;
        if (process != null)
        {
            TryTerminateEncoder(process);
            process.Dispose();
        }
        if (timeProvider != null)
        {
            timeProvider.isStart = false;
            timeProvider.RestoreFrameRate();
        }

        Time.captureFramerate = 0;
        Time.timeScale = 1f;
        scheduledStopAt = -1f;

        OutputFileName = "out.mp4";

        if (shouldRestoreResolution)
        {
            Screen.SetResolution(Mathf.Max(320, restoreWidth), Mathf.Max(320, restoreHeight), restoreFullscreen);
            shouldRestoreResolution = false;
        }

        if (bgManager != null)
            bgManager.PauseVideo();
        MediaTimeline?.SetPlaybackActive(false);
    }

    private void OnDestroy()
    {
        var process = encoderProcess;
        encoderProcess = null;
        if (process == null)
            return;
        TryTerminateEncoder(process);
        process.Dispose();
    }
}
