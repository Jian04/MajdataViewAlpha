/*
  Copyright (c) Moying-moe All rights reserved. Licensed under the MIT license.
  See LICENSE in the project root for license information.
*/

namespace MajdataEdit.AutoSaveModule;

/// <summary>
///     Global autosave.
///     Stores autosave files in the Majdata root directory.
/// </summary>
public class GlobalAutoSave : IAutoSave
{
    private readonly IAutoSaveIndexManager indexManager = new AutoSaveIndexManager();
    private readonly IAutoSaveContext saveContext = new GlobalAutoSaveContext();

    public GlobalAutoSave()
    {
        indexManager.ChangePath(saveContext.GetSavePath());
        indexManager.SetMaxAutoSaveCount(AutoSaveManager.GLOBAL_AUTOSAVE_MAX_COUNT);
    }


    public bool DoAutoSave()
    {
        var newSaveFilePath = indexManager.GetNewAutoSaveFileName();

        SimaiProcess.SaveData(newSaveFilePath);

        indexManager.RefreshIndex();

        return true;
    }
}