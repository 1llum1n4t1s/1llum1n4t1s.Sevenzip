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
namespace Cube.FileSystem.SevenZip.Tests;

/// <summary>
/// Io.Move の衝突時の動作を検証する。
/// </summary>
[TestFixture]
internal class IoMoveTest : FileFixture
{
    [Test]
    public void MoveWithoutOverwriteThrowsAndPreservesBothFiles()
    {
        var src  = Get(nameof(MoveWithoutOverwriteThrowsAndPreservesBothFiles), "src.txt");
        var dest = Get(nameof(MoveWithoutOverwriteThrowsAndPreservesBothFiles), "dest.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(src)!);
        File.WriteAllText(src, "source");
        File.WriteAllText(dest, "destination");

        Assert.Throws<IOException>(() => Io.Move(src, dest, false));

        Assert.That(File.ReadAllText(src), Is.EqualTo("source"));
        Assert.That(File.ReadAllText(dest), Is.EqualTo("destination"));
    }

    [Test]
    public void RecursiveMoveWithoutOverwritePreservesSourceOnCollision()
    {
        var root = Get(nameof(RecursiveMoveWithoutOverwritePreservesSourceOnCollision));
        var src  = Path.Combine(root, "src");
        var dest = Path.Combine(root, "dest");
        Directory.CreateDirectory(src);
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(src, "collision.txt"), "source");
        File.WriteAllText(Path.Combine(dest, "collision.txt"), "destination");

        Assert.Throws<IOException>(() => Io.Move(src, dest, false));

        Assert.That(Directory.Exists(src), Is.True);
        Assert.That(File.ReadAllText(Path.Combine(src, "collision.txt")), Is.EqualTo("source"));
        Assert.That(File.ReadAllText(Path.Combine(dest, "collision.txt")), Is.EqualTo("destination"));
    }

    [Test]
    public void MoveWithOverwriteKeepsBackupOnDestinationVolume()
    {
        var root = Get(nameof(MoveWithOverwriteKeepsBackupOnDestinationVolume));
        var src  = Path.Combine(root, "src.txt");
        var dest = Path.Combine(root, "dest.txt");
        Directory.CreateDirectory(root);
        File.WriteAllText(src, "source");
        File.WriteAllText(dest, "destination");

        var controller = new RecordingIoController();
        Io.Configure(controller);
        Io.Move(src, dest, true);

        Assert.That(File.Exists(src), Is.False);
        Assert.That(File.ReadAllText(dest), Is.EqualTo("source"));
        Assert.That(controller.Moves, Has.Count.EqualTo(2));
        Assert.That(Path.GetFullPath(controller.Moves[0].Source), Is.EqualTo(Path.GetFullPath(dest)));
        Assert.That(
            Path.GetDirectoryName(Path.GetFullPath(controller.Moves[0].Destination)),
            Is.EqualTo(Path.GetDirectoryName(Path.GetFullPath(dest)))
        );
    }

    private sealed class RecordingIoController : IoController
    {
        public List<(string Source, string Destination)> Moves { get; } = [];

        public override void Move(string src, string dest)
        {
            Moves.Add((src, dest));
            base.Move(src, dest);
        }
    }
}
