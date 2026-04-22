using Assets.Scripts.Types;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

public class ScreenRecorder : MonoBehaviour
{
    public float CutoffTime;
    public GameObject APObj;
    JsonDataLoader loader;
    ObjectCounter counter;
    AudioTimeProvider timeProvider;
    BGManager bgManager;

    private bool isRecording;
    private string recordFailureMessage;

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
            if (loader.State is not (NoteLoaderStatus.Idle or NoteLoaderStatus.Finished))
                return;

            if (CutoffTime > 0f && timeProvider != null && timeProvider.AudioTime >= CutoffTime)
                isRecording = false;
        }
    }

    public void StartRecording(string maidata_path)
    {
        if (isRecording)
            return;

        recordFailureMessage = null;
        StartCoroutine(CaptureScreen(maidata_path));
    }

    public void StopRecording()
    {
        print("stop recording");
        isRecording = false;
    }

    private IEnumerator CaptureScreen(string maidata_path)
    {
        if (Screen.width % 2 != 0 || Screen.height % 2 != 0)
        {
            GameObject.Find("ErrText").GetComponent<Text>().text =
                "无法开始编码，因为分辨率宽度或高度不是偶数。\nCan not start render because the width/height is not even.\n当前分辨率:" +
                Screen.width + "x" + Screen.height + "\n";
            yield break;
        }

        if (File.Exists(maidata_path + "\\out.mp4"))
            File.Delete(maidata_path + "\\out.mp4");

        byte[] data;
        var captureRect = new Rect(0, 0, Screen.width, Screen.height);
        var texture = new Texture2D(Screen.width, Screen.height, TextureFormat.RGBA32, false);
        using (var pipeServer = new NamedPipeServerStream("majdataRec", PipeDirection.Out))
        {
            const string wavpath = "out.wav";
            const string outputfile = "out.mp4";

            var arguments = string.Format(
                File.ReadAllText(Application.streamingAssetsPath + "\\ffarguments.txt").Trim(),
                Screen.width, Screen.height,
                wavpath, outputfile,
                int.MaxValue
            );
            var startinfo = new ProcessStartInfo(Application.streamingAssetsPath + "\\ffmpeg.exe", arguments);
            startinfo.UseShellExecute = false;
            startinfo.CreateNoWindow = true;
            startinfo.WorkingDirectory = maidata_path;
            startinfo.EnvironmentVariables.Add("FFREPORT", "file=out.log:level=24");
            print(arguments);

            Process p;
            try
            {
                p = Process.Start(startinfo);
            }
            catch (System.Exception ex)
            {
                GameObject.Find("ErrText").GetComponent<Text>().text +=
                    "FFmpeg start failed.\n" + ex.Message + "\n";
                CleanupRecordingState();
                yield break;
            }

            if (p == null)
            {
                GameObject.Find("ErrText").GetComponent<Text>().text += "FFmpeg start failed.\n";
                CleanupRecordingState();
                yield break;
            }

            Task waitForConnection = pipeServer.WaitForConnectionAsync();
            while (!waitForConnection.IsCompleted)
            {
                if (p.HasExited)
                {
                    GameObject.Find("ErrText").GetComponent<Text>().text +=
                        "FFmpeg exited before pipe connection.\nExitCode:" + p.ExitCode;
                    CleanupRecordingState();
                    yield break;
                }

                yield return null;
            }

            if (waitForConnection.IsFaulted)
            {
                GameObject.Find("ErrText").GetComponent<Text>().text +=
                    "Named pipe connection failed.\n";
                CleanupRecordingState();
                yield break;
            }

            isRecording = true;
            using (var bw = new BinaryWriter(pipeServer))
            {
                do
                {
                    yield return new WaitForEndOfFrame();
                    try
                    {
                        texture.ReadPixels(captureRect, 0, 0, false);
                        texture.Apply(false, false);
                        data = texture.GetRawTextureData();

                        bw.Write(data, 0, data.Length);
                        bw.Flush();
                    }
                    catch (System.Exception ex)
                    {
                        recordFailureMessage =
                            $"Recording failed at {timeProvider?.AudioTime:0.000}s.\n{ex.GetType().Name}: {ex.Message}\n";
                        GameObject.Find("ErrText").GetComponent<Text>().text += recordFailureMessage;
                        UnityEngine.Debug.LogException(ex);
                        isRecording = false;
                    }
                } while (
                    pipeServer.IsConnected &&
                    isRecording &&
                    !p.HasExited
                );
            }

            while (!p.HasExited)
                yield return null;

            if (!string.IsNullOrEmpty(recordFailureMessage))
            {
                GameObject.Find("ErrText").GetComponent<Text>().text +=
                    "Recording aborted before ffmpeg finished.\n";
            }
            else if (File.Exists(maidata_path + "/out.mp4") && p.ExitCode == 0)
            {
                GameObject.Find("ErrText").GetComponent<Text>().text += "渲染成功，视频生成在" + maidata_path +
                                                                        "\\out.mp4\nRender Successed\nExitCode:" +
                                                                        p.ExitCode;
                Process.Start("explorer", "/select,\"" + maidata_path + "\\out.mp4" + "\"");
            }
            else
            {
                GameObject.Find("ErrText").GetComponent<Text>().text +=
                    "编码器已退出\nFFmpeg Exited.\nExitCode:" + p.ExitCode;
            }
        }

        Destroy(texture);

        CleanupRecordingState();
    }

    private void CleanupRecordingState()
    {
        if (timeProvider != null)
        {
            timeProvider.isStart = false;
            timeProvider.isRecord = false;
        }

        Time.captureFramerate = 0;
        Time.timeScale = 1f;

        if (bgManager != null)
            bgManager.PauseVideo();
    }
}
