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
using Cube.Text.Extensions;
using SuperLightLogger;
using System;
namespace Cube.Logging;

/* ------------------------------------------------------------------------- */
///
/// LoggerSource
///
/// <summary>
/// Provides the ILoggerSource implementation by using the SuperLightLogger package.
/// </summary>
///
/* ------------------------------------------------------------------------- */
public sealed class LoggerSource : ILoggerSource
{
    #region Constructors

    /* --------------------------------------------------------------------- */
    ///
    /// LoggerSource
    ///
    /// <summary>
    /// Initializes a new instance of the LoggerSource class with default
    /// configuration values.
    /// </summary>
    ///
    /// <remarks>
    /// The SuperLightLogger file target is configured exactly once per
    /// process on the first instantiation. Subsequent instantiations reuse
    /// the first caller's configuration.
    /// </remarks>
    ///
    /* --------------------------------------------------------------------- */
    public LoggerSource() : this("application.log") { }

    /* --------------------------------------------------------------------- */
    ///
    /// LoggerSource
    ///
    /// <summary>
    /// Initializes a new instance of the LoggerSource class with the
    /// specified file name.
    /// </summary>
    ///
    /// <param name="fileName">Target log file name.</param>
    /// <param name="layout">Log layout template (SuperLightLogger syntax).</param>
    /// <param name="archiveAboveSize">Archive the file when it exceeds this size (bytes).</param>
    /// <param name="maxArchiveFiles">Maximum number of archive files to keep.</param>
    ///
    /// <remarks>
    /// The SuperLightLogger file target is configured exactly once per
    /// process on the first instantiation. Arguments passed on later
    /// instantiations are silently ignored to preserve the first
    /// caller's configuration.
    /// </remarks>
    ///
    /* --------------------------------------------------------------------- */
    public LoggerSource(
        string fileName,
        string layout = DefaultLayout,
        long archiveAboveSize = DefaultArchiveAboveSize,
        int maxArchiveFiles = DefaultMaxArchiveFiles)
    {
        lock (_lock)
        {
            if (_configured) return;

            LogManager.Configure(builder =>
            {
                builder.AddSuperLightFile(opt =>
                {
                    opt.FileName         = fileName;
                    opt.Layout           = layout;
                    opt.ArchiveAboveSize = archiveAboveSize;
                    opt.MaxArchiveFiles  = maxArchiveFiles;
                });
                builder.SetMinimumLevel("Debug");
            });

            _configured = true;
        }
    }

    #endregion

    #region Methods

    /* --------------------------------------------------------------------- */
    ///
    /// Log
    ///
    /// <summary>
    /// Writes a log entry.
    /// </summary>
    ///
    /// <param name="path">Source file path.</param>
    /// <param name="number">Source line number.</param>
    /// <param name="level">Log level.</param>
    /// <param name="message">Logging message.</param>
    ///
    /* --------------------------------------------------------------------- */
    public void Log(string path, int number, LogLevel level, string message)
    {
        var e = LogManager.GetLogger(GetLoggerName(path));
        var m = $"({number}) {message}";

        switch (level)
        {
            case LogLevel.Trace:       e.Trace(m); break;
            case LogLevel.Debug:       e.Debug(m); break;
            case LogLevel.Information: e.Info(m);  break;
            case LogLevel.Warning:     e.Warn(m);  break;
            case LogLevel.Error:       e.Error(m); break;
        }
    }

    #endregion

    #region Implementations

    /* --------------------------------------------------------------------- */
    ///
    /// GetLoggerName
    ///
    /// <summary>
    /// Gets the logger name with the specified path.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    private static string GetLoggerName(string path)
    {
        if (!path.HasValue()) return "None";

        var p0 = Math.Min(path.LastIndexOfAny(new[] { '/', '\\' }) + 1, path.Length - 1);
        var p1 = path.LastIndexOf('.');
        return p1 > p0 ? path.Substring(p0, p1 - p0) : path.Substring(p0);
    }

    #endregion

    #region Fields

    private const string DefaultLayout =
        "${longdate} [${uppercase:${level}}] ${logger} ${message}";
    private const long DefaultArchiveAboveSize = 1_000_000L;
    private const int  DefaultMaxArchiveFiles  = 5;

    // SuperLightLogger の静的グローバル設定を 1 回限定にするための同期オブジェクト
    private static readonly object _lock = new();
    private static bool _configured;

    #endregion
}
