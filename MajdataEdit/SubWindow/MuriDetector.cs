using System.Text.Json;
using System.Text.Json.Serialization;
using MajdataCore;

namespace MajdataEdit;

internal class MaimaiOperationMultNote
{
    public int endArea;
    public double endTime;
    public string noteContent;
    public int ntype;
    public int positionX;
    public int positionY;

    public int startArea;

    // Represents one operation for simultaneous-note detection.
    public double startTime;

    public MaimaiOperationMultNote(double _startTime, double _endTime, int _startArea,
        int _endArea, int _ntype, string _noteContent, int _positionX,
        int _positionY)
    {
        startTime = _startTime;
        endTime = _endTime;
        startArea = _startArea;
        endArea = _endArea;
        ntype = _ntype;
        noteContent = _noteContent;
        positionX = _positionX;
        positionY = _positionY;
    }
}

internal class MaimaiOperationSlide
{
    public int area;
    public string noteContent;
    public int ntype;
    public int positionX;

    public int positionY;

    // Represents one operation for Slide-tail collision detection.
    public double time;

    public MaimaiOperationSlide(double _time, int _area, int _ntype,
        string _noteContent, int _positionX, int _positionY)
    {
        time = _time;
        area = _area;
        ntype = _ntype;
        noteContent = _noteContent;
        positionX = _positionX;
        positionY = _positionY;
    }
}

internal readonly struct MuriWarning
{
    public MuriWarning(string content, int positionX, int positionY)
    {
        Content = content;
        PositionX = positionX;
        PositionY = positionY;
    }

    public string Content { get; }
    public int PositionX { get; }
    public int PositionY { get; }
}

internal readonly struct MuriPassArea
{
    public MuriPassArea(double time, int area)
    {
        Time = time;
        Area = area;
    }

    // Where in the Slide's travel the hand crosses Area, as a ratio of the
    // segment's own duration.
    public double Time { get; }
    public int Area { get; }
}

// The measured pass areas from slide_time.json. Only the shapes that were
// measured are present, so a shape this table does not know about cannot be
// checked rather than being a chart error.
internal sealed class MuriSlideTimeTable
{
    private sealed class PassAreaEntry
    {
        [JsonPropertyName("time")] public double Time { get; set; }
        [JsonPropertyName("area")] public int Area { get; set; }
    }

    private readonly Dictionary<string, Dictionary<string, List<MuriPassArea>>> shapes;

    private MuriSlideTimeTable(
        Dictionary<string, Dictionary<string, List<MuriPassArea>>> shapes)
    {
        this.shapes = shapes;
    }

    public static bool TryLoad(string json, out MuriSlideTimeTable? table, out string error)
    {
        table = null;
        error = string.Empty;
        try
        {
            var raw = JsonSerializer.Deserialize<
                Dictionary<string, Dictionary<string, List<PassAreaEntry>>>>(json);
            if (raw == null)
            {
                error = "slide_time.json is empty";
                return false;
            }

            var shapes = new Dictionary<string, Dictionary<string, List<MuriPassArea>>>();
            foreach (var shape in raw)
            {
                var offsets = new Dictionary<string, List<MuriPassArea>>();
                foreach (var offset in shape.Value)
                    offsets[offset.Key] = offset.Value
                        .ConvertAll(entry => new MuriPassArea(entry.Time, entry.Area));
                shapes[shape.Key] = offsets;
            }

            table = new MuriSlideTimeTable(shapes);
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    public bool TryGetPassAreas(
        string shape, string offsetKey, out List<MuriPassArea> areas)
    {
        areas = new List<MuriPassArea>();
        if (!shapes.TryGetValue(shape, out var offsets))
            return false;
        if (!offsets.TryGetValue(offsetKey, out var measured))
            return false;
        areas = measured;
        return true;
    }
}

// The detection half of the muri check. Slides are read from the shared path
// AST that the runtime already produced, so a valid Note can no longer be
// reported as a syntax error here just because this file's own text scanning
// did not know a form the parser accepts.
internal sealed class MuriDetector
{
    private readonly List<SimaiTimingPoint> notelist;
    private readonly MuriSlideTimeTable slideTime;
    private readonly List<MuriWarning> warnings = new();

    public MuriDetector(List<SimaiTimingPoint> notelist, MuriSlideTimeTable slideTime)
    {
        this.notelist = notelist;
        this.slideTime = slideTime;
    }

    public IReadOnlyList<MuriWarning> Warnings => warnings;

    private readonly struct SlideSegmentWindow
    {
        public SlideSegmentWindow(
            SlidePathSegmentData segment, double startTime, double duration)
        {
            Segment = segment;
            StartTime = startTime;
            Duration = duration;
        }

        public SlidePathSegmentData Segment { get; }
        public double StartTime { get; }
        public double Duration { get; }
    }

    private void AddWarning(string content, int positionX, int positionY)
    {
        warnings.Add(new MuriWarning(content, positionX, positionY));
    }

    private static int notePos(int pos, bool relative)
    {
        if (pos <= 0) pos += 8;

        if (relative)
            pos %= 8;
        else
            pos = (pos - 1) % 8 + 1;
        return pos;
    }

    // A connected Slide's segments each get their own time window. When every
    // segment carries its own duration the windows are exact; with one total
    // duration playback splits it by the drawn length of each segment, which
    // the editor cannot measure, so the total is split evenly here.
    private static List<SlideSegmentWindow> ResolveSegmentWindows(
        SimaiTimingPoint group, SimaiNote note)
    {
        var windows = new List<SlideSegmentWindow>();
        var segments = note.slidePath;
        if (segments.Count == 0)
            return windows;

        var perSegment = new List<double>(segments.Count);
        var authored = true;
        foreach (var segment in segments)
        {
            if (string.IsNullOrEmpty(segment.duration) ||
                !SlideSyntaxValidator.TryGetLengthSeconds(
                    segment.duration, group.currentBpm, out var seconds))
            {
                authored = false;
                break;
            }
            perSegment.Add(seconds);
        }

        if (!authored)
        {
            perSegment.Clear();
            for (var i = 0; i < segments.Count; i++)
                perSegment.Add(note.slideTime / segments.Count);
        }

        var startTime = note.slideStartTime;
        for (var i = 0; i < segments.Count; i++)
        {
            windows.Add(new SlideSegmentWindow(segments[i], startTime, perSegment[i]));
            startTime += perSegment[i];
        }
        return windows;
    }

    // Returns null when the chart holds a Note kind the check cannot model.
    public List<MaimaiOperationMultNote>? BuildMultNoteOperations()
    {
        var TIME_EPS = 5;

        // See comments in MaiMuriDetector.multNoteDetect(self, eps=5): https://github.com/Moying-moe/maimaiMuriDetector

        var opSequence = new List<MaimaiOperationMultNote>();

        foreach (var noteGroup in notelist)
        {
            var baseTime = noteGroup.time;
            var positionX = noteGroup.rawTextPositionX;
            var positionY = noteGroup.rawTextPositionY;

            foreach (var note in noteGroup.getNotes())
                if (note.noteType == SimaiNoteType.Tap)
                {
                    opSequence.Add(new MaimaiOperationMultNote(
                        Math.Round(baseTime, TIME_EPS),
                        Math.Round(baseTime, TIME_EPS),
                        note.startPosition,
                        note.startPosition,
                        0,
                        note.noteContent!,
                        positionX,
                        positionY
                    ));
                }
                else if (note.noteType == SimaiNoteType.Slide)
                {
                    if (note.isTouchSlide)
                        continue;
                    opSequence.Add(new MaimaiOperationMultNote(
                        Math.Round(baseTime, TIME_EPS),
                        Math.Round(baseTime, TIME_EPS),
                        note.startPosition,
                        note.startPosition,
                        1,
                        note.noteContent!,
                        positionX,
                        positionY
                    ));
                    if (note.slidePath.Count == 0)
                    {
                        AddWarning(string.Format(
                            MainWindow.GetLocalizedString("SyntaxError"),
                            note.noteContent,
                            positionY + 1,
                            positionX + 1
                        ), positionX, positionY);
                        continue;
                    }

                    // The hand ends up wherever the last segment ends, which is
                    // what the AST already resolved for every Slide form.
                    var endPosition = note.slidePath[^1].endPosition;

                    opSequence.Add(new MaimaiOperationMultNote(
                        Math.Round(note.slideStartTime, TIME_EPS),
                        Math.Round(note.slideStartTime + note.slideTime, TIME_EPS),
                        note.startPosition,
                        endPosition,
                        3,
                        note.noteContent!,
                        positionX,
                        positionY
                    ));
                }
                else if (note.noteType == SimaiNoteType.Hold)
                {
                    opSequence.Add(new MaimaiOperationMultNote(
                        Math.Round(baseTime, TIME_EPS),
                        Math.Round(baseTime + note.holdTime, TIME_EPS),
                        note.startPosition,
                        note.startPosition,
                        2,
                        note.noteContent!,
                        positionX,
                        positionY
                    ));
                }
                else
                {
                    // TODO: Support DX charts.
                    return null;
                }
        }

        return opSequence;
    }

    public int DetectMultNote()
    {
        var opSequence = BuildMultNoteOperations();
        if (opSequence == null)
            return -1;

        var errorCnt = 0;

        opSequence.Sort(delegate(MaimaiOperationMultNote x, MaimaiOperationMultNote y)
        {
            if (x.startTime == y.startTime)
            {
                if (x.ntype == y.ntype)
                    return 0;
                return x.ntype < y.ntype ? -1 : 1;
            }

            return x.startTime < y.startTime ? -1 : 1;
        });

        var inHandling = new List<MaimaiOperationMultNote>();

        foreach (var op in opSequence)
        {
            for (var i = inHandling.Count - 1; i >= 0; i--)
                if (inHandling[i].endTime < op.startTime)
                    inHandling.RemoveAt(i);

            if (op.ntype == 3)
            {
                for (var i = inHandling.Count - 1; i >= 0; i--)
                    if (inHandling[i].endTime == op.startTime &&
                        inHandling[i].endArea == op.startArea)
                        inHandling.RemoveAt(i);
            }
            else if (op.ntype == 1)
            {
                for (var i = inHandling.Count - 1; i >= 0; i--)
                    if (inHandling[i].ntype == 1 &&
                        inHandling[i].startTime == op.startTime &&
                        inHandling[i].startArea == op.startArea)
                        inHandling.RemoveAt(i);
            }

            inHandling.Add(op);

            if (inHandling.Count > 2)
            {
                var warningText = MainWindow.GetLocalizedString("MultNoteError1");
                foreach (var e in inHandling)
                {
                    if (e.ntype == 1) warningText += "*";
                    warningText += string.Format(
                        "\"{0}\"({1}L,{2}C) ",
                        e.noteContent, e.positionY + 1, e.positionX + 1
                    );
                }

                warningText += string.Format(MainWindow.GetLocalizedString("MultNoteError2"), inHandling.Count);
                AddWarning(warningText, inHandling[0].positionX, inHandling[0].positionY);
                errorCnt++;
            }
        }

        return errorCnt;
    }

    // Returns null when the chart holds a Note kind the check cannot model.
    public List<MaimaiOperationSlide>? BuildSlideOperations()
    {
        // See comments in MaiMuriDetector.slideDetect(self, judgementLength = 0.15): https://github.com/Moying-moe/maimaiMuriDetector

        var opSequence = new List<MaimaiOperationSlide>();

        foreach (var noteGroup in notelist)
        {
            var baseTime = noteGroup.time;
            var positionX = noteGroup.rawTextPositionX;
            var positionY = noteGroup.rawTextPositionY;

            foreach (var note in noteGroup.getNotes())
                if (note.noteType == SimaiNoteType.Tap ||
                    note.noteType == SimaiNoteType.Hold)
                {
                    opSequence.Add(new MaimaiOperationSlide(
                        baseTime,
                        note.startPosition,
                        0,
                        note.noteContent!,
                        positionX,
                        positionY
                    ));
                }
                else if (note.noteType == SimaiNoteType.Slide)
                {
                    if (note.isTouchSlide)
                        continue;
                    // Add the star head to the queue.
                    opSequence.Add(new MaimaiOperationSlide(
                        baseTime,
                        note.startPosition,
                        0,
                        note.noteContent!,
                        positionX,
                        positionY
                    ));

                    foreach (var window in ResolveSegmentWindows(noteGroup, note))
                        AddSegmentPassAreas(
                            note, window, positionX, positionY, opSequence);
                }
                else
                {
                    // TODO: Support DX charts.
                    return null;
                }
        }

        return opSequence;
    }

    public int DetectSlide(double judgementLength)
    {
        var opSequence = BuildSlideOperations();
        if (opSequence == null)
            return -1;

        opSequence.Sort(delegate(MaimaiOperationSlide x, MaimaiOperationSlide y)
        {
            if (x.time == y.time)
            {
                if (x.ntype == y.ntype)
                    return 0;
                return x.ntype > y.ntype ? -1 : 1;
            }

            return x.time < y.time ? -1 : 1;
        });
        var errorCnt = 0;

        var inJudgement = new List<MaimaiOperationSlide>();

        foreach (var op in opSequence)
        {
            var curTime = op.time;

            for (var i = inJudgement.Count - 1; i >= 0; i--)
                if (inJudgement[i].time + judgementLength < curTime)
                    inJudgement.RemoveAt(i);

            if (op.ntype == 1)
                inJudgement.Add(op);
            else if (op.ntype == 0)
                foreach (var e in inJudgement)
                    if (e.area == op.area &&
                        op.time - judgementLength < e.time &&
                        e.time < op.time)
                    {
                        AddWarning(string.Format(
                            MainWindow.GetLocalizedString("SlideError"),
                            e.noteContent, e.positionY + 1, e.positionX + 1,
                            op.noteContent, op.positionY + 1, op.positionX + 1,
                            Math.Floor((op.time - e.time) * 1000)
                        ), e.positionX, e.positionY);
                        errorCnt++;
                    }
        }

        return errorCnt;
    }

    private void AddSegmentPassAreas(
        SimaiNote note,
        SlideSegmentWindow window,
        int positionX,
        int positionY,
        List<MaimaiOperationSlide> opSequence)
    {
        var segment = window.Segment;
        var startPosition = segment.startPosition;
        var shape = segment.shape;
        string offsetKey;

        if (segment.hasMiddle)
        {
            // Turning type
            offsetKey =
                notePos(segment.middlePosition - startPosition, true) + "," +
                notePos(segment.endPosition - startPosition, true);
        }
        else
        {
            offsetKey = notePos(segment.endPosition - startPosition, true)
                .ToString();
        }

        if (shape == ">" && startPosition >= 3 && startPosition <= 6)
            /*
             * WARNING:
             * This is a legacy issue in the measurement data.
             * Each Slide type was measured from position 1 and stored using relative positions.
             * Runtime judgment computes absolute positions from the actual start and relative positions, effectively rotating the measurements.
             * However, the direction of > and < Slides depends on their starting position.
             * For example, > is clockwise from starts 7, 8, 1, or 2, but counterclockwise from starts 3, 4, 5, or 6.
             * Because measurements always start at 1, > is always clockwise there and < is always counterclockwise.
             * Therefore, in SLIDE_TIME, > means an always-clockwise curved Slide, not one that initially curves right.
             * Handle > and < specially by reversing the operator when runtime direction differs from measurement direction.
             *
             * This is a temporary workaround and may be corrected later.
             * **/
            // A > Slide starting at 3, 4, 5, or 6 runs opposite to the measured direction.
            shape = "<";
        else if (shape == "<" && startPosition >= 3 && startPosition <= 6)
            shape = ">";

        if (!slideTime.TryGetPassAreas(shape, offsetKey, out var passAreas))
        {
            // The shape itself is valid; there is simply no measurement for it,
            // so this Slide cannot take part in the check.
            AddWarning(string.Format(
                MainWindow.GetLocalizedString("MuriUnmeasuredSlide"),
                note.noteContent,
                positionY + 1,
                positionX + 1
            ), positionX, positionY);
            return;
        }

        foreach (var passArea in passAreas)
            opSequence.Add(new MaimaiOperationSlide(
                passArea.Time * window.Duration + window.StartTime,
                notePos(passArea.Area + startPosition, false),
                1,
                note.noteContent!,
                positionX,
                positionY
            ));
    }
}
