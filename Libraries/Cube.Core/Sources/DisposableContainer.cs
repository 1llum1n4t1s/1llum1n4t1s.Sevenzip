/* ------------------------------------------------------------------------- */
//
// Copyright (c) 2010 CubeSoft, Inc.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//  http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
/* ------------------------------------------------------------------------- */
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
namespace Cube;

/* ------------------------------------------------------------------------- */
///
/// DisposableContainer
///
/// <summary>
/// Provides functionality to invoke the provided IDisposable objects
/// at once.
/// </summary>
///
/* ------------------------------------------------------------------------- */
public class DisposableContainer : DisposableBase
{
    #region Constructors

    /* --------------------------------------------------------------------- */
    ///
    /// DisposableContainer
    ///
    /// <summary>
    /// Initializes a new instance of the DisposableContainer class.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    public DisposableContainer() { }

    /* --------------------------------------------------------------------- */
    ///
    /// DisposableContainer
    ///
    /// <summary>
    /// Initializes a new instance of the DisposableContainer class with
    /// the specified IDisposable object.
    /// </summary>
    ///
    /// <param name="src">IDisposable object.</param>
    ///
    /* --------------------------------------------------------------------- */
    public DisposableContainer(IDisposable src) => Add(src);

    /* --------------------------------------------------------------------- */
    ///
    /// DisposableContainer
    ///
    /// <summary>
    /// Initializes a new instance of the DisposableContainer class with
    /// the specified one or more IDisposable objects.
    /// </summary>
    ///
    /// <param name="src">IDisposable object.</param>
    /// <param name="more">IDisposable objects.</param>
    ///
    /* --------------------------------------------------------------------- */
    public DisposableContainer(IDisposable src, params IDisposable[] more) => Add(src, more);

    #endregion

    #region Methods

    /* --------------------------------------------------------------------- */
    ///
    /// Contains
    ///
    /// <summary>
    /// Determines whether the specified object is included.
    /// </summary>
    ///
    /// <param name="src">IDisposable objects.</param>
    ///
    /// <returns>true for included.</returns>
    ///
    /* --------------------------------------------------------------------- */
    public bool Contains(IDisposable src) => _core.Contains(src);

    /* --------------------------------------------------------------------- */
    ///
    /// Add
    ///
    /// <summary>
    /// Adds the specified disposable object.
    /// </summary>
    ///
    /// <param name="src">IDisposable objects.</param>
    ///
    /// <remarks>
    /// If the object has already been disposed when called, the Dispose
    /// method of the specified object will be invoked immediately.
    /// </remarks>
    ///
    /* --------------------------------------------------------------------- */
    public void Add(IDisposable src)
    {
        if (Disposed) src.Dispose();
        else _core.Enqueue(src);
    }

    /* --------------------------------------------------------------------- */
    ///
    /// Add
    ///
    /// <summary>
    /// Adds the specified one or more disposable objects.
    /// </summary>
    ///
    /// <param name="src">IDisposable objects.</param>
    /// <param name="more">IDisposable objects.</param>
    ///
    /// <remarks>
    /// If the object has already been disposed when called, the Dispose
    /// method of the specified object will be invoked immediately.
    /// </remarks>
    ///
    /* --------------------------------------------------------------------- */
    public void Add(IDisposable src, params IDisposable[] more)
    {
        Add(src);
        foreach (var e in more) Add(e);
    }

    /* --------------------------------------------------------------------- */
    ///
    /// Add
    ///
    /// <summary>
    /// Converts the specified action to an IDisposable object and adds it.
    /// </summary>
    ///
    /// <param name="action">
    /// Action to be invoked when disposing.
    /// </param>
    ///
    /* --------------------------------------------------------------------- */
    public void Add(Action action) => Add(Disposable.Create(action));

    /* --------------------------------------------------------------------- */
    ///
    /// Dispose
    ///
    /// <summary>
    /// 明示的な破棄では全ての子を解放し、解放失敗を呼び出し元へ返す。
    /// ファイナライザからは他のマネージドオブジェクトを操作しない。
    /// </summary>
    ///
    /// <param name="disposing">
    /// マネージドリソースを明示的に解放する場合は true。
    /// </param>
    ///
    /* --------------------------------------------------------------------- */
    protected override void Dispose(bool disposing)
    {
        if (!disposing) return;

        var errors = new List<Exception>();
        while (_core.TryDequeue(out var e))
        {
            try { e.Dispose(); }
            catch (Exception error) { errors.Add(error); }
        }

        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException(
            "Multiple contained resources failed to dispose.", errors);
    }

    #endregion

    #region Fields
    private readonly ConcurrentQueue<IDisposable> _core = new();
    #endregion
}
