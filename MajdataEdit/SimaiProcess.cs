using System.IO;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;

namespace MajdataEdit;

internal static class SimaiProcess
{
    public static string? title;
    public static string? artist;
    public static string? designer;
    public static string? wholeBpm;
    public static string? clockCount;
    public static string? other_commands;
    public static float first;
    public static string[] fumens = new string[7];
    public static string[] levels = new string[7];

    /// <summary>
    ///     the timing points that contains notedata
    /// </summary>
    public static List<SimaiTimingPoint> notelist = new();

    /// <summary>
    ///     the timing points made by "," in maidata
    /// </summary>
    public static List<SimaiTimingPoint> timinglist = new();

    public static List<SvPoint> svTable = new();
    public static List<SpeedChange> hsTable = new();
    public static List<SpawnChange> spawnTable = new();
    public static List<BounceChange> bounceTable = new();
    public static List<ColorChange> colorTable = new();
    public static List<SizeChange> sizeTable = new();
    public static List<AlphaChange> alphaTable = new();
    public static List<DisplayChange> displayTable = new();
    public static List<SubtitleChange> subtitleTable = new();
    public static List<EffectChange> effectTable = new();
    public static List<MediaChange> mediaTable = new();
    // Editor-only time signature markers used by the waveform grid.
    public static List<MeterChange> meterTable = new();
    public static double? mediaTrimStart;
    public static double? mediaTrimEnd;

    /// <summary>
    ///     Reset all the data in the static class.
    /// </summary>
    public static void ClearData()
    {
        title = "";
        artist = "";
        designer = "";
        wholeBpm = "";
        clockCount = "";
        other_commands = "";
        first = 0;
        fumens = new string[7];
        levels = new string[7];
        notelist = new List<SimaiTimingPoint>();
        timinglist = new List<SimaiTimingPoint>();
        svTable = new List<SvPoint>();
        hsTable = new List<SpeedChange>();
        spawnTable = new List<SpawnChange>();
        bounceTable = new List<BounceChange>();
        colorTable = new List<ColorChange>();
        sizeTable = new List<SizeChange>();
        alphaTable = new List<AlphaChange>();
        displayTable = new List<DisplayChange>();
        subtitleTable = new List<SubtitleChange>();
        effectTable = new List<EffectChange>();
        mediaTable = new List<MediaChange>();
        meterTable = new List<MeterChange>();
        mediaTrimStart = null;
        mediaTrimEnd = null;
    }

    /// <summary>
    ///     Read the maidata.txt into the static class, including the variables. Show up a messageBox when enconter any
    ///     exception.
    /// </summary>
    /// <param name="filename">file path of maidata.txt</param>
    /// <returns>if the read process faced any error</returns>
    public static bool ReadData(string filename)
    {
        var i = 0;
        other_commands = "";
        try
        {
            var maidataTxt = File.ReadAllLines(filename, Encoding.UTF8);
            for (i = 0; i < maidataTxt.Length; i++)
                if (maidataTxt[i].StartsWith("&title="))
                    title = GetValue(maidataTxt[i]);
                else if (maidataTxt[i].StartsWith("&artist="))
                    artist = GetValue(maidataTxt[i]);
                else if (maidataTxt[i].StartsWith("&des="))
                    designer = GetValue(maidataTxt[i]);
                else if (maidataTxt[i].StartsWith("&wholebpm=", StringComparison.OrdinalIgnoreCase))
                    wholeBpm = GetValue(maidataTxt[i]);
                else if (maidataTxt[i].StartsWith("&clock_count=", StringComparison.OrdinalIgnoreCase))
                    clockCount = GetValue(maidataTxt[i]);
                else if (maidataTxt[i].StartsWith("&first="))
                    first = float.Parse(GetValue(maidataTxt[i]));
                else if (maidataTxt[i].StartsWith("&lv_") || maidataTxt[i].StartsWith("&inote_"))
                    for (var j = 1; j < 8 && i < maidataTxt.Length; j++)
                    {
                        if (maidataTxt[i].StartsWith("&lv_" + j + "="))
                            levels[j - 1] = GetValue(maidataTxt[i]);
                        if (maidataTxt[i].StartsWith("&inote_" + j + "="))
                        {
                            var TheNote = "";
                            TheNote += GetValue(maidataTxt[i]) + "\n";
                            i++;
                            for (; i < maidataTxt.Length; i++)
                            {
                                if (i < maidataTxt.Length)
                                    if (IsMaidataCommandLine(maidataTxt[i]))
                                        break;
                                TheNote += maidataTxt[i] + "\n";
                            }

                            fumens[j - 1] = TheNote;
                        }
                    }
                else
                    other_commands += maidataTxt[i].Trim() + "\n";

            other_commands = other_commands.Trim();
            return true;
        }
        catch (Exception e)
        {
            MessageBox.Show(
                string.Format(MainWindow.GetLocalizedString("ChartReadErrorBody"), i + 1, e.Message),
                MainWindow.GetLocalizedString("ChartReadErrorTitle"));
            return false;
        }
    }

    /// <summary>
    ///     Save the static data to maidata.txt
    /// </summary>
    /// <param name="filename">file path of maidata.txt</param>
    public static void SaveData(string filename)
    {
        var maidata = new List<string>
        {
            "&title=" + title,
            "&artist=" + artist,
            "&first=" + first,
            "&des=" + designer,
            BuildOtherCommandsForSave()
        };
        for (var i = 0; i < levels.Length; i++)
            if (levels[i] != null && levels[i] != "")
                maidata.Add("&lv_" + (i + 1) + "=" + levels[i].Trim());
        for (var i = 0; i < fumens.Length; i++)
            if (fumens[i] != null && fumens[i] != "")
                maidata.Add("&inote_" + (i + 1) + "=" + fumens[i].Trim());
        File.WriteAllLines(filename, maidata.ToArray());
    }

    private static string GetValue(string varline)
    {
        return varline.Substring(varline.IndexOf("=") + 1);
    }

    public static string GetWholeBpmText()
    {
        if (!string.IsNullOrWhiteSpace(wholeBpm))
            return wholeBpm.Trim();

        var commandValue = GetOtherCommandValue("wholebpm");
        if (!string.IsNullOrWhiteSpace(commandValue))
            return commandValue.Trim();

        foreach (var timing in timinglist)
        {
            if (timing.currentBpm > 0f)
                return FormatBpm(timing.currentBpm);
        }
        return "";
    }

    public static string GetDesignerText(int difficultyIndex)
    {
        if (!string.IsNullOrWhiteSpace(designer))
            return designer.Trim();

        var commandValue = GetOtherCommandValue("des_" + (difficultyIndex + 1));
        if (!string.IsNullOrWhiteSpace(commandValue))
            return commandValue.Trim();

        return designer ?? "";
    }

    public static string GetClockCountText()
    {
        if (!string.IsNullOrWhiteSpace(clockCount))
            return clockCount.Trim();

        var commandValue = GetOtherCommandValue("clock_count");
        return string.IsNullOrWhiteSpace(commandValue) ? "" : commandValue.Trim();
    }

    private static string BuildOtherCommandsForSave()
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(other_commands))
        {
            foreach (var line in other_commands.Split(
                         new[] { "\r\n", "\n" },
                         StringSplitOptions.None))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("&wholebpm=", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("&clock_count=", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (trimmed.Length > 0)
                    lines.Add(trimmed);
            }
        }

        if (!string.IsNullOrWhiteSpace(wholeBpm))
            lines.Add("&wholebpm=" + wholeBpm.Trim());
        if (!string.IsNullOrWhiteSpace(clockCount))
            lines.Add("&clock_count=" + clockCount.Trim());

        return string.Join("\n", lines);
    }

    private static string GetOtherCommandValue(string key)
    {
        if (string.IsNullOrWhiteSpace(other_commands))
            return "";

        var prefix = "&" + key + "=";
        foreach (var line in other_commands.Split(
                     new[] { "\r\n", "\n" },
                     StringSplitOptions.None))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return trimmed.Substring(prefix.Length);
        }
        return "";
    }

    private static string FormatBpm(float bpm)
    {
        return Math.Abs(bpm - MathF.Round(bpm)) < 0.001f
            ? MathF.Round(bpm).ToString("0")
            : bpm.ToString("0.###");
    }

    private static bool IsMaidataCommandLine(string line)
    {
        if (!line.StartsWith("&"))
            return false;

        // Editor-only section markers belong to the chart body even when
        // placed at the start of a line.
        if (IsEditorSectionMarker(line))
            return false;

        return true;
    }

    private static bool IsEditorSectionMarker(string line)
    {
        if (line.Length >= 5 && line[0] is '@' or '&' &&
            string.Equals(line.Substring(1, 4), "NULL", StringComparison.OrdinalIgnoreCase))
            return true;

        return line.Length >= 7 &&
               line[0] is '@' or '&' &&
               line.Substring(1, 6).All(Uri.IsHexDigit);
    }

    /// <summary>
    ///     This method serialize the fumen data and load it into the static class.
    /// </summary>
    /// <param name="text">fumen text</param>
    /// <param name="position">the position of the cusor, to get the return time</param>
    /// <returns>the song time at the position</returns>
    public static double Serialize(string text, long position = 0)
    {
        text = StripBlockComments(text);
        var _notelist = new List<SimaiTimingPoint>();
        var overlayNotes = new List<SimaiTimingPoint>();
        var _timinglist = new List<SimaiTimingPoint>();
        var svPoints     = new List<SvPoint>();
        var hsPoints     = new List<SpeedChange>();
        var spawnPoints  = new List<SpawnChange>();
        var bouncePoints = new List<BounceChange>();
        var colorPoints  = new List<ColorChange>();
        var sizePoints   = new List<SizeChange>();
        var alphaPoints  = new List<AlphaChange>();
        var displayPoints = new List<DisplayChange>();
        var subtitlePoints = new List<SubtitleChange>();
        var effectPoints = new List<EffectChange>();
        var mediaPoints = new List<MediaChange>();
        var meterPoints = new List<MeterChange>();
        double? trimStart = null;
        double? trimEnd = null;
        try
        {
            float bpm = 0;
            var curHSpeed = 1f;
            double time = first; //in seconds
            double requestedTime = 0;
            var beats = 4;
            var haveNote = false;
            var noteTemp = "";
            int Ycount = 0, Xcount = 0;

            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '|' && i + 1 < text.Length && text[i + 1] == '|')
                {
                    // Skip block comments.
                    Xcount++;
                    while (i < text.Length && text[i] != '\n')
                    {
                        i++;
                        Xcount++;
                    }

                    Ycount++;
                    Xcount = 0;
                    continue;
                }

                if (text[i] == '\n')
                {
                    Ycount++;
                    Xcount = 0;
                }
                else
                {
                    Xcount++;
                }

                if (i - 1 < position) requestedTime = time;

                if (text[i] == '@' && !haveNote &&
                    TryReadOverlayLine(
                        text, i, time, bpm, curHSpeed, Xcount, Ycount, position,
                        overlayNotes, out var overlayEnd, out var overlayCaretTime))
                {
                    if (overlayCaretTime.HasValue)
                        requestedTime = overlayCaretTime.Value;
                    Xcount += overlayEnd - i - 1;
                    i = overlayEnd - 1;
                    noteTemp = "";
                    haveNote = false;
                    continue;
                }

                if (text[i] is '@' or '&' && !haveNote)
                {
                    // @4/4, @3/4 and legacy ampersand markers only control the editor grid.
                    // They are deliberately ignored by the View chart serializer.
                    var meterEnd = text.IndexOfAny(new[] { '\r', '\n' }, i + 1);
                    if (meterEnd < 0) meterEnd = text.Length;
                    var meterText = text.Substring(i + 1, meterEnd - i - 1).Trim();
                    if (text[i] == '@' &&
                        (string.Equals(meterText, "start", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(meterText, "end", StringComparison.OrdinalIgnoreCase)))
                    {
                        if (string.Equals(meterText, "start", StringComparison.OrdinalIgnoreCase))
                            trimStart = time;
                        else
                            trimEnd = time;
                        Xcount += meterEnd - i - 1;
                        i = meterEnd - 1;
                        noteTemp = "";
                        continue;
                    }
                    var slash = meterText.IndexOf('/');
                    if (slash > 0 &&
                        int.TryParse(meterText.Substring(0, slash).Trim(), out var numerator) &&
                        int.TryParse(meterText.Substring(slash + 1).Trim(), out var denominator) &&
                        numerator > 0 && denominator > 0)
                    {
                        meterPoints.Add(new MeterChange
                        {
                            time = time,
                            numerator = numerator,
                            denominator = denominator
                        });
                        Xcount += meterEnd - i - 1;
                        i = meterEnd - 1;
                        noteTemp = "";
                        continue;
                    }

                    var markerLength = i + 4 < text.Length &&
                                       string.Equals(text.Substring(i + 1, 4), "NULL",
                                           StringComparison.OrdinalIgnoreCase)
                        ? 5
                        : i + 6 < text.Length &&
                          text.Substring(i + 1, 6).All(Uri.IsHexDigit)
                            ? 7
                            : 0;
                    if (markerLength > 0)
                    {
                        i += markerLength - 1;
                        Xcount += markerLength - 1;
                        noteTemp = "";
                        continue;
                    }
                }
                if (text[i] == '(')
                    //Get bpm
                {
                    haveNote = false;
                    noteTemp = "";
                    var bpm_s = "";
                    i++;
                    Xcount++;
                    while (text[i] != ')')
                    {
                        bpm_s += text[i];
                        i++;
                        Xcount++;
                    }

                    bpm = float.Parse(bpm_s);
                    //Console.WriteLine("BPM" + bpm);
                    continue;
                }

                if (text[i] == '{')
                    //Get beats
                {
                    haveNote = false;
                    noteTemp = "";
                    var beats_s = "";
                    i++;
                    Xcount++;
                    while (text[i] != '}')
                    {
                        beats_s += text[i];
                        i++;
                        Xcount++;
                    }

                    beats = int.Parse(beats_s);
                    //Console.WriteLine("BEAT" + beats);
                    continue;
                }

                if (text[i] == '<' && !haveNote && AlphaCommandBoundary.IsPotentialStart(text, i))
                {
                    var tokenEnd = text.IndexOf('>', i + 1);
                    var looksLikeAlphaCommand = true;
                    if (tokenEnd >= 0)
                    {
                        var token = text.Substring(i + 1, tokenEnd - i - 1);
                        if (TryParseMediaChange(token, time, bpm, out var mediaChange))
                        {
                            if (!string.IsNullOrEmpty(mediaChange.kind))
                                mediaPoints.Add(mediaChange);
                            Xcount += tokenEnd - i;
                            i = tokenEnd;
                            noteTemp = "";
                            continue;
                        }
                        if (TryParseTypedSvChange(token, time, svPoints))
                        {
                            Xcount += tokenEnd - i;
                            i = tokenEnd;
                            noteTemp = "";
                            continue;
                        }
                        if (token.StartsWith("SV*", StringComparison.OrdinalIgnoreCase) &&
                            float.TryParse(token.Substring(3).Trim(),
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out var svMultiplier))
                        {
                            svPoints.Add(new SvPoint { time = time, multiplier = svMultiplier });
                            Xcount += tokenEnd - i;
                            i = tokenEnd;
                            noteTemp = "";
                            continue;
                        }
                        if (TryParseTypedSpeedChange(token, time, hsPoints))
                        {
                            Xcount += tokenEnd - i;
                            i = tokenEnd;
                            noteTemp = "";
                            continue;
                        }
                        if (TryParseSpawnChange(token, time, spawnPoints))
                        {
                            Xcount += tokenEnd - i;
                            i = tokenEnd;
                            noteTemp = "";
                            continue;
                        }
                        if (TryParseBounceChange(token, time, bpm, bouncePoints))
                        {
                            Xcount += tokenEnd - i;
                            i = tokenEnd;
                            noteTemp = "";
                            continue;
                        }
                        if (token.StartsWith("HS*", StringComparison.OrdinalIgnoreCase) &&
                            float.TryParse(token.Substring(3).Trim(),
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out var hSpeed))
                        {
                            curHSpeed = hSpeed;
                            Xcount += tokenEnd - i;
                            i = tokenEnd;
                            noteTemp = "";
                            continue;
                        }
                        if (TryParseScreenEffect(token, time, bpm, out var effectChange))
                        {
                            if (!string.IsNullOrEmpty(effectChange.effect))
                                effectPoints.Add(effectChange);
                            Xcount += tokenEnd - i;
                            i = tokenEnd;
                            noteTemp = "";
                            continue;
                        }
                        if (TryParseSubtitleChange(token, time, bpm, out var subtitleChange))
                        {
                            subtitlePoints.Add(subtitleChange);
                            Xcount += tokenEnd - i;
                            i = tokenEnd;
                            noteTemp = "";
                            continue;
                        }
                        if (TryParseDisplayChange(token, time, bpm, out var displayChange))
                        {
                            if (!string.IsNullOrEmpty(displayChange.property))
                                displayPoints.Add(displayChange);
                            Xcount += tokenEnd - i;
                            i = tokenEnd;
                            noteTemp = "";
                            continue;
                        }
                        if (TryParseColorChange(token, time, colorPoints) ||
                            TryParseSizeChange(token, time, sizePoints) ||
                            TryParseAlphaChange(token, time, alphaPoints) ||
                            TryParseJudgeLineChange(token, time, bpm, colorPoints))
                        {
                            Xcount += tokenEnd - i;
                            i = tokenEnd;
                            noteTemp = "";
                            continue;
                        }

                        // An invalid or not-yet-supported Alpha command is still an angle
                        // command, not a slide. Validation reports it separately; consuming
                        // it here prevents malformed preview/playback notes at position zero.
                        if (looksLikeAlphaCommand)
                        {
                            Xcount += tokenEnd - i;
                            i = tokenEnd;
                            noteTemp = "";
                            continue;
                        }
                    }
                    else if (looksLikeAlphaCommand)
                    {
                        // While the user is typing, ignore the unfinished command through
                        // the current cell. It becomes active after the closing '>' exists.
                        var fragmentEnd = text.IndexOfAny(new[] { ',', '\r', '\n' }, i + 1);
                        if (fragmentEnd < 0)
                            fragmentEnd = text.Length;
                        Xcount += fragmentEnd - i - 1;
                        i = fragmentEnd - 1;
                        noteTemp = "";
                        continue;
                    }
                }

                if (text[i] == 'H')
                    //Get HS
                {
                    haveNote = false;
                    noteTemp = "";
                    var hs_s = "";
                    if (text[i + 1] == 'S' && text[i + 2] == '*')
                    {
                        i += 3;
                        Xcount += 3;
                    }

                    while (text[i] != '>')
                    {
                        hs_s += text[i];
                        i++;
                        Xcount++;
                    }

                    curHSpeed = float.Parse(hs_s);
                    //Console.WriteLine("HS" + curHSpeed);
                    continue;
                }

                if (text[i] == 'S' && !haveNote && i + 1 < text.Length && text[i + 1] == 'V'
                    && i + 2 < text.Length && text[i + 2] == '*')
                {
                    noteTemp = "";
                    var sv_s = "";
                    i += 3; // skip 'S','V','*'
                    Xcount += 3;
                    while (i < text.Length && text[i] != '>')
                    {
                        sv_s += text[i];
                        i++;
                        Xcount++;
                    }
                    if (float.TryParse(sv_s, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var svMult))
                    {
                        svPoints.Add(new SvPoint { time = time, multiplier = svMult });
                    }
                    continue;
                }

                if (isNote(text[i])) haveNote = true;
                if (haveNote && text[i] != ',') noteTemp += text[i];
                if (text[i] == ',')
                {
                    if (haveNote)
                    {
                        if (noteTemp.Contains('`'))
                        {
                            // Fake each notes are separated by backticks.
                            var fakeEachList = noteTemp.Split('`');
                            var fakeTime = time;
                            var timeInterval = 1.875 / bpm; // One 128th-note interval.
                            foreach (var fakeEachGroup in fakeEachList)
                            {
                                Console.WriteLine(fakeEachGroup);
                                _notelist.Add(new SimaiTimingPoint(fakeTime, Xcount, Ycount, fakeEachGroup, bpm,
                                    curHSpeed));
                                fakeTime += timeInterval;
                            }
                        }
                        else
                        {
                            _notelist.Add(new SimaiTimingPoint(time, Xcount, Ycount, noteTemp, bpm, curHSpeed));
                        }
                        //Console.WriteLine("Note:" + noteTemp);

                        noteTemp = "";
                    }

                    _timinglist.Add(new SimaiTimingPoint(time, Xcount, Ycount, "", bpm));


                    time += 1d / (bpm / 60d) * 4d / beats;
                    //Console.WriteLine(time);
                    haveNote = false;
                }
            }

            AddOverlayNotes(_notelist, overlayNotes);
            notelist  = _notelist;
            timinglist = _timinglist;
            svTable    = svPoints;
            hsTable    = hsPoints;
            spawnTable = spawnPoints;
            bounceTable = bouncePoints;
            colorTable = colorPoints;
            sizeTable  = sizePoints;
            alphaTable = alphaPoints;
            displayTable = displayPoints;
            subtitleTable = subtitlePoints;
            effectTable = effectPoints;
            mediaTable = mediaPoints;
            meterTable = meterPoints;
            mediaTrimStart = trimStart;
            mediaTrimEnd = trimEnd;
            return requestedTime;
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            return 0;
        }
    }

    private static bool TryReadOverlayLine(
        string text,
        int markerIndex,
        double startTime,
        float bpm,
        float hSpeed,
        int markerColumn,
        int line,
        long caretPosition,
        ICollection<SimaiTimingPoint> output,
        out int lineEnd,
        out double? caretTime)
    {
        lineEnd = markerIndex;
        caretTime = null;
        if (markerIndex + 2 >= text.Length || text[markerIndex + 1] != '{')
            return false;

        lineEnd = text.IndexOfAny(new[] { '\r', '\n' }, markerIndex + 2);
        if (lineEnd < 0)
            lineEnd = text.Length;
        var closeBrace = text.IndexOf('}', markerIndex + 2);
        if (closeBrace < 0 || closeBrace >= lineEnd ||
            !int.TryParse(text.Substring(markerIndex + 2, closeBrace - markerIndex - 2),
                out var division) || division <= 0)
            return false;

        var localBpm = bpm;
        var localHSpeed = hSpeed;
        var localDivision = division;
        var slotTime = startTime;
        var slotColumn = closeBrace + 1;
        var slotContent = new StringBuilder();
        double? localCaretTime = null;

        void AddSlot(int commaIndex)
        {
            var content = slotContent.ToString().Trim();
            if (localBpm > 0f && content.Any(isNote))
            {
                if (content.Contains('`'))
                {
                    var fakeTime = slotTime;
                    foreach (var fakeEachGroup in content.Split('`'))
                    {
                        if (!string.IsNullOrWhiteSpace(fakeEachGroup))
                            output.Add(new SimaiTimingPoint(
                                fakeTime, markerColumn + slotColumn - markerIndex, line,
                                fakeEachGroup, localBpm, localHSpeed));
                        fakeTime += 1.875d / localBpm;
                    }
                }
                else
                {
                    output.Add(new SimaiTimingPoint(
                        slotTime, markerColumn + slotColumn - markerIndex, line,
                        content, localBpm, localHSpeed));
                }
            }

            if (caretPosition >= slotColumn && caretPosition <= commaIndex)
                localCaretTime = slotTime;
            if (localBpm > 0f && localDivision > 0)
                slotTime += 240d / localBpm / localDivision;
            slotContent.Clear();
            slotColumn = commaIndex + 1;
        }

        for (var index = closeBrace + 1; index < lineEnd; index++)
        {
            if (text[index] == '<' && AlphaCommandBoundary.IsPotentialStart(text, index))
            {
                var commandEnd = text.IndexOf('>', index + 1);
                if (commandEnd >= 0 && commandEnd < lineEnd)
                {
                    var token = text.Substring(index + 1, commandEnd - index - 1);
                    if (token.StartsWith("HS*", StringComparison.OrdinalIgnoreCase) &&
                        float.TryParse(token[3..].Trim(), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out var parsedHSpeed))
                    {
                        localHSpeed = parsedHSpeed;
                    }
                    index = commandEnd;
                    slotColumn = commandEnd + 1;
                    continue;
                }
            }

            if (text[index] == '(')
            {
                var bpmEnd = text.IndexOf(')', index + 1);
                if (bpmEnd >= 0 && bpmEnd < lineEnd &&
                    float.TryParse(text.Substring(index + 1, bpmEnd - index - 1),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedBpm) &&
                    parsedBpm > 0f)
                {
                    localBpm = parsedBpm;
                    index = bpmEnd;
                    slotColumn = bpmEnd + 1;
                    continue;
                }
            }

            if (text[index] == '{')
            {
                var divisionEnd = text.IndexOf('}', index + 1);
                if (divisionEnd >= 0 && divisionEnd < lineEnd &&
                    int.TryParse(text.Substring(index + 1, divisionEnd - index - 1),
                        out var parsedDivision) && parsedDivision > 0)
                {
                    localDivision = parsedDivision;
                    index = divisionEnd;
                    slotColumn = divisionEnd + 1;
                    continue;
                }
            }

            if (text[index] == ',')
            {
                AddSlot(index);
                continue;
            }

            slotContent.Append(text[index]);
        }

        if (caretPosition >= slotColumn && caretPosition <= lineEnd)
            localCaretTime = slotTime;
        caretTime = localCaretTime;
        return true;
    }

    private static void AddOverlayNotes(
        List<SimaiTimingPoint> mainNotes,
        IEnumerable<SimaiTimingPoint> overlayNotes)
    {
        mainNotes.AddRange(overlayNotes);
        mainNotes.Sort((left, right) => left.time.CompareTo(right.time));
    }

    private static string StripBlockComments(string text)
    {
        var result = text.ToCharArray();
        var inComment = false;
        for (var i = 0; i < result.Length; i++)
        {
            if (!inComment && i + 1 < result.Length && result[i] == '|' && result[i + 1] == '*')
            {
                inComment = true;
                result[i] = result[i + 1] = ' ';
                i++;
                continue;
            }
            if (inComment && i + 1 < result.Length && result[i] == '*' && result[i + 1] == '|')
            {
                result[i] = result[i + 1] = ' ';
                inComment = false;
                i++;
                continue;
            }
            if (inComment && result[i] != '\r' && result[i] != '\n')
                result[i] = ' ';
        }
        return new string(result);
    }

    internal static IReadOnlyList<AlphaCommandError> ValidateAlphaCommands(string text)
    {
        var source = StripBlockComments(text);
        var errors = new List<AlphaCommandError>();
        var line = 0;
        var lineStart = 0;

        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] == '\n')
            {
                line++;
                lineStart = i + 1;
                continue;
            }
            if (source[i] == '|' && i + 1 < source.Length && source[i + 1] == '|')
            {
                while (i < source.Length && source[i] != '\n')
                    i++;
                if (i < source.Length)
                {
                    line++;
                    lineStart = i + 1;
                }
                continue;
            }
            if (source[i] != '<' || !AlphaCommandBoundary.IsPotentialStart(source, i))
                continue;

            var end = source.IndexOf('>', i + 1);
            var newline = source.IndexOf('\n', i + 1);
            if (end < 0 || newline >= 0 && newline < end)
            {
                errors.Add(new AlphaCommandError(
                    i - lineStart,
                    line,
                    MainWindow.GetLocalizedString("AlphaMissingClose")));
                continue;
            }

            var token = source.Substring(i + 1, end - i - 1).Trim();
            if (!TryValidateAlphaCommand(token, out var message))
                errors.Add(new AlphaCommandError(i - lineStart, line, message));
            i = end;
        }
        return errors;
    }

    private static bool TryValidateAlphaCommand(string token, out string message)
    {
        message = MainWindow.GetLocalizedString("AlphaFormatError");
        var separator = token.IndexOf('*');
        if (separator <= 0)
        {
            message = MainWindow.GetLocalizedString("AlphaMissingAsterisk");
            return false;
        }

        var command = token[..separator].Trim().ToUpperInvariant();
        const float bpm = 120f;
        switch (command)
        {
            case "SV":
            {
                var points = new List<SvPoint>();
                if (TryParseTypedSvChange(token, 0d, points))
                    return points.Count > 0;
                return float.TryParse(token[(separator + 1)..].Trim(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out _);
            }
            case "HS":
            {
                var points = new List<SpeedChange>();
                if (TryParseTypedSpeedChange(token, 0d, points))
                    return points.Count > 0;
                return float.TryParse(token[(separator + 1)..].Trim(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out _);
            }
            case "SPAWN":
            {
                var points = new List<SpawnChange>();
                TryParseSpawnChange(token, 0d, points);
                return points.Count > 0;
            }
            case "BOUNCE":
            {
                var points = new List<BounceChange>();
                TryParseBounceChange(token, 0d, bpm, points);
                return points.Count > 0;
            }
            case "COLOR":
            {
                var points = new List<ColorChange>();
                TryParseColorChange(token, 0d, points);
                return points.Count > 0 && points.All(point =>
                    string.Equals(point.color, "NULL", StringComparison.OrdinalIgnoreCase) ||
                    point.color.Length == 6 && point.color.All(Uri.IsHexDigit));
            }
            case "SIZE":
            {
                var points = new List<SizeChange>();
                TryParseSizeChange(token, 0d, points);
                return points.Count > 0;
            }
            case "ALPHA":
            {
                var points = new List<AlphaChange>();
                TryParseAlphaChange(token, 0d, points);
                return points.Count > 0;
            }
            case "JLINE":
            {
                var points = new List<ColorChange>();
                TryParseJudgeLineChange(token, 0d, bpm, points);
                return points.Count > 0;
            }
            case "TEXT":
            {
                TryParseSubtitleChange(token, 0d, bpm, out var subtitle);
                var body = token[(separator + 1)..].Trim();
                return !string.IsNullOrWhiteSpace(subtitle.text) &&
                       (!body.StartsWith('(') || body.EndsWith(')'));
            }
            case "AUDIO":
            case "PVOVERLAY":
                return TryParseMediaChange(token, 0d, bpm, out var media) &&
                       !string.IsNullOrEmpty(media.kind);
        }

        if (TryParseScreenEffect(token, 0d, bpm, out var effect))
            return !string.IsNullOrEmpty(effect.effect);
        if (TryParseDisplayChange(token, 0d, bpm, out var display))
            return !string.IsNullOrEmpty(display.property);

        message = string.Format(MainWindow.GetLocalizedString("AlphaUnknownCommand"), command);
        return false;
    }

    public static void ClearNoteListPlayedState()
    {
        notelist.Sort((x, y) => x.time.CompareTo(y.time));
        for (var i = 0; i < notelist.Count; i++) notelist[i].havePlayed = false;
    }

    private static bool TryParseMediaChange(string token, double time, float bpm, out MediaChange change)
    {
        change = new MediaChange();
        var separator = token.IndexOf('*');
        if (separator <= 0)
            return false;

        var command = token[..separator].Trim().ToUpperInvariant();
        if (command is not ("AUDIO" or "PVOVERLAY"))
            return false;

        var body = token[(separator + 1)..].Trim();
        if (body.Length < 3 || body[0] != '(' || body[^1] != ')')
            return true;

        var values = body[1..^1].Split(',', StringSplitOptions.TrimEntries);
        if (values.Length < 1 || !bool.TryParse(values[0], out var enabled))
            return true;

        change.time = time;
        change.kind = command == "AUDIO" ? "audio" : "pvOverlay";
        change.enabled = enabled;
        if (!enabled)
        {
            if (command == "AUDIO" && values.Length != 1)
                change.kind = "";
            else if (command == "PVOVERLAY" &&
                     (values.Length > 2 ||
                      values.Length == 2 &&
                      (!TryParseCommandDuration(values[1], bpm, out change.transition) ||
                       change.transition < 0f)))
                change.kind = "";
            return true;
        }

        var expectedMaximum = command == "PVOVERLAY" ? 3 : 2;
        if (values.Length < 2 || values.Length > expectedMaximum)
        {
            change.kind = "";
            return true;
        }

        if (command == "PVOVERLAY" && values.Length == 3 &&
            (!TryParseCommandDuration(values[2], bpm, out change.transition) ||
             change.transition < 0f))
        {
            change.kind = "";
            return true;
        }

        var mediaPath = values[1].Trim().Trim('"').Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(mediaPath) || Path.IsPathRooted(mediaPath) ||
            mediaPath.Split('/').Any(part => part == ".."))
        {
            change.kind = "";
            return true;
        }

        var extension = Path.GetExtension(mediaPath);
        var supported = command == "AUDIO"
            ? extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase) ||
              extension.Equals(".wav", StringComparison.OrdinalIgnoreCase) ||
              extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
            : extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
              extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
              extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
              extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase);
        if (!supported)
        {
            change.kind = "";
            return true;
        }

        change.path = mediaPath;
        return true;
    }

    // Reserved note-type keys usable in <COLOR*key=..> / <ALPHA*key=..>.
    private static readonly string[] ColorNoteTypes =
        { "tap", "each", "hold", "slide", "star", "break", "touch", "touchhold" };

    private static bool TryParseTypedSvChange(string token, double time, List<SvPoint> into)
    {
        if (!token.StartsWith("SV*", StringComparison.OrdinalIgnoreCase))
            return false;
        var body = token[3..].Trim();
        if (!body.Contains('='))
            return false;

        foreach (var pair in body.Split(','))
        {
            var kv = pair.Split('=', 2, StringSplitOptions.TrimEntries);
            if (kv.Length != 2 || !ColorNoteTypes.Contains(kv[0], StringComparer.OrdinalIgnoreCase))
                continue;
            if (string.Equals(kv[1], "NULL", StringComparison.OrdinalIgnoreCase))
                into.Add(new SvPoint { time = time, noteType = kv[0].ToLowerInvariant(), reset = true });
            else if (float.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                into.Add(new SvPoint
                {
                    time = time,
                    noteType = kv[0].ToLowerInvariant(),
                    multiplier = value
                });
        }
        return true;
    }

    private static bool TryParseTypedSpeedChange(string token, double time, List<SpeedChange> into)
    {
        if (!token.StartsWith("HS*", StringComparison.OrdinalIgnoreCase))
            return false;
        var body = token[3..].Trim();
        if (!body.Contains('='))
            return false;

        foreach (var pair in body.Split(','))
        {
            var kv = pair.Split('=', 2, StringSplitOptions.TrimEntries);
            if (kv.Length != 2 || !ColorNoteTypes.Contains(kv[0], StringComparer.OrdinalIgnoreCase))
                continue;
            var value = 1f;
            if (!string.Equals(kv[1], "NULL", StringComparison.OrdinalIgnoreCase) &&
                !float.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                continue;
            into.Add(new SpeedChange
            {
                time = time,
                noteType = kv[0].ToLowerInvariant(),
                multiplier = value
            });
        }
        return true;
    }

    private static readonly string[] SpawnNoteTypes =
        { "tap", "each", "hold", "star", "break" };

    private static bool TryParseSpawnChange(string token, double time, List<SpawnChange> into)
    {
        if (!token.StartsWith("SPAWN*", StringComparison.OrdinalIgnoreCase))
            return false;

        static bool TryValue(string text, out float radius, out bool reset)
        {
            reset = string.Equals(text.Trim(), "NULL", StringComparison.OrdinalIgnoreCase);
            if (reset)
            {
                radius = 1.225f;
                return true;
            }

            return float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture,
                       out radius) &&
                   radius is >= -4.8f and <= 4.8f;
        }

        var body = token.Substring(6).Trim();
        if (!body.Contains('='))
        {
            if (TryValue(body, out var radius, out var reset))
                into.Add(new SpawnChange
                {
                    time = time,
                    radius = radius,
                    reset = reset
                });
            return true;
        }

        foreach (var pair in body.Split(','))
        {
            var kv = pair.Split('=', 2, StringSplitOptions.TrimEntries);
            if (kv.Length != 2 ||
                !SpawnNoteTypes.Contains(kv[0], StringComparer.OrdinalIgnoreCase) ||
                !TryValue(kv[1], out var radius, out var reset))
                continue;
            into.Add(new SpawnChange
            {
                time = time,
                noteType = kv[0].ToLowerInvariant(),
                radius = radius,
                reset = reset
            });
        }
        return true;
    }

    private static bool TryParseBounceChange(
        string token,
        double time,
        float bpm,
        List<BounceChange> into)
    {
        if (!token.StartsWith("BOUNCE*", StringComparison.OrdinalIgnoreCase))
            return false;
        var body = token[7..].Trim();
        var defaultTypes = new[] { "tap", "star", "each", "hold" };

        static bool IsBounceType(string value) =>
            value.Equals("tap", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("star", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("each", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("hold", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("break", StringComparison.OrdinalIgnoreCase);

        if (!body.Contains('='))
        {
            var reset = string.Equals(body, "NULL", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(body, "FALSE", StringComparison.OrdinalIgnoreCase);
            var duration = 0f;
            if (!reset && (!TryParseCommandDuration(body, bpm, out duration) || duration <= 0f))
                return true;
            foreach (var noteType in defaultTypes)
                into.Add(new BounceChange
                {
                    time = time,
                    noteType = noteType,
                    duration = reset ? 0f : duration,
                    reset = reset
                });
            return true;
        }

        foreach (var pair in body.Split(','))
        {
            var keyValue = pair.Split('=', 2, StringSplitOptions.TrimEntries);
            if (keyValue.Length != 2 || !IsBounceType(keyValue[0]))
                continue;
            var reset = string.Equals(keyValue[1], "NULL", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(keyValue[1], "FALSE", StringComparison.OrdinalIgnoreCase);
            var duration = 0f;
            if (!reset && (!TryParseCommandDuration(keyValue[1], bpm, out duration) || duration <= 0f))
                continue;
            into.Add(new BounceChange
            {
                time = time,
                noteType = keyValue[0].ToLowerInvariant(),
                duration = reset ? 0f : duration,
                reset = reset
            });
        }
        return true;
    }

    private static bool TryParseColorChange(string token, double time, List<ColorChange> into)
    {
        if (!token.StartsWith("COLOR*", StringComparison.OrdinalIgnoreCase))
            return false;
        var body = token.Substring(6).Trim();
        if (string.Equals(body, "NULL", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var t in ColorNoteTypes)
                into.Add(new ColorChange { time = time, noteType = t, color = "NULL" });
        }
        else if (!body.Contains('='))
        {
            var color = body.TrimStart('#');
            if (color.Length == 6 && color.All(Uri.IsHexDigit))
                foreach (var t in ColorNoteTypes)
                    into.Add(new ColorChange { time = time, noteType = t, color = color });
        }
        else
        {
            foreach (var pair in body.Split(','))
            {
                var kv = pair.Trim().Split('=');
                if (kv.Length != 2)
                    continue;
                var noteType = kv[0].Trim();
                var colorStr = kv[1].Trim();
                if (string.Equals(colorStr, "NULL", StringComparison.OrdinalIgnoreCase))
                    into.Add(new ColorChange { time = time, noteType = noteType, color = "NULL" });
                else
                {
                    var color = colorStr.TrimStart('#');
                    if (color.Length == 6 && color.All(Uri.IsHexDigit))
                        into.Add(new ColorChange { time = time, noteType = noteType, color = color });
                }
            }
        }
        return true;
    }

    // Global size does not scale slide bodies; slide size must be set explicitly.
    private static bool TryParseSizeChange(string token, double time, List<SizeChange> into)
    {
        if (!token.StartsWith("SIZE*", StringComparison.OrdinalIgnoreCase))
            return false;
        var body = token.Substring(5).Trim();
        if (body.Contains('='))
        {
            foreach (Match match in Regex.Matches(body,
                         @"(?<type>[A-Za-z]+)\s*=\s*(?<value>NULL|[-+]?\d*\.?\d+|\(\s*[-+]?\d*\.?\d+\s*,\s*[-+]?\d*\.?\d+\s*\))",
                         RegexOptions.IgnoreCase))
            {
                var noteType = match.Groups["type"].Value.Trim();
                var val = match.Groups["value"].Value.Trim();
                if (string.Equals(val, "NULL", StringComparison.OrdinalIgnoreCase))
                    into.Add(NewSizeChange(time, noteType, 1f, 1f));
                else if (TryParseScalePair(val, out var x, out var y))
                    into.Add(NewSizeChange(time, noteType, x, y));
                else if (float.TryParse(val, System.Globalization.NumberStyles.Float,
                             System.Globalization.CultureInfo.InvariantCulture, out var s))
                    into.Add(NewSizeChange(time, noteType, s, s));
            }
        }
        else if (string.Equals(body, "NULL", StringComparison.OrdinalIgnoreCase))
        {
            into.Add(NewSizeChange(time, null, 1f, 1f));
        }
        else if (TryParseScalePair(body, out var x, out var y))
        {
            into.Add(NewSizeChange(time, null, x, y));
        }
        else if (float.TryParse(body, System.Globalization.NumberStyles.Float,
                     System.Globalization.CultureInfo.InvariantCulture, out var scale))
        {
            into.Add(NewSizeChange(time, null, scale, scale));
        }
        return true;
    }

    private static SizeChange NewSizeChange(double time, string? noteType, float x, float y) => new()
    {
        time = time,
        noteType = noteType,
        scale = MathF.Sqrt(MathF.Abs(x * y)),
        scaleX = x,
        scaleY = y
    };

    private static bool TryParseScalePair(string value, out float x, out float y)
    {
        x = y = 1f;
        var match = Regex.Match(value,
            @"^\(\s*(?<x>[-+]?\d*\.?\d+)\s*,\s*(?<y>[-+]?\d*\.?\d+)\s*\)$");
        return match.Success &&
               float.TryParse(match.Groups["x"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out x) &&
               float.TryParse(match.Groups["y"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out y);
    }

    private static bool TryParseAlphaChange(string token, double time, List<AlphaChange> into)
    {
        if (!token.StartsWith("ALPHA*", StringComparison.OrdinalIgnoreCase))
            return false;
        var body = token.Substring(6).Trim();
        if (body.Contains('='))
        {
            foreach (var pair in body.Split(','))
            {
                var kv = pair.Trim().Split('=');
                if (kv.Length != 2)
                    continue;
                var noteType = kv[0].Trim();
                var val = kv[1].Trim();
                if (string.Equals(val, "NULL", StringComparison.OrdinalIgnoreCase))
                    into.Add(new AlphaChange { time = time, noteType = noteType, alpha = 1f });
                else if (float.TryParse(val, System.Globalization.NumberStyles.Float,
                             System.Globalization.CultureInfo.InvariantCulture, out var ptAlpha))
                    into.Add(new AlphaChange { time = time, noteType = noteType, alpha = Math.Clamp(ptAlpha, 0f, 1f) });
            }
        }
        else if (string.Equals(body, "NULL", StringComparison.OrdinalIgnoreCase))
        {
            into.Add(new AlphaChange { time = time, noteType = null, alpha = 1f });
        }
        else if (float.TryParse(body, System.Globalization.NumberStyles.Float,
                     System.Globalization.CultureInfo.InvariantCulture, out var naAlpha))
        {
            into.Add(new AlphaChange { time = time, noteType = null, alpha = Math.Clamp(naAlpha, 0f, 1f) });
        }
        return true;
    }

    private static bool TryParseJudgeLineChange(
        string token,
        double time,
        float bpm,
        List<ColorChange> into)
    {
        if (!token.StartsWith("JLINE*", StringComparison.OrdinalIgnoreCase))
            return false;

        var body = token.Substring(6).Trim();
        if (body.Length >= 2 && body[0] == '(' && body[^1] == ')')
            body = body.Substring(1, body.Length - 2);

        var values = body.Split(',', StringSplitOptions.TrimEntries);
        if (values.Length is < 1 or > 2)
            return true;

        var color = values[0].Trim().TrimStart('#');
        if (!string.Equals(color, "NULL", StringComparison.OrdinalIgnoreCase) &&
            (color.Length is not (6 or 8) || !color.All(Uri.IsHexDigit)))
            return true;

        var duration = 0f;
        if (values.Length == 2 && !TryParseCommandDuration(values[1], bpm, out duration))
            return true;

        into.Add(new ColorChange
        {
            time = time,
            noteType = "judgeline",
            color = color.ToUpperInvariant(),
            duration = Math.Max(0f, duration)
        });
        return true;
    }

    private static bool TryParseDisplayChange(string token, double time, float bpm, out DisplayChange change)
    {
        change = new DisplayChange();
        var separator = token.IndexOf('*');
        if (separator <= 0)
            return false;

        var canonicalProperty = token.Substring(0, separator).Trim().ToUpperInvariant() switch
        {
            "SHOWJUDGELINE" => "ShowJudgeLine",
            "SHOWJUDGEAREA" => "ShowJudgeArea",
            "SHOWJUDGEINFO" => "ShowJudgeInfo",
            "SHOWCOMBOINFO" => "ShowComboInfo",
            "OUTERBRIGHTNESS" => "OuterBrightness",
            "INNERBRIGHTNESS" => "InnerBrightness",
            "SHOWJUDGETEXT" => "ShowJudgeText",
            "COMBODISPLAY" => "ComboDisplay",
            _ => null
        };
        if (canonicalProperty == null)
            return false;

        var body = token.Substring(separator + 1).Trim();
        if (body.Length < 5 || body[0] != '(' || body[^1] != ')')
            return true;

        var values = body.Substring(1, body.Length - 2).Split(',');
        if (values.Length is < 1 or > 2)
            return true;
        var duration = 0f;
        if (values.Length == 2 && !TryParseCommandDuration(values[1], bpm, out duration))
            return true;

        float target;
        if (canonicalProperty == "ComboDisplay")
        {
            if (!TryParseComboDisplay(values[0].Trim(), out var mode))
                return true;
            target = (float)mode;
        }
        else if (canonicalProperty.StartsWith("Show", StringComparison.Ordinal))
        {
            if (!bool.TryParse(values[0].Trim(), out var enabled))
                return true;
            target = enabled ? 1f : 0f;
        }
        else if (!float.TryParse(values[0].Trim(), System.Globalization.NumberStyles.Float,
                     System.Globalization.CultureInfo.InvariantCulture, out target))
        {
            return true;
        }

        change = new DisplayChange
        {
            time = time,
            property = canonicalProperty,
            target = canonicalProperty == "ComboDisplay" ? target : Math.Clamp(target, 0f, 1f),
            duration = Math.Max(0f, duration)
        };
        return true;
    }

    private static bool TryParseComboDisplay(string value, out EditorComboIndicator mode)
    {
        mode = EditorComboIndicator.None;
        var normalized = value.Trim().Replace(" ", "").Replace("_", "");
        if (int.TryParse(normalized, out var numeric) &&
            Enum.IsDefined(typeof(EditorComboIndicator), numeric))
        {
            mode = (EditorComboIndicator)numeric;
            return true;
        }

        var aliases = new Dictionary<string, EditorComboIndicator>(StringComparer.OrdinalIgnoreCase)
        {
            ["NONE"] = EditorComboIndicator.None,
            ["OFF"] = EditorComboIndicator.None,
            ["COMBO"] = EditorComboIndicator.Combo,
            ["SCORE"] = EditorComboIndicator.ScoreClassic,
            ["SCORECLASSIC"] = EditorComboIndicator.ScoreClassic,
            ["ACHIEVEMENT"] = EditorComboIndicator.AchievementClassic,
            ["ACC"] = EditorComboIndicator.AchievementClassic,
            ["ACHIEVEMENTCLASSIC"] = EditorComboIndicator.AchievementClassic,
            ["ACCDOWN"] = EditorComboIndicator.AchievementDownClassic,
            ["ACHIEVEMENTDOWNCLASSIC"] = EditorComboIndicator.AchievementDownClassic,
            ["DXACC"] = EditorComboIndicator.AchievementDeluxe,
            ["ACHIEVEMENTDELUXE"] = EditorComboIndicator.AchievementDeluxe,
            ["DXACCDOWN"] = EditorComboIndicator.AchievementDownDeluxe,
            ["ACHIEVEMENTDOWNDELUXE"] = EditorComboIndicator.AchievementDownDeluxe,
            ["DXSCORE"] = EditorComboIndicator.ScoreDeluxe,
            ["SCOREDELUXE"] = EditorComboIndicator.ScoreDeluxe,
            ["CSCORE"] = EditorComboIndicator.CScoreDedeluxe,
            ["CSCOREDEDX"] = EditorComboIndicator.CScoreDedeluxe,
            ["CSCOREDEDXDOWN"] = EditorComboIndicator.CScoreDownDedeluxe
        };
        if (aliases.TryGetValue(normalized, out mode))
            return true;

        return Enum.TryParse(value, true, out mode);
    }

    private static bool TryParseSubtitleChange(string token, double time, float bpm, out SubtitleChange change)
    {
        change = new SubtitleChange();
        if (!token.StartsWith("TEXT*", StringComparison.OrdinalIgnoreCase))
            return false;

        var body = token.Substring(5).Trim();
        var duration = -1f;
        var content = body;

        if (body.Length >= 3 && body[0] == '(' && body[^1] == ')')
        {
            var inner = body.Substring(1, body.Length - 2);
            var separator = inner.LastIndexOf(',');
            if (separator >= 0 &&
                TryParseCommandDuration(inner.Substring(separator + 1), bpm, out var parsedDuration))
            {
                content = inner.Substring(0, separator).Trim();
                duration = Math.Max(0f, parsedDuration);
            }
            else
            {
                content = inner;
            }
        }

        change = new SubtitleChange
        {
            time = time,
            text = content,
            duration = duration
        };
        return true;
    }

    private static bool TryParseScreenEffect(string token, double time, float bpm, out EffectChange change)
    {
        change = new EffectChange();
        var separator = token.IndexOf('*');
        if (separator <= 0)
            return false;

        var effect = token.Substring(0, separator).Trim().ToUpperInvariant() switch
        {
            "GAUSSIAN" => "Gaussian",
            "NEON" => "Neon",
            "TRAIL" => "Trail",
            "FADE" => "Flash",
            "BRIGHTNESS" => "Brightness",
            "SATURATION" => "Saturation",
            "CONTRAST" => "Contrast",
            "RAINBOW" => "Rainbow",
            "FLASH" => "Flash",
            "VIGNETTE" => "Vignette",
            "ZOOM" => "Zoom",
            "GLITCH" => "Glitch",
            "TVNOISE" => "TVNoise",
            "HUE" => "Hue",
            "TINT" => "Tint",
            "MOVE" => "Move",
            "ROTATE" => "Rotate",
            "SHAKE" => "Shake",
            _ => null
        };
        if (effect == null)
            return false;

        var body = token.Substring(separator + 1).Trim();
        if (body.Length < 5 || body[0] != '(' || body[^1] != ')')
            return true;

        var values = body.Substring(1, body.Length - 2).Split(',');
        float F(int i) => float.TryParse(values[i].Trim(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? v
            : float.NaN;
        float D(int i) => TryParseCommandDuration(values[i], bpm, out var v) ? v : float.NaN;

        if (values.Length >= 1 && bool.TryParse(values[0].Trim(), out var enabled))
        {
            var state = new EffectChange
            {
                time = time,
                effect = effect,
                stateful = true,
                enabled = enabled
            };

            if (!enabled)
            {
                if (values.Length > 2)
                    return true;
                var transition = values.Length == 2 ? D(1) : 0f;
                if (float.IsNaN(transition))
                    return true;
                state.transition = Math.Max(0f, transition);
                state.duration = state.transition;
                change = state;
                return true;
            }

            var transitionIndex = -1;
            switch (effect)
            {
                case "Move":
                {
                    if (values.Length is < 3 or > 4)
                        return true;
                    var dx = F(1);
                    var dy = F(2);
                    if (float.IsNaN(dx) || float.IsNaN(dy))
                        return true;
                    state.intensity = 1f;
                    state.paramA = dx;
                    state.paramB = dy;
                    transitionIndex = values.Length >= 4 ? 3 : -1;
                    break;
                }
                case "Tint":
                {
                    if (values.Length is < 3 or > 4)
                        return true;
                    var hex = values[1].Trim().TrimStart('#');
                    var amount = F(2);
                    if (float.IsNaN(amount) || hex.Length != 6 || !hex.All(Uri.IsHexDigit))
                        return true;
                    state.intensity = amount;
                    state.color = hex.ToUpperInvariant();
                    transitionIndex = values.Length >= 4 ? 3 : -1;
                    break;
                }
                case "Shake":
                {
                    if (values.Length is < 3 or > 5)
                        return true;
                    var strength = F(1);
                    var frequency = F(2);
                    if (float.IsNaN(strength) || float.IsNaN(frequency) || frequency <= 0f)
                        return true;
                    state.intensity = strength;
                    state.paramA = frequency;
                    if (values.Length >= 4 && !string.IsNullOrWhiteSpace(values[3]))
                    {
                        var angle = F(3);
                        if (float.IsNaN(angle))
                            return true;
                        state.hasDirection = true;
                        state.paramB = angle * (float)Math.PI / 180f;
                    }
                    if (values.Length == 5)
                    {
                        transitionIndex = 4;
                    }
                    break;
                }
                default:
                {
                    if (values.Length is < 2 or > 3)
                        return true;
                    var intensity = F(1);
                    if (float.IsNaN(intensity))
                        return true;
                    state.intensity = token.StartsWith("FADE*", StringComparison.OrdinalIgnoreCase)
                        ? -Math.Abs(intensity)
                        : intensity;
                    transitionIndex = values.Length >= 3 ? 2 : -1;
                    break;
                }
            }

            if (transitionIndex >= 0)
            {
                var transition = D(transitionIndex);
                if (float.IsNaN(transition))
                    return true;
                state.transition = Math.Max(0f, transition);
                state.duration = state.transition;
            }

            change = state;
            return true;
        }

        if (values.Length == 2)
        {
            var duration = D(0);
            var intensity = F(1);
            if (float.IsNaN(duration) || float.IsNaN(intensity))
                return true;

            change = new EffectChange
            {
                time = time,
                effect = effect,
                duration = Math.Max(0f, duration),
                intensity = token.StartsWith("FADE*", StringComparison.OrdinalIgnoreCase)
                    ? -Math.Abs(intensity)
                    : intensity
            };
            if (effect == "Tint")
                change.color = "FFFFFF";
            else if (effect == "Move")
            {
                change.intensity = 1f;
                change.paramA = intensity;
                change.paramB = 0f;
            }
            return true;
        }

        if (values.Length < 4)
            return true;
        var attack = D(0);
        var holdFor = D(1);
        var release = D(2);
        if (float.IsNaN(attack) || float.IsNaN(holdFor) || float.IsNaN(release))
            return true;
        attack = Math.Max(0f, attack);
        holdFor = Math.Max(0f, holdFor);
        release = Math.Max(0f, release);

        var v2 = new EffectChange
        {
            time = time,
            effect = effect,
            attack = attack,
            holdTime = holdFor,
            release = release,
            duration = attack + holdFor + release
        };

        switch (effect)
        {
            case "Move":
            {
                var dx = F(3);
                var dy = values.Length >= 5 ? F(4) : 0f;
                if (float.IsNaN(dx) || float.IsNaN(dy))
                    return true;
                v2.intensity = 1f;
                v2.paramA = dx;
                v2.paramB = dy;
                break;
            }
            case "Tint":
            {
                var amount = F(3);
                if (float.IsNaN(amount))
                    return true;
                var hex = values.Length >= 5 ? values[4].Trim().TrimStart('#') : "FFFFFF";
                if (hex.Length != 6 || !hex.All(Uri.IsHexDigit))
                    return true;
                v2.intensity = amount;
                v2.color = hex.ToUpperInvariant();
                break;
            }
            case "Shake":
            {
                var strength = F(3);
                if (float.IsNaN(strength))
                    return true;
                v2.intensity = strength;
                var freq = values.Length >= 5 ? F(4) : 18f;
                v2.paramA = float.IsNaN(freq) || freq <= 0f ? 18f : freq;
                break;
            }
            default:
            {
                var intensity = F(3);
                if (float.IsNaN(intensity))
                    return true;
                v2.intensity = token.StartsWith("FADE*", StringComparison.OrdinalIgnoreCase)
                    ? -Math.Abs(intensity)
                    : intensity;
                break;
            }
        }

        change = v2;
        return true;
    }

    private static bool TryParseCommandDuration(string value, float bpm, out float seconds)
    {
        value = value.Trim();
        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds))
            return true;

        var parts = value.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || bpm <= 0f ||
            !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var division) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ||
            division <= 0 || count < 0)
        {
            seconds = 0f;
            return false;
        }

        seconds = 60f / bpm * 4f / division * count;
        return true;
    }

    private static bool isNote(char noteText)
    {
        var SlideMarks = "1234567890ABCDE"; ///ABCDE for touch
        foreach (var mark in SlideMarks)
            if (noteText == mark)
                return true;
        return false;
    }

    public static string GetDifficultyText(int index)
    {
        if (index == 0) return "EASY";
        if (index == 1) return "BASIC";
        if (index == 2) return "ADVANCED";
        if (index == 3) return "EXPERT";
        if (index == 4) return "MASTER";
        if (index == 5) return "Re:MASTER";
        if (index == 6) return "ORIGINAL";
        return "DEFAULT";
    }
}

internal class SimaiTimingPoint
{
    private bool notesParsed;
    public float currentBpm = -1;
    public bool havePlayed;
    public float HSpeed = 1f;
    public List<SimaiNote> noteList = new(); //only used for json serialize
    [Newtonsoft.Json.JsonIgnore] public string? noteParseError;
    public string notesContent;
    public int rawTextPositionX;
    public int rawTextPositionY;
    public double time;

    public SimaiTimingPoint(double _time, int textposX = 0, int textposY = 0, string _content = "", float bpm = 0f,
        float _hspeed = 1f)
    {
        time = _time;
        rawTextPositionX = textposX;
        rawTextPositionY = textposY;
        notesContent = _content.Replace("\n", "").Replace(" ", "");
        currentBpm = bpm;
        HSpeed = _hspeed;
    }

    public List<SimaiNote> getNotes()
    {
        if (notesParsed || noteList.Count != 0)
        {
            notesParsed = true;
            return noteList;
        }

        var simaiNotes = new List<SimaiNote>();
        if (notesContent == "")
        {
            notesParsed = true;
            return simaiNotes;
        }
        try
        {
            var dummy = 0;
            if (notesContent.Length == 2 && int.TryParse(notesContent, out dummy))
            {
                simaiNotes.Add(getSingleNote(notesContent[0].ToString()));
                simaiNotes.Add(getSingleNote(notesContent[1].ToString()));
                noteList = simaiNotes;
                notesParsed = true;
                return noteList;
            }

            if (notesContent.Contains('/'))
            {
                var notes = notesContent.Split('/');
                foreach (var note in notes)
                    if (note.Contains('*'))
                        simaiNotes.AddRange(getSameHeadSlide(note));
                    else
                        simaiNotes.Add(getSingleNote(note));
                noteList = simaiNotes;
                notesParsed = true;
                return noteList;
            }

            if (notesContent.Contains('*'))
            {
                simaiNotes.AddRange(getSameHeadSlide(notesContent));
                noteList = simaiNotes;
                notesParsed = true;
                return noteList;
            }

            simaiNotes.Add(getSingleNote(notesContent));
            noteList = simaiNotes;
            notesParsed = true;
            return noteList;
        }
        catch (Exception e)
        {
            noteParseError = e.Message;
            noteList = new List<SimaiNote>();
            notesParsed = true;
            return noteList;
        }
    }

    private List<SimaiNote> getSameHeadSlide(string content)
    {
        var simaiNotes = new List<SimaiNote>();
        var noteContents = content.Split('*');
        var note1 = getSingleNote(noteContents[0]);
        simaiNotes.Add(note1);
        var newNoteContent = noteContents.ToList();
        newNoteContent.RemoveAt(0);
        // The first parsed note is a temporary seed.
        foreach (var item in newNoteContent)
        {
            var note2text = note1.startPosition + item;
            var note2 = getSingleNote(note2text);
            note2.isSlideNoHead = true;
            note2.isBreak = false;
            note2.isEx = note1.isEx;
            note2.isMonoHead = false;
            note2.isSlideMono = false;
            note2.isSlideBreak = false;
            note2.isDZone = note1.isDZone;
            note2.isDZoneEnd = note1.isDZoneEnd;
            simaiNotes.Add(note2);
        }

        return simaiNotes;
    }

    private SimaiNote getSingleNote(string noteText)
    {
        var simaiNote = new SimaiNote();
        var touchSlide = Regex.Match(
            noteText,
            @"^(?<start>(?:[1-8]d?|[ABDE][1-8]|C1?))(?<headMods>[bxfm!?]*)" +
            @"(?:(?<shape>[-<>^])(?<end>(?:[1-8]d?|[ABDE][1-8]|C1?))(?<bodyMods>[bxfm]*))+" +
            @"(?<duration>\[[^\[\]]+\])$",
            RegexOptions.CultureInvariant);
        var hasTouchEndpoint = touchSlide.Success &&
                               (char.IsLetter(touchSlide.Groups["start"].Value[0]) ||
                                touchSlide.Groups["end"].Captures.Cast<Capture>()
                                    .Any(capture => char.IsLetter(capture.Value[0])));
        if (hasTouchEndpoint)
        {
            static (char Area, int Position, bool IsDZone) ParseTouchSlidePosition(string value)
                => char.IsDigit(value[0])
                    ? ('K', value[0] - '0', value.EndsWith("d", StringComparison.Ordinal))
                    : value[0] == 'C'
                    ? ('C', 8, false)
                    : (value[0], value[1] - '0', false);

            var start = ParseTouchSlidePosition(touchSlide.Groups["start"].Value);
            var endCaptures = touchSlide.Groups["end"].Captures;
            var shapeCaptures = touchSlide.Groups["shape"].Captures;
            var end = ParseTouchSlidePosition(endCaptures[^1].Value);
            var headModifiers = touchSlide.Groups["headMods"].Value;
            var bodyModifiers = string.Concat(
                touchSlide.Groups["bodyMods"].Captures.Cast<Capture>().Select(capture => capture.Value));
            simaiNote.noteType = SimaiNoteType.Slide;
            simaiNote.isTouchSlide = true;
            simaiNote.isSlideNoHead = headModifiers.Contains('!') || headModifiers.Contains('?');
            simaiNote.isBreak = headModifiers.Contains('b');
            simaiNote.isSlideBreak = bodyModifiers.Contains('b');
            simaiNote.isEx = headModifiers.Contains('x') || bodyModifiers.Contains('x');
            simaiNote.isHanabi = headModifiers.Contains('f');
            simaiNote.isMonoHead = headModifiers.Contains('m');
            simaiNote.isSlideMono = bodyModifiers.Contains('m');
            simaiNote.touchArea = start.Area;
            simaiNote.touchEndArea = end.Area;
            simaiNote.startPosition = start.Position;
            simaiNote.touchEndPosition = end.Position;
            simaiNote.isDZone = start.IsDZone;
            simaiNote.isDZoneEnd = end.IsDZone;
            simaiNote.touchSlideShape = shapeCaptures[0].Value[0];
            simaiNote.slideStartTime = time + getStarWaitTime(noteText);
            simaiNote.slideTime = getTimeFromBeats(noteText);
            simaiNote.noteContent = noteText;
            return simaiNote;
        }

        if (isTouchNote(noteText))
        {
            simaiNote.touchArea = noteText[0];
            if (simaiNote.touchArea != 'C') simaiNote.startPosition = int.Parse(noteText[1].ToString());
            else simaiNote.startPosition = 8;
            simaiNote.noteType = SimaiNoteType.Touch;
        }
        else
        {
            simaiNote.startPosition = int.Parse(noteText[0].ToString());
            simaiNote.noteType = SimaiNoteType.Tap; //if nothing happen in following if
        }

        // Preserve D-zone ownership before normalizing for the existing parser.
        var originalNoteText = noteText;
        var isSlide = isSlideNote(originalNoteText);
        if (originalNoteText.Length >= 2 && originalNoteText[1] == 'd')
            simaiNote.isDZone = true;
        if (isSlide)
        {
            var timingStart = originalNoteText.IndexOf('[');
            var slideHead = timingStart >= 0
                ? originalNoteText.Substring(0, timingStart)
                : originalNoteText;
            simaiNote.isDZoneEnd = slideHead.EndsWith("d", StringComparison.Ordinal);
        }
        if (simaiNote.isDZone || simaiNote.isDZoneEnd)
        {
            noteText = noteText.Replace("d", "");
        }

        // Firework is a modifier, not part of the underlying note grammar.
        if (noteText.Contains('f'))
        {
            simaiNote.isHanabi = true;
            noteText = noteText.Replace("f", "");
        }
        //hold
        if (noteText.Contains('h'))
        {
            if (isTouchNote(noteText))
            {
                simaiNote.noteType = SimaiNoteType.TouchHold;
                simaiNote.holdTime = getTimeFromBeats(noteText);
                //Console.WriteLine("Hold:" +simaiNote.touchArea+ simaiNote.startPosition + " TimeLastFor:" + simaiNote.holdTime);
            }
            else
            {
                simaiNote.noteType = SimaiNoteType.Hold;
                if (noteText.Last() == 'h')
                    simaiNote.holdTime = 0;
                else
                    simaiNote.holdTime = getTimeFromBeats(noteText);
                //Console.WriteLine("Hold:" + simaiNote.startPosition + " TimeLastFor:" + simaiNote.holdTime);
            }
        }

        //slide
        if (isSlideNote(noteText))
        {
            simaiNote.noteType = SimaiNoteType.Slide;
            simaiNote.slideTime = getTimeFromBeats(noteText);
            var timeStarWait = getStarWaitTime(noteText);
            simaiNote.slideStartTime = time + timeStarWait;
            if (noteText.Contains('!'))
            {
                simaiNote.isSlideNoHead = true;
                noteText = noteText.Replace("!", "");
            }
            else if (noteText.Contains('?'))
            {
                simaiNote.isSlideNoHead = true;
                noteText = noteText.Replace("?", "");
            }
            //Console.WriteLine("Slide:" + simaiNote.startPosition + " TimeLastFor:" + simaiNote.slideTime);
        }

        if (noteText.Contains('m'))
        {
            if (simaiNote.noteType == SimaiNoteType.Slide)
            {
                var slidePathIndex = noteText.IndexOfAny(new[]
                {
                    '-', '^', 'v', '<', '>', 'p', 'q', 's', 'z', 'V', 'w'
                });
                simaiNote.isMonoHead = slidePathIndex > 0 &&
                                       noteText.AsSpan(0, slidePathIndex).Contains('m');
                simaiNote.isSlideMono = slidePathIndex >= 0 &&
                                        noteText.AsSpan(slidePathIndex).Contains('m');
            }
            else
            {
                simaiNote.isMonoHead = true;
            }
            noteText = noteText.Replace("m", "");
        }

        //break
        if (noteText.Contains('b'))
        {
            if (simaiNote.noteType == SimaiNoteType.Slide)
            {
                // A slide break marker may belong to either the star head or the path.
                var startIndex = 0;
                while ((startIndex = noteText.IndexOf('b', startIndex)) != -1)
                {
                    if (startIndex < noteText.Length - 1)
                    {
                        if (noteText[startIndex + 1] == '[')
                            simaiNote.isSlideBreak = true;
                        else
                            // A marker not followed by a duration belongs to the head.
                            simaiNote.isBreak = true;
                    }
                    else
                    {
                        simaiNote.isSlideBreak = true;
                    }

                    startIndex++;
                }
            }
            else
            {
                simaiNote.isBreak = true;
            }

            noteText = noteText.Replace("b", "");
        }

        //EX
        if (noteText.Contains('x'))
        {
            simaiNote.isEx = true;
            noteText = noteText.Replace("x", "");
        }

        //starHead
        if (noteText.Contains('$'))
        {
            simaiNote.isForceStar = true;
            if (noteText.Count(o => o == '$') == 2)
                simaiNote.isFakeRotate = true;
            noteText = noteText.Replace("$", "");
        }

        simaiNote.noteContent = noteText;
        return simaiNote;
    }

    private bool isSlideNote(string noteText)
    {
        var SlideMarks = "-^v<>Vpqszw";
        foreach (var mark in SlideMarks)
            if (noteText.Contains(mark))
                return true;
        return false;
    }

    private bool isTouchNote(string noteText)
    {
        var SlideMarks = "ABCDE";
        foreach (var mark in SlideMarks)
            if (noteText.StartsWith(mark.ToString()))
                return true;
        return false;
    }

    private double getTimeFromBeats(string noteText)
    {
        if (noteText.Count(c => { return c == '['; }) > 1)
        {
            // Connected slides may define one duration per segment.
            double wholeTime = 0;

            var partStartIndex = 0;
            while (noteText.IndexOf('[', partStartIndex) >= 0)
            {
                var startIndex = noteText.IndexOf('[', partStartIndex);
                var overIndex = noteText.IndexOf(']', partStartIndex);
                partStartIndex = overIndex + 1;
                var innerString = noteText.Substring(startIndex + 1, overIndex - startIndex - 1);
                var timeOneBeat = 1d / (currentBpm / 60d);
                if (innerString.Count(o => o == '#') == 1)
                {
                    var times = innerString.Split('#');
                    if (times[1].Contains(':'))
                    {
                        innerString = times[1];
                        timeOneBeat = 1d / (double.Parse(times[0]) / 60d);
                    }
                    else
                    {
                        wholeTime += double.Parse(times[1]);
                        continue;
                    }
                }

                if (innerString.Count(o => o == '#') == 2)
                {
                    var times = innerString.Split('#');
                    if (times[2].Contains(':'))
                    {
                        var ratioParts = times[2].Split(':');
                        wholeTime += timeOneBeat * 4d /
                                     int.Parse(ratioParts[0]) * int.Parse(ratioParts[1]);
                    }
                    else
                    {
                        wholeTime += double.Parse(times[2]);
                    }
                    continue;
                }

                if (innerString.Count(o => o == '#') == 3)
                {
                    var times = innerString.Split('#');
                    var ratioParts = times[3].Split(':');
                    timeOneBeat = 1d / (double.Parse(times[2]) / 60d);
                    wholeTime += timeOneBeat * 4d /
                                 int.Parse(ratioParts[0]) * int.Parse(ratioParts[1]);
                    continue;
                }

                var numbers = innerString.Split(':');
                var divide = int.Parse(numbers[0]);
                var count = int.Parse(numbers[1]);


                wholeTime += timeOneBeat * 4d / divide * count;
            }

            return wholeTime;
        }

        {
            var startIndex = noteText.IndexOf('[');
            var overIndex = noteText.IndexOf(']');
            var innerString = noteText.Substring(startIndex + 1, overIndex - startIndex - 1);
            var timeOneBeat = 1d / (currentBpm / 60d);
            if (innerString.Count(o => o == '#') == 1)
            {
                var times = innerString.Split('#');
                if (times[1].Contains(':'))
                {
                    innerString = times[1];
                    timeOneBeat = 1d / (double.Parse(times[0]) / 60d);
                }
                else
                {
                    return double.Parse(times[1]);
                }
            }

            if (innerString.Count(o => o == '#') == 2)
            {
                var times = innerString.Split('#');
                if (!times[2].Contains(':'))
                    return double.Parse(times[2]);

                var ratio = times[2].Split(':');
                return timeOneBeat * 4d /
                       int.Parse(ratio[0]) * int.Parse(ratio[1]);
            }

            if (innerString.Count(o => o == '#') == 3)
            {
                var times = innerString.Split('#');
                var ratio = times[3].Split(':');
                timeOneBeat = 1d / (double.Parse(times[2]) / 60d);
                return timeOneBeat * 4d /
                       int.Parse(ratio[0]) * int.Parse(ratio[1]);
            }

            var numbers = innerString.Split(':'); //TODO:customBPM
            var divide = int.Parse(numbers[0]);
            var count = int.Parse(numbers[1]);


            return timeOneBeat * 4d / divide * count;
        }
    }

    private double getStarWaitTime(string noteText)
    {
        var startIndex = noteText.IndexOf('[');
        var overIndex = noteText.IndexOf(']');
        var innerString = noteText.Substring(startIndex + 1, overIndex - startIndex - 1);
        double bpm = currentBpm;
        if (innerString.Count(o => o == '#') == 1)
        {
            var times = innerString.Split('#');
            bpm = double.Parse(times[0]);
        }

        if (innerString.Count(o => o == '#') >= 2)
        {
            var times = innerString.Split('#');
            return double.Parse(times[0]);
        }

        return 1d / (bpm / 60d);
    }
}

internal enum SimaiNoteType
{
    Tap,
    Slide,
    Hold,
    Touch,
    TouchHold
}

internal readonly record struct AlphaCommandError(int PositionX, int PositionY, string Message);

internal class SimaiNote
{
    public double holdTime;
    public bool isBreak;
    public bool isEx;
    public bool isFakeRotate;
    public bool isForceStar;
    public bool isHanabi;
    public bool isMonoHead;
    public bool isSlideMono;
    public bool isSlideBreak;
    public bool isSlideNoHead;
    public bool isTouchSlide;
    public bool isDZone;
    public bool isDZoneEnd;

    public string? noteContent; //used for star explain
    public SimaiNoteType noteType;

    public double slideStartTime;
    public double slideTime;

    public int startPosition = 1;
    public char touchArea = ' ';
    public int touchEndPosition = 1;
    public char touchEndArea = ' ';
    public char touchSlideShape = '-';
}

internal class MeterChange
{
    public double time;
    public int numerator;
    public int denominator;
}
