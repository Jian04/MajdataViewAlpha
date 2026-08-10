/*
  Copyright (c) Moying-moe All rights reserved. Licensed under the MIT license.
  See LICENSE in the project root for license information.
*/

using System.Timers;
using Timer = System.Timers.Timer;

namespace MajdataEdit.AutoSaveModule;

/// <summary>
///     Autosave manager.
///     **Runs as a singleton.**
///     Schedules autosave operations and manages IAutoSave implementations.
/// </summary>
public sealed class AutoSaveManager
{
    public static readonly int LOCAL_AUTOSAVE_MAX_COUNT = 5;
    public static readonly int GLOBAL_AUTOSAVE_MAX_COUNT = 30;

    private readonly List<IAutoSave> autoSavers = new();

    /// <summary>
    ///     Autosave timer, which checks every 60 seconds by default.
    /// </summary>
    private readonly Timer autoSaveTimer = new(1000 * 60);

    /// <summary>
    ///     Whether changes have occurred since the last save.
    /// </summary>
    private bool isFileChanged;


    /// <summary>
    ///     Constructor.
    /// </summary>
    private AutoSaveManager()
    {
        // Local and global autosave providers
        autoSavers.Add(new LocalAutoSave());
        autoSavers.Add(new GlobalAutoSave());

        // Save event
        autoSaveTimer.AutoReset = true;
        autoSaveTimer.Elapsed += autoSaveTimer_Elapsed;
    }

    /// <summary>
    ///     Gets the autosave timer interval.
    /// </summary>
    /// <returns></returns>
    public double GetAutoSaveTimerInterval()
    {
        return autoSaveTimer.Interval;
    }

    /// <summary>
    ///     Sets the autosave timer interval.
    /// </summary>
    /// <param name="interval"></param>
    public void SetAutoSaveTimerInterval(double interval)
    {
        autoSaveTimer.Interval = interval;
    }

    /// <summary>
    ///     Marks the file as changed.
    /// </summary>
    public void SetFileChanged()
    {
        isFileChanged = true;
    }

    /// <summary>
    ///     Handles timer events.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void autoSaveTimer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        // Skip autosave if the file has not changed.
        if (!isFileChanged) return;

        // Perform autosave.
        foreach (var saver in autoSavers) saver.DoAutoSave();

        // Mark the changes as saved.
        isFileChanged = false;
    }

    public void SetAutoSaveEnable(bool enabled)
    {
        if (enabled)
            autoSaveTimer.Start();
        else
            autoSaveTimer.Stop();
    }

    #region Singleton

    private static volatile AutoSaveManager? _instance;
    private static readonly object syncLock = new();

    public static AutoSaveManager Of()
    {
        if (_instance == null)
            lock (syncLock)
            {
                if (_instance == null) _instance = new AutoSaveManager();
            }

        return _instance;
    }

    #endregion
}