#nullable enable
using KasaManager.Domain.Reports.HesapKontrol;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;

namespace KasaManager.Infrastructure.Services;

/// <summary>
/// MS2 CQRS-lite: Yazma işlemleri (Confirm, Cancel, Track, Resolve, Revert, Approve, Reject).
/// </summary>
public sealed partial class BankaHesapKontrolService
{
    // ═════════════════════════════════════════════════════════════
    // Kullanıcı Etkileşimi
    // ═════════════════════════════════════════════════════════════

    public async Task<bool> ConfirmMatchAsync(Guid kayitId, int actorUserId, string? actorUsername, string? not, CancellationToken ct = default)
    {
        ValidateActorUserId(actorUserId);
        var kayit = await _db.HesapKontrolKayitlari.FindAsync(new object[] { kayitId }, ct);
        if (kayit == null || kayit.Durum != KayitDurumu.Acik) return false;

        kayit.Durum = KayitDurumu.Onaylandi;
        kayit.KullaniciOnay = true;
        kayit.OnaylayanKullanici = actorUsername;
        kayit.ApprovedByUserId = actorUserId;
        kayit.OnayTarihi = DateTime.UtcNow;
        kayit.CozulmeTarihi = DateOnly.FromDateTime(DateTime.UtcNow);
        kayit.Notlar = (kayit.Notlar ?? "") +
            $"\n[{DateTime.UtcNow:dd.MM.yyyy HH:mm}] Kullanıcı onayı: {actorUsername}" +
            (not != null ? $" — {not}" : "");

        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> CancelAsync(Guid kayitId, int actorUserId, string? actorUsername, string? sebep, CancellationToken ct = default)
    {
        ValidateActorUserId(actorUserId);
        var kayit = await _db.HesapKontrolKayitlari.FindAsync(new object[] { kayitId }, ct);
        if (kayit == null || kayit.Durum != KayitDurumu.Acik) return false;

        kayit.Durum = KayitDurumu.Iptal;
        kayit.CancelledByUserId = actorUserId;
        kayit.CozulmeTarihi = DateOnly.FromDateTime(DateTime.UtcNow);
        kayit.Notlar = (kayit.Notlar ?? "") +
            $"\n[{DateTime.UtcNow:dd.MM.yyyy HH:mm}] İptal eden: {actorUsername}" +
            (sebep != null ? $" — Sebep: {sebep}" : "");

        await _db.SaveChangesAsync(ct);
        return true;
    }

    // ═════════════════════════════════════════════════════════════════
    // Takip İşlemleri
    // ═════════════════════════════════════════════════════════════════

    public async Task<bool> StartTrackingAsync(Guid kayitId, int actorUserId, string? actorUsername, string? not, CancellationToken ct = default)
    {
        ValidateActorUserId(actorUserId);
        var kayit = await _db.HesapKontrolKayitlari.FindAsync(new object[] { kayitId }, ct);
        if (kayit == null || kayit.Durum != KayitDurumu.Acik) return false;

        var followFp = GetFollowIdentityFingerprint(kayit);
        _logger.LogInformation(
            "[HK-FOLLOW-FINGERPRINT] Action=StartTracking KayitId={KayitId} Date={Date} FollowIdentity={FollowIdentity} HesapTuru={HesapTuru} Yon={Yon} DosyaNo={DosyaNo} Birim={Birim} Tutar={Tutar}",
            kayit.Id,
            kayit.AnalizTarihi,
            followFp,
            kayit.HesapTuru,
            kayit.Yon,
            kayit.DosyaNo,
            kayit.BirimAdi,
            kayit.Tutar);

        var takipteAyniGercekKayitlar = await _db.HesapKontrolKayitlari
            .Where(x => x.Id != kayitId
                     && x.Durum == KayitDurumu.Takipte
                     && x.HesapTuru != BankaHesapTuru.Stopaj)
            .ToListAsync(ct);
        var takipteDuplicate = takipteAyniGercekKayitlar
            .FirstOrDefault(x => GetFollowIdentityFingerprint(x) == followFp);

        if (takipteDuplicate != null)
        {
            var eskiTarih = takipteDuplicate.AnalizTarihi;
            var orijinalTarih = takipteDuplicate.AnalizTarihi <= kayit.AnalizTarihi
                ? takipteDuplicate.AnalizTarihi
                : kayit.AnalizTarihi;

            takipteDuplicate.AnalizTarihi = orijinalTarih;
            takipteDuplicate.Notlar = (takipteDuplicate.Notlar ?? "") +
                $"\n[{DateTime.UtcNow:dd.MM.yyyy HH:mm}] Duplicate takip talebi ile orijinal eksik tarihi {orijinalTarih:dd.MM.yyyy} olarak hizalandi. Kaynak: {kayit.Id:N}";

            kayit.Durum = KayitDurumu.Iptal;
            kayit.CancelledByUserId = actorUserId;
            kayit.CozulmeTarihi = DateOnly.FromDateTime(DateTime.Now);
            kayit.Notlar = (kayit.Notlar ?? "") +
                $"\n[{DateTime.UtcNow:dd.MM.yyyy HH:mm}] Duplicate takip kaydi ile birlestirildi. Aktif takip: {takipteDuplicate.Id:N}";

            await _db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "[HK-DUPLICATE-FOLLOW-MERGED] Action=StartTracking FollowIdentity={FollowIdentity} TrackedId={TrackedId} OldTrackedDate={OldTrackedDate} NewTrackedDate={NewTrackedDate} PassiveDuplicateId={PassiveDuplicateId}",
                followFp,
                takipteDuplicate.Id,
                eskiTarih,
                takipteDuplicate.AnalizTarihi,
                kayit.Id);
            return true;
        }
        kayit.Durum = KayitDurumu.Takipte;
        kayit.OnaylayanKullanici = actorUsername;
        kayit.TrackingStartedByUserId = actorUserId;
        kayit.OnayTarihi = DateTime.UtcNow;
        kayit.TakipBaslangicTarihi = DateOnly.FromDateTime(DateTime.Now);
        kayit.Notlar = (kayit.Notlar ?? "") +
            $"\n[{DateTime.UtcNow:dd.MM.yyyy HH:mm}] Takibe alan: {actorUsername}" +
            (not != null ? $" — {not}" : "");

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("HesapKontrol takibe alındı: {Id} by {User}", kayitId, actorUsername);
        return true;
    }

    public async Task<bool> ResolveTrackedAsync(Guid kayitId, int actorUserId, string? actorUsername, string? not, CancellationToken ct = default)
    {
        ValidateActorUserId(actorUserId);
        var kayit = await _db.HesapKontrolKayitlari.FindAsync(new object[] { kayitId }, ct);
        if (kayit == null || kayit.Durum != KayitDurumu.Takipte) return false;

        kayit.Durum = KayitDurumu.Onaylandi;
        kayit.KullaniciOnay = true;
        kayit.ResolvedByUserId = actorUserId;
        kayit.CozulmeTarihi = DateOnly.FromDateTime(DateTime.UtcNow);
        kayit.Notlar = (kayit.Notlar ?? "") +
            $"\n[{DateTime.UtcNow:dd.MM.yyyy HH:mm}] Çözüldü: {actorUsername}" +
            (not != null ? $" — {not}" : "");

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("HesapKontrol takipte kayit çözüldü: {Id} by {User}", kayitId, actorUsername);
        return true;
    }

    // ═════════════════════════════════════════════════════════════════
    // CrossDay Potansiyel Eşleşme Onay/Red
    // ═════════════════════════════════════════════════════════════════

    public async Task<bool> ApprovePotentialMatchAsync(Guid eksikKayitId, Guid fazlaKayitId, int actorUserId, string? actorUsername, CancellationToken ct = default)
    {
        ValidateActorUserId(actorUserId);
        var eksik = await _db.HesapKontrolKayitlari.FindAsync(new object[] { eksikKayitId }, ct);
        var fazla = await _db.HesapKontrolKayitlari.FindAsync(new object[] { fazlaKayitId }, ct);
        if (eksik == null || fazla == null) return false;
        if (eksik.Durum != KayitDurumu.Takipte && eksik.Durum != KayitDurumu.Acik) return false;

        var bugun = DateOnly.FromDateTime(DateTime.Now);
        var bildirim = $"✅ Kısmi eşleşme kullanıcı tarafından onaylandı ({eksik.DosyaNo ?? "N/A"} {eksik.Tutar:N2} ₺)";

        eksik.Durum = KayitDurumu.Cozuldu;
        eksik.CozulmeTarihi = bugun;
        eksik.CozulmeKaynakId = fazlaKayitId;
        eksik.KullaniciOnay = true;
        eksik.OnaylayanKullanici = actorUsername;
        eksik.ApprovedByUserId = actorUserId;
        eksik.OnayTarihi = DateTime.UtcNow;
        eksik.Notlar = (eksik.Notlar ?? "") +
            $"\n[{DateTime.UtcNow:dd.MM.yyyy HH:mm}] {bildirim} — Onaylayan: {actorUsername}";

        fazla.Durum = KayitDurumu.Cozuldu;
        fazla.CozulmeTarihi = bugun;
        fazla.CozulmeKaynakId = eksikKayitId;
        fazla.KullaniciOnay = true;
        fazla.OnaylayanKullanici = actorUsername;
        fazla.ApprovedByUserId = actorUserId;
        fazla.Notlar = (fazla.Notlar ?? "") +
            $"\n[{DateTime.UtcNow:dd.MM.yyyy HH:mm}] {bildirim} — Eşleşen eksik: {eksikKayitId:N}";

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("CrossDay kısmi eşleşme onaylandı: Eksik {EksikId} ↔ Fazla {FazlaId} by {User}",
            eksikKayitId, fazlaKayitId, actorUsername);
        return true;
    }

    public async Task<bool> RejectPotentialMatchAsync(Guid eksikKayitId, Guid fazlaKayitId, int actorUserId, string? actorUsername, CancellationToken ct = default)
    {
        ValidateActorUserId(actorUserId);
        var eksik = await _db.HesapKontrolKayitlari.FindAsync(new object[] { eksikKayitId }, ct);
        if (eksik == null) return false;

        eksik.Notlar = (eksik.Notlar ?? "") +
            $"\n[{DateTime.UtcNow:dd.MM.yyyy HH:mm}] ❌ Kısmi eşleşme reddedildi (Fazla: {fazlaKayitId:N}) — {actorUsername}: Bu eşleşme geçerli değil.";

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("CrossDay kısmi eşleşme reddedildi: Eksik {EksikId} ↛ Fazla {FazlaId} by {User}",
            eksikKayitId, fazlaKayitId, actorUsername);
        return true;
    }

    public async Task<bool> RevertAsync(Guid kayitId, int actorUserId, string? actorUsername, string? sebep, CancellationToken ct = default)
    {
        ValidateActorUserId(actorUserId);
        var kayit = await _db.HesapKontrolKayitlari.FindAsync(new object[] { kayitId }, ct);
        if (kayit == null) return false;

        // Sadece kapalı durumlardan geri alınabilir
        if (kayit.Durum == KayitDurumu.Acik) return false;

        var oncekiDurum = kayit.Durum;
        var dogrudanTakipGecisiGeriAliniyor = oncekiDurum == KayitDurumu.Takipte;

        kayit.Durum = KayitDurumu.Acik;
        kayit.CozulmeTarihi = null;
        kayit.CozulmeKaynakId = null;
        kayit.GeriAlanKullanici = actorUsername;
        kayit.GeriAlmaTarihi = DateTime.UtcNow;
        if (dogrudanTakipGecisiGeriAliniyor)
        {
            kayit.TakipBaslangicTarihi = null;
            kayit.TrackingStartedByUserId = null;
        }

        switch (oncekiDurum)
        {
            case KayitDurumu.Iptal:
                kayit.CancelledByUserId = null;
                break;
            case KayitDurumu.Onaylandi:
                if (kayit.ResolvedByUserId.HasValue)
                    kayit.ResolvedByUserId = null;
                else
                    kayit.ApprovedByUserId = null;
                break;
            case KayitDurumu.Cozuldu:
                kayit.ResolvedByUserId = null;
                kayit.ApprovedByUserId = null;
                break;
        }
        kayit.Notlar = (kayit.Notlar ?? "") +
            $"\n[{DateTime.UtcNow:dd.MM.yyyy HH:mm}] ↩ Geri alan: {actorUsername} (önceki durum: {oncekiDurum})" +
            (sebep != null ? $" — {sebep}" : "");

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("HesapKontrol geri alındı: {Id} ({OncekiDurum} → Acik) by {User}",
            kayitId, oncekiDurum, actorUsername);
        return true;
    }

    private static void ValidateActorUserId(int actorUserId)
    {
        if (actorUserId <= 0)
            throw new ArgumentOutOfRangeException(nameof(actorUserId));
    }
}
