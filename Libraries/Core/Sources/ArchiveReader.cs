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
using System.Collections.Generic;
namespace Cube.FileSystem.SevenZip;

/* ------------------------------------------------------------------------- */
///
/// ArchiveReader
///
/// <summary>
/// Provides functionality to extract an archived file.
/// </summary>
///
/* ------------------------------------------------------------------------- */
public sealed class ArchiveReader : DisposableBase
{
    #region Constructors

    /* --------------------------------------------------------------------- */
    ///
    /// ArchiveReader
    ///
    /// <summary>
    /// Initializes a new instance of the ArchiveReader class with
    /// the specified path.
    /// </summary>
    ///
    /// <param name="src">Path of the archive.</param>
    ///
    /* --------------------------------------------------------------------- */
    public ArchiveReader(string src) : this(src, string.Empty) { }

    /* --------------------------------------------------------------------- */
    ///
    /// ArchiveReader
    ///
    /// <summary>
    /// Initializes a new instance of the ArchiveReader class with
    /// the specified arguments.
    /// </summary>
    ///
    /// <param name="src">Path of the archive.</param>
    /// <param name="password">Password of the archive.</param>
    ///
    /* --------------------------------------------------------------------- */
    public ArchiveReader(string src, string password) : this(src, password, new()) { }

    /* --------------------------------------------------------------------- */
    ///
    /// ArchiveReader
    ///
    /// <summary>
    /// Initializes a new instance of the ArchiveReader class with
    /// the specified arguments.
    /// </summary>
    ///
    /// <param name="src">Path of the archive.</param>
    /// <param name="password">Query object to get password.</param>
    ///
    /* --------------------------------------------------------------------- */
    public ArchiveReader(string src, IQuery<string> password) : this(src, password, new()) { }

    /* --------------------------------------------------------------------- */
    ///
    /// ArchiveReader
    ///
    /// <summary>
    /// Initializes a new instance of the ArchiveReader class with
    /// the specified arguments.
    /// </summary>
    ///
    /// <param name="src">Path of the archive.</param>
    /// <param name="options">Options to extract the archive.</param>
    ///
    /* --------------------------------------------------------------------- */
    public ArchiveReader(string src, ArchiveOption options) : this(src, string.Empty, options) { }

    /* --------------------------------------------------------------------- */
    ///
    /// ArchiveReader
    ///
    /// <summary>
    /// Initializes a new instance of the ArchiveReader class with
    /// the specified arguments.
    /// </summary>
    ///
    /// <param name="src">Path of the archive.</param>
    /// <param name="password">Password of the archive.</param>
    /// <param name="options">Options to extract the archive.</param>
    ///
    /* --------------------------------------------------------------------- */
    public ArchiveReader(string src, string password, ArchiveOption options) :
        this(FormatFactory.From(src), src, new(password), options) { }

    /* --------------------------------------------------------------------- */
    ///
    /// ArchiveReader
    ///
    /// <summary>
    /// Initializes a new instance of the ArchiveReader class with
    /// the specified arguments.
    /// </summary>
    ///
    /// <param name="src">Path of the archive.</param>
    /// <param name="password">Query object to get password.</param>
    /// <param name="options">Options to extract the archive.</param>
    ///
    /* --------------------------------------------------------------------- */
    public ArchiveReader(string src, IQuery<string> password, ArchiveOption options) :
        this(FormatFactory.From(src), src, new(password), options) { }

    /* --------------------------------------------------------------------- */
    ///
    /// ArchiveReader
    ///
    /// <summary>
    /// Initializes a new instance of the ArchiveReader class with
    /// the specified arguments.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    private ArchiveReader(Format format, string src, PasswordQuery password, ArchiveOption options)
    {
        if (format == Format.Unknown) throw new UnknownFormatException();

        Source  = src;
        Format  = format;
        Options = options;
        _password  = password;
        var lib = Hook(new SevenZipLibrary());
        _core = lib.GetInArchive(format);

        var cb = Hook(new OpenCallback(src) { Password = _password });
        var ss = new ArchiveStreamReader(Io.Open(src));
        cb.Streams.Add(ss);

        // Keep managed references alive to prevent GC from collecting
        // objects whose CCWs are held by native 7-Zip.
        _openStream   = ss;
        _openCallback = cb;

        var code = _core.Open(ss, IntPtr.Zero, cb);
        GC.KeepAlive(cb);
        GC.KeepAlive(ss);
        if (code != 0) Logger.Warn($"[Open] Code:{code}");

        Items = new ArchiveCollection(_core, (int)_core.GetNumberOfItems(), src);
    }

    #endregion

    #region Properties

    /* --------------------------------------------------------------------- */
    ///
    /// Source
    ///
    /// <summary>
    /// Gets the archive path.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    public string Source { get; }

    /* --------------------------------------------------------------------- */
    ///
    /// Format
    ///
    /// <summary>
    /// Gets the archive format.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    public Format Format { get; }

    /* --------------------------------------------------------------------- */
    ///
    /// Items
    ///
    /// <summary>
    /// Gets the collection of archived items.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    public IReadOnlyList<ArchiveEntity> Items { get; }

    /* --------------------------------------------------------------------- */
    ///
    /// Options
    ///
    /// <summary>
    /// Gets the options to extract the provided archive.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    public ArchiveOption Options { get; }

    #endregion

    #region Methods

    /* --------------------------------------------------------------------- */
    ///
    /// Save
    ///
    /// <summary>
    /// Extracts all files and saves them in the specified directory.
    /// </summary>
    ///
    /// <param name="dest">
    /// Path of the directory to save. If the parameter is set to null
    /// or empty, the method invokes as a test mode.
    /// </param>
    ///
    /* --------------------------------------------------------------------- */
    public void Save(string dest) => Save(dest, null);

    /* --------------------------------------------------------------------- */
    ///
    /// Save
    ///
    /// <summary>
    /// Extracts all files except those matching the specified filter
    /// function and saves them in the specified directory.
    /// </summary>
    ///
    /// <param name="dest">
    /// Path of the directory to save. If the parameter is set to null
    /// or empty, the method invokes as a test mode.
    /// </param>
    ///
    /// <param name="progress">Progress object.</param>
    ///
    /* --------------------------------------------------------------------- */
    public void Save(string dest, IProgress<Report> progress) => Save(dest, null, progress);

    /* --------------------------------------------------------------------- */
    ///
    /// Save
    ///
    /// <summary>
    /// Extracts the files corresponding to the specified indices except
    /// those matching the specified filter function, and saves them
    /// in the specified directory.
    /// </summary>
    ///
    /// <param name="dest">
    /// Path of the directory to save. If the parameter is set to null
    /// or empty, the method invokes as a test mode.
    /// </param>
    /// <param name="src">Source indices to extract.</param>
    /// <param name="progress">Progress object.</param>
    ///
    /* --------------------------------------------------------------------- */
    public unsafe void Save(string dest, uint[] src, IProgress<Report> progress)
    {
        try
        {
            using var cb = CreateCallback(dest, src, progress);
            var n    = (uint?)src?.Length ?? uint.MaxValue;
            var test = dest.HasValue() ? 0 : 1;

            int code;
            fixed (uint* p = src)
            {
                code = _core.Extract(p, n, test, cb);
            }
            GC.KeepAlive(cb);

            Logger.Debug($"Code:{code}");
            cb.ThrowIfError(code, checkPassword: true);
        }
        finally { _password.Reset(); }
    }

    /* --------------------------------------------------------------------- */
    ///
    /// Dispose
    ///
    /// <summary>
    /// Releases the unmanaged resources used by the ArchiveReader
    /// and optionally releases the managed resources.
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
        if (_core != null)
        {
            _core.Close();
            SevenZipLibrary.ReleaseComWrapper(_core);
        }
        _openStream   = null;
        _openCallback = null;
        _disposable.Dispose();
    }

    #endregion

    #region Implementations

    /* --------------------------------------------------------------------- */
    ///
    /// Hook
    ///
    /// <summary>
    /// Attaches the specified object as disposable.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    private T Hook<T>(T src) where T : IDisposable
    {
        _disposable.Add(src);
        return src;
    }

    /* --------------------------------------------------------------------- */
    ///
    /// CreateCallback
    ///
    /// <summary>
    /// Creates a new instance of the ExtractCallback class with the
    /// specified arguments.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    private ExtractCallback CreateCallback(string dest, uint[] src, IProgress<Report> progress)
    {
        var e = src is not null ?
                new ArchiveEnumerator(Items, src) :
                new ArchiveEnumerator(Items);

        return new(Source, e, progress)
        {
            Destination = dest ?? string.Empty,
            Password    = _password,
            Filter      = Options.Filter,
        };
    }

    #endregion

    #region Fields
    private readonly IInArchive _core;
    private readonly PasswordQuery _password;
    private readonly DisposableContainer _disposable = new();
    // Prevent GC from collecting stream/callback whose CCWs are held by native 7-Zip.
    private object _openStream;
    private object _openCallback;
    #endregion
}
