#nullable enable
using System.Text.Json;
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Reports;
using KasaManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KasaManager.Infrastructure.Services;

/// <summary>
/// Vergide Biriken akıllı ledger hesaplayıcı.
/// 
/// İş Mantığı:
///   VergiKasa       = O günkü vergi veznedarlarının toplam bakiyesi (günlük kazanç)
///   VergidenGelen   = O gün vergiden ana kasaya getirilen tutar
///   VergideBiriken  = Önceki bakiye − getirilen + yeni kazanılan
///
/// Kümülatif formül:
///   VergideBiriken = InitialSeed + Σ(VergiKasa) − Σ(VergidenGelen)
///   
/// InitialSeed = İlk snapshot'tan ÖNCE vergide birikmiş bakiye.
///   (Genellikle ilk günün VergidenGelen değerine eşittir, 
///    çünkü ilk gün eski birikeni getirirsiniz.)
///
/// Aynı tarih için hem Sabah hem Aksam snapshot'u varsa yalnızca Aksam kullanılır
/// (çift sayımı önlemek için).
/// </summary>
public sealed class VergideBirikenLedgerService : IVergideBirikenLedgerService
{
    private readonly KasaManagerDbContext _db;
    private readonly IKasaGlobalDefaultsService _globalDefaults;
    private readonly ILogger<VergideBirikenLedgerService> _log;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public VergideBirikenLedgerService(
        KasaManagerDbContext db,
        IKasaGlobalDefaultsService globalDefaults,
        ILogger<VergideBirikenLedgerService> log)
    {
        _db = db;
        _globalDefaults = globalDefaults;
        _log = log;
    }

    public async Task<VergideBirikenResult> CalculateAsync(DateOnly upToDate, string kasaType, CancellationToken ct = default)
    {
        // 1. İlk seed (ilk snapshot'tan ÖNCE vergide birikmiş bakiye)
        var defaults = await _globalDefaults.GetAsync(ct);
        var initialSeed = defaults.VergideBirikenSeed ?? 0m;

        // 2. Sabah Kasa: bugünü dahil etme (D-1'e kadar hesapla)
        //    Aksam Kasa: bugünü dahil et (mevcut davranış)
        //    Bu sayede Sabah kasası "dünkü Aksam sonrası birikmiş bakiye"yi gösterir.
        var isSabah = string.Equals(kasaType, "Sabah", StringComparison.OrdinalIgnoreCase);
        var effectiveDate = isSabah ? upToDate.AddDays(-1) : upToDate;

        _log.LogInformation(
            "VergideBiriken ledger: kasaType={KasaType}, upToDate={UpTo}, effectiveDate={Eff}",
            kasaType, upToDate, effectiveDate);

        // 3. Tüm aktif Sabah+Aksam snapshot'ları (tarih ≤ effectiveDate)
        var snapshots = await _db.CalculatedKasaSnapshots
            .AsNoTracking()
            .Where(s => s.IsActive
                     && !s.IsDeleted
                     && s.RaporTarihi <= effectiveDate
                     && (s.KasaTuru == KasaRaporTuru.Sabah || s.KasaTuru == KasaRaporTuru.Aksam))
            .OrderBy(s => s.RaporTarihi)
            .ThenBy(s => s.KasaTuru)
            .Select(s => new { s.RaporTarihi, s.KasaTuru, s.KasaRaporDataJson })
            .ToListAsync(ct);

        // 3. Her tarih için yalnızca EN SON snapshot'ı al (Aksam > Sabah)
        //    Aynı gün için hem Sabah hem Aksam varsa yalnızca Aksam kullanılır.
        //    Böylece VergiKasa ve VergidenGelen çift sayılmaz.
        var perDaySnapshots = snapshots
            .GroupBy(s => s.RaporTarihi)
            .Select(g => g.OrderByDescending(s => s.KasaTuru).First())
            .OrderBy(s => s.RaporTarihi)
            .ToList();

        decimal totalVergiKasa = 0m;
        decimal totalVergidenGelen = 0m;
        int count = 0;
        DateOnly? lastDate = null;

        foreach (var snap in perDaySnapshots)
        {
            if (string.IsNullOrWhiteSpace(snap.KasaRaporDataJson))
                continue;

            try
            {
                var raporData = JsonSerializer.Deserialize<KasaRaporData>(snap.KasaRaporDataJson, _jsonOptions);
                if (raporData == null) continue;

                totalVergiKasa += raporData.VergiKasa;
                totalVergidenGelen += raporData.VergidenGelen;
                count++;
                lastDate = snap.RaporTarihi;

                _log.LogDebug(
                    "VergideBiriken ledger: {Tarih} {Tip} → VergiKasa={VK:N2}, VergidenGelen={VG:N2}",
                    snap.RaporTarihi, snap.KasaTuru, raporData.VergiKasa, raporData.VergidenGelen);
            }
            catch (JsonException ex)
            {
                _log.LogWarning(ex, "VergideBiriken: Snapshot JSON parse hatası (tarih={Tarih})", snap.RaporTarihi);
            }
        }

        var vergideBiriken = initialSeed + totalVergiKasa - totalVergidenGelen;

        _log.LogInformation(
            "VergideBiriken hesaplandı: Seed={Seed:N2} + ΣVergiKasa={TVK:N2} − ΣVergidenGelen={TVG:N2} = {VB:N2} ({Count} gün, {Total} snapshot, son={Son})",
            initialSeed, totalVergiKasa, totalVergidenGelen, vergideBiriken, count, snapshots.Count, lastDate);

        return new VergideBirikenResult(
            InitialSeed: initialSeed,
            TotalVergiKasa: totalVergiKasa,
            TotalVergidenGelen: totalVergidenGelen,
            VergideBiriken: vergideBiriken,
            SnapshotCount: count,
            LastSnapshotDate: lastDate
        );
    }
}
