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
namespace Cube.FileSystem.SevenZip;

/* ------------------------------------------------------------------------- */
///
/// PasswordCallback
///
/// <summary>
/// Provides callback functions to query the password when extracting
/// archived files.
/// </summary>
///
/* ------------------------------------------------------------------------- */
internal abstract partial class PasswordCallback : CallbackBase, ICryptoGetTextPassword
{
    #region Constructors

    /* --------------------------------------------------------------------- */
    ///
    /// PasswordCallback
    ///
    /// <summary>
    /// Initializes a new instance of the PasswordCallback with the specified
    /// arguments.
    /// </summary>
    ///
    /// <param name="src">Source archive.</param>
    /// <param name="progress">User object to report the progress.</param>
    ///
    /* --------------------------------------------------------------------- */
    protected PasswordCallback(string src, IProgress<Report> progress) : base(progress) => Source = src;

    #endregion

    #region Properties

    /* --------------------------------------------------------------------- */
    ///
    /// Source
    ///
    /// <summary>
    /// Gets the path of the archive file.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    public string Source { get; }

    /* --------------------------------------------------------------------- */
    ///
    /// Password
    ///
    /// <summary>
    /// Gets or sets the object to query the password.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    public IQuery<string> Password { get; init; }

    /* --------------------------------------------------------------------- */
    ///
    /// PasswordTimes
    ///
    /// <summary>
    /// Get the number of times the password was requested.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    public int PasswordTimes { get; private set; }

    #endregion

    #region ICryptoGetTextPassword

    /* --------------------------------------------------------------------- */
    ///
    /// CryptoGetTextPassword
    ///
    /// <summary>
    /// Gets the password of the provided archive.
    /// </summary>
    ///
    /// <param name="value">Password result.</param>
    ///
    /// <returns>Operation result</returns>
    ///
    /* --------------------------------------------------------------------- */
    public int CryptoGetTextPassword(out string value)
    {
        PasswordTimes++;
        value = string.Empty;
        if (Password is null) return (int)SevenZipCode.WrongPassword;

        var e = Query.NewMessage(Source);

        // 利用者実装の IQuery<string> を呼ぶため、他のユーザーコールバック (CallbackBase.Run /
        // FireFileEvent) と同じ規約で保護する。これは CCW メソッドなので、例外を素通しすると
        // HRESULT へ変換されてマネージ例外オブジェクトが破棄され、原因が完全に失われる。
        // 例: AsyncPasswordQuery が UI スレッド検出時に投げる案内文付き InvalidOperationException が
        // 「アーカイブではない」に化けてガードの目的が消える。Exceptions へ積めば呼び出し元が
        // inner exception として内包して投げ直せる。
        try { Password.Request(e); }
        catch (Exception err) when (!IsFatalException(err))
        {
            PushException(err);
            return (int)SevenZipCode.UnknownError;
        }

        if (e.Cancel) return (int)SevenZipCode.Cancel;

        var done = e.Value.HasValue();
        if (done) value = e.Value;
        return (int)(done ? SevenZipCode.Success : SevenZipCode.WrongPassword);
    }

    #endregion
}
