/*
  Copyright (c) Moying-moe All rights reserved. Licensed under the MIT license.
  See LICENSE in the project root for license information.
*/

using System.IO;

namespace MajdataEdit.AutoSaveModule;

public class AutoSaveRecoverer : IAutoSaveRecoverer
{
    private readonly IAutoSaveContext globalContext = new GlobalAutoSaveContext();
    private readonly IAutoSaveIndexManager globalIndex;
    private readonly IAutoSaveContext localContext = new LocalAutoSaveContext();
    private readonly IAutoSaveIndexManager localIndex;

    public AutoSaveRecoverer()
    {
        localIndex = new AutoSaveIndexManager(AutoSaveManager.LOCAL_AUTOSAVE_MAX_COUNT);
        try
        {
            localIndex.ChangePath(localContext.GetSavePath());
        }
        catch (LocalDirNotOpenYetException)
        {
        }

        globalIndex = new AutoSaveIndexManager(AutoSaveManager.GLOBAL_AUTOSAVE_MAX_COUNT);
        globalIndex.ChangePath(globalContext.GetSavePath());
    }

    public List<AutoSaveIndex.FileInfo> GetLocalAutoSaves()
    {
        var result = new List<AutoSaveIndex.FileInfo>();

        try
        {
            localIndex.ChangePath(localContext.GetSavePath());
        }
        catch (LocalDirNotOpenYetException)
        {
            return result;
        }

        result.AddRange(localIndex.GetFileInfos());
        result.Sort(delegate(AutoSaveIndex.FileInfo f1, AutoSaveIndex.FileInfo f2)
        {
            return f2.SavedTime.CompareTo(f1.SavedTime);
        });

        return result;
    }

    public List<AutoSaveIndex.FileInfo> GetGlobalAutoSaves()
    {
        var result = new List<AutoSaveIndex.FileInfo>();
        result.AddRange(globalIndex.GetFileInfos());
        result.Sort(delegate(AutoSaveIndex.FileInfo f1, AutoSaveIndex.FileInfo f2)
        {
            return f2.SavedTime.CompareTo(f1.SavedTime);
        });

        return result;
    }

    public List<AutoSaveIndex.FileInfo> GetAllAutoSaves()
    {
        var result = new List<AutoSaveIndex.FileInfo>();

        result.AddRange(GetLocalAutoSaves());
        result.AddRange(GetGlobalAutoSaves());

        return result;
    }

    public FumenInfos GetFumenInfos(string path)
    {
        return FumenInfos.FromFile(path);
    }

    public bool RecoverFile(AutoSaveIndex.FileInfo recoveredFileInfo)
    {
        // Original maidata path
        var rawMaidataPath = recoveredFileInfo.RawPath + "/maidata.txt";
        // Backup path for the original maidata before recovery
        var backupMaidataPath = recoveredFileInfo.RawPath + "/maidata.before_recovery.txt";
        // Autosaved maidata path
        var autosaveMaidataPath = recoveredFileInfo.FileName;

        try
        {
            // Delete the previous backup, if any.
            if (File.Exists(backupMaidataPath)) File.Delete(backupMaidataPath);
            // Back up maidata before recovery.
            File.Move(rawMaidataPath, backupMaidataPath);
            // Restore the autosaved maidata to its original directory.
            File.Copy(autosaveMaidataPath!, rawMaidataPath);
        }
        catch
        {
            return false;
        }

        return true;
    }
}