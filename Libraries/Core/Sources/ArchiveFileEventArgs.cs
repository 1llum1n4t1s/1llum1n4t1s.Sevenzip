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
/// 圧縮・解凍処理中に単一ファイルの処理開始・終了を通知するイベント引数。
/// </summary>
/// <remarks>
/// <see cref="ArchiveReader.FileExtracting"/> /
/// <see cref="ArchiveReader.FileExtracted"/> /
/// <see cref="ArchiveWriter.FileCompressing"/> /
/// <see cref="ArchiveWriter.FileCompressed"/> で使用する。
/// </remarks>
public class ArchiveFileEventArgs : EventArgs
{
    /// <summary>
    /// 処理中のエントリ情報を取得する。
    /// 呼び出し時点で null の場合がある（取得途中のコールバックなど）。
    /// </summary>
    public Entity Target { get; init; }

    /// <summary>
    /// アーカイブ内のエントリインデックスを取得する。
    /// 未確定の場合は -1。
    /// </summary>
    public int Index { get; init; } = -1;

    /// <summary>
    /// 処理済みファイル数を取得する。
    /// </summary>
    public long Count { get; init; }

    /// <summary>
    /// 処理対象の総ファイル数を取得する。
    /// </summary>
    public long TotalCount { get; init; }

    /// <summary>
    /// 処理のキャンセルを要求するフラグを取得または設定する。
    /// </summary>
    /// <remarks>
    /// イベントハンドラ内で true に設定すると、次のコールバックで
    /// キャンセルコード (SevenZipCode.Cancel) が 7z.dll に伝播して処理が中断される。
    /// </remarks>
    public bool Cancel { get; set; }
}
