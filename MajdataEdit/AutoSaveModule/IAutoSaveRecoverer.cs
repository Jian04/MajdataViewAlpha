/*
  Copyright (c) Moying-moe All rights reserved. Licensed under the MIT license.
  See LICENSE in the project root for license information.
*/

namespace MajdataEdit.AutoSaveModule;

internal interface IAutoSaveRecoverer
{
    /// <summary>
    ///     Gets the local autosave file list.
    /// </summary>
    /// <returns></returns>
    List<AutoSaveIndex.FileInfo> GetLocalAutoSaves();

    /// <summary>
    ///     Gets the global autosave file list.
    /// </summary>
    /// <returns></returns>
    List<AutoSaveIndex.FileInfo> GetGlobalAutoSaves();

    /// <summary>
    ///     Gets all local and global autosave files.
    /// </summary>
    /// <returns></returns>
    List<AutoSaveIndex.FileInfo> GetAllAutoSaves();

    /// <summary>
    ///     Gets chart information for the specified path.
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    FumenInfos GetFumenInfos(string path);

    /// <summary>
    ///     Restores a file from recoveredFileInfo.
    /// </summary>
    /// <param name="recoveredFileInfo"></param>
    /// <returns></returns>
    bool RecoverFile(AutoSaveIndex.FileInfo recoveredFileInfo);
}