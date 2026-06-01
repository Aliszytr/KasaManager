# ADR-004: EF Core Toplu DELETE Pattern — Contains vs Foreach

**Tarih:** 2026-04-27  
**Durum:** Kabul edildi  
**Karar Veren:** Geliştirici + AI Pair  
**Etkilenen Dosya:** `src/KasaManager.Infrastructure/Services/FactNormalizationService.cs`

## Bağlam

Shadow Ingestion sırasında eski DailyFacts kayıtlarının temizlenmesi gerekiyor.
Başlangıçta OPENJSON tabanlı bir yaklaşım kullanılıyordu, sonra foreach
döngüsüne geçildi:

```csharp
// Versiyon 1 (OPENJSON): SQL Server'da büyük JSON parametresi timeout
// Versiyon 2 (foreach): N adet round-trip, N adet lock acquisition
foreach (var bid in previousBatchIds)
{
    totalDeletedFacts += await _dbContext.DailyFacts
        .Where(x => x.ImportBatchId == bid)
        .ExecuteDeleteAsync(ct);
}
```

### Kanıtlanan Sorun
- Her `ExecuteDeleteAsync` ayrı bir SQL DELETE komutu = N round-trip
- Hepsi aynı transaction içinde → kilitler birikir
- Lock Escalation riski: N×satır kilitinin tablo kilidine dönüşmesi
- (Not: Asıl sorun paralel erişimdi — ADR-001'de çözüldü. Bu ADR,
  DELETE sorgusunun kendisini optimize eder.)

## Karar

Döngü kaldırılıp tek `.Contains()` sorgusu ile toplu silme uygulandı:

```csharp
// Versiyon 3 (Contains → SQL IN):
var totalDeletedFacts = await _dbContext.DailyFacts
    .Where(x => previousBatchIds.Contains(x.ImportBatchId))
    .ExecuteDeleteAsync(ct);
```

### EF Core 8 Davranışı
- `previousBatchIds` tipi: `List<Guid>` (`.ToListAsync()` dönüşü)
- EF Core 8'de `List<T>.Contains()` → parametreleştirilmiş `WHERE ... IN (@p0, @p1, ...)`
- Tipik element sayısı: 1-5 batch ID (aynı dosya+tarih kombinasyonu)
- OPENJSON edge case: >2048 element veya `IEnumerable` kullanıldığında tetiklenir
  — burada `List<Guid>` + az element olduğu için IN(...) güvenli

### Index Kullanımı
- `IX_DailyFacts_ImportBatchId` (Nonclustered) mevcut ve doğrulandı
- `IX_DailyFacts_ImportBatchId` migration: `20260427080010_AddDailyFactsImportBatchIdIndex`
- OnModelCreating satır 456-457'de tanımlı

## Performans Karşılaştırması

| Metrik | foreach (N sorgu) | Contains (tek sorgu) |
|--------|-------------------|----------------------|
| Round-trip | 2×N | 2 (sabit) |
| Lock acquisition | N kez ardışık | 1 kez (tablo başına) |
| SQL plan cache | N adet ayrı plan | 1 plan (tekrar kullanılır) |
| Transaction süresi | Uzun (N bekleme) | Kısa (2 sorgu) |

## Değerlendirilen Alternatifler

### A) OPENJSON ile Toplu Parametre
```sql
DELETE FROM DailyFacts WHERE ImportBatchId IN (
    SELECT value FROM OPENJSON(@batchIds)
)
```
- **Neden reddedildi:** Önceki timeout sorununun kaynağıydı. OPENJSON büyük
  JSON parametreleriyle SQL Server'da table scan tetikleyebilir.

### B) Raw SQL ile Parametre Gönderme
```csharp
await _dbContext.Database.ExecuteSqlRawAsync(
    "DELETE FROM DailyFacts WHERE ImportBatchId IN ({0}, {1})", ...);
```
- **Neden reddedildi:** Parametre sayısı dinamik → SQL injection riski
  veya karmaşık parametre yönetimi. EF Core'un type-safe `.Contains()`
  yaklaşımı daha güvenli ve bakımı kolay.

## Sonuçlar

### Olumlu
- Tek sorgu, tek lock acquisition
- IX_DailyFacts_ImportBatchId index'i optimal şekilde kullanılıyor
- EF Core'un parametre güvenliği korunuyor

### Dikkat Edilecekler
- `previousBatchIds` element sayısı çok artarsa (>2048) EF Core OPENJSON'a
  düşebilir. Ancak aynı dosya+tarih için 2048 batch olması pratikte imkansız.
- Bu optimizasyon ADR-001 (global lock) ile birlikte çalışır. Global lock
  olmadan tek başına yeterli değildir (paralel erişim sorunu kalır).
