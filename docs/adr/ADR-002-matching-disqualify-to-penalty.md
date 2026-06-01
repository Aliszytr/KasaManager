# ADR-002: Matching Diskalifiye Kuralı → Puan Cezası

**Tarih:** 2026-04-27  
**Durum:** Kabul edildi  
**Karar Veren:** Geliştirici + AI Pair  
**Etkilenen Dosya:** `src/KasaManager.Application/Services/Comparison/ComparisonService.Matching.cs`

## Bağlam

Karşılaştırma motoru (`FindBestMatch`), banka açıklamalarını online kayıtlarla
eşleştirirken çok katmanlı bir puanlama sistemi kullanır:
- Tutar eşleşmesi: +0.1 (bazal)
- EsasNo tam eşleşme: +0.4
- BirimAdı eşleşmesi: +0.3
- Tarih eşleşmesi: +0.2

Son güncellemede eklenen "agresif diskalifiye" kuralı:
```csharp
if (esasNoMatches && courtNumberMismatch) {
    continue; // Adayı tamamen listeden çıkar
}
```

### Kanıtlanan Sorun
- Banka açıklamaları serbest metin: "Ankara 11. idare", "ankara onbirinci idare",
  "11 idare", "11. İdare Mahkemesi" gibi varyasyonlar içerir
- `courtNumberMismatch` değişkeni string parse/regex farklılıklarına aşırı duyarlı
- Ufak bir parse sapmasında `true` dönerek EsasNo eşleşen adayı tamamen siliyordu
- Sonuç: Yüzlerce geçerli eşleşme "Eşleşmedi (182 adet)" sepetine düşüyordu
- Akıllı Öneriler sekmesi boşalıyordu — kullanıcı onaylayacak kayıt bulamıyordu

## Karar

Agresif `continue` (tamamen diskalifiye) kaldırılıp **puan cezası** uygulandı:

```csharp
if (esasNoMatches && courtNumberMismatch)
{
    score -= 0.4; // EsasNo bonusunu geri al
    if (score < 0.1) score = 0.1; // Minimum bazal puan
    reasons.Add("⚠ Mahkeme numarası uyuşmuyor (EsasNo eşleşti ama teyit gerekli)");
}
```

### Puan Akışı Tablosu

| Senaryo | Eski Davranış | Yeni Davranış |
|---------|---------------|---------------|
| EsasNo ✓ + Mahkeme ✓ | score=0.5+ → Matched | Değişmedi |
| EsasNo ✓ + Mahkeme ✗ | `continue` → **KAYIP** | score=0.1~0.3 → PartialMatch |
| EsasNo ✗ | Değişmedi | Değişmedi |

## Değerlendirilen Alternatifler

### A) Mahkeme Parse Algoritmasını İyileştirme
- Regex'i daha toleranslı yapmak ("onbirinci" → "11" dönüşümü vb.)
- **Neden ertelendi:** Parse edge case'leri sonsuz. Her yeni varyasyon için
  regex eklemek bakım yükü oluşturur. Puan cezası yaklaşımı tüm edge case'leri
  tek seferde kapsar.

### B) Fuzzy String Matching (Levenshtein)
- Mahkeme isimlerinde %80+ benzerlik skoru
- **Neden reddedildi:** Yeni paket bağımlılığı (yasak). Ayrıca "11" vs
  "onbirinci" Levenshtein mesafesi çok yüksek ama aynı mahkeme.

### C) Diskalifiye Kuralını Tamamen Kaldırma
- `courtNumberMismatch` bloğunu silmek
- **Neden reddedildi:** Gerçekten farklı mahkemelere ait eşleşmelerin
  yüksek güvenle Matched olarak gösterilmesi yanıltıcı olur.
  Puan cezası ile PartialMatch'e düşürmek daha doğru.

## Sonuçlar

### Olumlu
- **Smoke test:** Eşleşen: 494, Kısmi: 4, Eşleşmeyen: 0 (498 toplam)
- Önceki "182 Eşleşmedi" tamamen temizlendi
- Akıllı Öneriler sekmesi tekrar aktif — kullanıcı onay/ret yapabiliyor
- "⚠ Mahkeme numarası uyuşmuyor" uyarısı kullanıcıya transparan bilgi veriyor

### Olumsuz (Kabul Edilen Trade-off)
- Gerçekten yanlış eşleşmeler de Kısmi Eşleşme olarak görünebilir
- **Mitigation:** Bunlar otomatik kabul edilmez, kullanıcı Onayla/Reddet yapmalı
- ⚠ uyarı metni bu kayıtları açıkça işaretliyor

## Tasarım Prensibi
> "Yazılım, belirsiz durumlarda kullanıcıyı bilgilendirmeli,
> kullanıcı adına sessizce karar vermemeli."
>
> Agresif `continue` = sessiz karar (veri kaybı).
> Puan cezası + uyarı = kullanıcıya seçim hakkı.
