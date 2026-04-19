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
using System.Text;
namespace Cube.FileSystem.SevenZip;

/* ------------------------------------------------------------------------- */
///
/// ArchiveOption
///
/// <summary>
/// Represents options when creating a new archive.
/// </summary>
///
/* ------------------------------------------------------------------------- */
public class ArchiveOption
{
    /* --------------------------------------------------------------------- */
    ///
    /// ThreadCount
    ///
    /// <summary>
    /// Gets or sets the number of threads that the archiver is
    /// available.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    public int ThreadCount { get; init; } = 1;

    /* --------------------------------------------------------------------- */
    ///
    /// CodePage
    ///
    /// <summary>
    /// Gets or sets the value of code page.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    public CodePage CodePage { get; init; } = CodePage.Oem;

    /* --------------------------------------------------------------------- */
    ///
    /// Encoding
    ///
    /// <summary>
    /// ZIP エントリ名のデコードに使用する任意の Encoding を取得する。
    /// </summary>
    /// <remarks>
    /// 非 null の場合、<see cref="CodePage"/> より優先される。
    /// CP437 や Shift_JIS など <see cref="SevenZip.CodePage"/> enum に含まれない
    /// コードページを指定したい場合に使う（例: <c>Encoding.GetEncoding(437)</c>）。
    /// </remarks>
    /* --------------------------------------------------------------------- */
    public Encoding Encoding { get; init; }

    /* --------------------------------------------------------------------- */
    ///
    /// ResolveCodePage
    ///
    /// <summary>
    /// <see cref="Encoding"/> が指定されていればそのコードページを、
    /// それ以外は <see cref="CodePage"/> を uint として返す。
    /// </summary>
    /// <returns>7z.dll の "cp" プロパティに渡すコードページ番号。</returns>
    /* --------------------------------------------------------------------- */
    internal uint ResolveCodePage() =>
        Encoding is not null ? (uint)Encoding.CodePage : (uint)CodePage;

    /* --------------------------------------------------------------------- */
    ///
    /// IsDefaultCodePage
    ///
    /// <summary>
    /// コードページ指定がデフォルト (Oem かつ Encoding 未指定) かどうかを返す。
    /// </summary>
    /* --------------------------------------------------------------------- */
    internal bool IsDefaultCodePage() =>
        Encoding is null && CodePage == CodePage.Oem;

    /* --------------------------------------------------------------------- */
    ///
    /// Filter
    ///
    /// <summary>
    /// Gets or sets the predicate function to filter some of files or
    /// directories.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    public Predicate<Entity> Filter { get; init; }
}
