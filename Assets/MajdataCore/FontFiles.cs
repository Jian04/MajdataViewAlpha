using System;
using System.IO;
using System.Security.Cryptography;
using System.Collections.Generic;

namespace MajdataCore
{
    public static class FontFiles
    {
        private static readonly Dictionary<string, (long Ticks, long Length, string Copy)> loadCopies = new(StringComparer.OrdinalIgnoreCase);
        public static bool IsFontFile(string path)
        {
            var extension = Path.GetExtension(path) ?? "";
            return extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".otf", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".ttc", StringComparison.OrdinalIgnoreCase);
        }

        public static string DirectoryForApplication(string applicationDirectory)
        {
            var directory = new DirectoryInfo(applicationDirectory);
            if (directory.Parent?.Name.Equals("App", StringComparison.OrdinalIgnoreCase) == true)
                directory = new DirectoryInfo(Path.Combine(directory.Parent.FullName, "MajdataEdit"));
            return Path.Combine(directory.FullName, "Fonts");
        }

        public static string Resolve(string requested, string fontDirectory, string chartDirectory = null)
        {
            var path = requested.Trim().Trim('"');
            if (!IsFontFile(path)) throw new ArgumentException("字体文件必须为 TTF、OTF 或 TTC：" + requested);
            if (Path.IsPathRooted(path))
            {
                var fullPath = Path.GetFullPath(path);
                var previousRoot = Path.Combine(AlphaSession.InstallRoot, "Fonts") + Path.DirectorySeparatorChar;
                // Keep saved references to the former installation font directory valid after the move.
                return fullPath.StartsWith(previousRoot, StringComparison.OrdinalIgnoreCase)
                    ? Path.Combine(fontDirectory, fullPath.Substring(previousRoot.Length)) : fullPath;
            }
            if (!string.IsNullOrEmpty(chartDirectory))
            {
                var local = Path.GetFullPath(Path.Combine(chartDirectory, path));
                if (File.Exists(local)) return local;
            }
            if (path.Replace('\\', '/').StartsWith("Fonts/", StringComparison.OrdinalIgnoreCase)) path = path.Substring(6);
            return Path.GetFullPath(Path.Combine(fontDirectory, path));
        }

        public static string CreateLoadCopy(string path)
        {
            path = Path.GetFullPath(path);
            var info = new FileInfo(path);
            lock (loadCopies)
            {
                if (loadCopies.TryGetValue(path, out var existing) && existing.Ticks == info.LastWriteTimeUtc.Ticks &&
                    existing.Length == info.Length && File.Exists(existing.Copy)) return existing.Copy;
                // Font renderers retain open file handles. Keep the user-managed file replaceable.
                var bytes = File.ReadAllBytes(path);
                using var hash = SHA256.Create();
                var name = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "") + Path.GetExtension(path);
                var directory = Path.Combine(AlphaSession.EditorDataRoot, "Cache", "Fonts");
                Directory.CreateDirectory(directory);
                var copy = Path.Combine(directory, name);
                if (!File.Exists(copy)) File.WriteAllBytes(copy, bytes);
                loadCopies[path] = (info.LastWriteTimeUtc.Ticks, info.Length, copy);
                return copy;
            }
        }

    }
}
