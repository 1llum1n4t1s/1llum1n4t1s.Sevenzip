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
using System.IO;
using System.Linq;
namespace Cube.FileSystem.SevenZip.Tests;

/* ------------------------------------------------------------------------- */
///
/// ArchiveWriterRegressionTest
///
/// <summary>
/// ArchiveWriter の回帰テスト。
/// </summary>
///
/* ------------------------------------------------------------------------- */
[TestFixture]
internal class ArchiveWriterRegressionTest : FileFixture
{
    /* --------------------------------------------------------------------- */
    ///
    /// SaveTarToReusedStreamTruncatesOldTail
    ///
    /// <summary>
    /// 既存データより短い TAR を保存したとき、古い末尾を残さないこと。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void SaveTarToReusedStreamTruncatesOldTail()
    {
        const int originalLength = 2 * 1024 * 1024;
        using var destination = new MemoryStream();
        destination.Write(new byte[originalLength]);
        destination.Position = 0;

        using (var writer = new ArchiveWriter(Format.Tar))
        {
            writer.Add(GetSource("Sample.txt"), "sample.txt");
            writer.Save(destination);
        }

        Assert.That(destination.Length, Is.LessThan(originalLength));

        destination.Position = 0;
        using var reader = new ArchiveReader(destination);
        Assert.That(reader.Format, Is.EqualTo(Format.Tar));
        Assert.That(reader.Items.Select(e => e.FullName), Does.Contain("sample.txt"));
    }

    /* --------------------------------------------------------------------- */
    ///
    /// CopyVolumeExactlyThrowsOnPrematureEnd
    ///
    /// <summary>
    /// 分割元が想定より早く終端に達した場合に無限ループしないこと。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void CopyVolumeExactlyThrowsOnPrematureEnd()
    {
        using var source = new MemoryStream();
        using var destination = new MemoryStream();

        Assert.That(() => ArchiveWriter.CopyVolumeExactly(source, destination, new byte[16], 1),
            Throws.TypeOf<EndOfStreamException>());
    }
}
