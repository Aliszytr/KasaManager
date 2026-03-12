#nullable enable

namespace KasaManager.Domain.FinancialExceptions;

/// <summary>
/// Merkezi server-side doğrulama guard'ı.
/// ARCHITECTURE LOCK: Tüm Create/Update validasyonları bu sınıftan geçer.
/// Domain katmanında kalır — Application DTO'larına bağımlılık yoktur.
/// </summary>
public static class FinansalIstisnaValidator
{
    // ═══════════════════════════════════════
    // Tür ↔ Kategori Uyumluluk Matrisi
    // ═══════════════════════════════════════

    private static readonly Dictionary<IstisnaTuru, HashSet<IstisnaKategorisi>> _turKategoriMap = new()
    {
        [IstisnaTuru.BasarisizVirman] = new() { IstisnaKategorisi.BankaTransferHatasi },
        [IstisnaTuru.SistemeGirilmeyenEft] = new() { IstisnaKategorisi.BekleyenSistemGirisi },
        [IstisnaTuru.GecikmeliBankaHareketi] = new() { IstisnaKategorisi.GecikmeliYansima },
        [IstisnaTuru.KismiIslem] = new() { IstisnaKategorisi.KismiIslenmis },
        [IstisnaTuru.BankadanCikamayanTutar] = new() { IstisnaKategorisi.BankaTransferHatasi, IstisnaKategorisi.BekleyenSistemGirisi },
    };

    // ═══════════════════════════════════════
    // Create Validasyonu (raw parametreler)
    // ═══════════════════════════════════════

    /// <summary>
    /// Create parametrelerini doğrular. Boş liste = geçerli.
    /// </summary>
    public static List<string> ValidateCreate(
        IstisnaTuru tur,
        IstisnaKategorisi kategori,
        KasaEtkiYonu etkiYonu,
        decimal beklenenTutar,
        decimal gerceklesenTutar,
        decimal sistemeGirilenTutar)
    {
        var errors = new List<string>();

        // ── Tanımsız enum değerlerini reddet ──
        if (!Enum.IsDefined(tur))
            errors.Add($"Geçersiz istisna türü: {(int)tur}.");

        if (!Enum.IsDefined(kategori))
            errors.Add($"Geçersiz kategori: {(int)kategori}.");

        if (!Enum.IsDefined(etkiYonu))
            errors.Add($"Geçersiz etki yönü: {(int)etkiYonu}.");

        // Tanımsız enum varsa diğer kuralları kontrol etmenin anlamı yok
        if (errors.Count > 0) return errors;

        // ── Tür ↔ Kategori uyumluluk ──
        if (_turKategoriMap.TryGetValue(tur, out var izinliKategoriler))
        {
            if (!izinliKategoriler.Contains(kategori))
            {
                errors.Add($"Tür '{tur}' ile kategori '{kategori}' uyumlu değil.");
            }
        }

        // ── EtkiYönü: Create sırasında Nötr yasak ──
        if (etkiYonu == KasaEtkiYonu.Notr)
            errors.Add("Yeni istisna oluşturulurken Etki Yönü 'Nötr' seçilemez.");

        // ── Genel tutar kuralları ──
        if (beklenenTutar < 0m)
            errors.Add("Beklenen tutar negatif olamaz.");

        if (gerceklesenTutar < 0m)
            errors.Add("Gerçekleşen tutar negatif olamaz.");

        if (sistemeGirilenTutar < 0m)
            errors.Add("Sisteme girilen tutar negatif olamaz.");

        // ── Tür bazlı zorunlu tutar alanları ──
        switch (tur)
        {
            case IstisnaTuru.BasarisizVirman:
            case IstisnaTuru.BankadanCikamayanTutar:
            case IstisnaTuru.GecikmeliBankaHareketi:
            case IstisnaTuru.KismiIslem:
                if (beklenenTutar <= 0m)
                    errors.Add($"Tür '{tur}' için beklenen tutar sıfırdan büyük olmalıdır.");
                break;

            case IstisnaTuru.SistemeGirilmeyenEft:
                if (gerceklesenTutar <= 0m)
                    errors.Add("Sisteme girilmeyen EFT için gerçekleşen tutar sıfırdan büyük olmalıdır.");
                break;
        }

        return errors;
    }

    // ═══════════════════════════════════════
    // Update Tutar Validasyonu
    // ═══════════════════════════════════════

    /// <summary>
    /// Update tutar değerlerini doğrular. Boş liste = geçerli.
    /// </summary>
    public static List<string> ValidateUpdateAmounts(
        decimal? beklenenTutar,
        decimal? gerceklesenTutar,
        decimal? sistemeGirilenTutar)
    {
        var errors = new List<string>();

        if (beklenenTutar.HasValue && beklenenTutar.Value < 0m)
            errors.Add("Beklenen tutar negatif olamaz.");

        if (gerceklesenTutar.HasValue && gerceklesenTutar.Value < 0m)
            errors.Add("Gerçekleşen tutar negatif olamaz.");

        if (sistemeGirilenTutar.HasValue && sistemeGirilenTutar.Value < 0m)
            errors.Add("Sisteme girilen tutar negatif olamaz.");

        return errors;
    }
}
