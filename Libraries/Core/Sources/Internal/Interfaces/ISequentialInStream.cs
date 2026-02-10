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
/// ISequentialInStream
///
/// <summary>
/// Represents an interface for processing the input stream of an archive.
/// </summary>
///
/* ------------------------------------------------------------------------- */
[GeneratedComInterface]
[Guid("23170F69-40C1-278A-0000-000300010000")]
internal partial interface ISequentialInStream
{
    /* --------------------------------------------------------------------- */
    ///
    /// Read
    ///
    /// <summary>
    /// Writes data to 7-zip packer
    /// </summary>
    ///
    /// <param name="data">Pointer to the buffer for writing data into.</param>
    /// <param name="size">Buffer size.</param>
    /// <param name="processedSize">Pointer to receive actual size read.</param>
    ///
    /// <returns>S_OK if success</returns>
    ///
    /// <remarks>
    /// If (size > 0) and there are bytes in stream,
    /// this function must read at least 1 byte.
    /// This function is allowed to read less than "size" bytes.
    /// You must call Read function in loop, if you need exact
    /// amount of data.
    /// </remarks>
    /* --------------------------------------------------------------------- */
    [PreserveSig]
    int Read(nint data, uint size, nint processedSize);
}
