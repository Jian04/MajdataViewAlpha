using System.IO;
using Un4seen.Bass;
using Un4seen.Bass.AddOn.Fx;

namespace MajdataEdit;

// Prepare before replacing the current chart. BASS's current device is thread
// local, so worker decoders must explicitly select the editor's initialized device.
internal sealed class EditorSongAudio : IDisposable
{
    private int stream;
    public short[][] Waveforms { get; }
    public double Length { get; }
    public int Frequency { get; }
    public bool DecodeOnly { get; }
    private EditorSongAudio(int stream, short[][] waveforms, double length, int frequency, bool decodeOnly)
    { this.stream = stream; Waveforms = waveforms; Length = length; Frequency = frequency; DecodeOnly = decodeOnly; }

    public int TakeStream() { var result = stream; stream = 0; return result; }
    public void Dispose() { if (stream != 0) { Bass.BASS_StreamFree(stream); stream = 0; } }

    internal static EditorSongAudio Prepare(string path, int device, bool decodeOnly,
        CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!Bass.BASS_SetDevice(device)) throw Failure(path, "选择音频设备");
        var playback = OpenPlayback(path, decodeOnly, out var actualDecodeOnly);
        try
        {
            var waveform = ReadWaveform(path, cancellation);
            var info = Bass.BASS_ChannelGetInfo(playback) ?? throw Failure(path, "读取音频信息");
            return new EditorSongAudio(playback, waveform.Samples, waveform.Length, info.freq, actualDecodeOnly);
        }
        catch { Bass.BASS_StreamFree(playback); throw; }
    }

    internal static int OpenPlayback(string path, bool decodeOnly, out bool actualDecodeOnly)
    {
        actualDecodeOnly = decodeOnly;
        var decoder = OpenDecoder(path, BASSFlag.BASS_STREAM_PRESCAN);
        var playback = BassFx.BASS_FX_TempoCreate(decoder, BASSFlag.BASS_FX_FREESOURCE |
            (decodeOnly ? BASSFlag.BASS_STREAM_DECODE : BASSFlag.BASS_DEFAULT));
        if (playback != 0) return playback;
        var error = Bass.BASS_ErrorGetCode();
        Bass.BASS_StreamFree(decoder);
        // An output-device failure does not mean a downloaded MP3 failed decoding.
        if (!decodeOnly && error is BASSError.BASS_ERROR_DEVICE or BASSError.BASS_ERROR_INIT or
            BASSError.BASS_ERROR_DRIVER or BASSError.BASS_ERROR_NOTAVAIL)
        {
            if (Bass.BASS_Init(0, 44100, BASSInit.BASS_DEVICE_DEFAULT, IntPtr.Zero) || Bass.BASS_SetDevice(0))
            {
                decoder = OpenDecoder(path, BASSFlag.BASS_STREAM_PRESCAN);
                playback = BassFx.BASS_FX_TempoCreate(decoder, BASSFlag.BASS_FX_FREESOURCE | BASSFlag.BASS_STREAM_DECODE);
                if (playback != 0) { actualDecodeOnly = true; return playback; }
                error = Bass.BASS_ErrorGetCode();
                Bass.BASS_StreamFree(decoder);
            }
        }
        throw Failure(path, "创建播放通道", error);
    }

    internal static (short[][] Samples, double Length) ReadWaveform(string path, CancellationToken cancellation = default)
    {
        var decoder = OpenDecoder(path);
        try
        {
            var length = Bass.BASS_ChannelBytes2Seconds(decoder, Bass.BASS_ChannelGetLength(decoder, BASSMode.BASS_POS_BYTE));
            var levels = new[] { new List<short>(), new List<short>(), new List<short>() };
            var strides = new[] { 20, 50, 100 };
            var samples = new short[16384]; long sampleOffset = 0; int bytes;
            while ((bytes = Bass.BASS_ChannelGetData(decoder, samples, samples.Length * sizeof(short))) > 0)
            {
                cancellation.ThrowIfCancellationRequested();
                var count = bytes / sizeof(short);
                for (var level = 0; level < strides.Length; level++)
                    for (var i = (int)((strides[level] - sampleOffset % strides[level]) % strides[level]); i < count; i += strides[level])
                        levels[level].Add(samples[i]);
                sampleOffset += count;
            }
            if (bytes < 0 && Bass.BASS_ErrorGetCode() != BASSError.BASS_ERROR_ENDED)
                throw Failure(path, "生成波形");
            foreach (var level in levels) level.Add(0);
            return (levels.Select(level => level.ToArray()).ToArray(), length);
        }
        finally { Bass.BASS_StreamFree(decoder); }
    }

    private static int OpenDecoder(string path, BASSFlag extra = BASSFlag.BASS_DEFAULT)
    {
        var decoder = Bass.BASS_StreamCreateFile(path, 0L, 0L, BASSFlag.BASS_STREAM_DECODE | extra);
        if (decoder == 0) throw Failure(path, "解码音频");
        return decoder;
    }
    private static IOException Failure(string path, string stage, BASSError? error = null) =>
        new($"{stage}失败（BASS：{error ?? Bass.BASS_ErrorGetCode()}）。文件：{path}");
}
