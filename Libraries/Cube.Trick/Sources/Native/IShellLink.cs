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
namespace Cube;

/* ------------------------------------------------------------------------- */
///
/// IShellLink
///
/// <summary>
/// https://msdn.microsoft.com/en-us/library/windows/desktop/bb774950.aspx
/// </summary>
///
/* ------------------------------------------------------------------------- */
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("000214F9-0000-0000-C000-000000000046")]
internal unsafe partial interface IShellLink
{
    void GetPath(char* pszFile, int cchMaxPath, IntPtr pfd, int fFlags);
    void GetIDList(out IntPtr ppidl);
    void SetIDList(IntPtr pidl);
    void GetDescription(char* pszName, int cchMaxName);
    void SetDescription(string pszName);
    void GetWorkingDirectory(char* pszDir, int cchMaxPath);
    void SetWorkingDirectory(string pszDir);
    void GetArguments(char* pszArgs, int cchMaxPath);
    void SetArguments(string pszArgs);
    void GetHotkey(out short pwHotkey);
    void SetHotkey(short wHotkey);
    void GetShowCmd(out int piShowCmd);
    void SetShowCmd(int iShowCmd);
    void GetIconLocation(char* pszIconPath, int cchIconPath, out int piIcon);
    void SetIconLocation(string pszIconPath, int iIcon);
    void SetRelativePath(string pszPathRel, int dwReserved);
    void Resolve(IntPtr hwnd, int fFlags);
    void SetPath(string pszFile);
}
