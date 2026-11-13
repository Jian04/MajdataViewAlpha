using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace MajdataEdit;

public partial class AlphaSyntaxHelp : Window
{
    private readonly List<(string Title, Block Target)> sections = new();
    private Block[][] chapterBlocks = Array.Empty<Block[]>();
    private void SelectChapter(int index)
    {
        if (index < 0 || index >= chapterBlocks.Length) return;
        HelpViewer.Document.Blocks.Clear();
        foreach (var block in chapterBlocks[index]) HelpViewer.Document.Blocks.Add(block);
        chapterBlocks[index].FirstOrDefault()?.BringIntoView();
    }
    internal void UseSidebarLayout(FrameworkElement content)
    {
        if (content is not Grid grid) return;
        grid.ColumnDefinitions[0].Width = new GridLength(0);
        grid.ColumnDefinitions[1].Width = new GridLength(0);
        grid.Children[0].Visibility = Visibility.Collapsed;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(grid.Children[1], 2);
        var language = new ComboBox { ItemsSource = new[] { "Simai 语法帮助", "Alpha 语法帮助" },
            SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 6) };
        MajdataSessionUi.SessionTheme.Input(language);
        Grid.SetColumn(language, 2); Grid.SetRow(language, 0); grid.Children.Add(language);
        var navigation = new ComboBox { ItemsSource = sections.Select(s => s.Title).ToArray(),
            SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 10), ToolTip = "选择章节" };
        MajdataSessionUi.SessionTheme.Input(navigation);
        navigation.SelectionChanged += (_, _) =>
        {
            SelectChapter(navigation.SelectedIndex);
        };
        language.SelectionChanged += (_, _) =>
        {
            BuildDocument(language.SelectedIndex == 0 ? SimaiGuide : MainWindow.GetLocalizedString("AlphaHelpStructuredText"));
            navigation.ItemsSource = sections.Select(s => s.Title).ToArray();
            navigation.SelectedIndex = 0;
            SelectChapter(0);
        };
        Grid.SetColumn(navigation, 2); Grid.SetRow(navigation, 1); grid.Children.Add(navigation);
        HelpViewer.Document.PagePadding = new Thickness(18, 16, 18, 24);
        SelectChapter(0);
    }
    private static FontFamily BodyFont => (FontFamily)Application.Current.FindResource("UiFontFamily");
    private static readonly FontFamily CodeFont =
        new("Cascadia Mono, Cascadia Code, Consolas, Microsoft YaHei UI");

    public AlphaSyntaxHelp()
    {
        InitializeComponent();
        BuildDocument(SimaiGuide);
    }

    private void BuildDocument(string source)
    {
        NavigationPanel.Children.Clear();
        sections.Clear();
        var document = new FlowDocument
        {
            PagePadding = new Thickness(28, 24, 32, 36),
            ColumnWidth = double.PositiveInfinity,
            FontFamily = BodyFont,
            FontSize = 13.5,
            LineHeight = 21,
            TextAlignment = TextAlignment.Left
        };
        document.SetResourceReference(TextElement.ForegroundProperty, "ButtonForeground");

        var language=WPFLocalizeExtension.Engine.LocalizeDictionary.Instance.Culture?.TwoLetterISOLanguageName
            ?? System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var lines = AlphaGuideFormatter.Format(source, language);
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (string.IsNullOrWhiteSpace(line))
                continue;
            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                document.Blocks.Add(CreateHeading(line[2..], 28, new Thickness(0, 0, 0, 9)));
                continue;
            }
            if (line.StartsWith("> ", StringComparison.Ordinal))
            {
                document.Blocks.Add(CreateLead(line[2..]));
                continue;
            }
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                var title = line[3..];
                var section = CreateSectionHeading(title);
                document.Blocks.Add(section);
                AddNavigationButton(title, section);
                continue;
            }
            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                document.Blocks.Add(CreateHeading(line[4..], 17, new Thickness(0, 15, 0, 5)));
                continue;
            }
            if (line.StartsWith("#### ", StringComparison.Ordinal)) {
                document.Blocks.Add(CreateHeading(line[5..],13,new Thickness(0,10,0,3)));
                continue;
            }
            if (line.StartsWith("S ", StringComparison.Ordinal))
            {
                document.Blocks.Add(CreateCodeCard(
                    MainWindow.GetLocalizedString("AlphaHelpSyntaxLabel"), line[2..], false));
                continue;
            }
            if (line.StartsWith("E ", StringComparison.Ordinal))
            {
                document.Blocks.Add(CreateCodeCard(
                    MainWindow.GetLocalizedString("AlphaHelpExampleLabel"), line[2..], true));
                continue;
            }
            if (line.StartsWith("! ", StringComparison.Ordinal))
            {
                document.Blocks.Add(CreateCallout(line[2..]));
                continue;
            }
            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                document.Blocks.Add(CreateParagraph("•  " + line[2..], new Thickness(12, 1, 0, 2)));
                continue;
            }
            document.Blocks.Add(CreateParagraph(line, new Thickness(0, 1, 0, 6)));
        }

        HelpViewer.Document = document;
        var blocks = document.Blocks.ToArray();
        chapterBlocks = sections.Select((s, i) => blocks.Skip(Array.IndexOf(blocks, s.Target))
            .Take((i + 1 < sections.Count ? Array.IndexOf(blocks, sections[i + 1].Target) : blocks.Length)
                - Array.IndexOf(blocks, s.Target)).ToArray()).ToArray();
    }

    // Follow the official Simai notation, then document this parser's boundaries.
    // Keep Alpha-only commands in the separate Alpha guide.
    private const string SimaiGuide = @"
# Simai 语法帮助
> 按“文件与时间 → 基本音符 → Slide → 进阶写法”的顺序学习。以 Simai 官方记法为主，并结合 MajdataView 写谱文档、本项目解析器说明兼容范围；Alpha 新增音符与命令见另一份 Alpha 语法帮助。
## 文件与谱面结构
谱面文件通常是歌曲文件夹内的 maidata.txt。同文件夹放 track.mp3 或 track.ogg；曲名、作者和各难度分别写在元数据字段中。
S &title=曲名
E &artist=曲师
E &des_5=谱师
E &lv_5=14+
E &inote_5=(190){16}1,2,3,4,
数字 1–8 是从右上角 1 开始顺时针排列的外圈按键。不同难度使用对应的 &inote_编号。
- &first 是第一槽相对音频起点的秒数，可带小数；它不改变音符之间的间隔。
- &wholebpm 用于歌曲信息显示，实际时间轴的 BPM 仍要在 &inote 内用 (BPM) 指定。
- &lv_1 到 &lv_7、&des_1 到 &des_7、&inote_1 到 &inote_7 分别保存各难度等级、谱师和谱面；&des 是通用谱师。
E &first=1.25
E &wholebpm=180
! 元数据只写在 maidata.txt 的文件字段中；Edit 的谱面文本框只填写对应 &inote 的内容。
## 时间槽、BPM 与分音
圆括号写 BPM，花括号写分音。逗号推进一个槽位，空槽即休止；可以在谱面中途再改变 BPM 或分音。
S (BPM){分音}音符,音符,
E (180){16}1,2,3,4,
E (180){4},,,,
- 一个 {N} 槽的秒数为 240 ÷ BPM ÷ N。例如 BPM 120 时，{4} 每槽 0.5 秒，{8} 每槽 0.25 秒。
- {3}、{6}、{12} 等可写三连音；改变分音不会移动已经写完的槽位。
- 本项目的分音需为正整数。官方另有 {#秒数} 固定槽长和小数分音记法，当前解析器尚不支持，不能直接照搬。
- 换行和空白方便阅读，不推进时间；真正推进时间的是英文逗号。中文逗号不等价。
- 本项目只读取有英文逗号结尾的音符槽。最后一颗音符也要补逗号；E 不能代替这个逗号。
E (120){4}1,2,{8}3,4,5,6,(180){4}7,8,
## 持续时间的统一写法
Hold 的按住时间和 Slide 的移动时间都使用方括号。n:m 表示 m 个 n 分音符，按该音符前最近一次 BPM 换算。
S [分音:个数]
E 1h[8:3],2h[4:1],
- 当前 BPM 为 120 时，[8:3] 为 0.75 秒，[1:1] 为 2 秒；[16:1] 比 [8:1] 短一半。
- [指定BPM#分音:个数] 用指定 BPM 计算时长，不改变主谱 BPM。
- Hold 的 [#秒数] 可直接写秒数。Slide 的 # 还会影响启动等待，见后面的启动时间章节。
E 1h[150#4:1],2h[#0.4],
## Tap、Break 与 EX
外圈数字是 Tap；b 表示 Break；x 表示 EX。一个槽位写完要用英文逗号结束。
S 1,2b,3x,4bx,
- b 与 x 可以叠加到 Hold 和 Slide 星星头；推荐按 b、x、h 的顺序写，便于阅读。
E 1bxh[8:1],2bx-6[8:1],
## Hold
h 是 Hold；方括号给出持续时间。[4:1] 表示当前 BPM 下一个四分音符。也可以用 # 写秒数。
S 1h[4:1],2bh[8:1],
E 1h[#2.5],
- 不写方括号的 1h 是短 Hold，不会继承前面另一颗 Hold 的时长。
- 按住过程中遇到新的 BPM，不会重新解释这颗 Hold 已写定的时长。
E (120){4}1h[2:1],(180)2,3,4,
## Slide 基本形状
Slide 由起点、路径、终点、持续时间组成。起点星星与轨迹是同一个音符。
S 1-5[8:1],
- -：两点间的直线。普通键位不能以自身或紧邻键为终点，例如 1-5 合法，1-2 不合法。
- <、>：沿外圈向左或向右。它们不是固定的逆时针、顺时针：上半圈 1/2/7/8 的 > 是顺时针，下半圈 3/4/5/6 的 > 是逆时针；< 相反。
- ^：选择较短的外圈弧线，不能用正对面的终点制造方向歧义。
- v：经中心折向终点；可回到自身键位（如 1v1[8:1]），不能到正对面键位。
- p、q：绕内圈，p 逆时针、q 顺时针；pp、qq：向侧边绕行的相应圆弧。它们可以回到自己的起点。
- s、z：方向相反的闪电形；w：扇形 Wifi。普通键位的终点必须在起点正对面。
- V：先到指定转折键，再到终点，必须写两个数字，例如 1V36 表示 1 → 3 → 6；转折键和终点仍需满足几何限制。
E 1>5[4:1],2q6[8:3],1V36[8:1],
! 每种轨迹有合法终点范围；输入后以预览和语法错误提示为准。
## Slide 组合与时间
星号连接同一起点的多条 Slide；连续路径可以写在一个音符中。每段可单独指定时间。
S 1-3[8:1]*-5[8:1],
E 1>5-8[1:1],
E 1>5[4:1]-8[8:1],
官方 Break Slide 在时长方括号后加 b，例如 1-4[4:1]b；本项目也接受放在时长前的 1-4b[4:1]。
- 星星头的 b 和轨迹的 b 互相独立：1b-5[8:1] 是绝赞头，1-5[8:1]b 是绝赞轨迹，1b-5[8:1]b 两者都是。
- 同头多条轨迹的每个分支都要从同一个起点完整接出；不要把某个分支尾部当成其他分支的起点。
- 连续 Slide 可只在最后写一个总时长，也可以每一段都写时长；不能只给中间某些段写时间。
E 1-5-2[2:1]*-5-8[2:1],
E 1-5[4:1]-2[8:1],
E 1b-5b[8:1]*-7[4:1],
## 进阶 Slide：无头、星星与启动时间
在起点后写 ? 或 ! 可以去掉需要击打的星星头；? 保留星星渐入，! 不渐入。只想显示星星而不写轨迹时，在数字后加 $。
S 1?-5[8:1],1!-5[8:1],1$,
E 2?-6[4:1],3!-7[4:1],
Slide 时长括号的前半段可以设置星星击打后等待多久再启动，## 后半段是移动时长；未指定等待时间时默认等当前 BPM 的一拍。
S 1-5[等待秒数##移动秒数],
E 1-5[0.2##0.8],
E 1-5[190#8:1],2-6[190#0.8],
- [指定BPM#分音:个数]：按指定 BPM 的一拍等待，再按指定 BPM 计算移动时间。
- [指定BPM#秒数]：按指定 BPM 的一拍等待，后面直接给移动秒数。
- [等待秒数##移动秒数]：等待和移动都直接按秒指定。
- [等待秒数##分音:个数]：等待按秒指定，移动按当前 BPM 的分音换算。
- [等待秒数##指定BPM#分音:个数]：等待按秒指定，移动按方括号内指定 BPM 换算。上述形式均不改变主谱 BPM。
E 1-5[0.2##8:3],2-6[0.2##150#8:3],
- $ 是固定朝向的星形 Tap，$$ 是旋转星形 Tap；它们自身不生成 Slide 轨迹。
- 官方可用 1@-5[8:1] 把星形头改成普通 Tap；当前解析器不支持这项写法。不要与行首的 Alpha 音符流 @ 混淆。
E 1-5[60#1],2-6[0##0.5],
E 1$,2$$,
! 多条同起点 Slide 的星星只击打一次；无头轨迹也不额外计入一个 Tap。
## Touch 与 TouchHold
A、B、D、E 为触摸区，C 是中心。f 给 Touch 加烟花；Ch 是中心 TouchHold。
S B1,C,D3f,Ch[4:1],
- A/B/D/E 区需带 1–8 编号；C、C1、C2 都表示中心区。
- f 给成功击打的 Touch 或 TouchHold 添加烟花，不改变时长。
- 非 C 区也可写 TouchHold，例如 E1h[4:1]。
- Touch 的绝赞、地雷等扩展，在 Alpha 帮助中单独说明。
E E1h[4:1],B3h[8:1],Ch,
## 同时音符与顺序
同槽位用 / 连接多个音符，例如 1/2。两个简单 Tap 也可写 12。反引号表示相差极短时间的伪双押。
S 1/2,1b/3,1-5[8:1]/2,
E 1`2,
- / 两边都应写完整音符，带 b、x、h 或路径时不要用紧挨数字的简写。
- 反引号按书写顺序将相邻音符错开极短时间，制造外观接近同时、判定时序不同的伪双压。
## 结束、注释与排错
末尾可写 E 标明结束；末颗音符后仍要有英文逗号。本项目播放和录制的结束点包含最后音符的持续时间，具体导出选项还会控制收尾。
S (120){4}1,2,3,4,E
单行注释从 || 到本行末尾。Alpha 另提供块注释、多行独立音符流和拍号标记，详见 Alpha 帮助。
E (120){4}1,2,|| 此行后面不解析
- 检查括号是否配对，特别是 Slide 的 [时长]、BPM 的 ( ) 和分音的 { }。
- 要写两个同时音符，用 /；要推进到下一个时间点，用逗号；要分开排版，用换行。
- 每种 Slide 都有合法的起终点组合；先用简单的一条路径确认，再添加连接段、修饰和同头分支。
- 编辑到一半的槽位会按光标位置延迟报错；移出该槽位后可查看语法错误栏。
官方记法：https://w.atwiki.jp/simai/pages/1002.html
原版写谱文档：https://github.com/LingFeng-bbben/MajdataView/wiki/怎样写谱？
";

    private static Paragraph CreateHeading(string text, double size, Thickness margin)
    {
        var paragraph = new Paragraph(new Run(text))
        {
            FontFamily = BodyFont,
            FontSize = size,
            FontWeight = FontWeights.SemiBold,
            Margin = margin,
            TextAlignment = TextAlignment.Left,
            KeepWithNext = true
        };
        paragraph.SetResourceReference(TextElement.ForegroundProperty, "ButtonForeground");
        return paragraph;
    }

    private static Paragraph CreateParagraph(string text, Thickness margin)
    {
        var paragraph = new Paragraph()
        {
            Margin = margin,
            TextAlignment = TextAlignment.Left
        };
        var position = 0;
        foreach (System.Text.RegularExpressions.Match match in
                 System.Text.RegularExpressions.Regex.Matches(text, @"https?://\S+"))
        {
            paragraph.Inlines.Add(new Run(text.Substring(position, match.Index - position)));
            var link = new Hyperlink(new Run(match.Value)) { NavigateUri = new Uri(match.Value) };
            link.RequestNavigate += (_, args) =>
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(args.Uri.AbsoluteUri)
                    { UseShellExecute = true });
                args.Handled = true;
            };
            paragraph.Inlines.Add(link);
            position = match.Index + match.Length;
        }
        paragraph.Inlines.Add(new Run(text.Substring(position)));
        paragraph.SetResourceReference(TextElement.ForegroundProperty, "ButtonForeground");
        return paragraph;
    }

    private static Paragraph CreateLead(string text)
    {
        var paragraph = new Paragraph(new Run(text))
        {
            Padding = new Thickness(13, 10, 13, 10),
            Margin = new Thickness(0, 0, 0, 18),
            FontFamily = BodyFont,
            FontSize = 13.5,
            LineHeight = 21,
            BorderThickness = new Thickness(1)
        };
        paragraph.SetResourceReference(TextElement.ForegroundProperty, "ButtonForeground");
        paragraph.SetResourceReference(TextElement.BackgroundProperty, "EditorBackground");
        paragraph.SetResourceReference(Block.BorderBrushProperty, "MenuSeparator");
        return paragraph;
    }

    private static Paragraph CreateSectionHeading(string text)
    {
        var title = new Paragraph(new Run(text))
        {
            FontFamily = BodyFont,
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(0, 1, 0, 5),
            Margin = new Thickness(0, 18, 0, 7),
            TextAlignment = TextAlignment.Left,
            KeepWithNext = true
        };
        title.SetResourceReference(TextElement.ForegroundProperty, "HelperForeground");
        return title;
    }

    private static Paragraph CreateCodeCard(string label, string code, bool example)
    {
        var paragraph = new Paragraph
        {
            TextAlignment = TextAlignment.Left,
            FontFamily = CodeFont,
            FontSize = 13,
            LineHeight = 20,
            Padding = new Thickness(12, 8, 12, 9),
            Margin = new Thickness(0, 3, 0, 6),
            BorderThickness = new Thickness(example ? 1 : 0, 1, 1, 1)
        };
        paragraph.SetResourceReference(TextElement.ForegroundProperty, "ButtonForeground");
        paragraph.SetResourceReference(TextElement.BackgroundProperty, "EditorBackground");
        paragraph.SetResourceReference(Block.BorderBrushProperty,
            example ? "HelperForeground" : "MenuSeparator");
        var labelRun = new Run(label)
        {
            FontFamily = BodyFont,
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold
        };
        labelRun.SetResourceReference(TextElement.ForegroundProperty,
            example ? "HelperForeground" : "ButtonForeground");
        paragraph.Inlines.Add(labelRun);
        paragraph.Inlines.Add(new LineBreak());
        AddCodeRuns(paragraph, code, !example);
        return paragraph;
    }

    private static Paragraph CreateCallout(string text)
    {
        var paragraph = new Paragraph(new Run(text))
        {
            FontFamily = BodyFont,
            Padding = new Thickness(11, 8, 10, 8),
            Margin = new Thickness(0, 5, 0, 8),
            LineHeight = 20,
            BorderThickness = new Thickness(4, 0, 0, 0)
        };
        paragraph.SetResourceReference(TextElement.ForegroundProperty, "ButtonForeground");
        paragraph.SetResourceReference(TextElement.BackgroundProperty, "EditorBackground");
        paragraph.SetResourceReference(Block.BorderBrushProperty, "HelperForeground");
        return paragraph;
    }

    // An omittable argument is written "[,name]" in the help text, and the brackets
    // are notation rather than something to type, so they are not drawn: the
    // argument is simply grey and italic, which is how the completion popup has
    // always shown it. Every other bracket is part of the syntax being described -
    // a Touch Hold really is written h[duration] - and is left exactly as written.
    private static void AddCodeRuns(Paragraph paragraph, string code, bool styleOptional)
    {
        if (!styleOptional)
        {
            paragraph.Inlines.Add(new Run(code));
            return;
        }

        var start = 0;
        while (start < code.Length)
        {
            var open = code.IndexOf("[,", start, StringComparison.Ordinal);
            var close = open < 0 ? -1 : code.IndexOf(']', open);
            if (close < 0)
                break;
            if (open > start)
                paragraph.Inlines.Add(new Run(code[start..open]));
            paragraph.Inlines.Add(CreateCodeRun(code[(open + 1)..close], true));
            start = close + 1;
        }
        if (start < code.Length)
            paragraph.Inlines.Add(new Run(code[start..]));
    }

    private static Run CreateCodeRun(string text, bool optional)
    {
        var run = new Run(text);
        if (!optional)
            return run;
        var source = Application.Current?.TryFindResource("ButtonForeground") as Brush ?? Brushes.Gray;
        var brush = source.CloneCurrentValue();
        brush.Opacity = 0.48;
        run.Foreground = brush;
        run.FontStyle = FontStyles.Italic;
        return run;
    }

    private void AddNavigationButton(string title, Block target)
    {
        sections.Add((title, target));
        var button = new Button
        {
            Content = title,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(9, 6, 7, 6),
            Margin = new Thickness(0, 1, 0, 1),
            FontFamily = BodyFont,
            FontSize = 12.5
        };
        button.SetResourceReference(Control.TemplateProperty, "DarkButton");
        button.SetResourceReference(Control.ForegroundProperty, "ButtonForeground");
        button.SetResourceReference(Control.BackgroundProperty, "ButtonsBackground");
        button.Click += (_, _) => SelectChapter(sections.FindIndex(s => s.Target == target));
        NavigationPanel.Children.Add(button);
    }
}
