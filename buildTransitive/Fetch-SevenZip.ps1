<#
.SYNOPSIS
    1llum1n4t1s.Sevenzip: ビルド時の最新 7-Zip native DLL 自動取得スクリプト。

.DESCRIPTION
    GitHub Releases (ip7z/7zip) から最新の 7-Zip インストーラーをダウンロードし、
    Authenticode 署名検証のうえ、中の 7z.dll を抽出してコンシューマーの
    OutputDir に配置する。

    設計方針:
      1. どんな失敗 (ネット断・検証失敗・展開失敗) もビルドを止めない。
         エラー時は何もせず exit 0。OutputDir には呼び出し側 (.targets) が
         事前に配置した埋め込み版 7z.dll が残る。
      2. 24h キャッシュ (%LOCALAPPDATA%\1llum1n4t1s.Sevenzip\<rid>\)。
      3. HTTPS 固定、ドメインは github.com / api.github.com のみ。
      4. Authenticode Subject に "Igor Pavlov" を要求 (インストーラー検証)。
      5. 抽出は OutputDir に既に配置されている Cube.FileSystem.SevenZip.dll を
         Add-Type で load し、ArchiveReader 経由で行う (bootstrap 問題回避)。

.PARAMETER Rid
    ターゲット RID (win-x64 / win-arm64)。

.PARAMETER OutputDir
    コンシューマープロジェクトの OutputPath。成功時ここの 7z.dll を更新する。
    同時に Cube.FileSystem.SevenZip.dll の所在としても使用される (Add-Type 用)。
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Rid,

    [Parameter(Mandatory = $true)]
    [string]$OutputDir
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# -----------------------------------------------------------------------------
# ヘルパー関数 (PowerShell はトップレベルで定義してから呼び出す必要がある)
# -----------------------------------------------------------------------------

# Authenticode 検証ヘルパー。署名が Valid かつ Subject に "Igor Pavlov" を含む場合 true。
function Test-SignatureValid {
    param([string]$Path)
    try {
        $sig = Get-AuthenticodeSignature -FilePath $Path
        if ($sig.Status -ne 'Valid') { return $false }
        if ($null -eq $sig.SignerCertificate) { return $false }
        return ($sig.SignerCertificate.Subject -like '*Igor Pavlov*')
    } catch {
        return $false
    }
}

# 7-Zip SFX インストーラーから 7z.dll を展開する。
#
# 7-Zip 公式インストーラーは PE ローダー + 末尾に 7z アーカイブブロックが
# 連結された「7z SFX」形式。7z シグネチャ (37 7A BC AF 27 1C) をバイナリ内で
# 全件検索し、StartHeader (next_header_offset + size + 32 == remaining) が
# 整合する位置を本物のアーカイブ先頭として切り出す。
#
# 抽出には LibDir に配置されている Cube.FileSystem.SevenZip.dll を
# Add-Type でロードして ArchiveReader を使う (bootstrap 問題回避)。
# 展開は一時ディレクトリに対して行い、7z.dll だけを CacheDir にコピーする。
#
# 戻り値: 抽出成功時は CacheDir\7z.dll のフルパス、失敗時は $null。
function Invoke-SevenZDllExtraction {
    param(
        [string]$InstallerPath,
        [string]$CacheDir,
        [string]$LibDir
    )

    # --- Cube.FileSystem.SevenZip.dll の存在確認 (bootstrap DLL は LibDir にある) ---
    $libDll = Join-Path $LibDir 'Cube.FileSystem.SevenZip.dll'
    if (-not (Test-Path $libDll)) {
        throw "Cube.FileSystem.SevenZip.dll not found at: $libDll"
    }

    # --- 7z シグネチャを全件探索し、StartHeader 整合性で本物を特定 ---
    $bytes = [System.IO.File]::ReadAllBytes($InstallerPath)
    $sig = [byte[]](0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C)

    $validOffset = -1
    for ($i = 0; $i -le ($bytes.Length - 32); $i++) {
        $match = $true
        for ($j = 0; $j -lt $sig.Length; $j++) {
            if ($bytes[$i + $j] -ne $sig[$j]) { $match = $false; break }
        }
        if (-not $match) { continue }

        # StartHeader の next_header_offset + size + 32 が remaining と一致すれば有効
        $nextOffset = [BitConverter]::ToUInt64($bytes, $i + 12)
        $nextSize   = [BitConverter]::ToUInt64($bytes, $i + 20)
        $total      = [uint64]32 + $nextOffset + $nextSize
        $remaining  = [uint64]($bytes.Length - $i)
        if ($total -eq $remaining) { $validOffset = $i; break }
    }
    if ($validOffset -lt 0) { throw 'no valid 7z archive found in installer' }

    # --- CacheDir 外の一時ディレクトリに展開 (キャッシュ汚染回避) ---
    $tmpExtract = Join-Path ([System.IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $tmpExtract -Force | Out-Null

    $tmp7z = Join-Path $tmpExtract 'inner.7z'
    $fs = [System.IO.File]::OpenWrite($tmp7z)
    try {
        $fs.Write($bytes, $validOffset, $bytes.Length - $validOffset)
    } finally {
        $fs.Close()
    }

    try {
        # Cube.FileSystem.SevenZip.dll を Add-Type でロード (LibDir 隣の 7z.dll を使う)
        Add-Type -Path $libDll

        $reader = New-Object Cube.FileSystem.SevenZip.ArchiveReader($tmp7z)
        try {
            $reader.Save($tmpExtract)
        } finally {
            $reader.Dispose()
        }

        $extracted = Join-Path $tmpExtract '7z.dll'
        if (-not (Test-Path $extracted)) { return $null }

        # 7z.dll のみを CacheDir にコピー
        $cachedDll = Join-Path $CacheDir '7z.dll'
        Copy-Item $extracted $cachedDll -Force
        return $cachedDll
    } finally {
        Remove-Item $tmpExtract -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# -----------------------------------------------------------------------------
# メイン処理: すべての実行パスを try/catch で包み、何が起きても exit 0 で
# ビルドを止めない。
# -----------------------------------------------------------------------------
try {
    $cacheRoot  = Join-Path $env:LOCALAPPDATA '1llum1n4t1s.Sevenzip'
    $ridCache   = Join-Path $cacheRoot $Rid
    $cachedDll  = Join-Path $ridCache '7z.dll'
    $cachedExe  = Join-Path $ridCache 'installer.exe'
    $tsFile     = Join-Path $ridCache 'lastcheck.txt'
    $targetDll  = Join-Path $OutputDir '7z.dll'

    New-Item -ItemType Directory -Path $ridCache -Force | Out-Null

    # --- 24h キャッシュ有効チェック ---
    $cacheHot = $false
    if ((Test-Path $cachedDll) -and (Test-Path $tsFile) -and (Test-Path $cachedExe)) {
        try {
            $last = [datetime](Get-Content $tsFile -Raw).Trim()
            $delta = (Get-Date) - $last
            # 負のデルタ (時計巻き戻し等) もキャッシュ無効として扱う
            if ($delta.TotalHours -ge 0 -and $delta.TotalHours -lt 24) {
                # キャッシュ再読み込み時も署名を再検証 (改ざん対策)
                if (Test-SignatureValid -Path $cachedExe) {
                    $cacheHot = $true
                } else {
                    Write-Host "1llum1n4t1s.Sevenzip: cached installer signature invalid, re-fetching"
                    Remove-Item $cachedDll, $cachedExe, $tsFile -Force -ErrorAction SilentlyContinue
                }
            }
        } catch {
            $cacheHot = $false
        }
    }

    if ($cacheHot) {
        # 24h 以内なら GitHub API を叩かず、キャッシュ済み DLL を OutputDir に配置。
        # ただし既存ファイルが同一内容なら no-op でスキップ (毎ビルドごとのタイムスタンプ汚染
        # = 下流の incremental build の無効化を防ぐ)。Copy-Item は LastWriteTime を維持するため
        # 2 ビルド目以降は Length + LastWriteTime の一致で判定できる。
        $cachedInfo = Get-Item $cachedDll
        $targetInfo = if (Test-Path $targetDll) { Get-Item $targetDll } else { $null }
        if ($null -eq $targetInfo -or
            $targetInfo.Length -ne $cachedInfo.Length -or
            $targetInfo.LastWriteTime -ne $cachedInfo.LastWriteTime) {
            Copy-Item $cachedDll $targetDll -Force
        }
        return
    }

    # --- GitHub Releases API で最新バージョン取得 ---
    $apiUrl = 'https://api.github.com/repos/ip7z/7zip/releases/latest'
    $headers = @{
        'User-Agent' = '1llum1n4t1s.Sevenzip-autofetch/1.0'
        'Accept'     = 'application/vnd.github+json'
    }
    $release = Invoke-RestMethod -Uri $apiUrl -Headers $headers -TimeoutSec 15

    if ($null -eq $release) { throw 'empty response from GitHub API' }
    # Set-StrictMode Latest 下では存在しないプロパティへのアクセスが throw するため、
    # PSObject.Properties で事前に存在を確認する (GitHub API のスキーマ変更に対する防御)。
    if ($null -eq $release.PSObject.Properties['assets']) {
        throw 'release payload missing assets property'
    }

    # --- RID に応じて asset を選択 ---
    $archSuffix = if ($Rid -eq 'win-arm64') { 'arm64' } else { 'x64' }
    $assetPattern = "7z*-$archSuffix.exe"
    $asset = $release.assets |
        Where-Object { $_.name -like $assetPattern -and $_.name -notlike '*msi*' } |
        Select-Object -First 1
    if (-not $asset) { throw "asset not found matching: $assetPattern" }

    # --- HTTPS / ドメイン固定チェック ---
    $downloadUri = [Uri]$asset.browser_download_url
    if ($downloadUri.Scheme -ne 'https') {
        throw "non-HTTPS URL rejected: $($asset.browser_download_url)"
    }
    if ($downloadUri.Host -ne 'github.com' -and $downloadUri.Host -ne 'objects.githubusercontent.com') {
        throw "unexpected host: $($downloadUri.Host)"
    }

    # --- ダウンロード ---
    $tmpExe = Join-Path ([System.IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString('N') + '.exe')
    try {
        Invoke-WebRequest -Uri $downloadUri -OutFile $tmpExe -UseBasicParsing -TimeoutSec 60

        # --- Authenticode 検証 ---
        if (-not (Test-SignatureValid -Path $tmpExe)) {
            throw "Authenticode verification failed for $($asset.name)"
        }

        # --- 7-Zip SFX 内部の 7z アーカイブを切り出して展開 ---
        $extractedDll = Invoke-SevenZDllExtraction `
            -InstallerPath $tmpExe `
            -CacheDir $ridCache `
            -LibDir $OutputDir
        if ($null -eq $extractedDll -or -not (Test-Path $extractedDll)) {
            throw 'failed to extract 7z.dll from installer'
        }

        # --- キャッシュ installer.exe を更新 + タイムスタンプ記録 ---
        Move-Item $tmpExe $cachedExe -Force
        Set-Content $tsFile -Value (Get-Date).ToString('o') -NoNewline

        # --- OutputDir に配置 ---
        Copy-Item $cachedDll $targetDll -Force
        Write-Host "1llum1n4t1s.Sevenzip: updated to $($asset.name)"
    } finally {
        if (Test-Path $tmpExe) {
            Remove-Item $tmpExe -Force -ErrorAction SilentlyContinue
        }
    }
}
catch {
    # ネットワーク失敗・検証失敗・展開失敗 → OutputDir には埋め込み版 7z.dll が残る
    Write-Host "1llum1n4t1s.Sevenzip: auto-update skipped ($($_.Exception.Message))"
}

exit 0
