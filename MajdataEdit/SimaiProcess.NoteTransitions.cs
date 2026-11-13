using System.Globalization;
using MajdataCore;

namespace MajdataEdit;

internal static partial class SimaiProcess
{
    private static bool TryParseNoteTransition(string token, double time, float bpm,
        int stream, int position, ref float currentHs, List<SvPoint> sv, List<SpeedChange> hs,
        List<SpawnChange> spawn, List<BounceChange> bounce, List<DestroyChange> destroy,
        List<SizeChange> size, List<AlphaChange> alpha, List<AngleChange> angle)
    {
        if (!AlphaCommandGrammar.TrySplitToken(token, out var name, out var body) ||
            !AlphaCommandGrammar.TryFind(name, out var command) || command!.scalar == null ||
            command.kind == AlphaCommandKind.Color ||
            (!command.scalar.allowsTransition && command.kind != AlphaCommandKind.Angle) ||
            (!body.Contains('(') && command.kind != AlphaCommandKind.Angle &&
             !(command.kind == AlphaCommandKind.Sv && command.scalar.IsReset(body)))) return false;
        if (!AlphaCommandGrammar.TryValidate(token, bpm, out _)) return true;

        var typed = body.Contains('=');
        AlphaCommandGrammar.TrySplitValues(body, out var parts);
        foreach (var part in typed ? parts : new List<string> { body })
        {
            var pair = part.Split('=', 2, StringSplitOptions.TrimEntries);
            var type = typed ? AlphaCommandGrammar.NormalizeTarget(pair[0]) : null;
            var text = typed ? pair[1] : part.Trim();
            var spec = typed ? command.TypedValueSpec! : command.scalar;
            var reset = spec.IsReset(AlphaCommandGrammar.Unwrap(text));
            NoteValueTransition.TryParse(text, bpm, spec, out var transition);
            float x = 0, y = 0;
            if (transition != null) { x = transition.to; y = transition.toY; }
            else if (!reset)
            {
                if (spec.kind == AlphaValueKind.Scale &&
                    AlphaCommandGrammar.TryParseScalePair(text, out x, out y)) { }
                else if (spec.kind == AlphaValueKind.Duration)
                { AlphaCommandGrammar.TryParseDuration(AlphaCommandGrammar.Unwrap(text), bpm, out x); y = x; }
                else
                { float.TryParse(AlphaCommandGrammar.Unwrap(text), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out x); y = x; }
            }
            switch (command.kind)
            {
                case AlphaCommandKind.Sv:
                    sv.Add(new SvPoint { time=time, sourcePosition=position, streamIndex=stream,
                        noteType=type, multiplier=x, reset=reset, transition=transition }); break;
                case AlphaCommandKind.Hs:
                    hs.Add(new SpeedChange { time=time, sourcePosition=position, streamIndex=stream,
                        noteType=type, multiplier=x, reset=reset, transition=transition });
                    if (type == null) currentHs = transition?.from ?? x;
                    break;
                case AlphaCommandKind.Spawn:
                    spawn.Add(new SpawnChange { time=time, sourcePosition=position, streamIndex=stream,
                        noteType=type, radius=x, reset=reset, transition=transition }); break;
                case AlphaCommandKind.Destroy:
                    destroy.Add(new DestroyChange { time=time, sourcePosition=position, streamIndex=stream,
                        noteType=type, radius=x, reset=reset, transition=transition }); break;
                case AlphaCommandKind.Size:
                    size.Add(new SizeChange { time=time, sourcePosition=position, streamIndex=stream,
                        noteType=type, scale=MathF.Sqrt(MathF.Abs(x*y)), scaleX=x, scaleY=y,
                        reset=reset, transition=transition, live=name.Equals("SIZEV",StringComparison.OrdinalIgnoreCase) });
                    break;
                case AlphaCommandKind.Alpha:
                    alpha.Add(new AlphaChange { time=time, sourcePosition=position, streamIndex=stream,
                        noteType=type, alpha=Math.Clamp(x,0f,1f), reset=reset, transition=transition,
                        live=name.Equals("ALPHAV",StringComparison.OrdinalIgnoreCase) }); break;
                case AlphaCommandKind.Angle:
                    angle.Add(new AngleChange { time=time, sourcePosition=position, streamIndex=stream,
                        noteType=type, angle=x, reset=reset, transition=transition,
                        live=name.Equals("ANGLEV",StringComparison.OrdinalIgnoreCase) }); break;
            }
        }
        return true;
    }
}
