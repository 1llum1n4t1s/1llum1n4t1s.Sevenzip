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
using Cube.Collections;
using System.Collections.Generic;
namespace Cube.FileSystem.SevenZip;

/// <summary>
/// 提供されたアーカイブ内のアイテムコレクションを表す。
/// </summary>
/// <remarks>
/// インデクサアクセスは遅延評価かつキャッシュ付きで実装されており、
/// 同一インデックスへの複数アクセスで COM プロパティ取得が繰り返されない。
/// </remarks>
internal sealed class ArchiveCollection : EnumerableBase<ArchiveEntity>, IReadOnlyList<ArchiveEntity>
{
    #region Constructors

    /// <summary>
    /// 指定したコントローラーで ArchiveCollection クラスの新しいインスタンスを初期化する。
    /// </summary>
    /// <param name="core">7-zip COM コアオブジェクト。プロパティ取得に使用する。</param>
    /// <param name="count">アーカイブ内のアイテム数。</param>
    /// <param name="path">アーカイブファイルのパス。TAR 判定に使用する。</param>
    public ArchiveCollection(IInArchive core, int count, string path)
    {
        _core = core;
        _path = path;
        Count = count;
        // キャッシュ配列は最初のアクセス時に遅延生成する（_cache ??= ... を使用）
    }

    #endregion

    #region Properties

    /// <summary>
    /// アイテム数を取得する。
    /// </summary>
    public int Count { get; }

    /// <summary>
    /// 指定したインデックスの要素を取得する。
    /// </summary>
    /// <param name="index">取得する要素のインデックス。</param>
    /// <returns>ArchiveEntity オブジェクト。</returns>
    /// <remarks>
    /// 初回アクセス時に COM レイヤーからプロパティを取得してキャッシュする。
    /// 2回目以降はキャッシュから返す。
    /// </remarks>
    public ArchiveEntity this[int index]
    {
        get
        {
            // キャッシュ配列を遅延初期化する（Count 分確保）
            _cache ??= new ArchiveEntity[Count];

            if (_cache[index] is null)
            {
                // COM 経由でプロパティを取得し ArchiveEntitySource 経由で ArchiveEntity を生成する。
                // using で囲んで PropVariant バッファを確実に解放する。
                using var src = new ArchiveEntitySource(_core, index, _path);
                _cache[index] = new(src);
            }
            // キャッシュ済みのインスタンスを返す
            return _cache[index];
        }
    }

    #endregion

    #region Methods

    /// <summary>
    /// コレクションを反復処理する列挙子を返す。
    /// </summary>
    /// <returns>コレクションを反復処理するために使用できる列挙子。</returns>
    public override IEnumerator<ArchiveEntity> GetEnumerator()
    {
        // インデクサを介してキャッシュを活用しながら順に列挙する
        for (var i = 0; i < Count; ++i) yield return this[i];
    }

    /// <summary>
    /// オブジェクトが使用するリソースを解放する。
    /// </summary>
    /// <param name="disposing">
    /// マネージドリソースとアンマネージドリソースの両方を解放する場合は true；
    /// アンマネージドリソースのみを解放する場合は false。
    /// </param>
    protected override void Dispose(bool disposing)
    {
        // キャッシュ配列を null にして GC がエントリを回収できるようにする
        _cache = null;
        // COM オブジェクトへの参照を切る（ReleaseComWrapper は呼び出し元が担当）
        _core = null;
    }

    #endregion

    #region Fields
    // 7-zip COM アーカイブオブジェクト（Dispose 後は null になる）
    private IInArchive _core;
    // アーカイブファイルのパス（TAR 判定用）
    private readonly string _path;
    // ArchiveEntity のキャッシュ配列（初回アクセス時に生成）
    private ArchiveEntity[] _cache;
    #endregion
}
