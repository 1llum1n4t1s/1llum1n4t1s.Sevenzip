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

/* ------------------------------------------------------------------------- */
///
/// SevenZipException
///
/// <summary>
/// Represents the exception that an error occurs in the 7-Zip library.
/// </summary>
///
/* ------------------------------------------------------------------------- */
[Serializable]
public class SevenZipException : IOException
{
    #region Constructors

    /* --------------------------------------------------------------------- */
    ///
    /// SevenZipException
    ///
    /// <summary>
    /// Initializes a new instance of the SevenZipException class with the
    /// specified error reason.
    /// </summary>
    ///
    /// <param name="code">Error code.</param>
    ///
    /* --------------------------------------------------------------------- */
    public SevenZipException(SevenZipCode code) : base(code.ToString()) => Code = code;

    /* --------------------------------------------------------------------- */
    ///
    /// SevenZipException
    ///
    /// <summary>
    /// Initializes a new instance of the SevenZipException class with the
    /// specified arguments.
    /// </summary>
    ///
    /// <param name="code">Error code.</param>
    /// <param name="inner">Inner exception.</param>
    ///
    /* --------------------------------------------------------------------- */
    public SevenZipException(SevenZipCode code, Exception inner) :
        base(code.ToString(), inner)
    {
        Code = code;
        // 本来の原因 (ディスク満杯・デバイス切断・使用中などの I/O 例外) の HRESULT を
        // 引き継ぐことで、Code だけでなく HResult を見る呼び出し側でも正しく分類できる。
        // inner が既定の IOException HRESULT (COR_E_IO) しか持たない場合は上書きしない。
        if (inner is not null && inner.HResult != 0) HResult = inner.HResult;
    }

    #endregion

    #region Properties

    /* --------------------------------------------------------------------- */
    ///
    /// Code
    ///
    /// <summary>
    /// Gets the 7-Zip operation code.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    public SevenZipCode Code { get; }

    #endregion
}
