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
using Cube.FileSystem.SevenZip.Kernel32;
using Cube.Reflection.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
namespace Cube.FileSystem.SevenZip;

/* ------------------------------------------------------------------------- */
///
/// SevenZipLibrary
///
/// <summary>
/// Provides functionality of the 7z.dll.
/// </summary>
///
/* ------------------------------------------------------------------------- */
internal sealed class SevenZipLibrary : DisposableBase
{
    private static readonly StrategyBasedComWrappers s_comWrappers = new();

    #region Constructors

    /* --------------------------------------------------------------------- */
    ///
    /// SevenZipLibrary
    ///
    /// <summary>
    /// Initializes a new instance of the SevenZipLibrary class.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    public SevenZipLibrary()
    {
        var dll = Io.Combine(GetType().Assembly.GetDirectoryName(), "7z.dll");
        _handle = NativeMethods.LoadLibrary(dll);
        if (_handle.IsInvalid) throw new Win32Exception("LoadLibrary");

        _createObjectPtr = NativeMethods.GetProcAddress(_handle, "CreateObject");
        if (_createObjectPtr == IntPtr.Zero) throw new Win32Exception("GetProcAddress");
    }

    #endregion

    #region Methods

    /* --------------------------------------------------------------------- */
    ///
    /// GetInArchive
    ///
    /// <summary>
    /// Gets the InArchive object with the specified format.
    /// </summary>
    ///
    /// <param name="format">Archive format.</param>
    ///
    /// <returns>InArchive object.</returns>
    ///
    /* --------------------------------------------------------------------- */
    public IInArchive GetInArchive(Format format) => GetArchive<IInArchive>(format.ToClassId());

    /* --------------------------------------------------------------------- */
    ///
    /// GetInArchive
    ///
    /// <summary>
    /// Gets the InArchive object with the specified class ID.
    /// </summary>
    ///
    /// <param name="clsid">Class ID.</param>
    ///
    /// <returns>InArchive object.</returns>
    ///
    /* --------------------------------------------------------------------- */
    public IInArchive GetInArchive(Guid clsid) => GetArchive<IInArchive>(clsid);

    /* --------------------------------------------------------------------- */
    ///
    /// GetOutArchive
    ///
    /// <summary>
    /// Gets the OutArchive object with the specified archive format.
    /// </summary>
    ///
    /// <param name="format">Archive format.</param>
    ///
    /// <returns>OutArchive object.</returns>
    ///
    /* --------------------------------------------------------------------- */
    public IOutArchive GetOutArchive(Format format) => GetArchive<IOutArchive>(format.ToClassId());

    /* --------------------------------------------------------------------- */
    ///
    /// GetOutArchive
    ///
    /// <summary>
    /// Gets the OutArchive object with the specified class ID.
    /// </summary>
    ///
    /// <param name="clsid">Class ID.</param>
    ///
    /// <returns>OutArchive object.</returns>
    ///
    /* --------------------------------------------------------------------- */
    public IOutArchive GetOutArchive(Guid clsid) => GetArchive<IOutArchive>(clsid);

    /* --------------------------------------------------------------------- */
    ///
    /// QueryInterface
    ///
    /// <summary>
    /// Queries a COM object for a specific interface using its IID.
    /// </summary>
    ///
    /// <typeparam name="T">Target interface type.</typeparam>
    ///
    /// <param name="comObject">
    /// Source COM object wrapped by ComWrappers.
    /// </param>
    ///
    /// <returns>
    /// Wrapped interface or null if the interface is not supported.
    /// </returns>
    ///
    /* --------------------------------------------------------------------- */
    public T QueryInterface<T>(object comObject) where T : class
    {
        if (!ComWrappers.TryGetComInstance(comObject, out var unkPtr))
            return null;
        try
        {
            var iid = typeof(T).GUID;
            var hr = Marshal.QueryInterface(unkPtr, in iid, out var ptr);
            if (hr != 0 || ptr == IntPtr.Zero) return null;
            // UniqueInstance takes ownership of the reference from QueryInterface
            var obj = s_comWrappers
                .GetOrCreateObjectForComInstance(ptr, CreateObjectFlags.UniqueInstance);
            Track(obj);
            return (T)obj;
        }
        finally { Marshal.Release(unkPtr); }
    }

    /* --------------------------------------------------------------------- */
    ///
    /// ReleaseComWrapper
    ///
    /// <summary>
    /// Explicitly releases a COM wrapper created with UniqueInstance to
    /// prevent GC Finalize from calling Release after the DLL is unloaded.
    /// Uses ComObject.FinalRelease() which atomically releases the COM
    /// pointer, suppresses finalization, and is safe to call multiple times.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    public static StrategyBasedComWrappers GetComWrappers() => s_comWrappers;

    public static void ReleaseComWrapper(object comWrapper)
    {
        if (comWrapper is ComObject comObject) comObject.FinalRelease();
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
        // Deterministically release all tracked COM wrappers BEFORE unloading
        // the DLL. ComObject.FinalRelease() atomically releases the COM pointer
        // and suppresses finalization, preventing the GC from later calling
        // Release on vtable pointers inside the unloaded 7z.dll.
        foreach (var obj in _tracked) ReleaseComWrapper(obj);
        _tracked.Clear();

        if (_handle != null && !_handle.IsClosed) _handle.Close();
    }

    #endregion

    #region Implementations

    /* --------------------------------------------------------------------- */
    ///
    /// Track
    ///
    /// <summary>
    /// Tracks a COM wrapper object for deterministic release during Dispose.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    private void Track(object comWrapper) => _tracked.Add(comWrapper);

    /* --------------------------------------------------------------------- */
    ///
    /// GetArchive
    ///
    /// <summary>
    /// Creates a COM archive object with the specified class ID and
    /// wraps it as the specified interface type.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    private T GetArchive<T>(Guid clsid) where T : class
    {
        var iid = typeof(T).GUID;
        var ptr = CreateObject(ref clsid, ref iid);
        var obj = s_comWrappers
            .GetOrCreateObjectForComInstance(ptr, CreateObjectFlags.UniqueInstance);
        Track(obj);
        return (T)obj;
    }

    /* --------------------------------------------------------------------- */
    ///
    /// CreateObject
    ///
    /// <summary>
    /// Invokes the 7z.dll CreateObject function via an unsafe function
    /// pointer and returns the raw COM interface pointer.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    private unsafe nint CreateObject(ref Guid clsid, ref Guid iid)
    {
        var fn = (delegate* unmanaged[Stdcall]<Guid*, Guid*, nint*, int>)_createObjectPtr;
        nint result;
        int hr;
        fixed (Guid* pClsid = &clsid)
        fixed (Guid* pIid = &iid)
        {
            hr = fn(pClsid, pIid, &result);
        }
        if (hr != 0) Marshal.ThrowExceptionForHR(hr);
        return result;
    }

    #endregion

    #region Fields
    private readonly SafeLibraryHandle _handle;
    private readonly IntPtr _createObjectPtr;
    private readonly List<object> _tracked = [];
    #endregion
}
