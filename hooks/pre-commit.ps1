# KasaManager — pre-commit secrets guard (PowerShell)
# Kurulum: Copy-Item hooks\pre-commit.ps1 .git\hooks\pre-commit.ps1

$blocked = $false

# Staged dosyaları al
$staged = git diff --cached --name-only

# 1) appsettings.Development.json
if ($staged | Select-String "appsettings.Development.json") {
    Write-Host "BLOCKED: appsettings.Development.json commit edilemez!" -ForegroundColor Red
    $blocked = $true
}

# 2) secrets.json
if ($staged | Select-String "secrets.json") {
    Write-Host "BLOCKED: secrets.json commit edilemez!" -ForegroundColor Red
    $blocked = $true
}

# 3) ConnectionStrings dolu mu
if ($staged | Select-String "appsettings.json") {
    $content = git show ":src/KasaManager.Web/appsettings.json" 2>$null
    if ($content -match '"SqlConnection"\s*:\s*"[^"]+"') {
        Write-Host "BLOCKED: appsettings.json SqlConnection dolu!" -ForegroundColor Red
        $blocked = $true
    }
    if ($content -match '"Password"\s*:\s*"[^"]+"') {
        Write-Host "BLOCKED: appsettings.json Password dolu!" -ForegroundColor Red
        $blocked = $true
    }
}

# 4) Excel dosyası
if ($staged | Select-String "\.(xlsx|xls)$") {
    Write-Host "BLOCKED: Excel dosyasi commit edilemez!" -ForegroundColor Red
    $blocked = $true
}

if ($blocked) {
    Write-Host "`nCommit iptal edildi." -ForegroundColor Yellow
    exit 1
}

exit 0
