[CmdletBinding()]
param([string]$Destination)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Destination)) { $Destination = Join-Path (Split-Path -Parent $PSScriptRoot) 'InterviewLab-Source-BaoMat-10-11-2026.zip' }
& (Join-Path $PSScriptRoot 'Test-SourceSecrets.ps1')
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$sourceRoot = (Resolve-Path -LiteralPath $PSScriptRoot).Path
$destinationPath = [IO.Path]::GetFullPath($Destination)
if ([IO.File]::Exists($destinationPath)) { throw 'ZIP da ton tai. Chon Destination moi de giu ban cu.' }
$archive = [IO.Compression.ZipFile]::Open($destinationPath, [IO.Compression.ZipArchiveMode]::Create)
try {
    $files = Get-ChildItem -LiteralPath $sourceRoot -File -Recurse | Where-Object {
        $_.FullName -notmatch '[\\/](bin|obj|\.vs|\.git|App_Data|TestResults)[\\/]' -and
        $_.Name -notin 'appsettings.Local.json','secrets.json' -and
        $_.Name -notlike 'dependency-*.json' -and
        $_.Extension -notin '.zip','.log','.mdf','.ldf','.bak','.user','.suo','.trx'
    }
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($sourceRoot.Length).TrimStart('\','/').Replace('\','/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, ('AiInterview/' + $relative), [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}
finally { $archive.Dispose() }
Write-Host "Da tao ZIP source sach ($($files.Count) tep): $destinationPath"
Get-FileHash -LiteralPath $destinationPath -Algorithm SHA256 | Select-Object Hash
