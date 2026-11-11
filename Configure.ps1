[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectDirectory = Join-Path $PSScriptRoot 'src\AiInterview.Web'
. (Join-Path $PSScriptRoot 'SecretStore.ps1')
$localPath = Get-InterviewSecretsPath
$basePath = Join-Path $projectDirectory 'appsettings.json'

function ConvertTo-ConfigMap {
    param([AllowNull()][object]$Value)
    if ($null -eq $Value) { return $null }
    if ($Value -is [System.Collections.IDictionary]) {
        $result = @{}
        foreach ($key in $Value.Keys) { $result[$key] = ConvertTo-ConfigMap $Value[$key] }
        return $result
    }
    if ($Value -is [pscustomobject]) {
        $result = @{}
        foreach ($property in $Value.PSObject.Properties) { $result[$property.Name] = ConvertTo-ConfigMap $property.Value }
        return $result
    }
    if ($Value -is [System.Collections.IEnumerable] -and $Value -isnot [string]) {
        $result = @()
        foreach ($item in $Value) { $result += ,(ConvertTo-ConfigMap $item) }
        return ,$result
    }
    return $Value
}

function Read-ConfigMap {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return @{} }
    try { $parsed = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json }
    catch { throw "Tep JSON khong hop le: $Path. Giu nguyen tep, sua loi JSON roi chay lai." }
    $result = ConvertTo-ConfigMap $parsed
    if ($result -is [System.Collections.IDictionary]) { $result = Expand-InterviewSecretKeys $result }
    if ($result -isnot [System.Collections.IDictionary]) { throw "Cau hinh phai la mot JSON object: $Path" }
    return $result
}

function Ensure-ConfigSection {
    param([hashtable]$Config, [string]$Name)
    if (-not $Config.ContainsKey($Name)) { $Config[$Name] = @{} }
    if ($Config[$Name] -isnot [System.Collections.IDictionary]) { throw "Muc '$Name' phai la JSON object. Tep cu chua bi thay doi." }
}

function Reveal-ForLocalSave {
    param([System.Security.SecureString]$Value)
    $pointer = [IntPtr]::Zero
    try {
        $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Value)
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    }
    finally {
        if ($pointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
    }
}

$localExisted = [IO.File]::Exists($localPath)
$originalBytes = if ($localExisted) { [IO.File]::ReadAllBytes($localPath) } else { $null }
$localConfig = Read-ConfigMap $localPath
$baseConfig = Read-ConfigMap $basePath
Write-Host 'Cau hinh AiInterview tren may nay. Enter de giu gia tri cu.'
Write-Host 'Key, mat khau va connection string khong duoc hien thi khi nhap.'
Write-Host 'Tep user-secrets (ngoai source code) chi dung cuc bo, da duoc loai khoi Git. Khong gui tep nay cho nguoi khac.'
Write-Host 'Luu vao user-secrets; bien moi truong neu da dat se uu tien hon. Khong chia se thu muc UserSecrets.'

$currentProvider = 'Gemini'
foreach ($sourceConfig in @($baseConfig, $localConfig)) {
    if ($sourceConfig.ContainsKey('AI') -and $sourceConfig['AI'] -is [System.Collections.IDictionary] -and $sourceConfig['AI'].ContainsKey('Provider')) {
        $currentProvider = [string]$sourceConfig['AI']['Provider']
    }
}
$providerInput = Read-Host "Nha cung cap AI: Gemini hoac OpenAI [$currentProvider] (Enter = giu nguyen)"
$provider = if ([string]::IsNullOrWhiteSpace($providerInput)) { $currentProvider } else { $providerInput.Trim() }
if ($provider -ieq 'Gemini') { $provider = 'Gemini' }
elseif ($provider -ieq 'OpenAI') { $provider = 'OpenAI' }
else { throw 'Nha cung cap phai la Gemini hoac OpenAI. Tep cu chua bi thay doi.' }
Write-Host "Dang cau hinh $provider. Key cua nha cung cap khac van duoc giu nguyen."
$apiKeyInput = Read-Host "$provider API key (Enter = giu nguyen key cua $provider)" -AsSecureString
$demoPasswordInput = Read-Host 'Mat khau mau: >=10 ky tu, hoa/thuong/so/ky tu dac biet (Enter = giu nguyen)' -AsSecureString
$connectionInput = Read-Host 'Connection string SQL Server (Enter = giu nguyen, mac dinh LocalDB)' -AsSecureString
$currentModel = if ($provider -eq 'OpenAI') { 'gpt-4.1-mini' } else { 'gemini-3.1-flash-lite' }
foreach ($sourceConfig in @($baseConfig, $localConfig)) {
    if ($sourceConfig.ContainsKey($provider) -and $sourceConfig[$provider] -is [System.Collections.IDictionary] -and $sourceConfig[$provider].ContainsKey('Model')) {
        $currentModel = [string]$sourceConfig[$provider]['Model']
    }
}
$modelInput = Read-Host "Model $provider [$currentModel] (Enter = giu nguyen)"

$temporaryPath = $null
$plainKey = $null
$plainPassword = $null
$plainConnection = $null
try {
    Ensure-ConfigSection $localConfig 'AI'
    $localConfig['AI']['Provider'] = $provider
    # Unwrap secrets only for this authorized local save; never pass them on a command line.
    if ($apiKeyInput.Length -gt 0) {
        $plainKey = Reveal-ForLocalSave $apiKeyInput
        if ([string]::IsNullOrWhiteSpace($plainKey)) { throw 'API key khong duoc chi gom khoang trang. Tep cu chua bi thay doi.' }
        Ensure-ConfigSection $localConfig $provider
        $localConfig[$provider]['ApiKey'] = $plainKey.Trim()
    }
    if ($demoPasswordInput.Length -gt 0) {
        $plainPassword = Reveal-ForLocalSave $demoPasswordInput
        if ($plainPassword.Length -lt 10 -or $plainPassword -cnotmatch '[A-Z]' -or $plainPassword -cnotmatch '[a-z]' -or $plainPassword -notmatch '[0-9]' -or $plainPassword -notmatch '[^a-zA-Z0-9]') {
            throw 'Mat khau mau can >=10 ky tu, chu hoa, chu thuong, so va ky tu dac biet. Tep cu chua bi thay doi.'
        }
        Ensure-ConfigSection $localConfig 'Seed'
        $localConfig['Seed']['Password'] = $plainPassword
    }
    if ($connectionInput.Length -gt 0) {
        $plainConnection = Reveal-ForLocalSave $connectionInput
        if ([string]::IsNullOrWhiteSpace($plainConnection)) { throw 'Connection string khong duoc chi gom khoang trang. Tep cu chua bi thay doi.' }
        Ensure-ConfigSection $localConfig 'ConnectionStrings'
        $localConfig['ConnectionStrings']['DefaultConnection'] = $plainConnection.Trim()
    }
    if (-not [string]::IsNullOrWhiteSpace($modelInput)) {
        Ensure-ConfigSection $localConfig $provider
        $localConfig[$provider]['Model'] = $modelInput.Trim()
    }

    # Temporary credentials stay in the user profile outside the source tree.
    $temporaryDirectory = Split-Path -Parent $localPath
    if (-not (Test-Path -LiteralPath $temporaryDirectory)) { New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null }
    $temporaryPath = Join-Path $temporaryDirectory ('.local-config-' + [Guid]::NewGuid().ToString('N') + '.json')
    $serializedConfig = $localConfig | ConvertTo-Json -Depth 100
    [IO.File]::WriteAllText($temporaryPath, $serializedConfig, (New-Object Text.UTF8Encoding($false)))
    # Preserve changes made by another setup window while these prompts were open.
    if ([IO.File]::Exists($localPath) -ne $localExisted) {
        throw 'User-secrets da thay doi trong khi nhap. Chua ghi de; hay chay lai.'
    }
    if ($localExisted) {
        $currentBytes = [IO.File]::ReadAllBytes($localPath)
        if ([Convert]::ToBase64String($currentBytes) -cne [Convert]::ToBase64String($originalBytes)) {
            throw 'User-secrets da thay doi trong khi nhap. Chua ghi de; hay chay lai.'
        }
        [IO.File]::Replace($temporaryPath, $localPath, [NullString]::Value)
    }
    else { [IO.File]::Move($temporaryPath, $localPath) }
    $temporaryPath = $null
    Write-Host 'Da luu cau hinh cuc bo. Chay .\Run.ps1 de khoi dong.'
    Write-Host 'Doi Seed:Password chi anh huong tai khoan mau chua ton tai; khong reset mat khau tai khoan cu.'
}
finally {
    if ($temporaryPath -and (Test-Path -LiteralPath $temporaryPath)) { Remove-Item -LiteralPath $temporaryPath -Force }
    $plainKey = $null; $plainPassword = $null; $plainConnection = $null; $serializedConfig = $null; $localConfig = $null
    $originalBytes = $null; $currentBytes = $null
    $apiKeyInput.Dispose(); $demoPasswordInput.Dispose(); $connectionInput.Dispose()
}
