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
