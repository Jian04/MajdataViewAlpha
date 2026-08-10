/*
  Copyright (c) Moying-moe All rights reserved. Licensed under the MIT license.
  See LICENSE in the project root for license information.
*/

namespace MajdataEdit.AutoSaveModule;

/// <summary>
///     Global autosave context.
/// </summary>
public class GlobalAutoSaveContext : IAutoSaveContext
{
    public string GetSavePath()
    {
        return Environment.CurrentDirectory + "/.autosave";
    }
}