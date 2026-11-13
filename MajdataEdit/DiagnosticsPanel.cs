using System.Windows;
using System.Windows.Controls;
using MajdataSessionUi;

namespace MajdataEdit;

internal sealed record ChartDiagnostic(int Column, int Line, string Message, string Category = "");

internal sealed class DiagnosticsPanel : Window
{
    private readonly ComboBox category = new();
    private readonly StackPanel entries = new();
    private readonly Func<int, IEnumerable<ChartDiagnostic>> read;
    private readonly Action<int, int> locate;
    private int shownCategory = -1;
    private ChartDiagnostic[] shownItems = Array.Empty<ChartDiagnostic>();

    public DiagnosticsPanel(Func<int, IEnumerable<ChartDiagnostic>> read, Action<int, int> locate)
    {
        this.read = read; this.locate = locate;
        Title = "谱面诊断";
        SessionTheme.Apply(this, 360, 500);
        var root = new DockPanel { Margin = new Thickness(12) };
        category.ItemsSource = new[] { "语法错误", "静态无理", "动态无理" };
        SessionTheme.Input(category); category.Margin = new Thickness(0, 0, 0, 10);
        category.SelectionChanged += (_, _) => Refresh();
        DockPanel.SetDock(category, Dock.Top); root.Children.Add(category);
        root.Children.Add(new ScrollViewer { Content = entries, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
    }

    public void Select(int index) { category.SelectedIndex = index; Refresh(); }
    public void Refresh()
    {
        if (category.SelectedIndex < 0) return;
        var items = read(category.SelectedIndex).ToArray();
        if (shownCategory == category.SelectedIndex && items.SequenceEqual(shownItems)) return;
        shownCategory = category.SelectedIndex;
        shownItems = items;
        entries.Children.Clear();
        if (items.Length == 0)
        {
            entries.Children.Add(SessionTheme.Text(category.SelectedIndex == 2 ? "0 项 · 播放 DJAuto 后显示判定异常位置。" : "0 项 · 未发现问题。", true));
            return;
        }
        foreach (var group in items.GroupBy(item => item.Category))
        {
            if (category.SelectedIndex == 0)
            {
                var description = group.Key switch {
                    "指令参数" => "Alpha 命令、参数和类型格式",
                    "预览载入" => "View 回报的轨迹、素材和资源载入问题",
                    _ => "音符、轨迹、时长、分音及分隔结构"
                };
                var heading = SessionTheme.Text($"{group.Key} · {group.Count()}\n{description}");
                heading.Margin = new Thickness(0, 4, 0, 8);
                entries.Children.Add(heading);
            }
            foreach (var item in group)
            {
                var text = SessionTheme.Text($"第 {item.Line + 1} 行，第 {item.Column + 1} 列\n{item.Message}");
                text.TextAlignment = TextAlignment.Center;
                var button = SessionTheme.Button("");
                button.Content = text; button.Height = double.NaN; button.Padding = new Thickness(9);
                button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                button.Margin = new Thickness(0, 0, 0, 8);
                button.Click += (_, _) => locate(item.Column, item.Line);
                entries.Children.Add(button);
            }
        }
    }
}
