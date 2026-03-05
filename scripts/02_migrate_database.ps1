$ErrorActionPreference = "Stop"

# Run from repository root
$web = Join-Path $PSScriptRoot "..\src\KasaManager.Web"

Write-Host "Restoring..."
Push-Location $web
try {
    dotnet restore

    Write-Host "Ensuring dotnet-ef is available..."
    $existing = dotnet tool list --global | Select-String -Pattern "dotnet-ef" -Quiet
    if (-not $existing) {
        Write-Host "dotnet-ef not found. Run scripts\01_install_dotnet_ef.ps1 first." -ForegroundColor Yellow
        throw "dotnet-ef global tool is not installed."
    }

    $infra = Join-Path $web "..\KasaManager.Infrastructure"
    $migrationsPath = Join-Path $infra "Migrations"

    $migrationName = "InitialCreate_SqlServer_Clean"

    Write-Host "Creating migration ($migrationName) if none exists..."

    $hasMigration = $false
    if (Test-Path $migrationsPath) {
        $count = (Get-ChildItem -Path $migrationsPath -Filter "*.cs" -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -notlike "*Designer.cs" -and $_.Name -notlike "*Snapshot.cs" } |
            Measure-Object).Count
        if ($count -gt 0) { $hasMigration = $true }
    }

    if (-not $hasMigration) {
        dotnet ef migrations add $migrationName -p ..\KasaManager.Infrastructure -s .
    } else {
        Write-Host "Migrations already exist. Skipping migrations add." -ForegroundColor Cyan
    }

    Write-Host "Updating database..."
    dotnet ef database update -p ..\KasaManager.Infrastructure -s .

    Write-Host "Done."
}
finally {
    Pop-Location
}
