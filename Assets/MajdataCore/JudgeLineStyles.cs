using System;
using System.Globalization;

namespace MajdataCore
{
    public static class JudgeLineStyles
    {
        public static readonly int[] Available = { 0, 1, 2, 3 };
        public static bool TryParse(string text, out int style)
        {
            if (string.Equals(text.Trim(), "false", StringComparison.OrdinalIgnoreCase))
            { style = 0; return true; }
            // Reserve positive IDs for additional styles without changing the chart format.
            return int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out style)
                && style >= 0 && style <= 65535;
        }
        public static int Supported(int style) => Array.IndexOf(Available, style) >= 0 ? style : 1;
    }
}
