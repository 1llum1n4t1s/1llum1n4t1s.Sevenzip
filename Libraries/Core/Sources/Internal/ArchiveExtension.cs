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
using System.Runtime.InteropServices;
namespace Cube.FileSystem.SevenZip;

/// <summary>
/// IInArchive インターフェースの拡張メソッドを提供する。
/// </summary>
/// <remarks>
/// 7-zip の GetProperty は PropVariant 経由でデータを返すため、
/// ボックス化の回避やネイティブバッファの解放を拡張メソッドとして集約する。
/// </remarks>
internal static class ArchiveExtension
{
    #region Methods

    /// <summary>
    /// 指定した ID に対応する情報を取得する。
    /// </summary>
    /// <typeparam name="T">取得する値の型。</typeparam>
    /// <param name="src">7-zip コアオブジェクト。</param>
    /// <param name="index">アーカイブ内のインデックス。</param>
    /// <param name="pid">取得するプロパティ ID。</param>
    /// <returns>
    /// 指定した型にキャストできる場合はその値；
    /// キャストできない場合は default(T)。
    /// </returns>
    /// <remarks>
    /// 文字列型（VT_BSTR）を取得する場合はネイティブバッファが解放されない。
    /// 文字列の取得には <see cref="GetString"/> を使用すること。
    /// </remarks>
    public static T Get<T>(this IInArchive src, int index, ItemPropId pid)
    {
        var pv = new PropVariant();
        // 7-zip COM 経由でプロパティを PropVariant に取得する
        src.GetProperty((uint)index, pid, ref pv);

        var obj = pv.Object;
        // 型が一致する場合はキャスト、一致しない場合は default を返す
        return obj is T dest ? dest : default;
    }

    /// <summary>
    /// 文字列プロパティを取得し、PropVariant のネイティブバッファ（BSTR）を解放する。
    /// </summary>
    /// <param name="src">7-zip コアオブジェクト。</param>
    /// <param name="index">アーカイブ内のインデックス。</param>
    /// <param name="pid">取得するプロパティ ID。</param>
    /// <returns>文字列値；存在しない場合は null。</returns>
    /// <remarks>
    /// Get&lt;string&gt; では VT_BSTR の COM メモリが解放されずリークする。
    /// このメソッドは pv.Clear() を呼び出して確実にメモリを解放する。
    /// </remarks>
    public static string GetString(this IInArchive src, int index, ItemPropId pid)
    {
        var pv = new PropVariant();
        src.GetProperty((uint)index, pid, ref pv);
        // Object プロパティ経由で文字列を取得する（BSTR はここで文字列にコピーされる）
        var obj = pv.Object;
        // PropVariantClear を呼び出して BSTR ネイティブバッファを解放する
        pv.Clear();
        return obj as string;
    }

    /// <summary>
    /// ボックス化を回避して uint 値を直接取得する。
    /// </summary>
    /// <param name="src">7-zip コアオブジェクト。</param>
    /// <param name="index">アーカイブ内のインデックス。</param>
    /// <param name="pid">取得するプロパティ ID。</param>
    /// <returns>uint 値；型が一致しない場合は default(uint)。</returns>
    public static uint GetUInt32(this IInArchive src, int index, ItemPropId pid)
    {
        var pv = new PropVariant();
        src.GetProperty((uint)index, pid, ref pv);
        // VT_UI4 または VT_UINT の場合のみ値を返す（それ以外は default）
        return pv.VarType == VarEnum.VT_UI4 || pv.VarType == VarEnum.VT_UINT
            ? pv.GetUInt32() : default;
    }

    /// <summary>
    /// ボックス化を回避して bool 値を直接取得する。
    /// </summary>
    /// <param name="src">7-zip コアオブジェクト。</param>
    /// <param name="index">アーカイブ内のインデックス。</param>
    /// <param name="pid">取得するプロパティ ID。</param>
    /// <returns>bool 値；型が一致しない場合は false。</returns>
    public static bool GetBool(this IInArchive src, int index, ItemPropId pid)
    {
        var pv = new PropVariant();
        src.GetProperty((uint)index, pid, ref pv);
        // VT_BOOL かつ値が非ゼロの場合のみ true を返す
        return pv.VarType == VarEnum.VT_BOOL && pv.GetBool();
    }

    /// <summary>
    /// ボックス化を回避して ulong 値を直接取得する。
    /// </summary>
    /// <param name="src">7-zip コアオブジェクト。</param>
    /// <param name="index">アーカイブ内のインデックス。</param>
    /// <param name="pid">取得するプロパティ ID。</param>
    /// <returns>ulong 値；型が一致しない場合は default(ulong)。</returns>
    public static ulong GetUInt64(this IInArchive src, int index, ItemPropId pid)
    {
        var pv = new PropVariant();
        src.GetProperty((uint)index, pid, ref pv);
        // VT_UI8 の場合のみ値を返す（それ以外は default）
        return pv.VarType == VarEnum.VT_UI8 ? pv.GetUInt64() : default;
    }

    /// <summary>
    /// ボックス化を回避して DateTime 値を直接取得する。
    /// </summary>
    /// <param name="src">7-zip コアオブジェクト。</param>
    /// <param name="index">アーカイブ内のインデックス。</param>
    /// <param name="pid">取得するプロパティ ID。</param>
    /// <returns>DateTime 値；VT_FILETIME でない場合は default(DateTime)。</returns>
    public static DateTime GetDateTime(this IInArchive src, int index, ItemPropId pid)
    {
        var pv = new PropVariant();
        src.GetProperty((uint)index, pid, ref pv);
        // PropVariant.GetFileTime() 内で VarType チェックを行い default を返す
        return pv.GetFileTime();
    }

    /// <summary>
    /// 指定したインデックスのパスを取得する。
    /// </summary>
    /// <param name="src">7-zip コアオブジェクト。</param>
    /// <param name="index">アーカイブ内のターゲットインデックス。</param>
    /// <param name="path">アーカイブファイルのパス。</param>
    /// <returns>指定したインデックスのパス。</returns>
    /// <remarks>
    /// TAR ファイルはパス情報を取得できないため、アーカイブファイル名の
    /// 拡張子を .tar に変更したものをパスとして使用する。
    /// </remarks>
    public static string GetPath(this IInArchive src, int index, string path)
    {
        // まず 7-zip からパスを取得する（GetString を使って BSTR メモリを解放する）
        var dest = src.Get<string>(index, ItemPropId.Path);
        if (dest.HasValue()) return dest; // 取得できた場合はそのまま返す

        // パスが取得できなかった場合（TAR など）はアーカイブ名から生成する
        var tmp = Io.GetBaseName(path);
        // 拡張子が既知のアーカイブ形式なら、その拡張子なしのファイル名を返す
        var fmt = FormatFactory.FromExtension(Io.GetExtension(tmp));
        if (fmt != Format.Unknown) return Io.GetFileName(tmp);

        // インデックスが 0 以外の場合は (index) を末尾に付けて区別する
        var name = index == 0 ? Io.GetFileName(tmp) : $"{Io.GetFileName(tmp)}({index})";
        var ext  = Io.GetExtension(path).ToLowerInvariant();
        // .tbz2 / .tb2 / .tXz 形式（TAR 系圧縮フォーマット）かどうかを判定する
        var tar  = ext == ".tb2" || ext == ".tbz2" ||
                   ext.Length == 4 && ext[0] == '.' && ext[1] == 't' && ext[3] == 'z';
        // TAR 系の場合は .tar 拡張子を付与する
        return tar ? $"{name}.tar" : name;
    }

    #endregion
}
