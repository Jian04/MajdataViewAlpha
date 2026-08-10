/*
  Copyright (c) Moying-moe All rights reserved. Licensed under the MIT license.
  See LICENSE in the project root for license information.
*/

namespace MajdataEdit.AutoSaveModule;

/// <summary>
///     Index of autosave files in the current environment.
/// </summary>
public class AutoSaveIndex
{
    /// <summary>
    ///     Number of existing autosave files.
    /// </summary>
    public int Count = 0;

    /// <summary>
    ///     Autosave file list.
    /// </summary>
    public List<FileInfo> FilesInfo = new();

    public class FileInfo
    {
        /// <summary>
        ///     Autosave filename.
        /// </summary>
        public string? FileName;

        /// <summary>
        ///     Original file path.
        /// </summary>
        public string? RawPath;

        /// <summary>
        ///     Autosave time.
        /// </summary>
        public long SavedTime;
    }
}