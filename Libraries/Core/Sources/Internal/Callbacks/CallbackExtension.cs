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
/// CallbackExtension
///
/// <summary>
/// Provides extended methods of the CallbackBase class.
/// </summary>
///
/* ------------------------------------------------------------------------- */
internal static class CallbackExtension
{
    #region Methods

    /* --------------------------------------------------------------------- */
    ///
    /// GetException
    ///
    /// <summary>
    /// Creates a new instance of the SevenZipException class
    /// with the specified arguments.
    /// </summary>
    ///
    /// <param name="src">Callback object.</param>
    /// <param name="code">Operation result.</param>
    ///
    /// <returns>Exception object.</returns>
    ///
    /* --------------------------------------------------------------------- */
    public static SevenZipException GetException(this CallbackBase src, int code) =>
        src.Exceptions.Count > 0 ?
        new SevenZipException((SevenZipCode)code, src.Exceptions.Pop()) :
        new SevenZipException((SevenZipCode)code);

    /* --------------------------------------------------------------------- */
    ///
    /// CreateCancelException
    ///
    /// <summary>
    /// Creates a new instance of the OperationCanceledException class
    /// with the specified arguments.
    /// </summary>
    ///
    /// <param name="src">Callback object.</param>
    ///
    /// <returns>Exception object.</returns>
    ///
    /* --------------------------------------------------------------------- */
    public static OperationCanceledException GetCancelException(this CallbackBase src) =>
        src.Exceptions.Count > 0 ?
        new OperationCanceledException("", src.Exceptions.Pop()) :
        new OperationCanceledException();

    /* --------------------------------------------------------------------- */
    ///
    /// ThrowIfError
    ///
    /// <summary>
    /// 7-Zip 操作の結果コードを検査し、エラーの場合は適切な例外をスローします。
    /// </summary>
    ///
    /// <param name="src">コールバックオブジェクト。</param>
    /// <param name="code">操作結果コード。</param>
    /// <param name="checkPassword">
    /// WrongPassword コードを EncryptionException に変換するかどうか。
    /// </param>
    ///
    /* --------------------------------------------------------------------- */
    public static void ThrowIfError(this CallbackBase src, int code, bool checkPassword = false)
    {
        if (code == (int)SevenZipCode.Success) return;
        if (checkPassword && code == (int)SevenZipCode.WrongPassword) throw new EncryptionException();
        if (code == (int)SevenZipCode.Cancel)
        {
            // Cancel コードは「ユーザー主導の中断」と「処理失敗による中断」の両方で返る。
            // CallbackBase.Run は func() が例外を投げると Exceptions に積んだ上で Failed を
            // 報告し、Make が Cancel=true を立てるため 7-Zip へ Cancel コードを返す。一方、
            // 純粋なユーザーキャンセルでは例外は積まれない (Exceptions が空)。
            // したがって Exceptions が非空なら失敗起因の中断であり、OperationCanceledException
            // で包むと呼び出し側にキャンセル扱いされ本当の失敗 (ディスク満杯・I/O エラー等) が
            // 握り潰される。失敗を保持したまま例外を投げ分ける。
            if (src.Exceptions.Count > 0) throw src.GetException(code);
            throw src.GetCancelException();
        }
        throw src.GetException(code);
    }

    #endregion
}
