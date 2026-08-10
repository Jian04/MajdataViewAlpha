/*
  Copyright (c) Moying-moe All rights reserved. Licensed under the MIT license.
  See LICENSE in the project root for license information.
*/

using System.IO;

namespace MajdataEdit.AutoSaveModule;

/// <summary>
///     Abnormal-termination detector.
///     Runs as a singleton for the lifetime of Edit.
/// </summary>
public sealed class SafeTerminationDetector
{
    public readonly string RecordPath = Environment.CurrentDirectory + "/PROGRAM_RUNNING";

    private SafeTerminationDetector()
    {
    }

    /// <summary>
    ///     Checks whether the previous exit was normal.
    /// </summary>
    /// <returns>true if the previous exit was normal; otherwise false.</returns>
    public bool IsLastTerminationSafe()
    {
        if (File.Exists(RecordPath)) return false;

        return true;
    }

    /// <summary>
    ///     Call this function when starting the application.
    ///     **Important: call it before IsLastTerminationSafe!**
    /// </summary>
    public void RecordProgramStart()
    {
        File.WriteAllText(RecordPath, "");
    }

    public void ChangePath(string path)
    {
        File.WriteAllText(RecordPath, path);
    }

    /// <summary>
    ///     Call this function when exiting the application.
    /// </summary>
    public void RecordProgramClose()
    {
        File.Delete(RecordPath);
    }

    #region Singleton

    private static readonly SafeTerminationDetector _instance = new();

    public static SafeTerminationDetector Of()
    {
        return _instance;
    }

    #endregion
}