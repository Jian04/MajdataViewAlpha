using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace MajdataEdit;

internal sealed record AutoOnsetRequest(
    string AudioPath,
    string Level,
    double? Bpm,
    double? First,
    double Threshold,
    string? Title);

internal sealed record AutoOnsetResult(
    string Chart,
    double Bpm,
    double First,
    int PredictedOnsets);

internal static class AutoOnsetRunner
{
    public static async Task<AutoOnsetResult> GenerateAsync(
        AutoOnsetRequest request,
        Action<string>? reportProgress,
        CancellationToken cancellationToken)
    {
        var toolDirectory = Path.Combine(AppContext.BaseDirectory, "tools", "Maicaiyin");
        var inferenceScript = Path.Combine(toolDirectory, "infer.py");
        var modelPath = Path.Combine(toolDirectory, "joint-placement-numpy.npz");
        var packageDirectory = Path.Combine(toolDirectory, "packages");
        if (!File.Exists(inferenceScript) || !File.Exists(modelPath) ||
            !Directory.Exists(packageDirectory))
            throw new FileNotFoundException(MainWindow.GetLocalizedString("AutoOnsetEngineMissing"), toolDirectory);

        var bootstrap = ResolveBundledPython(toolDirectory);
        var runtime = new PythonRuntime(
            bootstrap.FileName,
            bootstrap.PrefixArguments,
            packageDirectory);

        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "MajdataEdit-AutoOnset-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);
        try
        {
            reportProgress?.Invoke(MainWindow.GetLocalizedString("AutoOnsetRunning"));
            var arguments = new List<string>
            {
                inferenceScript,
                request.AudioPath,
                "--output", outputDirectory,
                "--level", request.Level,
                "--threshold", request.Threshold.ToString("R", CultureInfo.InvariantCulture),
                "--model", modelPath
            };
            if (request.Bpm.HasValue)
            {
                arguments.Add("--bpm");
                arguments.Add(request.Bpm.Value.ToString("R", CultureInfo.InvariantCulture));
            }
            if (request.First.HasValue)
            {
                arguments.Add("--offset");
                arguments.Add(request.First.Value.ToString("R", CultureInfo.InvariantCulture));
            }
            if (!string.IsNullOrWhiteSpace(request.Title))
            {
                arguments.Add("--title");
                arguments.Add(request.Title);
            }

            var processResult = await RunProcessAsync(
                runtime.FileName,
                BuildIsolatedScriptArguments(runtime, inferenceScript, arguments.Skip(1)),
                toolDirectory,
                reportProgress,
                cancellationToken);
            if (processResult.ExitCode != 0)
                throw new InvalidOperationException(BuildProcessError(processResult));

            var maidataPath = Path.Combine(outputDirectory, "maidata.txt");
            var reportPath = Path.Combine(outputDirectory, "generation.json");
            if (!File.Exists(maidataPath) || !File.Exists(reportPath))
                throw new InvalidOperationException(MainWindow.GetLocalizedString("AutoOnsetNoOutput"));

            var maidata = await File.ReadAllTextAsync(maidataPath, Encoding.UTF8, cancellationToken);
            var report = JObject.Parse(await File.ReadAllTextAsync(reportPath, Encoding.UTF8, cancellationToken));
            var chart = ExtractGeneratedChart(maidata);
            var bpm = report.Value<double?>("bpm")
                      ?? throw new InvalidOperationException(MainWindow.GetLocalizedString("AutoOnsetNoOutput"));
            var first = report.Value<double?>("offset_seconds")
                        ?? throw new InvalidOperationException(MainWindow.GetLocalizedString("AutoOnsetNoOutput"));
            var predictedOnsets = report.Value<int?>("predicted_onsets") ?? 0;
            return new AutoOnsetResult(chart, bpm, first, predictedOnsets);
        }
        finally
        {
            try
            {
                if (Directory.Exists(outputDirectory))
                    Directory.Delete(outputDirectory, true);
            }
            catch
            {
                // Temporary output cleanup must not hide a successful generation.
            }
        }
    }

    private static PythonCommand ResolveBundledPython(string workingDirectory)
    {
        var runtime = new PythonCommand(
            Path.Combine(workingDirectory, "python", "python.exe"),
            Array.Empty<string>());
        if (File.Exists(runtime.FileName))
            return runtime;

        throw new InvalidOperationException(MainWindow.GetLocalizedString("AutoOnsetPythonMissing"));
    }

    private static IReadOnlyList<string> BuildIsolatedScriptArguments(
        PythonRuntime runtime,
        string script,
        IEnumerable<string> scriptArguments)
    {
        const string runner =
            "import os,runpy,sys; " +
            "packages=os.path.abspath(sys.argv[1]); script=os.path.abspath(sys.argv[2]); " +
            "sys.path=[packages,os.path.dirname(script)]+[p for p in sys.path if 'site-packages' not in p.lower() and 'dist-packages' not in p.lower()]; " +
            "sys.argv=sys.argv[2:]; runpy.run_path(script,run_name='__main__')";
        var arguments = new List<string>(runtime.PrefixArguments)
        {
            "-I", "-c", runner, runtime.PackageDirectory, script
        };
        arguments.AddRange(scriptArguments);
        return arguments;
    }

    private static string ExtractGeneratedChart(string maidata)
    {
        const string marker = "&inote_1=";
        var start = maidata.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
            throw new InvalidOperationException(MainWindow.GetLocalizedString("AutoOnsetNoOutput"));
        start += marker.Length;
        while (start < maidata.Length && maidata[start] is '\r' or '\n')
            start++;
        var chart = maidata[start..].Trim();
        if (!chart.EndsWith("E", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(MainWindow.GetLocalizedString("AutoOnsetNoOutput"));
        return chart;
    }

    private static string BuildProcessError(ProcessResult result)
    {
        var message = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;
        return string.Format(
            CultureInfo.CurrentCulture,
            MainWindow.GetLocalizedString("AutoOnsetProcessFailed"),
            result.ExitCode,
            message.Trim());
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        Action<string>? reportProgress,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        startInfo.Environment["PYTHONUTF8"] = "1";
        startInfo.Environment["PYTHONNOUSERSITE"] = "1";
        startInfo.Environment["CUDA_VISIBLE_DEVICES"] = "";
        startInfo.Environment["PIP_NO_INDEX"] = "1";
        startInfo.Environment["PIP_DISABLE_PIP_VERSION_CHECK"] = "1";

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(true);
            }
            catch
            {
                // Cancellation is best effort.
            }
        });

        var output = new StringBuilder();
        var error = new StringBuilder();
        var outputTask = ReadLinesAsync(process.StandardOutput, output, reportProgress);
        var errorTask = ReadLinesAsync(process.StandardError, error, reportProgress);
        await process.WaitForExitAsync(cancellationToken);
        await Task.WhenAll(outputTask, errorTask);
        return new ProcessResult(process.ExitCode, output.ToString(), error.ToString());
    }

    private static async Task ReadLinesAsync(
        StreamReader reader,
        StringBuilder destination,
        Action<string>? reportProgress)
    {
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            destination.AppendLine(line);
            if (!string.IsNullOrWhiteSpace(line))
                reportProgress?.Invoke(line);
        }
    }

    private sealed record PythonCommand(string FileName, IReadOnlyList<string> PrefixArguments);
    private sealed record PythonRuntime(
        string FileName,
        IReadOnlyList<string> PrefixArguments,
        string PackageDirectory);
    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
