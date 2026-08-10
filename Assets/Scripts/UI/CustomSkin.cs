using System.IO;
using UnityEngine;

public class CustomSkin : MonoBehaviour
{
    public Sprite Tap;
    public Sprite Tap_Each;
    public Sprite Tap_Break;
    public Sprite Tap_Ex;

    public Sprite Slide;
    public Sprite Slide_Each;
    public Sprite Slide_Break;
    public Sprite[] Wifi = new Sprite[11];
    public Sprite[] Wifi_Each = new Sprite[11];
    public Sprite[] Wifi_Break = new Sprite[11];

    public Sprite Star;
    public Sprite Star_Double;
    public Sprite Star_Each;
    public Sprite Star_Each_Double;
    public Sprite Star_Break;
    public Sprite Star_Break_Double;
    public Sprite Star_Ex;
    public Sprite Star_Ex_Double;

    public Sprite Hold;
    public Sprite Hold_On;
    public Sprite Hold_Off;
    public Sprite Hold_Each;
    public Sprite Hold_Each_On;
    public Sprite Hold_Ex;
    public Sprite Hold_Break;
    public Sprite Hold_Break_On;

    public Sprite[] Just = new Sprite[36];
    public Sprite[] JudgeText = new Sprite[5];
    public Sprite JudgeText_Break;
    public Sprite FastText;
    public Sprite LateText;

    public Sprite Touch;
    public Sprite Touch_Each;
    public Sprite Touch_Break;
    public Sprite TouchPoint;
    public Sprite TouchPoint_Each;
    public Sprite TouchPoint_Break;
    public Sprite TouchJust;
    public Sprite[] TouchBorder = new Sprite[2];
    public Sprite[] TouchBorder_Each = new Sprite[2];

    public Sprite[] TouchHold = new Sprite[5];
    public Sprite[] TouchHold_Break = new Sprite[5];

    public Sprite JudgeArea;

    public Texture2D test;
    private SpriteRenderer Outline;
    private string loadedSkinKey;

    // Start is called before the first frame update
    private void Start()
    {
        LoadSkin("dx");
    }

    public void LoadSkin(string skinName, string tapSkinName = null, string holdSkinName = null,
        string starSkinName = null, bool pinkStar = false)
    {
        // Prefer packaged skins so Editor and release builds resolve the same files.
        var external = Path.Combine(new DirectoryInfo(Application.dataPath).Parent.FullName, "Skin");
        var bundled = Path.Combine(Application.streamingAssetsPath, "Skin");
        var root = Directory.Exists(bundled) ? bundled : external;
        var requested = Path.Combine(root, SanitizeSkinName(skinName));
        var dx = Path.Combine(root, "dx");
        var path = Directory.Exists(requested)
            ? requested
            : Directory.Exists(dx)
                ? dx
                : root;
        var tapPath = ResolvePartPath(root, tapSkinName, path);
        var holdPath = ResolvePartPath(root, holdSkinName, path);
        var legacyPinkStar = TryGetPinkStarBase(starSkinName, out var starBaseSkinName);
        pinkStar |= legacyPinkStar;
        var starPath = ResolvePartPath(root, starBaseSkinName, path);
        var skinKey = string.Join("|", path, tapPath, holdPath, starPath, pinkStar);
        if (string.Equals(loadedSkinKey, skinKey, System.StringComparison.OrdinalIgnoreCase))
            return;
        loadedSkinKey = skinKey;
        Outline = gameObject.GetComponent<SpriteRenderer>();
        Debug.Log($"Loading skin: base={path}, tap={tapPath}, hold={holdPath}, star={starPath}");

        Outline.sprite = SpriteLoader.LoadSpriteFromFile(Path.Combine(path, "outline.png"));
        JudgeArea = LoadOrFallback(Path.Combine(path, "judge_area.png"), null);

        Tap = LoadPart(tapPath, path, "tap.png");
        Tap_Each = LoadPart(tapPath, path, "tap_each.png");
        Tap_Break = LoadPart(tapPath, path, "tap_break.png");
        Tap_Ex = SpriteLoader.LoadSpriteFromFile(Path.Combine(path, "tap_ex.png"));

        Slide = SpriteLoader.LoadSpriteFromFile(path + "/slide.png");
        Slide_Each = SpriteLoader.LoadSpriteFromFile(path + "/slide_each.png");
        Slide_Break = SpriteLoader.LoadSpriteFromFile(path + "/slide_break.png");
        for (var i = 0; i < 11; i++)
        {
            Wifi[i] = SpriteLoader.LoadSpriteFromFile(path + "/wifi_" + i + ".png");
            Wifi_Each[i] = SpriteLoader.LoadSpriteFromFile(path + "/wifi_each_" + i + ".png");
            Wifi_Break[i] = SpriteLoader.LoadSpriteFromFile(path + "/wifi_break_" + i + ".png");
        }

        Star = pinkStar
            ? LoadVariantPart(starPath, path, "star_pink.png", "star.png")
            : LoadPart(starPath, path, "star.png");
        Star_Double = pinkStar
            ? LoadVariantPart(starPath, path, "star_pink_double.png", "star_double.png")
            : LoadPart(starPath, path, "star_double.png");
        Star_Each = LoadPart(starPath, path, "star_each.png");
        Star_Each_Double = LoadPart(starPath, path, "star_each_double.png");
        Star_Break = LoadPart(starPath, path, "star_break.png");
        Star_Break_Double = LoadPart(starPath, path, "star_break_double.png");
        Star_Ex = SpriteLoader.LoadSpriteFromFile(Path.Combine(path, "star_ex.png"));
        Star_Ex_Double = SpriteLoader.LoadSpriteFromFile(Path.Combine(path, "star_ex_double.png"));

        var border = new Vector4(0, 58, 0, 58);
        Hold = LoadPart(holdPath, path, "hold.png", border);
        Hold_Each = LoadPart(holdPath, path, "hold_each.png", border);
        Hold_Ex = SpriteLoader.LoadSpriteFromFile(Path.Combine(path, "hold_ex.png"), border);
        Hold_Break = LoadPart(holdPath, path, "hold_break.png", border);
        Hold_On = LoadOptionalPart(holdPath, path, "hold_on.png", border, Hold);
        Hold_Off = LoadPart(holdPath, path, "hold_off.png", border);
        Hold_Each_On = LoadOptionalPart(holdPath, path, "hold_each_on.png", border, Hold_Each);
        Hold_Break_On = LoadOptionalPart(holdPath, path, "hold_break_on.png", border, Hold_Break);

        Just[0] = SpriteLoader.LoadSpriteFromFile(path + "/just_curv_r.png");
        Just[1] = SpriteLoader.LoadSpriteFromFile(path + "/just_str_r.png");
        Just[2] = SpriteLoader.LoadSpriteFromFile(path + "/just_wifi_u.png");
        Just[3] = SpriteLoader.LoadSpriteFromFile(path + "/just_curv_l.png");
        Just[4] = SpriteLoader.LoadSpriteFromFile(path + "/just_str_l.png");
        Just[5] = SpriteLoader.LoadSpriteFromFile(path + "/just_wifi_d.png");

        Just[6] = SpriteLoader.LoadSpriteFromFile(path + "/just_curv_r_fast_gr.png");
        Just[7] = SpriteLoader.LoadSpriteFromFile(path + "/just_str_r_fast_gr.png");
        Just[8] = SpriteLoader.LoadSpriteFromFile(path + "/just_wifi_u_fast_gr.png");
        Just[9] = SpriteLoader.LoadSpriteFromFile(path + "/just_curv_l_fast_gr.png");
        Just[10] = SpriteLoader.LoadSpriteFromFile(path + "/just_str_l_fast_gr.png");
        Just[11] = SpriteLoader.LoadSpriteFromFile(path + "/just_wifi_d_fast_gr.png");

        Just[12] = SpriteLoader.LoadSpriteFromFile(path + "/just_curv_r_fast_gd.png");
        Just[13] = SpriteLoader.LoadSpriteFromFile(path + "/just_str_r_fast_gd.png");
        Just[14] = SpriteLoader.LoadSpriteFromFile(path + "/just_wifi_u_fast_gd.png");
        Just[15] = SpriteLoader.LoadSpriteFromFile(path + "/just_curv_l_fast_gd.png");
        Just[16] = SpriteLoader.LoadSpriteFromFile(path + "/just_str_l_fast_gd.png");
        Just[17] = SpriteLoader.LoadSpriteFromFile(path + "/just_wifi_d_fast_gd.png");

        Just[18] = SpriteLoader.LoadSpriteFromFile(path + "/just_curv_r_late_gr.png");
        Just[19] = SpriteLoader.LoadSpriteFromFile(path + "/just_str_r_late_gr.png");
        Just[20] = SpriteLoader.LoadSpriteFromFile(path + "/just_wifi_u_late_gr.png");
        Just[21] = SpriteLoader.LoadSpriteFromFile(path + "/just_curv_l_late_gr.png");
        Just[22] = SpriteLoader.LoadSpriteFromFile(path + "/just_str_l_late_gr.png");
        Just[23] = SpriteLoader.LoadSpriteFromFile(path + "/just_wifi_d_late_gr.png");

        Just[24] = SpriteLoader.LoadSpriteFromFile(path + "/just_curv_r_late_gd.png");
        Just[25] = SpriteLoader.LoadSpriteFromFile(path + "/just_str_r_late_gd.png");
        Just[26] = SpriteLoader.LoadSpriteFromFile(path + "/just_wifi_u_late_gd.png");
        Just[27] = SpriteLoader.LoadSpriteFromFile(path + "/just_curv_l_late_gd.png");
        Just[28] = SpriteLoader.LoadSpriteFromFile(path + "/just_str_l_late_gd.png");
        Just[29] = SpriteLoader.LoadSpriteFromFile(path + "/just_wifi_d_late_gd.png");

        Just[30] = SpriteLoader.LoadSpriteFromFile(path + "/miss_curv_r.png");
        Just[31] = SpriteLoader.LoadSpriteFromFile(path + "/miss_str_r.png");
        Just[32] = SpriteLoader.LoadSpriteFromFile(path + "/miss_wifi_u.png");
        Just[33] = SpriteLoader.LoadSpriteFromFile(path + "/miss_curv_l.png");
        Just[34] = SpriteLoader.LoadSpriteFromFile(path + "/miss_str_l.png");
        Just[35] = SpriteLoader.LoadSpriteFromFile(path + "/miss_wifi_d.png");

        JudgeText[0] = SpriteLoader.LoadSpriteFromFile(path + "/judge_text_miss.png");
        JudgeText[1] = SpriteLoader.LoadSpriteFromFile(path + "/judge_text_good.png");
        JudgeText[2] = SpriteLoader.LoadSpriteFromFile(path + "/judge_text_great.png");
        JudgeText[3] = SpriteLoader.LoadSpriteFromFile(path + "/judge_text_perfect.png");
        JudgeText[4] = SpriteLoader.LoadSpriteFromFile(path + "/judge_text_cPerfect.png");
        JudgeText_Break = SpriteLoader.LoadSpriteFromFile(path + "/judge_text_break.png");

        FastText = SpriteLoader.LoadSpriteFromFile(path + "/fast.png");
        LateText = SpriteLoader.LoadSpriteFromFile(path + "/late.png");

        Touch = SpriteLoader.LoadSpriteFromFile(path + "/touch.png");
        Touch_Each = SpriteLoader.LoadSpriteFromFile(path + "/touch_each.png");
        TouchPoint = SpriteLoader.LoadSpriteFromFile(path + "/touch_point.png");
        TouchPoint_Each = SpriteLoader.LoadSpriteFromFile(path + "/touch_point_each.png");

        TouchJust = SpriteLoader.LoadSpriteFromFile(path + "/touch_just.png");

        TouchBorder[0] = SpriteLoader.LoadSpriteFromFile(path + "/touch_border_2.png");
        TouchBorder[1] = SpriteLoader.LoadSpriteFromFile(path + "/touch_border_3.png");
        TouchBorder_Each[0] = SpriteLoader.LoadSpriteFromFile(path + "/touch_border_2_each.png");
        TouchBorder_Each[1] = SpriteLoader.LoadSpriteFromFile(path + "/touch_border_3_each.png");

        for (var i = 0; i < 4; i++) TouchHold[i] = SpriteLoader.LoadSpriteFromFile(path + "/touchhold_" + i + ".png");
        TouchHold[4] = SpriteLoader.LoadSpriteFromFile(path + "/touchhold_border.png");

        // Third-party skins without break sprites use their normal sprites.
        Touch_Break = LoadOrFallback(path + "/touch_break.png", Touch);
        TouchPoint_Break = LoadOrFallback(path + "/touch_point_break.png", TouchPoint);
        for (var i = 0; i < 4; i++)
            TouchHold_Break[i] = LoadOrFallback(path + "/touchhold_break_" + i + ".png", TouchHold[i]);
        TouchHold_Break[4] = LoadOrFallback(path + "/touchhold_break_border.png", TouchHold[4]);

    }

    private static Sprite LoadOrFallback(string filePath, Sprite fallback)
        => File.Exists(filePath) ? SpriteLoader.LoadSpriteFromFile(filePath) : fallback;

    private static Sprite LoadPart(string primaryPath, string fallbackPath, string fileName)
    {
        var primary = Path.Combine(primaryPath, fileName);
        return SpriteLoader.LoadSpriteFromFile(File.Exists(primary)
            ? primary
            : Path.Combine(fallbackPath, fileName));
    }

    private static Sprite LoadVariantPart(string primaryPath, string fallbackPath, string variantFileName,
        string defaultFileName)
    {
        var primaryVariant = Path.Combine(primaryPath, variantFileName);
        if (File.Exists(primaryVariant))
            return SpriteLoader.LoadSpriteFromFile(primaryVariant);

        var fallbackVariant = Path.Combine(fallbackPath, variantFileName);
        return File.Exists(fallbackVariant)
            ? SpriteLoader.LoadSpriteFromFile(fallbackVariant)
            : LoadPart(primaryPath, fallbackPath, defaultFileName);
    }

    private static Sprite LoadPart(string primaryPath, string fallbackPath, string fileName, Vector4 border)
    {
        var primary = Path.Combine(primaryPath, fileName);
        return SpriteLoader.LoadSpriteFromFile(File.Exists(primary)
            ? primary
            : Path.Combine(fallbackPath, fileName), border);
    }

    private static Sprite LoadOptionalPart(string primaryPath, string fallbackPath, string fileName,
        Vector4 border, Sprite fallback)
    {
        var primary = Path.Combine(primaryPath, fileName);
        if (File.Exists(primary))
            return SpriteLoader.LoadSpriteFromFile(primary, border);
        var secondary = Path.Combine(fallbackPath, fileName);
        return File.Exists(secondary) ? SpriteLoader.LoadSpriteFromFile(secondary, border) : fallback;
    }

    private static string ResolvePartPath(string root, string skinName, string fallbackPath)
    {
        if (string.IsNullOrWhiteSpace(skinName))
            return fallbackPath;
        var candidate = Path.Combine(root, SanitizeSkinName(skinName));
        return Directory.Exists(candidate) ? candidate : fallbackPath;
    }

    private static bool TryGetPinkStarBase(string skinName, out string baseSkinName)
    {
        const string suffix = "-pink";
        if (!string.IsNullOrWhiteSpace(skinName) &&
            skinName.EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase))
        {
            baseSkinName = skinName.Substring(0, skinName.Length - suffix.Length);
            return !string.IsNullOrWhiteSpace(baseSkinName);
        }

        baseSkinName = skinName;
        return false;
    }

    private static string SanitizeSkinName(string skinName)
    {
        if (string.IsNullOrWhiteSpace(skinName))
            return "dx";

        skinName = Path.GetFileName(skinName.Trim());
        foreach (var invalid in Path.GetInvalidFileNameChars())
            skinName = skinName.Replace(invalid.ToString(), "");
        return string.IsNullOrWhiteSpace(skinName) ? "dx" : skinName;
    }

}
