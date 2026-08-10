using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WPFLocalizeExtension.Engine;

namespace MajdataEdit;

/// <summary>
///     Interaction logic for BPMtap.xaml
/// </summary>
public partial class EditorSettingPanel : Window
{
    private sealed record SkinGroupOption(string Key, string DisplayName, string PreviewFile);

    private sealed class SkinGroupRow
    {
        public Image Preview { get; init; } = null!;
        public TextBlock Selection { get; init; } = null!;
        public string SelectedSkin { get; set; } = "dx";
    }

    private static readonly SkinGroupOption[] SkinGroups =
    {
        new("tap", "Tap", "tap.png"),
        new("hold", "Hold", "hold.png"),
        new("star", "Star", "star.png")
    };

    private readonly bool dialogMode;
    private readonly string[] langList = new string[3] { "zh-CN", "en-US", "ja" }; // Language list
    private readonly List<string> skinChoices = new();
    private readonly Dictionary<string, SkinGroupRow> skinGroupRows = new(StringComparer.OrdinalIgnoreCase);
    private TextBlock? starColorSelection;
    private Image? starColorPreview;
    private bool pinkStar;
    private bool settingsApplied;

    public EditorSettingPanel(bool _dialogMode = false)
    {
        dialogMode = _dialogMode;
        InitializeComponent();
        BuildSkinGroupRows();
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
            // Switch to English when the language setting is missing or unknown.
            boxIndex = 1;

        LanguageComboBox.SelectedIndex = boxIndex;

        RenderModeComboBox.SelectedIndex = window.editorSetting.RenderMode;
        LoadSkinChoices(window.editorSetting.Skin);
        pinkStar = window.editorSetting.PinkStar;
        UpdateStarColorSelection();
        SetSkinGroup("tap", window.editorSetting.TapSkin, window.editorSetting.Skin);
        SetSkinGroup("hold", window.editorSetting.HoldSkin, window.editorSetting.Skin);
        SetSkinGroup("star", window.editorSetting.StarSkin, window.editorSetting.Skin);
        LoadThemeChoices(window.editorSetting.EditorTheme);

        InnerViewerCover.Text = window.editorSetting.InnerBackgroundCover.ToString();
        OuterViewerCover.Text = window.editorSetting.OuterBackgroundCover.ToString();
        BackgroundFitMode.SelectedIndex = Math.Clamp(window.editorSetting.BackgroundFitMode, 0, 1);
        EditorFontPreset.SelectedIndex = Math.Clamp(window.editorSetting.EditorFontPreset, 0, EditorFontPreset.Items.Count - 1);
        EditorFontSize.Text = window.editorSetting.FontSize.ToString("0.#", CultureInfo.InvariantCulture);
        ViewDisplayFont.SelectedIndex = Math.Clamp(window.editorSetting.ViewDisplayFontPreset, 0,
            ViewDisplayFont.Items.Count - 1);
        SongDetailStyle.SelectedIndex = Math.Clamp(window.editorSetting.SongDetailStyle, 0, 1);
        EditorBackgroundStyle.SelectedIndex = StyleToIndex(window.editorSetting.EditorBackgroundStyle);
        ViewIntroStyle.SelectedIndex = StyleToIndex(window.editorSetting.ViewIntroStyle);
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
        SmoothSlideAnime.IsChecked = window.editorSetting.SmoothSlideAnime;
        ShowJudgeInfo.IsChecked = window.editorSetting.ShowJudgeInfo;
        ShowComboInfo.IsChecked = window.editorSetting.ShowComboInfo;
        ShowJudgeLine.IsChecked = window.editorSetting.ShowJudgeLine;
        ShowJudgeText.IsChecked = window.editorSetting.ShowJudgeText;
        ShowJudgeArea.IsChecked = window.editorSetting.ShowJudgeArea;
        UpdateJudgeAreaEnabled();
        ShowSongDetail.IsChecked = window.editorSetting.ShowSongDetail;
        ShowAllPerfect.IsChecked = window.editorSetting.ShowAllPerfect;
        ShowGeneratedMark.IsChecked = window.editorSetting.ShowGeneratedMark;
        SyntaxCheckLevel.SelectedIndex = window.editorSetting.SyntaxCheckLevel;
        ViewerSpeed.Text = window.editorSetting.playSpeed.ToString("F1");
        ViewerTouchSpeed.Text = window.editorSetting.touchSpeed.ToString("F1");
        StarSpeed.Text = window.editorSetting.starSpeed.ToString("F1");
    }

    // The judgment area depends on the judgment line; hiding the line disables the checkbox and forces the area off (see MainWindowCore).
    private void ShowJudgeLine_Toggled(object sender, RoutedEventArgs e)
    {
        UpdateJudgeAreaEnabled();
    }

    private void UpdateJudgeAreaEnabled()
    {
        if (ShowJudgeArea == null)
            return;
        ShowJudgeArea.IsEnabled = ShowJudgeLine.IsChecked == true;
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

    private static void NormalizeNumber(TextBox textBox, float fallback, float min, float max)
    {
        if (!float.TryParse(textBox.Text, out var value))
            value = fallback;
        value = Math.Clamp(value, min, max);
        textBox.Text = value.ToString("F1");
    }

    private void SpeedTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender == ViewerSpeed)
            NormalizeNumber(ViewerSpeed, ((MainWindow)Owner).editorSetting!.playSpeed, 1f, 10f);
        else if (sender == ViewerTouchSpeed)
            NormalizeNumber(ViewerTouchSpeed, ((MainWindow)Owner).editorSetting!.touchSpeed, 1f, 10f);
        else if (sender == StarSpeed)
            NormalizeNumber(StarSpeed, ((MainWindow)Owner).editorSetting!.starSpeed, -1f, 1f);
    }

    private void FontSizeTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        var fallback = ((MainWindow)Owner).editorSetting!.FontSize;
        if (!float.TryParse(EditorFontSize.Text, NumberStyles.Float, CultureInfo.InvariantCulture,
                out var value) && !float.TryParse(EditorFontSize.Text, out value))
            value = fallback;
        EditorFontSize.Text = Math.Clamp(value, 8f, 32f)
            .ToString("0.#", CultureInfo.InvariantCulture);
    }

    private bool ApplyAndSave()
    {
        var window = (MainWindow)Owner;
        if (!float.TryParse(InnerViewerCover.Text, out var innerBrightness) ||
            !float.TryParse(OuterViewerCover.Text, out var outerBrightness) ||
            !float.TryParse(ViewerSpeed.Text, out var noteSpeed) ||
            !float.TryParse(ViewerTouchSpeed.Text, out var touchSpeed) ||
            !float.TryParse(StarSpeed.Text, out var starSpeed) ||
            (!float.TryParse(EditorFontSize.Text, NumberStyles.Float, CultureInfo.InvariantCulture,
                 out var editorFontSize) && !float.TryParse(EditorFontSize.Text, out editorFontSize)) ||
            !int.TryParse(ChartRefreshDelay.Text, out var refreshDelay))
        {
            MessageBox.Show(
                MainWindow.GetLocalizedString("InvalidEditorSettings"),
                MainWindow.GetLocalizedString("Error"));
            return false;
        }
        var oldTheme = window.editorSetting!.EditorTheme;
        var oldFontPreset = window.editorSetting.EditorFontPreset;
        var oldFontSize = window.editorSetting.FontSize;
        var oldViewDisplayFontPreset = window.editorSetting.ViewDisplayFontPreset;
        var oldBackgroundStyle = window.editorSetting.EditorBackgroundStyle;
        var oldSkin = window.editorSetting.Skin;
        var oldTapSkin = window.editorSetting.TapSkin;
        var oldHoldSkin = window.editorSetting.HoldSkin;
        var oldStarSkin = window.editorSetting.StarSkin;
        var oldPinkStar = window.editorSetting.PinkStar;
        var oldSongDetailStyle = window.editorSetting.SongDetailStyle;
        var oldInnerCover = window.editorSetting.InnerBackgroundCover;
        var oldOuterCover = window.editorSetting.OuterBackgroundCover;
        var oldBackgroundFitMode = window.editorSetting.BackgroundFitMode;
        var oldShowJudgeInfo = window.editorSetting.ShowJudgeInfo;
        var oldShowComboInfo = window.editorSetting.ShowComboInfo;
        var oldShowJudgeLine = window.editorSetting.ShowJudgeLine;
        var oldShowJudgeText = window.editorSetting.ShowJudgeText;
        var oldShowJudgeArea = window.editorSetting.ShowJudgeArea;
        var oldShowSongDetail = window.editorSetting.ShowSongDetail;
        var oldShowAllPerfect = window.editorSetting.ShowAllPerfect;
        var oldShowGeneratedMark = window.editorSetting.ShowGeneratedMark;
        var oldComboDisplay = window.editorSetting.comboStatusType;
        var oldIntroStyle = window.editorSetting.ViewIntroStyle;

        window.editorSetting.Language = langList[LanguageComboBox.SelectedIndex];
        window.editorSetting!.RenderMode = RenderModeComboBox.SelectedIndex;
        window.editorSetting!.Skin = SkinComboBox.SelectedItem?.ToString() ?? "dx";
        window.editorSetting!.TapSkin = GetSkinGroup("tap", window.editorSetting.Skin);
        window.editorSetting!.HoldSkin = GetSkinGroup("hold", window.editorSetting.Skin);
        window.editorSetting!.StarSkin = GetSkinGroup("star", window.editorSetting.Skin);
        window.editorSetting!.PinkStar = pinkStar;
        window.editorSetting!.EditorTheme = (ThemeComboBox.SelectedItem as EditorTheme)?.name ?? ThemeManager.DefaultTheme;
        window.editorSetting!.InnerBackgroundCover = Math.Clamp(innerBrightness, 0f, 1f);
        window.editorSetting!.OuterBackgroundCover = Math.Clamp(outerBrightness, 0f, 1f);
        window.editorSetting!.BackgroundFitMode = Math.Clamp(BackgroundFitMode.SelectedIndex, 0, 1);
        window.editorSetting!.backgroundCover = window.editorSetting.InnerBackgroundCover;
        window.editorSetting!.EditorFontPreset = Math.Clamp(EditorFontPreset.SelectedIndex, 0, EditorFontPreset.Items.Count - 1);
        window.editorSetting!.FontSize = Math.Clamp(editorFontSize, 8f, 32f);
        window.editorSetting!.ViewDisplayFontPreset = Math.Clamp(ViewDisplayFont.SelectedIndex, 0,
            ViewDisplayFont.Items.Count - 1);
        window.editorSetting!.SongDetailStyle = Math.Clamp(SongDetailStyle.SelectedIndex, 0, 1);
        window.editorSetting!.EditorBackgroundStyle = IndexToStyle(EditorBackgroundStyle.SelectedIndex);
        window.editorSetting!.ViewIntroStyle = IndexToStyle(ViewIntroStyle.SelectedIndex);
        window.editorSetting!.EditorLightTheme = false;
        window.editorSetting!.ChartRefreshDelay = Math.Max(1, refreshDelay);
        window.editorSetting!.playSpeed = Math.Clamp(noteSpeed, 1f, 10f);
        window.editorSetting!.touchSpeed = Math.Clamp(touchSpeed, 1f, 10f);
        window.editorSetting!.starSpeed = Math.Clamp(starSpeed, -1f, 1f);
        window.editorSetting!.SmoothSlideAnime = (bool) SmoothSlideAnime.IsChecked!;
        window.editorSetting!.ShowJudgeInfo = ShowJudgeInfo.IsChecked == true;
        window.editorSetting!.ShowComboInfo = ShowComboInfo.IsChecked == true;
        window.editorSetting!.ShowJudgeLine = ShowJudgeLine.IsChecked == true;
        window.editorSetting!.ShowJudgeText = ShowJudgeText.IsChecked == true;
        // Hiding the judgment line always hides the judgment area.
        window.editorSetting!.ShowJudgeArea = ShowJudgeArea.IsChecked == true && ShowJudgeLine.IsChecked == true;
        window.editorSetting!.ShowSongDetail = ShowSongDetail.IsChecked == true;
        window.editorSetting!.ShowAllPerfect = ShowAllPerfect.IsChecked == true;
        window.editorSetting!.ShowGeneratedMark = ShowGeneratedMark.IsChecked == true;
        window.editorSetting!.editorPlayMethod = (EditorPlayMethod)PlayMethod.SelectedIndex;
        window.editorSetting!.SyntaxCheckLevel = SyntaxCheckLevel.SelectedIndex;
        // window.editorSetting.isComboEnabled = (bool) ComboDisplay.IsChecked!;
        window.editorSetting!.comboStatusType = (EditorComboIndicator)Enum.GetValues(
            window.editorSetting!.comboStatusType.GetType()
        ).GetValue(ComboDisplay.SelectedIndex)!;
        window.SaveEditorSetting();

        window.ViewerSpeed.Content = window.editorSetting.playSpeed.ToString("F1"); // Format the speed as "7.0", "9.5", etc.
        window.ViewerTouchSpeed.Content = window.editorSetting.touchSpeed.ToString("F1");
        window.chartChangeTimer.Interval = window.editorSetting.ChartRefreshDelay;
        var appearanceChanged = !string.Equals(oldTheme, window.editorSetting.EditorTheme,
                                    StringComparison.OrdinalIgnoreCase) ||
                                oldFontPreset != window.editorSetting.EditorFontPreset ||
                                Math.Abs(oldFontSize - window.editorSetting.FontSize) > 0.001f ||
                                !string.Equals(oldBackgroundStyle, window.editorSetting.EditorBackgroundStyle,
                                    StringComparison.OrdinalIgnoreCase);
        var waveSkinChanged = !string.Equals(oldSkin, window.editorSetting.Skin,
                                  StringComparison.OrdinalIgnoreCase) ||
                              !string.Equals(oldTapSkin, window.editorSetting.TapSkin,
                                  StringComparison.OrdinalIgnoreCase) ||
                              !string.Equals(oldHoldSkin, window.editorSetting.HoldSkin,
                                  StringComparison.OrdinalIgnoreCase) ||
                              !string.Equals(oldStarSkin, window.editorSetting.StarSkin,
                                  StringComparison.OrdinalIgnoreCase) ||
                              oldPinkStar != window.editorSetting.PinkStar;

        if (appearanceChanged)
            window.ApplyEditorAppearance();
        if (appearanceChanged || waveSkinChanged)
            window.RefreshWaveNoteSkin();
        var displayChanged = waveSkinChanged ||
                             !string.Equals(oldTheme, window.editorSetting.EditorTheme,
                                 StringComparison.OrdinalIgnoreCase) ||
                             oldInnerCover != window.editorSetting.InnerBackgroundCover ||
                              oldOuterCover != window.editorSetting.OuterBackgroundCover ||
                              oldBackgroundFitMode != window.editorSetting.BackgroundFitMode ||
                             oldShowJudgeInfo != window.editorSetting.ShowJudgeInfo ||
                             oldShowComboInfo != window.editorSetting.ShowComboInfo ||
                             oldShowJudgeLine != window.editorSetting.ShowJudgeLine ||
                             oldShowJudgeText != window.editorSetting.ShowJudgeText ||
                             oldShowJudgeArea != window.editorSetting.ShowJudgeArea ||
                             oldShowSongDetail != window.editorSetting.ShowSongDetail ||
                             oldShowAllPerfect != window.editorSetting.ShowAllPerfect ||
                             oldShowGeneratedMark != window.editorSetting.ShowGeneratedMark ||
                             oldViewDisplayFontPreset != window.editorSetting.ViewDisplayFontPreset ||
                             oldComboDisplay != window.editorSetting.comboStatusType ||
                             oldSongDetailStyle != window.editorSetting.SongDetailStyle ||
                             !string.Equals(oldIntroStyle, window.editorSetting.ViewIntroStyle,
                                 StringComparison.OrdinalIgnoreCase);
        if (displayChanged)
            window.SendDisplaySettings();
        if (oldSongDetailStyle != window.editorSetting.SongDetailStyle &&
            window.editorSetting.SongDetailStyle == 1)
            window.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                new Action(window.PreBakeSongDetail));


        settingsApplied = true;
        return true;
    }

    private void LoadSkinChoices(string selectedSkin)
    {
        SkinComboBox.Items.Clear();
        skinChoices.Clear();
        var skinRoot = FindSkinRoot();
        if (Directory.Exists(skinRoot))
        {
            foreach (var directory in Directory.GetDirectories(skinRoot)
                         .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(directory);
                SkinComboBox.Items.Add(name);
                skinChoices.Add(name);
            }
        }

        if (SkinComboBox.Items.Count == 0)
        {
            SkinComboBox.Items.Add("dx");
            skinChoices.Add("dx");
        }

        SkinComboBox.SelectedItem = selectedSkin;
        if (SkinComboBox.SelectedIndex < 0)
            SkinComboBox.SelectedIndex = 0;
    }

    private List<string> GetSkinChoices(string key)
    {
        return skinChoices;
    }

    private string ResolveSkinChoice(string key, string selected, string fallback)
    {
        var choices = GetSkinChoices(key);
        var match = choices.FirstOrDefault(name =>
            string.Equals(name, selected, StringComparison.OrdinalIgnoreCase));
        if (match != null)
            return match;
        match = choices.FirstOrDefault(name =>
            string.Equals(name, fallback, StringComparison.OrdinalIgnoreCase));
        return match ?? choices[0];
    }

    private void SkinComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // The base folder controls common assets only. Tap/Hold/Star keep their
        // explicit selections and are changed with the arrow controls below.
    }

    private void SkinGroupArrow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not string key ||
            !skinGroupRows.TryGetValue(key, out var row))
            return;
        var choices = GetSkinChoices(key);
        if (choices.Count == 0)
            return;
        var delta = int.TryParse(button.Tag?.ToString(), out var parsed) ? parsed : 1;
        var index = choices.FindIndex(name =>
            string.Equals(name, row.SelectedSkin, StringComparison.OrdinalIgnoreCase));
        index = (Math.Max(0, index) + delta + choices.Count) % choices.Count;
        row.SelectedSkin = choices[index];
        row.Selection.Text = FormatSkinChoice(row.SelectedSkin);
        UpdateSkinGroupPreview(key);
        // Refresh the pink-star preview when the star skin changes between DX and SD.
        if (string.Equals(key, "star", StringComparison.OrdinalIgnoreCase))
            UpdateStarColorPreview();
    }

    private void BuildSkinGroupRows()
    {
        foreach (var group in SkinGroups)
        {
            var isTallPreview = group.Key is "tap" or "hold";
            var preview = new Image
            {
                Width = 92,
                Height = isTallPreview ? 66 : 58,
                Stretch = Stretch.Uniform,
                Margin = group.Key is "tap" or "hold"
                    ? new Thickness(5, 3, 5, 3)
                    : new Thickness(5)
            };
            var selection = new TextBlock
            {
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0),
            };
            selection.SetResourceReference(TextBlock.ForegroundProperty, "HelperForeground");
            var card = new Border
            {
                Margin = new Thickness(0, 0, 0, 7),
                Padding = new Thickness(6),
                CornerRadius = new CornerRadius(5),
                BorderThickness = new Thickness(1)
            };
            card.SetResourceReference(Border.BorderBrushProperty, "ButtonsBackground");
            var panel = new StackPanel();
            var label = new TextBlock
            {
                Text = group.DisplayName,
                Margin = new Thickness(2, 0, 0, 4)
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "ButtonForeground");
            var previewGrid = new Grid();
            previewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            previewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            previewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            var previous = CreateSkinArrow(group.Key, "‹", -1);
            var next = CreateSkinArrow(group.Key, "›", 1);
            var previewBorder = new Border
            {
                Height = isTallPreview ? 76 : 66,
                Opacity = 0.9,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Child = preview
            };
            previewBorder.SetResourceReference(Border.BackgroundProperty, "ButtonsBackground");
            previewBorder.SetResourceReference(Border.BorderBrushProperty, "MenuSeparator");
            Grid.SetColumn(previewBorder, 1);
            Grid.SetColumn(next, 2);
            previewGrid.Children.Add(previous);
            previewGrid.Children.Add(previewBorder);
            previewGrid.Children.Add(next);
            panel.Children.Add(label);
            panel.Children.Add(previewGrid);
            panel.Children.Add(selection);
            card.Child = panel;
            SkinGroupList.Children.Add(card);
            skinGroupRows[group.Key] = new SkinGroupRow { Preview = preview, Selection = selection };
        }
        SkinGroupList.Children.Add(BuildStarColorCard());
    }

    private Button CreateSkinArrow(string key, string content, int delta)
    {
        var button = new Button
        {
            Content = content,
            FontSize = 18,
            Padding = new Thickness(0),
            Margin = new Thickness(3),
            Tag = delta,
            DataContext = key,
        };
        button.SetResourceReference(Control.ForegroundProperty, "ButtonForeground");
        button.SetResourceReference(Control.BackgroundProperty, "ButtonsBackground");
        button.Click += SkinGroupArrow_Click;
        return button;
    }

    private FrameworkElement BuildStarColorCard()
    {
        var card = new Border
        {
            Margin = new Thickness(0, 0, 0, 7),
            Padding = new Thickness(6),
            CornerRadius = new CornerRadius(5),
            BorderThickness = new Thickness(1)
        };
        card.SetResourceReference(Border.BorderBrushProperty, "ButtonsBackground");
        var panel = new StackPanel();
        var label = new TextBlock
        {
            Text = MainWindow.GetLocalizedString("StarColor"),
            Margin = new Thickness(2, 0, 0, 4)
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "ButtonForeground");
        panel.Children.Add(label);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });

        var previous = CreateStarColorArrow("‹");
        var next = CreateStarColorArrow("›");
        starColorPreview = new Image
        {
            Width = 92,
            Height = 58,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(5)
        };
        var previewBorder = new Border
        {
            Height = 66,
            Opacity = 0.9,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = starColorPreview
        };
        previewBorder.SetResourceReference(Border.BackgroundProperty, "ButtonsBackground");
        previewBorder.SetResourceReference(Border.BorderBrushProperty, "MenuSeparator");
        starColorSelection = new TextBlock
        {
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        starColorSelection.SetResourceReference(TextBlock.ForegroundProperty, "HelperForeground");
        Grid.SetColumn(previewBorder, 1);
        Grid.SetColumn(next, 2);
        grid.Children.Add(previous);
        grid.Children.Add(previewBorder);
        grid.Children.Add(next);
        panel.Children.Add(grid);
        panel.Children.Add(starColorSelection);
        card.Child = panel;
        UpdateStarColorSelection();
        return card;
    }

    private Button CreateStarColorArrow(string content)
    {
        var button = new Button
        {
            Content = content,
            FontSize = 18,
            Padding = new Thickness(0),
            Margin = new Thickness(3),
            ToolTip = MainWindow.GetLocalizedString("StarColor")
        };
        button.SetResourceReference(Control.ForegroundProperty, "ButtonForeground");
        button.SetResourceReference(Control.BackgroundProperty, "ButtonsBackground");
        button.Click += StarColorArrow_Click;
        return button;
    }

    private void StarColorArrow_Click(object sender, RoutedEventArgs e)
    {
        pinkStar = !pinkStar;
        UpdateStarColorSelection();
        UpdateStarColorPreview();
    }

    private void UpdateStarColorSelection()
    {
        if (starColorSelection != null)
            starColorSelection.Text = MainWindow.GetLocalizedString(pinkStar ? "Pink" : "Original");
        UpdateStarColorPreview();
    }

    private void SetSkinGroup(string key, string selected, string fallback)
    {
        if (!skinGroupRows.TryGetValue(key, out var row))
            return;
        row.SelectedSkin = ResolveSkinChoice(key, selected, fallback);
        row.Selection.Text = FormatSkinChoice(row.SelectedSkin);
        UpdateSkinGroupPreview(key);
        if (string.Equals(key, "star", StringComparison.OrdinalIgnoreCase))
            UpdateStarColorPreview();
    }

    private string GetSkinGroup(string key, string fallback) =>
        skinGroupRows.TryGetValue(key, out var row) && !string.IsNullOrWhiteSpace(row.SelectedSkin)
            ? row.SelectedSkin
            : fallback;

    private void UpdateSkinGroupPreview(string key)
    {
        if (!skinGroupRows.TryGetValue(key, out var row) || string.IsNullOrWhiteSpace(row.SelectedSkin))
            return;
        var previewFile = SkinGroups.First(group => group.Key == key).PreviewFile;
        var selectedSkin = row.SelectedSkin;
        var previewPath = Path.Combine(FindSkinRoot(), selectedSkin, previewFile);
        row.Preview.Source = File.Exists(previewPath)
            ? LoadPreviewImage(previewPath, string.Equals(key, "star", StringComparison.OrdinalIgnoreCase))
            : null;
    }

    private void UpdateStarColorPreview()
    {
        if (starColorPreview == null || !skinGroupRows.TryGetValue("star", out var starRow) ||
            string.IsNullOrWhiteSpace(starRow.SelectedSkin))
            return;
        var fileName = pinkStar ? "star_pink.png" : "star.png";
        var path = Path.Combine(FindSkinRoot(), starRow.SelectedSkin, fileName);
        // SD uses a 126px normal canvas and a 180px pink canvas. Cropping both to
        // their visible bounds gives the variants the same apparent size and center.
        starColorPreview.Source = File.Exists(path) ? LoadPreviewImage(path, true) : null;
    }

    private void ComboBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ComboBox comboBox || comboBox.IsDropDownOpen)
            return;

        DependencyObject current = comboBox;
        while (current != null && current is not ScrollViewer)
            current = VisualTreeHelper.GetParent(current);
        if (current is ScrollViewer scrollViewer)
        {
            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
            e.Handled = true;
        }
    }

    private static string FormatSkinChoice(string choice)
    {
        const string suffix = "-pink";
        return choice.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? $"{choice[..^suffix.Length].ToUpperInvariant()} Pink"
            : choice;
    }

    private static BitmapSource LoadPreviewImage(string path, bool trimTransparent = false)
    {
        using var stream = File.OpenRead(path);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        if (!trimTransparent)
            return image;

        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0d);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        var minX = converted.PixelWidth;
        var minY = converted.PixelHeight;
        var maxX = -1;
        var maxY = -1;
        for (var y = 0; y < converted.PixelHeight; y++)
        for (var x = 0; x < converted.PixelWidth; x++)
        {
            if (pixels[y * stride + x * 4 + 3] == 0)
                continue;
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }

        if (maxX < minX || maxY < minY)
            return image;
        const int padding = 4;
        minX = Math.Max(0, minX - padding);
        minY = Math.Max(0, minY - padding);
        maxX = Math.Min(converted.PixelWidth - 1, maxX + padding);
        maxY = Math.Min(converted.PixelHeight - 1, maxY + padding);
        var cropped = new CroppedBitmap(converted,
            new Int32Rect(minX, minY, maxX - minX + 1, maxY - minY + 1));
        cropped.Freeze();
        return cropped;
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

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || Owner is not MainWindow window ||
            ThemeComboBox.SelectedItem is not EditorTheme theme)
            return;
        window.PreviewEditorTheme(theme);
    }

    private static int StyleToIndex(string? style) => style?.ToLowerInvariant() switch
    {
        "circleplus" => 1,
        "circle" => 2,
        _ => 0
    };

    private static string IndexToStyle(int index) => index switch
    {
        1 => "circleplus",
        2 => "circle",
        _ => "default"
    };

    // Preview window backgrounds immediately without saving; persist only when confirmed.
    private void EditorBackgroundStyle_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || Owner is not MainWindow window)
            return;
        EditorDecorationBg.Apply(window.DecorationBgHost, IndexToStyle(EditorBackgroundStyle.SelectedIndex));
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
        if (!settingsApplied && !ApplyAndSave())
            e.Cancel = true;
    }
}
