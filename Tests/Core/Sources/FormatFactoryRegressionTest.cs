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
using NUnit.Framework;
using System;
using System.IO;
namespace Cube.FileSystem.SevenZip.Tests;

[TestFixture]
internal class FormatFactoryRegressionTest
{
    [Test]
    public void FromStream_RecognizesEmptyZip()
    {
        byte[] bytes =
        [
            0x50, 0x4B, 0x05, 0x06,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00,
        ];
        using var stream = new MemoryStream(bytes);

        Assert.That(FormatFactory.From(stream), Is.EqualTo(Format.Zip));
        Assert.That(stream.Position, Is.Zero);
    }

    [Test]
    public void FromStream_RecognizesZipRecordsAndRestoresPosition()
    {
        byte[][] signatures =
        [
            [0x50, 0x4B, 0x06, 0x06],
            [0x50, 0x4B, 0x07, 0x08],
        ];

        foreach (var signature in signatures)
        {
            using var stream = new MemoryStream([0x00, 0x00, .. signature]);
            stream.Position = 2;

            Assert.That(FormatFactory.From(stream), Is.EqualTo(Format.Zip));
            Assert.That(stream.Position, Is.EqualTo(2));
        }
    }

    [Test]
    public void FromStream_DoesNotTreatArbitrary78PrefixAsDmg()
    {
        byte[][] samples =
        [
            [0x78],
            [0x78, 0x9C, 0x03, 0x00],
        ];

        foreach (var sample in samples)
        {
            using var stream = new MemoryStream(sample);

            Assert.That(FormatFactory.From(stream), Is.EqualTo(Format.Unknown));
            Assert.That(stream.Position, Is.Zero);
        }
    }

    [Test]
    public void FromStream_HandlesShortReads()
    {
        using (var zip = new OneByteReadStream(
            [0x00, 0x00, 0x50, 0x4B, 0x03, 0x04, 0x00, 0x00]))
        {
            zip.Position = 2;

            Assert.That(FormatFactory.From(zip), Is.EqualTo(Format.Zip));
            Assert.That(zip.Position, Is.EqualTo(2));
        }

        var tarBytes = new byte[0x106];
        "ustar"u8.CopyTo(tarBytes.AsSpan(0x101));
        using var tar = new OneByteReadStream(tarBytes);

        Assert.That(FormatFactory.From(tar), Is.EqualTo(Format.Tar));
        Assert.That(tar.Position, Is.Zero);
    }

    private sealed class OneByteReadStream(byte[] buffer) : MemoryStream(buffer)
    {
        public override int Read(byte[] buffer, int offset, int count) =>
            base.Read(buffer, offset, count > 1 ? 1 : count);

        public override int Read(Span<byte> buffer) =>
            base.Read(buffer[..(buffer.Length > 1 ? 1 : buffer.Length)]);
    }
}
