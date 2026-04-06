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
using Cube.FileSystem.SevenZip.Ole32;
using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
namespace Cube.FileSystem.SevenZip;

/// <summary>
/// Windows COM の PROPVARIANT 構造体のマネージドラッパー。
/// </summary>
/// <remarks>
/// 詳細: https://msdn.microsoft.com/en-us/library/windows/desktop/aa380072.aspx
/// FieldOffset 属性により全フィールドをオフセット 8 で共用体化している。
/// VT_BSTR 型の値を保持した場合は必ず <see cref="Clear"/> を呼び出してネイティブバッファを解放すること。
/// </remarks>
[SupportedOSPlatform("windows")]
[StructLayout(LayoutKind.Explicit)]
internal struct PropVariant
{
    #region Properties

    /// <summary>
    /// 格納されている値の型を取得する。
    /// </summary>
    /// <remarks>
    /// _vt フィールドの下位 16 ビットが VARTYPE に対応する。
    /// </remarks>
    public VarEnum VarType
    {
        get => (VarEnum)_vt;
        private set => _vt = (ushort)value;
    }

    /// <summary>
    /// 格納された値を object 型として取得する。
    /// </summary>
    /// <remarks>
    /// ボックス化が発生するため、型が判明している場合は GetUInt32/GetUInt64/GetBool/GetFileTime を使用すること。
    /// VT_BSTR の文字列取得後は必ず <see cref="Clear"/> でネイティブバッファを解放すること。
    /// </remarks>
    public object Object => VarType switch
    {
        VarEnum.VT_EMPTY   => null,
        VarEnum.VT_BOOL    => _v64u != 0,                                          // VARIANT_BOOL は -1(true) / 0(false)
        VarEnum.VT_I1      => (sbyte)_v64,
        VarEnum.VT_UI1     => (byte)_v64u,
        VarEnum.VT_I2      => (short)_v64,
        VarEnum.VT_UI2     => (ushort)_v64u,
        VarEnum.VT_I4 or VarEnum.VT_INT => (int)_v32u,
        VarEnum.VT_UI4 or VarEnum.VT_UINT => _v32u,
        VarEnum.VT_I8      => _v64,
        VarEnum.VT_UI8     => _v64u,
        VarEnum.VT_R4      => BitConverter.Int32BitsToSingle((int)_v32u),
        VarEnum.VT_R8      => BitConverter.Int64BitsToDouble(_v64),
        VarEnum.VT_BSTR    => _vstr != IntPtr.Zero ? Marshal.PtrToStringBSTR(_vstr) : null,    // COM BSTR
        VarEnum.VT_LPWSTR  => _vstr != IntPtr.Zero ? Marshal.PtrToStringUni(_vstr) : null,     // ワイド文字列ポインタ
        VarEnum.VT_LPSTR   => _vstr != IntPtr.Zero ? Marshal.PtrToStringAnsi(_vstr) : null,    // ANSI 文字列ポインタ
        VarEnum.VT_FILETIME => DateTime.FromFileTime(_v64),                        // FILETIME → DateTime 変換
        _                  => null,
    };

    #endregion

    #region Methods

    /// <summary>
    /// フィールドをクリアしてネイティブバッファを解放する。
    /// </summary>
    /// <remarks>
    /// VT_BSTR など COM が割り当てたメモリを解放するために必ず呼び出すこと。
    /// Ole32 の PropVariantClear を呼び出す。
    /// </remarks>
    public void Clear() => NativeMethods.PropVariantClear(ref this);

    #region Set

    /// <summary>
    /// 指定した bool 値を設定する。
    /// </summary>
    /// <param name="value">設定する値。</param>
    public void Set(bool value)
    {
        VarType = VarEnum.VT_BOOL;
        // VARIANT_BOOL 規格: true=0xFFFF だが 7-zip は 0 以外を true と判定するため 1 で代用する
        _v64u   = value ? 1UL : 0UL;
    }

    /// <summary>
    /// 指定した uint 値を設定する。
    /// </summary>
    /// <param name="value">設定する値。</param>
    public void Set(uint value)
    {
        VarType = VarEnum.VT_UI4;
        _v32u   = value;
    }

    /// <summary>
    /// 指定した ulong 値を設定する。
    /// </summary>
    /// <param name="value">設定する値。</param>
    public void Set(ulong value)
    {
        VarType = VarEnum.VT_UI8;
        _v64u   = value;
    }

    /// <summary>
    /// 指定した文字列を BSTR として設定する。
    /// </summary>
    /// <param name="value">設定する文字列。</param>
    /// <remarks>
    /// Marshal.StringToBSTR でネイティブヒープに BSTR を割り当てる。
    /// 使い終わったら <see cref="Clear"/> で解放すること。
    /// </remarks>
    public void Set(string value)
    {
        VarType = VarEnum.VT_BSTR;
        _vstr   = Marshal.StringToBSTR(value);
    }

    /// <summary>
    /// 指定した DateTime を FILETIME として設定する。
    /// </summary>
    /// <param name="value">設定する日時。</param>
    public void Set(DateTime value)
    {
        VarType = VarEnum.VT_FILETIME;
        // DateTime.ToFileTime() は UTC ベースの 100ns 単位の FILETIME 値を返す
        _v64    = value.ToFileTime();
    }

    #endregion

    #region GetTyped

    /// <summary>
    /// ボックス化を回避して uint 値を直接取得する。
    /// </summary>
    /// <returns>格納されている uint 値。</returns>
    /// <remarks>
    /// 呼び出し前に VarType が VT_UI4 または VT_UINT であることを確認すること。
    /// </remarks>
    public uint GetUInt32() => _v32u;

    /// <summary>
    /// ボックス化を回避して ulong 値を直接取得する。
    /// </summary>
    /// <returns>格納されている ulong 値。</returns>
    /// <remarks>
    /// 呼び出し前に VarType が VT_UI8 であることを確認すること。
    /// </remarks>
    public ulong GetUInt64() => _v64u;

    /// <summary>
    /// ボックス化を回避して bool 値を直接取得する。
    /// </summary>
    /// <returns>格納されている bool 値。</returns>
    /// <remarks>
    /// 呼び出し前に VarType が VT_BOOL であることを確認すること。
    /// </remarks>
    public bool GetBool() => _v64u != 0;

    /// <summary>
    /// ボックス化を回避して DateTime 値を直接取得する。
    /// </summary>
    /// <returns>VT_FILETIME の場合は変換された DateTime；それ以外は default。</returns>
    public DateTime GetFileTime() =>
        // VT_FILETIME 以外の型の場合はデフォルト値を返す
        VarType == VarEnum.VT_FILETIME ? DateTime.FromFileTime(_v64) : default;

    #endregion

    #region Create

    /// <summary>
    /// 指定した bool 値で PropVariant の新しいインスタンスを生成する。
    /// </summary>
    /// <param name="value">設定する値。</param>
    /// <returns>VT_BOOL 型の PropVariant オブジェクト。</returns>
    public static PropVariant Create(bool value)
    {
        var dest = new PropVariant();
        dest.Set(value);
        return dest;
    }

    /// <summary>
    /// 指定した uint 値で PropVariant の新しいインスタンスを生成する。
    /// </summary>
    /// <param name="value">設定する値。</param>
    /// <returns>VT_UI4 型の PropVariant オブジェクト。</returns>
    public static PropVariant Create(uint value)
    {
        var dest = new PropVariant();
        dest.Set(value);
        return dest;
    }

    /// <summary>
    /// 指定した文字列で PropVariant の新しいインスタンスを生成する。
    /// </summary>
    /// <param name="value">設定する文字列。</param>
    /// <returns>VT_BSTR 型の PropVariant オブジェクト。</returns>
    /// <remarks>
    /// 返されたインスタンスを使い終わったら <see cref="Clear"/> でネイティブバッファを解放すること。
    /// </remarks>
    public static PropVariant Create(string value)
    {
        var dest = new PropVariant();
        dest.Set(value);
        return dest;
    }

    #endregion

    #endregion

    #region Fields
    [FieldOffset(0)] private ushort _vt;        // VARTYPE: VarEnum の値を格納する
    [FieldOffset(8)] private IntPtr _vstr;       // 文字列ポインタ（VT_BSTR / VT_LPWSTR / VT_LPSTR）
    [FieldOffset(8)] private uint   _v32u;       // 32-bit 符号なし整数（VT_UI4 / VT_UINT）
    [FieldOffset(8)] private long   _v64;        // 64-bit 符号あり整数（VT_I8 / VT_FILETIME）
    [FieldOffset(8)] private ulong  _v64u;       // 64-bit 符号なし整数（VT_UI8 / VT_BOOL）
    [FieldOffset(8)] private PropArray _hack;    // PropArray との共用体整合性のためのダミーフィールド
    #endregion
}
