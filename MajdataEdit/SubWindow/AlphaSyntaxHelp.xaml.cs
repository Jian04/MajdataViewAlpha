using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace MajdataEdit;

public partial class AlphaSyntaxHelp : Window
{
    private static readonly FontFamily BodyFont =
        new("Segoe UI, Microsoft YaHei UI, Yu Gothic UI");
    private static readonly FontFamily CodeFont =
        new("Cascadia Mono, Cascadia Code, Consolas, Microsoft YaHei UI");

    public AlphaSyntaxHelp()
    {
        InitializeComponent();
        BuildDocument(MainWindow.GetLocalizedString("AlphaHelpStructuredText"));
    }

    private void BuildDocument(string source)
    {
        NavigationPanel.Children.Clear();
        var document = new FlowDocument
        {
            PagePadding = new Thickness(28, 24, 32, 36),
            ColumnWidth = double.PositiveInfinity,
            FontFamily = BodyFont,
            FontSize = 13.5,
            LineHeight = 21
        };
        document.SetResourceReference(TextElement.ForegroundProperty, "ButtonForeground");

        var lines = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
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
    }

    private static Paragraph CreateHeading(string text, double size, Thickness margin)
    {
        var paragraph = new Paragraph(new Run(text))
        {
            FontFamily = BodyFont,
            FontSize = size,
            FontWeight = FontWeights.SemiBold,
            Margin = margin,
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
            KeepWithNext = true
        };
        title.SetResourceReference(TextElement.ForegroundProperty, "HelperForeground");
        return title;
    }

    private static Paragraph CreateCodeCard(string label, string code, bool example)
    {
        var paragraph = new Paragraph
        {
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

    private static void AddCodeRuns(Paragraph paragraph, string code, bool styleOptional)
    {
        if (!styleOptional || !code.Contains('['))
        {
            paragraph.Inlines.Add(new Run(code));
            return;
        }

        var start = 0;
        var optionalDepth = 0;
        for (var index = 0; index <= code.Length; index++)
        {
            var boundary = index == code.Length || code[index] is '[' or ']';
            if (!boundary)
                continue;
            if (index > start)
                paragraph.Inlines.Add(CreateCodeRun(code[start..index], optionalDepth > 0));
            if (index == code.Length)
                break;

            var opening = code[index] == '[';
            if (opening)
                optionalDepth++;
            paragraph.Inlines.Add(CreateCodeRun(code[index].ToString(), true));
            if (!opening)
                optionalDepth = Math.Max(0, optionalDepth - 1);
            start = index + 1;
        }
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
        button.Click += (_, _) => target.BringIntoView();
        NavigationPanel.Children.Add(button);
    }
}
