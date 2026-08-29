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

/// <summary>
/// 再解析ポイントを含むファイルシステム操作の安全性を検証する。
/// </summary>
[TestFixture]
internal class ReparsePointSafetyTest : FileFixture
{
    [Test]
    public void ArchiveWriterRejectsDirectoryJunction()
    {
        var src     = Get(nameof(ArchiveWriterRejectsDirectoryJunction), "src");
        var outside = Get(nameof(ArchiveWriterRejectsDirectoryJunction), "outside");
        Directory.CreateDirectory(src);
        Directory.CreateDirectory(outside);
        var sentinel = Path.Combine(outside, "sentinel.txt");
        File.WriteAllText(sentinel, "sentinel");
        var link = Path.Combine(src, "junction");
        CreateJunction(link, outside);

        try
        {
            using var writer = new ArchiveWriter(Format.Zip);
            Assert.Throws<AccessException>(() => writer.Add(src));
            Assert.That(File.Exists(sentinel), Is.True);
        }
        finally { DeleteJunction(link); }
    }

    [Test]
    public void ArchiveWriterCanReportAndSkipDirectoryJunction()
    {
        var src     = Get(nameof(ArchiveWriterCanReportAndSkipDirectoryJunction), "src");
        var outside = Get(nameof(ArchiveWriterCanReportAndSkipDirectoryJunction), "outside");
        var dest    = Get(nameof(ArchiveWriterCanReportAndSkipDirectoryJunction), "out.zip");
        Directory.CreateDirectory(src);
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "sentinel.txt"), "sentinel");
        var link = Path.Combine(src, "junction");
        CreateJunction(link, outside);

        try
        {
            var skipped = new List<FileSkippedEventArgs>();
            var options = new CompressionOption { SkipInaccessibleFiles = true };
            using (var writer = new ArchiveWriter(Format.Zip, options))
            {
                writer.FileSkipped += (_, e) => skipped.Add(e);
                writer.Add(src);
                writer.Save(dest);
            }

            Assert.That(skipped, Has.Count.EqualTo(1));
            Assert.That(skipped[0].FullName, Is.EqualTo(link));
            using var reader = new ArchiveReader(dest);
            Assert.That(reader.Items.Select(e => e.FullName),
                Has.None.EndsWith("junction\\sentinel.txt"));
        }
        finally { DeleteJunction(link); }
    }

    [Test]
    public void RecursiveDeleteRemovesOnlyDirectoryJunction()
    {
        var src     = Get(nameof(RecursiveDeleteRemovesOnlyDirectoryJunction), "src");
        var outside = Get(nameof(RecursiveDeleteRemovesOnlyDirectoryJunction), "outside");
        Directory.CreateDirectory(src);
        Directory.CreateDirectory(outside);
        var sentinel = Path.Combine(outside, "sentinel.txt");
        File.WriteAllText(sentinel, "sentinel");
        var link = Path.Combine(src, "junction");
        CreateJunction(link, outside);

        Io.Delete(src);

        Assert.That(Directory.Exists(src), Is.False);
        Assert.That(File.Exists(sentinel), Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RecursiveTransferRejectsDirectoryJunction(bool move)
    {
        var name    = $"{nameof(RecursiveTransferRejectsDirectoryJunction)}-{move}";
        var src     = Get(name, "src");
        var outside = Get(name, "outside");
        var dest    = Get(name, "dest");
        Directory.CreateDirectory(src);
        Directory.CreateDirectory(outside);
        var sentinel = Path.Combine(outside, "sentinel.txt");
        File.WriteAllText(sentinel, "sentinel");
        var link = Path.Combine(src, "junction");
        CreateJunction(link, outside);

        try
        {
            Assert.Throws<IOException>(() => {
                if (move) Io.Move(src, dest, false);
                else Io.Copy(src, dest, false);
            });
            Assert.That(File.Exists(sentinel), Is.True);
        }
        finally { DeleteJunction(link); }
    }

    [Test]
    public void ExtractionRejectsExistingDirectoryJunction()
    {
        var src     = GetSource("Sample.zip");
        var dest    = Get(nameof(ExtractionRejectsExistingDirectoryJunction), "dest");
        var outside = Get(nameof(ExtractionRejectsExistingDirectoryJunction), "outside");
        Directory.CreateDirectory(dest);
        Directory.CreateDirectory(outside);
        var link = Path.Combine(dest, "Sample");
        CreateJunction(link, outside);

        try
        {
            using var reader = new ArchiveReader(src);
            var error = Assert.Throws<SevenZipException>(() => reader.Save(dest));
            Assert.That(error.InnerException, Is.TypeOf<IOException>());
            Assert.That(Directory.EnumerateFileSystemEntries(outside), Is.Empty);
        }
        finally { DeleteJunction(link); }
    }

    private static void CreateJunction(string link, string target)
    {
        var info = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute         = false,
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

    private static void DeleteJunction(string path)
    {
        if (Directory.Exists(path) &&
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            Directory.Delete(path, false);
        }
    }
}
