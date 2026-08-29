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
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Cube.FileSystem.SevenZip.Tests;

/* ------------------------------------------------------------------------- */
///
/// UpdateCallbackProgressTest
///
/// <summary>
/// UpdateCallback の SetCompleted（進捗集計）を検証するテスト。
/// </summary>
///
/// <remarks>
/// 7-zip の completeValue は SetTotal と同尺度のグローバル累積値だが、マルチスレッド
/// 圧縮ではスレッド間の読み取りレースで直前より小さい値がまれに届く。
/// 旧実装はこの後退を「ファイル切替によるリセット」と誤認して直前値を累積加算し、
/// 大規模アーカイブで進捗が早期に 100% へ張り付く原因になっていた
/// （実測: 528k ファイル / 60GB の ZIP で 23.7% 過剰計上 → 9.2 分間 100% 表示のまま）。
/// </remarks>
///
/* ------------------------------------------------------------------------- */
[TestFixture]
internal class UpdateCallbackProgressTest
{
    /* --------------------------------------------------------------------- */
    ///
    /// SetCompleted_NonMonotonic_DoesNotOvercount
    ///
    /// <summary>
    /// 非単調な completeValue 列（マルチスレッド圧縮の読み取りレースを模擬）でも
    /// Bytes が過剰計上されず、単調最大値として扱われることを確認します。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void SetCompleted_NonMonotonic_DoesNotOvercount()
    {
        var src = Path.Combine(Path.GetTempPath(), $"UpdateCallbackProgressTest_{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(src, new byte[1000]);
        try
        {
            var items = new List<RawEntity> { new(src, "a.bin") };
            using var cb = new UpdateCallback(items, new SilentProgress())
            {
                Destination = string.Empty,
                Password    = string.Empty,
            };
            Assert.That(cb.TotalBytes, Is.EqualTo(1000L));

            var p = Marshal.AllocHGlobal(sizeof(long));
            try
            {
                // 後退 (400→350, 500→480) を含むグローバル累積値の列。
                // 旧実装 (リセット検出 + 累積加算) は 400 + 500 + 900 = 1800 → clamp 1000 (100%)
                // と過剰計上していた。正しくは単調最大の 900 (90%)。
                foreach (var value in new long[] { 100, 400, 350, 500, 480, 900 })
                {
                    Marshal.WriteInt64(p, value);
                    _ = cb.SetCompleted(p);
                }
                Assert.That(cb.Bytes, Is.EqualTo(900L));
            }
            finally { Marshal.FreeHGlobal(p); }
        }
        finally { File.Delete(src); }
    }

    /* --------------------------------------------------------------------- */
    ///
    /// SetCompleted_ExceedsTotal_ClampsToTotalBytes
    ///
    /// <summary>
    /// completeValue が TotalBytes を超えた場合（complexity 尺度のヘッダ定数等）に
    /// Bytes が TotalBytes でクランプされることを確認します。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void SetCompleted_ExceedsTotal_ClampsToTotalBytes()
    {
        var src = Path.Combine(Path.GetTempPath(), $"UpdateCallbackProgressTest_{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(src, new byte[1000]);
        try
        {
            var items = new List<RawEntity> { new(src, "a.bin") };
            using var cb = new UpdateCallback(items, new SilentProgress())
            {
                Destination = string.Empty,
                Password    = string.Empty,
            };

            var p = Marshal.AllocHGlobal(sizeof(long));
            try
            {
                Marshal.WriteInt64(p, 1080L); // ヘッダ定数込みで僅かに超過
                _ = cb.SetCompleted(p);
                Assert.That(cb.Bytes, Is.EqualTo(1000L));
            }
            finally { Marshal.FreeHGlobal(p); }
        }
        finally { File.Delete(src); }
    }

    /* --------------------------------------------------------------------- */
    ///
    /// SetCompleted_ConcurrentCalls_RemainsMonotonicMax
    ///
    /// <summary>
    /// SetCompleted の並行呼び出し（ZIP Ultra 等のマルチスレッド圧縮を模擬）でも
    /// Bytes が後退せず、最終的に単調最大値へ収束することを確認します。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void SetCompleted_ConcurrentCalls_RemainsMonotonicMax()
    {
        var src = Path.Combine(Path.GetTempPath(), $"UpdateCallbackProgressTest_{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(src, new byte[1000]);
        try
        {
            var items = new List<RawEntity> { new(src, "a.bin") };
            using var cb = new UpdateCallback(items, new SilentProgress())
            {
                Destination = string.Empty,
                Password    = string.Empty,
            };

            // 後退を含む値列を複数スレッドから同時投入する。バッファはスレッドごとに
            // 確保し、テスト自体の race を排除する。
            Parallel.ForEach(new long[] { 100, 700, 650, 900, 850, 300, 880, 200 }, value =>
            {
                var p = Marshal.AllocHGlobal(sizeof(long));
                try
                {
                    Marshal.WriteInt64(p, value);
                    _ = cb.SetCompleted(p);
                }
                finally { Marshal.FreeHGlobal(p); }
            });

            Assert.That(cb.Bytes, Is.EqualTo(900L));
        }
        finally { File.Delete(src); }
    }

    [Test]
    public void SetOperationResult_ConcurrentStreamsKeepsOwnIndex()
    {
        var first = Path.Combine(Path.GetTempPath(), $"UpdateCallbackProgressTest_{Guid.NewGuid():N}_a.bin");
        var second = Path.Combine(Path.GetTempPath(), $"UpdateCallbackProgressTest_{Guid.NewGuid():N}_b.bin");
        File.WriteAllBytes(first, [1]);
        File.WriteAllBytes(second, [2]);
        try
        {
            var items = new List<RawEntity> { new(first, "a.bin"), new(second, "b.bin") };
            var finished = new string[2];
            using var cb = new UpdateCallback(items, new SilentProgress())
            {
                OnFileFinished = e => finished[e.Index] = ((RawEntity)e.Target).RelativeName,
            };
            using var barrier = new Barrier(2);

            var tasks = new Task[2];
            for (var i = 0; i < tasks.Length; i++)
            {
                var index = i;
                tasks[i] = Task.Run(() =>
                {
                    Assert.That(cb.GetStream((uint)index, out _), Is.Zero);
                    barrier.SignalAndWait();
                    Assert.That(cb.SetOperationResult(SevenZipCode.Success), Is.Zero);
                });
            }
            Task.WaitAll(tasks);

            Assert.That(finished, Is.EqualTo(new[] { "a.bin", "b.bin" }));
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
        }
    }

    /* --------------------------------------------------------------------- */
    ///
    /// SilentProgress
    ///
    /// <summary>
    /// 進捗報告を破棄する IProgress 実装。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    private sealed class SilentProgress : IProgress<Report>
    {
        public void Report(Report value) { }
    }
}
