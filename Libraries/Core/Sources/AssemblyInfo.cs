/* ------------------------------------------------------------------------- */
//
// Copyright (c) 2010 CubeSoft, Inc.
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as
// published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version.
//
/* ------------------------------------------------------------------------- */
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
[assembly: SupportedOSPlatform("windows")]
// UpdateCallback 等の internal な進捗ロジックを単体テストから検証するため
[assembly: InternalsVisibleTo("Cube.FileSystem.SevenZip.Tests")]
