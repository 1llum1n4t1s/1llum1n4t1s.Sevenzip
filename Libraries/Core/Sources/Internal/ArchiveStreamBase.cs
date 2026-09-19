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
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
namespace Cube.FileSystem.SevenZip;

/* ------------------------------------------------------------------------- */
///
/// ArchiveStreamBase
///
/// <summary>
/// Represents the base class for streams that handle archives.
/// </summary>
///
/* ------------------------------------------------------------------------- */
internal class ArchiveStreamBase : DisposableBase
{
    #region Constructors

    /* --------------------------------------------------------------------- */
    ///
    /// ArchiveStreamBase
    ///
    /// <summary>
    /// Initializes a new instance of the ArchiveStreamBase class with
    /// the specified arguments.
    /// </summary>
    ///
    /// <param name="src">Target stream.</param>
    /// <param name="dispose">
    /// Value indicating whether to discard the BaseStream object when
    /// disposed.
    /// </param>
    /// <param name="errorHandler">Stream I/O 例外の通知先。</param>
    ///
    /* --------------------------------------------------------------------- */
    protected ArchiveStreamBase(Stream src, bool dispose, Action<Exception> errorHandler = null)
    {
        BaseStream = src;
        _dispose   = dispose;
        _errorHandler = errorHandler;
    }

    #endregion

    #region Properties

    /* --------------------------------------------------------------------- */
    ///
    /// BaseStream
    ///
    /// <summary>
    /// Gets the target stream.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    protected Stream BaseStream { get; }

    #endregion

    #region Methods

    /* --------------------------------------------------------------------- */
    ///
    /// Seek
    ///
    /// <summary>
    /// Sets the position of the stream.
    /// The method implements IInStream.Seek(long, SeekOrigin, IntPtr).
    /// </summary>
    ///
    /// <param name="offset">Offset value from the origin.</param>
    /// <param name="origin">Starting position.</param>
    /// <param name="result">Position after setting.</param>
    ///
    /* --------------------------------------------------------------------- */
    public virtual void Seek(long offset, SeekOrigin origin, IntPtr result)
    {
        try
        {
            var pos = BaseStream.Seek(offset, origin);
            if (result != IntPtr.Zero) Marshal.WriteInt64(result, pos);
        }
        catch (Exception e)
        {
            Capture(e);
            throw;
        }
    }

    /// <summary>
    /// COM 境界で HRESULT に変換された最初の Stream 例外を呼び出し元へ戻す。
    /// </summary>
    internal void ThrowIfError() => Volatile.Read(ref _error)?.Throw();

    /// <summary>
    /// Stream I/O 例外の通知先を設定する。
    /// </summary>
    internal void SetErrorHandler(Action<Exception> errorHandler) => _errorHandler = errorHandler;

    /// <summary>
    /// COM 境界を越えられない Stream 例外を保持し、HRESULT を返す。
    /// </summary>
    protected int Capture(Exception error)
    {
        var captured = ExceptionDispatchInfo.Capture(error);
        if (Interlocked.CompareExchange(ref _error, captured, null) is null)
            _errorHandler?.Invoke(error);
        return error.HResult;
    }

    /* --------------------------------------------------------------------- */
    ///
    /// Dispose
    ///
    /// <summary>
    /// Releases the unmanaged resources used by the object and
    /// optionally releases the managed resources.
    /// </summary>
    ///
    /// <param name="disposing">
    /// true to release both managed and unmanaged resources;
    /// false to release only unmanaged resources.
    /// </param>
    ///
    /* --------------------------------------------------------------------- */
    protected override void Dispose(bool disposing)
    {
        if (disposing && _dispose) BaseStream.Dispose();
    }

    #endregion

    #region Fields
    private readonly bool _dispose = true;
    private Action<Exception> _errorHandler;
    private ExceptionDispatchInfo _error;
    #endregion
}
