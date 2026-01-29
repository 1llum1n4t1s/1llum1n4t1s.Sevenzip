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
namespace Cube.FileSystem.SevenZip;

/* ------------------------------------------------------------------------- */
///
/// ReportExtension
///
/// <summary>
/// Provides extended methods of the Report class.
/// </summary>
///
/* ------------------------------------------------------------------------- */
public static class ReportExtension
{
    /* --------------------------------------------------------------------- */
    ///
    /// GetRatio
    ///
    /// <summary>
    /// Gets the progress ratio within the range of [0, 1].
    /// Returns byte-based ratio (0 to 100%) when TotalBytes > 0.
    /// Returns 1.0 (100%) when TotalBytes == 0 (no data to process).
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    public static double GetRatio(this Report src)
    {
        if (src.TotalBytes > 0)
        {
            return Math.Min(src.Bytes / (double)src.TotalBytes, 1.0);
        }
        
        // Fallback to file count when TotalBytes is 0 (e.g., directory-only operations)
        if (src.TotalCount > 0)
        {
            return Math.Min(src.Count / (double)src.TotalCount, 1.0);
        }
        
        return 1.0;
    }
}
