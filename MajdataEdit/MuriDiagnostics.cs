using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace MajdataEdit;

public partial class MainWindow
{
    private List<ChartDiagnostic> staticDiagnostics = new();
    private List<ChartDiagnostic> dynamicDiagnostics = new();
    private DiagnosticsPanel? diagnosticsPanel;
    private void SyntaxDiagnostics_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => ShowDiagnostics(0);
    private void StaticDiagnostics_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => ShowDiagnostics(1);
    private void DynamicDiagnostics_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => ShowDiagnostics(2);
    private void ShowDiagnostics(int category)
    {
        if (diagnosticsPanel == null)
        {
            diagnosticsPanel = new DiagnosticsPanel(index => index switch {
                0 => latestParseErrors.Select(x => new ChartDiagnostic(x.PositionX, x.PositionY, x.Message)),
                1 => StaticMuriCount.Content is int count && count > 0 ? staticDiagnostics : Enumerable.Empty<ChartDiagnostic>(),
                _ => dynamicDiagnostics
            }, ScrollToFumenContentSelection);
            diagnosticsPanel.Closed += (_, _) => diagnosticsPanel = null;
            ShowTool(diagnosticsPanel);
        }
        else FocusTool<DiagnosticsPanel>();
        diagnosticsPanel.Select(category);
    }
    private string? muriSource;
    private MuriSlideTimeTable? muriMeasurements;
    private string? playedMuriSource;
    private bool wasMuriPlaying;
    private readonly DispatcherTimer muriTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    private void StartMuriDiagnostics()
    {
        muriTimer.Tick += (_, _) =>
        {
            if (isPlaying && !wasMuriPlaying) playedMuriSource = GetRawFumenText();
            wasMuriPlaying = isPlaying;
            int count = editorSetting?.editorPlayMethod == EditorPlayMethod.DJAuto && playedMuriSource == GetRawFumenText()
                ? MajdataCore.AlphaSession.Current?.ReadDynamicMuri() ?? 0 : 0;
            DynamicMuriCount.Content = count;
            dynamicDiagnostics.Clear();
            if (count > 0)
                foreach (var detail in MajdataCore.AlphaSession.Current?.ReadDynamicMuriDetails() ?? Array.Empty<string>())
                {
                    var parts = detail.Split('\t');
                    if (parts.Length != 3 || !int.TryParse(parts[0], out var line) || !int.TryParse(parts[1], out var column)) continue;
                    try { dynamicDiagnostics.Add(new ChartDiagnostic(column, line, System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(parts[2])))); }
                    catch (FormatException) { }
                }
            diagnosticsPanel?.Refresh();
            DynamicMuriRow.Visibility = Visibility.Visible;
        };
        muriTimer.Start();
        Closed += (_, _) => muriTimer.Stop();
    }

    private void RefreshStaticMuri()
    {
        var source = GetRawFumenText();
        if (muriSource == source) return;
        muriSource = source;
        playedMuriSource = null;
        StaticMuriCount.Content = 0;
        staticDiagnostics.Clear();
        dynamicDiagnostics.Clear();
        DynamicMuriCount.Content = 0;
        try
        {
            if (muriMeasurements == null)
            {
                var path = Path.Combine(AppContext.BaseDirectory, "slide_time.json");
                if (!File.Exists(path) || !MuriSlideTimeTable.TryLoad(File.ReadAllText(path), out muriMeasurements, out _))
                    muriMeasurements = MuriSlideTimeTable.Empty;
            }
            var detector = new MuriDetector(SimaiProcess.notelist, muriMeasurements!, allowPartial: true);
            var count = detector.DetectStatic();
            staticDiagnostics = detector.Warnings.Select(w => new ChartDiagnostic(w.PositionX, w.PositionY, w.Content)).ToList();
            StaticMuriCount.Content = count;
            StaticMuriRow.Visibility = Visibility.Visible;
        }
        catch (Exception error) when (error is IOException or FormatException or ArgumentException)
        {
            StaticMuriCount.Content = 0;
        }
    }
}
