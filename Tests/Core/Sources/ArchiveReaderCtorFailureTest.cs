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
using System.IO;
namespace Cube.FileSystem.SevenZip.Tests;

/* ------------------------------------------------------------------------- */
///
/// ArchiveReaderCtorFailureTest
///
/// <summary>
/// ArchiveReader のコンストラクタ失敗時 (ヘッダ暗号化書庫をパスワード無しで
/// 開く等) のクリーンアップを検証する。
/// 旧実装は ctor 失敗時に取得済みリソースを解放せず throw していたため、
/// (1) FileStream・COM ラッパー・ライブラリ参照カウントが GC までリークし、
/// (2) 孤児化したインスタンスの finalizer (~DisposableBase → Dispose(false)) が
///     先に finalize 済みの ComObject (IInArchive) の Close() を呼んで
///     ObjectDisposedException → finalizer スレッド未処理例外 → プロセス
///     クラッシュを引き起こしていた (Lhamiel テストスイートで再現・実測)。
/// </summary>
///
/* ------------------------------------------------------------------------- */
[TestFixture]
internal class ArchiveReaderCtorFailureTest : FileFixture
{
    #region Tests

    /* --------------------------------------------------------------------- */
    ///
    /// OpenHeaderEncrypted_WithoutPassword_ReleasesFileHandle
    ///
    /// <summary>
    /// ヘッダ暗号化書庫をパスワード無しで開いて ctor が失敗したとき、
    /// 開いた FileStream が同期的に解放されることを確認する
    /// (旧実装は GC までファイルハンドルをリークしていた)。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void OpenHeaderEncrypted_WithoutPassword_ReleasesFileHandle()
    {
        // GetSource の共有資材を排他オープンすると他テストと競合するため作業用にコピー
        var src = Get("CtorFailureHandle.7z");
        File.Copy(GetSource("PasswordHeader.7z"), src, overwrite: true);

        Assert.That(() => { using var r = new ArchiveReader(src); }, Throws.Exception);

        // ctor 失敗直後に排他アクセスできる = ハンドルが解放されている
        Assert.That(() =>
        {
            using var fs = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.None);
        }, Throws.Nothing);
    }

    /* --------------------------------------------------------------------- */
    ///
    /// OpenHeaderEncrypted_WithoutPassword_DoesNotCrashFinalizerThread
    ///
    /// <summary>
    /// ctor 失敗を繰り返した後に finalizer を強制実行しても、finalizer
    /// スレッドの未処理例外 (= プロセスクラッシュ) が起きないことを確認する。
    /// 旧実装では本テストがプロセスごと落ちる。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void OpenHeaderEncrypted_WithoutPassword_DoesNotCrashFinalizerThread()
    {
        var src = Get("CtorFailureFinalizer.7z");
        File.Copy(GetSource("PasswordHeader.7z"), src, overwrite: true);

        for (var i = 0; i < 5; i++)
        {
            Assert.That(() => { using var r = new ArchiveReader(src); }, Throws.Exception);
        }

        // 孤児が残っていれば finalizer がここで走る。
        // 修正後は ctor 内で Dispose + SuppressFinalize 済みのため何も起きない。
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
    /// OpenHeaderEncrypted_WithPassword_StillWorks
    ///
    /// <summary>
    /// ctor 失敗のクリーンアップ追加後も、正しいパスワードでの読み取りが
    /// 引き続き機能することを確認する (回帰ガード)。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void OpenHeaderEncrypted_WithPassword_StillWorks()
    {
        var src = Get("CtorFailureWithPw.7z");
        File.Copy(GetSource("PasswordHeader.7z"), src, overwrite: true);

        // 直前の失敗 (孤児化ケース) の後でも正パスワードで開ける
        Assert.That(() => { using var r = new ArchiveReader(src); }, Throws.Exception);

        using var reader = new ArchiveReader(src, "password");
        Assert.That(reader.Items.Count, Is.GreaterThan(0));
    }

    #endregion
}
