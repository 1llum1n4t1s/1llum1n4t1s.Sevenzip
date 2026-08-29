/* ------------------------------------------------------------------------- */
//
// Copyright (c) 2010 CubeSoft, Inc.
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as
// published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.
//
/* ------------------------------------------------------------------------- */
using System;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
namespace Cube.FileSystem.SevenZip.Kernel32;

/* ------------------------------------------------------------------------- */
///
/// Kernel32.NativeMethods
///
/// <summary>
/// Provides native methods defined in the kernel32.dll.
/// </summary>
///
/* ------------------------------------------------------------------------- */
internal static partial class NativeMethods
{
    #region Methods

    /* --------------------------------------------------------------------- */
    ///
    /// LoadLibrary
    ///
    /// <summary>
    /// https://msdn.microsoft.com/en-us/library/windows/desktop/ms684175.aspx
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [LibraryImport(LibName, EntryPoint = "LoadLibraryW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    public static partial SafeLibraryHandle LoadLibrary(string lpFileName);

    /* --------------------------------------------------------------------- */
    ///
    /// GetProcAddress
    ///
    /// <summary>
    /// https://msdn.microsoft.com/en-us/library/windows/desktop/ms683212.aspx
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [LibraryImport(LibName, SetLastError = true)]
    public static partial IntPtr GetProcAddress(
        SafeLibraryHandle hModule,
        [MarshalUsing(typeof(AnsiStringMarshaller))] string procName
    );

    /* --------------------------------------------------------------------- */
    ///
    /// FreeLibrary
    ///
    /// <summary>
    /// https://msdn.microsoft.com/en-us/library/windows/desktop/ms683152.aspx
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [LibraryImport(LibName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FreeLibrary(IntPtr hModule);

    /* --------------------------------------------------------------------- */
    ///
    /// CreateFile
    ///
    /// <summary>
    /// Opens a file system object without automatically following its final
    /// reparse point.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [LibraryImport(LibName, EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    public static partial SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile
    );

    #endregion

    #region Fields
    private const string LibName = "kernel32.dll";
    #endregion
}
