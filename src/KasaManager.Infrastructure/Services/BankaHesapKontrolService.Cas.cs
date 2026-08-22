#nullable enable
using KasaManager.Domain.Reports.HesapKontrol;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KasaManager.Infrastructure.Services;

/// <summary>
/// KASAMANAGER-2026-08-21-FINAL-PLAN-CLOSURE Revision 3, Section 2/4.
/// DB-seviyesi CAS (compare-and-set) yardımcıları ve tek-transaction pair resolution.
/// ExecuteUpdateAsync tracked entity state'ini güncellemez — bu yüzden her CAS metodu
/// kendi authoritative sonucunu (yazdığı değerleri) döner; çağıran taraf ASLA stale
/// tracked entity alanı (ör. later.KasaEtkisiIsTarihi!.Value) okumamalıdır.
/// </summary>
public sealed partial class BankaHesapKontrolService
{
    public readonly record struct CasResult(bool Success, decimal? PersistedImpact, DateOnly? PersistedImpactDate, string? Conflict = null);
    public readonly record struct CasDateResult(bool Success, DateOnly? PersistedDate, string? Conflict = null);
    public readonly record struct CasLifecycleResult(bool Success, string? Conflict = null);
    public sealed record PairResolutionResult(bool Success, string? ConflictReason = null);

    /// <summary>
    /// KasaEtkisiTutari/KasaEtkisiIsTarihi set-once CAS.
    /// NULL→X: yazar. X→aynı X: idempotent success. X→farklı Y: fail-closed.
    /// </summary>
    private async Task<CasResult> CasSetKasaEtkisiAsync(Guid id, decimal tutar, DateOnly isTarihi, CancellationToken ct)
    {
        var rows = await _db.HesapKontrolKayitlari
            .Where(x => x.Id == id && x.KasaEtkisiTutari == null && x.KasaEtkisiIsTarihi == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.KasaEtkisiTutari, tutar)
                .SetProperty(x => x.KasaEtkisiIsTarihi, isTarihi), ct);

        if (rows == 1)
            return new CasResult(true, tutar, isTarihi);

        var current = await _db.HesapKontrolKayitlari
            .Where(x => x.Id == id)
            .Select(x => new { x.KasaEtkisiTutari, x.KasaEtkisiIsTarihi })
            .FirstOrDefaultAsync(ct);

        if (current == null)
            return new CasResult(false, null, null, $"[CAS-NOTFOUND] KasaEtkisi: Id={id} bulunamadı.");

        if (current.KasaEtkisiTutari == tutar && current.KasaEtkisiIsTarihi == isTarihi)
            return new CasResult(true, tutar, isTarihi); // idempotent no-op

        _logger.LogWarning(
            "[CAS-CONFLICT] KasaEtkisi: Id={Id} Existing(Tutar={ExistingTutar},IsTarihi={ExistingIsTarihi}) Requested(Tutar={ReqTutar},IsTarihi={ReqIsTarihi})",
            id, current.KasaEtkisiTutari, current.KasaEtkisiIsTarihi, tutar, isTarihi);
        return new CasResult(false, current.KasaEtkisiTutari, current.KasaEtkisiIsTarihi,
            $"KasaEtkisi zaten Tutar={current.KasaEtkisiTutari} IsTarihi={current.KasaEtkisiIsTarihi} — " +
            $"farklı değer (Tutar={tutar} IsTarihi={isTarihi}) reddedildi.");
    }

    /// <summary>
    /// KasaEtkisiTersDonusIsTarihi set-once CAS. KasaEtkisiIsTarihi set edilmemişse veya
    /// tersDonusIsTarihi ondan önceyse fail-closed (reversal &lt; origin asla kabul edilmez).
    /// </summary>
    private async Task<CasDateResult> CasSetTersDonusAsync(Guid id, DateOnly tersDonusIsTarihi, CancellationToken ct)
    {
        var rows = await _db.HesapKontrolKayitlari
            .Where(x => x.Id == id
                     && x.KasaEtkisiIsTarihi != null
                     && x.KasaEtkisiIsTarihi <= tersDonusIsTarihi
                     && x.KasaEtkisiTersDonusIsTarihi == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.KasaEtkisiTersDonusIsTarihi, tersDonusIsTarihi), ct);

        if (rows == 1)
            return new CasDateResult(true, tersDonusIsTarihi);

        var current = await _db.HesapKontrolKayitlari
            .Where(x => x.Id == id)
            .Select(x => new { x.KasaEtkisiIsTarihi, x.KasaEtkisiTersDonusIsTarihi })
            .FirstOrDefaultAsync(ct);

        if (current == null)
            return new CasDateResult(false, null, $"[CAS-NOTFOUND] TersDonus: Id={id} bulunamadı.");
        if (!current.KasaEtkisiIsTarihi.HasValue)
            return new CasDateResult(false, null, "KasaEtkisi henüz ayarlanmamış bir kayıt için ters dönüş ayarlanamaz.");
        if (current.KasaEtkisiIsTarihi.Value > tersDonusIsTarihi)
            return new CasDateResult(false, current.KasaEtkisiTersDonusIsTarihi,
                $"Ters dönüş tarihi ({tersDonusIsTarihi}) orijinal etki tarihinden ({current.KasaEtkisiIsTarihi.Value}) önce olamaz.");
        if (current.KasaEtkisiTersDonusIsTarihi == tersDonusIsTarihi)
            return new CasDateResult(true, tersDonusIsTarihi); // idempotent no-op

        _logger.LogWarning(
            "[CAS-CONFLICT] TersDonus: Id={Id} Existing={Existing} Requested={Requested}",
            id, current.KasaEtkisiTersDonusIsTarihi, tersDonusIsTarihi);
        return new CasDateResult(false, current.KasaEtkisiTersDonusIsTarihi,
            $"KasaEtkisiTersDonus zaten {current.KasaEtkisiTersDonusIsTarihi} — farklı değer ({tersDonusIsTarihi}) reddedildi.");
    }

    /// <summary>
    /// Durum/CozulmeTarihi/CozulmeKaynakId set-once CAS. Yalnızca Acik/Takipte durumundan geçiş kabul eder
    /// (ApprovePotentialMatchAsync'teki mevcut guard ile aynı kural).
    /// </summary>
    private async Task<CasLifecycleResult> CasSetLifecycleAsync(
        Guid id, KayitDurumu newDurum, DateOnly cozulmeTarihi, Guid cozulmeKaynakId, CancellationToken ct)
    {
        var rows = await _db.HesapKontrolKayitlari
            .Where(x => x.Id == id && (x.Durum == KayitDurumu.Acik || x.Durum == KayitDurumu.Takipte))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Durum, newDurum)
                .SetProperty(x => x.CozulmeTarihi, cozulmeTarihi)
                .SetProperty(x => x.CozulmeKaynakId, cozulmeKaynakId), ct);

        if (rows == 1)
            return new CasLifecycleResult(true);

        var current = await _db.HesapKontrolKayitlari
            .Where(x => x.Id == id)
            .Select(x => new { x.Durum, x.CozulmeTarihi, x.CozulmeKaynakId })
            .FirstOrDefaultAsync(ct);

        if (current == null)
            return new CasLifecycleResult(false, $"[CAS-NOTFOUND] Lifecycle: Id={id} bulunamadı.");

        if (current.Durum == newDurum && current.CozulmeTarihi == cozulmeTarihi && current.CozulmeKaynakId == cozulmeKaynakId)
            return new CasLifecycleResult(true); // idempotent no-op

        _logger.LogWarning(
            "[CAS-CONFLICT] Lifecycle: Id={Id} Existing(Durum={ExistingDurum},Cozulme={ExistingCozulme},Kaynak={ExistingKaynak}) Requested(Durum={ReqDurum},Cozulme={ReqCozulme},Kaynak={ReqKaynak})",
            id, current.Durum, current.CozulmeTarihi, current.CozulmeKaynakId, newDurum, cozulmeTarihi, cozulmeKaynakId);
        return new CasLifecycleResult(false,
            $"Lifecycle zaten Durum={current.Durum} CozulmeTarihi={current.CozulmeTarihi} CozulmeKaynakId={current.CozulmeKaynakId} — çakışma.");
    }

    /// <summary>
    /// older/later çifti için TÜM finansal etki + ters dönüş + yaşam döngüsü (Durum/CozulmeTarihi/
    /// CozulmeKaynakId) yazımlarını TEK transaction içinde uygular. Herhangi bir CAS uyuşmazlığı
    /// veya exception → tam rollback (hiçbir kısmi finansal/yaşam-döngüsü yazımı kalıcı olmaz).
    ///
    /// older: origin = olderOriginDate (kendi AnalizTarihi), reversal = laterOriginDate (later'ın AnalizTarihi).
    /// later: origin = laterOriginDate (kendi AnalizTarihi), reversal = KENDİ origin'i (self-neutralize, aynı gün → net 0).
    /// Reversal her zaman &gt;= origin (older/later seçimi date-comparison ile üst katmanda yapılmış olmalı).
    ///
    /// Audit-only alanlar (Notlar/KullaniciOnay/OnayTarihi/ApprovedByUserId) bu transaction'ın DIŞINDA,
    /// başarı sonrası çağıran tarafından ayrı bir SaveChangesAsync ile yazılır — bunlar finansal/yaşam
    /// döngüsü kritik değildir, kaybı kısmi finansal durum yaratmaz.
    /// </summary>
    public async Task<PairResolutionResult> ExecutePairResolutionAsync(
        Guid olderId, DateOnly olderOriginDate, decimal olderImpact,
        Guid laterId, DateOnly laterOriginDate, decimal laterImpact,
        DateOnly actionDate,
        CancellationToken ct = default)
    {
        if (laterOriginDate < olderOriginDate)
            return Fail("later.AnalizTarihi < older.AnalizTarihi — older/later seçimi hatalı, reversal < origin olamaz.");

        // EnableRetryOnFailure ile yapılandırılmış SqlServerRetryingExecutionStrategy, kullanıcı-başlatımlı
        // transaction'ları yalnızca CreateExecutionStrategy().ExecuteAsync(...) delege'i İÇİNDE kabul eder
        // (aksi halde InvalidOperationException). Delege bütün transaction'ı (BeginTransactionAsync'ten
        // CommitAsync'e kadar) sarmalı — tek tek CAS çağrıları için ayrı retry scope'ları AÇILMAZ.
        var strategy = _db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var olderImpactCas = await CasSetKasaEtkisiAsync(olderId, olderImpact, olderOriginDate, ct);
                if (!olderImpactCas.Success) { await tx.RollbackAsync(ct); return Fail(olderImpactCas.Conflict); }

                var laterImpactCas = await CasSetKasaEtkisiAsync(laterId, laterImpact, laterOriginDate, ct);
                if (!laterImpactCas.Success) { await tx.RollbackAsync(ct); return Fail(laterImpactCas.Conflict); }

                var olderReversalCas = await CasSetTersDonusAsync(olderId, laterOriginDate, ct);
                if (!olderReversalCas.Success) { await tx.RollbackAsync(ct); return Fail(olderReversalCas.Conflict); }

                var laterReversalCas = await CasSetTersDonusAsync(laterId, laterOriginDate, ct);
                if (!laterReversalCas.Success) { await tx.RollbackAsync(ct); return Fail(laterReversalCas.Conflict); }

                var olderLifecycle = await CasSetLifecycleAsync(olderId, KayitDurumu.Cozuldu, actionDate, laterId, ct);
                if (!olderLifecycle.Success) { await tx.RollbackAsync(ct); return Fail(olderLifecycle.Conflict); }

                var laterLifecycle = await CasSetLifecycleAsync(laterId, KayitDurumu.Cozuldu, actionDate, olderId, ct);
                if (!laterLifecycle.Success) { await tx.RollbackAsync(ct); return Fail(laterLifecycle.Conflict); }

                await tx.CommitAsync(ct);
                return new PairResolutionResult(true);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        });
    }

    private static PairResolutionResult Fail(string? reason) => new(false, reason);
}
