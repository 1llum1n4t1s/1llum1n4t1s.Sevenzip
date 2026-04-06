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
namespace Cube.FileSystem.SevenZip;

/// <summary>
/// アーカイブに追加するファイルまたはディレクトリの情報を表す。
/// </summary>
/// <remarks>
/// <see cref="Entity"/> を継承し、アーカイブ内での相対パスとコメント情報を追加する。
/// UpdateCallback はこのクラスのリストを受け取り、7-zip へプロパティを提供する。
/// </remarks>
public class RawEntity : Entity
{
    #region Constructors

    /// <summary>
    /// 指定した引数で RawEntity クラスの新しいインスタンスを初期化する。
    /// </summary>
    /// <param name="src">実ファイルシステム上のパス（FullName として使用）。</param>
    /// <param name="name">アーカイブ内での相対パス（RelativeName として使用）。</param>
    public RawEntity(string src, string name) : base(IoEx.GetEntitySource(src)) => RelativeName = name;

    #endregion

    #region Properties

    /// <summary>
    /// アーカイブ内での相対パスを取得する。
    /// </summary>
    /// <remarks>
    /// UpdateCallback.GetProperty で ItemPropId.Path として 7-zip に渡される。
    /// パス区切り文字は '/' または '\' のどちらでも使用できるが、
    /// UpdatePlan 内で '\' に正規化される。
    /// </remarks>
    public string RelativeName { get; }

    /// <summary>
    /// アーカイブ内のアイテムに付与するコメントを取得または設定する。
    /// </summary>
    /// <remarks>
    /// ZIP 形式でのみ有効。null の場合はコメントを設定しない。
    /// UpdateCallback.GetProperty で ItemPropId.Comment として 7-zip に渡される。
    /// </remarks>
    public string Comment { get; set; }

    #endregion
}
