using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MajdataSessionUi;

namespace MajdataEdit;

internal sealed class ShortcutSettingsPanel : Window
{
    private static string DisplayGesture(string value)
    {
        var parts = value.Split('+');
        for (var i = 0; i < parts.Length; i++)
        {
            parts[i] = parts[i] switch
            {
                "OemSemicolon" => ";",
                "OemQuotes" => "'",
                "OemComma" => ",",
                "OemPeriod" => ".",
                "OemQuestion" => "/",
                "OemOpenBrackets" => "[",
                "OemCloseBrackets" => "]",
                "OemMinus" => "-",
                "OemPlus" => "=",
                "OemTilde" => "`",
                "Back" => "Backspace",
                var other => other
            };
        }
        return string.Join("+", parts);
    }

    private static readonly (string Field, string Name)[] Shortcuts =
    {
        ("PlayPauseKey", "播放／暂停"), ("PlayStopKey", "终止播放"),
        ("SaveKey", "保存谱面"), ("SendViewerKey", "发送到 View"),
        ("IncreasePlaybackSpeedKey", "加快播放"), ("DecreasePlaybackSpeedKey", "减慢播放"),
        ("FindKey", "查找"), ("MirrorLeftRightKey", "左右镜像"),
        ("MirrorUpDownKey", "上下镜像"), ("Mirror180Key", "旋转 180°"),
        ("Mirror45Key", "顺时针旋转"), ("MirrorCcw45Key", "逆时针旋转"),
        ("SidebarKey", "显示／隐藏侧栏"), ("MoveCaretUpKey", "光标上移一行"),
        ("MoveCaretDownKey", "光标下移一行"), ("OverwriteKey", "切换覆盖输入"),
        ("MediaDeleteKey", "剪辑：删除片段"), ("MediaDeleteAlternateKey", "剪辑：删除片段（备用）"),
        ("MediaCopyKey", "剪辑：复制片段"),
        ("MediaPasteKey", "剪辑：粘贴片段"), ("MediaUndoKey", "剪辑：撤销"),
        ("MediaRedoKey", "剪辑：重做"), ("MediaSplitKey", "剪辑：切割片段")
    };
    private readonly MainWindow main;
    internal ShortcutSettingsPanel(MainWindow main)
    {
        this.main = main;
        Title = "快捷键设置";
        SessionTheme.Apply(this, 410, 560);
        var rows = new StackPanel { Margin = new Thickness(12) };
        var inputs = new Dictionary<string, TextBox>();
        var defaults = new EditorSetting();
        void Save()
        {
            main.SaveEditorSetting();
            main.RefreshShortcutBindings();
        }
        foreach (var (fieldName, label) in Shortcuts)
        {
            var field = typeof(EditorSetting).GetField(fieldName, BindingFlags.Public | BindingFlags.Instance)!;
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            var caption = SessionTheme.Text(label);
            caption.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(caption);
            var input = new TextBox { Text = DisplayGesture((string?)field.GetValue(main.editorSetting) ?? ""), IsReadOnly = true,
                Cursor = Cursors.Hand, ToolTip = "点击后按下新快捷键；Esc 取消" };
            SessionTheme.Input(input);
            Grid.SetColumn(input, 1); row.Children.Add(input);
            inputs[fieldName] = input;
            input.GotKeyboardFocus += (_, _) =>
            {
                input.Text = "按键…";
                input.BorderBrush = Brushes.DodgerBlue;
                input.BorderThickness = new Thickness(2);
            };
            input.LostKeyboardFocus += (_, _) =>
            {
                input.Text = DisplayGesture((string?)field.GetValue(main.editorSetting) ?? "");
                input.ClearValue(Control.BorderBrushProperty);
                input.ClearValue(Control.BorderThicknessProperty);
            };
            input.PreviewKeyDown += (_, args) =>
            {
                var key = args.Key == Key.System ? args.SystemKey : args.Key;
                args.Handled = true;
                if (key == Key.Escape) { Keyboard.ClearFocus(); return; }
                if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
                try
                {
                    var value = Keyboard.Modifiers == ModifierKeys.None ? key.ToString() :
                        new KeyGestureConverter().ConvertToString(new KeyGesture(key, Keyboard.Modifiers)) ?? "";
                    if (Shortcuts.Any(item => item.Field != fieldName &&
                        string.Equals((string?)typeof(EditorSetting).GetField(item.Field)!.GetValue(main.editorSetting),
                            value, StringComparison.OrdinalIgnoreCase)))
                    { MessageBox.Show(this, "这个快捷键已被其他功能使用。", "快捷键冲突"); return; }
                    field.SetValue(main.editorSetting, value);
                    Save();
                    Keyboard.ClearFocus();
                }
                catch (Exception error) when (error is ArgumentException or NotSupportedException)
                { MessageBox.Show(this, "这个快捷键不能使用，请换一个。", "快捷键无效"); }
            };
            var reset = new Button { Content = "重置", Width = 44, Margin = new Thickness(4, 0, 0, 0) };
            reset.SetResourceReference(Control.TemplateProperty, "DarkButton");
            reset.Click += (_, _) =>
            {
                var value = (string?)field.GetValue(defaults) ?? "";
                foreach (var (otherField, _) in Shortcuts)
                {
                    if (otherField == fieldName) continue;
                    var other = typeof(EditorSetting).GetField(otherField)!;
                    if (!string.Equals((string?)other.GetValue(main.editorSetting), value, StringComparison.OrdinalIgnoreCase)) continue;
                    other.SetValue(main.editorSetting, (string?)field.GetValue(main.editorSetting) ?? "");
                    inputs[otherField].Text = DisplayGesture((string?)other.GetValue(main.editorSetting) ?? "");
                    break;
                }
                field.SetValue(main.editorSetting, value);
                input.Text = DisplayGesture(value);
                Save();
                Keyboard.ClearFocus();
            };
            Grid.SetColumn(reset, 2); row.Children.Add(reset);
            rows.Children.Add(row);
        }
        var resetAll = new Button { Content = "全部恢复默认", Margin = new Thickness(0, 8, 0, 0), Width = 114,
            HorizontalAlignment = HorizontalAlignment.Right };
        resetAll.SetResourceReference(Control.TemplateProperty, "DarkButton");
        resetAll.Click += (_, _) =>
        {
            foreach (var (fieldName, _) in Shortcuts)
            {
                var field = typeof(EditorSetting).GetField(fieldName)!;
                var value = (string?)field.GetValue(defaults) ?? "";
                field.SetValue(main.editorSetting, value);
                inputs[fieldName].Text = DisplayGesture(value);
            }
            Save();
            Keyboard.ClearFocus();
        };
        rows.Children.Add(resetAll);
        Content = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }
}
