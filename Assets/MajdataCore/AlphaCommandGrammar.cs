using System;
using System.Collections.Generic;
using System.Globalization;

namespace MajdataCore
{
    public enum AlphaCommandCategory
    {
        Note,
        Display,
        Filter,
        Media
    }

    // Which parser owns the command at playback. The category above only groups
    // the completion popup; this decides where the command's values end up, and it
    // used to be a name switch inside every parser.
    public enum AlphaCommandKind
    {
        Sv,
        Hs,
        Spawn,
        SpawnMode,
        Bounce,
        Destroy,
        Fake,
        Color,
        Size,
        Alpha,
        JudgeLine,
        Subtitle,
        Display,
        Effect,
        Media
    }

    public enum AlphaValueKind
    {
        Number,
        Duration,
        Color,
        Boolean,
        Keyword,
        Scale,
        Text,
        MediaPath,
        ComboMode
    }

    // Which argument list a positional command was written in. The first argument
    // decides it, because that is how the runtime reads them: "Instant" starts a
    // one-shot envelope, True and False switch a stateful command, and anything
    // else is one of the two older forms.
    public enum AlphaArgumentFormKind
    {
        Plain,
        Instant,
        StateOn,
        StateOff
    }

    // What one argument may contain. The accepted range, the NULL spelling and the
    // keyword list used to be repeated by the runtime parser, the syntax check and
    // the completion popup, so a command could play fine while being flagged as an
    // error, or be silently dropped with no error at all.
    public sealed class AlphaValueSpec
    {
        private static readonly string[] NoWords = new string[0];

        public AlphaValueKind kind = AlphaValueKind.Number;
        public float minimum = float.NegativeInfinity;
        public float maximum = float.PositiveInfinity;
        // BOUNCE only accepts a positive round trip; zero would never move.
        public bool positiveOnly;
        public bool allowsNull;
        // BOUNCE spells its reset as either NULL or FALSE.
        public bool allowsFalseReset;
        public string[] keywords = NoWords;
        public string[] extensions = NoWords;
        // The judge line also accepts RRGGBBAA.
        public bool allowsAlphaChannel;
        // FAKE accepts 1 and 0 next to TRUE and FALSE.
        public bool allowsNumericBoolean;
        // SHAKE lets the direction stay empty to set only the transition.
        public bool allowsEmpty;
        public bool requiresQuotes;
        public bool optional;
        public string defaultText = string.Empty;

        public bool IsReset(string value)
        {
            var text = (value ?? string.Empty).Trim();
            if (allowsNull && string.Equals(text, "NULL", StringComparison.OrdinalIgnoreCase))
                return true;
            return allowsFalseReset &&
                   string.Equals(text, "FALSE", StringComparison.OrdinalIgnoreCase);
        }

        public bool Matches(string value, float bpm)
        {
            var text = (value ?? string.Empty).Trim();
            if (text.Length == 0)
                return allowsEmpty;
            if (IsReset(text))
                return true;

            switch (kind)
            {
                case AlphaValueKind.Number:
                    return TryNumber(text, out _);
                case AlphaValueKind.Duration:
                    float seconds;
                    if (!AlphaCommandGrammar.TryParseDuration(text, bpm, out seconds))
                        return false;
                    return !positiveOnly || seconds > 0f;
                case AlphaValueKind.Color:
                    return IsColor(text);
                case AlphaValueKind.Boolean:
                    if (allowsNumericBoolean && (text == "1" || text == "0"))
                        return true;
                    return string.Equals(text, "TRUE", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(text, "FALSE", StringComparison.OrdinalIgnoreCase);
                case AlphaValueKind.Keyword:
                    return IndexOfKeyword(text) >= 0;
                case AlphaValueKind.Scale:
                    float x, y;
                    return AlphaCommandGrammar.TryParseScalePair(text, out x, out y) ||
                           TryNumber(text, out _);
                case AlphaValueKind.Text:
                    return !requiresQuotes ||
                           (text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"');
                case AlphaValueKind.MediaPath:
                    return IsMediaPath(text);
                case AlphaValueKind.ComboMode:
                    int mode;
                    return AlphaCommandGrammar.TryParseComboMode(text, out mode);
                default:
                    return false;
            }
        }

        public int IndexOfKeyword(string value)
        {
            for (var index = 0; index < keywords.Length; index++)
                if (string.Equals(keywords[index], value, StringComparison.OrdinalIgnoreCase))
                    return index;
            return -1;
        }

        private bool TryNumber(string text, out float value)
        {
            if (!float.TryParse(
                    text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                float.IsNaN(value) || float.IsInfinity(value))
                return false;
            return value >= minimum && value <= maximum;
        }

        private bool IsColor(string text)
        {
            var digits = text.TrimStart('#');
            if (digits.Length != 6 && !(allowsAlphaChannel && digits.Length == 8))
                return false;
            foreach (var digit in digits)
                if (!Uri.IsHexDigit(digit))
                    return false;
            return true;
        }

        private bool IsMediaPath(string text)
        {
            var path = text.Trim().Trim('"').Replace('\\', '/');
            if (path.Length == 0 || path[0] == '/' || path.IndexOf(':') >= 0)
                return false;
            foreach (var part in path.Split('/'))
                if (part == "..")
                    return false;
            foreach (var extension in extensions)
                if (path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        public string Describe(bool chinese)
        {
            var text = DescribeKind(chinese);
            if (allowsNull)
                text += chinese ? "，或 NULL" : ", OR NULL";
            if (allowsFalseReset)
                text += chinese ? "，或 FALSE" : ", OR FALSE";
            return text;
        }

        private string DescribeKind(bool chinese)
        {
            switch (kind)
            {
                case AlphaValueKind.Number:
                    return DescribeRange(chinese);
                case AlphaValueKind.Duration:
                    if (chinese)
                        return positiveOnly
                            ? "大于 0 的时长（秒数或 8:1）"
                            : "时长（秒数或 8:1）";
                    return positiveOnly
                        ? "A DURATION ABOVE 0, IN SECONDS OR BEATS SUCH AS 8:1"
                        : "A DURATION IN SECONDS OR BEATS SUCH AS 8:1";
                case AlphaValueKind.Color:
                    if (chinese)
                        return allowsAlphaChannel ? "RRGGBB 或 RRGGBBAA 颜色" : "RRGGBB 颜色";
                    return allowsAlphaChannel
                        ? "AN RRGGBB OR RRGGBBAA COLOR"
                        : "AN RRGGBB COLOR";
                case AlphaValueKind.Boolean:
                    if (chinese)
                        return allowsNumericBoolean ? "TRUE、FALSE、1 或 0" : "True 或 False";
                    return allowsNumericBoolean ? "TRUE, FALSE, 1 OR 0" : "TRUE OR FALSE";
                case AlphaValueKind.Keyword:
                    return string.Join(chinese ? " 或 " : " OR ", keywords);
                case AlphaValueKind.Scale:
                    return chinese ? "倍率数字或 (x,y)" : "A SCALE NUMBER OR (X,Y)";
                case AlphaValueKind.Text:
                    return requiresQuotes
                        ? (chinese ? "双引号内的文本" : "TEXT INSIDE DOUBLE QUOTES")
                        : (chinese ? "文本" : "TEXT");
                case AlphaValueKind.MediaPath:
                    return chinese
                        ? "谱面目录内的相对路径（" + string.Join("、", extensions) + "）"
                        : "A RELATIVE PATH INSIDE THE CHART FOLDER (" +
                          string.Join(", ", extensions) + ")";
                case AlphaValueKind.ComboMode:
                    return chinese
                        ? "显示模式，例如 Combo、DxScore、Achievement、None"
                        : "A DISPLAY MODE SUCH AS COMBO, DXSCORE, ACHIEVEMENT OR NONE";
                default:
                    return chinese ? "参数" : "AN ARGUMENT";
            }
        }

        private string DescribeRange(bool chinese)
        {
            var hasMinimum = !float.IsNegativeInfinity(minimum);
            var hasMaximum = !float.IsPositiveInfinity(maximum);
            if (!hasMinimum && !hasMaximum)
                return chinese ? "数字" : "A NUMBER";
            var low = minimum.ToString("0.###", CultureInfo.InvariantCulture);
            var high = maximum.ToString("0.###", CultureInfo.InvariantCulture);
            if (hasMinimum && hasMaximum)
                return chinese
                    ? low + " 到 " + high + " 的数字"
                    : "A NUMBER FROM " + low + " TO " + high;
            if (hasMinimum)
                return chinese ? "不小于 " + low + " 的数字" : "A NUMBER OF AT LEAST " + low;
            return chinese ? "不大于 " + high + " 的数字" : "A NUMBER OF AT MOST " + high;
        }
    }

    // One accepted argument list. Trailing slots may be optional; a command whose
    // duration comes last declares one form per length instead, because there the
    // meaning of a slot depends on how many were written.
    public sealed class AlphaArgumentForm
    {
        public AlphaArgumentFormKind kind = AlphaArgumentFormKind.Plain;
        public AlphaValueSpec[] slots = new AlphaValueSpec[0];

        public int MinimumCount
        {
            get
            {
                var count = 0;
                for (var index = 0; index < slots.Length; index++)
                    if (!slots[index].optional)
                        count = index + 1;
                return count;
            }
        }

        public int MaximumCount { get { return slots.Length; } }

        public bool Accepts(int count)
        {
            return count >= MinimumCount && count <= MaximumCount;
        }
    }

    public sealed class AlphaCommandDescriptor
    {
        private static readonly string[] NoWords = new string[0];
        private static readonly AlphaArgumentForm[] NoForms = new AlphaArgumentForm[0];

        // How the command is written and offered in the editor.
        public string name = string.Empty;
        // What the player calls the same thing, for example FADE drives "Flash".
        public string canonical = string.Empty;
        public AlphaCommandCategory category = AlphaCommandCategory.Note;
        public AlphaCommandKind kind = AlphaCommandKind.Sv;
        // Display, filter and media commands read their arguments from "(...)".
        public bool requiresParentheses;
        // Typed keys such as "tap=" or "slide="; empty when the command has none.
        public string[] targets = NoWords;
        // The bare body, for example "<SPAWN*1.2>".
        public AlphaValueSpec? scalar;
        // The value after a typed key; falls back to the bare body's rules.
        public AlphaValueSpec? typedValue;
        public AlphaArgumentForm[] forms = NoForms;
        // "<SPAWNMODE*(Once)>" means the same as "<SPAWNMODE*Once>".
        public bool allowsParenthesizedScalar;
        // A caption may contain commas and brackets, so nothing in it is an error.
        public bool acceptsAnyBody;
        // FADE is FLASH aimed at black: the strength is applied negatively.
        public bool negatesIntensity;

        public string Canonical
        {
            get { return canonical.Length > 0 ? canonical : name; }
        }

        public bool SupportsTargets { get { return targets.Length > 0; } }

        public AlphaValueSpec? TypedValueSpec
        {
            get { return typedValue ?? scalar; }
        }

        // The editor inserts "NAME*()" for everything that is not a note command.
        public bool InsertsParentheses
        {
            get { return category != AlphaCommandCategory.Note; }
        }

        // Only offered where NULL is valid on its own, so the completion popup
        // never writes text the parser would reject.
        public bool SupportsNullReset
        {
            get
            {
                if (scalar != null)
                    return scalar.allowsNull;
                for (var index = 0; index < forms.Length; index++)
                {
                    var slots = forms[index].slots;
                    if (slots.Length > 0 && slots[0].allowsNull)
                        return true;
                }
                return false;
            }
        }

        public bool HasTarget(string key)
        {
            for (var index = 0; index < targets.Length; index++)
                if (string.Equals(targets[index], key, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        public bool Matches(string commandName)
        {
            return string.Equals(name, commandName, StringComparison.OrdinalIgnoreCase);
        }

        public AlphaArgumentForm? FormFor(AlphaArgumentFormKind kind, int count)
        {
            for (var index = 0; index < forms.Length; index++)
                if (forms[index].kind == kind && forms[index].Accepts(count))
                    return forms[index];
            return null;
        }

        private bool HasStateForms
        {
            get
            {
                for (var index = 0; index < forms.Length; index++)
                    if (forms[index].kind != AlphaArgumentFormKind.Plain)
                        return true;
                return false;
            }
        }

        // How many arguments the editor may still add. `enabled` is what the first
        // argument says so far: false selects the short "switch it off" form.
        public int MaximumArgumentCount(bool? enabled)
        {
            if (forms.Length == 0)
                return 1;
            var stateful = HasStateForms;
            var maximum = 0;
            for (var index = 0; index < forms.Length; index++)
            {
                var form = forms[index];
                // The older comma forms stay parseable but are not offered.
                if (stateful && form.kind == AlphaArgumentFormKind.Plain)
                    continue;
                if (enabled == true && form.kind == AlphaArgumentFormKind.StateOff)
                    continue;
                if (enabled == false &&
                    (form.kind == AlphaArgumentFormKind.StateOn ||
                     form.kind == AlphaArgumentFormKind.Instant))
                    continue;
                if (form.MaximumCount > maximum)
                    maximum = form.MaximumCount;
            }
            return maximum == 0 ? 1 : maximum;
        }

        public string? DefaultArgument(int parameterIndex, bool? enabled)
        {
            if (forms.Length == 0)
                return parameterIndex == 1 && scalar != null && scalar.defaultText.Length > 0
                    ? scalar.defaultText
                    : null;

            var wanted = enabled == false
                ? AlphaArgumentFormKind.StateOff
                : AlphaArgumentFormKind.StateOn;
            var form = LongestForm(wanted) ?? LongestForm(AlphaArgumentFormKind.Plain);
            if (form == null || parameterIndex < 1 || parameterIndex > form.slots.Length)
                return null;
            var text = form.slots[parameterIndex - 1].defaultText;
            return text.Length > 0 ? text : null;
        }

        private AlphaArgumentForm? LongestForm(AlphaArgumentFormKind kind)
        {
            AlphaArgumentForm? best = null;
            for (var index = 0; index < forms.Length; index++)
            {
                var form = forms[index];
                if (form.kind != kind)
                    continue;
                if (best == null || form.MaximumCount > best.MaximumCount)
                    best = form;
            }
            return best;
        }
    }

    // The command vocabulary and the shape of every argument list, in one place.
    // The runtime reads it to know that an angle command is a command at all, the
    // syntax check reads it to report a bad one, and the completion popup reads it
    // to offer the same names and defaults the parser accepts.
    public static class AlphaCommandGrammar
    {
        // "mine" is a target of its own rather than a variant of the note it sits on,
        // the way "break" and "each" are, and it is read before either of those: a
        // mine is the thing the player must not touch, so a chart that colours or
        // slows mines means every mine.
        private static readonly string[] StateNoteTypes =
        {
            "tap", "each", "hold", "slide", "star", "break", "mine", "touch",
            "touchhold"
        };
        // A typed slide value controls the guide-star fade. Global HS still only
        // controls falling notes; slide path motion comes from SV*slide.
        private static readonly string[] HsNoteTypes =
        {
            "tap", "each", "hold", "slide", "star", "break", "mine", "touch",
            "touchhold"
        };
        private static readonly string[] VisualNoteTypes =
        {
            "tap", "each", "hold", "slide", "star", "break", "mine", "touch",
            "touchhold", "slidestar"
        };
        // Only ring notes travel, so only they have a spawn or destroy radius.
        private static readonly string[] RingNoteTypes =
        {
            "tap", "each", "hold", "star", "break", "mine"
        };

        private static readonly Dictionary<string, int> ComboModes =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "NONE", 0 },
                { "OFF", 0 },
                { "COMBO", 1 },
                { "SCORE", 2 },
                { "SCORECLASSIC", 2 },
                { "ACHIEVEMENT", 3 },
                { "ACC", 3 },
                { "ACHIEVEMENTCLASSIC", 3 },
                { "ACCDOWN", 4 },
                { "ACHIEVEMENTDOWNCLASSIC", 4 },
                { "DXACC", 11 },
                { "ACHIEVEMENTDELUXE", 11 },
                { "DXACCDOWN", 12 },
                { "ACHIEVEMENTDOWNDELUXE", 12 },
                { "DXSCORE", 13 },
                { "SCOREDELUXE", 13 },
                { "CSCORE", 101 },
                { "CSCOREDEDX", 101 },
                { "CSCOREDEDELUXE", 101 },
                { "CSCOREDEDXDOWN", 102 },
                { "CSCOREDOWNDEDELUXE", 102 }
            };

        private static readonly AlphaCommandDescriptor[] Descriptors = BuildDescriptors();

        public static IReadOnlyList<AlphaCommandDescriptor> Commands
        {
            get { return Descriptors; }
        }

        public static bool TryFind(string commandName, out AlphaCommandDescriptor? descriptor)
        {
            descriptor = null;
            if (string.IsNullOrEmpty(commandName))
                return false;
            var name = commandName.Trim();
            for (var index = 0; index < Descriptors.Length; index++)
                if (Descriptors[index].Matches(name))
                {
                    descriptor = Descriptors[index];
                    return true;
                }
            return false;
        }

        // Splits the text between "<" and ">" into its name and its body.
        public static bool TrySplitToken(string token, out string name, out string body)
        {
            name = string.Empty;
            body = string.Empty;
            if (string.IsNullOrEmpty(token))
                return false;
            var separator = token.IndexOf('*');
            if (separator <= 0)
                return false;
            name = token.Substring(0, separator).Trim();
            body = token.Substring(separator + 1).Trim();
            return name.Length > 0;
        }

        public static bool TryReadCommand(string token, out AlphaCommandDescriptor? descriptor)
        {
            descriptor = null;
            string name, body;
            return TrySplitToken(token, out name, out body) && TryFind(name, out descriptor);
        }

        public static bool TryValidate(string token, float bpm, out string error)
        {
            AlphaCommandDescriptor? descriptor;
            return TryValidate(token, bpm, out descriptor, out error);
        }

        public static bool TryValidate(
            string token,
            float bpm,
            out AlphaCommandDescriptor? descriptor,
            out string error)
        {
            descriptor = null;
            error = string.Empty;
            string name, body;
            if (!TrySplitToken(token, out name, out body))
            {
                error = Diagnose(
                    "Alpha 命令要写成 <命令*参数>",
                    "AN ALPHA COMMAND IS WRITTEN AS <COMMAND*ARGUMENTS>",
                    token ?? string.Empty);
                return false;
            }

            if (!TryFind(name, out var found))
            {
                error = Diagnose(
                    "不存在的 Alpha 命令「" + name + "」",
                    "UNKNOWN ALPHA COMMAND '" + name.ToUpperInvariant() + "'",
                    token);
                return false;
            }

            descriptor = found;
            if (found!.kind == AlphaCommandKind.Subtitle)
                return TryValidateSubtitle(body, bpm, token, out error);
            if (found!.acceptsAnyBody)
                return true;
            return found.forms.Length > 0
                ? TryValidatePositional(found, body, bpm, token, out error)
                : TryValidateValues(found, body, bpm, token, out error);
        }

        private static bool TryValidateSubtitle(
            string body,
            float bpm,
            string token,
            out string error)
        {
            error = string.Empty;
            if (body.Length < 4 || body[0] != '(' || body[body.Length - 1] != ')')
            {
                error = Diagnose(
                    "TEXT 参数必须写在括号内",
                    "TEXT PARAMETERS MUST BE WRAPPED IN PARENTHESES",
                    token);
                return false;
            }

            if (!TrySplitValues(body.Substring(1, body.Length - 2), out var values) ||
                values.Count is < 1 or > 9 ||
                values[0].Length < 2 ||
                values[0][0] != '"' ||
                values[0][values[0].Length - 1] != '"')
            {
                error = Diagnose(
                    "TEXT 需要双引号字幕，后面可写持续时间、x、y 和字号",
                    "TEXT REQUIRES QUOTED CONTENT FOLLOWED BY OPTIONAL DURATION, X, Y AND SIZE",
                    token);
                return false;
            }

            var positional = values.Count > 1;
            for (var index = 1; index < values.Count; index++)
                positional &= values[index].IndexOf('=') < 0;
            if (positional)
            {
                if (values.Count > 9 ||
                    values[1].Length > 0 && !TryParseDuration(values[1], bpm, out _) ||
                    values.Count > 2 && values[2].Length > 0 &&
                    !TryFiniteNumber(values[2], 0f, 1f) ||
                    values.Count > 3 && values[3].Length > 0 &&
                    !TryFiniteNumber(values[3], 0f, 1f) ||
                    values.Count > 4 && values[4].Length > 0 &&
                    !TryFiniteNumber(values[4], 8f, 200f) ||
                    values.Count > 5 && values[5].Length > 0 &&
                    !TryNormalizeSubtitleFont(values[5], out _) ||
                    values.Count > 6 && values[6].Length > 0 &&
                    (!int.TryParse(values[6], NumberStyles.Integer,
                         CultureInfo.InvariantCulture, out var subtitleIndex) ||
                     subtitleIndex < 0) ||
                    values.Count > 7 && values[7].Length > 0 &&
                    !TryNormalizeSubtitleStyle(values[7], out _) ||
                    values.Count > 8 && values[8].Length > 0 &&
                    !TryParseDuration(values[8], bpm, out _))
                {
                    error = Diagnose(
                        "TEXT 位置参数格式为（\"内容\"，持续时间，x，y，字号，字体，索引，样式，过渡时间）",
                        "TEXT POSITIONAL FORM IS (\"CONTENT\", DURATION, X, Y, SIZE, FONT, INDEX, STYLE, TRANSITION)",
                        token);
                    return false;
                }
                return true;
            }

            var durationSeen = false;
            var optionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 1; index < values.Count; index++)
            {
                var value = values[index];
                var equals = value.IndexOf('=');
                if (equals > 0)
                {
                    var key = value.Substring(0, equals).Trim().ToLowerInvariant();
                    var option = value.Substring(equals + 1).Trim();
                    if (!optionKeys.Add(key))
                    {
                        error = Diagnose(
                            "TEXT 的命名参数不能重复",
                            "TEXT NAMED PARAMETERS CANNOT BE REPEATED",
                            token);
                        return false;
                    }
                    var valid = key switch
                    {
                        "x" or "y" => TryFiniteNumber(option, 0f, 1f),
                        "size" => TryFiniteNumber(option, 8f, 200f),
                        "font" => TryNormalizeSubtitleFont(option, out _),
                        "index" => int.TryParse(option, NumberStyles.Integer,
                                       CultureInfo.InvariantCulture, out var subtitleIndex) &&
                                   subtitleIndex >= 0,
                        "style" => TryNormalizeSubtitleStyle(option, out _),
                        "transition" => TryParseDuration(option, bpm, out _),
                        _ => false
                    };
                    if (valid)
                        continue;
                    error = Diagnose(
                        "TEXT 命名参数格式错误；支持 x、y、size、font、index、style 和 transition",
                        "INVALID TEXT NAMED PARAMETER; USE X, Y, SIZE, FONT, INDEX, STYLE OR TRANSITION",
                        token);
                    return false;
                }

                if (durationSeen || !TryParseDuration(value, bpm, out _))
                {
                    error = Diagnose(
                        "TEXT 最多只能写一个持续时间",
                        "TEXT ACCEPTS AT MOST ONE DURATION",
                        token);
                    return false;
                }
                durationSeen = true;
            }
            return true;
        }

        public static bool TryNormalizeSubtitleFont(
            string value, out string normalized)
        {
            normalized = string.Empty;
            var compact = (value ?? string.Empty).Trim()
                .Replace(" ", string.Empty)
                .Replace("-", string.Empty)
                .ToUpperInvariant();
            normalized = compact switch
            {
                "DEFAULT" => "Default",
                "CASCADIAMONO" => "CascadiaMono",
                "CASCADIACODE" => "CascadiaCode",
                "MICROSOFTYAHEI" or "YAHEI" => "MicrosoftYaHei",
                "NOTOSANSSC" => "NotoSansSC",
                "SIMSUN" => "SimSun",
                "DENGXIAN" => "DengXian",
                "NOTOSERIFSC" => "NotoSerifSC",
                "GLOBALMONOSPACE" => "GlobalMonospace",
                "AILERON" => "Aileron",
                "ALLERTA" => "Allerta",
                _ => string.Empty
            };
            return normalized.Length != 0;
        }

        public static bool TryNormalizeSubtitleStyle(
            string value, out string normalized)
        {
            normalized = (value ?? string.Empty).Trim();
            if (normalized.Equals("Default", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("Fade", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "Fade";
                return true;
            }
            if (normalized.Equals("Typewriter", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "Typewriter";
                return true;
            }
            normalized = string.Empty;
            return false;
        }

        private static bool TryFiniteNumber(
            string text, float minimum, float maximum)
        {
            return float.TryParse(
                       text.Trim(),
                       NumberStyles.Float,
                       CultureInfo.InvariantCulture,
                       out var value) &&
                   !float.IsNaN(value) &&
                   !float.IsInfinity(value) &&
                   value >= minimum && value <= maximum;
        }

        private static bool TryValidateValues(
            AlphaCommandDescriptor descriptor,
            string body,
            float bpm,
            string token,
            out string error)
        {
            error = string.Empty;
            if (body.Length == 0)
            {
                error = MissingArguments(descriptor, token);
                return false;
            }

            if (body.IndexOf('=') < 0)
            {
                var value = descriptor.allowsParenthesizedScalar ? Unwrap(body) : body;
                if (descriptor.scalar == null)
                {
                    error = Diagnose(
                        descriptor.name + " 必须写成「目标=值」，例如 " +
                        descriptor.name + "*" + descriptor.targets[0] + "=…",
                        descriptor.name + " NEEDS A TYPED ARGUMENT SUCH AS " +
                        descriptor.targets[0] + "=…",
                        token);
                    return false;
                }
                if (!descriptor.scalar.Matches(value, bpm))
                {
                    error = BadValue(descriptor, descriptor.scalar, value, token);
                    return false;
                }
                return true;
            }

            if (!descriptor.SupportsTargets)
            {
                error = Diagnose(
                    descriptor.name + " 不支持「目标=值」写法",
                    descriptor.name + " DOES NOT ACCEPT TYPED ARGUMENTS",
                    token);
                return false;
            }

            List<string> pairs;
            if (!TrySplitValues(body, out pairs))
            {
                error = Diagnose(
                    descriptor.name + " 的参数括号没有配对",
                    descriptor.name + " HAS UNBALANCED PARENTHESES",
                    token);
                return false;
            }

            var spec = descriptor.TypedValueSpec;
            if (spec == null)
                return true;
            foreach (var pair in pairs)
            {
                var separator = pair.IndexOf('=');
                if (separator <= 0)
                {
                    error = Diagnose(
                        descriptor.name + " 的每一项都要写成「目标=值」",
                        "EVERY " + descriptor.name + " ITEM MUST BE WRITTEN AS TARGET=VALUE",
                        token);
                    return false;
                }
                var key = pair.Substring(0, separator).Trim();
                var value = pair.Substring(separator + 1).Trim();
                if (!descriptor.HasTarget(key))
                {
                    error = Diagnose(
                        descriptor.name + " 不支持目标「" + key + "」，可用：" +
                        string.Join("、", descriptor.targets),
                        descriptor.name + " DOES NOT SUPPORT TARGET '" + key +
                        "'; AVAILABLE: " + string.Join(", ", descriptor.targets),
                        token);
                    return false;
                }
                if (descriptor.allowsParenthesizedScalar)
                    value = Unwrap(value);
                if (!spec.Matches(value, bpm))
                {
                    error = BadValue(descriptor, spec, value, token);
                    return false;
                }
            }
            return true;
        }

        private static bool TryValidatePositional(
            AlphaCommandDescriptor descriptor,
            string body,
            float bpm,
            string token,
            out string error)
        {
            error = string.Empty;
            var parenthesized = body.Length >= 2 && body[0] == '(' &&
                                body[body.Length - 1] == ')';
            if (descriptor.requiresParentheses && !parenthesized)
            {
                error = Diagnose(
                    descriptor.name + " 的参数必须写在括号里，例如 " + descriptor.name + "*(…)",
                    descriptor.name + " ARGUMENTS MUST BE WRAPPED IN PARENTHESES SUCH AS " +
                    descriptor.name + "*(…)",
                    token);
                return false;
            }

            var inner = parenthesized ? body.Substring(1, body.Length - 2).Trim() : body;
            if (inner.Length == 0)
            {
                error = MissingArguments(descriptor, token);
                return false;
            }

            List<string> values;
            if (!TrySplitValues(inner, out values))
            {
                error = Diagnose(
                    descriptor.name + " 的参数括号没有配对",
                    descriptor.name + " HAS UNBALANCED PARENTHESES",
                    token);
                return false;
            }

            var kind = ClassifyForm(values[0]);
            var form = descriptor.FormFor(kind, values.Count);
            if (form == null)
            {
                error = ArityError(descriptor, kind, values.Count, token);
                return false;
            }

            for (var index = 0; index < values.Count; index++)
            {
                var slot = form.slots[index];
                if (slot.Matches(values[index], bpm))
                    continue;
                error = BadValue(descriptor, slot, values[index], token);
                return false;
            }
            return true;
        }

        public static AlphaArgumentFormKind ClassifyForm(string first)
        {
            var value = (first ?? string.Empty).Trim();
            if (string.Equals(value, "Instant", StringComparison.OrdinalIgnoreCase))
                return AlphaArgumentFormKind.Instant;
            bool enabled;
            if (bool.TryParse(value, out enabled))
                return enabled ? AlphaArgumentFormKind.StateOn : AlphaArgumentFormKind.StateOff;
            return AlphaArgumentFormKind.Plain;
        }

        private static string MissingArguments(AlphaCommandDescriptor descriptor, string token)
        {
            return Diagnose(
                descriptor.name + " 缺少参数",
                descriptor.name + " IS MISSING ITS ARGUMENTS",
                token);
        }

        private static string BadValue(
            AlphaCommandDescriptor descriptor,
            AlphaValueSpec spec,
            string value,
            string token)
        {
            var shown = value.Length == 0 ? "（空）" : value;
            return Diagnose(
                descriptor.name + " 的参数「" + shown + "」不合法，需要" + spec.Describe(true),
                descriptor.name + " ARGUMENT '" + value + "' IS INVALID; EXPECTED " +
                spec.Describe(false),
                token);
        }

        private static string ArityError(
            AlphaCommandDescriptor descriptor,
            AlphaArgumentFormKind kind,
            int count,
            string token)
        {
            var minimum = int.MaxValue;
            var maximum = 0;
            for (var index = 0; index < descriptor.forms.Length; index++)
            {
                var form = descriptor.forms[index];
                if (form.kind != kind)
                    continue;
                if (form.MinimumCount < minimum)
                    minimum = form.MinimumCount;
                if (form.MaximumCount > maximum)
                    maximum = form.MaximumCount;
            }

            if (maximum == 0)
                return Diagnose(
                    descriptor.name + " 不支持这种写法",
                    descriptor.name + " DOES NOT ACCEPT THIS FORM",
                    token);

            var written = count.ToString(CultureInfo.InvariantCulture);
            var low = minimum.ToString(CultureInfo.InvariantCulture);
            var high = maximum.ToString(CultureInfo.InvariantCulture);
            var wanted = minimum == maximum ? low : low + " 到 " + high;
            var wantedEnglish = minimum == maximum ? low : low + " TO " + high;
            return Diagnose(
                descriptor.name + " 需要 " + wanted + " 个参数，现在写了 " + written + " 个",
                descriptor.name + " NEEDS " + wantedEnglish + " ARGUMENTS BUT GOT " + written,
                token);
        }

        internal static string Diagnose(string chinese, string english, string token)
        {
            var message = ParserMessageLocale.Pick(chinese, english);
            if (string.IsNullOrEmpty(token))
                return message;
            var separator = ParserMessageLocale.PreferChinese ? "：<" : ": <";
            return message + separator + token + ">";
        }

        public static string Unwrap(string value)
        {
            var text = (value ?? string.Empty).Trim();
            return text.Length >= 2 && text[0] == '(' && text[text.Length - 1] == ')'
                ? text.Substring(1, text.Length - 2).Trim()
                : text;
        }

        // Commas at depth zero separate arguments; a nested "(x,y)" stays together.
        public static bool TrySplitValues(string text, out List<string> values)
        {
            values = new List<string>();
            if (text == null)
                return false;
            var depth = 0;
            var quoted = false;
            var start = 0;
            for (var index = 0; index < text.Length; index++)
            {
                var character = text[index];
                if (character == '"' && (index == 0 || text[index - 1] != '\\'))
                {
                    quoted = !quoted;
                    continue;
                }
                if (quoted)
                    continue;
                if (character == '(')
                    depth++;
                else if (character == ')')
                {
                    if (depth == 0)
                        return false;
                    depth--;
                }
                else if (character == ',' && depth == 0)
                {
                    values.Add(text.Substring(start, index - start).Trim());
                    start = index + 1;
                }
            }
            if (depth != 0 || quoted)
                return false;
            values.Add(text.Substring(start).Trim());
            return true;
        }

        public static bool TryParseDuration(string value, float bpm, out float seconds)
        {
            seconds = 0f;
            if (value == null)
                return false;
            var text = value.Trim();
            if (float.TryParse(
                    text, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds))
                return !float.IsNaN(seconds) && !float.IsInfinity(seconds);

            var parts = text.Split(':');
            int division, count;
            if (parts.Length != 2 || bpm <= 0f ||
                !int.TryParse(
                    parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out division) ||
                !int.TryParse(
                    parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out count) ||
                division <= 0 || count < 0)
            {
                seconds = 0f;
                return false;
            }

            seconds = 60f / bpm * 4f / division * count;
            return true;
        }

        public static bool TryParseScalePair(string value, out float x, out float y)
        {
            x = 1f;
            y = 1f;
            if (value == null)
                return false;
            var text = value.Trim();
            if (text.Length < 3 || text[0] != '(' || text[text.Length - 1] != ')')
                return false;
            var parts = text.Substring(1, text.Length - 2).Split(',');
            return parts.Length == 2 &&
                   float.TryParse(
                       parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture,
                       out x) &&
                   float.TryParse(
                       parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture,
                       out y) &&
                   !float.IsNaN(x) && !float.IsInfinity(x) &&
                   !float.IsNaN(y) && !float.IsInfinity(y);
        }

        // Accepts the aliases, the enum member spellings and the numbers the player
        // knows. An unknown number used to be taken as a mode and then displayed
        // nothing at all.
        public static bool TryParseComboMode(string value, out int mode)
        {
            mode = 0;
            if (string.IsNullOrEmpty(value))
                return false;
            var text = value.Trim().Replace(" ", string.Empty).Replace("_", string.Empty);
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out mode))
                return IsKnownComboMode(mode);
            if (ComboModes.TryGetValue(text, out mode))
                return true;
            mode = 0;
            return false;
        }

        public static bool IsKnownComboMode(int mode)
        {
            foreach (var known in ComboModes.Values)
                if (known == mode)
                    return true;
            return false;
        }

        private static AlphaValueSpec Number(
            float minimum,
            float maximum,
            bool allowsNull = false,
            string defaultText = "")
        {
            return new AlphaValueSpec
            {
                kind = AlphaValueKind.Number,
                minimum = minimum,
                maximum = maximum,
                allowsNull = allowsNull,
                defaultText = defaultText
            };
        }

        private static AlphaValueSpec Number(
            bool allowsNull = false,
            string defaultText = "")
        {
            return Number(
                float.NegativeInfinity, float.PositiveInfinity, allowsNull, defaultText);
        }

        private static AlphaValueSpec Duration(
            bool optional = false,
            bool positiveOnly = false,
            bool allowsNull = false,
            bool allowsFalseReset = false,
            string defaultText = "8:1")
        {
            return new AlphaValueSpec
            {
                kind = AlphaValueKind.Duration,
                optional = optional,
                positiveOnly = positiveOnly,
                allowsNull = allowsNull,
                allowsFalseReset = allowsFalseReset,
                defaultText = defaultText
            };
        }

        private static AlphaValueSpec Color(
            bool allowsNull = false,
            bool allowsAlphaChannel = false,
            string defaultText = "FF6699")
        {
            return new AlphaValueSpec
            {
                kind = AlphaValueKind.Color,
                allowsNull = allowsNull,
                allowsAlphaChannel = allowsAlphaChannel,
                defaultText = defaultText
            };
        }

        private static AlphaValueSpec Boolean(
            bool allowsNumericBoolean = false,
            string defaultText = "True")
        {
            return new AlphaValueSpec
            {
                kind = AlphaValueKind.Boolean,
                allowsNumericBoolean = allowsNumericBoolean,
                defaultText = defaultText
            };
        }

        private static AlphaValueSpec Scale(string defaultText)
        {
            return new AlphaValueSpec
            {
                kind = AlphaValueKind.Scale,
                allowsNull = true,
                defaultText = defaultText
            };
        }

        private static AlphaArgumentForm Form(
            AlphaArgumentFormKind kind, params AlphaValueSpec[] slots)
        {
            return new AlphaArgumentForm { kind = kind, slots = slots };
        }

        private static AlphaCommandDescriptor[] BuildDescriptors()
        {
            var commands = new List<AlphaCommandDescriptor>
            {
                new AlphaCommandDescriptor
                {
                    name = "SV",
                    kind = AlphaCommandKind.Sv,
                    targets = StateNoteTypes,
                    scalar = Number(defaultText: "1"),
                    typedValue = Number(allowsNull: true)
                },
                new AlphaCommandDescriptor
                {
                    name = "HS",
                    kind = AlphaCommandKind.Hs,
                    targets = HsNoteTypes,
                    scalar = Number(defaultText: "1"),
                    typedValue = Number(allowsNull: true)
                },
                new AlphaCommandDescriptor
                {
                    name = "SPAWN",
                    kind = AlphaCommandKind.Spawn,
                    targets = RingNoteTypes,
                    scalar = Number(-4.8f, 4.8f, allowsNull: true, defaultText: "1.225")
                },
                new AlphaCommandDescriptor
                {
                    name = "SPAWNMODE",
                    kind = AlphaCommandKind.SpawnMode,
                    targets = RingNoteTypes,
                    allowsParenthesizedScalar = true,
                    scalar = new AlphaValueSpec
                    {
                        kind = AlphaValueKind.Keyword,
                        keywords = new[] { "Rewind", "Once" },
                        allowsNull = true,
                        defaultText = "Rewind"
                    }
                },
                new AlphaCommandDescriptor
                {
                    name = "BOUNCE",
                    kind = AlphaCommandKind.Bounce,
                    targets = RingNoteTypes,
                    scalar = Duration(
                        positiveOnly: true, allowsNull: true, allowsFalseReset: true)
                },
                new AlphaCommandDescriptor
                {
                    name = "DESTROY",
                    kind = AlphaCommandKind.Destroy,
                    targets = RingNoteTypes,
                    scalar = Number(-20f, 20f, allowsNull: true, defaultText: "4.8")
                },
                new AlphaCommandDescriptor
                {
                    name = "FAKE",
                    kind = AlphaCommandKind.Fake,
                    targets = StateNoteTypes,
                    scalar = Boolean(allowsNumericBoolean: true, defaultText: "TRUE")
                },
                new AlphaCommandDescriptor
                {
                    name = "COLOR",
                    kind = AlphaCommandKind.Color,
                    targets = VisualNoteTypes,
                    scalar = Color(allowsNull: true)
                },
                new AlphaCommandDescriptor
                {
                    name = "COLORV",
                    kind = AlphaCommandKind.Color,
                    targets = VisualNoteTypes,
                    scalar = Color(allowsNull: true)
                },
                new AlphaCommandDescriptor
                {
                    name = "SIZE",
                    kind = AlphaCommandKind.Size,
                    targets = VisualNoteTypes,
                    scalar = Scale("1")
                },
                new AlphaCommandDescriptor
                {
                    name = "SIZEV",
                    kind = AlphaCommandKind.Size,
                    targets = VisualNoteTypes,
                    scalar = Scale("1")
                },
                new AlphaCommandDescriptor
                {
                    name = "ALPHA",
                    kind = AlphaCommandKind.Alpha,
                    targets = VisualNoteTypes,
                    scalar = Number(allowsNull: true, defaultText: "1")
                },
                new AlphaCommandDescriptor
                {
                    name = "ALPHAV",
                    kind = AlphaCommandKind.Alpha,
                    targets = VisualNoteTypes,
                    scalar = Number(allowsNull: true, defaultText: "1")
                },
                new AlphaCommandDescriptor
                {
                    name = "JLINE",
                    kind = AlphaCommandKind.JudgeLine,
                    category = AlphaCommandCategory.Display,
                    forms = new[]
                    {
                        Form(
                            AlphaArgumentFormKind.Plain,
                            Color(allowsNull: true, allowsAlphaChannel: true),
                            Duration(optional: true))
                    }
                },
                new AlphaCommandDescriptor
                {
                    name = "TEXT",
                    kind = AlphaCommandKind.Subtitle,
                    category = AlphaCommandCategory.Display,
                    requiresParentheses = true,
                    forms = new[]
                    {
                        Form(
                            AlphaArgumentFormKind.Plain,
                            new AlphaValueSpec
                            {
                                kind = AlphaValueKind.Text,
                                allowsEmpty = true,
                                requiresQuotes = true,
                                defaultText = "\"字幕\""
                            },
                            Duration(optional: true, defaultText: "2"),
                            new AlphaValueSpec
                            {
                                kind = AlphaValueKind.Text,
                                optional = true,
                                defaultText = "0"
                            },
                            new AlphaValueSpec
                            {
                                kind = AlphaValueKind.Text,
                                optional = true,
                                defaultText = "0"
                            },
                            new AlphaValueSpec
                            {
                                kind = AlphaValueKind.Text,
                                optional = true,
                                defaultText = "32"
                            },
                            new AlphaValueSpec
                            {
                                kind = AlphaValueKind.Text,
                                optional = true,
                                defaultText = "Default"
                            },
                            new AlphaValueSpec
                            {
                                kind = AlphaValueKind.Text,
                                optional = true,
                                defaultText = "0"
                            },
                            new AlphaValueSpec
                            {
                                kind = AlphaValueKind.Text,
                                optional = true,
                                defaultText = "Fade"
                            },
                            new AlphaValueSpec
                            {
                                kind = AlphaValueKind.Text,
                                optional = true,
                                defaultText = "0.3"
                            })
                    }
                }
            };

            var toggles = new[]
            {
                new[] { "SHOWJUDGELINE", "ShowJudgeLine" },
                new[] { "SHOWJUDGEAREA", "ShowJudgeArea" },
                new[] { "SHOWJUDGEINFO", "ShowJudgeInfo" },
                new[] { "SHOWCOMBOINFO", "ShowComboInfo" },
                new[] { "SHOWJUDGETEXT", "ShowJudgeText" }
            };
            foreach (var toggle in toggles)
                commands.Add(new AlphaCommandDescriptor
                {
                    name = toggle[0],
                    canonical = toggle[1],
                    kind = AlphaCommandKind.Display,
                    category = AlphaCommandCategory.Display,
                    requiresParentheses = true,
                    forms = new[]
                    {
                        Form(
                            AlphaArgumentFormKind.StateOn,
                            Boolean(),
                            Duration(optional: true)),
                        Form(
                            AlphaArgumentFormKind.StateOff,
                            Boolean(defaultText: "False"),
                            Duration(optional: true))
                    }
                });

            var brightness = new[]
            {
                new[] { "OUTERBRIGHTNESS", "OuterBrightness" },
                new[] { "INNERBRIGHTNESS", "InnerBrightness" }
            };
            foreach (var entry in brightness)
                commands.Add(new AlphaCommandDescriptor
                {
                    name = entry[0],
                    canonical = entry[1],
                    kind = AlphaCommandKind.Display,
                    category = AlphaCommandCategory.Display,
                    requiresParentheses = true,
                    // The player clamps the brightness, so a chart that writes 2 is
                    // read as full brightness rather than reported as an error.
                    forms = new[]
                    {
                        Form(
                            AlphaArgumentFormKind.Plain,
                            Number(defaultText: "0.5"),
                            Duration(optional: true))
                    }
                });

            commands.Add(new AlphaCommandDescriptor
            {
                name = "COMBODISPLAY",
                    kind = AlphaCommandKind.Display,
                canonical = "ComboDisplay",
                category = AlphaCommandCategory.Display,
                requiresParentheses = true,
                forms = new[]
                {
                    Form(
                        AlphaArgumentFormKind.Plain,
                        new AlphaValueSpec
                        {
                            kind = AlphaValueKind.ComboMode,
                            defaultText = "Combo"
                        },
                        Duration(optional: true))
                }
            });

            commands.Add(new AlphaCommandDescriptor
            {
                name = "AUDIO",
                    kind = AlphaCommandKind.Media,
                canonical = "audio",
                category = AlphaCommandCategory.Media,
                requiresParentheses = true,
                forms = new[]
                {
                    Form(
                        AlphaArgumentFormKind.StateOn,
                        Boolean(),
                        new AlphaValueSpec
                        {
                            kind = AlphaValueKind.MediaPath,
                            extensions = new[] { ".ogg", ".wav", ".mp3" },
                            defaultText = "media/audio.ogg"
                        }),
                    Form(AlphaArgumentFormKind.StateOff, Boolean(defaultText: "False"))
                }
            });

            commands.Add(new AlphaCommandDescriptor
            {
                name = "PVOVERLAY",
                    kind = AlphaCommandKind.Media,
                canonical = "pvOverlay",
                category = AlphaCommandCategory.Media,
                requiresParentheses = true,
                forms = new[]
                {
                    Form(
                        AlphaArgumentFormKind.StateOn,
                        Boolean(),
                        new AlphaValueSpec
                        {
                            kind = AlphaValueKind.MediaPath,
                            extensions = new[] { ".png", ".jpg", ".jpeg", ".mp4" },
                            defaultText = "media/overlay.mp4"
                        },
                        Duration(optional: true)),
                    Form(
                        AlphaArgumentFormKind.StateOff,
                        Boolean(defaultText: "False"),
                        Duration(optional: true))
                }
            });

            foreach (var effect in BuildScreenEffects())
                commands.Add(effect);

            return commands.ToArray();
        }

        private static List<AlphaCommandDescriptor> BuildScreenEffects()
        {
            var effects = new List<AlphaCommandDescriptor>();
            var simple = new[]
            {
                new[] { "GAUSSIAN", "Gaussian", "1" },
                new[] { "NEON", "Neon", "1" },
                new[] { "TRAIL", "Trail", "1" },
                new[] { "FLASH", "Flash", "1" },
                new[] { "BRIGHTNESS", "Brightness", "1" },
                new[] { "SATURATION", "Saturation", "1" },
                new[] { "CONTRAST", "Contrast", "1" },
                new[] { "RAINBOW", "Rainbow", "1" },
                new[] { "VIGNETTE", "Vignette", "1" },
                new[] { "GLITCH", "Glitch", "1" },
                new[] { "TVNOISE", "TVNoise", "1" },
                new[] { "ZOOM", "Zoom", "1.5" },
                new[] { "HUE", "Hue", "45" },
                new[] { "ROTATE", "Rotate", "10" }
            };
            foreach (var entry in simple)
                effects.Add(BuildScreenEffect(
                    entry[0],
                    entry[1],
                    false,
                    new[] { Number(defaultText: entry[2]) },
                    null));

            // FADE drives the same filter as FLASH, aimed at black.
            effects.Add(BuildScreenEffect(
                "FADE",
                "Flash",
                true,
                new[] { Number(defaultText: "1") },
                null));

            effects.Add(BuildScreenEffect(
                "TINT",
                "Tint",
                false,
                new[] { Color(defaultText: "FF6699"), Number(defaultText: "0.5") },
                // The envelope form takes the strength first and the color last.
                new[] { Number(defaultText: "0.5"), Color(defaultText: "FF6699") }));

            effects.Add(BuildScreenEffect(
                "MOVE",
                "Move",
                false,
                new[] { Number(defaultText: "0.1"), Number(defaultText: "0.1") },
                null));

            effects.Add(BuildScreenEffect(
                "SHAKE",
                "Shake",
                false,
                new[]
                {
                    Number(defaultText: "0.5"),
                    Number(defaultText: "12"),
                    new AlphaValueSpec
                    {
                        kind = AlphaValueKind.Number,
                        optional = true,
                        allowsEmpty = true,
                        defaultText = "30"
                    }
                },
                // The envelope form has no direction, and its frequency is optional.
                new[] { Number(defaultText: "0.5"), Number(defaultText: "12") }));

            return effects;
        }

        private static AlphaCommandDescriptor BuildScreenEffect(
            string name,
            string canonical,
            bool negatesIntensity,
            AlphaValueSpec[] arguments,
            AlphaValueSpec[]? envelopeArguments)
        {
            var forms = new List<AlphaArgumentForm>();

            // <NAME*(Instant,arguments…,duration)> holds the effect for one duration.
            var instant = new List<AlphaValueSpec>
            {
                new AlphaValueSpec
                {
                    kind = AlphaValueKind.Keyword,
                    keywords = new[] { "Instant" },
                    defaultText = "Instant"
                }
            };
            foreach (var argument in arguments)
                if (!argument.optional)
                    instant.Add(Clone(argument, false));
            instant.Add(Duration());
            forms.Add(Form(AlphaArgumentFormKind.Instant, instant.ToArray()));
            foreach (var argument in arguments)
            {
                if (!argument.optional)
                    continue;
                // An optional argument sits before the duration, so its presence
                // changes what the last slot means: declare that length on its own.
                var longer = new List<AlphaValueSpec>(instant);
                longer.Insert(longer.Count - 1, Clone(argument, false));
                forms.Add(Form(AlphaArgumentFormKind.Instant, longer.ToArray()));
            }

            // <NAME*(True,arguments…[,transition])> stays on until it is turned off.
            var on = new List<AlphaValueSpec> { Boolean() };
            on.AddRange(arguments);
            on.Add(Duration(optional: true));
            forms.Add(Form(AlphaArgumentFormKind.StateOn, on.ToArray()));
            forms.Add(Form(
                AlphaArgumentFormKind.StateOff,
                Boolean(defaultText: "False"),
                Duration(optional: true)));

            // The two older forms: (duration,strength) and the four-part envelope.
            forms.Add(Form(AlphaArgumentFormKind.Plain, Duration(), Number()));
            var envelope = new List<AlphaValueSpec> { Duration(), Duration(), Duration() };
            var tail = envelopeArguments ?? arguments;
            for (var index = 0; index < tail.Length; index++)
                envelope.Add(Clone(tail[index], index > 0));
            forms.Add(Form(AlphaArgumentFormKind.Plain, envelope.ToArray()));

            return new AlphaCommandDescriptor
            {
                name = name,
                canonical = canonical,
                category = AlphaCommandCategory.Filter,
                kind = AlphaCommandKind.Effect,
                requiresParentheses = true,
                negatesIntensity = negatesIntensity,
                forms = forms.ToArray()
            };
        }

        private static AlphaValueSpec Clone(AlphaValueSpec source, bool optional)
        {
            return new AlphaValueSpec
            {
                kind = source.kind,
                minimum = source.minimum,
                maximum = source.maximum,
                positiveOnly = source.positiveOnly,
                allowsNull = source.allowsNull,
                allowsFalseReset = source.allowsFalseReset,
                keywords = source.keywords,
                extensions = source.extensions,
                allowsAlphaChannel = source.allowsAlphaChannel,
                allowsNumericBoolean = source.allowsNumericBoolean,
                allowsEmpty = source.allowsEmpty,
                optional = optional,
                defaultText = source.defaultText
            };
        }
    }
}
