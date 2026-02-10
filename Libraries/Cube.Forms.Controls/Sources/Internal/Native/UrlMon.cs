/* ------------------------------------------------------------------------- */
//
// Copyright (c) 2010 CubeSoft, Inc.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//  http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
/* ------------------------------------------------------------------------- */
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
namespace Cube.Forms.UrlMon;

/* ------------------------------------------------------------------------- */
///
/// UrlMon.NativeMethods
///
/// <summary>
/// Provides functions defined in urlmon.dll.
/// </summary>
///
/* ------------------------------------------------------------------------- */
internal static partial class NativeMethods
{
    #region Methods

    /* --------------------------------------------------------------------- */
    ///
    /// UrlMkSetSessionOption
    ///
    /// <summary>
    /// https://msdn.microsoft.com/ja-jp/library/ms775125.aspx
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [LibraryImport(LibName)]
    public static partial int UrlMkSetSessionOption(int dwOption,
        [MarshalAs(UnmanagedType.LPStr)] string pBuffer,
        int dwBufferLength, int dwReserved);

    /* --------------------------------------------------------------------- */
    ///
    /// UrlMkGetSessionOption
    ///
    /// <summary>
    /// https://msdn.microsoft.com/ja-jp/library/ms775124.aspx
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [LibraryImport(LibName)]
    public static partial int UrlMkGetSessionOption(int dwOption,
        byte[] pBuffer,
        int dwBufferLength, ref int pdwBufferLength, int dwReserved);

    /* --------------------------------------------------------------------- */
    ///
    /// CoInternetIsFeatureEnabled
    ///
    /// <summary>
    /// https://msdn.microsoft.com/ja-jp/library/ms537164.aspx
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [LibraryImport(LibName)]
    public static partial int CoInternetIsFeatureEnabled(int featureEntry, int dwFlags);

    /* --------------------------------------------------------------------- */
    ///
    /// CoInternetSetFeatureEnabled
    ///
    /// <summary>
    /// https://msdn.microsoft.com/ja-jp/library/ms537168.aspx
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [LibraryImport(LibName)]
    public static partial int CoInternetSetFeatureEnabled(int featureEntry, int dwFlags,
        [MarshalAs(UnmanagedType.Bool)] bool fEnable);

    #endregion

    #region Fields

    private const string LibName = "urlmon.dll";
    #endregion
}
