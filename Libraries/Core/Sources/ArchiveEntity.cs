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

/* ------------------------------------------------------------------------- */
///
/// ArchiveEntity
///
/// <summary>
/// Represents an item in the archive.
/// </summary>
///
/* ------------------------------------------------------------------------- */
[Serializable]
public class ArchiveEntity : Entity
{
    #region Constructors

    /* --------------------------------------------------------------------- */
    ///
    /// ArchiveEntity
    ///
    /// <summary>
    /// Initializes a new instance of the ArchiveEntity class with the
    /// specified arguments.
    /// </summary>
    ///
    /// <param name="src">Source object.</param>
    ///
    /// <remarks>
    /// 派生クラス（例: ZipArchiveEntity）からも呼び出せるよう protected internal とする。
    /// src は本メソッド内で Dispose される。
    /// </remarks>
    ///
    /* --------------------------------------------------------------------- */
    internal ArchiveEntity(ArchiveEntitySource src) : base(src, false)
    {
        try
        {
            Index     = src.Index;
            Crc       = src.Crc;
            Encrypted = src.Encrypted;

            // Unicode 判定: FullName == RawName かつ U+FFFD を含まない場合に true とみなす。
            // これは ZIP general purpose bit 11 の厳密な値ではないが、呼び出し側が
            // エンコーディングのフォールバック判定に使える実用的なヒューリスティックとなる。
            IsUnicodeText = ComputeIsUnicodeText(src.RawName, FullName);
        }
        finally { src.Dispose(); }
    }

    #endregion

    #region Properties

    /* --------------------------------------------------------------------- */
    ///
    /// Index
    ///
    /// <summary>
    /// Gets the index in the archive.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    public int Index { get; }

    /* --------------------------------------------------------------------- */
    ///
    /// Crc
    ///
    /// <summary>
    /// Gets the CRC value of the item.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    public uint Crc { get; }

    /* --------------------------------------------------------------------- */
    ///
    /// Encrypted
    ///
    /// <summary>
    /// Gets the value indicating whether the archive is encrypted.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    public bool Encrypted { get; }

    /* --------------------------------------------------------------------- */
    ///
    /// IsUnicodeText
    ///
    /// <summary>
    /// エントリ名が Unicode として正しくデコードされているかどうかを示すヒューリスティック値。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>重要:</b> これは ZIP の general purpose bit 11 (Language encoding flag) の
    /// 厳密な値では**ありません**。7z.dll がエントリ名をデコードした結果を基にした
    /// **ヒューリスティック判定** です。以下のいずれかに該当する場合 false を返します:
    /// </para>
    /// <list type="bullet">
    /// <item><description>FullName に U+FFFD (REPLACEMENT CHARACTER) が含まれる</description></item>
    /// <item><description>RawName が U+FFFD を含む</description></item>
    /// </list>
    /// <para>
    /// false の場合、<see cref="ArchiveOption.Encoding"/> を Shift_JIS などに
    /// 変更して <see cref="ArchiveReader"/> を再オープンすると正しく読める可能性が高い。
    /// </para>
    /// <para>
    /// <b>既知の制限:</b> OEM エンコードのエントリでも ASCII 範囲のみなら U+FFFD を
    /// 生まないため、このプロパティが true を返しても「bit 11 が立っていた (UTF-8)」
    /// ことは保証されません。厳密な bit 11 値が必要な場合は ZIP バイナリを直接読んで
    /// ください。
    /// </para>
    /// </remarks>
    ///
    /* --------------------------------------------------------------------- */
    public bool IsUnicodeText { get; }

    // --- RawName について (基底 Entity.RawName として既に public 公開されている) ---
    //
    // 7z.dll から受け取った生のエントリパス文字列を返します（パスサニタイズ前の値）。
    //
    // バイト列に戻したい場合は以下のようにします:
    //   var bytes = Encoding.UTF8.GetBytes(entity.RawName);
    //   var bytes = Encoding.Latin1.GetBytes(entity.RawName);
    //
    // CP437 / Shift_JIS 等のレガシーコードページを使う場合は、.NET Core では事前に
    // `System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)` の
    // 呼び出しが必要です (System.Text.Encoding.CodePages パッケージを同梱済み)。

    #endregion

    #region Implementations

    /// <summary>
    /// 名前が Unicode として整合的にデコードされているかのヒューリスティック判定。
    /// </summary>
    private static bool ComputeIsUnicodeText(string rawName, string fullName)
    {
        if (string.IsNullOrEmpty(fullName)) return true;

        // U+FFFD は replacement character。不正デコード時に現れる
        if (fullName.Contains('\uFFFD')) return false;

        // RawName が空なら判定材料なしで true
        if (string.IsNullOrEmpty(rawName)) return true;

        // 7z.dll がパスサニタイズを行った場合、FullName と RawName の差は主に
        // Windows 禁止文字のエスケープ (U+F0xx 領域) となる。
        // 他にも大文字小文字の違い等は生じないため、RawName 側にも U+FFFD が無いか見る
        return !rawName.Contains('\uFFFD');
    }

    #endregion
}
