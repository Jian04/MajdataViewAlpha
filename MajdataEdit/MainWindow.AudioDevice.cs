using System.Windows;
using System.Windows.Interop;
using Un4seen.Bass;

namespace MajdataEdit;

public partial class MainWindow
{
    private bool audioOutputUnavailable;
    private int editorAudioDevice;
    private void InitializeEditorAudio(IntPtr handle)
    {
        if (Bass.BASS_Init(-1, 44100, BASSInit.BASS_DEVICE_CPSPEAKERS, handle))
        { editorAudioDevice = Bass.BASS_GetDevice(); return; }
        var outputError = Bass.BASS_ErrorGetCode();
        if (!Bass.BASS_Init(0, 44100, BASSInit.BASS_DEVICE_DEFAULT, handle) &&
            !Bass.BASS_SetDevice(0))
            throw new InvalidOperationException($"音频系统初始化失败：{outputError} / {Bass.BASS_ErrorGetCode()}");
        audioOutputUnavailable = true;
        editorAudioDevice = Bass.BASS_GetDevice();
        TraceStartup($"Audio output unavailable ({outputError}); using decode-only device.");
    }

    private bool RestoreAudioOutput()
    {
        if (!audioOutputUnavailable) return true;
        if (!Bass.BASS_Init(-1, 44100, BASSInit.BASS_DEVICE_CPSPEAKERS, new WindowInteropHelper(this).Handle))
        {
            Bass.BASS_SetDevice(0);
            MessageBox.Show(this, "音频输出设备当前不可用，可能被其他软件独占。仍可编辑谱面、查看波形和拖动预览；释放声卡后再次点击播放即可重试。", "音频输出不可用");
            return false;
        }
        audioOutputUnavailable = false;
        editorAudioDevice = Bass.BASS_GetDevice();
        var position = selectedDifficulty >= 0 ? GetTimelinePosition() : 0;
        var tempo = (GetPlaybackSpeed() - 1f) * 100f;
        if (bgmStream > 0) Bass.BASS_StreamFree(bgmStream);
        bgmStream = -1024;
        ReadSoundEffect();
        ReloadCurrentTrack(position, editorSetting?.Default_BGM_Level ?? 1f, tempo);
        return true;
    }
}
