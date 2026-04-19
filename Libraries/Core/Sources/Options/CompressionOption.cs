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
using System.Collections.Generic;
namespace Cube.FileSystem.SevenZip;

/* ------------------------------------------------------------------------- */
///
/// CompressionOption
///
/// <summary>
/// Represents options when creating a new archive.
/// Some formats may support only some of these options.
/// </summary>
///
/* ------------------------------------------------------------------------- */
public class CompressionOption : ArchiveOption
{
    /* --------------------------------------------------------------------- */
    ///
    /// CompressionLevel
    ///
    /// <summary>
    /// Gets or sets the compression level.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    public CompressionLevel CompressionLevel { get; init; } = CompressionLevel.Normal;

    /* --------------------------------------------------------------------- */
    ///
    /// CompressionMethod
    ///
    /// <summary>
    /// Gets or sets the compression method.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    public CompressionMethod CompressionMethod { get; init; } = CompressionMethod.Default;

    /* --------------------------------------------------------------------- */
    ///
    /// EncryptionMethod
    ///
    /// <summary>
    /// Gets or sets the encryption method.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    public EncryptionMethod EncryptionMethod { get; init; } = EncryptionMethod.Default;

    /* --------------------------------------------------------------------- */
    ///
    /// Password
    ///
    /// <summary>
    /// 作成するアーカイブの暗号化パスワードを取得する。
    /// </summary>
    /// <remarks>
    /// <b>メモリ保持の注意</b>: このプロパティは <c>init</c> 専用の <see cref="string"/>
    /// なので <see cref="ArchiveWriter"/> が長寿命オブジェクトとして保持される場合は
    /// パスワードが GC されるまで平文で残る。セキュリティを重視する場合は
    /// <see cref="ArchiveWriter"/> のスコープを <c>using</c> 等で最小限に留めること。
    /// </remarks>
    ///
    /* --------------------------------------------------------------------- */
    public string Password { get; init; } = string.Empty;

    /* --------------------------------------------------------------------- */
    ///
    /// CustomParameters
    ///
    /// <summary>
    /// 7z.dll の ISetProperties に直接渡すカスタムパラメータを取得する。
    /// </summary>
    /// <remarks>
    /// 7z.exe の -m スイッチ相当のキー/値ペアをそのままフォーマットハンドラに注入する。
    /// 例: <c>mt=1</c> (スレッド数)、<c>cu=on</c> (ZIP UTF-8 強制)、<c>d=64m</c> (LZMA 辞書)、
    /// <c>fb=128</c> (LZMA word size) など。既知キー (x / mt / cp / m / em / 0) と衝突した場合は
    /// このコレクションの値が優先される。全ての値は BSTR として注入する。
    /// </remarks>
    /* --------------------------------------------------------------------- */
    // デフォルトは null。CompressionOptionSetter 側で null ガードするため、
    // 使わないケースで辞書 allocation が発生しないようにする。
    public IDictionary<string, string> CustomParameters { get; init; }

    /* --------------------------------------------------------------------- */
    ///
    /// IncludeEmptyDirectories
    ///
    /// <summary>
    /// 空ディレクトリをアーカイブに含めるかどうかを示す値を取得する。
    /// </summary>
    /// <remarks>
    /// 既定値は true（既存互換）。false の場合、子孫エントリを 1 件も含まない
    /// ディレクトリは出力アーカイブから除外する。
    /// </remarks>
    /* --------------------------------------------------------------------- */
    public bool IncludeEmptyDirectories { get; init; } = true;


    /* --------------------------------------------------------------------- */
    ///
    /// VolumeSize
    ///
    /// <summary>
    /// ボリューム分割サイズ (bytes) を取得する。0 以下の場合は分割なし（単一ファイル）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 0 より大きい値を指定すると、<see cref="ArchiveWriter.Save(string)"/> で
    /// <c>dest.001, dest.002, ...</c> の形式で自動分割書き出しを行う。
    /// 例: <c>VolumeSize = 100 * 1024 * 1024</c> で 100MB ごとに分割。
    /// </para>
    /// <para>
    /// <b>対応フォーマット:</b> 7z / Zip 等の ISequentialOutStream 受け取り型のフォーマット。
    /// 非対応フォーマットの場合は警告ログを出力して無視される。
    /// </para>
    /// <para>
    /// <b>制限:</b> <see cref="ArchiveWriter.Save(System.IO.Stream, System.IProgress{Report}, bool)"/>
    /// (Stream 版保存) や <see cref="ArchiveWriter.Update(string, string)"/> との併用は未対応。
    /// </para>
    /// </remarks>
    ///
    /* --------------------------------------------------------------------- */
    public long VolumeSize { get; init; } = 0;

    /* --------------------------------------------------------------------- */
    ///
    /// FlushToDisk
    ///
    /// <summary>
    /// path ベース <see cref="ArchiveWriter.Save(string)"/> / <see cref="ArchiveWriter.Update(string, string)"/>
    /// 完了時に <c>FlushFileBuffers</c> (Win32) 相当を呼び、OS のファイルキャッシュをディスクに
    /// 同期的に書き出すかどうかを取得する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 既定値は <c>false</c>。<c>true</c> にすると、Save / Update 完了直前に
    /// <see cref="System.IO.FileStream.Flush(bool)"/> を <c>flushToDisk: true</c> で呼び、
    /// NTFS 書き込みキャッシュがディスクメディアへ確実に到達してからメソッドを抜ける。
    /// </para>
    /// <para>
    /// <b>用途:</b> 停電・ブルースクリーン等の予期しないシステムクラッシュに対する耐性を
    /// 高めたい場合に有効。<see cref="AtomicSave"/> と組み合わせると、新アーカイブが
    /// ディスクに到達してから古いアーカイブを rename で置き換えるので、クラッシュ耐性が
    /// 最大化される。
    /// </para>
    /// <para>
    /// <b>パフォーマンス注意:</b> <c>FlushFileBuffers</c> は数百 ms 〜 数秒のレイテンシを
    /// 招くため、大量の小さいアーカイブを連続生成する処理では off のまま使う方が速い。
    /// </para>
    /// <para>
    /// <b>Stream 版の扱い:</b> <see cref="ArchiveWriter.Save(System.IO.Stream, System.IProgress{Report}, bool)"/>
    /// など Stream ベース API では、渡された Stream が <see cref="System.IO.FileStream"/> の場合のみ
    /// flush する。<see cref="System.IO.MemoryStream"/> / NetworkStream 等は何もしない。
    /// </para>
    /// </remarks>
    ///
    /* --------------------------------------------------------------------- */
    public bool FlushToDisk { get; init; } = false;

    /* --------------------------------------------------------------------- */
    ///
    /// AtomicSave
    ///
    /// <summary>
    /// <see cref="ArchiveWriter.Save(string)"/> で「tmp file → atomic rename」パターンを使って
    /// アーカイブを書き出すかどうかを取得する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 既定値は <c>false</c>（従来通り dest を直接 truncate して書き込む）。
    /// <c>true</c> にすると以下の順で保存する:
    /// </para>
    /// <list type="number">
    /// <item><description>dest と同一ディレクトリに <c>{GUID}.ext</c> の tmp ファイルを作成して書き込む</description></item>
    /// <item><description>既存 dest があれば <c>{GUID}.bak</c> に退避 (atomic rename)</description></item>
    /// <item><description>tmp ファイルを dest にリネーム (atomic rename)</description></item>
    /// <item><description><see cref="KeepBackupOnUpdate"/> が false なら .bak を削除、true なら残す</description></item>
    /// </list>
    /// <para>
    /// <b>クラッシュ耐性:</b> 書き込み途中で停電しても元の dest は無傷。
    /// rename は NTFS 等のジャーナリング FS では同一ボリューム内で atomic。
    /// <see cref="FlushToDisk"/> と併用すると、rename 前に tmp の内容がディスクに到達済みとなり最強。
    /// </para>
    /// <para>
    /// <b>制限:</b>
    /// </para>
    /// <list type="bullet">
    /// <item><description><see cref="VolumeSize"/> &gt; 0 時は非対応 (警告ログを出して無視)。</description></item>
    /// <item><description><c>Format.Tar</c> は既に tmp 経由で生成されるため、AtomicSave は Tar でも動作するが追加のリネームが入る。</description></item>
    /// <item><description>tmp ファイル分の空き容量 (dest とほぼ同サイズ) が dest のディレクトリに必要。</description></item>
    /// <item><description>dest と tmp を別ボリュームにはできない (atomic rename の制約)。本実装は強制的に同一ディレクトリに tmp を作る。</description></item>
    /// </list>
    /// </remarks>
    ///
    /* --------------------------------------------------------------------- */
    public bool AtomicSave { get; init; } = false;

    /* --------------------------------------------------------------------- */
    ///
    /// KeepBackupOnUpdate
    ///
    /// <summary>
    /// <see cref="ArchiveWriter.Update(string, string)"/> の自己更新 (source==dest) や
    /// <see cref="AtomicSave"/> モードで生成される <c>{GUID}.bak</c> バックアップファイルを
    /// 正常完了後も削除せず残すかどうかを取得する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 既定値は <c>false</c>（正常完了時に .bak を削除する従来動作）。
    /// <c>true</c> にすると .bak ファイルが残り、そのパスは
    /// <see cref="ArchiveWriter.LastBackupPath"/> プロパティで取得できる。
    /// </para>
    /// <para>
    /// <b>用途:</b> 新アーカイブ生成後も一定期間「1 世代前」を保持したい GUI アプリ向け。
    /// 呼び出し側は適切なタイミングで <see cref="ArchiveWriter.LastBackupPath"/> を削除する責任を持つ。
    /// 削除し忘れるとディスク容量が肥大するので注意。
    /// </para>
    /// <para>
    /// このオプションは <see cref="ArchiveWriter.Update(string, string)"/> 自己更新および
    /// <see cref="AtomicSave"/> <c>= true</c> の <see cref="ArchiveWriter.Save(string)"/> 上書き時のみ効果がある。
    /// </para>
    /// </remarks>
    ///
    /* --------------------------------------------------------------------- */
    public bool KeepBackupOnUpdate { get; init; } = false;
}
