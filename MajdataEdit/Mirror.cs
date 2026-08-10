using System.Text;
using System.Text.RegularExpressions;

namespace MajdataEdit;

// Component maintained by Xiao'e.
internal static class Mirror
{
    public enum HandleType
    {
        LRMirror,
        UDMirror,
        HalfRotation,
        Rotation45,
        CcwRotation45
    }

    private static readonly Dictionary<char, char> MIRROR_LEFT_RIGHT_MAP = new()
    {
        { '1', '8' },
        { '2', '7' },
        { '3', '6' },
        { '4', '5' },
        { '5', '4' },
        { '6', '3' },
        { '7', '2' },
        { '8', '1' },
        { 'q', 'p' },
        { 'p', 'q' },
        { '<', '>' },
        { '>', '<' },
        { 'z', 's' },
        { 's', 'z' }
    };

    // Use a special mapping table for these characters.
    private static readonly HashSet<char> MIRROR_SPECIAL_PREFIX = new() { 'D', 'E' };

    private static readonly Dictionary<char, char> MIRROR_LEFT_RIGHT_SPECIAL_MAP = new()
    {
        { '8', '2' },
        { '2', '8' },
        { '3', '7' },
        { '7', '3' },
        { '4', '6' },
        { '6', '4' },
        { '1', '1' },
        { '5', '5' }
    };

    private static readonly Dictionary<char, char> MIRROR_UPSIDE_DOWN_MAP = new()
    {
        { '4', '1' },
        { '5', '8' },
        { '6', '7' },
        { '3', '2' },
        { '7', '6' },
        { '2', '3' },
        { '8', '5' },
        { '1', '4' },
        { 'q', 'p' },
        { 'p', 'q' },
        { 'z', 's' },
        { 's', 'z' }
    };

    private static readonly Dictionary<char, char> MIRROR_UPSIDE_DOWN_SPECIAL_MAP = new()
    {
        { '4', '2' },
        { '2', '4' },
        { '1', '5' },
        { '5', '1' },
        { '8', '6' },
        { '6', '8' },
        { '3', '3' },
        { '7', '7' }
    };

    private static readonly Dictionary<char, char> ROTATE_CW_45_MAP = new()
    {
        { '8', '1' },
        { '7', '8' },
        { '6', '7' },
        { '5', '6' },
        { '4', '5' },
        { '3', '4' },
        { '2', '3' },
        { '1', '2' }
    };

    private static readonly Dictionary<char, char> ROTATE_CCW_45_MAP = new()
    {
        { '1', '8' },
        { '2', '1' },
        { '3', '2' },
        { '4', '3' },
        { '5', '4' },
        { '6', '5' },
        { '7', '6' },
        { '8', '7' }
    };

    private static readonly HashSet<char> ROTATE_CW_45_SPECIAL_PREFIX = new() { '2', '6' };

    private static readonly HashSet<char> ROTATE_CCW_45_SPECIAL_PREFIX = new() { '3', '7' };

    private static readonly Dictionary<char, char> ROTATE_45_SPECIAL_MAP = new()
    {
        { '<', '>' },
        { '>', '<' }
    };

    private static readonly string HS_SEQUENCE = "<HS*";

    public static string NoteMirrorHandle(string str, HandleType type)
    {
        // NOTE: SimaiProcess accepts strings such as 1-5[8:1]{16}, but they cannot be mirrored correctly.
        // This is intentional because the syntax itself is invalid even though SimaiProcess does not reject it.

        StringBuilder resultString = new StringBuilder();   // Final result
        StringBuilder curPart = new StringBuilder();        // Current segment
        bool isPartIgnored = false;     // Whether to ignore the current segment
        int hsStatus = 0;              // HS parse state: 0 none, 1 "<", 2 "H", 3 "S", 4 "*"; all states must occur in order.
        
        // Whitespace is retained in every segment and ignored by helpers so its position remains unchanged.
        foreach (char c in str)
        {
            curPart.Append(c);

            // A segment containing any of these characters must be ignored.
            if (!isPartIgnored && (c == '{' || c == '}' || c == '(' || c == ')'))
            {
                isPartIgnored = true;
            }

            if (hsStatus == 0)
            {
                if (HS_SEQUENCE[0] == c)
                {
                    // A possible HS prefix was found; inspect the following characters.
                    hsStatus = 1;
                }
            }
            else if (hsStatus != HS_SEQUENCE.Length)
            {
                // Once hsStatus is nonzero, the remaining HS characters must follow contiguously,
                // excluding whitespace.
                if (!Char.IsWhiteSpace(c))
                {
                    if (HS_SEQUENCE[hsStatus] == c)
                    {
                        // Advance on a match.
                        hsStatus++;
                        if (hsStatus == HS_SEQUENCE.Length)
                        {
                            // Reaching the end confirms an HS construct.
                            isPartIgnored = true;
                        }
                    }
                    else
                    {
                        // A mismatch disproves the HS construct; reset and continue scanning.
                        hsStatus = 0;
                    }
                }
            }

            // These characters end the current segment.
            if (c == '}' || c == ')' || c == ',' || c == '/' || c == '`' ||
                (hsStatus == 4 && c == '>'))
            {
                if (isPartIgnored)
                {
                    // Append ignored segments unchanged.
                    resultString.Append(curPart.ToString());
                }
                else
                {
                    // Transform segments that require mirroring.
                    resultString.Append(NoteMirrorPart(curPart.ToString(), type));
                }

                isPartIgnored = false;
                hsStatus = 0;
                curPart.Clear();
            }
        }

        // Process any remaining unconverted text, such as when a trailing comma was not included.
        if (curPart.Length > 0)
        {
            if (isPartIgnored)
            {
                // Append ignored segments unchanged.
                resultString.Append(curPart.ToString());
            }
            else
            {
                // Transform segments that require mirroring.
                resultString.Append(NoteMirrorPart(curPart.ToString(), type));
            }
        }

        return resultString.ToString();
    }

    private static string NoteMirrorPart(string str, HandleType type)
    {
        switch (type)
        {
            case HandleType.LRMirror:    
                str = NormalMirrorPart(str, MIRROR_LEFT_RIGHT_MAP, MIRROR_LEFT_RIGHT_SPECIAL_MAP, MIRROR_SPECIAL_PREFIX);
                break;    
            case HandleType.UDMirror:
                str = NormalMirrorPart(str, MIRROR_UPSIDE_DOWN_MAP, MIRROR_UPSIDE_DOWN_SPECIAL_MAP, MIRROR_SPECIAL_PREFIX);
                break;
            case HandleType.HalfRotation:
                // 180 degrees combines horizontal and vertical mirroring.
                str = NormalMirrorPart(str, MIRROR_LEFT_RIGHT_MAP, MIRROR_LEFT_RIGHT_SPECIAL_MAP, MIRROR_SPECIAL_PREFIX);
                str = NormalMirrorPart(str, MIRROR_UPSIDE_DOWN_MAP, MIRROR_UPSIDE_DOWN_SPECIAL_MAP, MIRROR_SPECIAL_PREFIX);
                break;
            case HandleType.Rotation45:
                str = NormalMirrorPart(str, ROTATE_CW_45_MAP, ROTATE_45_SPECIAL_MAP, ROTATE_CW_45_SPECIAL_PREFIX);
                break;
            case HandleType.CcwRotation45:
                str = NormalMirrorPart(str, ROTATE_CCW_45_MAP, ROTATE_45_SPECIAL_MAP, ROTATE_CCW_45_SPECIAL_PREFIX);
                break;
        }

        return str;
    }

    private static string NormalMirrorPart(string str, Dictionary<char, char> normalMap, Dictionary<char, char> specialMap, HashSet<char> specialPrefix)
    {
        StringBuilder result = new StringBuilder();
        bool isSpecialPrefix = false;
        bool isInBracket = false;

        foreach (char c in str)
        {
            // Ignore whitespace.
            if (Char.IsWhiteSpace(c))
            {
                result.Append(c);
                continue;
            }

            // Character transformation
            if (isInBracket || c == '[')
            {
                // Ignore content inside brackets because it contains duration and related settings.
                // '[' also enters this branch because isInBrancket remains false until the later state update.
                result.Append(c);
            }
            else if (isSpecialPrefix)
            {
                // Use the special mapping when the previous character was D or E.
                isSpecialPrefix = false;
                if (specialMap.ContainsKey(c))
                {
                    result.Append(specialMap[c]);
                }
                else if (int.TryParse(c.ToString(), out int i) && normalMap.ContainsKey(c))
                {
                    result.Append(normalMap[c]);
                }
                else
                {
                    result.Append(c);
                }
            }
            else
            {
                // Otherwise use the default mapping.
                if (normalMap.ContainsKey(c))
                {
                    result.Append(normalMap[c]);
                }
                else
                {
                    result.Append(c);
                }
            }

            // State tracking
            // Track whether the parser is inside duration brackets.
            if (c == '[')
            {
                isInBracket = true;
            }
            else if (c == ']')
            {
                isInBracket = false;
            }
            // Track special prefixes whose following character requires the special mapping.
            // For horizontal or vertical mirroring, this handles Touches in zones D and E.
            // For 45-degree rotation, this handles Simai's unusual ">" and "<" without checking whether the Note is a Tap.
            if (specialPrefix.Contains(c))
            {
                isSpecialPrefix = true;
            }
        }

        return result.ToString();
    }
}