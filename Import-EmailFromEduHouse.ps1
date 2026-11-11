[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$ArchivePath,
    [string]$PublicBaseUrl = 'http://localhost:5088'
)

$ErrorActionPreference = 'Stop'
$projectDirectory = Join-Path $PSScriptRoot 'src\AiInterview.Web'
. (Join-Path $PSScriptRoot 'SecretStore.ps1')
$localPath = Get-InterviewSecretsPath

function ConvertTo-ImportedConfigMap {
    param([AllowNull()][object]$Value)
    if ($null -eq $Value) { return $null }
    if ($Value -is [System.Collections.IDictionary]) {
        $result = @{}
        foreach ($key in $Value.Keys) { $result[$key] = ConvertTo-ImportedConfigMap $Value[$key] }
        return $result
    }
    if ($Value -is [System.Collections.IEnumerable] -and $Value -isnot [string]) {
        $result = @()
        foreach ($item in $Value) { $result += ,(ConvertTo-ImportedConfigMap $item) }
        return ,$result
    }
    if ($Value -is [pscustomobject]) {
        $result = @{}
        foreach ($property in $Value.PSObject.Properties) { $result[$property.Name] = ConvertTo-ImportedConfigMap $property.Value }
        return $result
    }
    return $Value
}

function Read-ImportedLocalConfig {
    param([AllowNull()][byte[]]$Bytes)
    if ($null -eq $Bytes) { return @{} }
    try {
        $json = [Text.Encoding]::UTF8.GetString($Bytes).TrimStart([char]0xFEFF)
        if ($json -notmatch '^\s*\{') { throw 'JSON root must be an object.' }
        $parsed = $json | ConvertFrom-Json
    }
    catch { throw 'Tep local khong phai JSON hop le. Tep cu chua bi thay doi.' }
    finally { $json = $null }
    $result = ConvertTo-ImportedConfigMap $parsed
    if ($result -is [System.Collections.IDictionary]) { $result = Expand-InterviewSecretKeys $result }
    if ($result -isnot [System.Collections.IDictionary]) {
        throw 'Cau hinh local phai la JSON object. Tep cu chua bi thay doi.'
    }
    if ($result.ContainsKey('Email') -and $result['Email'] -isnot [System.Collections.IDictionary]) {
        throw 'Muc Email phai la JSON object. Tep cu chua bi thay doi.'
    }
    return $result
}

function Assert-ImportedText {
    param([string]$Value, [string]$Name, [int]$MaximumLength = [int]::MaxValue)
    if ([string]::IsNullOrWhiteSpace($Value) -or $Value -match '\p{Cc}' -or $Value.Length -gt $MaximumLength) {
        throw "$Name trong, qua dai hoac chua ky tu dieu khien. Tep cu chua bi thay doi."
    }
}

function Get-ImportedPublicBaseUrl {
    param([string]$Value)
    Assert-ImportedText $Value 'PublicBaseUrl'
    $uri = $null
    if ($Value -notmatch '^https?://[^/?#\\\s]+/?$' -or
        -not [Uri]::TryCreate($Value, [UriKind]::Absolute, [ref]$uri) -or
        -not [string]::IsNullOrEmpty($uri.UserInfo) -or
        -not [string]::IsNullOrEmpty($uri.Query) -or
        -not [string]::IsNullOrEmpty($uri.Fragment) -or
        $uri.AbsolutePath -ne '/' -or
        ($uri.Scheme -ne 'https' -and -not ($uri.Scheme -eq 'http' -and $uri.IsLoopback))) {
        throw 'PublicBaseUrl can la dia chi goc HTTPS hoac HTTP loopback, khong duong dan, userinfo, query hay fragment. Tep cu chua bi thay doi.'
    }
    return $uri.GetLeftPart([UriPartial]::Authority)
}

function Test-ImportedPlaceholder {
    param([string]$Value)
    # Detect obvious template markers without changing the credential that will be saved.
    return ($Value -match '(?i)^(?:\s*(?:change[-_ ]?me|replace[-_ ]?me|your[-_ ]?(?:email|gmail|password|app[-_ ]?password|smtp[-_ ]?password)|app[-_ ]?password|password|placeholder|redacted|todo|x{3,}|\*{3,}|\.\.\.)\s*)(?:@.*)?$' -or
        $Value -match '^[<\[].*[>\]]$')
}

function Assert-ImportedSender {
    param([string]$Address)
    Assert-ImportedText $Address 'MailUser' 256
    if (Test-ImportedPlaceholder $Address) { throw 'MailUser van la gia tri mau. Tep cu chua bi thay doi.' }
    try { $mailbox = New-Object System.Net.Mail.MailAddress -ArgumentList $Address }
    catch { throw 'MailUser khong phai dia chi email hop le. Tep cu chua bi thay doi.' }
    if ($mailbox.Address -cne $Address -or $Address -notmatch '^[^\s@]+@[^\s@]+\.[^\s@]+$') {
        throw 'MailUser phai la dia chi email day du, khong kem ten hoac khoang trang. Tep cu chua bi thay doi.'
    }
}

function Read-EduHouseMailSettings {
    param([object]$Entry)
    if ($Entry.Length -gt 2097152) {
        throw 'Web.config trong archive vuot gioi han 2 MB. Tep cu chua bi thay doi.'
    }
    $stream = $null
    $reader = $null
    $document = $null
    try {
        $stream = $Entry.Open()
        $readerSettings = New-Object System.Xml.XmlReaderSettings
        $readerSettings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
        $readerSettings.XmlResolver = $null
        $readerSettings.MaxCharactersInDocument = 2097152
        $reader = [System.Xml.XmlReader]::Create($stream, $readerSettings)
        $document = New-Object System.Xml.XmlDocument
        $document.XmlResolver = $null
        # Never include parser exceptions in output: XML may contain private values.
        try { $document.Load($reader) }
        catch { throw 'Web.config khong phai XML an toan/hop le. Tep cu chua bi thay doi.' }
        $sections = $document.SelectNodes('/configuration/appSettings')
        if ($sections.Count -gt 1) { throw 'Web.config co nhieu muc appSettings. Tep cu chua bi thay doi.' }
        $settings = @{}
        if ($sections.Count -eq 1) {
            if ($sections[0].HasAttribute('file') -or $sections[0].HasAttribute('configSource')) {
                throw 'appSettings tham chieu tep ngoai; can cau hinh truc tiep trong Web.config. Tep cu chua bi thay doi.'
            }
            foreach ($node in $sections[0].SelectNodes('add')) {
                $key = $node.GetAttribute('key')
                if ($key -in @('MailMode', 'MailUser', 'MailPassword')) {
                    if ($settings.ContainsKey($key)) { throw 'Web.config co khoa email bi trung. Tep cu chua bi thay doi.' }
                    $settings[$key] = $node.GetAttribute('value')
                }
            }
        }
        return $settings
    }
    finally {
        if ($null -ne $reader) { $reader.Dispose() }
        if ($null -ne $stream) { $stream.Dispose() }
        $document = $null
    }
}

function Get-EduHouseMailSettings {
    param([string]$Path)
    $archive = $null
    try {
        try { $archive = [System.IO.Compression.ZipFile]::OpenRead($Path) }
        catch { throw 'Khong mo duoc archive ZIP. Tep cu chua bi thay doi.' }
        $exact = @($archive.Entries | Where-Object { $_.FullName.Replace('\', '/') -ieq 'EduHouse/EduHouse/Web.config' })
        if ($exact.Count -gt 1) { throw 'Archive co nhieu Web.config cung duong dan EduHouse. Tep cu chua bi thay doi.' }
        if ($exact.Count -eq 1) { return Read-EduHouseMailSettings $exact[0] }
        $matchingSettings = @()
        foreach ($entry in $archive.Entries) {
            if ($entry.FullName.Replace('\', '/') -match '(^|/)Web\.config$') {
                $settings = Read-EduHouseMailSettings $entry
                if ($settings.ContainsKey('MailMode') -and $settings.ContainsKey('MailUser') -and $settings.ContainsKey('MailPassword')) {
                    $matchingSettings += ,$settings
                }
            }
        }
        if ($matchingSettings.Count -ne 1) {
            throw 'Can dung mot Web.config chua MailMode, MailUser va MailPassword; khong tim thay hoac archive mo ho. Tep cu chua bi thay doi.'
        }
        return $matchingSettings[0]
    }
    finally {
        if ($null -ne $archive) { $archive.Dispose() }
    }
}

$temporaryPath = $null
$originalBytes = $null
$currentBytes = $null
$localConfig = $null
$mailSettings = $null
$sender = $null
$password = $null
$serializedConfig = $null
try {
    if (-not [IO.Directory]::Exists($projectDirectory)) {
        throw 'Khong tim thay src\AiInterview.Web. Dat script trong thu muc goc InterviewLab.'
    }
    $validatedBaseUrl = Get-ImportedPublicBaseUrl $PublicBaseUrl
    $localExisted = [IO.File]::Exists($localPath)
    if ($localExisted) { $originalBytes = [IO.File]::ReadAllBytes($localPath) }
    $localConfig = Read-ImportedLocalConfig $originalBytes
    Write-Host 'Dang nhap cau hinh SMTP tu archive EduHouse; khong hien thi thong tin dang nhap.'

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    try { $resolvedArchive = (Resolve-Path -LiteralPath $ArchivePath -ErrorAction Stop).ProviderPath }
    catch { throw 'Khong tim thay archive ZIP. Tep cu chua bi thay doi.' }
    $mailSettings = Get-EduHouseMailSettings $resolvedArchive
    foreach ($key in @('MailMode', 'MailUser', 'MailPassword')) {
        if (-not $mailSettings.ContainsKey($key)) { throw 'Web.config thieu khoa email bat buoc. Tep cu chua bi thay doi.' }
    }
    if ([string]$mailSettings['MailMode'] -ine 'smtp') {
        throw 'Chi nhap MailMode=smtp; khong chuyen che do pickup/demo sang gui thu that. Tep cu chua bi thay doi.'
    }
    $sender = [string]$mailSettings['MailUser']
    Assert-ImportedSender $sender
    $password = [string]$mailSettings['MailPassword']
    Assert-ImportedText $password 'MailPassword'
    if (Test-ImportedPlaceholder $password) { throw 'MailPassword van la gia tri mau. Tep cu chua bi thay doi.' }
    # Google displays a 16-letter App Password in four groups. Normalize only that exact display form.
    if ($password -cmatch '^[a-zA-Z]{4}( [a-zA-Z]{4}){3}$') { $password = $password.Replace(' ', '') }

    if (-not $localConfig.ContainsKey('Email')) { $localConfig['Email'] = @{} }
    $localConfig['Email']['Enabled'] = $true
    $localConfig['Email']['Host'] = 'smtp.gmail.com'
    $localConfig['Email']['Port'] = 587
    $localConfig['Email']['Security'] = 'StartTls'
    $localConfig['Email']['Username'] = $sender
    $localConfig['Email']['Password'] = $password
    $localConfig['Email']['FromAddress'] = $sender
    $localConfig['Email']['FromName'] = 'InterviewLab'
    $localConfig['Email']['PublicBaseUrl'] = $validatedBaseUrl

    # Secret material is written outside the source tree.
    $temporaryDirectory = Split-Path -Parent $localPath
    if (-not [IO.Directory]::Exists($temporaryDirectory)) { [IO.Directory]::CreateDirectory($temporaryDirectory) | Out-Null }
    $temporaryPath = Join-Path $temporaryDirectory ('.edu-email-import-' + [Guid]::NewGuid().ToString('N') + '.json')
    $serializedConfig = $localConfig | ConvertTo-Json -Depth 100
    [IO.File]::WriteAllText($temporaryPath, $serializedConfig, (New-Object Text.UTF8Encoding($false)))

    if ([IO.File]::Exists($localPath) -ne $localExisted) { throw 'Tep local da thay doi trong khi nhap. Chua ghi de; hay chay lai.' }
    if ($localExisted) {
        $currentBytes = [IO.File]::ReadAllBytes($localPath)
        if ([Convert]::ToBase64String($currentBytes) -cne [Convert]::ToBase64String($originalBytes)) {
            throw 'Tep local da thay doi trong khi nhap. Chua ghi de; hay chay lai.'
        }
        [IO.File]::Replace($temporaryPath, $localPath, [NullString]::Value)
    }
    else { [IO.File]::Move($temporaryPath, $localPath) }
    $temporaryPath = $null
    Write-Host 'Da nhap SMTP vao user-secrets (ngoai source code) va giu cac muc cau hinh khac. Khoi dong lai ung dung de ap dung.'
    Write-Host 'Chua ket noi SMTP hay gui email thu. Khong chia se tep local chua bi mat.'
}
finally {
    if ($temporaryPath -and [IO.File]::Exists($temporaryPath)) { Remove-Item -LiteralPath $temporaryPath -Force }
    $password = $null; $sender = $null; $mailSettings = $null; $serializedConfig = $null; $localConfig = $null
    $originalBytes = $null; $currentBytes = $null
}
