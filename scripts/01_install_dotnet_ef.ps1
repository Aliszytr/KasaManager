$ErrorActionPreference = "Stop"

Write-Host "Installing dotnet-ef global tool (if not installed)..."

$existing = dotnet tool list --global | Select-String -Pattern "dotnet-ef" -Quiet
if (-not $existing) {
    dotnet tool install --global dotnet-ef
} else {
    Write-Host "dotnet-ef already installed."
}

dotnet ef --version
Write-Host "OK"
