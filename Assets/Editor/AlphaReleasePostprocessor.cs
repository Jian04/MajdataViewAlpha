using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

internal sealed class AlphaReleasePostprocessor : IPostprocessBuildWithReport
{
    public int callbackOrder => 1000;

    public void OnPostprocessBuild(BuildReport report)
    {
        var summary = report.summary;
        if (summary.platform != BuildTarget.StandaloneWindows && summary.platform != BuildTarget.StandaloneWindows64)
            return;

        CollectReleaseFiles(summary.outputPath);

        // The player moved to App\MajdataView, so Unity auto-run only fails at the root with a system dialog
        if ((summary.options & BuildOptions.AutoRunPlayer) != 0)
            UnityEngine.Debug.LogWarning(
                "[MajdataViewAlpha] 请使用 Build 而不是 Build And Run:播放器已移入 App\\MajdataView," +
                "Unity 的自动运行会报“找不到文件”(构建本身已成功,可忽略该弹窗,手动运行 MajdataLauncher.exe)。");
    }

    private static void CollectReleaseFiles(string builtPlayerPath)
    {
        var projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                          ?? throw new BuildFailedException("Cannot resolve the Unity project directory.");
        var releaseRoot = Path.GetDirectoryName(builtPlayerPath)
                          ?? throw new BuildFailedException("Cannot resolve the player output directory.");
        var publishRoot = Path.Combine(projectRoot, "Library", "ReleasePublish");
        var tempRoot = Path.Combine(publishRoot, "temp");
        Directory.CreateDirectory(publishRoot);
        Directory.CreateDirectory(tempRoot);

        // Keep only the single-file Launcher and Pets assets at root; place View and Edit under App.
        // Never flatten both self-contained outputs into one directory: dependencies with the same
        // name, such as System.Drawing.Common, have different versions and would break Edit startup.
        var appRoot = Path.Combine(releaseRoot, "App");
        var viewRoot = Path.Combine(appRoot, "MajdataView");
        var editRoot = Path.Combine(appRoot, "MajdataEdit");
        var playerName = Path.GetFileNameWithoutExtension(builtPlayerPath);

        MovePlayerIntoAppFolder(releaseRoot, builtPlayerPath, viewRoot, playerName);

        // ReadyToRun avoids long first startup from antivirus scanning plus JIT in self-contained builds
        var editOutput = Publish(projectRoot, "MajdataEdit", publishRoot, tempRoot,
            "-p:PublishReadyToRun=true");
        var launcherOutput = Publish(projectRoot, "MajdataLauncher", publishRoot, tempRoot,
            "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true");
        CopyDirectory(editOutput, editRoot);
        CopyLauncherToRoot(launcherOutput, releaseRoot);
        // Single-file publishing bundles PNG/WebP content but leaves JSON external, so copy pet assets explicitly
        CopyDirectory(Path.Combine(projectRoot, "MajdataLauncher", "Pets"), Path.Combine(releaseRoot, "Pets"));

        var skinSource = Path.Combine(projectRoot, "Skin");
        if (Directory.Exists(skinSource))
            CopyDirectory(skinSource, Path.Combine(editRoot, "Skin"));
        CollectChartLibrary(projectRoot, editRoot);
        CopyIfPresent(Path.Combine(projectRoot, "Assets", "StreamingAssets", "ffmpeg.exe"),
            Path.Combine(editRoot, "ffmpeg.exe"));
        RemoveObsoleteReleaseNotes(releaseRoot);
        CopyIfPresent(Path.Combine(projectRoot, "README.md"), Path.Combine(releaseRoot, "README.md"));

        ValidateRelease(viewRoot, editRoot, releaseRoot, playerName, !EditorUserBuildSettings.development);
        UnityEngine.Debug.Log($"[MajdataViewAlpha] Release dependencies collected in {releaseRoot}");
    }

    private static void MovePlayerIntoAppFolder(
        string releaseRoot, string builtPlayerPath, string viewRoot, string playerName)
    {
        // View contains only build output; recreate it on export to avoid mixing old and new player files
        if (Directory.Exists(viewRoot))
            Directory.Delete(viewRoot, true);
        Directory.CreateDirectory(viewRoot);

        foreach (var burstDebug in Directory.GetDirectories(releaseRoot,
                     playerName + "_BurstDebugInformation*", SearchOption.TopDirectoryOnly))
            Directory.Delete(burstDebug, true);

        var entries = new[]
        {
            Path.GetFileName(builtPlayerPath),
            playerName + "_Data",
            "UnityPlayer.dll",
            "UnityCrashHandler64.exe",
            "MonoBleedingEdge",
            "D3D12",
            "baselib.dll",
            "WinPixEventRuntime.dll",
            "dstorage.dll",
            "dstoragecore.dll"
        };
        foreach (var entry in entries)
        {
            var source = Path.Combine(releaseRoot, entry);
            var destination = Path.Combine(viewRoot, entry);
            if (Directory.Exists(source))
                Directory.Move(source, destination);
            else if (File.Exists(source))
                File.Move(source, destination);
        }
    }

    private static void CopyLauncherToRoot(string source, string releaseRoot)
    {
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            if (Path.GetExtension(file).Equals(".pdb", StringComparison.OrdinalIgnoreCase))
                continue;
            var target = Path.Combine(releaseRoot, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    private static void ValidateRelease(
        string viewRoot, string editRoot, string releaseRoot, string playerName, bool fullRelease)
    {
        var dataRoot = Path.Combine(viewRoot, playerName + "_Data");
        var required = new[]
        {
            Path.Combine(viewRoot, playerName + ".exe"),
            Path.Combine(dataRoot, "StreamingAssets", "ffmpeg.exe"),
            Path.Combine(dataRoot, "StreamingAssets", "ffarguments.txt"),
            Path.Combine(dataRoot, "StreamingAssets", "Skin"),
            Path.Combine(dataRoot, "StreamingAssets", "Background"),
            Path.Combine(editRoot, "MajdataEdit.exe"),
            Path.Combine(editRoot, "MajdataEdit.runtimeconfig.json"),
            Path.Combine(editRoot, "ffmpeg.exe"),
            Path.Combine(editRoot, "bass.dll"),
            Path.Combine(editRoot, "bass_fx.dll"),
            Path.Combine(editRoot, "ICSharpCode.AvalonEdit.dll"),
            Path.Combine(editRoot, "EditorSetting.json"),
            Path.Combine(editRoot, "slide_time.json"),
            Path.Combine(editRoot, "SFX"),
            Path.Combine(editRoot, "Themes"),
            Path.Combine(editRoot, "Skin"),
            Path.Combine(editRoot, "tools", "MaiMuriDX", "lib", "python.exe"),
            Path.Combine(editRoot, "tools", "MaiMuriDX", "lib", "cli.py"),
            Path.Combine(editRoot, "tools", "Maicaiyin", "infer.py"),
            Path.Combine(editRoot, "tools", "Maicaiyin", "joint-placement-numpy.npz"),
            Path.Combine(editRoot, "tools", "Maicaiyin", "python", "python.exe"),
            Path.Combine(editRoot, "tools", "Maicaiyin", "python", "python312.dll"),
            Path.Combine(editRoot, "tools", "Maicaiyin", "packages", "numpy-2.3.5.dist-info"),
            Path.Combine(editRoot, "tools", "Maicaiyin", "packages", "scipy-1.17.1.dist-info"),
            Path.Combine(editRoot, "tools", "Maicaiyin", "packages", "librosa-0.11.0.dist-info"),
            Path.Combine(editRoot, "tools", "Maicaiyin", "packages", "soundfile-0.14.0.dist-info"),
            Path.Combine(releaseRoot, "MajdataLauncher.exe"),
            Path.Combine(releaseRoot, "README.md"),
            Path.Combine(releaseRoot, "Pets", "dilaxiong", "pet.json"),
            Path.Combine(releaseRoot, "Pets", "dilaxiong", "spritesheet.png")
        };
        foreach (var path in required)
            RequirePath(path);

        if (!fullRelease)
            return;
        RequirePath(Path.Combine(editRoot, "charts"));
    }

    private static void RequirePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            throw new BuildFailedException($"Release dependency is missing: {path}");
    }

    private static void CollectChartLibrary(string projectRoot, string editRoot)
    {
        var configured = Environment.GetEnvironmentVariable("MAJDATA_CHART_LIBRARY");
        var repositoryRoot = Directory.GetParent(projectRoot)?.FullName;
        var candidates = new[]
        {
            configured,
            Path.Combine(projectRoot, "charts"),
            repositoryRoot == null
                ? null
                : Path.Combine(repositoryRoot, "release", "MaiChartAssistant", "charts")
        };
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate) || !Directory.Exists(candidate))
                continue;
            CopyDirectory(candidate, Path.Combine(editRoot, "charts"));
            return;
        }

        UnityEngine.Debug.LogWarning(
            "[MajdataViewAlpha] Chart library was not found. " +
            "Set MAJDATA_CHART_LIBRARY before building to include charts in the release.");
    }

    private static string Publish(
        string projectRoot, string projectName, string publishRoot, string tempRoot,
        params string[] extraArguments)
    {
        var projectFile = Path.Combine(projectRoot, projectName, projectName + ".csproj");
        if (!File.Exists(projectFile))
            throw new BuildFailedException($"Missing release project: {projectFile}");

        var workRoot = Path.Combine(publishRoot, projectName);
        var output = Path.Combine(workRoot, "publish");
        if (Directory.Exists(workRoot))
            Directory.Delete(workRoot, true);
        Directory.CreateDirectory(output);

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = projectRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        var intermediateRoot = Path.Combine(workRoot, "obj") + Path.DirectorySeparatorChar;
        var buildOutputRoot = Path.Combine(workRoot, "bin") + Path.DirectorySeparatorChar;
        foreach (var argument in new[]
                 {
                     "publish", projectFile, "-c", "Release", "-r", "win-x64", "--self-contained", "true",
                     "--nologo", "-o", output,
                     $"-p:BaseIntermediateOutputPath={intermediateRoot}",
                     $"-p:BaseOutputPath={buildOutputRoot}"
                 })
            startInfo.ArgumentList.Add(argument);
        foreach (var argument in extraArguments)
            startInfo.ArgumentList.Add(argument);
        startInfo.Environment["TEMP"] = tempRoot;
        startInfo.Environment["TMP"] = tempRoot;
        startInfo.Environment["DOTNET_CLI_HOME"] = Path.Combine(tempRoot, "dotnet-home");

        using var process = Process.Start(startInfo)
                            ?? throw new BuildFailedException($"Failed to start dotnet publish for {projectName}.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        var standardOutput = outputTask.GetAwaiter().GetResult();
        var standardError = errorTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
            throw new BuildFailedException(
                $"dotnet publish failed for {projectName}.\n{standardOutput}\n{standardError}");
        return output;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    private static void CopyIfPresent(string source, string destination)
    {
        if (File.Exists(source))
            File.Copy(source, destination, true);
    }

    private static void RemoveObsoleteReleaseNotes(string releaseRoot)
    {
        foreach (var path in Directory.GetFiles(
                     releaseRoot, "RELEASE_NOTES*.md", SearchOption.TopDirectoryOnly))
            File.Delete(path);
    }
}
