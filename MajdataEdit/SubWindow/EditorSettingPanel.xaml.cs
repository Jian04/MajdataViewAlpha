using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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
    public enum SettingsPage { Edit, View }
    private readonly SettingsPage settingsPage;
    private readonly HashSet<FrameworkElement> hiddenLegacySettings = new();
    private bool appearanceReady;
    private bool savingSettings;
    private readonly System.Windows.Threading.DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private sealed record SkinGroupOption(string Key, string DisplayName, string PreviewFile);

    private sealed class SkinGroupRow
    {
        public Image Preview { get; init; } = null!;
        public Border Host { get; init; } = null!;
        public TextBlock Selection { get; init; } = null!;
        public string SelectedSkin { get; set; } = "dx";
    }

    private static readonly SkinGroupOption[] SkinGroups =
    {
        new("tap", "Tap", "tap.png"),
        new("hold", "Hold", "hold.png"),
        new("star", "Star", "star.png"),
        new("slide", "Slide", "slide.png")
    };

    private readonly bool dialogMode;
    private readonly string[] langList = new string[3] { "zh-CN", "en-US", "ja" }; // Language list
    private readonly List<string> skinChoices = new();
    private readonly Dictionary<string, SkinGroupRow> skinGroupRows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (DateTime Modified, BitmapSource Image)> previewImages = new(StringComparer.OrdinalIgnoreCase);
    private TextBlock? starColorSelection;
    private Image? starColorPreview;
    private bool pinkStar;
    private bool settingsApplied;
    private sealed record PetChoice(string Id, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }
    private string? launcherSettingsPath;
    private string savedPetId = "dilaxiong";

    public EditorSettingPanel(bool _dialogMode = false, SettingsPage page = SettingsPage.Edit)
    {
        dialogMode = _dialogMode;
        settingsPage = page;
        InitializeComponent();
        FontFamily = (FontFamily)FindResource("UiFontFamily"); FontSize = 13; UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        Title = page == SettingsPage.Edit ? "Edit 设置" : "View 设置";
        while(SettingsFields.RowDefinitions.Count <= 26) SettingsFields.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        foreach(var item in SettingsFields.Children.OfType<FrameworkElement>().Where(x=>x.Visibility==Visibility.Collapsed)) hiddenLegacySettings.Add(item);
        ApplySettingsCategory();
        BuildSkinGroupRows();
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); if (appearanceReady && !savingSettings) { savingSettings = true; try { ApplyAndSave(false); } finally { savingSettings = false; } } };
        ((UIElement)Content).AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, _) => QueueSettingsSave()));
        ((UIElement)Content).AddHandler(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent, new SelectionChangedEventHandler((_, _) => QueueSettingsSave()));
        ((UIElement)Content).AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new RoutedEventHandler((_, _) => QueueSettingsSave()));
        JudgeLineStylePicker.SelectionChanged += (_, _) => QueueSettingsSave();
        EditorBackgroundStyle.SelectionChanged += (_, _) => QueueSettingsSave();
        ViewIntroStyle.SelectionChanged += (_, _) => QueueSettingsSave();
        Closed += (_, _) => saveTimer.Stop();
    }

    private void ApplySettingsCategory()
    {
        if(SettingsFields == null) return;
        var editRows = new HashSet<int> { 0, 1, 3, 6, 14, 15, 16, 21, 25 };
        foreach(var item in SettingsFields.Children.OfType<FrameworkElement>())
        {
            // The hidden legacy checkboxes still participate in serialization.
            if(hiddenLegacySettings.Contains(item)) continue;
            bool edit = editRows.Contains(Grid.GetRow(item));
            item.Visibility = edit == (settingsPage == SettingsPage.Edit) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void QueueSettingsSave()
    {
        if (!appearanceReady || savingSettings) return;
        settingsApplied = false; saveTimer.Stop(); saveTimer.Start();
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
        SetSkinGroup("slide", window.editorSetting.SlideSkin, window.editorSetting.Skin);
        LoadThemeChoices(window.editorSetting.EditorTheme);
        LoadPetChoices();

        InnerViewerCover.Text = window.editorSetting.InnerBackgroundCover.ToString();
        OuterViewerCover.Text = window.editorSetting.OuterBackgroundCover.ToString();
        BackgroundFitMode.SelectedIndex = Math.Clamp(window.editorSetting.BackgroundFitMode, 0, 1);
        EditorFontPreset.SelectedIndex = Math.Clamp(window.editorSetting.EditorFontPreset, 0, EditorFontPreset.Items.Count - 1);
        EditorFontSize.Text = window.editorSetting.FontSize.ToString("0.#", CultureInfo.InvariantCulture);
        ViewDisplayFont.SelectedIndex = Math.Clamp(window.editorSetting.ViewDisplayFontPreset, 0,
            ViewDisplayFont.Items.Count - 1);
        LoadFontChoices(window.editorSetting.EditorFontFile, window.editorSetting.ViewDisplayFontFile);
        SongDetailStyle.SelectedIndex = Math.Clamp(window.editorSetting.SongDetailStyle, 0, 1);
        DisableAutomaticSongDetailRefresh.IsChecked = window.editorSetting.DisableAutomaticSongDetailRefresh;
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
        starDisplayMode = window.editorSetting.StarDisplayMode == 0 ? 0 : 1;
        UpdateStarOrderPreview();
        ShowJudgeInfo.IsChecked = window.editorSetting.ShowJudgeInfo;
        ShowComboInfo.IsChecked = window.editorSetting.ShowComboInfo;
        JudgeLineStylePicker.SelectedIndex = MajdataCore.JudgeLineStyles.Supported(window.editorSetting.JudgeLineStyle);
        ShowJudgeText.IsChecked = window.editorSetting.ShowJudgeText;
        ShowMineHitFeedback.IsChecked = window.editorSetting.ShowMineHitFeedback;
        ClipBackgroundToRing.IsChecked = window.editorSetting.ClipBackgroundToRing;
        ShowSongDetail.IsChecked = window.editorSetting.ShowSongDetail;
        ShowAllPerfect.IsChecked = window.editorSetting.ShowAllPerfect;
        ShowGeneratedMark.IsChecked = window.editorSetting.ShowGeneratedMark;
        ViewTopLeftDisplayMode.SelectedIndex = Math.Clamp(window.editorSetting.ViewTopLeftDisplayMode, 0, 4);
        appearanceReady = true;
        SyntaxCheckLevel.SelectedIndex = window.editorSetting.SyntaxCheckLevel;
        ViewerSpeed.Text = window.editorSetting.playSpeed.ToString("F1");
        ViewerTouchSpeed.Text = window.editorSetting.touchSpeed.ToString("F1");
        StarSpeed.Text = window.editorSetting.starSpeed.ToString("F1");
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        //LanguageComboBox.SelectedIndex
        var language = langList[LanguageComboBox.SelectedIndex];
        LocalizeDictionary.Instance.Culture = new CultureInfo(language);
        MajdataCore.ParserMessageLocale.SetCulture(language);
    }

    private void LoadPetChoices()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        string? petsRoot = null;
        for (var depth = 0; depth < 10 && directory != null; depth++, directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "Pets");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(directory.FullName, "launcher.json")))
            {
                petsRoot = candidate;
                launcherSettingsPath = Path.Combine(directory.FullName, "launcher.json");
                break;
            }
            candidate = Path.Combine(directory.FullName, "MajdataLauncher", "Pets");
            if (petsRoot == null && Directory.Exists(candidate)) petsRoot = candidate;
        }
        var choices = new List<PetChoice>();
        var errors = new List<string>();
        if (petsRoot != null)
        {
            foreach (var folder in Directory.EnumerateDirectories(petsRoot).OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal))
            {
                try
                {
                    var appearance = MajdataCollaboration.PetAppearance.Read(folder);
                    choices.Add(new PetChoice(Path.GetFileName(folder), appearance.Name));
                }
                catch (Exception error) when (error is IOException or JsonException)
                { errors.Add(error.Message); }
            }
        }
        if (choices.Count == 0) choices.Add(new PetChoice("dilaxiong", "迪拉熊"));
        PetComboBox.ItemsSource = choices;
        if (errors.Count > 0) MessageBox.Show(this, string.Join("\n", errors), "桌宠素材");
        if (launcherSettingsPath != null)
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(launcherSettingsPath));
                if (document.RootElement.TryGetProperty("pet", out var selected))
                    savedPetId = selected.GetString() ?? "dilaxiong";
                if (savedPetId == "03") savedPetId = "乙姬";
            }
            catch (JsonException) { }
        }
        PetComboBox.SelectedItem = PetComboBox.Items.Cast<PetChoice>()
            .FirstOrDefault(choice => string.Equals(choice.Id, savedPetId, StringComparison.OrdinalIgnoreCase))
            ?? PetComboBox.Items[0];
    }

    private void PetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!appearanceReady || launcherSettingsPath == null ||
            PetComboBox.SelectedItem is not PetChoice choice ||
            string.Equals(choice.Id, savedPetId, StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            var json = JsonNode.Parse(File.ReadAllText(launcherSettingsPath)) as JsonObject ?? new JsonObject();
            json["pet"] = choice.Id;
            json["petSelectionVersion"] = 1;
            var temporaryPath = launcherSettingsPath + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(temporaryPath, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, launcherSettingsPath, true);
            savedPetId = choice.Id;
            PetStatusClient.Notify("skin", choice.Id);
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "桌宠设置", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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

    private bool ApplyAndSave(bool showErrors = true)
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
            if (showErrors) MessageBox.Show(
                MainWindow.GetLocalizedString("InvalidEditorSettings"),
                MainWindow.GetLocalizedString("Error"));
            return false;
        }
        var oldTheme = window.editorSetting!.EditorTheme;
        var oldFontPreset = window.editorSetting.EditorFontPreset;
        var oldEditorFontFile = window.editorSetting.EditorFontFile;
        var oldFontSize = window.editorSetting.FontSize;
        var oldViewDisplayFontPreset = window.editorSetting.ViewDisplayFontPreset;
        var oldViewFontFile = window.editorSetting.ViewDisplayFontFile;
        var oldBackgroundStyle = window.editorSetting.EditorBackgroundStyle;
        var oldSkin = window.editorSetting.Skin;
        var oldTapSkin = window.editorSetting.TapSkin;
        var oldHoldSkin = window.editorSetting.HoldSkin;
        var oldStarSkin = window.editorSetting.StarSkin;
        var oldSlideSkin = window.editorSetting.SlideSkin;
        var oldPinkStar = window.editorSetting.PinkStar;
        var oldStarDisplayMode = window.editorSetting.StarDisplayMode;
        var oldSongDetailStyle = window.editorSetting.SongDetailStyle;
        var oldInnerCover = window.editorSetting.InnerBackgroundCover;
        var oldOuterCover = window.editorSetting.OuterBackgroundCover;
        var oldBackgroundFitMode = window.editorSetting.BackgroundFitMode;
        var oldShowJudgeInfo = window.editorSetting.ShowJudgeInfo;
        var oldShowComboInfo = window.editorSetting.ShowComboInfo;
        var oldJudgeLineStyle = window.editorSetting.JudgeLineStyle;
        var oldShowJudgeText = window.editorSetting.ShowJudgeText;
        var oldShowMineHitFeedback = window.editorSetting.ShowMineHitFeedback;
        var oldClipBackgroundToRing = window.editorSetting.ClipBackgroundToRing;
        var oldShowSongDetail = window.editorSetting.ShowSongDetail;
        var oldShowAllPerfect = window.editorSetting.ShowAllPerfect;
        var oldShowGeneratedMark = window.editorSetting.ShowGeneratedMark;
        var oldViewTopLeftDisplayMode = window.editorSetting.ViewTopLeftDisplayMode;
        var oldComboDisplay = window.editorSetting.comboStatusType;
        var oldIntroStyle = window.editorSetting.ViewIntroStyle;

        // Each page owns only its fields. Hidden controls may contain older values
        // when both pages are open and must never overwrite the other page.
        if (settingsPage == SettingsPage.Edit)
        {
            window.editorSetting.Language = langList[LanguageComboBox.SelectedIndex];
            window.editorSetting.RenderMode = RenderModeComboBox.SelectedIndex;
            window.editorSetting.EditorTheme = (ThemeComboBox.SelectedItem as EditorTheme)?.name ?? ThemeManager.DefaultTheme;
            window.editorSetting.EditorFontFile = SelectedFontFile(EditorFontPreset);
            window.editorSetting.EditorFontPreset = EditorFontPreset.SelectedIndex < BuiltinFontCount ? Math.Max(0, EditorFontPreset.SelectedIndex) : 0;
            window.editorSetting.FontSize = Math.Clamp(editorFontSize, 8f, 32f);
            window.editorSetting.EditorBackgroundStyle = IndexToStyle(EditorBackgroundStyle.SelectedIndex);
            window.editorSetting.EditorLightTheme = false;
            window.editorSetting.ChartRefreshDelay = Math.Max(1, refreshDelay);
        }
        else
        {
        window.editorSetting!.Skin = SkinComboBox.SelectedItem?.ToString() ?? "dx";
        window.editorSetting!.TapSkin = GetSkinGroup("tap", window.editorSetting.Skin);
        window.editorSetting!.HoldSkin = GetSkinGroup("hold", window.editorSetting.Skin);
        window.editorSetting!.StarSkin = GetSkinGroup("star", window.editorSetting.Skin);
        window.editorSetting!.SlideSkin = GetSkinGroup("slide", window.editorSetting.Skin);
        window.editorSetting!.PinkStar = pinkStar;
        window.editorSetting!.InnerBackgroundCover = Math.Clamp(innerBrightness, 0f, 1f);
        window.editorSetting!.OuterBackgroundCover = Math.Clamp(outerBrightness, 0f, 1f);
        window.editorSetting!.BackgroundFitMode = Math.Clamp(BackgroundFitMode.SelectedIndex, 0, 1);
        window.editorSetting!.backgroundCover = window.editorSetting.InnerBackgroundCover;
        window.editorSetting!.ViewDisplayFontFile = SelectedFontFile(ViewDisplayFont);
        window.editorSetting.ViewDisplayFontPreset = ViewDisplayFont.SelectedIndex < BuiltinFontCount ? Math.Max(0, ViewDisplayFont.SelectedIndex) : 0;
        window.editorSetting!.SongDetailStyle = Math.Clamp(SongDetailStyle.SelectedIndex, 0, 1);
        window.editorSetting.DisableAutomaticSongDetailRefresh = DisableAutomaticSongDetailRefresh.IsChecked == true;
        window.editorSetting!.ViewIntroStyle = IndexToStyle(ViewIntroStyle.SelectedIndex);
        window.editorSetting!.playSpeed = Math.Clamp(noteSpeed, 1f, 10f);
        window.editorSetting!.touchSpeed = Math.Clamp(touchSpeed, 1f, 10f);
        window.editorSetting!.starSpeed = Math.Clamp(starSpeed, -1f, 1f);
        window.editorSetting!.SmoothSlideAnime = (bool) SmoothSlideAnime.IsChecked!;
        window.editorSetting!.StarDisplayMode = starDisplayMode;
        window.editorSetting!.ShowJudgeInfo = ShowJudgeInfo.IsChecked == true;
        window.editorSetting!.ShowComboInfo = ShowComboInfo.IsChecked == true;
        window.editorSetting!.JudgeLineStyle = MajdataCore.JudgeLineStyles.Available[JudgeLineStylePicker.SelectedIndex];
        window.editorSetting!.ShowJudgeText = ShowJudgeText.IsChecked == true;
        window.editorSetting!.ShowMineHitFeedback = ShowMineHitFeedback.IsChecked == true;
        window.editorSetting!.ClipBackgroundToRing = ClipBackgroundToRing.IsChecked == true;
        window.editorSetting!.ShowSongDetail = ShowSongDetail.IsChecked == true;
        window.editorSetting!.ShowAllPerfect = ShowAllPerfect.IsChecked == true;
        window.editorSetting!.ShowGeneratedMark = ShowGeneratedMark.IsChecked == true;
        window.editorSetting.ViewTopLeftDisplayMode = Math.Clamp(ViewTopLeftDisplayMode.SelectedIndex, 0, 4);
        window.editorSetting!.editorPlayMethod = (EditorPlayMethod)PlayMethod.SelectedIndex;
        // window.editorSetting.isComboEnabled = (bool) ComboDisplay.IsChecked!;
        window.editorSetting!.comboStatusType = (EditorComboIndicator)Enum.GetValues(
            window.editorSetting!.comboStatusType.GetType()
        ).GetValue(ComboDisplay.SelectedIndex)!;
        }
        window.SaveEditorSetting();

        window.ViewerSpeed.Content = window.editorSetting.playSpeed.ToString("F1"); // Format the speed as "7.0", "9.5", etc.
        window.ViewerTouchSpeed.Content = window.editorSetting.touchSpeed.ToString("F1");
        window.chartChangeTimer.Interval = window.editorSetting.ChartRefreshDelay;
        var appearanceChanged = !string.Equals(oldTheme, window.editorSetting.EditorTheme,
                                    StringComparison.OrdinalIgnoreCase) ||
                                oldFontPreset != window.editorSetting.EditorFontPreset ||
                                oldEditorFontFile != window.editorSetting.EditorFontFile ||
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
                              !string.Equals(oldSlideSkin, window.editorSetting.SlideSkin, StringComparison.OrdinalIgnoreCase) ||
                              oldPinkStar != window.editorSetting.PinkStar;

        if (appearanceChanged)
            window.ApplyEditorAppearance();
        if (appearanceChanged || waveSkinChanged)
            window.RefreshWaveNoteSkin();
        var displayChanged = waveSkinChanged ||
                             oldStarDisplayMode != window.editorSetting.StarDisplayMode ||
                             !string.Equals(oldTheme, window.editorSetting.EditorTheme,
                                 StringComparison.OrdinalIgnoreCase) ||
                             oldInnerCover != window.editorSetting.InnerBackgroundCover ||
                              oldOuterCover != window.editorSetting.OuterBackgroundCover ||
                              oldBackgroundFitMode != window.editorSetting.BackgroundFitMode ||
                             oldShowJudgeInfo != window.editorSetting.ShowJudgeInfo ||
                             oldShowComboInfo != window.editorSetting.ShowComboInfo ||
                             oldJudgeLineStyle != window.editorSetting.JudgeLineStyle ||
                             oldShowJudgeText != window.editorSetting.ShowJudgeText ||
                             oldShowMineHitFeedback != window.editorSetting.ShowMineHitFeedback ||
                             oldClipBackgroundToRing != window.editorSetting.ClipBackgroundToRing ||
                             oldShowSongDetail != window.editorSetting.ShowSongDetail ||
                             oldShowAllPerfect != window.editorSetting.ShowAllPerfect ||
                             oldShowGeneratedMark != window.editorSetting.ShowGeneratedMark ||
                             oldViewTopLeftDisplayMode != window.editorSetting.ViewTopLeftDisplayMode ||
                             oldViewDisplayFontPreset != window.editorSetting.ViewDisplayFontPreset ||
                             oldViewFontFile != window.editorSetting.ViewDisplayFontFile ||
                             oldComboDisplay != window.editorSetting.comboStatusType ||
                             oldSongDetailStyle != window.editorSetting.SongDetailStyle ||
                             !string.Equals(oldIntroStyle, window.editorSetting.ViewIntroStyle,
                                 StringComparison.OrdinalIgnoreCase);
        if (displayChanged)
            window.SendDisplaySettings(background: true);
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
        foreach (var name in MajdataCore.SkinPaths.Names(FindSkinRoots()))
        {
            SkinComboBox.Items.Add(name);
            skinChoices.Add(name);
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
        var group = SkinGroups.FirstOrDefault(candidate =>
            string.Equals(candidate.Key, key, StringComparison.OrdinalIgnoreCase));
        if (group == null)
            return new List<string>(skinChoices);

        var choices = skinChoices
            .Where(name => File.Exists(PreviewPath(name, group.PreviewFile)))
            .ToList();
        return choices.Count > 0 ? choices : new List<string>(skinChoices);
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
        var previousImage = row.Preview.Source;
        UpdateSkinGroupPreview(key, key == "slide" ? delta : 0);
        if (key != "slide") CarouselTransition.Image(row.Preview, previousImage, delta);
        if (key == "star") UpdateStarColorPreview();
        if (key == "slide") UpdateStarOrderPreview();
    }

    private void BuildSkinGroupRows()
    {
        foreach (var group in SkinGroups)
        {
            if (group.Key == "slide") SkinGroupList.Children.Add(BuildStarColorCard());
            var preview = new Image { MaxWidth = 224, Height = 64, Stretch = Stretch.Uniform };
            var selection = new TextBlock();
            var host = new Border { Height = 66, ClipToBounds = true, Child = preview };
            var picker = VisualStylePicker.CreatePicker(host,
                CreateSkinArrow(group.Key, "‹", -1), CreateSkinArrow(group.Key, "›", 1));
            SkinGroupList.Children.Add(VisualStylePicker.CreateRow(new TextBlock { Text = group.DisplayName }, selection, picker));
            skinGroupRows[group.Key] = new SkinGroupRow { Preview = preview, Host = host, Selection = selection };
        }
        SkinGroupList.Children.Add(BuildStarOrderCard());
    }

    private Button CreateSkinArrow(string key, string content, int delta)
    {
        var button = new Button { Content = content, Tag = delta, DataContext = key };
        button.Click += SkinGroupArrow_Click;
        return button;
    }

    private FrameworkElement BuildStarColorCard()
    {
        starColorPreview = new Image { MaxWidth = 224, Height = 64, Stretch = Stretch.Uniform };
        var host = new Border { Height = 66, ClipToBounds = true, Child = starColorPreview };
        starColorSelection = new TextBlock();
        var picker = VisualStylePicker.CreatePicker(host, CreateStarColorArrow("‹"), CreateStarColorArrow("›"));
        var row = VisualStylePicker.CreateRow(new TextBlock { Text = MainWindow.GetLocalizedString("StarColor") }, starColorSelection, picker);
        UpdateStarColorSelection();
        return row;
    }

    private Button CreateStarColorArrow(string content)
    {
        var button = new Button { Content = content, ToolTip = MainWindow.GetLocalizedString("StarColor") };
        button.Click += StarColorArrow_Click;
        return button;
    }

    private void StarColorArrow_Click(object sender, RoutedEventArgs e)
    {
        var previousImage = starColorPreview?.Source;
        pinkStar = !pinkStar;
        UpdateStarColorSelection();
        if (starColorPreview != null) CarouselTransition.Image(starColorPreview, previousImage,
            (sender as Button)?.Content?.ToString() == "‹" ? -1 : 1);
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

    private void UpdateSkinGroupPreview(string key, int direction = 0)
    {
        if (!skinGroupRows.TryGetValue(key, out var row) || string.IsNullOrWhiteSpace(row.SelectedSkin))
            return;
        if (key == "slide")
        {
            CarouselTransition.Replace(row.Host, BuildSlidePreview(false), direction);
            return;
        }
        var previewFile = key == "star" && pinkStar ? "star_pink.png" : SkinGroups.First(group => group.Key == key).PreviewFile;
        var selectedSkin = row.SelectedSkin;
        var previewPath = PreviewPath(selectedSkin, previewFile);
        row.Preview.Source = File.Exists(previewPath)
            ? StarSample(selectedSkin, previewFile, key == "hold")
            : null;
    }

    private void UpdateStarColorPreview()
    {
        if (starColorPreview == null || !skinGroupRows.TryGetValue("star", out var starRow) ||
            string.IsNullOrWhiteSpace(starRow.SelectedSkin))
            return;
        var fileName = pinkStar ? "star_pink.png" : "star.png";
        var path = PreviewPath(starRow.SelectedSkin, fileName);
        // SD uses a 126px normal canvas and a 180px pink canvas. Cropping both to
        // their visible bounds gives the variants the same apparent size and center.
        starColorPreview.Source = File.Exists(path) ? StarSample(starRow.SelectedSkin, fileName) : null;
        UpdateSkinGroupPreview("star");
        UpdateSkinGroupPreview("slide");
        UpdateStarOrderPreview();
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

    private BitmapSource LoadPreviewImage(string path, bool trimTransparent = false)
    {
        var key = path + (trimTransparent ? "|trim" : "|full");
        var modified = File.GetLastWriteTimeUtc(path);
        if (previewImages.TryGetValue(key, out var cached) && cached.Modified == modified)
            return cached.Image;
        BitmapSource Cache(BitmapSource result) { previewImages[key] = (modified, result); return result; }
        using var stream = File.OpenRead(path);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        if (!trimTransparent)
            return Cache(image);

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
            return Cache(image);
        const int padding = 4;
        minX = Math.Max(0, minX - padding);
        minY = Math.Max(0, minY - padding);
        maxX = Math.Min(converted.PixelWidth - 1, maxX + padding);
        maxY = Math.Min(converted.PixelHeight - 1, maxY + padding);
        var cropped = new CroppedBitmap(converted,
            new Int32Rect(minX, minY, maxX - minX + 1, maxY - minY + 1));
        cropped.Freeze();
        return Cache(cropped);
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
        if (!EditorBackgroundStyle.IsLoaded || Owner is not MainWindow window ||
            ThemeComboBox.SelectedItem is not EditorTheme theme)
            return;
        window.PreviewEditorTheme(theme);
    }

    private static int StyleToIndex(string? style) => style?.ToLowerInvariant() switch
    {
        "circleplus" => 1,
        "circle" => 2,
        "chunithm-xversex" => 3,
        "chunithm-mate" => 4,
        "magical" => 5,
        _ => 0
    };

    private void ViewIntroStyle_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!appearanceReady || Owner is not MainWindow window || ViewIntroStyle.SelectedIndex < 0) return;
        window.editorSetting!.ViewIntroStyle = IndexToStyle(ViewIntroStyle.SelectedIndex);
        window.SaveEditorSetting();
        window.SendDisplaySettings(background: true);
    }

    private static string IndexToStyle(int index) => index switch
    {
        1 => "circleplus",
        2 => "circle",
        3 => "chunithm-xversex",
        4 => "chunithm-mate",
        5 => "magical",
        _ => "default"
    };

    // Preview window backgrounds immediately without saving; persist only when confirmed.
    private void EditorBackgroundStyle_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!EditorBackgroundStyle.IsLoaded || Owner is not MainWindow window)
            return;
        EditorDecorationBg.Apply(window.DecorationBgHost, IndexToStyle(EditorBackgroundStyle.SelectedIndex));
    }

    private static string[] FindSkinRoots() => MajdataCore.SkinPaths.Roots(AppContext.BaseDirectory);
    private static string PreviewPath(string skin, string file) => Path.Combine(
        MajdataCore.SkinPaths.Folder(FindSkinRoots(), skin, Path.Combine(AppContext.BaseDirectory, "Skin", skin)), file);

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (!settingsApplied && !ApplyAndSave())
            e.Cancel = true;
    }
}
