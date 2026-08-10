using System.Windows;

namespace MajdataEdit;

public partial class OrganizeOptionsWindow : Window
{
    public bool? AddMeasureComments { get; private set; }

    public OrganizeOptionsWindow()
    {
        InitializeComponent();
    }

    private void AddComments_Click(object sender, RoutedEventArgs e)
    {
        AddMeasureComments = true;
        DialogResult = true;
    }

    private void SkipComments_Click(object sender, RoutedEventArgs e)
    {
        AddMeasureComments = false;
        DialogResult = true;
    }
}
