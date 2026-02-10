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

/* ------------------------------------------------------------------------- */
///
/// PropVariant
///
/// <summary>
/// https://msdn.microsoft.com/en-us/library/windows/desktop/aa380072.aspx
/// </summary>
///
/* ------------------------------------------------------------------------- */
[SupportedOSPlatform("windows")]
[StructLayout(LayoutKind.Explicit)]
internal struct PropVariant
{
    #region Properties

    /* --------------------------------------------------------------------- */
    ///
    /// VarType
    ///
    /// <summary>
    /// Gets the object type.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    public VarEnum VarType
    {
        get => (VarEnum)_vt;
        private set => _vt = (ushort)value;
    }

    /* --------------------------------------------------------------------- */
    ///
    /// Object
    ///
    /// <summary>
    /// Gets the content.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    public object Object => VarType switch
    {
        VarEnum.VT_EMPTY   => null,
        VarEnum.VT_BOOL    => _v64u != 0,
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
        VarEnum.VT_BSTR    => _vstr != IntPtr.Zero ? Marshal.PtrToStringUni(_vstr) : null,
        VarEnum.VT_LPWSTR  => _vstr != IntPtr.Zero ? Marshal.PtrToStringUni(_vstr) : null,
        VarEnum.VT_LPSTR   => _vstr != IntPtr.Zero ? Marshal.PtrToStringAnsi(_vstr) : null,
        VarEnum.VT_FILETIME => DateTime.FromFileTime(_v64),
        _                  => null,
    };

    #endregion

    #region Methods

    /* --------------------------------------------------------------------- */
    ///
    /// Clear
    ///
    /// <summary>
    /// Clears the fields.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    public void Clear() => NativeMethods.PropVariantClear(ref this);

    #region Set

    /* --------------------------------------------------------------------- */
    ///
    /// Set
    ///
    /// <summary>
    /// Sets the specified boolean value.
    /// </summary>
    ///
    /// <param name="value">Setting value.</param>
    ///
    /* --------------------------------------------------------------------- */
    public void Set(bool value)
    {
        VarType = VarEnum.VT_BOOL;
        _v64u   = value ? 1UL : 0UL;
    }

    /* --------------------------------------------------------------------- */
    ///
    /// Set
    ///
    /// <summary>
    /// Sets the specified uint value.
    /// </summary>
    ///
    /// <param name="value">Setting value.</param>
    ///
    /* --------------------------------------------------------------------- */
    public void Set(uint value)
    {
        VarType = VarEnum.VT_UI4;
        _v32u   = value;
    }

    /* --------------------------------------------------------------------- */
    ///
    /// Set
    ///
    /// <summary>
    /// Sets the specified ulong value.
    /// </summary>
    ///
    /// <param name="value">Setting value.</param>
    ///
    /* --------------------------------------------------------------------- */
    public void Set(ulong value)
    {
        VarType = VarEnum.VT_UI8;
        _v64u   = value;
    }

    /* --------------------------------------------------------------------- */
    ///
    /// Set
    ///
    /// <summary>
    /// Sets the specified string.
    /// </summary>
    ///
    /// <param name="value">Setting value.</param>
    ///
    /* --------------------------------------------------------------------- */
    public void Set(string value)
    {
        VarType = VarEnum.VT_BSTR;
        _vstr   = Marshal.StringToBSTR(value);
    }

    /* --------------------------------------------------------------------- */
    ///
    /// Set
    ///
    /// <summary>
    /// Sets the specified DateTime object.
    /// </summary>
    ///
    /// <param name="value">Setting value.</param>
    ///
    /* --------------------------------------------------------------------- */
    public void Set(DateTime value)
    {
        VarType = VarEnum.VT_FILETIME;
        _v64    = value.ToFileTime();
    }

    #endregion

    #region Create

    /* --------------------------------------------------------------------- */
    ///
    /// Create
    ///
    /// <summary>
    /// Creates a new instance of the PropVariant class with the
    /// specified boolean value.
    /// </summary>
    ///
    /// <param name="value">Setting value</param>
    ///
    /// <returns>PropVariant object.</returns>
    ///
    /* --------------------------------------------------------------------- */
    public static PropVariant Create(bool value)
    {
        var dest = new PropVariant();
        dest.Set(value);
        return dest;
    }

    /* --------------------------------------------------------------------- */
    ///
    /// Create
    ///
    /// <summary>
    /// Creates a new instance of the PropVariant class with the
    /// specified uint value.
    /// </summary>
    ///
    /// <param name="value">Setting value</param>
    ///
    /// <returns>PropVariant object.</returns>
    ///
    /* --------------------------------------------------------------------- */
    public static PropVariant Create(uint value)
    {
        var dest = new PropVariant();
        dest.Set(value);
        return dest;
    }

    /* --------------------------------------------------------------------- */
    ///
    /// Create
    ///
    /// <summary>
    /// Creates a new instance of the PropVariant class with the
    /// specified string.
    /// </summary>
    ///
    /// <param name="value">Setting value</param>
    ///
    /// <returns>PropVariant object.</returns>
    ///
    /* --------------------------------------------------------------------- */
    public static PropVariant Create(string value)
    {
        var dest = new PropVariant();
        dest.Set(value);
        return dest;
    }

    #endregion

    #endregion

    #region Fields
    [FieldOffset(0)] private ushort _vt;
    [FieldOffset(8)] private IntPtr _vstr;
    [FieldOffset(8)] private uint   _v32u;
    [FieldOffset(8)] private long   _v64;
    [FieldOffset(8)] private ulong  _v64u;
    [FieldOffset(8)] private PropArray _hack;
    #endregion
}
