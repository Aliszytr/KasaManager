#nullable enable
using KasaManager.Domain.Reports.HesapKontrol;
using Microsoft.Extensions.Logging;

namespace KasaManager.Infrastructure.Services;

/// <summary>
/// MS2 CQRS-lite: Yazma işlemleri (Confirm, Cancel, Track, Resolve, Revert, Approve, Reject).
/// </summary>
public sealed partial class BankaHesapKontrolService
{
    // ═════════════════════════════════════════════════════════════
    // Kullanıcı Etkileşimi
    // ═════════════════════════════════════════════════════════════

    public async Task<bool> ConfirmMatchAsync(Guid kayitId, string kullanici, string? not)
    {
        var kayit = await _db.HesapKontrolKayitlari.FindAsync(kayitId);
        if (kayit == null || kayit.Durum != KayitDurumu.Acik) return false;

        kayit.Durum = KayitDurumu.Onaylandi;
        kayit.KullaniciOnay = true;
        kayit.OnaylayanKullanici = kullanici;
        kayit.OnayTarihi = DateTime.UtcNow;
        kayit.CozulmeTarihi = DateOnly.FromDateTime(DateTime.UtcNow);
        kayit.Notlar = (kayit.Notlar ?? "") +
            $"\n[{DateTime.UtcNow:dd.MM.yyyy HH:mm}] Kullanıcı onayı: {kullanici}" +
            (not != null ? $" — {not}" : "");

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> CancelAsync(Guid kayitId, string kullanici, string? sebep)
    {
        var kayit = await _db.HesapKontrolKayitlari.FindAsync(kayitId);
        if (kayit == null || kayit.Durum != KayitDurumu.Acik) return false;

        kayit.Durum = KayitDurumu.Iptal;
        kayit.CozulmeTarihi = DateOnly.FromDateTime(DateTime.UtcNow);
        kayit.Notlar = (kayit.Notlar ?? "") +
            $"\n[{DateTime.UtcNow:dd.MM.yyyy HH:mm}] İptal eden: {kullanici}" +
            (sebep != null ? $" — Sebep: {sebep}" : "");

        await _db.SaveChangesAsync();
        return true;
    }

    // ═════════════════════════════════════════════════════════════════
    // Takip İşlemleri
    // ═════════════════════════════════════════════════════════════════

    public async Task<bool> StartTrackingAsync(Guid kayitId, string kullanici, string? not)
    {
        var kayit = await _db.HesapKontrolKayitlari.FindAsync(kayitId);
        if (kayit == null || kayit.Durum != KayitDurumu.Acik) return false;

        kayit.Durum = KayitDurumu.Takipte;
        kayit.OnaylayanKullanici = kullanici;
        kayit.OnayTarihi = DateTime.UtcNow;
        kayit.TakipBaslangicTarihi = DateOnly.FromDateTime(DateTime.Now);
        kayit.Notlar = (kayit.Notlar ?? "") +
            $"\n[{DateTime.UtcNow:dd.MM.yyyy HH:mm}] Takibe alan: {kullanici}" +
            (not != null ? $" — {not}" : "");

        await _db.SaveChangesAsync();
        _logger.LogInformation("HesapKontrol takibe alındı: {Id} by {User}", kayitId, kullanici);
        return true;
    }

    public async Task<bool> ResolveTrackedAsync(Guid kayitId, string kullanici, string? not)
    {
        var kayit = await _db.HesapKontrolKayitlari.FindAsync(kayitId);
        if (kayit == null || kayit.Durum != KayitDurumu.Takipte) return false;

        kayit.Durum = KayitDurumu.Onaylandi;
        kayit.KullaniciOnay = true;
        kayit.CozulmeTarihi = DateOnly.FromDateTime(DateTime.UtcNow);
        kayit.Notlar = (kayit.Notlar ?? "") +
            $"\n[{DateTime.UtcNow:dd.MM.yyyy HH:mm}] Çözüldü: {kullanici}" +
            (not != null ? $" — {not}" : "");

        await _db.SaveChangesAsync();
        _logger.LogInformation("HesapKontrol takipte kayit çözüldü: {Id} by {User}", kayitId, kullanici);
        return true;
    }

    // ═════════════════════════════════════════════════════════════════
    // CrossDay Potansiyel Eşleşme Onay/Red
    // ═════════════════════════════════════════════════════════════════

    public async Task<bool> ApprovePotentialMatchAsync(Guid eksikKayitId, Guid fazlaKayitId, string kullanici)
    {
        var eksik = await _db.HesapKontrolKayitlari.FindAsync(eksikKayitId);
        var fazla = await _db.HesapKontrolKayitlari.FindAsync(fazlaKayitId);
        if (eksik == null || fazla == null) return false;
        if (eksik.Durum != KayitDurumu.Takipte && eksik.Durum != KayitDurumu.Acik) return false;

        var bugun = DateOnly.FromDateTime(DateTime.Now);
        var bildirim = $"✅ Kısmi eşleşme kullanıcı tarafından onaylandı ({eksik.DosyaNo ?? "N/A"} {eksik.Tutar:N2} ₺)";

        eksik.Durum = KayitDurumu.Cozuldu;
        eksik.CozulmeTarihi = bugun;
        eksik.CozulmeKaynakId = fazlaKayitId;
        eksik.KullaniciOnay = true;
        eksik.OnaylayanKullanici = kullanici;
        eksik.OnayTarihi = DateTime.UtcNow;
        eksik.Notlar = (eksik.Notlar ?? "") +
            $"\n[{DateTime.UtcNow:dd.MM.yyyy HH:mm}] {bildirim} — Onaylayan: {kullanici}";

        fazla.Durum = KayitDurumu.Cozuldu;
        fazla.CozulmeTarihi = bugun;
        fazla.CozulmeKaynakId = eksikKayitId;
        fazla.KullaniciOnay = true;
        fazla.OnaylayanKullanici = kullanici;
        fazla.Notlar = (fazla.Notlar ?? "") +
            $"\n[{DateTime.UtcNow:dd.MM.yyyy HH:mm}] {bildirim} — Eşleşen eksik: {eksikKayitId:N}";

        await _db.SaveChangesAsync();
        _logger.LogInformation("CrossDay kısmi eşleşme onaylandı: Eksik {EksikId} ↔ Fazla {FazlaId} by {User}",
            eksikKayitId, fazlaKayitId, kullanici);
        return true;
    }

    public async Task<bool> RejectPotentialMatchAsync(Guid eksikKayitId, Guid fazlaKayitId, string kullanici)
    {
        var eksik = await _db.HesapKontrolKayitlari.FindAsync(eksikKayitId);
        if (eksik == null) return false;

        eksik.Notlar = (eksik.Notlar ?? "") +
            $"\n[{DateTime.UtcNow:dd.MM.yyyy HH:mm}] ❌ Kısmi eşleşme reddedildi (Fazla: {fazlaKayitId:N}) — {kullanici}: Bu eşleşme geçerli değil.";

        await _db.SaveChangesAsync();
        _logger.LogInformation("CrossDay kısmi eşleşme reddedildi: Eksik {EksikId} ↛ Fazla {FazlaId} by {User}",
            eksikKayitId, fazlaKayitId, kullanici);
        return true;
    }

    public async Task<bool> RevertAsync(Guid kayitId, string kullanici, string? sebep)
    {
        var kayit = await _db.HesapKontrolKayitlari.FindAsync(kayitId);
        if (kayit == null) return false;

        // Sadece kapalı durumlardan geri alınabilir
        if (kayit.Durum == KayitDurumu.Acik) return false;

        var oncekiDurum = kayit.Durum;

        kayit.Durum = KayitDurumu.Acik;
        kayit.CozulmeTarihi = null;
        kayit.CozulmeKaynakId = null;
        kayit.GeriAlanKullanici = kullanici;
        kayit.GeriAlmaTarihi = DateTime.UtcNow;
        kayit.TakipBaslangicTarihi = null;
        kayit.Notlar = (kayit.Notlar ?? "") +
            $"\n[{DateTime.UtcNow:dd.MM.yyyy HH:mm}] ↩ Geri alan: {kullanici} (önceki durum: {oncekiDurum})" +
            (sebep != null ? $" — {sebep}" : "");

        await _db.SaveChangesAsync();
        _logger.LogInformation("HesapKontrol geri alındı: {Id} ({OncekiDurum} → Acik) by {User}",
            kayitId, oncekiDurum, kullanici);
        return true;
    }
}
