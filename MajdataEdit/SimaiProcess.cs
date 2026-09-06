using System.IO;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using MajdataCore;

namespace MajdataEdit;

internal static class SimaiProcess
{
    /// <summary>
    /// Tempo used when a chart reaches its first beat without declaring one.
    /// </summary>
    public const float DefaultBpm = 120f;

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
    public static List<SpawnModeChange> spawnModeTable = new();
    public static List<BounceChange> bounceTable = new();
    public static List<DestroyChange> destroyTable = new();
    public static List<FakeChange> fakeTable = new();
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
        spawnModeTable = new List<SpawnModeChange>();
        bounceTable = new List<BounceChange>();
        destroyTable = new List<DestroyChange>();
        fakeTable = new List<FakeChange>();
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
                    first = TryReadOffset(GetValue(maidataTxt[i]), out var readFirst)
                        ? readFirst
                        : 0f;
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
            "&title=" + SingleLine(title),
            "&artist=" + SingleLine(artist),
            "&first=" + first.ToString(CultureInfo.InvariantCulture),
            "&des=" + SingleLine(designer),
            BuildOtherCommandsForSave()
        };
        for (var i = 0; i < levels.Length; i++)
        {
            // A level is whatever the charter wants to call it - 13+, 14.9, a
            // word - so it is kept as written; it just has to stay on its line.
            var level = SingleLine(levels[i]);
            if (level.Length > 0)
                maidata.Add("&lv_" + (i + 1) + "=" + level);
        }
        for (var i = 0; i < fumens.Length; i++)
            if (fumens[i] != null && fumens[i] != "")
                maidata.Add("&inote_" + (i + 1) + "=" + fumens[i].Trim());
        File.WriteAllLines(filename, maidata.ToArray());
    }

    private static string GetValue(string varline)
    {
        return varline.Substring(varline.IndexOf("=") + 1);
    }

    /// <summary>
    /// Reads an offset, accepting the way it is written here and the way it is
    /// written everywhere else.
    /// </summary>
    /// <remarks>
    /// A chart is a file people send each other, so an offset saved on a machine
    /// that writes 0,5 has to still be an offset on a machine that writes 0.5.
    /// Nothing here throws: an offset that cannot be read leaves the one already
    /// loaded alone rather than silently moving the whole chart to zero.
    /// </remarks>
    public static bool TryReadOffset(string text, out float value)
    {
        value = 0f;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var trimmed = text.Trim();
        return float.TryParse(
                   trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
               float.TryParse(
                   trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    }

    /// <summary>
    /// Flattens a value that has to live on one line of maidata.txt.
    /// </summary>
    /// <remarks>
    /// Every &amp;field= is read back a line at a time, so a line break inside a
    /// title or a level does not save a title with a line break in it: it saves a
    /// broken file, and the rest of the field is read back as a stray line.
    /// </remarks>
    private static string SingleLine(string? value) =>
        (value ?? string.Empty)
        .Replace("\r\n", " ")
        .Replace('\r', ' ')
        .Replace('\n', ' ')
        .Trim();

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

    private static bool IsEditorSectionMarker(string line) =>
        MajdataCore.EditorDirectiveScanner.TryRead(line, 0, out var directive) &&
        directive.kind is MajdataCore.EditorDirectiveKind.SectionReset
            or MajdataCore.EditorDirectiveKind.SectionColor;

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
        var spawnModePoints = new List<SpawnModeChange>();
        var bouncePoints = new List<BounceChange>();
        var destroyPoints = new List<DestroyChange>();
        var fakePoints = new List<FakeChange>();
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
            // Zero means "no (bpm) seen yet", not a real tempo.
            float bpm = 0;
            var curHSpeed = 1f;
            double time = first; //in seconds
            double requestedTime = 0;
            var beats = 4;
            var haveNote = false;
            var noteTemp = "";
            var nextOverlayStreamIndex = 1;
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
                    TryReadOverlayStream(
                        text, i, time, bpm, curHSpeed, Xcount, Ycount, position,
                        nextOverlayStreamIndex++, overlayNotes, svPoints, hsPoints,
                        spawnPoints, spawnModePoints, bouncePoints, destroyPoints, fakePoints,
                        colorPoints, sizePoints, alphaPoints,
                        displayPoints, subtitlePoints, effectPoints, mediaPoints,
                        out var overlayEnd, out var overlayCaretTime))
                {
                    if (overlayCaretTime.HasValue)
                        requestedTime = overlayCaretTime.Value;
                    AdvanceSourcePosition(
                        text, i + 1, overlayEnd, ref Xcount, ref Ycount);
                    i = overlayEnd - 1;
                    noteTemp = "";
                    haveNote = false;
                    continue;
                }

                // @4/4, @start/@end and the legacy ampersand tints only control the
                // editor grid. They are deliberately ignored by the View chart
                // serializer. Overlay lines were already consumed above.
                if (text[i] is '@' or '&' && !haveNote &&
                    MajdataCore.EditorDirectiveScanner.TryRead(text, i, out var directive) &&
                    directive.kind != MajdataCore.EditorDirectiveKind.Overlay)
                {
                    switch (directive.kind)
                    {
                        case MajdataCore.EditorDirectiveKind.ClipStart:
                            trimStart = time;
                            break;
                        case MajdataCore.EditorDirectiveKind.ClipEnd:
                            trimEnd = time;
                            break;
                        case MajdataCore.EditorDirectiveKind.Meter:
                            meterPoints.Add(new MeterChange
                            {
                                time = time,
                                numerator = directive.numerator,
                                denominator = directive.denominator
                            });
                            break;
                    }

                    Xcount += directive.length - 1;
                    i += directive.length - 1;
                    noteTemp = "";
                    continue;
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

                if (text[i] == '<' && haveNote)
                {
                    var misplacedEnd = text.IndexOf('>', i + 1);
                    var misplacedNewline = text.IndexOfAny(
                        new[] { '\r', '\n' }, i + 1);
                    if (misplacedEnd >= 0 &&
                        (misplacedNewline < 0 || misplacedEnd < misplacedNewline))
                    {
                        var misplacedToken = text.Substring(
                            i + 1, misplacedEnd - i - 1);
                        if (NamesKnownAlphaCommand(misplacedToken))
                        {
                            Xcount += misplacedEnd - i;
                            i = misplacedEnd;
                            continue;
                        }
                    }
                }
                if (text[i] == '<' && AlphaCommandBoundary.IsPotentialStart(text, i))
                {
                    var tokenEnd = text.IndexOf('>', i + 1);
                    var looksLikeAlphaCommand = true;
                    if (tokenEnd >= 0)
                    {
                        var token = text.Substring(i + 1, tokenEnd - i - 1);
                        if (TryParseGlobalTimelineChange(
                                token, time, bpm,
                                displayPoints, subtitlePoints, effectPoints, mediaPoints,
                                colorPoints))
                        {
                            Xcount += tokenEnd - i;
                            i = tokenEnd;
                            continue;
                        }
                        if (TryParseTypedSvChange(token, time, svPoints, i))
                        {
                            Xcount += tokenEnd - i;
                            i = tokenEnd;
                            continue;
                        }
                        if (token.StartsWith("SV*", StringComparison.OrdinalIgnoreCase) &&
                            float.TryParse(token.Substring(3).Trim(),
                                NumberStyles.Float,
                                CultureInfo.InvariantCulture,
                                out var svMultiplier) &&
                            IsFinite(svMultiplier))
                        {
                            svPoints.Add(new SvPoint
                            {
                                time = time,
                                sourcePosition = i,
                                multiplier = svMultiplier
                            });
                            Xcount += tokenEnd - i;
                            i = tokenEnd;
                            continue;
                        }
                        if (TryParseTypedSpeedChange(token, time, hsPoints, i))
                        {
                            Xcount += tokenEnd - i;
                            i = tokenEnd;
                            continue;
                        }
                        if (TryParseSpawnChange(token, time, spawnPoints, i))
                        {
                            Xcount += tokenEnd - i;
                            i = tokenEnd;
                            continue;
                        }
                        if (TryParseSpawnModeChange(token, time, spawnModePoints, i))
                        {
                            Xcount += tokenEnd - i;
                            i = tokenEnd;
                            continue;
                        }
                        if (TryParseBounceChange(token, time, bpm, bouncePoints, i))
                        {
                            Xcount += tokenEnd - i;
                            i = tokenEnd;
                            continue;
                        }
                        if (TryParseDestroyChange(token, time, destroyPoints, i) ||
                            TryParseFakeChange(token, time, fakePoints, i))
                        {
                            Xcount += tokenEnd - i;
                            i = tokenEnd;
                            continue;
                        }
                        if (token.StartsWith("HS*", StringComparison.OrdinalIgnoreCase) &&
                            float.TryParse(token.Substring(3).Trim(),
                                NumberStyles.Float,
                                CultureInfo.InvariantCulture,
                                out var hSpeedMultiplier) &&
                            IsFinite(hSpeedMultiplier))
                        {
                            curHSpeed = hSpeedMultiplier;
                            hsPoints.Add(new SpeedChange
                            {
                                time = time,
                                sourcePosition = i,
                                multiplier = curHSpeed
                            });
                            Xcount += tokenEnd - i;
                            i = tokenEnd;
                            continue;
                        }
                        if (TryParseColorChange(token, time, colorPoints, i) ||
                            TryParseSizeChange(token, time, sizePoints, i) ||
                            TryParseAlphaChange(token, time, alphaPoints, i))
                        {
                            Xcount += tokenEnd - i;
                            i = tokenEnd;
                            continue;
                        }

                        // An invalid or not-yet-supported Alpha command is still an angle
                        // command, not a slide. Validation reports it separately; consuming
                        // it here prevents malformed preview/playback notes at position zero.
                        if (looksLikeAlphaCommand)
                        {
                            Xcount += tokenEnd - i;
                            i = tokenEnd;
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
                        continue;
                    }
                }

                if (text[i] == 'H' &&
                    !haveNote &&
                    i + 2 < text.Length &&
                    text[i + 1] == 'S' &&
                    text[i + 2] == '*')
                    //Get HS
                {
                    haveNote = false;
                    noteTemp = "";
                    var hs_s = "";
                    i += 3;
                    Xcount += 3;

                    while (i < text.Length && text[i] != '>')
                    {
                        hs_s += text[i];
                        i++;
                        Xcount++;
                    }

                    if (float.TryParse(
                            hs_s,
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out var parsedHSpeed) &&
                        IsFinite(parsedHSpeed))
                        curHSpeed = parsedHSpeed;
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
                            System.Globalization.CultureInfo.InvariantCulture, out var svMult) &&
                        IsFinite(svMult))
                    {
                        svPoints.Add(new SvPoint
                        {
                            time = time,
                            sourcePosition = i,
                            multiplier = svMult
                        });
                    }
                    continue;
                }

                if (isNote(text[i])) haveNote = true;
                if (haveNote && text[i] != ',') noteTemp += text[i];
                if (text[i] == ',')
                {
                    // With no (bpm) declared yet the beat length below divides by
                    // zero, so every note in the chart lands at infinity and the
                    // view cannot place any of them - the chart reads as silently
                    // empty. Falling back to a tempo keeps the rest of it usable.
                    if (bpm <= 0f)
                        bpm = DefaultBpm;
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
                                _notelist.Add(new SimaiTimingPoint(
                                    fakeTime,
                                    Math.Max(0, Xcount - noteTemp.Length - 1),
                                    Ycount,
                                    fakeEachGroup, bpm,
                                    curHSpeed, i));
                                fakeTime += timeInterval;
                            }
                        }
                        else
                        {
                            _notelist.Add(new SimaiTimingPoint(
                                time,
                                Math.Max(0, Xcount - noteTemp.Length - 1),
                                Ycount,
                                noteTemp, bpm, curHSpeed, i));
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
            MarkSimultaneousNotes(_notelist);
            ApplyFakeState(_notelist, fakePoints);
            notelist  = _notelist;
            timinglist = _timinglist;
            svTable    = svPoints;
            hsTable    = hsPoints;
            spawnTable = spawnPoints;
            spawnModeTable = spawnModePoints;
            bounceTable = bouncePoints;
            destroyTable = destroyPoints;
            fakeTable = fakePoints;
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
            notelist = new List<SimaiTimingPoint>();
            timinglist = new List<SimaiTimingPoint>();
            svTable = new List<SvPoint>();
            hsTable = new List<SpeedChange>();
            spawnTable = new List<SpawnChange>();
            spawnModeTable = new List<SpawnModeChange>();
            bounceTable = new List<BounceChange>();
            destroyTable = new List<DestroyChange>();
            fakeTable = new List<FakeChange>();
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
            return 0;
        }
    }

    private static bool TryParseGlobalTimelineChange(
        string token,
        double time,
        float bpm,
        List<DisplayChange> displayChanges,
        List<SubtitleChange> subtitleChanges,
        List<EffectChange> effectChanges,
        List<MediaChange> mediaChanges,
        List<ColorChange> colorChanges)
    {
        if (TryParseMediaChange(token, time, bpm, out var mediaChange))
        {
            if (!string.IsNullOrEmpty(mediaChange.kind))
                mediaChanges.Add(mediaChange);
            return true;
        }

        if (TryParseScreenEffect(token, time, bpm, out var effectChange))
        {
            if (!string.IsNullOrEmpty(effectChange.effect))
                effectChanges.Add(effectChange);
            return true;
        }

        if (TryParseSubtitleChange(token, time, bpm, out var subtitleChange))
        {
            subtitleChanges.Add(subtitleChange);
            return true;
        }

        if (TryParseDisplayChange(token, time, bpm, out var displayChange))
        {
            if (!string.IsNullOrEmpty(displayChange.property))
                displayChanges.Add(displayChange);
            return true;
        }

        return TryParseJudgeLineChange(
            token, time, bpm, colorChanges);
    }

    private static bool TryReadOverlayStream(
        string text,
        int markerIndex,
        double startTime,
        float bpm,
        float hSpeed,
        int markerColumn,
        int line,
        long caretPosition,
        int streamIndex,
        ICollection<SimaiTimingPoint> output,
        List<SvPoint> svChanges,
        List<SpeedChange> speedChanges,
        List<SpawnChange> spawnChanges,
        List<SpawnModeChange> spawnModeChanges,
        List<BounceChange> bounceChanges,
        List<DestroyChange> destroyChanges,
        List<FakeChange> fakeChanges,
        List<ColorChange> colorChanges,
        List<SizeChange> sizeChanges,
        List<AlphaChange> alphaChanges,
        List<DisplayChange> displayChanges,
        List<SubtitleChange> subtitleChanges,
        List<EffectChange> effectChanges,
        List<MediaChange> mediaChanges,
        out int streamEnd,
        out double? caretTime)
    {
        streamEnd = markerIndex;
        caretTime = null;
        if (markerIndex + 2 >= text.Length)
            return false;

        var block = text[markerIndex + 1] == '*';
        var contentStart = markerIndex + (block ? 2 : 1);
        if (block)
        {
            var close = text.IndexOf("*@", contentStart, StringComparison.Ordinal);
            if (close < 0)
                return false;
            streamEnd = close + 2;
        }
        else
        {
            if (text[contentStart] != '{')
                return false;
            streamEnd = text.IndexOfAny(new[] { '\r', '\n' }, contentStart);
            if (streamEnd < 0)
                streamEnd = text.Length;
        }

        while (contentStart < streamEnd && char.IsWhiteSpace(text[contentStart]))
            contentStart++;
        if (contentStart >= streamEnd || text[contentStart] != '{')
            return false;

        var closeBrace = text.IndexOf('}', contentStart + 1);
        if (closeBrace < 0 || closeBrace >= streamEnd ||
            !int.TryParse(text.Substring(contentStart + 1, closeBrace - contentStart - 1),
                out var division) || division <= 0)
            return false;

        var localBpm = bpm;
        // Overlay streams are isolated state timelines. They start from defaults and
        // only commands written on the same overlay line may change their notes.
        var localHSpeed = 1f;
        var localDivision = division;
        var slotTime = startTime;
        var slotColumn = closeBrace + 1;
        var slotContent = new StringBuilder();
        double? localCaretTime = null;

        bool TryParseScopedState(string token, double eventTime, int sourcePosition)
        {
            var start = svChanges.Count;
            if (TryParseTypedSvChange(
                    token, eventTime, svChanges, sourcePosition))
            {
                SetStreamIndex(svChanges, start, streamIndex);
                return true;
            }
            if (token.StartsWith("SV*", StringComparison.OrdinalIgnoreCase) &&
                float.TryParse(token[3..].Trim(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var svMultiplier) &&
                IsFinite(svMultiplier))
            {
                svChanges.Add(new SvPoint
                {
                    time = eventTime,
                    sourcePosition = sourcePosition,
                    streamIndex = streamIndex,
                    multiplier = svMultiplier
                });
                return true;
            }

            start = speedChanges.Count;
            if (TryParseTypedSpeedChange(token, eventTime, speedChanges, sourcePosition))
            {
                SetStreamIndex(speedChanges, start, streamIndex);
                return true;
            }
            start = spawnChanges.Count;
            if (TryParseSpawnChange(token, eventTime, spawnChanges, sourcePosition))
            {
                SetStreamIndex(spawnChanges, start, streamIndex);
                return true;
            }
            start = spawnModeChanges.Count;
            if (TryParseSpawnModeChange(
                    token, eventTime, spawnModeChanges, sourcePosition))
            {
                SetStreamIndex(spawnModeChanges, start, streamIndex);
                return true;
            }
            start = bounceChanges.Count;
            if (TryParseBounceChange(token, eventTime, localBpm, bounceChanges, sourcePosition))
            {
                SetStreamIndex(bounceChanges, start, streamIndex);
                return true;
            }
            start = destroyChanges.Count;
            if (TryParseDestroyChange(token, eventTime, destroyChanges, sourcePosition))
            {
                SetStreamIndex(destroyChanges, start, streamIndex);
                return true;
            }
            start = fakeChanges.Count;
            if (TryParseFakeChange(token, eventTime, fakeChanges, sourcePosition))
            {
                SetStreamIndex(fakeChanges, start, streamIndex);
                return true;
            }
            start = colorChanges.Count;
            if (TryParseColorChange(token, eventTime, colorChanges, sourcePosition))
            {
                SetStreamIndex(colorChanges, start, streamIndex);
                return true;
            }
            start = sizeChanges.Count;
            if (TryParseSizeChange(token, eventTime, sizeChanges, sourcePosition))
            {
                SetStreamIndex(sizeChanges, start, streamIndex);
                return true;
            }
            start = alphaChanges.Count;
            if (TryParseAlphaChange(token, eventTime, alphaChanges, sourcePosition))
            {
                SetStreamIndex(alphaChanges, start, streamIndex);
                return true;
            }
            return false;
        }

        (int Column, int Line) SourceCoordinates(int index)
        {
            var sourceLine = line;
            var sourceColumn = markerColumn;
            for (var cursor = markerIndex + 1; cursor <= index; cursor++)
            {
                if (text[cursor] == '\n')
                {
                    sourceLine++;
                    sourceColumn = 0;
                }
                else if (text[cursor] != '\r')
                {
                    sourceColumn++;
                }
            }
            return (sourceColumn, sourceLine);
        }

        void AddSlot(int commaIndex)
        {
            var rawContent = slotContent.ToString();
            var content = rawContent.Trim();
            var leadingWhitespace = rawContent.Length - rawContent.TrimStart().Length;
            var sourceIndex = Math.Max(
                markerIndex,
                commaIndex - rawContent.Length + leadingWhitespace);
            var source = SourceCoordinates(sourceIndex);
            if (localBpm > 0f && content.Any(isNote))
            {
                if (content.Contains('`'))
                {
                    var fakeTime = slotTime;
                    foreach (var fakeEachGroup in content.Split('`'))
                    {
                        if (!string.IsNullOrWhiteSpace(fakeEachGroup))
                            output.Add(new SimaiTimingPoint(
                                fakeTime, Math.Max(0, source.Column - 1), source.Line,
                                fakeEachGroup, localBpm, localHSpeed, commaIndex, streamIndex));
                        fakeTime += 1.875d / localBpm;
                    }
                }
                else
                {
                    output.Add(new SimaiTimingPoint(
                        slotTime, Math.Max(0, source.Column - 1), source.Line,
                        content, localBpm, localHSpeed, commaIndex, streamIndex));
                }
            }

            if (caretPosition >= slotColumn && caretPosition <= commaIndex)
                localCaretTime = slotTime;
            if (localBpm > 0f && localDivision > 0)
                slotTime += 240d / localBpm / localDivision;
            slotContent.Clear();
            slotColumn = commaIndex + 1;
        }

        var contentEnd = block ? streamEnd - 2 : streamEnd;
        for (var index = closeBrace + 1; index < contentEnd; index++)
        {
            if (text[index] == '<' && AlphaCommandBoundary.IsPotentialStart(text, index))
            {
                var commandEnd = text.IndexOf('>', index + 1);
                if (commandEnd >= 0 && commandEnd < contentEnd)
                {
                    if (caretPosition >= slotColumn &&
                        caretPosition <= commandEnd)
                        localCaretTime = slotTime;
                    var token = text.Substring(index + 1, commandEnd - index - 1);
                    if (TryParseGlobalTimelineChange(
                            token, slotTime, localBpm,
                            displayChanges, subtitleChanges, effectChanges, mediaChanges,
                            colorChanges))
                    {
                        // Global visual/media timelines are shared by every note
                        // stream; the line only determines their authored time.
                    }
                    else if (token.StartsWith("HS*", StringComparison.OrdinalIgnoreCase) &&
                        !token.Contains('=') &&
                        float.TryParse(token[3..].Trim(), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out var hSpeedMultiplier) &&
                        IsFinite(hSpeedMultiplier))
                    {
                        localHSpeed = hSpeedMultiplier;
                        speedChanges.Add(new SpeedChange
                        {
                            time = slotTime,
                            sourcePosition = index,
                            streamIndex = streamIndex,
                            multiplier = localHSpeed
                        });
                    }
                    else
                        TryParseScopedState(token, slotTime, index);
                    index = commandEnd;
                    slotColumn = commandEnd + 1;
                    continue;
                }
            }

            if (text[index] == '(')
            {
                var bpmEnd = text.IndexOf(')', index + 1);
                if (bpmEnd >= 0 && bpmEnd < contentEnd &&
                    float.TryParse(text.Substring(index + 1, bpmEnd - index - 1),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedBpm) &&
                    IsFinite(parsedBpm) &&
                    parsedBpm > 0f)
                {
                    if (caretPosition >= slotColumn &&
                        caretPosition <= bpmEnd)
                        localCaretTime = slotTime;
                    localBpm = parsedBpm;
                    index = bpmEnd;
                    slotColumn = bpmEnd + 1;
                    continue;
                }
            }

            if (text[index] == '{')
            {
                var divisionEnd = text.IndexOf('}', index + 1);
                if (divisionEnd >= 0 && divisionEnd < contentEnd &&
                    int.TryParse(text.Substring(index + 1, divisionEnd - index - 1),
                        out var parsedDivision) && parsedDivision > 0)
                {
                    if (caretPosition >= slotColumn &&
                        caretPosition <= divisionEnd)
                        localCaretTime = slotTime;
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

        if (caretPosition >= slotColumn && caretPosition <= contentEnd)
            localCaretTime = slotTime;
        caretTime = localCaretTime;
        return true;
    }

    private static void AdvanceSourcePosition(
        string text,
        int start,
        int end,
        ref int column,
        ref int line)
    {
        for (var index = start; index < end; index++)
        {
            if (text[index] == '\n')
            {
                line++;
                column = 0;
            }
            else if (text[index] != '\r')
            {
                column++;
            }
        }
    }

    private static void SetStreamIndex(List<SvPoint> changes, int start, int streamIndex)
    { for (var i = start; i < changes.Count; i++) changes[i].streamIndex = streamIndex; }
    private static void SetStreamIndex(List<SpeedChange> changes, int start, int streamIndex)
    { for (var i = start; i < changes.Count; i++) changes[i].streamIndex = streamIndex; }
    private static void SetStreamIndex(List<SpawnChange> changes, int start, int streamIndex)
    { for (var i = start; i < changes.Count; i++) changes[i].streamIndex = streamIndex; }
    private static void SetStreamIndex(List<SpawnModeChange> changes, int start, int streamIndex)
    { for (var i = start; i < changes.Count; i++) changes[i].streamIndex = streamIndex; }
    private static void SetStreamIndex(List<BounceChange> changes, int start, int streamIndex)
    { for (var i = start; i < changes.Count; i++) changes[i].streamIndex = streamIndex; }
    private static void SetStreamIndex(List<DestroyChange> changes, int start, int streamIndex)
    { for (var i = start; i < changes.Count; i++) changes[i].streamIndex = streamIndex; }
    private static void SetStreamIndex(List<FakeChange> changes, int start, int streamIndex)
    { for (var i = start; i < changes.Count; i++) changes[i].streamIndex = streamIndex; }
    private static void SetStreamIndex(List<ColorChange> changes, int start, int streamIndex)
    { for (var i = start; i < changes.Count; i++) changes[i].streamIndex = streamIndex; }
    private static void SetStreamIndex(List<SizeChange> changes, int start, int streamIndex)
    { for (var i = start; i < changes.Count; i++) changes[i].streamIndex = streamIndex; }
    private static void SetStreamIndex(List<AlphaChange> changes, int start, int streamIndex)
    { for (var i = start; i < changes.Count; i++) changes[i].streamIndex = streamIndex; }

    private static void AddOverlayNotes(
        List<SimaiTimingPoint> mainNotes,
        IEnumerable<SimaiTimingPoint> overlayNotes)
    {
        mainNotes.AddRange(overlayNotes);
        mainNotes.Sort((left, right) =>
        {
            var byTime = left.time.CompareTo(right.time);
            return byTime != 0
                ? byTime
                : left.sourcePosition.CompareTo(right.sourcePosition);
        });
    }

    private static void MarkSimultaneousNotes(IEnumerable<SimaiTimingPoint> notes)
    {
        var materialized = notes.ToList();
        static int CountHeads(IEnumerable<SimaiTimingPoint> points) =>
            points.Sum(point => point.getNotes().Count(note =>
                EachRule.CountsTowardEach(note.isSlideNoHead)));

        // Rendering and waveform use the chart-wide Each state.
        foreach (var group in materialized.GroupBy(note => Math.Round(note.time, 7)))
        {
            var points = group.ToList();
            var isEach = EachRule.IsEach(false, CountHeads(points));
            foreach (var point in points)
                point.isEach = isEach;
        }

        // Typed Alpha state is isolated per note stream.
        foreach (var group in materialized.GroupBy(note => (
                     Time: Math.Round(note.time, 7),
                     note.streamIndex)))
        {
            var points = group.ToList();
            var isEachInStream = EachRule.IsEach(false, CountHeads(points));
            foreach (var point in points)
                point.isEachInStream = isEachInStream;
        }
    }

    private static void ApplyFakeState(
        IEnumerable<SimaiTimingPoint> timings,
        IReadOnlyCollection<FakeChange> changes)
    {
        if (changes.Count == 0)
            return;

        FakeChange? Latest(int stream, string type, double time)
            => changes
                .Where(change => change.streamIndex == stream && change.time <= time + 0.000001d &&
                                 string.Equals(change.noteType ?? string.Empty, type,
                                     StringComparison.OrdinalIgnoreCase))
                .OrderBy(change => change.time)
                .ThenBy(change => change.sourcePosition)
                .LastOrDefault();

        bool Resolve(SimaiTimingPoint timing, string baseType, bool isBreak)
        {
            var typed = isBreak
                ? Latest(timing.streamIndex, "break", timing.time)
                : (timing.isEachInStream ?? timing.isEach)
                    ? Latest(timing.streamIndex, "each", timing.time)
                    : null;
            typed ??= Latest(timing.streamIndex, baseType, timing.time);
            if (typed != null && !typed.reset)
                return typed.enabled;

            var global = Latest(timing.streamIndex, string.Empty, timing.time);
            return global != null && !global.reset && global.enabled;
        }

        foreach (var timing in timings)
        foreach (var note in timing.getNotes())
        {
            if (note.noteType == SimaiNoteType.Slide)
            {
                note.isFakeHead = Resolve(timing, "star", note.isBreak);
                note.isFakeSlide = Resolve(timing, "slide", note.isSlideBreak);
                note.isFake = note.isFakeHead && note.isFakeSlide;
                continue;
            }

            var baseType = note.noteType switch
            {
                SimaiNoteType.Tap => note.isForceStar ? "star" : "tap",
                SimaiNoteType.Hold => "hold",
                SimaiNoteType.Touch => "touch",
                SimaiNoteType.TouchHold => "touchhold",
                _ => string.Empty
            };
            note.isFake = Resolve(timing, baseType, note.isBreak);
            note.isFakeHead = note.isFake;
            note.isFakeSlide = note.isFake;
        }
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
        var haveNote = false;

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
            if (source[i] == ',' || source[i] == ')' || source[i] == '}')
            {
                haveNote = false;
                continue;
            }
            if (isNote(source[i]))
                haveNote = true;
            if (source[i] != '<')
                continue;

            var isBoundary = AlphaCommandBoundary.IsPotentialStart(source, i);
            var end = source.IndexOf('>', i + 1);
            var newline = source.IndexOf('\n', i + 1);
            if (end < 0 || newline >= 0 && newline < end)
            {
                if (isBoundary)
                    errors.Add(new AlphaCommandError(
                        i - lineStart,
                        line,
                        MainWindow.GetLocalizedString("AlphaMissingClose")));
                continue;
            }

            var token = source.Substring(i + 1, end - i - 1).Trim();
            if (haveNote && NamesKnownAlphaCommand(token))
                errors.Add(new AlphaCommandError(
                    i - lineStart,
                    line,
                    string.Format(
                        MainWindow.GetLocalizedString("AlphaCommandAfterNote"),
                        token)));
            else if (!isBoundary)
                continue;
            else if (!TryValidateAlphaCommand(token, out var message))
                errors.Add(new AlphaCommandError(i - lineStart, line, message));
            i = end;
        }
        return errors;
    }

    // A command is checked against the same grammar the parsers below follow, so
    // the editor cannot report an error for something that plays, or stay silent
    // about something playback will drop. Every rule used to be written twice.
    private static bool TryValidateAlphaCommand(string token, out string message)
    {
        message = MainWindow.GetLocalizedString("AlphaFormatError");
        if (!AlphaCommandGrammar.TrySplitToken(token, out var command, out _))
        {
            message = MainWindow.GetLocalizedString("AlphaMissingAsterisk");
            return false;
        }
        if (!AlphaCommandGrammar.TryFind(command, out _))
        {
            message = string.Format(
                MainWindow.GetLocalizedString("AlphaUnknownCommand"),
                command.ToUpperInvariant());
            return false;
        }

        // Durations may be written as beats, so validation needs a tempo. Which
        // tempo does not matter: only whether the form is readable at all.
        return AlphaCommandGrammar.TryValidate(token, 120f, out message);
    }

    private static bool NamesKnownAlphaCommand(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;
        var separator = token.IndexOfAny(new[] { '*', '=' });
        var name = (separator < 0 ? token : token.Substring(0, separator)).Trim();
        return AlphaCommandGrammar.TryFind(name, out _);
    }

    public static void ClearNoteListPlayedState()
    {
        notelist.Sort((x, y) => x.time.CompareTo(y.time));
        for (var i = 0; i < notelist.Count; i++) notelist[i].havePlayed = false;
    }

    // One table says which parser owns a command, so a new command cannot reach
    // playback through a name switch that another layer never heard of.
    private static bool TryReadCommand(
        string token,
        AlphaCommandKind kind,
        out AlphaCommandDescriptor descriptor,
        out string body)
    {
        descriptor = null!;
        if (!AlphaCommandGrammar.TrySplitToken(token, out var name, out body) ||
            !AlphaCommandGrammar.TryFind(name, out var found) ||
            found!.kind != kind)
            return false;
        descriptor = found;
        return true;
    }

    private static bool TryParseMediaChange(string token, double time, float bpm, out MediaChange change)
    {
        change = new MediaChange();
        if (!TryReadCommand(token, AlphaCommandKind.Media, out var descriptor, out var body))
            return false;

        var command = descriptor.name;
        if (body.Length < 3 || body[0] != '(' || body[^1] != ')')
            return true;

        var values = body[1..^1].Split(',', StringSplitOptions.TrimEntries);
        if (values.Length < 1 || !bool.TryParse(values[0], out var enabled))
            return true;

        change.time = time;
        change.kind = descriptor.Canonical;
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

        // Which extensions play, and that the path may not escape the chart folder,
        // is stated once by the grammar the syntax check reads.
        var pathSpec = descriptor.FormFor(AlphaArgumentFormKind.StateOn, 2)?.slots[1];
        if (pathSpec == null || !pathSpec.Matches(values[1], bpm))
        {
            change.kind = "";
            return true;
        }

        change.path = values[1].Trim().Trim('"').Replace('\\', '/');
        return true;
    }

    // Shared typed keys. HS*slide controls guide-star fade, while visual commands
    // add slidestar as a separate render target.
    private static readonly string[] StateNoteTypes =
    {
        "tap", "each", "hold", "slide", "star", "break", "mine", "touch", "touchhold"
    };
    private static readonly string[] HsNoteTypes =
        { "tap", "each", "hold", "slide", "star", "break", "mine", "touch", "touchhold" };
    private static readonly string[] VisualStateNoteTypes =
        StateNoteTypes.Concat(new[] { "slidestar" }).ToArray();

    private static bool TrySplitTopLevelValues(
        string text,
        out List<string> values)
    {
        values = new List<string>();
        var depth = 0;
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            switch (text[index])
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    if (depth == 0)
                        return false;
                    depth--;
                    break;
                case ',' when depth == 0:
                    if (index == start)
                        return false;
                    values.Add(text.Substring(start, index - start).Trim());
                    start = index + 1;
                    break;
            }
        }

        if (depth != 0 || start >= text.Length)
            return false;
        values.Add(text.Substring(start).Trim());
        return values.All(value => value.Length > 0);
    }

    private static bool TryParseTypedSvChange(
        string token,
        double time,
        List<SvPoint> into,
        int sourcePosition = 0)
    {
        if (!token.StartsWith("SV*", StringComparison.OrdinalIgnoreCase))
            return false;
        var body = token[3..].Trim();
        if (!body.Contains('='))
            return false;

        var start = into.Count;
        if (!TrySplitTopLevelValues(body, out var pairs))
            return true;
        foreach (var pair in pairs)
        {
            var kv = pair.Split('=', 2, StringSplitOptions.TrimEntries);
            if (kv.Length != 2 ||
                !StateNoteTypes.Contains(
                    kv[0], StringComparer.OrdinalIgnoreCase))
            {
                into.RemoveRange(start, into.Count - start);
                return true;
            }
            if (string.Equals(kv[1], "NULL", StringComparison.OrdinalIgnoreCase))
                into.Add(new SvPoint
                {
                    time = time,
                    sourcePosition = sourcePosition,
                    noteType = kv[0].ToLowerInvariant(),
                    reset = true
                });
            else if (float.TryParse(
                         kv[1],
                         NumberStyles.Float,
                         CultureInfo.InvariantCulture,
                         out var value) &&
                     IsFinite(value))
                into.Add(new SvPoint
                {
                    time = time,
                    sourcePosition = sourcePosition,
                    noteType = kv[0].ToLowerInvariant(),
                    multiplier = value
                });
            else
            {
                into.RemoveRange(start, into.Count - start);
                return true;
            }
        }
        return true;
    }

    private static bool TryParseTypedSpeedChange(
        string token, double time, List<SpeedChange> into, int sourcePosition = 0)
    {
        if (!token.StartsWith("HS*", StringComparison.OrdinalIgnoreCase))
            return false;
        var body = token[3..].Trim();
        if (!body.Contains('='))
            return false;

        var start = into.Count;
        if (!TrySplitTopLevelValues(body, out var pairs))
            return true;
        foreach (var pair in pairs)
        {
            var kv = pair.Split('=', 2, StringSplitOptions.TrimEntries);
            if (kv.Length != 2 ||
                !HsNoteTypes.Contains(
                    kv[0], StringComparer.OrdinalIgnoreCase))
            {
                into.RemoveRange(start, into.Count - start);
                return true;
            }
            var value = 1f;
            if (!string.Equals(kv[1], "NULL", StringComparison.OrdinalIgnoreCase) &&
                (!float.TryParse(
                     kv[1],
                     NumberStyles.Float,
                     CultureInfo.InvariantCulture,
                     out value) ||
                 !IsFinite(value)))
            {
                into.RemoveRange(start, into.Count - start);
                return true;
            }
            into.Add(new SpeedChange
            {
                time = time,
                sourcePosition = sourcePosition,
                noteType = kv[0].ToLowerInvariant(),
                multiplier = value,
                reset = string.Equals(kv[1], "NULL", StringComparison.OrdinalIgnoreCase)
            });
        }
        return true;
    }

    private static readonly string[] SpawnNoteTypes =
        { "tap", "each", "hold", "star", "break", "mine" };

    private static bool TryParseSpawnChange(
        string token,
        double time,
        List<SpawnChange> into,
        int sourcePosition = 0)
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
                    sourcePosition = sourcePosition,
                    radius = radius,
                    reset = reset
                });
            return true;
        }

        var start = into.Count;
        if (!TrySplitTopLevelValues(body, out var pairs))
            return true;
        foreach (var pair in pairs)
        {
            var kv = pair.Split('=', 2, StringSplitOptions.TrimEntries);
            if (kv.Length != 2 ||
                !SpawnNoteTypes.Contains(kv[0], StringComparer.OrdinalIgnoreCase) ||
                !TryValue(kv[1], out var radius, out var reset))
            {
                into.RemoveRange(start, into.Count - start);
                return true;
            }
            into.Add(new SpawnChange
            {
                time = time,
                sourcePosition = sourcePosition,
                noteType = kv[0].ToLowerInvariant(),
                radius = radius,
                reset = reset
            });
        }
        return true;
    }

    private static bool TryParseSpawnModeChange(
        string token,
        double time,
        List<SpawnModeChange> into,
        int sourcePosition = 0)
    {
        if (!token.StartsWith("SPAWNMODE*", StringComparison.OrdinalIgnoreCase))
            return false;

        static string NormalizeValue(string text)
        {
            var value = text.Trim();
            return value.Length >= 2 && value[0] == '(' && value[^1] == ')'
                ? value[1..^1].Trim()
                : value;
        }

        static bool TryValue(
            string text,
            out SpawnVisualMode mode,
            out bool reset)
        {
            var value = NormalizeValue(text);
            reset = string.Equals(value, "NULL", StringComparison.OrdinalIgnoreCase);
            if (reset || string.Equals(value, "REWIND", StringComparison.OrdinalIgnoreCase))
            {
                mode = SpawnVisualMode.Rewind;
                return true;
            }
            if (string.Equals(value, "ONCE", StringComparison.OrdinalIgnoreCase))
            {
                mode = SpawnVisualMode.Once;
                return true;
            }
            mode = SpawnVisualMode.Rewind;
            return false;
        }

        var body = token[10..].Trim();
        if (!body.Contains('='))
        {
            if (TryValue(body, out var mode, out var reset))
                into.Add(new SpawnModeChange
                {
                    time = time,
                    sourcePosition = sourcePosition,
                    mode = mode,
                    reset = reset
                });
            return true;
        }

        var start = into.Count;
        if (!TrySplitTopLevelValues(body, out var pairs))
            return true;
        foreach (var pair in pairs)
        {
            var keyValue = pair.Split('=', 2, StringSplitOptions.TrimEntries);
            if (keyValue.Length != 2 ||
                !SpawnNoteTypes.Contains(keyValue[0], StringComparer.OrdinalIgnoreCase) ||
                !TryValue(keyValue[1], out var mode, out var reset))
            {
                into.RemoveRange(start, into.Count - start);
                return true;
            }
            into.Add(new SpawnModeChange
            {
                time = time,
                sourcePosition = sourcePosition,
                noteType = keyValue[0].ToLowerInvariant(),
                mode = mode,
                reset = reset
            });
        }
        return true;
    }

    private static bool TryParseBounceChange(
        string token,
        double time,
        float bpm,
        List<BounceChange> into,
        int sourcePosition = 0)
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
            value.Equals("break", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("mine", StringComparison.OrdinalIgnoreCase);

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
                    sourcePosition = sourcePosition,
                    noteType = noteType,
                    duration = reset ? 0f : duration,
                    reset = reset
                });
            return true;
        }

        var start = into.Count;
        if (!TrySplitTopLevelValues(body, out var pairs))
            return true;
        foreach (var pair in pairs)
        {
            var keyValue = pair.Split('=', 2, StringSplitOptions.TrimEntries);
            if (keyValue.Length != 2 || !IsBounceType(keyValue[0]))
            {
                into.RemoveRange(start, into.Count - start);
                return true;
            }
            var reset = string.Equals(keyValue[1], "NULL", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(keyValue[1], "FALSE", StringComparison.OrdinalIgnoreCase);
            var duration = 0f;
            if (!reset && (!TryParseCommandDuration(keyValue[1], bpm, out duration) || duration <= 0f))
            {
                into.RemoveRange(start, into.Count - start);
                return true;
            }
            into.Add(new BounceChange
            {
                time = time,
                sourcePosition = sourcePosition,
                noteType = keyValue[0].ToLowerInvariant(),
                duration = reset ? 0f : duration,
                reset = reset
            });
        }
        return true;
    }

    private static bool TryParseDestroyChange(
        string token,
        double time,
        List<DestroyChange> into,
        int sourcePosition = 0)
    {
        if (!token.StartsWith("DESTROY*", StringComparison.OrdinalIgnoreCase))
            return false;

        static bool TryValue(string text, out float radius, out bool reset)
        {
            reset = string.Equals(text.Trim(), "NULL", StringComparison.OrdinalIgnoreCase);
            if (reset)
            {
                radius = 4.8f;
                return true;
            }
            return float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture,
                       out radius) && radius is >= -20f and <= 20f;
        }

        var body = token[8..].Trim();
        if (!body.Contains('='))
        {
            if (TryValue(body, out var radius, out var reset))
                into.Add(new DestroyChange
                {
                    time = time,
                    sourcePosition = sourcePosition,
                    radius = radius,
                    reset = reset
                });
            return true;
        }

        var start = into.Count;
        if (!TrySplitTopLevelValues(body, out var pairs))
            return true;
        foreach (var pair in pairs)
        {
            var keyValue = pair.Split('=', 2, StringSplitOptions.TrimEntries);
            if (keyValue.Length != 2 ||
                !SpawnNoteTypes.Contains(keyValue[0], StringComparer.OrdinalIgnoreCase) ||
                !TryValue(keyValue[1], out var radius, out var reset))
            {
                into.RemoveRange(start, into.Count - start);
                return true;
            }
            into.Add(new DestroyChange
            {
                time = time,
                sourcePosition = sourcePosition,
                noteType = keyValue[0].ToLowerInvariant(),
                radius = radius,
                reset = reset
            });
        }
        return true;
    }

    private static bool TryParseFakeChange(
        string token,
        double time,
        List<FakeChange> into,
        int sourcePosition = 0)
    {
        if (!token.StartsWith("FAKE*", StringComparison.OrdinalIgnoreCase))
            return false;

        static bool TryValue(string text, out bool enabled, out bool reset)
        {
            var value = text.Trim();
            reset = false;
            enabled = string.Equals(value, "TRUE", StringComparison.OrdinalIgnoreCase) || value == "1";
            var disabled = string.Equals(value, "FALSE", StringComparison.OrdinalIgnoreCase) || value == "0";
            return enabled || disabled;
        }

        var body = token[5..].Trim();
        if (!body.Contains('='))
        {
            if (TryValue(body, out var enabled, out var reset))
                into.Add(new FakeChange
                {
                    time = time,
                    sourcePosition = sourcePosition,
                    enabled = enabled,
                    reset = reset
                });
            return true;
        }

        var start = into.Count;
        if (!TrySplitTopLevelValues(body, out var pairs))
            return true;
        foreach (var pair in pairs)
        {
            var keyValue = pair.Split('=', 2, StringSplitOptions.TrimEntries);
            if (keyValue.Length != 2 ||
                !StateNoteTypes.Contains(keyValue[0], StringComparer.OrdinalIgnoreCase) ||
                !TryValue(keyValue[1], out var enabled, out var reset))
            {
                into.RemoveRange(start, into.Count - start);
                return true;
            }
            into.Add(new FakeChange
            {
                time = time,
                sourcePosition = sourcePosition,
                noteType = keyValue[0].ToLowerInvariant(),
                enabled = enabled,
                reset = reset
            });
        }
        return true;
    }

    private static bool TryParseColorChange(
        string token, double time, List<ColorChange> into, int sourcePosition = 0)
    {
        var live = token.StartsWith("COLORV*", StringComparison.OrdinalIgnoreCase);
        if (!live && !token.StartsWith("COLOR*", StringComparison.OrdinalIgnoreCase))
            return false;
        var body = token.Substring(live ? 7 : 6).Trim();
        if (string.Equals(body, "NULL", StringComparison.OrdinalIgnoreCase))
        {
            into.Add(new ColorChange
            {
                time = time,
                sourcePosition = sourcePosition,
                noteType = string.Empty,
                color = "NULL",
                live = live
            });
        }
        else if (!body.Contains('='))
        {
            var color = body.TrimStart('#');
            if (color.Length == 6 && color.All(Uri.IsHexDigit))
                into.Add(new ColorChange
                {
                    time = time,
                    sourcePosition = sourcePosition,
                    noteType = string.Empty,
                    color = color,
                    live = live
                });
        }
        else
        {
            var start = into.Count;
            if (!TrySplitTopLevelValues(body, out var pairs))
                return true;
            foreach (var pair in pairs)
            {
                var kv = pair.Split('=', 2, StringSplitOptions.TrimEntries);
                if (kv.Length != 2 ||
                    !VisualStateNoteTypes.Contains(
                        kv[0], StringComparer.OrdinalIgnoreCase))
                {
                    into.RemoveRange(start, into.Count - start);
                    return true;
                }
                var noteType = kv[0].ToLowerInvariant();
                var colorStr = kv[1].Trim();
                if (string.Equals(colorStr, "NULL", StringComparison.OrdinalIgnoreCase))
                    into.Add(new ColorChange { time = time, sourcePosition = sourcePosition, noteType = noteType, color = "NULL", live = live });
                else
                {
                    var color = colorStr.TrimStart('#');
                    if (color.Length != 6 || !color.All(Uri.IsHexDigit))
                    {
                        into.RemoveRange(start, into.Count - start);
                        return true;
                    }
                    into.Add(new ColorChange { time = time, sourcePosition = sourcePosition, noteType = noteType, color = color, live = live });
                }
            }
        }
        return true;
    }

    // Global size does not scale slide bodies; slide size must be set explicitly.
    private static bool TryParseSizeChange(
        string token, double time, List<SizeChange> into, int sourcePosition = 0)
    {
        var live = token.StartsWith("SIZEV*", StringComparison.OrdinalIgnoreCase);
        if (!live && !token.StartsWith("SIZE*", StringComparison.OrdinalIgnoreCase))
            return false;
        var body = token.Substring(live ? 6 : 5).Trim();
        if (body.Contains('='))
        {
            var start = into.Count;
            if (!TrySplitTopLevelValues(body, out var pairs))
                return true;
            foreach (var pair in pairs)
            {
                var keyValue = pair.Split(
                    '=', 2, StringSplitOptions.TrimEntries);
                if (keyValue.Length != 2 ||
                    !VisualStateNoteTypes.Contains(
                        keyValue[0], StringComparer.OrdinalIgnoreCase))
                {
                    into.RemoveRange(start, into.Count - start);
                    return true;
                }
                var noteType = keyValue[0].ToLowerInvariant();
                var val = keyValue[1];
                if (string.Equals(val, "NULL", StringComparison.OrdinalIgnoreCase))
                {
                    var change = NewSizeChange(time, noteType, 1f, 1f, sourcePosition);
                    change.reset = true;
                    change.live = live;
                    into.Add(change);
                }
                else if (TryParseScalePair(val, out var x, out var y))
                {
                    var change = NewSizeChange(time, noteType, x, y, sourcePosition);
                    change.live = live;
                    into.Add(change);
                }
                else if (float.TryParse(val, System.Globalization.NumberStyles.Float,
                             System.Globalization.CultureInfo.InvariantCulture, out var s) &&
                         IsFinite(s))
                {
                    var change = NewSizeChange(time, noteType, s, s, sourcePosition);
                    change.live = live;
                    into.Add(change);
                }
                else
                {
                    into.RemoveRange(start, into.Count - start);
                    return true;
                }
            }
        }
        else if (string.Equals(body, "NULL", StringComparison.OrdinalIgnoreCase))
        {
            var change = NewSizeChange(time, null, 1f, 1f, sourcePosition);
            change.reset = true;
            change.live = live;
            into.Add(change);
        }
        else if (TryParseScalePair(body, out var x, out var y))
        {
            var change = NewSizeChange(time, null, x, y, sourcePosition);
            change.live = live;
            into.Add(change);
        }
        else if (float.TryParse(body, System.Globalization.NumberStyles.Float,
                     System.Globalization.CultureInfo.InvariantCulture, out var scale) &&
                 IsFinite(scale))
        {
            var change = NewSizeChange(time, null, scale, scale, sourcePosition);
            change.live = live;
            into.Add(change);
        }
        return true;
    }

    private static SizeChange NewSizeChange(
        double time, string? noteType, float x, float y, int sourcePosition) => new()
    {
        time = time,
        sourcePosition = sourcePosition,
        noteType = noteType,
        scale = MathF.Sqrt(MathF.Abs(x * y)),
        scaleX = x,
        scaleY = y
    };

    private static bool TryParseScalePair(string value, out float x, out float y) =>
        AlphaCommandGrammar.TryParseScalePair(value, out x, out y);

    private static bool TryParseAlphaChange(
        string token, double time, List<AlphaChange> into, int sourcePosition = 0)
    {
        var live = token.StartsWith("ALPHAV*", StringComparison.OrdinalIgnoreCase);
        if (!live && !token.StartsWith("ALPHA*", StringComparison.OrdinalIgnoreCase))
            return false;
        var body = token.Substring(live ? 7 : 6).Trim();
        if (body.Contains('='))
        {
            var start = into.Count;
            if (!TrySplitTopLevelValues(body, out var pairs))
                return true;
            foreach (var pair in pairs)
            {
                var kv = pair.Split('=', 2, StringSplitOptions.TrimEntries);
                if (kv.Length != 2 ||
                    !VisualStateNoteTypes.Contains(
                        kv[0], StringComparer.OrdinalIgnoreCase))
                {
                    into.RemoveRange(start, into.Count - start);
                    return true;
                }
                var noteType = kv[0].ToLowerInvariant();
                var val = kv[1].Trim();
                if (string.Equals(val, "NULL", StringComparison.OrdinalIgnoreCase))
                    into.Add(new AlphaChange { time = time, sourcePosition = sourcePosition, noteType = noteType, alpha = 1f, reset = true, live = live });
                else if (float.TryParse(val, System.Globalization.NumberStyles.Float,
                             System.Globalization.CultureInfo.InvariantCulture, out var ptAlpha) &&
                         IsFinite(ptAlpha))
                    into.Add(new AlphaChange { time = time, sourcePosition = sourcePosition, noteType = noteType, alpha = Math.Clamp(ptAlpha, 0f, 1f), live = live });
                else
                {
                    into.RemoveRange(start, into.Count - start);
                    return true;
                }
            }
        }
        else if (string.Equals(body, "NULL", StringComparison.OrdinalIgnoreCase))
        {
            into.Add(new AlphaChange { time = time, sourcePosition = sourcePosition, noteType = null, alpha = 1f, reset = true, live = live });
        }
        else if (float.TryParse(body, System.Globalization.NumberStyles.Float,
                     System.Globalization.CultureInfo.InvariantCulture, out var naAlpha) &&
                 IsFinite(naAlpha))
        {
            into.Add(new AlphaChange { time = time, sourcePosition = sourcePosition, noteType = null, alpha = Math.Clamp(naAlpha, 0f, 1f), live = live });
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
        if (!TryReadCommand(token, AlphaCommandKind.Display, out var descriptor, out var body))
            return false;

        var canonicalProperty = descriptor.Canonical;
        if (body.Length < 3 || body[0] != '(' || body[^1] != ')')
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
        else if (!TryParseFinite(values[0], out target))
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
        // The aliases and the accepted numbers live in the grammar, so the syntax
        // check and the completion popup accept exactly what the player shows.
        if (!AlphaCommandGrammar.TryParseComboMode(value, out var numeric))
            return false;
        mode = (EditorComboIndicator)numeric;
        return true;
    }

    /// <summary>
    /// Reads a caption: its text, how long it stays, where it sits and how big it is.
    /// </summary>
    /// <remarks>
    /// Caption text is quoted. This makes commas unambiguous and keeps syntax
    /// completion, validation and playback on one grammar.
    /// </remarks>
    private static bool TryParseSubtitleChange(string token, double time, float bpm, out SubtitleChange change)
    {
        change = new SubtitleChange();
        if (!token.StartsWith("TEXT*", StringComparison.OrdinalIgnoreCase))
            return false;

        var body = token.Substring(5).Trim();
        var duration = -1f;
        var content = string.Empty;
        var x = 0f;
        var y = 0f;
        var size = 0f;
        var font = string.Empty;
        var subtitleIndex = 0;
        var style = "Fade";
        var transition = 0f;

        if (body.Length < 4 || body[0] != '(' || body[^1] != ')' ||
            !AlphaCommandGrammar.TrySplitValues(
                body.Substring(1, body.Length - 2), out var values) ||
            values.Count == 0 || values[0].Length < 2 ||
            values[0][0] != '"' || values[0][^1] != '"')
            return false;

        content = values[0].Substring(1, values[0].Length - 2)
            .Replace("\\\"", "\"");
        var positional = values.Count > 1 &&
                         values.Skip(1).All(value => !value.Contains('='));
        if (positional)
        {
            if (values[1].Length > 0)
            {
                if (!TryParseCommandDuration(values[1], bpm, out duration))
                    return false;
                duration = Math.Max(0f, duration);
            }
            if (values.Count > 2 && values[2].Length > 0 &&
                !TryParseSubtitleNumber(values[2], 0f, 1f, out x))
                return false;
            if (values.Count > 3 && values[3].Length > 0 &&
                !TryParseSubtitleNumber(values[3], 0f, 1f, out y))
                return false;
            if (values.Count > 4 && values[4].Length > 0 &&
                !TryParseSubtitleNumber(values[4], 8f, 200f, out size))
                return false;
            if (values.Count > 5 && values[5].Length > 0 &&
                !AlphaCommandGrammar.TryNormalizeSubtitleFont(values[5], out font))
                return false;
            if (values.Count > 6 && values[6].Length > 0 &&
                (!int.TryParse(values[6], NumberStyles.Integer,
                     CultureInfo.InvariantCulture, out subtitleIndex) ||
                 subtitleIndex < 0))
                return false;
            if (values.Count > 7 && values[7].Length > 0 &&
                !AlphaCommandGrammar.TryNormalizeSubtitleStyle(values[7], out style))
                return false;
            if (values.Count > 8 && values[8].Length > 0)
            {
                if (!TryParseCommandDuration(values[8], bpm, out transition))
                    return false;
                transition = Math.Max(0f, transition);
            }
        }
        else
        for (var index = 1; index < values.Count; index++)
        {
            var value = values[index];
            if (TryParseSubtitlePlacement(value, ref x, ref y, ref size))
                continue;
            if (TryParseSubtitleOption(
                    value, bpm, ref font, ref subtitleIndex,
                    ref style, ref transition))
                continue;
            if (duration < 0f &&
                TryParseCommandDuration(value, bpm, out var parsedDuration))
            {
                duration = Math.Max(0f, parsedDuration);
                continue;
            }
            return false;
        }

        change = new SubtitleChange
        {
            time = time,
            text = content,
            duration = duration,
            x = x,
            y = y,
            size = size,
            font = font,
            index = subtitleIndex,
            style = style,
            transition = transition
        };
        return true;
    }

    private static bool TryParseSubtitleNumber(
        string text, float minimum, float maximum, out float value)
    {
        if (!float.TryParse(
                text.Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value) ||
            float.IsNaN(value) || float.IsInfinity(value))
            return false;
        value = Math.Clamp(value, minimum, maximum);
        return true;
    }

    private static bool TryParseSubtitlePlacement(
        string text, ref float x, ref float y, ref float size)
    {
        var equals = text.IndexOf('=');
        if (equals <= 0)
            return false;
        var key = text.Substring(0, equals).Trim().ToLowerInvariant();
        if (!float.TryParse(
                text.Substring(equals + 1).Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var value) ||
            float.IsNaN(value) || float.IsInfinity(value))
            return false;
        switch (key)
        {
            case "x":
                x = Math.Clamp(value, 0f, 1f);
                return true;
            case "y":
                y = Math.Clamp(value, 0f, 1f);
                return true;
            case "size":
                size = Math.Clamp(value, 8f, 200f);
                return true;
            default:
                return false;
        }
    }

    private static bool TryParseSubtitleOption(
        string text,
        float bpm,
        ref string font,
        ref int index,
        ref string style,
        ref float transition)
    {
        var equals = text.IndexOf('=');
        if (equals <= 0)
            return false;
        var key = text.Substring(0, equals).Trim().ToLowerInvariant();
        var value = text.Substring(equals + 1).Trim();
        switch (key)
        {
            case "font":
                if (!AlphaCommandGrammar.TryNormalizeSubtitleFont(value, out font))
                    return false;
                return true;
            case "index":
                return int.TryParse(
                           value, NumberStyles.Integer,
                           CultureInfo.InvariantCulture, out index) &&
                       index >= 0;
            case "style":
                return AlphaCommandGrammar.TryNormalizeSubtitleStyle(value, out style);
            case "transition":
                if (!TryParseCommandDuration(value, bpm, out transition))
                    return false;
                transition = Math.Max(0f, transition);
                return true;
            default:
                return false;
        }
    }

    private static bool TryParseScreenEffect(string token, double time, float bpm, out EffectChange change)
    {
        change = new EffectChange();
        if (!TryReadCommand(token, AlphaCommandKind.Effect, out var descriptor, out var body))
            return false;

        var effect = descriptor.Canonical;
        // FADE is FLASH aimed at black, so its strength is applied negatively.
        var negated = descriptor.negatesIntensity;
        if (body.Length < 3 || body[0] != '(' || body[^1] != ')')
            return true;

        var values = body.Substring(1, body.Length - 2).Split(',');
        float F(int i) => TryParseFinite(values[i], out var v) ? v : float.NaN;
        float D(int i) => TryParseCommandDuration(values[i], bpm, out var v) ? v : float.NaN;

        if (values.Length >= 1 &&
            string.Equals(values[0].Trim(), "Instant", StringComparison.OrdinalIgnoreCase))
        {
            var instant = new EffectChange
            {
                time = time,
                effect = effect,
                attack = 0f,
                release = 0f
            };
            var durationIndex = -1;
            switch (effect)
            {
                case "Move":
                    if (values.Length != 4)
                        return true;
                    instant.intensity = 1f;
                    instant.paramA = F(1);
                    instant.paramB = F(2);
                    durationIndex = 3;
                    break;
                case "Tint":
                    if (values.Length != 4)
                        return true;
                    instant.color = values[1].Trim().TrimStart('#').ToUpperInvariant();
                    instant.intensity = F(2);
                    durationIndex = 3;
                    if (instant.color.Length != 6 || !instant.color.All(Uri.IsHexDigit))
                        return true;
                    break;
                case "Shake":
                    if (values.Length is < 4 or > 5)
                        return true;
                    instant.intensity = F(1);
                    instant.paramA = F(2);
                    if (values.Length == 5)
                    {
                        instant.hasDirection = true;
                        instant.paramB = F(3) * (float)Math.PI / 180f;
                    }
                    durationIndex = values.Length - 1;
                    break;
                default:
                    if (values.Length != 3)
                        return true;
                    instant.intensity = F(1);
                    if (effect == "Zoom")
                        instant.intensity -= 1f;
                    if (negated)
                        instant.intensity = -Math.Abs(instant.intensity);
                    durationIndex = 2;
                    break;
            }

            var duration = D(durationIndex);
            if (float.IsNaN(instant.intensity) || float.IsNaN(instant.paramA) ||
                float.IsNaN(instant.paramB) || float.IsNaN(duration))
                return true;
            instant.holdTime = Math.Max(0f, duration);
            instant.duration = instant.holdTime;
            change = instant;
            return true;
        }

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
                    state.intensity = negated ? -Math.Abs(intensity) : intensity;
                    if (effect == "Zoom")
                        state.intensity -= 1f;
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
                intensity = negated ? -Math.Abs(intensity) : intensity
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

        // The oldest form is (attack,hold,release,strength) plus the one extra value
        // some effects take. Anything past that used to be dropped without a word.
        var envelopeMaximum = effect is "Move" or "Tint" or "Shake" ? 5 : 4;
        if (values.Length < 4 || values.Length > envelopeMaximum)
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
                v2.intensity = negated ? -Math.Abs(intensity) : intensity;
                break;
            }
        }

        change = v2;
        return true;
    }

    private static bool TryParseCommandDuration(string value, float bpm, out float seconds) =>
        AlphaCommandGrammar.TryParseDuration(value, bpm, out seconds);

    // Commands never mean NaN or Infinity, and a chart that writes one used to
    // reach playback as a filter that renders nothing.
    private static bool TryParseFinite(string value, out float result) =>
        float.TryParse(
            value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result) &&
        IsFinite(result);

    private static bool IsFinite(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value);

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
    public bool isEach;
    public bool? isEachInStream;
    public float HSpeed = 1f;
    public List<SimaiNote> noteList = new(); //only used for json serialize
    [Newtonsoft.Json.JsonIgnore] public string? noteParseError;
    public string notesContent;
    public int rawTextPositionX;
    public int rawTextPositionY;
    public int sourcePosition;
    public int streamIndex;
    public double time;

    public SimaiTimingPoint(double _time, int textposX = 0, int textposY = 0, string _content = "", float bpm = 0f,
        float _hspeed = 1f, int _sourcePosition = 0, int _streamIndex = 0)
    {
        time = _time;
        rawTextPositionX = textposX;
        rawTextPositionY = textposY;
        sourcePosition = _sourcePosition;
        streamIndex = _streamIndex;
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
        // Only the first problem of a beat is kept. Appending every branch's
        // message stacked several diagnostics onto one squiggle, which is what
        // made the tooltips overlap and run long.
        void recordError(string message)
        {
            if (string.IsNullOrWhiteSpace(noteParseError))
                noteParseError = message;
        }

        try
        {
            // How a slot splits into notes is decided once, in the shared parser,
            // so the editor cannot mark text the player accepts (or the reverse).
            if (!NoteSlotParser.TrySplit(
                    notesContent, out var entries, out var splitError))
                recordError(splitError);

            for (var index = 0; index < entries.Count;)
            {
                var group = entries[index].groupIndex;
                var parsed = new List<SimaiNote>();
                var failed = false;
                // A same-head group is one authored note: if any branch is broken the
                // whole group stays off the playfield, and only it is reported.
                while (index < entries.Count && entries[index].groupIndex == group)
                {
                    var entry = entries[index++];
                    try
                    {
                        var note = getSingleNote(entry.text);
                        if (entry.fromSameHead)
                        {
                            if (note.noteType != SimaiNoteType.Slide)
                                throw new Exception(
                                    MajdataCore.ParserMessageLocale.PreferChinese
                                        ? $"同头星星的每一条都必须是星星：{entry.text}"
                                        : "EVERY SAME-HEAD BRANCH MUST BE A SLIDE: " +
                                          entry.text);
                            if (parsed.Count != 0)
                                InheritSameHead(parsed[0], note);
                        }
                        parsed.Add(note);
                    }
                    catch (Exception error)
                    {
                        recordError(error.Message);
                        failed = true;
                    }
                }

                if (!failed)
                    simaiNotes.AddRange(parsed);
            }

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

    // Only the first branch of a same-head group keeps a head; the rest follow its
    // appearance so the group reads as one star.
    private static void InheritSameHead(SimaiNote head, SimaiNote branch)
    {
        branch.isSlideNoHead = true;
        branch.suppressSlideGuideStarFade = head.suppressSlideGuideStarFade;
        branch.isBreak = false;
        branch.isEx = head.isEx;
        branch.isMineHead = false;
    }


    private static void ApplyModifiers(
        SimaiNote note,
        ParsedNoteModifiers modifiers)
    {
        note.isBreak = modifiers.HasHead(NoteModifierFlags.Break);
        note.isSlideBreak = modifiers.HasSlide(NoteModifierFlags.Break);
        note.isEx = modifiers.HasAny(NoteModifierFlags.Ex);
        note.isHanabi = modifiers.HasAny(NoteModifierFlags.Firework);
        note.isMineHead = modifiers.HasHead(NoteModifierFlags.Mine);
        note.isMineSlide = modifiers.HasSlide(NoteModifierFlags.Mine);
        note.isSlideNoHead = modifiers.HasAny(NoteModifierFlags.NoHead);
        note.suppressSlideGuideStarFade =
            modifiers.HasAny(NoteModifierFlags.NoHeadWithoutFade);
        note.isForceStar = modifiers.HasAny(NoteModifierFlags.ForceStar);
        note.isFakeRotate = modifiers.HasAny(NoteModifierFlags.FakeRotate);
    }

    // The Note kind, its position and its duration all come from one parse in
    // MajdataCore. This used to be decided here by scanning the text for 'h', '['
    // and the slide marks, separately from the syntax check, the preview and View,
    // which is how the same text could be a Hold in one layer and an error in
    // another.
    private SimaiNote getSingleNote(string noteText)
    {
        if (!NoteExpressionParser.TryParse(
                noteText, out var expression, out var parseError))
            throw new Exception(parseError);

        var simaiNote = new SimaiNote();
        simaiNote.pathExpression = noteText;
        var position = expression.position;
        ApplyModifiers(simaiNote, expression.modifiers);
        simaiNote.startPosition = position.position;
        simaiNote.isDZone = position.isDZone;
        simaiNote.touchRadius = position.radius;
        simaiNote.noteSkin = position.skin;

        if (expression.trajectory != null)
        {
            // Everything a slide would have produced except the two things a
            // borrow is defined by not producing: no arc, no head, no judgement.
            // The path is followed exactly as written, so it brings its own start
            // and its own timing with it.
            var borrowed = expression.trajectory;
            var borrowedEnd = borrowed.segments[^1].end;
            simaiNote.trajectoryCarrierPosition = expression.position.position;
            simaiNote.trajectoryCarrierIsDZone = expression.position.isDZone;
            simaiNote.trajectoryCarrierType = expression.kind switch
            {
                NoteExpressionKind.Hold => SimaiNoteType.Hold,
                NoteExpressionKind.Touch => SimaiNoteType.Touch,
                NoteExpressionKind.TouchHold => SimaiNoteType.TouchHold,
                _ => SimaiNoteType.Tap
            };
            simaiNote.noteType = SimaiNoteType.Slide;
            simaiNote.slidePath = borrowed.segments;
            simaiNote.slideTime = getSlideTime(borrowed);
            // A borrowed carrier starts moving on its own note beat. The borrowed
            // slide contributes its route and duration, not a slide-head wait beat.
            simaiNote.slideStartTime = time;
            simaiNote.startPosition = borrowed.segments[0].start.position;
            simaiNote.touchEndPosition = borrowedEnd.position;
            simaiNote.isDZone = borrowed.segments[0].start.isDZone;
            simaiNote.isDZoneEnd = borrowedEnd.isDZone;
            simaiNote.isTrajectoryOnly = true;
            simaiNote.isSlideNoHead = true;
            simaiNote.isFake = true;
            simaiNote.isFakeHead = true;
            simaiNote.isFakeSlide = true;
            if (expression.isTouchPath)
            {
                // A touch star reaches the view through the touch slide fields, and
                // its areas come from the path it travels rather than from the note
                // that borrowed it.
                simaiNote.isTouchSlide = true;
                simaiNote.touchArea = borrowed.segments[0].start.area;
                simaiNote.touchEndArea = borrowedEnd.area;
                simaiNote.touchSlideShape = borrowed.segments[0].shape[0];
                simaiNote.noteContent = expression.trajectorySource;
                return simaiNote;
            }
            simaiNote.noteContent = NormalizeNoteText(expression.trajectorySource);
            return simaiNote;
        }

        if (expression.kind == NoteExpressionKind.Slide)
        {
            var path = expression.path;
            var end = path.segments[^1].end;
            simaiNote.noteType = SimaiNoteType.Slide;
            simaiNote.slidePath = path.segments;
            simaiNote.slideTime = getSlideTime(path);
            simaiNote.slideStartTime = time + getStarWaitTime(noteText);
            simaiNote.touchEndPosition = end.position;
            simaiNote.isDZoneEnd = end.isDZone;
            if (expression.isTouchPath)
            {
                simaiNote.isTouchSlide = true;
                simaiNote.touchArea = position.area;
                simaiNote.touchEndArea = end.area;
                simaiNote.touchSlideShape = path.segments[0].shape[0];
                simaiNote.noteContent = noteText;
                return simaiNote;
            }

            simaiNote.noteContent = NormalizeNoteText(noteText);
            return simaiNote;
        }

        if (position.IsTouch)
            simaiNote.touchArea = position.area;
        simaiNote.noteType = expression.kind switch
        {
            NoteExpressionKind.Hold => SimaiNoteType.Hold,
            NoteExpressionKind.Touch => SimaiNoteType.Touch,
            NoteExpressionKind.TouchHold => SimaiNoteType.TouchHold,
            _ => SimaiNoteType.Tap
        };
        if (expression.IsHold)
            simaiNote.holdTime = expression.isZeroLengthHold
                ? 0d
                : getTimeFromBeats(expression.duration);
        simaiNote.noteContent = NormalizeNoteText(noteText);
        return simaiNote;
    }

    // View and the shape detectors read noteContent, which has always been the text
    // without modifiers or D-zone suffixes.
    private static string NormalizeNoteText(string noteText) =>
        NoteModifierParser.RemoveModifiers(
            SlidePathParser.RemoveDZoneSuffixes(noteText));

    private double getSlideTime(SlidePathData path)
    {
        double total = 0d;
        foreach (var segment in path.segments)
        {
            if (string.IsNullOrEmpty(segment.duration))
                continue;
            if (!SlideSyntaxValidator.TryGetLengthSeconds(
                    segment.duration, currentBpm, out var seconds))
                throw new Exception(currentBpm <= 0f
                    ? $"星星时长需要有效 BPM，当前 BPM 为 {currentBpm}：{segment.duration}\n" +
                      $"SLIDE DURATION NEEDS A VALID BPM (current {currentBpm}): {segment.duration}"
                    : $"星星时长写法错误：{segment.duration}\n" +
                      $"INVALID SLIDE DURATION: {segment.duration}");
            total += seconds;
        }
        return total;
    }

    // Hold durations go through the same token parser as Slide durations, so the
    // two can no longer disagree about forms like [#2] or [3##8:1].
    private double getTimeFromBeats(string durationToken)
    {
        if (!SlideSyntaxValidator.TryGetLengthSeconds(
                durationToken, currentBpm, out var seconds))
            throw new Exception(currentBpm <= 0f
                ? $"时长需要有效 BPM，当前 BPM 为 {currentBpm}：{durationToken}\n" +
                  $"DURATION NEEDS A VALID BPM (current {currentBpm}): {durationToken}"
                : $"时长写法错误：{durationToken}\n" +
                  $"INVALID DURATION: {durationToken}");
        return seconds;
    }

    // How long the guide star waits before it starts travelling: a leading
    // "[delay##..." is that delay, otherwise it is one beat of the duration's BPM.
    private double getStarWaitTime(string noteText)
    {
        var open = noteText.IndexOf('[');
        var close = noteText.IndexOf(']');
        double bpm = currentBpm;
        if (open >= 0 && close > open &&
            SlideSyntaxValidator.TryParseDuration(
                noteText.Substring(open, close - open + 1), out var duration))
        {
            if (duration.hasDelay)
                return duration.delay;
            bpm = duration.bpm ?? currentBpm;
        }

        return bpm > 0d ? 60d / bpm : 0d;
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
    public bool isMineHead;
    public bool isMineSlide;
    [Newtonsoft.Json.JsonProperty("isMonoHead")]
    private bool LegacyMineHead
    {
        get => isMineHead;
        set
        {
            if (value)
                isMineHead = true;
        }
    }
    [Newtonsoft.Json.JsonProperty("isSlideMono")]
    private bool LegacyMineSlide
    {
        get => isMineSlide;
        set
        {
            if (value)
                isMineSlide = true;
        }
    }
    public bool isSlideBreak;
    public bool isSlideNoHead;
    public bool suppressSlideGuideStarFade;
    public bool isTouchSlide;
    public bool isDZone;
    public bool isDZoneEnd;
    public bool isFake;
    public bool isFakeHead;
    public bool isFakeSlide;
    // "1~[5-7[8:1]]": this note is only borrowing the star trajectory. It draws no
    // arc, drops no head and is never judged; the carrier note itself travels it.
    public bool isTrajectoryOnly;
    // Visual note carried along a borrowed path. noteType remains Slide because
    // the path controller builds and moves it.
    public SimaiNoteType trajectoryCarrierType = SimaiNoteType.Tap;
    public int trajectoryCarrierPosition = 1;
    public bool trajectoryCarrierIsDZone;

    public string? noteContent; //used for star explain
    public string? pathExpression;
    public List<SlidePathSegmentData> slidePath = new();
    public SimaiNoteType noteType;

    public double slideStartTime;
    public double slideTime;

    public int startPosition = 1;
    public char touchArea = ' ';
    public int touchEndPosition = 1;
    public char touchEndArea = ' ';
    public char touchSlideShape = '-';
    // 0 keeps the Touch area's authored distance; a positive value draws the Note at
    // that distance along the same direction (see SlidePositionData.radius).
    public float touchRadius;
    // An image file, relative to the chart's folder, that replaces this one Note's
    // skin (see SlidePositionData.skin). Empty means the default skin.
    public string noteSkin = string.Empty;
}

internal class MeterChange
{
    public double time;
    public int numerator;
    public int denominator;
}
