$ErrorActionPreference = "Stop"

# Run from repository root
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$web  = Join-Path $root "src\KasaManager.Web"
$infra = Join-Path $root "src\KasaManager.Infrastructure"
$migrationsPath = Join-Path $infra "Migrations"

Write-Host "HARD RESET: Removing migrations and snapshot..." -ForegroundColor Yellow

if (Test-Path $migrationsPath) {
    Get-ChildItem -Path $migrationsPath -File -Force |
      Where-Object { $_.Name -like "*.cs" -or $_.Name -like "*.Designer.cs" -or $_.Name -like "*Snapshot.cs" } |
      ForEach-Object {
        Write-Host "Deleting: $($_.FullName)"
        Remove-Item -Force -LiteralPath $_.FullName
      }
}

Write-Host "Restoring and re-creating clean baseline migration..." -ForegroundColor Yellow
Push-Location $web
try {
    dotnet restore

    $existing = dotnet tool list --global | Select-String -Pattern "dotnet-ef" -Quiet
    if (-not $existing) {
        Write-Host "dotnet-ef not found. Run scripts\01_install_dotnet_ef.ps1 first." -ForegroundColor Yellow
        throw "dotnet-ef global tool is not installed."
    }

    $migrationName = "InitialCreate_SqlServer_Clean"

    dotnet ef migrations add $migrationName -p ..\KasaManager.Infrastructure -s .
    dotnet ef database update -p ..\KasaManager.Infrastructure -s .

    Write-Host "Reset complete." -ForegroundColor Green
}
finally {
    Pop-Location
}
