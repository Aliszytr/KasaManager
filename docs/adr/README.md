# Architecture Decision Records (ADR)

Bu klasör, KasaManager projesinde alınan önemli mimari ve teknik kararları
belgeler. Her ADR, kararın bağlamını, alternatifleri, trade-off'ları ve
sonuçlarını içerir.

## Kayıt Listesi

| ADR | Başlık | Tarih | Durum |
|-----|--------|-------|-------|
| [ADR-001](ADR-001-shadow-ingestion-lock-granularity.md) | Shadow Ingestion Lock Granularitesi | 2026-04-27 | Kabul edildi |
| [ADR-002](ADR-002-matching-disqualify-to-penalty.md) | Matching Diskalifiye → Puan Cezası | 2026-04-27 | Kabul edildi |
| [ADR-003](ADR-003-hesapkontrol-form-routing-ssot.md) | HesapKontrol Form Routing SSOT | 2026-04-27 | Kabul edildi |
| [ADR-004](ADR-004-ef-core-bulk-delete-pattern.md) | EF Core Toplu DELETE Pattern | 2026-04-27 | Kabul edildi |

## Kurallar

- **Format:** Her ADR `Bağlam → Karar → Alternatifler → Sonuçlar` yapısını takip eder
- **Numaralama:** Sıralı, `ADR-NNN` formatında
- **Durum:** `Teklif`, `Kabul edildi`, `Reddedildi`, `Kullanımdan kaldırıldı`
- **Değiştirme:** Eski ADR silinmez, `Durum` güncellenir ve yeni ADR ile referanslanır
