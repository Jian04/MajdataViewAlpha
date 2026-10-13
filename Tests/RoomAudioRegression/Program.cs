using MajdataEdit;
using Un4seen.Bass;

if (!Bass.BASS_Init(0, 44100, BASSInit.BASS_DEVICE_DEFAULT, IntPtr.Zero))
    throw new Exception("BASS initialization failed: " + Bass.BASS_ErrorGetCode());
try
{
    foreach (var path in args)
    {
        using var audio = await Task.Run(() => EditorSongAudio.Prepare(path, 0, true));
        if (audio.Length <= 0 || audio.Waveforms.Length != 3 || audio.Waveforms.Any(w => w.Length <= 1))
            throw new Exception("Downloaded audio has no waveform or duration");
        var stream = audio.TakeStream();
        if (stream == 0 || Bass.BASS_ChannelGetInfo(stream) == null) throw new Exception("Prepared stream was released");
        audio.Dispose();
        if (Bass.BASS_ChannelGetInfo(stream) == null) throw new Exception("Transferred stream was double-freed");
        Bass.BASS_StreamFree(stream);
        Console.WriteLine($"AUDIO PASS: {Path.GetFileName(path)}, {audio.Frequency} Hz, {audio.Length:F3} s, all waveform levels");
    }
    var malformed = Path.Combine(AppContext.BaseDirectory, "invalid-track.mp3");
    File.WriteAllText(malformed, "This is not an audio file.");
    try { using var bad = EditorSongAudio.Prepare(malformed, 0, true); throw new Exception("Invalid audio accepted"); }
    catch (IOException error) when (error.Message.Contains("BASS_ERROR_FILEFORM") && error.Message.Contains(malformed))
    { Console.WriteLine("AUDIO PASS: preserves the decoder's exact error and source path"); }
    using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
    try { using var cancelled = EditorSongAudio.Prepare(args[0], 0, true, cancellation.Token); throw new Exception("Cancellation ignored"); }
    catch (OperationCanceledException) { Console.WriteLine("AUDIO PASS: disconnected joins do not install prepared audio"); }
}
finally { Bass.BASS_Free(); }
