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

        if (kayit.HesapTuru == BankaHesapTuru.Stopaj)
        {
            // Stopaj KasaEtkisi mekanizmasının tamamen dışında — yalnızca lifecycle transition.
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("HesapKontrol takibe alındı: {Id} by {User}", kayitId, actorUsername);
            return true;
        }

        // Revision 3 ORIGIN MATERIALIZATION (Stage 2 blocker kapanışı): bir kayıt authoritative olarak
        // Takipte'ye geçtiği ANDA origin (KasaEtkisiTutari/KasaEtkisiIsTarihi) burada, lifecycle
        // transition'la AYNI transaction içinde materialize edilir. Daha önce origin yalnızca
        // ExecutePairResolutionAsync sırasında (yani karşı taraf bulununca) yazılıyordu — bu, henüz
        // eşleşme oluşmadan hesaplanan immutable day-1 snapshot'ların origin etkisini hiç görmemesine
        // sebep oluyordu. İmza kuralı (Eksik→-Tutar, Fazla→+Tutar) ve origin tarihi (kayit.AnalizTarihi)
        // ExecutePairResolutionAsync'in aynı kayıt için hesaplayacağı değerlerle BİREBİR aynıdır — bu
        // yüzden daha sonra bir pair resolution çalıştığında CAS bunu "exact-match idempotent success"
        // olarak görür (Cas.cs'teki mevcut kontrat hiç değişmedi).
        //
        // EnableRetryOnFailure ile yapılandırılmış SqlServerRetryingExecutionStrategy, kullanıcı-başlatımlı
        // transaction'ları yalnızca CreateExecutionStrategy().ExecuteAsync(...) delege'i İÇİNDE kabul
        // eder (bkz. ExecutePairResolutionAsync/MaterializeLegacyKasaEtkisiAsync'teki aynı düzeltme).
        // CasSetKasaEtkisiAsync ExecuteUpdateAsync ile yazar — tracked `kayit` örneğinin
        // KasaEtkisiTutari/IsTarihi alanlarına HİÇ dokunmaz (private set, yalnızca SetKasaEtkisi ile
        // yazılır) — bu yüzden aşağıdaki SaveChangesAsync'in ürettiği UPDATE yalnızca gerçekten
        // değiştirilen (Durum/OnaylayanKullanici/...) kolonları içerir; KasaEtkisi* kolonlarının stale
        // in-memory değerle ezilme riski yoktur (ApprovePotentialMatchAsync'teki aynı ispatlanmış desen).
        var originImpact = kayit.Yon == KayitYonu.Fazla ? kayit.Tutar : -kayit.Tutar;
        var originStrategy = _db.Database.CreateExecutionStrategy();
        var originResult = await originStrategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var originCas = await CasSetKasaEtkisiAsync(kayitId, originImpact, kayit.AnalizTarihi, ct);
                if (!originCas.Success)
                {
                    await tx.RollbackAsync(ct);
                    return originCas;
                }

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return originCas;
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        });

        if (!originResult.Success)
        {
            _logger.LogWarning(
                "[HK-ORIGIN-CAS-CONFLICT] Action=StartTracking KayitId={KayitId} Reason={Reason}",
                kayitId, originResult.Conflict);
            return false;
        }

        _logger.LogInformation("HesapKontrol takibe alındı: {Id} by {User}", kayitId, actorUsername);
        return true;
    }

    /// <summary>
    /// Revision 3 Manual Resolve Write-BusinessDate Surgical Closure: eski tek ResolveTrackedAsync
    /// iki tip-güvenli komuta bölündü (ResolveTrackedStopajAsync/ResolveTrackedFinancialAsync).
    /// Stopaj KasaEtkisi finansal mekanizmasının tamamen dışındadır — date-free kalır, hiçbir
    /// zaman KasaEtkisiTersDonusIsTarihi almaz. Yanlış tür/duruma yönlendirilirse fail-closed (false).
    /// </summary>
    public async Task<bool> ResolveTrackedStopajAsync(Guid kayitId, int actorUserId, string? actorUsername, string? not, CancellationToken ct = default)
    {
        ValidateActorUserId(actorUserId);
        var kayit = await _db.HesapKontrolKayitlari.FindAsync(new object[] { kayitId }, ct);
        if (kayit == null || kayit.Durum != KayitDurumu.Takipte || kayit.HesapTuru != BankaHesapTuru.Stopaj)
            return false;

        kayit.Durum = KayitDurumu.Onaylandi;
        kayit.KullaniciOnay = true;
        kayit.ResolvedByUserId = actorUserId;
        kayit.CozulmeTarihi = DateOnly.FromDateTime(DateTime.UtcNow);
        kayit.Notlar = (kayit.Notlar ?? "") +
            $"\n[{DateTime.UtcNow:dd.MM.yyyy HH:mm}] Çözüldü: {actorUsername}" +
            (not != null ? $" — {not}" : "");

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("HesapKontrol takipte kayit çözüldü (Stopaj): {Id} by {User}", kayitId, actorUsername);
        return true;
    }

    /// <summary>
    /// Revision 3: reversalBusinessDate artık çağıranın (Manuel Resolve orkestrasyonu, bkz.
    /// IManualResolveWriteBusinessDateResolver) sağlamak ZORUNDA olduğu required, sistem saatinden
    /// türetilmemiş bir parametredir — burada hiçbir zaman DateTime.Now/UtcNow/Today veya
    /// CozulmeTarihi'nden hesaplanmaz. CozulmeTarihi (lifecycle/audit) ayrı ve bağımsız hesaplanmaya
    /// devam eder — ikisi kasıtlı olarak farklı semantiklere sahiptir ve farklı değerler taşıyabilir.
    /// Origin StartTrackingAsync sırasında zaten yazıldığı için burada yalnızca reversal'ı
    /// (KasaEtkisiTersDonusIsTarihi) AYNI transaction içinde CAS ile yazarız.
    /// </summary>
    public async Task<bool> ResolveTrackedFinancialAsync(Guid kayitId, DateOnly reversalBusinessDate, int actorUserId, string? actorUsername, string? not, CancellationToken ct = default)
    {
        ValidateActorUserId(actorUserId);
        var kayit = await _db.HesapKontrolKayitlari.FindAsync(new object[] { kayitId }, ct);
        if (kayit == null || kayit.Durum != KayitDurumu.Takipte || kayit.HesapTuru == BankaHesapTuru.Stopaj)
            return false;

        kayit.Durum = KayitDurumu.Onaylandi;
        kayit.KullaniciOnay = true;
        kayit.ResolvedByUserId = actorUserId;
        kayit.CozulmeTarihi = DateOnly.FromDateTime(DateTime.UtcNow);
        kayit.Notlar = (kayit.Notlar ?? "") +
            $"\n[{DateTime.UtcNow:dd.MM.yyyy HH:mm}] Çözüldü: {actorUsername}" +
            (not != null ? $" — {not}" : "");

        var reversalStrategy = _db.Database.CreateExecutionStrategy();
        var reversalResult = await reversalStrategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var reversalCas = await CasSetTersDonusAsync(kayitId, reversalBusinessDate, ct);
                if (!reversalCas.Success)
                {
                    await tx.RollbackAsync(ct);
                    return reversalCas;
                }

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return reversalCas;
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        });

        if (!reversalResult.Success)
        {
            _logger.LogWarning(
                "[HK-REVERSAL-CAS-CONFLICT] Action=ResolveTrackedFinancial KayitId={KayitId} Reason={Reason}",
                kayitId, reversalResult.Conflict);
            return false;
        }

        _logger.LogInformation(
            "HesapKontrol takipte kayit çözüldü (Financial): {Id} by {User} ReversalBusinessDate={ReversalBusinessDate}",
            kayitId, actorUsername, reversalBusinessDate);
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
        if ((eksik.Durum != KayitDurumu.Takipte && eksik.Durum != KayitDurumu.Acik)
            || (fazla.Durum != KayitDurumu.Takipte && fazla.Durum != KayitDurumu.Acik))
            return false;
        if (eksik.Yon != KayitYonu.Eksik
            || fazla.Yon != KayitYonu.Fazla
            || eksik.HesapTuru != fazla.HesapTuru
            || eksik.HesapTuru == BankaHesapTuru.Stopaj
            || eksik.Tutar <= 0m
            || fazla.Tutar <= 0m)
            return false;

        const decimal tutarTolerans = 0.01m;
        if (Math.Abs(eksik.Tutar - fazla.Tutar) > tutarTolerans)
        {
            _logger.LogWarning(
                "[HK-PARTIAL-MATCH-BLOCKED] EksikId={EksikId} FazlaId={FazlaId} EksikTutar={EksikTutar} FazlaTutar={FazlaTutar} Reason=AuditSafeRemainderModelRequired",
                eksikKayitId,
                fazlaKayitId,
                eksik.Tutar,
                fazla.Tutar);
            return false;
        }

        var now = DateTime.UtcNow;
        var bugun = DateOnly.FromDateTime(now);

        // Revision 3: older/later date-comparison seçimi (Eksik/Fazla etiketine göre DEĞİL).
        // Eksik her zaman -Tutar, Fazla her zaman +Tutar imzalı katkı taşır (mevcut Net() işaret
        // kuralı ile aynı: BuildActiveFollowTotals'taki Yon==Fazla?+Tutar:-Tutar).
        var (olderId, olderDate, olderImpact, laterId, laterDate, laterImpact) =
            eksik.AnalizTarihi <= fazla.AnalizTarihi
                ? (eksik.Id, eksik.AnalizTarihi, -eksik.Tutar, fazla.Id, fazla.AnalizTarihi, fazla.Tutar)
                : (fazla.Id, fazla.AnalizTarihi, fazla.Tutar, eksik.Id, eksik.AnalizTarihi, -eksik.Tutar);

        var pairResult = await ExecutePairResolutionAsync(
            olderId, olderDate, olderImpact,
            laterId, laterDate, laterImpact,
            bugun, ct);

        if (!pairResult.Success)
        {
            _logger.LogWarning(
                "[HK-PAIR-CAS-CONFLICT] Action=ApprovePotentialMatch EksikId={EksikId} FazlaId={FazlaId} Reason={Reason}",
                eksikKayitId, fazlaKayitId, pairResult.ConflictReason);
            return false;
        }

        var bildirim = $"✅ Kısmi eşleşme kullanıcı tarafından onaylandı ({eksik.DosyaNo ?? "N/A"} {eksik.Tutar:N2} ₺)";

        // Audit-only alanlar: finansal/yaşam döngüsü (Durum/CozulmeTarihi/CozulmeKaynakId/KasaEtkisi*)
        // ExecutePairResolutionAsync içinde CAS ile zaten commit edildi. Bu tracked-entity update'ler
        // yalnızca audit alanlarını değiştirir — Durum/CozulmeTarihi/CozulmeKaynakId/KasaEtkisi* bu
        // instance'larda hiç set edilmediği için EF change tracker onları UPDATE'e dahil etmez
        // (stale tracked değer geri yazılmaz).
        eksik.KullaniciOnay = true;
        eksik.OnaylayanKullanici = actorUsername;
        eksik.ApprovedByUserId = actorUserId;
        eksik.OnayTarihi = now;
        eksik.Notlar = (eksik.Notlar ?? "") +
            $"\n[{DateTime.UtcNow:dd.MM.yyyy HH:mm}] {bildirim} — Onaylayan: {actorUsername}";

        fazla.KullaniciOnay = true;
        fazla.OnaylayanKullanici = actorUsername;
        fazla.ApprovedByUserId = actorUserId;
        fazla.OnayTarihi = now;
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
