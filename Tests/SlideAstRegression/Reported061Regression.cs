using System.Diagnostics;
using MajdataCore;

namespace MajdataEdit;

internal static class Reported061Regression
{
    internal static void Inspect(string path)
    {
        SimaiProcess.ClearData();
        if (!SimaiProcess.ReadData(path)) throw new Exception("Cannot read fixture");
        for (var difficulty = 0; difficulty < SimaiProcess.fumens.Length; difficulty++)
        {
            var text = SimaiProcess.fumens[difficulty];
            if (string.IsNullOrWhiteSpace(text)) continue;
            SimaiProcess.Serialize(text);
            Console.WriteLine($"DIFFICULTY {difficulty}, slots={SimaiProcess.notelist.Count}, end={SimaiProcess.timinglist.LastOrDefault()?.time:F3}");
            foreach (var timing in SimaiProcess.notelist)
            {
                _ = timing.getNotes();
                if (!string.IsNullOrEmpty(timing.noteParseError))
                    Console.WriteLine($"ERROR t={timing.time:F3} line={timing.rawTextPositionY + 1} {timing.notesContent}: {timing.noteParseError}");
            }
            foreach (var error in SimaiProcess.ValidateAlphaCommands(text)) Console.WriteLine("COMMAND ERROR " + error);
            foreach (var group in SimaiProcess.notelist.SelectMany(t => t.getNotes().Select(n => (t.time, n)))
                         .GroupBy(pair => (int)(pair.time / 10)).OrderBy(g => g.Key))
                Console.WriteLine($"{group.Key * 10}s: {group.Count()} notes, types=" + string.Join(",", group.GroupBy(n => n.n.noteType).Select(g => $"{g.Key}:{g.Count()}")));
        }
    }
    internal static void Export(string path, string output)
    {
        SimaiProcess.ClearData();
        if (!SimaiProcess.ReadData(path)) throw new Exception("Cannot read fixture");
        SimaiProcess.Serialize(SimaiProcess.fumens[4]);
        foreach (var timing in SimaiProcess.notelist) timing.noteList = timing.getNotes();
        var chart = new Majson { filePath = Path.GetDirectoryName(Path.GetFullPath(path))!, title = SimaiProcess.title!,
            timingList = SimaiProcess.notelist, svTable = SimaiProcess.svTable, hsTable = SimaiProcess.hsTable,
            spawnTable = SimaiProcess.spawnTable, spawnModeTable = SimaiProcess.spawnModeTable, bounceTable = SimaiProcess.bounceTable,
            destroyTable = SimaiProcess.destroyTable, fakeTable = SimaiProcess.fakeTable, colorTable = SimaiProcess.colorTable,
            sizeTable = SimaiProcess.sizeTable, alphaTable = SimaiProcess.alphaTable, angleTable = SimaiProcess.angleTable,
            displayTable = SimaiProcess.displayTable, subtitleTable = SimaiProcess.subtitleTable,
            effectTable = SimaiProcess.effectTable, mediaTable = SimaiProcess.mediaTable };
        File.WriteAllText(output, System.Text.Json.JsonSerializer.Serialize(chart, new System.Text.Json.JsonSerializerOptions { IncludeFields = true }));
    }
    internal static void Probe()
    {
        VerifyNotesAndComments();
        SimaiProcess.ClearData();
        SimaiProcess.Serialize("(120){4},,,,\n@StArT\n1,2,3,4,\n@EnD\n5,6,7,8,");
        if (SimaiProcess.mediaTrimStart != 2d || SimaiProcess.mediaTrimEnd != 4d || SimaiProcess.notelist.Count != 8)
            throw new Exception("Recording marks must not occupy a note slot");
        foreach (var marker in new[] { "@FFFFFF", "@NULL", "&aabbcc", "&null" })
        {
            if (!EditorDirectiveScanner.TryRead(marker + "\n1,", 0, out _) ||
                EditorDirectiveScanner.TryRead(marker + "1,", 0, out _) || EditorDirectiveScanner.TryRead("1," + marker, 2, out _))
                throw new Exception("Section tint must own its line: " + marker);
        }
        Console.WriteLine("MARKER PASS: recording range 2-4s, tint/reset requires its own line.");
        foreach (var expression in new[] { "B5m-E6-B8[1:1]", "B5-E6-B8m[1:1]", "1~[5d-1d[1:2]]" })
        {
            var valid = NoteExpressionParser.TryParse(expression, out _, out var error);
            if (!valid) throw new Exception(expression + ": " + error);
            Console.WriteLine($"NOTE {expression}: {valid} {error}");
            SimaiProcess.ClearData();
            SimaiProcess.Serialize("(120){4}" + expression + ",");
            foreach (var timing in SimaiProcess.notelist)
            foreach (var note in timing.getNotes())
                if (note.slidePath is { Count: > 0 })
                    Console.WriteLine("AST " + string.Join(" | ", note.slidePath.Select(segment => segment.ToExpression(true))) +
                        " valid=" + SlideSyntaxValidator.TryValidateSegments(note.slidePath, out var pathError) + " " + pathError);
        }
        foreach (var chart in new[] { "(120)@*{4}1,|| comment\n2,3,*@\n{4}4,", "(120)@*{4}1,\n2,3,*@\n{4}4," })
        {
            SimaiProcess.ClearData();
            SimaiProcess.Serialize(chart);
            Console.WriteLine("FLOW " + string.Join(" | ", SimaiProcess.notelist.Select(t => $"{t.time:F3}:{t.rawTextPositionY}:{t.rawTextPositionX}:{t.notesContent}:{t.noteParseError}")));
        }
        var path = @"C:\Users\sjtu_\Downloads\⚡朋友的酒⚡\⚡朋友的酒⚡\maidata.txt";
        SimaiProcess.ClearData();
        var watch = Stopwatch.StartNew();
        SimaiProcess.ReadData(path);
        Console.WriteLine($"Read file: {watch.ElapsedMilliseconds} ms");
        for (var difficulty = 0; difficulty < SimaiProcess.fumens.Length; difficulty++)
        {
            var text = SimaiProcess.fumens[difficulty];
            if (string.IsNullOrEmpty(text)) continue;
            watch.Restart();
            SimaiProcess.Serialize(text);
            var parse = watch.ElapsedMilliseconds;
            watch.Restart();
            foreach (var timing in SimaiProcess.notelist) _ = timing.getNotes();
            Console.WriteLine($"Difficulty {difficulty}: {text.Length} chars, {SimaiProcess.notelist.Count} slots; serialize {parse} ms; notes {watch.ElapsedMilliseconds} ms");
            watch.Restart();
            _ = new MuriDetector(SimaiProcess.notelist, MuriSlideTimeTable.Empty, allowPartial: true).DetectStatic();
            Console.WriteLine($"Static analysis: {watch.ElapsedMilliseconds} ms");
        }
    }

    private static void VerifyNotesAndComments()
    {
        foreach (var text in new[] {
            "(120)@*\n|| stream description\n{4}1,\n2,\n*@\n{4}3,",
            "(120)@*{4}1,|| *@ is only comment text\n2,*@\n{4}3,"
        })
        {
            SimaiProcess.ClearData(); SimaiProcess.Serialize(text);
            var notes = SimaiProcess.notelist.SelectMany(timing => timing.getNotes()).ToList();
            if (notes.Count != 3 || SimaiProcess.notelist.Count(timing => timing.streamIndex > 0) != 2 ||
                SimaiProcess.notelist.Any(timing => !string.IsNullOrEmpty(timing.noteParseError)))
                throw new Exception("Comments must not end or flatten an independent note stream");
        }
        SimaiProcess.ClearData(); SimaiProcess.Serialize("(120){4}1,(\n2,3,");
        var remaining = SimaiProcess.notelist.SelectMany(timing => timing.getNotes()).Select(note => note.startPosition).ToArray();
        if (!remaining.SequenceEqual(new[] { 1, 3 })) throw new Exception("Unfinished BPM must only invalidate its own slot");
        Console.WriteLine("PARSER PASS: leading/inline comments, commented stream terminator, unfinished BPM isolation.");
    }
}
