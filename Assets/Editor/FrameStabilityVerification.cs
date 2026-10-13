using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class FrameStabilityVerification
{
    private const string Pending = "AlphaFrameStabilityVerification";
    static FrameStabilityVerification()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false);
            var probe = new GameObject("Frame stability verification");
            UnityEngine.Object.DontDestroyOnLoad(probe);
            probe.AddComponent<FrameStabilityProbe>();
        };
    }
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Server.unity");
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }
    public static void BuildPlayer()
    {
        var previousTiming = PlayerSettings.enableFrameTimingStats;
        try
        {
            PlayerSettings.enableFrameTimingStats = true;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray(),
                locationPathName = Path.GetFullPath(".codex-build/FrameTest/MajdataView.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development,
                extraScriptingDefines = new[] { "ALPHA_FRAME_VERIFICATION" }
            });
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new Exception("Stress player build failed");
        }
        finally { PlayerSettings.enableFrameTimingStats = previousTiming; }
    }
}

