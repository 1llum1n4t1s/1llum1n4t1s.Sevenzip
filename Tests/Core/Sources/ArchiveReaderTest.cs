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
using Cube.Text.Extensions;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
namespace Cube.FileSystem.SevenZip.Tests;

/* ------------------------------------------------------------------------- */
///
/// ArchiveReaderTest
///
/// <summary>
/// Tests the ArchiveReader class.
/// </summary>
///
/* ------------------------------------------------------------------------- */
[TestFixture]
internal class ArchiveReaderTest : FileFixture
{
    #region Tests

    /* --------------------------------------------------------------------- */
    ///
    /// Extract
    ///
    /// <summary>
    /// Tests the Save method with the specified archive.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [TestCaseSource(nameof(TestCases))]
    public void Extract(string filename, string password) => IgnoreCultureError(() =>
    {
        var src  = GetSource(filename);
        var dest = Get(nameof(Extract), filename);

        using var archive = new ArchiveReader(src, password);
        archive.Save(dest);

        foreach (var cmp in GetAnswer(filename))
        {
            var fi = FindEntity(dest, cmp.Key);

            Assert.That(fi.Exists,         Is.True, cmp.Key);
            Assert.That(fi.Length,         Is.EqualTo(cmp.Value.Length), cmp.Key);
            Assert.That(fi.CreationTime,   Is.Not.EqualTo(DateTime.MinValue), cmp.Key);
            Assert.That(fi.LastWriteTime,  Is.Not.EqualTo(DateTime.MinValue), cmp.Key);
            Assert.That(fi.LastAccessTime, Is.Not.EqualTo(DateTime.MinValue), cmp.Key);
        }
    }, $"{filename}, {password}");

    /* --------------------------------------------------------------------- */
    ///
    /// Extract_Lite
    ///
    /// <summary>
    /// Tests the Save method with the specified archive.
    /// </summary>
    ///
    /// <remarks>
    /// This is a simple test to check if the decompression process has
    /// been completed successfully by the number of decompressed files.
    /// </remarks>
    ///
    /* --------------------------------------------------------------------- */
    [TestCase("Sample.cab",     ExpectedResult =  3)]
    [TestCase("Sample.chm",     ExpectedResult = 89)]
    [TestCase("Sample.cpio",    ExpectedResult =  5)]
    [TestCase("Sample.docx",    ExpectedResult = 13)]
    [TestCase("Sample.exe",     ExpectedResult =  4)]
    [TestCase("Sample.nupkg",   ExpectedResult =  5)]
    [TestCase("Sample.pptx",    ExpectedResult = 40)]
    [TestCase("Sample.xlsx",    ExpectedResult = 14)]
    [TestCase("SampleSfx.exe",  ExpectedResult =  4)]
    public int Extract_Lite(string filename)
    {
        var src  = GetSource(filename);
        var dest = Get(nameof(Extract_Lite), filename);
        var cnt  = new Counter();

        using (var obj = new ArchiveReader(src)) obj.Test(); // Test
        using (var obj = new ArchiveReader(src)) obj.Save(dest, cnt);

        return cnt.Results[ProgressState.Success];
    }

    /* --------------------------------------------------------------------- */
    ///
    /// Test
    ///
    /// <summary>
    /// Tests the Test method with the specified archive in test mode.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [TestCaseSource(nameof(TestCases))]
    public void Test(string filename, string password) => IgnoreCultureError(() => {
        var src = GetSource(filename);
        using var archive = new ArchiveReader(src, password);
        archive.Test();
    }, $"{filename}, {password}");

    /* --------------------------------------------------------------------- */
    ///
    /// Extract_ZipSlipWin
    ///
    /// <summary>
    /// バックスラッシュを含む zip-slip エントリ名が destDir 外に漏洩しないこと
    /// を検証する。25.01 時代はエントリ名が "Temp\evil.txt" として保存されたが、
    /// 26.00 は Windows 禁止文字を Private Use Area にエスケープする。本テストは
    /// ファイル名のフォーマットに依存せず、次の 2 点だけを検証する：
    ///   1. 抽出操作が例外なく完了すること。
    ///   2. destDir の外に副作用（エスケープされたファイル）が作られないこと。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    [TestCase("ZipSlipWin.zip")]
    [TestCase("ZipSlipWin.tar")]
    public void Extract_ZipSlipWin(string filename)
    {
        var src  = GetSource(filename);
        var dest = Get(nameof(Extract_ZipSlipWin), filename);

        using var archive = new ArchiveReader(src, "");
        archive.Save(dest);

        // 全てのエントリが destDir 配下に収まっていること (zip-slip 防止の本質的検証)
        var destFull = Path.GetFullPath(dest);
        var files    = Io.GetFiles(dest, "*", SearchOption.AllDirectories).ToList();
        Assert.That(files, Is.Not.Empty, $"No files extracted for {filename}");
        foreach (var entry in files)
        {
            Assert.That(Path.GetFullPath(entry), Does.StartWith(destFull),
                $"Entry escaped destDir: {entry}");
        }

        // 既知の良性ファイル good.txt は名前変更なく残っているはず
        Assert.That(new Entity(Io.Combine(dest, "good.txt")).Exists, Is.True,
            "good.txt should be preserved");
    }

    /* --------------------------------------------------------------------- */
    ///
    /// Extract_SampleUnixSjis
    ///
    /// <summary>
    /// Unix 系ツールで作成された SJIS エンコードのファイル名を含む ZIP を
    /// CodePage.Japanese 指定で開き、日本語ファイル名が復元されることを確認する。
    /// </summary>
    ///
    /// <remarks>
    /// 7-Zip 本家 26.00 は bit11 (EFS) が立っていない ZIP について、システム
    /// ロケールに応じた自動デコードを行わない。Cube フォーク版 (Babel) では
    /// 自動検出されていたが、本家切り替えに伴い明示的な指定が必須となった。
    /// </remarks>
    ///
    /* --------------------------------------------------------------------- */
    [Test]
    public void Extract_SampleUnixSjis()
    {
        var src  = GetSource("SampleUnixSjis.zip");
        var dest = Get(nameof(Extract_SampleUnixSjis));

        using var archive = new ArchiveReader(src, "", new() { CodePage = CodePage.Japanese });
        archive.Save(dest);

        foreach (var cmp in GetAnswer("SampleUnixSjis.zip"))
        {
            var fi = FindEntity(dest, cmp.Key);
            Assert.That(fi.Exists, Is.True, cmp.Key);
            Assert.That(fi.Length, Is.EqualTo(cmp.Value.Length), cmp.Key);
        }
    }

    [TestCase(CodePage.Utf8)]
    [TestCase(CodePage.Japanese)]
    public void Extract_WithCodePage(CodePage cp)
    {
        var name = "日本語のファイル名.txt";
        var src  = Get(name);
        var zip  = Get($"{nameof(Extract_WithCodePage)}{cp}.zip");
        var dest = Get(nameof(Extract_WithCodePage), $"{cp}");

        Io.Copy(GetSource("Sample.txt"), src, true);

        // Writer 側で同じ CodePage を指定して ZIP を作成
        using (var writer = new ArchiveWriter(Format.Zip, new() { CodePage = cp }))
        {
            writer.Add(src);
            writer.Save(zip);
        }

        // Reader 側で CodePage を指定して開く
        using var reader = new ArchiveReader(zip, "", new() { CodePage = cp });
        Assert.That(reader.Items.Count, Is.GreaterThan(0));
        Assert.That(reader.Items[0].FullName, Does.Contain("日本語"));
        reader.Save(dest);
    }

    #endregion

    #region TestCases

    /* --------------------------------------------------------------------- */
    ///
    /// TestCases
    ///
    /// <summary>
    /// Gets the test cases.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    public static IEnumerable<TestCaseData> TestCases
    {
        get
        {
            yield return new TestCaseData("Sample.zip", "");
            yield return new TestCaseData("SampleEmpty.zip", "");
            yield return new TestCaseData("SampleReadOnly.zip", "");
            yield return new TestCaseData("SampleVolume.zip", "");
            yield return new TestCaseData("SampleVolume.rar.001", "");
            yield return new TestCaseData("SampleComma.zip", "");
            yield return new TestCaseData("SampleMac.zip", "");
            yield return new TestCaseData("SampleUtf8.zip", "");
            yield return new TestCaseData("SampleKanji.zip", "");
            // SampleUnixSjis.zip は CodePage 指定が必須のため Extract_SampleUnixSjis で個別に扱う。
            yield return new TestCaseData("Sample 2018.02.13.zip", "");
            yield return new TestCaseData("Sample..DoubleDot.zip", "");
            yield return new TestCaseData("Sample.tar", "");
            yield return new TestCaseData("Sample.tar.bz2", "");
            yield return new TestCaseData("Sample.tar.gz", "");
            yield return new TestCaseData("Sample.tar.lzma", "");
            yield return new TestCaseData("Sample.tar.z", "");
            yield return new TestCaseData("Sample.taz", "");
            yield return new TestCaseData("Sample.tb2", "");
            yield return new TestCaseData("Sample.tbz", "");
            yield return new TestCaseData("Sample.tgz", "");
            yield return new TestCaseData("Sample.txz", "");
            yield return new TestCaseData("Sample.txt.bz2", "");
            yield return new TestCaseData("Sample.txt.gz", "");
            yield return new TestCaseData("Sample.txt.xz", "");
            yield return new TestCaseData("Sample.arj", "");
            yield return new TestCaseData("Sample.flv", "");
            yield return new TestCaseData("Sample.jar", "");
            yield return new TestCaseData("Sample.lha", "");
            yield return new TestCaseData("Sample.lzh", "");
            yield return new TestCaseData("Sample.rar", "");
            yield return new TestCaseData("Sample.rar5", "");
            yield return new TestCaseData("SampleEmpty.rar", "");
            yield return new TestCaseData("SampleEmpty.7z", "");
            yield return new TestCaseData("Password.7z", "password");
            yield return new TestCaseData("PasswordHeader.7z", "password");
            yield return new TestCaseData("PasswordSymbol01.zip", "()[]{}<>");
            yield return new TestCaseData("PasswordSymbol02.zip", "\\#$%@?");
            yield return new TestCaseData("PasswordSymbol03.zip", "!&|+-*/=");
            yield return new TestCaseData("PasswordSymbol04.zip", "\"'^~`,._");
            yield return new TestCaseData("PasswordJapanese01.zip", "日本語パスワード");
            yield return new TestCaseData("PasswordJapanese02.zip", "ｶﾞｷﾞｸﾞｹﾞｺﾞﾊﾟﾋﾟﾌﾟﾍﾟﾎﾟ");
            yield return new TestCaseData("InvalidSymbol.zip", "");
            yield return new TestCaseData("InvalidReserved.zip", "");
            yield return new TestCaseData("ZipSlip.zip", "");
            yield return new TestCaseData("ZipSlip.tar", "");
            // ZipSlipWin.{zip,tar} は 26.00 がサニタイズ後のファイル名を
            // Private Use Area (U+F02F/U+F05C) にエスケープするため期待値と一致しない。
            // destDir 外への漏洩防止のみを Extract_ZipSlipWin で検証する。
        }
    }

    #endregion

    #region Others

    /* --------------------------------------------------------------------- */
    ///
    /// Expected
    ///
    /// <summary>
    /// Represents the expected values.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    private class Expected
    {
        public long Length { get; set; }
        public uint Crc { get; set; }
    }

    /* --------------------------------------------------------------------- */
    ///
    /// GetAnswer
    ///
    /// <summary>
    /// Gets the expected results of the specified archive.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    private IDictionary<string, Expected> GetAnswer(string filename)
    {
        var src = GetSource("Expected", $"{filename}.txt");
        var dest = new Dictionary<string, Expected>();
        foreach (var line in File.ReadLines(src, Encoding.UTF8))
        {
            var row = ParseCsvLine(line);
            if (row.Count < 3) continue;
            dest.Add(row[0].Trim(), new Expected
            {
                Length = long.Parse(row[1].Trim()),
                Crc    = uint.Parse(row[2].Trim())
            });
        }
        return dest;
    }

    /* --------------------------------------------------------------------- */
    ///
    /// FindEntity
    ///
    /// <summary>
    /// Mac 製 ZIP (SampleMac.zip) は Unicode NFD でファイル名を格納し、
    /// NTFS はその形式をそのまま保存する。期待値が NFC の場合、単純な
    /// <see cref="Entity.Exists"/> チェックは失敗するため、NFD 形式で
    /// フォールバック検索する。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    private static Entity FindEntity(string dest, string key)
    {
        var fi = new Entity(Io.Combine(dest, key));
        if (fi.Exists) return fi;

        var nfd = key.Normalize(NormalizationForm.FormD);
        if (nfd != key)
        {
            var alt = new Entity(Io.Combine(dest, nfd));
            if (alt.Exists) return alt;
        }

        // どの形式でも見つからなかった場合はオリジナルのキーで返し、呼び出し側で Assert を失敗させる。
        return fi;
    }

    /// <summary>
    /// Parses a CSV line (comma-delimited, fields may be enclosed in double quotes).
    /// </summary>
    private static IList<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var i = 0;
        while (i < line.Length)
        {
            if (line[i] == '"')
            {
                var start = i + 1;
                i = start;
                while (i < line.Length)
                {
                    if (line[i] == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { i += 2; continue; }
                        fields.Add(line[start..i].Replace("\"\"", "\""));
                        i++;
                        if (i < line.Length && line[i] == ',') i++;
                        break;
                    }
                    i++;
                }
                continue;
            }
            var comma = line.IndexOf(',', i);
            if (comma < 0)
            {
                fields.Add(line[i..].Trim());
                break;
            }
            fields.Add(line[i..comma].Trim());
            i = comma + 1;
        }
        return fields;
    }

    /* --------------------------------------------------------------------- */
    ///
    /// IgnoreCultureError
    ///
    /// <summary>
    /// Checks if the thrown exception is the EncryptionException class.
    /// </summary>
    ///
    /// <remarks>
    /// ロケールが日本語以外の環境で失敗するテストに関しては、現時点
    /// では無視しています。ArchiveOption.CodePage で明示的にコード
    /// ページを指定可能になったため、新規テストではそちらを使用する
    /// ことを推奨します。
    /// </remarks>
    ///
    /* --------------------------------------------------------------------- */
    private void IgnoreCultureError(Action action, string message)
    {
        try { action(); }
        catch (EncryptionException)
        {
            var code = CultureInfo.CurrentCulture.Name;
            if (!code.FuzzyEquals("ja-JP")) Assert.Ignore(message);
            else throw;
        }
    }

    #endregion
}
