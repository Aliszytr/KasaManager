# Test script for BankaAciklamaParser regex patterns - UPDATED with Vezne pattern

$standardPattern = "^((?<il>\w+)\s+(?<no>\d{1,2})\.\s*(?<tip>İdare|Vergi|İcra|Asliye|Hukuk|Ceza)\s*Mahkemesi)-(?<esas>\d{4}/\d+)"
$veznePattern = "^(?<birim>Ankara\s+İdare\s+ve\s+Vergi\s+Mahkemeleri\s+Vezne(?:\s+ve\s+Ön\s+Bürosu)?)[-\s](?<esas>\d{4}/\d+)"

$testCases = @(
    "Ankara 1. Vergi Mahkemesi-2024/981 Vergi-Ankara 2. Vergi Dava Dairesi-2026/233",
    "Ankara İdare ve Vergi Mahkemeleri Vezne-2026/1652 -Hatay 1. Vergi Mahkemesi-2026/93",
    "Ankara İdare ve Vergi Mahkemeleri Vezne ve Ön Bürosu-2023/44652-Detaylar",
    "Ankara 5. Vergi Mahkemesi-2024/950 Vergi-Ankara 1. Vergi Dava Dairesi-2025/441"
)

Write-Host "Testing ReddiyatGondericiPattern and VezneGondericiPattern:`n" -ForegroundColor Cyan

foreach ($test in $testCases) {
    Write-Host "Test: $test" -ForegroundColor Yellow
    
    # Önce standart pattern dene
    $standardMatch = [regex]::Match($test, $standardPattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    
    if ($standardMatch.Success) {
        Write-Host "  Standart Pattern: TRUE" -ForegroundColor Green
        Write-Host "  İl: $($standardMatch.Groups['il'].Value)"
        Write-Host "  No: $($standardMatch.Groups['no'].Value)"
        Write-Host "  Tip: $($standardMatch.Groups['tip'].Value)"
        Write-Host "  Esas: $($standardMatch.Groups['esas'].Value)"
    }
    else {
        # Vezne pattern dene
        $vezneMatch = [regex]::Match($test, $veznePattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
        
        if ($vezneMatch.Success) {
            Write-Host "  Vezne Pattern: TRUE" -ForegroundColor Green
            Write-Host "  Birim: $($vezneMatch.Groups['birim'].Value)"
            Write-Host "  Esas: $($vezneMatch.Groups['esas'].Value)"
        }
        else {
            Write-Host "  Tüm Patternler: FALSE - Eşleşme bulunamadı!" -ForegroundColor Red
        }
    }
    Write-Host ""
}
