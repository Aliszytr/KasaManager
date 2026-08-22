[CmdletBinding()]
param(
    # KasaManager Revision 3 EF Production Migration Authority Fix: the script no longer trusts an
    # ambient $env:ASPNETCORE_ENVIRONMENT/$env:DOTNET_ENVIRONMENT that the operator may or may not
    # have set correctly in their shell beforehand. The environment is now a required, explicit
    # argument — this is the single source of truth pinned for this invocation.
    [Parameter(Mandatory = $true)]
    [ValidateSet("Development", "Test", "Production")]
    [string]$Environment
)

$ErrorActionPreference = "Stop"

# Save prior ambient values so they can be restored once this script finishes — prevents a
# leftover pinned value from silently steering a later, unrelated command in the same session.
$previousDotnetEnvironment = $env:DOTNET_ENVIRONMENT
$previousAspNetCoreEnvironment = $env:ASPNETCORE_ENVIRONMENT

try {
    # Pin BOTH variables to the same explicit value. The modern .NET generic host (which
    # WebApplication.CreateBuilder uses) reads DOTNET_ENVIRONMENT with priority over
    # ASPNETCORE_ENVIRONMENT — a stale/opposing value left in either one could otherwise steer
    # the EF design-time factory (or the app itself) to a different environment than intended.
    $env:DOTNET_ENVIRONMENT = $Environment
    $env:ASPNETCORE_ENVIRONMENT = $Environment
    Write-Host "Effective environment for this migration run: $Environment" -ForegroundColor Cyan

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

        Write-Host "Updating database (Environment=$Environment)..."
        dotnet ef database update -p ..\KasaManager.Infrastructure -s .

        Write-Host "Done."
    }
    finally {
        Pop-Location
    }
}
finally {
    # Restore whatever the caller's shell had before (including "unset", if that was the case).
    $env:DOTNET_ENVIRONMENT = $previousDotnetEnvironment
    $env:ASPNETCORE_ENVIRONMENT = $previousAspNetCoreEnvironment
}
