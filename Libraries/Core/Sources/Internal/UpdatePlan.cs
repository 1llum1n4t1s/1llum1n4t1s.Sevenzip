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

/// <summary>
/// 既存アーカイブと新規アイテムのマージ計画を表す。
/// 7-zip の UpdateItems に渡すインデックス空間を管理する。
/// </summary>
/// <remarks>
/// 7-zip の IArchiveUpdateCallback では、出力アーカイブ内の全アイテムを
/// 連続したインデックスで管理する。このクラスは既存アイテムの Keep/Replace と
/// 新規アイテムの Add を組み合わせた最終的なエントリ配列を構築する。
/// </remarks>
internal sealed class UpdatePlan
{
    #region Types

    /// <summary>
    /// 更新プラン内の1エントリを表す。
    /// </summary>
    /// <remarks>
    /// readonly struct にすることでスタック割り当てを保証し、ボックス化を回避する。
    /// </remarks>
    internal readonly struct Entry
    {
        /// <summary>
        /// 新規または置換アイテムであるか（新しいデータ/プロパティが必要）を取得する。
        /// true の場合、GetUpdateItemInfo で newdata=1, newprop=1 を返す。
        /// false の場合は既存アーカイブからデータをそのままコピーする。
        /// </summary>
        public bool IsNewOrReplaced { get; init; }

        /// <summary>
        /// 既存アーカイブ内の元インデックスを取得する。
        /// 純粋な新規アイテム（既存エントリとの対応なし）の場合は uint.MaxValue。
        /// </summary>
        public uint OriginalIndex { get; init; }

        /// <summary>
        /// 新規アイテムリスト内のインデックスを取得する（新規/置換アイテム用）。
        /// 既存アイテムを保持する場合（IsNewOrReplaced=false）は -1。
        /// </summary>
        public int NewItemIndex { get; init; }

        /// <summary>
        /// 保持エントリをリネームする場合の新しい相対パスを取得する。
        /// null の場合はリネームなし。
        /// </summary>
        /// <remarks>
        /// 非 null の場合、UpdateCallback は該当エントリに対して
        /// newdata=0（既存データ再利用）+ newprop=1（新プロパティ適用）を返し、
        /// GetProperty の ItemPropId.Path では <see cref="RenameTo"/> を返す。
        /// </remarks>
        public string RenameTo { get; init; }
    }

    #endregion

    #region Constructors

    /// <summary>
    /// 既存アーカイブのアイテムと新規アイテムをマージした更新プランを生成する。
    /// </summary>
    /// <param name="existingCount">既存アーカイブ内のアイテム数。</param>
    /// <param name="existingPaths">既存インデックスから相対パスを取得する関数。</param>
    /// <param name="newItems">追加/置換する新規アイテムのリスト。</param>
    /// <param name="removeSet">削除対象の既存インデックスの集合。null の場合は削除なし。</param>
    public UpdatePlan(
        uint existingCount,
        Func<uint, string> existingPaths,
        IList<RawEntity> newItems,
        HashSet<uint> removeSet)
        : this(existingCount, existingPaths, newItems, removeSet, renameMap: null) { }

    /// <summary>
    /// rename マップを含む更新プランを生成する。
    /// </summary>
    /// <param name="existingCount">既存アーカイブ内のアイテム数。</param>
    /// <param name="existingPaths">既存インデックスから相対パスを取得する関数。</param>
    /// <param name="newItems">追加/置換する新規アイテムのリスト。</param>
    /// <param name="removeSet">削除対象の既存インデックスの集合。null の場合は削除なし。</param>
    /// <param name="renameMap">
    /// 既存インデックス → 新しい相対パス のマップ。値が null/empty のエントリは削除扱い。
    /// removeSet と両方で指定された場合は removeSet が優先される。
    /// </param>
    public UpdatePlan(
        uint existingCount,
        Func<uint, string> existingPaths,
        IList<RawEntity> newItems,
        HashSet<uint> removeSet,
        IReadOnlyDictionary<uint, string> renameMap)
    {
        // 新規アイテムの相対パスをキーとした置換マップを構築する。
        // '/' は '\' に正規化して外部作成 ZIP（非 Windows ツール）との一致を保証する。
        // OrdinalIgnoreCase: Windows ファイルシステムは大文字小文字を区別しないため。
        var replaceMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < newItems.Count; i++)
            replaceMap[newItems[i].RelativeName.Replace('/', '\\')] = i;

        var entries = new List<Entry>();

        // 置換で使用済みの新規アイテムインデックスを追跡し、Phase 2 での重複追加を防ぐ。
        var usedNewIndices = new HashSet<int>();

        // Phase 1: 既存アイテムを順に処理し、Keep / Replace / Rename / Remove を決定する
        for (uint i = 0; i < existingCount; i++)
        {
            // 削除セットに含まれるアイテムはエントリに追加しない（= 削除扱い）。
            // エントリに含めないことで TotalCount が減り、出力アーカイブから除外される。
            if (removeSet?.Contains(i) == true) continue;

            // renameMap でも削除扱い（値が null/empty）の場合はスキップ
            string renameTo = null;
            var hasRename = renameMap is not null && renameMap.TryGetValue(i, out renameTo);
            if (hasRename && string.IsNullOrEmpty(renameTo)) continue;

            // パス区切り文字を正規化（外部作成 ZIP は '/' を使用する場合がある）
            var path = existingPaths(i).Replace('/', '\\');

            // Add との照合は「出力上のパス」(rename があれば rename 後のパス) で行う。
            // 元パスだけで照合すると、renameMap{i:"x"} と Add(..., "x") の併用で
            // rename 後のエントリ "x" と Add の "x" が両方出力され、同名エントリが 2 つになる
            // (Update(Stream, Stream, renameMap, ...) の「Add の内容が優先」契約に反する)。
            // rename 先が Add と一致した場合、rename 元エントリは Add のデータで置換する。
            var effectivePath = hasRename ? renameTo.Replace('/', '\\') : path;

            // 既存側にケースだけ違う重複パス (非 Windows 製アーカイブの "README" と "readme" 等) が
            // あると、OrdinalIgnoreCase の replaceMap では両方が同じ新規アイテムにヒットして
            // 出力に同一パスのエントリが 2 つ並び、片方の元データが意図せず失われる。
            // 先にヒットした 1 件だけを置換し、残りは Keep に倒して挙動を決定的にする。
            if (replaceMap.TryGetValue(effectivePath, out var newIdx) && !usedNewIndices.Contains(newIdx))
            {
                // 置換: 新規アイテムのデータ/プロパティで既存エントリを上書きする。
                // indexInArchive に元インデックスを渡すことで 7-zip がデータを再利用できる。
                entries.Add(new Entry
                {
                    IsNewOrReplaced = true,
                    OriginalIndex   = i,
                    NewItemIndex    = newIdx,
                });
                // このインデックスは Phase 2 での追加処理をスキップする。
                usedNewIndices.Add(newIdx);
            }
            else if (hasRename)
            {
                // 保持 + rename: データは既存のまま、プロパティ（Path）だけ新しい値に差し替え。
                // RenameTo を正規化してから格納する。
                entries.Add(new Entry
                {
                    IsNewOrReplaced = false,
                    OriginalIndex   = i,
                    NewItemIndex    = -1,
                    RenameTo        = effectivePath,
                });
            }
            else
            {
                // 保持: 既存データをそのままコピーする (newdata=0, newprop=0)。
                // 7-zip はこのエントリについて GetStream を呼び出さない。
                entries.Add(new Entry
                {
                    IsNewOrReplaced = false,
                    OriginalIndex   = i,
                    NewItemIndex    = -1, // 対応する新規アイテムは存在しない
                });
            }
        }

        // Phase 2: 既存エントリへの置換で使われなかった純粋な新規アイテムを末尾に追加する
        for (var i = 0; i < newItems.Count; i++)
        {
            // 既存エントリの置換に使用済みのものはスキップ
            if (usedNewIndices.Contains(i)) continue;

            entries.Add(new Entry
            {
                IsNewOrReplaced = true,
                OriginalIndex   = uint.MaxValue, // 対応する既存エントリなし（純粋新規）
                NewItemIndex    = i,
            });
        }

        // rename によって出力に同名エントリが生じないことを検証する (書き込み開始前に拒否)。
        ThrowIfRenameCollides(entries, existingPaths, newItems, renameMap);

        // List<Entry> をイミュータブルな配列に変換して Entries プロパティに格納する
        Entries = [.. entries];
    }

    #endregion

    #region Implementations

    /// <summary>
    /// rename に起因する出力パスの重複を検出し、<see cref="ArgumentException"/> で拒否する。
    /// </summary>
    /// <param name="entries">構築済みの出力エントリ。</param>
    /// <param name="existingPaths">既存インデックスから相対パスを取得する関数。</param>
    /// <param name="newItems">追加/置換する新規アイテムのリスト。</param>
    /// <param name="renameMap">既存インデックス → 新しい相対パス のマップ。</param>
    /// <remarks>
    /// <para>
    /// rename 先が保持される別エントリ (rename 後のパスを含む) と一致すると、7-Zip は
    /// 同名エントリを 2 つ持つ書庫を黙って生成する。展開時にどちらが残るかは展開側次第で、
    /// 利用者の意図 (rename) と無関係にデータが失われうるため、更新前に拒否する。
    /// rename 値が安全化後に空となる場合の <see cref="ArgumentException"/> と同じく、
    /// 不正な renameMap 入力として扱う。
    /// </para>
    /// <para>
    /// 判定は「重複したパスの持ち主に rename 由来のエントリ (rename のみ、または rename 先で
    /// Add に置換されたもの) が含まれる」場合に限る。既存書庫が元々持っている
    /// ケース違いの重複 (rename も Add も関与しない Keep 同士) は従来どおり許容する。
    /// 互いの名前の入れ替え (a → b, b → a) は出力上で重複しないため許容される。
    /// </para>
    /// </remarks>
    private static void ThrowIfRenameCollides(List<Entry> entries, Func<uint, string> existingPaths,
        IList<RawEntity> newItems, IReadOnlyDictionary<uint, string> renameMap)
    {
        if (renameMap is null || renameMap.Count == 0) return;

        // 出力パス → (件数, rename 由来を含むか)。OrdinalIgnoreCase は replaceMap と同じ規則。
        var names = new Dictionary<string, (int Count, bool Renamed)>(entries.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            string name;
            if (e.IsNewOrReplaced) name = newItems[e.NewItemIndex].RelativeName.Replace('/', '\\');
            else if (e.RenameTo is not null) name = e.RenameTo;
            else name = existingPaths(e.OriginalIndex).Replace('/', '\\');

            // rename 先で Add に置換されたエントリも rename 由来として数える
            var renamed = e.RenameTo is not null ||
                (e.OriginalIndex != uint.MaxValue && renameMap.ContainsKey(e.OriginalIndex));

            names.TryGetValue(name, out var cur);
            names[name] = (cur.Count + 1, cur.Renamed || renamed);
        }

        foreach (var kv in names)
        {
            if (kv.Value.Count > 1 && kv.Value.Renamed)
                throw new ArgumentException(
                    $"The renamed archive path '{kv.Key}' collides with another entry in the updated archive.",
                    nameof(renameMap));
        }
    }

    #endregion

    #region Properties

    /// <summary>
    /// 更新プランの全エントリを取得する。
    /// </summary>
    /// <remarks>
    /// インデックスは 7-zip UpdateItems コールバックの index 引数と対応する。
    /// </remarks>
    public Entry[] Entries { get; }

    /// <summary>
    /// 更新後のアーカイブ内の総アイテム数を取得する。
    /// </summary>
    /// <remarks>
    /// 削除されたアイテムはエントリに含まれないため、既存の Count より少なくなる場合がある。
    /// </remarks>
    public int TotalCount => Entries.Length;

    #endregion
}
