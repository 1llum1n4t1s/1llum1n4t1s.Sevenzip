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
/// CallbackRegressionTest
///
/// <summary>
/// 7-Zip コールバック境界の回帰テスト。
/// </summary>
///
/* ------------------------------------------------------------------------- */
[TestFixture]
internal class CallbackRegressionTest : FileFixture
{
    /* --------------------------------------------------------------------- */
    ///
    /// ExtractSingleEntryFromSolidArchive
    ///
    /// <summary>
    /// ソリッド 7z の途中エントリを path と Stream の両方へ部分展開できること。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void ExtractSingleEntryFromSolidArchive()
    {
        var payloads = new Dictionary<string, byte[]>
        {
            ["first.txt"]  = Encoding.UTF8.GetBytes(new string('A', 4096)),
            ["second.txt"] = Encoding.UTF8.GetBytes(new string('B', 4096)),
            ["third.txt"]  = Encoding.UTF8.GetBytes(new string('C', 4096)),
        };

        byte[] archiveBytes;
        using (var archive = new MemoryStream())
        {
            using (var writer = new ArchiveWriter(Format.SevenZip))
            {
                foreach (var pair in payloads)
                {
                    using var input = new MemoryStream(pair.Value);
                    writer.Add(input, pair.Key);
                }
                writer.Save(archive);
            }
            archiveBytes = archive.ToArray();
        }

        const string targetName = "third.txt";
        var expected = payloads[targetName];

        using (var input = new MemoryStream(archiveBytes))
        using (var reader = new ArchiveReader(input))
        using (var output = new MemoryStream())
        {
            var target = reader.Items.Single(e => e.FullName == targetName);
            reader.Extract(target.Index, output);
            Assert.That(output.ToArray(), Is.EqualTo(expected));
        }

        var destination = Get(nameof(ExtractSingleEntryFromSolidArchive));
        using (var input = new MemoryStream(archiveBytes))
        using (var reader = new ArchiveReader(input))
        {
            var target = reader.Items.Single(e => e.FullName == targetName);
            reader.Save(destination, [(uint)target.Index], null);
        }

        Assert.That(File.ReadAllBytes(Path.Combine(destination, targetName)), Is.EqualTo(expected));
    }

    /* --------------------------------------------------------------------- */
    ///
    /// OpenVolumeRejectsPathsOutsideArchiveDirectory
    ///
    /// <summary>
    /// ボリューム名からアーカイブと異なるディレクトリを開かないこと。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void OpenVolumeRejectsPathsOutsideArchiveDirectory()
    {
        var root = Get(nameof(OpenVolumeRejectsPathsOutsideArchiveDirectory));
        var volumeDirectory = Path.Combine(root, "volumes");
        Directory.CreateDirectory(volumeDirectory);

        var source = Path.Combine(volumeDirectory, "archive.rar.001");
        var sibling = Path.Combine(volumeDirectory, "archive.rar.002");
        var outside = Path.Combine(root, "outside.rar.002");
        File.WriteAllText(source, "first");
        File.WriteAllText(sibling, "sibling");
        File.WriteAllText(outside, "outside");

        using var callback = new OpenCallback(source);

        var absoluteCode = callback.GetStream(outside, out var absoluteStream);
        Assert.Multiple(() =>
        {
            Assert.That(absoluteCode, Is.Not.EqualTo((int)SevenZipCode.Success));
            Assert.That(absoluteStream, Is.Null);
        });

        var traversalCode = callback.GetStream(Path.Combine("..", "outside.rar.002"), out var traversalStream);
        Assert.Multiple(() =>
        {
            Assert.That(traversalCode, Is.Not.EqualTo((int)SevenZipCode.Success));
            Assert.That(traversalStream, Is.Null);
        });

        var siblingCode = callback.GetStream(sibling, out var siblingStream);
        Assert.Multiple(() =>
        {
            Assert.That(siblingCode, Is.EqualTo((int)SevenZipCode.Success));
            Assert.That(siblingStream, Is.Not.Null);
        });
    }

    /* --------------------------------------------------------------------- */
    ///
    /// OpenVolumeCapturesIoFailure
    ///
    /// <summary>
    /// ボリュームの I/O 例外を COM 境界の手前で保存すること。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void OpenVolumeCapturesIoFailure()
    {
        var root = Get(nameof(OpenVolumeCapturesIoFailure));
        Directory.CreateDirectory(root);

        var source = Path.Combine(root, "archive.rar.001");
        var volume = Path.Combine(root, "archive.rar.002");
        File.WriteAllText(source, "first");
        File.WriteAllText(volume, "second");

        using var locked = new FileStream(volume, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var callback = new OpenCallback(source);

        var code = callback.GetStream("archive.rar.002", out var stream);

        Assert.Multiple(() =>
        {
            Assert.That(code, Is.Not.EqualTo((int)SevenZipCode.Success));
            Assert.That(stream, Is.Null);
            Assert.That(callback.Exceptions.TryPeek(out _), Is.True);
        });
    }
}
