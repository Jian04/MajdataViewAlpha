/*
  Copyright (c) Moying-moe All rights reserved. Licensed under the MIT license.
  See LICENSE in the project root for license information.
*/

namespace MajdataEdit.AutoSaveModule;

/// <summary>
///     Autosave operation interface.
///     Responsible only for performing autosave.
/// </summary>
internal interface IAutoSave
{
    /// <summary>
    ///     Performs an autosave.
    /// </summary>
    /// <returns>Whether the save succeeded.</returns>
    bool DoAutoSave();
}