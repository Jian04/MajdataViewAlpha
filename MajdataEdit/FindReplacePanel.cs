using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using MajdataSessionUi;

namespace MajdataEdit;

internal sealed class FindReplacePanel : Window
{
    private readonly TextEditor editor;
    private readonly TextBox query = new();
    private readonly TextBox replacement = new();
    private readonly ListBox results = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    private readonly TextBlock count = SessionTheme.Text("");
    private readonly Button replace = SessionTheme.Button("替换选中");
    private readonly Button replaceAll = SessionTheme.Button("全部替换");
    private readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private string source = "";
    private string searched = "";
    private List<Match> matches = new();

    internal FindReplacePanel(TextEditor editor)
    {
        this.editor = editor;
        Title = "查找和替换";
        SessionTheme.Apply(this, 360, 560);
        var body = new Grid { Margin = new Thickness(12) };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition());
        var fields = new StackPanel();
        SessionTheme.Field(fields, "查找", query);
        SessionTheme.Field(fields, "替换为", replacement);
        var actions = new WrapPanel();
        replace.Margin = new Thickness(0, 0, 8, 0);
        replaceAll.Margin = new Thickness(0);
        actions.Children.Add(replace); actions.Children.Add(replaceAll);
        fields.Children.Add(actions);
        count.Margin = new Thickness(0, 12, 0, 8);
        count.SetResourceReference(TextBlock.ForegroundProperty, "MutedForeground");
        fields.Children.Add(count); body.Children.Add(fields);

        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(Match.Display)));
        text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        text.SetValue(MarginProperty, new Thickness(4, 6, 4, 6));
        text.SetResourceReference(TextBlock.ForegroundProperty, "ButtonForeground");
        results.ItemTemplate = new DataTemplate { VisualTree = text };
        results.SetResourceReference(ForegroundProperty, "ButtonForeground");
        VirtualizingPanel.SetVirtualizationMode(results, VirtualizationMode.Recycling);
        Grid.SetRow(results, 1); body.Children.Add(results); Content = body;

        query.TextChanged += (_, _) => ScheduleRefresh();
        query.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            RefreshResults();
            if (results.Items.Count > 0) { results.SelectedIndex = 0; JumpToResult(); }
            e.Handled = true;
        };
        editor.TextChanged += EditorTextChanged;
        refresh.Tick += (_, _) => RefreshResults();
        results.SelectionChanged += (_, _) => UpdateButtons();
        results.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (ItemsControl.ContainerFromElement(results, e.OriginalSource as DependencyObject) is ListBoxItem)
                JumpToResult();
        };
        results.KeyDown += (_, e) => { if (e.Key == Key.Enter) { JumpToResult(); e.Handled = true; } };
        replace.Click += (_, _) => ReplaceSelected();
        replaceAll.Click += (_, _) => ReplaceAll();
        Closed += (_, _) => { refresh.Stop(); editor.TextChanged -= EditorTextChanged; };
        RefreshResults();
    }

    internal void FocusQuery()
    {
        query.Focus(); query.SelectAll();
    }

    private void EditorTextChanged(object? sender, EventArgs e) => ScheduleRefresh();

    private void ScheduleRefresh()
    {
        refresh.Stop(); refresh.Start();
    }

    private void RefreshResults()
    {
        refresh.Stop();
        source = editor.Document.Text;
        searched = query.Text;
        matches = new List<Match>();
        if (searched.Length > 0)
        {
            for (var offset = source.IndexOf(searched, StringComparison.CurrentCultureIgnoreCase); offset >= 0;
                 offset = source.IndexOf(searched, offset + searched.Length, StringComparison.CurrentCultureIgnoreCase))
            {
                var line = editor.Document.GetLineByOffset(offset);
                var column = offset - line.Offset;
                var excerptStart = Math.Max(0, column - 24);
                var excerptLength = Math.Min(line.Length - excerptStart, 160);
                var excerpt = editor.Document.GetText(line.Offset + excerptStart, excerptLength);
                matches.Add(new Match(offset, searched.Length,
                    $"{line.LineNumber}:{column + 1}  {(excerptStart > 0 ? "…" : "")}{excerpt}"));
            }
        }
        results.ItemsSource = matches;
        count.Text = searched.Length == 0 ? "" : $"{matches.Count} 个结果";
        UpdateButtons();
    }

    private bool ResultsAreCurrent()
    {
        if (source == editor.Document.Text && searched == query.Text) return true;
        RefreshResults();
        return false;
    }

    private void JumpToResult()
    {
        if (!ResultsAreCurrent() || results.SelectedItem is not Match match) return;
        editor.Select(match.Offset, match.Length);
        editor.ScrollToLine(editor.Document.GetLineByOffset(match.Offset).LineNumber);
        editor.Focus();
    }

    private void ReplaceSelected()
    {
        if (!ResultsAreCurrent() || results.SelectedItem is not Match match || editor.IsReadOnly) return;
        editor.Document.Replace(match.Offset, match.Length, replacement.Text);
        RefreshResults();
    }

    private void ReplaceAll()
    {
        if (!ResultsAreCurrent() || editor.IsReadOnly || matches.Count == 0) return;
        using (editor.Document.RunUpdate())
            for (var index = matches.Count - 1; index >= 0; index--)
                editor.Document.Replace(matches[index].Offset, matches[index].Length, replacement.Text);
        RefreshResults();
    }

    private void UpdateButtons()
    {
        replace.IsEnabled = !editor.IsReadOnly && results.SelectedItem is Match;
        replaceAll.IsEnabled = !editor.IsReadOnly && matches.Count > 0;
    }

    private sealed record Match(int Offset, int Length, string Display);
}
