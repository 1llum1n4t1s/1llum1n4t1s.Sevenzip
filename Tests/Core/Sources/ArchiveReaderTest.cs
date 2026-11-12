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
    /// <param name="filename">対象アーカイブ名。</param>
    /// <param name="password">パスワード。不要な場合は空文字。</param>
    /// <param name="codePage">
    /// エントリ名のデコードに使うコードページ。
    /// 既定の <see cref="CodePage.Oem"/> は「OS の非 Unicode プログラム用コードページに従う」を
    /// 意味するため、UTF-8 フラグ (bit11) の立っていない非 ASCII 名を含むアーカイブでは
    /// 実行環境のロケールによって結果が変わる。そのようなアーカイブは実体のエンコーディングを
    /// 明示指定してロケール非依存にする (TestCases のコメント参照)。
    /// </param>
    ///
    /* --------------------------------------------------------------------- */
    [TestCaseSource(nameof(TestCases))]
    public void Extract(string filename, string password, CodePage codePage = CodePage.Oem) => RunPasswordCase(password, () =>
    {
        var src  = GetSource(filename);
        var dest = Get(nameof(Extract), filename);

        using var archive = new ArchiveReader(src, password, new() { CodePage = codePage });
        archive.Save(dest);

        foreach (var cmp in GetAnswer(filename))
        {
            var fi = FindEntity(dest, cmp.Key);

            Assert.That(fi.Exists,         Is.True, cmp.Key);
            Assert.That(fi.Length,         Is.EqualTo(cmp.Value.Length), cmp.Key);
            Assert.That(fi.CreationTime,   Is.Not.EqualTo(DateTime.MinValue), cmp.Key);
            Assert.That(fi.LastWriteTime,  Is.Not.EqualTo(DateTime.MinValue), cmp.Key);
            Assert.That(fi.LastAccessTime, Is.Not.EqualTo(DateTime.MinValue), cmp.Key);

            // 期待 CSV の CRC は書庫に記録された CRC (7-Zip の kpidCrc)。0 はディレクトリまたは
            // CRC 未記録 (TAR 等) を表すため比較しない。長さが一致しても内容が壊れている
            // 展開を検出する。LHA / LZH は書庫形式として CRC-16 を記録するため CRC-16 で比較する。
            if (cmp.Value.Crc != 0 && !fi.IsDirectory)
            {
                var lha = filename.EndsWith(".lzh", StringComparison.OrdinalIgnoreCase) ||
                          filename.EndsWith(".lha", StringComparison.OrdinalIgnoreCase);
                var actual = lha ? Crc16(fi.FullName) : Crc32(fi.FullName);
                Assert.That(actual, Is.EqualTo(cmp.Value.Crc), $"CRC mismatch: {cmp.Key}");
            }
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
    /// <param name="filename">対象アーカイブ名。</param>
    /// <param name="password">パスワード。不要な場合は空文字。</param>
    /// <param name="codePage">
    /// エントリ名のデコードに使うコードページ。<see cref="TestCases"/> を
    /// <see cref="Extract"/> と共有しているため同じシグネチャを持つ。
    /// </param>
    ///
    /* --------------------------------------------------------------------- */
    [TestCaseSource(nameof(TestCases))]
    public void Test(string filename, string password, CodePage codePage = CodePage.Oem) => RunPasswordCase(password, () => {
        var src = GetSource(filename);
        using var archive = new ArchiveReader(src, password, new() { CodePage = codePage });
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
    /// <remarks>
    /// 書庫の生エントリ名は "..\..\(中略)..\Temp\evil.txt" で、漏洩した場合の書き込み先は
    /// destDir と同じドライブの "\Temp\evil.txt" になる。destDir 内を列挙するだけでは
    /// 外部への漏洩を検出できないため、展開前後で (1) このフィクスチャの Results 全体
    /// (destDir の親を含む) と (2) 漏洩先の絶対パスの状態を比較する。
    /// </remarks>
    ///
    /* --------------------------------------------------------------------- */
    [TestCase("ZipSlipWin.zip")]
    [TestCase("ZipSlipWin.tar")]
    public void Extract_ZipSlipWin(string filename)
    {
        var src  = GetSource(filename);
        var dest = Get(nameof(Extract_ZipSlipWin), filename);
        var destFull = Path.GetFullPath(dest);
        var escaped  = Path.Combine(Path.GetPathRoot(destFull), "Temp", "evil.txt");

        // destDir とその祖先は展開で作られる正当なエントリなので、比較前に作っておく
        Directory.CreateDirectory(destFull);
        var outsideBefore = SnapshotOutside(Results, destFull);
        var escapedBefore = Stat(escaped);

        using (var archive = new ArchiveReader(src, ""))
        {
            archive.Save(dest);
        }

        // destDir 外 (親ディレクトリ以上) に新規・変更エントリが無いこと
        var outsideAfter = SnapshotOutside(Results, destFull);
        Assert.That(outsideAfter.Except(outsideBefore), Is.Empty,
            $"Entries created or modified outside destDir for {filename}");
        Assert.That(Stat(escaped), Is.EqualTo(escapedBefore),
            $"Zip-slip target was created or modified: {escaped}");

        // 全てのエントリが destDir 配下に収まっていること (zip-slip 防止の本質的検証)
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

    /* --------------------------------------------------------------------- */
    ///
    /// Items_ExplicitCodePage_OverridesHostCodePage
    ///
    /// <summary>
    /// UTF-8 フラグ (bit11) の立っていないアーカイブについて、明示指定した
    /// <see cref="CodePage"/> がエントリ名のデコードを支配していることを確認する。
    /// </summary>
    ///
    /// <remarks>
    /// <para>
    /// これは「ロケール非依存になったこと」の機械的な根拠となる回帰テストである。
    /// 正しい CodePage で期待名が得られ、かつ<b>誤った CodePage では得られない</b>ことを
    /// 両方確認することで、デコードが OS の非 Unicode プログラム用コードページではなく
    /// 指定値によって決まっていると言える (指定が無視されていれば、どちらの指定でも
    /// ホストのコードページに従った同一の結果になるはずである)。
    /// </para>
    /// <para>
    /// 各アーカイブの実体エンコーディングは生バイト列から判定した。
    /// SampleComma.zip / SampleKanji.zip は cp932 でのみ、SampleMac.zip は utf-8 でのみ
    /// デコード可能である。
    /// </para>
    /// </remarks>
    ///
    /* --------------------------------------------------------------------- */
    [TestCase("SampleComma.zip", CodePage.Japanese, CodePage.Utf8,     "カンマ")]
    [TestCase("SampleKanji.zip", CodePage.Japanese, CodePage.Utf8,     "似.txt")]
    [TestCase("SampleMac.zip",   CodePage.Utf8,     CodePage.Japanese, "名称未設定フォルダ")]
    public void Items_ExplicitCodePage_OverridesHostCodePage(
        string filename, CodePage correct, CodePage wrong, string expected)
    {
        var src = GetSource(filename);

        // 正しい CodePage では期待名が得られる（NFD 格納の SampleMac に備えて正規化して比較）
        using (var reader = new ArchiveReader(src, "", new() { CodePage = correct }))
        {
            var names = reader.Items.Select(e => e.FullName.Normalize(NormalizationForm.FormC));
            Assert.That(names.Any(e => e.Contains(expected)), Is.True,
                $"{filename}: CodePage.{correct} で '{expected}' が得られること");
        }

        // 誤った CodePage では得られない = 指定が実際にデコードを制御している
        using (var reader = new ArchiveReader(src, "", new() { CodePage = wrong }))
        {
            var names = reader.Items.Select(e => e.FullName.Normalize(NormalizationForm.FormC));
            Assert.That(names.Any(e => e.Contains(expected)), Is.False,
                $"{filename}: CodePage.{wrong} では '{expected}' が得られないこと " +
                "(得られる場合は CodePage 指定が無視されており、ホストのロケール依存が残る)");
        }
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
            // 以下 3 件は UTF-8 フラグ (bit11) が立っていない非 ASCII 名を含むため、
            // CodePage を明示しないと OS の非 Unicode プログラム用コードページ依存になる
            // (ja-JP では通るが en-US runner では文字化けして期待パスが見つからない)。
            // 実体のエンコーディングは各アーカイブの生バイト列から判定した:
            //   SampleComma.zip / SampleKanji.zip … cp932 でのみデコード可 → Japanese
            //   SampleMac.zip                     … utf-8 でのみデコード可 (NFD) → Utf8
            yield return new TestCaseData("SampleComma.zip", "", CodePage.Japanese);
            yield return new TestCaseData("SampleMac.zip", "", CodePage.Utf8);
            // SampleUtf8.zip は非 ASCII エントリに bit11 が立っているので指定不要。
            yield return new TestCaseData("SampleUtf8.zip", "");
            yield return new TestCaseData("SampleKanji.zip", "", CodePage.Japanese);
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
    /// SnapshotOutside
    ///
    /// <summary>
    /// 指定ディレクトリ配下のうち、除外ディレクトリ外の全エントリの
    /// ファイルは「パス|長さ|更新日時」、ディレクトリはパスとして列挙する。
    /// </summary>
    ///
    /// <remarks>
    /// 再解析ポイントは辿らない (他テストが残した junction 経由で外部を走査しない)。
    /// </remarks>
    ///
    /* --------------------------------------------------------------------- */
    private static HashSet<string> SnapshotOutside(string root, string exclude)
    {
        var prefix  = exclude.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip      = FileAttributes.ReparsePoint,
            IgnoreInaccessible    = true,
        };
        return new DirectoryInfo(root)
            .EnumerateFileSystemInfos("*", options)
            .Where(e => !e.FullName.Equals(exclude, StringComparison.OrdinalIgnoreCase) &&
                        !e.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            // ディレクトリは存在だけを比較する。列挙で得るディレクトリの更新日時は親の
            // インデックス由来で遅延更新されるため、直前の他テストの書き込みで揺れる。
            .Select(e => e is FileInfo f ? $"{f.FullName}|{f.Length}|{f.LastWriteTimeUtc.Ticks}" : e.FullName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /* --------------------------------------------------------------------- */
    ///
    /// Stat
    ///
    /// <summary>
    /// ファイルの有無・長さ・更新日時を比較用の文字列で返す。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    private static string Stat(string path)
    {
        var fi = new FileInfo(path);
        return fi.Exists ? $"{fi.Length}|{fi.LastWriteTimeUtc.Ticks}" : "(none)";
    }

    /* --------------------------------------------------------------------- */
    ///
    /// Crc32
    ///
    /// <summary>
    /// ファイル内容の CRC-32 (IEEE 802.3、ZIP / 7z と同じ反転多項式 0xEDB88320) を計算する。
    /// </summary>
    ///
    /// <remarks>
    /// System.IO.Hashing はパッケージ追加 (packages.lock.json の変更) を伴うため使わず、
    /// テーブル方式で自前実装する。
    /// </remarks>
    ///
    /* --------------------------------------------------------------------- */
    private static uint Crc32(string path)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in File.ReadAllBytes(path)) crc = s_crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return ~crc;
    }

    /* --------------------------------------------------------------------- */
    ///
    /// Crc16
    ///
    /// <summary>
    /// ファイル内容の CRC-16/ARC (LHA が記録する CRC、反転多項式 0xA001) を計算する。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    private static uint Crc16(string path)
    {
        var crc = 0u;
        foreach (var b in File.ReadAllBytes(path))
        {
            crc ^= b;
            for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xA001u ^ (crc >> 1) : crc >> 1;
        }
        return crc;
    }

    private static readonly uint[] s_crcTable =Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    /* --------------------------------------------------------------------- */
    ///
    /// RunPasswordCase
    ///
    /// <summary>
    /// 非 ASCII (CJK 等) のパスワードを使うケースだけを <see cref="IgnoreCultureError"/> で
    /// 包んで実行し、それ以外はそのまま実行する。
    /// </summary>
    ///
    /// <remarks>
    /// ASCII パスワードや無パスワードのケースで EncryptionException が出るのは
    /// ロケールと無関係な不具合なので、非 ja-JP 環境でも Ignore せず失敗させる。
    /// </remarks>
    ///
    /* --------------------------------------------------------------------- */
    private void RunPasswordCase(string password, Action action, string message)
    {
        if (password is not null && password.Any(c => c > 0x7F)) IgnoreCultureError(action, message);
        else action();
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
    /// 非 ASCII パスワード (PasswordJapanese01/02 等) は、書庫作成時のパスワード
    /// エンコーディングが実行環境のコードページに依存するため、非 ja-JP 環境で
    /// EncryptionException になる場合に限り無視する。呼び出しは
    /// <see cref="RunPasswordCase"/> 経由とし、ロケール依存を隠す用途に使わない。
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
