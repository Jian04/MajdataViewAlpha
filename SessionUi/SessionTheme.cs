using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MajdataSessionUi;

// Use Edit's live theme resources; the pet uses the same palette as its dark panels.
public static class SessionTheme
{
    public static Brush Brush(string key, string fallback) =>
        Application.Current.TryFindResource(key) as Brush ?? (Brush)new BrushConverter().ConvertFromString(fallback)!;
    public static void Apply(Window window, double width, double height)
    {
        window.Width = width; window.Height = height;
        window.FontFamily = new FontFamily("Microsoft YaHei UI"); window.FontSize = 13;
        window.Background = Brush("WindowBackground", "#20242B");
        window.Foreground = Brush("ButtonForeground", "#F4F7FA");
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.UseLayoutRounding = true;
    }
    public static TextBlock Text(string value, bool muted = false, double size = 13) => new()
    {
        Text = value, TextWrapping = TextWrapping.Wrap, FontSize = size,
        Foreground = Brush(muted ? "HelperForeground" : "ButtonForeground", muted ? "#ADB4C0" : "#F4F7FA"),
        LineHeight = size * 1.4
    };
    public static StackPanel Heading(string title, string description)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        var heading = Text(title, size: 18); heading.FontWeight = FontWeights.SemiBold;
        panel.Children.Add(heading);
        var subtitle = Text(description, true); subtitle.Margin = new Thickness(0, 6, 0, 0);
        panel.Children.Add(subtitle); return panel;
    }
    public static Border Card(UIElement child) => new()
    {
        Background = Brush("EditorBackground", "#272C34"),
        BorderBrush = Brush("MenuSeparator", "#414752"), BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(5), Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 10), Child = child
    };
    public static Slider PlaybackSlider()
    {
        var slider = new Slider { Minimum = 0, Maximum = 1, Height = 22, IsMoveToPointEnabled = true,
            Cursor = System.Windows.Input.Cursors.Hand };
        slider.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Slider'>
 <Grid Height='22' Margin='7,0'>
  <Border Height='5' CornerRadius='3' Background='{DynamicResource ScrollTrack}' VerticalAlignment='Center'/>
  <Track Name='PART_Track' Height='16' VerticalAlignment='Center'>
   <Track.DecreaseRepeatButton>
    <RepeatButton Command='Slider.DecreaseLarge'>
     <RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Height='5' CornerRadius='3' Background='{DynamicResource HelperForeground}' VerticalAlignment='Center'/></ControlTemplate></RepeatButton.Template>
    </RepeatButton>
   </Track.DecreaseRepeatButton>
   <Track.IncreaseRepeatButton>
    <RepeatButton Command='Slider.IncreaseLarge'>
     <RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template>
    </RepeatButton>
   </Track.IncreaseRepeatButton>
   <Track.Thumb>
    <Thumb Width='14' Height='14'>
     <Thumb.Template><ControlTemplate TargetType='Thumb'><Border Width='14' Height='14' CornerRadius='7' Background='{DynamicResource ButtonForeground}' BorderBrush='{DynamicResource HelperForeground}' BorderThickness='2'/></ControlTemplate></Thumb.Template>
    </Thumb>
   </Track.Thumb>
  </Track>
 </Grid>
</ControlTemplate>");
        return slider;
    }
    public static void Field(Panel panel, string label, Control control)
    {
        var caption = Text(label); caption.Margin = new Thickness(0, 0, 0, 7);
        panel.Children.Add(caption);
        Input(control);
        control.Margin = new Thickness(0, 0, 0, 10);
        panel.Children.Add(control);
    }
    public static void Input(Control control)
    {
        control.Height = 30; control.MinHeight = 30;
        control.FontSize = 13;
        control.VerticalContentAlignment = VerticalAlignment.Center;
        control.HorizontalContentAlignment = HorizontalAlignment.Left;
        control.HorizontalAlignment = double.IsNaN(control.Width) ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        if (control is TextBox textBox) textBox.TextAlignment = TextAlignment.Left;
        control.Padding = new Thickness(8, 0, 8, 0);
        control.Background = Brush("ButtonsBackground", "#343A43");
        control.Foreground = Brush("ButtonForeground", "#F4F7FA");
        control.BorderBrush = Brush("MenuSeparator", "#525966"); control.BorderThickness = new Thickness(1);
        if (control is ComboBox combo)
        {
            var items = new Style(typeof(ComboBoxItem), Application.Current.TryFindResource(typeof(ComboBoxItem)) as Style);
            items.Setters.Add(new Setter(Control.BackgroundProperty, control.Background));
            items.Setters.Add(new Setter(Control.ForegroundProperty, control.Foreground));
            items.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 7, 10, 7)));
            var selected = new Trigger { Property = ComboBoxItem.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Control.BackgroundProperty, Brush("AccentBrush", "#376F91")));
            selected.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            items.Triggers.Add(selected);
            combo.ItemContainerStyle = items;
            if (!combo.IsEditable) combo.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ComboBox'>
 <Grid>
  <ToggleButton Focusable='False' Background='{TemplateBinding Background}' Foreground='{TemplateBinding Foreground}'
    BorderBrush='{TemplateBinding BorderBrush}' IsChecked='{Binding IsDropDownOpen,RelativeSource={RelativeSource TemplatedParent},Mode=TwoWay}'>
   <ToggleButton.Template><ControlTemplate TargetType='ToggleButton'><Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='4'><TextBlock Text='▾' HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,10,0' Foreground='{TemplateBinding Foreground}'/></Border></ControlTemplate></ToggleButton.Template>
  </ToggleButton>
  <ContentPresenter Content='{TemplateBinding SelectionBoxItem}' ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}' Margin='8,0,26,0' VerticalAlignment='Center' HorizontalAlignment='Left' IsHitTestVisible='False'/>
  <Popup Name='PART_Popup' Placement='Bottom' IsOpen='{TemplateBinding IsDropDownOpen}' AllowsTransparency='True' Focusable='False'>
   <Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' MinWidth='{Binding ActualWidth,RelativeSource={RelativeSource TemplatedParent}}' MaxHeight='260'>
    <ScrollViewer><ItemsPresenter/></ScrollViewer>
   </Border>
  </Popup>
 </Grid>
</ControlTemplate>");
        }
        // System PasswordBox otherwise remains white when hosted by Edit.
        if (control is TextBox or PasswordBox)
        {
            // TextBoxView already consumes Control.Padding. Put the inset in
            // just one place so text fields line up with ComboBox selections.
            control.Padding = new Thickness(0);
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            border.SetValue(Border.BackgroundProperty, new System.Windows.TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new System.Windows.TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.PaddingProperty, new Thickness(5, 0, 5, 0));
            var content = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost"); border.AppendChild(content);
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Stretch);
            content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            content.SetValue(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left);
            content.SetValue(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center);
            // Do not inherit Edit's implicit ScrollViewer template, which adds
            // its own padding and centres the text viewport.
            content.SetValue(FrameworkElement.StyleProperty, null);
            content.SetValue(Control.PaddingProperty, new Thickness(0));
            var template = new ControlTemplate(control.GetType()) { VisualTree = border };
            var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
            focus.Setters.Add(new Setter(Control.BorderBrushProperty, Brush("AccentBrush", "#72B4D4")));
            template.Triggers.Add(focus); control.Template = template;
        }
    }
    public static Button Button(string title, bool primary = false)
    {
        var button = new Button { Content = title, Height = 30, MinHeight = 30, FontSize = 13, FontFamily = new FontFamily("Microsoft YaHei UI"), Padding = new Thickness(10, 0, 10, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0), Cursor = System.Windows.Input.Cursors.Hand };
        var style = new Style(typeof(Button), Application.Current.TryFindResource(typeof(Button)) as Style);
        button.Background = Brush("ButtonsBackground", "#343A43"); button.Foreground = Brush("ButtonForeground", "#F4F7FA");
        button.BorderBrush = Brush("MenuSeparator", "#525966"); button.BorderThickness = new Thickness(1);
        button.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'>
 <Border Name='frame' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='4' Padding='{TemplateBinding Padding}'>
  <ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center'/>
 </Border>
 <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='frame' Property='Opacity' Value='0.8'/></Trigger></ControlTemplate.Triggers>
</ControlTemplate>");
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45)); style.Triggers.Add(disabled); button.Style = style;
        if (primary) { button.FontWeight = FontWeights.SemiBold; button.BorderBrush = Brush("AccentBrush", "#72B4D4"); button.BorderThickness = new Thickness(1); }
        return button;
    }
}
