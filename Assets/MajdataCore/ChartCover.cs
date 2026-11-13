using System;
using System.IO;

namespace MajdataCore
{
    public static class ChartCover
    {
        public static string Find(string directory)
        {
            if (!Directory.Exists(directory)) return null;
            var files = Directory.GetFiles(directory);
            foreach (var name in new[] { "cover.mp4", "cover.gif", "cover.apng", "cover.png", "cover.jpg", "cover.jpeg", "cover.bmp", "bg.apng", "bg.png", "bg.jpg", "bg.jpeg" })
                foreach (var file in files)
                    if (string.Equals(Path.GetFileName(file), name, StringComparison.OrdinalIgnoreCase)) return file;
            return null;
        }

        public static string FindBackground(string directory, bool includeVideo = true)
        {
            if (!Directory.Exists(directory)) return null;
            var files = Directory.GetFiles(directory);
            foreach (var name in new[] { "pv.mp4", "mv.mp4", "bg.mp4", "bg.apng", "bg.png", "bg.jpg", "bg.jpeg" })
            {
                if (!includeVideo && name.EndsWith(".mp4", StringComparison.Ordinal)) continue;
                foreach (var file in files)
                    if (string.Equals(Path.GetFileName(file), name, StringComparison.OrdinalIgnoreCase)) return file;
            }
            return null;
        }
    }
}
