using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class AlphaCommandLineBuild
{
    public static void BuildOct12CheckedView()
    {
        DjAutoHoldVerification.Run();
        DjAutoOverlapVerification.Run();
        CoverCacheVerification.Run();
        CoverCacheVerification.RenderSamplingComparison();
        BuildView();
    }
    public static void BuildOct11CheckedView()
    {
        StarDisplayOrderVerification.Run();
        DjAutoHoldVerification.Run();
        DjAutoOverlapVerification.Run();
        BuildView();
    }
    public static void BuildCoverCheckedView() { CoverCacheVerification.Run(); BuildView(); }
    public static void BuildOct10View() { DjAutoHoldVerification.Run(); BuildView(); }
    public static void BuildNoiseCheckedView() { NoiseZoneVerification.Run(); BuildView(); }
    public static void BuildOct08View() { NoiseZoneVerification.Run(); RotatePvVerification.Run(); BuildView(); }
    public static void BuildGradientCheckedView()
    {
        VignetteGradientVerification.Run();
        BuildView();
    }
    // Unity -batchmode -quit -projectPath <alpha> -executeMethod AlphaCommandLineBuild.BuildView
    public static void BuildView()
    {
        var root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        var output = Path.Combine(root, ".codex-build", "ViewBuild", "MajdataView.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled)
            .Select(scene => scene.path).ToArray();
        if (scenes.Length == 0)
            throw new InvalidOperationException("No enabled Unity scenes are configured.");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = output,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException($"View build failed: {report.summary.result}");
    }
}
