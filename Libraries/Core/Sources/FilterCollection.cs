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
using Cube.Text.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
namespace Cube.FileSystem.SevenZip;

/* ------------------------------------------------------------------------- */
///
/// FilterCollection
///
/// <summary>
/// Provides functionality to determine if the provided file or
/// directory is filtered.
/// </summary>
///
/* ------------------------------------------------------------------------- */
public class FilterCollection
{
    #region Constructors

    /* --------------------------------------------------------------------- */
    ///
    /// FilterCollection
    ///
    /// <summary>
    /// Initializes a new instance of the FilterCollection class with
    /// the specified file or directory names.
    /// </summary>
    ///
    /// <param name="src">
    /// Collection of file or directory  names to be filtered.
    /// </param>
    ///
    /* --------------------------------------------------------------------- */
    public FilterCollection(IEnumerable<string> src)
    {
        Names = src;

        // 判定用のインデックスを一度だけ構築する。
        // (1) Names を IEnumerable のまま毎エントリ再列挙すると、呼び出し側が LINQ クエリや
        //     File.ReadLines を渡した場合にエントリ数に比例して再評価・再 I/O が走る。
        // (2) 比較は Ordinal 系にする。旧実装の string.Compare(a, b, true) はカルチャ依存で、
        //     tr-TR 等では I / ı の扱いが変わり「同じアーカイブでも環境によって除外される
        //     ファイルが変わる」という再現性のない挙動になっていた。
        _names = src is null
               ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
               : new HashSet<string>(src.Where(e => e.HasValue()), StringComparer.OrdinalIgnoreCase);
    }

    #endregion

    #region Properties

    /* --------------------------------------------------------------------- */
    ///
    /// Names
    ///
    /// <summary>
    /// Gets the collection of file or directory names to be filtered.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    public IEnumerable<string> Names { get; }

    #endregion

    #region Methods

    /* --------------------------------------------------------------------- */
    ///
    /// Match
    ///
    /// <summary>
    /// Determines if the specified file or directory is filtered.
    /// </summary>
    ///
    /// <param name="src">File or directory information.</param>
    ///
    /// <returns>true for filtered.</returns>
    ///
    /* --------------------------------------------------------------------- */
    public bool Match(Entity src)
    {
        if (_names.Count == 0) return false;

        // パス要素ごとに O(1) で判定する（除外名の件数に依存しない）
        foreach (var e in Split(src.FullName))
        {
            if (_names.Contains(e)) return true;
        }
        return false;
    }

    #endregion

    #region Implementations

    /* --------------------------------------------------------------------- */
    ///
    /// Split
    ///
    /// <summary>
    /// Splits the specified path with the path separator.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    private static IEnumerable<string> Split(string src) =>
        src.Split(s_separators)
           .SkipWhile(s => !s.HasValue());

    #endregion

    #region Fields
    // 区切り文字配列は呼び出しごとに確保しない（Match は全エントリで通る）
    private static readonly char[] s_separators = [.. SafePath.SeparatorChars];
    // Ordinal (大文字小文字無視) の判定用インデックス
    private readonly HashSet<string> _names;
    #endregion
}
