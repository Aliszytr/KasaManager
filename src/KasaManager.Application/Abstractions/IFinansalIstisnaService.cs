#nullable enable
using KasaManager.Domain.FinancialExceptions;

namespace KasaManager.Application.Abstractions;

/// <summary>
/// Finansal İstisna CRUD ve sorgu servisi.
/// Faz 1: Manuel CRUD + tarih bazlı listeleme + aggregate hesaplama.
/// Faz 2: Devredilmiş listesi + ertesi gün kayıt açma.
/// </summary>
public interface IFinansalIstisnaService
{
    /// <summary>Yeni istisna oluşturur (KararDurumu = IncelemeBekliyor).</summary>
    Task<FinansalIstisna> CreateAsync(FinansalIstisnaCreateRequest request, CancellationToken ct = default);

    /// <summary>Mevcut istisnayı günceller.</summary>
    Task<FinansalIstisna?> UpdateAsync(Guid id, FinansalIstisnaUpdateRequest request, CancellationToken ct = default);

    /// <summary>Karar durumunu günceller (Onayla / Reddet).</summary>
    Task<FinansalIstisna?> SetKararAsync(Guid id, KararDurumu karar, string kullanici, CancellationToken ct = default);

    /// <summary>Yaşam döngüsü durumunu günceller (Çözüldü, İptal, vb.).</summary>
    Task<FinansalIstisna?> SetDurumAsync(Guid id, IstisnaDurumu durum, string kullanici, decimal? sistemeGirilenTutar = null, CancellationToken ct = default);

    /// <summary>Belirli bir tarihteki tüm istisnaları listeler.</summary>
    Task<IReadOnlyList<FinansalIstisna>> ListByDateAsync(DateOnly islemTarihi, CancellationToken ct = default);

    /// <summary>Belirli bir tarih için onaylı aktif istisnaların Pool aggregate değerlerini hesaplar.</summary>
    Task<IReadOnlyList<FinansalIstisnaPoolEntry>> GetPoolAggregateAsync(DateOnly islemTarihi, CancellationToken ct = default);

    /// <summary>Belirli bir istisnayı getirir.</summary>
    Task<FinansalIstisna?> GetByIdAsync(Guid id, CancellationToken ct = default);

    // ─── Faz 2 ───

    /// <summary>Dün ErtesiGuneDevredildi durumuna geçen kayıtları listeler (operatör farkındalığı).</summary>
    Task<IReadOnlyList<FinansalIstisna>> ListDevredilmisAsync(DateOnly bugun, CancellationToken ct = default);

    /// <summary>Devredilen kayıttan bugün için yeni istisna açar (ParentIstisnaId ile ilişkilendirir).</summary>
    Task<FinansalIstisna> CreateFromDevredilmisAsync(Guid parentId, DateOnly bugun, string kullanici, CancellationToken ct = default);

    // ─── Faz 3.1: Historical ───

    /// <summary>Bir istisnanın tüm history eventlerini getirir.</summary>
    Task<IReadOnlyList<FinansalIstisnaHistory>> GetHistoryAsync(Guid istisnaId, CancellationToken ct = default);

    /// <summary>Hedef tarihte bir istisnanın effective state'ini resolve eder.</summary>
    Task<HistoricalState> GetEffectiveStateAtAsync(Guid istisnaId, DateOnly targetDate, CancellationToken ct = default);

    /// <summary>Hedef tarih için historical runtime projection aggregate hesaplar.</summary>
    Task<IReadOnlyList<FinansalIstisnaPoolEntry>> GetPoolAggregateAtAsync(DateOnly islemTarihi, DateOnly targetDate, CancellationToken ct = default);
}

/// <summary>Create request.</summary>
public sealed record FinansalIstisnaCreateRequest(
    DateOnly IslemTarihi,
    IstisnaTuru Tur,
    IstisnaKategorisi Kategori,
    Domain.Reports.HesapKontrol.BankaHesapTuru HesapTuru,
    decimal BeklenenTutar,
    decimal GerceklesenTutar,
    decimal SistemeGirilenTutar,
    KasaEtkiYonu EtkiYonu,
    string? Neden,
    string? Aciklama,
    string? HedefHesapAciklama,
    string? OlusturanKullanici,
    Guid? ParentIstisnaId = null);

/// <summary>Update request.</summary>
public sealed record FinansalIstisnaUpdateRequest(
    decimal? BeklenenTutar = null,
    decimal? GerceklesenTutar = null,
    decimal? SistemeGirilenTutar = null,
    string? Neden = null,
    string? Aciklama = null,
    string? GuncelleyenKullanici = null);

/// <summary>Pool injection için aggregate entry.</summary>
public sealed record FinansalIstisnaPoolEntry(
    string CanonicalKey,
    decimal Value,
    string Notes);
