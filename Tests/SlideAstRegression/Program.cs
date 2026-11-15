using MajdataCore;
using MajdataEdit.Editor;

namespace MajdataEdit;

internal static class Program
{
    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static List<SimaiNote> ParseSlot(string source)
    {
        var timing = new SimaiTimingPoint(1d, _content: source, bpm: 120f);
        var notes = timing.getNotes();
        Require(string.IsNullOrEmpty(timing.noteParseError), $"{source}: {timing.noteParseError}");
        return notes;
    }

    public static void Main()
    {
        var checks = new (string Name, Action Run)[]
        {
            ("Tap / Hold", CheckBasicNotes),
            ("Non-C TouchHold", CheckTouchHold),
            ("Same-head Slide branches", CheckSlideBranches),
            ("D-zone / TouchSlide", CheckTouchSlide),
            ("SlideCode A/B nodes", CheckSlideCode),
            ("Trajectory inheritance", CheckInheritance),
            ("Comma-terminated slots", CheckCommaTermination),
            ("BPM / subdivision timing", CheckTiming),
            ("Comments inside note streams", CheckStreamComments),
            ("Invalid-slot isolation", CheckInvalidSlot),
            ("TEXT coordinates / origin", CheckText),
            ("Alpha command grammar", CheckAlphaCommands),
            ("Negative / positive SV", CheckScroll),
            ("Noise lifetime boundaries", CheckNoiseLifetime),
            ("Headless / Mine EACH exclusion", CheckEach),
            ("Mirror geometry", CheckMirror),
            ("Duration / modifier brushes", CheckBrushes)
        };

        foreach (var (name, run) in checks)
        {
            SimaiProcess.ClearData();
            run();
            Console.WriteLine($"PASS: {name}");
        }
        SimaiProcess.ClearData();
        Console.WriteLine($"PASS: {checks.Length} core checks");
    }

    private static void CheckBasicNotes()
    {
        var tap = ParseSlot("1").Single();
        var hold = ParseSlot("2h[8:1]").Single();
        Require(tap.noteType == SimaiNoteType.Tap && tap.startPosition == 1,
            "Tap type or position changed.");
        Require(hold.noteType == SimaiNoteType.Hold && Math.Abs(hold.holdTime - .25) < 1e-6,
            "Hold duration at 120 BPM must be 0.25 seconds.");
    }

    private static void CheckTouchHold()
    {
        var note = ParseSlot("E1h[8:1]").Single();
        Require(note.noteType == SimaiNoteType.TouchHold && note.touchArea == 'E',
            "Non-C TouchHold lost its type or area.");
    }

    private static void CheckSlideBranches()
    {
        var notes = ParseSlot("1-5[8:1]*-7[4:1]");
        Require(notes.Count == 2 && !notes[0].isSlideNoHead && notes[1].isSlideNoHead,
            "Same-head branches must own exactly one head.");
    }

    private static void CheckTouchSlide()
    {
        var note = ParseSlot("4d-E1-B3[8:1]").Single();
        Require(note.isTouchSlide && note.isDZone && note.slidePath.Count == 2 &&
                note.touchEndArea == 'B' && note.touchEndPosition == 3,
            "Mixed D-zone / TouchSlide lost its route or endpoint.");
    }

    private static void CheckSlideCode()
    {
        const string source = "1A3B5K7[8:1]";
        Require(SlidePathParser.TryParsePath(source, out var path) &&
                SlideSyntaxValidator.TryValidate(path, out _),
            "SlideCode A/B nodes must remain valid.");
        Require(ParseSlot(source).Single().slidePath.Count > 0,
            "SlideCode route was lost by the runtime parser.");
    }

    private static void CheckInheritance()
    {
        var note = ParseSlot("1~[5-7[8:1]]").Single();
        Require(note.isTrajectoryOnly && note.trajectoryCarrierType == SimaiNoteType.Tap &&
                note.slidePath.Count > 0,
            "Inherited Tap must retain its carrier type and trajectory.");
    }

    private static void CheckCommaTermination()
    {
        SimaiProcess.Serialize("(120){16}1,2");
        Require(SimaiProcess.notelist.Count == 1 &&
                SimaiProcess.notelist[0].getNotes().Single().startPosition == 1,
            "An unfinished final slot must not be read or remove a completed slot.");
    }

    private static void CheckTiming()
    {
        SimaiProcess.Serialize("(120){4}1,(240){8}2,3,");
        var times = SimaiProcess.notelist.Select(point => point.time).ToArray();
        Require(times.Length == 3 && Math.Abs(times[1] - times[0] - .5) < 1e-6 &&
                Math.Abs(times[2] - times[1] - .125) < 1e-6,
            "BPM or subdivision changes shifted note times.");
    }

    private static void CheckStreamComments()
    {
        SimaiProcess.Serialize("(120)@*{4}1,|| *@ is only comment text\n2,*@\n{4}3,");
        Require(SimaiProcess.notelist.SelectMany(point => point.getNotes()).Count() == 3 &&
                SimaiProcess.notelist.Count(point => point.streamIndex > 0) == 2 &&
                SimaiProcess.notelist.All(point => string.IsNullOrEmpty(point.noteParseError)),
            "A comment must not end or flatten a note stream.");
    }

    private static void CheckInvalidSlot()
    {
        SimaiProcess.Serialize("(120){4}1,(\n2,3,");
        var positions = SimaiProcess.notelist.SelectMany(point => point.getNotes())
            .Select(note => note.startPosition);
        Require(positions.SequenceEqual(new[] { 1, 3 }),
            "An invalid slot must not stop later notes from parsing.");
    }

    private static void CheckText()
    {
        SimaiProcess.Serialize("(120){4}<TEXT*(\"hello\",origin=Center,x=9,y=-4,size=9999)>,");
        var text = SimaiProcess.subtitleTable.Single();
        Require(text.origin == "Center" && text.x == 9f && text.y == -4f && text.size == 200f,
            "TEXT must preserve off-screen coordinates and origin while limiting font size.");
    }

    private static void CheckAlphaCommands()
    {
        foreach (var token in new[] { "SV*tap=(1,2,8:1,linear)", "ALPHAV*(0,1,4:1,easeout)",
                     "COLOR*(FF0000,00BBFF,8:1,linear)", "FLIP*(Instant,90,0.5,True)" })
            Require(AlphaCommandGrammar.TryValidate(token, 120, out var error), token + ": " + error);
        Require(!AlphaCommandGrammar.TryValidate("SV*(1,2,1,unknown)", 120, out _),
            "An unknown transition function must not be accepted.");
    }

    private static void CheckScroll()
    {
        SvController.Load(new List<SvPoint>
        {
            new() { time = 0, multiplier = -1 },
            new() { time = 2, multiplier = 1 }
        });
        Require(Math.Abs(SvController.GetCumulativeScroll(1) + 1) < 1e-6 &&
                Math.Abs(SvController.GetCumulativeScroll(3) + 1) < 1e-6,
            "Reversing SV must integrate without a position jump.");
        SvController.Load(new List<SvPoint>());
    }

    private static void CheckNoiseLifetime()
    {
        var timeline = new NoiseZoneTimeline();
        timeline.Configure(new[] { new NoiseZoneEvent(17, 2, 1, .5), new NoiseZoneEvent(16, 2, 0, .5) });
        Require(!timeline.IsBlocked(17, 1.5) && timeline.IsBlocked(17, 2) &&
                !timeline.IsBlocked(17, 3) && !timeline.IsBlocked(16, 2) &&
                !timeline.TryGetVisualSpan(16, 2, out _, out _),
            "Noise must block only during its real duration; zero duration is warning-only.");
    }

    private static void CheckEach()
    {
        foreach (var source in new[] { "1m/1", "1?-5[8:1]/4", "1!-5[8:1]/4" })
        {
            SimaiProcess.ClearData();
            SimaiProcess.Serialize("(120){4}" + source + ",");
            var timing = SimaiProcess.notelist.Single();
            Require(timing.getNotes().Count == 2 && !timing.isEach,
                "Mine or headless Slide must not make the other head EACH: " + source);
        }
    }

    private static void CheckMirror()
    {
        Require(Mirror.NoteMirrorHandle("1>6[8:1]", Mirror.HandleType.UDMirror) == "4>7[8:1]",
            "Vertical mirroring must preserve the authored arc direction.");
    }

    private static void CheckBrushes()
    {
        Require(NoteDurationBrush.Transform("1-5[8:1]/1h", "max") == "1-5[8:1]/1h[8:1]",
            "Duration brush must update implicit Holds.");
        Require(NoteModifierBrush.Transform("2h[8:1]/4<8d", 'b') == "2hb[8:1]/4b<8db",
            "Break brush must retain Hold duration and Slide geometry.");
    }
}
