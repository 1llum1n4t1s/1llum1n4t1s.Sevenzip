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
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
namespace Cube.FileSystem.SevenZip;

/* ------------------------------------------------------------------------- */
///
/// CompressionOptionSetter
///
/// <summary>
/// Provides the functionality to set optional settings for archives.
/// </summary>
///
/* ------------------------------------------------------------------------- */
internal class CompressionOptionSetter
{
    #region Constructors

    /* --------------------------------------------------------------------- */
    ///
    /// CompressionOptionSetter
    ///
    /// <summary>
    /// Initializes a new instance of the CompressionOptionSetter class
    /// with the specified options.
    /// </summary>
    ///
    /// <param name="options">Archive options.</param>
    ///
    /* --------------------------------------------------------------------- */
    public CompressionOptionSetter(CompressionOption options) =>
        Options = options ?? throw new ArgumentNullException();

    #endregion

    #region Properties

    /* --------------------------------------------------------------------- */
    ///
    /// Options
    ///
    /// <summary>
    /// Gets the archive options.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    public CompressionOption Options { get; }

    #endregion

    #region Methods

    /* --------------------------------------------------------------------- */
    ///
    /// From
    ///
    /// <summary>
    /// Creates a new instance of the CompressionOptionSetter class
    /// with the specified arguments.
    /// </summary>
    ///
    /// <param name="format">Archive format.</param>
    /// <param name="options">Archive options.</param>
    ///
    /// <returns>CompressionOptionSetter object.</returns>
    ///
    /* --------------------------------------------------------------------- */
    public static CompressionOptionSetter From(Format format, CompressionOption options) => format switch
    {
        Format.Zip      => new ZipOptionSetter(options),
        Format.SevenZip => new SevenZipOptionSetter(options),
        Format.Tar      => null,
        _               => new CompressionOptionSetter(options),
    };

    /* --------------------------------------------------------------------- */
    ///
    /// Invoke
    ///
    /// <summary>
    /// Sets the current options to the specified archive.
    /// </summary>
    ///
    /// <param name="dest">Archive object.</param>
    ///
    /* --------------------------------------------------------------------- */
    public void Invoke(ISetProperties dest)
    {
        if (dest == null) return;

        // 1. フォーマット固有の既知キー (cp / m / em / 0 など) を集める
        var src = new Dictionary<string, PropVariant>();
        Invoke(src);

        // 2. 共通キー x (CompressionLevel) / mt (ThreadCount) を追加
        src["x"]  = PropVariant.Create((uint)Options.CompressionLevel);
        src["mt"] = PropVariant.Create((uint)Options.ThreadCount);

        // 3. CustomParameters をマージ（ユーザー指定が既知キーより優先）
        //    全ての値は BSTR (文字列) として注入する。7z.dll 側が必要に応じて数値解釈する。
        var custom = Options.CustomParameters;
        if (custom is not null)
        {
            foreach (var kv in custom)
            {
                if (string.IsNullOrEmpty(kv.Key)) continue;
                src[kv.Key] = PropVariant.Create(kv.Value ?? string.Empty);
            }
        }

        var k = new string[src.Count];
        var v = new PropVariant[src.Count];
        var idx = 0;
        foreach (var kv in src)
        {
            k[idx] = kv.Key;
            v[idx] = kv.Value;
            idx++;
        }

        var obj = GCHandle.Alloc(v, GCHandleType.Pinned);
        try { _ = dest.SetProperties(k, obj.AddrOfPinnedObject(), (uint)k.Length); }
        finally
        {
            obj.Free();
            // VT_BSTR 等のネイティブバッファを保持する PropVariant を解放する。
            // GCHandle.Free() はピン解除のみで BSTR は解放しないため、明示的に Clear() を呼ぶ。
            // (AddCompressionMethod / AddEncryptionMethod / CustomParameters の文字列値が該当)
            for (var i = 0; i < v.Length; i++)
            {
                if (v[i].VarType == VarEnum.VT_BSTR ||
                    v[i].VarType == VarEnum.VT_LPWSTR ||
                    v[i].VarType == VarEnum.VT_LPSTR)
                {
                    v[i].Clear();
                }
            }
        }
    }

    /* --------------------------------------------------------------------- */
    ///
    /// Invoke
    ///
    /// <summary>
    /// Sets the current options to the specified collection.
    /// </summary>
    ///
    /// <param name="dest">Collection of options.</param>
    ///
    /* --------------------------------------------------------------------- */
    protected virtual void Invoke(IDictionary<string, PropVariant> dest)
    {
        // Encoding が指定されていれば優先、それ以外は CodePage enum を使用
        if (!Options.IsDefaultCodePage())
        {
            dest.Add("cp", PropVariant.Create(Options.ResolveCodePage()));
        }
    }

    /* --------------------------------------------------------------------- */
    ///
    /// AddCompressionMethod
    ///
    /// <summary>
    /// 圧縮メソッドを設定に追加します。指定されたメソッドがサポート対象外の
    /// 場合は何もしません。
    /// </summary>
    ///
    /// <param name="dest">設定コレクション。</param>
    /// <param name="key">プロパティキー。</param>
    /// <param name="defaultMethod">
    /// CompressionMethod.Default の場合に使用するメソッド。
    /// </param>
    /// <param name="supported">サポートされる圧縮メソッドの配列。</param>
    ///
    /* --------------------------------------------------------------------- */
    protected void AddCompressionMethod(
        IDictionary<string, PropVariant> dest,
        string key,
        CompressionMethod defaultMethod,
        CompressionMethod[] supported)
    {
        var m = Options.CompressionMethod == CompressionMethod.Default ?
                defaultMethod :
                Options.CompressionMethod;
        if (!supported.Contains(m)) return;
        dest.Add(key, PropVariant.Create(m.ToString()));
    }

    #endregion
}
