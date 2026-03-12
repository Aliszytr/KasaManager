#nullable enable
using System.ComponentModel.DataAnnotations;
using KasaManager.Application.Abstractions;
using KasaManager.Domain.FinancialExceptions;
using KasaManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KasaManager.Infrastructure.Services;

/// <summary>
/// Finansal İstisna CRUD ve aggregate servisi.
/// ARCHITECTURE LOCK: FormulaEngine'i değiştirmez, runtime Pool injection sağlar.
/// Faz 3.1: Tüm mutasyonlarda FinansalIstisnaHistory event yazılır.
/// </summary>
public sealed class FinansalIstisnaService : IFinansalIstisnaService
{
    private readonly KasaManagerDbContext _db;

    public FinansalIstisnaService(KasaManagerDbContext db)
    {
        _db = db;
    }

    // ═══════════════════════════════════════════
    // CRUD + Karar + Durum (Faz 1 + Faz 3.1 History)
    // ═══════════════════════════════════════════

    public async Task<FinansalIstisna> CreateAsync(FinansalIstisnaCreateRequest request, CancellationToken ct = default)
    {
        // Validation guard — geçersiz veri DB'ye ulaşmadan reddedilir
        var errors = FinansalIstisnaValidator.ValidateCreate(
            request.Tur, request.Kategori, request.EtkiYonu,
            request.BeklenenTutar, request.GerceklesenTutar, request.SistemeGirilenTutar);
        if (errors.Count > 0)
            throw new ValidationException(string.Join(" ", errors));

        var entity = new FinansalIstisna
        {
            IslemTarihi = request.IslemTarihi,
            Tur = request.Tur,
            Kategori = request.Kategori,
            HesapTuru = request.HesapTuru,
            BeklenenTutar = request.BeklenenTutar,
            GerceklesenTutar = request.GerceklesenTutar,
            SistemeGirilenTutar = request.SistemeGirilenTutar,
            EtkiYonu = request.EtkiYonu,
            Neden = request.Neden,
            Aciklama = request.Aciklama,
            HedefHesapAciklama = request.HedefHesapAciklama,
            OlusturanKullanici = request.OlusturanKullanici,
            ParentIstisnaId = request.ParentIstisnaId,
            KararDurumu = KararDurumu.IncelemeBekliyor,
            Durum = IstisnaDurumu.Acik
        };

        _db.FinansalIstisnalar.Add(entity);

        // Faz 3.1: Created history event
        WriteHistory(entity.Id, IstisnaHistoryEventType.Created,
            oldKarar: null, newKarar: KararDurumu.IncelemeBekliyor,
            oldDurum: null, newDurum: IstisnaDurumu.Acik,
            newBeklenen: request.BeklenenTutar,
            newGerceklesen: request.GerceklesenTutar,
            newSisteme: request.SistemeGirilenTutar,
            kullanici: request.OlusturanKullanici,
            aciklama: $"İstisna oluşturuldu: {request.Tur}");

        await _db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<FinansalIstisna?> UpdateAsync(Guid id, FinansalIstisnaUpdateRequest request, CancellationToken ct = default)
    {
        // Update tutar validation guard
        var errors = FinansalIstisnaValidator.ValidateUpdateAmounts(
            request.BeklenenTutar, request.GerceklesenTutar, request.SistemeGirilenTutar);
        if (errors.Count > 0)
            throw new ValidationException(string.Join(" ", errors));

        var entity = await _db.FinansalIstisnalar.FindAsync(new object[] { id }, ct);
        if (entity is null) return null;

        var oldBeklenen = entity.BeklenenTutar;
        var oldGerceklesen = entity.GerceklesenTutar;
        var oldSisteme = entity.SistemeGirilenTutar;

        if (request.BeklenenTutar.HasValue) entity.BeklenenTutar = request.BeklenenTutar.Value;
        if (request.GerceklesenTutar.HasValue) entity.GerceklesenTutar = request.GerceklesenTutar.Value;
        if (request.SistemeGirilenTutar.HasValue) entity.SistemeGirilenTutar = request.SistemeGirilenTutar.Value;
        if (request.Neden is not null) entity.Neden = request.Neden;
        if (request.Aciklama is not null) entity.Aciklama = request.Aciklama;

        entity.GuncelleyenKullanici = request.GuncelleyenKullanici;
        entity.GuncellemeTarihiUtc = DateTime.UtcNow;

        // Faz 3.1: TutarGuncellendi history event
        WriteHistory(entity.Id, IstisnaHistoryEventType.TutarGuncellendi,
            oldKarar: entity.KararDurumu, newKarar: entity.KararDurumu,
            oldDurum: entity.Durum, newDurum: entity.Durum,
            oldBeklenen: oldBeklenen, newBeklenen: entity.BeklenenTutar,
            oldGerceklesen: oldGerceklesen, newGerceklesen: entity.GerceklesenTutar,
            oldSisteme: oldSisteme, newSisteme: entity.SistemeGirilenTutar,
            kullanici: request.GuncelleyenKullanici,
            aciklama: "Tutar güncellendi");

        await _db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<FinansalIstisna?> SetKararAsync(Guid id, KararDurumu karar, string kullanici, CancellationToken ct = default)
    {
        var entity = await _db.FinansalIstisnalar.FindAsync(new object[] { id }, ct);
        if (entity is null) return null;

        var oldKarar = entity.KararDurumu;

        entity.KararDurumu = karar;
        entity.KararVerenKullanici = kullanici;
        entity.KararTarihiUtc = DateTime.UtcNow;
        entity.GuncelleyenKullanici = kullanici;
        entity.GuncellemeTarihiUtc = DateTime.UtcNow;

        // Faz 3.1: Karar history event
        var eventType = karar == KararDurumu.Onaylandi
            ? IstisnaHistoryEventType.KararOnaylandi
            : IstisnaHistoryEventType.KararReddedildi;

        WriteHistory(entity.Id, eventType,
            oldKarar: oldKarar, newKarar: karar,
            oldDurum: entity.Durum, newDurum: entity.Durum,
            kullanici: kullanici,
            aciklama: $"Karar: {oldKarar} → {karar}");

        await _db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<FinansalIstisna?> SetDurumAsync(Guid id, IstisnaDurumu durum, string kullanici, decimal? sistemeGirilenTutar = null, CancellationToken ct = default)
    {
        var entity = await _db.FinansalIstisnalar.FindAsync(new object[] { id }, ct);
        if (entity is null) return null;

        var oldDurum = entity.Durum;
        var oldSisteme = entity.SistemeGirilenTutar;

        entity.Durum = durum;
        entity.GuncelleyenKullanici = kullanici;
        entity.GuncellemeTarihiUtc = DateTime.UtcNow;

        if (sistemeGirilenTutar.HasValue)
            entity.SistemeGirilenTutar = sistemeGirilenTutar.Value;

        if (durum == IstisnaDurumu.Cozuldu || durum == IstisnaDurumu.Iptal)
            entity.CozulmeTarihi = DateOnly.FromDateTime(DateTime.Now);

        // Faz 3.1: Durum history event
        var eventType = durum switch
        {
            IstisnaDurumu.KismiCozuldu => IstisnaHistoryEventType.KismiCozuldu,
            IstisnaDurumu.Cozuldu => IstisnaHistoryEventType.Cozuldu,
            IstisnaDurumu.ErtesiGuneDevredildi => IstisnaHistoryEventType.ErtesiGuneDevredildi,
            IstisnaDurumu.Iptal => IstisnaHistoryEventType.IptalEdildi,
            _ => IstisnaHistoryEventType.DurumDegisti
        };

        WriteHistory(entity.Id, eventType,
            oldKarar: entity.KararDurumu, newKarar: entity.KararDurumu,
            oldDurum: oldDurum, newDurum: durum,
            oldSisteme: oldSisteme, newSisteme: entity.SistemeGirilenTutar,
            kullanici: kullanici,
            aciklama: $"Durum: {oldDurum} → {durum}");

        await _db.SaveChangesAsync(ct);
        return entity;
    }

    // ═══════════════════════════════════════════
    // Queries (Faz 1)
    // ═══════════════════════════════════════════

    public async Task<IReadOnlyList<FinansalIstisna>> ListByDateAsync(DateOnly islemTarihi, CancellationToken ct = default)
    {
        return await _db.FinansalIstisnalar
            .Where(x => x.IslemTarihi == islemTarihi)
            .OrderByDescending(x => x.OlusturmaTarihiUtc)
            .ToListAsync(ct);
    }

    public async Task<FinansalIstisna?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.FinansalIstisnalar.FindAsync(new object[] { id }, ct);
    }

    // ═══════════════════════════════════════════
    // Pool Aggregate (Faz 1 + Faz 2 IsRuntimeEffective)
    // ═══════════════════════════════════════════

    public async Task<IReadOnlyList<FinansalIstisnaPoolEntry>> GetPoolAggregateAsync(DateOnly islemTarihi, CancellationToken ct = default)
    {
        var aktifKayitlar = await _db.FinansalIstisnalar
            .Where(x => x.IslemTarihi == islemTarihi)
            .ToListAsync(ct);

        var hesaplayaKatilanlar = aktifKayitlar
            .Where(BekleyenTutarCalculator.IsRuntimeEffective)
            .ToList();

        return BuildPoolEntries(hesaplayaKatilanlar);
    }

    // ═══════════════════════════════════════════
    // Faz 2: Devretme
    // ═══════════════════════════════════════════

    public async Task<IReadOnlyList<FinansalIstisna>> ListDevredilmisAsync(DateOnly bugun, CancellationToken ct = default)
    {
        var dun = bugun.AddDays(-1);
        return await _db.FinansalIstisnalar
            .Where(x => x.Durum == IstisnaDurumu.ErtesiGuneDevredildi
                        && x.IslemTarihi <= dun)
            .OrderByDescending(x => x.IslemTarihi)
            .ToListAsync(ct);
    }

    public async Task<FinansalIstisna> CreateFromDevredilmisAsync(Guid parentId, DateOnly bugun, string kullanici, CancellationToken ct = default)
    {
        var parent = await _db.FinansalIstisnalar.FindAsync(new object[] { parentId }, ct)
            ?? throw new InvalidOperationException($"Parent istisna bulunamadı: {parentId}");

        var child = new FinansalIstisna
        {
            IslemTarihi = bugun,
            Tur = parent.Tur,
            Kategori = parent.Kategori,
            HesapTuru = parent.HesapTuru,
            BeklenenTutar = BekleyenTutarCalculator.Hesapla(parent),
            GerceklesenTutar = 0m,
            SistemeGirilenTutar = 0m,
            EtkiYonu = parent.EtkiYonu,
            HedefHesapAciklama = parent.HedefHesapAciklama,
            Neden = $"Devredilen istisna ({parent.IslemTarihi:dd.MM.yyyy})",
            Aciklama = parent.Neden,
            OlusturanKullanici = kullanici,
            ParentIstisnaId = parentId,
            KararDurumu = KararDurumu.IncelemeBekliyor,
            Durum = IstisnaDurumu.Acik
        };

        _db.FinansalIstisnalar.Add(child);

        // Faz 3.1: CreateFromDevredilmis history event
        WriteHistory(child.Id, IstisnaHistoryEventType.CreateFromDevredilmis,
            newKarar: KararDurumu.IncelemeBekliyor,
            newDurum: IstisnaDurumu.Acik,
            newBeklenen: child.BeklenenTutar,
            kullanici: kullanici,
            aciklama: $"Devredilmişten oluşturuldu (parent: {parentId})");

        await _db.SaveChangesAsync(ct);
        return child;
    }

    // ═══════════════════════════════════════════
    // Faz 3.1: Historical Queries
    // ═══════════════════════════════════════════

    public async Task<IReadOnlyList<FinansalIstisnaHistory>> GetHistoryAsync(Guid istisnaId, CancellationToken ct = default)
    {
        return await _db.FinansalIstisnaHistory
            .Where(h => h.FinansalIstisnaId == istisnaId)
            .OrderBy(h => h.EventTarihiUtc)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<HistoricalState> GetEffectiveStateAtAsync(Guid istisnaId, DateOnly targetDate, CancellationToken ct = default)
    {
        var entity = await _db.FinansalIstisnalar.FindAsync(new object[] { istisnaId }, ct);
        if (entity is null) return HistoricalState.NotExisted;

        var history = await GetHistoryAsync(istisnaId, ct);
        return HistoricalEffectiveStateResolver.ResolveStateAt(entity, targetDate, history);
    }

    public async Task<IReadOnlyList<FinansalIstisnaPoolEntry>> GetPoolAggregateAtAsync(
        DateOnly islemTarihi, DateOnly targetDate, CancellationToken ct = default)
    {
        var kayitlar = await _db.FinansalIstisnalar
            .Where(x => x.IslemTarihi == islemTarihi)
            .ToListAsync(ct);

        if (kayitlar.Count == 0)
            return Array.Empty<FinansalIstisnaPoolEntry>();

        // OB-2 FIX: N+1 query → tek toplu sorgu ile tüm history kayıtlarını getir
        var kayitIds = kayitlar.Select(x => x.Id).ToList();
        var allHistory = await _db.FinansalIstisnaHistory
            .Where(h => kayitIds.Contains(h.FinansalIstisnaId))
            .OrderBy(h => h.EventTarihiUtc)
            .AsNoTracking()
            .ToListAsync(ct);

        var historyByIstisna = allHistory
            .GroupBy(h => h.FinansalIstisnaId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<FinansalIstisnaHistory>)g.ToList());

        // Her kayıt için hedef tarihteki state'i resolve et
        var effectiveList = new List<(FinansalIstisna Ex, HistoricalState State)>();

        foreach (var ex in kayitlar)
        {
            var history = historyByIstisna.TryGetValue(ex.Id, out var h)
                ? h
                : (IReadOnlyList<FinansalIstisnaHistory>)Array.Empty<FinansalIstisnaHistory>();

            var state = HistoricalEffectiveStateResolver.ResolveStateAt(ex, targetDate, history);

            if (HistoricalEffectiveStateResolver.IsEffective(state))
                effectiveList.Add((ex, state));
        }

        if (effectiveList.Count == 0)
            return Array.Empty<FinansalIstisnaPoolEntry>();

        // Pool entries oluştur (historical state + tür-spesifik hesap kullanarak)
        var result = new List<FinansalIstisnaPoolEntry>();
        decimal toplamEtki = 0m;

        var grouped = effectiveList.GroupBy(x => BekleyenTutarCalculator.CanonicalKey(x.Ex));

        foreach (var group in grouped)
        {
            decimal aggregate = 0m;
            var notes = new List<string>();

            foreach (var (ex, state) in group)
            {
                // KB-2 FIX: Tür-spesifik formül ile hesap — runtime ile aynı parity
                var bekleyen = state.EffectiveBekleyenTutar;
                var signed = ex.EtkiYonu == KasaEtkiYonu.Azaltan ? -bekleyen : bekleyen;
                aggregate += signed;

                notes.Add($"{ex.Tur}: {bekleyen:N2} ({state.Explanation})");
            }

            result.Add(new FinansalIstisnaPoolEntry(
                CanonicalKey: group.Key,
                Value: aggregate,
                Notes: string.Join("; ", notes)));

            toplamEtki += aggregate;
        }

        result.Add(new FinansalIstisnaPoolEntry(
            CanonicalKey: BekleyenTutarCalculator.AggregateTotalKey,
            Value: toplamEtki,
            Notes: $"{effectiveList.Count} tarihsel onaylı istisna ({targetDate:dd.MM.yyyy})"));

        return result;
    }

    // ═══════════════════════════════════════════
    // Private Helpers
    // ═══════════════════════════════════════════

    private void WriteHistory(
        Guid istisnaId,
        IstisnaHistoryEventType eventType,
        KararDurumu? oldKarar = null, KararDurumu? newKarar = null,
        IstisnaDurumu? oldDurum = null, IstisnaDurumu? newDurum = null,
        decimal? oldBeklenen = null, decimal? newBeklenen = null,
        decimal? oldGerceklesen = null, decimal? newGerceklesen = null,
        decimal? oldSisteme = null, decimal? newSisteme = null,
        string? kullanici = null,
        string? aciklama = null)
    {
        _db.FinansalIstisnaHistory.Add(new FinansalIstisnaHistory
        {
            FinansalIstisnaId = istisnaId,
            EventType = eventType,
            OldKararDurumu = oldKarar,
            NewKararDurumu = newKarar,
            OldDurum = oldDurum,
            NewDurum = newDurum,
            OldBeklenenTutar = oldBeklenen,
            NewBeklenenTutar = newBeklenen,
            OldGerceklesenTutar = oldGerceklesen,
            NewGerceklesenTutar = newGerceklesen,
            OldSistemeGirilenTutar = oldSisteme,
            NewSistemeGirilenTutar = newSisteme,
            EventKullanici = kullanici,
            Aciklama = aciklama
        });
    }

    private static IReadOnlyList<FinansalIstisnaPoolEntry> BuildPoolEntries(List<FinansalIstisna> kayitlar)
    {
        if (kayitlar.Count == 0)
            return Array.Empty<FinansalIstisnaPoolEntry>();

        var result = new List<FinansalIstisnaPoolEntry>();
        decimal toplamEtki = 0m;

        var grouped = kayitlar.GroupBy(x => BekleyenTutarCalculator.CanonicalKey(x));

        foreach (var group in grouped)
        {
            decimal aggregate = 0m;
            var notes = new List<string>();

            foreach (var ex in group)
            {
                var bekleyen = BekleyenTutarCalculator.Hesapla(ex);
                var signed = ex.EtkiYonu == KasaEtkiYonu.Azaltan ? -bekleyen : bekleyen;
                aggregate += signed;
                notes.Add($"{ex.Tur}: {bekleyen:N2} ({ex.Neden ?? "—"})");
            }

            result.Add(new FinansalIstisnaPoolEntry(
                CanonicalKey: group.Key,
                Value: aggregate,
                Notes: string.Join("; ", notes)));

            toplamEtki += aggregate;
        }

        result.Add(new FinansalIstisnaPoolEntry(
            CanonicalKey: BekleyenTutarCalculator.AggregateTotalKey,
            Value: toplamEtki,
            Notes: $"{kayitlar.Count} onaylı istisna"));

        return result;
    }
}
