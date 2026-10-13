using MajdataCore;
using System.Text.RegularExpressions;
namespace MajdataEdit;

// Keep the authored details intact, but give every syntax entry the same reading order.
internal static class AlphaGuideFormatter
{
    internal static string[] Format(string source, string language = "zh")
    {
        source = Regex.Replace(source, @"旧(?:版|写法)[^。\r\n]*。", "");
        var lines=source.Replace("\r\n","\n").Replace('\r','\n').Split('\n')
            .SelectMany(line => line.StartsWith("S ") || line.StartsWith("E ")
                ? line[2..].Split(" / ").Select(form => line[..2] + form) : new[] { line }).ToArray();
        var result=new List<string>();
        for(int i=0;i<lines.Length;) {
            if(!lines[i].StartsWith("### ",StringComparison.Ordinal)) { result.Add(lines[i++]); continue; }
            var title=lines[i++]; result.Add(title);
            var body=new List<string>();
            while(i<lines.Length && !lines[i].StartsWith("## ") && !lines[i].StartsWith("### ")) body.Add(lines[i++]);
            body = body.Where(line => !line.StartsWith("- 参数位置：") && !line.StartsWith("- 参数顺序："))
                .SelectMany(line => line.StartsWith("S ") || line.StartsWith("E ")
                    ? line[2..].Split(" / ").Select(form => line[..2] + form)
                    : new[] { line }).ToList();
            var description=body.FirstOrDefault(l=>l.Length>0 && !l.StartsWith("S ") && !l.StartsWith("E ") && !l.StartsWith("! "));
            if(description!=null) {
                var introduction=description.StartsWith("- ") ? description[2..] : description;
                result.Add(introduction);
            } else if(title.Contains(" — ")) result.Add(title[4..].Replace(" — "," · "));
            var syntax=body.Where(l=>l.StartsWith("S ")).ToArray();
            var commandNames=syntax.SelectMany(line=>Regex.Matches(line,@"<([A-Za-z]+)\*").Cast<Match>())
                .Select(match=>match.Groups[1].Value.ToUpperInvariant()).Distinct().ToArray();
            if(commandNames.Length>0)
                syntax=commandNames.Select(name=>"S "+AlphaCommandExamples.Header(name, language)).ToArray();
            var chinese=language is not ("en" or "ja");
            if(syntax.Length>0) { result.Add(chinese ? "#### 写法" : language=="ja" ? "#### 書式" : "#### Syntax"); result.AddRange(syntax); }
            var examples=commandNames.Length>0
                ? commandNames.SelectMany(name=>AlphaCommandExamples.Examples(name).Split(" / ")).Select(s=>"E "+s).ToArray()
                : body.Where(l=>l.StartsWith("E ")).ToArray();
            if(examples.Length>0) { result.Add(chinese ? "#### 示例" : language=="ja" ? "#### 使用例" : "#### Examples"); result.AddRange(examples); }
            var details=body.Where(l=>l.Length>0 && l != description && !l.StartsWith("S ") && !l.StartsWith("E ")).Distinct().ToArray();
            var parameterHeading=chinese ? "#### 参数与影响" : language=="ja" ? "#### 引数と効果" : "#### Parameters and effects";
            if(details.Length>0) { result.Add(parameterHeading); result.AddRange(details); }
            var name=title[4..].Split(new[]{' ','—','：'},StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if(AlphaExtraFilters.Find(name ?? "") is { } effect) {
                if(details.Length==0) result.Add(parameterHeading);
                if(chinese) {
                    result.Add("- 参数 1 · 状态：True 持续开启；False 关闭；Instant 临时开启。");
                    result.Add(effect.Canonical=="Kaleidoscope" ?
                        "- 参数 2 · 角度（度）：例如 0、45、-90。改变镜像扇区方向；0 度仍显示万花筒，关闭请用 False。" :
                        "- 参数 2 · 强度（True / Instant）："+effect.Chinese);
                    result.Add("- 参数 3 · 时间：True 时为可省略的过渡时间；Instant 时为必填持续时间。可写秒数或 8:1 等拍数。");
                    result.Add("- False 写法：关闭时间位于参数 2，例如 (False,0.5)；省略时立即关闭。");
                } else if(language=="ja") {
                    result.Add("- 引数1・状態：Trueは継続、Falseは無効、Instantは一時的に有効。");
                    result.Add("- 引数2（True / Instant）："+effect.Japanese);
                    result.Add("- 引数3・時間：Trueは省略可能な遷移時間、Instantは必須の持続時間。秒数や8:1で指定。");
                    result.Add("- Falseでは終了の遷移時間が引数2。省略すると直ちに無効。");
                } else {
                    result.Add("- Parameter 1 · State: True stays enabled; False disables; Instant enables temporarily.");
                    result.Add("- Parameter 2 (True / Instant): "+effect.English);
                    result.Add("- Parameter 3 · Time: optional transition for True; required duration for Instant. Use seconds or beat lengths such as 8:1.");
                    result.Add("- False uses parameter 2 for the optional closing transition; omission closes immediately.");
                }
            } else if(chinese) {
                var commands=syntax.SelectMany(line=>Regex.Matches(line,@"<([A-Za-z]+)\*").Cast<Match>())
                    .Select(match=>match.Groups[1].Value.ToUpperInvariant()).Distinct().ToArray();
                var parameters=commands.SelectMany(ParameterEffects).ToArray();
                if(parameters.Length>0) {
                    if(details.Length==0) result.Add(parameterHeading);
                    result.AddRange(parameters.Select(detail=>"- "+detail));
                }
            }
        }
        return result.ToArray();
    }

    private static IEnumerable<string> ParameterEffects(string command) => command switch {
        "SV" => new[]{"SV · 倍率：1 原速、2 双速、0 停止、负值倒退。全局默认控制 blockarea，不控制 Slide 轨迹；显式 slide= 改变轨迹运动。", "SV · blockarea=倍率：单独控制噪域预警。SV=2 的预警提前量减半，与同流速 Touch 同时出现。类型=NULL 恢复跟随全局值。"},
        "HS" => new[]{"HS · 倍率：改变出现与下落速度；全局默认控制 blockarea，不影响 Slide；slide= 只改变轨迹渐入，不改变移动时长。", "HS · blockarea=倍率：单独控制噪域预警，使用 Touch 的速度公式。类型=NULL 恢复默认倍率。"},
        "SPAWN" => new[]{"SPAWN · 半径：0 中心、4.8 本侧判定线、-4.8 对侧判定线；允许圈外有限数值。NULL 恢复默认出生位置。"},
        "DESTROY" => new[]{"DESTROY · 半径：设置视觉终点，判定时刻不变；NULL 恢复 4.8。"},
        "SPAWNMODE" => new[]{"SPAWNMODE · 模式：Rewind 在回退到出生点之前隐藏；Once 出生一次后保持可见；NULL 恢复 Rewind。"},
        "BOUNCE" => new[]{"BOUNCE · 时长：控制从判定线跳起再返回的完整往返时间；秒数或拍数必须大于 0。NULL / False 取消往返运动。"},
        "FAKE" => new[]{"FAKE · 状态：True 仅显示，False 恢复正常；类型=状态只作用于指定音符类型。"},
        "COLOR" or "COLORV" => new[]{command+" · 颜色：6 位十六进制 RGB；FFFFFF 白色。全局默认不控制 blockarea；blockarea= 指定噪域。分类型值优先；COLORV 也修改已出现音符。"},
        "SIZE" or "SIZEV" => new[]{command+" · 倍率：1 原大小，0.5 一半；(X,Y) 分别缩放宽高。全局不控制轨迹或 blockarea；blockarea= 指定噪域。slide= 控制轨迹，slidestar= 控制运动星。SIZEV 也修改已出现音符。"},
        "ALPHA" or "ALPHAV" => new[]{command+" · 不透明度：0 隐藏、1 完全可见。全局默认不控制 blockarea；blockarea= 指定噪域。只改显示，ALPHAV 也修改已出现音符。"},
        "JLINE" => new[]{"JLINE · 参数 1 颜色：RRGGBB 或 RRGGBBAA；NULL 恢复皮肤颜色。参数 2 过渡时间：可省略，省略立即改变。"},
        "TEXT" => new[]{"TEXT · 参数 1 内容：双引号内的文字。参数 2 持续时间：可省略，省略保持到相同 index 的下一条字幕。",
            "TEXT · x / y：字幕位置；size：字号；font：字体名；index：任意非负整数的独立字幕通道。",
            "TEXT · style：Fade 渐入或 Typewriter 逐字出现；transition：渐入时长或逐字显示完成时长。"},
        "JUDGELINEEXPAND" => new[]{"JUDGELINEEXPAND · 参数 1 开关：True 展开，False 立即移除附加白线，保留原判定线；True 后的参数 2 过渡时间默认 0 秒，参数 3 方向默认 1 / CW 顺时针，-1 / CCW 逆时针，目标均为 22.5°。"},
        "JUDGELINE" => new[]{"JUDGELINE · 参数 1 样式：0 / False 关闭，1 默认，2 判定区，3 Magical；Default 恢复设置中的判定线样式；参数 2 过渡时间可省略。"},
        "SHOWJUDGETEXT" or "SHOWJUDGEINFO" or "SHOWCOMBOINFO" => new[]{command+" · 参数 1 开关：True 显示，False 隐藏，Default 恢复设置中的开关；参数 2 过渡时间：可省略，省略立即切换。"},
        "COMBODISPLAY" => new[]{"COMBODISPLAY · 参数 1 模式：选择中央显示的数据，None 隐藏，Default 恢复设置中的中央显示模式；参数 2 过渡时间可省略。"},
        "OUTERBRIGHTNESS" or "INNERBRIGHTNESS" => new[]{command+" · 参数 1 明暗值：0 全亮，1 最暗，Default 恢复设置中的亮度；参数 2 过渡时间可省略。"},
        "HUE" => new[]{command+" · 参数 2 角度：单位为度，允许正负；参数 3 为可省略的过渡时间。False 关闭，Instant 的末参数为必填持续时间。"},
        "ROTATE" => new[]{"ROTATE · 角度：单位为度，允许正负；True 的时间为可省略过渡时间，Instant 的时间为必填持续时间；False 关闭。", "ROTATE · 旋转 PV：末尾可附加 True / False，默认 False，仅旋转谱面。True 同时旋转歌曲 PV 和 PVOVERLAY；切换和关闭均支持过渡、回拖。"},
        "TINT" => new[]{"TINT · 参数 2 颜色：RRGGBB；参数 3 强度：0 保留原图，1 完全染成目标色；参数 4 为过渡时间（True）或持续时间（Instant）。"},
        "MOVE" => new[]{"MOVE · 参数 2 dx：正数向右；参数 3 dy：正数向上；参数 4 为过渡时间（True）或持续时间（Instant）。"},
        "SHAKE" => new[]{"SHAKE · 参数 2 强度：1 约为画面尺寸的 5%；参数 3 频率：每秒震动次数；参数 4 角度：0 水平、90 垂直，省略为二维震动；末参数为时间。"},
        "VIGNETTE" => new[]{"VIGNETTE · 参数 2 强度：越大圆形可见区域越小；参数 3 过渡时间；参数 4 渐变：默认 True，False 为硬边。"},
        "ZOOM" => new[]{"ZOOM · 参数 2 倍率：1 原大小，0.6 缩小，1.5 放大；参数 3 过渡时间可省略。"},
        "AUDIO" => new[]{"AUDIO · 参数 1 开关：True 播放、False 停止；参数 2 路径：相对谱面文件夹的音频文件。"},
        "PVOVERLAY" => new[]{"PVOVERLAY · 参数 1 开关：True 覆盖、False 恢复；参数 2 路径：相对谱面文件夹的图片或视频；参数 3 过渡时间可省略。False 的时间在参数 2。"},
        "GAUSSIAN" or "NEON" or "TRAIL" or "FADE" or "FLASH" or "BRIGHTNESS" or "SATURATION" or "CONTRAST" or "RAINBOW" or "GLITCH" or "TVNOISE" => new[]{command+" · 参数 1 状态：True 保持、False 关闭、Instant 临时开启；参数 2 为强度（True / Instant）或关闭时间（False）；参数 3 为过渡时间（True）或必填持续时间（Instant）。"},
        _ => Array.Empty<string>()
    };
}
