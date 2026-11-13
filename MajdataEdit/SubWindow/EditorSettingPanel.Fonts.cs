using System.Windows;
using System.Windows.Controls;
using MajdataEdit.Editor;

namespace MajdataEdit;

public partial class EditorSettingPanel
{
    private const int BuiltinFontCount = 11;
    private static string SelectedFontFile(ComboBox box) =>
        (box.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

    private void LoadFontChoices(string editorFile, string viewFile)
    {
        var choices = FontLibrary.ReadChoices(out var errors);
        if (errors.Count > 0) MessageBox.Show(this, string.Join("\n", errors), "无法读取字体",
            MessageBoxButton.OK, MessageBoxImage.Error);
        void Fill(ComboBox box, string selected)
        {
            var previous = box.SelectedIndex;
            while (box.Items.Count > BuiltinFontCount) box.Items.RemoveAt(box.Items.Count - 1);
            box.SelectedIndex = previous >= 0 && previous < BuiltinFontCount ? previous : 0;
            foreach (var choice in choices)
            {
                var item = new ComboBoxItem { Content = choice.DisplayName, Tag = choice.FileName, ToolTip = choice.FileName };
                box.Items.Add(item);
                if (choice.FileName.Equals(selected, StringComparison.OrdinalIgnoreCase)) box.SelectedItem = item;
            }
        }
        Fill(EditorFontPreset, editorFile);
        Fill(ViewDisplayFont, viewFile);
    }

}
