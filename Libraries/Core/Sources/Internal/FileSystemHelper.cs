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
using System.IO;
namespace Cube.FileSystem.SevenZip;

/// <summary>
/// ファイル I/O に関する共通ヘルパー。
/// </summary>
/// <remarks>
/// P2-17: <c>ArchiveWriter</c> と <c>UpdateCallback</c> に重複していた
/// <c>IsFileLocked</c> を共通化。
/// </remarks>
internal static class FileSystemHelper
{
    /// <summary>
    /// HResult が共有違反 (Sharing/Lock Violation) かどうかを判定する。
    /// </summary>
    /// <param name="ex">捕捉した IOException。</param>
    /// <returns>他プロセスがファイルをロックしている場合は true。</returns>
    public static bool IsFileLocked(IOException ex)
    {
        const int SharingViolation = unchecked((int)0x80070020);
        const int LockViolation    = unchecked((int)0x80070021);
        return ex.HResult == SharingViolation || ex.HResult == LockViolation;
    }
}
