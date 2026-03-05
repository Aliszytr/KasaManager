$ErrorActionPreference = "Stop"

Write-Host "Cleaning bin/obj and local caches (repo only)..."

$root = Resolve-Path (Join-Path $PSScriptRoot "..")

Get-ChildItem -Path $root -Recurse -Directory -Force |
  Where-Object { $_.Name -in @("bin", "obj", ".vs") } |
  ForEach-Object {
    try {
      Write-Host "Removing: $($_.FullName)"
      Remove-Item -Recurse -Force -LiteralPath $_.FullName
    } catch {
      Write-Warning "Could not remove: $($_.FullName) -> $($_.Exception.Message)"
    }
  }

Write-Host "Done. Now run: dotnet restore && dotnet build"
