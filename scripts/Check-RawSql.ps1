param([string]$ProjectRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path -LiteralPath $ProjectRoot).Path
$allowlist = Get-Content -LiteralPath (Join-Path $project 'scripts/raw-sql-reviewed.json') -Raw | ConvertFrom-Json
$pattern = 'FromSql|ExecuteSql|SqlCommand|CreateCommand\s*\(|CommandText\s*=|migrationBuilder\s*\.\s*Sql'
$violations = @()
$rg = Get-Command rg -ErrorAction SilentlyContinue
if ($rg) {
    $files = @(& $rg.Source -l -g '*.cs' -g '!bin/**' -g '!obj/**' $pattern (Join-Path $project 'src'))
    if ($LASTEXITCODE -gt 1) { throw 'Không thể quét source.' }
} else {
    $files = @(Get-ChildItem -LiteralPath (Join-Path $project 'src') -Filter '*.cs' -Recurse |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
        Where-Object { [IO.File]::ReadAllText($_.FullName) -match $pattern } | Select-Object -ExpandProperty FullName)
}
foreach ($file in $files) {
    $absolute = [IO.Path]::GetFullPath($file)
    $rootPrefix = $project.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $absolute.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Đường dẫn quét nằm ngoài project.' }
    $relative = $absolute.Substring($rootPrefix.Length).Replace('\', '/')
    $reviewed = @($allowlist | Where-Object path -eq $relative)
    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
    if ($reviewed.Count -ne 1 -or $reviewed[0].sha256 -ne $hash) { $violations += $relative }
}
if ($violations.Count) {
    $violations | ForEach-Object { Write-Output "Cần review SQL: $_" }
    throw 'Raw SQL mới/thay đổi: cần giải thích, tham số hóa giá trị, kiểm thử và review trước khi cập nhật allowlist.'
}
Write-Output "Đạt: $($files.Count) file SQL hạ tầng đã review; không phát hiện raw SQL mới trong ứng dụng."
