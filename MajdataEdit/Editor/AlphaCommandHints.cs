using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using WPFLocalizeExtension.Engine;

namespace MajdataEdit.Editor;

internal static class AlphaCommandHints
{
    private const string BooleanTrue = "True";
    private const string BooleanFalse = "False";
    private const string BooleanChoices = BooleanTrue + "|" + BooleanFalse;

    private static string CurrentLanguage =>
        LocalizeDictionary.Instance.Culture?.TwoLetterISOLanguageName ??
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

    private static string Localized(string chinese, string english, string japanese) =>
        CurrentLanguage switch
        {
            "en" => english,
            "ja" => japanese,
            _ => chinese
        };

    private sealed record AlphaCommand(string Name, string Signature, string Description);

    private static string cachedCommandLanguage = "";
    private static AlphaCommand[] cachedCommands = Array.Empty<AlphaCommand>();

    private static AlphaCommand[] Commands
    {
        get
        {
            var language = CurrentLanguage;
            if (cachedCommands.Length != 0 &&
                string.Equals(cachedCommandLanguage, language, StringComparison.Ordinal))
                return cachedCommands;

            cachedCommandLanguage = language;
            cachedCommands = BuildCommands();
            return cachedCommands;
        }
    }

    private static AlphaCommand[] BuildCommands()
    {
        if (CurrentLanguage == "en")
            return new AlphaCommand[]
            {
                new("SV", "<SV*multiplier> / <SV*tap=multiplier,slide=multiplier>", "True scroll-speed multiplier. Supports per-note values and NULL reset."),
                new("HS", "<HS*multiplier> / <HS*tap=multiplier,slide=multiplier>", "Traditional fall-speed multiplier. Supports per-note values and NULL reset."),
                new("SPAWN", "<SPAWN*radius> / <SPAWN*tap=radius,hold=radius>", "Ring-note visual spawn radius from -4.8 to 4.8. 0 is center; -4.8 is the opposite judge line. Supports NULL reset."),
                new("BOUNCE", "<BOUNCE*duration> / <BOUNCE*tap=8:1,hold=4:1> / <BOUNCE*NULL>", "Makes Tap, Star, Each, and Hold notes travel from the judge line to the spawn radius and back. Typed values override individual note families."),
                new("COLOR", "<COLOR*RRGGBB>", "Colors notes. Supports NULL reset and per-type values such as tap=FF0000."),
                new("SIZE", "<SIZE*scale> / <SIZE*tap=(localX,localY)> / <SIZE*slide=scale>", "Supports per-note values and NULL reset. Global scale excludes Slide bodies; use slide= for those."),
                new("ALPHA", "<ALPHA*opacity>", "Note opacity from 0 (transparent) to 1 (opaque). Supports NULL reset."),
                new("JLINE", "<JLINE*RRGGBB> / <JLINE*(RRGGBB[,duration])>", "Transitions the judge-line color during playback. NULL restores the skin color."),
                new("TEXT", "<TEXT*(content[,duration])>", "Top-left caption. Without a duration it remains until the next TEXT command."),
                new("SHOWJUDGELINE", $"<SHOWJUDGELINE*({BooleanChoices}[,duration])>", "Shows or hides the judge line, optionally with a transition."),
                new("SHOWJUDGEAREA", $"<SHOWJUDGEAREA*({BooleanChoices}[,duration])>", "Shows or hides judgment areas, optionally with a transition."),
                new("SHOWJUDGETEXT", $"<SHOWJUDGETEXT*({BooleanChoices}[,duration])>", "Shows or hides judgment text such as Critical Perfect."),
                new("SHOWJUDGEINFO", $"<SHOWJUDGEINFO*({BooleanChoices}[,duration])>", "Shows or hides the left-side judgment statistics."),
                new("SHOWCOMBOINFO", $"<SHOWCOMBOINFO*({BooleanChoices}[,duration])>", "Shows or hides the right-side combo and achievement display."),
                new("COMBODISPLAY", "<COMBODISPLAY*(mode[,duration])>", "Changes the center display: NONE, COMBO, SCORE, ACC, DXACC, DXSCORE, and others."),
                new("OUTERBRIGHTNESS", "<OUTERBRIGHTNESS*(brightness[,duration])>", "Outer-ring brightness from 0 (black) to 1 (full)."),
                new("INNERBRIGHTNESS", "<INNERBRIGHTNESS*(brightness[,duration])>", "Inner-area brightness from 0 (black) to 1 (full)."),
                new("GAUSSIAN", $"<GAUSSIAN*({BooleanTrue},strength[,duration])>", "Gaussian blur. Strength is required."),
                new("NEON", $"<NEON*({BooleanTrue},strength[,duration])>", "Neon glow and RGB separation."),
                new("TRAIL", $"<TRAIL*({BooleanTrue},strength[,duration])>", "Previous-frame trail."),
                new("FADE", $"<FADE*({BooleanTrue},strength[,duration])>", "Fades the frame toward black."),
                new("FLASH", $"<FLASH*({BooleanTrue},strength[,duration])>", "Flashes the frame toward white."),
                new("BRIGHTNESS", $"<BRIGHTNESS*({BooleanTrue},strength[,duration])>", "Adjusts frame brightness."),
                new("SATURATION", $"<SATURATION*({BooleanTrue},strength[,duration])>", "Adjusts frame saturation."),
                new("CONTRAST", $"<CONTRAST*({BooleanTrue},strength[,duration])>", "Adjusts frame contrast."),
                new("RAINBOW", $"<RAINBOW*({BooleanTrue},strength[,duration])>", "Cycles the frame hue."),
                new("VIGNETTE", $"<VIGNETTE*({BooleanTrue},strength[,duration])>", "Circular vignette effect."),
                new("ZOOM", $"<ZOOM*({BooleanTrue},scale[,duration])>", "Zooms the frame; maximum scale is 8."),
                new("GLITCH", $"<GLITCH*({BooleanTrue},strength[,duration])>", "Glitch displacement effect."),
                new("TVNOISE", $"<TVNOISE*({BooleanTrue},strength[,duration])>", "TV scan-line noise."),
                new("HUE", $"<HUE*({BooleanTrue},degrees[,duration])>", "Rotates the frame hue in degrees."),
                new("TINT", $"<TINT*({BooleanTrue},RRGGBB,strength[,duration])> / <TINT*({BooleanFalse}[,duration])>", "Blends any color, including black and white."),
                new("MOVE", $"<MOVE*({BooleanTrue},dx,dy[,duration])> / <MOVE*({BooleanFalse}[,duration])>", "Moves the frame relative to its center."),
                new("ROTATE", $"<ROTATE*({BooleanTrue},degrees[,duration])>", "Rotates the frame around its center; negative angles are allowed."),
                new("SHAKE", $"<SHAKE*({BooleanTrue},strength,frequency[,degrees,duration])>", "Shakes the camera. Angle and transition are independently optional; leave the angle empty to set only transition."),
                new("AUDIO", $"<AUDIO*({BooleanTrue},relative/path.ogg)> / <AUDIO*({BooleanFalse})>", "Plays one OGG, WAV, or MP3 file. False stops the current clip early."),
                new("PVOVERLAY", $"<PVOVERLAY*({BooleanTrue},relative/path.mp4[,duration])> / <PVOVERLAY*({BooleanFalse}[,duration])>", "Replaces the chart PV with PNG, JPG, or MP4 media. The optional duration crossfades the original PV or the preceding overlay.")
            };

        if (CurrentLanguage == "ja")
            return new AlphaCommand[]
            {
                new("SV", "<SV*倍率> / <SV*tap=倍率,slide=倍率>", "実スクロール速度の倍率。ノーツ種別ごとの設定と NULL リセットに対応します。"),
                new("HS", "<HS*倍率> / <HS*tap=倍率,slide=倍率>", "従来の落下速度の倍率。ノーツ種別ごとの設定と NULL リセットに対応します。"),
                new("SPAWN", "<SPAWN*半径> / <SPAWN*tap=半径,hold=半径>", "リングノーツの出現半径（-4.8～4.8）。0 は中央、-4.8 は反対側の判定ラインです。NULL でリセットします。"),
                new("BOUNCE", "<BOUNCE*時間> / <BOUNCE*tap=8:1,hold=4:1> / <BOUNCE*NULL>", "Tap、Star、Each、Hold を判定ラインから出現半径まで移動させ、判定ラインへ戻します。種類別指定にも対応します。"),
                new("COLOR", "<COLOR*RRGGBB>", "ノーツを着色します。NULL リセットと tap=FF0000 のような種別指定に対応します。"),
                new("SIZE", "<SIZE*倍率> / <SIZE*tap=(X倍率,Y倍率)> / <SIZE*slide=倍率>", "種別指定と NULL リセットに対応します。全体倍率は Slide 本体を変更しません。"),
                new("ALPHA", "<ALPHA*透明度>", "ノーツの透明度。0 は透明、1 は不透明です。"),
                new("JLINE", "<JLINE*RRGGBB> / <JLINE*(RRGGBB[,時間])>", "再生中の判定ライン色を切り替えます。NULL でスキン色に戻します。"),
                new("TEXT", "<TEXT*(内容[,時間])>", "左上の字幕。時間を省略すると次の TEXT まで表示します。"),
                new("SHOWJUDGELINE", $"<SHOWJUDGELINE*({BooleanChoices}[,時間])>", "判定ラインを表示または非表示にします。"),
                new("SHOWJUDGEAREA", $"<SHOWJUDGEAREA*({BooleanChoices}[,時間])>", "判定エリアを表示または非表示にします。"),
                new("SHOWJUDGETEXT", $"<SHOWJUDGETEXT*({BooleanChoices}[,時間])>", "Critical Perfect などの判定文字を表示または非表示にします。"),
                new("SHOWJUDGEINFO", $"<SHOWJUDGEINFO*({BooleanChoices}[,時間])>", "左側の判定集計を表示または非表示にします。"),
                new("SHOWCOMBOINFO", $"<SHOWCOMBOINFO*({BooleanChoices}[,時間])>", "右側のコンボと達成率を表示または非表示にします。"),
                new("COMBODISPLAY", "<COMBODISPLAY*(モード[,時間])>", "中央表示を切り替えます：NONE、COMBO、SCORE、ACC、DXACC、DXSCORE など。"),
                new("OUTERBRIGHTNESS", "<OUTERBRIGHTNESS*(明るさ[,時間])>", "外周の明るさ。0 は黒、1 は最大です。"),
                new("INNERBRIGHTNESS", "<INNERBRIGHTNESS*(明るさ[,時間])>", "内側の明るさ。0 は黒、1 は最大です。"),
                new("GAUSSIAN", $"<GAUSSIAN*({BooleanTrue},強度[,時間])>", "ガウスぼかし。強度は必須です。"),
                new("NEON", $"<NEON*({BooleanTrue},強度[,時間])>", "ネオン発光と RGB 分離。"),
                new("TRAIL", $"<TRAIL*({BooleanTrue},強度[,時間])>", "残像エフェクト。"),
                new("FADE", $"<FADE*({BooleanTrue},強度[,時間])>", "画面を黒へフェード。"),
                new("FLASH", $"<FLASH*({BooleanTrue},強度[,時間])>", "画面を白へフラッシュ。"),
                new("BRIGHTNESS", $"<BRIGHTNESS*({BooleanTrue},強度[,時間])>", "画面の明るさを調整します。"),
                new("SATURATION", $"<SATURATION*({BooleanTrue},強度[,時間])>", "画面の彩度を調整します。"),
                new("CONTRAST", $"<CONTRAST*({BooleanTrue},強度[,時間])>", "画面のコントラストを調整します。"),
                new("RAINBOW", $"<RAINBOW*({BooleanTrue},強度[,時間])>", "色相を循環させます。"),
                new("VIGNETTE", $"<VIGNETTE*({BooleanTrue},強度[,時間])>", "円形のビネット効果。"),
                new("ZOOM", $"<ZOOM*({BooleanTrue},倍率[,時間])>", "画面を拡大します。最大倍率は 8 です。"),
                new("GLITCH", $"<GLITCH*({BooleanTrue},強度[,時間])>", "グリッチずれ効果。"),
                new("TVNOISE", $"<TVNOISE*({BooleanTrue},強度[,時間])>", "テレビ走査線ノイズ。"),
                new("HUE", $"<HUE*({BooleanTrue},角度[,時間])>", "画面の色相を度単位で回転します。"),
                new("TINT", $"<TINT*({BooleanTrue},RRGGBB,強度[,時間])> / <TINT*({BooleanFalse}[,時間])>", "黒や白を含む任意の色を合成します。"),
                new("MOVE", $"<MOVE*({BooleanTrue},dx,dy[,時間])> / <MOVE*({BooleanFalse}[,時間])>", "画面中央を基準に移動します。"),
                new("ROTATE", $"<ROTATE*({BooleanTrue},角度[,時間])>", "画面中央を基準に回転します。負の角度も使用できます。"),
                new("SHAKE", $"<SHAKE*({BooleanTrue},強度,周波数[,角度,時間])>", "カメラを振動させます。角度と切替時間は個別に省略でき、切替時間だけの場合は角度を空欄にします。"),
                new("AUDIO", $"<AUDIO*({BooleanTrue},相対パス.ogg)> / <AUDIO*({BooleanFalse})>", "OGG、WAV、MP3 を1回再生します。False で途中停止できます。"),
                new("PVOVERLAY", $"<PVOVERLAY*({BooleanTrue},相対パス.mp4[,時間])> / <PVOVERLAY*({BooleanFalse}[,時間])>", "PNG、JPG、MP4 で譜面 PV を置き換えます。時間を指定すると元の PV または直前のメディアとクロスフェードします。")
            };

        return new AlphaCommand[]
        {
        new("SV", "<SV*倍率> / <SV*tap=倍率,slide=倍率>", "真实 SV 倍率。支持按音符类型设置及 NULL 恢复。"),
        new("HS", "<HS*倍率> / <HS*tap=倍率,slide=倍率>", "传统下落速度倍率。支持按音符类型设置及 NULL 恢复。"),
        new("SPAWN", "<SPAWN*半径> / <SPAWN*tap=半径,hold=半径>", "环形音符视觉出生半径，范围 -4.8～4.8；0 是中心，-4.8 是对面判定线。支持 NULL 恢复。"),
        new("BOUNCE", "<BOUNCE*时长> / <BOUNCE*tap=8:1,hold=4:1> / <BOUNCE*NULL>", "默认让 Tap、Star、Each、Hold 从判定线运动到生成半径后回落；可按音符类型分别设置。"),
        new("COLOR", "<COLOR*RRGGBB>", "音符染色。支持 NULL 恢复，也支持 tap=FF0000 这种按类型设置。"),
        new("SIZE", "<SIZE*倍率> / <SIZE*tap=(局部X,局部Y)> / <SIZE*slide=倍率>", "支持按音符类型设置与 NULL 恢复。全局倍率不缩放 Slide 体，Slide 必须写 slide=。"),
        new("ALPHA", "<ALPHA*透明度>", "音符透明度，0 为透明，1 为不透明。支持 NULL 恢复。"),
        new("JLINE", "<JLINE*RRGGBB> / <JLINE*(RRGGBB[,过渡时间])>", "播放时渐变判定线颜色；NULL 恢复皮肤颜色，停止播放后不会影响待机判定线。"),
        new("TEXT", "<TEXT*(内容[,持续时间])>", "左上字幕。不写持续时间时会保持到下一条 TEXT。"),
        new("SHOWJUDGELINE", $"<SHOWJUDGELINE*({BooleanChoices}[,过渡时间])>", "渐变显示或隐藏判定线；省略过渡时间时立即切换。"),
        new("SHOWJUDGEAREA", $"<SHOWJUDGEAREA*({BooleanChoices}[,过渡时间])>", "渐变显示或隐藏判定区；省略过渡时间时立即切换。"),
        new("SHOWJUDGETEXT", $"<SHOWJUDGETEXT*({BooleanChoices}[,过渡时间])>", "渐变显示或隐藏 Critical Perfect 等判定文字。"),
        new("SHOWJUDGEINFO", $"<SHOWJUDGEINFO*({BooleanChoices}[,过渡时间])>", "渐变显示或隐藏左侧判定统计。"),
        new("SHOWCOMBOINFO", $"<SHOWCOMBOINFO*({BooleanChoices}[,过渡时间])>", "渐变显示或隐藏右侧 combo / 达成率信息。"),
        new("COMBODISPLAY", "<COMBODISPLAY*(模式[,过渡时间])>", "切换中间显示内容。模式: NONE / COMBO / SCORE / ACC / DXACC / DXSCORE 等。"),
        new("OUTERBRIGHTNESS", "<OUTERBRIGHTNESS*(亮度[,过渡时间])>", "外圈亮度，0 为全黑，1 为全亮。"),
        new("INNERBRIGHTNESS", "<INNERBRIGHTNESS*(亮度[,过渡时间])>", "内圈亮度，0 为全黑，1 为全亮。"),
        new("GAUSSIAN", $"<GAUSSIAN*({BooleanTrue},强度[,过渡时间])>", "高斯模糊。强度必填，省略过渡时间时立即生效。"),
        new("NEON", $"<NEON*({BooleanTrue},强度[,过渡时间])>", "霓虹与 RGB 分离。"),
        new("TRAIL", $"<TRAIL*({BooleanTrue},强度[,过渡时间])>", "拖影残影。"),
        new("FADE", $"<FADE*({BooleanTrue},强度[,过渡时间])>", "压黑画面。"),
        new("FLASH", $"<FLASH*({BooleanTrue},强度[,过渡时间])>", "闪白画面。"),
        new("BRIGHTNESS", $"<BRIGHTNESS*({BooleanTrue},强度[,过渡时间])>", "调整画面亮度。"),
        new("SATURATION", $"<SATURATION*({BooleanTrue},强度[,过渡时间])>", "调整画面饱和度。"),
        new("CONTRAST", $"<CONTRAST*({BooleanTrue},强度[,过渡时间])>", "调整画面对比度。"),
        new("RAINBOW", $"<RAINBOW*({BooleanTrue},强度[,过渡时间])>", "循环改变色相。"),
        new("VIGNETTE", $"<VIGNETTE*({BooleanTrue},强度[,过渡时间])>", "暗角特效。"),
        new("ZOOM", $"<ZOOM*({BooleanTrue},倍率[,过渡时间])>", "画面缩放，倍率最大 8（约两倍）。"),
        new("GLITCH", $"<GLITCH*({BooleanTrue},强度[,过渡时间])>", "故障错位特效。"),
        new("TVNOISE", $"<TVNOISE*({BooleanTrue},强度[,过渡时间])>", "电视扫描噪声。"),
        new("HUE", $"<HUE*({BooleanTrue},角度[,过渡时间])>", "旋转整幅画面的色相，单位为度；灰色区域基本不变。"),
        new("TINT", $"<TINT*({BooleanTrue},RRGGBB,强度[,过渡时间])> / <TINT*({BooleanFalse}[,过渡时间])>", "按强度叠加任意颜色，包括黑色和白色。"),
        new("MOVE", $"<MOVE*({BooleanTrue},dx,dy[,过渡时间])> / <MOVE*({BooleanFalse}[,过渡时间])>", "平移画面；dx/dy 是相对画面中心的目标坐标。"),
        new("ROTATE", $"<ROTATE*({BooleanTrue},角度[,过渡时间])>", "画面绕中心旋转，角度可为负。"),
        new("SHAKE", $"<SHAKE*({BooleanTrue},强度,频率[,角度,过渡时间])>", "镜头震动；角度和过渡时间可分别省略，只写过渡时间时保留空的角度位置。"),
        new("AUDIO", $"<AUDIO*({BooleanTrue},相对路径.ogg)> / <AUDIO*({BooleanFalse})>", "播放一次 OGG、WAV 或 MP3；False 可中途停止当前音频。"),
        new("PVOVERLAY", $"<PVOVERLAY*({BooleanTrue},相对路径.mp4[,过渡时间])> / <PVOVERLAY*({BooleanFalse}[,过渡时间])>", "用 PNG、JPG 或 MP4 替换谱面 PV；可选时间用于和原 PV 或前一个覆盖媒体交叉渐变。")
        };
    }

    private static string? GetStateSignature(string name, bool? enabled = null, int parameterCount = 1)
    {
        var duration = Localized("过渡时间", "duration", "時間");
        var strength = Localized("强度", "strength", "強度");
        var scale = Localized("倍率", "scale", "倍率");
        var degrees = Localized("角度", "degrees", "角度");
        var frequency = Localized("频率", "frequency", "周波数");
        string? on = name switch
        {
            "TINT" => $"<TINT*({BooleanTrue},RRGGBB,{strength}[,{duration}])>",
            "MOVE" => $"<MOVE*({BooleanTrue},dx,dy[,{duration}])>",
            "SHAKE" => $"<SHAKE*({BooleanTrue},{strength},{frequency}[,{degrees},{duration}])>",
            "ZOOM" => $"<ZOOM*({BooleanTrue},{scale}[,{duration}])>",
            "HUE" or "ROTATE" => $"<{name}*({BooleanTrue},{degrees}[,{duration}])>",
            "GAUSSIAN" or "NEON" or "TRAIL" or "FADE" or "FLASH" or
            "BRIGHTNESS" or "SATURATION" or "CONTRAST" or "RAINBOW" or
            "VIGNETTE" or "GLITCH" or "TVNOISE" =>
                $"<{name}*({BooleanTrue},{strength}[,{duration}])>",
            _ => null
        };
        if (on == null)
            return null;

        var off = $"<{name}*({BooleanFalse}[,{duration}])>";
        return enabled switch
        {
            true => on,
            false => off,
            _ => on + " | " + off
        };
    }

    private static int RequiredEffectParameterCount(string name) => name switch
    {
        "TINT" or "MOVE" or "SHAKE" => 3,
        _ => 2
    };

    private static string? GetStateSignatureOverview(string name)
    {
        if (!AlphaOverloadProvider.IsScreenEffectName(name))
            return null;

        return GetStateSignature(name);
    }

    private static CompletionWindow? completionWindow;
    private static bool durationCompletionActive;
    private static bool durationCompletionFromSelection;
    private static int durationCompletionOffset = -1;
    private static OverloadInsightWindow? insightWindow;
    private static AlphaOverloadProvider? insightProvider;
    private static AlphaCommand? insightCommand;
    private static bool completionRefreshQueued;

    private static Brush HintBackground => ThemeBrush("EditorBackground", Color.FromRgb(0x1E, 0x1E, 0x1E));
    private static Brush HintForeground => ThemeBrush("ButtonForeground", Color.FromRgb(0xD4, 0xD4, 0xD4));
    private static Brush HintSecondary => ThemeBrush("ButtonForeground", Color.FromRgb(0x9A, 0x9A, 0x9A));
    private static Brush HintOptional => WithOpacity(HintSecondary, 0.58);
    private static Brush HintBorder => ThemeBrush("MenuSeparator", Color.FromRgb(0x45, 0x45, 0x48));
    private static Brush HintAccent => ThemeBrush("HelperForeground", Color.FromRgb(0x56, 0x9C, 0xD6));
    private static Brush HintParamActive => ThemeBrush("ScrollThumb", Color.FromRgb(0x26, 0x4F, 0x78));
    private static Brush HintSelection => IsLightHintTheme()
        ? Frozen(new SolidColorBrush(Color.FromRgb(0x63, 0xA8, 0xD8)))
        : Frozen(new SolidColorBrush(Color.FromRgb(0x09, 0x47, 0x71)));
    private static readonly FontFamily HintMonoFont = new("Cascadia Mono, Consolas");
    private static readonly Regex DurationTargetPattern = new(
        @"(?i)(?:[1-8][bxfm]*h[bxfm]*|[ABCDE](?:[1-8])?[bfx]*h[bfx]*|(?:[1-8]d?|[ABDE][1-8]|C1?)[bxfm!?]*(?:[-<>^](?:[1-8]d?|[ABDE][1-8]|C1?)[bxfm]*)+|[1-8][bxfm]*(?:(?:pp|qq|rp|rq)[1-8]|V[1-8]{2}|[-<>^vpqszw][1-8])[bxfm]*|(?:(?:pp|qq|rp|rq)[1-8]|V[1-8]{2}|[-<>^vpqszw][1-8]))$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly string[] CommonDurations = { "[8:1]", "[4:1]", "[2:1]", "[16:3]", "[1:0]" };

    private static Brush Frozen(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    private static Brush ThemeBrush(string key, Color fallback) =>
        Application.Current?.TryFindResource(key) as Brush ?? Frozen(new SolidColorBrush(fallback));

    private static bool IsLightHintTheme()
    {
        if (HintBackground is not SolidColorBrush brush)
            return false;
        var color = brush.Color;
        return (color.R * 299 + color.G * 587 + color.B * 114) / 1000d > 150d;
    }

    public static void Attach(TextEditor editor)
    {
        var selectionHintTimer = new DispatcherTimer(DispatcherPriority.Background, editor.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(360)
        };
        var selectionAnchor = -1;
        var selectionLength = 0;
        var mouseSelecting = false;

        void QueueStableSelectionHint()
        {
            selectionHintTimer.Stop();
            selectionAnchor = editor.SelectionStart;
            selectionLength = editor.SelectionLength;
            if (selectionLength > 0)
                selectionHintTimer.Start();
        }

        selectionHintTimer.Tick += (_, _) =>
        {
            selectionHintTimer.Stop();
            if (mouseSelecting || !editor.IsKeyboardFocusWithin ||
                editor.SelectionStart != selectionAnchor || editor.SelectionLength != selectionLength ||
                selectionLength == 0)
                return;

            var selection = editor.SelectedText;
            if (!TryGetSelectedDurationTarget(selection, out var slideTarget))
                return;
            ShowDurationCompletion(editor.TextArea, selectionAnchor + selectionLength, slideTarget, true);
        };

        editor.TextArea.TextEntered += (_, e) => OnTextEntered(editor.TextArea, e);
        editor.TextArea.TextEntering += (_, e) => OnTextEntering(editor.TextArea, e);
        editor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            if (completionWindow != null && !durationCompletionActive &&
                !TryGetAlphaPrefix(editor.TextArea, out _, out _))
                completionWindow.Close();
            UpdateInsight(editor.TextArea);
            QueueCompletionRefresh(editor);
        };
        editor.TextArea.PreviewTextInput += (_, _) => CloseDurationCompletion();
        editor.TextArea.PreviewMouseLeftButtonDown += (_, _) =>
        {
            mouseSelecting = true;
            selectionHintTimer.Stop();
            CloseDurationCompletion();
        };
        editor.TextArea.PreviewMouseLeftButtonUp += (_, _) =>
        {
            mouseSelecting = false;
            QueueStableSelectionHint();
        };
        editor.TextArea.SelectionChanged += (_, _) =>
        {
            if (!mouseSelecting)
                QueueStableSelectionHint();
        };
        editor.TextArea.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Tab)
                return;
            if (completionWindow?.CompletionList.SelectedItem != null)
            {
                e.Handled = true;
                completionWindow.CompletionList.RequestInsertion(e);
                return;
            }
            if (TryHandleAlphaArgumentTab(editor.TextArea))
                e.Handled = true;
        };
        editor.TextChanged += (_, _) => QueueCompletionRefresh(editor);
    }

    private static void QueueCompletionRefresh(TextEditor editor)
    {
        if (completionRefreshQueued)
            return;

        completionRefreshQueued = true;
        editor.Dispatcher.BeginInvoke(() =>
        {
            completionRefreshQueued = false;
            if (editor.SelectionLength != 0)
                return;
            if (TryShowCaretCommandCompletion(editor.TextArea))
                return;
            UpdateDurationCompletion(editor.TextArea);
        }, DispatcherPriority.Background);
    }

    private static bool TryShowCaretCommandCompletion(TextArea textArea)
    {
        if (completionWindow != null)
            return true;

        if (TryGetAlphaPrefix(textArea, out var nameStart, out var prefix))
        {
            if (Commands.Any(command => string.Equals(command.Name, prefix,
                    StringComparison.OrdinalIgnoreCase)))
                return false;
            ShowCompletion(textArea, prefix, nameStart);
            return true;
        }

        var caret = textArea.Caret.Offset;
        if (caret <= 0 || textArea.Document.GetCharAt(caret - 1) != '<' ||
            !IsCellStart(textArea.Document, caret - 1))
            return false;
        ShowCompletion(textArea, string.Empty, caret);
        return true;
    }

    private static void UpdateDurationCompletion(TextArea textArea)
    {
        if (!TryGetDurationTarget(textArea, out var slideTarget))
        {
            CloseDurationCompletion();
            return;
        }
        ShowDurationCompletion(textArea, textArea.Caret.Offset, slideTarget, false);
    }

    private static void ShowDurationCompletion(
        TextArea textArea,
        int insertionOffset,
        bool slideTarget,
        bool fromSelection)
    {
        if (durationCompletionActive && durationCompletionOffset == insertionOffset &&
            durationCompletionFromSelection == fromSelection)
            return;
        completionWindow?.Close();
        var window = new CompletionWindow(textArea)
        {
            StartOffset = insertionOffset,
            EndOffset = insertionOffset,
            Width = 124,
            MinWidth = 124
        };
        completionWindow = window;
        durationCompletionOffset = insertionOffset;
        durationCompletionFromSelection = fromSelection;
        ApplyDarkStyle(window);
        foreach (var duration in CommonDurations.Where(value => !slideTarget || value != "[1:0]"))
            window.CompletionList.CompletionData.Add(new DurationCompletionData(duration));
        if (window.CompletionList.CompletionData.Count > 0)
            window.CompletionList.SelectedItem = window.CompletionList.CompletionData[0];
        durationCompletionActive = true;
        window.Closed += (_, _) =>
        {
            if (!ReferenceEquals(completionWindow, window))
                return;
            completionWindow = null;
            durationCompletionActive = false;
            durationCompletionFromSelection = false;
            durationCompletionOffset = -1;
        };
        window.Show();
    }

    private static bool TryGetDurationTarget(TextArea textArea, out bool slideTarget)
    {
        slideTarget = false;
        var document = textArea.Document;
        var caret = textArea.Caret.Offset;
        if (caret <= 0 || caret > document.TextLength)
            return false;
        var line = document.GetLineByOffset(caret);
        var prefix = document.GetText(line.Offset, caret - line.Offset);
        if (IsInsideAlphaCommand(prefix) || prefix.TrimStart().StartsWith("||", StringComparison.Ordinal))
            return false;
        var tail = prefix.Length > 48 ? prefix[^48..] : prefix;
        if (tail.EndsWith(']'))
            return false;
        var match = DurationTargetPattern.Match(tail);
        if (!match.Success || match.Index + match.Length != tail.Length)
            return false;
        slideTarget = Regex.IsMatch(match.Value,
            @"(?i)(?:pp|qq|rp|rq|V|[-<>^vpqszw])", RegexOptions.CultureInvariant);
        return true;
    }

    private static bool TryGetSelectedDurationTarget(string selection, out bool slideTarget)
    {
        slideTarget = false;
        if (string.IsNullOrWhiteSpace(selection) ||
            selection.IndexOfAny(new[] { '\r', '\n', ',', '/' }) >= 0)
            return false;

        var candidate = selection.Trim();
        if (!DurationTargetPattern.IsMatch(candidate))
            return false;

        slideTarget = Regex.IsMatch(candidate,
            @"(?i)(?:pp|qq|rp|rq|V|[-<>^vpqszw])", RegexOptions.CultureInvariant);
        return true;
    }

    private static bool IsInsideAlphaCommand(string prefix)
    {
        var open = prefix.LastIndexOf('<');
        if (open <= prefix.LastIndexOf('>') || open + 1 >= prefix.Length)
            return false;
        if (!char.IsLetter(prefix[open + 1]))
            return false;

        for (var i = open - 1; i >= 0; i--)
        {
            if (char.IsWhiteSpace(prefix[i]))
                continue;
            return prefix[i] is ',' or '}' or '>' or ')';
        }
        return true;
    }

    private static void CloseDurationCompletion()
    {
        if (durationCompletionActive)
            completionWindow?.Close();
    }

    private static void OnTextEntering(TextArea textArea, TextCompositionEventArgs e)
    {
        if (e.Text.Length == 0)
            return;

        var document = textArea.Document;
        var caret = textArea.Caret.Offset;
        var typed = e.Text[0];
        if (typed is '>' or ')' &&
            caret < document.TextLength && document.GetCharAt(caret) == typed)
        {
            completionWindow?.Close();
            textArea.Caret.Offset = caret + 1;
            e.Handled = true;
            return;
        }
        if (typed == '*' && caret > 0 && document.GetCharAt(caret - 1) == '*')
        {
            completionWindow?.Close();
            e.Handled = true;
            return;
        }
        if (typed == '*' &&
            TryGetAlphaPrefix(textArea, out _, out var prefix) &&
            Commands.Any(command => string.Equals(command.Name, prefix,
                StringComparison.OrdinalIgnoreCase)))
        {
            completionWindow?.Close();
            return;
        }

        if (completionWindow == null)
            return;
        if (durationCompletionFromSelection)
            return;

        if (!char.IsLetter(typed))
            completionWindow.CompletionList.RequestInsertion(e);
    }

    private static void OnTextEntered(TextArea textArea, TextCompositionEventArgs e)
    {
        if (e.Text.Length == 0)
            return;

        var ch = e.Text[0];
        switch (ch)
        {
            case '<' when IsCellStart(textArea.Document, textArea.Caret.Offset - 1):
                ShowCompletion(textArea, "", textArea.Caret.Offset);
                break;
            case '*':
            case ',':
            case '(':
            case ')':
                break;
            case '>':
                insightWindow?.Close();
                break;
            default:
                if (char.IsLetter(ch) && completionWindow == null &&
                    TryGetAlphaPrefix(textArea, out var nameStart, out var prefix))
                    ShowCompletion(textArea, prefix, nameStart);
                break;
        }
    }

    private static bool IsCellStart(TextDocument document, int angleOffset)
    {
        for (var i = angleOffset - 1; i >= 0; i--)
        {
            var ch = document.GetCharAt(i);
            if (char.IsWhiteSpace(ch))
                continue;
            return ch is ',' or '}' or '>' or ')';
        }
        return true;
    }

    private static bool TryGetAlphaPrefix(TextArea textArea, out int nameStart, out string prefix)
    {
        var document = textArea.Document;
        var caret = textArea.Caret.Offset;
        nameStart = caret;
        prefix = "";

        while (nameStart > 0 && char.IsLetter(document.GetCharAt(nameStart - 1)))
            nameStart--;
        if (nameStart == caret || nameStart == 0 || document.GetCharAt(nameStart - 1) != '<')
            return false;
        if (!IsCellStart(document, nameStart - 1))
            return false;

        prefix = document.GetText(nameStart, caret - nameStart);
        return true;
    }

    private static void ShowCompletion(TextArea textArea, string prefix, int startOffset)
    {
        completionWindow?.Close();
        var window = new CompletionWindow(textArea)
        {
            StartOffset = startOffset,
            Width = 215,
            MinWidth = 215
        };
        completionWindow = window;
        ApplyDarkStyle(window);

        var data = window.CompletionList.CompletionData;
        foreach (var command in Commands
                     .Where(c => c.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(c => GetCategoryOrder(GetCategory(c.Name))))
            data.Add(new AlphaCompletionData(command));
        if (data.Count == 0)
            foreach (var command in Commands.OrderBy(c => GetCategoryOrder(GetCategory(c.Name))))
                data.Add(new AlphaCompletionData(command));
        if (data.Count > 0)
            window.CompletionList.SelectedItem = data[0];

        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(completionWindow, window))
                completionWindow = null;
        };
        window.Show();
    }

    private static void ApplyDarkStyle(CompletionWindow window)
    {
        window.Background = HintBackground;
        window.BorderBrush = HintBorder;
        window.BorderThickness = new Thickness(1);
        window.Foreground = HintForeground;

        var listBox = window.CompletionList.ListBox;
        if (listBox != null)
        {
            listBox.Background = HintBackground;
            listBox.Foreground = HintForeground;
            listBox.BorderThickness = new Thickness(0);
            listBox.FontFamily = HintMonoFont;
            listBox.Padding = new Thickness(0, 1, 0, 1);
            listBox.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            listBox.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            listBox.Resources[SystemColors.HighlightBrushKey] = HintSelection;
            listBox.Resources[SystemColors.HighlightTextBrushKey] = HintForeground;
            listBox.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = HintSelection;
            listBox.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = HintForeground;
            listBox.PreviewMouseLeftButtonUp += (_, e) =>
            {
                if (listBox.SelectedItem == null)
                    return;
                window.CompletionList.RequestInsertion(e);
                e.Handled = true;
            };
            listBox.PreviewMouseMove += (_, e) =>
            {
                var element = e.OriginalSource as DependencyObject;
                while (element != null && element is not ListBoxItem)
                    element = VisualTreeHelper.GetParent(element);
                if (element is ListBoxItem item)
                    item.IsSelected = true;
            };

            var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            contentFactory.SetBinding(ContentPresenter.ContentProperty, new Binding(nameof(ICompletionData.Content)));
            contentFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            listBox.ItemTemplate = new DataTemplate { VisualTree = contentFactory };

            var itemStyle = new Style(typeof(ListBoxItem));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(4, 2, 6, 2)));
            itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            itemStyle.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
            itemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(3, 0, 0, 0)));
            itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,
                HorizontalAlignment.Stretch));

            var itemRoot = new FrameworkElementFactory(typeof(Border));
            itemRoot.SetBinding(Border.BackgroundProperty,
                new Binding(nameof(Control.Background)) { RelativeSource = RelativeSource.TemplatedParent });
            itemRoot.SetBinding(Border.BorderBrushProperty,
                new Binding(nameof(Control.BorderBrush)) { RelativeSource = RelativeSource.TemplatedParent });
            itemRoot.SetBinding(Border.BorderThicknessProperty,
                new Binding(nameof(Control.BorderThickness)) { RelativeSource = RelativeSource.TemplatedParent });
            itemRoot.SetBinding(Border.PaddingProperty,
                new Binding(nameof(Control.Padding)) { RelativeSource = RelativeSource.TemplatedParent });
            var itemContent = new FrameworkElementFactory(typeof(ContentPresenter));
            itemContent.SetBinding(ContentPresenter.ContentProperty,
                new Binding(nameof(ContentControl.Content)) { RelativeSource = RelativeSource.TemplatedParent });
            itemContent.SetBinding(ContentPresenter.ContentTemplateProperty,
                new Binding(nameof(ContentControl.ContentTemplate)) { RelativeSource = RelativeSource.TemplatedParent });
            itemContent.SetBinding(ContentPresenter.ContentTemplateSelectorProperty,
                new Binding(nameof(ContentControl.ContentTemplateSelector))
                    { RelativeSource = RelativeSource.TemplatedParent });
            itemContent.SetBinding(ContentPresenter.ContentStringFormatProperty,
                new Binding(nameof(ContentControl.ContentStringFormat))
                    { RelativeSource = RelativeSource.TemplatedParent });
            itemContent.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            itemRoot.AppendChild(itemContent);
            itemStyle.Setters.Add(new Setter(Control.TemplateProperty,
                new ControlTemplate(typeof(ListBoxItem)) { VisualTree = itemRoot }));

            var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Control.BackgroundProperty, HintSelection));
            selected.Setters.Add(new Setter(Control.ForegroundProperty, HintForeground));
            selected.Setters.Add(new Setter(Control.BorderBrushProperty, HintAccent));
            itemStyle.Triggers.Add(selected);
            listBox.ItemContainerStyle = itemStyle;
        }

        var tipStyle = new Style(typeof(ToolTip));
        tipStyle.Setters.Add(new Setter(Control.BackgroundProperty, HintBackground));
        tipStyle.Setters.Add(new Setter(Control.ForegroundProperty, HintForeground));
        tipStyle.Setters.Add(new Setter(Control.BorderBrushProperty, HintBorder));
        tipStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        tipStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 8, 10, 8)));
        window.Resources[typeof(ToolTip)] = tipStyle;
    }

    private static FrameworkElement BuildHintPanel(AlphaCommand command)
    {
        var panel = new StackPanel { MaxWidth = 480 };
        var signature = BuildSignatureBlock(GetStateSignatureOverview(command.Name) ?? command.Signature, 0);
        signature.Margin = new Thickness(0, 0, 0, 6);
        panel.Children.Add(signature);
        panel.Children.Add(new TextBlock
        {
            Text = command.Description,
            Foreground = HintForeground,
            TextWrapping = TextWrapping.Wrap
        });
        return panel;
    }

    private static void UpdateInsight(TextArea textArea)
    {
        ShowOrUpdateInsight(textArea);
    }

    private static void ShowOrUpdateInsight(TextArea textArea)
    {
        var document = textArea.Document;
        var caret = textArea.Caret.Offset;
        if (!TryFindOpenAlphaCommand(document, caret, out var openOffset, out var starOffset, out var command))
        {
            insightWindow?.Close();
            return;
        }
        if (command == null)
            return;

        var parameterIndex = GetParameterIndex(document, starOffset + 1, caret);
        var enabled = GetBooleanArgument(document, starOffset + 1, caret);
        if (insightWindow != null &&
            string.Equals(insightCommand?.Name, command.Name, StringComparison.OrdinalIgnoreCase) &&
            insightProvider != null)
        {
            insightProvider.Update(parameterIndex, enabled);
            return;
        }

        insightWindow?.Close();
        insightCommand = command;
        insightProvider = new AlphaOverloadProvider(command, parameterIndex, enabled);
        var window = new OverloadInsightWindow(textArea)
        {
            Provider = insightProvider,
            StartOffset = openOffset + 1,
            EndOffset = document.TextLength,
            Background = HintBackground,
            Foreground = HintForeground,
            BorderBrush = HintBorder,
            BorderThickness = new Thickness(1)
        };
        insightWindow = window;
        window.Closed += (_, _) =>
        {
            if (!ReferenceEquals(insightWindow, window))
                return;
            insightWindow = null;
            insightProvider = null;
            insightCommand = null;
        };
        window.Show();
    }

    private static bool TryFindOpenAlphaCommand(
        TextDocument document,
        int caret,
        out int openOffset,
        out int starOffset,
        out AlphaCommand? command)
    {
        openOffset = -1;
        starOffset = -1;
        command = null;

        for (var i = caret - 1; i >= 0; i--)
        {
            var ch = document.GetCharAt(i);
            if (ch is '\r' or '\n' or ';' or '；')
                return false;
            if (ch == '>')
                return false;
            if (ch == '<')
            {
                openOffset = i;
                break;
            }
        }
        if (openOffset < 0)
            return false;

        for (var i = openOffset + 1; i < caret; i++)
        {
            if (document.GetCharAt(i) == '*')
            {
                starOffset = i;
                break;
            }
        }
        if (starOffset < 0)
            return false;

        var name = document.GetText(openOffset + 1, starOffset - openOffset - 1);
        if (string.IsNullOrWhiteSpace(name) || name.Any(c => !char.IsLetter(c)))
            return false;

        command = Array.Find(Commands, c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        return command != null;
    }

    private static int GetParameterIndex(TextDocument document, int startOffset, int caretOffset)
    {
        var index = 1;
        var depth = 0;
        var topLevelDepth = 0;
        for (var i = startOffset; i < caretOffset && i < document.TextLength; i++)
        {
            if (!char.IsWhiteSpace(document.GetCharAt(i)))
            {
                topLevelDepth = document.GetCharAt(i) == '(' ? 1 : 0;
                break;
            }
        }
        for (var i = startOffset; i < caretOffset && i < document.TextLength; i++)
        {
            var ch = document.GetCharAt(i);
            if (ch == '(')
                depth++;
            else if (ch == ')' && depth > 0)
                depth--;
            else if (ch == ',' && depth == topLevelDepth)
                index++;
        }
        return Math.Max(1, index);
    }

    private static bool? GetBooleanArgument(TextDocument document, int startOffset, int caretOffset)
    {
        if (caretOffset <= startOffset)
            return null;
        var value = document.GetText(startOffset, caretOffset - startOffset).TrimStart();
        if (value.StartsWith("(", StringComparison.Ordinal))
            value = value.Substring(1).TrimStart();
        var end = value.IndexOfAny(new[] { ',', ')' });
        if (end >= 0)
            value = value.Substring(0, end);
        return bool.TryParse(value.Trim(), out var enabled) ? enabled : null;
    }

    private static bool TryHandleAlphaArgumentTab(TextArea textArea)
    {
        var document = textArea.Document;
        var caret = textArea.Caret.Offset;
        if (!TryFindOpenAlphaCommand(document, caret, out _, out var starOffset, out var command) ||
            command == null || !TryGetArgumentBounds(document, starOffset, caret,
                out var argumentStart, out var argumentEnd, out var parameterIndex))
            return false;

        var enabled = GetBooleanArgument(document, starOffset + 1, caret);
        var current = document.GetText(argumentStart, argumentEnd - argumentStart).Trim();
        if (current.Length == 0)
        {
            var defaultValue = GetDefaultArgument(command.Name, parameterIndex, enabled);
            if (defaultValue == null)
                return false;
            document.Replace(argumentStart, argumentEnd - argumentStart, defaultValue);
            textArea.Caret.Offset = argumentStart + defaultValue.Length;
            ShowOrUpdateInsight(textArea);
            return true;
        }

        if (argumentEnd < document.TextLength && document.GetCharAt(argumentEnd) == ',')
        {
            textArea.Caret.Offset = argumentEnd + 1;
            ShowOrUpdateInsight(textArea);
            return true;
        }

        if (parameterIndex >= GetMaximumArgumentCount(command.Name, enabled))
            return false;
        document.Insert(argumentEnd, ",");
        textArea.Caret.Offset = argumentEnd + 1;
        ShowOrUpdateInsight(textArea);
        return true;
    }

    private static bool TryGetArgumentBounds(
        TextDocument document,
        int starOffset,
        int caret,
        out int argumentStart,
        out int argumentEnd,
        out int parameterIndex)
    {
        argumentStart = argumentEnd = caret;
        parameterIndex = 1;
        var contentStart = starOffset + 1;
        while (contentStart < document.TextLength && char.IsWhiteSpace(document.GetCharAt(contentStart)))
            contentStart++;
        if (contentStart < document.TextLength && document.GetCharAt(contentStart) == '(')
            contentStart++;
        if (caret < contentStart)
            return false;

        var depth = 0;
        argumentStart = contentStart;
        for (var i = contentStart; i < caret && i < document.TextLength; i++)
        {
            var ch = document.GetCharAt(i);
            if (ch == '(')
                depth++;
            else if (ch == ')' && depth > 0)
                depth--;
            else if (ch == ',' && depth == 0)
            {
                parameterIndex++;
                argumentStart = i + 1;
            }
        }

        argumentEnd = argumentStart;
        depth = 0;
        while (argumentEnd < document.TextLength)
        {
            var ch = document.GetCharAt(argumentEnd);
            if (ch == '(')
                depth++;
            else if ((ch == ')' && depth == 0) || ch == '>' || ch is '\r' or '\n')
                break;
            else if (ch == ')' && depth > 0)
                depth--;
            else if (ch == ',' && depth == 0)
                break;
            argumentEnd++;
        }
        return true;
    }

    private static int GetMaximumArgumentCount(string name, bool? enabled)
    {
        if (AlphaOverloadProvider.IsScreenEffectName(name))
            return enabled == false ? 2 : name == "SHAKE" ? 5 : RequiredEffectParameterCount(name) + 1;
        if (name == "AUDIO")
            return enabled == false ? 1 : 2;
        if (name == "PVOVERLAY")
            return enabled == false ? 2 : 3;
        return name is "TEXT" or "JLINE" or "COMBODISPLAY" or
            "SHOWJUDGELINE" or "SHOWJUDGEAREA" or "SHOWJUDGETEXT" or
            "SHOWJUDGEINFO" or "SHOWCOMBOINFO" or "OUTERBRIGHTNESS" or
            "INNERBRIGHTNESS" ? 2 : 1;
    }

    private static string? GetDefaultArgument(string name, int parameterIndex, bool? enabled)
    {
        if (AlphaOverloadProvider.IsScreenEffectName(name))
        {
            if (parameterIndex == 1)
                return BooleanTrue;
            if (enabled == false)
                return parameterIndex == 2 ? "8:1" : null;
            if (name == "SHAKE")
                return parameterIndex switch
                {
                    2 => "0.5",
                    3 => "12",
                    4 => "30",
                    5 => "8:1",
                    _ => null
                };
            if (parameterIndex == RequiredEffectParameterCount(name) + 1)
                return "8:1";
            return (name, parameterIndex) switch
            {
                ("TINT", 2) => "FF6699",
                ("TINT", 3) => "0.5",
                ("MOVE", 2) => "0.1",
                ("MOVE", 3) => "0.1",
                ("ZOOM", 2) => "1.5",
                ("HUE", 2) => "45",
                ("ROTATE", 2) => "10",
                (_, 2) => "1",
                _ => null
            };
        }

        return (name, parameterIndex) switch
        {
            ("JLINE", 1) => "FF6699",
            ("JLINE", 2) => "8:1",
            ("TEXT", 1) => Localized("字幕", "caption", "字幕"),
            ("TEXT", 2) => "2",
            ("COMBODISPLAY", 1) => "Combo",
            ("COMBODISPLAY", 2) => "8:1",
            ("SHOWJUDGELINE" or "SHOWJUDGEAREA" or "SHOWJUDGETEXT" or
                "SHOWJUDGEINFO" or "SHOWCOMBOINFO", 1) => BooleanTrue,
            ("SHOWJUDGELINE" or "SHOWJUDGEAREA" or "SHOWJUDGETEXT" or
                "SHOWJUDGEINFO" or "SHOWCOMBOINFO", 2) => "8:1",
            ("OUTERBRIGHTNESS" or "INNERBRIGHTNESS", 1) => "0.5",
            ("OUTERBRIGHTNESS" or "INNERBRIGHTNESS", 2) => "8:1",
            ("AUDIO" or "PVOVERLAY", 1) => BooleanTrue,
            ("AUDIO", 2) => "media/audio.ogg",
            ("PVOVERLAY", 2) => enabled == false ? "8:1" : "media/overlay.mp4",
            ("PVOVERLAY", 3) => "8:1",
            ("COLOR", 1) => "FF6699",
            ("ALPHA", 1) => "1",
            ("SPAWN", 1) => "1.225",
            ("BOUNCE", 1) => "8:1",
            ("SV" or "HS" or "SIZE", 1) => "1",
            _ => null
        };
    }

    private sealed class AlphaCompletionData : ICompletionData
    {
        private readonly AlphaCommand command;

        public AlphaCompletionData(AlphaCommand command) => this.command = command;

        public ImageSource? Image => null;
        public string Text => command.Name;
        public object Content => BuildCompletionItem(command);
        public object Description => BuildHintPanel(command);
        public double Priority => 0;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        {
            var insertionOffset = completionSegment.Offset;
            var parenthesized = RequiresParenthesizedArguments(command.Name);
            var replacement = parenthesized
                ? command.Name + "*()>"
                : command.Name + "*>";
            textArea.Document.Replace(completionSegment, replacement);
            textArea.Caret.Offset = insertionOffset + command.Name.Length +
                                   (parenthesized ? 2 : 1);
            ShowOrUpdateInsight(textArea);
        }
    }

    private static bool RequiresParenthesizedArguments(string name)
    {
        return name is "TEXT" or "JLINE" or "COMBODISPLAY" ||
               name is "AUDIO" or "PVOVERLAY" ||
               name.StartsWith("SHOW", StringComparison.Ordinal) ||
               name is "OUTERBRIGHTNESS" or "INNERBRIGHTNESS" ||
               AlphaOverloadProvider.IsScreenEffectName(name);
    }

    private static FrameworkElement BuildCompletionItem(AlphaCommand command)
    {
        var category = GetCategory(command.Name);
        var (label, color) = category switch
        {
            "Display" => (Localized("显示", "Display", "表示"), Color.FromRgb(0x24, 0x64, 0x7A)),
            "Filter" => (Localized("滤镜", "Filter", "フィルター"), Color.FromRgb(0x69, 0x3A, 0x78)),
            "Media" => (Localized("媒体", "Media", "メディア"), Color.FromRgb(0x26, 0x68, 0x4A)),
            _ => (Localized("音符", "Note", "ノーツ"), Color.FromRgb(0x55, 0x53, 0x2D))
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new Border
        {
            Background = Frozen(new SolidColorBrush(color)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(5, 1, 5, 1),
            Margin = new Thickness(0, 0, 7, 0),
            Child = new TextBlock { Text = label, Foreground = Brushes.White, FontSize = 10 }
        });
        panel.Children.Add(new TextBlock
        {
            Text = command.Name,
            Foreground = HintForeground,
            FontFamily = HintMonoFont,
            VerticalAlignment = VerticalAlignment.Center
        });
        return new Border
        {
            Background = Frozen(new SolidColorBrush(Color.FromArgb(52, color.R, color.G, color.B))),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(3, 1, 6, 1),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = panel
        };
    }

    private static string GetCategory(string name)
    {
        if (name is "JLINE" or "TEXT" or "SHOWJUDGELINE" or "SHOWJUDGEAREA" or "SHOWJUDGETEXT" or
            "SHOWJUDGEINFO" or "SHOWCOMBOINFO" or "COMBODISPLAY" or
            "OUTERBRIGHTNESS" or "INNERBRIGHTNESS")
            return "Display";
        if (name is "AUDIO" or "PVOVERLAY")
            return "Media";
        return AlphaOverloadProvider.IsScreenEffectName(name) ? "Filter" : "Note";
    }

    private static int GetCategoryOrder(string category) => category switch
    {
        "Note" => 0,
        "Display" => 1,
        "Filter" => 2,
        "Media" => 3,
        _ => 4
    };

    private sealed class DurationCompletionData : ICompletionData
    {
        private readonly string value;

        public DurationCompletionData(string value) => this.value = value;

        public ImageSource? Image => null;
        public string Text => value;
        public object Content => value;
        public object? Description => null;
        public double Priority => 0;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        {
            if (!textArea.Selection.IsEmpty)
            {
                var insertionOffset = Math.Clamp(
                    completionSegment.Offset, 0, textArea.Document.TextLength);
                textArea.Document.Insert(insertionOffset, value);
                textArea.Caret.Offset = insertionOffset + value.Length;
                textArea.ClearSelection();
                return;
            }
            textArea.Document.Replace(completionSegment, value);
        }
    }

    private sealed class AlphaOverloadProvider : IOverloadProvider
    {
        private readonly AlphaCommand command;
        private int parameterIndex;
        private bool? enabled;

        public AlphaOverloadProvider(AlphaCommand command, int parameterIndex, bool? enabled)
        {
            this.command = command;
            this.parameterIndex = parameterIndex;
            this.enabled = enabled;
        }

        public void Update(int newParameterIndex, bool? newEnabled)
        {
            if (parameterIndex == newParameterIndex && enabled == newEnabled)
                return;
            parameterIndex = newParameterIndex;
            enabled = newEnabled;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentHeader)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentContent)));
        }

        public int SelectedIndex { get => 0; set { } }
        public int Count => 1;
        public string? CurrentIndexText => null;
        public object CurrentHeader => BuildHeader();
        public object CurrentContent => new TextBlock
        {
            Text = string.Join("\n", new[]
            {
                GetCurrentParameterHint(),
                command.Description
            }.Where(value => !string.IsNullOrEmpty(value))),
            Foreground = HintSecondary,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 440,
            Margin = new Thickness(6, 2, 6, 6)
        };

        public event PropertyChangedEventHandler? PropertyChanged;

        private StackPanel BuildHeader()
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(BuildSignatureBlock(
                GetStateSignature(command.Name, enabled, parameterIndex) ?? command.Signature, parameterIndex));
            return panel;
        }

        private string GetCurrentParameterHint()
        {
            if (IsScreenEffect(command.Name))
            {
                var maximumHintParameter = command.Name == "SHAKE"
                    ? 4
                    : RequiredEffectParameterCount(command.Name);
                if (parameterIndex == 1 || enabled == false ||
                    parameterIndex > maximumHintParameter)
                    return string.Empty;
                return command.Name switch
                {
                    "GAUSSIAN" => Localized("当前参数：模糊强度。数值越大，采样半径越大。", "Current parameter: blur strength. Larger values increase the sample radius.", "現在の引数：ぼかし強度。値が大きいほどサンプル半径が広がります。"),
                    "NEON" => Localized("当前参数：RGB 分离与边缘发光强度。", "Current parameter: RGB split and edge-glow strength.", "現在の引数：RGB 分離と輪郭発光の強度。"),
                    "TRAIL" => Localized("当前参数：历史画面混合强度，越大残影越明显。", "Current parameter: previous-frame blend strength. Larger values create stronger trails.", "現在の引数：過去フレームの合成強度。値が大きいほど残像が強くなります。"),
                    "FADE" => Localized("当前参数：压黑强度，0 无变化，1 为全黑。", "Current parameter: fade-to-black strength; 0 is unchanged and 1 is black.", "現在の引数：黒へのフェード強度。0 は変化なし、1 は完全な黒です。"),
                    "FLASH" => Localized("当前参数：闪白强度，0 无变化，1 为全白。", "Current parameter: flash-to-white strength; 0 is unchanged and 1 is white.", "現在の引数：白へのフラッシュ強度。0 は変化なし、1 は完全な白です。"),
                    "BRIGHTNESS" => Localized("当前参数：额外亮度强度。", "Current parameter: additional brightness.", "現在の引数：追加の明るさ。"),
                    "SATURATION" => Localized("当前参数：去饱和强度，0 保持原色，1 接近黑白。", "Current parameter: desaturation; 0 keeps the original color and 1 approaches monochrome.", "現在の引数：彩度低下。0 は元の色、1 はほぼ白黒です。"),
                    "CONTRAST" => Localized("当前参数：对比度增强强度。", "Current parameter: contrast enhancement.", "現在の引数：コントラスト強調。"),
                    "RAINBOW" => Localized("当前参数：动态彩虹混合强度。", "Current parameter: animated rainbow blend strength.", "現在の引数：動く虹色の合成強度。"),
                    "VIGNETTE" => Localized("当前参数：圆形可视区域收缩强度，0 不收缩，1 收到中心。", "Current parameter: circular visible-area shrink; 0 is unchanged and 1 reaches the center.", "現在の引数：円形表示範囲の縮小強度。0 は変化なし、1 は中心まで縮小します。"),
                    "ZOOM" => Localized("当前参数：缩放倍率参数；1 约放大 12%，数值越大放大越多。", "Current parameter: zoom scale; 1 is about 12% and larger values zoom further.", "現在の引数：ズーム倍率。1 は約 12% 拡大し、値が大きいほど拡大します。"),
                    "GLITCH" => Localized("当前参数：横向分段错位强度。", "Current parameter: horizontal segment displacement.", "現在の引数：横方向の分割ずれ強度。"),
                    "TVNOISE" => Localized("当前参数：扫描线、横向噪声和画面错位强度。", "Current parameter: scan-line, horizontal-noise, and frame-displacement strength.", "現在の引数：走査線、横ノイズ、画面ずれの強度。"),
                    "HUE" => Localized("当前参数：色相旋转角度，单位为度。", "Current parameter: hue rotation in degrees.", "現在の引数：色相回転角度（度）。"),
                    "TINT" when parameterIndex == 2 => Localized("当前参数：目标颜色，格式为 RRGGBB。", "Current parameter: target color in RRGGBB format.", "現在の引数：対象色（RRGGBB 形式）。"),
                    "TINT" => Localized("当前参数：颜色混合强度，0 保持原画面，1 完全变为目标颜色。", "Current parameter: color blend; 0 keeps the frame and 1 fully applies the target color.", "現在の引数：色の合成強度。0 は元の画面、1 は対象色になります。"),
                    "MOVE" when parameterIndex == 2 => Localized("当前参数：目标 X。相对画面中心，正值向右，负值向左。", "Current parameter: target X relative to center; positive moves right and negative moves left.", "現在の引数：中心基準の X。正は右、負は左へ移動します。"),
                    "MOVE" => Localized("当前参数：目标 Y。相对画面中心，正值向上，负值向下。", "Current parameter: target Y relative to center; positive moves up and negative moves down.", "現在の引数：中心基準の Y。正は上、負は下へ移動します。"),
                    "ROTATE" => Localized("当前参数：绕画面中心旋转的角度，单位为度。", "Current parameter: rotation around the frame center in degrees.", "現在の引数：画面中央を基準にした回転角度（度）。"),
                    "SHAKE" when parameterIndex == 2 => Localized("当前参数：震动位移强度；1 约为画面尺寸的 5%。", "Current parameter: shake displacement; 1 is about 5% of the frame size.", "現在の引数：振動の移動強度。1 は画面サイズの約 5% です。"),
                    "SHAKE" when parameterIndex == 3 => Localized("当前参数：震动频率，单位 Hz。", "Current parameter: shake frequency in Hz.", "現在の引数：振動周波数（Hz）。"),
                    "SHAKE" => Localized("当前参数：震动方向角度，单位为度；0 度水平，90 度垂直。", "Current parameter: shake direction in degrees; 0 is horizontal and 90 is vertical.", "現在の引数：振動方向の角度（度）。0度は水平、90度は垂直です。"),
                    _ => string.Empty
                };
            }

            return (command.Name, parameterIndex) switch
            {
                ("JLINE", 1) => Localized("当前参数：判定线目标颜色 RRGGBB；NULL 恢复皮肤颜色。", "Current parameter: judge-line target color in RRGGBB; NULL restores the skin color.", "現在の引数：判定ラインの対象色（RRGGBB）。NULL でスキン色に戻します。"),
                ("JLINE", 2) => Localized("当前参数：颜色过渡时间，可写秒数或 8:1。", "Current parameter: color transition duration in seconds or beat length such as 8:1.", "現在の引数：色の切り替え時間。秒数または 8:1 などの拍長を指定できます。"),
                ("TEXT", 1) => Localized("当前参数：字幕内容。", "Current parameter: caption text.", "現在の引数：字幕内容。"),
                ("TEXT", 2) => Localized("当前参数：显示时长；省略时保持到下一条 TEXT。", "Current parameter: display duration; omit it to keep the caption until the next TEXT.", "現在の引数：表示時間。省略すると次の TEXT まで表示します。"),
                ("SHOWJUDGELINE" or "SHOWJUDGEAREA" or "SHOWJUDGETEXT" or
                    "SHOWJUDGEINFO" or "SHOWCOMBOINFO", 1) => Localized("当前参数：True 显示，False 隐藏。", "Current parameter: True shows and False hides.", "現在の引数：True で表示、False で非表示。"),
                ("SHOWJUDGELINE" or "SHOWJUDGEAREA" or "SHOWJUDGETEXT" or
                    "SHOWJUDGEINFO" or "SHOWCOMBOINFO", 2) => Localized("当前参数：显示状态的过渡时间。", "Current parameter: visibility transition duration.", "現在の引数：表示状態の切り替え時間。"),
                ("OUTERBRIGHTNESS" or "INNERBRIGHTNESS", 1) => Localized("当前参数：目标亮度，0 为全暗，1 为全亮。", "Current parameter: target brightness; 0 is dark and 1 is full brightness.", "現在の引数：目標の明るさ。0 は暗く、1 は最大です。"),
                ("OUTERBRIGHTNESS" or "INNERBRIGHTNESS", 2) => Localized("当前参数：亮度过渡时间。", "Current parameter: brightness transition duration.", "現在の引数：明るさの切り替え時間。"),
                ("COMBODISPLAY", 1) => Localized("当前参数：中间显示模式，例如 Combo、DxScore、Achievement、None。", "Current parameter: center display mode, such as Combo, DxScore, Achievement, or None.", "現在の引数：中央表示モード（Combo、DxScore、Achievement、None など）。"),
                ("COMBODISPLAY", 2) => Localized("当前参数：模式切换过渡时间。", "Current parameter: mode transition duration.", "現在の引数：モード切り替え時間。"),
                _ => string.Empty
            };
        }

        internal static bool IsScreenEffectName(string name) => name is
            "GAUSSIAN" or "NEON" or "TRAIL" or "FADE" or "FLASH" or
            "BRIGHTNESS" or "SATURATION" or "CONTRAST" or "RAINBOW" or
            "VIGNETTE" or "ZOOM" or "GLITCH" or "TVNOISE" or "HUE" or
            "TINT" or "MOVE" or "ROTATE" or "SHAKE";

        private static bool IsScreenEffect(string name) => IsScreenEffectName(name);

    }

    private sealed record SignatureParameter(string Text, bool Optional);

    private static TextBlock BuildSignatureBlock(string signature, int activeParameter)
    {
        var block = new TextBlock
        {
            Foreground = HintAccent,
            FontFamily = HintMonoFont,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 440,
            Margin = new Thickness(6, 6, 6, 2)
        };

        var star = signature.IndexOf('*');
        var close = star >= 0 ? signature.IndexOf('>', star) : -1;
        if (star < 0 || close <= star)
        {
            block.Inlines.Add(signature);
            return block;
        }

        block.Inlines.Add(signature[..(star + 1)]);
        var parameterText = signature.Substring(star + 1, close - star - 1);
        var wrapped = parameterText.Length >= 2 && parameterText[0] == '(' && parameterText[^1] == ')';
        var parameters = wrapped ? parameterText[1..^1] : parameterText;
        var parts = SplitSignatureParameters(parameters);
        if (wrapped)
            block.Inlines.Add("(");
        for (var index = 0; index < parts.Count; index++)
        {
            var part = parts[index];
            if (index > 0)
            {
                var comma = new Run(",");
                if (part.Optional)
                {
                    comma.Foreground = HintOptional;
                    comma.FontStyle = FontStyles.Italic;
                }
                block.Inlines.Add(comma);
            }

            var run = new Run(part.Text);
            if (part.Optional)
            {
                run.Foreground = HintOptional;
                run.FontStyle = FontStyles.Italic;
            }
            if (index + 1 == activeParameter)
            {
                run.FontWeight = FontWeights.Bold;
                run.Background = HintParamActive;
                if (!part.Optional)
                    run.Foreground = Brushes.White;
            }
            block.Inlines.Add(run);
        }
        if (wrapped)
            block.Inlines.Add(")");
        block.Inlines.Add(signature[close..]);
        return block;
    }

    private static List<SignatureParameter> SplitSignatureParameters(string text)
    {
        var result = new List<SignatureParameter>();
        var builder = new System.Text.StringBuilder();
        var roundDepth = 0;
        var optional = false;
        var currentOptional = false;
        foreach (var ch in text)
        {
            if (ch == '[')
            {
                optional = true;
                if (builder.Length == 0)
                    currentOptional = true;
                continue;
            }
            if (ch == ']')
            {
                optional = false;
                continue;
            }
            if (ch == '(')
                roundDepth++;
            else if (ch == ')' && roundDepth > 0)
                roundDepth--;
            if (ch == ',' && roundDepth == 0)
            {
                result.Add(new SignatureParameter(builder.ToString(), currentOptional));
                builder.Clear();
                currentOptional = optional;
                continue;
            }
            builder.Append(ch);
        }
        result.Add(new SignatureParameter(builder.ToString(), currentOptional));
        return result;
    }

    private static Brush WithOpacity(Brush source, double opacity)
    {
        var brush = source.CloneCurrentValue();
        brush.Opacity = opacity;
        brush.Freeze();
        return brush;
    }
}
