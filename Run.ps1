[CmdletBinding()]
param(
    [switch]$NoLaunch,
    [switch]$MigrateOnly
)

$ErrorActionPreference = 'Stop'
if ($NoLaunch -and $MigrateOnly) { throw 'Chi dung mot trong hai tuy chon -NoLaunch hoac -MigrateOnly.' }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Chua tim thay .NET SDK. Cai .NET 8 SDK roi mo lai PowerShell.' }
$webProject = Join-Path $PSScriptRoot 'src\AiInterview.Web\AiInterview.Web.csproj'
$solution = Join-Path $PSScriptRoot 'AiInterview.sln'
$sdkList = & dotnet --list-sdks
if ($LASTEXITCODE -ne 0 -or -not ($sdkList | Where-Object { $_ -match '^(8|9|[1-9][0-9])\.' })) { throw 'Can .NET SDK 8 hoac moi hon de build do an target .NET 8.' }
$runtimeList = & dotnet --list-runtimes
if (-not ($runtimeList | Where-Object { $_ -match '^Microsoft.AspNetCore.App 8\.' })) { throw 'Can ASP.NET Core Runtime 8 de chay ung dung target .NET 8.' }

Push-Location $PSScriptRoot
try {
    & dotnet restore $solution
    if ($LASTEXITCODE -ne 0) { throw 'Restore that bai. Kiem tra mang, NuGet va thong bao o tren.' }
    & dotnet build $solution --no-restore --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build that bai. Xem file va dong trong thong bao o tren.' }
    if ($NoLaunch) { Write-Host 'Restore va build hoan tat. -NoLaunch nen chua khoi dong ung dung.'; return }
    if ($MigrateOnly) {
        & dotnet run --project $webProject --no-build --launch-profile AiInterview -- --migrate
        if ($LASTEXITCODE -ne 0) { throw 'Tao/cap nhat database that bai. Kiem tra SQL Server va connection string.' }
        Write-Host 'Da cap nhat database va chay seed theo cau hinh. Chua khoi dong web.'
        return
    }
    Write-Host 'Mo trinh duyet tai http://localhost:5088 sau khi xuat hien Now listening.'
    Write-Host 'Ung dung chay trong cua so nay. Nhan Ctrl+C de dung.'
    & dotnet run --project $webProject --no-build --launch-profile AiInterview
    if ($LASTEXITCODE -ne 0) { throw 'Ung dung dung voi loi. Kiem tra thong bao moi nhat va README.' }
}
finally { Pop-Location }
