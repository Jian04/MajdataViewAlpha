using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WPFLocalizeExtension.Engine;

namespace MajdataEdit;

/// <summary>
///     BPMtap.xaml 的交互逻辑
/// </summary>
public partial class EditorSettingPanel : Window
{
    private readonly bool dialogMode;
    private readonly string[] langList = new string[3] { "zh-CN", "en-US", "ja" }; // 语言列表
    private bool saveFlag;

    public EditorSettingPanel(bool _dialogMode = false)
    {
        dialogMode = _dialogMode;
        InitializeComponent();

        if (dialogMode) Cancel_Button.IsEnabled = false;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var window = (MainWindow)Owner;

        var curLang = window.editorSetting!.Language;
        var boxIndex = -1;
        for (var i = 0; i < langList.Length; i++)
            if (curLang == langList[i])
            {
                boxIndex = i;
                break;
            }

        if (boxIndex == -1)
            // 如果没有语言设置 或者语言未知 就自动切换到English
            boxIndex = 1;

        LanguageComboBox.SelectedIndex = boxIndex;

        RenderModeComboBox.SelectedIndex = window.editorSetting.RenderMode;
        LoadSkinChoices(window.editorSetting.Skin);
        LoadThemeChoices(window.editorSetting.EditorTheme);

        InnerViewerCover.Text = window.editorSetting.InnerBackgroundCover.ToString();
        OuterViewerCover.Text = window.editorSetting.OuterBackgroundCover.ToString();
        EditorFontPreset.SelectedIndex = Math.Clamp(window.editorSetting.EditorFontPreset, 0, EditorFontPreset.Items.Count - 1);
        SongDetailStyle.SelectedIndex = Math.Clamp(window.editorSetting.SongDetailStyle, 0, 1);
        EditorLightTheme.IsChecked = false;
        ComboDisplay.SelectedIndex = Array.IndexOf(
            Enum.GetValues(window.editorSetting.comboStatusType.GetType()),
            window.editorSetting.comboStatusType
        );
        if (ComboDisplay.SelectedIndex < 0)
            ComboDisplay.SelectedIndex = 0;

        PlayMethod.SelectedIndex = Array.IndexOf(
            Enum.GetValues(window.editorSetting.editorPlayMethod.GetType()),
            window.editorSetting.editorPlayMethod
        );
        if(PlayMethod.SelectedIndex < 0) 
            PlayMethod.SelectedIndex = 0;

        ChartRefreshDelay.Text = window.editorSetting.ChartRefreshDelay.ToString();
        AutoUpdate.IsChecked = window.editorSetting.AutoCheckUpdate;
        SmoothSlideAnime.IsChecked = window.editorSetting.SmoothSlideAnime;
        ShowJudgeInfo.IsChecked = window.editorSetting.ShowJudgeInfo;
        ShowComboInfo.IsChecked = window.editorSetting.ShowComboInfo;
        ShowJudgeLine.IsChecked = window.editorSetting.ShowJudgeLine;
        ShowJudgeText.IsChecked = window.editorSetting.ShowJudgeText;
        ShowAllPerfect.IsChecked = window.editorSetting.ShowAllPerfect;
        SyntaxCheckLevel.SelectedIndex = window.editorSetting.SyntaxCheckLevel;
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        //LanguageComboBox.SelectedIndex
        LocalizeDictionary.Instance.Culture = new CultureInfo(langList[LanguageComboBox.SelectedIndex]);
    }

    private void RenderModeComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RenderOptions.ProcessRenderMode =
            RenderModeComboBox.SelectedIndex == 0 ? RenderMode.Default : RenderMode.SoftwareOnly;
    }

    private void ViewerCover_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var textBox = (TextBox)sender;
        var offset = float.Parse(textBox.Text);
        offset += e.Delta > 0 ? 0.1f : -0.1f;
        textBox.Text = Math.Clamp(offset, 0f, 1f).ToString("0.0");
    }

    private void Save_Button_Click(object sender, RoutedEventArgs e)
    {
        var window = (MainWindow)Owner;
        window.editorSetting!.Language = langList[LanguageComboBox.SelectedIndex];
        window.editorSetting!.RenderMode = RenderModeComboBox.SelectedIndex;
        window.editorSetting!.Skin = SkinComboBox.SelectedItem?.ToString() ?? "dx";
        window.editorSetting!.EditorTheme = (ThemeComboBox.SelectedItem as EditorTheme)?.name ?? ThemeManager.DefaultTheme;
        window.editorSetting!.InnerBackgroundCover = Math.Clamp(float.Parse(InnerViewerCover.Text), 0f, 1f);
        window.editorSetting!.OuterBackgroundCover = Math.Clamp(float.Parse(OuterViewerCover.Text), 0f, 1f);
        window.editorSetting!.backgroundCover = window.editorSetting.InnerBackgroundCover;
        window.editorSetting!.EditorFontPreset = Math.Clamp(EditorFontPreset.SelectedIndex, 0, EditorFontPreset.Items.Count - 1);
        window.editorSetting!.SongDetailStyle = Math.Clamp(SongDetailStyle.SelectedIndex, 0, 1);
        window.editorSetting!.EditorLightTheme = false;
        window.editorSetting!.ChartRefreshDelay = int.Parse(ChartRefreshDelay.Text);
        window.editorSetting!.AutoCheckUpdate = (bool) AutoUpdate.IsChecked!;
        window.editorSetting!.SmoothSlideAnime = (bool) SmoothSlideAnime.IsChecked!;
        window.editorSetting!.ShowJudgeInfo = ShowJudgeInfo.IsChecked == true;
        window.editorSetting!.ShowComboInfo = ShowComboInfo.IsChecked == true;
        window.editorSetting!.ShowJudgeLine = ShowJudgeLine.IsChecked == true;
        window.editorSetting!.ShowJudgeText = ShowJudgeText.IsChecked == true;
        window.editorSetting!.ShowAllPerfect = ShowAllPerfect.IsChecked == true;
        window.editorSetting!.editorPlayMethod = (EditorPlayMethod)PlayMethod.SelectedIndex;
        window.editorSetting!.SyntaxCheckLevel = SyntaxCheckLevel.SelectedIndex;
        // window.editorSetting.isComboEnabled = (bool) ComboDisplay.IsChecked!;
        window.editorSetting!.comboStatusType = (EditorComboIndicator)Enum.GetValues(
            window.editorSetting!.comboStatusType.GetType()
        ).GetValue(ComboDisplay.SelectedIndex)!;
        window.SaveEditorSetting();

        window.ViewerSpeed.Content = window.editorSetting.playSpeed.ToString("F1"); // 转化为形如"7.0", "9.5"这样的速度
        window.ViewerTouchSpeed.Content = window.editorSetting.touchSpeed.ToString("F1");
        window.chartChangeTimer.Interval = window.editorSetting.ChartRefreshDelay;
        window.ApplyEditorAppearance();
        window.RefreshWaveNoteSkin();
        window.SendDisplaySettings();


        saveFlag = true;
        window.SyntaxCheck();
        Close();
    }

    private void Cancel_Button_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void LoadSkinChoices(string selectedSkin)
    {
        SkinComboBox.Items.Clear();
        var skinRoot = FindSkinRoot();
        if (Directory.Exists(skinRoot))
        {
            foreach (var directory in Directory.GetDirectories(skinRoot)
                         .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
                SkinComboBox.Items.Add(Path.GetFileName(directory));
        }

        if (SkinComboBox.Items.Count == 0)
            SkinComboBox.Items.Add("dx");

        SkinComboBox.SelectedItem = selectedSkin;
        if (SkinComboBox.SelectedIndex < 0)
            SkinComboBox.SelectedIndex = 0;
    }

    private void LoadThemeChoices(string selectedTheme)
    {
        ThemeComboBox.Items.Clear();
        foreach (var theme in ThemeManager.LoadThemes())
            ThemeComboBox.Items.Add(theme);

        for (var i = 0; i < ThemeComboBox.Items.Count; i++)
        {
            if (ThemeComboBox.Items[i] is EditorTheme theme &&
                string.Equals(theme.name, selectedTheme, StringComparison.OrdinalIgnoreCase))
            {
                ThemeComboBox.SelectedIndex = i;
                break;
            }
        }

        if (ThemeComboBox.SelectedIndex < 0 && ThemeComboBox.Items.Count > 0)
            ThemeComboBox.SelectedIndex = 0;
    }

    private static string FindSkinRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            for (var depth = 0; directory != null && depth < 6; depth++, directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName, "Skin");
                if (Directory.Exists(candidate))
                    return candidate;
            }
        }

        return Path.Combine(Environment.CurrentDirectory, "Skin");
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (!saveFlag)
        {
            // 取消或直接关闭窗口
            if (dialogMode)
            {
                // 模态窗口状态下 则阻止关闭
                e.Cancel = true;
                MessageBox.Show(MainWindow.GetLocalizedString("NoEditorSetting"),
                    MainWindow.GetLocalizedString("Error"));
            }
            else
            {
                LocalizeDictionary.Instance.Culture = new CultureInfo(((MainWindow)Owner).editorSetting!.Language);
            }
        }
        else
        {
            if (dialogMode) DialogResult = true;
        }
    }
}
