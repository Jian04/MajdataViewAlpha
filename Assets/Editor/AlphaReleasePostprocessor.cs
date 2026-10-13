using System;
using System.Collections.Generic;
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
        ArchiveBuildBackup(projectRoot, releaseRoot, Path.GetFileNameWithoutExtension(builtPlayerPath));
        // Also accept building directly to the packaged View directory.
        var outputDirectory = new DirectoryInfo(releaseRoot);
        if (outputDirectory.Name.Equals("MajdataView", StringComparison.OrdinalIgnoreCase) &&
            outputDirectory.Parent?.Name.Equals("App", StringComparison.OrdinalIgnoreCase) == true)
            releaseRoot = outputDirectory.Parent.Parent!.FullName;
        var dotnet = ResolveDotNetSdk(projectRoot);
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

        // ReadyToRun avoids long first startup from antivirus scanning plus JIT in self-contained builds
        var editOutput = Publish(dotnet, projectRoot, "MajdataEdit", publishRoot, tempRoot,
            "-p:PublishReadyToRun=true");
        var launcherOutput = Publish(dotnet, projectRoot, "MajdataLauncher", publishRoot, tempRoot,
            "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true");
        MovePlayerIntoAppFolder(Path.GetDirectoryName(builtPlayerPath)!, builtPlayerPath, viewRoot, playerName);
        CopyDirectory(editOutput, editRoot, leanEditor: true);
        CopyLauncherToRoot(launcherOutput, releaseRoot);
        // Single-file publishing bundles PNG/WebP content but leaves JSON external, so copy pet assets explicitly
        CopyDirectory(Path.Combine(projectRoot, "MajdataLauncher", "Pets"), Path.Combine(releaseRoot, "Pets"));

        var skinSource = Path.Combine(projectRoot, "Skin");
        if (Directory.Exists(skinSource))
            CopyDirectory(skinSource, Path.Combine(editRoot, "Skin"));

        // Edit and View share the copy in View's StreamingAssets.
        PruneRetiredEditorFiles(editRoot);
        RemoveObsoleteReleaseNotes(releaseRoot);
        CopyIfPresent(Path.Combine(projectRoot, "README.md"), Path.Combine(releaseRoot, "README.md"));
        var licenses=Path.Combine(projectRoot,"Licenses");
        if(Directory.Exists(licenses)) CopyDirectory(licenses,Path.Combine(releaseRoot,"Licenses"));

        ValidateRelease(viewRoot, editRoot, releaseRoot, playerName, !EditorUserBuildSettings.development);
        UnityEngine.Debug.Log($"[MajdataViewAlpha] Release dependencies collected in {releaseRoot}");
    }

    private static void ArchiveBuildBackup(string projectRoot, string outputDirectory, string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName) || Path.GetFileName(playerName) != playerName)
            throw new BuildFailedException("Invalid player name for build backup.");
        var outputRoot = Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var source = Path.GetFullPath(Path.Combine(outputRoot, playerName + "_BackUpThisFolder_ButDontShipItWithYourGame"));
        if (!source.StartsWith(outputRoot, StringComparison.OrdinalIgnoreCase))
            throw new BuildFailedException("Build backup is outside the player output directory.");
        if (!Directory.Exists(source)) return;
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            throw new BuildFailedException("Refusing to move a linked build backup directory.");
        var archiveRoot = Path.Combine(projectRoot, "Library", "BuildBackups");
        Directory.CreateDirectory(archiveRoot);
        var destination = Path.Combine(archiveRoot, playerName + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.Move(source, destination);
        UnityEngine.Debug.Log($"[MajdataViewAlpha] Build debugging files archived in {destination}");
    }

    private static void MovePlayerIntoAppFolder(
        string releaseRoot, string builtPlayerPath, string viewRoot, string playerName)
    {
        if (Path.GetFullPath(releaseRoot).TrimEnd(Path.DirectorySeparatorChar)
            .Equals(Path.GetFullPath(viewRoot).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
            return;
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
            Path.Combine(releaseRoot, "MajdataLauncher.exe"),
            Path.Combine(releaseRoot, "README.md"),
            Path.Combine(releaseRoot, "Pets", "dilaxiong", "pet.json"),
            Path.Combine(releaseRoot, "Pets", "dilaxiong", "spritesheet.png"),
            Path.Combine(releaseRoot, "Pets", "uni", "pet.json"),
            Path.Combine(releaseRoot, "Pets", "uni", "spritesheet.png")
        };
        foreach (var path in required)
            RequirePath(path);

        if (!fullRelease)
            return;

    }

    private static void RequirePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            throw new BuildFailedException($"Release dependency is missing: {path}");
    }

    private static string ResolveDotNetSdk(string projectRoot)
    {
        var candidates = new List<string>
        {
            Environment.GetEnvironmentVariable("MAJDATA_DOTNET"),
            Path.Combine(projectRoot, ".codex-build", "dotnet", "dotnet.exe")
        };
        foreach (var root in new[]
                 {
                     Environment.GetEnvironmentVariable("DOTNET_ROOT_X64"),
                     Environment.GetEnvironmentVariable("DOTNET_ROOT"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet")
                 })
            if (!string.IsNullOrWhiteSpace(root))
                candidates.Add(Path.Combine(root, "dotnet.exe"));
        candidates.Add("dotnet");

        var attempted = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate) || !seen.Add(candidate))
                continue;
            try
            {
                // A runtime-only host exists and starts, but cannot publish.
                // --version also checks SDK selection under this project's global.json.
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = candidate,
                    Arguments = "--version",
                    WorkingDirectory = projectRoot,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });
                if (process == null) continue;
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(15000))
                {
                    process.Kill();
                    attempted.Add(candidate + ": SDK probe timed out");
                    continue;
                }
                var version = output.GetAwaiter().GetResult().Trim();
                var failure = error.GetAwaiter().GetResult().Trim();
                if (process.ExitCode == 0 &&
                    Version.TryParse(version.Split('-')[0], out var sdkVersion) && sdkVersion.Major >= 6)
                {
                    UnityEngine.Debug.Log($"[MajdataViewAlpha] Desktop SDK: {candidate} ({version})");
                    return candidate;
                }
                attempted.Add(candidate + ": " + version + " " + failure);
            }
            catch (Exception exception)
            {
                attempted.Add(candidate + ": " + exception.Message);
            }
        }
        throw new BuildFailedException(
            "未找到可用的 .NET SDK（仅安装 Runtime 不能打包 Edit / Launcher）。" +
            "安装 .NET 8 SDK，或将 MAJDATA_DOTNET 设置为含 SDK 的 dotnet.exe 完整路径。\n" +
            string.Join("\n", attempted));
    }

    private static string Publish(
        string dotnet, string projectRoot, string projectName, string publishRoot, string tempRoot,
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
            FileName = dotnet,
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
        var packageCache = Path.Combine(projectRoot, ".codex-build", "nuget");
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NUGET_PACKAGES")) &&
            Directory.Exists(packageCache))
            startInfo.Environment["NUGET_PACKAGES"] = packageCache;

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

    private static bool ExcludeEditorPayload(string relative)
    {
        var path = relative.Replace('\\', '/');
        return path.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("tools/Maicaiyin/", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("ChartSearchCache/", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("charts/", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".pyc", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("/__pycache__/");
    }

    private static void PruneRetiredEditorFiles(string editRoot)
    {
        var root = Path.GetFullPath(editRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var relative in new[] { "tools/Maicaiyin", "ChartSearchCache", "ffmpeg.exe" })
        {
            var path = Path.GetFullPath(Path.Combine(root, relative));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new BuildFailedException("Invalid release path.");
            if (Directory.Exists(path)) Directory.Delete(path, true);
            else if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void CopyDirectory(string source, string destination, bool leanEditor = false)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (leanEditor && ExcludeEditorPayload(relative)) continue;
            var target = Path.Combine(destination, relative);
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
