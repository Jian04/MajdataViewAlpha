using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MajdataCore
{
    // Shared by Edit's picker and View's loader. External packs override bundled packs.
    public static class SkinPaths
    {
        public static string[] Roots(string applicationDirectory, string bundled = null, bool view = false)
        {
            var roots = new List<string>();
            var directory = new DirectoryInfo(applicationDirectory);
            if (view && directory.Parent != null)
                roots.Add(Path.Combine(directory.Parent.FullName, "MajdataEdit", "Skin"));
            for (var depth = 0; directory != null && depth < 6; depth++, directory = directory.Parent)
                roots.Add(Path.Combine(directory.FullName, "Skin"));
            if (!string.IsNullOrWhiteSpace(bundled)) roots.Add(bundled);
            return roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        public static string Folder(IEnumerable<string> roots, string name, string fallback)
        {
            if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name) || name == "." || name == "..") return fallback;
            return roots.Select(root => Path.Combine(root, name)).FirstOrDefault(Directory.Exists) ?? fallback;
        }
        public static string[] Names(IEnumerable<string> roots) => roots.SelectMany(Directory.GetDirectories)
            .Select(Path.GetFileName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();

    }
}
