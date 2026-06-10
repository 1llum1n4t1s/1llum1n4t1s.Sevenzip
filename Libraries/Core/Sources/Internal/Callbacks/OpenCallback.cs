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
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
namespace Cube.FileSystem.SevenZip;

/* ------------------------------------------------------------------------- */
///
/// OpenCallback
///
/// <summary>
/// Provides callback functions to open an archived file.
/// </summary>
///
/* ------------------------------------------------------------------------- */
[GeneratedComClass]
internal partial class OpenCallback : PasswordCallback, IArchiveOpenCallback, IArchiveOpenVolumeCallback
{
    #region Constructors

    /* --------------------------------------------------------------------- */
    ///
    /// OpenCallback
    ///
    /// <summary>
    /// Initializes a new instance of the OpenCallback class with the
    /// specified arguments.
    /// </summary>
    ///
    /// <param name="src">Path of the archived file.</param>
    ///
    /* --------------------------------------------------------------------- */
    public OpenCallback(string src) : base(src, null) { }

    #endregion

    #region Properties

    /* --------------------------------------------------------------------- */
    ///
    /// Streams
    ///
    /// <summary>
    /// Gets the collection of input streams.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    public ICollection<ArchiveStreamReader> Streams { get; } = new List<ArchiveStreamReader>();

    #endregion

    #region IArchiveOpenCallback

    /* --------------------------------------------------------------------- */
    ///
    /// SetTotal
    ///
    /// <summary>
    /// Gets the total size of the compressed file upon decompression.
    /// </summary>
    ///
    /// <param name="count">Total number of files.</param>
    /// <param name="bytes">Total compressed bytes.</param>
    ///
    /// <returns>ErrorCode.None for success.</returns>
    ///
    /* --------------------------------------------------------------------- */
    public int SetTotal(IntPtr count, IntPtr bytes)
    {
        if (count != IntPtr.Zero) TotalCount = Marshal.ReadInt64(count);
        if (bytes != IntPtr.Zero) TotalBytes = Marshal.ReadInt64(bytes);
        return (int)SevenZipCode.Success;
    }

    /* --------------------------------------------------------------------- */
    ///
    /// SetCompleted
    ///
    /// <summary>
    /// Get the size of the stream ready to be read.
    /// </summary>
    ///
    /// <param name="count">Number of files.</param>
    /// <param name="bytes">Completed bytes.</param>
    ///
    /// <returns>ErrorCode.None for success.</returns>
    ///
    /* --------------------------------------------------------------------- */
    public int SetCompleted(IntPtr count, IntPtr bytes)
    {
        if (count != IntPtr.Zero) Count = Marshal.ReadInt64(count);
        if (bytes != IntPtr.Zero) Bytes = Marshal.ReadInt64(bytes);
        return (int)SevenZipCode.Success;
    }

    #endregion

    #region IArchiveOpenVolumeCallback

    /* --------------------------------------------------------------------- */
    ///
    /// GetProperty
    ///
    /// <summary>
    /// Gets the property of the compressed file for the specified ID.
    /// </summary>
    ///
    /// <param name="pid">Property ID</param>
    /// <param name="value">Value for the specified property ID.</param>
    ///
    /// <returns>ErrorCode.None for success.</returns>
    ///
    /* --------------------------------------------------------------------- */
    public int GetProperty(ItemPropId pid, ref PropVariant value)
    {
        if (pid == ItemPropId.Name) value.Set(Source);
        else value.Clear();
        return (int)SevenZipCode.Success;
    }

    /* --------------------------------------------------------------------- */
    ///
    /// GetStream
    ///
    /// <summary>
    /// Get the stream corresponding to the volume to be read.
    /// </summary>
    ///
    /// <param name="name">Volume name.</param>
    /// <param name="stream">Target input stream.</param>
    ///
    /// <returns>ErrorCode.None for success.</returns>
    ///
    /* --------------------------------------------------------------------- */
    public int GetStream(string name, out IInStream stream)
    {
        stream = null;

        var src = Io.Exists(name) ? name : Combine(Io.GetDirectoryName(Source), name);
        if (Io.Exists(src))
        {
            var dest = new ArchiveStreamReader(Io.Open(src));
            Streams.Add(dest);
            stream = dest;
        }

        return (int)SevenZipCode.Success;
    }

    #endregion

    #region IDisposable

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
        // finalizer 経路 (disposing == false) では他のマネージドオブジェクト
        // (ArchiveStreamReader → BaseStream) に触らない。各ストリームは自身の
        // finalizer / SafeHandle が回収する (ArchiveReader.Dispose の同ガード参照)。
        if (disposing)
        {
            foreach (var item in Streams) item.Dispose();
        }
        Streams.Clear();
    }

    #endregion
}
