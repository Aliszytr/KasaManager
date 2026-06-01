# ADR-001: Shadow Ingestion Lock Granularitesi

**Tarih:** 2026-04-27  
**Durum:** Kabul edildi  
**Karar Veren:** Geliştirici + AI Pair  
**Etkilenen Dosya:** `src/KasaManager.Infrastructure/Services/FactNormalizationService.cs`

## Bağlam

ImportOrchestrator, dosya yüklemesi sırasında `Task.Run` ile her dosya için
bağımsız bir shadow ingestion arka plan görevi başlatır (throttle yok).
FactNormalizationService içindeki SemaphoreSlim lock anahtarı dosya+tarih
bazlıydı (`shadow_lock_{fileName}_{targetDate}`), yani farklı dosyalar
birbirini hiç beklemiyordu.

6 dosya aynı anda yüklendiğinde, 6 paralel DELETE + INSERT işlemi
DailyFacts tablosuna (6.1M satır, `IX_DailyFacts_ImportBatchId` nonclustered
index mevcut) eşzamanlı erişim yapıyordu.

### Kanıtlanan Sorun (SQL Server)
- DELETE işlemleri Row Lock alıyor → satır sayısı >5000 olduğunda SQL Server
  Lock Escalation tetikliyor → Table X-Lock
- İlk transaction Table Lock aldığında diğer 5 task `LCK_M_X` bekleme durumuna
  geçiyor
- Tüm bekleyen task'lar CommandTimeout (60sn) dolduğunda aynı saniyede
  patlıyor (log kanıtı: 6 dosya 13:18:52'de timeout)

### Veritabanı Bulguları (sqlcmd ile doğrulandı)
- `DailyFacts` satır sayısı: **6.096.538**
- `IX_DailyFacts_ImportBatchId`: Nonclustered, mevcut
- RCSI: Açık (`is_read_committed_snapshot_on = 1`)
- Connection string'de MARS yok

## Karar

SemaphoreSlim lock anahtarı dosya bazından **global sabit**'e çevrildi:

```csharp
// Eski:
var lockKey = $"shadow_lock_{fileName}_{targetDate:yyyyMMdd}";

// Yeni:
const string GlobalShadowLockKey = "shadow_lock_dailyfacts_global";
var lockKey = GlobalShadowLockKey;
```

Tüm shadow ingestion'lar artık aynı SemaphoreSlim(1,1) üzerinden sıralı
geçiyor. DailyFacts tablosuna aynı anda yalnızca 1 transaction erişiyor.

## Değerlendirilen Alternatifler

### A) Throttled Paralelizm (Channel + N Worker)
- `ImportOrchestrator`'da `Channel<ShadowIngestionJob>` kurup, bounded
  consumer (MaxDegreeOfParallelism=1) ile işlemek
- **Neden reddedildi:** Mimari değişim çok büyük. ImportOrchestrator'ın
  fire-and-forget yapısına dokunmak domino etkisi yaratabilir.
  Global lock aynı sonucu çok daha az invaziv şekilde sağlıyor.

### B) Transaction Scope Daraltma
- DELETE ve INSERT'i ayrı transaction'lara bölmek
- **Neden reddedildi:** Atomiklik kaybı. DELETE başarılı olup INSERT
  başarısız olursa veri kaybı. RCSI açık olsa bile yazma-yazma çakışması
  devam eder.

### C) Batch Chunking (100'lük DELETE'ler)
- previousBatchIds'i chunk'lara böl, her chunk ayrı transaction
- **Neden reddedildi:** ImportBatchId başına zaten az kayıt var (tipik 1-5
  batch). Chunk'lama ek karmaşıklık getirir, asıl sorun (paralel erişim)
  çözülmez.

### D) CommandTimeout Artırma
- **Neden reddedildi:** Semptom tedavisi. Kök neden (lock contention)
  gizlenir, kullanıcı deneyimi kötüleşir (bekleme süresi artar).

## Sonuçlar

### Olumlu
- **Throughput:** 6 dosya ~12sn (2sn × 6, sıralı) — önceden hepsi timeout
- **Basitlik:** Tek satır değişiklik, mevcut altyapı korundu
- **Güvenilirlik:** Lock contention sıfırlandı, timeout riski ortadan kalktı

### Olumsuz (Kabul Edilen Trade-off)
- Tüm ingestion'lar sıralı çalışıyor — paralel throughput yok
- Ancak önceki paralel mod zaten çalışmıyordu (6/6 timeout), dolayısıyla
  fiili throughput kaybı sıfır

### Gelecek İyileştirme Fırsatları
- DailyFacts tablasu partitioning (tarih bazlı) uygulanırsa lock escalation
  partition seviyesinde kalır → paralel ingestion güvenli hale gelir
- ImportOrchestrator'a bounded Channel eklenirse global lock kaldırılabilir
  (ama partitioning olmadan lock contention devam eder)
