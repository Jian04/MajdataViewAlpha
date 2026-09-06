using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using MajdataCore;

namespace MajdataEdit;

internal static class Program
{
    private static int assertions;

    private static void Assert(bool condition, string message)
    {
        assertions++;
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static SlidePathData ParseAst(string source)
    {
        Assert(
            SlidePathParser.TryParsePath(source, out var path),
            $"Parser rejected {source}");
        Assert(
            SlideSyntaxValidator.TryValidate(path, out var error),
            $"Validator rejected {source}: {error}");
        Assert(
            NoteModifierParser.TryParse(source, path.segments, out _),
            $"Modifier parser rejected {source}");
        return path;
    }

    private static bool IsValid(string source)
    {
        return SlidePathParser.TryParsePath(source, out var path) &&
               SlideSyntaxValidator.TryValidate(path, out _) &&
               NoteModifierParser.TryParse(source, path.segments, out _);
    }

    /// <summary>
    /// Walks every way a duration can be spread over a connected slide instead of
    /// trusting a hand-picked list. A connected slide is timed either by one total
    /// on its last segment or by one duration per segment; every other arrangement
    /// leaves part of the path with a length nobody wrote. Hand-picked cases had
    /// missed a lone duration sitting on an earlier joint, which was accepted in
    /// silence.
    /// </summary>
    private static void CheckDurationPlacementMatrix()
    {
        // Endpoints never repeat, so no case is rejected for its geometry instead
        // of its durations. The last row ends on a Touch area, where per-segment
        // durations are allowed too and the renderer paces by their sum.
        foreach (var joints in new[]
                 {
                     new[] { "1", "3", "5", "7", "2" },
                     new[] { "1", "3", "5", "7", "E2" }
                 })
        {
            for (var segmentCount = 1; segmentCount < joints.Length; segmentCount++)
            {
                for (var mask = 0; mask < 1 << segmentCount; mask++)
                {
                    var text = new System.Text.StringBuilder(joints[0]);
                    for (var segment = 0; segment < segmentCount; segment++)
                    {
                        text.Append('-').Append(joints[segment + 1]);
                        if ((mask & (1 << segment)) != 0)
                            text.Append("[8:1]");
                    }

                    var written = System.Numerics.BitOperations.PopCount((uint)mask);
                    var onLastSegment = (mask & (1 << (segmentCount - 1))) != 0;
                    var expected = written == segmentCount ||
                                   (written == 1 && onLastSegment);
                    var source = text.ToString();
                    Assert(
                        IsValid(source) == expected,
                        $"Duration placement for {source} is " +
                        $"{(IsValid(source) ? "accepted" : "rejected")}, expected " +
                        $"{(expected ? "accepted" : "rejected")}.");
                }
            }
        }
    }

    private static void CheckMirror()
    {
        static string Apply(string source, Mirror.HandleType type) =>
            Mirror.NoteMirrorHandle(source, type);

        Assert(Apply("1d", Mirror.HandleType.LRMirror) == "1d",
            "LR mirror changed D1's centered horizontal position.");
        Assert(Apply("1d", Mirror.HandleType.UDMirror) == "5d",
            "UD mirror did not map D1 to D5.");
        Assert(Apply("A1/B3/C2/D1/E2", Mirror.HandleType.LRMirror) ==
               "A8/B6/C2/D1/E8",
            "LR Touch-area mapping is incorrect.");
        Assert(Apply("A1/B3/C2/D1/E2", Mirror.HandleType.UDMirror) ==
               "A4/B2/C2/D5/E4",
            "UD Touch-area mapping is incorrect.");

        const string tangent = "A1pE5[8:1]/A1ppE5[8:1]";
        Assert(Apply(tangent, Mirror.HandleType.LRMirror) ==
               "A8qE5[8:1]/A8qqE5[8:1]",
            "LR mirror did not reverse p/pp chirality.");
        Assert(Apply(tangent, Mirror.HandleType.UDMirror) ==
               "A4qE1[8:1]/A4qqE1[8:1]",
            "UD mirror did not reverse p/pp chirality.");
        Assert(Apply(tangent, Mirror.HandleType.HalfRotation) ==
               "A5pE1[8:1]/A5ppE1[8:1]",
            "Half rotation incorrectly changed p/pp chirality.");
        Assert(Apply("A1<E5[8:1]", Mirror.HandleType.UDMirror) ==
               "A4>E1[8:1]",
            "UD mirror did not reverse ring direction.");
        Assert(Apply("A1<E5[8:1]", Mirror.HandleType.HalfRotation) ==
               "A5<E1[8:1]",
            "Half rotation incorrectly changed ring direction.");
        Assert(Apply(tangent, Mirror.HandleType.Rotation45) ==
               "A2pE6[8:1]/A2ppE6[8:1]",
            "45-degree rotation incorrectly changed tangent chirality.");

        const string commandSource =
            "A1<E5[8:1],<SV*slide=1.5,touch=-1>,<COLOR*slide=FF00AA>";
        var mirrored = Apply(commandSource, Mirror.HandleType.LRMirror);
        Assert(
            mirrored ==
            "A8>E5[8:1],<SV*slide=1.5,touch=-1>,<COLOR*slide=FF00AA>",
            "Mirror changed Alpha command names or values.");

        var sample = "1d/A2pE6[8:1]/B7qqD3[4:1]/C2-E4[8:1]";
        Assert(
            Apply(
                Apply(sample, Mirror.HandleType.LRMirror),
                Mirror.HandleType.LRMirror) == sample &&
            Apply(
                Apply(sample, Mirror.HandleType.UDMirror),
                Mirror.HandleType.UDMirror) == sample &&
            Apply(
                Apply(sample, Mirror.HandleType.Rotation45),
                Mirror.HandleType.CcwRotation45) == sample,
            "Mirror transformations are not reversible.");
    }

    private static SimaiNote ParseRuntime(string source)
    {
        var timing = new SimaiTimingPoint(
            1d, _content: source, bpm: 120f);
        var notes = timing.getNotes();
        Assert(
            timing.noteParseError == null,
            $"Runtime rejected {source}: {timing.noteParseError}");
        Assert(notes.Count == 1, $"Runtime note count for {source}");
        return notes[0];
    }

    private static string? ParseError(string source)
    {
        var timing = new SimaiTimingPoint(
            1d, _content: source, bpm: 120f);
        timing.getNotes();
        return timing.noteParseError;
    }

    private static void RejectRuntime(string source)
    {
        var timing = new SimaiTimingPoint(
            1d, _content: source, bpm: 120f);
        Assert(
            timing.getNotes().Count == 0,
            $"Invalid note reached runtime: {source}");
        Assert(
            !string.IsNullOrWhiteSpace(timing.noteParseError),
            $"Invalid note has no parse error: {source}");
        Assert(
            timing.getNotes().Count == 0,
            $"Invalid note changed on repeated access: {source}");
    }

    private static void CheckSyntax(string source, bool expected)
        => CheckSyntaxRaw($"(120){{4}}{source},E", expected, source);

    /// <summary>
    /// The editor's real gate: the two passes MainWindow.SyntaxCheck() runs to
    /// decide what gets a squiggle. This used to drive SyntaxChecker, which no
    /// part of the editor ever called, so a green test said nothing about what
    /// the user would actually see.
    /// </summary>
    private static int EditorErrorCount(string chart)
    {
        SimaiProcess.ClearData();
        SimaiProcess.Serialize(chart);
        var errors = SimaiProcess.notelist.Count(point =>
            !string.IsNullOrWhiteSpace(point.noteParseError));
        errors += SimaiProcess.ValidateAlphaCommands(chart).Count;
        SimaiProcess.ClearData();
        return errors;
    }

    private static void CheckSyntaxRaw(
        string chart, bool expected, string? label = null)
    {
        Assert(
            (EditorErrorCount(chart) == 0) == expected,
            $"Syntax/runtime disagreement for {label ?? chart}");
    }

    /// <summary>
    /// BOUNCE has to answer to both SV and HS. Its duration is authored in scroll
    /// distance, so SV and HS together decide how much real time that is; and the
    /// side it takes off from follows the combined direction, not HS alone.
    /// </summary>
    private static void CheckBounceSigns()
    {
        const double judge = 4d;
        const float duration = 1f;
        const float spawn = 1.225f;
        const float destroy = 4.8f;

        static void LoadSv(float multiplier) =>
            SvController.Load(
                new List<global::SvPoint>
                {
                    new global::SvPoint
                    {
                        time = 0d,
                        multiplier = multiplier,
                        noteType = string.Empty
                    }
                },
                0d);

        // What NoteDrop.GetBounceDistance computes.
        static float Radius(float progress, float direction)
        {
            var fromApex = progress * 2f - 1f;
            var magnitude = spawn + (destroy - spawn) * fromApex * fromApex;
            return direction < 0f ? -magnitude : magnitude;
        }

        foreach (var sv in new[] { 2f, 1f, 0.5f, -1f, -2f })
        foreach (var hs in new[] { 2f, 1f, 0.5f, -0.5f, -1f, -2f })
        {
            LoadSv(sv);
            var direction = SvController.GetBounceDirection(judge, hs);
            var takeoff = SvController.GetBounceStartTime(judge, duration, hs);
            var label = $"SV={sv} HS={hs}";

            // Both speeds scale the time the bounce occupies.
            var span = judge - takeoff;
            var expectedSpan = duration / Math.Abs(sv * hs);
            Assert(
                Math.Abs(span - expectedSpan) < 0.001d,
                $"{label}: bounce span {span:F3} should be {expectedSpan:F3}.");

            // Flipping either speed flips the side; flipping both flips it back.
            var expectedDirection = sv * hs < 0f ? -1f : 1f;
            Assert(
                Math.Abs(direction - expectedDirection) < 0.001f,
                $"{label}: direction {direction} should be {expectedDirection}.");

            // The path runs from the destroy ring in to the spawn ring and back
            // out, on the side the direction chose.
            var atTakeoff = SvController.GetBounceProgress(
                judge, duration, hs, direction, takeoff);
            var atApex = SvController.GetBounceProgress(
                judge, duration, hs, direction, takeoff + span / 2d);
            var atJudge = SvController.GetBounceProgress(
                judge, duration, hs, direction, judge);
            Assert(
                Math.Abs(atTakeoff) < 0.001f &&
                Math.Abs(atApex - 0.5f) < 0.001f &&
                Math.Abs(atJudge - 1f) < 0.001f,
                $"{label}: progress {atTakeoff:F3}/{atApex:F3}/{atJudge:F3} " +
                "should run 0 to 1.");
            Assert(
                Math.Abs(Math.Abs(Radius(atTakeoff, direction)) - destroy) < 0.001f &&
                Math.Abs(Math.Abs(Radius(atApex, direction)) - spawn) < 0.001f,
                $"{label}: bounce must reach the spawn ring at its apex.");
            Assert(
                Radius(atApex, direction) < 0f == expectedDirection < 0f,
                $"{label}: bounce drew on the wrong side of the centre.");
        }

        SvController.Load(new List<global::SvPoint>(), 0d);
    }

    private static void CheckChartTokens()
    {
        // The invariant that keeps the offsets honest: every reported span must
        // still be the text it was parsed from. A second scan could drift from
        // the parse; reporting during the parse cannot.
        static ChartTokenList Tokens(string expression)
        {
            var tokens = new ChartTokenList();
            Assert(
                SlidePathParser.TryParsePath(expression, out _, tokens),
                $"Token scan needs a parsable path: {expression}");
            return tokens;
        }

        static void AssertCovers(string expression, ChartTokenKind kind, string expected)
        {
            var tokens = Tokens(expression);
            var found = string.Concat(tokens.tokens
                .Where(token => token.kind == kind)
                .OrderBy(token => token.start)
                .Select(token => expression.Substring(token.start, token.length)));
            Assert(
                found == expected,
                $"{kind} spans of {expression} read '{found}', expected '{expected}'.");
        }

        AssertCovers("1-5[8:1]", ChartTokenKind.Position, "15");
        AssertCovers("1-5[8:1]", ChartTokenKind.Shape, "-");
        AssertCovers("1-5[8:1]", ChartTokenKind.Duration, "[8:1]");
        AssertCovers("1-5[8:1]", ChartTokenKind.Modifier, "");

        // Head modifiers, body modifiers and the 4.4 form that writes the body
        // modifier after the duration must all land on the modifier colour.
        AssertCovers("1b-5[8:1]", ChartTokenKind.Modifier, "b");
        AssertCovers("1-5b[8:1]", ChartTokenKind.Modifier, "b");
        AssertCovers("1-5[8:1]b", ChartTokenKind.Modifier, "b");
        AssertCovers("1bf-5m[8:1]", ChartTokenKind.Modifier, "bfm");

        // Multi-character shapes must be one span, not one per character.
        AssertCovers("1pp5[8:1]", ChartTokenKind.Shape, "pp");
        AssertCovers("1rq5[8:1]", ChartTokenKind.Shape, "rq");
        AssertCovers("1<<5[8:1]", ChartTokenKind.Shape, "<<");
        Assert(
            Tokens("1pp5[8:1]").tokens.Count(t => t.kind == ChartTokenKind.Shape) == 1,
            "A two-character shape is one span.");

        // A V slide reports its middle position too, and D-zone and touch
        // positions keep their suffix inside the same span.
        AssertCovers("1V35[8:1]", ChartTokenKind.Position, "135");
        AssertCovers("1d-5d[8:1]", ChartTokenKind.Position, "1d5d");
        AssertCovers("E1-E5[8:1]", ChartTokenKind.Position, "E1E5");
        AssertCovers("A1VCE2[8:1]", ChartTokenKind.Position, "A1CE2");

        // A chained slide shares one position object between segments, so the
        // joint must be reported once, not twice.
        AssertCovers("1-3[8:1]-5[8:1]", ChartTokenKind.Position, "135");
        AssertCovers("1-3[8:1]-5[8:1]", ChartTokenKind.Duration, "[8:1][8:1]");

        // A path is nothing but the pieces the parser reports, so the spans must
        // tile it exactly: no gap, no overlap, nothing past the end. That is a
        // stronger statement than "each span is right" and it is what lets a
        // caller colour every character without a fallback.
        foreach (var expression in new[]
                 {
                     "1-5[8:1]", "1bf-5m[8:1]", "1V35[8:1]", "A1VCE2[8:1]",
                     "1-3[8:1]-5[8:1]", "1d-5d[8:1]", "1<<5[8:1]", "1-5[8:1]b",
                     "1pp5[8:1]", "1rq5[8:1]", "E1-E5[8:1]", "1b-5[8:1]"
                 })
            Assert(
                TokensTile(expression, out var reason),
                $"Token spans must tile {expression}: {reason}");
    }

    /// <summary>
    /// True when the reported spans cover the expression exactly once, in order.
    /// </summary>
    private static bool TokensTile(string expression, out string reason)
    {
        reason = string.Empty;
        var tokens = new ChartTokenList();
        if (!SlidePathParser.TryParsePath(expression, out _, tokens))
        {
            reason = "path did not parse";
            return false;
        }

        var previousEnd = 0;
        foreach (var token in tokens.tokens.OrderBy(token => token.start))
        {
            if (token.start != previousEnd)
            {
                reason = token.start < previousEnd
                    ? $"overlap at {token.start}"
                    : $"gap at {previousEnd}";
                return false;
            }
            previousEnd = token.End;
        }

        if (previousEnd != expression.Length)
        {
            reason = $"covered {previousEnd} of {expression.Length}";
            return false;
        }
        return true;
    }

    private static void CheckBracketTracker()
    {
        // Where a beat ends, the way the error squiggle asks it. The old copies
        // counted every '<' as nesting, so a '<' slide left them permanently
        // "inside a bracket" and the squiggle ran to the end of the line.
        static int TokenEnd(string text, int from)
        {
            var tracker = new ChartBracketTracker();
            var end = from;
            while (end < text.Length)
            {
                if (tracker.IsTopLevel &&
                    (text[end] == ',' || char.IsWhiteSpace(text[end])))
                    break;
                tracker.Advance(text, end);
                end++;
            }
            return end;
        }

        Assert(TokenEnd("1<5[8:1],2,", 0) == 8, "'<' slide must not open a group.");
        Assert(TokenEnd("1>5[8:1],2,", 0) == 8, "'>' slide must not close a group.");
        Assert(TokenEnd("1-5[8:1],2,", 0) == 8, "Plain slide token end.");
        Assert(TokenEnd("E1<E6-E3[8:8],2,", 0) == 13,
            "Touch '<' slide must not open a group.");
        Assert(TokenEnd("<SV*1>1-5[8:1],2,", 0) == 14,
            "A real command keeps its comma inside the token.");
        Assert(TokenEnd("1-5[8:1]/2,3,", 0) == 10, "Each group stays one token.");

        // A duration comma is nested, so it must not end the beat.
        var nested = new ChartBracketTracker();
        var source = "1-5[8:1]";
        nested.Advance(source, 3);
        Assert(!nested.IsTopLevel, "'[' opens a group immediately.");
        Assert(nested.Innermost == ChartBracket.Square, "'[' is the square group.");
        nested.Advance(source, 7);
        Assert(nested.IsTopLevel, "']' closes its group.");

        // One stray closer must not make the rest of the line look nested.
        var stray = new ChartBracketTracker();
        stray.Advance("]1,", 0);
        Assert(stray.IsTopLevel, "A closer with nothing open is ignored.");

        // A command's own '>' only closes what a command opened.
        var command = new ChartBracketTracker();
        var withCommand = "<SV*1>";
        for (var i = 0; i < withCommand.Length; i++)
            command.Advance(withCommand, i);
        Assert(command.IsTopLevel, "A command closes its own group.");
    }

    private static void CheckBeatBrush()
    {
        // Total measure units must survive the brush. A remainder that no beat
        // divides used to be dropped, which silently shortened the chart.
        static int MeasureUnits(string text)
        {
            var beat = 4;
            var total = 0;
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '{')
                {
                    var close = text.IndexOf('}', i + 1);
                    if (close > i &&
                        int.TryParse(text.Substring(i + 1, close - i - 1), out var parsed))
                    {
                        beat = parsed;
                        i = close;
                        continue;
                    }
                }
                if (text[i] == ',')
                    total += 384 / beat;
            }
            return total;
        }

        static void SameLength(string source)
        {
            var whole = Editor.BeatFormatBrush.Transform(source, null);
            Assert(
                MeasureUnits(source) == MeasureUnits(whole),
                $"Beat brush changed the total length of {source}");
            var selection = Editor.BeatFormatBrush.TransformSelection(
                source, 0, source.Length, null);
            Assert(
                MeasureUnits(source) == MeasureUnits(selection),
                $"Beat-brush selection changed the total length of {source}");
        }

        Assert(
            Editor.BeatFormatBrush.Transform("{16}1,{32},,1,,,,1,,,", null) ==
            "{32}1,,,,1,,,,1,,,",
            "Highest-beat brush did not normalize a {16} start.");
        Assert(
            Editor.BeatFormatBrush.Transform("{8}1,{32},,1,,,,1,,,", null) ==
            "{32}1,,,,,,1,,,,1,,,",
            "Highest-beat brush did not normalize an {8} start.");
        Assert(
            Editor.BeatFormatBrush.Transform("{8}1,1,1,1,1,1,1,", 24) ==
            "{24}1,,,1,,,1,,,1,,,1,,,1,,,1,,,",
            "The 24-beat brush did not preserve seven eighth-note intervals.");
        Assert(
            Editor.BeatFormatBrush.Transform("{8}1,", 384) ==
            "{384}1" + new string(',', 48),
            "The explicit 384-beat brush produced the wrong interval length.");
        Assert(
            Editor.BeatFormatBrush.Transform("{256}1,{384}2,", null) ==
            "{768}1,,,2,,",
            "Automatic beat brushing must not cap the common beat at 384.");
        Assert(
            Editor.BeatFormatBrush.Transform("{16}1,\n{32},,1,,,,1,,,\n", null)
                .Contains('\n'),
            "Beat brush collapsed the chart onto one line.");
        foreach (var source in new[]
                 {
                     "{16}1,{32},,1,,,,1,,,",
                     "{8}1,{32},,1,,,,1,,,",
                     "{12}1,{32},,1,,,,1,,,",
                     "{4}1,,{16}2,,,,3,",
                     "{16},{32},,1,,,,1,,,",
                     "{16}1,{32},,1,,,,1,,,,",
                     "{12}1,{16},,{32},,,1,",
                     "{24}1,,{32},,{16},1,"
                 })
            SameLength(source);
    }


    // A rejection is written in exactly one language, on one line, and quotes the
    // text it is complaining about. Showing both languages at once doubled the
    // length of every tooltip, so a second line is now a defect.
    private static void CheckDiagnosticShape(string source, string error)
    {
        Assert(!string.IsNullOrWhiteSpace(error),
            $"Rejecting {source} produced an empty message.");
        Assert(!error.Contains('\n'),
            $"Message for {source} spans more than one line: {error}");

        var hasChinese = error.Any(
            character => character >= 0x4E00 && character <= 0x9FFF);
        if (ParserMessageLocale.PreferChinese)
        {
            Assert(hasChinese,
                $"Chinese message for {source} has no Chinese: {error}");
            return;
        }

        Assert(!hasChinese,
            $"English message for {source} contains Chinese: {error}");
        // The sentence itself is uppercase; the quoted shape and the quoted
        // offending text keep the author's own casing.
        var sentence = error;
        var tokenStart = sentence.LastIndexOf(": ", StringComparison.Ordinal);
        if (tokenStart >= 0)
            sentence = sentence[..tokenStart];
        sentence = string.Concat(
            sentence.Split('\'').Where((_, index) => index % 2 == 0));
        Assert(
            sentence.Any(char.IsUpper) && !sentence.Any(char.IsLower),
            $"English message for {source} is not uppercase: {error}");
    }

    private static void CheckDiagnostics(IEnumerable<string> invalidSources)
    {
        foreach (var source in invalidSources)
        {
            if (!SlidePathParser.TryParsePath(source, out var path))
                continue;
            if (SlideSyntaxValidator.TryValidate(path, out var error))
                continue;
            CheckDiagnosticShape(source, error);
            Assert(
                error.Contains(ParserMessageLocale.PreferChinese ? "：" : ": "),
                $"Message for {source} does not quote the offending text: {error}");
        }

        // A duration without h is the mistake that used to report only
        // "unknown modifier", so it must name the note and suggest the fix.
        var holdError = ParseError("4[12:1]") ?? string.Empty;
        CheckDiagnosticShape("4[12:1]", holdError);
        Assert(holdError.Contains("4[12:1]", StringComparison.Ordinal),
            $"Hold-without-h error does not quote the note: {holdError}");
        Assert(holdError.Contains("4h[12:1]", StringComparison.Ordinal),
            $"Hold-without-h error does not suggest 4h[12:1]: {holdError}");

        // Each distinct cause needs its own sentence; three different chain
        // problems used to share one message.
        var causes = new[]
        {
            ParseError("1-5[8:1]-7-3[8:1]"),
            ParseError("1-3b-5[8:1]"),
            ParseError("1-5[8:1]-7")
        };
        Assert(causes.Distinct().Count() == causes.Length,
            "Different chain problems still share one message.");
    }

    // The note-level AST is not wired into SimaiProcess yet. Until it is, its only
    // job is to agree with the shipping parser on every note in the corpus: same
    // accept/reject verdict, same kind, same position. Any disagreement here is a
    // bug in the AST, not a new behaviour.
    private static void CheckNoteExpression()
    {
        foreach (var slot in new[]
                 {
                     "Ch[8:4]f", "2B4K6[4:1]", "2B4K6[8:1]/6B8K2[8:1]",
                     "5P6K7[8:1]/4Q4K2[8:1]", "1P333K4[4:3]/8",
                     "8Q777K5[4:3]/1", "3Q1K6[8:5]b",
                     "2P1K7[8:1]", "6Q5K3[8:1]", "2Q1K7[8:1]", "6P5K3[8:1]",
                     "2B1K6[8:1]/5P5K1[8:1]", "4Q5K8[8:1]/7B8K3[8:1]",
                     "6B3K1[8:1]/3B6K8[8:1]", "1Q1CP5K5[8:1]/8P1CQ5K4[8:1]"
                 })
        {
            var timing = new SimaiTimingPoint(1d, _content: slot, bpm: 120f);
            var parsedNotes = timing.getNotes();
            Assert(string.IsNullOrWhiteSpace(timing.noteParseError) &&
                   parsedNotes.Count == slot.Count(character => character == '/') + 1,
                $"Reported chart slot failed: {slot}: {timing.noteParseError}");
            Assert(NotePreviewModule.ExpandPreview(slot).Count > 0,
                $"Reported chart slot has no preview: {slot}");
        }
        Assert(NoteExpressionParser.TryParse("Ch[8:4]f", out var fireworkHold, out _) &&
               fireworkHold.kind == NoteExpressionKind.TouchHold &&
               fireworkHold.duration == "[8:4]" &&
               fireworkHold.modifiers.HasHead(NoteModifierFlags.Firework),
            "A trailing firework modifier must preserve TouchHold duration and type.");
        Assert(NoteExpressionParser.TryParse("1~[skins/K1.png]", out var skinCarrier, out _) &&
               skinCarrier.kind == NoteExpressionKind.Tap,
            "A K in a skin filename must not turn its carrier into SlideCode.");
        foreach (var invalid in new[] { "Ch[8:4]ff", "Ch[8:4]h", "Ch[8:4]2", "2B4[8:1]" })
            Assert(!NoteExpressionParser.TryParse(invalid, out _, out _),
                $"Invalid modifier suffix or missing K was accepted: {invalid}");

        static NoteExpressionKind? KindOf(SimaiNote note) => note.noteType switch
        {
            SimaiNoteType.Tap => NoteExpressionKind.Tap,
            SimaiNoteType.Hold => NoteExpressionKind.Hold,
            SimaiNoteType.Touch => NoteExpressionKind.Touch,
            SimaiNoteType.TouchHold => NoteExpressionKind.TouchHold,
            SimaiNoteType.Slide => NoteExpressionKind.Slide,
            _ => null
        };

        var corpus = new[]
        {
            "1", "8", "1b", "1x", "1f", "1bx", "1$", "1$$",
            "4d", "4db", "C", "C1", "C2", "E1", "A8", "B3", "D5",
            "Cb", "Cf", "E1x", "1h", "1bh", "1h[8:1]", "1hf[8:1]",
            "4dh[8:1]", "Ch[8:1]", "E1h[8:1]", "Chf[8:1]",
            "1-5[8:1]", "1?-5[8:1]", "1!-5[8:1]", "1b-5m[8:1]",
            "1V36[8:1]", "1w5[8:1]", "4d-E1-B3[8:1]", "A1<<E5[8:1]",
            "1-5[8:1]-7[4:1]", "1-5[3##160#8:1]",
            "4[12:1]", "1[8:1]", "E1[8:1]", "1[8:1]h", "1h[]",
            "0", "9", "1k5[8:1]", "1r5[8:1]", "1!?-5[8:1]",
            "1-2[8:1]", "1^5[8:1]", "F1", "1d5", "", "  "
        };

        foreach (var source in corpus)
        {
            var accepted = NoteExpressionParser.TryParse(
                source, out var expression, out var astError);
            var legacy = new SimaiTimingPoint(1d, _content: source, bpm: 120f);
            var notes = legacy.getNotes();
            var legacyAccepted = notes.Count == 1 &&
                                 string.IsNullOrWhiteSpace(legacy.noteParseError);
            Assert(accepted == legacyAccepted,
                $"AST and parser disagree on whether {source} is valid " +
                $"(AST {accepted}, parser {legacyAccepted}: {astError})");
            if (!accepted)
            {
                CheckDiagnosticShape(source, astError);
                continue;
            }

            var expected = KindOf(notes[0]);
            Assert(expected == expression.kind,
                $"AST kind for {source} is {expression.kind}, parser says {expected}");
            Assert(expression.position.position == notes[0].startPosition,
                $"AST position for {source} is {expression.position.position}, " +
                $"parser says {notes[0].startPosition}");
            if (expression.kind != NoteExpressionKind.Slide)
                Assert(
                    expression.position.IsTouch ==
                    (notes[0].noteType is SimaiNoteType.Touch or
                        SimaiNoteType.TouchHold),
                    $"AST Touch classification for {source} disagrees.");
        }

        // Kind must come from the AST, not from scanning for characters.
        Assert(
            NoteExpressionParser.TryParse("1h[8:1]", out var hold, out _) &&
            hold.kind == NoteExpressionKind.Hold &&
            hold.duration == "[8:1]" &&
            !hold.isZeroLengthHold,
            "Hold duration was not captured by the AST.");
        Assert(
            NoteExpressionParser.TryParse("1h", out var shortHold, out _) &&
            shortHold.isZeroLengthHold && shortHold.duration.Length == 0,
            "Zero-length Hold was not recognized by the AST.");
        Assert(
            NoteExpressionParser.TryParse("E1h[8:1]", out var touchHold, out _) &&
            touchHold.kind == NoteExpressionKind.TouchHold &&
            touchHold.position.area == 'E',
            "Touch Hold was not recognized by the AST.");
        Assert(
            !NoteExpressionParser.TryParse("4[12:1]", out _, out var missingH) &&
            missingH.Contains("4h[12:1]", StringComparison.Ordinal),
            "AST does not suggest 4h[12:1] for a duration without h.");
        Assert(
            NoteExpressionParser.TryParse("4d-E1-B3[8:1]", out var mixedPath, out _) &&
            mixedPath.kind == NoteExpressionKind.Slide &&
            mixedPath.isTouchPath &&
            mixedPath.position.isDZone,
            "AST lost the D-zone head of a mixed Touch path.");
    }

    // "E1~[4.8]" keeps E1's direction and sensor but is drawn at distance 4.8.
    private static void CheckTouchRadius()
    {
        static SimaiNote? Parse(string source)
        {
            var timing = new SimaiTimingPoint(1d, _content: source, bpm: 120f);
            var notes = timing.getNotes();
            return notes.Count == 1 &&
                   string.IsNullOrWhiteSpace(timing.noteParseError)
                ? notes[0]
                : null;
        }

        Assert(
            NoteExpressionParser.TryParse("E1~[4.8]", out var touch, out var touchError) &&
            touch.kind == NoteExpressionKind.Touch &&
            touch.position.area == 'E' &&
            touch.position.position == 1 &&
            Math.Abs(touch.position.radius - 4.8f) < 0.0001f,
            $"E1~[4.8] did not parse as a Touch with a custom distance: {touchError}");
        Assert(
            touch.position.ToExpression() == "E1~[4.8]",
            $"Custom distance is lost when the position is written back: " +
            $"{touch.position.ToExpression()}");

        var loaded = Parse("E1~[4.8]");
        Assert(
            loaded is not null &&
            loaded.noteType == SimaiNoteType.Touch &&
            loaded.touchArea == 'E' &&
            loaded.startPosition == 1 &&
            Math.Abs(loaded.touchRadius - 4.8f) < 0.0001f,
            "The custom Touch distance does not reach the note sent to View.");
        Assert(
            Parse("E1") is { touchRadius: 0f },
            "A plain Touch must keep the area's own distance.");
        Assert(
            Parse("B3~[1.5]") is { touchArea: 'B', startPosition: 3 } inner &&
            Math.Abs(inner.touchRadius - 1.5f) < 0.0001f,
            "A Touch drawn closer to the centre was not carried through.");

        // Modifiers still apply to the head, the distance is only a position.
        Assert(
            Parse("E1~[4.8]b") is { isBreak: true } &&
            Parse("E1~[4.8]f") is { isHanabi: true },
            "Modifiers stopped working once a custom distance was written.");

        foreach (var invalid in new[]
                 {
                     "1~[4.8]", "4d~[4.8]", "C~[4.8]", "C1~[4.8]",
                     "E1~4.8", "E1~", "E1~[", "E1~[]", "E1~[abc]",
                     "E1~[0]", "E1~[-1]", "E1~[48]", "E1~[1e9]",
                     "E1~[4.8]h[8:1]", "E1~[4.8]h", "A1~[3]-A5[8:1]",
                     "1-5~[3][8:1]", "E1~[4,8]", "E1~[4.8][8:1]"
                 })
        {
            Assert(
                !NoteExpressionParser.TryParse(invalid, out _, out var error),
                $"{invalid} must be rejected, a custom distance is Touch only.");
            CheckDiagnosticShape(invalid, error);
            Assert(
                Parse(invalid) is null,
                $"{invalid} was rejected by the AST but still loaded as a note.");
            CheckSyntax(invalid, false);
        }

        CheckSyntax("E1~[4.8]", true);
        CheckSyntax("E1~[4.8]b/A3~[2]", true);
        Assert(
            NotePreviewModule.ExpandPreview("E1~[4.8]").Contains("E1~[4.8]"),
            "A Touch with a custom distance does not preview as typed.");
        Assert(
            NotePreviewModule.ExpandPreview("E1~[9.9]").Count == 1,
            "A Touch inside the allowed range must preview.");
        Assert(
            NotePreviewModule.ExpandPreview("E1~[48]").Count == 0,
            "An out-of-range distance must not preview.");

        // Mirroring only maps positions; the bracket is data and must survive.
        foreach (var handle in new[]
                 {
                     Mirror.HandleType.LRMirror,
                     Mirror.HandleType.UDMirror,
                     Mirror.HandleType.Rotation45
                 })
        {
            var mirrored = Mirror.NoteMirrorHandle("E1~[4.8]", handle);
            Assert(
                mirrored.Contains("~[4.8]", StringComparison.Ordinal),
                $"Mirroring dropped the custom distance ({handle}): {mirrored}");
        }
    }

    // The shape grammar that used to live in the editor, kept here as an oracle so
    // the shared resolver cannot silently change which prefab a segment draws with.
    private static string? LegacyShapeFromText(string content)
    {
        static int RelativeEnd(int start, int end)
        {
            end -= start;
            if (end < 0) end += 8;
            if (end > 8) end -= 8;
            return end + 1;
        }

        static int MirrorKeys(int key) => key switch
        {
            1 => 1, 2 => 8, 3 => 7, 4 => 6,
            5 => 5, 6 => 4, 7 => 3, 8 => 2,
            _ => key
        };

        static bool IsUpperHalf(int key) => key is 7 or 8 or 1 or 2;

        try
        {
            if (content.Contains('-'))
            {
                var digits = content.Substring(0, 3).Split('-');
                var end = RelativeEnd(int.Parse(digits[0]), int.Parse(digits[1]));
                return end is < 3 or > 7 ? null : "line" + end;
            }
            if (content.Contains('>'))
            {
                var digits = content.Substring(0, 3).Split('>');
                var start = int.Parse(digits[0]);
                var end = RelativeEnd(start, int.Parse(digits[1]));
                return IsUpperHalf(start) ? "circle" + end : "-circle" + MirrorKeys(end);
            }
            if (content.Contains('<'))
            {
                var digits = content.Substring(0, 3).Split('<');
                var start = int.Parse(digits[0]);
                var end = RelativeEnd(start, int.Parse(digits[1]));
                return !IsUpperHalf(start) ? "circle" + end : "-circle" + MirrorKeys(end);
            }
            if (content.Contains('^'))
            {
                var digits = content.Substring(0, 3).Split('^');
                var end = RelativeEnd(int.Parse(digits[0]), int.Parse(digits[1]));
                if (end is 1 or 5)
                    return null;
                return end < 5 ? "circle" + end : "-circle" + MirrorKeys(end);
            }
            if (content.Contains('v'))
            {
                var digits = content.Substring(0, 3).Split('v');
                var end = RelativeEnd(int.Parse(digits[0]), int.Parse(digits[1]));
                return end == 5 ? null : "v" + end;
            }
            if (content.Contains("rp"))
            {
                var digits = content.Substring(0, 4)
                    .Split(new[] { "rp" }, StringSplitOptions.None);
                return "rppqq" + RelativeEnd(int.Parse(digits[1]), int.Parse(digits[0]));
            }
            if (content.Contains("rq"))
            {
                var digits = content.Substring(0, 4)
                    .Split(new[] { "rq" }, StringSplitOptions.None);
                return "-rppqq" +
                       MirrorKeys(RelativeEnd(int.Parse(digits[1]), int.Parse(digits[0])));
            }
            if (content.Contains("pp"))
            {
                var digits = content.Substring(0, 4).Split('p');
                return "ppqq" + RelativeEnd(int.Parse(digits[0]), int.Parse(digits[2]));
            }
            if (content.Contains("qq"))
            {
                var digits = content.Substring(0, 4).Split('q');
                return "-ppqq" +
                       MirrorKeys(RelativeEnd(int.Parse(digits[0]), int.Parse(digits[2])));
            }
            if (content.Contains('p'))
            {
                var digits = content.Substring(0, 3).Split('p');
                return "pq" + RelativeEnd(int.Parse(digits[0]), int.Parse(digits[1]));
            }
            if (content.Contains('q'))
            {
                var digits = content.Substring(0, 3).Split('q');
                return "-pq" +
                       MirrorKeys(RelativeEnd(int.Parse(digits[0]), int.Parse(digits[1])));
            }
            if (content.Contains('s'))
            {
                var digits = content.Substring(0, 3).Split('s');
                return RelativeEnd(int.Parse(digits[0]), int.Parse(digits[1])) != 5
                    ? null
                    : "s";
            }
            if (content.Contains('z'))
            {
                var digits = content.Substring(0, 3).Split('z');
                return RelativeEnd(int.Parse(digits[0]), int.Parse(digits[1])) != 5
                    ? null
                    : "-s";
            }
            if (content.Contains('V'))
            {
                var digits = content.Substring(0, 4).Split('V');
                var start = int.Parse(digits[0]);
                var turn = RelativeEnd(start, int.Parse(digits[1][0].ToString()));
                var end = RelativeEnd(start, int.Parse(digits[1][1].ToString()));
                if (turn == 7)
                    return end is < 2 or > 5 ? null : "L" + end;
                if (turn == 3)
                    return end < 5 ? null : "-L" + MirrorKeys(end);
                return null;
            }
            if (content.Contains('w'))
            {
                var digits = content.Substring(0, 3).Split('w');
                return RelativeEnd(int.Parse(digits[0]), int.Parse(digits[1])) != 5
                    ? null
                    : "wifi";
            }
        }
        catch (Exception)
        {
            return null;
        }

        return null;
    }

    /// <summary>
    /// The prefab resolver answers a key-position question: which of View's slide
    /// prefabs draws this segment. Touch slides never use one, because
    /// TouchSlideDrop samples its own geometry from AreaPosition, so the
    /// resolver's key rules must not gate them. "E5-E6" is refused as a key
    /// slide for having no key between its ends, but E5 and E6 are adjacent
    /// touch areas and that rule means nothing there.
    ///
    /// Nothing in the resolver's signature says this, and the three call sites
    /// honour it by convention alone. The play gate that enforces it lives in a
    /// WPF file the suite cannot compile, so the guards are pinned as source.
    /// </summary>
    private static void CheckTouchSlideBypassesPrefabResolver()
    {
        foreach (var expression in new[]
                 {
                     "E5-E6[8:1]", "E5-E6-E3[8:8]", "E1-E2[8:1]",
                     "A1-A2[8:1]", "E5-E6-E7-E8[8:8]"
                 })
        {
            Assert(
                SlidePathParser.TryParsePath(expression, out var path) &&
                path.isTouchPath,
                $"{expression} should parse as a touch path.");
            Assert(
                SlideSyntaxValidator.TryValidate(path, out var error),
                $"{expression} is a legal touch slide, but the shared validator " +
                $"refused it: {error}");

            // The other half of the statement: the resolver really does refuse
            // these, so the refusal has to stay confined to key-slide callers.
            Assert(
                !SlideShapeResolver.TryResolve(
                    path.segments[0], out _, out var issue, out _) &&
                issue == SlideShapeIssue.StraightTooClose,
                $"{expression} segment 0 was expected to be a key-slide refusal, " +
                "so this test would silently stop proving anything.");
        }

        var editor = File.ReadAllText("MajdataEdit/MainWindowCore.cs");
        Assert(
            editor.Contains(
                "note.noteType == SimaiNoteType.Slide && !note.isTouchSlide",
                StringComparison.Ordinal),
            "The play gate must keep skipping touch slides before it validates a " +
            "slide against View's prefab table.");

        var loader = File.ReadAllText("Assets/Scripts/JsonDataLoader.cs");
        Assert(
            !MethodBody(loader, "InstantiateTouchSlide")
                .Contains("DetectShape", StringComparison.Ordinal),
            "InstantiateTouchSlide must not consult the key-slide prefab " +
            "resolver: touch geometry is sampled, not looked up.");
    }

    /// <summary>
    /// The playfield and the notes on it must appear at the same moment.
    ///
    /// They did not: the judge line and background revealed at -2s while notes
    /// were withheld until 0. A note's approach starts before its own judge time,
    /// so a note near the start of the chart spent its whole approach withheld and
    /// then appeared already sitting on the judgement ring, unhittable, looking
    /// like a note nobody wrote.
    ///
    /// Two things have to hold. Every gate reads one constant, so they cannot
    /// drift apart again; and that constant is early enough for the slowest
    /// approach, so nothing is still mid-flight when it is finally drawn.
    /// </summary>
    /// <summary>
    /// ZOOM and MOVE have to carry the canvas half of the frame - the cover
    /// panels, the HUD readouts, the song card - and a Canvas batches its
    /// meshes in PostLateUpdate, before any camera callback. Driving those
    /// targets from OnPreCull left them pinned to the screen while the notes
    /// and the aperture moved, so they are moved in LateUpdate and, unlike the
    /// sprite targets, not restored afterwards.
    /// </summary>
    private static void CheckCanvasFrameMovesBeforeItRebuilds()
    {
        var source = File.ReadAllText("Assets/Scripts/UI/ScreenEffectController.cs");

        Assert(
            source.Contains("private void LateUpdate()", StringComparison.Ordinal),
            "Canvas frame targets have to be moved from LateUpdate: no camera " +
            "callback runs early enough to affect a canvas rebuild.");
        Assert(
            source.Contains("onCanvas: true", StringComparison.Ordinal) &&
            source.Contains("onCanvas: false", StringComparison.Ordinal),
            "The two halves of the frame move at different times, so applying " +
            "the transform has to be able to select one of them.");
        Assert(
            source.Contains("target.OnCanvas != onCanvas", StringComparison.Ordinal),
            "Applying the frame transform must honour the canvas split.");

        // The flag existed once before without a single reader, which is why the
        // HUD never moved. Every part of the pass has to consult it.
        foreach (var (method, marker) in new[]
                 {
                     ("ResetFrameTargets", "!target.OnCanvas"),
                     ("CaptureFrameTargets", "!target.OnCanvas")
                 })
        {
            var start = source.IndexOf($"private void {method}()", StringComparison.Ordinal);
            Assert(start >= 0, $"{method} should still exist.");
            var body = source.Substring(start, Math.Min(400, source.Length - start));
            Assert(
                body.Contains(marker, StringComparison.Ordinal),
                $"{method} must skip canvas targets ({marker}): restoring or " +
                "re-reading their pose undoes the move, or folds it into the base " +
                "so the frame creeps.");
        }

        Assert(
            source.Contains("RestoreCanvasTargets();", StringComparison.Ordinal),
            "Canvas targets sit in their moved pose, so re-registering them has " +
            "to put them back first or the offset becomes the new base.");
        Assert(
            source.Contains("onCanvas: true)", StringComparison.Ordinal),
            "Canvas children have to be registered as canvas targets; without " +
            "that the flag is dead and the HUD stays pinned.");
    }

    /// <summary>
    /// A bounce radius has to stay positive. getPositionFromDistance scales the
    /// key's direction vector by it, so a negative radius does not reverse the
    /// bounce - it puts the note on the key directly opposite, and a bounce
    /// written on key 1 was being drawn on key 5.
    /// </summary>
    // Playing out of a paused preview used to keep the preview's notes alive: the
    // paused-preview flag went false first, so they stopped taking the preview
    // branch and flew in against the playback clock as notes the chart never had.
    private static void CheckPlayRetiresThePausedPreviewsNotes()
    {
        var source = File.ReadAllText("Assets/Scripts/HttpHandler.cs");
        var start = source.IndexOf(
            "var replacePausedPreview = pausedTimelinePreviewActive;",
            StringComparison.Ordinal);
        Assert(start >= 0, "The start handler should still capture replacePausedPreview.");
        var head = source.Substring(start, Math.Min(3200, source.Length - start));
        var cancel = head.IndexOf("loader.CancelPendingLoad();", StringComparison.Ordinal);
        var clear = head.IndexOf("loader.ClearPreviewNotes();", StringComparison.Ordinal);
        var load = head.IndexOf("loader.LoadJson(", StringComparison.Ordinal);
        Assert(
            load >= 0,
            "The start handler should still load the playback chart.");
        Assert(
            cancel >= 0 && clear >= 0,
            "Replacing a paused preview must both cancel the pending load and " +
            "retire the preview's notes.");
        Assert(
            clear < load,
            "The paused preview's notes must be retired before the playback chart " +
            "is loaded, not after it finishes binding: until then they animate " +
            "against the playback clock as extra notes.");
    }

    // A beat that is legal text but that View cannot build has to travel back to
    // the editor. Otherwise the note is simply absent with nothing to search for.
    private static void CheckUnbuildableBeatsReachTheEditor()
    {
        var handler = File.ReadAllText("Assets/Scripts/HttpHandler.cs");
        Assert(
            handler.Contains("droppedBeats = drops", StringComparison.Ordinal),
            "Every View response must carry the beats the loader could not build.");
        var loader = File.ReadAllText("Assets/Scripts/JsonDataLoader.cs");
        Assert(
            loader.Contains("droppedBeats.Add(new DroppedBeat(", StringComparison.Ordinal) &&
            loader.Contains("droppedBeats.Clear();", StringComparison.Ordinal),
            "The loader must record dropped beats and clear them on each load, so " +
            "a fixed beat stops being reported.");
        var web = File.ReadAllText("MajdataEdit/WebControl.cs");
        Assert(
            web.Contains("LastDroppedBeats = result?.droppedBeats", StringComparison.Ordinal),
            "Edit must read the dropped-beat report off every response.");
        var core = File.ReadAllText("MajdataEdit/MainWindowCore.cs");
        Assert(
            core.Contains("foreach (var drop in WebControl.LastDroppedBeats)",
                StringComparison.Ordinal) &&
            core.Contains("ViewCouldNotBuildBeat", StringComparison.Ordinal),
            "Dropped beats must be merged into the same error marks syntax errors use.");
        foreach (var lang in new[] { "zh-CN", "en-US", "ja" })
            Assert(
                File.ReadAllText($"MajdataEdit/Langs/Langs.{lang}.resx")
                    .Contains("name=\"ViewCouldNotBuildBeat\"", StringComparison.Ordinal),
                $"Langs.{lang}.resx needs the unbuildable-beat message.");
    }

    // Whatever the editor accepts, the view has to be able to build. Nothing was
    // checking that, and the chart corpus cannot check it either: every one of the
    // 3089 connected slides on disk is written with a duration per segment, so the
    // total-duration form - the one a person actually types - was never once
    // exercised by any corpus run. This generates the forms the corpus lacks and
    // holds the two halves to the same answer.
    private static void CheckEverySlideFormTheEditorAcceptsCanBeBuilt()
    {
        var prefabKeys = ViewBuildDiff.LoadPrefabKeys();
        var shapes = new[]
        {
            "-", "^", "<", ">", "v", "p", "q", "pp", "qq", "s", "z", "rp", "rq"
        };
        var expressions = new List<string>();

        foreach (var shape in shapes)
        foreach (var middle in new[] { 3, 5 })
        foreach (var dZone in new[] { false, true })
        {
            var end = dZone ? "7d" : "7";
            var first = $"1{shape}{middle}";
            // The same slide written both ways: one total duration, and one per
            // segment. Only the second form exists in the corpus.
            expressions.Add($"{first}-{end}[8:1]");
            expressions.Add($"{first}[8:1]-{end}[8:1]");
            expressions.Add($"{first}-{end}-2[8:1]");
            expressions.Add($"{first}-{end}[8:1]-2[8:1]");
            foreach (var modifier in new[] { "b", "f", "!", "?", "m", "bm" })
                expressions.Add($"1{modifier}{shape}{middle}-{end}[8:1]");
        }
        foreach (var shape in new[] { "-", "^", "<", ">", "v", "pp", "qq" })
        {
            expressions.Add($"E1{shape}E5-E3[8:1]");
            expressions.Add($"B1{shape}B5-B3[8:1]");
            expressions.Add($"1{shape}5-A3[8:1]");
        }
        expressions.Add("1V35-7[8:1]");
        expressions.Add("1V35[8:1]-7[8:1]");
        expressions.Add("1V35-7d-3[8:1]");
        expressions.Add("6>3pp5d[16:2]");
        expressions.Add("7>2qq1d[16:2]");
        expressions.Add("6>3pp5d[16:2]/7>2qq1d[16:2]");

        var accepted = 0;
        var connected = 0;
        foreach (var expression in expressions)
        {
            var timing = new SimaiTimingPoint(0d, _content: expression, bpm: 120f);
            List<SimaiNote> notes;
            try { notes = timing.getNotes(); }
            catch { continue; }
            // Whatever the editor turns away is not this test's business; the bug
            // class is what it lets through and the view then cannot draw.
            if (!string.IsNullOrEmpty(timing.noteParseError))
                continue;
            accepted++;
            foreach (var note in notes)
            {
                if (note.noteType != SimaiNoteType.Slide)
                    continue;
                if (note.slidePath is { Count: > 1 })
                    connected++;
                string? reason;
                try
                {
                    reason = ViewBuildDiff.WhyViewCannotBuild(
                        note, timing.currentBpm, prefabKeys);
                }
                catch (Exception e)
                {
                    reason = "build threw: " + e.Message;
                }
                Assert(
                    reason == null,
                    $"The editor accepts '{expression}', so the view must build " +
                    $"it: {reason}");
            }
        }

        Assert(
            accepted > 400,
            $"This check is only worth its runtime if it covers real ground; it " +
            $"accepted {accepted} forms.");
        Assert(
            connected > 200,
            $"The whole point is the connected slides the corpus lacks; only " +
            $"{connected} were seen.");
    }

    // The view splits a connected slide into one note per segment and builds each
    // one, so whatever it does to a segment has to be legal for a segment. Handing
    // it back to the whole-note validator was not: under total-duration syntax
    // every segment but the last carries no duration, so "1-3-5[8:1]" was rejected
    // for a duration the note does have, mid-build, after the star head was in the
    // scene - no slide, and a stray head left drifting into a miss.
    private static void CheckConnectedSlideSegmentsSurviveTheLoader()
    {
        foreach (var expression in new[]
                 {
                     "1-3-5[8:1]", "6>3pp5d[16:2]", "7>2qq1d[16:2]",
                     "1V35-7[8:1]", "1-3-5-7[8:1]"
                 })
        {
            Assert(
                SlidePathParser.TryParsePath(expression, out var path) &&
                SlideSyntaxValidator.TryValidate(path, out _),
                $"'{expression}' is a legal connected slide.");
            var leading = path.segments[0].ToExpression(includeDZone: true);
            Assert(
                SlidePathParser.TryParsePath(leading, out var lone) &&
                !SlideSyntaxValidator.TryValidate(lone, out _),
                $"'{leading}' carries no duration of its own, so validating it as a " +
                "whole note rejects it - which is why the loader must not.");
        }

        var loader = File.ReadAllText("Assets/Scripts/JsonDataLoader.cs");
        Assert(
            loader.Contains(
                "if (isConnectedSegment && note.slidePath is { Count: > 0 })",
                StringComparison.Ordinal) &&
            loader.Contains(
                "ResolveSlidePath(note, info.IsGroupPart)[0]",
                StringComparison.Ordinal),
            "A segment of a connected slide must reuse the path its parent already " +
            "validated instead of being validated again on its own.");
        Assert(
            loader.Contains("RollBackBeatJudgeRegistrations();", StringComparison.Ordinal) &&
            loader.Contains("for (var i = notes.transform.childCount - 1; i >= beatObjectCount; i--)",
                StringComparison.Ordinal),
            "A beat that fails halfway must take back both the objects and the " +
            "judgement slots it already claimed, or it leaves a ghost note behind " +
            "and stalls its key.");
    }

    // The branch that calls a note a miss is also the branch that destroys it, and
    // a fake note skips that branch, so something else has to end its life. That
    // something used a window of its own, longer than a miss, which is how a fake
    // note ended up well past the ring before it went. One number, in one place.
    private static void CheckFakeNotesEndLikeAMiss()
    {
        var note = File.ReadAllText("Assets/Scripts/Notes/NoteDrop.cs");
        var lifetime = File.ReadAllText("Assets/Scripts/Notes/FakeNoteLifetime.cs");
        Assert(
            note.Contains("public const float MissWindow = 0.15f;", StringComparison.Ordinal),
            "The window a note that nobody hit stays for lives in one place.");
        Assert(
            lifetime.Contains("NoteDrop.MissWindow", StringComparison.Ordinal) &&
            !Regex.IsMatch(lifetime, @"0\.\d+f"),
            "A fake note's lifetime must be the miss window itself, not a number " +
            "of its own that can drift longer than one.");
        Assert(
            !note.Contains("if (isFake && GetJudgeTiming() >= 0f)", StringComparison.Ordinal),
            "Nothing may freeze a fake note at the ring: a real note that gets " +
            "missed keeps travelling for the miss window, and a fake one is " +
            "supposed to look like that.");
        foreach (var file in new[]
                 {
                     "Assets/Scripts/Notes/TapBase.cs",
                     "Assets/Scripts/Notes/HoldDrop.cs"
                 })
            Assert(
                Regex.IsMatch(
                    File.ReadAllText(file), @"timing > (MissWindow|0\.15f)") &&
                File.ReadAllText(file).Contains("MissWindow", StringComparison.Ordinal),
                $"{Path.GetFileName(file)} times out its miss on the shared window, " +
                "or the fake path and the miss path drift apart again.");
        Assert(
            !lifetime.Contains("NoteLongDrop longNote =>", StringComparison.Ordinal) &&
            note.Contains("internal float TailDuration", StringComparison.Ordinal),
            "How long a note's body lasts is one switch, not one per caller.");

        var slide = File.ReadAllText("Assets/Scripts/Notes/SlideDrop.cs");
        var wifi = File.ReadAllText("Assets/Scripts/Notes/WifiDrop.cs");
        var touchSlide = File.ReadAllText("Assets/Scripts/Notes/TouchSlideDrop.cs");
        Assert(
            slide.Contains("if (!isFake &&", StringComparison.Ordinal) &&
            wifi.Contains("if (!isFake &&", StringComparison.Ordinal),
            "Auto-play must not trace a fake slide, in either slide kind.");
        Assert(
            touchSlide.Contains("SetBarAlpha(1f, JudgmentDisabled ? 0f : trailProgress);",
                StringComparison.Ordinal) &&
            !touchSlide.Contains("SetBarAlpha(0f, 1f);", StringComparison.Ordinal),
            "A fake touch slide keeps its trail like a fake key slide does; the " +
            "two kinds must not end up looking different.");
    }

    // Dragging the timeline backwards is the one thing gameplay never does, so any
    // state a note latches on the way forward has to be undone here. Ring notes
    // read everything off the clock and need nothing; touch notes latch, and used
    // to keep their fans and their multi-touch slot after being scrubbed past.
    private static void CheckNotesRewindWithTheTimeline()
    {
        var note = File.ReadAllText("Assets/Scripts/Notes/NoteDrop.cs");
        Assert(
            note.Contains("protected bool ClockMovedBackwards()", StringComparison.Ordinal),
            "The backward-clock test belongs to every note, in one place.");
        foreach (var file in new[]
                 {
                     "Assets/Scripts/Notes/TouchDrop.cs",
                     "Assets/Scripts/Notes/TouchHoldDrop.cs"
                 })
        {
            var source = File.ReadAllText(file);
            Assert(
                source.Contains("if (ClockMovedBackwards())", StringComparison.Ordinal) &&
                source.Contains("RewindVisualState();", StringComparison.Ordinal),
                $"{Path.GetFileName(file)} must put itself back when the timeline " +
                "moves back before it.");
        }
        Assert(
            File.ReadAllText("Assets/Scripts/Notes/TouchDrop.cs")
                .Contains("multTouchHandler.cancelTouch(this);", StringComparison.Ordinal),
            "Rewinding must give up the multi-touch slot too, or the stacked-touch " +
            "overlay stays on screen.");
    }

    // A note that builds but cannot be drawn must say so. Every one of these
    // paths used to end in an invisible note and a clean-looking chart.
    private static void CheckUnrenderableNotesSpeakUp()
    {
        var note = File.ReadAllText("Assets/Scripts/Notes/NoteDrop.cs");
        Assert(
            note.Contains("protected void ReportUnrenderable(string reason)",
                StringComparison.Ordinal) &&
            note.Contains("if (reportedUnrenderable || previewOnly || renderReporter == null)",
                StringComparison.Ordinal),
            "NoteDrop needs a deduplicated way for a note to report it cannot be " +
            "drawn, skipped for previews.");

        var loader = File.ReadAllText("Assets/Scripts/JsonDataLoader.cs");
        Assert(
            loader.Contains("public void ReportUnrenderable(", StringComparison.Ordinal) &&
            loader.Contains("component.renderReporter = this;", StringComparison.Ordinal) &&
            loader.Contains("currentBeatLine = timing.rawTextPositionY;",
                StringComparison.Ordinal),
            "The loader must hand every note its source position and accept " +
            "unrenderable reports, or the editor cannot mark them.");

        var slide = File.ReadAllText("Assets/Scripts/Notes/SlideDrop.cs");
        Assert(
            slide.Contains("route came out NaN", StringComparison.Ordinal) &&
            slide.Contains("route collapsed to a single point", StringComparison.Ordinal) &&
            slide.Contains("route produced no slide bars", StringComparison.Ordinal),
            "SlideDrop must report a NaN route, a collapsed route and a route " +
            "that yields no bars.");
        Assert(
            !slide.Contains("if (totalLength <= 0.0001f)\n            return route;",
                StringComparison.Ordinal),
            "A degenerate tangent-circle route must fall back to the straight " +
            "line, not hand back an all-zero route that collapses onto the origin.");

        var touch = File.ReadAllText("Assets/Scripts/Notes/TouchSlideDrop.cs");
        var build = touch.IndexOf("BuildPath();", StringComparison.Ordinal);
        Assert(build >= 0, "TouchSlideDrop should still build its path in Start.");
        Assert(
            touch.IndexOf("nothing will be drawn", build, StringComparison.Ordinal) >= 0,
            "A touch slide whose path has fewer than two points is skipped by " +
            "Update forever, so it has to report right after BuildPath.");
    }

    // Hand play already refused to judge a fake slide, but auto-play traced it:
    // it wiped the trail bar by bar and retired the note, so the same chart
    // behaved two different ways depending on the play mode.
    private static void CheckAutoPlayDoesNotTraceFakeSlides()
    {
        foreach (var file in new[]
                 {
                     "Assets/Scripts/Notes/SlideDrop.cs",
                     "Assets/Scripts/Notes/WifiDrop.cs"
                 })
        {
            var source = File.ReadAllText(file);
            var name = Path.GetFileName(file);
            var searched = 0;
            while (true)
            {
                var at = source.IndexOf("HideBar(", searched, StringComparison.Ordinal);
                if (at < 0)
                    break;
                searched = at + 1;
                // Only the auto-play tracing sites matter; the judged sites are
                // already behind JudgmentDisabled.
                var windowStart = Math.Max(0, at - 420);
                var window = source.Substring(windowStart, at - windowStart);
                var autoAt = window.LastIndexOf("AutoPlayMode.Enable", StringComparison.Ordinal);
                if (autoAt < 0)
                    continue;
                var guard = window.Substring(Math.Max(0, autoAt - 120));
                Assert(
                    guard.Contains("!isFake", StringComparison.Ordinal) ||
                    window.Contains("if (JudgmentDisabled)", StringComparison.Ordinal),
                    $"{name}: auto-play must not trace a fake slide. Every " +
                    "auto-play HideBar needs an isFake guard, or a JudgmentDisabled " +
                    "return ahead of it.");
            }
        }
    }

    // The build yields after every slice of work, so wall-clock time is the build's
    // own cost multiplied by how many frames it is spread over. A paused preview has
    // no audio to protect and someone waiting on it, so it gets a wider slice.
    private static void CheckPreviewBuildsWithoutThrottling()
    {
        var source = File.ReadAllText("Assets/Scripts/JsonDataLoader.cs");
        Assert(
            source.Contains("sw.ElapsedMilliseconds >= (previewOnly ? 12 : 2)",
                StringComparison.Ordinal),
            "A preview load must take a wider time slice than a playback load, " +
            "otherwise the wait before a paused preview follows the drag is the " +
            "build's cost stretched across eight times as many frames.");
    }

    // A hold's end cap lives on child 1 and ships enabled, so between Instantiate
    // and Start it draws at the object's untouched transform - the origin. Starting
    // mid-chart builds holds that are already due, which is when it showed.
    // Mirrors the chromatic branch of Assets/NoteColorTint.shader closely enough to
    // tell whether two requested colours can still be told apart on screen.
    /// <summary>
    /// How much of the texture's own contrast survives a request for a dark colour,
    /// mirroring _DarkDetail in the shader.
    /// </summary>
    private const double DarkDetail = 0.35;

    private static (double R, double G, double B) TintBody(
        (double R, double G, double B) texel,
        (double R, double G, double B) target)
    {
        static (double H, double S, double V) ToHsv((double R, double G, double B) c)
        {
            var max = Math.Max(c.R, Math.Max(c.G, c.B));
            var min = Math.Min(c.R, Math.Min(c.G, c.B));
            var d = max - min;
            var h = 0d;
            if (d > 1e-9)
            {
                if (max == c.R) h = ((c.G - c.B) / d + 6) % 6;
                else if (max == c.G) h = (c.B - c.R) / d + 2;
                else h = (c.R - c.G) / d + 4;
                h /= 6;
            }
            return (h, max <= 1e-9 ? 0 : d / max, max);
        }

        static (double R, double G, double B) ToRgb((double H, double S, double V) c)
        {
            var i = (int)Math.Floor(c.H * 6) % 6;
            var f = c.H * 6 - Math.Floor(c.H * 6);
            var p = c.V * (1 - c.S);
            var q = c.V * (1 - f * c.S);
            var t = c.V * (1 - (1 - f) * c.S);
            return i switch
            {
                0 => (c.V, t, p),
                1 => (q, c.V, p),
                2 => (p, c.V, t),
                3 => (p, q, c.V),
                4 => (t, p, c.V),
                _ => (c.V, p, q),
            };
        }

        static double SmoothStep(double a, double b, double x)
        {
            var t = Math.Clamp((x - a) / (b - a), 0, 1);
            return t * t * (3 - 2 * t);
        }

        var orig = ToHsv(texel);
        var tgt = ToHsv(target);
        var satScale = tgt.S;
        var valueScale = tgt.V;
        var detailGain = DarkDetail * (1 - tgt.V);
        var shaped = Math.Clamp(
            orig.V * valueScale + detailGain * (orig.V - 0.5), 0, 1);
        var tinted = ToRgb((tgt.H, orig.S * satScale, shaped));
        var weight = SmoothStep(0.04, 0.16, orig.V) * SmoothStep(0.05, 0.25, orig.S);
        return (texel.R + (tinted.R - texel.R) * weight,
                texel.G + (tinted.G - texel.G) * weight,
                texel.B + (tinted.B - texel.B) * weight);
    }

    // rp and rq are the mirrored twins of pp and qq. A touch slide used to accept
    // one pair and reject the other, and because the check is per segment a mixed
    // path could smuggle an rp segment into a touch slide that had no geometry for
    // it and drew some other shape without saying so.
    private static void CheckTouchSlideAcceptsMirroredArcs()
    {
        foreach (var shape in new[] { "pp", "qq", "rp", "rq" })
        {
            Assert(
                ParseError($"E1{shape}5d[8:1]") == null,
                $"A touch slide should accept '{shape}' the same as its twin.");
            Assert(
                ParseError($"1{shape}5d[8:1]-E7[8:1]") == null,
                $"A mixed path carrying '{shape}' should parse.");
        }

        // What stays excluded has no touch geometry at all.
        foreach (var shape in new[] { "w", "s", "z" })
            Assert(
                ParseError($"E1{shape}5[8:1]") != null,
                $"A touch slide must still refuse '{shape}'.");

        var touchSlide = File.ReadAllText("Assets/Scripts/Notes/TouchSlideDrop.cs");
        Assert(
            touchSlide.Contains(
                "if (segmentShape is \"pp\" or \"qq\" or \"rp\" or \"rq\")",
                StringComparison.Ordinal),
            "TouchSlideDrop must inherit the authored route for all four arcs, or a " +
            "touch slide carrying rp is drawn as a different shape in silence.");
        Assert(
            touchSlide.Contains("segmentShape[^1] == 'p'", StringComparison.Ordinal),
            "The arc direction must come from the last character, which is the one " +
            "all four shapes share.");
    }

    private static void CheckSelectableOrbitSlides()
    {
        foreach (var source in new[]
                 {
                     "1P34[8:1]",
                     "1P35[8:1]",
                     "1Q84[8:1]",
                     "1Q05[8:1]",
                     "1Q95[8:1]",
                     "1PE85[8:1]",
                     "1P3E5Q0A5[8:1]"
                 })
        {
            Assert(
                SlidePathParser.TryParsePath(source, out var path) &&
                SlideSyntaxValidator.TryValidate(path, out _) &&
                path.isTouchPath,
                $"Selectable orbit slide should parse as a touch path: {source}");
            Assert(
                path.source == source &&
                string.Concat(path.segments.Select((segment, index) =>
                    index == 0
                        ? segment.ToExpression(includeDZone: true)
                        : segment.ToExpression(includeDZone: true)
                            .Substring(segment.start.ToExpression().Length))) == source,
                $"Selectable orbit slide should round-trip: {source}");
        }

        Assert(
            SlidePathParser.TryParsePath("1P35[8:1]", out var aroundB) &&
            aroundB.segments[0].middle.area == 'B' &&
            aroundB.segments[0].middle.position == 3,
            "P3 must select B3 as its orbit centre.");
        Assert(
            SlidePathParser.TryParsePath("1Q05[8:1]", out var aroundC) &&
            aroundC.segments[0].middle.area == 'C',
            "Q0 must select the central orbit.");
        Assert(
            SlidePathParser.TryParsePath("1Q95[8:1]", out var aroundOuter) &&
            aroundOuter.segments[0].middle.area == 'O' &&
            aroundOuter.segments[0].middle.position == 9,
            "Q9 must select the outer orbit.");
        Assert(
            SlidePathParser.TryParsePath("1PE85[8:1]", out var aroundE) &&
            aroundE.segments[0].middle.area == 'E' &&
            aroundE.segments[0].middle.position == 8,
            "An explicit Touch position must be usable as an orbit centre.");
        Assert(
            NotePreviewModule.ExpandPreview("1P3E5Q0A5[8:1]").Count == 1,
            "Selectable orbit slides must use the normal live-preview parser.");
        Assert(
            NoteDurationTarget.TryFromTypedText("1P35", out var slideTarget) &&
            slideTarget,
            "Selectable orbit slides must offer the normal Slide duration completion.");
        Assert(
            ParseError("1P35") != null,
            "Selectable orbit slides must reject missing durations.");

        const string slideCode = "5Q9A1P98CQ49K5[8:1]";
        Assert(
            SlidePathParser.TryParsePath(slideCode, out var parsedCode) &&
            SlideSyntaxValidator.TryValidate(parsedCode, out _) &&
            parsedCode.isTouchPath &&
            parsedCode.segments.Count == 1 &&
            parsedCode.segments[0].shape == "SC" &&
            parsedCode.segments[0].slideCode == "5Q9A1P98CQ49K5",
            "A complete SlideCode must use the shared custom-path AST.");
        Assert(
            SlideCodeParser.TryParse(
                "5Q9A1P98CQ49K5", out var expanded, out _) &&
            expanded.instructions.Count == 9 &&
            expanded.instructions[4].command == SlideCodeCommand.P &&
            expanded.instructions[4].parameter == 8 &&
            expanded.instructions[^1].command == SlideCodeCommand.Key,
            "SlideCode must expand repeated parameters against their latest command.");
        Assert(
            SlideCodeParser.TryParse(
                "1A3571P0K1", out var nodeCode, out _) &&
                nodeCode.instructions.Count == 7,
            "SlideCode must expand consecutive A-node parameters.");
        Assert(
            SlideCodeParser.TryParse(
                "1Q8K4", out var legacyQCode, out _) &&
            legacyQCode.instructions.Count == 3 &&
            legacyQCode.instructions[1].command == SlideCodeCommand.Q &&
            legacyQCode.instructions[1].parameter == 8 &&
            legacyQCode.instructions[2].command == SlideCodeCommand.Key &&
            legacyQCode.instructions[2].parameter == 4,
            "1Q8K4 must describe the same Q8 orbit and key-4 exit as 1Q84/1qq4.");
        Assert(
            SlidePathParser.TryParsePath("1Q85[8:1]", out var shortQ) &&
            shortQ.segments.Count == 1 &&
            shortQ.segments[0].middle.source == "8" &&
            shortQ.segments[0].endPosition == 5 &&
            SlideCodeParser.TryParse("1Q8K5", out var codeQ, out _) &&
            codeQ.instructions[1].command == SlideCodeCommand.Q &&
            codeQ.instructions[1].parameter == 8 &&
            codeQ.instructions[^1].command == SlideCodeCommand.Key &&
            codeQ.instructions[^1].parameter == 5,
            "Ordinary 1Q85 and SlideCode 1Q8K5 must independently select the same " +
            "Q8 orbit and key-5 exit as 1qq5.");
        Assert(
            NotePreviewModule.ExpandPreview(slideCode).Count == 1 &&
            NoteDurationTarget.TryFromTypedText(
                "5Q9A1P98CQ49K5", out var codeDuration) && codeDuration,
            "SlideCode must use the normal preview and duration-completion paths.");
        Assert(
            !SlideCodeParser.TryParse("1P3Q4K5", out _, out _) &&
            !SlideCodeParser.TryParse("1P09K5", out _, out _) &&
            !SlideCodeParser.TryParse("1CP0K1", out _, out _) &&
            !SlideCodeParser.TryParse("1B2P3K1", out _, out _) &&
            !SlideCodeParser.TryParse("1A35", out _, out _) &&
            ParseError("1-5B6A4[8:1]") != null,
            "SlideCode must reject direction changes, 0/9 transfers, inside nodes, " +
            "missing K, and SlideCode commands embedded in legacy paths.");

        var renderer = File.ReadAllText("Assets/Scripts/Notes/TouchSlideDrop.cs");
        var geometry = File.ReadAllText(
            "Assets/Scripts/Notes/SlideCodePathGeometry.cs");
        var codeParser = File.ReadAllText("Assets/MajdataCore/SlideCodeParser.cs");
        Assert(
            renderer.Contains("AppendSelectableOrbitSegment(", StringComparison.Ordinal) &&
            renderer.Contains("orbitIsNumber", StringComparison.Ordinal) &&
            !renderer.Contains("TryAppendSingleOrbitSlideCode(", StringComparison.Ordinal) &&
            renderer.Contains("TryAppendLegacyNumericOrbit(", StringComparison.Ordinal) &&
            renderer.Contains("SlideCodePathGeometry.AppendSingleOrbit(", StringComparison.Ordinal) &&
            !renderer.Contains("AppendExactTemplate(", StringComparison.Ordinal) &&
            !renderer.Contains("legacyOrbitPosition", StringComparison.Ordinal) &&
            renderer.Contains("AppendOriginalTangentRoute(", StringComparison.Ordinal) &&
            renderer.Contains("BuildAdaptiveTangentCircleRoute(", StringComparison.Ordinal) &&
            renderer.Contains(
                "var sourceEnd = (sourceStart + 3) % 8 + 1;",
                StringComparison.Ordinal) &&
            !renderer.Contains("'B' => 1.15f", StringComparison.Ordinal) &&
            geometry.Contains("(135f - index * 45f)", StringComparison.Ordinal) &&
            !geometry.Contains("SideOrbitJoinTolerance", StringComparison.Ordinal) &&
            codeParser.Contains("(135d - index * 45d)", StringComparison.Ordinal),
            "Ordinary numeric P/Q must retain the legacy pp/qq route, while SlideCode " +
            "uses its independent shortest tangent route; explicit Touch centres retain " +
            "fitted tangent-circle geometry.");

        var soundEffects = File.ReadAllText("MajdataEdit/SoundEffect.cs");
        var touchHoldCase = soundEffects.Substring(
            soundEffects.IndexOf("case SimaiNoteType.TouchHold:", StringComparison.Ordinal),
            soundEffects.IndexOf("private void renderSoundEffect", StringComparison.Ordinal) -
            soundEffects.IndexOf("case SimaiNoteType.TouchHold:", StringComparison.Ordinal));
        Assert(
            !touchHoldCase.Contains(
                "release.AddSound(SoundDataType.Break", StringComparison.Ordinal) &&
            !touchHoldCase.Contains(
                "tHoldRelease.AddSound(SoundDataType.Break", StringComparison.Ordinal),
            "Break TouchHold may cheer at its head, but its release must match Break Hold " +
            "and must not play another Break cheer.");
    }

    // The editor's timeline and the view have to agree on which beats are each, or
    // a beat is drawn one colour while writing it and another while playing it.
    private static void CheckEachCountAgreesBetweenEditorAndView()
    {
        // The rule is shared code, so it can be exercised directly rather than by
        // matching source text on both sides.
        Assert(
            !EachRule.CountsTowardEach(isHeadlessSlide: true) &&
            EachRule.CountsTowardEach(isHeadlessSlide: false),
            "Only a note with a head can pair into an each.");
        Assert(
            EachRule.IsEach(beatMarkedEach: false, notesCountingTowardEach: 2) &&
            !EachRule.IsEach(beatMarkedEach: false, notesCountingTowardEach: 1) &&
            EachRule.IsEach(beatMarkedEach: true, notesCountingTowardEach: 1),
            "A beat is each on two hits, or when the stream marked it.");
        Assert(
            EachRule.TrailsAreEach(2) && !EachRule.TrailsAreEach(1) &&
            !EachRule.TrailsAreEach(0),
            "Trails are each when two of them travel together.");

        var view = File.ReadAllText("Assets/Scripts/JsonDataLoader.cs");
        var editor = File.ReadAllText("MajdataEdit/MainWindowCore.cs");
        Assert(
            !view.Contains("timing.noteList.Count > 1", StringComparison.Ordinal),
            "No site in the view may count raw notes for each-ness any more.");
        Assert(
            view.Split("BeatIsEach(timing)").Length - 1 >= 6,
            "Every each decision in the view must come from the one helper, or they " +
            "drift apart again.");
        foreach (var (side, source) in new[] { ("view", view), ("editor", editor) })
            Assert(
                source.Contains("EachRule.IsEach(", StringComparison.Ordinal) &&
                source.Contains("EachRule.CountsTowardEach(", StringComparison.Ordinal),
                $"The {side} must take its each rule from the shared one, not its own " +
                "copy, which is how these two disagreed for a year.");

        // The beat that exposed the split: a touch plus a headless slide is one hit,
        // not two, so it is not an each on either side.
        var timing = new SimaiTimingPoint(0d, _content: "A8/8?-C-5[8:1]", bpm: 120f);
        var notes = timing.getNotes();
        Assert(notes.Count == 2, "'A8/8?-C-5[8:1]' should still parse as two notes.");
        Assert(
            notes.Count(n => !n.isSlideNoHead) == 1,
            "Only the touch in 'A8/8?-C-5[8:1]' has a head, so the beat is not each.");
        Assert(
            new SimaiTimingPoint(0d, _content: "A8/8-C-5[8:1]", bpm: 120f)
                .getNotes().Count(n => !n.isSlideNoHead) == 2,
            "The same beat with a headed slide is two hits and stays each.");

        SimaiProcess.ClearData();
        SimaiProcess.Serialize("(120){4}1-5[8:1]*-3[8:1],E");
        var serializedSameHead = SimaiProcess.notelist.FirstOrDefault(point =>
            point.getNotes().Count(note => note.noteType == SimaiNoteType.Slide) == 2);
        Assert(
            serializedSameHead != null &&
            serializedSameHead.isEach == false &&
            serializedSameHead.isEachInStream == false,
            "A serialized same-head pair must use the plain double-star head, not " +
            "the yellow each-double head. Parsed: " +
            string.Join(" | ", SimaiProcess.notelist.Select(point =>
                $"{point.notesContent}:{point.getNotes().Count}:{point.currentBpm}:" +
                $"{point.noteParseError}")));

        SimaiProcess.ClearData();
        SimaiProcess.Serialize("(120){4}1-5[8:1]*-3[8:1]/2,E");
        var serializedSameHeadEach = SimaiProcess.notelist.FirstOrDefault(point =>
            point.getNotes().Count(note => note.noteType == SimaiNoteType.Slide) == 2);
        Assert(
            serializedSameHeadEach != null &&
            serializedSameHeadEach.isEach == true &&
            serializedSameHeadEach.isEachInStream == true,
            "A same-head pair struck beside another note must use the each-double head.");

        // A same-head pair splits the two questions: one struck head, two trails. The
        // view used to answer both with the head's count, so correcting the head count
        // turned these trails plain.
        foreach (var pair in new[]
                 {
                     "C-B7<B3[8:1]*-B3>B7[8:1]", "1-5[8:1]*-3[8:1]",
                     "A1-E2[8:1]*-B3[8:1]",
                 })
        {
            var beat = new SimaiTimingPoint(0d, _content: pair, bpm: 120f);
            var branches = beat.getNotes();
            Assert(
                beat.noteParseError == null && branches.Count == 2,
                $"'{pair}' should parse as a same-head pair.");
            Assert(
                !EachRule.IsEach(
                    beat.isEach,
                    branches.Count(
                        note => EachRule.CountsTowardEach(note.isSlideNoHead))),
                $"'{pair}' strikes one head, so the head is drawn plain.");
            Assert(
                EachRule.TrailsAreEach(
                    branches.Count(note => note.noteType == SimaiNoteType.Slide)),
                $"'{pair}' moves two trails at once, so both are drawn each.");
        }

        foreach (var (side, source) in new[] { ("view", view), ("editor", editor) })
            Assert(
                source.Contains("EachRule.TrailsAreEach(", StringComparison.Ordinal),
                $"The {side} must take the trail rule from the shared one too.");
        Assert(
            view.Split("BeatTrailsAreEach(timing)").Length - 1 >= 3,
            "Every trail in the view - slide, wifi and touch slide - must ask the " +
            "trail rule, not the head's.");
        Assert(
            !view.Contains(": isEach ? customSkin", StringComparison.Ordinal),
            "A trail's own sprite may not be picked by the head's each-ness.");
    }

    private static void CheckTouchSegmentsMustCoverGround()
    {
        // A touch trail is sampled from its two ends rather than taken from a prefab,
        // so a segment whose ends are one point samples that point over and over: the
        // chart was accepted and the trail was zero length, drawn nowhere and reported
        // nowhere. Key segments have always refused this; these are the same refusals
        // for the shapes only a touch slide can reach.
        foreach (var rejected in new[]
                 {
                     "B3-B3[8:1]", "E3-E3[8:1]", "A3-A3[8:1]", "B3^B3[8:1]",
                     "C-C[8:1]", "C^C[8:1]", "C1-C2[8:1]", "C2-C1[8:1]",
                     // Away from the centre these shapes circle around something. At
                     // the centre there is nothing to circle.
                     "C<C[8:1]", "C>C[8:1]", "CvC[8:1]", "CpC[8:1]", "CqC[8:1]",
                     "C<<C[8:1]",
                     // A joint inside a connected path is checked the same way.
                     "1-C-C[8:1]", "B3-B7-B7[8:1]",
                 })
        {
            RejectRuntime(rejected);
            CheckSyntax(rejected, false);
        }

        // A full circle is written by ending where it began, which is exactly what the
        // rule above must not swallow. Every area but the centre has a radius to
        // circle at.
        foreach (var accepted in new[]
                 {
                     "B3<B3[8:1]", "B3>B3[8:1]", "E3<E3[8:1]", "A3<A3[8:1]",
                     "D3<D3[8:1]", "B3<<B3[8:1]", "B3<<<B3[8:1]",
                     // These leave and come back by a route, so they cover ground.
                     "B3vB3[8:1]", "B3pB3[8:1]", "B3qB3[8:1]", "B3VB7B3[8:1]",
                     "CVB3C[8:1]",
                     // And the key ring keeps its own circles, drawn from prefabs.
                     "1<1[8:1]", "1>1[8:1]",
                 })
        {
            var note = ParseRuntime(accepted);
            Assert(
                note.noteType == SimaiNoteType.Slide,
                $"'{accepted}' should still be read as a slide.");
            CheckSyntax(accepted, true);
        }
    }

    private static void CheckSameHeadBranchesKeepTheirHead()
    {
        // Every branch after the first is rebuilt by writing the head back in front of
        // it. 0.4.2 wrote only the head's position number, so a touch head came back as
        // the key of the same number and the second trail started from the outer ring
        // instead of where it was authored: "C-B7<B3[8:1]*-B3>B7[8:1]" drew its second
        // trail from key 8 rather than the centre, "E1-..." from key 1, and a D-zone
        // head lost its zone.
        foreach (var (text, area, position, isDZone) in new[]
                 {
                     ("C-B7<B3[8:1]*-B3>B7[8:1]", 'C', 8, false),
                     ("E1-E5[8:1]*-E3[8:1]", 'E', 1, false),
                     ("A2-B4[8:1]*-B6[8:1]", 'A', 2, false),
                     ("B7-B3[8:1]*-A1[8:1]", 'B', 7, false),
                     ("1d-5[8:1]*-3[8:1]", 'K', 1, true),
                     ("1-5[8:1]*-3[8:1]", 'K', 1, false),
                 })
        {
            Assert(
                SlidePathParser.TryExpandSameHead(text, out var branches) &&
                branches.Count == 2,
                $"'{text}' should expand into two branches.");
            foreach (var branch in branches)
            {
                Assert(
                    SlidePathParser.TryParsePath(branch, out var path),
                    $"Branch '{branch}' of '{text}' should parse on its own.");
                Assert(
                    path.head.area == area &&
                    path.head.position == position &&
                    path.head.isDZone == isDZone,
                    $"Branch '{branch}' of '{text}' must start at the head that was " +
                    $"written, {area}{position}, not at another area of the same number.");
                Assert(
                    path.segments.Count > 0 &&
                    path.segments[0].start.area == area &&
                    path.segments[0].start.position == position &&
                    path.segments[0].start.isDZone == isDZone,
                    $"Branch '{branch}' of '{text}' must draw from that head too.");
            }

            var notes = new SimaiTimingPoint(0d, _content: text, bpm: 120f).getNotes();
            Assert(notes.Count == 2, $"'{text}' should reach the view as two notes.");
            Assert(
                notes.All(note =>
                    note.startPosition == notes[0].startPosition &&
                    note.touchArea == notes[0].touchArea &&
                    note.isDZone == notes[0].isDZone),
                $"Both branches of '{text}' share one head, so they must agree on " +
                "where it is.");
            Assert(
                notes[0].startPosition == position &&
                notes[0].isDZone == isDZone &&
                (area == 'K' || notes[0].touchArea == area),
                $"'{text}' must keep the head it was written with.");
        }
    }

    private static void CheckPerNoteSkin()
    {
        // A skin rides the same "~[...]" suffix as a radius, so the first thing to
        // pin down is that neither reading ever claims the other's input.
        Assert(
            ParseRuntime("1~[star.png]") is { noteSkin: "star.png", touchRadius: 0f },
            "A key note must accept an image skin.");
        Assert(
            ParseRuntime("E1~[star.png]") is { noteSkin: "star.png", touchRadius: 0f },
            "A touch note must accept an image skin.");
        // A subfolder has to be written with a backslash: '/' already separates the
        // notes sharing a beat, so a forward slash never survives to reach this far.
        Assert(
            ParseRuntime(@"E1~[skins\star.png]") is { noteSkin: "skins/star.png" },
            "A skin may name a subfolder of the chart's own folder.");
        Assert(
            ParseRuntime("E1~[4.8]") is { touchRadius: 4.8f, noteSkin: "" },
            "A bare number must still be read as a radius, not a skin.");
        Assert(
            ParseRuntime("E1") is { noteSkin: "" },
            "A note with no skin written must carry none.");

        foreach (var accepted in new[]
                 {
                     // The suffix sits right after the position, the same place a
                     // radius has always had to go, with modifiers following it.
                     "E1~[star.png]", @"E2~[a\star.PNG]", "A3~[s.jpg]",
                     "B1~[t.jpeg]", "D4~[star.png]",
                 })
            Assert(
                ParseError(accepted) == null,
                $"'{accepted}' should be accepted as a skinned note.");

        // Absolute paths and anything climbing out of the chart's folder have to be
        // refused: the file name comes from the chart, which can come from anywhere.
        foreach (var rejected in new[]
                 {
                     @"E1~[\etc\passwd.png]", @"E1~[..\..\secret.png]",
                     @"E1~[C:\win.png]", "E1~[.png]", "E1~[star.exe]",
                     @"E1~[a\\b.png]", "E1~[]",
                 })
            Assert(
                ParseError(rejected) != null,
                $"'{rejected}' must be rejected, not loaded.");

        // The message has to name the real problem. Reporting a bad file name as a
        // malformed radius is what sends a charter looking in the wrong place.
        var skinError = ParseError("1~[star.exe]") ?? string.Empty;
        Assert(
            skinError.Contains("png", StringComparison.OrdinalIgnoreCase) ||
            skinError.Contains("图片", StringComparison.Ordinal),
            $"A bad skin must be reported as a skin problem, got '{skinError}'.");

        // The EX halo and the each/break guide lines are drawn on their own renderers.
        // Pointing them at the skin draws the image a second time, oversized.
        var tapBase = File.ReadAllText("Assets/Scripts/Notes/TapBase.cs");
        var applyAt = tapBase.IndexOf(
            "protected void ApplyCustomSkinToSprites()", StringComparison.Ordinal);
        Assert(applyAt >= 0, "TapBase should still apply per-note skins.");
        var apply = tapBase.Substring(applyAt, 600);
        Assert(
            apply.Contains("eachSpr = skin;", StringComparison.Ordinal) &&
            apply.Contains("breakSpr = skin;", StringComparison.Ordinal),
            "An each or break note must wear the skin too.");
        Assert(
            !apply.Contains("exSpr = skin;", StringComparison.Ordinal) &&
            !apply.Contains("eachLine = skin;", StringComparison.Ordinal) &&
            !apply.Contains("breakLine = skin;", StringComparison.Ordinal),
            "The EX halo and the guide lines must keep their own sprites.");

        Assert(
            SlidePathParser.IsSkinPathUsable("skins/star.png", out var normalized) &&
            normalized == "skins/star.png",
            "A usable skin path must come back normalized.");
        Assert(
            SlidePathParser.IsSkinPathUsable("skins\\star.png", out var windows) &&
            windows == "skins/star.png",
            "Backslashes must be normalized so charts move between systems.");
    }

    private static void CheckNoteTintReachesMoreThanHues()
    {
        var shader = File.ReadAllText("Assets/NoteColorTint.shader");
        Assert(
            shader.Contains("float satScale = tgtHSV.y;", StringComparison.Ordinal) &&
            shader.Contains("float valueScale = tgtHSV.z;", StringComparison.Ordinal),
            "The tint must scale the texture's own saturation and value by the target's. " +
            "Lerping toward the target flattens the variation the texture carries.");
        Assert(
            shader.Contains("origHSV.y * satScale", StringComparison.Ordinal) &&
            shader.Contains("tintedValue * valueScale", StringComparison.Ordinal),
            "Both scales must multiply the texture's channels, not replace them.");
        Assert(
            shader.Contains("float detailGain = _DarkDetail * (1.0 - tgtHSV.z);",
                StringComparison.Ordinal) &&
            shader.Contains("detailGain * (tintedValue - 0.5)", StringComparison.Ordinal) &&
            shader.Contains("detailGain * (sourceLuma.xxx - 0.5)", StringComparison.Ordinal),
            "The darker the request, the more of the texture's contrast has to be " +
            "added back, on the coloured path and the grey one alike.");
        Assert(
            !shader.Contains("max(tgtHSV.z,", StringComparison.Ordinal) &&
            !shader.Contains("max(valueScale,", StringComparison.Ordinal),
            "Detail is added back rather than floored: a floor makes every request " +
            "past it land on one shade.");

        // Neither scale may exceed one: a fully saturated, fully bright target has to
        // land exactly where it did before this change, or every existing chart shifts.
        var reference = TintBody((0.90, 0.10, 0.10), (1.0, 0.0, 0.0));
        Assert(
            Math.Abs(reference.R - 0.90) < 0.02 &&
            Math.Abs(reference.G - 0.10) < 0.02,
            "A saturated bright target must not change how notes were already tinted.");

        // These four all used to collapse onto the same red, which is what limited the
        // palette to one ring of hues.
        var variants = new (string Name, (double, double, double) Target)[]
        {
            ("bright", (1.0, 0.0, 0.0)),
            ("deep", (0.69, 0.0, 0.0)),
            ("dark", (0.50, 0.0, 0.0)),
            ("pale", (1.0, 0.80, 0.80)),
        };
        var seen = new List<(string Name, (double R, double G, double B) Out)>();
        foreach (var (name, target) in variants)
        {
            var body = TintBody((0.90, 0.10, 0.10), target);
            foreach (var (other, prior) in seen)
            {
                var apart = Math.Abs(body.R - prior.R) +
                            Math.Abs(body.G - prior.G) +
                            Math.Abs(body.B - prior.B);
                Assert(
                    apart > 0.05,
                    $"Requested colours '{name}' and '{other}' still render alike " +
                    $"({apart:0.###} apart), so the reachable palette is no wider.");
            }
            seen.Add((name, body));
        }

        // An achromatic request has to drive the grey axis. Scaling the colour, which
        // is what this did before, could only make a red Note a brighter red however
        // white you asked for.
        Assert(
            shader.Contains("sourceLuma.xxx * (tgtHSV.z * 2.0)", StringComparison.Ordinal),
            "A grey or white target must desaturate the texture, not brighten its hue.");
        Assert(
            !shader.Contains("rgb * (tgtHSV.z * 2.0)", StringComparison.Ordinal),
            "The old brightness-only path must be gone, or FFFFFF is not white.");

        static double Achromatic((double R, double G, double B) texel, double targetValue)
        {
            var luma = 0.299 * texel.R + 0.587 * texel.G + 0.114 * texel.B;
            return Math.Clamp(
                luma * targetValue * 2.0 +
                DarkDetail * (1 - targetValue) * (luma - 0.5),
                0, 1);
        }
        Assert(
            Achromatic((1.0, 0.55, 0.55), 1.0) > 0.99 &&
            Achromatic((0.90, 0.10, 0.10), 1.0) is > 0.55 and < 0.85,
            "FFFFFF must read as a white note, highlights at white.");
        Assert(
            Achromatic((1.0, 0.55, 0.55), 0.5) - Achromatic((0.45, 0.05, 0.05), 0.5) > 0.10,
            "A greyscale note must keep its shading.");
        Assert(
            Achromatic((0.90, 0.10, 0.10), 0.0) < 0.12,
            "000000 must read as a black note.");
        Assert(
            Achromatic((1.0, 0.55, 0.55), 0.0) -
            Achromatic((0.45, 0.05, 0.05), 0.0) > 0.03,
            "000000 must still be a note and not a silhouette: multiplying the " +
            "texture by nothing is what wiped it out.");

        // No dead zones. A floor on either scale reads as safety but it makes every
        // request past it land on the same shade, which is the collapse this whole
        // change exists to undo, so each step of a ramp has to come out different.
        static string Body((double R, double G, double B) target)
        {
            var body = TintBody((0.90, 0.10, 0.10), target);
            return $"{body.R:0.###},{body.G:0.###},{body.B:0.###}";
        }
        foreach (var (axis, ramp) in new (string, List<(double, double, double)>)[]
                 {
                     ("darker", Enumerable.Range(1, 10)
                         .Select(i => (i * 0.05, 0d, 0d)).ToList()),
                     ("paler", Enumerable.Range(1, 10)
                         .Select(i => (1d, 1d - i * 0.05, 1d - i * 0.05)).ToList()),
                 })
        {
            var results = ramp.Select(Body).ToList();
            Assert(
                results.Distinct().Count() == results.Count,
                $"Ten steps along the {axis} axis produced only " +
                $"{results.Distinct().Count()} different colours, so part of that " +
                "axis is a dead zone.");
        }
        foreach (var (name, target) in variants)
        {
            var highlight = TintBody((1.0, 0.55, 0.55), target);
            var shadow = TintBody((0.45, 0.05, 0.05), target);
            static double Luma((double R, double G, double B) c) =>
                0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
            Assert(
                Luma(highlight) - Luma(shadow) > 0.10,
                $"Target '{name}' flattens the texture: highlight and shadow are " +
                $"{Luma(highlight) - Luma(shadow):0.###} apart in brightness.");
        }
    }

    // COLORV/SIZEV/ALPHAV used to be pushed onto a snapshot of the notes taken
    // once, so a note that was not in it, or that built a renderer after the push,
    // kept its old look and nothing came back for it. Scrubbing re-pushed and hid
    // that; playing straight through did not, which is how a live colour could
    // show in the editor's preview and not in playback, and how a slide's arc
    // could stay its old colour while its guide star changed.
    private static void CheckTouchLeavesGrowAwayFromTheCentre()
    {
        // All four leaves are children of the one note transform. SIZE and SIZEV
        // scale that parent, so every shared edge remains shared only if the
        // authored local offsets stay unchanged. A second, per-leaf correction
        // based on sprite bounds breaks the common transform and opens a gap; skin
        // transparency makes that measurement even less meaningful.
        foreach (var path in new[]
                 {
                     "Assets/Scripts/Notes/TouchDrop.cs",
                     "Assets/Scripts/Notes/TouchHoldDrop.cs"
                 })
        {
            var source = File.ReadAllText(path);
            Assert(
                source.Contains(
                    "(0.226f + distance) * GetAngle(index)",
                    StringComparison.Ordinal),
                $"{path} must keep the authored local leaf offset so parent scaling " +
                "moves leaf positions and geometry together.");
            Assert(
                !source.Contains("MeasureLeafExtent", StringComparison.Ordinal) &&
                !source.Contains("GetTouchFanOffset", StringComparison.Ordinal),
                $"{path} must not move each leaf independently from sprite bounds.");
        }
        var hold = File.ReadAllText("Assets/Scripts/Notes/TouchHoldDrop.cs");
        Assert(
            hold.Contains("KeepCenterPointSize();", StringComparison.Ordinal) &&
            hold.Contains("authoredCenterPointScale.x / scaleX",
                StringComparison.Ordinal),
            "TouchHold leaves scale around the common centre, but the centre cross " +
            "must cancel that parent scale instead of opening a larger cross gap.");
    }

    private static void CheckTouchSlideTrailMeetsItsGuideStar()
    {
        const float spacing = 0.4f;
        var shortRouteLead = AlphaVisualTiming.GetTouchSlideTrailLead(10f, spacing);
        var longArcLead = AlphaVisualTiming.GetTouchSlideTrailLead(40f, spacing);

        Assert(
            Math.Abs(shortRouteLead * 10f - spacing * 0.5f) < 1e-6f &&
            Math.Abs(longArcLead * 40f - spacing * 0.5f) < 1e-6f,
            "The TouchSlide trail must lead by half a bar in world distance on " +
            "both short routes and long arcs.");
        Assert(
            longArcLead < shortRouteLead,
            "A long arc needs a smaller normalized lead; a fixed normalized lead " +
            "makes its disappearing edge run ahead of the guide star.");
        Assert(
            AlphaVisualTiming.GetTouchSlideTrailLead(0f, spacing) == 0f,
            "A path with no length cannot have a trail lead.");

        var source = File.ReadAllText("Assets/Scripts/Notes/TouchSlideDrop.cs");
        Assert(
            source.Contains(
                "AlphaVisualTiming.GetTouchSlideTrailLead(",
                StringComparison.Ordinal) &&
            !source.Contains(
                "progress + 0.015f", StringComparison.Ordinal),
            "TouchSlideDrop must derive the disappearance lead from actual bar " +
            "spacing instead of applying one normalized constant to every shape.");
    }

    private static void CheckEveryHintFormDropsTheOptionalBrackets()
    {
        var source = File.ReadAllText("MajdataEdit/Editor/AlphaCommandHints.cs");

        // A hint often spells several accepted forms separated by " / ". Only the
        // first was taken apart into parameters and the rest were pasted in as
        // written, so the brackets that stand for "you may leave this out" were
        // still on screen for exactly those commands that had a second form.
        Assert(
            source.Contains("Split(\" / \")", StringComparison.Ordinal),
            "Each accepted form in a hint has to be laid out on its own, or the " +
            "later ones keep whatever notation they were written with.");
        Assert(
            !source.Contains("block.Inlines.Add(signature[close..]);",
                StringComparison.Ordinal),
            "Whatever trails a form must not go on screen unread: that is how the " +
            "brackets got through.");
        Assert(
            source.Contains("StripOptionalBrackets(signature[close..])",
                StringComparison.Ordinal) &&
            source.Contains("block.Inlines.Add(StripOptionalBrackets(signature));",
                StringComparison.Ordinal),
            "Both places that write a signature straight out have to drop the " +
            "brackets marking an omittable argument first.");

        // Grey italics is the one way an omittable argument is spelled, so no
        // command may be left describing itself with brackets in prose either.
        foreach (var culture in new[] { "zh-CN", "en-US", "ja" })
        {
            var resx = File.ReadAllText($"MajdataEdit/Langs/Langs.{culture}.resx");
            var at = resx.IndexOf("AlphaSyntaxOptional", StringComparison.Ordinal);
            if (at < 0)
                continue;
            var entry = resx.Substring(at, Math.Min(600, resx.Length - at));
            Assert(
                !entry.Contains("[,", StringComparison.Ordinal),
                $"The {culture} help text still explains omittable arguments with " +
                "brackets, which is no longer what the editor draws.");
        }
    }

    private static SubtitleChange ParseSubtitle(string token)
    {
        SimaiProcess.ClearData();
        SimaiProcess.Serialize("(120){4}<" + token + ">1,\nE");
        var table = SimaiProcess.subtitleTable;
        var caption = table.Count > 0 ? table[0] : null;
        SimaiProcess.ClearData();
        return caption;
    }

    private static void CheckCaptionsCanBePlacedAndSized()
    {
        // Quoted caption text may contain commas without becoming extra arguments.
        var plain = ParseSubtitle("TEXT*(\"hello, world\")");
        Assert(
            plain != null && plain.text == "hello, world",
            $"A comma in the text is part of the text, got '{plain?.text}'.");
        Assert(
            plain.duration < 0f && plain.x == 0f && plain.y == 0f && plain.size == 0f,
            "A caption that asks for nothing has to come out asking for nothing, " +
            "so it lands where captions have always landed.");
        Assert(
            plain.font.Length == 0 && plain.index == 0 &&
            plain.style == "Fade" && plain.transition == 0f,
            "A plain caption must keep the original independent caption font, " +
            "channel zero and an immediate fade style.");

        var timed = ParseSubtitle("TEXT*(\"hello\",4:1)");
        Assert(
            timed.text == "hello" && timed.duration > 0f,
            $"A trailing duration still has to be read as one, got text " +
            $"'{timed.text}' and duration {timed.duration}.");

        var placed = ParseSubtitle("TEXT*(\"hello\",4:1,x=0.5,y=0.8,size=48)");
        Assert(
            placed.text == "hello" && placed.duration > 0f &&
            Math.Abs(placed.x - 0.5f) < 1e-5f &&
            Math.Abs(placed.y - 0.8f) < 1e-5f &&
            Math.Abs(placed.size - 48f) < 1e-5f,
            $"Placement and size have to survive alongside a duration, got " +
            $"'{placed.text}' {placed.duration} {placed.x} {placed.y} {placed.size}.");

        var positional = ParseSubtitle("TEXT*(\"hello\",3,0.2,0.3,44)");
        Assert(
            positional != null && Math.Abs(positional.duration - 3f) < 1e-5f &&
            Math.Abs(positional.x - 0.2f) < 1e-5f &&
            Math.Abs(positional.y - 0.3f) < 1e-5f &&
            Math.Abs(positional.size - 44f) < 1e-5f,
            "The documented five-position TEXT form must reach playback unchanged.");

        // Any order, any subset, and no duration needed to reach them.
        var reordered = ParseSubtitle("TEXT*(\"hello\",size=20,x=0.25)");
        Assert(
            reordered.text == "hello" && reordered.duration < 0f &&
            Math.Abs(reordered.size - 20f) < 1e-5f &&
            Math.Abs(reordered.x - 0.25f) < 1e-5f && reordered.y == 0f,
            "Writing only some of the settings, in any order and with no " +
            "duration, has to work: that is what the grey italics promise.");

        var styled = ParseSubtitle(
            "TEXT*(\"逐字字幕\",4,font=Allerta,index=3,style=Typewriter,transition=8:1)");
        Assert(
            styled != null && styled.font == "Allerta" && styled.index == 3 &&
            styled.style == "Typewriter" && styled.transition > 0f,
            "TEXT font, index and typewriter timing must survive serialization.");

        var positionalStyled = ParseSubtitle(
            "TEXT*(\"逐字字幕\",4,0.1,0.2,36,Allerta,2,Typewriter,8:1)");
        Assert(
            positionalStyled != null && positionalStyled.font == "Allerta" &&
            positionalStyled.index == 2 &&
            positionalStyled.style == "Typewriter" &&
            positionalStyled.transition > 0f,
            "The complete positional TEXT form must use the same parser as named options.");

        var skippedStyled = ParseSubtitle(
            "TEXT*(\"跳过默认项\",,0.2,,44,Allerta,,Typewriter,1)");
        Assert(
            skippedStyled != null && skippedStyled.duration < 0f &&
            Math.Abs(skippedStyled.x - 0.2f) < 1e-5f &&
            skippedStyled.y == 0f && skippedStyled.font == "Allerta" &&
            skippedStyled.index == 0 && skippedStyled.style == "Typewriter" &&
            Math.Abs(skippedStyled.transition - 1f) < 1e-5f,
            "Empty TEXT slots must retain their defaults without requiring named arguments.");

        var clamped = ParseSubtitle("TEXT*(\"hello\",x=9,y=-4,size=9999)");
        Assert(
            Math.Abs(clamped.x - 1f) < 1e-5f && clamped.y == 0f &&
            clamped.size <= 200f && clamped.size >= 8f,
            $"A caption asked to sit outside the screen or be the size of a " +
            $"building has to be held to something visible, got {clamped.x}, " +
            $"{clamped.y}, {clamped.size}.");

        var unknown = ParseSubtitle("TEXT*(\"score=100\")");
        Assert(
            unknown.text == "score=100" &&
            unknown.x == 0f && unknown.y == 0f && unknown.size == 0f,
            $"An unknown key belongs to the caption, got '{unknown.text}'.");
        Assert(
            AlphaCommandGrammar.TryValidate(
                "TEXT*(\"hello, world\",2)", 120f, out _),
            "Quoted TEXT must pass the shared grammar.");
        Assert(
            AlphaCommandGrammar.TryValidate(
                "TEXT*(\"hello\",4:1,x=0.5,y=0.8,size=48)", 120f, out _),
            "TEXT placement and size must pass the same shared grammar as runtime parsing.");
        Assert(
            AlphaCommandGrammar.TryValidate(
                "TEXT*(\"hello\",3,0.2,0.3,44)", 120f, out _),
            "The shared grammar must accept the five-position TEXT form.");
        Assert(
            AlphaCommandGrammar.TryValidate(
                "TEXT*(\"hello\",size=20,x=0.25)", 120f, out _),
            "TEXT must allow placement arguments without a duration.");
        Assert(
            AlphaCommandGrammar.TryValidate(
                "TEXT*(\"hello\",font=Default,index=4,style=Fade,transition=16:1)",
                120f, out _),
            "TEXT must validate the independent font, channel and transition options.");
        Assert(
            AlphaCommandGrammar.TryValidate(
                "TEXT*(\"hello\",3,0.2,0.3,44,Allerta,4,Typewriter,16:1)",
                120f, out _),
            "TEXT validation must accept all documented positional options.");
        Assert(
            AlphaCommandGrammar.TryValidate(
                "TEXT*(\"hello\",,0.2,,44,Allerta,,Typewriter,1)",
                120f, out _),
            "TEXT validation must allow commas to skip optional positional values.");
        Assert(
            !AlphaCommandGrammar.TryValidate(
                "TEXT*(\"hello\",x=0.2,x=0.4)", 120f, out _),
            "TEXT must reject duplicate placement arguments.");
        Assert(
            !AlphaCommandGrammar.TryValidate(
                "TEXT*(hello)", 120f, out _),
            "Unquoted TEXT must be rejected instead of being parsed differently by each layer.");
        ParserMessageLocale.SetCulture("zh-CN");
        AlphaCommandGrammar.TryValidate("TEXT*(\"hello\",3,2,0.3,44)", 120f, out var chineseError);
        Assert(
            chineseError.Contains("TEXT 位置参数格式", StringComparison.Ordinal),
            $"Chinese UI must receive a Chinese TEXT diagnostic, got '{chineseError}'.");
        ParserMessageLocale.SetCulture("en-US");
        Assert(
            AlphaCommandGrammar.TryValidate(
                "PVOVERLAY*(True,path/image.png,8:1)", 120f, out _),
            "PVOVERLAY must accept a chart-relative subfolder path.");

        var view = File.ReadAllText("Assets/Scripts/UI/DisplayTimelineController.cs");
        Assert(
            view.Contains("subtitleStyle.fontSize = size", StringComparison.Ordinal) &&
            view.Contains("Mathf.Clamp01(subtitle.x)", StringComparison.Ordinal) &&
            view.Contains("Mathf.Clamp01(subtitle.y)", StringComparison.Ordinal) &&
            view.Contains("WarmupSubtitleGlyphs", StringComparison.Ordinal) &&
            view.Contains("ResolveSubtitleFont(subtitle.font)", StringComparison.Ordinal) &&
            view.Contains("? FontStyle.Bold", StringComparison.Ordinal) &&
            view.Contains("activeSubtitles[subtitle.index] = subtitle",
                StringComparison.Ordinal) &&
            view.Contains("StringInfo.ParseCombiningCharacters", StringComparison.Ordinal),
            "The player has to draw the caption where and at the size it was " +
            "asked for, preserve independent channels and reveal whole characters.");
    }

    private static void CheckLiveColourReachesTheSlideArc()
    {
        // COLOR paints a slide's route through its own list of bar renderers,
        // which are not children of the note, so the live commands could not see
        // them: three overrides handed back only what the base class found, and
        // COLORV left every arc in the chart uncoloured.
        foreach (var (path, bars, star) in new[]
                 {
                     ("Assets/Scripts/Notes/SlideDrop.cs",
                         "slideBarRenderers", "spriteRenderer_star"),
                     ("Assets/Scripts/Notes/WifiDrop.cs",
                         "sbRender", "spriteRenderer_star"),
                     ("Assets/Scripts/Notes/TouchSlideDrop.cs",
                         "bars", "starRenderer")
                 })
        {
            var source = File.ReadAllText(path);
            var at = source.IndexOf(
                "protected override IEnumerable<SpriteRenderer> GetLiveVisualRenderers()",
                StringComparison.Ordinal);
            Assert(at >= 0, $"{path} has to say which renderers carry its live look.");
            var body = source.Substring(at, 700);
            Assert(
                body.Contains($"foreach (var renderer in {bars})",
                    StringComparison.Ordinal),
                $"{path} must hand back {bars} - the same renderers COLOR paints - " +
                "or COLORV keeps skipping the arc.");
            Assert(
                body.Contains(star, StringComparison.Ordinal),
                $"{path} must keep {star} out of the route's set: COLOR lets the " +
                "guide star follow the star category, and the two have to agree.");
        }
    }

    private static void CheckANoteCanBorrowAStarTrajectory()
    {
        // "1~[5-7[8:1]]" is a note carried along another slide's star path. It
        // keeps the carrier's radial guide, but owns no slide bars, slide head or
        // judgement.
        Assert(
            NoteExpressionParser.TryParse("1~[5-7[8:1]]", out var borrow, out var error),
            $"A note has to be able to borrow a trajectory: {error}");
        Assert(
            borrow.trajectory != null && borrow.trajectorySource == "5-7[8:1]",
            "The borrowed path has to survive parsing as a path, not as text.");
        Assert(
            borrow.kind == NoteExpressionKind.Tap && borrow.position.position == 1,
            "What is left after the borrow is an ordinary note and still says " +
            "where it was written.");

        // The brackets nest, so the first ']' is not the end of anything.
        Assert(
            borrow.trajectory!.segments.Count == 1 &&
            borrow.trajectory.segments[0].start.position == 5 &&
            borrow.trajectory.segments[0].end.position == 7,
            "The whole borrowed path has to be read, including its own duration " +
            "bracket; stopping at the first closing bracket loses the slide.");

        // Modifiers say what travels, and may be written on either side of the
        // borrow, because the borrow is lifted out before anything else reads.
        foreach (var text in new[] { "1b~[5-7[8:1]]", "1~[5-7[8:1]]b" })
        {
            Assert(
                NoteExpressionParser.TryParse(text, out var marked, out var markError),
                $"'{text}' has to parse: {markError}");
            Assert(
                marked.trajectory != null &&
                marked.modifiers.HasHead(NoteModifierFlags.Break),
                $"'{text}' has to keep both its borrow and its break.");
        }

        foreach (var (text, carrierType, isBreak) in new[]
                 {
                     ("1~[1-5[8:1]]", SimaiNoteType.Tap, false),
                     ("1~[1-5[8:1]]b", SimaiNoteType.Tap, true),
                     ("1~[1-5[8:1]]h", SimaiNoteType.Hold, false),
                     ("E1~[1-5[8:1]]", SimaiNoteType.Touch, false)
                 })
        {
            Assert(
                NoteExpressionParser.TryParse(text, out var carrier, out var carrierError) &&
                carrier.trajectory != null,
                $"Borrowed carrier '{text}' did not parse: {carrierError}");
            SimaiProcess.ClearData();
            SimaiProcess.Serialize($"(120){{4}}{text},\nE");
            var runtimeCarrier = SimaiProcess.notelist[0].getNotes()[0];
            Assert(
                runtimeCarrier.isTrajectoryOnly &&
                runtimeCarrier.trajectoryCarrierType == carrierType &&
                runtimeCarrier.isBreak == isBreak &&
                runtimeCarrier.trajectoryCarrierPosition == carrier.position.position &&
                runtimeCarrier.trajectoryCarrierIsDZone == carrier.position.isDZone,
                $"Borrowed carrier '{text}' lost its kind or modifiers.");
        }

        SimaiProcess.ClearData();
        SimaiProcess.Serialize(
            "(120){4}1!-5m[8:1]*-3m[8:1],E1~[4.8]," +
            "1~[1-5[8:1]],1,1,2,2,\nE");
        var exactCarriers = SimaiProcess.notelist
            .SelectMany(point => point.getNotes())
            .Where(note => note.isTrajectoryOnly)
            .ToList();
        Assert(
            exactCarriers.Count == 1 &&
            exactCarriers[0].trajectoryCarrierPosition == 1 &&
            exactCarriers[0].trajectoryCarrierType == SimaiNoteType.Tap,
            "The complete chart fragment around a borrowed Tap must retain exactly " +
            "one visible Tap carrier.");
        SimaiProcess.ClearData();
        Assert(
            NoteExpressionParser.TryParse("1$~[5-7[8:1]]", out var star, out _) &&
            star.modifiers.HasAny(NoteModifierFlags.ForceStar),
            "A star-shaped travelling note has to be sayable.");
        Assert(
            NoteExpressionParser.TryParse("1h[4:1]~[5-7[8:1]]", out var hold, out _) &&
            hold.kind == NoteExpressionKind.Hold && hold.trajectory != null,
            "A hold that travels has to keep being a hold.");

        // A note already following a path cannot also be a slide.
        Assert(
            !NoteExpressionParser.TryParse(
                "1~[5-7[8:1]]-3[8:1]", out _, out var slideError) &&
            slideError.Length > 0,
            "A slide written after a borrow has to be reported, not silently " +
            "dropped or silently drawn.");
        Assert(
            !NoteExpressionParser.TryParse(
                "1~[5-7[8:1]]~[3-4[8:1]]", out _, out var twiceError) &&
            twiceError.Length > 0,
            "Borrowing two trajectories at once has to be reported.");
        Assert(
            !NoteExpressionParser.TryParse("1~[]", out _, out var emptyError) &&
            emptyError.Length > 0,
            "An empty borrow has to be reported.");
        Assert(
            !NoteExpressionParser.TryParse(
                "1~[5-7]", out _, out var untimedError) &&
            untimedError.Length > 0,
            "A borrowed path with no duration has to be reported: a trajectory " +
            "with no time to run in is not a trajectory.");

        // The same "~[...]" spelling still carries a Touch's distance and its
        // picture. Nothing about those may change.
        Assert(
            NoteExpressionParser.TryParse("E1~[4.8]", out var radius, out var radiusError) &&
            radius.trajectory == null && radius.position.HasCustomRadius,
            $"A Touch distance is still a distance: {radiusError}");
        Assert(
            NoteExpressionParser.TryParse(
                "E1~[my-star.png]", out var skin, out var skinError) &&
            skin.trajectory == null && skin.position.HasSkin,
            $"A picture whose name has a dash in it is still a picture, not a " +
            $"trajectory: {skinError}");
        Assert(
            NoteExpressionParser.TryParse(
                "E1~[path/image.png]", out var nestedSkin, out var nestedSkinError) &&
            nestedSkin.trajectory == null &&
            nestedSkin.position.skin == "path/image.png",
            $"A note image must accept a safe chart-relative subfolder: {nestedSkinError}");
        Assert(
            !NoteExpressionParser.TryParse("1~[4.8]", out _, out var keyRadius) &&
            keyRadius.Length > 0,
            "A distance on a key note is still refused.");
        SimaiProcess.ClearData();
        SimaiProcess.Serialize("(120){4}E1~[4.8],\nE");
        var radiusNote = SimaiProcess.notelist[0].getNotes()[0];
        Assert(
            radiusNote.noteType == SimaiNoteType.Touch &&
            radiusNote.slidePath.Count == 0 && !radiusNote.isTrajectoryOnly,
            "E1~[4.8] must reach editor validation as a Touch radius, never as a slide.");
        SimaiProcess.ClearData();
    }

    private static void CheckABorrowedTrajectoryIsNeverJudged()
    {
        SimaiProcess.ClearData();
        SimaiProcess.Serialize("(120){4}1~[5-7[8:1]],\nE");
        var notes = SimaiProcess.notelist[0].getNotes();
        Assert(notes.Count == 1, $"One note, got {notes.Count}.");
        var note = notes[0];
        Assert(
            note.isTrajectoryOnly && note.isFake &&
            note.isFakeHead && note.isFakeSlide && note.isSlideNoHead,
            "A borrowed trajectory has to come out fake on every part and with " +
            "no head: nothing about it is judged and nothing is dropped at the " +
            "position it was written at.");
        Assert(
            note.noteType == SimaiNoteType.Slide &&
            note.trajectoryCarrierType == SimaiNoteType.Tap &&
            note.slidePath.Count == 1 &&
            note.startPosition == 5,
            $"The Tap carrier follows the borrowed path exactly as written, so it " +
             $"starts where that path starts, got {note.startPosition}.");
        var trajectoryJsonOptions = new JsonSerializerOptions { IncludeFields = true };
        var trajectoryJson = JsonSerializer.Serialize(note, trajectoryJsonOptions);
        var trajectoryRoundTrip = JsonSerializer.Deserialize<SimaiNote>(
            trajectoryJson, trajectoryJsonOptions);
        Assert(
            trajectoryRoundTrip is
            {
                isTrajectoryOnly: true,
                isFakeSlide: true,
                isSlideNoHead: true,
                trajectoryCarrierType: SimaiNoteType.Tap,
                trajectoryCarrierPosition: 1
            } &&
            trajectoryRoundTrip.slidePath.Count == 1,
            "Borrowed-trajectory flags and path must survive the editor-to-view JSON boundary.");
        Assert(
            note.slideTime > 0d && Math.Abs(note.slideStartTime) < 0.000001d,
            $"The borrowed duration must come along, but its carrier must move " +
            $"from the note beat without a slide-head wait: " +
            $"duration={note.slideTime}, start={note.slideStartTime}.");
        SimaiProcess.ClearData();

        SimaiProcess.Serialize("(120){4}1~[1-5[8:1]],\nE");
        var sameOrigin = SimaiProcess.notelist[0].getNotes()[0];
        Assert(
            sameOrigin.isTrajectoryOnly && sameOrigin.startPosition == 1 &&
            sameOrigin.touchEndPosition == 5 && sameOrigin.slidePath.Count == 1,
            "1~[1-5[8:1]] must carry one fake Tap from key 1 to key 5; " +
            "it must not fall back to a normal visible Slide.");
        SimaiProcess.ClearData();

        SimaiProcess.Serialize("(120){4}1$~[5-7[8:1]],\nE");
        var forcedStar = SimaiProcess.notelist[0].getNotes()[0];
        Assert(
            forcedStar.trajectoryCarrierType == SimaiNoteType.Tap &&
            forcedStar.isForceStar,
            "The carrier stays a Tap by kind; only its explicit $ modifier tells " +
            "the View to draw the moving star sprite.");
        SimaiProcess.ClearData();

        // An ordinary slide is untouched by any of this.
        SimaiProcess.ClearData();
        SimaiProcess.Serialize("(120){4}1-5[8:1],\nE");
        var plain = SimaiProcess.notelist[0].getNotes()[0];
        Assert(
            !plain.isTrajectoryOnly && !plain.isFake && !plain.isSlideNoHead,
            "A plain slide must keep its head, its arc and its judgement.");
        SimaiProcess.ClearData();

        // A wifi star is three stars on three paths, so there is no one trajectory
        // to hand over. It is refused with a message rather than drawn wrongly.
        Assert(
            !NoteExpressionParser.TryParse("1~[5w7[8:1]]", out _, out var refusal) &&
            refusal.Length > 0,
            "A wifi trajectory is not one trajectory and has to say so.");

        // A touch star travels a path of its own kind, and a note may borrow it.
        // Which star travels is decided by the path, not by the note that borrowed
        // it, so a key note wearing a touch star has to come out as a touch slide.
        foreach (var text in new[] { "1~[C1-A5[8:1]]", "A1~[C1-A5[8:1]]" })
        {
            Assert(
                NoteExpressionParser.TryParse(text, out var touch, out var touchError),
                $"'{text}' has to be able to borrow a touch trajectory: {touchError}");
            Assert(
                touch.trajectory != null && touch.isTouchPath,
                $"'{text}' has to be carried by a touch star.");
        }
        SimaiProcess.ClearData();
        SimaiProcess.Serialize("(120){4}1~[C1-A5[8:1]],\nE");
        var borrowedTouch = SimaiProcess.notelist[0].getNotes()[0];
        Assert(
            borrowedTouch.isTouchSlide && borrowedTouch.isTrajectoryOnly &&
            borrowedTouch.isFakeSlide && borrowedTouch.isSlideNoHead,
            "A borrowed touch trajectory reaches the view as a touch slide that " +
            "is fake and headless.");
        Assert(
            borrowedTouch.touchArea == 'C' && borrowedTouch.touchEndArea == 'A' &&
            borrowedTouch.touchEndPosition == 5,
            $"Its areas come from the path it travels, got " +
            $"{borrowedTouch.touchArea}->{borrowedTouch.touchEndArea}" +
            $"{borrowedTouch.touchEndPosition}.");
        SimaiProcess.ClearData();

        // Borrowed trajectories use a dedicated visual component. They must not
        // enter either regular slide class, because those classes own judgement,
        // route bars and stateful pause/resume transitions.
        var trajectoryLoader = File.ReadAllText("Assets/Scripts/JsonDataLoader.cs");
        Assert(
            trajectoryLoader.Contains(
                "InstantiateTrajectoryCarrier(timing, note);", StringComparison.Ordinal) &&
            trajectoryLoader.Contains(
                "AddComponent<TrajectoryCarrierDrop>()", StringComparison.Ordinal),
            "A borrowed trajectory must instantiate only its dedicated carrier.");
        var carrierSource = File.ReadAllText(
            "Assets/Scripts/Notes/TrajectoryCarrierDrop.cs");
        Assert(
            carrierSource.Contains("public sealed class TrajectoryCarrierDrop", StringComparison.Ordinal) &&
            carrierSource.Contains("carrier.forceRenderingOff", StringComparison.Ordinal) &&
            !carrierSource.Contains("Check(", StringComparison.Ordinal),
            "The dedicated carrier must remain visual-only and never judge input.");
        var loader = File.ReadAllText("Assets/Scripts/JsonDataLoader.cs");
        var resolveAt = loader.IndexOf(
            "private List<SlidePathSegmentData> ResolveSlidePath(",
            StringComparison.Ordinal);
        Assert(
            resolveAt >= 0 &&
            loader.IndexOf("var serialized = note.slidePath;", resolveAt,
                StringComparison.Ordinal) <
            loader.IndexOf("if (!string.IsNullOrEmpty(note.pathExpression))",
                resolveAt, StringComparison.Ordinal),
            "The player must use the serialized AST before attempting to reparse " +
            "a borrowed trajectory wrapper that is not itself a slide path.");
        Assert(
            File.ReadAllText("MajdataEdit/MainWindowCore.cs").Contains(
                "note.slidePath != null && note.slidePath.Count > 0",
                StringComparison.Ordinal),
            "Editor validation must consume the AST segments already parsed for " +
            "borrowed trajectories, not reparse their wrapper as a slide path.");
    }

    // A filter written behind the notes of its own beat used to be dropped by the
    // serializer, the validator and the colourizer at once: it never played, and
    // nothing said why. What keeps a slide arc from being read the same way is the
    // command name, so the touch slide below has to survive the same scan.
    // v0.4.2 read a command only while its beat had no notes yet, so a filter
    // written among the notes was dropped and never played. The placements it did
    // handle have to keep the times it gave them; only the dropped ones may change.
    private static void CheckFilterPlacementAgainstV042()
    {
        static string Times(string chart, bool baseline)
        {
            if (baseline)
            {
                Baseline042.SimaiProcess.ClearData();
                Baseline042.SimaiProcess.Serialize(chart);
                var old = string.Join(" ", Baseline042.SimaiProcess.effectTable
                    .Select(effect => $"{effect.effect}@{effect.time:F3}"));
                Baseline042.SimaiProcess.ClearData();
                return old;
            }
            SimaiProcess.ClearData();
            SimaiProcess.Serialize(chart);
            var now = string.Join(" ", SimaiProcess.effectTable
                .Select(effect => $"{effect.effect}@{effect.time:F3}"));
            SimaiProcess.ClearData();
            return now;
        }

        // Written before the notes, or alone on the beat: v0.4.2 played these, and
        // they have to keep playing at exactly the same time.
        foreach (var chart in new[]
                 {
                     "(120){4}<TINT*(TRUE,FF0000,1)>,1,1,1,\nE",
                     "(120){4}1,<TINT*(TRUE,FF0000,1)>,1,1,\nE",
                     "(120){4}1,1,<TINT*(TRUE,FF0000,1)>1,1,\nE",
                 })
            Assert(
                Times(chart, true) == Times(chart, false) && Times(chart, false).Length > 0,
                $"v0.4.2 and this build have to agree on {chart.Replace("\n", "\\n")}: " +
                $"042=[{Times(chart, true)}] now=[{Times(chart, false)}]");

        // Written among the notes: v0.4.2 dropped the command by accident. This
        // build deliberately rejects it, reports why, and keeps the chart intact.
        foreach (var chart in new[]
                 {
                     "(120){4}1,2,3<TINT*(TRUE,FF0000,1)>,4,\nE",
                     "(120){4}1\n<TINT*(TRUE,FF0000,1)>,2,3,4,\nE",
                     "(120){4}1,2,3/4<GAUSSIAN*(TRUE,1)>,4,\nE",
                 })
        {
            Assert(
                Times(chart, true).Length == 0,
                $"this is the v0.4.2 bug being fixed, so v0.4.2 has to be the one " +
                $"that loses it: {chart.Replace("\n", "\\n")}");
            Assert(
                Times(chart, false).Length == 0 &&
                SimaiProcess.ValidateAlphaCommands(chart).Count > 0,
                $"this build has to reject and report a command after notes: " +
                $"{chart.Replace("\n", "\\n")}");

            // v0.4.2 left the '<' in the note text, where the slide parser read it
            // as an arc and threw on the argument list. Serialize catches that by
            // emptying every table, so one filter written among the notes took the
            // whole chart down with it.
            Baseline042.SimaiProcess.ClearData();
            Baseline042.SimaiProcess.Serialize(chart);
            var lostNotes = Baseline042.SimaiProcess.notelist.Count;
            Baseline042.SimaiProcess.ClearData();
            SimaiProcess.ClearData();
            SimaiProcess.Serialize(chart);
            var keptNotes = SimaiProcess.notelist.Count;
            SimaiProcess.ClearData();
            Assert(
                lostNotes == 0 && keptNotes == 4,
                $"v0.4.2 lost the whole chart to this, and this build keeps every " +
                $"note of it: 042 kept {lostNotes} notes, this build kept " +
                $"{keptNotes}.");
        }
    }

    // The judge labels and the counts beside them are two text boxes drawn row for
    // row, so they line up only while they agree about where a row is. A font whose
    // lines are a hair taller used to push the last row out of the authored height
    // and, with truncation on, the Late count simply vanished - which is why both
    // boxes overflow now and why the scene has to keep them the same shape.
    private static void CheckTheJudgeColumnsLineUp()
    {
        var scene = File.ReadAllText("Assets/Scenes/SampleScene.unity");
        var documents = scene.Split("--- !u!");

        static string? Field(string document, string field)
        {
            var at = document.IndexOf(field + ":", StringComparison.Ordinal);
            if (at < 0)
                return null;
            var end = document.IndexOf('\n', at);
            return document.Substring(at + field.Length + 1, end - at - field.Length - 1).Trim();
        }

        string? Component(string name, string kind, string field)
        {
            var owner = documents.FirstOrDefault(document =>
                document.Contains("GameObject:", StringComparison.Ordinal) &&
                document.Contains("m_Name: " + name, StringComparison.Ordinal));
            Assert(owner != null, $"{name} has to exist in the scene.");
            foreach (var id in Regex.Matches(owner!, @"component: \{fileID: (\d+)\}")
                         .Select(match => match.Groups[1].Value))
            {
                var document = documents.FirstOrDefault(candidate =>
                    Regex.IsMatch(candidate, @"^\d+ &" + id + @"\b"));
                if (document == null)
                    continue;
                if (kind == "rect" && !document.Contains("RectTransform:", StringComparison.Ordinal))
                    continue;
                if (kind == "text" && !document.Contains("m_FontData:", StringComparison.Ordinal))
                    continue;
                return Field(document, field);
            }
            return null;
        }

        // A row of one column sits at the same height as the row beside it only if
        // both boxes start at the same place and step by the same amount.
        foreach (var (kind, field) in new[]
                 {
                     ("rect", "m_AnchoredPosition"),
                     ("rect", "m_SizeDelta"),
                     ("rect", "m_Pivot"),
                     ("rect", "m_AnchorMin"),
                     ("rect", "m_AnchorMax"),
                     ("text", "m_FontSize"),
                     ("text", "m_LineSpacing"),
                     ("text", "m_Alignment"),
                     ("text", "m_BestFit")
                 })
        {
            var labels = Component("JudgeResultText", kind, field);
            var counts = Component("JudgeResultCount", kind, field);
            Assert(labels != null && counts != null,
                $"both judge columns have to declare {field}.");
            if (field == "m_AnchoredPosition" || field == "m_SizeDelta")
            {
                // The columns stand side by side, so only x may differ.
                var labelY = Regex.Match(labels!, @"y: (-?[\d.]+)").Groups[1].Value;
                var countY = Regex.Match(counts!, @"y: (-?[\d.]+)").Groups[1].Value;
                Assert(labelY == countY,
                    $"the judge columns have to agree on {field} y: " +
                    $"labels {labelY}, counts {countY}.");
                continue;
            }
            Assert(labels == counts,
                $"the judge columns have to agree on {field}: " +
                $"labels {labels}, counts {counts}.");
        }

        // Eight rows on the left, eight on the right, with the gap in the same place.
        var labelText = Regex.Match(scene, @"m_Text: '(CriticalPf.*?)'", RegexOptions.Singleline)
            .Groups[1].Value;
        var rows = Regex.Split(labelText.Trim(), @"\n\s*\n")
            .Select(row => row.Trim())
            .ToList();
        Assert(rows.Count == 7,
            $"the label column is authored as seven blocks of rows, got {rows.Count}.");
        var counter = File.ReadAllText("Assets/Scripts/UI/ObjectCounter.cs");
        var countLine = counter
            .Split('\n')
            .First(line => line.Contains("judgeResultCount.text = $\"", StringComparison.Ordinal));
        Assert(
            countLine.Split("\\n").Length - 1 == 7,
            $"the count column has to have as many rows as the label column: {countLine.Trim()}");
        Assert(
            countLine.Contains("\\n\\n", StringComparison.Ordinal),
            "the blank row before Fast has to be in the counts too, or every row " +
            "below it sits beside the wrong label.");

        // Neither box may cut a row off, at any resolution.
        Assert(
            counter.Contains("VerticalWrapMode.Overflow", StringComparison.Ordinal) &&
            counter.Contains("HorizontalWrapMode.Overflow", StringComparison.Ordinal),
            "both columns have to overflow rather than truncate or wrap.");
        Assert(
            counter.Contains("authoredJudgeTextPosition", StringComparison.Ordinal) &&
            counter.Contains("authoredJudgeCountPosition", StringComparison.Ordinal) &&
            counter.Contains("selectedDisplayFontPreset != 0", StringComparison.Ordinal),
            "the original player font must retain the authored 4.4.0 layout while " +
            "custom fonts apply only their explicit count-column correction.");
    }

    private static void CheckAFilterBehindNotesIsRejectedSafely()
    {
        static (double? effect, string notes, int errors) Read(string chart)
        {
            SimaiProcess.ClearData();
            SimaiProcess.Serialize(chart);
            var effect = SimaiProcess.effectTable.Count == 1
                ? SimaiProcess.effectTable[0].time
                : (double?)null;
            var notes = string.Join(
                " ", SimaiProcess.notelist.Select(note => $"{note.notesContent}@{note.time:F2}"));
            var errors = SimaiProcess.ValidateAlphaCommands(chart).Count;
            SimaiProcess.ClearData();
            return (effect, notes, errors);
        }

        // A misplaced command is rejected, but its note text is preserved instead
        // of being sent through the slide parser with the command still attached.
        foreach (var (chart, expectedNotes) in new[]
                 {
                     ("(120){4}1,2,3<TINT*(TRUE,FF0000,1)>,4,\nE", "3@1.00"),
                     ("(120){4}1,2,3/4<TINT*(TRUE,FF0000,1)>,4,\nE", "3/4@1.00"),
                     ("(120){4}1,2,3-5[8:1]<TINT*(TRUE,FF0000,1)>,4,\nE", "3-5[8:1]@1.00"),
                     ("(120){4}1,2,3h[8:1]<GAUSSIAN*(TRUE,1)>,4,\nE", "3h[8:1]@1.00"),
                 })
        {
            var (effect, notes, errors) = Read(chart);
            Assert(
                !effect.HasValue,
                $"a filter behind its notes must not run: {chart}");
            Assert(notes.Contains(expectedNotes, StringComparison.Ordinal),
                $"the notes it was written behind still play: {chart}");
            Assert(errors > 0, $"its placement must be reported: {chart}");
        }

        // Written before the notes, or alone on the beat, as it always worked.
        Assert(Read("(120){4}1,2,<TINT*(TRUE,FF0000,1)>3,4,\nE").effect == 1.0,
            "a filter written before its notes is unchanged");
        Assert(Read("(120){4}1,2,<TINT*(TRUE,FF0000,1)>,4,\nE").effect == 1.0,
            "a filter alone on its beat is unchanged");

        // '<' after note text is a slide arc far more often than a command, and a
        // touch slide puts a letter after it too.
        foreach (var chart in new[]
                 {
                     "(120){4}1,C-B7<B3[8:1],1,1,\nE",
                     "(120){4}1,B7<B3[8:1],1,1,\nE",
                     "(120){4}1,1<5[8:1],1,1,\nE",
                 })
        {
            var (effect, notes, _) = Read(chart);
            Assert(!effect.HasValue, $"a slide arc is not a command: {chart}");
            Assert(notes.Contains('<'), $"and keeps its arc: {chart}");
        }

        // A name no command answers to stays note text, typo or not.
        Assert(!Read("(120){4}1,2,3<NOSUCHTHING*1>,4,\nE").effect.HasValue,
            "an unknown name behind a note is not promoted to a command");
    }

    private static void CheckLiveVisualsCannotMissANote()
    {
        var controller = File.ReadAllText("Assets/Scripts/UI/LiveNoteVisualController.cs");
        Assert(
            !controller.Contains("noteCache", StringComparison.Ordinal) &&
            !controller.Contains("GetComponentsInChildren", StringComparison.Ordinal),
            "The live visual controller must not hold a list of notes: any note " +
            "missing from it is a note that never gets its live look.");
        Assert(
            controller.Contains("public int Version { get; private set; }",
                StringComparison.Ordinal),
            "The controller has to publish a version for notes to compare against.");

        var note = File.ReadAllText("Assets/Scripts/Notes/NoteDrop.cs");
        var lateUpdateAt = note.IndexOf("protected virtual void LateUpdate()",
            StringComparison.Ordinal);
        Assert(
            lateUpdateAt >= 0,
            "Every note has to ask for its live look once a frame from one shared " +
            "place, or each note type is one more that can be forgotten.");
        var pull = note.Substring(lateUpdateAt, 600);
        Assert(
            pull.Contains("live.Version == appliedLiveVisualVersion",
                StringComparison.Ordinal) &&
            pull.Contains("live.ApplyCurrent(this)", StringComparison.Ordinal),
            "A note must re-ask exactly when the version it applied is no longer " +
            "the current one.");

        // Unity dispatches LateUpdate to the most derived declaration only, so a
        // note type declaring its own without chaining silently opts out of the
        // pull and stops getting live colours.
        foreach (var path in Directory.GetFiles("Assets/Scripts/Notes", "*.cs"))
        {
            var source = File.ReadAllText(path);
            var at = source.IndexOf("void LateUpdate()", StringComparison.Ordinal);
            if (at < 0 || path.EndsWith("NoteDrop.cs", StringComparison.Ordinal))
                continue;
            if (!source.Contains(": NoteDrop", StringComparison.Ordinal) &&
                !source.Contains(": NoteLongDrop", StringComparison.Ordinal) &&
                !source.Contains(": TapBase", StringComparison.Ordinal))
                continue;
            Assert(
                source.Contains("protected override void LateUpdate()",
                    StringComparison.Ordinal) &&
                source.IndexOf("base.LateUpdate();", StringComparison.Ordinal) > at,
                $"{Path.GetFileName(path)} declares its own LateUpdate, so it must " +
                "override and chain to the base one or it stops asking for live " +
                "colours entirely.");
        }

        // A slide deforms its route by building bars of its own. Bars that did not
        // exist when the live colour was put on are bars still wearing the old one.
        var slide = File.ReadAllText("Assets/Scripts/Notes/SlideDrop.cs");
        var rebuildAt = slide.IndexOf("slideBars.AddRange(visualBars);",
            StringComparison.Ordinal);
        Assert(rebuildAt >= 0, "SlideDrop should still rebuild its bar list.");
        Assert(
            slide.IndexOf("InvalidateLiveVisual();", rebuildAt,
                StringComparison.Ordinal) > 0 &&
            slide.IndexOf("InvalidateLiveVisual();", rebuildAt,
                StringComparison.Ordinal) < rebuildAt + 700,
            "A rebuilt route must ask for the live look again, or the arc keeps " +
            "its original colour while the guide star changes.");

        foreach (var path in new[]
                 {
                     "Assets/Scripts/Notes/TapBase.cs",
                     "Assets/Scripts/Notes/HoldDrop.cs"
                 })
        {
            var source = File.ReadAllText(path);
            Assert(
                source.Contains(
                    "protected override IEnumerable<SpriteRenderer> GetLiveVisualRenderers()",
                    StringComparison.Ordinal) &&
                source.Contains("yield return lineSpriteRender;", StringComparison.Ordinal),
                $"{path} must include its sibling guide line in COLORV/ALPHAV just " +
                "as static COLOR/ALPHA do.");
        }

        // The override layer reads the live commands and nothing else: what a note
        // was painted with already carries every non-live COLOR, and repeating it
        // here would keep restating a colour after COLORV*NULL asked for it back.
        var loader = File.ReadAllText("Assets/Scripts/JsonDataLoader.cs");
        foreach (var resolver in new[]
                 {
                     "ResolveLiveColor", "ResolveLiveSize", "ResolveLiveAlpha",
                     "ResolveLiveGuideStarColor", "ResolveLiveGuideStarSize",
                     "ResolveLiveGuideStarAlpha"
                 })
        {
            var at = loader.IndexOf("internal", loader.IndexOf(
                resolver + "(NoteDrop", StringComparison.Ordinal) - 40,
                StringComparison.Ordinal);
            Assert(at >= 0, $"{resolver} should still exist.");
            var body = loader.Substring(at, loader.IndexOf(';', at) - at);
            Assert(
                body.Contains("liveOnly: true", StringComparison.Ordinal),
                $"{resolver} must ask for the live answer only.");
        }

        // A note wearing nothing live goes back into its own material. Left in the
        // tint material it would come back the right colour and stop shining.
        var restoreAt = note.IndexOf("private void ApplyOrRestore", StringComparison.Ordinal);
        Assert(restoreAt >= 0, "ApplyOrRestore should own that decision in one place.");
        var restore = note.Substring(restoreAt, 700);
        Assert(
            restore.Contains("state.HasColorOverride || state.HasAlphaOverride",
                StringComparison.Ordinal) &&
            restore.Contains("renderer.sharedMaterial = state.Material;",
                StringComparison.Ordinal),
            "Without an override left, the captured material has to come back.");
    }

    // A Touch's petals are placed from the SV integral, which can sit on either
    // side of the note. The far side used to read as "already landed", so a Touch
    // approached through a negative integral stayed shut for its whole approach.
    private static void CheckTouchOpensFromTheCentreOnEitherSide()
    {
        foreach (var name in new[] { "TouchDrop.cs", "TouchHoldDrop.cs" })
        {
            var source = File.ReadAllText("Assets/Scripts/Notes/" + name);
            var at = source.IndexOf("var timing = GetTouchVisualTiming();",
                StringComparison.Ordinal);
            Assert(at >= 0, $"{name} should still read its visual timing from SV.");
            var body = source.Substring(at, 900);
            Assert(
                body.Contains("if (timeProvider.AudioTime < time)",
                    StringComparison.Ordinal) &&
                body.Contains("timing = -Mathf.Abs(timing);", StringComparison.Ordinal),
                $"{name} must mirror the far side of the scroll while the note is " +
                "still ahead of the clock, or a negative SV integral draws the " +
                "note shut for its whole approach.");
            var mirrorAt = body.IndexOf("timing = -Mathf.Abs(timing);",
                StringComparison.Ordinal);
            Assert(
                mirrorAt < body.IndexOf("var pow =", StringComparison.Ordinal),
                $"{name} must mirror before the petal distance is worked out.");
        }

        // The mirror is what the petals are placed from; judgement stays on the
        // audio clock, where an SV integral has no say.
        var hold = File.ReadAllText("Assets/Scripts/Notes/TouchHoldDrop.cs");
        var judgeAt = hold.IndexOf("var judgeTiming = GetJudgeTiming();",
            StringComparison.Ordinal);
        Assert(
            judgeAt > hold.IndexOf("timing = -Mathf.Abs(timing);", StringComparison.Ordinal),
            "The hold's own duration must still come from the audio clock.");
        Assert(
            hold.Contains("(LastFor - judgeTiming) / LastFor", StringComparison.Ordinal),
            "The mask must still be driven by the judge clock, not the mirrored one.");

        // Mirroring is a function of the timing alone, so a clock or an integral
        // running backwards reopens the petals on its own.
        static float Distance(float timing, float moveDuration)
        {
            var pow = -MathF.Exp(8 * (-MathF.Abs(timing) * 0.4f / moveDuration) - 0.85f)
                      + 0.42f;
            return Math.Clamp(pow, 0f, 0.4f);
        }
        Assert(
            Math.Abs(Distance(-0.30f, 0.8f) - Distance(0.30f, 0.8f)) < 1e-6,
            "Both sides of the note have to place the petals alike.");
        Assert(
            Distance(0f, 0.8f) < 0.001f,
            "The note's own moment is still the shut one.");
        Assert(
            Distance(0.30f, 0.8f) > Distance(0.10f, 0.8f),
            "Further out in scroll terms has to read as further open, so an " +
            "integral running backwards opens the petals out of the centre.");
    }

    // An omittable argument is grey italics in the completion popup and used to be
    // grey italics wrapped in visible brackets in the help window, so the same
    // thing was written two ways.
    private static void CheckOptionalArgumentsLookTheSameEverywhere()
    {
        var help = File.ReadAllText("MajdataEdit/SubWindow/AlphaSyntaxHelp.xaml.cs");
        var at = help.IndexOf("private static void AddCodeRuns", StringComparison.Ordinal);
        Assert(at >= 0, "AddCodeRuns should still render the help window's syntax lines.");
        var body = help.Substring(at, help.IndexOf("private static Run CreateCodeRun",
            StringComparison.Ordinal) - at);
        Assert(
            body.Contains("code.IndexOf(\"[,\"", StringComparison.Ordinal),
            "Only a bracket group opening with a comma marks an omittable " +
            "argument; every other bracket belongs to the syntax being described.");
        Assert(
            !body.Contains("CreateCodeRun(code[index].ToString(), true)",
                StringComparison.Ordinal),
            "The brackets around an omittable argument are notation and must not " +
            "be drawn: grey italics is what says it can be left out.");
        Assert(
            body.Contains("paragraph.Inlines.Add(new Run(code[start..]))",
                StringComparison.Ordinal),
            "Text outside an omittable group has to be written as authored, or " +
            "syntax like h[duration] loses the brackets it is typed with.");

        // What the reader is told has to match what the reader is shown.
        foreach (var lang in new[] { "en-US", "zh-CN", "ja" })
        {
            var resx = File.ReadAllText($"MajdataEdit/Langs/Langs.{lang}.resx");
            Assert(
                !resx.Contains("Square brackets in this document mark optional",
                    StringComparison.Ordinal) &&
                !resx.Contains("方括号只用于本页表示", StringComparison.Ordinal) &&
                !resx.Contains("このページの角括弧は省略可能", StringComparison.Ordinal),
                $"{lang} still explains optionality as brackets, which are no " +
                "longer drawn.");
        }
    }

    private static void CheckHoldEndCapIsHiddenBeforeStart()
    {
        var source = File.ReadAllText("Assets/Scripts/Notes/HoldDrop.cs");
        var awakeAt = source.IndexOf("private void Awake()", StringComparison.Ordinal);
        Assert(awakeAt >= 0, "HoldDrop should still suppress rendering in Awake.");
        var startAt = source.IndexOf("private void Start()", StringComparison.Ordinal);
        Assert(startAt > awakeAt, "Start should still follow Awake in HoldDrop.");
        var awake = source.Substring(awakeAt, startAt - awakeAt);
        Assert(
            awake.Contains("GetChild(1)", StringComparison.Ordinal) &&
            awake.Contains("enabled = false", StringComparison.Ordinal),
            "Awake must switch off the hold's end cap on child 1, or it flashes at " +
            "the centre of the playfield for a frame.");
        Assert(
            !awake.Contains("forceRenderingOff = true;\n        if (transform.childCount > 1",
                StringComparison.Ordinal) &&
            awake.Contains("endCap.enabled = false;", StringComparison.Ordinal),
            "The end cap must be cleared by 'enabled', the way Start and Update " +
            "drive it: forceRenderingOff is never set back and would hide it for good.");
    }

    private static void CheckBounceStaysOnItsOwnKey()
    {
        var source = File.ReadAllText("Assets/Scripts/Notes/NoteDrop.cs");
        var start = source.IndexOf("float GetBounceDistance", StringComparison.Ordinal);
        Assert(start >= 0, "GetBounceDistance should still exist in NoteDrop.");
        var body = source.Substring(start, source.IndexOf("protected SpawnPresentation",
            start, StringComparison.Ordinal) - start);
        Assert(
            !body.Contains("-magnitude", StringComparison.Ordinal),
            "GetBounceDistance must not negate the radius to express direction: " +
            "that mirrors the note onto the opposite key instead of reversing " +
            "the bounce.");

        // Same shape the method uses, checked over the whole bounce.
        var spawn = AlphaVisualTiming.DefaultSpawnRadius;
        var destroy = AlphaVisualTiming.DefaultDestroyRadius;
        foreach (var direction in new[] { 1f, -1f })
            for (var progress = 0f; progress <= 1f; progress += 0.01f)
            {
                var fromApex = progress * 2f - 1f;
                var excursion = (destroy - spawn) * (1f - fromApex * fromApex);
                var radius = direction < 0f ? destroy + excursion : destroy - excursion;
                Assert(
                    radius > 0f,
                    $"Bounce radius went to {radius} at progress {progress} with " +
                    $"direction {direction}; anything at or below zero flips the " +
                    "note onto the opposite side of the playfield.");
            }

        // The two directions have to leave the ring on opposite sides, otherwise
        // a negative bounce is indistinguishable from a positive one.
        var apexIn = destroy - (destroy - spawn);
        var apexOut = destroy + (destroy - spawn);
        Assert(
            apexIn < destroy && apexOut > destroy,
            "A positive bounce should dip inside the judgement ring and a " +
            "negative one should bulge outside it.");
    }

    private static void CheckNotesAndPlayfieldRevealTogether()
    {
        foreach (var path in new[]
                 {
                     "Assets/Scripts/Notes/TapBase.cs",
                     "Assets/Scripts/Notes/HoldDrop.cs",
                     "Assets/Scripts/Notes/StarDrop.cs",
                     "Assets/Scripts/Notes/EachLineDrop.cs"
                 })
        {
            var source = File.ReadAllText(path);
            Assert(
                !source.Contains("AudioTime < 0f", StringComparison.Ordinal),
                $"{path} must not hide notes until time zero: that withholds the " +
                "approach of every note near the start of the chart.");
            Assert(
                source.Contains("GameplayRevealTime", StringComparison.Ordinal),
                $"{path} must gate on the shared reveal time.");
        }

        foreach (var path in new[]
                 {
                     "Assets/Scripts/UI/BGManager.cs",
                     "Assets/Scripts/UI/DisplayTimelineController.cs"
                 })
            Assert(
                File.ReadAllText(path).Contains(
                    "AlphaVisualTiming.GameplayRevealTime",
                    StringComparison.Ordinal),
                $"{path} must take the reveal time from the shared constant " +
                "rather than repeating the number.");

        // The floor has to sit before the earliest moment any note starts moving.
        // Slower notes travel longer, so the slowest speed sets the requirement.
        var curve = new[] { new ScrollPoint(0d, 0d, 1f) };
        foreach (var speed in new[] { 2f, 3f, 4f, 7f, 10f })
        {
            var travel = AlphaVisualTiming.DefaultDestroyRadius -
                         AlphaVisualTiming.DefaultSpawnRadius;
            // Judge time 0 is the worst case: nothing in a chart starts earlier.
            var approachStart = -travel / speed;
            Assert(
                approachStart > AlphaVisualTiming.GameplayRevealTime,
                $"At speed {speed} a note at time 0 starts its approach at " +
                $"{approachStart:F3}s, which is before the reveal time " +
                $"{AlphaVisualTiming.GameplayRevealTime}s, so it would still pop " +
                "into view part way down the path.");

            // And it really is at the ring at its judge time, which is what makes
            // withholding the approach fatal rather than cosmetic.
            var radius = AlphaVisualTiming.GetVisualRadius(
                AlphaVisualTiming.GetCumulativeScroll(curve, 0d),
                AlphaVisualTiming.GetCumulativeScroll(curve, 0d),
                speed,
                AlphaVisualTiming.DefaultSpawnRadius,
                AlphaVisualTiming.DefaultDestroyRadius);
            Assert(
                Math.Abs(radius - AlphaVisualTiming.DefaultDestroyRadius) < 0.001f,
                $"A note at time 0 should sit on the judgement ring at time 0, " +
                $"got radius {radius}.");
        }
    }

    /// <summary>
    /// The text of one method, sliced from its signature to the next member at
    /// the same indent. Good enough to ask what a method does not mention.
    /// </summary>
    private static string MethodBody(string source, string name)
    {
        var start = source.IndexOf(name + "(", StringComparison.Ordinal);
        Assert(start >= 0, $"{name} not found; this test needs updating.");
        var end = source.IndexOf("\n    private ", start, StringComparison.Ordinal);
        return end < 0 ? source[start..] : source[start..end];
    }

    private static void CheckSlideShapeResolver()
    {
        // View owns the prefab table; the resolver mirrors its keys so the editor can
        // reject undrawable shapes without keeping a second list.
        var loader = File.ReadAllText("Assets/Scripts/JsonDataLoader.cs");
        var mapStart = loader.IndexOf("SLIDE_PREFAB_MAP", StringComparison.Ordinal);
        var mapEnd = loader.IndexOf("};", mapStart, StringComparison.Ordinal);
        var prefabKeys = System.Text.RegularExpressions.Regex
            .Matches(loader[mapStart..mapEnd], "\\{\"(?<key>[A-Za-z0-9]+)\"")
            .Select(match => match.Groups["key"].Value)
            .ToHashSet(StringComparer.Ordinal);
        Assert(
            prefabKeys.SetEquals(SlideShapeResolver.SupportedPrefabKeys),
            "Shared prefab key list drifted from View's slide prefab table: " +
            string.Join(
                ", ",
                prefabKeys
                    .Except(SlideShapeResolver.SupportedPrefabKeys)
                    .Concat(SlideShapeResolver.SupportedPrefabKeys.Except(prefabKeys))));

        string?[] shapes =
        {
            "-", ">", "<", "^", "v", "p", "q", "pp", "qq",
            "rp", "rq", "s", "z", "w"
        };

        var compared = 0;
        foreach (var shape in shapes)
        {
            for (var start = 1; start <= 8; start++)
            for (var end = 1; end <= 8; end++)
            {
                var text = $"{start}{shape}{end}";
                if (!SlidePathParser.TryParsePath($"{text}[8:1]", out var path) ||
                    path.segments.Count != 1)
                    continue;
                var resolved = SlideShapeResolver.TryResolve(
                    path.segments[0], out var key, out _, out _);
                var legacy = LegacyShapeFromText(text);
                Assert(
                    resolved == (legacy != null) &&
                    (!resolved || key == legacy),
                    $"Shape resolution changed for {text}: " +
                    $"{(resolved ? key : "rejected")} vs {legacy ?? "rejected"}");
                compared++;
            }
        }

        for (var start = 1; start <= 8; start++)
        for (var turn = 1; turn <= 8; turn++)
        for (var end = 1; end <= 8; end++)
        {
            var text = $"{start}V{turn}{end}";
            if (!SlidePathParser.TryParsePath($"{text}[8:1]", out var path) ||
                path.segments.Count != 1)
                continue;
            var resolved = SlideShapeResolver.TryResolve(
                path.segments[0], out var key, out _, out _);
            var legacy = LegacyShapeFromText(text);
            Assert(
                resolved == (legacy != null) && (!resolved || key == legacy),
                $"V shape resolution changed for {text}: " +
                $"{(resolved ? key : "rejected")} vs {legacy ?? "rejected"}");
            compared++;
        }

        Assert(compared > 500, $"Shape matrix only covered {compared} combinations.");

        // A D-zone head used to make the text scanners read "4d" as a number and
        // abort, so the same route resolved differently depending on the layer.
        foreach (var (dzone, plain) in new[]
                 {
                     ("4d-8", "4-8"), ("2d^8", "2^8"), ("1dV35", "1V35"),
                     ("3dpp7d", "3pp7"), ("5drq1", "5rq1")
                 })
        {
            Assert(
                SlidePathParser.TryParsePath($"{dzone}[8:1]", out var dzonePath) &&
                SlidePathParser.TryParsePath($"{plain}[8:1]", out var plainPath) &&
                SlideShapeResolver.TryResolve(
                    dzonePath.segments[0], out var dzoneKey, out _, out _) &&
                SlideShapeResolver.TryResolve(
                    plainPath.segments[0], out var plainKey, out _, out _) &&
                dzoneKey == plainKey,
                $"D-zone route {dzone} does not resolve like {plain}.");
            Assert(
                LegacyShapeFromText(dzone) == null,
                $"Oracle unexpectedly handled the D-zone form {dzone}.");
        }

        // Every issue the resolver reports must be a distinct, localizable reason.
        var issues = new HashSet<SlideShapeIssue>();
        foreach (var text in new[]
                 {
                     "1-2[8:1]", "1^5[8:1]", "1v5[8:1]",
                     "1s4[8:1]", "1V25[8:1]", "1V31[8:1]"
                 })
        {
            if (!SlidePathParser.TryParsePath(text, out var path) ||
                path.segments.Count != 1)
                continue;
            Assert(
                !SlideShapeResolver.TryResolve(
                    path.segments[0], out _, out var issue, out var error) &&
                issue != SlideShapeIssue.None &&
                !string.IsNullOrWhiteSpace(error),
                $"Rejected shape {text} lacks a reported reason.");
            SlideShapeResolver.TryResolve(path.segments[0], out _, out var kind, out _);
            issues.Add(kind);
        }

        Assert(issues.Count >= 3, "Shape rejections collapse into one reason.");

        // Guard against a layer growing its own copy of the grammar again.
        var editor = File.ReadAllText("MajdataEdit/MainWindowCore.cs");
        Assert(
            loader.Contains("SlideShapeResolver", StringComparison.Ordinal) &&
            editor.Contains("SlideShapeResolver", StringComparison.Ordinal),
            "A layer resolves slide shapes without the shared resolver.");
        foreach (var scan in new[]
                 {
                     "detectShapeFromText", "Split('>')", "Split('<')",
                     "Split('^')", "Split('v')"
                 })
            Assert(
                !loader.Contains(scan, StringComparison.Ordinal) &&
                !editor.Contains(scan, StringComparison.Ordinal),
                $"A shape grammar is being re-derived from text: {scan}");
    }

    // Mirror rewrites the chart as text, because it has to keep comments, spacing
    // and Alpha commands untouched. The AST is used here as a checker instead: a
    // mirrored Note must still parse, keep its kind, duration and modifiers, and
    // land on the mirrored positions.
    private static int MirroredPosition(
        int position, Mirror.HandleType type, bool innerRing)
    {
        static int Wrap(int value) => (value - 1 + 800) % 8 + 1;
        return type switch
        {
            Mirror.HandleType.LRMirror => innerRing
                ? Wrap(10 - position)
                : Wrap(9 - position),
            Mirror.HandleType.UDMirror => innerRing
                ? Wrap(6 - position)
                : Wrap(5 - position),
            Mirror.HandleType.HalfRotation => Wrap(position + 4),
            Mirror.HandleType.Rotation45 => Wrap(position + 1),
            Mirror.HandleType.CcwRotation45 => Wrap(position - 1),
            _ => position
        };
    }

    private static string MirroredShape(
        string shape, int startPosition, Mirror.HandleType type)
    {
        static string Flip(string value)
        {
            var flipped = new char[value.Length];
            for (var i = 0; i < value.Length; i++)
                flipped[i] = value[i] switch
                {
                    'p' => 'q',
                    'q' => 'p',
                    's' => 'z',
                    'z' => 's',
                    '<' => '>',
                    '>' => '<',
                    _ => value[i]
                };
            return new string(flipped);
        }

        switch (type)
        {
            case Mirror.HandleType.LRMirror:
            case Mirror.HandleType.UDMirror:
                return Flip(shape);
            case Mirror.HandleType.Rotation45 when startPosition is 2 or 6:
            case Mirror.HandleType.CcwRotation45 when startPosition is 3 or 7:
                // Simai's > and < mean "clockwise" and "counter-clockwise" only
                // from starts 7, 8, 1 and 2, so a rotation across that boundary
                // has to swap the mark to keep the drawn shape.
                return shape is "<" or ">" ? Flip(shape) : shape;
            default:
                return shape;
        }
    }

    // Anything an argument slot might be written as, valid or not. The pool is
    // deliberately shared by every command so a rule that only one side knows
    // shows up as a disagreement instead of as a silently dropped command.
    private static readonly string[] CommandValuePool =
    {
        "1", "0", "-1", "2.5", "-4.8", "4.8", "5", "-20", "20", "21", "0.5",
        "NULL", "null", "True", "FALSE", "false", "Instant", "Rewind", "Once",
        "8:1", "0:1", "4:0", "1:2:3",
        "FF6699", "#FF6699", "FF66", "FF6699AA", "GGGGGG",
        "Combo", "DxScore", "999",
        "media/a.ogg", "media/a.mp4", "/abs/a.ogg", "../a.ogg", "a.txt",
        "(1,2)", "(1,2,3)", "abc", "", " ", "NaN", "Infinity"
    };

    private static readonly string[] CommandTailPool =
    {
        "1", "0.5", "8:1", "True", "FALSE", "FF6699", "media/a.mp4", "abc", "",
        "NaN", "Infinity", "1e5", "(1,2)"
    };

    private static string ExpectedCommandTable(AlphaCommandDescriptor command)
    {
        switch (command.name)
        {
            case "SV": return "sv";
            case "HS": return "hs";
            case "SPAWN": return "spawn";
            case "SPAWNMODE": return "spawnmode";
            case "BOUNCE": return "bounce";
            case "DESTROY": return "destroy";
            case "FAKE": return "fake";
            case "COLOR":
            case "COLORV":
            case "JLINE": return "color";
            case "SIZE":
            case "SIZEV": return "size";
            case "ALPHA":
            case "ALPHAV": return "alpha";
            case "TEXT": return "subtitle";
            case "AUDIO":
            case "PVOVERLAY": return "media";
            default:
                return command.category == AlphaCommandCategory.Filter
                    ? "effect"
                    : "display";
        }
    }

    // Which timeline the runtime actually wrote the command into. Empty means the
    // command was dropped, which is what "the runtime rejected it" looks like.
    private static string RuntimeCommandTables(string token)
    {
        SimaiProcess.ClearData();
        SimaiProcess.Serialize("(120){4}<" + token + ">1,\nE");
        var touched = new List<string>();
        void Check(int count, string name)
        {
            if (count > 0)
                touched.Add(name);
        }

        Check(SimaiProcess.svTable.Count, "sv");
        Check(SimaiProcess.hsTable.Count, "hs");
        Check(SimaiProcess.spawnTable.Count, "spawn");
        Check(SimaiProcess.spawnModeTable.Count, "spawnmode");
        Check(SimaiProcess.bounceTable.Count, "bounce");
        Check(SimaiProcess.destroyTable.Count, "destroy");
        Check(SimaiProcess.fakeTable.Count, "fake");
        Check(SimaiProcess.colorTable.Count, "color");
        Check(SimaiProcess.sizeTable.Count, "size");
        Check(SimaiProcess.alphaTable.Count, "alpha");
        Check(SimaiProcess.displayTable.Count, "display");
        Check(SimaiProcess.subtitleTable.Count, "subtitle");
        Check(SimaiProcess.effectTable.Count, "effect");
        Check(SimaiProcess.mediaTable.Count, "media");
        return string.Join("+", touched);
    }

    private static void CollectCommandTokens(
        AlphaCommandDescriptor command, List<string> tokens, Random random)
    {
        foreach (var value in CommandValuePool)
        {
            tokens.Add(command.name + "*" + value);
            if (command.SupportsTargets)
            {
                tokens.Add(command.name + "*" + command.targets[0] + "=" + value);
                tokens.Add(command.name + "*slide=" + value);
                tokens.Add(command.name + "*slidestar=" + value);
                tokens.Add(command.name + "*bogus=" + value);
                tokens.Add(
                    command.name + "*" + command.targets[0] + "=" + value +
                    "," + command.targets[1] + "=1");
            }
            if (command.forms.Length > 0)
                tokens.Add(command.name + "*(" + value + ")");
        }

        if (command.forms.Length == 0)
            return;
        for (var length = 2; length <= 5; length++)
            for (var sample = 0; sample < 70; sample++)
            {
                var parts = new string[length];
                parts[0] = CommandValuePool[random.Next(CommandValuePool.Length)];
                for (var index = 1; index < length; index++)
                    parts[index] = CommandTailPool[random.Next(CommandTailPool.Length)];
                tokens.Add(command.name + "*(" + string.Join(",", parts) + ")");
            }
    }

    // The grammar decides what the editor reports and what the completion popup
    // offers, so anything it calls valid has to survive playback, and anything it
    // rejects must not quietly reach the timeline.
    private static void CheckAlphaCommandGrammar()
    {
        var random = new Random(20260819);
        var mismatches = new List<string>();
        var checkedTokens = 0;

        foreach (var command in AlphaCommandGrammar.Commands)
        {
            var tokens = new List<string>();
            CollectCommandTokens(command, tokens, random);
            foreach (var token in tokens)
            {
                var grammarOk = AlphaCommandGrammar.TryValidate(
                    token, 120f, out var error);
                var tables = RuntimeCommandTables(token);
                var runtimeOk = tables.Length > 0;
                checkedTokens++;
                // What the editor's error list says has to match, or a chart shows
                // a red line for a command that plays, or plays nothing quietly.
                var reported = SimaiProcess
                    .ValidateAlphaCommands("(120){4}<" + token + ">1,")
                    .Count > 0;
                if (reported == grammarOk)
                    mismatches.Add(
                        $"<{token}> grammar={(grammarOk ? "ok" : "reject")} " +
                        $"but the editor {(reported ? "reported" : "accepted")} it.");
                if (grammarOk != runtimeOk)
                    mismatches.Add(
                        $"<{token}> grammar={(grammarOk ? "ok" : "reject")} " +
                        $"runtime={(runtimeOk ? tables : "reject")} :: " +
                        error.Replace("\n", " | "));
                else if (grammarOk && tables != ExpectedCommandTable(command))
                    mismatches.Add(
                        $"<{token}> reached {tables} instead of " +
                        ExpectedCommandTable(command));
            }
        }

        if (mismatches.Count > 0)
        {
            File.WriteAllLines("alpha-command-mismatches.txt", mismatches);
            foreach (var line in mismatches.Take(40))
                Console.WriteLine(line);
        }
        Assert(
            mismatches.Count == 0,
            $"{mismatches.Count} of {checkedTokens} Alpha commands disagree between " +
            "the grammar and playback (see alpha-command-mismatches.txt).");

        // A misspelled name is stripped by the syntax check like any command, so if
        // the grammar stayed quiet about it the chart would look clean and play
        // without the effect.
        foreach (var token in new[] { "SVV*2", "SPAWNN*1", "COLORVV*FF0000", "SV2" })
            Assert(
                SimaiProcess.ValidateAlphaCommands("(120){4}<" + token + ">1,").Count > 0,
                $"<{token}> must be reported rather than silently ignored.");
    }

    // A command must precede this beat's notes. A misplaced command is reported and
    // rejected without leaking its '<' into the slide parser or damaging the notes.
    private static void CheckAlphaCommandPlacement()
    {
        void Check(string chart, bool reaches, bool reported)
        {
            SimaiProcess.Serialize(chart);
            var readable = chart.Replace("\n", "\\n");
            Assert(
                SimaiProcess.svTable.Count > 0 == reaches,
                $"{readable} must {(reaches ? "reach" : "not reach")} playback.");
            Assert(
                SimaiProcess.ValidateAlphaCommands(chart).Count > 0 == reported,
                $"{readable} must be {(reported ? "reported" : "accepted")} by the editor.");
        }

        Check("(120){4}<SV*2>1,2,", true, false);
        Check("(120){4}1,<SV*2>2,", true, false);
        Check("(120){4}1,\n<SV*2>2,", true, false);
        // A command after this beat's notes is rejected and reported.
        Check("(120){4}1\n<SV*2>,2,", false, true);
        Check("(120){4}1-5[8:1]\n<SV*2>,2,", false, true);
        Check("(120){4}1<SV*2>,2,", false, true);
        Check("(120){4}1-5[8:1]<SV*2>,2,", false, true);
        // A tempo or beat marker opens a fresh slot, so these do run.
        Check("(120){4}1\n(240)<SV*2>,2,", true, false);
        Check("(120){4}1\n{8}<SV*2>,2,", true, false);
    }

    // The hint popup builds its list from the grammar, so a new command always
    // appears; what can still go missing is its wording. AlphaCommandHints needs WPF
    // and cannot be linked here, so the three language tables are read as text.
    private static void CheckAlphaCommandHintCoverage()
    {
        var path = Path.Combine(
            RepositoryRoot(), "MajdataEdit", "Editor", "AlphaCommandHints.cs");
        Assert(File.Exists(path), "AlphaCommandHints.cs must exist for hint coverage.");
        var source = File.ReadAllText(path);
        var described = Regex
            .Matches(source, "new\\(\"(?<name>[A-Za-z]+)\",")
            .Select(match => match.Groups["name"].Value)
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(),
                StringComparer.OrdinalIgnoreCase);

        var missing = new List<string>();
        foreach (var command in AlphaCommandGrammar.Commands)
        {
            if (!described.TryGetValue(command.name, out var languages))
            {
                missing.Add(command.name + " (no wording at all)");
                continue;
            }
            // English, Japanese and Chinese each describe every command once.
            if (languages != 3)
                missing.Add($"{command.name} (described in {languages} of 3 languages)");
        }
        Assert(
            missing.Count == 0,
            "Alpha commands without complete hint wording: " + string.Join(", ", missing));
    }

    // A mine used to be a modifier that forced grey and nothing else, so a chart
    // could not say anything about mines on their own. It is a target now, and it is
    // read before break and each, because a mine is the thing the player must avoid
    // whatever note it happens to sit on.
    private static void CheckMineIsItsOwnTarget()
    {
        foreach (var token in new[]
                 {
                     "SV*mine=2", "HS*mine=1.5", "COLOR*mine=00FF00",
                     "COLORV*mine=00FF00", "SIZE*mine=1.2", "SIZEV*mine=1.2",
                     "ALPHA*mine=0.5", "ALPHAV*mine=0.5", "SPAWN*mine=3",
                     "SPAWNMODE*mine=Once", "DESTROY*mine=5", "BOUNCE*mine=0.2",
                     "FAKE*mine=TRUE",
                 })
        {
            Assert(
                AlphaCommandGrammar.TryValidate(token, 120f, out var error),
                $"<{token}> must be accepted: mines need a key of their own. {error}");
            Assert(
                SimaiProcess.ValidateAlphaCommands("(120){4}<" + token + ">1,").Count == 0,
                $"<{token}> must not be reported as an error by the editor either.");
            Assert(
                RuntimeCommandTables(token).Length > 0,
                $"<{token}> must reach a playback table, not parse and vanish.");
        }

        var view = File.ReadAllText("Assets/Scripts/JsonDataLoader.cs");
        var editor = File.ReadAllText("MajdataEdit/MainWindowCore.cs");

        // Order matters and is easy to lose: whoever adds the next resolver has to put
        // the mine lookup above the break one.
        foreach (var (side, source) in new[] { ("view", view), ("editor", editor) })
        {
            var mineLookups = Regex.Matches(source, "\"mine\"").Count;
            var breakLookups = Regex.Matches(source, "\"break\"").Count;
            Assert(
                mineLookups >= 5,
                $"The {side} reads \"mine\" only {mineLookups} times; every resolver " +
                "that special-cases break needs the same branch for mine.");
            Assert(
                source.IndexOf("\"mine\"", StringComparison.Ordinal) <
                source.IndexOf("\"break\"", StringComparison.Ordinal) ||
                mineLookups >= breakLookups - 2,
                $"The {side} must ask about mines before breaks in each resolver.");
        }

        // Grey is the default a mine falls back to, not a rule: a chart that names a
        // colour for mines has said what it wants.
        Assert(
            view.Contains("MineMaterial(", StringComparison.Ordinal) &&
            !Regex.IsMatch(
                view,
                @"isMine\w*\s*\?\s*CreateTintMaterial\(\s*\n?\s*null"),
            "No mine may go straight to grey any more; they all go through the one " +
            "helper that lets an explicit mine colour win.");
        Assert(
            view.Contains("GetMineColorAt(", StringComparison.Ordinal),
            "The mine colour must come from the mine key alone, not from the global " +
            "colour, which mines have always ignored.");

        // The appearance-time glyph was drawn in the mine grey, so a scrolled note's
        // spawn marker and a mine looked like the same thing on the waveform.
        Assert(
            editor.Contains("WaveSpawnMarkerColor", StringComparison.Ordinal),
            "The appearance-time marker needs a colour of its own.");
        var glyph = editor.Substring(editor.IndexOf(
            "private static void DrawScrollSpawnGlyph", StringComparison.Ordinal));
        glyph = glyph.Substring(0, glyph.IndexOf("\n    }", StringComparison.Ordinal));
        Assert(
            !glyph.Contains("WaveMineColor", StringComparison.Ordinal) &&
            Regex.Matches(glyph, "WaveSpawnMarkerColor").Count >= 6,
            "Every appearance-time glyph must use the marker colour, not the mine grey.");
        var marker = Regex.Match(
            editor,
            @"WaveSpawnMarkerColor\s*=\s*Color\.FromArgb\(\s*(\d+),\s*(\d+),\s*(\d+),\s*(\d+)\)");
        Assert(marker.Success, "The marker colour must be a plain ARGB value.");
        var alpha = int.Parse(marker.Groups[1].Value);
        var red = int.Parse(marker.Groups[2].Value);
        var green = int.Parse(marker.Groups[3].Value);
        var blue = int.Parse(marker.Groups[4].Value);
        // The waveform is cleared to near-black, so alpha here darkens rather than
        // lightens: a pale marker at low alpha comes out muddier than the mine grey
        // it was supposed to stop looking like.
        Assert(
            alpha >= 200,
            $"On a near-black waveform the marker has to stay mostly opaque to " +
            $"read as light; alpha {alpha} does not.");
        Assert(
            green > red + 40 && green > blue + 40,
            $"The marker is green; ({red},{green},{blue}) is not.");
        Assert(
            red > 120 && blue > 120,
            $"It is a pale green rather than a saturated one, so it sits behind " +
            $"the notes instead of competing; ({red},{green},{blue}) is not pale.");
    }

    // The side judgement panel is two fixed columns of eight lines, in boxes sized
    // for the font they were authored with. Unity's default is to truncate, so a
    // font whose lines are a hair taller loses the eighth line - the Late count -
    // and nothing says so.
    /// <summary>
    /// The spawn-crossing question is asked once per note per frame and answered by
    /// searching the chart from its start, so a chart with many scroll commands used
    /// to get slower the longer it played. The memo has to give the same answers as
    /// the search it replaces - including after the timeline is dragged backwards -
    /// and it has to actually cost less, or it is just more code.
    /// </summary>
    private static void CheckSpawnCrossingIsNotRescannedEveryFrame()
    {
        var random = new Random(20260822);
        var changes = new List<ScrollChange>();
        var at = 0d;
        for (var index = 0; index < 400; index++)
        {
            at += 0.05d + random.NextDouble() * 0.3d;
            changes.Add(new ScrollChange(
                at, (float)(random.NextDouble() * 4d - 1.5d), index));
        }
        var curve = AlphaVisualTiming.BuildScrollCurve(changes);
        var start = curve[0].Time;

        // Playing forward, dragged back, then forward again.
        var timeline = new List<double>();
        for (var frame = 0; frame < 700; frame++)
            timeline.Add(start + frame * 0.1d);
        timeline.Add(start + 4d);
        for (var frame = 0; frame < 300; frame++)
            timeline.Add(start + 4d + frame * 0.1d);

        var cases = new List<(double Scroll, float Speed)>();
        foreach (var speed in new[] { 1f, 7.5f, -3f })
        foreach (var scroll in new[] { 2d, 25d, 60d, 130d })
            cases.Add((scroll, speed));

        var scanned = Stopwatch.StartNew();
        var expected = new List<bool>();
        foreach (var (scroll, speed) in cases)
        foreach (var now in timeline)
            expected.Add(AlphaVisualTiming.HasEverCrossedSpawn(
                curve, start, now, scroll, speed,
                AlphaVisualTiming.DefaultSpawnRadius,
                AlphaVisualTiming.DefaultDestroyRadius));
        scanned.Stop();

        var remembered = Stopwatch.StartNew();
        var actual = new List<bool>();
        foreach (var (scroll, speed) in cases)
        {
            var memo = new SpawnCrossingMemo();
            foreach (var now in timeline)
                actual.Add(memo.HasEverCrossed(
                    curve, start, now, scroll, speed,
                    AlphaVisualTiming.DefaultSpawnRadius,
                    AlphaVisualTiming.DefaultDestroyRadius));
        }
        remembered.Stop();

        for (var index = 0; index < expected.Count; index++)
            Assert(
                expected[index] == actual[index],
                $"Remembering must not change the answer; frame {index} " +
                $"said {actual[index]} where the search says {expected[index]}.");
        Assert(
            expected.Contains(true) && expected.Contains(false),
            "This proves nothing unless the walk covers both answers.");
        Assert(
            remembered.Elapsed < scanned.Elapsed,
            $"The memo has to be the cheaper way to ask: {remembered.ElapsedMilliseconds}ms " +
            $"against {scanned.ElapsedMilliseconds}ms over {expected.Count} frames.");

        // A ring note also has to stop asking when the answer cannot matter: only
        // SPAWNMODE=once reads it, and it is the default mode that has to be free.
        foreach (var name in new[] { "NoteDrop.cs", "EachLineDrop.cs" })
        {
            var source = File.ReadAllText(Path.Combine(
                RepositoryRoot(), "Assets/Scripts/Notes", name));
            foreach (Match call in Regex.Matches(
                         source, @"SvController\.HasEverCrossedSpawn\(\s*"))
                Assert(
                    source.Substring(call.Index + call.Length)
                        .StartsWith("ref ", StringComparison.Ordinal),
                    $"{name} asks per frame, so it must ask with a memo.");
            Assert(
                Regex.IsMatch(
                    source,
                    @"SpawnVisualMode\.Once\s*&&\s*\r?\n?\s*SvController\.HasEverCrossedSpawn"),
                $"{name} must not search the curve for an answer only " +
                "SPAWNMODE=once reads.");
        }
    }

    /// <summary>
    /// Dragging the timeline back before a slide has to take its guide star off the
    /// screen. Every branch before the slide's own start only ever switched the star
    /// on, so a star the clock had already passed stayed where it was.
    /// </summary>
    private static void CheckReloadedViewDoesNotRetainChartState()
    {
        var loader = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "Assets/Scripts/JsonDataLoader.cs"));
        Assert(
            Regex.Matches(loader, @"ObjectCounter\.ResetForChart\(\);").Count == 2,
            "Both asynchronous playback loads and immediate recording loads must " +
            "reset judged and total object counts before counting the new chart.");

        var background = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "Assets/Scripts/UI/BGManager.cs"));
        var editor = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "MajdataEdit/MainWindowCore.cs"));
        Assert(
            editor.Contains("var viewHasPreviousChart =", StringComparison.Ordinal) &&
            editor.Contains("if (viewHasPreviousChart)\n            sendRequestStop();",
                StringComparison.Ordinal),
            "Changing folders must stop the previous View chart and clear its " +
            "paused seek state so the next request loads the new PV.");
        Assert(
            background.Contains("Mathf.Abs(innerEdge) * 2f", StringComparison.Ordinal),
            "Top and bottom masks must stop at the side-panel inner edges instead " +
            "of overlapping translucent black across the corners.");
        Assert(
            background.Contains(
                "if (Mathf.Abs(rect.anchoredPosition.x) < 1f)",
                StringComparison.Ordinal) &&
            background.Contains("image.gameObject.SetActive(false);",
                StringComparison.Ordinal),
            "Legacy scene top/bottom masks must be disabled before runtime masks " +
            "are added, otherwise their black alpha is composited twice.");

        var touch = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "Assets/Scripts/Notes/TouchDrop.cs"));
        var landed = touch.IndexOf("if (timing >= 0)", StringComparison.Ordinal);
        Assert(
            landed >= 0 &&
            !touch.Substring(landed, Math.Min(450, touch.Length - landed))
                .Contains("registerTouch(this)", StringComparison.Ordinal),
            "A landed Touch must not newly claim a multi-touch slot; 0.4.40 only " +
            "registered while the note was approaching, and late registration " +
            "creates overlap borders during direct playback.");
        var previewRetire = touch.IndexOf(
            "IsPausedTimelinePreview && timeProvider.AudioTime > time",
            StringComparison.Ordinal);
        Assert(
            previewRetire >= 0 &&
            touch.Substring(previewRetire,
                    Math.Min(550, touch.Length - previewRetire))
                .Contains("multTouchHandler.cancelTouch(this)",
                    StringComparison.Ordinal),
            "A Touch hidden after paused preview must release its overlap slot.");
    }

    private static void CheckGuideStarLeavesWhenTheTimelineGoesBack()
    {
        var slide = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "Assets/Scripts/Notes/SlideDrop.cs"));
        var beforeStart = slide.IndexOf(
            "var startiming = timeProvider.AudioTime - timeStart;",
            StringComparison.Ordinal);
        Assert(beforeStart > 0, "SlideDrop still decides its own fade-in start.");
        var switchedOn = slide.IndexOf(
            "star_slide.SetActive(true);", beforeStart, StringComparison.Ordinal);
        var switchedOff = slide.IndexOf(
            "star_slide.SetActive(false);", beforeStart, StringComparison.Ordinal);
        Assert(
            switchedOff > 0 && switchedOff < switchedOn,
            "The pre-start branch has to put the guide star away before the " +
            "running branch switches it on, or dragging back leaves it behind.");

        var wifi = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "Assets/Scripts/Notes/WifiDrop.cs"));
        Assert(
            wifi.Contains("now > timeStart &&", StringComparison.Ordinal),
            "Wifi decides the same thing from the clock; if that changed, the " +
            "two kinds have drifted apart again.");

        var head = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "Assets/Scripts/Notes/StarDrop.cs"));
        var rewind = head.IndexOf("ClockMovedBackwards()", StringComparison.Ordinal);
        var disableBody = head.IndexOf(
            "slide.SetActive(false);", rewind, StringComparison.Ordinal);
        var enableBody = head.IndexOf(
            "slide.SetActive(true);", rewind, StringComparison.Ordinal);
        Assert(
            rewind >= 0 && disableBody > rewind &&
            (enableBody < 0 || disableBody < enableBody),
            "Dragging before the appearance threshold must undo StarDrop's latched " +
            "slide activation; otherwise OnDisable never runs and its guide star " +
            "cannot disappear.");

        // StarDrop can disable the body before its next Update. All guide stars
        // are siblings under Notes, so every body must hide them in OnDisable.
        foreach (var path in new[]
                 {
                     "Assets/Scripts/Notes/SlideDrop.cs",
                     "Assets/Scripts/Notes/WifiDrop.cs",
                     "Assets/Scripts/Notes/TouchSlideDrop.cs"
                 })
        {
            var source = File.ReadAllText(Path.Combine(RepositoryRoot(), path));
            var onDisable = source.IndexOf("private void OnDisable()",
                StringComparison.Ordinal);
            Assert(
                onDisable >= 0 &&
                source.Substring(onDisable, Math.Min(650, source.Length - onDisable))
                    .Contains("SetActive(false)", StringComparison.Ordinal),
                $"{path} must hide its external guide star before rewind disables " +
                "the body and stops Update.");
        }
    }

    private static void CheckSideJudgeColumnsCannotBeCut()
    {
        var counter = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "Assets/Scripts/UI/ObjectCounter.cs"));
        Assert(
            counter.Contains("judgeResultText.verticalOverflow = VerticalWrapMode.Overflow;",
                StringComparison.Ordinal) &&
            counter.Contains("judgeResultText.horizontalOverflow = HorizontalWrapMode.Overflow;",
                StringComparison.Ordinal) &&
            counter.Contains("judgeResultCount.verticalOverflow = VerticalWrapMode.Overflow;",
                StringComparison.Ordinal) &&
            counter.Contains("judgeResultCount.horizontalOverflow = HorizontalWrapMode.Overflow;",
                StringComparison.Ordinal),
            "The side columns must not truncate or wrap, whatever font is chosen.");

        var line = Regex.Match(counter, @"judgeResultCount\.text = \$""([^""]+)""");
        Assert(line.Success, "The judgement column is written in one place.");
        var rows = line.Groups[1].Value.Split("\\n").Length;
        Assert(
            rows == 8,
            $"The column is eight rows with Late last; this writes {rows}. If the " +
            "shape changed, the box has to be checked against the new one.");
    }

    // Background clipping is one editor checkbox that has to reach a renderer in the
    // View: a field the editor saves, a field on both sides of the wire, a request
    // builder for each control the editor sends, a handler call, and the renderer.
    // Miss any one of those and the box does nothing, quietly.
    private static void CheckBackgroundClipIsWired()
    {
        var settings = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "MajdataEdit/Majson.cs"));
        Assert(
            settings.Contains("public bool ClipBackgroundToRing;", StringComparison.Ordinal),
            "The editor must save the setting.");
        Assert(
            settings.Contains("public bool clipBackgroundToRing;", StringComparison.Ordinal),
            "The editor's request must carry it.");
        Assert(
            File.ReadAllText(Path.Combine(RepositoryRoot(), "Assets/Scripts/Majson.cs"))
                .Contains("public bool clipBackgroundToRing;", StringComparison.Ordinal),
            "The View's request must spell it the same way, or it arrives false.");

        // Every builder that fills the background's own fit mode is a builder the
        // View reads background settings out of, so each one owes the clip too;
        // a builder that forgets it turns the clip off for that control.
        var core = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "MajdataEdit/MainWindowCore.cs"));
        var fitSites = Regex.Matches(core, @"backgroundFitMode\s*=[^;,]+[;,]").Count;
        var clipSites = Regex.Matches(core, @"clipBackgroundToRing\s*=[^;,]+[;,]").Count;
        Assert(
            clipSites == fitSites && fitSites >= 4,
            $"{fitSites} request(s) set the fit mode but {clipSites} set the clip.");

        Assert(
            File.ReadAllText(Path.Combine(RepositoryRoot(), "Assets/Scripts/HttpHandler.cs"))
                .Contains("bgManager.SetBackgroundClip(data.clipBackgroundToRing);",
                    StringComparison.Ordinal),
            "The View must apply it when the editor sends display settings.");

        var bg = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "Assets/Scripts/UI/BGManager.cs"));
        Assert(
            bg.Contains("public void SetBackgroundClip(bool clip)", StringComparison.Ordinal),
            "The renderer side must exist.");
        Assert(
            bg.Contains("clipObject.transform.SetParent(circleRev.transform, false);",
                StringComparison.Ordinal),
            "The clip must hang off the frame it copies, so ZOOM and MOVE carry it.");
        Assert(
            bg.Contains("backgroundClipRenderer.sortingLayerID = spriteRender.sortingLayerID;",
                StringComparison.Ordinal),
            "The clip belongs on the background's own layer; above it and the notes " +
            "out there would be hidden, which is the whole thing this must not do.");
    }

    // The editor and the View each declare their own copy of the objects that travel
    // between them as JSON. A field spelled differently on the two sides costs nothing
    // at build time and silently arrives as null: the View's beat text was called
    // "noteContent" against the editor's "notesContent" for as long as nothing read it.
    private static void CheckWireFormatMatches()
    {
        static Dictionary<string, (string Type, bool Ignored)> Fields(
            string path, string className)
        {
            var source = File.ReadAllText(Path.Combine(RepositoryRoot(), path));
            var start = source.IndexOf("class " + className, StringComparison.Ordinal);
            Assert(start >= 0, $"{className} must exist in {path}.");
            var open = source.IndexOf('{', start);
            var depth = 0;
            var end = open;
            while (end < source.Length)
            {
                if (source[end] == '{') depth++;
                else if (source[end] == '}' && --depth == 0) break;
                end++;
            }

            var body = source[open..end];
            var fields = new Dictionary<string, (string, bool)>(StringComparer.Ordinal);
            foreach (Match match in Regex.Matches(
                         body,
                         @"(?<attr>\[[^\]]*\]\s*)?public\s+(?<type>[\w\.\?<>,\[\] ]+?)\s+" +
                         @"(?<name>\w+)\s*(=[^;]*)?;"))
                fields[match.Groups["name"].Value] = (
                    match.Groups["type"].Value.Trim().Replace("?", string.Empty),
                    match.Groups["attr"].Value.Contains("JsonIgnore", StringComparison.Ordinal));
            return fields;
        }

        foreach (var className in new[] { "SimaiTimingPoint", "SimaiNote" })
        {
            var editor = Fields("MajdataEdit/SimaiProcess.cs", className);
            var view = Fields("Assets/Scripts/Majson.cs", className);
            var written = editor
                .Where(pair => !pair.Value.Ignored)
                .ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal);

            var dropped = written.Keys.Where(name => !view.ContainsKey(name)).ToList();
            Assert(
                dropped.Count == 0,
                $"{className}: the editor sends {string.Join(", ", dropped)} and the " +
                "View has nowhere to put it, so that field is thrown away in transit.");

            var unfilled = view.Keys.Where(name => !editor.ContainsKey(name)).ToList();
            Assert(
                unfilled.Count == 0,
                $"{className}: the View reads {string.Join(", ", unfilled)} and the " +
                "editor never writes it, so it is always the default.");

            var mistyped = written
                .Where(pair => view.TryGetValue(pair.Key, out var mine) &&
                               mine.Type != pair.Value)
                .Select(pair => $"{pair.Key} ({pair.Value} vs {view[pair.Key].Type})")
                .ToList();
            Assert(
                mistyped.Count == 0,
                $"{className}: same name, different type across the wire: " +
                string.Join(", ", mistyped));

            Assert(
                written.Count >= 12,
                $"{className}: only {written.Count} fields were found, so the reader " +
                "above stopped matching the source and is no longer checking anything.");
        }
    }

    private static string RepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (directory != null && !Directory.Exists(Path.Combine(directory, "MajdataEdit")))
            directory = Path.GetDirectoryName(directory);
        Assert(directory != null, "The repository root must be reachable from the test binary.");
        return directory!;
    }

    private static void CheckMirrorAgainstAst()
    {
        var corpus = new List<string>
        {
            "1", "5", "8", "1b", "1x", "1f", "1$", "1$$", "1d", "8d",
            "1h[4:1]", "1hb[8:1]", "1h", "C", "Ch[4:1]", "E1~[4.8]",
            "A1", "A5", "B3", "B7", "C1", "D1", "D4", "E2", "E6",
            "A1-E5[8:1]", "A1pE5[8:1]", "1-5[8:1]-8[8:1]", "1-5[8:1]*-7[8:1]"
        };
        string?[] shapes =
        {
            "-", ">", "<", "^", "v", "p", "q", "pp", "qq",
            "rp", "rq", "s", "z", "w"
        };
        foreach (var shape in shapes)
        for (var start = 1; start <= 8; start++)
        for (var end = 1; end <= 8; end++)
        {
            var text = $"{start}{shape}{end}[8:1]";
            if (IsValid(text))
                corpus.Add(text);
        }
        for (var start = 1; start <= 8; start++)
        for (var turn = 1; turn <= 8; turn++)
        for (var end = 1; end <= 8; end++)
        {
            var text = $"{start}V{turn}{end}[8:1]";
            if (IsValid(text))
                corpus.Add(text);
        }

        var checked_ = 0;
        foreach (var type in Enum.GetValues<Mirror.HandleType>())
        foreach (var source in corpus)
        {
            if (source.IndexOf('*') >= 0)
            {
                // A same-head group is mirrored as a whole; its branches are
                // checked through the runtime splitter instead.
                var mirroredGroup = Mirror.NoteMirrorHandle(source, type);
                Assert(
                    NoteSlotParser.TrySplit(mirroredGroup, out var branches, out _) &&
                    branches.TrueForAll(branch =>
                        NoteExpressionParser.TryParse(branch.text, out _, out _)),
                    $"Mirroring {source} as {type} produced unparseable branches: " +
                    mirroredGroup);
                checked_++;
                continue;
            }

            Assert(
                NoteExpressionParser.TryParse(source, out var before, out _),
                $"Mirror fixture {source} does not parse.");
            var mirrored = Mirror.NoteMirrorHandle(source, type);
            Assert(
                NoteExpressionParser.TryParse(mirrored, out var after, out var error),
                $"Mirroring {source} as {type} produced {mirrored}: {error}");
            Assert(
                after.kind == before.kind &&
                after.duration == before.duration &&
                after.isZeroLengthHold == before.isZeroLengthHold &&
                after.modifiers.Head == before.modifiers.Head &&
                after.modifiers.Slide == before.modifiers.Slide,
                $"Mirroring {source} as {type} changed what the Note is: {mirrored}");
            Assert(
                after.position.area == before.position.area &&
                after.position.isDZone == before.position.isDZone &&
                Math.Abs(after.position.radius - before.position.radius) < 1e-6,
                $"Mirroring {source} as {type} changed its area: {mirrored}");

            var innerRing = before.position.isDZone ||
                            before.position.area is 'D' or 'E';
            if (before.position.area != 'C')
                Assert(
                    after.position.position ==
                    MirroredPosition(before.position.position, type, innerRing),
                    $"Mirroring {source} as {type} moved the head to " +
                    $"{after.position.position}: {mirrored}");
            else
                Assert(
                    after.position.position == before.position.position,
                    $"Mirroring {source} as {type} rotated the center: {mirrored}");

            if (before.kind == NoteExpressionKind.Slide)
            {
                Assert(
                    after.path.segments.Count == before.path.segments.Count,
                    $"Mirroring {source} as {type} changed the segment count.");
                for (var i = 0; i < before.path.segments.Count; i++)
                {
                    var wasSegment = before.path.segments[i];
                    var isSegment = after.path.segments[i];
                    Assert(
                        isSegment.shape == MirroredShape(
                            wasSegment.shape, wasSegment.startPosition, type),
                        $"Mirroring {source} as {type} drew {isSegment.shape} " +
                        $"instead of {wasSegment.shape}: {mirrored}");
                    Assert(
                        isSegment.duration == wasSegment.duration,
                        $"Mirroring {source} as {type} changed a duration: {mirrored}");
                    var endInner = wasSegment.endIsDZone ||
                                   wasSegment.end.area is 'D' or 'E';
                    if (wasSegment.end.area != 'C')
                        Assert(
                            isSegment.endPosition == MirroredPosition(
                                wasSegment.endPosition, type, endInner),
                            $"Mirroring {source} as {type} moved the tail to " +
                            $"{isSegment.endPosition}: {mirrored}");
                }
            }

            checked_++;
        }

        Assert(checked_ > 1000, $"Mirror only checked {checked_} notes.");
    }

    private static readonly System.Text.RegularExpressions.Regex LegacyDurationTarget =
        new(
            @"(?i)(?:[1-8][bxfm]*h[bxfm]*|[ABCDE](?:[1-8])?[bfx]*h[bfx]*|(?:[1-8]d?|[ABDE][1-8]|C1?)[bxfm!?]*(?:(?:<{2,}|>{2,}|pp|qq|[-<>^vpqsz])(?:[1-8]d?|[ABDE][1-8]|C1?)[bxfm]*|V(?:[1-8]d?|[ABDE][1-8]|C1?){2})+|[1-8]d?[bxfm]*(?:(?:pp|qq|rp|rq)[1-8]d?|V[1-8]d?[1-8]d?|[-<>^vpqszw][1-8]d?)[bxfm]*|(?:(?:pp|qq|rp|rq)[1-8]d?|V[1-8]d?[1-8]d?|[-<>^vpqszw][1-8]d?))$",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static void CheckDurationCompletion()
    {
        // The popup offers a duration exactly when the note grammar still wants
        // one, and hides the instant form [1:0] for Slides.
        foreach (var (typed, wanted, slide) in new[]
                 {
                     ("1", false, false), ("1b", false, false),
                     ("A1", false, false), ("C", false, false),
                     ("1h", true, false), ("1bh", true, false),
                     ("1hx", true, false), ("Ch", true, false),
                     ("E1h", true, false), ("1d", false, false),
                     ("1-5", true, true), ("1-5b", true, true),
                     ("1V35", true, true), ("1pp5", true, true),
                     ("1w5", true, true), ("1rp5", true, true),
                     ("1d-5d", true, true), ("A1-E5", true, true),
                     ("1!-5", true, true), ("1?-5", true, true),
                     ("1-5[8:1]-8", true, true),
                     ("1-5[8:1]*-7", true, true),
                     ("{8}1-5", true, true), (",1h", true, false),
                     ("(120){8}1h", true, false),
                     ("1-5[8:1]", false, false), ("1h[8:1]", false, false),
                     ("E1~[4.8]", false, false),
                     ("1-", false, false), ("1V3", false, false),
                     ("", false, false), ("<SV*", false, false)
                 })
        {
            var found = NoteDurationTarget.TryFromTypedText(typed, out var isSlide);
            Assert(
                found == wanted && (!found || isSlide == slide),
                $"Duration completion for \"{typed}\" is " +
                $"{(found ? isSlide ? "slide" : "hold" : "none")}, expected " +
                $"{(wanted ? slide ? "slide" : "hold" : "none")}.");
        }

        // Whatever the old pattern recognized must still be recognized, so the
        // popup did not quietly stop appearing where authors are used to it.
        var carried = 0;
        string?[] shapes =
        {
            null, "-", "^", "v", ">", "<", "p", "q", "pp", "qq",
            "rp", "rq", "s", "z", "w", "V3"
        };
        foreach (var head in new[] { "1", "5", "1d", "A1", "C", "E4" })
        foreach (var modifier in new[] { "", "b", "x", "f", "bx" })
        foreach (var shape in shapes)
        for (var end = 1; end <= 8; end++)
        {
            var typed = shape == null
                ? $"{head}{modifier}h"
                : $"{head}{modifier}{shape}{end}";
            if (!LegacyDurationTarget.IsMatch(typed))
                continue;
            // The old pattern also matched text that cannot become a Note, such
            // as the zero-length "1-1"; not offering a duration there is the
            // point of using the grammar.
            if (!NoteExpressionParser.TryParse($"{typed}[8:1]", out _, out _))
                continue;
            Assert(
                NoteDurationTarget.TryFromTypedText(typed, out _),
                $"Duration completion stopped appearing for \"{typed}\".");
            carried++;
        }

        Assert(carried > 200, $"Only {carried} legacy completion cases ran.");

        // Selecting a whole note offers its duration; selecting several does not.
        foreach (var (selection, wanted) in new[]
                 {
                     ("1h", true), ("1-5", true), ("1-5[8:1]", false),
                     ("1", false), ("1h/2h", false), ("1h,2h", false),
                     (" 1-5 ", true), ("", false)
                 })
            Assert(
                NoteDurationTarget.TryFromSelection(selection, out _) == wanted,
                $"Duration completion for the selection \"{selection}\" is wrong.");

        var source = File.ReadAllText("MajdataEdit/Editor/AlphaCommandHints.cs");
        Assert(
            !source.Contains("DurationTargetPattern", StringComparison.Ordinal) &&
            source.Contains("NoteDurationTarget", StringComparison.Ordinal),
            "The duration popup keeps its own copy of the note grammar.");
    }

    // View's own rule, from JsonDataLoader.CountNoteSum: one combo per Note plus
    // the Slide's guide star when it has one. The chart library's statistics have
    // to agree with what the game will show.
    private static int RuntimeCombo(string slot)
    {
        var timing = new SimaiTimingPoint(1d, _content: slot, bpm: 120f);
        var total = 0;
        foreach (var note in timing.getNotes())
        {
            if (note.noteType != SimaiNoteType.Slide)
            {
                total++;
                continue;
            }
            if (!note.isSlideNoHead)
                total++;
            total++;
        }
        Assert(
            timing.noteParseError == null,
            $"Combo fixture {slot} does not parse: {timing.noteParseError}");
        return total;
    }

    private static void CheckChartStatistics()
    {
        foreach (var slot in new[]
                 {
                     "1", "12", "1h[4:1]", "1b", "1x", "1d",
                     "1/2/3", "C", "A1", "Ch[4:1]",
                     "1-5[8:1]", "1-5[8:1]/3", "1?-5[8:1]", "1!-5[8:1]",
                     "1-5[8:1]-8[8:1]", "1-5-8[8:1]",
                     "1-5[8:1]*-7[8:1]", "1-5[8:1]*-7[8:1]*-3[8:1]",
                     "A1-E5[8:1]", "1V35[8:1]", "1w5[8:1]", "1-5b[8:1]",
                     "1f/2h[8:1]/3-7[8:1]"
                 })
        {
            var expected = RuntimeCombo(slot);
            var actual = ChartRhythmSearchEngine.CountComboToken(slot);
            Assert(
                actual == expected,
                $"Chart library counts {actual} combo for {slot}, " +
                $"the game counts {expected}.");
        }

        // A slot holding only an Alpha command is not a Note slot, and its name
        // and arguments must not be read as positions either.
        foreach (var command in new[]
                 {
                     "<SV*slide=1.5>", "<HS*2>", "<COLOR*slide=FF00AA>",
                     "<ALPHA*touch=0.5>", "<Shake*(1,2,8:1)>", "<FAKE*slide=1>"
                 })
        {
            Assert(
                ChartRhythmSearchEngine.CountComboToken(command) == 0 &&
                !ChartRhythmSearchEngine.ChartSlotHasNote(command) &&
                ChartRhythmSearchEngine.GetChartPositions(command).Count == 0,
                $"Alpha command {command} counts as a Note in the chart library.");
            Assert(
                ChartRhythmSearchEngine.CountCombo($"1,{command},2") == 2,
                $"Notes around {command} count " +
                $"{ChartRhythmSearchEngine.CountCombo($"1,{command},2")} combo.");
            // A command's own commas must not become slot separators, otherwise
            // every Note after it lands on the wrong beat.
            Assert(
                ChartRhythmSearchEngine.SplitSlots($"1,{command},2").Count == 3,
                $"Command {command} was split into " +
                $"{ChartRhythmSearchEngine.SplitSlots($"1,{command},2").Count} slots.");
        }

        // Positions drive the exact-match search, so they have to follow the same
        // splitting the runtime uses.
        foreach (var (slot, expected) in new (string, int[])[]
                 {
                     ("12", new[] { 1, 2 }),
                     ("1/5", new[] { 1, 5 }),
                     ("1d", new[] { 1 }),
                     ("1-5[8:1]", new[] { 1 }),
                     ("1-5[8:1]*-7[8:1]", new[] { 1 }),
                     ("C", new[] { -1 }),
                     ("A1/3", new[] { -1, 3 })
                 })
            Assert(
                ChartRhythmSearchEngine.GetChartPositions(slot)
                    .SetEquals(expected),
                $"Chart positions for {slot} are " +
                string.Join(
                    ",",
                    ChartRhythmSearchEngine.GetChartPositions(slot)));

        // Stars are the key-position Slides in the window.
        foreach (var (text, expected) in new[]
                 {
                     ("1-5[8:1]", 1), ("1-5[8:1]*-7[8:1]", 2),
                     ("1-5[8:1],3-7[8:1]", 2), ("A1-E5[8:1]", 0),
                     ("1h[4:1],1", 0), ("1-5-8[8:1]", 1)
                 })
            Assert(
                ChartRhythmSearchEngine.CountStars(text) == expected,
                $"Star count for {text} is " +
                $"{ChartRhythmSearchEngine.CountStars(text)}, expected {expected}.");

        // Guard against the third grammar coming back.
        var source = File.ReadAllText("MajdataEdit/ChartRhythmSearchEngine.cs");
        Assert(
            !source.Contains("\"bxf!\"", StringComparison.Ordinal) &&
            !source.Contains("SlideTypes", StringComparison.Ordinal) &&
            source.Contains("NoteSlotParser", StringComparison.Ordinal),
            "The chart library reads notes with its own grammar again.");
    }

    private static readonly System.Text.RegularExpressions.Regex LegacyMuriSlide =
        new(@"(\d)(.+?)(\d{1,2})\[.+?\]");

    private static int LegacyNotePos(int pos, bool relative)
    {
        if (pos <= 0) pos += 8;
        if (relative)
            pos %= 8;
        else
            pos = (pos - 1) % 8 + 1;
        return pos;
    }

    // The muri check used to pull the Slide's start, shape and end out of the
    // Note text with this regex. It is kept as an oracle: wherever it could read
    // a Slide the AST-driven detector has to agree with it exactly, and the
    // forms it could not read must now be handled instead of reported as chart
    // errors.
    private static List<(double Time, int Area)>? LegacyMuriSlideOps(
        SimaiNote note, MuriSlideTimeTable table)
    {
        var match = LegacyMuriSlide.Match(note.noteContent!);
        if (!match.Success)
            return null;
        var shape = match.Groups[2].Value;
        var endText = match.Groups[3].Value;
        if (!int.TryParse(match.Groups[1].Value, out var start))
            return null;

        string offsetKey;
        if (shape == "V")
        {
            if (endText.Length != 2)
                return null;
            offsetKey =
                LegacyNotePos(int.Parse(endText[..1]) - start, true) + "," +
                LegacyNotePos(int.Parse(endText[1..]) - start, true);
        }
        else
        {
            if (!int.TryParse(endText, out var end))
                return null;
            offsetKey = LegacyNotePos(end - start, true).ToString();
        }

        if (shape == ">" && start is >= 3 and <= 6)
            shape = "<";
        else if (shape == "<" && start is >= 3 and <= 6)
            shape = ">";

        if (!table.TryGetPassAreas(shape, offsetKey, out var passAreas))
            return null;

        var ops = new List<(double Time, int Area)>();
        foreach (var passArea in passAreas)
            ops.Add((
                passArea.Time * note.slideTime + note.slideStartTime,
                LegacyNotePos(passArea.Area + start, false)));
        return ops;
    }

    private static MuriDetector MuriFor(
        MuriSlideTimeTable table, params (double Time, string Content)[] groups)
    {
        var notelist = new List<SimaiTimingPoint>();
        foreach (var group in groups)
            notelist.Add(new SimaiTimingPoint(
                group.Time, _content: group.Content, bpm: 120f));
        return new MuriDetector(notelist, table);
    }

    private static List<(double Time, int Area)> MuriPassAreas(MuriDetector detector)
    {
        var ops = detector.BuildSlideOperations();
        Assert(ops != null, "Muri detector rejected a supported chart.");
        return ops!
            .Where(op => op.ntype == 1)
            .Select(op => (op.time, op.area))
            .ToList();
    }

    private static void CheckMuriDetector()
    {
        Assert(
            MuriSlideTimeTable.TryLoad(
                File.ReadAllText("MajdataEdit/slide_time.json"),
                out var loaded,
                out var loadError),
            $"Muri measurements failed to load: {loadError}");
        var table = loaded!;

        string?[] shapes =
        {
            "-", ">", "<", "^", "v", "p", "q", "pp", "qq",
            "rp", "rq", "s", "z", "w"
        };

        var agreed = 0;
        var unmeasured = 0;
        var texts = new List<string>();
        foreach (var shape in shapes)
        for (var start = 1; start <= 8; start++)
        for (var end = 1; end <= 8; end++)
            texts.Add($"{start}{shape}{end}[8:1]");
        for (var start = 1; start <= 8; start++)
        for (var turn = 1; turn <= 8; turn++)
        for (var end = 1; end <= 8; end++)
            texts.Add($"{start}V{turn}{end}[8:1]");

        foreach (var text in texts)
        {
            if (!IsValid(text))
                continue;
            var note = new SimaiTimingPoint(0d, _content: text, bpm: 120f)
                .getNotes()[0];
            var detector = MuriFor(table, (0d, text));
            var actual = MuriPassAreas(detector);
            var oracle = LegacyMuriSlideOps(note, table);

            if (oracle != null)
            {
                Assert(
                    actual.Count == oracle.Count,
                    $"Muri pass-area count changed for {text}: " +
                    $"{actual.Count} vs {oracle.Count}");
                for (var i = 0; i < actual.Count; i++)
                    Assert(
                        Math.Abs(actual[i].Time - oracle[i].Time) < 1e-9 &&
                        actual[i].Area == oracle[i].Area,
                        $"Muri pass area changed for {text}: " +
                        $"{actual[i]} vs {oracle[i]}");
                Assert(
                    detector.Warnings.Count == 0,
                    $"Measured Slide {text} produced a warning.");
                agreed++;
            }
            else
            {
                // Nothing measured for this shape: it has to be skipped with its
                // own reason, never reported as a syntax error.
                Assert(
                    actual.Count == 0 &&
                    detector.Warnings.Count == 1 &&
                    detector.Warnings[0].Content == "MuriUnmeasuredSlide",
                    $"Unmeasured Slide {text} was not reported as unmeasured.");
                unmeasured++;
            }
        }

        Assert(agreed > 100, $"Muri oracle only agreed on {agreed} Slides.");
        Assert(unmeasured > 0, "No unmeasured Slide shape was exercised.");

        // Modifiers and D-zone suffixes never reached the old text scanning,
        // because it read the normalized content. Reading the AST instead must
        // not change what those Notes contribute.
        foreach (var text in new[]
                 {
                     "1-5b[8:1]", "1d-5d[8:1]", "1x-5[8:1]",
                     "1!-5[8:1]", "1?-5[8:1]", "1f-5[8:1]", "1-5[8:1]b"
                 })
        {
            Assert(IsValid(text), $"Fixture {text} is not a valid Slide.");
            var note = new SimaiTimingPoint(0d, _content: text, bpm: 120f)
                .getNotes()[0];
            var oracle = LegacyMuriSlideOps(note, table);
            var actual = MuriPassAreas(MuriFor(table, (0d, text)));
            Assert(
                oracle != null &&
                actual.Count == oracle.Count &&
                actual.Zip(oracle).All(pair =>
                    Math.Abs(pair.First.Time - pair.Second.Time) < 1e-9 &&
                    pair.First.Area == pair.Second.Area),
                $"Modified Slide {text} changed its muri contribution.");
        }

        // Connected Slides are what the old text scanning could not read. With one
        // total duration it took "-5-" for the shape and reported a syntax error
        // on a chart the runtime plays; with per-segment durations it stopped at
        // the first segment and never checked the rest.
        foreach (var (text, passAreaCount) in new[]
                 {
                     ("1-5[8:1]-8[8:1]", 2), ("1-5-8[8:1]", 2),
                     ("1-5[8:1]-8[8:1]-3[8:1]", 3)
                 })
        {
            Assert(IsValid(text), $"Fixture {text} is not a valid Slide.");
            var note = new SimaiTimingPoint(0d, _content: text, bpm: 120f)
                .getNotes()[0];
            var oracle = LegacyMuriSlideOps(note, table);
            Assert(
                oracle == null || oracle.Count < passAreaCount,
                $"Oracle already covered {text}; it is no longer a fix case.");
            var detector = MuriFor(table, (0d, text));
            var actual = MuriPassAreas(detector);
            Assert(
                detector.Warnings.Count == 0,
                $"Valid Slide {text} still warns in the muri check.");
            Assert(
                actual.Count == passAreaCount,
                $"Connected Slide {text} covered {actual.Count} pass areas " +
                $"instead of {passAreaCount}.");
            Assert(
                actual.All(op =>
                    op.Time >= note.slideStartTime - 1e-9 &&
                    op.Time <= note.slideStartTime + note.slideTime + 1e-9),
                $"Muri pass areas for {text} fall outside the Slide's travel.");
        }

        // Each segment of a connected Slide owns its own part of the travel, so
        // the later segments are checked at the time they are actually played.
        var chain = MuriPassAreas(MuriFor(table, (0d, "1-5[8:1]-8[8:1]")));
        var chainNote = new SimaiTimingPoint(
            0d, _content: "1-5[8:1]-8[8:1]", bpm: 120f).getNotes()[0];
        var half = chainNote.slideStartTime + chainNote.slideTime / 2;
        Assert(
            chain.Count(op => op.Time < half) == 1 &&
            chain.Count(op => op.Time > half) == 1,
            "Connected Slide segments share one time window.");
        Assert(
            chain[0].Area == 5 && chain[1].Area == 8,
            $"Connected Slide pass areas are wrong: " +
            $"{chain[0].Area}, {chain[1].Area}");

        // The hand ends where the last segment ends; the old scanning read that
        // from the text and could not follow a chain.
        var multNote = MuriFor(table, (0d, "1-5-8[8:1]")).BuildMultNoteOperations();
        Assert(multNote != null, "Muri detector rejected a connected Slide.");
        var body = multNote!.Single(op => op.ntype == 3);
        Assert(
            body.startArea == 1 && body.endArea == 8,
            $"Connected Slide hand travel is {body.startArea}->{body.endArea}.");

        // Collision detection itself is unchanged: the tail of 1-5 crosses area 5
        // at 84% of its travel, so a Tap there right after it still collides.
        var slideNote = new SimaiTimingPoint(0d, _content: "1-5[8:1]", bpm: 120f)
            .getNotes()[0];
        var tailTime = slideNote.slideStartTime + 0.84112 * slideNote.slideTime;
        var collision = MuriFor(table, (0d, "1-5[8:1]"), (tailTime + 0.05, "5"));
        Assert(
            collision.DetectSlide(0.15) == 1 &&
            collision.Warnings.Count == 1 &&
            collision.Warnings[0].Content == "SlideError",
            "A Tap on a Slide's tail area is no longer reported.");
        var clear = MuriFor(table, (0d, "1-5[8:1]"), (tailTime + 0.5, "5"));
        Assert(
            clear.DetectSlide(0.15) == 0 && clear.Warnings.Count == 0,
            "A Tap well after the Slide tail is reported as a collision.");

        // Touch is still out of scope, and saying so must not depend on how the
        // Note text happens to be spelled.
        Assert(
            MuriFor(table, (0d, "C")).BuildSlideOperations() == null &&
            MuriFor(table, (0d, "A1")).BuildMultNoteOperations() == null,
            "The muri check silently accepted a Touch chart.");
        Assert(
            MuriPassAreas(MuriFor(table, (0d, "A1-E5[8:1]"))).Count == 0,
            "Touch Slides must stay out of the muri check.");

        // Guard against the text scanning coming back.
        var detectorSource = File.ReadAllText("MajdataEdit/SubWindow/MuriDetector.cs");
        var uiSource = File.ReadAllText("MajdataEdit/SubWindow/MuriCheck.xaml.cs");
        Assert(
            !detectorSource.Contains("Regex", StringComparison.Ordinal) &&
            !uiSource.Contains("Regex", StringComparison.Ordinal) &&
            detectorSource.Contains("note.slidePath", StringComparison.Ordinal),
            "The muri check reads Slides from text again.");
    }

    // Everything the editor greenlights must survive the gate View builds notes
    // through: shared parse, shared validation, a drawable prefab, and a duration
    // the runtime can turn into seconds.
    private static bool PassesPlayGate(string note)
    {
        // Runtime splits a timing slot the same way before it looks at a note.
        if (note.Contains('/', StringComparison.Ordinal))
            return note.Split('/').All(PassesPlayGate);
        if (note.Contains('*', StringComparison.Ordinal))
            return SlidePathParser.TryExpandSameHead(note, out var branches) &&
                   branches.All(PassesPlayGate);
        if (note.Length == 2 &&
            int.TryParse(note, out var pair) &&
            pair is >= 11 and <= 88)
            return true;
        if (!NoteExpressionParser.TryParse(note, out var expression, out _))
            return false;
        if (expression.kind != NoteExpressionKind.Slide || expression.isTouchPath)
            return true;
        if (!SlidePathParser.TryParsePath(expression.path.source, out var path))
            return false;
        if (!SlideSyntaxValidator.TryValidate(path, out _))
            return false;

        var durationCount = 0;
        foreach (var segment in path.segments)
        {
            if (!string.IsNullOrEmpty(segment.duration))
            {
                durationCount++;
                if (!SlideSyntaxValidator.TryGetLengthSeconds(
                        segment.duration, 120f, out _))
                    return false;
            }
            if (!SlideShapeResolver.TryResolve(segment, out var key, out _, out _))
                return false;
            if (!SlideShapeResolver.IsPrefabKeySupported(key))
                return false;
        }

        if (durationCount != 1 && durationCount != path.segments.Count)
            return false;
        return !(path.segments.Any(segment => segment.shape == "w") &&
                 path.segments.Count != 1);
    }

    private static void CheckLayerAgreement()
    {
        static bool RuntimeAccepts(string note)
        {
            var timing = new SimaiTimingPoint(1d, _content: note, bpm: 120f);
            var notes = timing.getNotes();
            return timing.noteParseError == null && notes.Count > 0;
        }

        static bool EditorAccepts(string note)
            => EditorErrorCount($"(120){{4}}{note},E") == 0;

        var corpus = new List<string>();
        string[] shapes = { "-", ">", "<", "^", "v", "p", "q", "pp", "qq", "rp", "rq", "s", "z", "w" };
        foreach (var shape in shapes)
        {
            for (var end = 1; end <= 8; end++)
            {
                corpus.Add($"1{shape}{end}[8:1]");
                corpus.Add($"1{shape}{end}");
                corpus.Add($"1d{shape}{end}d[8:1]");
                corpus.Add($"1b{shape}{end}m[8:1]");
            }
        }

        for (var turn = 1; turn <= 8; turn++)
        {
            corpus.Add($"1V{turn}5[8:1]");
            corpus.Add($"1V{turn}3[8:1]");
        }

        foreach (var area in new[] { "A", "B", "C", "D", "E" })
        {
            corpus.Add(area == "C" ? "C" : $"{area}1");
            corpus.Add(area == "C" ? "Ch[4:1]" : $"{area}1h[4:1]");
            corpus.Add($"{area}1f");
            corpus.Add($"{area}1-E5[8:1]");
        }

        corpus.AddRange(new[]
        {
            "1", "1b", "1x", "1f", "1bf", "1h[4:1]", "1bh[4:1]", "1h",
            "4[12:1]", "1[8:1]", "1$", "1$$", "1?", "1!",
            "1-5[8:1]*-3[8:1]", "1-5[8:1]*p3[8:1]", "1*-5[8:1]",
            "1-5[8:1]-7[8:1]", "1-5[8:1]-7", "1-5[3##8:1]-7",
            "1/2", "1/2/3", "12", "1-5[8:1]/3", "E1~[4.8]", "E1~[48]",
            "1~[4]", "Ch~[3]", "A1~[3]-A5[8:1]", "1-5[#2]", "1-5[160#8:1]",
            "1-5[3##1.5]", "1-5[8:1]b", "1-5b[8:1]", "1-5?[8:1]",
            "1!-5[8:1]", "1?-5[8:1]", "1w5-3[8:1]", "1<<5[8:1]",
            "A1<<E5[8:1]", "A1<<<E5[8:1]", "A1VCE2[8:1]", "1V25[8:1]"
        });

        // The legacy shorthand is a real slot form, so every layer has to know it:
        // the runtime read it as two taps while the editor called it a syntax error.
        Assert(
            NoteSlotParser.TrySplit("12", out var shorthand, out _) &&
            shorthand.Count == 2 &&
            shorthand[0].text == "1" && shorthand[1].text == "2",
            "Two-key shorthand does not split into two notes.");
        Assert(
            NotePreviewModule.ExpandPreview("12").Contains("1/2"),
            "Two-key shorthand does not preview as two notes.");
        var shorthandNotes = new SimaiTimingPoint(1d, _content: "12", bpm: 120f)
            .getNotes();
        Assert(
            shorthandNotes.Count == 2 &&
            shorthandNotes.All(note => note.noteType == SimaiNoteType.Tap),
            "Two-key shorthand does not play as two taps.");

        // A same-head group is one authored note: a broken branch may not leave the
        // rest of the group on the playfield.
        var brokenGroup = new SimaiTimingPoint(
            1d, _content: "1*-5[8:1]/3", bpm: 120f);
        var brokenNotes = brokenGroup.getNotes();
        Assert(
            brokenNotes.Count == 1 &&
            brokenNotes[0].noteType == SimaiNoteType.Tap &&
            !string.IsNullOrWhiteSpace(brokenGroup.noteParseError),
            "A broken same-head group must drop as a whole and keep its siblings.");

        var checkedNotes = 0;
        foreach (var note in corpus.Distinct())
        {
            var runtime = RuntimeAccepts(note);
            var editor = EditorAccepts(note);
            Assert(
                runtime == editor,
                $"Editor and runtime disagree on {note}: " +
                $"editor={(editor ? "ok" : "error")}, runtime={(runtime ? "ok" : "error")}");
            if (editor)
                Assert(
                    PassesPlayGate(note),
                    $"Editor accepts {note} but View could not build it.");
            checkedNotes++;
        }

        Assert(checkedNotes > 200, $"Agreement matrix only covered {checkedNotes} notes.");
    }

    private static void CheckVisualNoteEditor()
    {
        static string Merge(
            string current, string incoming,
            string action = "note", int slideStart = 0)
        {
            var result = Editor.VisualNoteEditor.Merge(
                current, incoming, action, slideStart);
            // Whatever the visual editor writes has to be playable text.
            var timing = new SimaiTimingPoint(1d, _content: result, bpm: 120f);
            var notes = timing.getNotes();
            Assert(
                timing.noteParseError == null && notes.Count > 0,
                $"Visual editor wrote unparseable text for '{current}' + " +
                $"'{incoming}': {result} ({timing.noteParseError})");
            return result;
        }

        // Cycling a key: Tap, Hold, break Tap, break Hold, and back.
        Assert(Merge("1", "1") == "1h[8:1]", "Key cycle Tap to Hold.");
        Assert(Merge("1h[8:1]", "1") == "1b", "Key cycle Hold to break Tap.");
        Assert(Merge("1b", "1") == "1hb[8:1]", "Key cycle break Tap to break Hold.");
        Assert(Merge("1hb[8:1]", "1") == "1", "Key cycle break Hold back to Tap.");

        // An authored Hold has its own length. The four hard-coded forms did not
        // match it, so clicking the key added a second note on top of the first.
        Assert(Merge("1h[4:1]", "1") == "1b", "A Hold with its own length must cycle.");
        Assert(
            Merge("1b", "1", "note") == "1hb[8:1]" &&
            Merge("1hb[4:1]", "1") == "1",
            "Break Hold with its own length must cycle.");
        Assert(
            Merge("1x", "1") == "1xh[8:1]" || Merge("1x", "1").Split('/').Length == 2,
            "An Ex note must either cycle or be placed beside, never corrupt.");

        // The two-key shorthand is one slot with two notes, so a third note joins
        // them instead of producing "12/3", which does not parse.
        Assert(Merge("12", "3") == "1/2/3", "Two-key shorthand merge.");

        // Touch cycles between Touch and Touch Hold.
        Assert(Merge("E1", "E1") == "E1h[8:1]", "Touch cycle to Touch Hold.");
        Assert(Merge("E1h[8:1]", "E1") == "E1", "Touch Hold cycle back.");
        Assert(Merge("C", "C") == "Ch[8:1]", "Centre Touch cycle.");
        Assert(Merge("1", "E1") == "1/E1", "A Touch is placed beside a key.");

        // Same head becomes a `*` group, and the branch must not repeat the head.
        Assert(
            Merge("1-5[8:1]", "1-3[8:1]") == "1-5[8:1]*-3[8:1]",
            "Same-head grouping.");
        Assert(
            Merge("1-5[8:1]*-3[8:1]", "1-3[8:1]") == "1-5[8:1]*-3[8:1]",
            "A branch that already exists must not be added twice.");
        // A D-zone head is two characters. Cutting one character off produced
        // "4d-5[8:1]*d-8[8:1]", which expands to "4dd-8[8:1]".
        Assert(
            Merge("4d-8d[8:1]", "4d-1d[8:1]") == "4d-8d[8:1]*-1d[8:1]",
            "D-zone same-head grouping.");

        // Chaining onto the end of an existing slide.
        Assert(
            Merge("1-5[8:1]", "5-8[8:1]") == "1-5[8:1]-8[8:1]",
            "Connected slide chaining.");
        Assert(
            Merge("1-5d[8:1]", "5d-8[8:1]") == "1-5d[8:1]-8[8:1]",
            "Chaining onto a D-zone end.");

        // Clicking a joint splits a connected slide. Both halves keep a length, and
        // a D-zone joint keeps its 'd' on both sides.
        Assert(
            Merge("1-5-8[8:1]", "5") == "1-5[8:1]/5-8[8:1]",
            "Connected slide split.");
        Assert(
            Merge("4d-8d-3d[8:1]", "8") == "4d-8d[8:1]/8d-3d[8:1]",
            "D-zone joint split lost its zone.");
        Assert(
            Merge("1-5[8:1]", "5") == "1-5[8:1]/5",
            "The end of a slide is not a joint.");

        // Head break toggling and joining two slides.
        Assert(
            Merge("1-5[8:1]", "1", "slideHead") == "1b-5[8:1]",
            "Slide head break toggle on.");
        Assert(
            Merge("1b-5[8:1]", "1", "slideHead") == "1-5[8:1]",
            "Slide head break toggle off.");
        Assert(
            Merge("1-5[8:1]/5-8[8:1]", "5", "slideHead") == "1-5[8:1]/5b-8[8:1]",
            "Chain cycle marks the following head as a break first.");
        Assert(
            Merge("1-5[8:1]/5b-8[8:1]", "5", "slideHead") == "1-5[8:1]-8[8:1]",
            "Chain cycle joins the two slides.");

        // Slide body break toggling from the path action.
        Assert(
            Merge("1-5[8:1]", "E1", "slidePath") == "1-5[8:1]b",
            "Slide body break toggle on.");
        Assert(
            Merge("1-5[8:1]b", "E1", "slidePath") == "1-5[8:1]/E1",
            "Slide body break toggle off adds the Touch.");
        Assert(
            Merge("1-5b[8:1]", "E1", "slidePath") == "1-5[8:1]/E1",
            "A body break written before the length must also toggle off.");

        // Whatever cannot be merged is placed beside, never written broken. Every
        // combination of a realistic slot and a click has to stay playable text.
        string[] slots =
        {
            "1", "1b", "1h[8:1]", "1hb[4:1]", "1x", "1f", "1$", "12",
            "E1", "E1h[8:1]", "C", "A1", "B3", "1/E1", "1/2/3",
            "1-5[8:1]", "1b-5[8:1]", "1-5[8:1]b", "1-5[8:1]-8[8:1]",
            "1-5[8:1]*-3[8:1]", "4d-8d[8:1]", "1w5[8:1]", "1V35[8:1]",
            "A1-E5[8:1]", "1-5[8:1]/5-8[8:1]", "1-5[8:1]/E1", "8p4[8:1]",
            "1-5[3##8:1]-8[8:1]", "E1~[4.8]", "1s5[8:1]"
        };
        string[] clicks =
        {
            "1", "5", "8", "4d", "1b", "1h[8:1]", "E1", "C", "A1",
            "1-5[8:1]", "5-8[8:1]", "1-3[8:1]", "4d-8d[8:1]", "1w5[8:1]",
            "A1-E5[8:1]", "1V35[8:1]", "E1~[4.8]"
        };
        var merges = 0;
        foreach (var slot in slots)
        foreach (var click in clicks)
        foreach (var action in new[] { "note", "slideHead", "slidePath" })
        {
            Merge(slot, click, action);
            merges++;
        }

        Assert(merges > 1000, $"Visual merge matrix only covered {merges} cases.");
    }

    /// <summary>
    /// The '@' and '&amp;' directives now have one owner instead of one copy per
    /// layer. The copies disagreed, and the colorizer's was the worst: it looked
    /// for the '/' of a meter anywhere in the document, so "@1," followed later by
    /// "1/2," painted everything between them as one marker.
    /// </summary>
    private static void CheckEditorDirectives()
    {
        static EditorDirective Read(string text)
        {
            Assert(
                EditorDirectiveScanner.TryRead(text, 0, out var directive),
                $"Directive not recognized: {text}");
            return directive;
        }

        static void Reject(string text) => Assert(
            !EditorDirectiveScanner.TryRead(text, 0, out _),
            $"Directive wrongly recognized: {text}");

        var overlay = Read("@{4}1/2,");
        Assert(
            overlay.kind == EditorDirectiveKind.Overlay && overlay.length == 4,
            "An overlay directive owns its head, not the notes after it.");

        Assert(
            Read("@start").kind == EditorDirectiveKind.ClipStart &&
            Read("@end").kind == EditorDirectiveKind.ClipEnd,
            "Clip marks.");
        Assert(
            Read("@START").kind == EditorDirectiveKind.ClipStart,
            "Clip marks ignore case.");
        // The chart parser reads the whole line, so a prefix is not a clip mark.
        Reject("@started");
        Reject("@endless");
        // '&' never carried the clip marks.
        Reject("&start");

        var meter = Read("@4/4");
        Assert(
            meter.kind == EditorDirectiveKind.Meter &&
            meter.numerator == 4 && meter.denominator == 4 &&
            meter.length == 4,
            "Meter directive.");
        Assert(
            Read("&3/4").kind == EditorDirectiveKind.Meter,
            "The legacy ampersand meter still reads.");
        Assert(
            Read("@4/4\n1,").length == 4,
            "A meter directive stops at its newline.");
        Reject("@4/0");
        Reject("@0/4");
        // Trailing text means the line is not a meter at all.
        Reject("@4/4x");

        // The bug: a lone '/' further down the document used to extend the marker
        // across the lines between them.
        Reject("@1,\n(120){4}1/2,");
        Reject("@9");

        var reset = Read("&NULL");
        Assert(
            reset.kind == EditorDirectiveKind.SectionReset && reset.length == 5,
            "Section reset.");
        var tint = Read("@FF0000,1,");
        Assert(
            tint.kind == EditorDirectiveKind.SectionColor &&
            tint.color == "FF0000" && tint.length == 7,
            "A section tint owns only itself, because notes may follow it.");
        Reject("@FF00ZZ");
        Reject("@FF00");

        // The runtime still turns these into the same chart state.
        SimaiProcess.ClearData();
        SimaiProcess.Serialize(
            "(120){4}1,\n@4/4\n2,\n@start\n3,\n@end\n4,\n&FF0000\n5,\nE");
        Assert(
            SimaiProcess.meterTable.Count == 1 &&
            SimaiProcess.meterTable[0].numerator == 4 &&
            SimaiProcess.meterTable[0].denominator == 4,
            "A meter line must reach the editor grid.");
        Assert(
            SimaiProcess.mediaTrimStart.HasValue && SimaiProcess.mediaTrimEnd.HasValue &&
            SimaiProcess.mediaTrimEnd > SimaiProcess.mediaTrimStart,
            "@start and @end must bound the media clip.");
        Assert(
            SimaiProcess.notelist.All(point =>
                string.IsNullOrEmpty(point.noteParseError)),
            "Editor directives must not be read as note text.");
        SimaiProcess.ClearData();
    }

    /// <summary>
    /// The colorizer used to run its own '&lt;' scan, looser than the one the chart
    /// parser used, so a malformed or misspelled command was painted as if the
    /// editor had understood it.
    /// </summary>
    private static void CheckAlphaCommandTokens()
    {
        static AlphaCommandToken Read(string text)
        {
            Assert(
                AlphaCommandBoundary.TryGetToken(text, 0, out var token),
                $"Alpha token not recognized: {text}");
            return token;
        }

        var known = Read("<SV*1>");
        Assert(
            known.name == "SV" && known.isKnown && known.length == 6,
            "A known command reports its name and extent.");
        Assert(
            !Read("<FOOBAR*1>").isKnown,
            "A well formed token with no matching command is reported as unknown.");
        Assert(
            !AlphaCommandBoundary.TryGetToken("<SV*1", 0, out _),
            "An unclosed token is not a command.");
        Assert(
            !AlphaCommandBoundary.TryGetToken("<SV\n*1>", 0, out _),
            "A command never spans a newline.");
        Assert(
            !AlphaCommandBoundary.TryGetToken("<SV1>", 0, out _),
            "A command needs its '*' separator.");
        Assert(
            AlphaCommandBoundary.RemoveCommands("<SV*1>1,") == "1,",
            "Removing commands leaves the notes.");
        Assert(
            !AlphaCommandBoundary.TryGetToken(
                "3<TINT*(TRUE,FF0000,1)>", 1, out _),
            "A command-looking token behind note text is not an Alpha boundary.");
        Assert(
            !AlphaCommandBoundary.IsPotentialStart("B7<B3[8:1]", 2) &&
            !AlphaCommandBoundary.IsPotentialStart("1<5[8:1]", 1),
            "Slide arcs must never be widened into Alpha command boundaries.");
        Assert(
            NoteSlotParser.TrySplit(
                "3<TINT*(TRUE,FF0000,1)>", out var notes, out _) &&
            notes.Count == 1 && notes[0].text == "3",
            "The note parser keeps the note as a safety net after rejecting its " +
            "misplaced Alpha command.");
    }

    private static void CheckOverlayBlocksAndMerge()
    {
        const string source = "@{3}1,1,1,\n{4}1,1,1,1,";
        Assert(
            Editor.NoteStreamMerger.TryBuildMerge(
                source, 2, out var start, out var length,
                out var merged, out var mergeError),
            $"Two fixed overlay streams must merge: {mergeError}");
        Assert(start == 0 && length == source.Length,
            "The merge must replace both source streams atomically.");
        Assert(merged == "{12}1/1,,,1,1,,1,,1,1,,,",
           $"Unexpected merged grid: {merged}");

        const string stacked = "@{2}2,2,\n@{3}1,1,1,\n{6}3,3,3,3,3,3,";
        Assert(
            Editor.NoteStreamMerger.TryFlattenAll(
                stacked, out var flattened, out var flattenError),
            $"All note streams must flatten before no-effect export: {flattenError}");
        Assert(
            !flattened.Contains("@", StringComparison.Ordinal) &&
            flattened.Contains("1", StringComparison.Ordinal) &&
            flattened.Contains("2", StringComparison.Ordinal) &&
            flattened.Contains("3", StringComparison.Ordinal),
            $"Flattening must preserve every stream and remove stream markers: {flattened}");

        SimaiProcess.ClearData();
        SimaiProcess.Serialize(
            "(120){4}\n@*\n{4}1,\n<HS*0.5>2,\n*@\n3,4,5,6,\nE");
        var overlay = SimaiProcess.notelist
            .Where(item => item.streamIndex != 0)
            .OrderBy(item => item.time)
            .ToList();
        Assert(overlay.Count == 2 && overlay[0].streamIndex == overlay[1].streamIndex,
            "A multi-line @* block must remain one isolated note stream.");
        Assert(overlay[1].HSpeed == 0.5f,
            "Scoped commands must continue across lines in one overlay block.");
        SimaiProcess.ClearData();
    }

    public static void Main(string[] args)
    {
        SlideGeometryRegression.Run();
        if (args.Contains("--mirror-only"))
        {
            CheckMirror();
            Console.WriteLine($"PASS: {assertions} mirror assertions");
            return;
        }

        if (args.Contains("--charts")) { Environment.ExitCode = ChartCorpusDiff.Run(args[Array.IndexOf(args, "--charts") + 1], 4000); return; }
        if (args.Contains("--baseline")) { Environment.ExitCode = BaselineDiff.Run(args.Contains("-v")); return; }
        if (args.Contains("--wholechart")) { Environment.ExitCode = WholeChartDiff.Run(args[Array.IndexOf(args, "--wholechart") + 1], 4000); return; }
        if (args.Contains("--viewbuild")) { Environment.ExitCode = ViewBuildDiff.Run(args[Array.IndexOf(args, "--viewbuild") + 1], 4000); return; }
        if (args.Contains("--probe")) { Probe.Run(); return; }
        CheckBounceSigns();
        CheckChartTokens();
        CheckBracketTracker();
        CheckBeatBrush();
        CheckEditorDirectives();
        CheckAlphaCommandTokens();
        CheckDurationPlacementMatrix();

        var valid = new[]
        {
            "1-5[8:1]", "1>5[8:1]", "1<5[8:1]",
            "1^3[8:1]", "1v3[8:1]", "1V36[8:1]",
            "1p5[8:1]", "1q5[8:1]", "1pp5[8:1]",
            "1qq5[8:1]", "1rp5[8:1]", "1rq5[8:1]",
            "1s5[8:1]", "1z5[8:1]", "1w5[8:1]",
            "1d-5d[8:1]", "4d-E1-B3[8:1]", "E1-4d[8:1]",
            "A1-E2-B3[8:1]", "C-E1[8:1]", "C1pE1[8:1]",
            // A touch slide inherits the authored arc for all four of these, so the
            // mirrored pair is no less drawable than the pair it mirrors.
            "A1ppE5[8:1]", "A1rpE5[8:1]", "A1rqE5[8:1]",
            "C2-E1[8:1]",
            "A1VCE2[8:1]", "A1<<E5[8:1]", "A1<<<E5[8:1]",
            "A1ppE5[8:1]", "1b-5m[8:1]", "1?-5[8:1]",
            "1!-5[8:1]", "1-5[8:1]b", "1-5[160#8:1]",
            "1-5[160#2]", "1-5[3##1.5]", "1-5[3##8:1]",
            "1-5[3##160#8:1]", "1-5-7[8:1]",
            "1-5-7[3##8:1]", "1-5-7[3##160#8:1]",
            "1-5[8:1]-7[4:1]",
            // Per-segment durations are fine when the path reaches a Touch area;
            // the renderer paces the whole path with their sum.
             "A1-E2[8:1]-B3[4:1]", "1v4[8:1]-E7bm[1:1]"
             , "5Q9A1P98CQ49K5[8:1]", "1A3571P0K1[4:1]"
        };
        foreach (var source in valid)
        {
            ParseAst(source);
            var note = ParseRuntime(source);
            Assert(note.slidePath.Count > 0, $"Runtime AST missing for {source}");
        }

        var invalid = new[]
        {
            "0-5[8:1]", "9-5[8:1]", "E0-5[8:1]",
            "E9-5[8:1]", "E1d-5[8:1]",
            "1r5[8:1]", "1k5[8:1]", "1-2[8:1]",
            "1^1[8:1]", "1^5[8:1]", "1v5[8:1]",
            "1V25[8:1]", "1s4[8:1]", "1z4[8:1]",
            "1w4[8:1]", "1w5-3[8:1]", "1<<5[8:1]",
            "A1wE5[8:1]", "1-5", "A1-E5",
            "A1sE5[8:1]", "A1zE5[8:1]",
            "1<<5-A1[8:1]", "1-1-A1[8:1]",
            "1-5[]", "1-5[8:0]", "1-5[0:1]", "1-5[-1]",
            "1-5[abc]", "1-5[8:1", "1-5[8:1]]",
            "1-5[8:1]-7-3[8:1]", "1-3b-5[8:1]",
            // A lone duration is the total for the whole slide, so writing it on
            // anything but the last segment leaves the rest of the path untimed.
            "1-5[8:1]-7", "1-5[3##8:1]-7", "1dV37[8:1]qA4",
            "1!?-5[8:1]", "1??-5[8:1]", "1$$$-5[8:1]",
            "1h-5[8:1]", "1$-5[8:1]", "1-5x[8:1]",
            "1-5f[8:1]", "1-5?[8:1]"
        };
        Assert(!IsValid(string.Empty), "Empty AST accepted.");
        foreach (var source in invalid)
        {
            Assert(!IsValid(source), $"Invalid AST accepted: {source}");
            RejectRuntime(source);
        }

        // Both languages are checked, so neither wording can rot unnoticed.
        var callerPrefersChinese = ParserMessageLocale.PreferChinese;
        foreach (var preferChinese in new[] { true, false })
        {
            ParserMessageLocale.PreferChinese = preferChinese;
            CheckDiagnostics(invalid);
        }
        ParserMessageLocale.PreferChinese = callerPrefersChinese;

        CheckNoteExpression();
        CheckTouchRadius();
        CheckSlideShapeResolver();
        CheckTouchSlideBypassesPrefabResolver();
        CheckNotesAndPlayfieldRevealTogether();
        CheckBounceStaysOnItsOwnKey();
        CheckPlayRetiresThePausedPreviewsNotes();
        CheckEverySlideFormTheEditorAcceptsCanBeBuilt();
        CheckConnectedSlideSegmentsSurviveTheLoader();
        CheckFakeNotesEndLikeAMiss();
        CheckNotesRewindWithTheTimeline();
        CheckUnrenderableNotesSpeakUp();
        CheckAutoPlayDoesNotTraceFakeSlides();
        CheckPreviewBuildsWithoutThrottling();
        CheckNoteTintReachesMoreThanHues();
        CheckTouchLeavesGrowAwayFromTheCentre();
        CheckTouchSlideTrailMeetsItsGuideStar();
        CheckEveryHintFormDropsTheOptionalBrackets();
        CheckCaptionsCanBePlacedAndSized();
        CheckLiveColourReachesTheSlideArc();
        CheckANoteCanBorrowAStarTrajectory();
        CheckABorrowedTrajectoryIsNeverJudged();
        CheckFilterPlacementAgainstV042();
        CheckTheJudgeColumnsLineUp();
        CheckAFilterBehindNotesIsRejectedSafely();
        CheckLiveVisualsCannotMissANote();
        CheckTouchOpensFromTheCentreOnEitherSide();
        CheckOptionalArgumentsLookTheSameEverywhere();
        CheckPerNoteSkin();
        CheckEachCountAgreesBetweenEditorAndView();
        CheckSameHeadBranchesKeepTheirHead();
        CheckTouchSegmentsMustCoverGround();
        CheckTouchSlideAcceptsMirroredArcs();
        CheckSelectableOrbitSlides();
        CheckHoldEndCapIsHiddenBeforeStart();
        CheckUnbuildableBeatsReachTheEditor();
        CheckCanvasFrameMovesBeforeItRebuilds();
        CheckLayerAgreement();
        CheckVisualNoteEditor();
        CheckMuriDetector();
        CheckChartStatistics();
        CheckDurationCompletion();
        CheckMirrorAgainstAst();
        CheckAlphaCommandGrammar();
        CheckAlphaCommandHintCoverage();
        CheckAlphaCommandPlacement();
        CheckMineIsItsOwnTarget();
        CheckWireFormatMatches();
        CheckSideJudgeColumnsCannotBeCut();
        CheckBackgroundClipIsWired();
        CheckSpawnCrossingIsNotRescannedEveryFrame();
        CheckReloadedViewDoesNotRetainChartState();
        CheckGuideStarLeavesWhenTheTimelineGoesBack();
        CheckOverlayBlocksAndMerge();

        var mixed = ParseRuntime("4d-E1-B3[8:1]");
        Assert(mixed.isTouchSlide, "Mixed route must use TouchSlide.");
        Assert(mixed.isDZone, "Mixed route must retain D-zone start.");
        Assert(mixed.touchEndArea == 'B', "Mixed route end area.");
        Assert(mixed.touchEndPosition == 3, "Mixed route end position.");
        Assert(mixed.slidePath.Count == 2, "Mixed route segment count.");

        var fade = ParseRuntime("1?-5[8:1]");
        Assert(
            fade.isSlideNoHead && !fade.suppressSlideGuideStarFade,
            "? must remove the head and retain guide-star fade.");
        var instant = ParseRuntime("1!-5[8:1]");
        Assert(
            instant.isSlideNoHead && instant.suppressSlideGuideStarFade,
            "! must remove the head and suppress guide-star fade.");
        var staticStar = ParseRuntime("1$");
        Assert(
            staticStar.isForceStar && !staticStar.isFakeRotate,
            "$ must create a non-rotating star.");
        var rotatingStar = ParseRuntime("1$$");
        Assert(
            rotatingStar.isForceStar && rotatingStar.isFakeRotate,
            "$$ must create a rotating star.");
        Assert(ParseRuntime("1f").isHanabi, "Tap firework modifier.");
        Assert(ParseRuntime("1hf[8:1]").isHanabi,
            "Hold firework modifier.");
        Assert(ParseRuntime("1f-5[8:1]").isHanabi,
            "Slide-head firework modifier.");
        Assert(ParseRuntime("Cf").isHanabi,
            "Touch firework modifier.");
        Assert(ParseRuntime("Chf[8:1]").isHanabi,
            "TouchHold firework modifier.");
        RejectRuntime("1?");
        RejectRuntime("1h$[4:1]");

        var sameHeadTiming = new SimaiTimingPoint(
            1d,
            _content: "A1-E2[8:1]*-B3[8:1]",
            bpm: 120f);
        var sameHead = sameHeadTiming.getNotes();
        Assert(sameHeadTiming.noteParseError == null, "Same-head parse.");
        Assert(sameHead.Count == 2, "Same-head branch count.");
        Assert(
            !sameHead[0].isSlideNoHead && sameHead[1].isSlideNoHead,
            "Same-head guide ownership.");
        RejectRuntime("1*-5[8:1]");
        RejectRuntime("1-5[8:1]*h");
        CheckSyntax("1*-5[8:1]", false);
        CheckSyntax("1-5[8:1]*h", false);
        var instantSameHead = new SimaiTimingPoint(
            1d,
            _content: "1!-5[8:1]*-7[8:1]",
            bpm: 120f).getNotes();
        Assert(
            instantSameHead.Count == 2 &&
            instantSameHead.All(note =>
                note.isSlideNoHead &&
                note.suppressSlideGuideStarFade),
            "! must propagate to every same-head guide star.");

        // Every branch of a same-head group carries its own body mine. The head
        // tap exists once, so only the first branch may own the head mine, but
        // clearing the body mine too made the second star of "1-3[8:1]m*-5[8:1]m"
        // judge as an ordinary slide.
        static SimaiNote[] SameHead(string content)
        {
            var timing = new SimaiTimingPoint(1d, _content: content, bpm: 120f);
            var branches = timing.getNotes();
            Assert(
                string.IsNullOrWhiteSpace(timing.noteParseError),
                $"Same-head mine fixture rejected: {content}");
            Assert(branches.Count == 2, $"Same-head branch count: {content}");
            return branches.ToArray();
        }

        var bothMine = SameHead("1-3[8:1]m*-5[8:1]m");
        Assert(
            bothMine.All(branch => branch.isMineSlide),
            "Both same-head branches must keep their own body mine.");
        var firstMine = SameHead("1-3[8:1]m*-5[8:1]");
        Assert(
            firstMine[0].isMineSlide && !firstMine[1].isMineSlide,
            "A body mine must not leak from the first same-head branch.");
        var secondMine = SameHead("1-3[8:1]*-5[8:1]m");
        Assert(
            !secondMine[0].isMineSlide && secondMine[1].isMineSlide,
            "A later same-head branch must keep a body mine of its own.");
        var headMine = SameHead("1m-3[8:1]*-5[8:1]");
        Assert(
            headMine[0].isMineHead && !headMine[1].isMineHead,
            "Only the branch that draws the head tap owns the head mine.");
        Assert(
            headMine.All(branch => !branch.isMineSlide),
            "A head mine must not become a body mine.");

        SimaiProcess.ClearData();
        const string overlaySource = "(120){4}1,\n@{4}2,";
        SimaiProcess.Serialize(overlaySource);
        var overlayTiming = SimaiProcess.notelist.Single(point =>
            point.streamIndex == 1);
        Assert(
            overlayTiming.rawTextPositionX == 4,
            "Overlay timing position must point to the note start, not the following comma; " +
            $"got {overlayTiming.rawTextPositionX}.");
        const string overlayCommandSource =
            "(120){4}1,\n@{4}2,<SV*2>3,";
        var overlayCommandTime = SimaiProcess.Serialize(
            overlayCommandSource,
            overlayCommandSource.IndexOf(
                "<SV*2>",
                StringComparison.Ordinal) + 2);
        Assert(
            Math.Abs(overlayCommandTime - 1d) < 0.000001d,
            "Overlay command caret fell back to stream start.");
        Assert(ParseRuntime("C2").noteType == SimaiNoteType.Touch,
            "C2 center Touch alias.");
        Assert(ParseRuntime("C2h").noteType == SimaiNoteType.TouchHold,
            "C2 short TouchHold alias.");
        Assert(ParseRuntime("Ch").noteType == SimaiNoteType.TouchHold,
            "Short TouchHold.");
        Assert(ParseRuntime("1bh").noteType == SimaiNoteType.Hold,
            "Unordered Hold modifiers.");
        // A duration without 'h' is a typo. Guessing a Hold hid the mistake and
        // v0.4.2 silently played a Tap, so both sides now report it by name.
        RejectRuntime("8[8:1]");
        RejectRuntime("4[12:1]");
        RejectRuntime("A1[8:1]");
        Assert(
            ParseError("4[12:1]")?.Contains("4h[12:1]") == true,
            "Hold typo must suggest the corrected note.");
        CheckSyntax("C2", true);
        CheckSyntax("C2h", true);
        CheckSyntax("Ch", true);
        CheckSyntax("1bh", true);
        CheckSyntax("8[8:1]", false);
        CheckSyntax("4[12:1]", false);
        CheckSyntax("A1[8:1]", false);
        CheckSyntax("2?^8dm[12:1]", true);
        CheckSyntax("5/2?^8dm[12:1]", true);
        // The D-zone arc must survive as a Slide with its mine and no-head flags,
        // because the runtime used to drop the whole note instead.
        var dZoneArc = ParseRuntime("2?^8dm[12:1]");
        Assert(
            dZoneArc.noteType == SimaiNoteType.Slide &&
            dZoneArc.isDZoneEnd &&
            dZoneArc.isMineSlide &&
            dZoneArc.isSlideNoHead &&
            !dZoneArc.suppressSlideGuideStarFade,
            "D-zone arc slide must keep its shape and flags.");
        SimaiProcess.ClearData();
        SimaiProcess.Serialize(
            "(120){16}5/2?^8dm[12:1],,6,5,4/2?^8dm[12:1],5,6,5,,5,5,\nE");
        Assert(
            SimaiProcess.notelist
                .SelectMany(point => point.getNotes())
                .Count(note => note.noteType == SimaiNoteType.Slide) == 2,
            "Every D-zone arc in a chart must reach playback.");
        Assert(
            SimaiProcess.notelist.All(point =>
                string.IsNullOrEmpty(point.noteParseError)),
            "A valid D-zone arc chart must parse without errors.");
        // A missing BPM used to be reported as a slide-chain error.
        var noBpm = new SimaiTimingPoint(0d, _content: "2?^8dm[12:1]", bpm: 0f);
        noBpm.getNotes();
        Assert(
            noBpm.noteParseError?.Contains("BPM") == true,
            "A duration without a BPM must name the BPM.");

        SimaiProcess.ClearData();
        SimaiProcess.Serialize(
            "(120){4}" +
            "<COLOR*slidestar=FF0000>" +
            "<SIZEV*slidestar=1.25>" +
            "<ALPHAV*slidestar=0.5>" +
            "<SV*slide=2>" +
            "<HS*star=1.5>" +
            "1-5[8:1],E");
        Assert(
            SimaiProcess.colorTable.Any(change =>
                change.noteType == "slidestar" &&
                change.color == "FF0000"),
            "COLOR slidestar target.");
        Assert(
            SimaiProcess.sizeTable.Any(change =>
                change.noteType == "slidestar" &&
                change.live),
            "SIZEV slidestar target.");
        Assert(
            SimaiProcess.alphaTable.Any(change =>
                change.noteType == "slidestar" &&
                change.live),
            "ALPHAV slidestar target.");
        Assert(
            SimaiProcess.svTable.Any(change =>
                change.noteType == "slide"),
            "SV slide target.");
        Assert(
            SimaiProcess.hsTable.Any(change =>
                change.noteType == "star"),
            "HS star target.");

        SimaiProcess.ClearData();
        SimaiProcess.Serialize(
            "(120){4}" +
            "<SV*slidestar=2>" +
            "<HS*slide=2>" +
            "<HS*slidestar=2>" +
            "1-5[8:1],E");
        Assert(
            !SimaiProcess.svTable.Any(change =>
                change.noteType == "slidestar"),
            "SV accepted slidestar.");
        Assert(
            SimaiProcess.hsTable.Any(change =>
                change.noteType == "slide"),
            "HS rejected the slide guide-star fade target.");
        Assert(
            !SimaiProcess.hsTable.Any(change =>
                change.noteType == "slidestar"),
            "HS accepted slidestar.");

        SimaiProcess.ClearData();
        SimaiProcess.Serialize(
            "(120){4}" +
            "<SPAWN*tap=1,invalid=2>" +
            "<SPAWNMODE*tap=Once,invalid=Rewind>" +
            "<BOUNCE*tap=8:1,invalid=4:1>" +
            "<DESTROY*tap=4,invalid=3>" +
            "<FAKE*tap=True,invalid=False>" +
            "1,E");
        Assert(SimaiProcess.spawnTable.Count == 0,
            "Malformed typed SPAWN partially applied.");
        Assert(SimaiProcess.spawnModeTable.Count == 0,
            "Malformed typed SPAWNMODE partially applied.");
        Assert(SimaiProcess.bounceTable.Count == 0,
            "Malformed typed BOUNCE partially applied.");
        Assert(SimaiProcess.destroyTable.Count == 0,
            "Malformed typed DESTROY partially applied.");
        Assert(SimaiProcess.fakeTable.Count == 0,
            "Malformed typed FAKE partially applied.");

        SimaiProcess.ClearData();
        SimaiProcess.Serialize(
            "(120){4}" +
            "<SV*tap=NaN><HS*tap=Infinity>" +
            "<SIZE*tap=NaN><ALPHA*tap=Infinity>" +
            "<BOUNCE*tap=NaN>" +
            "1,E");
        Assert(SimaiProcess.svTable.Count == 0, "SV accepted NaN.");
        Assert(SimaiProcess.hsTable.Count == 0, "HS accepted Infinity.");
        Assert(SimaiProcess.sizeTable.Count == 0, "SIZE accepted NaN.");
        Assert(SimaiProcess.alphaTable.Count == 0, "ALPHA accepted Infinity.");
        Assert(SimaiProcess.bounceTable.Count == 0, "BOUNCE accepted NaN.");

        foreach (var source in new[]
                 {
                     "1-5[8:1]", "4d-E1-B3[8:1]",
                     "A1-E2-B3[8:1]", "A1VCE2[8:1]",
                     "A1<<E5[8:1]", "1?-5[8:1]", "1!-5[8:1]"
                 })
            CheckSyntax(source, expected: true);
        foreach (var source in new[]
                 {
                     "1r5[8:1]", "1-2[8:1]", "1V25[8:1]",
                     "1w5-3[8:1]", "A1wE5[8:1]",
                     "1-5[8:1]-7-3[8:1]", "1!?-5[8:1]",
                     "1?", "1h$[4:1]", "1$-5[4:1]",
                     "1-5x[8:1]"
                 })
            CheckSyntax(source, expected: false);

        var ast = ParseAst("4d-E1-B3[8:1]");
        var jsonOptions = new JsonSerializerOptions { IncludeFields = true };
        var json = JsonSerializer.Serialize(ast.segments, jsonOptions);
        var roundTrip = JsonSerializer.Deserialize<List<SlidePathSegmentData>>(
            json, jsonOptions)!;
        Assert(
            roundTrip[0].ToExpression(includeDZone: true) == "4d-E1",
            "AST JSON first segment.");
        Assert(
            roundTrip[1].ToExpression(includeDZone: true) == "E1-B3[8:1]",
            "AST JSON second segment.");
        var legacy = new SlidePathSegmentData
        {
            startPosition = 1,
            shape = "-",
            endPosition = 5,
            duration = "[8:1]"
        };
        Assert(
            legacy.ToExpression(includeDZone: true) == "1-5[8:1]",
            "Legacy JSON fallback.");
        Assert(
            SlideSyntaxValidator.TryGetLengthSeconds(
                "[3##8:1]", 120d, out var delayedRatio) &&
            Math.Abs(delayedRatio - 0.25d) < 0.000001d,
            "Delayed ratio duration conversion.");
        Assert(
            SlideSyntaxValidator.TryGetLengthSeconds(
                "[3##150#8:1]", 120d, out var delayedBpmRatio) &&
            Math.Abs(delayedBpmRatio - 0.2d) < 0.000001d,
            "Delayed BPM ratio duration conversion.");
        Assert(
            Math.Abs(
                ParseRuntime("1-5-7[3##8:1]").slideTime -
                0.25d) < 0.000001d,
            "Single total delayed-ratio duration.");
        Assert(
            Math.Abs(
                ParseRuntime("1-5[8:1]-7[4:1]").slideTime -
                0.75d) < 0.000001d,
            "Per-segment duration sum.");
        Assert(
            !SlideSyntaxValidator.TryValidateSegments(
                new[]
                {
                    new SlidePathSegmentData
                    {
                        startPosition = 1,
                        shape = "-",
                        endPosition = 5
                    },
                    new SlidePathSegmentData
                    {
                        startPosition = 6,
                        shape = "-",
                        endPosition = 2,
                        duration = "[8:1]"
                    }
                },
                out _),
            "Discontinuous serialized AST accepted.");

        var touchSameHeadPreview =
            NotePreviewModule.ExpandPreview(
                "A1-E2[8:1]*-B3[8:1]");
        Assert(
            touchSameHeadPreview.Contains(
                "A1-E2[8:1]*-B3[8:1]"),
            "Touch same-head preview.");
        // A shape without an endpoint is still being typed. Guessing one here is
        // what produced a burst of ghost stars for every keystroke of "2?^".
        Assert(
            NotePreviewModule.ExpandPreview("1p").Count == 0,
            "Endpointless Slide must not be previewed.");
        Assert(
            NotePreviewModule.ExpandPreview("1p1").Contains("1p1[4:1]"),
            "Same-end full-loop preview.");
        Assert(
            NotePreviewModule.ExpandPreview(
                "1-2[8:1]").Count == 0,
            "Invalid complete Slide preview.");
        Assert(
            NotePreviewModule.ExpandPreview(
                "E1-E7-E5-E3").Contains("E1-E7-E5-E3[4:1]"),
            "Incremental Touch Slide preview requires a duration too early.");
        Assert(
            NotePreviewModule.ExpandPreview(
                "2dv4").Contains("2dv4[4:1]"),
            "Incremental D-zone v preview is missing.");
        Assert(
            NotePreviewModule.ExpandPreviewTimings(
                    "1/2dv4/")
                .Single()
                .Contains("1/2dv4[4:1]"),
            "One incomplete simultaneous branch drops valid preview siblings.");
        var dZoneModifierSlide = ParseRuntime("2?^8dm[12:1]");
        Assert(
            dZoneModifierSlide.isSlideNoHead &&
            !dZoneModifierSlide.suppressSlideGuideStarFade &&
            dZoneModifierSlide.isMineSlide &&
            dZoneModifierSlide.isDZoneEnd,
            "? + D-zone + mine Slide modifier wiring is invalid.");
        var dZoneMineSlide = ParseRuntime("2^8dm[12:1]");
        Assert(
            dZoneMineSlide.isMineSlide &&
            dZoneMineSlide.isDZoneEnd &&
            !dZoneMineSlide.isSlideNoHead &&
            NotePreviewModule.ExpandPreview("2^8dm")
                .Contains("2^8dm[4:1]"),
            "Exact 2^8dm runtime or incremental preview wiring is invalid.");
        var maximumBeat = Editor.BeatFormatBrush.Transform(
            "{4}1,,{16}2,,,,3,", null);
        Assert(
            maximumBeat.Contains("{16}", StringComparison.Ordinal) &&
            !maximumBeat.Contains("{4}", StringComparison.Ordinal),
            "Maximum-beat brush retained a smaller beat marker.");
        var legacyMine = new SimaiNote();
        typeof(SimaiNote)
            .GetProperty(
                "LegacyMineHead",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)!
            .SetValue(legacyMine, true);
        typeof(SimaiNote)
            .GetProperty(
                "LegacyMineSlide",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)!
            .SetValue(legacyMine, true);
        Assert(
            legacyMine.isMineHead && legacyMine.isMineSlide,
            "Legacy mine JSON aliases.");

        var slowSlideSv = new[]
        {
            new ScrollPoint(0d, 0d, 0.5f)
        };
        Assert(
            Math.Abs(AlphaVisualTiming.GetScrollProgress(
                slowSlideSv, 0d, 1d, 0.5d) - 0.5f) < 0.000001f &&
            Math.Abs(AlphaVisualTiming.GetScrollProgress(
                slowSlideSv, 0d, 1d, 1d) - 1f) < 0.000001f,
            "Positive Slide SV must preserve authored duration.");
        var pausedSlideSv = new[]
        {
            new ScrollPoint(0d, 0d, 0f),
            new ScrollPoint(0.5d, 0d, 1f)
        };
        Assert(
            Math.Abs(AlphaVisualTiming.GetScrollProgress(
                pausedSlideSv, 0d, 1d, 0.75d) - 0.5f) < 0.000001f &&
            Math.Abs(AlphaVisualTiming.GetScrollProgress(
                pausedSlideSv, 0d, 1d, 1d) - 1f) < 0.000001f,
            "Paused Slide SV must resume within authored duration.");
        var negativeSlideSv = new[]
        {
            new ScrollPoint(0d, 0d, -1f)
        };
        Assert(
            AlphaVisualTiming.GetScrollProgress(
                negativeSlideSv, 0d, 1d, 1d) == 0f,
            "Negative-net Slide SV must not be normalized forwards.");
        Assert(
            Math.Abs(AlphaVisualTiming.GetSpawnPresentationRadius(
                         0.5f,
                         AlphaVisualTiming.DefaultSpawnRadius,
                         hasEverCrossedSpawn: false) -
                     AlphaVisualTiming.DefaultSpawnRadius) < 0.000001f &&
            Math.Abs(AlphaVisualTiming.GetSpawnPresentationRadius(
                0.5f,
                AlphaVisualTiming.DefaultSpawnRadius,
                hasEverCrossedSpawn: true) - 0.5f) < 0.000001f,
            "Negative SV rewind snapped to SPAWN instead of returning smoothly toward centre.");

        const string alphabet = "12345678ABCDEdbxfm!?$h-<>^vVpqrszw[]:#";
        var random = new Random(20260818);
        for (var sample = 0; sample < 20000; sample++)
        {
            var chars = new char[random.Next(0, 28)];
            for (var index = 0; index < chars.Length; index++)
                chars[index] = alphabet[random.Next(alphabet.Length)];
            var source = new string(chars);
            if (!SlidePathParser.TryParsePath(source, out var path))
                continue;
            SlideSyntaxValidator.TryValidate(path, out _);
            NoteModifierParser.TryParse(source, path.segments, out _);
            foreach (var segment in path.segments)
                _ = segment.ToExpression(includeDZone: true);
        }

        var afterError = ParseRuntime("2-6[8:1]");
        Assert(
            afterError.startPosition == 2,
            "An earlier syntax error must not block later playback.");
        SimaiProcess.ClearData();
        SimaiProcess.Serialize("(120){4}1/1r5[8:1],2,E");
        var mixedTiming = SimaiProcess.notelist.First(point =>
            point.notesContent == "1/1r5[8:1]");
        var validTiming = SimaiProcess.notelist.First(point =>
            point.notesContent == "2");
        Assert(
            mixedTiming.getNotes().Count == 1 &&
            mixedTiming.noteList[0].startPosition == 1 &&
            !string.IsNullOrWhiteSpace(mixedTiming.noteParseError) &&
            validTiming.getNotes().Count == 1,
            "Invalid sibling must be marked and skipped without dropping valid notes.");
        Assert(
            Mirror.NoteMirrorHandle(
                "A1<E5[8:1],<HS*2>",
                Mirror.HandleType.LRMirror) ==
            "A8>E5[8:1],<HS*2>",
            "Mirror mistook a Touch Slide marker for an Alpha command.");
        CheckMirror();

        var languageFiles = new[]
        {
            "MajdataEdit/Langs/Langs.en-US.resx",
            "MajdataEdit/Langs/Langs.ja.resx",
            "MajdataEdit/Langs/Langs.zh-CN.resx"
        };
        HashSet<string>? languageKeys = null;
        foreach (var file in languageFiles)
        {
            var document = XDocument.Load(file);
            var entries = document.Root!
                .Elements("data")
                .ToDictionary(
                    entry => (string)entry.Attribute("name")!,
                    entry => entry.Element("value")?.Value ?? "");
            languageKeys ??= entries.Keys.ToHashSet();
            Assert(
                languageKeys.SetEquals(entries.Keys),
                $"Localization key mismatch: {file}");
            var help = entries["AlphaHelpStructuredText"];
            Assert(help.Contains("1f", StringComparison.Ordinal),
                $"Firework help missing: {file}");
            Assert(
                help.Contains("### FAKE", StringComparison.Ordinal) &&
                help.Contains("### COLORV", StringComparison.Ordinal) &&
                help.Contains("### SIZEV", StringComparison.Ordinal) &&
                help.Contains("### ALPHAV", StringComparison.Ordinal),
                $"Active command help incomplete: {file}");
            Assert(
                help.Contains("1?-5[8:1]", StringComparison.Ordinal) &&
                help.Contains("1!-5[8:1]", StringComparison.Ordinal),
                $"Slide modifier help incomplete: {file}");
            var spawn = help.IndexOf("### SPAWN ", StringComparison.Ordinal);
            var spawnMode = help.IndexOf(
                "### SPAWNMODE ", StringComparison.Ordinal);
            var destroy = help.IndexOf(
                "### DESTROY ", StringComparison.Ordinal);
            Assert(
                spawn >= 0 && spawn < spawnMode && spawnMode < destroy,
                $"SPAWNMODE help order: {file}");
        }
        var runtimeSchema = File.ReadAllText(
            "Assets/Scripts/Majson.cs");
        var runtimeControl = File.ReadAllText(
            "Assets/Scripts/HttpHandler.cs");
        var runtimeNotes = File.ReadAllText(
            "Assets/Scripts/Notes/TouchSlideDrop.cs");
        var runtimeLoader = File.ReadAllText(
            "Assets/Scripts/JsonDataLoader.cs");
        var editorControl = File.ReadAllText(
            "MajdataEdit/MainWindowCore.cs");
        var activeHints = File.ReadAllText(
            "MajdataEdit/Editor/AlphaCommandHints.cs");
        var audioClock = File.ReadAllText(
            "Assets/Scripts/AudioTimeProvider.cs");
        var recorder = File.ReadAllText(
            "Assets/Scripts/ScreenRecorder.cs");
        var legacyDestroy = File.ReadAllText(
            "Assets/Scripts/Misc/DestroySelf.cs");
        var webControl = File.ReadAllText(
            "MajdataEdit/WebControl.cs");
        var screenEffects = File.ReadAllText(
            "Assets/Scripts/UI/ScreenEffectController.cs");
        var soundEffects = File.ReadAllText(
            "MajdataEdit/SoundEffect.cs");
        var fakeLifetime = File.ReadAllText(
            "Assets/Scripts/Notes/FakeNoteLifetime.cs");
        var touchDrop = File.ReadAllText(
            "Assets/Scripts/Notes/TouchDrop.cs");
        var touchHoldDrop = File.ReadAllText(
            "Assets/Scripts/Notes/TouchHoldDrop.cs");
        var songDetail = File.ReadAllText(
            "Assets/Scripts/UI/SongDetailTemplateView.cs");
        var backgroundManager = File.ReadAllText(
            "Assets/Scripts/UI/BGManager.cs");
        var slideDrop = File.ReadAllText(
            "Assets/Scripts/Notes/SlideDrop.cs");
        var touchSlideDrop = File.ReadAllText(
            "Assets/Scripts/Notes/TouchSlideDrop.cs");
        var trajectoryCarrier = File.ReadAllText(
            "Assets/Scripts/Notes/TrajectoryCarrierDrop.cs");
        var mediaTimeline = File.ReadAllText(
            "MajdataEdit/MainWindow.MediaTimeline.cs");
        var mediaTimelineEditor = File.ReadAllText(
            "MajdataEdit/MediaTimelineEditor.xaml.cs");
        var counter = File.ReadAllText(
            "Assets/Scripts/UI/ObjectCounter.cs");
        Assert(
            runtimeSchema.Contains(
                "public bool deferPlaybackStart;",
                StringComparison.Ordinal),
            "Runtime request schema lacks two-phase playback.");
        Assert(
            runtimeControl.Contains(
                "CompleteContinueAt(",
                StringComparison.Ordinal) &&
            runtimeControl.Contains(
                "if (deferPlaybackStart)",
                StringComparison.Ordinal),
            "Runtime does not defer and schedule playback activation.");
        Assert(
            runtimeControl.Contains(
                "playbackStartDeferred = data.deferPlaybackStart;",
                StringComparison.Ordinal) &&
            runtimeControl.Contains(
                "var startsDeferredChart = playbackStartDeferred;",
                StringComparison.Ordinal) &&
            runtimeControl.Contains(
                "if (!resumedTimelinePreview && !startsDeferredChart)",
                StringComparison.Ordinal),
            "The deferred first playback can resume before restoring autoplay mode.");
        Assert(
            audioClock.Contains(
                "Time.realtimeSinceStartup >= startTime",
                StringComparison.Ordinal) &&
            audioClock.Contains(
                "isStart = !_isRecord && !scheduledStart;",
                StringComparison.Ordinal),
            "Future clock anchors still activate judgement early.");
        Assert(
            !legacyDestroy.Contains(
                "StopRecording()",
                StringComparison.Ordinal) &&
            recorder.Contains(
                "finalizeDeadline",
                StringComparison.Ordinal) &&
            recorder.Contains(
                "TryTerminateEncoder(p);",
                StringComparison.Ordinal),
            "Recording still has duplicate or unbounded stop ownership.");
        Assert(
            runtimeSchema.Contains(
                "public int protocolVersion = 1;",
                StringComparison.Ordinal) &&
            runtimeControl.Contains(
                "responseStatusCode",
                StringComparison.Ordinal) &&
            webControl.Contains(
                "protocolVersion: ProtocolVersion",
                StringComparison.Ordinal),
            "Editor/View protocol failures are not enforced.");
        Assert(
            runtimeControl.Contains(
                "HTTP client disconnected",
                StringComparison.Ordinal) &&
            webControl.Contains(
                "public static string? LastError",
                StringComparison.Ordinal),
            "HTTP failures can stop the listener or lose View diagnostics.");
        Assert(
            screenEffects.Contains(
                "private void OnPreCull()",
                StringComparison.Ordinal) &&
            screenEffects.Contains(
                "target.CaptureBase();",
                StringComparison.Ordinal),
            "Screen effects do not preserve animated UI transforms.");
        Assert(
            !soundEffects.Contains(
                "mineTypeSamples",
                StringComparison.Ordinal) &&
            soundEffects.Contains(
                "mineTouchHoldSamples",
                StringComparison.Ordinal),
            "Mine recording still duplicates every full-song sound buffer.");
        Assert(
            editorControl.Contains(
                "ViewClockLeadTime",
                StringComparison.Ordinal) &&
            editorControl.Contains(
                "if (generation != viewControlGeneration)",
                StringComparison.Ordinal),
            "Editor does not publish the second-phase clock anchor.");
        Assert(
            runtimeControl.Contains(
                "case EditorControlMethod.TimelinePreview:",
                StringComparison.Ordinal) &&
            runtimeControl.Contains(
                "timeProvider.SetPausedTimelineTime(data.startTime);",
                StringComparison.Ordinal) &&
            runtimeControl.Contains(
                "ConfigureDisplayTimeline(",
                StringComparison.Ordinal),
            "Runtime lacks paused shared-timeline preview.");
        Assert(
            editorControl.Contains(
                "previewToDrain is { IsCompleted: false }",
                StringComparison.Ordinal) &&
            editorControl.Contains(
                "if (previewDrain != null)",
                StringComparison.Ordinal),
            "Continue can still race an in-flight timeline preview.");
        Assert(
            editorControl.Contains(
                "startAt = resumeFromTimelinePreview",
                StringComparison.Ordinal) &&
            editorControl.Contains(
                "? DateTime.Now.Add(ViewClockLeadTime)",
                StringComparison.Ordinal) &&
            editorControl.Contains(
                ": DateTime.Now;",
                StringComparison.Ordinal),
            "An ordinary Pause/Continue must resume immediately; only a paused " +
            "timeline preview needs the replacement-note clock lead.");
        // Resuming a paused preview swaps its unjudgeable notes for playable ones in
        // place. Refusing Continue instead forced a full Start, which reloaded skin,
        // background and timelines and showed up as a hitch and a cover flash.
        Assert(
            !runtimeControl.Contains(
                "Continue cannot resume a timeline preview",
                StringComparison.Ordinal) &&
            runtimeControl.Contains(
                "Continue from a timeline preview requires jsonPath.",
                StringComparison.Ordinal) &&
            editorControl.Contains(
                "resumeFromTimelinePreview: resumePreview",
                StringComparison.Ordinal),
            "Resuming a paused preview still needs a full chart reload.");
        Assert(
            runtimeControl.Contains(
                "replacePausedPreview",
                StringComparison.Ordinal) &&
            runtimeLoader.Contains(
                "ClearPreviewNotes()",
                StringComparison.Ordinal) &&
            runtimeLoader.Contains(
                "component.previewOnly = previewOnly;",
                StringComparison.Ordinal) &&
            fakeLifetime.Contains(
                "if (note.previewOnly)",
                StringComparison.Ordinal),
            "Paused-preview staging and Fake-note lifetime are still conflated.");
        Assert(
            runtimeLoader.Contains(
                "includeActiveSustainsAtOffset",
                StringComparison.Ordinal) &&
            runtimeLoader.Contains(
                "RemainsVisibleAt(note, timing.time, ignoreOffset)",
                StringComparison.Ordinal) &&
            !runtimeLoader.Contains(
                "buildingResumeVisual",
                StringComparison.Ordinal),
            "Active Hold/Slide notes restored after a scrub must remain real " +
            "judgeable notes rather than visual-only fake notes.");
        Assert(
            touchDrop.Contains(
                "noteEffectManager.transform",
                StringComparison.Ordinal) &&
            touchHoldDrop.Contains(
                "noteEffectManager.transform",
                StringComparison.Ordinal),
            "Touch feedback is not placed in the effect plane, so ZOOM shifts it.");
        // The whole frame - aperture, both cover layers, the letterbox panels and
        // everything on the info canvas - travels as one rigid group under
        // ZOOM/MOVE. Re-fitting the panels to the aperture every frame was the
        // source of both the drifting side panels and the uncovered band across
        // the middle, so that per-frame path must stay gone.
        Assert(
            screenEffects.Contains(
                "AddFrameTarget(FindSceneTransform(\"1080Circle_Rev\"), GetGameplayPlaneSize(), true)",
                StringComparison.Ordinal) &&
            screenEffects.Contains(
                "AddFrameTarget(FindSceneTransform(\"BackgroundCover\"), GetGameplayPlaneSize(), true)",
                StringComparison.Ordinal) &&
            !screenEffects.Contains(
                "IsViewportMask",
                StringComparison.Ordinal) &&
            screenEffects.Contains(
                "AddCanvasInfoFrameTargets();",
                StringComparison.Ordinal) &&
            !screenEffects.Contains(
                "FitOuterCoverToAperture",
                StringComparison.Ordinal) &&
            // The side panels keep their authored inner edge - it already lines
            // up with the aperture, and one transform carries both - and only
            // grow outward. Ten viewports of outward growth is what still covers
            // the screen edge at the smallest ZOOM. Nothing recomputes the rect
            // from a measurement: that arithmetic assumed centre anchors, and on
            // a stretched anchor sizeDelta is an inset rather than a size, so it
            // blacked the window out as it grew.
            !backgroundManager.Contains(
                "public void LayoutOuterCoverOnce(",
                StringComparison.Ordinal) &&
            !backgroundManager.Contains(
                "CloneOuterCoverPanel",
                StringComparison.Ordinal) &&
            backgroundManager.Contains(
                "const float zoomOutMargin = 10f;",
                StringComparison.Ordinal) &&
            // Runtime strips replace the short scene top/bottom bars and must cover
            // the larger ZOOM area without stacking both versions.
            backgroundManager.Contains(
                "AddTopAndBottomCover(sidePanel);",
                StringComparison.Ordinal) &&
            backgroundManager.Contains(
                "sign * (parent.rect.height * 0.5f + height * 0.5f)",
                StringComparison.Ordinal) &&
            backgroundManager.Contains(
                "position.x += Mathf.Sign(position.x) * outward * 0.5f;",
                StringComparison.Ordinal) &&
            backgroundManager.Contains(
                "ExtendSidePanelToScreenEdge(rect);",
                StringComparison.Ordinal),
            "Cover layers do not track the play area under ZOOM/MOVE.");
        // Every note kind has to retire itself on a paused timeline, because nothing
        // judges there. Slide and TouchSlide used to pile up while dragging.
        Assert(
            slideDrop.Contains(
                "IsPausedTimelinePreview &&",
                StringComparison.Ordinal) &&
            touchSlideDrop.Contains(
                "IsPausedTimelinePreview && now > time + Mathf.Max(0f, duration)",
                StringComparison.Ordinal),
            "Slide paths are not retired on a paused timeline.");
        Assert(
            runtimeLoader.Contains(
                "note.suppressSlideGuideStarFade ? 1f : starSpeed",
                StringComparison.Ordinal) &&
            runtimeLoader.Contains(
                "private float ResolveSlideAppearanceSpeed(",
                StringComparison.Ordinal),
            "! must override the guide-star speed to 1.0 so nothing fades in.");
        Assert(
            trajectoryCarrier.Contains(
                "var sampleTime = IsPausedTimelinePreview",
                StringComparison.Ordinal) &&
            trajectoryCarrier.Contains(
                "previewOnly ? Mathf.Max(now, time) : now",
                StringComparison.Ordinal) &&
            editorControl.Contains(
                "if (noteD.isTrajectoryOnly)",
                StringComparison.Ordinal) &&
            editorControl.Contains(
                "DrawWaveRing(graphics, x, y, 3f, carrierColor);",
                StringComparison.Ordinal),
            "Borrowed trajectory previews are not rendered as their carrier Tap.");
        Assert(
            touchSlideDrop.Contains(
                "? (orbitPosition + 5) % 8 + 1",
                StringComparison.Ordinal) &&
            touchSlideDrop.Contains(
                "var sourceEnd = (sourceStart + 3) % 8 + 1;",
                StringComparison.Ordinal) &&
            !touchSlideDrop.Contains(
                "usesAuthoredOrbit",
                StringComparison.Ordinal),
            "Selectable P/Q must choose the rotated pp/qq template whose authored " +
            "circle exactly matches the selected area independently of the note start.");
        Assert(
            SlidePathParser.TryParsePath("1P35[8:1]", out var pOrbit) &&
            pOrbit.segments[0].middle.area == 'B' &&
            pOrbit.segments[0].middle.position == 3 &&
            SlidePathParser.TryParsePath("1Q85[8:1]", out var qOrbit) &&
            qOrbit.segments[0].middle.area == 'E' &&
            qOrbit.segments[0].middle.position == 8,
            "P3 and Q8 must select the B3 and E8 circles used by 1pp5 and " +
            "1qq5 respectively.");
        Assert(
            editorControl.IndexOf(
                "if (noteD.isTrajectoryOnly)", StringComparison.Ordinal) <
            editorControl.IndexOf(
                "if (noteD.noteType == SimaiNoteType.Slide", StringComparison.Ordinal),
            "Borrowed trajectory carriers must be drawn before note-type branches so " +
            "their Tap marker cannot disappear behind Slide handling.");
        Assert(
            slideDrop.Contains(
                "TryGetDirectedTangentPoint(",
                StringComparison.Ordinal) &&
            slideDrop.Contains(
                "return Vector3.Dot(",
                StringComparison.Ordinal) &&
            !slideDrop.Contains(
                "TryGetTangentPoint(",
                StringComparison.Ordinal),
            "Selectable P/Q routes can choose the tangent opposite their orbit direction.");
        Assert(
            mediaTimeline.Contains(
                "MediaTimelinePanel.CurrentPlayhead",
                StringComparison.Ordinal) &&
            mediaTimeline.Contains(
                "MediaTimelinePanel.SyncPlayhead(position);",
                StringComparison.Ordinal) &&
            mediaTimelineEditor.Contains(
                "internal double CurrentPlayhead => playhead;",
                StringComparison.Ordinal) &&
            mediaTimelineEditor.Contains(
                "if (!sameChart)",
                StringComparison.Ordinal),
            "Refreshing or reopening the media timeline can reset the playhead to zero.");
        Assert(
            counter.Contains("defaultDisplayFont", StringComparison.Ordinal) &&
            counter.Contains("displayFontCache", StringComparison.Ordinal) &&
            counter.Contains("authoredJudgeTextWidth", StringComparison.Ordinal) &&
            counter.Contains("authoredJudgeCountFontSize * 0.6f",
                StringComparison.Ordinal) &&
            !counter.Contains("PrewarmDisplayFont", StringComparison.Ordinal) &&
            !counter.Contains("authoredDisplayFonts", StringComparison.Ordinal) &&
            counter.Contains("Canvas.ForceUpdateCanvases();", StringComparison.Ordinal) &&
            !counter.Contains("Font.textureRebuilt", StringComparison.Ordinal) &&
            !counter.Contains("StabilizeDisplayFontLayout", StringComparison.Ordinal),
            "Player fonts must use one cached source, preserve the authored original " +
            "font, move only the custom-font count column, and finish layout before " +
            "the first judgement changes text.");
        var displayTimeline = File.ReadAllText(
            "Assets/Scripts/UI/DisplayTimelineController.cs");
        Assert(
            displayTimeline.Contains("ResolveSubtitleFont(subtitle.font)",
                StringComparison.Ordinal) &&
            !displayTimeline.Contains("SetSubtitleFont(", StringComparison.Ordinal) &&
            !counter.Contains("SetSubtitleFont(", StringComparison.Ordinal),
            "TEXT must retain its original default font and change fonts only when " +
            "the command explicitly supplies font=.");
        Assert(
            touchHoldDrop.Contains(
                "gameObject.AddComponent<SortingGroup>()",
                StringComparison.Ordinal) &&
            touchHoldDrop.Contains(
                "sortingGroup.sortingOrder = noteSortOrder;",
                StringComparison.Ordinal) &&
            touchHoldDrop.Contains("mask.backSortingOrder = 0;", StringComparison.Ordinal) &&
            touchHoldDrop.Contains("mask.frontSortingOrder = 5;", StringComparison.Ordinal),
            "Each TouchHold needs an isolated sorting group so nearby fan masks cannot " +
            "cover one another.");
        Assert(
            runtimeLoader.Contains("customSkin.TouchPoint_Each", StringComparison.Ordinal),
            "An each TouchHold must use the yellow centre point.");
        Assert(
            trajectoryCarrier.Contains("CreateGuideLine(", StringComparison.Ordinal) &&
            trajectoryCarrier.Contains("UpdateGuideLine();", StringComparison.Ordinal),
            "A borrowed Tap must retain its radial guide while travelling.");
        var simaiProcess = File.ReadAllText("MajdataEdit/SimaiProcess.cs");
        Assert(
            simaiProcess.Contains("Xcount - noteTemp.Length", StringComparison.Ordinal) &&
            simaiProcess.Contains("commaIndex - rawContent.Length", StringComparison.Ordinal),
            "Basic parse diagnostics must store the note start rather than the next comma.");
        Assert(
            songDetail.Contains(
                "designerText.font = regularFont;",
                StringComparison.Ordinal),
            "Recording song-detail designer font is not assigned.");
        Assert(
            editorControl.Contains(
                "sendRequestTimelineSeek",
                StringComparison.Ordinal) &&
            editorControl.Contains(
                "bool resumeFromTimelinePreview = false)",
                StringComparison.Ordinal),
            "Editor cannot tell Continue that it resumes a paused preview.");
        Assert(
            activeHints.Contains("new(\"FAKE\"", StringComparison.Ordinal) &&
            activeHints.Contains("new(\"COLORV\"", StringComparison.Ordinal) &&
            activeHints.Contains("new(\"SIZEV\"", StringComparison.Ordinal) &&
            activeHints.Contains("new(\"ALPHAV\"", StringComparison.Ordinal),
            "Active Alpha command hints are incomplete.");
        Assert(
            editorControl.Contains(
                "QueuePausedTimelineSeek(GetTimelinePosition())",
                StringComparison.Ordinal) &&
            !editorControl.Contains(
                "pausedTime + 0.001d < pausedTimelinePreviewTime",
                StringComparison.Ordinal) &&
            runtimeNotes.Contains(
                "if (JudgmentDisabled)",
                StringComparison.Ordinal),
            "Paused timeline still uses asymmetric forward/backward logic.");
        Assert(
            editorControl.Contains(
                "special != null && !special.reset",
                StringComparison.Ordinal) &&
            editorControl.Contains(
                "Lookup(\"break\") is { reset: false }",
                StringComparison.Ordinal) &&
            runtimeLoader.Contains(
                "value == null || value.reset ? null : value.radius",
                StringComparison.Ordinal),
            "Editor/View SPAWN or DESTROY reset inheritance diverged.");

        Console.WriteLine(
            $"PASS: {assertions} assertions and 20000 malformed-input cases");
    }
}
