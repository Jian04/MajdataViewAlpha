/*
  Copyright (c) Moying-moe All rights reserved. Licensed under the MIT license.
  See LICENSE in the project root for license information.
*/

namespace MajdataEdit.AutoSaveModule;

/// <summary>
///     Local autosave.
///     Stores autosave files in the current chart directory.
/// </summary>
public class LocalAutoSave : IAutoSave
{
    private readonly IAutoSaveIndexManager indexManager = new AutoSaveIndexManager();
    private readonly IAutoSaveContext saveContext = new LocalAutoSaveContext();

    public LocalAutoSave()
    {
        indexManager.SetMaxAutoSaveCount(AutoSaveManager.LOCAL_AUTOSAVE_MAX_COUNT);
    }


    public bool DoAutoSave()
    {
        // Before local autosave, always try to update the current directory to the open folder.
        indexManager.ChangePath(saveContext.GetSavePath());

        var newSaveFilePath = indexManager.GetNewAutoSaveFileName();

        SimaiProcess.SaveData(newSaveFilePath);

        indexManager.RefreshIndex();

        return true;
    }
}