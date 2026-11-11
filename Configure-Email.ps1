[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectDirectory = Join-Path $PSScriptRoot 'src\AiInterview.Web'
. (Join-Path $PSScriptRoot 'SecretStore.ps1')
$localPath = Get-InterviewSecretsPath
$basePath = Join-Path $projectDirectory 'appsettings.json'

function ConvertTo-EmailConfigMap {
    param([AllowNull()][object]$Value)
    if ($null -eq $Value) { return $null }
    if ($Value -is [System.Collections.IDictionary]) {
        $result = @{}
        foreach ($key in $Value.Keys) { $result[$key] = ConvertTo-EmailConfigMap $Value[$key] }
        return $result
    }
    if ($Value -is [pscustomobject]) {
        $result = @{}
        foreach ($property in $Value.PSObject.Properties) { $result[$property.Name] = ConvertTo-EmailConfigMap $property.Value }
        return $result
    }
    if ($Value -is [System.Collections.IEnumerable] -and $Value -isnot [string]) {
        $result = @()
        foreach ($item in $Value) { $result += ,(ConvertTo-EmailConfigMap $item) }
        return ,$result
    }
    return $Value
}

function Read-EmailConfigMap {
    param([AllowNull()][byte[]]$Bytes)
    if ($null -eq $Bytes) { return @{} }
    try {
        $json = [Text.Encoding]::UTF8.GetString($Bytes).TrimStart([char]0xFEFF)
        $parsed = $json | ConvertFrom-Json
    }
    catch { throw 'Tep cau hinh JSON khong hop le. Sua loi JSON roi chay lai; tep cu chua bi thay doi.' }
    finally { $json = $null }
    $result = ConvertTo-EmailConfigMap $parsed
    if ($result -is [System.Collections.IDictionary]) { $result = Expand-InterviewSecretKeys $result }
    if ($result -isnot [System.Collections.IDictionary]) {
        throw 'Cau hinh phai la JSON object. Tep cu chua bi thay doi.'
    }
    if ($result.ContainsKey('Email') -and $result['Email'] -isnot [System.Collections.IDictionary]) {
        throw 'Muc Email phai la JSON object. Tep cu chua bi thay doi.'
    }
    return $result
}

function Get-EmailConfigValue {
    param([string]$Name, [string]$Default)
    $value = $Default
    foreach ($config in @($baseConfig, $localConfig)) {
        if ($config.ContainsKey('Email') -and $config['Email'].ContainsKey($Name)) {
            $value = [string]$config['Email'][$Name]
        }
    }
    return $value
}

function Read-EmailSetting {
    param([string]$Prompt, [string]$Current)
    # Do not echo values read from a private config file, including in prompt defaults.
    $entered = Read-Host "$Prompt (Enter = giu nguyen/mac dinh)"
    if ($entered.Length -eq 0) { return $Current }
    return $entered
}

function Assert-EmailText {
    param([string]$Value, [string]$Name, [int]$MaximumLength = [int]::MaxValue)
    if ([string]::IsNullOrWhiteSpace($Value) -or $Value -match '\p{Cc}') {
        throw "$Name khong duoc trong hoac chua ky tu dieu khien. Tep cu chua bi thay doi."
    }
    if ($Value.Length -gt $MaximumLength) {
        throw "$Name toi da $MaximumLength ky tu. Tep cu chua bi thay doi."
    }
}

function Assert-SenderAddress {
    param([string]$Address)
    Assert-EmailText $Address 'Email nguoi gui' 256
    try { $parsedAddress = New-Object System.Net.Mail.MailAddress -ArgumentList $Address }
    catch { throw 'Dia chi email nguoi gui khong hop le. Tep cu chua bi thay doi.' }
    if ($parsedAddress.Address -cne $Address -or $Address -notmatch '^[^\s@]+@[^\s@]+\.[^\s@]+$') {
        throw 'Chi nhap dia chi email day du, khong kem ten hoac khoang trang. Tep cu chua bi thay doi.'
    }
}

function Get-ValidatedPublicBaseUrl {
    param([string]$Value)
    Assert-EmailText $Value 'PublicBaseUrl'
    $uri = $null
    # Check the original string too: URI parsing can normalize a path such as /a/.. to /.
    if ($Value -notmatch '^https?://[^/?#\\\s]+/?$' -or
        -not [Uri]::TryCreate($Value, [UriKind]::Absolute, [ref]$uri) -or
        -not [string]::IsNullOrEmpty($uri.UserInfo) -or
        -not [string]::IsNullOrEmpty($uri.Query) -or
        -not [string]::IsNullOrEmpty($uri.Fragment) -or
        $uri.AbsolutePath -ne '/' -or
        ($uri.Scheme -ne 'https' -and -not ($uri.Scheme -eq 'http' -and $uri.IsLoopback))) {
        throw 'PublicBaseUrl can la dia chi goc HTTPS, hoac HTTP loopback (vd http://localhost:5088); khong duong dan, userinfo, query hay fragment. Tep cu chua bi thay doi.'
    }
    return $uri.GetLeftPart([UriPartial]::Authority)
}

function Reveal-EmailPasswordForSave {
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

$temporaryPath = $null
$passwordInput = $null
$plainPassword = $null
$originalBytes = $null
$currentBytes = $null
$serializedConfig = $null
$localConfig = $null
$baseConfig = $null
try {
    $localExisted = [IO.File]::Exists($localPath)
    if ($localExisted) { $originalBytes = [IO.File]::ReadAllBytes($localPath) }
    $localConfig = Read-EmailConfigMap $originalBytes
    $baseBytes = if ([IO.File]::Exists($basePath)) { [IO.File]::ReadAllBytes($basePath) } else { $null }
    $baseConfig = Read-EmailConfigMap $baseBytes
    $baseBytes = $null

    Write-Host 'Cau hinh Gmail/Google Workspace cho InterviewLab tren may nay.'
    Write-Host 'SMTP: smtp.gmail.com, cong 587, StartTls. Username va FromAddress dung cung email nguoi gui.'
    Write-Host 'Bat xac minh 2 buoc trong Google va tao App Password; nhap ma khong co khoang trang.'
    Write-Host 'Mat khau duoc an khi nhap. Enter giu gia tri da co; Ctrl+C huy truoc khi luu.'
    Write-Host 'Mac dinh FromName: InterviewLab; PublicBaseUrl: http://localhost:5088.'
    Write-Host 'Tep user-secrets (ngoai source code) chua bi mat, chi dung cuc bo va da duoc loai khoi Git.'
    Write-Host 'Luu vao user-secrets; bien moi truong neu da dat se uu tien hon. Khong chia se thu muc UserSecrets.'

    $sender = Read-EmailSetting 'Email Gmail/Google Workspace nguoi gui' (Get-EmailConfigValue 'Username' (Get-EmailConfigValue 'FromAddress' ''))
    Assert-SenderAddress $sender
    $passwordInput = Read-Host 'Google App Password (Enter = giu mat khau da co)' -AsSecureString
    $plainPassword = if ($passwordInput.Length -gt 0) { Reveal-EmailPasswordForSave $passwordInput } else { Get-EmailConfigValue 'Password' '' }
    Assert-EmailText $plainPassword 'App Password'
    if ($plainPassword -match '\s') {
        throw 'App Password Gmail phai duoc nhap khong co khoang trang. Tep cu chua bi thay doi.'
    }
    $fromName = Read-EmailSetting 'Ten nguoi gui' (Get-EmailConfigValue 'FromName' 'InterviewLab')
    Assert-EmailText $fromName 'Ten nguoi gui' 120
    $publicBaseUrl = Get-ValidatedPublicBaseUrl (Read-EmailSetting 'Dia chi goc cua website (PublicBaseUrl)' (Get-EmailConfigValue 'PublicBaseUrl' 'http://localhost:5088'))

    if (-not $localConfig.ContainsKey('Email')) { $localConfig['Email'] = @{} }
    $localConfig['Email']['Enabled'] = $true
    $localConfig['Email']['Host'] = 'smtp.gmail.com'
    $localConfig['Email']['Port'] = 587
    $localConfig['Email']['Security'] = 'StartTls'
    $localConfig['Email']['Username'] = $sender
    $localConfig['Email']['Password'] = $plainPassword
    $localConfig['Email']['FromAddress'] = $sender
    $localConfig['Email']['FromName'] = $fromName
    $localConfig['Email']['PublicBaseUrl'] = $publicBaseUrl

    # Keep temporary secret material beside user-secrets, outside the source tree.
    $temporaryDirectory = Split-Path -Parent $localPath
    if (-not [IO.Directory]::Exists($temporaryDirectory)) { [IO.Directory]::CreateDirectory($temporaryDirectory) | Out-Null }
    $temporaryPath = Join-Path $temporaryDirectory ('.email-config-' + [Guid]::NewGuid().ToString('N') + '.json')
    $serializedConfig = $localConfig | ConvertTo-Json -Depth 100
    [IO.File]::WriteAllText($temporaryPath, $serializedConfig, (New-Object Text.UTF8Encoding($false)))

    # Refuse to overwrite edits made while the prompts were open.
    if ([IO.File]::Exists($localPath) -ne $localExisted) {
        throw 'Tep local da thay doi trong khi nhap. Chay lai de giu cau hinh moi; chua ghi de tep.'
    }
    if ($localExisted) {
        $currentBytes = [IO.File]::ReadAllBytes($localPath)
        if ([Convert]::ToBase64String($currentBytes) -cne [Convert]::ToBase64String($originalBytes)) {
            throw 'Tep local da thay doi trong khi nhap. Chay lai de giu cau hinh moi; chua ghi de tep.'
        }
        # NullString avoids PowerShell 5.1 coercing a null backup path into an empty string.
        [IO.File]::Replace($temporaryPath, $localPath, [NullString]::Value)
    }
    else {
        [IO.File]::Move($temporaryPath, $localPath)
    }
    $temporaryPath = $null
    Write-Host 'Da luu cau hinh email cuc bo. Khoi dong lai bang .\Run.ps1 de ap dung.'
    Write-Host 'Chua gui email thu. Dang ky bang hop thu ban so huu de kiem tra thu xac nhan.'
}
finally {
    if ($temporaryPath -and [IO.File]::Exists($temporaryPath)) { Remove-Item -LiteralPath $temporaryPath -Force }
    if ($null -ne $passwordInput) { $passwordInput.Dispose() }
    $plainPassword = $null; $serializedConfig = $null; $localConfig = $null; $baseConfig = $null
    $originalBytes = $null; $currentBytes = $null; $baseBytes = $null
}
