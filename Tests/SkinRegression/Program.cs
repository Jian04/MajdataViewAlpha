using MajdataCore;
using UnityEngine;

var root = Path.Combine(Path.GetTempPath(), "alpha-skin-test-" + Guid.NewGuid().ToString("N"));
var edit = Path.Combine(root, "App", "MajdataEdit");
var view = Path.Combine(root, "App", "MajdataView");
Application.dataPath = Path.Combine(view, "MajdataView_Data");
Application.streamingAssetsPath = Path.Combine(Application.dataPath, "StreamingAssets");
var local = Path.Combine(edit, "Skin");
var bundled = Path.Combine(Application.streamingAssetsPath, "Skin");
int checks = 0;
void Check(bool result, string name) { checks++; if (!result) throw new Exception(name); }
void Pack(string directory)
{
    Directory.CreateDirectory(directory);
    foreach (var file in new[] { "tap", "tap_ex", "hold", "hold_ex", "star", "star_ex", "star_ex_double" })
        File.WriteAllText(Path.Combine(directory, file + ".png"), "test");
}
try
{
    foreach (var name in new[] { "sd", "dx" }) { Pack(Path.Combine(local, name)); Pack(Path.Combine(bundled, name)); }
    var skin = new CustomSkin();
    skin.LoadSkin("sd", "dx", "dx", "dx");
    Check(skin.Star_Ex.Path == Path.Combine(local, "dx", "star_ex.png"), "EX star must follow selected DX and external override");
    Check(skin.Star_Ex_Double.Path == Path.Combine(local, "dx", "star_ex_double.png"), "EX double must follow selected DX");
    Check(skin.Tap_Ex.Path == Path.Combine(local, "dx", "tap_ex.png"), "EX tap selection");
    Check(skin.Hold_Ex.Path == Path.Combine(local, "dx", "hold_ex.png"), "EX hold selection");
    Pack(Path.Combine(local, "custom-added"));
    Check(SkinPaths.Names(SkinPaths.Roots(edit)).Contains("custom-added"), "New folder absent from Edit choices");
    skin.LoadSkin("sd", "custom-added", "custom-added", "custom-added");
    Check(skin.Star.Path == Path.Combine(local, "custom-added", "star.png"), "Custom star not loaded by View");
    Check(skin.Star_Ex.Path == Path.Combine(local, "custom-added", "star_ex.png"), "Custom EX not loaded");
    Pack(Path.Combine(local, "custom-pink"));
    skin.LoadSkin("sd", "sd", "sd", "custom-pink");
    Check(skin.Star_Ex.Path == Path.Combine(local, "custom-pink", "star_ex.png"), "Real custom -pink folder treated as legacy alias");
    skin.LoadSkin("dx", "sd", "sd", "sd");
    Check(skin.Star_Ex.Path == Path.Combine(local, "sd", "star_ex.png"), "Switch back to SD retained DX EX");
    Directory.CreateDirectory(Path.Combine(local, "incomplete"));
    skin.LoadSkin("sd", "incomplete", "incomplete", "incomplete");
    Check(skin.Star_Ex.Path == Path.Combine(local, "sd", "star_ex.png"), "Missing custom EX must fall back to base");
    Pack(Path.Combine(bundled, "bundled-only"));
    skin.LoadSkin("sd", "dx", "dx", "bundled-only");
    Check(skin.Star_Ex.Path == Path.Combine(bundled, "bundled-only", "star_ex.png"), "Bundled fallback missing");
    Check(SkinPaths.Folder(SkinPaths.Roots(edit), "../escape", "fallback") == "fallback", "Unsafe folder name accepted");
    Console.WriteLine($"PASS: {checks} skin checks; actual CustomSkin loader with filesystem and sprite-path stubs.");
}
finally { Directory.Delete(root, true); }

public static class SpriteLoader
{
    public static Sprite LoadSpriteFromFile(string path) => new() { Path = path };
    public static Sprite LoadSpriteFromFile(string path, Vector4 border) => LoadSpriteFromFile(path);
}
namespace UnityEngine
{
    public class Sprite { public string Path; }
    public class Texture2D { }
    public struct Vector4 { public Vector4(float x, float y, float z, float w) { } }
    public class SpriteRenderer { public Sprite sprite; }
    public class GameObject { public T GetComponent<T>() where T : new() => new(); }
    public class MonoBehaviour { public GameObject gameObject = new(); }
    public static class Application { public static string dataPath, streamingAssetsPath; }
    public static class Debug { public static void Log(string text) { } }
}
