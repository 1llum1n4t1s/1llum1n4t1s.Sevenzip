/* ------------------------------------------------------------------------- */
//
// Copyright (c) 2010 CubeSoft, Inc.
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as
// published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.
//
/* ------------------------------------------------------------------------- */
using NUnit.Framework;
using System;

namespace Cube.FileSystem.SevenZip.Tests;

/// <summary>
/// SevenZipLibrary.Lease (借用ハンドル) の参照カウント挙動をテストする。
/// </summary>
/// <remarks>
/// 旧実装は参照カウントが static で、借用側に返却済みフラグが無かったため
/// (1) Dispose のたびにカウントが減る (2) finalizer 経路で世代が差し替わった後の
/// 旧借用の返却が新世代のカウントを減らす、という 2 つの破綻があった。
/// </remarks>
[TestFixture]
class SevenZipLibraryLeaseTest
{
    /// <summary>
    /// Lease.Dispose が冪等であることを確認する。
    /// </summary>
    /// <remarks>
    /// 二重 Dispose でカウントが余分に減ると、まだ借用中の別インスタンスの
    /// 使用中に DLL がアンロードされる。lease2 が生きたまま COM オブジェクトを
    /// 生成できることで、カウントが壊れていないことを検証する。
    /// </remarks>
    [Test]
    public void Dispose_Twice_IsIdempotent()
    {
        var lease1 = SevenZipLibrary.Acquire();
        using var lease2 = SevenZipLibrary.Acquire();

        lease1.Dispose();
        lease1.Dispose(); // 2 回目以降は何も起こらない (旧実装ではここでカウントが 0 になる)

        // lease2 の借用が生きているので DLL はロードされたままで COM 生成に成功する
        var archive = lease2.GetInArchive(Format.Zip);
        Assert.That(archive, Is.Not.Null);
        lease2.ReleaseComWrapper(archive);
    }

    /// <summary>
    /// 返却済みの Lease を使うと ObjectDisposedException が送出されることを確認する。
    /// </summary>
    [Test]
    public void GetInArchive_AfterDispose_Throws()
    {
        var lease = SevenZipLibrary.Acquire();
        lease.Dispose();
        Assert.That(() => lease.GetInArchive(Format.Zip), Throws.TypeOf<ObjectDisposedException>());
    }

    /// <summary>
    /// finalizer 経路の返却で世代が差し替わった後、旧世代の Lease を返却しても
    /// 新世代の参照カウントに影響しないことを確認する。
    /// </summary>
    /// <remarks>
    /// 旧実装ではカウントが static だったため、old2 の返却が new1 のカウントを減らし、
    /// 使用中の新世代が共有インスタンスから外れて解放責任者を失っていた。
    /// </remarks>
    [Test]
    public void ReleaseFromFinalizer_DoesNotAffectNextGeneration()
    {
        // 旧世代を 2 つ借用し、片方を finalizer 経路で返却して世代を退役させる
        var old1 = SevenZipLibrary.Acquire();
        var old2 = SevenZipLibrary.Acquire();
        old1.ReleaseFromFinalizer();
        old2.ReleaseFromFinalizer(); // ここで旧世代のカウントが 0 → _shared から切り離される

        // 新世代を借用する
        using var new1 = SevenZipLibrary.Acquire();

        // 旧世代の残り借用を今さら返却しても (二重返却は冪等なので実質 no-op)、
        // 新世代のカウントは減らない
        old1.Dispose();
        old2.Dispose();

        var archive = new1.GetInArchive(Format.Zip);
        Assert.That(archive, Is.Not.Null);
        new1.ReleaseComWrapper(archive);
    }
}
