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
using System.IO;
namespace Cube.FileSystem.SevenZip.Tests;

/* ------------------------------------------------------------------------- */
///
/// ArchiveWriterExTest
///
/// <summary>
/// Provides additional tests for the ArchiveWriter class.
/// </summary>
///
/* ------------------------------------------------------------------------- */
[TestFixture]
internal class ArchiveWriterExTest : FileFixture
{
    #region Tests

    /* --------------------------------------------------------------------- */
    ///
    /// TestHierarchicalDirectory
    ///
    /// <summary>
    /// Tests to compress files and hierarchical directories.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void TestHierarchicalDirectory()
    {
        var expected = new[]
        {
            @"Sample.txt",
            @"Sample 00..01",
            @"Sample 00..01\Dot 2018.02.13.txt",
            @"Sample 00..01\Empty.txt",
            @"Sample 00..01\FileInDirectory.txt",
            @"Sample 00..01\Filter.txt",
            @"Sample 00..01\FilterDirectory",
            @"Sample 00..01\FilterDirectory\Filter.txt",
            @"Sample 00..01\FilterDirectory\Sample.txt",
        };

        var file = Get($"{nameof(TestHierarchicalDirectory)}.zip");
        using (var obj = new ArchiveWriter(Format.Zip, new()))
        {
            obj.Add(GetSource("Sample.txt"));
            obj.Add(GetSource("Sample 00..01"));
            obj.Save(file);
        }

        using var dest = new ArchiveReader(file);
        for (var i = 0; i < Math.Min(dest.Items.Count, expected.Length); ++i)
        {
            Assert.That(dest.Items[i].FullName, Is.EqualTo(expected[i]));
        }
    }

    /* --------------------------------------------------------------------- */
    ///
    /// WithCjk
    ///
    /// <summary>
    /// Tests to compress a file containing a CJK filename.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [TestCase(CodePage.Utf8)]
    [TestCase(CodePage.Japanese)]
    public void WithCjk(CodePage cp)
    {
        var zip  = Format.Zip;
        var src  = Get("日本語のファイル名.txt");
        var dest = Get($"{nameof(WithCjk)}{zip}{cp}.zip");

        Io.Copy(GetSource("Sample.txt"), src, true);
        Assert.That(Io.Exists(src), Is.True);

        using (var obj = new ArchiveWriter(zip, new() { CodePage = cp }))
        {
            obj.Add(src);
            obj.Save(dest);
        }

        using var ss = Io.Open(dest);
        Assert.That(FormatFactory.From(ss), Is.EqualTo(zip));
    }

    /* --------------------------------------------------------------------- */
    ///
    /// WithFilter
    ///
    /// <summary>
    /// Tests to create an archive with filter values.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [TestCase(true,  ExpectedResult = 5)]
    [TestCase(false, ExpectedResult = 9)]
    public int WithFilter(bool enabled)
    {
        var dest = Get($"Filter{enabled}.zip");
        var opts = new CompressionOption
        {
            Filter = enabled ?
                     Filter.From(new[] { "Filter.txt", "FilterDirectory" }) :
                     null,
        };

        using (var obj = new ArchiveWriter(Format.Zip, opts))
        {
            obj.Add(GetSource("Sample.txt"));
            obj.Add(GetSource("Sample 00..01"));
            obj.Save(dest);
        }

        using (var obj = new ArchiveReader(dest)) return obj.Items.Count;
    }

    /* --------------------------------------------------------------------- */
    ///
    /// Add_NotFound
    ///
    /// <summary>
    /// Tests the Add method with an inexistent file.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void Add_NotFound() => Assert.That(() =>
    {
        using var obj = new ArchiveWriter(Format.Zip);
        obj.Add(GetSource("NotFound.txt"));
    }, Throws.TypeOf<FileNotFoundException>());

    /* --------------------------------------------------------------------- */
    ///
    /// Add_FileShareNone_ThrowsAccessException
    ///
    /// <summary>
    /// 既定 (<see cref="CompressionOption.SkipInaccessibleFiles"/> = false) では、
    /// <see cref="FileShare.None"/> で他プロセスが排他保持しているファイルを Add すると
    /// <see cref="AccessException"/> が投げられる (後方互換挙動)。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void Add_FileShareNone_ThrowsAccessException()
    {
        var locked = Get("locked_default.bin");
        File.WriteAllText(locked, "exclusive");
        using var hold = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.That(() =>
        {
            using var obj = new ArchiveWriter(Format.Zip);
            obj.Add(locked);
        }, Throws.TypeOf<AccessException>());
    }

    /* --------------------------------------------------------------------- */
    ///
    /// Add_SkipInaccessibleFiles_SkipsAndFiresEvent
    ///
    /// <summary>
    /// <see cref="CompressionOption.SkipInaccessibleFiles"/> = true のとき、
    /// <see cref="FileShare.None"/> で排他保持されたファイルは例外を投げずにスキップされ、
    /// <see cref="ArchiveWriter.FileSkipped"/> イベントが発火する。読めるファイルだけが
    /// アーカイブに収まる。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void Add_SkipInaccessibleFiles_SkipsAndFiresEvent()
    {
        var readable = Get("readable.txt");
        var locked   = Get("locked_skip.bin");
        var dest     = Get($"{nameof(Add_SkipInaccessibleFiles_SkipsAndFiresEvent)}.zip");
        File.WriteAllText(readable, "ok");
        File.WriteAllText(locked, "exclusive");

        using var hold = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var skipped = new List<FileSkippedEventArgs>();
        using (var obj = new ArchiveWriter(Format.Zip, new() { SkipInaccessibleFiles = true }))
        {
            obj.FileSkipped += (_, e) => skipped.Add(e);

            Assert.That(() => obj.Add(readable), Throws.Nothing);
            Assert.That(() => obj.Add(locked),   Throws.Nothing);
            obj.Save(dest);
        }

        Assert.That(skipped, Has.Count.EqualTo(1));
        Assert.That(skipped[0].FullName,     Is.EqualTo(locked));
        Assert.That(skipped[0].RelativeName, Is.EqualTo("locked_skip.bin"));
        Assert.That(skipped[0].Reason,       Is.Not.Null);

        using var reader = new ArchiveReader(dest);
        Assert.That(reader.Items, Has.Count.EqualTo(1));
        Assert.That(reader.Items[0].FullName, Is.EqualTo("readable.txt"));
    }

    #endregion
}
