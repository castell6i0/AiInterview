[CmdletBinding()]
param([string]$LocalPath)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($LocalPath)) { $LocalPath = Join-Path $PSScriptRoot 'src\AiInterview.Web\appsettings.Local.json' }
. (Join-Path $PSScriptRoot 'SecretStore.ps1')

function ConvertTo-MigrationMap {
    param([AllowNull()][object]$Value)
    if ($null -eq $Value) { return $null }
    if ($Value -is [System.Collections.IDictionary]) {
        $result = @{}
        foreach ($key in $Value.Keys) { $result[$key] = ConvertTo-MigrationMap $Value[$key] }
        return $result
    }
    if ($Value -is [pscustomobject]) {
        $result = @{}
        foreach ($property in $Value.PSObject.Properties) { $result[$property.Name] = ConvertTo-MigrationMap $property.Value }
        return $result
    }
    if ($Value -is [System.Collections.IEnumerable] -and $Value -isnot [string]) {
        $result = @()
        foreach ($item in $Value) { $result += ,(ConvertTo-MigrationMap $item) }
        return ,$result
    }
    return $Value
}
function Read-MigrationMap {
    param([AllowNull()][byte[]]$Bytes)
    if ($null -eq $Bytes) { return @{} }
    try {
        $text = [Text.Encoding]::UTF8.GetString($Bytes).TrimStart([char]0xFEFF)
        if ($text -notmatch '^\s*\{') { throw 'Invalid root' }
        $map = ConvertTo-MigrationMap ($text | ConvertFrom-Json)
    }
    catch { throw 'JSON cau hinh khong hop le. Khong hien thi noi dung va khong xoa tep nguon.' }
    finally { $text = $null }
    if ($map -isnot [System.Collections.IDictionary]) { throw 'Cau hinh phai la JSON object. Khong xoa tep nguon.' }
    return Expand-InterviewSecretKeys $map
}
function Merge-MissingMigrationValues {
    param([System.Collections.IDictionary]$Target, [System.Collections.IDictionary]$Source)
    foreach ($key in $Source.Keys) {
        if (-not $Target.Contains($key)) { $Target[$key] = $Source[$key] }
        elseif ($Target[$key] -is [System.Collections.IDictionary] -and $Source[$key] -is [System.Collections.IDictionary]) {
            Merge-MissingMigrationValues $Target[$key] $Source[$key]
        }
        # Existing user-secrets always win, including intentionally empty/null values.
    }
}
function Test-MigrationBytes {
    param([byte[]]$First, [byte[]]$Second)
    return [Convert]::ToBase64String($First) -ceq [Convert]::ToBase64String($Second)
}

$temporaryPath = $null
$sourceBytes = $null; $originalBytes = $null; $writtenBytes = $null; $localConfig = $null; $secrets = $null
try {
    $sourcePath = [IO.Path]::GetFullPath($LocalPath)
    if ([IO.Path]::GetFileName($sourcePath) -ine 'appsettings.Local.json' -or -not [IO.File]::Exists($sourcePath)) {
        throw 'Chi chuyen mot tep appsettings.Local.json da ton tai. Khong thay doi tep nao.'
    }
    $sourceItem = Get-Item -LiteralPath $sourcePath -Force
    if (($sourceItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'Khong chuyen tep lien ket. Hay cung cap duong dan tep cau hinh goc.'
    }
    $secretsPath = [IO.Path]::GetFullPath((Get-InterviewSecretsPath))
    if ($sourcePath.Equals($secretsPath, [StringComparison]::OrdinalIgnoreCase)) { throw 'Tep nguon va dich phai khac nhau.' }
    $secretExisted = [IO.File]::Exists($secretsPath)
    $sourceBytes = [IO.File]::ReadAllBytes($sourcePath)
    if ($secretExisted) { $originalBytes = [IO.File]::ReadAllBytes($secretsPath) }
    $localConfig = Read-MigrationMap $sourceBytes
    $secrets = Read-MigrationMap $originalBytes
    Merge-MissingMigrationValues $secrets $localConfig
    $serialized = $secrets | ConvertTo-Json -Depth 100
    $temporaryPath = Join-Path (Split-Path -Parent $secretsPath) ('.migrate-secrets-' + [Guid]::NewGuid().ToString('N') + '.json')
    [IO.File]::WriteAllText($temporaryPath, $serialized, (New-Object Text.UTF8Encoding($false)))
    $writtenBytes = [IO.File]::ReadAllBytes($temporaryPath)

    if (-not [IO.File]::Exists($sourcePath) -or -not (Test-MigrationBytes ([IO.File]::ReadAllBytes($sourcePath)) $sourceBytes)) {
        throw 'Tep local da thay doi trong luc chuyen. Chua ghi user-secrets; khong xoa tep nguon.'
    }
    if ([IO.File]::Exists($secretsPath) -ne $secretExisted) { throw 'User-secrets da thay doi. Khong ghi de va khong xoa tep nguon.' }
    if ($secretExisted) {
        if (-not (Test-MigrationBytes ([IO.File]::ReadAllBytes($secretsPath)) $originalBytes)) {
            throw 'User-secrets da thay doi. Khong ghi de va khong xoa tep nguon.'
        }
        [IO.File]::Replace($temporaryPath, $secretsPath, [NullString]::Value)
    }
    else { [IO.File]::Move($temporaryPath, $secretsPath) }
    $temporaryPath = $null
    if (-not (Test-MigrationBytes ([IO.File]::ReadAllBytes($secretsPath)) $writtenBytes)) {
        throw 'Chua xac minh duoc ban user-secrets da ghi. Giu nguyen tep local.'
    }
    if (-not [IO.File]::Exists($sourcePath) -or -not (Test-MigrationBytes ([IO.File]::ReadAllBytes($sourcePath)) $sourceBytes)) {
        throw 'Da luu user-secrets, nhung tep local vua thay doi. Giu tep local de ban kiem tra.'
    }
    # Delete exactly the validated input file, never a directory or wildcard.
    [IO.File]::Delete($sourcePath)
    Write-Host 'Da chuyen cau hinh vao user-secrets ngoai source va xoa dung tep local vua chuyen.'
    Write-Host 'Gia tri da co trong user-secrets duoc uu tien. Khoi dong lai ung dung de ap dung.'
    Write-Host 'Viec nay khong thu hoi key, khong doi mat khau tai khoan cu va khong sua cac ZIP/ban sao cu.'
}
finally {
    if ($temporaryPath -and [IO.File]::Exists($temporaryPath)) { Remove-Item -LiteralPath $temporaryPath -Force }
    $sourceBytes = $null; $originalBytes = $null; $writtenBytes = $null; $localConfig = $null; $secrets = $null; $serialized = $null
}
