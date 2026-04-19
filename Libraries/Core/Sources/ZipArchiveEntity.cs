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
using System.ComponentModel;
using System.Runtime.InteropServices;
namespace Cube.FileSystem.SevenZip;

/// <summary>
/// ZIP 形式のアーカイブエントリの追加メタデータを公開するクラス。
/// </summary>
/// <remarks>
/// <see cref="ArchiveReader.Items"/> は <see cref="Format"/> が <see cref="Format.Zip"/> の場合のみ
/// このクラスのインスタンスを返す。呼び出し側は <c>entity as ZipArchiveEntity</c> で
/// 安全にキャストしてから ZIP 固有プロパティにアクセスする。
/// </remarks>
[Serializable]
public class ZipArchiveEntity : ArchiveEntity
{
    #region Constructors

    /// <summary>
    /// ZIP 固有メタデータ付きで <see cref="ZipArchiveEntity"/> を初期化する。
    /// </summary>
    /// <param name="src">ArchiveEntitySource オブジェクト（本メソッド内で Dispose される）。</param>
    /// <param name="core">7-Zip COM コア（拡張プロパティ取得用）。</param>
    /// <param name="index">アーカイブ内のインデックス。</param>
    internal ZipArchiveEntity(ArchiveEntitySource src, IInArchive core, int index) : base(src)
    {
        // NOTE: base(src) が src.Dispose() を呼ぶため、以下は core から直接取得する。
        // P1-10: 以前は空 catch で全例外握り潰していたが、AccessViolation / SEH 等の
        // 致命的例外までは catch しない。COMException / InvalidOperationException のみ許容。
        Method     = TryGetString(core, index, ItemPropId.Method);
        HostOS     = TryGetString(core, index, ItemPropId.HostOS);
        PackedSize = TryGetUInt64(core, index, ItemPropId.PackedSize);
        Comment    = TryGetString(core, index, ItemPropId.Comment);
    }

    private static string TryGetString(IInArchive core, int index, ItemPropId pid)
    {
        try { return core.GetString(index, pid); }
        catch (COMException ex) { Logger.Debug($"ZipArchiveEntity[{index}] {pid}: {ex.Message}"); }
        catch (InvalidOperationException ex) { Logger.Debug($"ZipArchiveEntity[{index}] {pid}: {ex.Message}"); }
        return null;
    }

    private static long TryGetUInt64(IInArchive core, int index, ItemPropId pid)
    {
        try { return (long)core.GetUInt64(index, pid); }
        catch (COMException ex) { Logger.Debug($"ZipArchiveEntity[{index}] {pid}: {ex.Message}"); }
        catch (InvalidOperationException ex) { Logger.Debug($"ZipArchiveEntity[{index}] {pid}: {ex.Message}"); }
        return 0L;
    }

    #endregion

    #region Properties

    /// <summary>
    /// 圧縮メソッド名（例: "Deflate", "Store"）を取得する。取得できない場合は null。
    /// </summary>
    public string Method { get; }

    /// <summary>
    /// エントリが作成されたホスト OS（例: "FAT", "Unix"）を取得する。取得できない場合は null。
    /// </summary>
    public string HostOS { get; }

    /// <summary>
    /// 圧縮後のサイズ (bytes) を取得する。取得できない場合は 0。
    /// </summary>
    public long PackedSize { get; }

    /// <summary>
    /// エントリに付与されたコメント文字列を取得する。無い場合は null。
    /// </summary>
    public string Comment { get; }

    /// <summary>
    /// ZIP general purpose bit flag を取得する (常に null)。
    /// </summary>
    /// <remarks>
    /// <b>注意:</b> 7z.dll の標準 API では取得できないため現状は常に null を返す。
    /// Unicode 判定は <see cref="ArchiveEntity.IsUnicodeText"/> を利用すること。
    /// 将来の <c>IArchiveGetRawProps</c> 対応で実値を返すようになる可能性がある。
    /// </remarks>
    [Obsolete("7z.dll の標準 API では取得できないため常に null を返します。" +
        "Unicode 判定は ArchiveEntity.IsUnicodeText を使用してください。", error: false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public ushort? GeneralPurposeBitFlag { get; } = null;

    /// <summary>
    /// ZIP extra field の生バイト列を取得する (常に null)。
    /// </summary>
    /// <remarks>
    /// <b>注意:</b> 7z.dll の標準 API では取得できないため現状は常に null を返す。
    /// </remarks>
    [Obsolete("7z.dll の標準 API では取得できないため常に null を返します。", error: false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public byte[] ExtraField { get; } = null;

    /// <summary>
    /// エントリを作成したバージョン（Made by version）を取得する (常に null)。
    /// </summary>
    /// <remarks>
    /// <b>注意:</b> 7z.dll の標準 API では取得できないため現状は常に null を返す。
    /// </remarks>
    [Obsolete("7z.dll の標準 API では取得できないため常に null を返します。", error: false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public ushort? MadeByVersion { get; } = null;

    /// <summary>
    /// 解凍に必要な最小バージョン（Version needed to extract）を取得する (常に null)。
    /// </summary>
    /// <remarks>
    /// <b>注意:</b> 7z.dll の標準 API では取得できないため現状は常に null を返す。
    /// </remarks>
    [Obsolete("7z.dll の標準 API では取得できないため常に null を返します。", error: false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public ushort? VersionNeeded { get; } = null;

    #endregion
}
