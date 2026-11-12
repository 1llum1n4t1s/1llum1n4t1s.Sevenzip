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
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
namespace Cube.FileSystem.SevenZip.Tests;

/* ------------------------------------------------------------------------- */
///
/// ReaderRegressionTest
///
/// <summary>
/// 読み取り (ArchiveReader / ExtractCallback / OpenCallback / SafePath) 経路の
/// レビュー指摘に対する回帰テスト。
/// </summary>
///
/// <remarks>
/// 範囲外 index のテストは「native へ渡る前に managed 側で拒否される」ことだけを
/// 検証する。修正前の実装では 7z.dll が無検査で配列参照してプロセスごと落ちうるため、
/// 範囲外 index を native へ届かせる形のテストは書かない。
/// </remarks>
///
/* ------------------------------------------------------------------------- */
[TestFixture]
internal class ReaderRegressionTest : FileFixture
{
    #region Open (R7 / R9)

    /* --------------------------------------------------------------------- */
    ///
    /// OpenHeaderEncrypted_CancelPassword_ThrowsOperationCanceled
    ///
    /// <summary>
    /// ヘッダー暗号化書庫のパスワード要求を利用者がキャンセルした場合、
    /// UnknownError (COMException) ではなく OperationCanceledException になること。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void OpenHeaderEncrypted_CancelPassword_ThrowsOperationCanceled()
    {
        var src   = GetSource("PasswordHeader.7z");
        var query = new Query<string>(e => e.Cancel = true);

        Assert.That(() => { using var r = new ArchiveReader(src, query); },
            Throws.InstanceOf<OperationCanceledException>());
    }

    /* --------------------------------------------------------------------- */
    ///
    /// OpenStream_Null_ThrowsArgumentNull
    ///
    /// <summary>
    /// Stream 版コンストラクタに null を渡すと、形式判定の NullReferenceException
    /// ではなく ArgumentNullException になること。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void OpenStream_Null_ThrowsArgumentNull()
    {
        Assert.That(() => { using var r = new ArchiveReader((Stream)null, leaveOpen: false); },
            Throws.InstanceOf<ArgumentNullException>());
        Assert.That(() => { using var r = new ArchiveReader((Stream)null, "", new ArchiveOption()); },
            Throws.InstanceOf<ArgumentNullException>());
    }

    /* --------------------------------------------------------------------- */
    ///
    /// OpenStream_NonSeekable_DisposesOwnedStream
    ///
    /// <summary>
    /// シーク不可 Stream を leaveOpen=false で渡して形式判定が失敗したとき、
    /// 所有権を受け取った Stream が閉じられること。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void OpenStream_NonSeekable_DisposesOwnedStream()
    {
        var bytes = File.ReadAllBytes(GetSource("Sample.zip"));
        var src   = new NonSeekableStream(bytes);

        Assert.That(() => { using var r = new ArchiveReader(src, leaveOpen: false); }, Throws.Exception);
        Assert.That(src.IsDisposed, Is.True, "所有権を受け取った Stream は ctor 失敗時に閉じる");

        // leaveOpen=true (既定) では呼び出し側の所有物なので閉じない
        var kept = new NonSeekableStream(bytes);
        Assert.That(() => { using var r = new ArchiveReader(kept); }, Throws.Exception);
        Assert.That(kept.IsDisposed, Is.False);
        kept.Dispose();
    }

    #endregion

    #region Index validation (R1)

    /* --------------------------------------------------------------------- */
    ///
    /// Save_OutOfRangeIndex_ThrowsBeforeNative
    ///
    /// <summary>
    /// Save(dest, uint[], …) に Items.Count 以上の index を渡すと、7z.dll へ渡す前に
    /// ArgumentOutOfRangeException になること。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void Save_OutOfRangeIndex_ThrowsBeforeNative()
    {
        var dest = Get(nameof(Save_OutOfRangeIndex_ThrowsBeforeNative));
        using var reader = new ArchiveReader(GetSource("Sample.zip"));
        var count = (uint)reader.Items.Count;

        Assert.That(() => reader.Save(dest, [0, count], null),
            Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => reader.Save(dest, [uint.MaxValue], null),
            Throws.TypeOf<ArgumentOutOfRangeException>());

        // 検査で reader が壊れていないこと (正常な index はそのまま展開できる)
        Assert.That(() => reader.Save(dest, [0], null), Throws.Nothing);
    }

    /* --------------------------------------------------------------------- */
    ///
    /// Extract_OutOfRangeKey_ThrowsBeforeNative
    ///
    /// <summary>
    /// Extract(dict) に負のキー・Items.Count 以上のキーを渡すと、uint キャストで
    /// 巨大値へ化ける前に ArgumentOutOfRangeException になること。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void Extract_OutOfRangeKey_ThrowsBeforeNative()
    {
        using var reader = new ArchiveReader(GetSource("Sample.zip"));
        using var output = new MemoryStream();

        Assert.That(() => reader.Extract(-1, output), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => reader.Extract(reader.Items.Count, output),
            Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(output.Length, Is.Zero);
    }

    /* --------------------------------------------------------------------- */
    ///
    /// Save_EntityFromOtherReader_ThrowsBeforeNative
    ///
    /// <summary>
    /// 別 reader の ArchiveEntity (index が自 reader の Items.Count 以上) を
    /// 拡張メソッド Save(dest, item) に渡しても native へ届かないこと。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void Save_EntityFromOtherReader_ThrowsBeforeNative()
    {
        var dest = Get(nameof(Save_EntityFromOtherReader_ThrowsBeforeNative));
        using var large = new ArchiveReader(GetSource("Sample.zip"));
        using var small = new ArchiveReader(GetSource("Sample.txt.gz"));
        var foreign = large.Items.Last();
        Assume.That(foreign.Index, Is.GreaterThanOrEqualTo(small.Items.Count));

        Assert.That(() => small.Save(dest, foreign), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    #endregion

    #region Stream errors (R5)

    /* --------------------------------------------------------------------- */
    ///
    /// Save_InputReadFailure_KeepsOriginalIOException
    ///
    /// <summary>
    /// 展開中に入力書庫 Stream の読み取りが失敗した場合、元の IOException が
    /// 展開例外の inner として保持されること。2 回目の操作でも新しい例外が
    /// 通知されること (通知済みフラグが操作ごとにリセットされること) も確認する。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void Save_InputReadFailure_KeepsOriginalIOException()
    {
        var bytes = CreateStoredSevenZip();
        using var src = new FailingReadStream(bytes);
        using var reader = new ArchiveReader(src);

        for (var i = 0; i < 2; i++)
        {
            var expected = new IOException($"injected read failure #{i}");
            src.Failure = expected;
            var dest = Get(nameof(Save_InputReadFailure_KeepsOriginalIOException), $"{i}");

            var error = Assert.Catch(() => reader.Save(dest));
            Assert.That(Chain(error).Any(e => ReferenceEquals(e, expected)), Is.True,
                $"attempt {i}: 元の IOException が失われた: {error}");
            src.Failure = null;
        }
    }

    #endregion

    #region Path sanitization (R2 / R3)

    /* --------------------------------------------------------------------- */
    ///
    /// SafePath_SegmentsEmptyAfterEscape_AreRemoved
    ///
    /// <summary>
    /// 末尾の空白・ピリオド除去で空になるセグメントが空要素として残らず、
    /// 結合結果がルート付き / UNC 形式のパスにならないこと。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [TestCase(@"...\a", ExpectedResult = "a")]
    [TestCase(@"   \x\y", ExpectedResult = @"x\y")]
    [TestCase(@". .\b", ExpectedResult = "b")]
    [TestCase(@"...\...", ExpectedResult = "")]
    [TestCase(@"a\ . \b", ExpectedResult = @"a\b")]
    [TestCase(@"...\\x\y", ExpectedResult = @"x\y")]
    public string SafePath_SegmentsEmptyAfterEscape_AreRemoved(string src) => new SafePath(src)
    {
        AllowParentDirectory  = false,
        AllowDriveLetter      = false,
        AllowCurrentDirectory = false,
        AllowInactivation     = false,
        AllowUnc              = false,
    }.Value;

    /* --------------------------------------------------------------------- */
    ///
    /// Extract_DotOnlySegment_ExtractsInsideDestination
    ///
    /// <summary>
    /// "..." のようにサニタイズで空になるディレクトリ名を含むエントリが、
    /// ルート付き FullName で Save 全体を中断させず、展開先配下へ書き出されること。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void Extract_DotOnlySegment_ExtractsInsideDestination()
    {
        var zip  = Get(nameof(Extract_DotOnlySegment_ExtractsInsideDestination), "src.zip");
        var dest = Get(nameof(Extract_DotOnlySegment_ExtractsInsideDestination), "dest");
        Io.CreateDirectory(Io.GetDirectoryName(zip));
        using (var fs = File.Create(zip))
        using (var za = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            using var w = za.CreateEntry(".../a.txt").Open();
            w.Write("hello"u8);
        }

        using var reader = new ArchiveReader(zip);
        Assert.That(reader.Items.Select(e => e.FullName), Is.EqualTo(new[] { "a.txt" }));
        reader.Save(dest);
        Assert.That(File.ReadAllText(Path.Combine(dest, "a.txt")), Is.EqualTo("hello"));
    }

    /* --------------------------------------------------------------------- */
    ///
    /// Writer_DotOnlySegment_IsNotStoredAsRootedName
    ///
    /// <summary>
    /// 書き込み側の名前サニタイズも同じ SafePath を使うため、"...\x.txt" を
    /// 追加名に指定しても書庫へ "\x.txt" (ルート付き) として格納されないこと。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void Writer_DotOnlySegment_IsNotStoredAsRootedName()
    {
        using var archive = new MemoryStream();
        using (var writer = new ArchiveWriter(Format.Zip))
        {
            using var input = new MemoryStream("data"u8.ToArray());
            writer.Add(input, @"...\x.txt");
            writer.Save(archive);
        }

        archive.Position = 0;
        using var reader = new ArchiveReader(archive);
        Assert.That(reader.Items.Select(e => e.RawName), Is.EqualTo(new[] { "x.txt" }));
    }

    /* --------------------------------------------------------------------- */
    ///
    /// ExtractStream_NamelessGZip_WritesFile
    ///
    /// <summary>
    /// sourceHint なしの Stream で開いた、FNAME を持たない gzip (エントリ名が
    /// 取得できない形式) を Save した場合に、何も書かずに成功するのではなく
    /// 既定名のファイルが 1 つ出力されること。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void ExtractStream_NamelessGZip_WritesFile()
    {
        var payload = Encoding.UTF8.GetBytes(new string('z', 1000));
        using var src = new MemoryStream();
        using (var gz = new GZipStream(src, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true)) gz.Write(payload);
        src.Position = 0;

        var dest = Get(nameof(ExtractStream_NamelessGZip_WritesFile));
        using var reader = new ArchiveReader(src);
        Assert.That(reader.Items, Has.Count.EqualTo(1));
        Assert.That(reader.Items[0].FullName, Is.Not.Empty, "名前の無いエントリにも既定名を与える");

        reader.Save(dest);
        var files = Directory.GetFiles(dest, "*", SearchOption.AllDirectories);
        Assert.That(files, Has.Length.EqualTo(1));
        Assert.That(File.ReadAllBytes(files[0]), Is.EqualTo(payload));
    }

    #endregion

    #region Final path verification (R4)

    /* --------------------------------------------------------------------- */
    ///
    /// Extract_DestinationUnderJunctionedAncestor_Succeeds
    ///
    /// <summary>
    /// 展開先 root より上位に junction があっても、出力ハンドルの実体パス検証が
    /// 誤検知せずに展開できること (root と出力の双方を同じ方法で実体解決している)。
    /// </summary>
    ///
    /// <remarks>
    /// 「作成直後の空の親を junction 化する」競合そのものは、テストから時機を
    /// 制御できないため自動テストでは再現していない。検出ロジックは
    /// <see cref="ThrowIfOutsideRoot_RejectsHandleOutsideRoot"/> で直接検証する。
    /// </remarks>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void Extract_DestinationUnderJunctionedAncestor_Succeeds()
    {
        var root = Get(nameof(Extract_DestinationUnderJunctionedAncestor_Succeeds));
        var real = Path.Combine(root, "real");
        var link = Path.Combine(root, "link");
        Directory.CreateDirectory(real);
        CreateJunction(link, real);

        try
        {
            var dest = Path.Combine(link, "dest");
            using var reader = new ArchiveReader(GetSource("Sample.zip"));
            reader.Save(dest);
            Assert.That(new FileInfo(Path.Combine(real, "dest", "Sample", "Bar.txt")).Length, Is.EqualTo(7816));
        }
        finally { DeleteJunction(link); }
    }

    /* --------------------------------------------------------------------- */
    ///
    /// ThrowIfOutsideRoot_RejectsHandleOutsideRoot
    ///
    /// <summary>
    /// 実体が root 配下に無いハンドルを拒否し、root 配下のハンドルは許可すること。
    /// 名前の前方一致 ("dest" と "dest2") を配下と誤認しないことも確認する。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void ThrowIfOutsideRoot_RejectsHandleOutsideRoot()
    {
        var baseDir = Get(nameof(ThrowIfOutsideRoot_RejectsHandleOutsideRoot));
        var inside  = Path.Combine(baseDir, "dest", "in.txt");
        var outside = Path.Combine(baseDir, "dest2", "out.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(inside));
        Directory.CreateDirectory(Path.GetDirectoryName(outside));
        File.WriteAllText(inside, "in");
        File.WriteAllText(outside, "out");

        using var rootHandle = FileSystemHelper.OpenDirectory(Path.Combine(baseDir, "dest"));
        using (var ok = File.OpenHandle(inside))
        {
            Assert.That(() => FileSystemHelper.ThrowIfOutsideRoot(rootHandle, ok, inside), Throws.Nothing);
        }
        using (var ng = File.OpenHandle(outside))
        {
            Assert.That(() => FileSystemHelper.ThrowIfOutsideRoot(rootHandle, ng, outside),
                Throws.TypeOf<IOException>());
        }

        // 攻撃形に近いケース: root 配下の親が junction に差し替えられ、パス表記上は
        // root 配下でも実体は root 外を指すハンドル。
        var link = Path.Combine(baseDir, "dest", "swapped");
        CreateJunction(link, Path.GetDirectoryName(outside));
        try
        {
            var viaLink = Path.Combine(link, "out.txt");
            using var redirected = File.OpenHandle(viaLink);
            Assert.That(() => FileSystemHelper.ThrowIfOutsideRoot(rootHandle, redirected, viaLink),
                Throws.TypeOf<IOException>());
        }
        finally { DeleteJunction(link); }
    }

    #endregion

    #region Volumes (R6)

    /* --------------------------------------------------------------------- */
    ///
    /// Open_RelativeVolumePath_ResolvesNextVolumes
    ///
    /// <summary>
    /// 相対パス "sub\x.rar.001" で分割書庫を開いたとき、次ボリュームが
    /// "sub\sub\..." へ二重解決されず、全ボリュームを読めること。
    /// </summary>
    ///
    /// <remarks>
    /// カレントディレクトリはプロセス全体の状態なので、finally で必ず戻す
    /// (本テストプロジェクトは並列実行しない)。
    /// </remarks>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void Open_RelativeVolumePath_ResolvesNextVolumes()
    {
        var root = Get(nameof(Open_RelativeVolumePath_ResolvesNextVolumes));
        var sub  = Path.Combine(root, "sub");
        var dest = Path.Combine(root, "dest");
        Directory.CreateDirectory(sub);
        for (var i = 1; i <= 5; i++)
        {
            var name = $"SampleVolume.rar.{i:D3}";
            File.Copy(GetSource(name), Path.Combine(sub, name), true);
        }

        var cwd = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = root;
            using var reader = new ArchiveReader(@"sub\SampleVolume.rar.001");
            reader.Save(dest);
        }
        finally { Environment.CurrentDirectory = cwd; }

        Assert.That(new FileInfo(Path.Combine(dest, "Split", "Manual.pdf")).Length, Is.EqualTo(990040));
    }

    #endregion

    #region Password (R8)

    /* --------------------------------------------------------------------- */
    ///
    /// SetOperationResult_DataErrorOnPlainEntry_IsNotWrongPassword
    ///
    /// <summary>
    /// パスワード要求後であっても、暗号化されていないエントリの DataError は
    /// WrongPassword に読み替えず、データ破損として通知されること。
    /// 暗号化エントリの DataError は従来どおり WrongPassword 扱いを維持する。
    /// </summary>
    ///
    /// <remarks>
    /// 暗号化・非暗号化混在で破損を含む書庫を作るのは難しいため、ExtractCallback を
    /// 直接駆動して 7z.dll の呼び出し順 (CryptoGetTextPassword → GetStream →
    /// PrepareOperation → SetOperationResult) を再現する。
    /// </remarks>
    ///
    /* --------------------------------------------------------------------- */
    [TestCase("Sample.zip", false, ExpectedResult = false)]
    [TestCase("Password.7z", true, ExpectedResult = true)]
    public bool SetOperationResult_DataErrorOnPlainEntry_IsNotWrongPassword(string filename, bool encrypted)
    {
        using var reader = new ArchiveReader(GetSource(filename), "password");
        var target = reader.Items.First(e => !e.IsDirectory && e.Encrypted == encrypted);

        using var cb = new ExtractCallback(string.Empty, new ArchiveEnumerator(reader.Items), null)
        {
            Password = new PasswordQuery("password"),
        };
        Assert.That(cb.CryptoGetTextPassword(out _), Is.EqualTo((int)SevenZipCode.Success));
        Assert.That(cb.GetStream((uint)target.Index, out _, AskMode.Test), Is.EqualTo((int)SevenZipCode.Success));
        Assert.That(cb.PrepareOperation(AskMode.Test), Is.EqualTo((int)SevenZipCode.Success));

        var result = cb.SetOperationResult(SevenZipCode.DataError);
        return result == (int)SevenZipCode.WrongPassword;
    }

    #endregion

    #region COM reference count (R10)

    /* --------------------------------------------------------------------- */
    ///
    /// ComWrapper_Release_LeavesNoNativeReference
    ///
    /// <summary>
    /// CreateObject / QueryInterface で得た COM ポインタを ComWrappers でラップし、
    /// ReleaseComWrapper で解放した後に、ネイティブ側の参照が残らないこと。
    /// </summary>
    ///
    /// <remarks>
    /// TryGetComInstance で +1 した観測用参照を最後に Release し、その戻り値
    /// (= 残りの参照数) が 0 であることを確認する。1 以上なら呼び出し側の参照が
    /// 余分に残っている (リーク)。
    /// </remarks>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void ComWrapper_Release_LeavesNoNativeReference()
    {
        using var lease = SevenZipLibrary.Acquire();

        var core = lease.GetInArchive(Format.Zip);
        Assert.That(System.Runtime.InteropServices.ComWrappers.TryGetComInstance(core, out var p0), Is.True);
        lease.ReleaseComWrapper(core);
        Assert.That(System.Runtime.InteropServices.Marshal.Release(p0), Is.Zero, "CreateObject 経路");

        var zip = lease.GetInArchive(Format.Zip);
        var props = lease.QueryInterface<ISetProperties>(zip);
        Assert.That(props, Is.Not.Null);
        Assert.That(System.Runtime.InteropServices.ComWrappers.TryGetComInstance(zip, out var p1), Is.True);
        lease.ReleaseComWrapper(props);
        lease.ReleaseComWrapper(zip);
        Assert.That(System.Runtime.InteropServices.Marshal.Release(p1), Is.Zero, "QueryInterface 経路");
    }

    #endregion

    #region Helpers

    /// <summary>
    /// ディレクトリ junction を作成する (ReparsePointSafetyTest と同じ手順)。
    /// </summary>
    private static void CreateJunction(string link, string target)
    {
        var info = new System.Diagnostics.ProcessStartInfo("cmd.exe")
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

        using var process = System.Diagnostics.Process.Start(info) ??
            throw new InvalidOperationException("Failed to start mklink.");
        process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException(process.StandardError.ReadToEnd());
    }

    /// <summary>
    /// junction 自体だけを削除する (リンク先の内容は消さない)。
    /// </summary>
    private static void DeleteJunction(string path)
    {
        if (Directory.Exists(path) &&
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            Directory.Delete(path, false);
        }
    }

    /// <summary>
    /// 例外とその InnerException / AggregateException の子を列挙する。
    /// </summary>
    private static System.Collections.Generic.IEnumerable<Exception> Chain(Exception src)
    {
        if (src is null) yield break;
        yield return src;
        if (src is AggregateException agg)
        {
            foreach (var e in agg.InnerExceptions.SelectMany(Chain)) yield return e;
        }
        else foreach (var e in Chain(src.InnerException)) yield return e;
    }

    /// <summary>
    /// 展開時に入力の読み取りが確実に発生するよう、非圧縮性データの 7z を作る。
    /// (7z はヘッダーを open 時に、データ本体を Extract 時に読む)
    /// </summary>
    private static byte[] CreateStoredSevenZip()
    {
        var payload = new byte[256 * 1024];
        new Random(7).NextBytes(payload);

        using var archive = new MemoryStream();
        using (var writer = new ArchiveWriter(Format.SevenZip))
        {
            using var input = new MemoryStream(payload);
            writer.Add(input, "payload.bin");
            writer.Save(archive);
        }
        return archive.ToArray();
    }

    /// <summary>
    /// <see cref="Failure"/> が設定されている間だけ Read で例外を投げる Stream。
    /// </summary>
    private sealed class FailingReadStream(byte[] buffer) : MemoryStream(buffer)
    {
        public Exception Failure { get; set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (Failure is not null) throw Failure;
            return base.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            if (Failure is not null) throw Failure;
            return base.Read(buffer);
        }
    }

    /// <summary>
    /// シーク不可で、Dispose されたかどうかを観測できる Stream。
    /// </summary>
    private sealed class NonSeekableStream(byte[] buffer) : MemoryStream(buffer)
    {
        public bool IsDisposed { get; private set; }
        public override bool CanSeek => false;
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override long Seek(long offset, SeekOrigin loc) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    #endregion
}
