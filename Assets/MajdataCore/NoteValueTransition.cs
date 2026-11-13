using System;
using System.Globalization;

namespace MajdataCore
{
    // Values are parsed once. Playback evaluates only this numeric curve.
    public sealed class NoteValueTransition
    {
        public static readonly string[] Functions = { "linear", "easein", "easeout", "easeinout",
            "sinein", "sineout", "sineinout", "expoin", "expoout", "expoinout", "circin", "circout", "circinout",
            "smoothstep", "smootherstep", "backin", "backout", "backinout",
            "elasticin", "elasticout", "elasticinout", "bouncein", "bounceout", "bounceinout" };
        public float from;
        public float to;
        public float fromY;
        public float toY;
        // Colour curves use the scalar, Y and Z channels for R, G and B.
        public float fromZ;
        public float toZ;
        public bool fromUntinted;
        public float duration;
        public string easing = "linear";
        public float parameter = 2f;

        public float Progress(double elapsed)
        {
            var t = duration <= 0f ? 1d : Math.Max(0d, Math.Min(1d, elapsed / duration));
            return easing switch
            {
                "easein" => (float)Math.Pow(t, parameter),
                "easeout" => (float)(1d - Math.Pow(1d - t, parameter)),
                "easeinout" => (float)(t < .5d
                    ? .5d * Math.Pow(2d * t, parameter)
                    : 1d - .5d * Math.Pow(2d * (1d - t), parameter)),
                "smoothstep" => (float)(t * t * (3d - 2d * t)),
                "smootherstep" => (float)(t * t * t * (t * (6d * t - 15d) + 10d)),
                "sinein" => (float)(1d-Math.Cos(Math.PI*t*.5d)),
                "sineout" => (float)Math.Sin(Math.PI*t*.5d),
                "sineinout" => (float)((1d - Math.Cos(Math.PI * t)) * .5d),
                "expoin" => t<=0d ? 0f : (float)Math.Pow(2d,10d*t-10d),
                "expoout" => t>=1d ? 1f : (float)(1d-Math.Pow(2d,-10d*t)),
                "expoinout" => t <= 0d ? 0f : t >= 1d ? 1f :
                    (float)(t < .5d ? Math.Pow(2d, 20d * t - 10d) * .5d : (2d - Math.Pow(2d, -20d * t + 10d)) * .5d),
                "circin" => (float)(1d-Math.Sqrt(1d-t*t)),
                "circout" => (float)Math.Sqrt(1d-(t-1d)*(t-1d)),
                "circinout" => (float)(t < .5d ? (1d-Math.Sqrt(1d-4d*t*t))*.5d
                    : (Math.Sqrt(1d-Math.Pow(-2d*t+2d,2d))+1d)*.5d),
                "backin" => (float)(1d-BackOut(1d-t)),
                "backout" => (float)BackOut(t),
                "backinout" => (float)(t<.5d ? Math.Pow(2d*t,2d)*((2.5949095d+1d)*2d*t-2.5949095d)*.5d
                    : (Math.Pow(2d*t-2d,2d)*((2.5949095d+1d)*(t*2d-2d)+2.5949095d)+2d)*.5d),
                "elasticin" => (float)(1d-ElasticOut(1d-t)),
                "elasticout" => (float)ElasticOut(t),
                "elasticinout" => t<=0d ? 0f : t>=1d ? 1f : (float)(t<.5d
                    ? -Math.Pow(2d,20d*t-10d)*Math.Sin((20d*t-11.125d)*2d*Math.PI/4.5d)*.5d
                    : Math.Pow(2d,-20d*t+10d)*Math.Sin((20d*t-11.125d)*2d*Math.PI/4.5d)*.5d+1d),
                "bouncein" => (float)(1d-BounceOut(1d-t)),
                "bounceout" => (float)BounceOut(t),
                "bounceinout" => (float)(t<.5d ? (1d-BounceOut(1d-2d*t))*.5d : (1d+BounceOut(2d*t-1d))*.5d),
                _ => (float)t
            };
        }

        private static double BackOut(double t) =>
            1d+2.70158d*Math.Pow(t-1d,3d)+1.70158d*Math.Pow(t-1d,2d);

        private static double ElasticOut(double t) => t<=0d ? 0d : t>=1d ? 1d :
            Math.Pow(2d,-10d*t)*Math.Sin((10d*t-.75d)*2d*Math.PI/3d)+1d;

        private static double BounceOut(double t)
        {
            if(t<1d/2.75d) return 7.5625d*t*t;
            if(t<2d/2.75d) { t-=1.5d/2.75d; return 7.5625d*t*t+.75d; }
            if(t<2.5d/2.75d) { t-=2.25d/2.75d; return 7.5625d*t*t+.9375d; }
            t-=2.625d/2.75d; return 7.5625d*t*t+.984375d;
        }

        public float Evaluate(double elapsed) => from + (to - from) * Progress(elapsed);
        public float EvaluateY(double elapsed) => fromY + (toY - fromY) * Progress(elapsed);
        public (float R,float G,float B) EvaluateColor(double elapsed)
        {
            var progress=Progress(elapsed);
            return (from+(to-from)*progress, fromY+(toY-fromY)*progress, fromZ+(toZ-fromZ)*progress);
        }

        public static bool TryParse(string text, float bpm, AlphaValueSpec spec,
            out NoteValueTransition transition)
        {
            transition = null;
            if (text.Length < 2 || text[0] != '(' || text[text.Length - 1] != ')' ||
                !AlphaCommandGrammar.TrySplitValues(text.Substring(1, text.Length - 2), out var args) ||
                args.Count < 4 || args.Count > 5)
                return false;
            var easing = args[3].ToLowerInvariant();
            if (Array.IndexOf(Functions, easing) < 0) return false;
            var fromUntinted = spec.kind == AlphaValueKind.Color && spec.IsReset(args[0]);
            if (!AlphaCommandGrammar.TryParseDuration(args[2], bpm, out var duration) || duration < 0f ||
                !spec.Matches(args[0], bpm) || !spec.Matches(args[1], bpm) ||
                (!fromUntinted && spec.IsReset(args[0])) || spec.IsReset(args[1])) return false;
            var parameter = 2f;
            if (args.Count == 5 &&
                (easing is not ("easein" or "easeout" or "easeinout") || !float.TryParse(args[4], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out parameter) ||
                 float.IsNaN(parameter) || parameter < .1f || parameter > 100f)) return false;
            if (!TryValue(fromUntinted ? args[1] : args[0], bpm, spec.kind, out var from, out var fromY, out var fromZ) ||
                !TryValue(args[1], bpm, spec.kind, out var to, out var toY, out var toZ)) return false;
            transition = new NoteValueTransition
            {
                from = from, to = to, fromY = fromY, toY = toY, fromZ=fromZ, toZ=toZ,
                duration = duration, easing = easing, parameter = parameter, fromUntinted = fromUntinted
            };
            return true;
        }

        private static bool TryValue(string text, float bpm, AlphaValueKind kind,
            out float x, out float y, out float z)
        {
            x = y = z = 0f;
            if (kind == AlphaValueKind.Color)
            {
                var value=AlphaCommandGrammar.Unwrap(text).TrimStart('#');
                if (value.Length!=6 || !uint.TryParse(value,NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,out var rgb)) return false;
                x=((rgb>>16)&255)/255f; y=((rgb>>8)&255)/255f; z=(rgb&255)/255f;
                return true;
            }
            if (kind == AlphaValueKind.Scale && AlphaCommandGrammar.TryParseScalePair(text, out x, out y))
                return true;
            var valid = kind == AlphaValueKind.Duration
                ? AlphaCommandGrammar.TryParseDuration(text, bpm, out x)
                : float.TryParse(AlphaCommandGrammar.Unwrap(text), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out x);
            y = x;
            return valid && !float.IsNaN(x) && !float.IsInfinity(x);
        }
    }
}
