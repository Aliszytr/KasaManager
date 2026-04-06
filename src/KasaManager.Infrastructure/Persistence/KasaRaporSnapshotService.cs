using KasaManager.Application.Abstractions;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.Snapshots;
using Microsoft.EntityFrameworkCore;

namespace KasaManager.Infrastructure.Persistence;

public sealed class KasaRaporSnapshotService : IKasaRaporSnapshotService
{
    private readonly KasaManagerDbContext _db;

    public KasaRaporSnapshotService(KasaManagerDbContext db)
    {
        _db = db;
    }

    public async Task<KasaRaporSnapshot> SaveAsync(KasaRaporSnapshot snapshot, CancellationToken ct = default)
    {
        // R7 Kuralı:
        // - DB'ye yazım yalnız "Kaydet" ile olur.
        // - Snapshot "gerçek"tir; sonradan yeniden hesap yapılmaz.
        //
        // Stabil yaklaşım:
        // - Aynı (RaporTarihi + RaporTuru) için kayıt varsa eski graph'ı silip yeni graph'ı ekleriz.

        // SQL Server retry strategy açıkken (EnableRetryOnFailure),
        // user-initiated transaction kullanacaksak EF'nin ExecutionStrategy'si içinde çalıştırmalıyız.
        var strategy = _db.Database.CreateExecutionStrategy();

        KasaRaporSnapshot? saved = null;

        await strategy.ExecuteAsync(async () =>
        {
            // Yeni graph ekleyeceğiz; Id/SnapshotId'leri garanti altına al.
            NormalizeNewSnapshotGraph(snapshot);

            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            var existing = await _db.KasaRaporSnapshots
                .Include(x => x.Rows)
                .Include(x => x.Inputs)
                .Include(x => x.Results)
                .FirstOrDefaultAsync(
                    x => x.RaporTarihi == snapshot.RaporTarihi && x.RaporTuru == snapshot.RaporTuru,
                    ct
                );

            if (existing != null)
            {
                // R6-AUDIT: Immutable model kuralı gereğince artık eski kaydı silmiyoruz.
                // Üzerine yazma isteği yeni bir snapshot versiyonudur. Eskisini Superseded yapıyoruz.
                existing.IsSuperseded = true;
                _db.KasaRaporSnapshots.Update(existing);

                try
                {
                    await _db.SaveChangesAsync(ct);
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Kayıt başka bir işlemde silinmiş olabilir; devam.
                    _db.ChangeTracker.Clear();
                }
            }

            _db.KasaRaporSnapshots.Add(snapshot);
            await _db.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);

            saved = await _db.KasaRaporSnapshots
                .Include(x => x.Rows)
                .Include(x => x.Inputs)
                .Include(x => x.Results)
                .SingleAsync(
                    x => x.RaporTarihi == snapshot.RaporTarihi && x.RaporTuru == snapshot.RaporTuru,
                    ct
                );
        });

        return saved!;
    }

    private static void NormalizeNewSnapshotGraph(KasaRaporSnapshot snapshot)
    {
        snapshot.Id = snapshot.Id == Guid.Empty ? Guid.NewGuid() : snapshot.Id;

        if (snapshot.Rows != null)
        {
            foreach (var r in snapshot.Rows)
            {
                r.Id = Guid.NewGuid();
                r.SnapshotId = snapshot.Id;
                r.Snapshot = null;
            }
        }

        if (snapshot.Inputs != null)
        {
            snapshot.Inputs.Id = Guid.NewGuid();
            snapshot.Inputs.SnapshotId = snapshot.Id;
            snapshot.Inputs.Snapshot = null;
        }

        if (snapshot.Results != null)
        {
            snapshot.Results.Id = Guid.NewGuid();
            snapshot.Results.SnapshotId = snapshot.Id;
            snapshot.Results.Snapshot = null;
        }
    }

    public Task<KasaRaporSnapshot?> GetAsync(DateOnly raporTarihi, KasaRaporTuru raporTuru, CancellationToken ct = default)
    {
        return _db.KasaRaporSnapshots
            .Include(x => x.Rows)
            .Include(x => x.Inputs)
            .Include(x => x.Results)
            // R6-AUDIT: Sadece aktif (superseded OLMAYAN) kaydı getir.
            .Where(x => !x.IsSuperseded)
            .FirstOrDefaultAsync(x => x.RaporTarihi == raporTarihi && x.RaporTuru == raporTuru, ct);
    }

    /// <summary>
    /// R19: Snapshot yüklerken eksik alanları varsayılan değerlerle doldurur.
    /// Bu sayede eski snapshot'lar yeni 83 alanlı yapıyla uyumlu çalışır.
    /// </summary>
    public async Task<KasaRaporSnapshot?> GetWithMissingFieldsAsync(
        DateOnly raporTarihi, 
        KasaRaporTuru raporTuru, 
        CancellationToken ct = default)
    {
        var snapshot = await GetAsync(raporTarihi, raporTuru, ct);
        if (snapshot == null) return null;
        
        // Inputs JSON'u normalize et (R19 backward compatibility)
        if (snapshot.Inputs != null && !string.IsNullOrWhiteSpace(snapshot.Inputs.ValuesJson))
        {
            try
            {
                var inputsDict = ParseValuesJsonAsDecimal(snapshot.Inputs.ValuesJson);
                var normalizedInputs = KasaManager.Domain.FormulaEngine.MissingFieldHandler.EnsureAllDecimalFields(inputsDict);
                snapshot.Inputs.ValuesJson = System.Text.Json.JsonSerializer.Serialize(normalizedInputs);
            }
            catch (Exception ex)
            {
                // P1-EXC-01: JSON parse hatası — orijinal değeri koru, ama sessiz kalma
                System.Diagnostics.Debug.WriteLine($"[KasaRaporSnapshotService] Inputs JSON normalize hatası (SnapshotId={snapshot.Id}): {ex.Message}");
            }
        }
        
        // Results JSON'u normalize et (R19 backward compatibility)
        if (snapshot.Results != null && !string.IsNullOrWhiteSpace(snapshot.Results.ValuesJson))
        {
            try
            {
                var resultsDict = ParseValuesJsonAsDecimal(snapshot.Results.ValuesJson);
                var normalizedResults = KasaManager.Domain.FormulaEngine.MissingFieldHandler.EnsureAllDecimalFields(resultsDict);
                snapshot.Results.ValuesJson = System.Text.Json.JsonSerializer.Serialize(normalizedResults);
            }
            catch (Exception ex)
            {
                // P1-EXC-01: JSON parse hatası — orijinal değeri koru, ama sessiz kalma
                System.Diagnostics.Debug.WriteLine($"[KasaRaporSnapshotService] Results JSON normalize hatası (SnapshotId={snapshot.Id}): {ex.Message}");
            }
        }
        
        return snapshot;
    }
    
    /// <summary>
    /// ValuesJson string'ini decimal dictionary'ye parse eder.
    /// String veya decimal değerler içerebilir.
    /// </summary>
    private static Dictionary<string, decimal> ParseValuesJsonAsDecimal(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            
        try
        {
            // Önce decimal olarak dene
            var decimalDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, decimal>>(json);
            if (decimalDict != null)
            {
                return new Dictionary<string, decimal>(decimalDict, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            // P1-EXC-01: Decimal parse başarısız — string fallback'e düşecek
            System.Diagnostics.Debug.WriteLine($"[KasaRaporSnapshotService] ValuesJson decimal parse fallback: {ex.Message}");
        }
        
        try
        {
            // String dictionary olarak parse et ve decimal'e çevir
            var stringDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string?>>(json);
            if (stringDict != null)
            {
                var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in stringDict)
                {
                    if (decimal.TryParse(kvp.Value?.Replace(",", "."), 
                        System.Globalization.NumberStyles.Any, 
                        System.Globalization.CultureInfo.InvariantCulture, 
                        out var val))
                    {
                        result[kvp.Key] = val;
                    }
                }
                return result;
            }
        }
        catch (Exception ex)
        {
            // P1-EXC-01: String→decimal parse de başarısız — boş dictionary dönecek
            System.Diagnostics.Debug.WriteLine($"[KasaRaporSnapshotService] ValuesJson string parse de başarısız: {ex.Message}");
        }
        
        return new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
    }


    public Task<KasaRaporSnapshot?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return _db.KasaRaporSnapshots
            .Include(x => x.Rows)
            .Include(x => x.Inputs)
            .Include(x => x.Results)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public Task<KasaRaporSnapshot?> GetLastBeforeOrOnAsync(DateOnly raporTarihi, KasaRaporTuru raporTuru, CancellationToken ct = default)
    {
        return _db.KasaRaporSnapshots
            .Include(x => x.Rows)
            .Include(x => x.Inputs)
            .Include(x => x.Results)
            .Where(x => x.RaporTuru == raporTuru && x.RaporTarihi <= raporTarihi)
            .OrderByDescending(x => x.RaporTarihi)
            .FirstOrDefaultAsync(ct);
    }

    public Task<KasaRaporSnapshot?> GetLastGenelKasaSnapshotBeforeOrOnAsync(DateOnly raporTarihi, CancellationToken ct = default)
    {
        // R12.5: "Genel" türündeki snapshot'lar iki farklı amaçla kullanılabiliyor:
        //  - KasaÜstRapor seçim snapshot'ı (Rows var, Results genelde yok)
        //  - Hesaplanmış Genel Kasa sonucu (Results dolu, Results.ValuesJson içinde "Genel Kasa" gibi alanlar)
        //
        // Bu metod, sadece "hesaplanmış" snapshot'ları hedefleyerek karışıklığı önler.
        return _db.KasaRaporSnapshots
            .Include(x => x.Rows)
            .Include(x => x.Inputs)
            .Include(x => x.Results)
            .Where(x => x.RaporTuru == KasaRaporTuru.Genel && x.RaporTarihi <= raporTarihi)
            .Where(x => x.Results != null)
            .Where(x => EF.Functions.Like(x.Results!.ValuesJson, "%Genel Kasa%")
                     || EF.Functions.Like(x.Results!.ValuesJson, "%GenelKasa%"))
            .OrderByDescending(x => x.RaporTarihi)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<DateOnly?> GetLastSnapshotDateAsync(KasaRaporTuru raporTuru, CancellationToken ct = default)
    {
        return await _db.KasaRaporSnapshots
            .Where(x => x.RaporTuru == raporTuru)
            .OrderByDescending(x => x.RaporTarihi)
            .Select(x => (DateOnly?)x.RaporTarihi)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<List<DateOnly>> GetAllSnapshotDatesAsync(KasaRaporTuru raporTuru, CancellationToken ct = default)
    {
        return await _db.KasaRaporSnapshots
            .Where(x => x.RaporTuru == raporTuru)
            .Select(x => x.RaporTarihi)
            .Distinct()
            .OrderByDescending(x => x)
            .ToListAsync(ct);
    }
}
