[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$sourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('InterviewLab-secrets-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$passed = 0
function Assert-Test([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Write-Json([string]$Path, [object]$Value) {
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 20), (New-Object Text.UTF8Encoding($false)))
}
function New-Fixture {
    $root = Join-Path $testRoot ([Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path (Join-Path $root 'src\AiInterview.Web'), (Join-Path $root 'outside-source') -Force | Out-Null
    foreach ($name in @('Configure.ps1', 'Migrate-LocalSecrets.ps1', 'SecretStore.ps1')) { Copy-Item -LiteralPath (Join-Path $sourceRoot $name) -Destination $root }
    $secret = Join-Path $root 'outside-source\secrets.json'
    # Mock the path resolver only in this isolated copy. Never call the real resolver.
    $fake = "`nfunction Get-InterviewSecretsPath { return '" + $secret.Replace("'", "''") + "' }`n"
    Add-Content -LiteralPath (Join-Path $root 'SecretStore.ps1') -Value $fake -Encoding UTF8
    Write-Json (Join-Path $root 'src\AiInterview.Web\appsettings.json') @{ AI = @{ Provider = 'Gemini' }; Gemini = @{ Model = 'synthetic-model' } }
    return @{ Root=$root; Secret=$secret; Local=(Join-Path $root 'src\AiInterview.Web\appsettings.Local.json') }
}
function Run-Test([string]$Name, [scriptblock]$Body) {
    & $Body
    $script:passed++
    Write-Host "PASS $Name"
}
function Assert-NoSecretOutput($Output) {
    Assert-Test (-not (($Output | Out-String) -match 'synthetic-private|synthetic-new-key')) 'Script printed a synthetic secret.'
}
function Add-ConcurrentMutation($Fixture, [string]$Target) {
    $path = $Target.Replace("'", "''")
    $hook = @'
$script:OriginalExpand = (Get-Command Expand-InterviewSecretKeys).ScriptBlock
$script:ExpandCalls = 0
function Expand-InterviewSecretKeys {
    param([System.Collections.IDictionary]$Config)
    $result = & $script:OriginalExpand $Config
    $script:ExpandCalls++
    if ($script:ExpandCalls -eq 2) { [IO.File]::WriteAllText('__TARGET__', '{"Concurrent":"new-value"}') }
    return $result
}
'@
    Add-Content -LiteralPath (Join-Path $Fixture.Root 'SecretStore.ps1') -Value $hook.Replace('__TARGET__', $path) -Encoding UTF8
}
try {
    Run-Test 'SecretStore expands flat keys without losing nested unrelated values' {
        $f=New-Fixture
        . (Join-Path $f.Root 'SecretStore.ps1')
        $map=Expand-InterviewSecretKeys @{ 'Email:Password'='synthetic-private-mail'; Email=@{Host='smtp.example.invalid'}; 'Gemini:ApiKey'='synthetic-private-ai' }
        Assert-Test ($map.Email.Host -eq 'smtp.example.invalid' -and $map.Email.Password -eq 'synthetic-private-mail' -and $map.Gemini.ApiKey -eq 'synthetic-private-ai') 'Expansion lost data.'
    }
    Run-Test 'Migration merges local values, existing secret wins, deletes only source' {
        $f=New-Fixture
        Write-Json $f.Secret @{ 'Gemini:ApiKey'='synthetic-private-current'; Extra=@{Enabled=$true}; KeepEmpty='' }
        Write-Json $f.Local @{ Gemini=@{ApiKey='synthetic-private-old';Model='new-model'}; Email=@{Password='synthetic-private-mail'}; KeepEmpty='must-not-replace-empty' }
        $neighbor=Join-Path $f.Root 'src\AiInterview.Web\keep.txt';[IO.File]::WriteAllText($neighbor,'keep')
        $output = & (Join-Path $f.Root 'Migrate-LocalSecrets.ps1') *>&1
        Assert-NoSecretOutput $output
        $saved=Get-Content -LiteralPath $f.Secret -Raw | ConvertFrom-Json
        Assert-Test ($saved.Gemini.ApiKey -eq 'synthetic-private-current' -and $saved.Gemini.Model -eq 'new-model' -and $saved.Email.Password -eq 'synthetic-private-mail' -and $saved.KeepEmpty -eq '') 'Precedence or missing value merge failed.'
        Assert-Test (-not [IO.File]::Exists($f.Local) -and [IO.File]::Exists($neighbor)) 'Unexpected source or sibling deletion.'
        Assert-Test (@(Get-ChildItem -LiteralPath (Split-Path -Parent $f.Secret) -Filter '.migrate-*').Count -eq 0) 'Temporary file remains.'
    }
    Run-Test 'Migration creates absent secrets from explicit local path' {
        $f=New-Fixture; $other=Join-Path $f.Root 'appsettings.Local.json'
        Write-Json $other @{ 'Email:Password'='synthetic-private-mail' }
        $output=& (Join-Path $f.Root 'Migrate-LocalSecrets.ps1') -LocalPath $other *>&1
        Assert-NoSecretOutput $output
        Assert-Test ([IO.File]::Exists($f.Secret) -and -not [IO.File]::Exists($other)) 'Migration did not persist/delete exactly explicit source.'
    }
    Run-Test 'Migration rejects malformed input and preserves both files without echoing content' {
        $f=New-Fixture;Write-Json $f.Secret @{Keep='existing'}
        [IO.File]::WriteAllText($f.Local,'{"broken":"synthetic-private-value"')
        $before=[IO.File]::ReadAllText($f.Secret);$failed=$false
        try { $null=& (Join-Path $f.Root 'Migrate-LocalSecrets.ps1') *>&1 } catch { $failed=$true;Assert-NoSecretOutput $_.Exception.Message }
        Assert-Test ($failed -and [IO.File]::Exists($f.Local) -and [IO.File]::ReadAllText($f.Secret) -ceq $before) 'Malformed source was not preserved.'
    }
    Run-Test 'Migration detects concurrent target edit and leaves input intact' {
        $f=New-Fixture;Write-Json $f.Secret @{Keep='existing'};Write-Json $f.Local @{Email=@{Password='synthetic-private-mail'}}
        Add-ConcurrentMutation $f $f.Secret
        $failed=$false;try {$null=& (Join-Path $f.Root 'Migrate-LocalSecrets.ps1') *>&1} catch {$failed=$true}
        $saved=Get-Content -LiteralPath $f.Secret -Raw | ConvertFrom-Json
        Assert-Test ($failed -and $saved.Concurrent -eq 'new-value' -and [IO.File]::Exists($f.Local)) 'Concurrent edit was overwritten.'
    }
    Run-Test 'Migration detects concurrent source edit and does not delete changed file' {
        $f=New-Fixture;Write-Json $f.Secret @{Keep='existing'};Write-Json $f.Local @{Email=@{Password='synthetic-private-mail'}}
        $before=[IO.File]::ReadAllText($f.Secret);Add-ConcurrentMutation $f $f.Local
        $failed=$false;try {$null=& (Join-Path $f.Root 'Migrate-LocalSecrets.ps1') *>&1} catch {$failed=$true}
        Assert-Test ($failed -and [IO.File]::Exists($f.Local) -and [IO.File]::ReadAllText($f.Secret) -ceq $before) 'Changed source was deleted or target altered.'
    }
    Run-Test 'Migration refuses arbitrary file names without deletion' {
        $f=New-Fixture;$other=Join-Path $f.Root 'keep.json';Write-Json $other @{Keep='existing'}
        $failed=$false;try {$null=& (Join-Path $f.Root 'Migrate-LocalSecrets.ps1') -LocalPath $other *>&1} catch {$failed=$true}
        Assert-Test ($failed -and [IO.File]::Exists($other) -and -not [IO.File]::Exists($f.Secret)) 'Unexpected input accepted.'
    }
    function global:Read-Host {
        param([string]$Prompt, [switch]$AsSecureString)
        if ($Prompt.StartsWith('Model ') -and $global:InterviewScriptTestState.Mutate) {
            [IO.File]::WriteAllText($global:InterviewScriptTestState.Secret, '{"Concurrent":"new-value"}')
        }
        if ($AsSecureString) {
            if ($Prompt -match 'API key') { return ConvertTo-SecureString 'synthetic-new-key' -AsPlainText -Force }
            return New-Object Security.SecureString
        }
        return ''
    }
    Run-Test 'Configure saves outside source, preserves unrelated secrets, does not echo credentials' {
        $f=New-Fixture;Write-Json $f.Secret @{ 'Email:Password'='synthetic-private-mail' }
        $global:InterviewScriptTestState=@{Secret=$f.Secret;Mutate=$false}
        $output=& (Join-Path $f.Root 'Configure.ps1') *>&1
        Assert-NoSecretOutput $output
        $saved=Get-Content -LiteralPath $f.Secret -Raw | ConvertFrom-Json
        Assert-Test ($saved.Gemini.ApiKey -eq 'synthetic-new-key' -and $saved.Email.Password -eq 'synthetic-private-mail' -and -not [IO.File]::Exists($f.Local)) 'Configure wrote to wrong place or lost settings.'
    }
    Run-Test 'Configure refuses concurrent changes during prompts and cleans temporary secret file' {
        $f=New-Fixture;Write-Json $f.Secret @{ 'Email:Password'='synthetic-private-mail' }
        $global:InterviewScriptTestState=@{Secret=$f.Secret;Mutate=$true}
        $failed=$false;try {$null=& (Join-Path $f.Root 'Configure.ps1') *>&1} catch {$failed=$true}
        $saved=Get-Content -LiteralPath $f.Secret -Raw | ConvertFrom-Json
        Assert-Test ($failed -and $saved.Concurrent -eq 'new-value') 'Configure overwrote concurrent changes.'
        Assert-Test (@(Get-ChildItem -LiteralPath (Split-Path -Parent $f.Secret) -Filter '.local-config-*').Count -eq 0) 'Temporary secret file remains.'
    }
    Write-Host "Secret script tests passed: $passed/9. Only synthetic fixtures were used."
}
finally {
    Remove-Item -LiteralPath 'Function:\Read-Host' -ErrorAction SilentlyContinue
    Remove-Variable InterviewScriptTestState -Scope Global -ErrorAction SilentlyContinue
    $absolute=[IO.Path]::GetFullPath($testRoot)
    $allowed=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if (-not $absolute.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($absolute) -notlike 'InterviewLab-secrets-tests-*') { throw 'Refusing cleanup outside isolated fixture directory.' }
    Remove-Item -LiteralPath $absolute -Recurse -Force
}
