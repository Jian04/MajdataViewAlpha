using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace MajdataEdit;

public partial class MainWindow
{
    private async Task NewChartFromMediaAsync(bool video)
    {
        if (!isSaved && !AskSave()) return;
        var source = new OpenFileDialog {
            Title = video ? "选择视频" : "选择音频",
            Filter = video ? "MP4 视频|*.mp4" : "MP3 音频|*.mp3"
        };
        if (source.ShowDialog(this) != true) return;
        var folder = Path.GetDirectoryName(source.FileName)!;
        var existingTrack = !video && Path.GetFileName(source.FileName).Equals("track.mp3", StringComparison.OrdinalIgnoreCase);
        var oldTitle = Title;
        try
        {
            Menu_New.IsEnabled = false;
            if (existingTrack)
            {
                CreateNewFumen(folder);
            }
            else
            {
                Title = video ? "正在提取音频并创建谱面…" : "正在创建谱面…";
                await MediaChartCreation.CreateAsync(source.FileName, folder, video,
                    "&title=" + GetLocalizedString("SetTitle") + "\n&artist=" + GetLocalizedString("SetArtist") +
                    "\n&des=" + GetLocalizedString("SetDes") + "\n&first=0\n");
                // The editor remains usable while FFmpeg works. Preserve any edits made meanwhile.
                if (!isSaved && !AskSave()) return;
            }
            initFromFile(folder);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "新建谱面", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { Menu_New.IsEnabled = true; if (Title.EndsWith("…")) Title = oldTitle; }
    }
}
