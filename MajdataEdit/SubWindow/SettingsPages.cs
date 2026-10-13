namespace MajdataEdit;

// Distinct sidebar identities allow both pages to stay open together.
public sealed class EditSettingsPanel : EditorSettingPanel
{
    public EditSettingsPanel(bool dialogMode = false) : base(dialogMode, SettingsPage.Edit) { }
}

public sealed class ViewSettingsPanel : EditorSettingPanel
{
    public ViewSettingsPanel() : base(false, SettingsPage.View) { }
}
