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
namespace Cube.FileSystem.SevenZip;

/// <summary>
/// <see cref="ArchiveWriter.FileSkipped"/> イベントの引数。
/// <see cref="CompressionOption.SkipInaccessibleFiles"/> が true のとき、
/// アクセス不能ファイルをスキップした際に発火する。
/// </summary>
public class FileSkippedEventArgs : EventArgs
{
    /// <summary>
    /// スキップしたファイルの絶対パス。
    /// </summary>
    public string FullName { get; init; }

    /// <summary>
    /// アーカイブ内に追加されるはずだった相対パス。
    /// </summary>
    public string RelativeName { get; init; }

    /// <summary>
    /// スキップの原因となった例外。
    /// 通常は <see cref="AccessException"/> でラップされたファイル共有違反 (SharingViolation /
    /// LockViolation) や権限不足 (UnauthorizedAccessException)。
    /// </summary>
    public Exception Reason { get; init; }
}
