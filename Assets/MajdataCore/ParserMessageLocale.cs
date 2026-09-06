using System.Globalization;

namespace MajdataCore
{
    /// <summary>
    /// Chooses the single language used for parser messages. Showing Chinese and
    /// English together doubled the length of every squiggle tooltip, so the
    /// editor sets this from its own UI language and the player follows the
    /// system culture.
    /// </summary>
    public static class ParserMessageLocale
    {
        private static bool? _preferChinese;

        public static bool PreferChinese
        {
            get
            {
                _preferChinese ??= DetectChinese();
                return _preferChinese.Value;
            }
            set => _preferChinese = value;
        }

        /// <summary>
        /// Follows the given UI culture name, for example "zh-CN" or "en-US".
        /// </summary>
        public static void SetCulture(string cultureName)
        {
            _preferChinese = !string.IsNullOrEmpty(cultureName) &&
                             cultureName.StartsWith(
                                 "zh",
                                 System.StringComparison.OrdinalIgnoreCase);
        }

        public static string Pick(string chinese, string english) =>
            PreferChinese ? chinese : english;

        private static bool DetectChinese()
        {
            try
            {
                return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
                    .Equals("zh", System.StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return true;
            }
        }
    }
}
