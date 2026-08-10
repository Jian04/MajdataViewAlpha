/*
  Copyright (c) Moying-moe All rights reserved. Licensed under the MIT license.
  See LICENSE in the project root for license information.
*/

namespace MajdataEdit.AutoSaveModule;

/// <summary>
///     Autosave index-file management interface.
/// </summary>
internal interface IAutoSaveIndexManager
{
    /// <summary>
    ///     Changes the current working path.
    /// </summary>
    /// <param name="path"></param>
    void ChangePath(string path);

    /// <summary>
    ///     Gets the maximum number of autosave files.
    /// </summary>
    /// <returns></returns>
    int GetMaxAutoSaveCount();

    /// <summary>
    ///     Sets the maximum number of autosave files.
    /// </summary>
    /// <param name="maxAutoSaveCount"></param>
    void SetMaxAutoSaveCount(int maxAutoSaveCount);

    /// <summary>
    ///     Gets whether the index-file manager is ready.
    /// </summary>
    /// <returns>true if the manager is ready.</returns>
    bool IsReady();

    /// <summary>
    ///     Gets a new autosave filename.
    /// </summary>
    /// <returns></returns>
    string GetNewAutoSaveFileName();

    /// <summary>
    ///     Refreshes and maintains the index, deleting stale autosaves when the maximum is exceeded.
    /// </summary>
    void RefreshIndex();

    /// <summary>
    ///     Gets the current number of autosave files.
    /// </summary>
    /// <returns></returns>
    int GetFileCount();

    /// <summary>
    ///     Gets information about current autosave files.
    /// </summary>
    /// <returns></returns>
    List<AutoSaveIndex.FileInfo> GetFileInfos();
}