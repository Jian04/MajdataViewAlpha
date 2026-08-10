using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Timers;
using Un4seen.Bass;
using Timer = System.Timers.Timer;

namespace MajdataEdit;

public partial class MainWindow
{
    private readonly Timer waveStopMonitorTimer = new(33);
    public int allperfectStream = -114514;
    public int answerStream = -114514;

    public int bgmStream = -114514;
    public int breakSlideStartStream = -114514; // Break Slide start sound effect
    public int breakSlideStream = -114514; // Break Slide cheer (Critical Perfect sound)
    public int breakStream = -114514; // This is the cheer sound.
    public int clockStream = -114514;
    private double extraTime4AllPerfect; // Seconds to wait after playback for the All Perfect effect.
    public int fanfareStream = -114514;
    public int hanabiStream = -114514;
    public int holdRiserStream = -114514;
    private bool isPlan2Stop; // Defers stopping when All Perfect cannot finish before the BGM ends.

    private bool isPlaying; // Supports automatic stopping at the end of playback.
    public int judgeBreakSlideStream = -114514; // Break Slide judgment sound
    public int judgeBreakStream = -114514; // Break judgment sound, not the cheer.
    public int judgeExStream = -114514;
    public int judgeStream = -114514;

    private double playStartTime;
    public int slideStream = -114514;
    public int touchStream = -114514;
    public int trackStartStream = -114514;

    private List<SoundEffectTiming>? waitToBePlayed;
    //private Stopwatch sw = new Stopwatch();

    // This update "middle" frequently to monitor if the wave has to be stopped
    private void WaveStopMonitorTimer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        WaveStopMonitorUpdate();
    }

    private void ReadSoundEffect()
    {
        var path = Environment.CurrentDirectory + "/SFX/";
        answerStream = Bass.BASS_StreamCreateFile(path + "answer.wav", 0L, 0L, BASSFlag.BASS_SAMPLE_FLOAT);
        judgeStream = Bass.BASS_StreamCreateFile(path + "judge.wav", 0L, 0L, BASSFlag.BASS_SAMPLE_FLOAT);
        judgeBreakStream = Bass.BASS_StreamCreateFile(path + "judge_break.wav", 0L, 0L, BASSFlag.BASS_SAMPLE_FLOAT);
        judgeExStream = Bass.BASS_StreamCreateFile(path + "judge_ex.wav", 0L, 0L, BASSFlag.BASS_SAMPLE_FLOAT);
        breakStream = Bass.BASS_StreamCreateFile(path + "break.wav", 0L, 0L, BASSFlag.BASS_SAMPLE_FLOAT);
        hanabiStream = Bass.BASS_StreamCreateFile(path + "hanabi.wav", 0L, 0L, BASSFlag.BASS_SAMPLE_FLOAT);
        holdRiserStream = Bass.BASS_StreamCreateFile(path + "touchHold_riser.wav", 0L, 0L, BASSFlag.BASS_SAMPLE_FLOAT);
        trackStartStream = Bass.BASS_StreamCreateFile(path + "track_start.wav", 0L, 0L, BASSFlag.BASS_SAMPLE_FLOAT);
        slideStream = Bass.BASS_StreamCreateFile(path + "slide.wav", 0L, 0L, BASSFlag.BASS_SAMPLE_FLOAT);
        touchStream = Bass.BASS_StreamCreateFile(path + "touch.wav", 0L, 0L, BASSFlag.BASS_SAMPLE_FLOAT);
        allperfectStream = Bass.BASS_StreamCreateFile(path + "all_perfect.wav", 0L, 0L, BASSFlag.BASS_SAMPLE_FLOAT);
        fanfareStream = Bass.BASS_StreamCreateFile(path + "fanfare.wav", 0L, 0L, BASSFlag.BASS_SAMPLE_FLOAT);
        clockStream = Bass.BASS_StreamCreateFile(path + "clock.wav", 0L, 0L, BASSFlag.BASS_SAMPLE_FLOAT);
        breakSlideStartStream =
            Bass.BASS_StreamCreateFile(path + "break_slide_start.wav", 0L, 0L, BASSFlag.BASS_SAMPLE_FLOAT);
        breakSlideStream = Bass.BASS_StreamCreateFile(path + "break_slide.wav", 0L, 0L, BASSFlag.BASS_SAMPLE_FLOAT);
        judgeBreakSlideStream =
            Bass.BASS_StreamCreateFile(path + "judge_break_slide.wav", 0L, 0L, BASSFlag.BASS_SAMPLE_FLOAT);
    }

    [DllImport("winmm")]
    private static extern void timeBeginPeriod(int t);

    [DllImport("winmm")]
    private static extern void timeEndPeriod(int t);

    private void StartSELoop()
    {
        var thread = new Thread(() =>
        {
            timeBeginPeriod(1);
            var lasttime = Bass.BASS_ChannelBytes2Seconds(bgmStream, Bass.BASS_ChannelGetPosition(bgmStream));
            while (isPlaying)
            {
                //sw.Reset();
                //sw.Start();
                SoundEffectUpdate();
                Thread.Sleep(1);
                //sw.Stop();
                //if(sw.Elapsed.TotalMilliseconds>1.5)
                //    Console.WriteLine(sw.Elapsed);
            }

            timeEndPeriod(1);
        })
        {
            Priority = ThreadPriority.Highest
        };
        thread.Start();
    }

    private void SoundEffectUpdate()
    {
        try
        {
            var currentTime = Bass.BASS_ChannelBytes2Seconds(bgmStream, Bass.BASS_ChannelGetPosition(bgmStream));
            //var waitToBePlayed = SimaiProcess.notelist.FindAll(o => o.havePlayed == false && o.time > currentTime);
            if (waitToBePlayed!.Count < 1) return;
            var nearestTime = waitToBePlayed[0].time;
            //Console.WriteLine(nearestTime - currentTime);
            if (nearestTime - currentTime <= 0.0545) //dont touch this!!!!! this related to delay
            {
                var se = waitToBePlayed[0];
                waitToBePlayed.RemoveAt(0);

                if (se.hasAnswer) Bass.BASS_ChannelPlay(answerStream, true);
                if (se.hasJudge) Bass.BASS_ChannelPlay(judgeStream, true);
                if (se.hasJudgeBreak) Bass.BASS_ChannelPlay(judgeBreakStream, true);
                if (se.hasJudgeEx) Bass.BASS_ChannelPlay(judgeExStream, true);
                if (se.hasBreak) Bass.BASS_ChannelPlay(breakStream, true);
                if (se.hasTouch) Bass.BASS_ChannelPlay(touchStream, true);
                if (se.hasHanabi) //may cause delay
                    Bass.BASS_ChannelPlay(hanabiStream, true);
                if (se.hasTouchHold) Bass.BASS_ChannelPlay(holdRiserStream, true);
                if (se.hasTouchHoldEnd) Bass.BASS_ChannelStop(holdRiserStream);
                if (se.hasSlide) Bass.BASS_ChannelPlay(slideStream, true);
                if (se.hasBreakSlideStart) Bass.BASS_ChannelPlay(breakSlideStartStream, true);
                if (se.hasBreakSlide) Bass.BASS_ChannelPlay(breakSlideStream, true);
                if (se.hasJudgeBreakSlide) Bass.BASS_ChannelPlay(judgeBreakSlideStream, true);
                if (se.hasAllPerfect)
                {
                    Bass.BASS_ChannelPlay(allperfectStream, true);
                    Bass.BASS_ChannelPlay(fanfareStream, true);
                }

                if (se.hasClock)
                    Bass.BASS_ChannelPlay(clockStream, true);
                //
                Dispatcher.Invoke(() =>
                {
                    if ((bool)FollowPlayCheck.IsChecked!)
                    {
                        ghostCusorPositionTime = (float)nearestTime;
                        SeekTextFromIndex(se.noteGroupIndex);
                    }
                });
            }
        }
        catch
        {
        }
    }

    private double GetAllPerfectStartTime()
    {
        // Find the theoretical All Perfect trigger time: the completion time of the final note.
        double latestNoteFinishTime = -1;
        double baseTime, noteTime;
        foreach (var noteGroup in SimaiProcess.notelist)
        {
            baseTime = noteGroup.time;
            foreach (var note in noteGroup.getNotes())
            {
                if (note.noteType == SimaiNoteType.Tap || note.noteType == SimaiNoteType.Touch)
                    noteTime = baseTime;
                else if (note.noteType == SimaiNoteType.Hold || note.noteType == SimaiNoteType.TouchHold)
                    noteTime = baseTime + note.holdTime;
                else if (note.noteType == SimaiNoteType.Slide)
                    noteTime = note.slideStartTime + note.slideTime;
                else
                    noteTime = -1;
                if (noteTime > latestNoteFinishTime) latestNoteFinishTime = noteTime;
            }
        }

        return latestNoteFinishTime;
    }

    private double GetRecordingEndTime()
    {
        var chartEnd = Math.Max(0d, GetAllPerfectStartTime());
        return chartEnd + (editorSetting?.ShowAllPerfect == true
            ? AllPerfectDuration + 3d
            : 5d);
    }

    private void generateSoundEffectList(double startTime, bool isOpIncluded)
    {
        waitToBePlayed = new List<SoundEffectTiming>();
        if (isOpIncluded)
        {
            var clockCountText = SimaiProcess.GetClockCountText();
            if (!string.IsNullOrWhiteSpace(clockCountText) && SimaiProcess.notelist.Count > 0)
            {
                try
                {
                    var clock_cnt = Math.Max(0, int.Parse(clockCountText));
                    var clock_int = 60.0d / SimaiProcess.notelist[0].currentBpm;
                    for (var i = 0; i < clock_cnt; i++)
                    {
                        var clockTime = i * clock_int;
                        if (!waitToBePlayed.Any(item => Math.Abs(item.time - clockTime) < 0.001d))
                            waitToBePlayed.Add(new SoundEffectTiming(clockTime, _hasClock: true));
                    }
                }
                catch
                {
                }
            }
        }

        for (var i = 0; i < SimaiProcess.notelist.Count; i++)
        {
            var noteGroup = SimaiProcess.notelist[i];
            if (noteGroup.time < startTime) continue;

            SoundEffectTiming stobj;

            // Reuse an existing SE at this point if one has already been created.
            var combIndex = waitToBePlayed.FindIndex(o => Math.Abs(o.time - noteGroup.time) < 0.001f);
            if (combIndex != -1)
                stobj = waitToBePlayed[combIndex];
            else
                stobj = new SoundEffectTiming(noteGroup.time);

            stobj.noteGroupIndex = i;

            var notes = noteGroup.getNotes();
            foreach (var note in notes)
                switch (note.noteType)
                {
                    case SimaiNoteType.Tap:
                    {
                        stobj.hasAnswer = true;
                        // ALPHA: 1f is a firework Tap that cheers on hit, like Cf.
                        if (note.isHanabi) stobj.hasHanabi = true;
                        if (note.isBreak)
                        {
                            // Break notes use both the Break judgment sound and Break cheer (2600).
                            stobj.hasBreak = true;
                            stobj.hasJudgeBreak = true;
                        }

                        if (note.isEx)
                            // Ex notes use the Ex judgment sound.
                            stobj.hasJudgeEx = true;
                        if (!note.isBreak && !note.isEx)
                            // Otherwise this is a normal note and uses the normal judgment sound.
                            stobj.hasJudge = true;
                        break;
                    }
                    case SimaiNoteType.Hold:
                    {
                        stobj.hasAnswer = true;
                        // ALPHA: 1hf is a firework Hold that cheers when its head is hit, like Cf.
                        if (note.isHanabi) stobj.hasHanabi = true;
                        // As with Tap, select Break or Ex sounds; otherwise use the normal sound.
                        if (note.isBreak)
                        {
                            stobj.hasBreak = true;
                            stobj.hasJudgeBreak = true;
                        }

                        if (note.isEx) stobj.hasJudgeEx = true;
                        if (!note.isBreak && !note.isEx) stobj.hasJudge = true;

                        // Calculate the Hold-tail sound effect.
                        if (!(note.holdTime <= 0.00f))
                        {
                            // Short Holds (hexagonal Taps) have no tail sound; calculate it only for longer Holds.
                            var targetTime = noteGroup.time + note.holdTime;
                            var nearIndex = waitToBePlayed.FindIndex(o => Math.Abs(o.time - targetTime) < 0.001f);
                            if (nearIndex != -1)
                            {
                                waitToBePlayed[nearIndex].hasAnswer = true;
                                if (!note.isBreak && !note.isEx) waitToBePlayed[nearIndex].hasJudge = true;
                            }
                            else
                            {
                                // Only normal Holds have an ending judgment sound; Break and Ex variants do not (inferred for Break).
                                var holdRelease = new SoundEffectTiming(targetTime, true, !note.isBreak && !note.isEx);
                                waitToBePlayed.Add(holdRelease);
                            }
                        }

                        break;
                    }
                    case SimaiNoteType.Slide:
                    {
                        if (!note.isSlideNoHead)
                        {
                            // Only headed Slides have answer and judgment sounds.
                            stobj.hasAnswer = true;
                            if (note.isTouchSlide && note.touchArea != 'K')
                                stobj.hasTouch = true;
                            // ALPHA: 1f-5 is a firework star head that cheers on hit, like Cf.
                            if (note.isHanabi) stobj.hasHanabi = true;
                            if (note.isBreak)
                            {
                                stobj.hasBreak = true;
                                stobj.hasJudgeBreak = true;
                            }

                            if (note.isEx) stobj.hasJudgeEx = true;
                            if (!note.isBreak && !note.isEx) stobj.hasJudge = true;
                        }

                        // Slide start sound effect
                        var targetTime = note.slideStartTime;
                        var nearIndex = waitToBePlayed.FindIndex(o => Math.Abs(o.time - targetTime) < 0.001f);
                        if (nearIndex != -1)
                        {
                            if (note.isSlideBreak)
                                // Use the Break Slide start sound for Break Slides.
                                waitToBePlayed[nearIndex].hasBreakSlideStart = true;
                            else
                                // Otherwise use the normal Slide start sound.
                                waitToBePlayed[nearIndex].hasSlide = true;
                        }
                        else
                        {
                            SoundEffectTiming slide;
                            if (note.isSlideBreak)
                                slide = new SoundEffectTiming(targetTime, _hasBreakSlideStart: true);
                            else
                                slide = new SoundEffectTiming(targetTime, _hasSlide: true);
                            waitToBePlayed.Add(slide);
                        }

                        // Add a Break sound at the Slide tail for Break Slides.
                        if (note.isSlideBreak)
                        {
                            targetTime = note.slideStartTime + note.slideTime;
                            nearIndex = waitToBePlayed.FindIndex(o => Math.Abs(o.time - targetTime) < 0.001f);
                            if (nearIndex != -1)
                            {
                                waitToBePlayed[nearIndex].hasBreakSlide = true;
                                waitToBePlayed[nearIndex].hasJudgeBreakSlide = true;
                            }
                            else
                            {
                                var slide = new SoundEffectTiming(targetTime, _hasBreakSlide: true,
                                    _hasJudgeBreakSlide: true);
                                waitToBePlayed.Add(slide);
                            }
                        }

                        break;
                    }
                    case SimaiNoteType.Touch:
                    {
                        stobj.hasAnswer = true;
                        stobj.hasTouch = true;
                        if (note.isHanabi) stobj.hasHanabi = true;
                        // ALPHA: Break Touch (Cxb) emits the Break judgment sound and cheer, like Break Tap/Hold.
                        if (note.isBreak)
                        {
                            stobj.hasBreak = true;
                            stobj.hasJudgeBreak = true;
                        }
                        break;
                    }
                    case SimaiNoteType.TouchHold:
                    {
                        stobj.hasAnswer = true;
                        stobj.hasTouch = true;
                        // ALPHA: Break TouchHold (Chb) emits the Break judgment sound and cheer at the head, like Break Hold.
                        if (note.isBreak)
                        {
                            stobj.hasBreak = true;
                            stobj.hasJudgeBreak = true;
                        }
                        // Play the riser and calculate the tail only for TouchHolds with a positive duration.
                        // A zero-duration form such as [1:0] parses holdTime as zero, placing riser start and end
                        // at the same time. Sorting may put Stop before Play, leaving the 12.68-second riser running forever.
                        if (note.holdTime > 0.00f)
                        {
                            stobj.hasTouchHold = true;
                            // Calculate the TouchHold tail.
                            var targetTime = noteGroup.time + note.holdTime;
                            var nearIndex = waitToBePlayed.FindIndex(o => Math.Abs(o.time - targetTime) < 0.001f);
                            if (nearIndex != -1)
                            {
                                if (note.isHanabi) waitToBePlayed[nearIndex].hasHanabi = true;
                                waitToBePlayed[nearIndex].hasAnswer = true;
                                waitToBePlayed[nearIndex].hasTouchHoldEnd = true;
                            }
                            else
                            {
                                var tHoldRelease = new SoundEffectTiming(targetTime, true, _hasHanabi: note.isHanabi,
                                    _hasTouchHoldEnd: true);
                                waitToBePlayed.Add(tHoldRelease);
                            }
                        }
                        else if (note.isHanabi)
                        {
                            // Treat a zero-duration TouchHold as a normal Touch while preserving its firework.
                            stobj.hasHanabi = true;
                        }

                        break;
                    }
                }

            if (combIndex != -1)
                waitToBePlayed[combIndex] = stobj;
            else
                waitToBePlayed.Add(stobj);
        }

        var allPerfectTime = GetAllPerfectStartTime();
        if (editorSetting?.ShowAllPerfect == true && allPerfectTime >= startTime - 0.001d)
            waitToBePlayed.Add(new SoundEffectTiming(allPerfectTime, _hasAllPerfect: true));
        waitToBePlayed.Sort((o1, o2) => o1.time < o2.time ? -1 : 1);

        var apTime = GetAllPerfectStartTime();
        if (songLength < apTime + AllPerfectDuration)
            extraTime4AllPerfect =
                apTime + AllPerfectDuration - songLength; // Extra post-playback time reserved for AP.
        else
            // If there is enough time, stop when the BGM ends.
            extraTime4AllPerfect = -1;

        //Console.WriteLine(JsonConvert.SerializeObject(waitToBePlayed));
    }

    private void renderSoundEffect(double delaySeconds)
    {
        // TODO: Make this asynchronous and add a prompt window.
        var path = Environment.CurrentDirectory + "/SFX";
        var tempPath = GetViewerWorkingDirectory();

        var pathEnv = new List<string>
        {
            tempPath,
            AppContext.BaseDirectory,
            Environment.CurrentDirectory
        };
        var systemPath = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(systemPath))
            pathEnv.AddRange(systemPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        var converterPath = pathEnv.FirstOrDefault(scanPath =>
            !string.IsNullOrWhiteSpace(scanPath) &&
            File.Exists(Path.Combine(scanPath, "ffmpeg.exe")));

        var throwErrorOnMismatch = string.IsNullOrEmpty(converterPath);

        // Default: 16-bit
        string getBasePath(string rawPath) { return Path.GetFileNameWithoutExtension(rawPath); }

        var useOgg = File.Exists(maidataDir + "/track.ogg");

        var bgmBank = new SoundBank(maidataDir + "/track" + (useOgg ? ".ogg" : ".mp3"));
        if (bgmBank.Frequency <= 0)
            throw new InvalidOperationException(
                $"无法读取谱面音频：{bgmBank.FilePath}\n请确认音频文件没有损坏，并可被 BASS 解码。");

        var comparableBanks = new Dictionary<string, SoundBank>();

        var answerBank = new SoundBank(path + "/answer.wav");
        var judgeBank = new SoundBank(path + "/judge.wav");
        var judgeBreakBank = new SoundBank(path + "/judge_break.wav");
        var judgeExBank = new SoundBank(path + "/judge_ex.wav");
        var breakBank = new SoundBank(path + "/break.wav");
        var hanabiBank = new SoundBank(path + "/hanabi.wav");
        var holdRiserBank = new SoundBank(path + "/touchHold_riser.wav");
        var trackStartBank = new SoundBank(path + "/track_start.wav");
        var slideBank = new SoundBank(path + "/slide.wav");
        var touchBank = new SoundBank(path + "/touch.wav");
        var apBank = new SoundBank(path + "/all_perfect.wav");
        var fanfareBank = new SoundBank(path + "/fanfare.wav");
        var clockBank = new SoundBank(path + "/clock.wav");
        var breakSlideStartBank = new SoundBank(path + "/break_slide_start.wav");
        var breakSlideBank = new SoundBank(path + "/break_slide.wav");
        var judgeBreakSlideBank = new SoundBank(path + "/judge_break_slide.wav");

        comparableBanks["Answer"] = answerBank;
        comparableBanks["Judge"] = judgeBank;
        comparableBanks["Judge Break"] = judgeBreakBank;
        comparableBanks["Judge EX"] = judgeExBank;
        comparableBanks["Break"] = breakBank;
        comparableBanks["Hanabi"] = hanabiBank;
        comparableBanks["Hold Riser"] = holdRiserBank;
        comparableBanks["Track Start"] = trackStartBank;
        comparableBanks["Slide"] = slideBank;
        comparableBanks["Touch"] = touchBank;
        comparableBanks["All Perfect"] = apBank;
        comparableBanks["Fanfare"] = fanfareBank;
        comparableBanks["Clock"] = clockBank;
        comparableBanks["Break Slide Start"] = breakSlideStartBank;
        comparableBanks["Break Slide"] = breakSlideBank;
        comparableBanks["Judge Break Slide"] = judgeBreakSlideBank;
        var mediaBanks = new Dictionary<string, SoundBank>(StringComparer.OrdinalIgnoreCase);
        foreach (var media in GetEffectiveMediaTable()
                     .Where(item => item.kind == "audio" && item.enabled &&
                                    !string.IsNullOrWhiteSpace(item.path)))
        {
            if (mediaBanks.ContainsKey(media.path))
                continue;
            var mediaPath = Path.GetFullPath(Path.Combine(
                maidataDir, media.path.Replace('/', Path.DirectorySeparatorChar)));
            var mediaBank = new SoundBank(mediaPath);
            mediaBanks[media.path] = mediaBank;
            comparableBanks["Media " + mediaBanks.Count] = mediaBank;
        }

        var conversionIndex = 0;
        foreach (var compPair in comparableBanks)
        {
            // Skip non existent file.
            if (compPair.Value.Frequency < 0)
                continue;

            if (bgmBank.MixFormatCheck(compPair.Value))
                continue;

            if (throwErrorOnMismatch)
                throw new Exception(
                    string.Format("BGM and {0} do not have the same stereo sample format. Convert {0} from {1}Hz into stereo {2}Hz!",
                        compPair.Key, compPair.Value.Frequency, bgmBank.Frequency)
                );

            Console.WriteLine("Convert sample of {0} ({1}/{2})...", compPair.Key,
                compPair.Value.Info?.length ?? 0,
                compPair.Value.Frequency);
            compPair.Value.Reassign(converterPath!, tempPath,
                $"t_{conversionIndex++}_{getBasePath(compPair.Value.FilePath)}.wav",
                bgmBank.Frequency);
        }

        var freq = bgmBank.Frequency;

        // Keep the mixed WAV beyond the intended video stop. ffmpeg -shortest will then
        // finish on the video pipe, never on an early audio EOF.
        const double encoderSafetySeconds = 5d;
        var renderDuration = Math.Max(songLength, GetRecordingEndTime() + encoderSafetySeconds);
        var sampleCount = (long)(renderDuration * freq * 2);
        bgmBank.RawSize = sampleCount;
        Console.WriteLine(sampleCount);
        bgmBank.InitializeRawSample();
        var bgmRaw = bgmBank.Raw ?? throw new InvalidOperationException(
            $"无法读取谱面音频采样：{bgmBank.FilePath}\n请尝试将 track.mp3/track.ogg 转换为 44100 Hz。");

        foreach (var compPair in comparableBanks)
        {
            // Skip non existent file.
            if (compPair.Value.Frequency < 0)
                continue;

            if (!bgmBank.MixFormatCheck(compPair.Value))
                continue;

            Console.WriteLine("Init sample for {0}...", compPair.Key);
            compPair.Value.InitializeRawSample();
        }
        // Use a silent lead-in if track_start.wav is missing or undecodable instead of crashing the recording.
        var trackStartRaw = trackStartBank.Raw ?? Array.Empty<short>();

        var trackOps = new List<SoundDataRange>();
        var typeSamples = new Dictionary<SoundDataType, short[]>();
        foreach (SoundDataType sType in Enum.GetValues(SoundDataType.None.GetType()))
        {
            if (sType == 0) continue;
            typeSamples[sType] = new short[sampleCount];
        }
        var mediaSamples = new short[sampleCount];
        var timelineAudioReplacesBgm = GetEffectiveMediaTable().Any(item =>
            item.timelineClip && item.kind == "audio" && item.enabled);

        SoundBank? getSampleFromType(SoundDataType type)
        {
            return type switch
            {
                SoundDataType.Answer => answerBank,
                SoundDataType.Judge => judgeBank,
                SoundDataType.JudgeBreak => judgeBreakBank,
                SoundDataType.JudgeEX => judgeExBank,
                SoundDataType.Break => breakBank,
                SoundDataType.Hanabi => hanabiBank,
                SoundDataType.TouchHold => holdRiserBank,
                SoundDataType.Slide => slideBank,
                SoundDataType.Touch => touchBank,
                SoundDataType.AllPerfect => apBank,
                SoundDataType.FullComboFanfare => fanfareBank,
                SoundDataType.Clock => clockBank,
                SoundDataType.BreakSlideStart => breakSlideStartBank,
                SoundDataType.BreakSlide => breakSlideBank,
                SoundDataType.JudgeBreakSlide => judgeBreakSlideBank,
                _ => null,
            };
        }

        void sampleWrite(int time, SoundDataType type)
        {
            var sample = getSampleFromType(type);
            if (sample == null) return;
            if (sample.Raw == null) return;
            if (sample.Frequency <= 0) return;
            for (var t = 0; t < sample.RawSize && time + t < typeSamples[type].Length; t++)
                typeSamples[type][time + t] = sample.Raw[t];
        }

        void sampleWipe(int timeFrom, int timeTo, SoundDataType type)
        {
            for (var t = timeFrom; t < timeTo && t < typeSamples[type].Length; t++)
                typeSamples[type][t] = 0;
        }

        // Generate the track for each sound effect.
        foreach (var soundTiming in waitToBePlayed!)
        {
            var startIndex = (int)(soundTiming.time * freq) * 2; // Multiply by two for the two channels.
            if (soundTiming.hasAnswer) sampleWrite(startIndex, SoundDataType.Answer);
            if (soundTiming.hasJudge) sampleWrite(startIndex, SoundDataType.Judge);
            if (soundTiming.hasJudgeBreak) sampleWrite(startIndex, SoundDataType.JudgeBreak);
            if (soundTiming.hasJudgeEx) sampleWrite(startIndex, SoundDataType.JudgeEX);
            if (soundTiming.hasBreak)
                // Reach for the Stars.ogg
                sampleWrite(startIndex, SoundDataType.Break);
            if (soundTiming.hasHanabi) sampleWrite(startIndex, SoundDataType.Hanabi);
            if (soundTiming.hasTouchHold)
            {
                // no need to "CutNow" as HoldEnd did the work.
                sampleWrite(startIndex, SoundDataType.TouchHold);
                trackOps.Add(new SoundDataRange(SoundDataType.TouchHold, startIndex, holdRiserBank.RawSize));
            }

            if (soundTiming.hasTouchHoldEnd)
            {
                // Overwrite only the available portion, not the entire track.
                var lastTouchHoldOp = trackOps.FindLast(trackOp => trackOp.Type == SoundDataType.TouchHold);
                sampleWipe(startIndex, (int)lastTouchHoldOp.To, SoundDataType.TouchHold);
                continue;
            }

            if (soundTiming.hasSlide) sampleWrite(startIndex, SoundDataType.Slide);
            if (soundTiming.hasTouch) sampleWrite(startIndex, SoundDataType.Touch);
            if (soundTiming.hasBreakSlideStart) sampleWrite(startIndex, SoundDataType.BreakSlideStart);
            if (soundTiming.hasBreakSlide) sampleWrite(startIndex, SoundDataType.BreakSlide);
            if (soundTiming.hasJudgeBreakSlide) sampleWrite(startIndex, SoundDataType.JudgeBreakSlide);
            if (soundTiming.hasAllPerfect)
            {
                sampleWrite(startIndex, SoundDataType.AllPerfect);
                sampleWrite(startIndex, SoundDataType.FullComboFanfare);
            }

            if (soundTiming.hasClock) sampleWrite(startIndex, SoundDataType.Clock);
        }

        void WriteMediaClip(MediaChange media, double? stopTime)
        {
            if (!mediaBanks.TryGetValue(media.path, out var bank) || bank.Raw == null)
                return;
            var destinationStart = (long)(media.time * freq) * 2;
            var skippedTimelineSamples = destinationStart < 0 ? -destinationStart : 0;
            var sourceStart = Math.Max(0, (long)(media.sourceOffset * freq) * 2);
            sourceStart += skippedTimelineSamples;
            destinationStart = Math.Max(0, destinationStart);
            var available = bank.Raw.LongLength - sourceStart;
            if (media.duration > 0d)
                available = Math.Min(available,
                    Math.Max(0, (long)(media.duration * freq) * 2 - skippedTimelineSamples));
            if (stopTime.HasValue)
                available = Math.Min(available,
                    Math.Max(0, (long)((stopTime.Value - media.time) * freq) * 2 - skippedTimelineSamples));
            available = Math.Min(available, mediaSamples.LongLength - destinationStart);
            for (long offset = 0; offset < available; offset++)
            {
                var mixed = mediaSamples[destinationStart + offset] + bank.Raw[sourceStart + offset];
                mediaSamples[destinationStart + offset] =
                    (short)Math.Clamp(mixed, short.MinValue, short.MaxValue);
            }
        }

        foreach (var trackEvents in GetEffectiveMediaTable()
                     .Where(item => item.kind == "audio")
                     .GroupBy(item => item.track))
        {
            MediaChange? activeMedia = null;
            foreach (var media in trackEvents.OrderBy(item => item.time))
            {
                if (media.enabled)
                {
                    if (activeMedia != null)
                        WriteMediaClip(activeMedia, media.time);
                    activeMedia = media;
                }
                else if (activeMedia != null)
                {
                    WriteMediaClip(activeMedia, media.time);
                    activeMedia = null;
                }
            }
            if (activeMedia != null)
                WriteMediaClip(activeMedia, null);
        }

        // Get the volume used during real-time playback.

        float bgmVol = 1f,
            answerVol = 1f,
            judgeVol = 1f,
            judgeExVol = 1f,
            hanabiVol = 1f,
            touchVol = 1f,
            slideVol = 1f,
            breakVol = 1f,
            breakSlideVol = 1f;
        Bass.BASS_ChannelGetAttribute(bgmStream, BASSAttribute.BASS_ATTRIB_VOL, ref bgmVol);
        Bass.BASS_ChannelGetAttribute(answerStream, BASSAttribute.BASS_ATTRIB_VOL, ref answerVol);
        Bass.BASS_ChannelGetAttribute(judgeStream, BASSAttribute.BASS_ATTRIB_VOL, ref judgeVol);
        Bass.BASS_ChannelGetAttribute(breakStream, BASSAttribute.BASS_ATTRIB_VOL, ref breakVol);
        Bass.BASS_ChannelGetAttribute(breakSlideStream, BASSAttribute.BASS_ATTRIB_VOL, ref breakSlideVol);
        Bass.BASS_ChannelGetAttribute(slideStream, BASSAttribute.BASS_ATTRIB_VOL, ref slideVol);
        Bass.BASS_ChannelGetAttribute(judgeExStream, BASSAttribute.BASS_ATTRIB_VOL, ref judgeExVol);
        Bass.BASS_ChannelGetAttribute(touchStream, BASSAttribute.BASS_ATTRIB_VOL, ref touchVol);
        Bass.BASS_ChannelGetAttribute(hanabiStream, BASSAttribute.BASS_ATTRIB_VOL, ref hanabiVol);

        var filedata = new List<byte>();
        var delayEmpty = new short[(int)(delaySeconds * freq * 2)];
        var filehead = CreateWaveFileHeader(bgmRaw.Length * 2 + delayEmpty.Length * 2, 2, freq, 16).ToList();

        //if (trackStartRAW.Length > delayEmpty.Length)
        //    throw new Exception("track_start is too long; keep it under five seconds.");

        for (var i = 0; i < delayEmpty.Length; i++)
        {
            if (i < trackStartRaw.Length)
                delayEmpty[i] = trackStartRaw[i];
            filehead.AddRange(BitConverter.GetBytes(delayEmpty[i]));
        }

        for (var i = 0; i < sampleCount; i++)
        {
            // Apply BGM Data
            var sampleValue = timelineAudioReplacesBgm
                ? mediaSamples[i] * bgmVol
                : bgmRaw[i] * bgmVol + mediaSamples[i];

            foreach (var sampleTuple in typeSamples)
            {
                var type = sampleTuple.Key;
                var track = sampleTuple.Value;

                switch (type)
                {
                    case SoundDataType.Answer:
                        sampleValue += track[i] * answerVol;
                        break;
                    case SoundDataType.Judge:
                        sampleValue += track[i] * judgeVol;
                        break;
                    case SoundDataType.JudgeBreak:
                        sampleValue += track[i] * breakVol;
                        break;
                    case SoundDataType.JudgeEX:
                        sampleValue += track[i] * judgeExVol;
                        break;
                    case SoundDataType.Break:
                        sampleValue += track[i] * breakVol * 0.75f;
                        break;
                    case SoundDataType.BreakSlide:
                    case SoundDataType.JudgeBreakSlide:
                        sampleValue += track[i] * breakSlideVol;
                        break;
                    case SoundDataType.Hanabi:
                    case SoundDataType.TouchHold:
                        sampleValue += track[i] * hanabiVol;
                        break;
                    case SoundDataType.Slide:
                    case SoundDataType.BreakSlideStart:
                        sampleValue += track[i] * slideVol;
                        break;
                    case SoundDataType.Touch:
                        sampleValue += track[i] * touchVol;
                        break;
                    case SoundDataType.AllPerfect:
                    case SoundDataType.FullComboFanfare:
                    case SoundDataType.Clock:
                        sampleValue += track[i] * bgmVol;
                        break;
                }
            }

            var value = (long)sampleValue;
            if (value > short.MaxValue)
                value = short.MaxValue;
            if (value < short.MinValue)
                value = short.MinValue;
            filedata.AddRange(BitConverter.GetBytes((short)value));
        }

        filehead.AddRange(filedata);
        File.WriteAllBytes(maidataDir + "/out.wav", filehead.ToArray());

        typeSamples.Clear();
        Array.Clear(mediaSamples);
        bgmBank.Free();
        comparableBanks.Values.ToList().ForEach(otherBank =>
        {
            if (otherBank.Temp) File.Delete(otherBank.FilePath);
            otherBank.Free();
        });
    }

    /// <summary>
    ///     Creates a WAV audio file header. Adapted from https://www.cnblogs.com/CUIT-DX037/p/14070754.html
    /// </summary>
    /// <param name="data_Len">Audio data length.</param>
    /// <param name="data_SoundCH">Number of audio channels.</param>
    /// <param name="data_Sample">Sample rate, commonly 11025, 22050, 44100, etc.</param>
    /// <param name="data_SamplingBits">Bits per sample, commonly 4, 8, 12, 16, 24, or 32.</param>
    /// <returns></returns>
    private static byte[] CreateWaveFileHeader(int data_Len, int data_SoundCH, int data_Sample, int data_SamplingBits)
    {
        // WAV audio file header
        var WAV_HeaderInfo = new List<byte>(); // Should be 44 bytes long.
        WAV_HeaderInfo.AddRange(
            Encoding.ASCII
                .GetBytes("RIFF")); // Four-byte ASCII "RIFF" signature marking a valid Resource Interchange File Format file.
        WAV_HeaderInfo.AddRange(BitConverter.GetBytes(data_Len + 44 - 8)); // Four-byte little-endian length of all following data: total length minus eight.
        WAV_HeaderInfo.AddRange(Encoding.ASCII.GetBytes("WAVE")); // Four-byte ASCII "WAVE" signature identifying the WAV format.
        WAV_HeaderInfo.AddRange(Encoding.ASCII.GetBytes("fmt ")); // Four-byte ASCII "fmt " format-block identifier, including the trailing space.
        WAV_HeaderInfo.AddRange(BitConverter.GetBytes(16)); // Four-byte little-endian fmt block length, normally 16 without extra information.
        var fmt_Struct = new
        {
            PCM_Code = (short)1, // Encoding format code; standard WAV files usually use PCM value 1.
            SoundChannel = (short)data_SoundCH, // 2B: number of channels
            SampleRate = data_Sample, // 4B: sample rate per channel, commonly 11025, 22050, 44100, etc.
            BytesPerSec =
                data_SamplingBits * data_Sample * data_SoundCH /
                8, // 4B: byte rate = channels × sample rate × bits per sample / 8; players use it to estimate buffer size.
            BlockAlign = (short)(data_SamplingBits * data_SoundCH / 8), // 2B: sample-frame size = channels × bits per sample / 8.
            SamplingBits = (short)data_SamplingBits // Bits per sample, commonly 4, 8, 12, 16, 24, or 32.
        };
        // Write the fmt block fields in order; the default length is 16.
        WAV_HeaderInfo.AddRange(BitConverter.GetBytes(fmt_Struct.PCM_Code));
        WAV_HeaderInfo.AddRange(BitConverter.GetBytes(fmt_Struct.SoundChannel));
        WAV_HeaderInfo.AddRange(BitConverter.GetBytes(fmt_Struct.SampleRate));
        WAV_HeaderInfo.AddRange(BitConverter.GetBytes(fmt_Struct.BytesPerSec));
        WAV_HeaderInfo.AddRange(BitConverter.GetBytes(fmt_Struct.BlockAlign));
        WAV_HeaderInfo.AddRange(BitConverter.GetBytes(fmt_Struct.SamplingBits));
        /* Additional extension data may follow, in which case the fmt length must increase. */

        WAV_HeaderInfo.AddRange(Encoding.ASCII.GetBytes("data")); // Four-byte ASCII "data" signature.
        WAV_HeaderInfo.AddRange(BitConverter.GetBytes(data_Len)); // Four-byte audio-data length; data is little-endian and channels are interleaved.
        /* The header is now complete and is normally 44 bytes long. */
        return WAV_HeaderInfo.ToArray();
    }

    private void WaveStopMonitorUpdate()
    {
        // Monitor whether playback should stop.
        if (!isPlan2Stop &&
            isPlaying &&
            Bass.BASS_ChannelIsActive(bgmStream) == BASSActive.BASS_ACTIVE_STOPPED)
        {
            isPlan2Stop = true;
            if (extraTime4AllPerfect < 0)
            {
                // Stop immediately when there is enough time to finish.
                Dispatcher.Invoke(() => { ToggleStop(); });
            }
            else
            {
                // Otherwise wait before stopping.
                var stopPlayingTimer = new Timer(double.IsNormal(extraTime4AllPerfect)? (int)(extraTime4AllPerfect * 1000) : int.MaxValue)
                {
                    AutoReset = false
                };
                stopPlayingTimer.Elapsed += (sender, e) => { Dispatcher.Invoke(() => { ToggleStop(); }); };
                stopPlayingTimer.Start();
            }
        }
    }

    private class SoundEffectTiming
    {
        public readonly bool hasAllPerfect;
        public readonly bool hasClock;
        public readonly double time;
        public bool hasAnswer;
        public bool hasBreak;
        public bool hasBreakSlide;
        public bool hasBreakSlideStart;
        public bool hasHanabi;
        public bool hasJudge;
        public bool hasJudgeBreak;
        public bool hasJudgeBreakSlide;
        public bool hasJudgeEx;
        public bool hasSlide;
        public bool hasTouch;
        public bool hasTouchHold;
        public bool hasTouchHoldEnd;
        public int noteGroupIndex = -1;

        public SoundEffectTiming(double _time, bool _hasAnswer = false, bool _hasJudge = false,
            bool _hasJudgeBreak = false,
            bool _hasBreak = false, bool _hasTouch = false, bool _hasHanabi = false,
            bool _hasJudgeEx = false, bool _hasTouchHold = false, bool _hasSlide = false,
            bool _hasTouchHoldEnd = false, bool _hasAllPerfect = false, bool _hasClock = false,
            bool _hasBreakSlideStart = false, bool _hasBreakSlide = false, bool _hasJudgeBreakSlide = false)
        {
            time = _time;
            hasAnswer = _hasAnswer;
            hasJudge = _hasJudge;
            hasJudgeBreak = _hasJudgeBreak; // Preserve the judgment-Break flag.
            hasBreak = _hasBreak;
            hasTouch = _hasTouch;
            hasHanabi = _hasHanabi;
            hasJudgeEx = _hasJudgeEx;
            hasTouchHold = _hasTouchHold;
            hasSlide = _hasSlide;
            hasTouchHoldEnd = _hasTouchHoldEnd;
            hasAllPerfect = _hasAllPerfect;
            hasClock = _hasClock;
            hasBreakSlideStart = _hasBreakSlideStart;
            hasBreakSlide = _hasBreakSlide;
            hasJudgeBreakSlide = _hasJudgeBreakSlide;
        }
    }

    private class SoundBank
    {
        internal SoundBank(string Path)
        {
            FilePath = Path;

            InitializeSampleData();
        }

        public bool Temp { get; private set; }
        public string FilePath { get; private set; }
        public int ID { get; private set; }
        public BASS_SAMPLE? Info { get; private set; }

        public long RawSize { get; set; }
        public short[]? Raw { get; private set; }

        public int Frequency
        {
            get
            {
                if (Info != null) return Info.freq;
                return -1;
            }
        }

        public void Reassign(string FFMpegDirectory, string NewDirectory, string Filename, int NewFrequency)
        {
            if (FFMpegDirectory.Length == 0)
                return;

            Func<string, string> NormalizePath = path =>
            {
                return string.Join(Path.DirectorySeparatorChar.ToString(), path.Split('/'));
            };

            Temp = true;
            var OriginalPath = FilePath;
            FilePath = NewDirectory + "/" + Filename;

            var args = string.Format(
                "-loglevel 24 -y -i \"{0}\" -ac 2 -ar {2} \"{1}\"",
                NormalizePath(OriginalPath),
                NormalizePath(FilePath),
                NewFrequency
            );
            var startInfo = new ProcessStartInfo(FFMpegDirectory + "/ffmpeg.exe", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };
            var proc = Process.Start(startInfo)!;
            proc.WaitForExit();
            if (proc.ExitCode != 0)
                throw new Exception(proc.StandardError.ReadToEnd());

            Free();
            InitializeSampleData();
        }

        private void InitializeSampleData()
        {
            ID = Bass.BASS_SampleLoad(FilePath, 0, 0, 1, BASSFlag.BASS_DEFAULT);
            if (ID != 0)
                Info = Bass.BASS_SampleGetInfo(ID);

            if (Info != null)
                RawSize = Info.length / 2;
            else
                RawSize = 0;
        }

        public void InitializeRawSample()
        {
            if (Info == null)
                return;

            Raw = new short[RawSize];
            Bass.BASS_SampleGetData(ID, Raw);
        }

        public void Free()
        {
            if (ID <= 0)
                return;

            Raw = null;
            Bass.BASS_SampleFree(ID);
        }

        public bool FrequencyCheck(SoundBank other)
        {
            return Frequency == other.Frequency && Frequency > 0;
        }

        public bool MixFormatCheck(SoundBank other)
        {
            return FrequencyCheck(other) && Info?.chans == 2 && other.Info?.chans == 2;
        }
    }

    private enum SoundDataType
    {
        None,
        Answer,
        Judge,
        JudgeBreak,
        JudgeEX,
        Break,
        Hanabi,
        TouchHold,
        Slide,
        Touch,
        AllPerfect,
        FullComboFanfare,
        Clock,
        BreakSlideStart,
        BreakSlide,
        JudgeBreakSlide
    }

    private struct SoundDataRange
    {
        internal SoundDataRange(SoundDataType type, long from, long len)
        {
            Type = type;
            From = from;
            To = from + len;
        }

        public SoundDataType Type { get; }
        public long From { get; }
        public long To { get; private set; }

        public long Length
        {
            get => To - From;
            set => To = From + value;
        }

        public bool In(long value)
        {
            return value >= From && value < To;
        }
    }
}
