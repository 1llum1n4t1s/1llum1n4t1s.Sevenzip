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
using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
namespace Cube.Forms;

/* ------------------------------------------------------------------------- */
///
/// IDocHostShowUI
///
/// <summary>
/// https://msdn.microsoft.com/en-us/library/aa753269.aspx
/// </summary>
///
/* ------------------------------------------------------------------------- */
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("C4D244B0-D43E-11CF-893B-00AA00BDCE1A")]
internal partial interface IDocHostShowUI
{
    #region Methods

    /* --------------------------------------------------------------------- */
    ///
    /// ShowMessage
    ///
    /// <summary>
    /// Called by MSHTML to display a message box.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [PreserveSig]
    int ShowMessage(IntPtr hwnd,
        string lpstrText,
        string lpstrCaption,
        int dwType,
        string lpstrHelpFile,
        int dwHelpContext,
        out int lpResult
    );

    /* --------------------------------------------------------------------- */
    ///
    /// ShowHelp
    ///
    /// <summary>
    /// Called by MSHTML to display Help.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [PreserveSig]
    int ShowHelp(
        IntPtr hwnd,
        string pszHelpFile,
        int uCommand,
        int dwData,
        IntPtr ptMouse,
        nint pDispatchObjectHit
    );

    #endregion
}
