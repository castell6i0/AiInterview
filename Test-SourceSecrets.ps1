[CmdletBinding()]
param([string]$Root)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) { $Root = $PSScriptRoot }
$rootPath = (Resolve-Path -LiteralPath $Root).Path
$findings = New-Object System.Collections.Generic.List[string]
$files = Get-ChildItem -LiteralPath $rootPath -Recurse -File | Where-Object {
    $_.FullName -notmatch '[\\/](bin|obj|\.git|\.vs|TestResults|App_Data)[\\/]' -and
    $_.Extension -in '.cs','.csproj','.json','.md','.js','.cjs','.ps1','.yml','.yaml','.config'
}
foreach ($file in $files) {
    $relative = $file.FullName.Substring($rootPath.Length).TrimStart('\','/')
    if ($file.Name -in 'appsettings.Local.json','secrets.json') { $findings.Add("Private configuration: $relative"); continue }
    $body = [IO.File]::ReadAllText($file.FullName)
    # Report file/rule only: never print the matched credential.
    if ($body -match 'AIza[0-9A-Za-z_-]{30,}' -or $body -match 'sk-(?:proj-|svcacct-)?[A-Za-z0-9_-]{30,}' -or
        $body -match '-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----') { $findings.Add("Credential pattern: $relative") }
    if ($file.Name -like 'appsettings*.json') {
        $json = $body | ConvertFrom-Json
        foreach ($section in @('Gemini','OpenAI','Email','Seed')) {
            foreach ($key in @('ApiKey','Password')) {
                if ($json.$section -and -not [string]::IsNullOrWhiteSpace([string]$json.$section.$key)) { $findings.Add("Nonempty secret setting: $relative ($section/$key)") }
            }
        }
        foreach ($entry in $json.ConnectionStrings.PSObject.Properties) {
            if ([string]$entry.Value -match '(?i)(password|pwd|user id|uid)\s*=' -or
                (-not [string]::IsNullOrWhiteSpace([string]$entry.Value) -and [string]$entry.Value -notmatch '^Server=\(localdb\)\\MSSQLLocalDB;Database=AiInterview;Trusted_Connection=True;')) {
                $findings.Add("Non-template connection: $relative")
            }
        }
    }
}
if ($findings.Count -gt 0) { $findings | ForEach-Object { Write-Host $_ }; throw "Secret review failed: $($findings.Count) finding(s). Values were not printed." }
Write-Host "Secret review passed: $($files.Count) source files; no matched credentials or private configuration."
Write-Host 'Pattern scanning cannot prove absence of every secret. No historical Git repository was included with the supplied source.'
