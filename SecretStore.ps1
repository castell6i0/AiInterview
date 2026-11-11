# Development credentials live outside the source/ZIP. Never print serialized configuration.
function Get-InterviewSecretsPath {
    $secretId = 'AiInterview-Local-2026'
    $folder = Join-Path ([Environment]::GetFolderPath('ApplicationData')) ('Microsoft\UserSecrets\' + $secretId)
    if (-not [IO.Directory]::Exists($folder)) { [IO.Directory]::CreateDirectory($folder) | Out-Null }
    return Join-Path $folder 'secrets.json'
}

function Expand-InterviewSecretKeys {
    param([System.Collections.IDictionary]$Config)
    $result = @{}
    foreach ($key in $Config.Keys) {
        if ([string]$key -notmatch ':') { $result[$key] = $Config[$key] }
    }
    foreach ($key in $Config.Keys) {
        if ([string]$key -notmatch ':') { continue }
        $parts = ([string]$key).Split(':')
        $section = $result
        for ($i = 0; $i -lt $parts.Length - 1; $i++) {
            if (-not $section.Contains($parts[$i])) { $section[$parts[$i]] = @{} }
            if ($section[$parts[$i]] -isnot [System.Collections.IDictionary]) { throw 'User-secrets co khoa trung khong hop le. Chua ghi de.' }
            $section = $section[$parts[$i]]
        }
        $section[$parts[-1]] = $Config[$key]
    }
    return $result
}
