using System.IO;
using System.Windows.Media;
using MajdataEdit.Editor;

internal static class FontLibraryRegression
{
    internal static void Run()
    {
        var file = "font-library-regression.ttf";
        try
        {
            foreach (var path in Directory.EnumerateFiles("Fonts").Where(MajdataCore.FontFiles.IsFontFile))
            {
                var source = Path.GetFileName(path);
                Console.WriteLine("Checking font library: " + source);
                FontLibrary.Import(Path.GetFullPath(Path.Combine("Fonts", source)), file);
                var choice = FontLibrary.Load(file);
                if(choice.DisplayName!=Path.GetFileNameWithoutExtension(file))
                    throw new Exception("Font choices must display the name without its extension");
                if (!new Typeface(choice.Family, System.Windows.FontStyles.Normal,
                        System.Windows.FontWeights.Normal, System.Windows.FontStretches.Normal)
                    .TryGetGlyphTypeface(out var glyph) || glyph.CharacterToGlyphMap.Count == 0)
                    throw new Exception("Library font must resolve to actual glyphs: " + source);
                if (FontLibrary.ReadChoices(out var errors).All(font => font.FileName != file) || errors.Count != 0)
                    throw new Exception("Imported font must appear in the choices");
            }
            FontLibrary.Delete(file);
            if (File.Exists(Path.Combine(FontLibrary.DirectoryPath, file)))
                throw new Exception("Font must remain deletable after WPF loaded it");
            Console.WriteLine("PASS: font library TTF/OTF/TTC import, replacement, glyph loading and deletion");
        }
        finally { FontLibrary.Delete(file); }
    }
}
