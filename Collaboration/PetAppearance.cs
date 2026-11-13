using System.IO;
using System.Linq;
using System.Text.Json;

namespace MajdataCollaboration;

internal sealed record PetAppearance(string Name, string Path, bool IsImage)
{
    public string Category => IsImage ? "单图类" : "桌宠";

    public static PetAppearance Read(string directory)
    {
        var name = System.IO.Path.GetFileName(directory);
        var manifestPath = System.IO.Path.Combine(directory, "pet.json");
        if (File.Exists(manifestPath))
        {
            using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = manifest.RootElement;
            if (root.TryGetProperty("displayName", out var display)) name = display.GetString() ?? name;
            var mode = root.TryGetProperty("mode", out var selectedMode) ? selectedMode.GetString() :
                root.TryGetProperty("spriteVersionNumber", out var version) && version.GetInt32() == 2 ? "codex" : "image";
            if (mode is not ("codex" or "image")) throw new InvalidDataException("桌宠 mode 只能是 codex 或 image：" + manifestPath);
            if (mode == "codex")
            {
                if (!root.TryGetProperty("spritesheetPath", out var sheet) || string.IsNullOrWhiteSpace(sheet.GetString()))
                    throw new InvalidDataException("codex 模式需要 spritesheetPath：" + manifestPath);
                var atlas = System.IO.Path.Combine(directory, sheet.GetString()!);
                if (!File.Exists(atlas)) throw new FileNotFoundException("找不到桌宠图集：" + atlas, atlas);
                return new(name, atlas, false);
            }
        }
        var images = Directory.EnumerateFiles(directory).Where(path =>
            !System.IO.Path.GetFileName(path).StartsWith("._") &&
            System.IO.Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".bmp").ToArray();
        if (images.Length != 1) throw new InvalidDataException("单图类桌宠需要且只需要一张图片：" + directory);
        return new(name, images[0], true);
    }
}
