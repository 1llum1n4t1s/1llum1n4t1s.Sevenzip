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
namespace Cube.FileSystem.SevenZip.Tests;

/* ------------------------------------------------------------------------- */
///
/// ArchiveStreamApiTest
///
/// <summary>
/// Stream ベース API（ArchiveReader(Stream) / Extract(index, Stream) /
/// ArchiveWriter.Save(Stream) / Update(Stream, Stream)）および新規追加
/// オプション群の回帰テスト。
/// </summary>
///
/* ------------------------------------------------------------------------- */
[TestFixture]
internal class ArchiveStreamApiTest : FileFixture
{
    /* --------------------------------------------------------------------- */
    ///
    /// Reader_Stream_OpenExistingZip
    ///
    /// <summary>
    /// 既存 ZIP を Stream 経由で開いて、path 版と同じエントリが列挙されること。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void Reader_Stream_OpenExistingZip()
    {
        var src = GetSource("Sample.zip");

        List<string> pathNames;
        using (var r0 = new ArchiveReader(src))
        {
            pathNames = r0.Items.Select(e => e.FullName).OrderBy(x => x).ToList();
        }

        using var fs = File.OpenRead(src);
        using var r1 = new ArchiveReader(fs);
        var streamNames = r1.Items.Select(e => e.FullName).OrderBy(x => x).ToList();

        Assert.That(r1.Format, Is.EqualTo(Format.Zip));
        Assert.That(streamNames, Is.EqualTo(pathNames));
        Assert.That(r1.Source, Is.Empty, "Stream 版では Source は空文字");
    }

    /* --------------------------------------------------------------------- */
    ///
    /// Reader_ExtractSingleEntryToStream
    ///
    /// <summary>
    /// Extract(index, Stream) で単一エントリをメモリに展開し、
    /// path 版で展開したファイルと同じバイト列になること。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void Reader_ExtractSingleEntryToStream()
    {
        var src  = GetSource("Sample.zip");
        var dest = Get(nameof(Reader_ExtractSingleEntryToStream));

        // path 版で全展開した結果を期待値とする
        using (var r0 = new ArchiveReader(src))
        {
            r0.Save(dest);
        }

        // Stream 版で特定のファイルエントリだけを展開する
        using var archive = new ArchiveReader(src);
        var target = archive.Items.FirstOrDefault(e => !e.IsDirectory);
        Assert.That(target, Is.Not.Null);

        using var output = new MemoryStream();
        archive.Extract(target.Index, output);

        var expected = File.ReadAllBytes(Io.Combine(dest, target.FullName));
        Assert.That(output.ToArray(), Is.EqualTo(expected));
    }

    /* --------------------------------------------------------------------- */
    ///
    /// Writer_Save_ToStream_Roundtrip
    ///
    /// <summary>
    /// ArchiveWriter.Save(Stream) で MemoryStream に ZIP を書き出し、
    /// 同じストリームから ArchiveReader(Stream) で読み直せること。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void Writer_Save_ToStream_Roundtrip()
    {
        var src = GetSource("Sample.txt");

        using var buffer = new MemoryStream();
        using (var w = new ArchiveWriter(Format.Zip))
        {
            w.Add(src);
            w.Save(buffer);
        }

        buffer.Position = 0;
        using var r = new ArchiveReader(buffer);
        Assert.That(r.Format, Is.EqualTo(Format.Zip));
        Assert.That(r.Items.Count, Is.EqualTo(1));
        Assert.That(r.Items[0].FullName, Is.EqualTo(Io.GetFileName(src)));
    }

    /* --------------------------------------------------------------------- */
    ///
    /// Writer_AddStream_RoundTrip
    ///
    /// <summary>
    /// Add(Stream, name) で仮想エントリを追加した ZIP をメモリ上で構築し、
    /// 読み直して内容が一致すること。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void Writer_AddStream_RoundTrip()
    {
        var payload = Encoding.UTF8.GetBytes("hello stream entry");
        using var buffer = new MemoryStream();
        using (var w = new ArchiveWriter(Format.Zip))
        using (var src = new MemoryStream(payload))
        {
            w.Add(src, "virtual/file.txt");
            w.Save(buffer);
        }

        buffer.Position = 0;
        using var r = new ArchiveReader(buffer);
        var entry = r.Items.First(e => !e.IsDirectory);

        using var extracted = new MemoryStream();
        r.Extract(entry.Index, extracted);
        Assert.That(extracted.ToArray(), Is.EqualTo(payload));
    }

    /* --------------------------------------------------------------------- */
    ///
    /// Writer_Update_Stream_RemoveAndAdd
    ///
    /// <summary>
    /// Update(Stream, Stream) で既存アーカイブから 1 エントリ削除 + 新規 1 件追加し、
    /// 結果が期待通りになること。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void Writer_Update_Stream_RemoveAndAdd()
    {
        var src = GetSource("Sample.zip");

        using var inBuffer = new MemoryStream();
        using (var fs = File.OpenRead(src)) fs.CopyTo(inBuffer);
        inBuffer.Position = 0;

        // 元 ZIP のエントリ一覧を取得
        string firstName;
        using (var r0 = new ArchiveReader(new MemoryStream(inBuffer.ToArray())))
        {
            firstName = r0.Items.First(e => !e.IsDirectory).FullName;
        }

        using var outBuffer = new MemoryStream();
        var payload = Encoding.UTF8.GetBytes("new entry content");
        using (var w = new ArchiveWriter(Format.Zip))
        using (var mem = new MemoryStream(payload))
        {
            w.Add(mem, "new_stream_entry.txt");
            w.Remove(firstName);
            inBuffer.Position = 0;
            w.Update(inBuffer, outBuffer);
        }

        outBuffer.Position = 0;
        using var r = new ArchiveReader(outBuffer);
        var names = r.Items.Select(e => e.FullName).ToList();
        Assert.That(names, Does.Contain("new_stream_entry.txt"));
        Assert.That(names, Does.Not.Contain(firstName));
    }

    /* --------------------------------------------------------------------- */
    ///
    /// Format_FromStream
    ///
    /// <summary>
    /// FormatFactory.From(Stream) で各フォーマットが自動判定されること。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [TestCase("Sample.zip", Format.Zip)]
    [TestCase("Sample.tar.gz", Format.GZip)]
    [TestCase("Sample.tar", Format.Tar)]
    [TestCase("Sample.rar5", Format.Rar5)]
    public void Format_FromStream(string filename, Format expected)
    {
        using var fs = File.OpenRead(GetSource(filename));
        Assert.That(FormatFactory.From(fs), Is.EqualTo(expected));
        // ストリーム位置が元に戻っていることを確認
        Assert.That(fs.Position, Is.EqualTo(0));
    }

    /* --------------------------------------------------------------------- */
    ///
    /// CompressionOption_CustomParameters
    ///
    /// <summary>
    /// CustomParameters で <c>cu=on</c> (UTF-8 強制) / <c>mt=1</c> を指定しても
    /// 正常に ZIP が作成できること。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void CompressionOption_CustomParameters()
    {
        var src  = GetSource("Sample.txt");
        var dest = Get(nameof(CompressionOption_CustomParameters), "out.zip");

        var opt = new CompressionOption
        {
            CustomParameters = new Dictionary<string, string>
            {
                { "cu", "on" },
                { "mt", "1" },
            }
        };
        using (var w = new ArchiveWriter(Format.Zip, opt))
        {
            w.Add(src);
            w.Save(dest);
        }

        using var r = new ArchiveReader(dest);
        Assert.That(r.Items.Count, Is.EqualTo(1));
    }

    /* --------------------------------------------------------------------- */
    ///
    /// ArchiveOption_Encoding_AndDefaultsToOem
    ///
    /// <summary>
    /// ArchiveOption.Encoding を指定しない場合は従来通り CodePage.Oem で動作し、
    /// 指定した場合は非デフォルトコードページとして扱われること（ArchiveReader の
    /// 内部分岐が例外を投げないこと）。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void ArchiveOption_Encoding_AndDefaultsToOem()
    {
        var src = GetSource("Sample.zip");

        var defaultOption = new ArchiveOption();
        Assert.That(defaultOption.Encoding, Is.Null);
        Assert.That(defaultOption.CodePage, Is.EqualTo(CodePage.Oem));

        // UTF-8 は .NET Core でもプロバイダ登録不要で使える
        var utf8Option = new ArchiveOption { Encoding = Encoding.UTF8 };
        Assert.That(utf8Option.Encoding, Is.Not.Null);
        Assert.That(utf8Option.Encoding.CodePage, Is.EqualTo(65001));

        // Sample.zip は UTF-8 で開いても破綻しないはず
        using var r = new ArchiveReader(src, utf8Option);
        Assert.That(r.Items.Count, Is.GreaterThan(0));
    }

    /* --------------------------------------------------------------------- */
    ///
    /// Writer_FileCompressed_EventFires
    ///
    /// <summary>
    /// FileCompressing / FileCompressed イベントが各エントリで発火すること。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void Writer_FileCompressed_EventFires()
    {
        var src  = GetSource("Sample.txt");
        var dest = Get(nameof(Writer_FileCompressed_EventFires), "out.zip");

        var started  = 0;
        var finished = 0;
        using (var w = new ArchiveWriter(Format.Zip))
        {
            w.FileCompressing += (_, _) => started++;
            w.FileCompressed  += (_, _) => finished++;
            w.Add(src);
            w.Save(dest);
        }

        Assert.That(started, Is.GreaterThan(0));
        Assert.That(finished, Is.EqualTo(started));
    }

    /* --------------------------------------------------------------------- */
    ///
    /// Reader_FileExtracted_EventFires
    ///
    /// <summary>
    /// FileExtracting / FileExtracted イベントが各エントリで発火すること。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void Reader_FileExtracted_EventFires()
    {
        var src  = GetSource("Sample.zip");
        var dest = Get(nameof(Reader_FileExtracted_EventFires));

        var started  = 0;
        var finished = 0;
        using (var r = new ArchiveReader(src))
        {
            r.FileExtracting += (_, _) => started++;
            r.FileExtracted  += (_, _) => finished++;
            r.Save(dest);
        }

        Assert.That(started, Is.GreaterThan(0));
        Assert.That(finished, Is.GreaterThan(0));
    }

    /* --------------------------------------------------------------------- */
    ///
    /// CompressionOption_IncludeEmptyDirectories
    ///
    /// <summary>
    /// IncludeEmptyDirectories = false で、子孫ファイルを持たないディレクトリ
    /// エントリが除外されること。true (既定) では含まれること。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void CompressionOption_IncludeEmptyDirectories()
    {
        var emptyRoot = Get(nameof(CompressionOption_IncludeEmptyDirectories), "src");
        var emptySub  = Io.Combine(emptyRoot, "empty_dir");
        var withFile  = Io.Combine(emptyRoot, "has_file");
        Io.CreateDirectory(emptySub);
        Io.CreateDirectory(withFile);
        File.WriteAllText(Io.Combine(withFile, "a.txt"), "hello");

        var dest1 = Get(nameof(CompressionOption_IncludeEmptyDirectories), "with_empty.zip");
        var dest2 = Get(nameof(CompressionOption_IncludeEmptyDirectories), "no_empty.zip");

        using (var w = new ArchiveWriter(Format.Zip,
            new CompressionOption { IncludeEmptyDirectories = true }))
        {
            w.Add(emptyRoot, "src");
            w.Save(dest1);
        }
        using (var w = new ArchiveWriter(Format.Zip,
            new CompressionOption { IncludeEmptyDirectories = false }))
        {
            w.Add(emptyRoot, "src");
            w.Save(dest2);
        }

        using var rWith = new ArchiveReader(dest1);
        using var rNo   = new ArchiveReader(dest2);

        var withNames = rWith.Items.Select(e => e.FullName.Replace('/', '\\')).ToList();
        var noNames   = rNo.Items.Select(e => e.FullName.Replace('/', '\\')).ToList();

        // with_empty には empty_dir エントリが含まれる
        Assert.That(withNames.Any(n => n.Contains("empty_dir")), Is.True);
        // no_empty には empty_dir エントリが含まれない (子孫ファイルを持たないため除外)
        Assert.That(noNames.Any(n => n.Contains("empty_dir")), Is.False);
        // has_file は両方に含まれる
        Assert.That(withNames.Any(n => n.Contains("has_file")), Is.True);
        Assert.That(noNames.Any(n => n.Contains("has_file")), Is.True);
    }
}
