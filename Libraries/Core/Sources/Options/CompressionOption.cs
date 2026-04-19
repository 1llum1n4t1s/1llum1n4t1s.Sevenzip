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
using System.Collections.Generic;
namespace Cube.FileSystem.SevenZip;

/* ------------------------------------------------------------------------- */
///
/// CompressionOption
///
/// <summary>
/// Represents options when creating a new archive.
/// Some formats may support only some of these options.
/// </summary>
///
/* ------------------------------------------------------------------------- */
public class CompressionOption : ArchiveOption
{
    /* --------------------------------------------------------------------- */
    ///
    /// CompressionLevel
    ///
    /// <summary>
    /// Gets or sets the compression level.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    public CompressionLevel CompressionLevel { get; init; } = CompressionLevel.Normal;

    /* --------------------------------------------------------------------- */
    ///
    /// CompressionMethod
    ///
    /// <summary>
    /// Gets or sets the compression method.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    public CompressionMethod CompressionMethod { get; init; } = CompressionMethod.Default;

    /* --------------------------------------------------------------------- */
    ///
    /// EncryptionMethod
    ///
    /// <summary>
    /// Gets or sets the encryption method.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    public EncryptionMethod EncryptionMethod { get; init; } = EncryptionMethod.Default;

    /* --------------------------------------------------------------------- */
    ///
    /// Password
    ///
    /// <summary>
    /// Gets or sets a password to encrypt the archive being created.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    public string Password { get; init; } = string.Empty;

    /* --------------------------------------------------------------------- */
    ///
    /// CustomParameters
    ///
    /// <summary>
    /// 7z.dll の ISetProperties に直接渡すカスタムパラメータを取得する。
    /// </summary>
    /// <remarks>
    /// 7z.exe の -m スイッチ相当のキー/値ペアをそのままフォーマットハンドラに注入する。
    /// 例: <c>mt=1</c> (スレッド数)、<c>cu=on</c> (ZIP UTF-8 強制)、<c>d=64m</c> (LZMA 辞書)、
    /// <c>fb=128</c> (LZMA word size) など。既知キー (x / mt / cp / m / em / 0) と衝突した場合は
    /// このコレクションの値が優先される。全ての値は BSTR として注入する。
    /// </remarks>
    /* --------------------------------------------------------------------- */
    // P2-20: デフォルトは null。CompressionOptionSetter 側で null ガードするため、
    // 使わないケースで辞書 allocation が発生しないようにする。
    public IDictionary<string, string> CustomParameters { get; init; }

    /* --------------------------------------------------------------------- */
    ///
    /// IncludeEmptyDirectories
    ///
    /// <summary>
    /// 空ディレクトリをアーカイブに含めるかどうかを示す値を取得する。
    /// </summary>
    /// <remarks>
    /// 既定値は true（既存互換）。false の場合、子孫エントリを 1 件も含まない
    /// ディレクトリは出力アーカイブから除外する。
    /// </remarks>
    /* --------------------------------------------------------------------- */
    public bool IncludeEmptyDirectories { get; init; } = true;


    /* --------------------------------------------------------------------- */
    ///
    /// VolumeSize
    ///
    /// <summary>
    /// ボリューム分割サイズ (bytes) を取得する。0 以下の場合は分割なし（単一ファイル）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 0 より大きい値を指定すると、<see cref="ArchiveWriter.Save(string)"/> で
    /// <c>dest.001, dest.002, ...</c> の形式で自動分割書き出しを行う。
    /// 例: <c>VolumeSize = 100 * 1024 * 1024</c> で 100MB ごとに分割。
    /// </para>
    /// <para>
    /// <b>対応フォーマット:</b> 7z / Zip 等の ISequentialOutStream 受け取り型のフォーマット。
    /// 非対応フォーマットの場合は警告ログを出力して無視される。
    /// </para>
    /// <para>
    /// <b>制限:</b> <see cref="ArchiveWriter.Save(System.IO.Stream, System.IProgress{Report}, bool)"/>
    /// (Stream 版保存) や <see cref="ArchiveWriter.Update(string, string)"/> との併用は未対応。
    /// </para>
    /// </remarks>
    ///
    /* --------------------------------------------------------------------- */
    public long VolumeSize { get; init; } = 0;
}
