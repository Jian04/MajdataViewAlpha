using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MajdataEdit;

/// <summary>
///     Interaction logic for BPMtap.xaml
/// </summary>
/*
 * The detection itself lives in MuriDetector; this file is the UI around it.
 */
public partial class MuriCheck : Window
{
    private MuriCheckResult? mcr;
    private MuriSlideTimeTable? slideTimeTable; // Measurements used for muri detection

    public MuriCheck()
    {
        InitializeComponent();
    }

    private void ReadMuriCheckSlideTime()
    {
        var json = File.ReadAllText("./slide_time.json");
        if (MuriSlideTimeTable.TryLoad(json, out var table, out var error))
        {
            slideTimeTable = table;
            return;
        }

        MessageBox.Show(error, MainWindow.GetLocalizedString("Error"));
    }

    private void StartCheck_Button_Click(object sender, RoutedEventArgs e)
    {
        double slideAccuracy;
        try
        {
            slideAccuracy = double.Parse(SlideAccuracy_TextBox.Text);
        }
        catch (FormatException)
        {
            MessageBox.Show(MainWindow.GetLocalizedString("SlideAccInputError"),
                MainWindow.GetLocalizedString("Error"));
            SlideAccuracy_TextBox.Text = "";
            return;
        }

        BeatmapMuriCheck(MultNote_Checkbox.IsChecked == true, slideAccuracy);
    }

    private void addWarning(string content, int posX, int posY)
    {
        if (mcr != null)
        {
            mcr.errorPosition.Add(new ErrorInfo(posX, posY));
            var resultRow = new ListBoxItem
            {
                Content = content,
                Name = "rr" + mcr.CheckResult_Listbox.Items.Count
            };
            resultRow.AddHandler(PreviewMouseDoubleClickEvent,
                new MouseButtonEventHandler(mcr.ListBoxItem_PreviewMouseDoubleClick));
            mcr.CheckResult_Listbox.Items.Add(resultRow);
        }
    }

    private void BeatmapMuriCheck(bool multNoteEnable, double slideCheckAccuracy)
    {
        if (mcr != null) mcr.Close();
        mcr = new MuriCheckResult();
        mcr.Owner = Owner;
        mcr.CheckResult_Listbox.Items.Clear();

        if (slideTimeTable == null)
            ReadMuriCheckSlideTime();
        var measurements = slideTimeTable;
        if (measurements == null)
            return;

        var detector = new MuriDetector(SimaiProcess.notelist, measurements);

        int multNoteError;
        if (multNoteEnable)
        {
            multNoteError = detector.DetectMultNote();
            if (multNoteError == -1)
            {
                // Exit because DX charts are unsupported.
                MessageBox.Show(
                    MainWindow.GetLocalizedString("MuriDxUnsupported"),
                    MainWindow.GetLocalizedString("Warning"));
                return;
            }
        }
        else
        {
            multNoteError = -114514; // This is a MAGIC NUMBER, Do not touch ;)
                                     // This is really a MAGIC NUMBER, Do not touch Xp
        }

        var slideError = detector.DetectSlide(slideCheckAccuracy);
        if (slideError == -1)
        {
            // Exit because DX charts are unsupported.
            MessageBox.Show(
                MainWindow.GetLocalizedString("MuriDxUnsupported"),
                MainWindow.GetLocalizedString("Warning"));
            return;
        }

        foreach (var warning in detector.Warnings)
            addWarning(warning.Content, warning.PositionX, warning.PositionY);

        mcr.Show();
        if (multNoteEnable)
            MessageBox.Show(
                string.Format(MainWindow.GetLocalizedString("CheckDone1"),
                    multNoteError, slideError),
                MainWindow.GetLocalizedString("Info")
            );
        else
            MessageBox.Show(
                string.Format(MainWindow.GetLocalizedString("CheckDone2"),
                    slideError),
                MainWindow.GetLocalizedString("Info")
            );
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SlideAccuracy_TextBox.Text = ((MainWindow)Owner).editorSetting!.DefaultSlideAccuracy.ToString();
    }

    private void Window_Initialized(object sender, EventArgs e)
    {
        ReadMuriCheckSlideTime();
    }
}
