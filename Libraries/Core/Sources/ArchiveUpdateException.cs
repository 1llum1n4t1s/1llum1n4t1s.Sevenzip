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
using System.IO;
namespace Cube.FileSystem.SevenZip;

/// <summary>
/// <see cref="ArchiveWriter.Update(string, string, string, IProgress{Report})"/> で
/// アーカイブ更新中にロールバック失敗が発生したときにスローされる構造化例外。
/// </summary>
/// <remarks>
/// <see cref="BackupPath"/> と <see cref="OriginalPath"/> を構造化プロパティとして公開し、
/// GUI アプリ等が「バックアップから復旧しますか?」といった UX を提供しやすくする。
/// 自動削除はせず、復旧判断は呼び出し側に委ねる (データ保全優先)。
/// </remarks>
[Serializable]
public class ArchiveUpdateException : IOException
{
    /// <summary>
    /// 元のアーカイブファイルのパス（上書き更新対象）。
    /// </summary>
    public string OriginalPath { get; }

    /// <summary>
    /// 保存されたバックアップファイルのパス（<c>{GUID}.bak</c>）。
    /// </summary>
    /// <remarks>
    /// 呼び出し側はこのパスを表示して「手動で復旧するか / 破棄するか」の
    /// 選択を提供できる。このライブラリは自動削除しない (データ保全優先)。
    /// </remarks>
    public string BackupPath { get; }

    /// <summary>
    /// 指定した情報で新しいインスタンスを初期化する。
    /// </summary>
    /// <param name="message">エラーメッセージ。</param>
    /// <param name="originalPath">元のアーカイブパス。</param>
    /// <param name="backupPath">バックアップファイルのパス。</param>
    /// <param name="innerException">内部例外。</param>
    public ArchiveUpdateException(string message, string originalPath, string backupPath,
        Exception innerException = null)
        : base(message, innerException)
    {
        OriginalPath = originalPath;
        BackupPath = backupPath;
    }
}
