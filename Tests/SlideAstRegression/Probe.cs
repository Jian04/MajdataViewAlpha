using MajdataCore;

namespace MajdataEdit;

internal static class Probe
{
    // A beat holding nothing but an alpha command is normal - ',<SV*0>,' shows up
    // all over real charts. Check the whole-chart path, since a probe that hands
    // unstripped content straight to the note parser would fake an error.
    private static void CommandOnlyBeatsInWholeChart()
    {
        Console.WriteLine("=== command-only beats, whole-chart path ===");
        foreach (var (label, text) in new[]
                 {
                     ("bare SV beat", "(120){4}1,<SV*0>,2,"),
                     ("bare HS beat", "(120){4}1,<HS*2>,2,"),
                     ("two commands", "(120){4}1,<SV*0><HS*2>,2,"),
                     ("command then note", "(120){4}1,<SV*0>3,2,"),
                     ("overlay style", "(120){4}1,<SV*0>,,<SV*1>,"),
                 })
        {
            SimaiProcess.Serialize(text);
            var errors = SimaiProcess.notelist
                .Where(t => !string.IsNullOrEmpty(t.noteParseError))
                .Select(t => $"t={t.time:F2} '{t.notesContent}' -> {t.noteParseError}")
                .ToList();
            var notes = SimaiProcess.notelist.SelectMany(t => t.getNotes()).Count();
            Console.WriteLine(
                $"  {label,-18} notes={notes} errors={errors.Count}" +
                (errors.Count == 0 ? "" : "  " + string.Join(" | ", errors)));
        }
        Console.WriteLine();
    }

    // A slide whose slideTime lands at zero divides by zero in StarDrop's spin,
    // which writes NaN into the transform: the star never travels and never
    // retires. Find the duration forms in real charts that reach that state.
    private static void ZeroLengthSlidesInCorpus()
    {
        var root = Environment.GetEnvironmentVariable("PROBE_CORPUS");
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            Console.WriteLine("=== zero-length slides: PROBE_CORPUS unset, skipped ===");
            return;
        }

        var files = Directory.EnumerateFiles(root, "maidata.txt", SearchOption.AllDirectories)
            .Take(4000).ToList();
        var hits = new List<string>();
        var scanned = 0;
        foreach (var file in files)
        {
            string text;
            try { text = File.ReadAllText(file); }
            catch { continue; }
            foreach (var body in SplitDifficulties(text))
            {
                scanned++;
                try { SimaiProcess.Serialize(body); }
                catch { continue; }
                foreach (var timing in SimaiProcess.notelist)
                foreach (var note in timing.getNotes())
                {
                    if (note.noteType != SimaiNoteType.Slide)
                        continue;
                    if (note.slideTime > 0.0)
                        continue;
                    hits.Add(
                        $"{Path.GetFileName(Path.GetDirectoryName(file))} " +
                        $"t={timing.time:F3} '{note.noteContent}' " +
                        $"slideTime={note.slideTime} start={note.slideStartTime}");
                }
            }
        }

        Console.WriteLine(
            $"=== zero-length slides: {hits.Count} hit(s) over " +
            $"{scanned} difficulties in {files.Count} files ===");
        foreach (var hit in hits.Take(20))
            Console.WriteLine("  " + hit);
    }

    // An inote body runs from its '&inote_N=' line until the next '&' key, so it
    // has to be accumulated across lines rather than read one line at a time.
    private static IEnumerable<string> SplitDifficulties(string text)
    {
        var body = new System.Text.StringBuilder();
        var collecting = false;
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (raw.StartsWith("&", StringComparison.Ordinal))
            {
                if (collecting && body.Length > 0)
                    yield return body.ToString();
                body.Clear();
                collecting = raw.StartsWith("&inote_", StringComparison.Ordinal);
                if (collecting)
                {
                    var eq = raw.IndexOf('=');
                    if (eq >= 0)
                        body.AppendLine(raw.Substring(eq + 1));
                }
                continue;
            }
            if (collecting)
                body.AppendLine(raw);
        }
        if (collecting && body.Length > 0)
            yield return body.ToString();
    }

    // InstantiateStarGroup hands every segment of a connected slide back to
    // ResolveSlidePath as a note of its own, so each segment is validated as if it
    // were the whole slide. A segment that carries no duration - every segment but
    // the last, under total-duration syntax - has to survive that or the beat dies
    // halfway through building, after the star head is already in the scene.
    private static void SubSlideRevalidation()
    {
        Console.WriteLine("=== connected slide: each segment revalidated alone ===");
        foreach (var text in new[]
                 {
                     "6>3pp5d[16:2]",
                     "7>2qq1d[16:2]",
                     "1-3-5[8:1]",
                     "1-3[8:1]-5[8:1]",
                     "1V35-7[8:1]",
                 })
        {
            var timing = new SimaiTimingPoint(0d, _content: text, bpm: 70f);
            var notes = timing.getNotes();
            foreach (var note in notes)
            {
                if (note.slidePath == null)
                    continue;
                foreach (var segment in note.slidePath)
                {
                    var expression = segment.ToExpression(includeDZone: true);
                    var ok = SlidePathParser.TryParsePath(expression, out var path);
                    var valid = ok && SlideSyntaxValidator.TryValidate(path, out _);
                    var error = string.Empty;
                    if (ok)
                        SlideSyntaxValidator.TryValidate(path, out error);
                    Console.WriteLine(
                        $"  {text,-20} segment '{expression,-14}' " +
                        $"parse={(ok ? "ok" : "FAILED")} " +
                        $"validate={(valid ? "ok" : "FAILED")}" +
                        (valid ? "" : $"  {error?.Replace('\n', ' ')}"));
                }
            }
        }
        Console.WriteLine();
    }

    public static void Run()
    {
        SubSlideRevalidation();
        DescribeAdHoc();
        EachFlagsAgainstBaseline();
        RpShapeMatrix();
        ShapeResolverOnEverySegment();
        CommandOnlyBeatsInWholeChart();
        ZeroLengthSlidesInCorpus();
        ViewSideSlideValidation();
        PhantomNoteFromWholeChartText();
        PhantomNoteFromContentThatHasNoNote();

        Console.WriteLine("=== silent no-show: multi-segment touch slides ===");
        foreach (var text in new[]
                 {
                     "E5<E6-E3[8:8]",
                     "E5<E6[8:8]",
                     "E5-E6-E3[8:8]",
                     "E5<E6-E3[8:1]",
                     "E1<E6-E3[8:8]",
                     "1<3-5[8:8]",
                     "E5<E6-E3[8:8]"
                 })
            Gates(text);
    }

    /// <summary>
    /// Walks the path the view walks. JsonDataLoader.ResolveSlidePath throws when
    /// the stored path fails validation, and the loader catches that into a
    /// Debug.LogWarning, so in a built player the note just is not there: no
    /// star, no bar, no error. Anything failing here disappears in exactly that
    /// way.
    /// </summary>
    // Reproduces what the view does per segment when it builds a key slide:
    // DetectShape calls SlideShapeResolver.TryResolve and throws when it fails,
    // and that throw takes the whole beat. The resolver is a separate check from
    // SlideSyntaxValidator, so a path the editor accepts can still die here.
    private static void ShapeResolverOnEverySegment()
    {
        Console.WriteLine("=== per-segment shape resolve (what drops the beat) ===");
        foreach (var text in new[]
                 {
                     "6>3pp5d[16:2]",
                     "7>2qq1d[16:2]",
                     "6>3pp5d[16:2]/7>2qq1d[16:2]",
                     "6>3pp5[16:2]",
                     "3pp5d[16:2]",
                     "6>3[16:2]",
                     "7>2qq1[16:2]",
                     "2qq1d[16:2]",
                 })
        {
            var timing = new SimaiTimingPoint(0d, _content: text, bpm: 70f);
            List<SimaiNote> notes;
            try { notes = timing.getNotes(); }
            catch (Exception e)
            {
                Console.WriteLine($"  {text,-30} PARSE THREW {e.Message}");
                continue;
            }
            if (!string.IsNullOrEmpty(timing.noteParseError))
            {
                Console.WriteLine($"  {text,-30} PARSE ERROR {timing.noteParseError}");
                continue;
            }

            foreach (var note in notes)
            {
                if (note.noteType != SimaiNoteType.Slide || note.slidePath == null)
                    continue;
                var parts = new List<string>();
                var durations = 0;
                foreach (var segment in note.slidePath)
                {
                    if (!string.IsNullOrEmpty(segment.duration))
                        durations++;
                    if (SlideShapeResolver.TryResolve(
                            segment, out var key, out var issue, out var error))
                        parts.Add($"{segment.ToExpression(true)}->{key}");
                    else
                        parts.Add(
                            $"{segment.ToExpression(true)}->RESOLVE FAILED " +
                            $"[{issue}] {error}");
                }
                var segs = note.slidePath.Count;
                var durationOk = durations == 1 || durations == segs;
                Console.WriteLine(
                    $"  {text,-30} segs={segs} durations={durations}" +
                    $"{(durationOk ? "" : " DURATION RULE WOULD THROW")}");
                foreach (var part in parts)
                    Console.WriteLine($"      {part}");
            }
        }
        Console.WriteLine();
    }

    // rp and rq are the mirrored twins of pp and qq, and the pp/qq D-zone star was
    // the note that went missing without a word. Anything the pair does not share
    // with its twin is worth knowing about before a chart relies on it.
    private static void RpShapeMatrix()
    {
        Console.WriteLine("=== rp/rq vs their pp/qq twins ===");
        var templates = new[]
        {
            "1{P}5[8:1]", "1{P}5d[8:1]", "1{P}5d[8:1]m",
            "1b{P}5d[8:1]", "1{P}5d[8:1]b", "1f{P}5d[8:1]",
            "1{P}5d[8:1]*{Q}3d[8:1]", "1{P}5d[8:1]/2{Q}6d[8:1]",
            "1!{P}5d[8:1]", "1?{Q}5d[8:1]",
            "1{P}5d[8:1]-E7[8:1]", "E1{P}5d[8:1]",
            "6>3{P}5d[16:2]/7>2{Q}1d[16:2]",
        };
        foreach (var template in templates)
        {
            var twin = Describe(
                template.Replace("{P}", "pp").Replace("{Q}", "qq"));
            var mirror = Describe(
                template.Replace("{P}", "rp").Replace("{Q}", "rq"));
            var agree = twin == mirror ? "same" : "DIFFERS";
            Console.WriteLine($"  {template,-32} {agree}");
            Console.WriteLine($"      pp/qq: {twin}");
            if (agree != "same")
                Console.WriteLine($"      rp/rq: {mirror}");
        }
        Console.WriteLine();
    }

    // Reduces a beat to the facts that decide whether it plays: whether it parsed,
    // the flags the view judges on, and whether every segment found a prefab. The
    // prefab's name is left out on purpose - twins differ there by design.
    private static string Describe(string text)
    {
        var timing = new SimaiTimingPoint(0d, _content: text, bpm: 70f);
        List<SimaiNote> notes;
        try { notes = timing.getNotes(); }
        catch (Exception e) { return $"THREW {e.Message}"; }
        if (!string.IsNullOrEmpty(timing.noteParseError))
            return $"rejected: {timing.noteParseError}";

        var parts = new List<string>();
        foreach (var note in notes)
        {
            var resolved = "n/a";
            if (note.noteType == SimaiNoteType.Slide && note.slidePath != null)
            {
                var ok = 0;
                foreach (var segment in note.slidePath)
                {
                    // Touch slides never reach the prefab resolver in production, so
                    // running one through it here would invent a failure.
                    if (note.isTouchSlide ||
                        SlideShapeResolver.TryResolve(segment, out _, out _, out _))
                        ok++;
                }
                resolved = $"{ok}/{note.slidePath.Count} resolved";
            }
            parts.Add(
                $"[{note.noteType} headBreak={note.isBreak} " +
                $"slideBreak={note.isSlideBreak} mine={note.isMineSlide} " +
                $"firework={note.isForceStar} touch={note.isTouchSlide} " +
                $"nohead={note.isSlideNoHead} {resolved}]");
        }
        return string.Join(" ", parts);
    }

    // A beat where one note turned yellow. Yellow is the each skin, so the question
    // is whether the beat is each and whether 0.4.2 agreed.
    private static void EachFlagsAgainstBaseline()
    {
        Console.WriteLine("=== each flags, current vs 0.4.2 ===");
        foreach (var text in new[]
                 {
                     "A8/8?-C-5[8:1]",
                     "A8/8-C-5[8:1]",
                     "8?-C-5[8:1]",
                     "A8/8?-5[8:1]",
                     "1/8?-C-5[8:1]",
                     "A8/A1",
                 })
        {
            var now = new SimaiTimingPoint(0d, _content: text, bpm: 120f);
            var nowNotes = now.getNotes();
            var old = new Baseline042.SimaiTimingPoint(0d, _content: text, bpm: 120f);
            var oldNotes = old.getNotes();

            // 0.4.2's timing point has no isEach field at all: back then a beat was
            // each purely by its note count, so that is all there is to compare.
            var headed = nowNotes.Count(n => !n.isSlideNoHead);
            Console.WriteLine(
                $"  {text,-22} now: isEach={now.isEach} count={nowNotes.Count} " +
                $"headed={headed}   042: count={oldNotes.Count}");
            for (var i = 0; i < Math.Max(nowNotes.Count, oldNotes.Count); i++)
            {
                var a = i < nowNotes.Count
                    ? $"{nowNotes[i].noteType} pos={nowNotes[i].startPosition} " +
                      $"area={nowNotes[i].touchArea} nohead={nowNotes[i].isSlideNoHead} " +
                      $"break={nowNotes[i].isBreak}"
                    : "-";
                var b = i < oldNotes.Count
                    ? $"{oldNotes[i].noteType} pos={oldNotes[i].startPosition} " +
                      $"area={oldNotes[i].touchArea} nohead={oldNotes[i].isSlideNoHead} " +
                      $"break={oldNotes[i].isBreak}"
                    : "-";
                Console.WriteLine($"      [{i}] {a}");
                if (a != b)
                    Console.WriteLine($"      [{i}] 042 DIFFERS: {b}");
            }
        }
        Console.WriteLine();
    }

    private static void DescribeAdHoc()
    {
        Console.WriteLine("=== ad-hoc parse ===");
        foreach (var text in new[]
                 {
                     "C<C[8:1]", "C>C[8:1]", "C-C[8:1]", "C^C[8:1]",
                     "CvC[8:1]", "C1-C2[8:1]", "CpC[8:1]",
                     "B3-B3[8:1]", "E3-E3[8:1]", "B3vB3[8:1]", "B3^B3[8:1]",
                     "B3pB3[8:1]", "C-B3<B3[8:1]",
                 })
        {
            var timing = new SimaiTimingPoint(0d, _content: text, bpm: 120f);
            List<SimaiNote> notes;
            try { notes = timing.getNotes(); }
            catch (Exception e)
            {
                Console.WriteLine($"  {text} THREW {e.Message}");
                continue;
            }
            if (!string.IsNullOrEmpty(timing.noteParseError))
            {
                Console.WriteLine($"  {text} REJECTED {timing.noteParseError}");
                continue;
            }

            var old = new Baseline042.SimaiTimingPoint(0d, _content: text, bpm: 120f);
            var oldNotes = old.getNotes();
            Console.WriteLine(
                $"  {text}: {notes.Count} notes (042: {oldNotes.Count})");
            foreach (var note in oldNotes)
                Console.WriteLine(
                    $"    042: {note.noteType} head={note.touchArea}{note.startPosition} " +
                    $"shape='{note.touchSlideShape}' " +
                    $"end={note.touchEndArea}{note.touchEndPosition} " +
                    $"nohead={note.isSlideNoHead} content='{note.noteContent}'");
            foreach (var note in notes)
            {
                Console.WriteLine(
                    $"    {note.noteType} head={note.touchArea}{note.startPosition} " +
                    $"dzone={note.isDZone} touchSlide={note.isTouchSlide} " +
                    $"nohead={note.isSlideNoHead} break={note.isBreak} " +
                    $"slideBreak={note.isSlideBreak} mine={note.isMineSlide} " +
                    $"start={note.slideStartTime:0.###} len={note.slideTime:0.###}");
                Console.WriteLine(
                    $"      legacy: shape='{note.touchSlideShape}' " +
                    $"end={note.touchEndArea}{note.touchEndPosition} " +
                    $"dzoneEnd={note.isDZoneEnd} " +
                    $"expr='{note.pathExpression}' content='{note.noteContent}'");
                if (note.slidePath == null)
                    continue;
                foreach (var segment in note.slidePath)
                    Console.WriteLine(
                        $"      seg {segment.ToExpression(true)} " +
                        $"shape='{segment.shape}' dur='{segment.duration}' " +
                        (note.isTouchSlide
                            ? "touch (no prefab)"
                            : SlideShapeResolver.TryResolve(
                                segment, out var key, out var issue, out var reason)
                            ? $"prefab='{key}'"
                            : $"RESOLVE FAILED [{issue}] {reason}"));
            }
        }
        Console.WriteLine();
    }

    private static void RpShapeMatrixDetail()
    {
        foreach (var text in Array.Empty<string>())
        {
            var timing = new SimaiTimingPoint(0d, _content: text, bpm: 70f);
            List<SimaiNote> notes;
            try { notes = timing.getNotes(); }
            catch (Exception e)
            {
                Console.WriteLine($"  {text,-30} PARSE THREW {e.Message}");
                continue;
            }
            if (!string.IsNullOrEmpty(timing.noteParseError))
            {
                Console.WriteLine($"  {text,-30} REJECTED {timing.noteParseError}");
                continue;
            }

            foreach (var note in notes)
            {
                if (note.noteType != SimaiNoteType.Slide || note.slidePath == null)
                    continue;
                var flags =
                    $"break={note.isSlideBreak} mine={note.isMineSlide} " +
                    $"touch={note.isTouchSlide} nohead={note.isSlideNoHead}";
                var parts = new List<string>();
                foreach (var segment in note.slidePath)
                {
                    // Touch slides never reach the prefab resolver in production, so
                    // running one through it here would invent a failure.
                    if (note.isTouchSlide)
                    {
                        parts.Add($"{segment.ToExpression(true)}->touch (no prefab)");
                        continue;
                    }
                    parts.Add(
                        SlideShapeResolver.TryResolve(
                            segment, out var key, out var issue, out var error)
                            ? $"{segment.ToExpression(true)}->{key}"
                            : $"{segment.ToExpression(true)}->RESOLVE FAILED " +
                              $"[{issue}] {error}");
                }
                Console.WriteLine($"  {text,-30} {flags}");
                foreach (var part in parts)
                    Console.WriteLine($"      {part}");
            }
        }
        Console.WriteLine();
    }

    private static void ViewSideSlideValidation()
    {
        Console.WriteLine("=== view-side validation of stored slide paths ===");
        foreach (var text in new[]
                 {
                     "7?^2dm[12:1]",
                     "2?^8dm[12:1]",
                     "7!^2dm[12:1]",
                     "7^2dm[12:1]",
                     "7^2d[12:1]",
                     "7?^2m[12:1]",
                     "4/7?^2dm[12:1]",
                     "8>4d[16:1]",
                     "8q5db[16:1]",
                     "8^5b[16:1]",
                     "1<6d[16:1]",
                     "1p5db[16:1]",
                     "E5<E6-E3[8:8]",
                     "A6b>7-3b[8:7]",
                     "A8?-C-A5[0.1##0.1]"
                 })
        {
            var timing = new SimaiTimingPoint(0d, _content: text, bpm: 70f);
            List<SimaiNote> notes;
            try
            {
                notes = timing.getNotes();
            }
            catch (Exception e)
            {
                Console.WriteLine($"  {text,-20} PARSE THREW {e.Message}");
                continue;
            }

            if (!string.IsNullOrEmpty(timing.noteParseError))
            {
                Console.WriteLine($"  {text,-20} PARSE ERROR {timing.noteParseError}");
                continue;
            }

            foreach (var note in notes)
            {
                if (note.noteType != SimaiNoteType.Slide)
                    continue;
                var stored = note.slidePath;
                if (stored == null || stored.Count == 0)
                {
                    Console.WriteLine(
                        $"  {text,-20} no stored path (expr='{note.pathExpression}')");
                    continue;
                }

                var ok = SlideSyntaxValidator.TryValidateSegments(
                    stored, out var error);
                Console.WriteLine(
                    $"  {text,-20} segs={stored.Count} " +
                    $"{(ok ? "VALID" : "REJECTED -> note vanishes silently: " + error)}");
            }
        }
        Console.WriteLine();
    }

    /// <summary>
    /// Runs a real chart through the production entry point and reports every
    /// beat the parser refuses, plus the specific beats called out as broken.
    /// </summary>
    private static void RealChartErrors()
    {
        foreach (var expr in new[]
                 {
                     "<MOVE*(False)><ZOOM*(False)>6>3pp5d[16:2]/7>2qq1d[16:2]",
                     "6>3pp5d[16:2]/7>2qq1d[16:2]",
                     "6>3pp5d[16:2]",
                     "7>2qq1d[16:2]",
                     "<MOVE*(False)>6>3pp5d[16:2]",
                     "<ZOOM*(False)>6>3pp5d[16:2]",
                     "<MOVE*(False)><ZOOM*(False)>1",
                     "2hx[1:0]`B2",
                     "4/7?^2dm[12:1]",
                     "A8?-C-A5[0.1##0.1]",
                     "A6b>7-3b[8:7]",
                     "3s7[12:1]"
                 })
        {
            var timing = new SimaiTimingPoint(0d, _content: expr, bpm: 70f);
            try
            {
                var notes = timing.getNotes();
                var err = timing.noteParseError ?? string.Empty;
                Console.WriteLine(
                    $"  {(err.Length == 0 ? "OK  " : "FAIL")} {expr,-56} " +
                    $"notes={notes.Count} {err}");
            }
            catch (Exception e)
            {
                Console.WriteLine($"  THREW {expr,-56} {e.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("=== whole chart ===");
        if (!File.Exists("/tmp/usrchart.txt"))
        {
            Console.WriteLine("  (no chart at /tmp/usrchart.txt, skipped)");
            Console.WriteLine();
            return;
        }

        var text = File.ReadAllText("/tmp/usrchart.txt");
        SimaiProcess.Serialize(text);
        var bad = 0;
        foreach (var timing in SimaiProcess.notelist)
        {
            List<SimaiNote> notes;
            try
            {
                notes = timing.getNotes();
            }
            catch (Exception e)
            {
                Console.WriteLine($"  THREW t={timing.time:F2} '{timing.notesContent}' {e.Message}");
                bad++;
                continue;
            }

            if (!string.IsNullOrEmpty(timing.noteParseError))
            {
                Console.WriteLine(
                    $"  FAIL t={timing.time:F2} '{timing.notesContent}' " +
                    $"-> {timing.noteParseError}");
                bad++;
            }
        }

        Console.WriteLine($"  beats={SimaiProcess.notelist.Count} failures={bad}");
        var first = SimaiProcess.notelist
            .SelectMany(t => t.getNotes().Select(n => (t, n)))
            .OrderBy(p => p.t.time)
            .Take(6);
        Console.WriteLine("  first notes in the chart:");
        foreach (var (t, n) in first)
            Console.WriteLine(
                $"    t={t.time:F3} {n.noteType} key={n.startPosition} " +
                $"'{n.noteContent}' noHead={n.isSlideNoHead}");
        Console.WriteLine();
    }

    /// <summary>
    /// What the loader reads when it decides where a slide's head star goes.
    /// InstantiateTouchSlide sends touchArea 'K' down the key-head path, which
    /// places a star at startPosition; startPosition defaults to 1, so any note
    /// that reaches that path without a real key lands a star on key 1.
    /// </summary>
    private static void TouchSlideHeadFields()
    {
        Console.WriteLine("=== head-star inputs: touchArea / startPosition ===");
        foreach (var text in new[]
                 {
                     "E5<E6-E3[8:8]",
                     "1-E5[8:1]",
                     "E5-3[8:1]",
                     "C-E5[8:1]",
                     "1-5[8:1]",
                     "1!-5[8:1]",
                     "1?-5[8:1]",
                     "E5<E6[8:1]",
                     "A1-E5[8:1]",
                     "C1-E5[8:1]"
                 })
        {
            var timing = new SimaiTimingPoint(0d, _content: text, bpm: 120f);
            List<SimaiNote> notes;
            try
            {
                notes = timing.getNotes();
            }
            catch (Exception e)
            {
                Console.WriteLine($"  {text,-16} THREW {e.Message}");
                continue;
            }

            foreach (var note in notes)
                Console.WriteLine(
                    $"  {text,-16} {note.noteType} touchArea='{note.touchArea}' " +
                    $"startPosition={note.startPosition} " +
                    $"touchSlide={note.isTouchSlide} noHead={note.isSlideNoHead}");
            if (notes.Count == 0)
                Console.WriteLine(
                    $"  {text,-16} no notes, err='{timing.noteParseError}'");
        }
        Console.WriteLine();
    }

    /// <summary>
    /// The production entry point, on chart text shaped the way a real chart opens.
    /// Serialize is what the editor actually runs, so a note in its notelist that
    /// the text never wrote is the phantom as the player sees it.
    /// </summary>
    private static void PhantomNoteFromWholeChartText()
    {
        Console.WriteLine("=== phantom notes from whole-chart Serialize ===");
        foreach (var (label, text) in new[]
                 {
                     ("command first, no leading newline", "<SV*1>,,,,1,"),
                     ("bpm then command", "(120)<SV*1>,,,,1,"),
                     ("meter then command", "{4}<SV*1>,,,,1,"),
                     ("bpm meter command", "(120){4}<SV*1>,,,,1,"),
                     ("command after newline", "\n<SV*1>,,,,1,"),
                     ("two commands", "<SV*1><HS*2>,,,,1,"),
                     ("shake", "<SHAKE*1>,,,,1,"),
                     ("fake", "<FAKE*1>,,,,1,"),
                     ("colorv", "<COLORV*FF0000>,,,,1,"),
                     ("unknown command", "<NOSUCHCMD*1>,,,,1,"),
                     ("command mid chart", "1,<SV*2>,2,"),
                     ("spawn", "<SPAWN*1>,,,,1,"),
                     ("destroy", "<DESTROY*4.8>,,,,1,")
                 })
        {
            try
            {
                SimaiProcess.Serialize(text);
                var phantoms = SimaiProcess.notelist
                    .SelectMany(timing => timing.getNotes()
                        .Select(note => (timing, note)))
                    .Where(pair => !text.Contains(pair.note.noteContent ?? "@@@"))
                    .ToList();
                var all = SimaiProcess.notelist
                    .SelectMany(timing => timing.getNotes()
                        .Select(note =>
                            $"t={timing.time:F2} {note.noteType} key={note.startPosition} " +
                            $"'{note.noteContent}'"))
                    .ToList();
                Console.WriteLine(
                    $"  {label,-34} notes={all.Count,2} " +
                    $"phantom={phantoms.Count} :: {string.Join(" | ", all)}");
            }
            catch (Exception e)
            {
                Console.WriteLine($"  {label,-34} THREW {e.Message}");
            }
        }
        Console.WriteLine();
    }

    /// <summary>
    /// Beats that carry no playable note at all. Any note that comes back from one
    /// of these is a phantom: SimaiNote defaults startPosition to 1 and time to 0,
    /// so an unfilled note lands on key 1 at second 0, which is exactly where one
    /// was reported appearing out of nowhere.
    /// </summary>
    private static void PhantomNoteFromContentThatHasNoNote()
    {
        Console.WriteLine("=== beats with no playable note: does one come back? ===");
        foreach (var content in new[]
                 {
                     "<SV*1>",
                     "<HS*2>",
                     "<SV*1><HS*1>",
                     "<COLOR*FF0000>",
                     "<SHAKE*1>",
                     "<FAKE*1>",
                     "",
                     " ",
                     "<SV*1>1",
                     "1<SV*1>",
                     "<SV*1>/1"
                 })
        {
            var timing = new SimaiTimingPoint(0d, _content: content, bpm: 120f);
            List<SimaiNote> notes;
            string error;
            try
            {
                notes = timing.getNotes();
                error = timing.noteParseError ?? string.Empty;
            }
            catch (Exception e)
            {
                Console.WriteLine($"  '{content,-16}' THREW {e.Message}");
                continue;
            }

            var detail = string.Join(
                " ; ",
                notes.Select(note =>
                    $"{note.noteType} key={note.startPosition} " +
                    $"fake={note.isFake} content='{note.noteContent}'"));
            Console.WriteLine(
                $"  '{content,-16}' notes={notes.Count} " +
                $"err='{(error.Length > 40 ? error.Substring(0, 40) : error)}' {detail}");
        }
        Console.WriteLine();
    }

    /// <summary>
    /// How much of an early note's approach falls before time zero, which is the
    /// window the 'AudioTime &lt; 0' gate in TapBase/StarDrop/HoldDrop/EachLineDrop
    /// refuses to draw. Whatever is hidden there is approach the player never
    /// sees: the note is already partway down when it finally appears.
    /// </summary>
    private static void ApproachHiddenByNegativeTimeGate()
    {
        Console.WriteLine(
            "=== approach hidden before t=0 (spawn " +
            $"{AlphaVisualTiming.DefaultSpawnRadius}, destroy " +
            $"{AlphaVisualTiming.DefaultDestroyRadius}) ===");

        var curve = new[] { new ScrollPoint(0d, 0d, 1f) };
        foreach (var speed in new[] { 4f, 7f, 10f })
        {
            foreach (var noteTime in new[] { 0f, 0.1f, 0.2f, 0.5f, 1f })
            {
                var noteScroll = AlphaVisualTiming.GetCumulativeScroll(curve, noteTime);

                // Walk back from judge time to find where the note leaves spawn.
                var appearTime = noteTime;
                for (var t = noteTime; t > -5f; t -= 0.001f)
                {
                    var radius = AlphaVisualTiming.GetVisualRadius(
                        noteScroll,
                        AlphaVisualTiming.GetCumulativeScroll(curve, t),
                        speed,
                        AlphaVisualTiming.DefaultSpawnRadius,
                        AlphaVisualTiming.DefaultDestroyRadius);
                    if (radius <= AlphaVisualTiming.DefaultSpawnRadius)
                    {
                        appearTime = t;
                        break;
                    }
                }

                var radiusAtZero = AlphaVisualTiming.GetVisualRadius(
                    noteScroll,
                    AlphaVisualTiming.GetCumulativeScroll(curve, 0d),
                    speed,
                    AlphaVisualTiming.DefaultSpawnRadius,
                    AlphaVisualTiming.DefaultDestroyRadius);
                var travel = AlphaVisualTiming.DefaultDestroyRadius -
                             AlphaVisualTiming.DefaultSpawnRadius;
                var shownFraction = Math.Clamp(
                    (AlphaVisualTiming.DefaultDestroyRadius - radiusAtZero) / travel,
                    0f,
                    1f);

                Console.WriteLine(
                    $"  speed {speed,4}  note t={noteTime,4}  " +
                    $"approach starts {appearTime,7:F3}  " +
                    $"radius at t=0 {radiusAtZero,6:F2}  " +
                    $"pops in {(1f - shownFraction) * 100f,5:F1}% down the path");
            }
        }
        Console.WriteLine();
    }

    /// <summary>
    /// Every gate the expression has to pass between the editor's text and a
    /// drawn note. A silent no-show means one of them said no without telling
    /// anyone, so they are checked one at a time rather than in one call.
    /// </summary>
    private static void Gates(string text)
    {
        Console.WriteLine();
        Console.WriteLine($"  {text}");

        var timing = new SimaiTimingPoint(1d, _content: text, bpm: 120f);
        var notes = timing.getNotes();
        Console.WriteLine(
            $"    1 runtime parse : " +
            (string.IsNullOrEmpty(timing.noteParseError)
                ? $"ok, {notes.Count} note(s)"
                : "REJECT " + timing.noteParseError.Split('\n')[0]));
        if (notes.Count == 0)
            return;

        var note = notes[0];
        Console.WriteLine(
            $"    2 note fields   : type={note.noteType} " +
            $"touchSlide={note.isTouchSlide} pathExpr='{note.pathExpression}' " +
            $"segments={note.slidePath.Count} slideTime={note.slideTime:F3} " +
            $"startTime={note.slideStartTime:F3}");

        // What TouchSlideDrop.TryBuildExpressionPath re-runs on the data it was
        // already handed. A reject here drops the note with no message.
        var expression = note.pathExpression ?? note.noteContent ?? string.Empty;
        if (!SlidePathParser.TryParsePath(expression, out var path))
        {
            Console.WriteLine("    3 view reparse  : REJECT TryParsePath");
            return;
        }
        Console.WriteLine(
            $"    3 view reparse  : ok, {path.segments.Count} segment(s) " +
            string.Join(" ", path.segments.Select(s =>
                $"[{s.start.area}{s.start.position}-{s.shape}->" +
                $"{s.end.area}{s.end.position} dur='{s.duration}']")));

        Console.WriteLine(
            "    4 validate      : " +
            (SlideSyntaxValidator.TryValidate(path, out var e1)
                ? "whole ok"
                : "REJECT " + e1.Split('\n')[0]) +
            " | " +
            (SlideSyntaxValidator.TryValidateSegments(path.segments, out var e2)
                ? "segments ok"
                : "REJECT " + e2.Split('\n')[0]));

        Console.WriteLine(
            "    5 modifiers     : " +
            (NoteModifierParser.TryParse(expression, path.segments, out _)
                ? "ok"
                : "REJECT -> loader throws -> note silently dropped"));

        // The geometry the renderer walks. A zero or NaN length draws nothing.
        for (var i = 0; i < path.segments.Count; i++)
        {
            var segment = path.segments[i];
            var resolved = SlideShapeResolver.TryResolve(
                segment, out var prefabKey, out var issue, out var reason);
            Console.WriteLine(
                $"    6 shape[{i}]     : " +
                (resolved
                    ? $"ok prefab='{prefabKey}' issue={issue}"
                    : $"REJECT issue={issue} {reason}"));
        }
    }
}
