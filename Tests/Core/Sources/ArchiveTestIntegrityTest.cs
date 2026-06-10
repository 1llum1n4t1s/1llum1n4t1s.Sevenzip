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
/// ArchiveTestIntegrityTest
///
/// <summary>
/// Test() (テストモード展開) が暗号化書庫の破損を検知できることを検証する。
/// 利用側 (Lhamiel 等) は展開後の CRC 検証を reader.Test() に依存しており、
/// 「正しいパスワード + 破損データ → 例外」が成立しないと検証が素通りする。
/// あわせて Dispose 後の Items アクセスが明示的な ObjectDisposedException に
/// なること (NullReferenceException への退化防止) も検証する。
/// </summary>
///
/* ------------------------------------------------------------------------- */
[TestFixture]
internal class ArchiveTestIntegrityTest : FileFixture
{
    #region Tests

    /* --------------------------------------------------------------------- */
    ///
    /// Test_CorruptedEncrypted_WithCorrectPassword_Throws
    ///
    /// <summary>
    /// パスワード付き書庫 (通常 / ヘッダ暗号化) の packed data を 1 バイト
    /// 破損させたとき、正しいパスワードの Test() が例外で検知することを
    /// 確認する。7z レイアウトは [32B 署名ヘッダ][packed streams][ヘッダ] の
    /// 順なので、offset 36 は確実に packed data 領域に入る (末尾ヘッダは
    /// 無傷のため open 自体は成功し、失敗が Test() で表面化する)。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [TestCase("Password.7z")]
    [TestCase("PasswordHeader.7z")]
    public void Test_CorruptedEncrypted_WithCorrectPassword_Throws(string filename)
    {
        var src = Get($"Corrupted_{filename}");
        File.Copy(GetSource(filename), src, overwrite: true);

        // 署名ヘッダ (32 bytes) 直後の packed data を 1 バイト反転させる
        using (var fs = new FileStream(src, FileMode.Open, FileAccess.ReadWrite))
        {
            fs.Position = 36;
            var b = fs.ReadByte();
            fs.Position = 36;
            fs.WriteByte((byte)(b ^ 0xFF));
        }

        using var reader = new ArchiveReader(src, "password");
        Assert.That(reader.Items.Count, Is.GreaterThan(0)); // open は成功 (ヘッダ無傷)
        Assert.That(() => reader.Test(), Throws.Exception);
    }

    /* --------------------------------------------------------------------- */
    ///
    /// Test_Intact_WithCorrectPassword_Succeeds
    ///
    /// <summary>
    /// 対照ケース: 無傷の書庫では同じ経路の Test() が例外を投げないことを
    /// 確認する (破損テストが「常に例外」で空振りしていないことの担保)。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [TestCase("Password.7z")]
    [TestCase("PasswordHeader.7z")]
    public void Test_Intact_WithCorrectPassword_Succeeds(string filename)
    {
        var src = Get($"Intact_{filename}");
        File.Copy(GetSource(filename), src, overwrite: true);

        using var reader = new ArchiveReader(src, "password");
        Assert.That(() => reader.Test(), Throws.Nothing);
    }

    /* --------------------------------------------------------------------- */
    ///
    /// Items_AfterDispose_ThrowsObjectDisposedException
    ///
    /// <summary>
    /// Dispose 後に Items へアクセスしたとき、明示的な
    /// ObjectDisposedException が投げられることを確認する。
    /// ガード追加前は _cache ??= が再生成 → null の _core への COM 呼び出しで
    /// NullReferenceException になり原因が分かりにくかった。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void Items_AfterDispose_ThrowsObjectDisposedException()
    {
        var src = Get("DisposedItems.7z");
        File.Copy(GetSource("Password.7z"), src, overwrite: true);

        var reader = new ArchiveReader(src, "password");
        var items = reader.Items;
        Assert.That(items.Count, Is.GreaterThan(0));
        reader.Dispose();

        Assert.That(() => _ = items[0], Throws.InstanceOf<ObjectDisposedException>());
    }

    #endregion
}
