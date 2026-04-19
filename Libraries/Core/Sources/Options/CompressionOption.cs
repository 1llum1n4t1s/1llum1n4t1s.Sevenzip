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
using System;
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
    /// OS ページキャッシュからディスクコントローラへの書き出しを同期する。
    /// </para>
    /// <para>
    /// <b>保証範囲 (重要):</b> <c>FlushFileBuffers</c> は <b>OS ページキャッシュまで</b>の同期を保証する。
    /// ストレージデバイス自体の揮発性 DRAM キャッシュ (PLP 非対応 NVMe / SATA SSD 等) のフラッシュは
    /// <b>保証しない</b>。完全な電源断耐性を得るには PLP (Power Loss Protection) 対応ストレージ
    /// またはエンタープライズ SSD / UPS 付与の環境が必要。
    /// </para>
    /// <para>
    /// <b>用途:</b> 予期しないシステムクラッシュ (プロセス kill / BSOD) に対する耐性強化。
    /// <see cref="AtomicSave"/> と組み合わせると、tmp にデータがディスクに到達してから
    /// atomic rename で古いアーカイブを置き換えるので、NTFS 等のジャーナリング FS では
    /// write-then-rename パターンが完成する。
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
    /// <item><description>既存 dest があれば <c>{GUID}.bak</c> に退避 (rename)</description></item>
    /// <item><description>tmp ファイルを dest にリネーム (rename)</description></item>
    /// <item><description><see cref="KeepBackupOnUpdate"/> が false なら .bak を削除、true なら残す</description></item>
    /// </list>
    /// <para>
    /// <b>保証範囲 (重要):</b> rename の "atomic" 性は <b>NTFS / ReFS v3 等のジャーナリング FS で、
    /// 同一ボリューム内での操作のみ</b>保証される。SMB 共有 / ReFS v1 / exFAT / FAT32 では
    /// atomic rename は保証されない (ネットワーク切断・電源断で中間状態に陥りうる)。
    /// ジャーナリング非対応 FS で使う場合はドキュメントの前提が成立しないため、運用側で
    /// 追加のフェイルセーフ (外部レプリカ等) が必要。
    /// </para>
    /// <para>
    /// <b>クラッシュ耐性:</b> NTFS 同一ボリュームなら、書き込み途中で停電しても元の dest は
    /// ほぼ無傷で残る。<see cref="FlushToDisk"/> と併用すると、rename 前に tmp の内容が OS
    /// ページキャッシュからディスクコントローラへ同期される。ただし PLP 非対応ストレージの
    /// デバイスキャッシュは同期されない点に注意 (詳細は <see cref="FlushToDisk"/> 参照)。
    /// </para>
    /// <para>
    /// <b>制限:</b>
    /// </para>
    /// <list type="bullet">
    /// <item><description><see cref="VolumeSize"/> &gt; 0 時は両立不可 (<see cref="System.InvalidOperationException"/> をスロー)。</description></item>
    /// <item><description><c>Format.Tar</c> は既に tmp 経由で生成されるため、AtomicSave は Tar でも動作するが追加のリネームが入る。</description></item>
    /// <item><description>tmp ファイル分の空き容量 (dest とほぼ同サイズ × 1.1) が dest のディレクトリに必要。事前にベストエフォートチェックが走る。</description></item>
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
    /// <see cref="AtomicSave"/> モードの <see cref="ArchiveWriter.Save(string)"/> 上書きで生成される
    /// <c>{GUID}.bak</c> バックアップファイルを正常完了後も削除せず残すかどうかを取得する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 既定値は <c>false</c>（正常完了時に .bak を削除する従来動作）。
    /// <c>true</c> にすると .bak ファイルが残り、そのパスは
    /// <see cref="ArchiveWriter.LastBackupPath"/> プロパティで取得できる。
    /// 複数回 Save/Update を呼ぶ場合、前回の .bak は次回操作で自動削除されるため孤立しない。
    /// 全履歴が必要な場合は <see cref="ArchiveWriter.BackupPaths"/> を参照。
    /// </para>
    /// <para>
    /// <b>用途:</b> 新アーカイブ生成後も一定期間「1 世代前」を保持したい GUI アプリ向け。
    /// 呼び出し側は適切なタイミングで <see cref="ArchiveWriter.LastBackupPath"/> を削除する責任を持つ。
    /// 削除し忘れるとディスク容量が肥大するので注意。
    /// </para>
    /// <para>
    /// <b>適用範囲:</b>
    /// </para>
    /// <list type="bullet">
    /// <item><description><see cref="ArchiveWriter.Update(string, string)"/> の自己更新 (source == dest)</description></item>
    /// <item><description><see cref="AtomicSave"/> = true の <see cref="ArchiveWriter.Save(string)"/> 上書き</description></item>
    /// <item><description><see cref="AtomicSave"/> = true の <see cref="ArchiveWriter.Update(string, string)"/> 非 sameFile 上書き</description></item>
    /// </list>
    /// <para>
    /// プロパティ名に "OnUpdate" が含まれているのは歴史的経緯。Save でも AtomicSave 上書き時に機能する。
    /// </para>
    /// </remarks>
    ///
    /* --------------------------------------------------------------------- */
    public bool KeepBackupOnUpdate { get; init; } = false;

    /// <summary>
    /// オプションの組み合わせを検証し、矛盾があれば <see cref="InvalidOperationException"/> をスローする。
    /// </summary>
    /// <param name="format">対象アーカイブフォーマット。</param>
    /// <remarks>
    /// 既知の早期検出可能な矛盾:
    /// - VolumeSize が負値
    /// - AtomicSave + VolumeSize の併用
    /// - ThreadCount が負値 (Normal 化されるのではなく例外に)
    /// - Password が Format.Tar で指定されている (TAR は暗号化非対応)
    /// 7z.dll が受け付けない組み合わせをライブラリ側で早期にキャッチし、不透明な
    /// <c>E_INVALIDARG</c> 例外より具体的なメッセージを呼び出し側に返す。
    /// </remarks>
    internal void Validate(Format format)
    {
        if (VolumeSize < 0)
            throw new ArgumentOutOfRangeException(
                nameof(VolumeSize), VolumeSize,
                "VolumeSize must be zero (no split) or positive.");

        if (AtomicSave && VolumeSize > 0)
            throw new InvalidOperationException(
                "AtomicSave and VolumeSize > 0 cannot be combined. Disable one of the options.");

        if (ThreadCount < 0)
            throw new ArgumentOutOfRangeException(
                nameof(ThreadCount), ThreadCount,
                "ThreadCount must be zero (auto) or positive.");

        if (format == Format.Tar && !string.IsNullOrEmpty(Password))
            throw new InvalidOperationException(
                "Format.Tar does not support encryption. Remove Password or switch to Format.SevenZip / Format.Zip.");
    }
}
