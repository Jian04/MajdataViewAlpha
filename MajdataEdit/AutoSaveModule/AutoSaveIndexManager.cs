/*
  Copyright (c) Moying-moe All rights reserved. Licensed under the MIT license.
  See LICENSE in the project root for license information.
*/

using System.IO;
using Newtonsoft.Json;

namespace MajdataEdit.AutoSaveModule;

internal class AutoSaveIndexManager : IAutoSaveIndexManager
{
    private string? curPath;
    private AutoSaveIndex? index;
    private bool isReady;
    private int maxAutoSaveCount;

    public AutoSaveIndexManager()
    {
        maxAutoSaveCount = 5;
    }

    public AutoSaveIndexManager(int maxAutoSaveCount)
    {
        this.maxAutoSaveCount = maxAutoSaveCount;
    }

    public void ChangePath(string path)
    {
        if (path != curPath)
        {
            // Read or write the index only when the new directory differs from the configured directory.
            curPath = path;
            LoadOrCreateIndexFile();
        }

        isReady = true;
    }

    public int GetFileCount()
    {
        if (!IsReady()) throw new AutoSaveIndexNotReadyException("AutoSaveIndexManager is not ready yet.");

        return index!.Count;
    }

    public List<AutoSaveIndex.FileInfo> GetFileInfos()
    {
        if (!IsReady()) throw new AutoSaveIndexNotReadyException("AutoSaveIndexManager is not ready yet.");

        return index!.FilesInfo;
    }

    public int GetMaxAutoSaveCount()
    {
        return maxAutoSaveCount;
    }

    public string GetNewAutoSaveFileName()
    {
        var path = curPath + "/autosave." + GetCurrentTimeString() + ".txt";

        var fileInfo = new AutoSaveIndex.FileInfo
        {
            FileName = path,
            SavedTime = DateTimeOffset.Now.AddHours(8).ToUnixTimeSeconds(),
            RawPath = MainWindow.maidataDir
        };
        index!.FilesInfo.Add(fileInfo);

        index.Count++;

        // Store changes in the index file.
        UpdateIndexFile();

        return path;
    }

    public bool IsReady()
    {
        return isReady;
    }

    public void RefreshIndex()
    {
        // Scan first and remove entries for deleted files.
        for (var i = index!.Count - 1; i >= 0; i--)
        {
            var fileInfo = index.FilesInfo[i];
            if (!File.Exists(fileInfo.FileName))
            {
                index.FilesInfo.RemoveAt(i);
                index.Count--;
            }
        }

        // Delete from the start of this.index.FileInfo until the autosave count meets maxAutoSaveCount.
        while (index.Count > maxAutoSaveCount)
        {
            var fileInfo = index.FilesInfo[0];
            File.Delete(fileInfo.FileName!);
            index.FilesInfo.RemoveAt(0);
            index.Count--;
        }

        // Store changes in the index file.
        UpdateIndexFile();
    }

    public void SetMaxAutoSaveCount(int maxAutoSaveCount)
    {
        this.maxAutoSaveCount = maxAutoSaveCount;
        Console.WriteLine("maxAutoSaveCount:" + maxAutoSaveCount);
    }


    private void LoadOrCreateIndexFile()
    {
        CreateDirectoryIfNotExists(curPath!);
        KeepDirectoryHidden(curPath!);

        var indexFilePath = curPath + "/.index.json";
        if (!File.Exists(indexFilePath))
        {
            index = new AutoSaveIndex();
            UpdateIndexFile();
        }
        else
        {
            LoadIndexFromFile();
        }
    }


    /// <summary>
    ///     Creates the folder if it does not exist.
    /// </summary>
    /// <param name="dirPath"></param>
    private void CreateDirectoryIfNotExists(string dirPath)
    {
        if (!Directory.Exists(dirPath)) Directory.CreateDirectory(dirPath);
    }

    /// <summary>
    ///     Ensures the folder is hidden.
    /// </summary>
    /// <param name="dirPath"></param>
    private void KeepDirectoryHidden(string dirPath)
    {
        var dirInfo = new DirectoryInfo(dirPath);

        if ((dirInfo.Attributes & FileAttributes.Hidden) != FileAttributes.Hidden)
            dirInfo.Attributes = FileAttributes.Hidden;
    }

    /// <summary>
    ///     Stores saveIndex in the index file.
    /// </summary>
    private void UpdateIndexFile()
    {
        var indexPath = curPath + "/.index.json";

        var jsonText = JsonConvert.SerializeObject(index);
        File.WriteAllText(indexPath, jsonText);
    }

    /// <summary>
    ///     Reads saveIndex from the index file.
    /// </summary>
    private void LoadIndexFromFile()
    {
        var indexPath = curPath + "/.index.json";

        var jsonText = File.ReadAllText(indexPath);
        index = JsonConvert.DeserializeObject<AutoSaveIndex>(jsonText);
    }

    /// <summary>
    ///     Gets the current time string.
    /// </summary>
    /// <returns></returns>
    private string GetCurrentTimeString()
    {
        var now = DateTime.Now;

        return now.Year + "-" +
               now.Month + "-" +
               now.Day + "_" +
               now.Hour + "-" +
               now.Minute + "-" +
               now.Second;
    }
}