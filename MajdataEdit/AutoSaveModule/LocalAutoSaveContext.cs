/*
  Copyright (c) Moying-moe All rights reserved. Licensed under the MIT license.
  See LICENSE in the project root for license information.
*/

namespace MajdataEdit.AutoSaveModule;

/// <summary>
///     Local autosave context.
/// </summary>
public class LocalAutoSaveContext : IAutoSaveContext
{
    public string GetSavePath()
    {
        var maidataDir = MainWindow.maidataDir;
        if (maidataDir.Length == 0) throw new LocalDirNotOpenYetException();

        return MainWindow.maidataDir + "/.autosave";
    }
}