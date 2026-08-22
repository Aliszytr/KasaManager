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

# Migration Wrapper Safety Closure: $ErrorActionPreference only converts PowerShell/cmdlet
# terminating errors — it does NOT turn a nonzero native-command ($LASTEXITCODE) exit into a
# script failure. Every safety-critical native invocation below is routed through this tiny
# helper so a failed `dotnet ef ...`/`dotnet restore` always throws (stopping before any later
# command) and the wrapper process exits non-zero, while still going through the outer
# environment-restoration `finally`.
function Invoke-CheckedNativeCommand {
    param(
        [Parameter(Mandatory = $true)][string]$Description,
        [Parameter(Mandatory = $true)][scriptblock]$Command
    )
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed (exit code $LASTEXITCODE)."
    }
}

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
        Invoke-CheckedNativeCommand -Description "dotnet restore" -Command { dotnet restore }

        # KasaManager dotnet-ef Tool Discovery Fix: "dotnet tool list --global" only sees globally
        # installed tools, but this repository resolves dotnet-ef through a LOCAL tool manifest
        # (dotnet-tools.json here in src/KasaManager.Web, discovered via directory walk-up) — so
        # the old --global-only check produced a false negative and aborted before ever reaching
        # the database, even though "dotnet ef ..." genuinely works in this exact context. The
        # prerequisite now probes the SAME command/working directory this script actually invokes
        # below, instead of guessing at install scope (global/local/PATH/manifest).
        Write-Host "Ensuring dotnet-ef is available (probing the exact command this script invokes)..."
        $efProbeOutput = Invoke-CheckedNativeCommand -Description "dotnet-ef availability probe (dotnet ef --version)" -Command { dotnet ef --version 2>&1 }
        Write-Host "dotnet-ef available: $efProbeOutput" -ForegroundColor Green

        # Migration Wrapper Safety Closure — explicit DbContext: the repository contains more
        # than one DbContext (KasaManagerDbContext, LegacyKasaDbContext), so every EF migration
        # command below must name its target explicitly rather than relying on EF's single-context
        # auto-detection, which fails with "More than one DbContext was found." once ambiguous.
        $targetContext = "KasaManagerDbContext"

        # Migration Wrapper Safety Closure — Production never generates migrations: this branch
        # is the ONLY place "dotnet ef migrations add" appears anywhere in this script, and it is
        # lexically unreachable when $Environment -eq "Production" — not merely gated by
        # $hasMigration/directory state. Production may only apply already-committed migrations
        # via "dotnet ef database update" below; it can never author new ones.
        if ($Environment -eq "Production") {
            Write-Host "Production: migration generation is disabled by design - only already-committed migrations are applied." -ForegroundColor Cyan
        }
        else {
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
                Invoke-CheckedNativeCommand -Description "dotnet ef migrations add" -Command {
                    dotnet ef migrations add $migrationName --context $targetContext -p ..\KasaManager.Infrastructure -s .
                }
            } else {
                Write-Host "Migrations already exist. Skipping migrations add." -ForegroundColor Cyan
            }
        }

        Write-Host "Updating database (Environment=$Environment, Context=$targetContext)..."
        Invoke-CheckedNativeCommand -Description "dotnet ef database update" -Command {
            dotnet ef database update --context $targetContext -p ..\KasaManager.Infrastructure -s .
        }

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
