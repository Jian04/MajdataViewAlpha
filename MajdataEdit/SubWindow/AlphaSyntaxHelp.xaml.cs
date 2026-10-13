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
    private static readonly FontFamily BodyFont =
        new("Segoe UI, Microsoft YaHei UI, Yu Gothic UI");
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

    // Paraphrased from the original MajdataView charting wiki and checked against
    // this project's parser. Keep Alpha-only commands in the separate Alpha guide.
    private const string SimaiGuide = @"
# Simai 语法帮助
> 从原版 MajdataView《怎样写谱？》整理。每个逗号是一个时间槽；先从 BPM、分拍和基本音符写起。
## 文件与谱面结构
谱面文件通常是歌曲文件夹内的 maidata.txt。同文件夹放 track.mp3 或 track.ogg；曲名、作者和各难度分别写在元数据字段中。
S &title=曲名
E &artist=曲师
E &des_5=谱师
E &lv_5=14+
E &inote_5=(190){16}1,2,3,4,
数字 1–8 是从右上角 1 开始顺时针排列的外圈按键。不同难度使用对应的 &inote_编号。
## BPM、分拍与休止
圆括号写 BPM，花括号写分拍。逗号推进一个槽位，空槽即休止；可以在谱面中途再改变 BPM 或分拍。
S (BPM){分拍}音符,音符,
E (180){16}1,2,3,4,
E (180){4},,,,
## Tap、Break 与 EX
外圈数字是 Tap；b 表示 Break；x 表示 EX。一个槽位写完要用英文逗号结束。
S 1,2b,3x,4bx,
## Hold
h 是 Hold；方括号给出持续时间。[4:1] 表示当前 BPM 下一个四分音符。也可以用 ## 写秒数。
S 1h[4:1],2bh[8:1],
E 1h[##2.5],
## Slide 基本形状
Slide 由起点、路径、终点、持续时间组成。起点星星与轨迹是同一个音符。
S 1-5[8:1],
- -：直线；<、>、^：沿外圈；v：经过中央折线。
- p、q：绕内圈；pp、qq：绕较大圆；s、z：闪电形。
- w：扇形；V：指定转折点，例如 1V36[8:1]。
E 1>5[4:1],2q6[8:3],1V36[8:1],
! 每种轨迹有合法终点范围；输入后以预览和语法错误提示为准。
## Slide 组合与时间
星号连接同一起点的多条 Slide；连续路径可以写在一个音符中。每段可单独指定时间。
S 1-3[8:1]*-5[8:1],
E 1>5-8[1:1],
E 1>5[4:1]-8[8:1],
Break Slide 在时长方括号前加 b，例如 1-4b[4:1]。
## 进阶 Slide：无头、星星与启动时间
在起点后写 ? 或 ! 可以去掉需要击打的星星头；? 保留星星渐入，! 不渐入。只想显示星星而不写轨迹时，在数字后加 $。
S 1?-5[8:1],1!-5[8:1],1$,
E 2?-6[4:1],3!-7[4:1],
Slide 时长括号的前半段可以设置星星击打后等待多久再启动，## 后半段是移动时长；未指定等待时间时默认等当前 BPM 的一拍。
S 1-5[等待秒数##移动秒数],
E 1-5[0.2##0.8],
E 1-5[190#8:1],
! 多条同起点 Slide 的星星只击打一次；无头轨迹也不额外计入一个 Tap。
## Touch 与 TouchHold
A、B、D、E 为触摸区，C 是中心。f 给 Touch 加烟花；Ch 是中心 TouchHold。
S B1,C,D3f,Ch[4:1],
## 同时音符与顺序
同槽位用 / 连接多个音符，例如 1/2。两个简单 Tap 也可写 12。反引号表示相差极短时间的伪双押。
S 1/2,1b/3,1-5[8:1]/2,
E 1`2,
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
        var paragraph = new Paragraph(new Run(text))
        {
            Margin = margin,
            TextAlignment = TextAlignment.Left
        };
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
