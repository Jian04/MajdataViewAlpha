using UnityEditor;
using UnityEngine;

public static class MajdataDebugBootstrap
{
    public static void StartPlayMode()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            Debug.Log("[MajdataDebug] Entering Play Mode for MajdataEdit.");
            EditorApplication.isPlaying = true;
        };
    }
}
