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
using System.Diagnostics;
using System.IO;
namespace Cube.FileSystem.SevenZip;

/* ------------------------------------------------------------------------- */
///
/// FormatFactory
///
/// <summary>
/// Provides functionality to detect the archive format with the specified
/// arguments.
/// </summary>
///
/* ------------------------------------------------------------------------- */
public static class FormatFactory
{
    #region Methods

    /* --------------------------------------------------------------------- */
    ///
    /// From
    ///
    /// <summary>
    /// Reads bytes from the specified stream and detects the archive format.
    /// </summary>
    ///
    /// <param name="src">Stream of the archive.</param>
    ///
    /// <returns>Archive format.</returns>
    ///
    /* --------------------------------------------------------------------- */
    public static Format From(Stream src)
    {
        var origin = src.Position;

        try
        {
            var bytes = new byte[16];
            var count = src.Read(bytes, 0, 16);
            if (count <= 0) return Format.Unknown;

            var span = bytes.AsSpan(0, count);
            foreach (var (sig, fmt) in s_signature.Value)
            {
                if (span.StartsWith(sig)) return fmt;
            }

            // 特殊シグネチャ（先頭以外のオフセット位置にある）
            if (Match(src, 0x101, "ustar"u8)) return Format.Tar;
            if (Match(src, 0x002, "-lh"u8)) return Format.Lzh;

            return Format.Unknown;
        }
        finally { _ = src.Seek(origin, SeekOrigin.Begin); }
    }

    /* --------------------------------------------------------------------- */
    ///
    /// From
    ///
    /// <summary>
    /// Detects the archvie format with the specified file path.
    /// </summary>
    ///
    /// <param name="src">Path of the archive file.</param>
    ///
    /// <returns>Archive format.</returns>
    ///
    /* --------------------------------------------------------------------- */
    public static Format From(string src)
    {
        if (Io.Exists(src))
        {
            using var ss = Io.Open(src);
            var format = From(ss);
            if (format != Format.Unknown)
            {
                var sfx = format == Format.PE && FileVersionInfo.GetVersionInfo(src).InternalName == "7z.sfx";
                return sfx ? Format.Sfx : format;
            }
        }
        return FromExtension(Io.GetExtension(src));
    }

    /* --------------------------------------------------------------------- */
    ///
    /// FromExtension
    ///
    /// <summary>
    /// Gets the archvie format corresponding to the specified extension.
    /// </summary>
    ///
    /// <param name="src">Extension.</param>
    ///
    /// <returns>Archive format.</returns>
    ///
    /* --------------------------------------------------------------------- */
    public static Format FromExtension(string src) =>
        s_extension.Value.TryGetValue(src.ToLowerInvariant(), out var dest) ?
        dest :
        Format.Unknown;

    #endregion

    #region Implementations

    /* --------------------------------------------------------------------- */
    ///
    /// Match
    ///
    /// <summary>
    /// ストリームの指定オフセットからバイト列を読み取り、期待されるシグネチャと
    /// 一致するかどうかを返します。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    private static bool Match(Stream stream, int offset, ReadOnlySpan<byte> expected)
    {
        Span<byte> bytes = stackalloc byte[expected.Length];
        _ = stream.Seek(offset, SeekOrigin.Begin);
        if (stream.Read(bytes) < expected.Length) return false;
        return bytes.SequenceEqual(expected);
    }

    /* --------------------------------------------------------------------- */
    ///
    /// CreateExtensionMap
    ///
    /// <summary>
    /// ファイル拡張子とアーカイブ形式の対応辞書を生成します。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    private static Dictionary<string, Format> CreateExtensionMap()
    {
        var dest = new Dictionary<string, Format>
        {
            { ".7z",   Format.SevenZip },
            { ".bz",   Format.BZip2    },
            { ".bz2",  Format.BZip2    },
            { ".tbz",  Format.BZip2    },
            { ".tb2",  Format.BZip2    },
            { ".tbz2", Format.BZip2    },
            { ".gz",   Format.GZip     },
            { ".tgz",  Format.GZip     },
            { ".xz",   Format.XZ       },
            { ".txz",  Format.XZ       },
            { ".z",    Format.Lzw      },
            { ".zst",  Format.Zstd     },
        };

        foreach (var item in Enum.GetValues<Format>())
        {
            var ext = $".{item.ToString().ToLowerInvariant()}";
            if (!dest.ContainsKey(ext)) dest.Add(ext, item);
        }

        return dest;
    }

    /* --------------------------------------------------------------------- */
    ///
    /// CreateSignatureMap
    ///
    /// <summary>
    /// バイトシグネチャとアーカイブ形式の対応配列を生成します。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    private static (byte[] Signature, Format Format)[] CreateSignatureMap() =>
    [
        ([0x50, 0x4B, 0x03, 0x04],                         Format.Zip),
        ([0x42, 0x5A, 0x68],                                Format.BZip2),
        ([0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00],       Format.Rar),
        ([0x60, 0xEA],                                      Format.Arj),
        ([0x1F, 0x9D, 0x90],                                Format.Lzw),
        ([0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C],             Format.SevenZip),
        ([0x4D, 0x53, 0x43, 0x46],                          Format.Cab),
        ([0x5D, 0x00, 0x00, 0x40, 0x00],                    Format.Lzma),
        ([0xFD, 0x37, 0x7A, 0x58, 0x5A],                    Format.XZ),
        ([0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00], Format.Rar5),
        ([0x46, 0x4C, 0x56],                                Format.Flv),
        ([0x46, 0x57, 0x53],                                Format.Swf),
        ([0x63, 0x6F, 0x6E, 0x65, 0x63, 0x74, 0x69, 0x78], Format.Vhd),
        ([0x4D, 0x5A],                                      Format.PE),
        ([0x7F, 0x45, 0x4C, 0x46],                          Format.Elf),
        ([0x78, 0x61, 0x72, 0x21],                          Format.Xar),
        ([0x78],                                            Format.Dmg),
        ([0x4D, 0x53, 0x57, 0x49, 0x4D, 0x00, 0x00, 0x00], Format.Wim),
        ([0x43, 0x44, 0x30, 0x30, 0x31],                    Format.Iso),
        ([0x49, 0x54, 0x53, 0x46],                          Format.Chm),
        ([0xED, 0xAB, 0xEE, 0xDB],                          Format.Rpm),
        ([0x1F, 0x8B, 0x08],                                Format.GZip),
        ([0x28, 0xB5, 0x2F, 0xFD],                          Format.Zstd),
    ];

    #endregion

    #region Fields
    private static readonly Lazy<(byte[] Signature, Format Format)[]> s_signature = new(CreateSignatureMap);
    private static readonly Lazy<Dictionary<string, Format>> s_extension = new(CreateExtensionMap);
    #endregion
}
