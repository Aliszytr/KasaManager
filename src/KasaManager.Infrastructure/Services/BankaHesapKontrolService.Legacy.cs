#nullable enable
using KasaManager.Domain.Reports.HesapKontrol;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KasaManager.Infrastructure.Services;

/// <summary>
/// KASAMANAGER-2026-08-21-FINAL-PLAN-CLOSURE Revision 3, Section 3 (Helpy override).
/// Legacy terminal kayıtlar (Cozuldu/Onaylandi, KasaEtkisiTutari IS NULL) için materialization.
///
/// KRİTİK — Helpy override 3: Reciprocal-validation failure ASLA "Explicit NoCashImpact" (0) DEĞİLDİR.
/// Karşı taraf reciprocal doğrulanamıyorsa (orphan/eksik/tutarsız) sonuç InsufficientProvenance'dır:
/// 3 alan da NULL bırakılır, hiçbir yazma yapılmaz, hiçbir örtük inference yapılmaz.
/// NULL = legacy/materialize edilmemiş/bilinmeyen. Unknown asla NoCashImpact'e eşitlenmez.
/// </summary>
public sealed partial class BankaHesapKontrolService
{
    public enum LegacyMaterializationOutcome
    {
        AlreadyMaterialized,
        Materialized,
        InsufficientProvenance,
        NotFound,
        NotApplicable
    }

    public sealed record LegacyMaterializationResult(LegacyMaterializationOutcome Outcome, string? Detail = null);

    public async Task<LegacyMaterializationResult> MaterializeLegacyKasaEtkisiAsync(Guid kayitId, CancellationToken ct = default)
    {
        var kayit = await _db.HesapKontrolKayitlari.AsNoTracking().FirstOrDefaultAsync(x => x.Id == kayitId, ct);
        if (kayit == null)
            return new LegacyMaterializationResult(LegacyMaterializationOutcome.NotFound);

        if (kayit.Durum != KayitDurumu.Cozuldu && kayit.Durum != KayitDurumu.Onaylandi)
            return new LegacyMaterializationResult(LegacyMaterializationOutcome.NotApplicable,
                $"Durum={kayit.Durum} — yalnızca Cozuldu/Onaylandi terminal kayıtlar materialize edilir.");

        if (kayit.HesapTuru == BankaHesapTuru.Stopaj)
            return new LegacyMaterializationResult(LegacyMaterializationOutcome.NotApplicable,
                "Stopaj bu mekanizmanın kapsamı dışında.");

        // Idempotent guard: zaten materialize edilmişse no-op (ne fail-closed ne tekrar yazma).
        if (kayit.KasaEtkisiTutari.HasValue)
            return new LegacyMaterializationResult(LegacyMaterializationOutcome.AlreadyMaterialized);

        if (!kayit.CozulmeKaynakId.HasValue || !kayit.CozulmeTarihi.HasValue)
        {
            _logger.LogWarning("[LEGACY-INSUFFICIENT-PROVENANCE] Id={Id} Reason=NoCounterpartReference", kayitId);
            return new LegacyMaterializationResult(LegacyMaterializationOutcome.InsufficientProvenance,
                "CozulmeKaynakId veya CozulmeTarihi eksik — karşı taraf referansı yok.");
        }

        var counterpart = await _db.HesapKontrolKayitlari.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == kayit.CozulmeKaynakId.Value, ct);

        // Reciprocal doğrulama — tek gerçek provenance kaynağı: karşı taraf GERİYE bu kayda işaret
        // etmeli (mutual CozulmeKaynakId), CozulmeTarihi eşleşmeli, Yon zıt olmalı, aynı HesapTuru,
        // tutar mevcut ApprovePotentialMatchAsync toleransı (0.01) içinde olmalı.
        var reciprocalValid =
            counterpart != null
            && counterpart.CozulmeKaynakId == kayit.Id
            && counterpart.CozulmeTarihi == kayit.CozulmeTarihi
            && counterpart.Yon != kayit.Yon
            && counterpart.HesapTuru == kayit.HesapTuru
            && Math.Abs(counterpart.Tutar - kayit.Tutar) <= 0.01m;

        if (!reciprocalValid)
        {
            _logger.LogWarning(
                "[LEGACY-INSUFFICIENT-PROVENANCE] Id={Id} CounterpartId={CounterpartId} CounterpartFound={CounterpartFound} Reason=ReciprocalValidationFailed",
                kayitId, kayit.CozulmeKaynakId, counterpart != null);
            return new LegacyMaterializationResult(LegacyMaterializationOutcome.InsufficientProvenance,
                "Karşı taraf reciprocal doğrulanamadı (orphan/mismatch/tolerans dışı) — KasaEtkisi alanları NULL bırakıldı, 0 YAZILMADI.");
        }

        // Eligibility reciprocal doğrulama ile kuruldu (bkz. Section 1). HesapTuru+Yon+Tutar BURADAN
        // SONRA yalnızca imzalı tutarı hesaplar — eligibility'yi belirlemez (zaten belirlenmiş).
        var kayitImpact = kayit.Yon == KayitYonu.Fazla ? kayit.Tutar : -kayit.Tutar;
        var counterpartImpact = counterpart!.Yon == KayitYonu.Fazla ? counterpart.Tutar : -counterpart.Tutar;

        var (olderId, olderDate, olderImpact, laterId, laterDate, laterImpact) =
            kayit.AnalizTarihi <= counterpart.AnalizTarihi
                ? (kayit.Id, kayit.AnalizTarihi, kayitImpact, counterpart.Id, counterpart.AnalizTarihi, counterpartImpact)
                : (counterpart.Id, counterpart.AnalizTarihi, counterpartImpact, kayit.Id, kayit.AnalizTarihi, kayitImpact);

        // Legacy path: Durum/CozulmeTarihi/CozulmeKaynakId zaten terminal — lifecycle'a dokunmuyoruz
        // (CasSetLifecycleAsync yalnızca Acik/Takipte'den geçişi kabul eder, bu yüzden burada
        // kullanılmıyor). Yalnızca KasaEtkisi* alanları tek transaction içinde CAS ile yazılır.
        //
        // EnableRetryOnFailure ile yapılandırılmış SqlServerRetryingExecutionStrategy, kullanıcı-başlatımlı
        // transaction'ları yalnızca CreateExecutionStrategy().ExecuteAsync(...) delege'i İÇİNDE kabul eder
        // (bkz. ExecutePairResolutionAsync'teki aynı düzeltme, BankaHesapKontrolService.Cas.cs). Delege
        // bütün transaction'ı (BeginTransactionAsync'ten CommitAsync'e kadar) sarmalı.
        var strategy = _db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var olderCas = await CasSetKasaEtkisiAsync(olderId, olderImpact, olderDate, ct);
                if (!olderCas.Success) { await tx.RollbackAsync(ct); return Insufficient(olderCas.Conflict); }

                var laterCas = await CasSetKasaEtkisiAsync(laterId, laterImpact, laterDate, ct);
                if (!laterCas.Success) { await tx.RollbackAsync(ct); return Insufficient(laterCas.Conflict); }

                var olderReversal = await CasSetTersDonusAsync(olderId, laterDate, ct);
                if (!olderReversal.Success) { await tx.RollbackAsync(ct); return Insufficient(olderReversal.Conflict); }

                var laterReversal = await CasSetTersDonusAsync(laterId, laterDate, ct);
                if (!laterReversal.Success) { await tx.RollbackAsync(ct); return Insufficient(laterReversal.Conflict); }

                await tx.CommitAsync(ct);
                _logger.LogInformation(
                    "[LEGACY-MATERIALIZED] Id={Id} CounterpartId={CounterpartId} OlderId={OlderId} LaterId={LaterId}",
                    kayitId, counterpart.Id, olderId, laterId);
                return new LegacyMaterializationResult(LegacyMaterializationOutcome.Materialized);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        });

        static LegacyMaterializationResult Insufficient(string? reason) =>
            new(LegacyMaterializationOutcome.InsufficientProvenance, reason);
    }
}
