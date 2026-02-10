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
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
namespace Cube.FileSystem.SevenZip;

/* ------------------------------------------------------------------------- */
///
/// IArchiveOpenVolumeCallback
///
/// <summary>
/// Represents an interface for decompressing a split archive.
/// </summary>
///
/* ------------------------------------------------------------------------- */
[GeneratedComInterface]
[Guid("23170F69-40C1-278A-0000-000600300000")]
internal partial interface IArchiveOpenVolumeCallback
{
    /* --------------------------------------------------------------------- */
    ///
    /// GetProperty
    ///
    /// <summary>
    /// Gets the property of the compressed file for the specified ID.
    /// </summary>
    ///
    /// <param name="pid">Property ID</param>
    /// <param name="value">
    /// Value corresponding to the property ID.
    /// </param>
    ///
    /// <returns>ErrorCode.None for success.</returns>
    ///
    /* --------------------------------------------------------------------- */
    [PreserveSig]
    int GetProperty(ItemPropId pid, ref PropVariant value);

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
    [PreserveSig]
    int GetStream([MarshalUsing(typeof(Utf16StringMarshaller))] string name, out IInStream stream);
}
