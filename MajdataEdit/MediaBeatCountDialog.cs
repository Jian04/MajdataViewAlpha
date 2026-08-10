using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MajdataEdit;

internal sealed class MediaBeatCountDialog : Window
{
    private readonly TextBox beatCountBox = new()
    {
        Text = "4",
        Width = 120,
        Margin = new Thickness(8, 0, 0, 0),
        VerticalContentAlignment = VerticalAlignment.Center
    };

    private double? result;

    private MediaBeatCountDialog()
    {
        Title = MainWindow.GetLocalizedString("BeatCountTitle");
        Width = 330;
        Height = 150;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Application.Current.TryFindResource("WindowBackground") as Brush;
        Foreground = Application.Current.TryFindResource("ButtonForeground") as Brush;

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var inputRow = new StackPanel { Orientation = Orientation.Horizontal };
        inputRow.Children.Add(new TextBlock
        {
            Text = MainWindow.GetLocalizedString("BeatCount"),
            VerticalAlignment = VerticalAlignment.Center
        });
        inputRow.Children.Add(beatCountBox);
        root.Children.Add(inputRow);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var ok = new Button
        {
            Content = MainWindow.GetLocalizedString("Confirm"),
            Width = 78,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        ok.Click += (_, _) => Confirm();
        var cancel = new Button
        {
            Content = MainWindow.GetLocalizedString("Cancel"),
            Width = 78,
            IsCancel = true
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);
        Content = root;
    }

    public static double? ShowDialog(Window owner)
    {
        var dialog = new MediaBeatCountDialog { Owner = owner };
        dialog.beatCountBox.SelectAll();
        dialog.beatCountBox.Focus();
        dialog.ShowDialog();
        return dialog.result;
    }

    private void Confirm()
    {
        if ((!double.TryParse(beatCountBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture,
                 out var beats) &&
             !double.TryParse(beatCountBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture,
                 out beats)) || !double.IsFinite(beats) || beats <= 0d)
        {
            MessageBox.Show(MainWindow.GetLocalizedString("InvalidBeatCount"));
            return;
        }

        result = beats;
        DialogResult = true;
    }
}
