using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace MajdataEdit;

/// <summary>
///     Interaction logic for Infomation.xaml
/// </summary>
public partial class Infomation : Window
{
    public event Action? InformationChanged;
    private bool loaded;
    private string chartDirectory = "";
    private readonly System.Windows.Threading.DispatcherTimer saveTimer = new()
    { Interval = TimeSpan.FromMilliseconds(600) };
    public Infomation()
    {
        InitializeComponent();
        saveTimer.Tick += (_, _) => FlushInformation();
        foreach (var input in new[] { TitleTextbox, ArtistTextbox, DesignTextbox,
                     BpmTextbox, ClockCountTextbox, OtherTextbox })
            input.TextChanged += (_, _) => CommitInformation();
        Closing += (_, _) => {
            if (!saveTimer.IsEnabled) return;
            FlushInformation();
        };
    }

    private void CommitInformation()
    {
        if (!loaded || !IsCurrentChart) return;
        SimaiProcess.title = TitleTextbox.Text;
        SimaiProcess.artist = ArtistTextbox.Text;
        SimaiProcess.designer = DesignTextbox.Text;
        SimaiProcess.wholeBpm = BpmTextbox.Text;
        SimaiProcess.clockCount = ClockCountTextbox.Text;
        SimaiProcess.other_commands = OtherTextbox.Text;
        saveTimer.Stop(); saveTimer.Start();
    }

    private bool IsCurrentChart => string.Equals(chartDirectory, MainWindow.maidataDir, StringComparison.OrdinalIgnoreCase);

    private void FlushInformation()
    {
        saveTimer.Stop();
        if (loaded && IsCurrentChart) InformationChanged?.Invoke();
    }

    internal void FlushPendingInformation()
    {
        if (saveTimer.IsEnabled) FlushInformation();
    }

    private void InfomationWindow_Loaded(object sender, RoutedEventArgs e) => ReloadChartInformation();

    internal void ReloadChartInformation()
    {
        saveTimer.Stop();
        loaded = false;
        chartDirectory = MainWindow.maidataDir;
        TitleTextbox.Text = SimaiProcess.title;
        ArtistTextbox.Text = SimaiProcess.artist;
        DesignTextbox.Text = SimaiProcess.designer;
        BpmTextbox.Text = SimaiProcess.GetWholeBpmText();
        ClockCountTextbox.Text = SimaiProcess.GetClockCountText();
        OtherTextbox.Text = SimaiProcess.other_commands;
        ReloadChartAssets();
        RenderOptions.SetBitmapScalingMode(SaltImage, BitmapScalingMode.HighQuality);
        loaded = true;
    }

    internal void ReloadChartAssets()
    {
        if (!IsCurrentChart) return;
        var paths = new[] { MajdataCore.ChartCover.Find(chartDirectory),
            MajdataCore.ChartCover.FindBackground(chartDirectory),
            (Owner as MainWindow)?.GetOriginalTrackPath() };
        var inputs = new[] { CoverTextbox, BackgroundTextbox, AudioTextbox };
        for (var index = 0; index < inputs.Length; index++)
        { inputs[index].Text = paths[index] == null ? "" : Path.GetFileName(paths[index]); inputs[index].ToolTip = paths[index]; }
        LoadImageFromDefault();
    }

    private async void SelectAsset_Click(object sender, RoutedEventArgs e)
    {
        if (IsCurrentChart && Owner is MainWindow main && sender is Button { Tag: string role })
            await main.SelectChartAssetAsync(Enum.Parse<ChartAssetRole>(role));
    }

    private void LoadImageFromDefault()
    {
        var cover = MajdataCore.ChartCover.Find(MainWindow.maidataDir);
        if (cover != null)
            SaltImage.Source = MainWindow.ReadChartThumbnail(cover, 512);
        else
            SaltImage.Source =
                new BitmapImage(new Uri("pack://application:,,,/MajdataEdit;component/Image/bg_dummy.jpg"));
    }

    private void LoadImageFromByte(byte[] data)
    {
        var image = new BitmapImage();
        using var stream = new MemoryStream(data);
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        SaltImage.Source = image;
    }

    private async Task ReadMetadataAsync(string path)
    {
        var formattedPath = path;

        if (!path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
        {
            var ext = ".mp3";

            if (File.Exists(path + ".ogg")) ext = ".ogg";

            formattedPath += ext;
        }

        using var file = TagLib.File.Create(formattedPath);
        TitleTextbox.Text = file.Tag.Title;
        ArtistTextbox.Text = file.Tag.JoinedPerformers;
        if (file.Tag.Pictures.Length > 0)
        {
            var pic = file.Tag.Pictures[0];
            LoadImageFromByte(pic.Data.Data);
            var result = MessageBox.Show(MainWindow.GetLocalizedString("IsOverridePicture"),
                MainWindow.GetLocalizedString("Info"), MessageBoxButton.YesNo);
            if (result != MessageBoxResult.Yes)
            {
                LoadImageFromDefault();
                return;
            }

            if (Owner is MainWindow main && (pic.MimeType.Contains("jpeg") || pic.MimeType.Contains("png")))
            {
                var temporary = MajdataCore.AlphaSession.ChartWorkFile(chartDirectory,
                    Guid.NewGuid() + (pic.MimeType.Contains("png") ? ".png" : ".jpg"));
                try
                {
                    await File.WriteAllBytesAsync(temporary, pic.Data.Data);
                    if (IsCurrentChart) await main.AssignChartAssetAsync(temporary, ChartAssetRole.Background, confirmReplace: false);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }

            Console.WriteLine(pic.MimeType);
            CommitInformation();
        }
    }

    private async void ReadTrackButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsCurrentChart) await ReadMetadataAsync(Path.Combine(chartDirectory, "track"));
    }

    private async void ReadFileButton_Click(object sender, RoutedEventArgs e)
    {
        var openFileDialog = new OpenFileDialog
        {
            Filter = "*.mp3, *.ogg|*.mp3;*.ogg"
        };
        if (IsCurrentChart && openFileDialog.ShowDialog() == true) await ReadMetadataAsync(openFileDialog.FileName);
    }

    private async void SaltImage_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (IsCurrentChart && Owner is MainWindow main) await main.SelectChartAssetAsync(ChartAssetRole.Cover);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
