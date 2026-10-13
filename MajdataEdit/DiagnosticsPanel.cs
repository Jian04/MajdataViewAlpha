using System.Windows;
using System.Windows.Controls;
using MajdataSessionUi;

namespace MajdataEdit;

internal sealed record ChartDiagnostic(int Column, int Line, string Message);

internal sealed class DiagnosticsPanel : Window
{
    private readonly ComboBox category = new();
    private readonly StackPanel entries = new();
    private readonly Func<int, IEnumerable<ChartDiagnostic>> read;
    private readonly Action<int, int> locate;
    private string signature = "";

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

    public void Select(int index) { category.SelectedIndex = index; signature = ""; Refresh(); }
    public void Refresh()
    {
        if (category.SelectedIndex < 0) return;
        var items = read(category.SelectedIndex).ToArray();
        var next = category.SelectedIndex + "|" + string.Join("|", items.Select(x => x.ToString()));
        if (next == signature) return;
        signature = next; entries.Children.Clear();
        if (items.Length == 0)
        {
            entries.Children.Add(SessionTheme.Text(category.SelectedIndex == 2 ? "0 项 · 播放 DJAuto 后显示判定异常位置。" : "0 项 · 未发现问题。", true));
            return;
        }
        foreach (var item in items)
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
