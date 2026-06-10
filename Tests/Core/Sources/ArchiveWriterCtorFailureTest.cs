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
using Cube.Tests.Fixtures;
using NUnit.Framework;
using System;
namespace Cube.FileSystem.SevenZip.Tests;

/* ------------------------------------------------------------------------- */
///
/// ArchiveWriterCtorFailureTest
///
/// <summary>
/// ArchiveWriter のコンストラクタ失敗時 (書き込み非対応フォーマットを指定した等)
/// のクリーンアップを検証する。
/// 旧実装は _lib をフィールド初期化子で取得しており、ctor 失敗時に解放せず
/// throw していたため、孤児化したインスタンスのライブラリ参照カウントが
/// GC までリークしていた (ArchiveReaderCtorFailureTest の Writer 版)。
/// </summary>
///
/* ------------------------------------------------------------------------- */
[TestFixture]
internal class ArchiveWriterCtorFailureTest : FileFixture
{
    #region Tests

    /* --------------------------------------------------------------------- */
    ///
    /// UnknownFormat_ThrowsUnknownFormatException
    ///
    /// <summary>
    /// Format.Unknown を指定したとき、リソース取得前に
    /// UnknownFormatException で fail-fast することを確認する
    /// (ArchiveReader と同じ早期検証)。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void UnknownFormat_ThrowsUnknownFormatException()
    {
        Assert.That(
            () => { using var w = new ArchiveWriter(Format.Unknown); },
            Throws.TypeOf<UnknownFormatException>()
        );
    }

    /* --------------------------------------------------------------------- */
    ///
    /// UnsupportedFormat_FailsFast
    ///
    /// <summary>
    /// 書き込み非対応フォーマット (Rar は 7-Zip では読み取り専用) を
    /// 指定したとき、Save() 時まで遅延せず ctor で失敗することを確認する。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void UnsupportedFormat_FailsFast()
    {
        Assert.That(
            () => { using var w = new ArchiveWriter(Format.Rar); },
            Throws.Exception
        );
    }

    /* --------------------------------------------------------------------- */
    ///
    /// UnsupportedFormat_DoesNotCrashFinalizerThread
    ///
    /// <summary>
    /// ctor 失敗を繰り返した後に finalizer を強制実行しても、finalizer
    /// スレッドの未処理例外 (= プロセスクラッシュ) が起きないことを確認する。
    /// 修正後は ctor 内で Dispose + SuppressFinalize 済みのため、孤児の
    /// finalizer 自体が登録解除されている。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void UnsupportedFormat_DoesNotCrashFinalizerThread()
    {
        for (var i = 0; i < 5; i++)
        {
            Assert.That(
                () => { using var w = new ArchiveWriter(Format.Rar); },
                Throws.Exception
            );
        }

        // 孤児が残っていれば finalizer がここで走る。
        for (var i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        // ここまで到達 = finalizer スレッドが死んでいない
        Assert.That(true, Is.True);
    }

    /* --------------------------------------------------------------------- */
    ///
    /// SupportedFormat_StillWorks
    ///
    /// <summary>
    /// ctor 失敗のクリーンアップ追加後も、対応フォーマットでの圧縮が
    /// 引き続き機能することを確認する (回帰ガード)。直前の ctor 失敗で
    /// ライブラリ参照カウントが壊れていないことも兼ねて検証する。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void SupportedFormat_StillWorks()
    {
        // 直前の失敗 (孤児化ケース) の後でも正常に圧縮できる
        Assert.That(
            () => { using var w = new ArchiveWriter(Format.Rar); },
            Throws.Exception
        );

        var dest = Get($"{nameof(SupportedFormat_StillWorks)}.zip");
        using (var writer = new ArchiveWriter(Format.Zip))
        {
            writer.Add(GetSource("Sample.txt"));
            writer.Save(dest);
        }

        using var reader = new ArchiveReader(dest);
        Assert.That(reader.Items.Count, Is.GreaterThan(0));
    }

    #endregion
}
