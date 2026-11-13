namespace MajdataEdit;

// A separate page identity keeps video and GIF options independent in the sidebar.
public sealed class GifExportWindow : RecordVideoWindow
{
    internal GifExportWindow(MainWindow main) : base(main, exportGif: true) { }
}
