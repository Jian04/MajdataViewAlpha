
using System.Windows.Media.Animation;
using System.Windows.Navigation;

namespace MajdataEdit.SyntaxModule
{
    enum InfomationLevel
    {
        Warning,
        Error
    }
    enum DirectionType
    {
        /// <summary>
        /// Clockwise
        /// </summary>
        Clockwise,
        /// <summary>
        /// Collinear
        /// </summary>
        Opposite,
        /// <summary>
        /// Counterclockwise
        /// </summary>
        Anticlockwise
    }
    internal class SimaiErrorInfo : ErrorInfo
    {
        public string eMessage;
        public InfomationLevel Level;
        public SimaiErrorInfo(int _posX, int _posY, string eMessage,InfomationLevel level = InfomationLevel.Error) : base(_posX, _posY)
        {
            this.eMessage = eMessage;
            this.Level = level;
        }
    }
    internal static class SyntaxChecker
    {
        static readonly string[] SlideTypeList = { "qq", "pp", "r", "q", "p", "w", "z", "s", "V", "v", "<", ">", "^", "-" };
        static readonly char[] SensorList = { 'A','B','C','D','E'};
        internal static List<SimaiErrorInfo> ErrorList = new();

        public static int GetErrorCount() => ErrorList.Where(e => e.Level is InfomationLevel.Error).Count();
        /// <summary>
        /// Checks raw Simai text.
        /// </summary>
        /// <param name="noteStr"></param>
        internal static async Task ScanAsync(string str)
        {
            str = System.Text.RegularExpressions.Regex.Replace(
                str,
                @"(?m)^[ \t]*@\{\d+\}[^\r\n]*(?:\r?\n|$)",
                "");
            str = System.Text.RegularExpressions.Regex.Replace(
                str,
                @"(?m)^[ \t]*[@&](?:\d+\s*/\s*\d+|[A-Fa-f0-9]{6}|NULL)[ \t]*(?:\r?\n|$)",
                "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            Action<string, int, int,string, InfomationLevel> addInfo = (s, x, y, localStr,level) =>
            {
                ErrorList.Add(new SimaiErrorInfo(x, y,
                    string.Format(
                        MainWindow.GetLocalizedString(localStr),
                        s,
                        y,
                        x), level));
            };
            Action<string, int, int> addError = (s, x, y) => addInfo(s,x,y, "SyntaxError",InfomationLevel.Error);

            await Task.Run(() =>
            {
                ErrorList.Clear();
                int line = 1;
                int column = 1;                
                var simaiChart = str.Split(",");

                if(!string.IsNullOrEmpty(str))
                {
                    if (simaiChart.Last().Replace("\n", "") == "E")// Remove the trailing E.
                        simaiChart = simaiChart.SkipLast(1).ToArray();
                    else
                        addInfo("", -1, -1, "SyntaxWarning", InfomationLevel.Warning);
                }

                foreach (var s in simaiChart)
                {
                    string simaiStr = s.Replace("\n", "");

                    if (string.IsNullOrEmpty(s))
                        continue;
                    if (s.Contains("\n"))
                    {
                        line++;
                        column = 1;
                    }

                    if (string.IsNullOrEmpty(simaiStr))
                        continue;

                    // Split simultaneous notes and pseudo-simultaneous notes.
                    var notes = simaiStr.Split(new char[] { '/','`'});
                    for (int i = 0;i < notes.Length;i++)
                    {
                        var noteStr = notes[i];

                        if (string.IsNullOrEmpty(noteStr))
                        {
                            addError(simaiStr, column, line);
                            continue;
                        }
                        if (i == 0 && !SpecialSyntaxCheck(ref noteStr, column, line))
                            continue;
                        else if (string.IsNullOrEmpty(noteStr))
                            continue;
                        NoteSyntaxCheck(noteStr, column, line);
                    }
                    column++;
                }
            });
        }
        /// <summary>
        /// Checks the parsed Note list.
        /// </summary>
        internal static void Scan()
        {
            var noteList = SimaiProcess.notelist;

            foreach (var note in noteList)
            {
                var raw = note.notesContent;
                var notes = raw.Split("/");

                foreach (var _note in notes)
                    NoteSyntaxCheck(_note,note.rawTextPositionX,note.rawTextPositionY);
            }

        }
        /// <summary>
        /// Validates BPM and time signatures.
        /// </summary>
        static bool SpecialSyntaxCheck(ref string simaiStr,int posX,int posY)
        {
            // Strip ALPHA extension tokens before any validation so they don't
            // confuse the BPM/beat/note checkers below.
            simaiStr = System.Text.RegularExpressions.Regex.Replace(
                simaiStr,
                @"<(COLOR|SIZE|ALPHA|SV|HS|JLINE|ShowJudgeLine|ShowJudgeArea|ShowJudgeInfo|ShowComboInfo|OuterBrightness|InnerBrightness|ShowJudgeText|ComboDisplay|TEXT|AUDIO|PVOVERLAY|Gaussian|Neon|Trail|Fade|Brightness|Saturation|Contrast|Rainbow|Flash|Vignette|Zoom|Glitch|TVNoise|Hue|Tint|Move|Rotate|Shake)\*[^>]*>",
                "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (string.IsNullOrEmpty(simaiStr))
                return false; // pure token slot — skip note check

            int bpmHeadCount = 0;
            int bpmTailCount = 0;
            int beatHeadCount = 0;
            int beatTailCount = 0;

            int bpmFirstIndex = simaiStr.IndexOf('(');
            int beatFirstIndex = simaiStr.IndexOf('{');

            int[]? tagIndex = FindHSpeedBody(simaiStr);

            Action<string> addError = s =>
            {
                ErrorList.Add(new SimaiErrorInfo(posX, posY,
                    string.Format(
                        MainWindow.GetLocalizedString("SyntaxError"),
                        s,
                        posY,
                        posX)));
            };

            for (int i = 0; i < simaiStr.Length; i++)
            {
                char c = simaiStr[i];
                switch(c)
                {
                    case '(':
                        bpmHeadCount++;
                        break;
                    case ')':
                        bpmTailCount++;
                        break;
                    case '{':
                        beatHeadCount++;
                        break;
                    case '}':
                        beatTailCount++;
                        break;
                }
            }

            // Skip validation for note-only statements.
            if ((bpmTailCount + bpmHeadCount + beatHeadCount + beatTailCount) == 0)
                return true;

            if(bpmHeadCount > 1 || bpmTailCount > 1)
            {
                addError(simaiStr);
                return false;
            }
            else if (bpmHeadCount != bpmTailCount)
            {
                addError(simaiStr);
                return false;
            }

            if (beatHeadCount > 1 || beatTailCount > 1)
            {
                addError(simaiStr);
                return false;
            }
            else if (beatHeadCount != beatTailCount)
            {
                addError(simaiStr);
                return false;
            }

            if (tagIndex is null)
            {
                addError(simaiStr);
                return false;
            }

            // {} and () must precede the Note.
            if (bpmFirstIndex != 0 && beatFirstIndex != 0)
                addError(simaiStr);
            else
            {
                int bpmEndIndex = simaiStr.IndexOf(')');
                int beatEndIndex = simaiStr.IndexOf('}');
                
                bool hadBpm = bpmFirstIndex != bpmEndIndex;
                bool hadBeat = beatFirstIndex != beatEndIndex;

                if((hadBpm || hadBeat) && simaiStr[0] is not ('(' or '{'))
                {
                    addError(simaiStr);
                    return false;
                }               

                // Validate HSpeed syntax.
                if(tagIndex.Length != 0)
                {
                    var tagHead = tagIndex[0];
                    var tagTail = tagIndex[1];
                    var body = simaiStr[(tagHead + 1)..tagTail];

                    var s = body.Split("HS*");

                    if (s.Length != 2)// A valid split produces an array of length two.
                    {
                        addError(simaiStr);
                        return false;
                    }
                    else if (!string.IsNullOrEmpty(s[0]))// The first element should be empty.
                    {
                        addError(simaiStr);
                        return false;
                    }
                    else if (!IsNum(s[1]))// The second element should be numeric.
                    {
                        addError(simaiStr);
                        return false;
                    }

                    // Remove the "<HS*1.0>" string before passing the rest to NoteSyntaxChecker.
                    simaiStr = simaiStr.Remove(tagHead, (tagTail - tagHead) + 1);
                }

                // Has a prefix but no suffix.
                if((bpmFirstIndex != -1 && bpmEndIndex == -1) || (bpmFirstIndex != -1 && beatEndIndex == -1))
                {
                    addError(simaiStr);
                    return false;
                }

                // (){} or {}()
                if (hadBpm && hadBeat)
                {
                    //(){}
                    if (bpmEndIndex < beatFirstIndex && (beatFirstIndex != bpmEndIndex + 1))
                    {
                        addError(simaiStr);
                        return false;
                    }
                    else if(bpmEndIndex < beatFirstIndex && (beatFirstIndex == bpmEndIndex + 1))
                    { 
                        // noting to do
                    }
                    //{}()
                    else if (beatEndIndex < bpmFirstIndex && (bpmFirstIndex != beatEndIndex + 1))
                    {
                        addError(simaiStr);
                        return false;
                    }

                }

                if (hadBeat && !IsInteger(simaiStr[(beatFirstIndex+1)..(beatEndIndex)]))
                {
                    addError(simaiStr);
                    return false;
                }
                if (hadBpm && !IsNum(simaiStr[(bpmFirstIndex + 1)..(bpmEndIndex)]))
                {
                    addError(simaiStr);
                    return false;
                }

                simaiStr = simaiStr[(Math.Max(bpmEndIndex, beatEndIndex) + 1)..];
                
            }
            return true;
        }
        /// <summary>
        /// Finds the HSpeed body.
        /// </summary>
        /// <param name="simaiStr"></param>
        /// <returns>
        /// Returns the HSpeed body start and end indexes, Empty if absent, or null for invalid HS syntax.
        /// </returns>
        static int[]? FindHSpeedBody(string simaiStr)
        {
            //<HS*>
            simaiStr = simaiStr.Replace(" ", "");
            List<int> bodyHead = new();
            List<int> bodyTail = new();
            int? tagHead = null;
            int? tagTail = null;            
            
            for(int i = 0;i < simaiStr.Length;i++)
            {
                if(i + 3 < simaiStr.Length)
                {
                    var s = simaiStr[i..(i + 3)];
                    if(s == "HS*")
                    {
                        if (tagHead != null)
                            return null;
                        tagHead = i;
                        tagTail = i + 2;
                    }
                }
                switch(simaiStr[i])
                {
                    case '<':
                        bodyHead.Add(i);
                        break;
                    case '>':
                        bodyTail.Add(i);
                        break;
                }
            }

            bool hadTag = tagHead is not null;
            if (hadTag)
            {
                int head = bodyHead.Where(h => h < tagHead).DefaultIfEmpty(-1).Max();
                int tail = bodyTail.Where(t => t > tagTail).DefaultIfEmpty(-1).Min();

                if (bodyHead.Count == 0 || bodyTail.Count == 0)
                    return null;
                if (head == -1 || tail == -1)
                    return null;

                return new int[] { head, tail };

            }

            return Array.Empty<int>();
        }
        /// <summary>
        /// Checks whether a Note statement body is valid, such as whether "[" or "]" is duplicated.
        /// </summary>
        /// <param name="bodyStr"></param>
        /// <returns>
        /// Indexes of "[" and "]".
        /// </returns>
        static int[]? BodySyntaxCheck(string bodyStr,bool isSlide = false)
        {
            List<int> bodyIndex = new();
            int bodyHeadCount = 0;
            int bodyTailCount = 0;

            for(int index = 0;index < bodyStr.Length;index++)
            {
                char c = bodyStr[index];
                if(c == '[')
                {
                    bodyHeadCount++;
                    bodyIndex.Add(index);
                }
                else if(c == ']')
                {
                    bodyTailCount++;
                    bodyIndex.Add(index);
                }
            }

            // Valid statements contain equal numbers of "[" and "]".
            // A non-Slide Note statement not ending in "]" is invalid.
            // Slides are an exception because a trailing b marks a Break Slide.
            if (bodyHeadCount != bodyTailCount)
                return null;
            else if (!isSlide && (bodyHeadCount != 1 || bodyTailCount != 1))
                return null;
            else if (!isSlide && (bodyIndex.Last() != bodyStr.Length - 1))
                return null;

            return bodyIndex.ToArray();

        }
        /// <summary>
        /// Validates a Note statement without checking BPM, time signatures, or speed changes.
        /// </summary>
        /// <param name="noteStr"></param>
        static bool NoteSyntaxCheck(string noteStr,int posX,int posY)
        {
            noteStr = noteStr.Replace("m", "");
            if (IsTouchSlide(noteStr))
            {
                if (TouchSlideSyntaxCheck(noteStr))
                    return true;
            }
            else if (IsTap(noteStr))
                return true;
            else if (IsHold(noteStr))
            {
                if (HoldSyntaxCheck(noteStr))
                    return true;
            }
            else if (IsSlide(ref noteStr))
            {
                if (SlideSyntaxCheck(noteStr))
                    return true;
            }
            else if (IsTouch(noteStr))
                return true;
            //else if(noteStr == "E")
            //    return true;
            ErrorList.Add(new SimaiErrorInfo(posX, posY,
                    string.Format(
                        MainWindow.GetLocalizedString("SyntaxError"),
                        noteStr,
                        posY,
                        posX)));
            return false;

        }

        static bool IsTouchSlide(string noteStr)
        {
            return System.Text.RegularExpressions.Regex.IsMatch(
                noteStr,
                @"^(?=[^\[]*[ABDEC])(?:[1-8]d?|[ABDE][1-8]|C1?)[bxf!?]*(?:[-<>^](?:[1-8]d?|[ABDE][1-8]|C1?)[bxf]*)+",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        }

        static bool TouchSlideSyntaxCheck(string noteStr)
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                noteStr,
                @"^(?=[^\[]*[ABDEC])(?:[1-8]d?|[ABDE][1-8]|C1?)[bxf!?]*(?:[-<>^](?:[1-8]d?|[ABDE][1-8]|C1?)[bxf]*)+\[(?<body>[^\[\]]+)\]$",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            if (!match.Success)
                return false;

            var body = match.Groups["body"].Value;
            var parameters = body.Split('#');
            try
            {
                return parameters.Length switch
                {
                    1 => PositiveRatioSyntaxCheck(parameters[0]),
                    2 => IsNum(parameters[0]) &&
                         double.Parse(parameters[0]) > 0 &&
                         IsPositiveSlideLength(parameters[1]),
                    3 => IsNum(parameters[0]) &&
                         double.Parse(parameters[0]) >= 0 &&
                         string.IsNullOrEmpty(parameters[1]) &&
                         IsPositiveSlideLength(parameters[2]),
                    4 => IsNum(parameters[0]) &&
                         double.Parse(parameters[0]) >= 0 &&
                         string.IsNullOrEmpty(parameters[1]) &&
                         IsNum(parameters[2]) &&
                         double.Parse(parameters[2]) > 0 &&
                         PositiveRatioSyntaxCheck(parameters[3]),
                    _ => false
                };
            }
            catch
            {
                return false;
            }
        }

        static bool IsPositiveSlideLength(string value)
        {
            return value.Contains(':')
                ? PositiveRatioSyntaxCheck(value)
                : double.TryParse(value, out var seconds) && seconds > 0;
        }
        /// <summary>
        /// Validates Hold parameters.
        /// </summary>
        /// <param name="holdStr"></param>
        /// <returns></returns>
        static bool HoldSyntaxCheck(string holdStr)
        {
            // Short Holds such as 2h were validated earlier and need no further check.
            if (holdStr.Length <= 4)
            {
                // Reject malformed forms such as 2h[], 2h[, or 2hxx.
                foreach (var s in holdStr[2..])
                    if (s is not ('b' or 'x'))
                        return false;
                return true;
            }

            int[]? bodyIndex = BodySyntaxCheck(holdStr);
            if (bodyIndex is null)// Invalid body
                return false;

            int startIndex = bodyIndex[0];
            int endIndex = bodyIndex[1];
            string body = holdStr[(startIndex + 1)..(endIndex)];
            if (body.Length < 2)// Shortest Hold parameter: #2, representing two seconds.
                return false;

            if (body.Contains("#"))
            {
                if (body[0] == '#')
                    return double.TryParse(body[1..], out double i) && i >= 0;
                else
                {
                    var splitBody = body.Split("#");
                    if (splitBody.Length != 2)// Valid format: 150#4:1
                        return false;
                    else
                        return RatioSyntaxCheck(splitBody[1]) && (double.TryParse(splitBody[0], out double i) && i > 0);
                }
            }
            else
                return RatioSyntaxCheck(body);
        }
        /// <summary>
        /// Validates Slide paths and parameters.
        /// </summary>
        /// <param name="slideStr"></param>
        /// <returns></returns>
        static bool SlideSyntaxCheck(string slideStr)
        {
            if (slideStr.Length < 3)
                return false;
            if (slideStr[1] is ('b' or 'x') && slideStr[2] is ('b' or 'x'))
                slideStr = slideStr.Remove(1,2);
            else if(slideStr[1] is ('b' or 'x'))
                slideStr = slideStr.Remove(1, 1);
            
            int starPoint = int.Parse(slideStr[0..1]);// Star-head position

            char[] typeList = string.Concat(SlideTypeList.Skip(2).ToArray()).ToCharArray();
            int slideCount = 0;

            foreach(var _slideStr in slideStr.Split("*"))// Handle Slides sharing one head.
            {
                // The argument should be 1-7-5[8:1] or -7-5[8:1].
                int[]? bodyIndex = BodySyntaxCheck(_slideStr, true);
                if (bodyIndex is null)// Invalid body
                    return false;                

                int? startPoint = null;
                int? endPoint = null;
                int? flexionPoint = null;
                string slideType = "";

                // Multiple parameters for a connected Slide.
                //e.g. 1-7[8:1]-5[8:1]
                // This representation is awkward but retained for compatibility.
                int subSlideCount = 0;

                // Validate the Slide path in this loop.
                for (int i = 0; i < _slideStr.Length;)
                {
                    // Get the Slide path start.
                    if (slideCount != 0 && i == 0)// Handle Slides sharing one head.
                        startPoint = starPoint;
                    else if (subSlideCount > 0)// Detect connected Slides.
                    {
                        startPoint = endPoint;
                        endPoint = null;
                        i++;
                    }
                    else
                    {
                        if (IsInteger(_slideStr[i..(i + 1)]))
                            startPoint = int.Parse(_slideStr[i..(i + 1)]);
                        else
                            return false;
                        i++;
                    }
                    

                    // Get the Slide type.
                    if (typeList.Contains(_slideStr[i]))
                    {
                        slideType = _slideStr[i..(i + 1)];
                        if ((i + 1 < _slideStr.Length) && (_slideStr[i] == _slideStr[i + 1] ||
                            (_slideStr[i] == 'r' && (_slideStr[i + 1] == 'p' || _slideStr[i + 1] == 'q'))))//pp,qq,rp,rq
                        {
                            slideType += _slideStr[i + 1];
                            i += 2;
                        }
                        else
                            i++;
                    }
                    else
                        return false;

                    // Get the turning point of a V-type Slide.
                    if (slideType == "V")
                    {
                        if (IsInteger(_slideStr[i..(i + 1)]))
                            flexionPoint = int.Parse(_slideStr[i..(i + 1)]);
                        else
                            return false;
                        i++;
                    }
                    // Get the Slide path endpoint.
                    if (i < _slideStr.Length && IsInteger(_slideStr[i..(i + 1)]))
                        endPoint = int.Parse(_slideStr[i..(i + 1)]);
                    else
                        return false;

                    // Validate the Slide path.
                    // Forms such as 1-7 will fail validation.
                    if (!SlidePathCheck(slideType, (int)startPoint, (int)endPoint, flexionPoint))
                        return false;

                    // Check whether the next character is "[" or "b" while avoiding out-of-range access.

                    if ((i + 1 < _slideStr.Length) && _slideStr[i + 1] == '[')
                    {
                        var headIndex = Array.IndexOf<int>(bodyIndex, ++i);
                        // No head index was found.
                        if (headIndex == -1)
                            return false;

                        // Move the current position past "]".
                        if (_slideStr.Last() == 'b')
                            i = bodyIndex[headIndex + 1] + 1;
                        else if (_slideStr.Last() == ']')
                            i = bodyIndex[headIndex + 1];
                        else
                            return false;
                    }
                    
                    subSlideCount++;
                    if (i + 1 >= _slideStr.Length)
                        break;
                }

                // 1-4-6[4:1]-1[4:1] is not allowed.
                // Use either 1-4-6-1[4:1]
                // or 1-4[4:1]-6[4:1]-1[4:1].
                if (subSlideCount != bodyIndex.Length / 2 && bodyIndex.Length != 2)
                    return false;

                // Validate parameters.
                Func<int,bool> bodyChecker = i =>
                {
                    int bodyStartIndex = bodyIndex[i * 2];
                    int bodyEndIndex = bodyIndex[i * 2 + 1];
                    string body = _slideStr[(bodyStartIndex + 1)..bodyEndIndex];
                    int paramType = 0;

                    // Match the parameter pattern.
                    for (int j = 0; j < body.Length; j++)
                        if (body[j] == '#')
                            paramType++;
                    if (paramType > 3)
                        return false;

                    try
                    {
                        switch (paramType)
                        {
                            case 0:
                                if (!PositiveRatioSyntaxCheck(body))
                                    return false;
                                break;
                            case 1://[150#8:1]
                            case 2:// [3##8:1] or [3##1]
                                var param = body.Split("#");
                                var bpmStr = param[0];
                                var length = paramType == 2 ? param[2] : param[1];

                                if (!IsNum(bpmStr))
                                    return false;
                                if (length.Contains(':'))
                                {
                                    if (!PositiveRatioSyntaxCheck(length))
                                        return false;
                                }
                                else if (!double.TryParse(length, out var seconds) || seconds <= 0)
                                    return false;

                                return paramType switch
                                {
                                    1 => double.Parse(bpmStr) > 0,
                                    2 => double.Parse(bpmStr) >= 0,
                                    _ => false
                                };
                            case 3://[3##150#8:1]
                                param = body.Split("#");
                                var startLength = param[0];
                                bpmStr = param[2];
                                length = param[3];

                                if (!IsNum(startLength))
                                    return false;
                                if (!IsNum(bpmStr))
                                    return false;
                                if (!PositiveRatioSyntaxCheck(length))
                                    return false;
                                if (double.Parse(bpmStr) <= 0 || double.Parse(startLength) < 0)
                                    return false;

                                break;
                        }
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                };
                if(bodyIndex.Length == 2)
                {
                    if (!bodyChecker(0))
                        return false;
                }
                else
                {
                    for (int i = 0; i < subSlideCount; i++)
                    {
                        if (!bodyChecker(i))
                            return false;
                    }
                }
                slideCount++;
            }
            return true;

        }
        /// <summary>
        /// Validates a Slide path.
        /// </summary>
        /// <param name="slideType"></param>
        /// <param name="startPoint"></param>
        /// <param name="endPoint"></param>
        /// <param name="flexionPoint"></param>
        /// <returns></returns>
        static bool SlidePathCheck(string slideType,int startPoint,int endPoint,
                                   int? flexionPoint = null)
        {
            if (!PointCheck(startPoint) || !PointCheck(endPoint))
                return false;

            switch(slideType)
            {
                case "^":
                case "v":
                    if (GetPointInterval(startPoint, endPoint) is (0 or 4))
                        return false;
                    return true;
                case "-":
                    if (startPoint == endPoint)
                        return false;
                    else if (GetPointInterval(startPoint, endPoint) < 2)
                        return false;
                    return true;
                case "V":
                    if (startPoint == endPoint)
                        return false;
                    else if (GetPointInterval(startPoint, (int)flexionPoint!) != 2)
                        return false;
                    else if (GetPointInterval((int)flexionPoint!, endPoint) < 2)
                        return false;
                    return true;
                case "s":
                case "z":
                case "w":
                    if (startPoint == endPoint)
                        return false;
                    else if ((DirectionType)PointCompare(startPoint, endPoint)! != DirectionType.Opposite)
                        return false;
                    return true;
            }
            return true;
        }
        /// <summary>
        /// Gets a position index for comparing relative key positions.
        /// </summary>
        /// <param name="point"></param>
        /// <returns>
        /// Returns the angle between a key and the target key, or null if the target is invalid.
        /// </returns>
        static int? GetPointIndex(int point)
        {
            // Use the line through #8 and #4 as the axis, with #8 as the origin.
            // The angle from the origin to the target becomes the position index.
            // For example, #1 has index 45 and #8 has index 0.
            // Except at #8, A index minus B index > 0 means B is counterclockwise from A; otherwise it is clockwise.

            if (!PointCheck(point))
                return null;
            switch(point)
            {
                case 8:
                    return 0;
                default:
                    return point * 45;
            }
        }
        /// <summary>
        /// Gets the shortest distance between two keys.
        /// </summary>
        /// <param name="point"></param>
        /// <param name="targetPoint"></param>
        /// <returns></returns>
        static int GetPointInterval(int point,int targetPoint)
        {
            int a = (int)GetPointIndex(point)!;
            int b = (int)GetPointIndex(targetPoint)!;
            int result = Math.Abs(a - b);

            if (result == 0)
                return 0;
            else
                return Math.Min(8 - (result / 45), result / 45);

        }
        /// <summary>
        /// Compares relative key positions.
        /// </summary>
        /// <param name="point"></param>
        /// <param name="targetPoint"></param>
        /// <returns>
        /// Direction of the target key: clockwise, collinear, or counterclockwise.
        /// </returns>
        static DirectionType? PointCompare(int point,int targetPoint)
        {
            if (!PointCheck(point) || !PointCheck(targetPoint))
                return null;
            if(point == targetPoint) return null;

            int a = (int)GetPointIndex(point)!;
            int b = (int)GetPointIndex(targetPoint)!;
            int result = a - b;

            if (Math.Abs(result) == 180)
                return DirectionType.Opposite;
            else if (result < -180 || (result > 0 && result < 180))
                return DirectionType.Anticlockwise;
            else
                return DirectionType.Clockwise;
        }
        /// <summary>
        /// Validates a proportional duration.
        /// </summary>
        /// <param name="ratioStr"></param>
        /// <returns></returns>
        static bool RatioSyntaxCheck(string ratioStr)
        {
            var s = ratioStr.Split(":");

            if (s.Length != 2)
                return false;

            return (int.TryParse(s[0], out int i) && i > 0) && (int.TryParse(s[1], out i) && i >= 0);
        }

        static bool PositiveRatioSyntaxCheck(string ratioStr)
        {
            var values = ratioStr.Split(':');
            return values.Length == 2 &&
                   int.TryParse(values[0], out var division) && division > 0 &&
                   int.TryParse(values[1], out var count) && count > 0;
        }
        /// <summary>
        /// Determines whether a statement is a Note.
        /// </summary>
        /// <param name="s"></param>
        /// <returns>
        /// Returns the Note type, or null if the statement is invalid.
        /// </returns>
        static SimaiNoteType? IsNote(string s)
        {
            if (IsTap(s))
                return SimaiNoteType.Tap;
            else if (IsHold(s))
                return SimaiNoteType.Hold;
            else if (IsSlide(ref s))
                return SimaiNoteType.Slide;
            else if (IsTouch(s))
                return SimaiNoteType.Touch;
            else
                return null;
        }
        /// <summary>
        /// Determines whether a statement is a Tap.
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        static bool IsTap(string s)
        {
            int index;

            if (s.Length >= 2 && s[1] == 'd')
                s = s.Remove(1, 1);

            if (!int.TryParse(s[0..1], out index))// Always inspect the first character.
                return false;
            if (!PointCheck(index))// Return immediately for an invalid position.
                return false;

            if(s.Contains("$"))
            {
                var f = s.IndexOf("$");
                var l = s.LastIndexOf("$");

                if (f == l && s[1] == '$')
                    s = s.Remove(1, 1);
                else if (Math.Abs(f - l) == 1 && s[1..3] == "$$")
                    s = s.Remove(1, 2);
                else 
                    return false;
            }

            if (s.Length == 1)
                return true;
            else if(s.Length == 2)// e.g. 28 , 2b , 2x , 2f
            {
                if (s[1] is ('b' or 'x' or 'f'))
                    return true;
                else
                    return int.TryParse(s, out int i) && (PointCheck(i % 10) && PointCheck(i / 10));
            }
            else if (s.Length == 3)// e.g. 2bx
            {
                var isBreak = s[1] is 'b' || s[2] is 'b';
                var isHanabi = s[1] is 'x' or 'f' || s[2] is 'x' or 'f';

                return isBreak && isHanabi;
            }

            return false;// All other cases are invalid.
        }
        /// <summary>
        /// Determines whether a statement is a Hold without validating Hold parameters.
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        static bool IsHold(string s)
        {
            int index = 0;
            var _s = s.Split("[");
            string header = _s[0];
            if (header.Length >= 2 && header[1] == 'd')
            {
                s = s.Remove(1, 1);
                header = header.Remove(1, 1);
            }
            if (IsTouchHoldHeader(header))
                return true;

            if (!int.TryParse(s[0..1], out index))// Always inspect the first character.
                return false;
            if (!PointCheck(index))// Return immediately for an invalid position.
                return false;
            if (s.Length < 2 || header.Length < 2)
                return false;
            // Strict Hold validation requires 'h' as the second character, while 'b' and 'x' may appear anywhere.
            // Use lenient validation for compatibility.
            //else if (header[1] != 'h')// Return immediately unless the second character is "h".
            //    return false;

            // Hold modifiers are unordered. 'f' is the modern firework spelling;
            // retain 'x' for existing charts.
            return header.Length switch
            {
                2 => header[1] is 'h',
                3 => header.Contains('h') && (header.Contains('b') || header.Contains('x') || header.Contains('f')),
                4 => header.Contains('h') && header.Contains('b') && (header.Contains('x') || header.Contains('f')),
                _ => false
            };

            // Strict Hold validation requires 'h' as the second character, while 'b' and 'x' may appear anywhere.
            //if (header.Length == 2)// e.g. 2h
            //    return true;
            //else if (header.Length == 3)// e.g. 2hb,2hx
            //    return s[2] is 'b' or 'x';
            //else if (header.Length == 4)// e.g. 2hbx,2hxb
            //{
            //    var isBreak = s[2] is 'b' || s[3] is 'b';
            //    var isHanabi = s[2] is 'x' || s[3] is 'x';

            //    return isBreak && isHanabi;
            //}

            //return false;
        }
        /// <summary>
        /// Determines whether a statement is a Slide by checking only its head, not its parameters.
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        static bool IsSlide(ref string s)
        {
            int index;
            if (s.Length >= 2 && s[1] == 'd')
                s = s.Remove(1, 1);
            s = s.Replace("d", "");
            var types = SlideTypeList.Skip(2).ToArray();
            string header = s.Split(string.Concat(types).ToCharArray())[0];            

            if (!int.TryParse(s[0..1], out index))// Always inspect the first character.
                return false;
            if (!PointCheck(index))// Return immediately for an invalid position.
                return false;

            if (header.Contains("?") || header.Contains("!"))
                if (header[1] is '?' or '!')
                {
                    header = header.Remove(1, 1);
                    s = s.Remove(1, 1);
                }
                else
                    return false;

            if (header.Length == 1)// For example, processing 1-8 leaves a header of 1.
                return true;
            else if (header.Length == 2 && header[1] is 'b' or 'x' or 'f')// e.g. 1x,1b,1f
                return true;
            else if (header.Length == 3)// e.g. 1bx,1xb
            {
                var isBreak = s[1] is 'b' || s[2] is 'b';
                var isHanabi = s[1] is 'x' or 'f' || s[2] is 'x' or 'f';

                return isBreak && isHanabi;
            }

            // Other lengths generally indicate an invalid Slide type.
            return false;
        }
        /// <summary>
        /// Determines whether a statement is a Touch.
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        static bool IsTouch(string s)
        {
            if (string.IsNullOrEmpty(s) || !SensorList.Contains(s[0]))
                return false;

            var modifierStart = 1;
            if (s[0] == 'C')
            {
                if (modifierStart < s.Length && char.IsDigit(s[modifierStart]))
                {
                    if (s[modifierStart] != '1')
                        return false;
                    modifierStart++;
                }
            }
            else
            {
                if (modifierStart >= s.Length ||
                    !int.TryParse(s[modifierStart].ToString(), out var position) ||
                    !PointCheck(position))
                    return false;
                modifierStart++;
            }

            var modifiers = s[modifierStart..];
            return modifiers.All(c => c is 'b' or 'f' or 'x') &&
                   modifiers.Distinct().Count() == modifiers.Length;
        }

        private static bool IsTouchHoldHeader(string header)
        {
            if (string.IsNullOrEmpty(header) || !SensorList.Contains(header[0]))
                return false;

            var modifierStart = 1;
            if (header[0] == 'C')
            {
                if (modifierStart < header.Length && char.IsDigit(header[modifierStart]))
                {
                    if (header[modifierStart] != '1')
                        return false;
                    modifierStart++;
                }
            }
            else
            {
                if (modifierStart >= header.Length ||
                    !int.TryParse(header[modifierStart].ToString(), out var position) ||
                    !PointCheck(position))
                    return false;
                modifierStart++;
            }

            var modifiers = header[modifierStart..];
            return modifiers.Count(c => c == 'h') == 1 &&
                   modifiers.All(c => c is 'h' or 'b' or 'f' or 'x') &&
                   modifiers.Distinct().Count() == modifiers.Length;
        }
        /// <summary>
        /// Determines whether a string is numeric.
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        static bool IsNum(string s) => IsInteger(s) || IsFloat(s);
        static bool IsInteger(string s) => int.TryParse(s, out int i);
        static bool IsFloat(string s) => double.TryParse(s, out double i);
        /// <summary>
        /// Determines whether a key position is valid.
        /// </summary>
        /// <param name="k"></param>
        /// <returns></returns>
        static bool PointCheck(int k) => k >= 1 && k <= 8;

    }
}
