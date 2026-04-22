#nullable enable
using KasaManager.Application.Services.Comparison;
using KasaManager.Domain.Reports;

namespace KasaManager.Tests.Application;

/// <summary>
/// MarkCancelledRecords algoritması birim testleri.
/// Plan v4'teki 6 kuralı doğrular.
/// </summary>
public sealed class MarkCancelledRecordsTests
{
    // Yardımcı: boş Parsed nesnesi (testlerde işlem türü Aciklama'dan tespit edilir)
    private static readonly ParsedBankaAciklama EmptyParsed = new(null, null, null, null, 0);

    /// <summary>
    /// Yardımcı: BankaRecord oluşturur.
    /// </summary>
    private static ComparisonService.BankaRecord MakeRecord(
        int rowIndex, decimal tutar, DateTime tarih,
        string aciklama, bool isBorc)
    {
        return new ComparisonService.BankaRecord(
            rowIndex, Math.Abs(tutar), tarih, aciklama, isBorc ? "Borç" : "Alacak", EmptyParsed)
        {
            IsBorc = isBorc
        };
    }

    // ─────────────────────────────────────────────────────────────
    // Test 1: Gerçek 21.04.2026 senaryosu
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void MarkCancelledRecords_ShouldDetectVirmanIptalPair()
    {
        // Arrange — Gerçek 21.04.2026 verisi
        var records = new List<ComparisonService.BankaRecord>
        {
            // Satır 767: İlk virman (yanlış tutar) — Borç
            MakeRecord(767, 18940.88m, new DateTime(2026, 4, 21, 17, 10, 13),
                "Virman 2026/244 MUH SAYI STOPAJ HESABINA", isBorc: true),

            // Satır 769: İptal — Alacak (iptal anahtar kelimesi: "İptal")
            MakeRecord(769, 18940.88m, new DateTime(2026, 4, 21, 17, 14, 32),
                "Virman İptal 2026/244 MUH SAYI STOPAJ HESABINA", isBorc: false),

            // Satır 771: Gerçek virman (doğru tutar) — Borç
            MakeRecord(771, 16419.44m, new DateTime(2026, 4, 21, 17, 20, 36),
                "Virman 2026/246 STOPAJ HESABINA", isBorc: true)
        };

        // Act
        var pairs = ComparisonService.MarkCancelledRecords(records);

        // Assert — Tek bir iptal çifti bulunmalı
        Assert.Single(pairs);
        Assert.Equal(18940.88m, pairs[0].Tutar);
        Assert.Equal(767, pairs[0].OrijinalRowIndex);
        Assert.Equal(769, pairs[0].IptalRowIndex);

        // Flag'ler doğru set edilmeli
        Assert.True(records[0].IsCancelled);   // 767 → iptal edilmiş
        Assert.True(records[1].IsCancelled);   // 769 → iptal satırı
        Assert.False(records[2].IsCancelled);  // 771 → GEÇERLİ kalmalı!
    }

    // ─────────────────────────────────────────────────────────────
    // Test 2: EFT_OTOMATIK_IADE hariç tutulması
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void MarkCancelledRecords_ShouldIgnoreEftOtomatikIade()
    {
        // Arrange — EFT iade kaydı iptal değil, meşru iade
        var records = new List<ComparisonService.BankaRecord>
        {
            MakeRecord(100, 5000m, new DateTime(2026, 4, 21, 10, 0, 0),
                "Virman STOPAJ HESABINA", isBorc: true),

            // EFT OTOMATİK İADE — bu iptal sayılmamalı!
            MakeRecord(101, 5000m, new DateTime(2026, 4, 21, 10, 30, 0),
                "EFT OTOMATİK İADE GERİ ÖDEME iptal", isBorc: false)
        };

        // Act
        var pairs = ComparisonService.MarkCancelledRecords(records);

        // Assert — EFT_OTOMATIK_IADE olduğu için eşleşme olmamalı
        Assert.Empty(pairs);
        Assert.False(records[0].IsCancelled);
        Assert.False(records[1].IsCancelled);
    }

    // ─────────────────────────────────────────────────────────────
    // Test 3: "iade" kelimesi TEK BAŞINA eşleşmemeli
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void MarkCancelledRecords_ShouldNotMatchOnlyIadeKeyword()
    {
        // Arrange — Sadece "iade" kelimesi geçiyor, iptal/geri alma/düzeltme yok
        var records = new List<ComparisonService.BankaRecord>
        {
            MakeRecord(200, 3000m, new DateTime(2026, 4, 21, 9, 0, 0),
                "Virman STOPAJ HESABINA", isBorc: true),

            // "iade" kelimesi var ama bizim anahtar kelimelerimizde yok
            MakeRecord(201, 3000m, new DateTime(2026, 4, 21, 9, 30, 0),
                "Havale iade bedeli gönderildi", isBorc: false)
        };

        // Act
        var pairs = ComparisonService.MarkCancelledRecords(records);

        // Assert — "iade" kelimesi anahtar kelime listesinde yok
        Assert.Empty(pairs);
        Assert.False(records[0].IsCancelled);
        Assert.False(records[1].IsCancelled);
    }

    // ─────────────────────────────────────────────────────────────
    // Test 4: Aynı saniyede olan işlemler eşleşmemeli (< kuralı)
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void MarkCancelledRecords_ShouldNotMatchWhenTimestampsEqual()
    {
        // Arrange — Aynı saniye, eşitlik hariç kuralı
        var sameTime = new DateTime(2026, 4, 21, 15, 0, 0);
        var records = new List<ComparisonService.BankaRecord>
        {
            MakeRecord(300, 7500m, sameTime,
                "Virman STOPAJ HESABINA", isBorc: true),

            MakeRecord(301, 7500m, sameTime,
                "Virman İptal STOPAJ HESABINA", isBorc: false)
        };

        // Act
        var pairs = ComparisonService.MarkCancelledRecords(records);

        // Assert — Aynı saniyede olduğu için eşleşme olmamalı
        Assert.Empty(pairs);
        Assert.False(records[0].IsCancelled);
        Assert.False(records[1].IsCancelled);
    }

    // ─────────────────────────────────────────────────────────────
    // Test 5: Birden fazla aday — en yakın zamanlı seçilmeli
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void MarkCancelledRecords_ShouldSelectClosestCandidateInTime()
    {
        // Arrange — İki aynı tutarlı virman, bir iptal
        // Beklenti: İptal zamana en yakın olana (17:08) eşleşmeli, sabahki (09:00) değil
        var records = new List<ComparisonService.BankaRecord>
        {
            // Sabah yapılan virman (uzak)
            MakeRecord(400, 10000m, new DateTime(2026, 4, 21, 9, 0, 0),
                "Virman STOPAJ HESABINA", isBorc: true),

            // Öğleden sonra yapılan virman (yakın)
            MakeRecord(401, 10000m, new DateTime(2026, 4, 21, 17, 8, 0),
                "Virman STOPAJ HESABINA", isBorc: true),

            // İptal (17:12)
            MakeRecord(402, 10000m, new DateTime(2026, 4, 21, 17, 12, 0),
                "Virman İptal STOPAJ HESABINA", isBorc: false)
        };

        // Act
        var pairs = ComparisonService.MarkCancelledRecords(records);

        // Assert — Tek çift, EN YAKIN olan (401) seçilmiş olmalı
        Assert.Single(pairs);
        Assert.Equal(401, pairs[0].OrijinalRowIndex);  // 17:08 → iptale en yakın
        Assert.Equal(402, pairs[0].IptalRowIndex);

        Assert.False(records[0].IsCancelled);  // 400 (sabah) → GEÇERLİ kalmalı
        Assert.True(records[1].IsCancelled);   // 401 (17:08) → iptal edilmiş
        Assert.True(records[2].IsCancelled);   // 402 → iptal satırı
    }

    // ─────────────────────────────────────────────────────────────
    // Test 6: Türkçe büyük/küçük harf varyasyonları
    // ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Virman İPTAL STOPAJ HESABINA")]    // Tümü büyük Türkçe İ
    [InlineData("Virman iptal STOPAJ HESABINA")]     // Tümü küçük
    [InlineData("Virman İptal STOPAJ HESABINA")]     // Mixed (standart)
    [InlineData("Virman IPTAL STOPAJ HESABINA")]     // ASCII büyük I (edge case)
    public void MarkCancelledRecords_ShouldHandleTurkishCaseVariations(string iptalAciklama)
    {
        // Arrange
        var records = new List<ComparisonService.BankaRecord>
        {
            MakeRecord(1, 1000m, new DateTime(2026, 4, 21, 10, 0, 0),
                "Virman STOPAJ HESABINA", isBorc: true),

            MakeRecord(2, 1000m, new DateTime(2026, 4, 21, 10, 5, 0),
                iptalAciklama, isBorc: false)
        };

        // Act
        var pairs = ComparisonService.MarkCancelledRecords(records);

        // Assert — Her varyant yakalanmalı
        Assert.Single(pairs);
        Assert.Equal(1000m, pairs[0].Tutar);
        Assert.True(records[0].IsCancelled);
        Assert.True(records[1].IsCancelled);
    }
}
