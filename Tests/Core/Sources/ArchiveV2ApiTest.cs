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
using Cube.Tests.Fixtures;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace Cube.FileSystem.SevenZip.Tests;

/* ------------------------------------------------------------------------- */
///
/// ArchiveV2ApiTest
///
/// <summary>
/// v2 追加機能の回帰テスト:
/// - 要望 1: Update(Stream, Stream, renameMap, ...)
/// - 要望 2: VolumeSize によるボリューム分割
/// - 要望 3: AsyncPasswordQuery
/// - 要望 4: ArchiveEntity.IsUnicodeText / RawName
/// - 要望 5: ZipArchiveEntity
/// </summary>
///
/* ------------------------------------------------------------------------- */
[TestFixture]
internal class ArchiveV2ApiTest : FileFixture
{
    /* --------------------------------------------------------------------- */
    ///
    /// RenameMap_BasicRename
    ///
    /// <summary>
    /// Update(Stream, Stream, renameMap) で保持エントリのパスだけを差し替え、
    /// 他のエントリは変更されないこと。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void RenameMap_BasicRename()
    {
        // まず Sample.txt だけを含む ZIP をメモリ上に構築する
        var srcTxt = GetSource("Sample.txt");
        using var original = new MemoryStream();
        using (var w = new ArchiveWriter(Format.Zip))
        {
            w.Add(srcTxt, "a.txt");
            w.Save(original);
        }

        // 元 ZIP のエントリ数・index を取得
        original.Position = 0;
        int index;
        using (var r = new ArchiveReader(new MemoryStream(original.ToArray())))
        {
            Assert.That(r.Items.Count, Is.EqualTo(1));
            Assert.That(r.Items[0].FullName, Is.EqualTo("a.txt"));
            index = r.Items[0].Index;
        }

        // rename: index 0 を "renamed/new.txt" に変更
        using var updated = new MemoryStream();
        using (var w = new ArchiveWriter(Format.Zip))
        {
            original.Position = 0;
            w.Update(original, updated,
                renameMap: new Dictionary<int, string> { { index, "renamed/new.txt" } });
        }

        // 更新後のエントリ名を確認
        updated.Position = 0;
        using var r2 = new ArchiveReader(updated);
        Assert.That(r2.Items.Count, Is.EqualTo(1));
        var full = r2.Items[0].FullName.Replace('/', '\\');
        Assert.That(full, Is.EqualTo("renamed\\new.txt"));
    }

    /* --------------------------------------------------------------------- */
    ///
    /// RenameMap_NullValueDeletes
    ///
    /// <summary>
    /// renameMap で値を null/空文字にしたエントリは削除扱い。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void RenameMap_NullValueDeletes()
    {
        var srcTxt = GetSource("Sample.txt");
        using var original = new MemoryStream();
        using (var w = new ArchiveWriter(Format.Zip))
        {
            w.Add(srcTxt, "keep.txt");
            using var m1 = new MemoryStream(Encoding.UTF8.GetBytes("delete me"));
            w.Add(m1, "delete.txt");
            w.Save(original);
        }

        int deleteIndex;
        original.Position = 0;
        using (var r = new ArchiveReader(new MemoryStream(original.ToArray())))
        {
            deleteIndex = r.Items.First(e => e.FullName == "delete.txt").Index;
        }

        using var updated = new MemoryStream();
        using (var w = new ArchiveWriter(Format.Zip))
        {
            original.Position = 0;
            w.Update(original, updated,
                renameMap: new Dictionary<int, string> { { deleteIndex, null } });
        }

        updated.Position = 0;
        using var r2 = new ArchiveReader(updated);
        Assert.That(r2.Items.Count, Is.EqualTo(1));
        Assert.That(r2.Items[0].FullName, Is.EqualTo("keep.txt"));
    }

    /* --------------------------------------------------------------------- */
    ///
    /// AsyncPasswordQuery_ReturnsPassword
    ///
    /// <summary>
    /// AsyncPasswordQuery がハンドラから返した文字列を QueryMessage.Value にセットすること。
    /// ハンドラが空文字を返した場合は Cancel=true にすること。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void AsyncPasswordQuery_ReturnsPassword()
    {
        var q = new AsyncPasswordQuery(async _ => { await Task.Yield(); return "secret"; });
        var msg = new QueryMessage<string, string>("test");
        q.Request(msg);
        Assert.That(msg.Value, Is.EqualTo("secret"));
        Assert.That(msg.Cancel, Is.False);
    }

    [Test]
    public void AsyncPasswordQuery_EmptyStringCancels()
    {
        var q = new AsyncPasswordQuery(async _ => { await Task.Yield(); return string.Empty; });
        var msg = new QueryMessage<string, string>("test");
        q.Request(msg);
        Assert.That(msg.Cancel, Is.True);
        Assert.That(msg.Value, Is.Null);
    }

    [Test]
    public void AsyncPasswordQuery_CancellationTokenCancels()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var q = new AsyncPasswordQuery(
            async ct => { await Task.Delay(5000, ct); return "unreached"; },
            cts.Token);
        var msg = new QueryMessage<string, string>("test");
        q.Request(msg);
        Assert.That(msg.Cancel, Is.True);
    }

    /* --------------------------------------------------------------------- */
    ///
    /// ArchiveEntity_IsUnicodeText_SimpleAscii
    ///
    /// <summary>
    /// 通常の ASCII エントリ名を持つ ZIP は IsUnicodeText=true となる。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void ArchiveEntity_IsUnicodeText_SimpleAscii()
    {
        var src = GetSource("Sample.zip");
        using var r = new ArchiveReader(src);
        foreach (var e in r.Items)
        {
            Assert.That(e.IsUnicodeText, Is.True,
                $"Expected IsUnicodeText=true for ASCII entry: {e.FullName}");
            Assert.That(e.RawName, Is.Not.Null);
        }
    }

    /* --------------------------------------------------------------------- */
    ///
    /// ZipArchiveEntity_CastAndProperties
    ///
    /// <summary>
    /// Format.Zip で開いた ArchiveReader の Items は ZipArchiveEntity インスタンス。
    /// Method / PackedSize プロパティが取得できる。
    /// 7z.dll 非対応フィールドは null。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void ZipArchiveEntity_CastAndProperties()
    {
        var src = GetSource("Sample.zip");
        using var r = new ArchiveReader(src);

        Assert.That(r.Format, Is.EqualTo(Format.Zip));
        var file = r.Items.First(e => !e.IsDirectory);
        Assert.That(file, Is.InstanceOf<ZipArchiveEntity>(),
            "Format.Zip では Items は ZipArchiveEntity を返す");

        var zip = (ZipArchiveEntity)file;
        Assert.That(zip.Method, Is.Not.Null.And.Not.Empty,
            "Method (圧縮方式) は取得できる");
        Assert.That(zip.PackedSize, Is.GreaterThanOrEqualTo(0),
            "PackedSize は非負値");

        // 7z.dll 非対応フィールドは null (Obsolete 警告は明示的に抑制してアクセス)
#pragma warning disable CS0618
        Assert.That(zip.GeneralPurposeBitFlag, Is.Null);
        Assert.That(zip.ExtraField, Is.Null);
        Assert.That(zip.MadeByVersion, Is.Null);
        Assert.That(zip.VersionNeeded, Is.Null);
#pragma warning restore CS0618
    }

    /* --------------------------------------------------------------------- */
    ///
    /// ZipArchiveEntity_NotCreatedForNonZip
    ///
    /// <summary>
    /// Format.SevenZip では通常の ArchiveEntity が返り、ZipArchiveEntity ではない。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void ZipArchiveEntity_NotCreatedForNonZip()
    {
        // 7z で圧縮して確認する
        var srcTxt = GetSource("Sample.txt");
        var dest = Get(nameof(ZipArchiveEntity_NotCreatedForNonZip), "out.7z");

        using (var w = new ArchiveWriter(Format.SevenZip))
        {
            w.Add(srcTxt);
            w.Save(dest);
        }

        using var r = new ArchiveReader(dest);
        Assert.That(r.Format, Is.EqualTo(Format.SevenZip));
        foreach (var e in r.Items)
        {
            Assert.That(e, Is.Not.InstanceOf<ZipArchiveEntity>(),
                "非 ZIP フォーマットでは ZipArchiveEntity を返さない");
        }
    }

    /* --------------------------------------------------------------------- */
    ///
    /// VolumeSize_SevenZip_SplitsIntoParts
    ///
    /// <summary>
    /// VolumeSize を小さい値に設定して 7z 保存すると、dest.001/.002... が生成される。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void VolumeSize_SevenZip_SplitsIntoParts()
    {
        // 十分に大きいデータを作って分割されるようにする (1MB のランダム風データ × 3 エントリ)
        var workDir = Get(nameof(VolumeSize_SevenZip_SplitsIntoParts), "src");
        Io.CreateDirectory(workDir);
        var rnd = new System.Random(42);
        for (var i = 0; i < 3; i++)
        {
            var buf = new byte[1024 * 1024];
            rnd.NextBytes(buf);
            File.WriteAllBytes(Io.Combine(workDir, $"chunk{i}.bin"), buf);
        }

        var dest = Get(nameof(VolumeSize_SevenZip_SplitsIntoParts), "out.7z");

        // VolumeSize = 512KB で分割
        var opt = new CompressionOption
        {
            CompressionLevel = CompressionLevel.Fast,
            VolumeSize       = 512 * 1024,
        };
        using (var w = new ArchiveWriter(Format.SevenZip, opt))
        {
            w.Add(workDir, "src");
            w.Save(dest);
        }

        // dest.001 が生成されていること (少なくとも 1 つ以上のボリューム)
        var part1 = $"{dest}.001";
        Assert.That(File.Exists(part1), Is.True,
            $"第一ボリューム {part1} が生成されていない");

        // 総ボリューム数 >= 2 (3MB / 512KB = 最低 6 volume 相当)
        var parts = new List<string>();
        for (var i = 1; i <= 20; i++)
        {
            var p = $"{dest}.{i:D3}";
            if (!File.Exists(p)) break;
            parts.Add(p);
        }
        Assert.That(parts.Count, Is.GreaterThanOrEqualTo(2),
            $"VolumeSize=512KB で 3MB データを圧縮したら 2 ボリューム以上生成されるはず (実際: {parts.Count})");

        // 各ボリュームが VolumeSize 以下であること (最後のボリュームを除く)
        for (var i = 0; i < parts.Count - 1; i++)
        {
            var size = new FileInfo(parts[i]).Length;
            Assert.That(size, Is.LessThanOrEqualTo(512 * 1024),
                $"ボリューム {parts[i]} が VolumeSize を超過: {size}");
        }

        // ラウンドトリップ検証: 全ボリュームを結合 → 再展開して元の内容と一致することを確認
        var joined = Get(nameof(VolumeSize_SevenZip_SplitsIntoParts), "joined.7z");
        using (var outStream = File.Create(joined))
        {
            foreach (var p in parts)
            {
                using var inStream = File.OpenRead(p);
                inStream.CopyTo(outStream);
            }
        }

        var extractDir = Get(nameof(VolumeSize_SevenZip_SplitsIntoParts), "extracted");
        using (var r = new ArchiveReader(joined))
        {
            r.Save(extractDir);
        }

        // 元の 3 ファイルが全て同じバイト列で復元されていること
        for (var i = 0; i < 3; i++)
        {
            var original  = Io.Combine(workDir, $"chunk{i}.bin");
            var restored  = Io.Combine(extractDir, "src", $"chunk{i}.bin");
            Assert.That(File.Exists(restored), Is.True,
                $"再展開後に {restored} が見つからない");
            Assert.That(new FileInfo(restored).Length, Is.EqualTo(new FileInfo(original).Length),
                $"{restored} のサイズが元と異なる");
            // バイト一致検証 (1MB × 3 = 軽量なので SHA でなく直接比較)
            var originalBytes = File.ReadAllBytes(original);
            var restoredBytes = File.ReadAllBytes(restored);
            Assert.That(restoredBytes, Is.EqualTo(originalBytes),
                $"{restored} の内容が元と異なる");
        }
    }

    /* --------------------------------------------------------------------- */
    ///
    /// VolumeSize_Zero_NoSplit
    ///
    /// <summary>
    /// VolumeSize = 0 (デフォルト) の場合は従来通り単一ファイル出力。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void VolumeSize_Zero_NoSplit()
    {
        var srcTxt = GetSource("Sample.txt");
        var dest = Get(nameof(VolumeSize_Zero_NoSplit), "out.7z");

        using (var w = new ArchiveWriter(Format.SevenZip))
        {
            w.Add(srcTxt);
            w.Save(dest);
        }

        Assert.That(File.Exists(dest), Is.True, "単一ファイルが生成される");
        Assert.That(File.Exists($"{dest}.001"), Is.False, "分割ファイルは生成されない");
    }

    /* --------------------------------------------------------------------- */
    ///
    /// FlushToDisk_PathBasedSave_Completes
    ///
    /// <summary>
    /// FlushToDisk=true を指定した path ベース Save が正常完了し、
    /// 生成されたアーカイブが読み取れることを確認する。
    /// (fsync 実行時の副作用で壊れない回帰テスト)
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void FlushToDisk_PathBasedSave_Completes()
    {
        var srcTxt = GetSource("Sample.txt");
        var dest = Get(nameof(FlushToDisk_PathBasedSave_Completes), "out.zip");

        var opt = new CompressionOption { FlushToDisk = true };
        using (var w = new ArchiveWriter(Format.Zip, opt))
        {
            w.Add(srcTxt);
            w.Save(dest);
        }

        Assert.That(File.Exists(dest), Is.True);
        using var r = new ArchiveReader(dest);
        Assert.That(r.Items.Count, Is.GreaterThan(0), "FlushToDisk 後のアーカイブが読める");
    }

    /* --------------------------------------------------------------------- */
    ///
    /// AtomicSave_NewFile_Completes
    ///
    /// <summary>
    /// AtomicSave=true で新規ファイルを保存した場合、tmp → dest のリネームが
    /// 正しく行われ、dest が有効なアーカイブになる。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void AtomicSave_NewFile_Completes()
    {
        var srcTxt = GetSource("Sample.txt");
        var destDir = Get(nameof(AtomicSave_NewFile_Completes));
        Io.CreateDirectory(destDir);
        var dest = Path.Combine(destDir, "out.zip");

        var opt = new CompressionOption
        {
            AtomicSave  = true,
            FlushToDisk = true,
        };
        using (var w = new ArchiveWriter(Format.Zip, opt))
        {
            w.Add(srcTxt);
            w.Save(dest);
        }

        Assert.That(File.Exists(dest), Is.True, "dest が作成されている");
        // .tmp / .bak 残骸が無いこと
        var leftovers = Directory.GetFiles(destDir, "*.tmp").Length
                      + Directory.GetFiles(destDir, "*.bak").Length;
        Assert.That(leftovers, Is.EqualTo(0), "tmp/bak 残骸が残っていない");

        using var r = new ArchiveReader(dest);
        Assert.That(r.Items.Count, Is.GreaterThan(0));
    }

    /* --------------------------------------------------------------------- */
    ///
    /// AtomicSave_Overwrite_KeepsBackup
    ///
    /// <summary>
    /// AtomicSave=true + KeepBackupOnUpdate=true で既存 dest を上書きした場合、
    /// .bak ファイルが残り LastBackupPath でパスが公開される。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void AtomicSave_Overwrite_KeepsBackup()
    {
        var srcTxt = GetSource("Sample.txt");
        var destDir = Get(nameof(AtomicSave_Overwrite_KeepsBackup));
        Io.CreateDirectory(destDir);
        var dest = Path.Combine(destDir, "out.zip");

        // Step 1: 初回保存 (上書き元の dest 準備)
        using (var w1 = new ArchiveWriter(Format.Zip))
        {
            w1.Add(srcTxt);
            w1.Save(dest);
        }
        Assert.That(File.Exists(dest), Is.True);
        var initialSize = new FileInfo(dest).Length;

        // Step 2: AtomicSave + KeepBackupOnUpdate で上書き
        var srcBin = GetSource("Sample.tar");  // 別ファイルで上書き
        var opt = new CompressionOption
        {
            AtomicSave          = true,
            KeepBackupOnUpdate  = true,
            FlushToDisk         = true,
        };
        string backupPath;
        using (var w2 = new ArchiveWriter(Format.Zip, opt))
        {
            w2.Add(srcBin);
            w2.Save(dest);
            backupPath = w2.LastBackupPath;
        }

        // LastBackupPath が設定され、.bak ファイルが実在すること
        Assert.That(backupPath, Is.Not.Null, "LastBackupPath が設定されている");
        Assert.That(File.Exists(backupPath), Is.True, $".bak ファイル {backupPath} が実在する");
        Assert.That(new FileInfo(backupPath).Length, Is.EqualTo(initialSize),
            ".bak は元の dest のサイズと一致する (中身が保存されている)");

        // dest は新しい内容に上書きされていること
        using var r = new ArchiveReader(dest);
        Assert.That(r.Items.Any(e => e.FullName.Contains("Sample.tar")), Is.True,
            "dest は新しい内容で上書きされている");
    }

    /* --------------------------------------------------------------------- */
    ///
    /// AtomicSave_Overwrite_DeletesBackupByDefault
    ///
    /// <summary>
    /// AtomicSave=true かつ KeepBackupOnUpdate=false (デフォルト) で上書きした場合、
    /// .bak は自動削除されて LastBackupPath は null のままになる。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void AtomicSave_Overwrite_DeletesBackupByDefault()
    {
        var srcTxt = GetSource("Sample.txt");
        var destDir = Get(nameof(AtomicSave_Overwrite_DeletesBackupByDefault));
        Io.CreateDirectory(destDir);
        var dest = Path.Combine(destDir, "out.zip");

        using (var w1 = new ArchiveWriter(Format.Zip))
        {
            w1.Add(srcTxt);
            w1.Save(dest);
        }

        var opt = new CompressionOption { AtomicSave = true };  // KeepBackupOnUpdate=false がデフォルト
        using (var w2 = new ArchiveWriter(Format.Zip, opt))
        {
            w2.Add(srcTxt);
            w2.Save(dest);
            Assert.That(w2.LastBackupPath, Is.Null, "デフォルトでは LastBackupPath は null");
        }

        var leftovers = Directory.GetFiles(destDir, "*.bak").Length;
        Assert.That(leftovers, Is.EqualTo(0), ".bak は自動削除される");
    }

    /* --------------------------------------------------------------------- */
    ///
    /// Update_SelfUpdate_KeepBackup
    ///
    /// <summary>
    /// Update(string src, string dest) で src==dest の自己更新時に
    /// KeepBackupOnUpdate=true を指定すると .bak が保持される。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void Update_SelfUpdate_KeepBackup()
    {
        var srcTxt = GetSource("Sample.txt");
        var destDir = Get(nameof(Update_SelfUpdate_KeepBackup));
        Io.CreateDirectory(destDir);
        var archive = Path.Combine(destDir, "test.zip");

        // 初回作成
        using (var w = new ArchiveWriter(Format.Zip))
        {
            w.Add(srcTxt);
            w.Save(archive);
        }

        // 自己更新 (KeepBackupOnUpdate=true)
        var opt = new CompressionOption { KeepBackupOnUpdate = true };
        string backupPath;
        using (var w = new ArchiveWriter(Format.Zip, opt))
        {
            w.Remove("Sample.txt");
            w.Add(GetSource("Sample.tar"));
            w.Update(archive, archive);
            backupPath = w.LastBackupPath;
        }

        Assert.That(backupPath, Is.Not.Null, "LastBackupPath が設定されている");
        Assert.That(File.Exists(backupPath), Is.True, ".bak が実在する");

        // dest は更新後の内容
        using var r = new ArchiveReader(archive);
        Assert.That(r.Items.Any(e => e.FullName.Contains("Sample.tar")), Is.True);
    }
}
