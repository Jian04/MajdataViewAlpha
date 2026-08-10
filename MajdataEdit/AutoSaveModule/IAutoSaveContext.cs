/*
  Copyright (c) Moying-moe All rights reserved. Licensed under the MIT license.
  See LICENSE in the project root for license information.
*/

namespace MajdataEdit.AutoSaveModule;

/// <summary>
///     Autosave context interface.
///     Provides context required for autosave, such as paths.
/// </summary>
internal interface IAutoSaveContext
{
    /// <summary>
    ///     Gets the save path without a filename or trailing slash.
    /// </summary>
    /// <returns></returns>
    string GetSavePath();
}