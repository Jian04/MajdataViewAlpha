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
using MajdataCore;
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

    // Which commands exist, in which order, comes from the grammar the parser and
    // the syntax check read; this file only carries the wording. A command added to
    // the grammar therefore shows up here even before it is described, instead of
    // being missing from the popup while playing fine.
    private static AlphaCommand[] BuildCommands()
    {
        var described = new Dictionary<string, AlphaCommand>(StringComparer.OrdinalIgnoreCase);
        foreach (var command in BuildLocalizedCommands())
            described[command.Name] = command;

        var commands = new List<AlphaCommand>();
        foreach (var descriptor in AlphaCommandGrammar.Commands)
            commands.Add(
                described.TryGetValue(descriptor.name, out var localized)
                    ? localized
                    : new AlphaCommand(
                        descriptor.name, BuildSignature(descriptor), string.Empty));
        return commands.ToArray();
    }

    private static string BuildSignature(AlphaCommandDescriptor descriptor)
    {
        if (descriptor.forms.Length == 0)
            return $"<{descriptor.name}*{Localized("值", "value", "値")}>";
        var slots = descriptor.forms[0].slots;
        var parts = new List<string>();
        for (var index = 0; index < slots.Length; index++)
            parts.Add(slots[index].optional
                ? $"[{index + 1}]"
                : (index + 1).ToString(CultureInfo.InvariantCulture));
        return $"<{descriptor.name}*({string.Join(",", parts)})>";
    }

    private static AlphaCommandDescriptor? Describe(string name) =>
        AlphaCommandGrammar.TryFind(name, out var descriptor) ? descriptor : null;

    private static AlphaCommand[] BuildLocalizedCommands()
    {
        if (CurrentLanguage == "en")
            return new AlphaCommand[]
            {
                new("SV", "<SV*multiplier> / <SV*tap=multiplier,touch=multiplier,slide=multiplier>", "True scroll-speed multiplier. Global SV affects ring notes, Touch, and TouchHold; slide= shapes Slide-path motion within the authored duration. Positive net motion is normalized to finish on time; non-positive motion is cut off at the authored end."),
                new("HS", "<HS*multiplier> / <HS*tap=multiplier,slide=appearance multiplier>", "Traditional fall-speed multiplier for note heads. HS*slide multiplies Slide appearance speed (for example, 999 is nearly instant) without changing path duration; global HS does not affect Slides. Path motion uses SV*slide."),
                new("SPAWN", "<SPAWN*radius> / <SPAWN*tap=radius,hold=radius>", "Ring-note visual spawn radius from -4.8 to 4.8; the default is 1.225. 0 is center and -4.8 is the opposite judge line. Supports NULL reset."),
                new("SPAWNMODE", "<SPAWNMODE*Rewind> / <SPAWNMODE*tap=Once,hold=Rewind>", "Rewind hides or shrinks a ring note again when SV crosses back before SPAWN. Once keeps it active after its first SPAWN crossing. Rewind is the default; NULL resets it."),
                new("DESTROY", "<DESTROY*radius> / <DESTROY*tap=radius,hold=radius> / <DESTROY*NULL>", "Changes the visual endpoint for Tap, Star, Each, and Hold without changing judgement timing. The authored radius is preserved; NULL restores 4.8."),
                new("BOUNCE", "<BOUNCE*duration> / <BOUNCE*tap=8:1,hold=4:1> / <BOUNCE*NULL>", "Makes Tap, Star, Each, and Hold travel from DESTROY to SPAWN and back. Rewind hides when SV retreats before takeoff; Once stays active after the first crossing. SV=0 pauses motion."),
                new("FAKE", "<FAKE*TRUE> / <FAKE*FALSE> / <FAKE*tap=TRUE,slide=TRUE>", "Makes following notes visual-only in the current note stream: no count, judgement, effects, text, or hit sound."),
                new("COLOR", "<COLOR*RRGGBB>", "Colors notes. star controls star-shaped notes and Slide heads; slidestar controls moving guide stars; slide controls paths."),
                new("COLORV", "<COLORV*RRGGBB> / <COLORV*slidestar=FF0000>", "Instantly recolors loaded notes. Typed targets include regular note types plus star, slidestar, and slide."),
                new("SIZE", "<SIZE*scale> / <SIZE*(scaleX,scaleY)> / <SIZE*type=(scaleX,scaleY)>", "Sets uniform or local X/Y scale. Typed targets include tap, hold, touch, star, slidestar, and slide; global scale excludes paths."),
                new("SIZEV", "<SIZEV*scale> / <SIZEV*(scaleX,scaleY)> / <SIZEV*type=(scaleX,scaleY)>", "Instantly resizes loaded notes with uniform or local X/Y scale and typed targets."),
                new("ALPHA", "<ALPHA*opacity>", "Opacity from 0 to 1. star controls star-shaped notes and Slide heads; slidestar controls moving guide stars; slide controls paths."),
                new("ALPHAV", "<ALPHAV*opacity> / <ALPHAV*slidestar=0.5>", "Instantly changes loaded-note opacity with separate star, slidestar, and slide targets."),
                new("JLINE", "<JLINE*RRGGBB> / <JLINE*(RRGGBB[,duration])>", "Transitions the judge-line color during playback. NULL restores the skin color."),
                new("TEXT", "<TEXT*(\"content\"[,duration][,x][,y][,size][,font][,index][,style][,transition])>", "Caption text must be quoted. Optional positional slots may be skipped with consecutive commas. index is any non-negative integer; Fade uses transition as fade-in time, while Typewriter uses it as typing time."),
                new("SHOWJUDGELINE", $"<SHOWJUDGELINE*({BooleanChoices}[,duration])>", "Shows or hides the judge line, optionally with a transition."),
                new("SHOWJUDGEAREA", $"<SHOWJUDGEAREA*({BooleanChoices}[,duration])>", "Shows or hides judgment areas, optionally with a transition."),
                new("SHOWJUDGETEXT", $"<SHOWJUDGETEXT*({BooleanChoices}[,duration])>", "Shows or hides judgment text such as Critical Perfect."),
                new("SHOWJUDGEINFO", $"<SHOWJUDGEINFO*({BooleanChoices}[,duration])>", "Shows or hides the left-side judgment statistics."),
                new("SHOWCOMBOINFO", $"<SHOWCOMBOINFO*({BooleanChoices}[,duration])>", "Shows or hides the right-side combo and achievement display."),
                new("COMBODISPLAY", "<COMBODISPLAY*(mode[,duration])>", "Changes the center display: NONE, COMBO, SCORE, ACC, DXACC, DXSCORE, and others."),
                new("OUTERBRIGHTNESS", "<OUTERBRIGHTNESS*(darkness[,duration])>", "Outer-ring darkness: 0 is fully bright and 1 is darkest."),
                new("INNERBRIGHTNESS", "<INNERBRIGHTNESS*(darkness[,duration])>", "Inner-area darkness: 0 is fully bright and 1 is darkest."),
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
                new("ZOOM", $"<ZOOM*({BooleanTrue},scale[,duration])>", "Scales the frame directly: 0.6 = 60%, 1 = unchanged, and 1.5 = 150%. Valid range: 0.1 to 8."),
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
                new("SV", "<SV*倍率> / <SV*tap=倍率,touch=倍率,slide=倍率>", "実スクロール速度の倍率。全体 SV はリングノーツと Touch / TouchHold に適用され、slide= は譜面で指定された時間内の軌道を制御します。正の総移動量は終了時刻に合わせて正規化し、0 以下は指定終了時刻で打ち切ります。"),
                new("HS", "<HS*倍率> / <HS*tap=倍率,slide=表示倍率>", "通常ノーツ頭の落下倍率です。HS*slide は Slide の表示速度を倍率化し（例: 999 はほぼ瞬時）、軌道時間は変えません。全体 HS は Slide に影響せず、軌道速度は SV*slide です。"),
                new("SPAWN", "<SPAWN*半径> / <SPAWN*tap=半径,hold=半径>", "リングノーツの出現半径（-4.8～4.8、既定値 1.225）。0 は中央、-4.8 は反対側の判定ラインです。NULL でリセットします。"),
                new("SPAWNMODE", "<SPAWNMODE*Rewind> / <SPAWNMODE*tap=Once,hold=Rewind>", "Rewind は SV が SPAWN より前へ戻ると再び縮小・非表示にします。Once は最初に SPAWN を越えた後も表示を維持します。既定値は Rewind、NULL でリセットします。"),
                new("DESTROY", "<DESTROY*半径> / <DESTROY*tap=半径,hold=半径> / <DESTROY*NULL>", "Tap、Star、Each、Hold の表示上の終点だけを変更し、判定時刻は変えません。指定した半径をそのまま使用し、NULL で 4.8 に戻します。"),
                new("BOUNCE", "<BOUNCE*時間> / <BOUNCE*tap=8:1,hold=4:1> / <BOUNCE*NULL>", "Tap、Star、Each、Hold を DESTROY から SPAWN へ往復させます。Rewind は SV が起点より前へ戻ると非表示、Once は最初の通過後も表示を維持します。SV=0 で停止します。"),
                new("FAKE", "<FAKE*TRUE> / <FAKE*FALSE> / <FAKE*tap=TRUE,slide=TRUE>", "同じノーツストリームの後続ノーツを表示専用にします。物量、判定、演出、判定文字、効果音は発生しません。"),
                new("COLOR", "<COLOR*RRGGBB>", "star は星型ノーツと Slide ヘッド、slidestar は移動星、slide は軌道を着色します。"),
                new("COLORV", "<COLORV*RRGGBB> / <COLORV*slidestar=FF0000>", "読み込み済みノーツを即時着色します。通常の種別に加え、star、slidestar、slide を個別指定できます。"),
                new("SIZE", "<SIZE*倍率> / <SIZE*(X倍率,Y倍率)> / <SIZE*種別=(X倍率,Y倍率)>", "等倍またはローカル X/Y を指定できます。種別には tap、hold、touch、star、slidestar、slide などを指定できます。"),
                new("SIZEV", "<SIZEV*倍率> / <SIZEV*(X倍率,Y倍率)> / <SIZEV*種別=(X倍率,Y倍率)>", "読み込み済みノーツを等倍またはローカル X/Y で即時拡縮します。"),
                new("ALPHA", "<ALPHA*透明度>", "透明度は 0～1。star は星型ノーツとヘッド、slidestar は移動星、slide は軌道に適用されます。"),
                new("ALPHAV", "<ALPHAV*透明度> / <ALPHAV*slidestar=0.5>", "star、slidestar、slide の透明度を個別に即時変更します。"),
                new("JLINE", "<JLINE*RRGGBB> / <JLINE*(RRGGBB[,時間])>", "再生中の判定ライン色を切り替えます。NULL でスキン色に戻します。"),
                new("TEXT", "<TEXT*(\"内容\"[,時間][,x][,y][,サイズ][,フォント][,index][,style][,transition])>", "字幕は二重引用符で囲みます。省略する位置引数は連続したカンマで飛ばせます。index は 0 以上の整数、Fade の transition はフェード時間、Typewriter では文字送り時間です。"),
                new("SHOWJUDGELINE", $"<SHOWJUDGELINE*({BooleanChoices}[,時間])>", "判定ラインを表示または非表示にします。"),
                new("SHOWJUDGEAREA", $"<SHOWJUDGEAREA*({BooleanChoices}[,時間])>", "判定エリアを表示または非表示にします。"),
                new("SHOWJUDGETEXT", $"<SHOWJUDGETEXT*({BooleanChoices}[,時間])>", "Critical Perfect などの判定文字を表示または非表示にします。"),
                new("SHOWJUDGEINFO", $"<SHOWJUDGEINFO*({BooleanChoices}[,時間])>", "左側の判定集計を表示または非表示にします。"),
                new("SHOWCOMBOINFO", $"<SHOWCOMBOINFO*({BooleanChoices}[,時間])>", "右側のコンボと達成率を表示または非表示にします。"),
                new("COMBODISPLAY", "<COMBODISPLAY*(モード[,時間])>", "中央表示を切り替えます：NONE、COMBO、SCORE、ACC、DXACC、DXSCORE など。"),
                new("OUTERBRIGHTNESS", "<OUTERBRIGHTNESS*(暗さ[,時間])>", "外周の遮暗量。0 は全亮、1 は最も暗い状態です。"),
                new("INNERBRIGHTNESS", "<INNERBRIGHTNESS*(暗さ[,時間])>", "内側の遮暗量。0 は全亮、1 は最も暗い状態です。"),
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
                new("ZOOM", $"<ZOOM*({BooleanTrue},倍率[,時間])>", "画面倍率を直接指定します。0.6 は 60%、1 は等倍、1.5 は 150%。範囲は 0.1～8 です。"),
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
        new("SV", "<SV*倍率> / <SV*tap=倍率,touch=倍率,slide=倍率>", "真实 SV 倍率。全局 SV 影响环形音符及 Touch/TouchHold；slide= 在谱面标记时长内控制 Slide 轨迹。净积分为正时归一化到准时结束，净积分不为正时到标记终点直接截断。"),
        new("HS", "<HS*倍率> / <HS*tap=倍率,slide=显现倍率>", "普通音符头的传统下落倍率。HS*slide 按倍率改变 Slide 的显现速度（例如 999 基本瞬间出现），但不改变轨迹时长；全局 HS 不影响 Slide，轨迹运动仍由 SV*slide 控制。"),
        new("SPAWN", "<SPAWN*半径> / <SPAWN*tap=半径,hold=半径>", "环形音符视觉出生半径，范围 -4.8～4.8，默认值 1.225；0 是中心，-4.8 是对面判定线。支持 NULL 恢复。"),
        new("SPAWNMODE", "<SPAWNMODE*Rewind> / <SPAWNMODE*tap=Once,hold=Rewind>", "Rewind 会在 SV 退回 SPAWN 前时重新缩小并隐藏；Once 在第一次越过 SPAWN 后保持激活。默认 Rewind，NULL 恢复默认。"),
        new("DESTROY", "<DESTROY*半径> / <DESTROY*tap=半径,hold=半径> / <DESTROY*NULL>", "仅修改 Tap、Star、Each、Hold 的视觉终点，不改变判定时刻；始终保留所写半径，NULL 恢复 4.8。"),
        new("BOUNCE", "<BOUNCE*时长> / <BOUNCE*tap=8:1,hold=4:1> / <BOUNCE*NULL>", "让 Tap、Star、Each、Hold 从 DESTROY 到 SPAWN 往返；Rewind 在 SV 退回起跳点前时隐藏，Once 首次越过后保持激活，SV=0 时暂停。"),
        new("FAKE", "<FAKE*TRUE> / <FAKE*FALSE> / <FAKE*tap=TRUE,slide=TRUE>", "让当前音符流后续音符仅显示：不计物量、不判定、无判定/击打特效、文字和音效。"),
        new("COLOR", "<COLOR*RRGGBB>", "star 控制星形音符和 Slide 头，slidestar 单独控制运动星，slide 控制轨道。"),
        new("COLORV", "<COLORV*RRGGBB> / <COLORV*slidestar=FF0000>", "即时染色已加载音符；可分别指定普通类型及 star、slidestar、slide。"),
        new("SIZE", "<SIZE*倍率> / <SIZE*(X倍率,Y倍率)> / <SIZE*类型=(X倍率,Y倍率)>", "支持等比或局部 X/Y 缩放；类型可写 tap、hold、touch、star、slidestar、slide 等，全局倍率不缩放轨道。"),
        new("SIZEV", "<SIZEV*倍率> / <SIZEV*(X倍率,Y倍率)> / <SIZEV*类型=(X倍率,Y倍率)>", "按等比或局部 X/Y 即时缩放已加载音符，也支持指定音符类型。"),
        new("ALPHA", "<ALPHA*透明度>", "透明度范围 0～1；star 控制星形音符和 Slide 头，slidestar 控制运动星，slide 控制轨道。"),
        new("ALPHAV", "<ALPHAV*透明度> / <ALPHAV*slidestar=0.5>", "分别即时修改 star、slidestar 和 slide 的透明度。"),
        new("JLINE", "<JLINE*RRGGBB> / <JLINE*(RRGGBB[,过渡时间])>", "播放时渐变判定线颜色；NULL 恢复皮肤颜色，停止播放后不会影响待机判定线。"),
        new("TEXT", "<TEXT*(\"内容\"[,持续时间][,x][,y][,字号][,字体][,index][,样式][,过渡时间])>", "字幕必须写在双引号内。可选位置参数可用连续逗号跳过，例如 TEXT*(\"字幕\",,0.2,,44,Allerta,,Typewriter,1)。index 可使用任意非负整数。"),
        new("SHOWJUDGELINE", $"<SHOWJUDGELINE*({BooleanChoices}[,过渡时间])>", "渐变显示或隐藏判定线；省略过渡时间时立即切换。"),
        new("SHOWJUDGEAREA", $"<SHOWJUDGEAREA*({BooleanChoices}[,过渡时间])>", "渐变显示或隐藏判定区；省略过渡时间时立即切换。"),
        new("SHOWJUDGETEXT", $"<SHOWJUDGETEXT*({BooleanChoices}[,过渡时间])>", "渐变显示或隐藏 Critical Perfect 等判定文字。"),
        new("SHOWJUDGEINFO", $"<SHOWJUDGEINFO*({BooleanChoices}[,过渡时间])>", "渐变显示或隐藏左侧判定统计。"),
        new("SHOWCOMBOINFO", $"<SHOWCOMBOINFO*({BooleanChoices}[,过渡时间])>", "渐变显示或隐藏右侧 combo / 达成率信息。"),
        new("COMBODISPLAY", "<COMBODISPLAY*(模式[,过渡时间])>", "切换中间显示内容。模式: NONE / COMBO / SCORE / ACC / DXACC / DXSCORE 等。"),
        new("OUTERBRIGHTNESS", "<OUTERBRIGHTNESS*(遮暗值[,过渡时间])>", "外圈遮暗值，0 为全亮，1 为最暗。"),
        new("INNERBRIGHTNESS", "<INNERBRIGHTNESS*(遮暗值[,过渡时间])>", "内圈遮暗值，0 为全亮，1 为最暗。"),
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
        new("ZOOM", $"<ZOOM*({BooleanTrue},倍率[,过渡时间])>", "直接指定画面倍率：0.6 为 60%，1 为原大小，1.5 为 150%；范围 0.1～8。"),
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

    private static string? GetStateSignature(
        string name,
        bool? enabled = null,
        bool instantMode = false,
        int parameterCount = 1)
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
        var instant = name switch
        {
            "TINT" => $"<TINT*(Instant,RRGGBB,{strength},{duration})>",
            "MOVE" => $"<MOVE*(Instant,dx,dy,{duration})>",
            "SHAKE" => $"<SHAKE*(Instant,{strength},{frequency}[,{degrees}],{duration})>",
            "ZOOM" => $"<ZOOM*(Instant,{scale},{duration})>",
            "HUE" or "ROTATE" => $"<{name}*(Instant,{degrees},{duration})>",
            _ => $"<{name}*(Instant,{strength},{duration})>"
        };
        if (instantMode)
            return instant;
        return enabled switch
        {
            true => on,
            false => off,
            _ => on + " | " + off + " | " + instant
        };
    }

    // Including the True that switches the effect on: TINT, MOVE and SHAKE take one
    // argument more than the rest, which the grammar already states.
    private static int RequiredEffectParameterCount(string name)
    {
        var form = Describe(name)?.forms
            .FirstOrDefault(candidate => candidate.kind == AlphaArgumentFormKind.StateOn);
        return form?.MinimumCount ?? 2;
    }

    private static bool IsScreenEffect(string name) =>
        Describe(name)?.category == AlphaCommandCategory.Filter;

    private static string? GetStateSignatureOverview(string name)
    {
        if (!IsScreenEffect(name))
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
        return NoteDurationTarget.TryFromTypedText(tail, out slideTarget);
    }

    private static bool TryGetSelectedDurationTarget(string selection, out bool slideTarget)
        => NoteDurationTarget.TryFromSelection(selection, out slideTarget);

    private static bool IsInsideAlphaCommand(string prefix)
    {
        var open = prefix.LastIndexOf('<');
        if (open <= prefix.LastIndexOf('>'))
            return false;
        return AlphaCommandBoundary.IsPotentialStart(prefix, open);
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
        var instantMode = IsInstantArgument(document, starOffset + 1, caret);
        if (insightWindow != null &&
            string.Equals(insightCommand?.Name, command.Name, StringComparison.OrdinalIgnoreCase) &&
            insightProvider != null)
        {
            insightProvider.Update(parameterIndex, enabled, instantMode);
            return;
        }

        insightWindow?.Close();
        insightCommand = command;
        insightProvider = new AlphaOverloadProvider(command, parameterIndex, enabled, instantMode);
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

    private static bool IsInstantArgument(TextDocument document, int startOffset, int caretOffset)
    {
        if (caretOffset <= startOffset)
            return false;
        var value = document.GetText(startOffset, caretOffset - startOffset).TrimStart();
        if (value.StartsWith("(", StringComparison.Ordinal))
            value = value.Substring(1).TrimStart();
        var end = value.IndexOfAny(new[] { ',', ')' });
        if (end >= 0)
            value = value.Substring(0, end);
        return string.Equals(value.Trim(), "Instant", StringComparison.OrdinalIgnoreCase);
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
        if (SupportsNullReset(command.Name) && current.Length > 0 &&
            "NULL".StartsWith(current, StringComparison.OrdinalIgnoreCase))
        {
            document.Replace(argumentStart, argumentEnd - argumentStart, "NULL");
            textArea.Caret.Offset = argumentStart + 4;
            ShowOrUpdateInsight(textArea);
            return true;
        }
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

    private static bool SupportsNullReset(string name) =>
        Describe(name)?.SupportsNullReset == true;

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

    private static int GetMaximumArgumentCount(string name, bool? enabled) =>
        Describe(name)?.MaximumArgumentCount(enabled) ?? 1;

    private static string? GetDefaultArgument(string name, int parameterIndex, bool? enabled)
    {
        // Only the caption placeholder is worth translating; every other default is
        // a value, and the grammar carries it next to the rule that accepts it.
        if (name == "TEXT" && parameterIndex == 1)
            return Localized("\"字幕\"", "\"caption\"", "\"字幕\"");
        return Describe(name)?.DefaultArgument(parameterIndex, enabled);
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

    private static bool RequiresParenthesizedArguments(string name) =>
        Describe(name)?.InsertsParentheses == true;

    private static FrameworkElement BuildCompletionItem(AlphaCommand command)
    {
        var (label, color) = GetCategory(command.Name) switch
        {
            AlphaCommandCategory.Display =>
                (Localized("显示", "Display", "表示"), Color.FromRgb(0x24, 0x64, 0x7A)),
            AlphaCommandCategory.Filter =>
                (Localized("滤镜", "Filter", "フィルター"), Color.FromRgb(0x69, 0x3A, 0x78)),
            AlphaCommandCategory.Media =>
                (Localized("媒体", "Media", "メディア"), Color.FromRgb(0x26, 0x68, 0x4A)),
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

    private static AlphaCommandCategory GetCategory(string name) =>
        Describe(name)?.category ?? AlphaCommandCategory.Note;

    private static int GetCategoryOrder(AlphaCommandCategory category) => category switch
    {
        AlphaCommandCategory.Note => 0,
        AlphaCommandCategory.Display => 1,
        AlphaCommandCategory.Filter => 2,
        AlphaCommandCategory.Media => 3,
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
        private bool instantMode;

        public AlphaOverloadProvider(
            AlphaCommand command,
            int parameterIndex,
            bool? enabled,
            bool instantMode)
        {
            this.command = command;
            this.parameterIndex = parameterIndex;
            this.enabled = enabled;
            this.instantMode = instantMode;
        }

        public void Update(int newParameterIndex, bool? newEnabled, bool newInstantMode)
        {
            if (parameterIndex == newParameterIndex && enabled == newEnabled &&
                instantMode == newInstantMode)
                return;
            parameterIndex = newParameterIndex;
            enabled = newEnabled;
            instantMode = newInstantMode;
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
                GetStateSignature(command.Name, enabled, instantMode, parameterIndex) ??
                command.Signature, parameterIndex));
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
                    "ZOOM" => Localized("当前参数：画面倍率；0.6 为 60%，1 为原大小，1.5 为 150%。", "Current parameter: frame scale; 0.6 = 60%, 1 = unchanged, and 1.5 = 150%.", "現在の引数：画面倍率。0.6 は 60%、1 は等倍、1.5 は 150% です。"),
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
                ("TEXT", 1) => Localized("当前参数：双引号内的字幕内容。", "Current parameter: caption text inside double quotes.", "現在の引数：二重引用符内の字幕内容。"),
                ("TEXT", 2) => Localized("当前参数：显示时长；省略时保持到下一条 TEXT。", "Current parameter: display duration; omit it to keep the caption until the next TEXT.", "現在の引数：表示時間。省略すると次の TEXT まで表示します。"),
                ("TEXT", 3) or ("TEXT", 4) or ("TEXT", 5) => Localized(
                    "当前参数：依次为 x、y、字号；x/y 是相对左上角的屏幕比例。保留默认值时留空该槽，也兼容 x=、y=、size= 命名写法。",
                    "Current parameters: x, y and size in order. x/y are screen fractions from the top left. Leave a slot empty for its default; named x=, y= and size= forms also work.",
                    "現在の引数：順に x、y、サイズ。x/y は左上からの画面比です。既定値は空欄で飛ばせ、x=、y=、size= 形式も使えます。"),
                ("TEXT", 6) => Localized(
                    "当前参数：字体；可选 Default、CascadiaMono、CascadiaCode、MicrosoftYaHei、NotoSansSC、SimSun、DengXian、NotoSerifSC、GlobalMonospace、Aileron、Allerta，也兼容 font=字体。",
                    "Current parameter: font. Choices: Default, CascadiaMono, CascadiaCode, MicrosoftYaHei, NotoSansSC, SimSun, DengXian, NotoSerifSC, GlobalMonospace, Aileron, Allerta; font=FONT also works.",
                    "現在の引数：フォント。Default、CascadiaMono、CascadiaCode、MicrosoftYaHei、NotoSansSC、SimSun、DengXian、NotoSerifSC、GlobalMonospace、Aileron、Allerta。font=形式も使えます。"),
                ("TEXT", 7) => Localized("当前参数：index 为任意非负整数；不同索引可同时显示，相同索引的新字幕替换旧字幕。", "Current parameter: index is any non-negative integer. Different indices coexist; a new caption replaces the same index.", "現在の引数：index は 0 以上の整数。異なる index は同時表示でき、同じ index の新字幕だけを置き換えます。"),
                ("TEXT", 8) => Localized("当前参数：样式，可写 Fade 或 Typewriter；也兼容 style=样式。", "Current parameter: style, either Fade or Typewriter; style=STYLE also works.", "現在の引数：スタイル。Fade または Typewriter。style=形式も使えます。"),
                ("TEXT", 9) => Localized("当前参数：过渡时长；Fade 时为渐入时间，Typewriter 时为逐字显示完成时间，也兼容 transition=时长。", "Current parameter: transition duration. It is fade-in time for Fade and typing time for Typewriter; transition=DURATION also works.", "現在の引数：切り替え時間。Fade ではフェード、Typewriter では文字送り完了時間です。transition=形式も使えます。"),
                ("SHOWJUDGELINE" or "SHOWJUDGEAREA" or "SHOWJUDGETEXT" or
                    "SHOWJUDGEINFO" or "SHOWCOMBOINFO", 1) => Localized("当前参数：True 显示，False 隐藏。", "Current parameter: True shows and False hides.", "現在の引数：True で表示、False で非表示。"),
                ("SHOWJUDGELINE" or "SHOWJUDGEAREA" or "SHOWJUDGETEXT" or
                    "SHOWJUDGEINFO" or "SHOWCOMBOINFO", 2) => Localized("当前参数：显示状态的过渡时间。", "Current parameter: visibility transition duration.", "現在の引数：表示状態の切り替え時間。"),
                ("OUTERBRIGHTNESS" or "INNERBRIGHTNESS", 1) => Localized("当前参数：遮暗值，0 为全亮，1 为最暗。", "Current parameter: darkness; 0 is fully bright and 1 is darkest.", "現在の引数：遮暗量。0 は最も明るく、1 は最も暗い状態です。"),
                ("OUTERBRIGHTNESS" or "INNERBRIGHTNESS", 2) => Localized("当前参数：亮度过渡时间。", "Current parameter: brightness transition duration.", "現在の引数：明るさの切り替え時間。"),
                ("SIZE" or "SIZEV", 1) => Localized(
                    "当前参数：等比倍率，或 (X倍率,Y倍率)；也可写 tap=(1,2)、hold=(1.1,0.8)、slide=1.25。X/Y 是音符自身的局部方向。",
                    "Current parameter: uniform scale or (scaleX,scaleY). Typed forms such as tap=(1,2), hold=(1.1,0.8), and slide=1.25 are supported; X/Y are local note axes.",
                    "現在の引数：等倍または (X倍率,Y倍率)。tap=(1,2)、hold=(1.1,0.8)、slide=1.25 も使用でき、X/Y はノーツのローカル軸です。"),
                ("SIZE" or "SIZEV", 2) => Localized(
                    "当前参数：Y 方向倍率；只写单个数字时 X/Y 等比。",
                    "Current parameter: local Y scale. A single number scales X and Y uniformly.",
                    "現在の引数：ローカル Y 倍率。数値を1つだけ指定すると X/Y を等倍にします。"),
                ("COMBODISPLAY", 1) => Localized("当前参数：中间显示模式，例如 Combo、DxScore、Achievement、None。", "Current parameter: center display mode, such as Combo, DxScore, Achievement, or None.", "現在の引数：中央表示モード（Combo、DxScore、Achievement、None など）。"),
                ("COMBODISPLAY", 2) => Localized("当前参数：模式切换过渡时间。", "Current parameter: mode transition duration.", "現在の引数：モード切り替え時間。"),
                ("SPAWN", 1) => Localized(
                    "当前参数：生成半径，默认 1.225；0 是中心，-4.8 是对面判定线，NULL 恢复默认。",
                    "Current parameter: spawn radius. The default is 1.225; 0 is center, -4.8 is the opposite judge line, and NULL restores the default.",
                    "現在の引数：出現半径。既定値は 1.225、0 は中心、-4.8 は反対側の判定ライン、NULL で既定値に戻します。"),
                ("SPAWNMODE", 1) => Localized(
                    "当前参数：Rewind 会在 SV 退回 SPAWN 前时再次隐藏，Once 只在首次越过时激活；NULL 恢复 Rewind。",
                    "Current parameter: Rewind hides again when SV returns before SPAWN; Once latches the first crossing. NULL restores Rewind.",
                    "現在の引数：Rewind は SV が SPAWN より前へ戻ると再び非表示、Once は最初の通過を保持します。NULL で Rewind に戻します。"),
                ("BOUNCE", 1) => Localized(
                    "当前参数：往返时长，可写秒数或 8:1；NULL 关闭。",
                    "Current parameter: round-trip duration in seconds or beat form such as 8:1; NULL disables it.",
                    "現在の引数：往復時間。秒数または 8:1 の拍形式を使用でき、NULL で無効にします。"),
                ("DESTROY", 1) => Localized(
                    "当前参数：Tap、Star、Each、Hold 的视觉终点半径；NULL 恢复 4.8。",
                    "Current parameter: visual endpoint radius for Tap, Star, Each, and Hold. NULL restores 4.8.",
                    "現在の引数：Tap、Star、Each、Hold の表示上の終点半径。NULL で 4.8 に戻します。"),
                ("FAKE", 1) => Localized(
                    "当前参数：TRUE 开启仅显示音符，FALSE 关闭。",
                    "Current parameter: TRUE enables visual-only notes; FALSE disables it.",
                    "現在の引数：TRUE で表示専用ノーツ、FALSE で解除します。"),
                _ => string.Empty
            };
        }

    }

    private sealed record SignatureParameter(string Text, bool Optional);

    // A hint often spells more than one accepted form, separated by " / ". Only the
    // first was broken into parameters; everything after it was pasted in as
    // written, brackets and all, which is why some commands showed the brackets
    // that mark an omittable argument and others did not.
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

        var forms = (signature ?? string.Empty).Split(" / ");
        for (var index = 0; index < forms.Length; index++)
        {
            if (index > 0)
                block.Inlines.Add(" / ");
            AppendSignatureForm(block, forms[index], activeParameter);
        }
        return block;
    }

    // The brackets around an omittable argument are notation for this document and
    // are never typed, so they are not drawn either: grey italics is what says an
    // argument can be left out. A bracket that is not opening an omittable group
    // belongs to the syntax itself, like a slide's [8:1], and stays.
    private static string StripOptionalBrackets(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains("[,", StringComparison.Ordinal))
            return text ?? string.Empty;
        var builder = new System.Text.StringBuilder(text.Length);
        var depth = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '[' && index + 1 < text.Length && text[index + 1] == ',')
            {
                depth++;
                continue;
            }
            if (text[index] == ']' && depth > 0)
            {
                depth--;
                continue;
            }
            builder.Append(text[index]);
        }
        return builder.ToString();
    }

    private static void AppendSignatureForm(
        TextBlock block,
        string signature,
        int activeParameter)
    {
        var star = signature.IndexOf('*');
        var close = star >= 0 ? signature.IndexOf('>', star) : -1;
        if (star < 0 || close <= star)
        {
            block.Inlines.Add(StripOptionalBrackets(signature));
            return;
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
        block.Inlines.Add(StripOptionalBrackets(signature[close..]));
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
