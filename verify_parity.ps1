param(
    [string]$ExpectedFile = "legacy_test_output.json",
    [string]$ActualFile = "new_test_output.json"
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "      KASA YONETIM PARITY HARNESS       " -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

if (-not (Test-Path $ExpectedFile)) {
    Write-Host "HATA: Beklenen sonuc (Expected) dosyasi bulunamadi: $ExpectedFile" -ForegroundColor Red
    Write-Host "Lutfen eski projeden alinan JSON sonucunu bu isme kaydedin." -ForegroundColor Yellow
    exit 1
}

if (-not (Test-Path $ActualFile)) {
    Write-Host "HATA: Guncel sonuc (Actual) dosyasi bulunamadi: $ActualFile" -ForegroundColor Red
    Write-Host "Lutfen yeni projeden alinan JSON sonucunu bu isme kaydedin." -ForegroundColor Yellow
    exit 1
}

$expectedRaw = Get-Content $ExpectedFile -Raw | ConvertFrom-Json
$actualRaw = Get-Content $ActualFile -Raw | ConvertFrom-Json

# Flatten function to easily extract nested keys regardless of depth
function Get-FlatObject($obj, $prefix = "") {
    $flat = @{}
    if ($null -eq $obj) { return $flat }
    
    foreach ($prop in $obj.psobject.properties) {
        $key = if ($prefix) { "$prefix.$($prop.Name)" } else { $prop.Name }
        if ($prop.Value -is [System.Management.Automation.PSCustomObject]) {
            $subFlat = Get-FlatObject $prop.Value $key
            foreach ($k in $subFlat.Keys) { $flat[$k] = $subFlat[$k] }
        } else {
            $flat[$key] = $prop.Value
        }
    }
    return $flat
}

$expected = Get-FlatObject $expectedRaw
$actual = Get-FlatObject $actualRaw

# Asgari kontrol edilecek alanlar
$keysToCompare = @(
    "DundenDevredenKasaNakit",
    "GenelKasa",
    "VergiKasaBakiyeToplam",
    "BankayaYatirilacakTahsilat",
    "BankayaYatirilacakHarc",
    "BozukParaHaricKasa",
    "SonrayaDevredecek"
)

$mismatchCount = 0
$matchCount = 0

Write-Host "Senaryo: $ExpectedFile vs $ActualFile karsilastirmasi"
Write-Host "--------------------------------------------------------"

foreach ($key in $keysToCompare) {
    # Find key loosely by case-insensitive matching in the flattened dictionaries
    $expMatchKey = $expected.Keys | Where-Object { $_ -match "$key$" } | Select-Object -First 1
    $actMatchKey = $actual.Keys | Where-Object { $_ -match "$key$" } | Select-Object -First 1
    
    $expectedVal = if ($expMatchKey) { $expected[$expMatchKey] } else { $null }
    $actualVal = if ($actMatchKey) { $actual[$actMatchKey] } else { $null }

    $isMatch = $false

    if ($null -eq $expectedVal -and $null -eq $actualVal) {
        $isMatch = $true
        $expectedVal = "YOK"
        $actualVal = "YOK"
    } elseif ($null -ne $expectedVal -and $null -ne $actualVal) {
        if ($expectedVal.GetType().Name -match "Decimal|Double|Int" -and $actualVal.GetType().Name -match "Decimal|Double|Int") {
            $isMatch = ([Math]::Round([decimal]$expectedVal, 2) -eq [Math]::Round([decimal]$actualVal, 2))
        } else {
             $isMatch = ("$expectedVal".Trim() -eq "$actualVal".Trim())
        }
    }

    if ($isMatch) {
         Write-Host "[ESLESTI]    $key -> $expectedVal" -ForegroundColor Green
         $matchCount++
    } else {
         Write-Host "[ESLESMEDI]  $key" -ForegroundColor Red
         Write-Host "   -> Beklenen (Eski) : $expectedVal" -ForegroundColor Yellow
         Write-Host "   -> Gelen    (Yeni) : $actualVal" -ForegroundColor Magenta
         $mismatchCount++
    }
}

Write-Host "--------------------------------------------------------"
if ($mismatchCount -eq 0) {
    Write-Host "SONUC: TUM KRITIK ALANLAR ESLESTI (100% PARITY) " -ForegroundColor Green
} else {
    Write-Host "SONUC: $mismatchCount ALAN ESLESMEDI! ($matchCount alan basarili)" -ForegroundColor Red
}
