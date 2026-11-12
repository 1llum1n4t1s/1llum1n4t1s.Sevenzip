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
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
namespace Cube.FileSystem.SevenZip.Tests;

/* ------------------------------------------------------------------------- */
///
/// WriterRegressionTest
///
/// <summary>
/// ArchiveWriter / UpdatePlan / UpdateCallback のレビュー指摘 (W1〜W11) に対する回帰テスト。
/// </summary>
///
/// <remarks>
/// I/O を差し替えるテストは Io.Configure で IoController を入れ替える。
/// 復帰は基底 SourceFileFixture.Teardown の Io.Configure(new()) に任せる。
/// </remarks>
///
/* ------------------------------------------------------------------------- */
[TestFixture]
internal class WriterRegressionTest : FileFixture
{
    #region W1: Stream 系 API もオプション検証を通す

    /* --------------------------------------------------------------------- */
    ///
    /// SaveStream_TarWithPassword_Throws
    ///
    /// <summary>
    /// Save(Stream) でも Tar + Password を拒否する (以前は平文 tar を黙って出力していた)。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void SaveStream_TarWithPassword_Throws()
    {
        using var output = new MemoryStream();
        using var writer = new ArchiveWriter(Format.Tar, new CompressionOption { Password = "password" });
        writer.Add(GetSource("Sample.txt"), "sample.txt");

        Assert.That(() => writer.Save(output), Throws.TypeOf<InvalidOperationException>());
        Assert.That(output.Length, Is.EqualTo(0), "検証失敗時は何も書き込まない");
    }

    /* --------------------------------------------------------------------- */
    ///
    /// StreamApis_NegativeThreadCount_Throws
    ///
    /// <summary>
    /// Save(Stream) / Update(Stream) でも負の ThreadCount を拒否する
    /// (以前は (uint) キャストで巨大なスレッド数として 7-Zip に渡っていた)。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void StreamApis_NegativeThreadCount_Throws()
    {
        using var writer = new ArchiveWriter(Format.Zip, new CompressionOption { ThreadCount = -1 });
        using var output = new MemoryStream();
        using var source = new MemoryStream(File.ReadAllBytes(GetSource("Sample.zip")));

        Assert.That(() => writer.Save(output), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => writer.Update(source, output), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    /* --------------------------------------------------------------------- */
    ///
    /// UpdateStream_UnsupportedOptions_Throw
    ///
    /// <summary>
    /// Update(Stream) は VolumeSize / AtomicSave を黙って無視せず NotSupportedException にする
    /// (Save(Stream) と同じ扱い)。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void UpdateStream_UnsupportedOptions_Throw()
    {
        var bytes = File.ReadAllBytes(GetSource("Sample.zip"));
        using var volume = new ArchiveWriter(Format.Zip, new CompressionOption { VolumeSize = 1024 });
        using var atomic = new ArchiveWriter(Format.Zip, new CompressionOption { AtomicSave = true });

        using (var src = new MemoryStream(bytes))
        using (var dst = new MemoryStream())
        {
            Assert.That(() => volume.Update(src, dst), Throws.TypeOf<NotSupportedException>());
            Assert.That(dst.Length, Is.EqualTo(0));
        }

        using (var src = new MemoryStream(bytes))
        using (var dst = new MemoryStream())
        {
            Assert.That(() => atomic.Update(src, dst), Throws.TypeOf<NotSupportedException>());
            Assert.That(dst.Length, Is.EqualTo(0));
        }
    }

    #endregion

    #region W2 / W3: Stream の所有権と FlushToDisk の順序

    /* --------------------------------------------------------------------- */
    ///
    /// SaveStream_FileStreamLeaveOpenFalseWithFlush_Succeeds
    ///
    /// <summary>
    /// FileStream + leaveOpen=false + FlushToDisk=true でも、Dispose 前に flush して成功し、
    /// 完了後に dest が閉じられていること (以前は ObjectDisposedException)。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [TestCase(Format.Zip)]
    [TestCase(Format.Tar)]
    public void SaveStream_FileStreamLeaveOpenFalseWithFlush_Succeeds(Format format)
    {
        var path = Get(nameof(SaveStream_FileStreamLeaveOpenFalseWithFlush_Succeeds), $"out-{format}.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var stream = File.Create(path);

        using (var writer = new ArchiveWriter(format, new CompressionOption { FlushToDisk = true }))
        {
            writer.Add(GetSource("Sample.txt"), "sample.txt");
            Assert.DoesNotThrow(() => writer.Save(stream, null, leaveOpen: false));
        }

        Assert.That(stream.CanWrite, Is.False, "leaveOpen=false なら dest は Dispose される");
        using var reader = new ArchiveReader(path);
        Assert.That(reader.Items.Select(e => e.FullName), Does.Contain("sample.txt"));
    }

    /* --------------------------------------------------------------------- */
    ///
    /// UpdateStream_FileStreamLeaveDestOpenFalseWithFlush_Succeeds
    ///
    /// <summary>
    /// Update(Stream) でも FileStream + leaveDestOpen=false + FlushToDisk=true が成功し、
    /// 指定どおり dest だけが閉じられること。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void UpdateStream_FileStreamLeaveDestOpenFalseWithFlush_Succeeds()
    {
        var path = Get(nameof(UpdateStream_FileStreamLeaveDestOpenFalseWithFlush_Succeeds), "out.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var source = new MemoryStream(File.ReadAllBytes(GetSource("Sample.zip")));
        var dest = File.Create(path);

        using (var writer = new ArchiveWriter(Format.Zip, new CompressionOption { FlushToDisk = true }))
        {
            Assert.DoesNotThrow(() => writer.Update(source, dest, leaveSourceOpen: true, leaveDestOpen: false));
        }

        Assert.That(dest.CanWrite, Is.False, "leaveDestOpen=false なら dest は Dispose される");
        Assert.That(source.CanRead, Is.True, "leaveSourceOpen=true なら source は開いたまま");
        using var reader = new ArchiveReader(path);
        Assert.That(reader.Items.Count, Is.GreaterThan(0));
    }

    /* --------------------------------------------------------------------- */
    ///
    /// SaveStream_TarLeaveOpenFalse_DisposesDestination
    ///
    /// <summary>
    /// Tar の Save(Stream) でも leaveOpen=false を守り dest を Dispose する。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void SaveStream_TarLeaveOpenFalse_DisposesDestination()
    {
        var output = new MemoryStream();
        using (var writer = new ArchiveWriter(Format.Tar))
        {
            writer.Add(GetSource("Sample.txt"), "sample.txt");
            writer.Save(output, null, leaveOpen: false);
        }
        Assert.That(output.CanRead, Is.False);
    }

    /* --------------------------------------------------------------------- */
    ///
    /// UpdateStream_SameStream_HonorsLeaveOpenFlags
    ///
    /// <summary>
    /// source と dest が同一 Stream の場合も leaveSourceOpen / leaveDestOpen を無視しない。
    /// どちらかが false なら 1 回だけ Dispose し、両方 true なら開いたまま。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [TestCase(true, true, true)]
    [TestCase(false, true, false)]
    [TestCase(true, false, false)]
    [TestCase(false, false, false)]
    public void UpdateStream_SameStream_HonorsLeaveOpenFlags(bool leaveSource, bool leaveDest, bool expectOpen)
    {
        var stream = new MemoryStream();
        stream.Write(File.ReadAllBytes(GetSource("Sample.zip")));
        stream.Position = 0;

        using (var writer = new ArchiveWriter(Format.Zip))
        {
            writer.Update(stream, stream, leaveSourceOpen: leaveSource, leaveDestOpen: leaveDest);
        }
        Assert.That(stream.CanRead, Is.EqualTo(expectOpen));
    }

    #endregion

    #region W7: LastTempPath / LastBackupPath は今回の操作の値

    /* --------------------------------------------------------------------- */
    ///
    /// AllEntries_ResetStaleLastBackupPath
    ///
    /// <summary>
    /// 前回操作で LastBackupPath が設定されていても、.bak を作らない次の操作
    /// (パス Save / Stream Save / Stream Update) の後には null になる。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [TestCase("SavePath")]
    [TestCase("SaveStream")]
    [TestCase("UpdateStream")]
    public void AllEntries_ResetStaleLastBackupPath(string operation)
    {
        var dir  = Get(nameof(AllEntries_ResetStaleLastBackupPath), operation);
        var dest = Path.Combine(dir, "self.zip");
        Directory.CreateDirectory(dir);
        File.Copy(GetSource("Sample.zip"), dest);

        using var writer = new ArchiveWriter(Format.Zip, new CompressionOption { KeepBackupOnUpdate = true });
        writer.Add(GetSource("Sample.txt"), "sample.txt");

        // 自己更新 + KeepBackupOnUpdate で .bak を残し、LastBackupPath を設定させる
        writer.Update(dest, dest);
        Assert.That(writer.LastBackupPath, Is.Not.Null, "前提: .bak が記録されている");

        RunOperation(writer, operation, Path.Combine(dir, "next.zip"));
        Assert.That(writer.LastBackupPath, Is.Null);
        Assert.That(writer.LastTempPath, Is.Null);
    }

    /* --------------------------------------------------------------------- */
    ///
    /// AllEntries_ResetStaleLastTempPath
    ///
    /// <summary>
    /// 前回操作の失敗で LastTempPath が残っていても、次の操作 (非 atomic の Save や
    /// Stream 系 API) の後には null になる。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [TestCase("SavePath")]
    [TestCase("SaveStream")]
    [TestCase("UpdateStream")]
    public void AllEntries_ResetStaleLastTempPath(string operation)
    {
        var dir  = Get(nameof(AllEntries_ResetStaleLastTempPath), operation);
        var dest = Path.Combine(dir, "self.zip");
        Directory.CreateDirectory(dir);
        File.Copy(GetSource("Sample.zip"), dest);

        using var writer = new ArchiveWriter(Format.Zip);
        writer.Add(GetSource("Sample.txt"), "sample.txt");

        // 自己更新の tmp → dest の rename と、その後の tmp 削除を失敗させて LastTempPath を残す
        Io.Configure(new FailingTempController(dest));
        Assert.That(() => writer.Update(dest, dest), Throws.InstanceOf<IOException>());
        Assert.That(writer.LastTempPath, Is.Not.Null, "前提: 削除できなかった tmp が記録されている");
        Io.Configure(new());

        RunOperation(writer, operation, Path.Combine(dir, "next.zip"));
        Assert.That(writer.LastTempPath, Is.Null);
    }

    #endregion

    #region W4: 列挙失敗も Add の例外境界で扱う

    /* --------------------------------------------------------------------- */
    ///
    /// Add_EnumerationDenied_SkipsWhenRequested
    ///
    /// <summary>
    /// ディレクトリの列挙がアクセス拒否で失敗しても、SkipInaccessibleFiles=true なら
    /// FileSkipped で通知してそのディレクトリだけを除外する。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void Add_EnumerationDenied_SkipsWhenRequested()
    {
        var root   = Get(nameof(Add_EnumerationDenied_SkipsWhenRequested), "src");
        var denied = Path.Combine(root, "denied");
        Directory.CreateDirectory(denied);
        File.WriteAllText(Path.Combine(root, "ok.txt"), "ok");
        File.WriteAllText(Path.Combine(denied, "secret.txt"), "secret");
        var dest = Get(nameof(Add_EnumerationDenied_SkipsWhenRequested), "out.zip");

        Io.Configure(new DenyEnumerationController(denied));
        var skipped = new List<FileSkippedEventArgs>();
        using (var writer = new ArchiveWriter(Format.Zip, new CompressionOption { SkipInaccessibleFiles = true }))
        {
            writer.FileSkipped += (_, e) => skipped.Add(e);
            Assert.DoesNotThrow(() => writer.Add(root));
            Io.Configure(new());
            writer.Save(dest);
        }

        Assert.That(skipped.Select(e => e.FullName), Is.EqualTo(new[] { denied }));
        Assert.That(skipped[0].Reason, Is.TypeOf<UnauthorizedAccessException>());
        using var reader = new ArchiveReader(dest);
        var names = reader.Items.Select(e => e.FullName).ToList();
        Assert.That(names, Does.Contain(@"src\ok.txt"));
        Assert.That(names, Has.None.Contains("denied"));
    }

    /* --------------------------------------------------------------------- */
    ///
    /// Add_EnumerationDenied_ThrowsAccessExceptionAndRollsBack
    ///
    /// <summary>
    /// SkipInaccessibleFiles=false なら列挙失敗を AccessException に包み、
    /// その Add で途中まで登録したアイテムを残さない。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void Add_EnumerationDenied_ThrowsAccessExceptionAndRollsBack()
    {
        var root   = Get(nameof(Add_EnumerationDenied_ThrowsAccessExceptionAndRollsBack), "src");
        var denied = Path.Combine(root, "z-denied");
        Directory.CreateDirectory(denied);
        File.WriteAllText(Path.Combine(root, "partial.txt"), "partial");
        Directory.CreateDirectory(Path.Combine(root, "a-sub"));
        File.WriteAllText(Path.Combine(root, "a-sub", "partial2.txt"), "partial2");
        var dest = Get(nameof(Add_EnumerationDenied_ThrowsAccessExceptionAndRollsBack), "out.zip");

        using var writer = new ArchiveWriter(Format.Zip);
        writer.Add(GetSource("Sample.txt"), "kept.txt");

        Io.Configure(new DenyEnumerationController(denied));
        var error = Assert.Throws<AccessException>(() => writer.Add(root));
        Assert.That(error!.InnerException, Is.TypeOf<UnauthorizedAccessException>());
        Io.Configure(new());

        writer.Save(dest);
        using var reader = new ArchiveReader(dest);
        Assert.That(reader.Items.Select(e => e.FullName), Is.EqualTo(new[] { "kept.txt" }),
            "失敗した Add の途中結果 (src, partial.txt 等) は残らない");
    }

    #endregion

    #region W5: rename と Add の照合・rename 衝突

    /* --------------------------------------------------------------------- */
    ///
    /// UpdateStream_RenameToAddedPath_AddWins
    ///
    /// <summary>
    /// renameMap{a:"x"} と Add(..., "x") を併用すると、Add のデータで置換された "x" が 1 つだけ出力される。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void UpdateStream_RenameToAddedPath_AddWins()
    {
        var dir = Get(nameof(UpdateStream_RenameToAddedPath_AddWins));
        var archive = CreateTwoEntryZip(dir);
        var added = Path.Combine(dir, "added.txt");
        File.WriteAllText(added, "ADDED");

        var output = UpdateWithRename(archive, map => map["a.txt"] = "x.txt",
            writer => writer.Add(added, "x.txt"));

        var entries = ReadAll(output);
        Assert.That(entries.Keys.Count(k => k.Equals("x.txt", StringComparison.OrdinalIgnoreCase)), Is.EqualTo(1));
        Assert.That(entries["x.txt"], Is.EqualTo("ADDED"));
        Assert.That(entries.ContainsKey("a.txt"), Is.False);
        Assert.That(entries["b.txt"], Is.EqualTo("B"));
    }

    /* --------------------------------------------------------------------- */
    ///
    /// UpdateStream_RenameCollidesWithKeptEntry_Throws
    ///
    /// <summary>
    /// rename 先が保持エントリと衝突する場合は ArgumentException で拒否し、出力しない。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [TestCase("b.txt")]
    [TestCase("B.TXT")]
    public void UpdateStream_RenameCollidesWithKeptEntry_Throws(string target)
    {
        // 大文字小文字だけ違う TestCase 同士でディレクトリが衝突しないよう名前を分ける
        var dir = Get(nameof(UpdateStream_RenameCollidesWithKeptEntry_Throws),
            target.Any(char.IsUpper) ? "upper" : "lower");
        var archive = CreateTwoEntryZip(dir);

        using var source = new MemoryStream(File.ReadAllBytes(archive));
        using var dest = new MemoryStream();
        using var writer = new ArchiveWriter(Format.Zip);
        var map = new Dictionary<int, string> { [IndexOf(archive, "a.txt")] = target };

        var error = Assert.Throws<ArgumentException>(() => writer.Update(source, dest, map));
        Assert.That(error!.ParamName, Is.EqualTo("renameMap"));
    }

    /* --------------------------------------------------------------------- */
    ///
    /// UpdateStream_RenameSwap_IsAllowed
    ///
    /// <summary>
    /// 互いの名前の入れ替え (a → b, b → a) は出力上重複しないため許可される。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void UpdateStream_RenameSwap_IsAllowed()
    {
        var dir = Get(nameof(UpdateStream_RenameSwap_IsAllowed));
        var archive = CreateTwoEntryZip(dir);

        var output = UpdateWithRename(archive, map =>
        {
            map["a.txt"] = "b.txt";
            map["b.txt"] = "a.txt";
        }, _ => { });

        var entries = ReadAll(output);
        Assert.That(entries.Count, Is.EqualTo(2));
        Assert.That(entries["a.txt"], Is.EqualTo("B"));
        Assert.That(entries["b.txt"], Is.EqualTo("A"));
    }

    #endregion

    #region W6: AtomicSave + Tar 圧縮の内包名

    /* --------------------------------------------------------------------- */
    ///
    /// AtomicSave_CompressedTar_UsesDestinationInnerName
    ///
    /// <summary>
    /// AtomicSave でも GZip 等で包む TAR の内包名は最終 dest から導出し、非 atomic と一致する
    /// (以前は tmp パス由来の "{GUID}.gz.tar" になっていた)。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [TestCase(CompressionMethod.GZip, "tar.gz")]
    [TestCase(CompressionMethod.BZip2, "tar.bz2")]
    [TestCase(CompressionMethod.XZ, "tar.xz")]
    public void AtomicSave_CompressedTar_UsesDestinationInnerName(CompressionMethod method, string ext)
    {
        var dir     = Get(nameof(AtomicSave_CompressedTar_UsesDestinationInnerName), method.ToString());
        var atomic  = Path.Combine(dir, $"atomic.{ext}");
        var control = Path.Combine(dir, $"control.{ext}");

        foreach (var (dest, isAtomic) in new[] { (atomic, true), (control, false) })
        {
            using var writer = new ArchiveWriter(Format.Tar,
                new CompressionOption { CompressionMethod = method, AtomicSave = isAtomic });
            writer.Add(GetSource("Sample.txt"), "sample.txt");
            writer.Save(dest);
        }

        // 非 atomic (control) の内包名規則 = dest のベース名 ("control.tar") と同じ規則であること
        Assert.That(InnerName(control), Is.EqualTo(Path.GetFileNameWithoutExtension(control)));
        Assert.That(InnerName(atomic), Is.EqualTo(Path.GetFileNameWithoutExtension(atomic)));
    }

    #endregion

    #region W9: rename エントリの属性・時刻保持

    /* --------------------------------------------------------------------- */
    ///
    /// UpdateStream_Rename_PreservesEntryProperties
    ///
    /// <summary>
    /// rename したファイル/ディレクトリエントリが、IsDirectory・最終更新日時・属性を
    /// 元エントリから引き継ぐ (以前は ZIP で時刻 0・IsDir=false、7z で時刻・属性が消えていた)。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [TestCase(Format.Zip)]
    [TestCase(Format.SevenZip)]
    public void UpdateStream_Rename_PreservesEntryProperties(Format format)
    {
        var dir  = Get(nameof(UpdateStream_Rename_PreservesEntryProperties), format.ToString());
        var src  = Path.Combine(dir, "src");
        var sub  = Path.Combine(src, "d");
        var file = Path.Combine(src, "f.txt");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "inner.txt"), "inner");
        File.WriteAllText(file, "file");
        // ZIP の DOS 時刻は 2 秒単位なので偶数秒にする
        File.SetLastWriteTime(file, new DateTime(2020, 1, 2, 3, 4, 6));
        File.SetAttributes(file, FileAttributes.Hidden | FileAttributes.Archive);
        Directory.SetLastWriteTime(sub, new DateTime(2019, 5, 6, 7, 8, 10));

        var archive = Path.Combine(dir, "source.bin");
        using (var writer = new ArchiveWriter(format))
        {
            writer.Add(sub, "d");
            writer.Add(file, "f.txt");
            writer.Save(archive);
        }

        // 期待値は元書庫が保持している値そのもの (ファイルシステムの値ではない)
        var original = Snapshot(archive);
        var map = new Dictionary<int, string>
        {
            [original["d"].Index]     = "d2",
            [original["f.txt"].Index] = "f2.txt",
        };

        using var source = new MemoryStream(File.ReadAllBytes(archive));
        using var dest = new MemoryStream();
        using (var writer = new ArchiveWriter(format)) writer.Update(source, dest, map);
        var renamedPath = Path.Combine(dir, "renamed.bin");
        File.WriteAllBytes(renamedPath, dest.ToArray());

        var renamed = Snapshot(renamedPath);
        AssertSameProperties(renamed["d2"], original["d"]);
        AssertSameProperties(renamed["f2.txt"], original["f.txt"]);
        Assert.That(renamed["d2"].IsDirectory, Is.True);
        Assert.That(renamed["f2.txt"].Attributes.HasFlag(FileAttributes.Hidden), Is.True);
    }

    #endregion

    #region W10: 移動成功後のメタデータ再設定失敗

    /* --------------------------------------------------------------------- */
    ///
    /// AtomicSave_MetadataFailureAfterRename_IsTreatedAsSuccess
    ///
    /// <summary>
    /// tmp → dest の rename 成功後に時刻再設定が失敗しても、移動は完了しているため
    /// 失敗扱い (ロールバック → ArchiveUpdateException) にせず、.bak も報告する。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void AtomicSave_MetadataFailureAfterRename_IsTreatedAsSuccess()
    {
        var dir  = Get(nameof(AtomicSave_MetadataFailureAfterRename_IsTreatedAsSuccess));
        var dest = Path.Combine(dir, "out.zip");
        Directory.CreateDirectory(dir);
        var options = new CompressionOption { AtomicSave = true, KeepBackupOnUpdate = true };

        using (var first = new ArchiveWriter(Format.Zip, options))
        {
            first.Add(GetSource("Sample.txt"), "old.txt");
            first.Save(dest);
        }

        using var writer = new ArchiveWriter(Format.Zip, options);
        writer.Add(GetSource("Sample.txt"), "new.txt");
        var full = Path.GetFullPath(dest);
        Io.Configure(new MetadataFailureController(p => Path.GetFullPath(p) == full));
        Assert.DoesNotThrow(() => writer.Save(dest));
        Io.Configure(new());

        Assert.That(writer.LastBackupPath, Is.Not.Null);
        Assert.That(File.Exists(writer.LastBackupPath), Is.True);
        Assert.That(writer.LastTempPath, Is.Null);
        using var reader = new ArchiveReader(dest);
        Assert.That(reader.Items.Select(e => e.FullName), Is.EqualTo(new[] { "new.txt" }));
    }

    /* --------------------------------------------------------------------- */
    ///
    /// Update_MetadataFailureWhileBackingUp_IsTreatedAsSuccess
    ///
    /// <summary>
    /// dest → .bak の退避 rename 成功後に時刻再設定が失敗しても、退避は完了しているため
    /// 処理を続行する (以前は例外で中断し、dest が消えたまま .bak も報告されなかった)。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void Update_MetadataFailureWhileBackingUp_IsTreatedAsSuccess()
    {
        var dir  = Get(nameof(Update_MetadataFailureWhileBackingUp_IsTreatedAsSuccess));
        var dest = Path.Combine(dir, "self.zip");
        Directory.CreateDirectory(dir);
        File.Copy(GetSource("Sample.zip"), dest);

        using var writer = new ArchiveWriter(Format.Zip, new CompressionOption { KeepBackupOnUpdate = true });
        writer.Add(GetSource("Sample.txt"), "added.txt");
        Io.Configure(new MetadataFailureController(p => p.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotThrow(() => writer.Update(dest, dest));
        Io.Configure(new());

        Assert.That(File.Exists(dest), Is.True);
        Assert.That(writer.LastBackupPath, Is.Not.Null);
        Assert.That(File.Exists(writer.LastBackupPath), Is.True);
        using var reader = new ArchiveReader(dest);
        Assert.That(reader.Items.Select(e => e.FullName), Does.Contain("added.txt"));
    }

    #endregion

    #region W11: 保存時オープンでのリンク追跡拒否

    /* --------------------------------------------------------------------- */
    ///
    /// Save_OpenedFileDiffersFromPath_Fails
    ///
    /// <summary>
    /// Add 後に、パスを開くと別のファイルへ到達する状態 (リンク追跡の模擬) になった場合、
    /// SkipInaccessibleFiles=true でもスキップせず保存失敗にする。
    /// </summary>
    ///
    /// <remarks>
    /// ファイルのシンボリックリンク作成には管理者権限が必要なため、IoController.Open で
    /// 別ファイルのストリームを返してリンク追跡を模擬する。
    /// </remarks>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void Save_OpenedFileDiffersFromPath_Fails()
    {
        var dir     = Get(nameof(Save_OpenedFileDiffersFromPath_Fails));
        var target  = Path.Combine(dir, "target.txt");
        var outside = Path.Combine(dir, "outside.txt");
        var dest    = Path.Combine(dir, "out.zip");
        Directory.CreateDirectory(dir);
        File.WriteAllText(target, "target");
        File.WriteAllText(outside, "outside-secret");

        var skipped = new List<FileSkippedEventArgs>();
        using var writer = new ArchiveWriter(Format.Zip, new CompressionOption { SkipInaccessibleFiles = true });
        writer.FileSkipped += (_, e) => skipped.Add(e);
        writer.Add(target, "target.txt");

        Io.Configure(new RedirectOpenController(target, outside));
        var error = Assert.Catch(() => writer.Save(dest));
        Io.Configure(new());

        Assert.That(Flatten(error), Has.Some.Matches<Exception>(e =>
            e is IOException && e.Message.StartsWith("Refusing to archive", StringComparison.Ordinal)));
        Assert.That(skipped, Is.Empty, "保存中の検出はスキップへ変換しない");
    }

    /* --------------------------------------------------------------------- */
    ///
    /// Save_FileReplacedByJunction_Fails
    ///
    /// <summary>
    /// Add 後にファイルが再解析ポイント (ジャンクション) へ差し替えられた場合、
    /// SkipInaccessibleFiles=true でも保存失敗にする。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void Save_FileReplacedByJunction_Fails()
    {
        var dir     = Get(nameof(Save_FileReplacedByJunction_Fails));
        var target  = Path.Combine(dir, "target.txt");
        var outside = Path.Combine(dir, "outside");
        var dest    = Path.Combine(dir, "out.zip");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "secret");
        File.WriteAllText(target, "target");

        var skipped = new List<FileSkippedEventArgs>();
        using var writer = new ArchiveWriter(Format.Zip, new CompressionOption { SkipInaccessibleFiles = true });
        writer.FileSkipped += (_, e) => skipped.Add(e);
        writer.Add(target, "target.txt");

        File.Delete(target);
        CreateJunction(target, outside);
        try
        {
            // ディレクトリ (ジャンクション) は通常オープン自体が失敗するため、拒否の経路は
            // OS の UnauthorizedAccessException の場合もある。いずれにせよ保存失敗であり、
            // スキップ扱いにも、リンク先の取り込みにもならないことを確認する。
            Assert.Catch(() => writer.Save(dest));
            Assert.That(skipped, Is.Empty);
        }
        finally
        {
            if (Directory.Exists(target)) Directory.Delete(target, false);
        }
        Assert.That(File.Exists(Path.Combine(outside, "secret.txt")), Is.True);
    }

    #endregion

    #region W12: 圧縮イベントとエントリの対応

    /* --------------------------------------------------------------------- */
    ///
    /// Save_SevenZipMultiThread_FileEventsMatchEntries
    ///
    /// <summary>
    /// 7z / LZMA2 のマルチスレッド圧縮で、FileCompressed が対応する FileCompressing と
    /// 同じエントリ (Index と Target の組) で発火し、各エントリ 1 回ずつ厳密に交互に並ぶこと。
    /// </summary>
    ///
    /// <remarks>
    /// <para>
    /// 7-Zip は GetStream / SetOperationResult を直列に呼ぶが、LZMA2 のブロック MT では
    /// SetOperationResult(k) が k+1 を開くスレッドから届く。旧実装 (処理中インデックスを
    /// ThreadLocal に保持) では FileCompressed の Target が null (Index=-1) や数件前の
    /// ファイルへ体系的に取り違わっていた。
    /// </para>
    /// <para>
    /// 判別力の確認: FileCompressing(k) と FileCompressed(k) のスレッドが異なる組が
    /// 1 つも無ければ、このテストは旧実装の不具合を検出できない環境なので Inconclusive にする。
    /// </para>
    /// </remarks>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void Save_SevenZipMultiThread_FileEventsMatchEntries()
    {
        var dir   = Get(nameof(Save_SevenZipMultiThread_FileEventsMatchEntries));
        var files = CreateRandomFiles(Path.Combine(dir, "src"), 40, 1024 * 1024);
        var dest  = Path.Combine(dir, "out.7z");

        using var writer = new ArchiveWriter(Format.SevenZip, new CompressionOption
        {
            CompressionMethod = CompressionMethod.Lzma2,
            CompressionLevel  = CompressionLevel.Fast,
            ThreadCount       = 4,
        });
        foreach (var file in files) writer.Add(file, Path.GetFileName(file));
        var events = RecordFileEvents(writer);
        writer.Save(dest);

        AssertFileEvents(events, files.Length, strictlyAlternating: true);

        var crossThread = 0;
        for (var i = 0; i + 1 < events.Count; i += 2)
        {
            if (events[i].ThreadId != events[i + 1].ThreadId) crossThread++;
        }
        if (crossThread == 0)
        {
            Assert.Inconclusive("FileCompressing(k) と FileCompressed(k) が全て同じスレッドで届いたため、" +
                "スレッドを跨ぐ SetOperationResult の取り違えを検出できない。");
        }
    }

    /* --------------------------------------------------------------------- */
    ///
    /// Save_OtherFormats_FileEventsMatchEntries
    ///
    /// <summary>
    /// ZIP (MT / ST)・Tar・GZip (単一エントリ) でも、FileCompressed が対応する
    /// FileCompressing と同じエントリで各 1 回発火し、GetStream(k) → SetOperationResult(k) の
    /// 交互で並ぶこと。
    /// </summary>
    ///
    /// <remarks>
    /// 7-Zip 26.x は全形式で GetStream / SetOperationResult を直列化する
    /// (ZIP MT は同一 CriticalSection 内で GetStream 直後に SetOperationResult を呼ぶため、
    /// FileCompressed は入力を圧縮スレッドへ渡した時点を表すが、交互性は保たれる)。
    /// </remarks>
    ///
    /* --------------------------------------------------------------------- */
    [TestCase(Format.Zip,  4, 20)]
    [TestCase(Format.Zip,  1, 20)]
    [TestCase(Format.Tar,  1, 20)]
    [TestCase(Format.GZip, 1, 1)]
    public void Save_OtherFormats_FileEventsMatchEntries(Format format, int threads, int count)
    {
        var dir   = Get(nameof(Save_OtherFormats_FileEventsMatchEntries), $"{format}_{threads}");
        var files = CreateRandomFiles(Path.Combine(dir, "src"), count, 256 * 1024);
        var dest  = Path.Combine(dir, "out.bin");

        using var writer = new ArchiveWriter(format, new CompressionOption
        {
            CompressionLevel = CompressionLevel.Fast,
            ThreadCount      = threads,
        });
        foreach (var file in files) writer.Add(file, Path.GetFileName(file));
        var events = RecordFileEvents(writer);
        writer.Save(dest);

        AssertFileEvents(events, files.Length, strictlyAlternating: true);
    }

    #endregion

    #region Helpers

    /// <summary>W12: FileCompressing / FileCompressed の 1 件分の記録。</summary>
    private sealed record FileEventRecord(bool Started, int Index, string Target, int ThreadId);

    /// <summary>
    /// W12: 指定数の乱数ファイル (圧縮で縮まない内容) を作成し、パスを返す。
    /// </summary>
    private static string[] CreateRandomFiles(string dir, int count, int size)
    {
        Directory.CreateDirectory(dir);
        var random = new Random(12345);
        var buffer = new byte[size];
        var files  = new string[count];
        for (var i = 0; i < count; i++)
        {
            random.NextBytes(buffer);
            files[i] = Path.Combine(dir, $"file{i:D2}.bin");
            File.WriteAllBytes(files[i], buffer);
        }
        return files;
    }

    /// <summary>
    /// W12: FileCompressing / FileCompressed を lock 内で発生順に記録する。
    /// </summary>
    /// <remarks>
    /// イベントは 7-Zip のコールバックスレッドから届き、SetCompleted と並行しうるため、
    /// 記録自体はロックで直列化する (記録順 = 発火順)。
    /// </remarks>
    private static List<FileEventRecord> RecordFileEvents(ArchiveWriter writer)
    {
        var events = new List<FileEventRecord>();
        var sync   = new object();
        void Record(bool started, ArchiveFileEventArgs e)
        {
            var record = new FileEventRecord(started, e.Index, e.Target?.FullName,
                Environment.CurrentManagedThreadId);
            lock (sync) events.Add(record);
        }
        writer.FileCompressing += (_, e) => Record(true, e);
        writer.FileCompressed  += (_, e) => Record(false, e);
        return events;
    }

    /// <summary>
    /// W12: FileCompressing / FileCompressed の記録が、GetStream が呼ばれた全エントリについて
    /// 各 1 回・同じ (Index, Target) の組であることを検証する。
    /// </summary>
    /// <param name="events">記録したイベント。</param>
    /// <param name="expectedCount">
    /// GetStream が呼ばれるエントリ数 (本テストは空でないファイルだけを入れるので全ファイル数)。
    /// </param>
    /// <param name="strictlyAlternating">
    /// true の場合、Compressing(k) → Compressed(k) → 次 の厳密な交互も検証する。
    /// </param>
    private static void AssertFileEvents(List<FileEventRecord> events, int expectedCount, bool strictlyAlternating)
    {
        var started  = events.Where(e => e.Started).ToList();
        var finished = events.Where(e => !e.Started).ToList();

        Assert.That(finished, Has.None.Matches<FileEventRecord>(e => e.Target is null || e.Index < 0),
            "FileCompressed の Target / Index が未設定になっている");
        Assert.That(started.Select(e => e.Index), Is.Unique, "FileCompressing が重複している");
        Assert.That(finished.Select(e => e.Index), Is.Unique, "FileCompressed が重複している");
        Assert.That(started.Count, Is.EqualTo(expectedCount));
        Assert.That(finished.Select(e => e.Index), Is.EquivalentTo(started.Select(e => e.Index)));

        // 同じ Index の FileCompressing と FileCompressed は同じファイルを指す。
        var targets = started.ToDictionary(e => e.Index, e => e.Target);
        foreach (var e in finished)
            Assert.That(e.Target, Is.EqualTo(targets[e.Index]), $"Index {e.Index} の Target が取り違わっている");

        if (!strictlyAlternating) return;
        Assert.That(events.Count, Is.EqualTo(expectedCount * 2));
        for (var i = 0; i < events.Count; i += 2)
        {
            Assert.That(events[i].Started, Is.True, $"{i} 番目が FileCompressing ではない");
            Assert.That(events[i + 1].Started, Is.False, $"{i + 1} 番目が FileCompressed ではない");
            Assert.That(events[i + 1].Index, Is.EqualTo(events[i].Index), $"{i + 1} 番目の Index が直前の開始と異なる");
            Assert.That(events[i + 1].Target, Is.EqualTo(events[i].Target), $"{i + 1} 番目の Target が直前の開始と異なる");
        }
    }

    /// <summary>W7 のテストで実行する操作を切り替える。</summary>
    private static void RunOperation(ArchiveWriter writer, string operation, string pathDest)
    {
        switch (operation)
        {
            case "SavePath":
                writer.Save(pathDest);
                break;
            case "SaveStream":
                using (var ms = new MemoryStream()) writer.Save(ms);
                break;
            case "UpdateStream":
                using (var src = new MemoryStream(File.ReadAllBytes(
                           Path.Combine(TestContext.CurrentContext.TestDirectory, "Examples", "Sample.zip"))))
                using (var dst = new MemoryStream())
                {
                    writer.Update(src, dst);
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    /// <summary>a.txt ("A") と b.txt ("B") を持つ ZIP を作成する。</summary>
    private static string CreateTwoEntryZip(string dir)
    {
        Directory.CreateDirectory(dir);
        var a = Path.Combine(dir, "a-src.txt");
        var b = Path.Combine(dir, "b-src.txt");
        File.WriteAllText(a, "A");
        File.WriteAllText(b, "B");
        var archive = Path.Combine(dir, "source.zip");
        using var writer = new ArchiveWriter(Format.Zip);
        writer.Add(a, "a.txt");
        writer.Add(b, "b.txt");
        writer.Save(archive);
        return archive;
    }

    /// <summary>書庫内パスからインデックスを取得する。</summary>
    private static int IndexOf(string archive, string name)
    {
        using var reader = new ArchiveReader(archive);
        return reader.Items.Single(e => e.FullName == name).Index;
    }

    /// <summary>名前 → 新名前の指定で rename しつつ Update(Stream) し、結果を一時ファイルへ書き出す。</summary>
    private static string UpdateWithRename(string archive, Action<Dictionary<string, string>> rename,
        Action<ArchiveWriter> configure)
    {
        var byName = new Dictionary<string, string>();
        rename(byName);
        var map = byName.ToDictionary(kv => IndexOf(archive, kv.Key), kv => kv.Value);

        using var source = new MemoryStream(File.ReadAllBytes(archive));
        using var dest = new MemoryStream();
        using (var writer = new ArchiveWriter(Format.Zip))
        {
            configure(writer);
            writer.Update(source, dest, map);
        }

        var output = Path.Combine(Path.GetDirectoryName(archive)!, "updated.zip");
        File.WriteAllBytes(output, dest.ToArray());
        return output;
    }

    /// <summary>書庫を展開し、パス → 内容の辞書を返す (同名エントリは件数を検証できるよう名前のみ別集計)。</summary>
    private static Dictionary<string, string> ReadAll(string archive)
    {
        var outDir = archive + ".extracted";
        using var reader = new ArchiveReader(archive);
        var names = reader.Items.Select(e => e.FullName).ToList();
        Assert.That(names.Count, Is.EqualTo(names.Distinct(StringComparer.OrdinalIgnoreCase).Count()),
            $"同名エントリが出力されている: {string.Join(", ", names)}");
        reader.Save(outDir);
        return names.ToDictionary(n => n, n => File.ReadAllText(Path.Combine(outDir, n)),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>単一エントリ書庫 (gz/bz2/xz) の内包名を取得する。</summary>
    private static string InnerName(string archive)
    {
        using var reader = new ArchiveReader(archive);
        Assert.That(reader.Items.Count, Is.EqualTo(1));
        return reader.Items[0].FullName;
    }

    /// <summary>W9 用のエントリ情報。</summary>
    private sealed record EntryInfo(int Index, bool IsDirectory, DateTime LastWriteTime, FileAttributes Attributes);

    /// <summary>書庫のエントリ情報を名前で引ける形で取得する。</summary>
    private static Dictionary<string, EntryInfo> Snapshot(string archive)
    {
        using var reader = new ArchiveReader(archive);
        return reader.Items.ToDictionary(e => e.FullName,
            e => new EntryInfo(e.Index, e.IsDirectory, e.LastWriteTime, e.Attributes));
    }

    /// <summary>rename 後のエントリが元エントリと同じ属性・時刻を持つことを検証する。</summary>
    private static void AssertSameProperties(EntryInfo actual, EntryInfo expected)
    {
        Assert.That(actual.IsDirectory, Is.EqualTo(expected.IsDirectory), "IsDirectory");
        Assert.That(actual.LastWriteTime, Is.EqualTo(expected.LastWriteTime), "LastWriteTime");
        Assert.That(actual.Attributes, Is.EqualTo(expected.Attributes), "Attributes");
        Assert.That(expected.LastWriteTime, Is.Not.EqualTo(default(DateTime)), "前提: 元エントリに時刻がある");
    }

    /// <summary>例外とその内部例外 (AggregateException を含む) を列挙する。</summary>
    private static IEnumerable<Exception> Flatten(Exception error)
    {
        if (error is null) yield break;
        yield return error;
        if (error is AggregateException agg)
        {
            foreach (var inner in agg.InnerExceptions)
                foreach (var e in Flatten(inner)) yield return e;
        }
        else
        {
            foreach (var e in Flatten(error.InnerException)) yield return e;
        }
    }

    /// <summary>ディレクトリジャンクションを作成する (管理者権限不要)。</summary>
    private static void CreateJunction(string link, string target)
    {
        var info = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute        = false,
            RedirectStandardError  = true,
            RedirectStandardOutput = true,
        };
        info.ArgumentList.Add("/d");
        info.ArgumentList.Add("/c");
        info.ArgumentList.Add("mklink");
        info.ArgumentList.Add("/J");
        info.ArgumentList.Add(link);
        info.ArgumentList.Add(target);

        using var process = Process.Start(info) ?? throw new InvalidOperationException("Failed to start mklink.");
        process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException(process.StandardError.ReadToEnd());
    }

    #endregion

    #region Controllers

    /// <summary>
    /// 指定ディレクトリのファイル列挙を、実際の遅延列挙と同じく MoveNext の時点で
    /// UnauthorizedAccessException にする IoController。
    /// </summary>
    private sealed class DenyEnumerationController(string denied) : IoController
    {
        public override IEnumerable<string> GetFiles(string path, string pattern, SearchOption option) =>
            IsDenied(path) ? Deny(path) : base.GetFiles(path, pattern, option);

        private bool IsDenied(string path) =>
            string.Equals(Path.GetFullPath(path), Path.GetFullPath(denied), StringComparison.OrdinalIgnoreCase);

        private static IEnumerable<string> Deny(string path)
        {
            // iterator なので例外は最初の MoveNext で発生する
            if (path.Length >= 0) throw new UnauthorizedAccessException($"Access to the path '{path}' is denied.");
            yield break;
        }
    }

    /// <summary>
    /// 自己更新の tmp → dest の rename と tmp の削除を失敗させる IoController (LastTempPath を残す)。
    /// </summary>
    private sealed class FailingTempController(string dest) : IoController
    {
        public override void Move(string src, string target)
        {
            if (IsDest(target) && IsTemp(src)) throw new IOException("move failed");
            base.Move(src, target);
        }

        public override void Delete(string path)
        {
            if (IsTemp(path)) throw new IOException("delete failed");
            base.Delete(path);
        }

        private bool IsDest(string path) =>
            string.Equals(Path.GetFullPath(path), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase);

        // 自己更新の tmp は dest と同じディレクトリの "{GUID:N}.zip"
        private static bool IsTemp(string path) =>
            Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileNameWithoutExtension(path).Length == 32;
    }

    /// <summary>条件に合うパスへの作成日時の再設定を IOException にする IoController。</summary>
    private sealed class MetadataFailureController(Func<string, bool> predicate) : IoController
    {
        public override void SetCreationTime(string path, DateTime time)
        {
            if (predicate(path)) throw new IOException("metadata failed");
            base.SetCreationTime(path, time);
        }
    }

    /// <summary>指定パスのオープンを別ファイルへ差し向ける IoController (リンク追跡の模擬)。</summary>
    private sealed class RedirectOpenController(string from, string to) : IoController
    {
        public override FileStream Open(string path) =>
            string.Equals(Path.GetFullPath(path), Path.GetFullPath(from), StringComparison.OrdinalIgnoreCase)
                ? base.Open(to)
                : base.Open(path);
    }

    #endregion
}
