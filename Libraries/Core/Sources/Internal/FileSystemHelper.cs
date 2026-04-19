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
/// <c>ArchiveWriter</c> と <c>UpdateCallback</c> で共有する <c>IsFileLocked</c> 判定と
/// 1MB バッファサイズを集約する。
/// </summary>
internal static class FileSystemHelper
{
    /// <summary>
    /// ストリームコピーに使用するデフォルトバッファサイズ (1MB)。
    /// </summary>
    /// <remarks>
    /// write syscall 数の削減と LOH 割り当て回避のバランスで 1MB を採用。
    /// </remarks>
    public const int DefaultBufferSize = 1024 * 1024;

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
