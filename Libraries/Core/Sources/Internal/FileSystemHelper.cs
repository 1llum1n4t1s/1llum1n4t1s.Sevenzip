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
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Cube.FileSystem.SevenZip.Kernel32;
using Microsoft.Win32.SafeHandles;
namespace Cube.FileSystem.SevenZip;

/// <summary>
/// ファイル I/O に関する共通ヘルパー。
/// <c>ArchiveWriter</c> と <c>UpdateCallback</c> で共有する <c>IsFileLocked</c> 判定と
/// 1MB バッファサイズを集約する。
/// </summary>
internal static class FileSystemHelper
{
    /// <summary>
    /// ストリームコピーに使用するデフォルトバッファサイズ (1MB)。
    /// </summary>
    /// <remarks>
    /// write syscall 数の削減と LOH 割り当て回避のバランスで 1MB を採用。
    /// </remarks>
    public const int DefaultBufferSize = 1024 * 1024;

    /// <summary>
    /// HResult が共有違反 (Sharing/Lock Violation) かどうかを判定する。
    /// </summary>
    /// <param name="ex">捕捉した IOException。</param>
    /// <returns>他プロセスがファイルをロックしている場合は true。</returns>
    public static bool IsFileLocked(IOException ex)
    {
        const int SharingViolation = unchecked((int)0x80070020);
        const int LockViolation    = unchecked((int)0x80070021);
        return ex.HResult == SharingViolation || ex.HResult == LockViolation;
    }

    /// <summary>
    /// 展開先ルートとアーカイブ内相対パスから、安全な出力パスを取得する。
    /// </summary>
    /// <exception cref="IOException">
    /// 出力パスがルート外へ出るか、既存の再解析ポイントを経由する場合。
    /// </exception>
    public static string GetExtractionPath(string root, string relativePath)
    {
        if (string.IsNullOrEmpty(root)) throw new ArgumentException("Extraction root is required.", nameof(root));
        if (relativePath is null) throw new ArgumentNullException(nameof(relativePath));

        var rootPath = Path.GetFullPath(root);
        var output   = Path.GetFullPath(Path.Combine(rootPath, relativePath));
        var prefix   = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                       Path.DirectorySeparatorChar;

        if (!string.Equals(output, rootPath, StringComparison.OrdinalIgnoreCase) &&
            !output.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException($"Refusing to extract outside destination: '{output}'.");
        }

        var relative = Path.GetRelativePath(rootPath, output);
        if (relative == ".") return output;

        var current = rootPath;
        foreach (var segment in relative.Split(
                     new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"Refusing to extract through reparse point: '{current}'.");
            }
            catch (FileNotFoundException) { break; }
            catch (DirectoryNotFoundException) { break; }
        }

        return output;
    }

    /// <summary>
    /// 再解析ポイントを辿らず、展開先ディレクトリを安全に作成します。
    /// </summary>
    public static void CreateExtractionDirectory(string root, string relativePath)
    {
        var output = GetExtractionPath(root, relativePath);
        var handles = LockDirectoryChain(root, output, create: true);
        Dispose(handles);
    }

    /// <summary>
    /// 親ディレクトリを削除・改名不能なハンドルで固定し、展開先ファイルを作成します。
    /// </summary>
    public static Stream CreateExtractionFile(string root, string relativePath)
    {
        var output = GetExtractionPath(root, relativePath);
        var parent = Path.GetDirectoryName(output) ?? throw new IOException(
            $"Extraction path has no parent directory: '{output}'.");
        var handles = LockDirectoryChain(root, parent, create: true);
        SafeFileHandle file = null;

        try
        {
            file = NativeMethods.CreateFile(
                ToExtendedPath(output),
                GenericWrite,
                ShareNone,
                IntPtr.Zero,
                OpenAlways,
                FileAttributeNormal | FileFlagOpenReparsePoint,
                IntPtr.Zero
            );
            ThrowIfInvalid(file, output);
            ThrowIfReparsePoint(output);

            var stream = new ExtractionFileStream(file, handles);
            file = null;
            handles = null;
            try
            {
                stream.SetLength(0);
                return stream;
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }
        finally
        {
            file?.Dispose();
            Dispose(handles);
        }
    }

    /// <summary>
    /// 展開済みパスを再解析ポイント競合から固定した状態で属性設定を実行します。
    /// </summary>
    public static void ApplyExtractionAttributes(string root, string relativePath, Action<string> action)
    {
        if (action is null) throw new ArgumentNullException(nameof(action));

        var output = GetExtractionPath(root, relativePath);
        var parent = Path.GetDirectoryName(output) ?? output;
        var handles = LockDirectoryChain(root, parent, create: false);
        if (handles is null) return;

        SafeFileHandle target = null;
        try
        {
            target = NativeMethods.CreateFile(
                ToExtendedPath(output),
                AccessNone,
                ShareRead | ShareWrite,
                IntPtr.Zero,
                OpenExisting,
                FileFlagBackupSemantics | FileFlagOpenReparsePoint,
                IntPtr.Zero
            );
            if (target.IsInvalid)
            {
                var error = Marshal.GetLastPInvokeError();
                if (error is ErrorFileNotFound or ErrorPathNotFound) return;
                throw new Win32Exception(error, $"Failed to lock extraction path: '{output}'.");
            }

            ThrowIfReparsePoint(output);
            action(output);
        }
        finally
        {
            target?.Dispose();
            Dispose(handles);
        }
    }

    private static List<SafeFileHandle> LockDirectoryChain(string root, string target, bool create)
    {
        var rootPath = Path.GetFullPath(root);
        var targetPath = Path.GetFullPath(target);
        var relative = Path.GetRelativePath(rootPath, targetPath);
        if (relative != "." && (Path.IsPathRooted(relative) || relative == ".." ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal)))
            throw new IOException($"Refusing to lock path outside destination: '{targetPath}'.");

        var handles = new List<SafeFileHandle>();
        try
        {
            if (create) Directory.CreateDirectory(rootPath);
            else if (!Directory.Exists(rootPath)) return null;
            handles.Add(OpenDirectory(rootPath));

            if (relative == ".") return handles;
            var current = rootPath;
            foreach (var segment in relative.Split(
                         new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                if (create) Directory.CreateDirectory(current);
                else if (!Directory.Exists(current))
                {
                    Dispose(handles);
                    return null;
                }
                handles.Add(OpenDirectory(current));
            }
            return handles;
        }
        catch
        {
            Dispose(handles);
            throw;
        }
    }

    private static SafeFileHandle OpenDirectory(string path)
    {
        var handle = NativeMethods.CreateFile(
            ToExtendedPath(path),
            AccessNone,
            ShareRead | ShareWrite,
            IntPtr.Zero,
            OpenExisting,
            FileFlagBackupSemantics | FileFlagOpenReparsePoint,
            IntPtr.Zero
        );
        try
        {
            ThrowIfInvalid(handle, path);
            ThrowIfReparsePoint(path);
            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static void ThrowIfInvalid(SafeFileHandle handle, string path)
    {
        if (!handle.IsInvalid) return;
        var error = Marshal.GetLastPInvokeError();
        throw new Win32Exception(error, $"Failed to lock extraction path: '{path}'.");
    }

    private static void ThrowIfReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Refusing to extract through reparse point: '{path}'.");
    }

    private static string ToExtendedPath(string path)
    {
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path;
        return path.StartsWith(@"\\", StringComparison.Ordinal)
            ? @"\\?\UNC\" + path[2..]
            : @"\\?\" + path;
    }

    private static void Dispose(List<SafeFileHandle> handles)
    {
        if (handles is null) return;
        for (var i = handles.Count - 1; i >= 0; i--) handles[i].Dispose();
    }

    private sealed class ExtractionFileStream : FileStream
    {
        public ExtractionFileStream(SafeFileHandle handle, List<SafeFileHandle> guards) :
            base(handle, FileAccess.Write, 4096, false) => _guards = guards;

        protected override void Dispose(bool disposing)
        {
            try { base.Dispose(disposing); }
            finally
            {
                if (disposing)
                {
                    FileSystemHelper.Dispose(_guards);
                    _guards = null;
                }
            }
        }

        private List<SafeFileHandle> _guards;
    }

    private const uint AccessNone = 0;
    private const uint GenericWrite = 0x40000000;
    private const uint ShareNone = 0;
    private const uint ShareRead = 0x00000001;
    private const uint ShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint OpenAlways = 4;
    private const uint FileAttributeNormal = 0x00000080;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const int ErrorFileNotFound = 2;
    private const int ErrorPathNotFound = 3;

    /// <summary>
    /// 指定したパスのファイルに対して <c>FlushFileBuffers</c> 相当の
    /// ディスク同期フラッシュを実行する。
    /// </summary>
    /// <param name="path">フラッシュ対象のファイルパス。</param>
    /// <remarks>
    /// 書き込み済みファイルを短時間 FileShare.Read で開き直し、<see cref="FileStream.Flush(bool)"/>
    /// を <c>flushToDisk: true</c> で呼ぶことで NTFS 等のファイルキャッシュをディスクに書き出す。
    /// ファイルが存在しない場合やロックされている場合は黙って失敗する (クリティカルではない)。
    /// </remarks>
    public static void FlushFile(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        try
        {
            // FileMode.Open + FileAccess.Write で開くと truncate されないため安全。
            // SeekOrigin.End に進めてから Flush を呼ぶ。書き込み先は空でも OK。
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Write,
                FileShare.Read, bufferSize: 1);
            fs.Flush(flushToDisk: true);
        }
        catch (Exception ex)
        {
            // アンチウイルス等で SharingViolation が発生するとこの経路を通り、FlushToDisk が
            // サイレントに失敗する。デバッグ可能なよう Warn ログを残す (path は secret ではない)。
            Logger.Warn($"[FlushFile] Failed to flush '{path}': {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// 渡された Stream が FileStream の場合、ディスク同期フラッシュを実行する。
    /// </summary>
    /// <param name="stream">フラッシュ対象の Stream。</param>
    /// <remarks>
    /// MemoryStream / NetworkStream など FileStream でない場合は通常の <see cref="Stream.Flush"/> のみ呼ぶ。
    /// </remarks>
    public static void FlushToDiskIfFileStream(Stream stream)
    {
        if (stream is null) return;
        if (stream is FileStream fs)
        {
            try { fs.Flush(flushToDisk: true); }
            catch (Exception ex)
            {
                Logger.Warn($"[FlushToDisk] FileStream.Flush(true) failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
        else
        {
            try { stream.Flush(); }
            catch (Exception ex)
            {
                Logger.Warn($"[FlushToDisk] Stream.Flush failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 指定ディレクトリに dest ファイル分 + バッファの空き容量があることをベストエフォートで確認する。
    /// </summary>
    /// <param name="workDir">tmp ファイルを配置するディレクトリ。</param>
    /// <param name="referenceFile">サイズ基準となる既存ファイル (通常は上書き先 dest)。存在しなければチェックスキップ。</param>
    /// <remarks>
    /// <para>
    /// 既存ファイルサイズの 1.1 倍の空き容量を要求する。圧縮後サイズは未知だが、上書きシナリオでは
    /// 概ね「元サイズ ± 数 %」に収まることが多いので近似として使える。
    /// </para>
    /// <para>
    /// 空き容量取得に失敗した場合 (UNC パス / 未サポート FS 等) は黙ってスキップする (早期失敗は回避)。
    /// </para>
    /// </remarks>
    /// <exception cref="IOException">空き容量が不足している場合。</exception>
    public static void EnsureEnoughFreeSpace(string workDir, string referenceFile)
    {
        if (string.IsNullOrEmpty(workDir)) return;
        if (string.IsNullOrEmpty(referenceFile) || !File.Exists(referenceFile)) return;

        try
        {
            var refSize = new FileInfo(referenceFile).Length;
            var required = (long)(refSize * 1.1);
            var root = Path.GetPathRoot(Path.GetFullPath(workDir));
            if (string.IsNullOrEmpty(root)) return;

            var drive = new DriveInfo(root);
            if (!drive.IsReady) return;
            var free = drive.AvailableFreeSpace;

            if (free < required)
            {
                throw new IOException(
                    $"Insufficient free space on '{root}'. Required {required:N0} bytes " +
                    $"(1.1x of existing {refSize:N0} bytes), available {free:N0} bytes.");
            }
        }
        catch (IOException)
        {
            throw; // 容量不足エラーはそのまま伝播
        }
        catch
        {
            // DriveInfo 取得失敗等はベストエフォート無視
        }
    }
}
