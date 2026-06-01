# ADR-003: HesapKontrol Form Routing — Tarihsel Mod SSOT

**Tarih:** 2026-04-27  
**Durum:** Kabul edildi  
**Karar Veren:** Geliştirici + AI Pair  
**Etkilenen Dosya:** `src/KasaManager.Web/Views/HesapKontrol/Index.cshtml`

## Bağlam

HesapKontrol modülü iki farklı modda çalışır:
1. **Normal mod:** `Index` action → `analizTarihiStr` parametresi ile tarih filtreleme
2. **Tarihsel mod (Zaman Makinesi):** `QueryDate` action → zorunlu `tarih` parametresi

Kullanıcı Karşılaştırma raporundan "Hesap Kontrole Git" butonuna
basıldığında `/HesapKontrol/QueryDate?tarih=2026-04-24` adresine yönlendirilir.

### Kanıtlanan Sorun
"Açık Kayıtlar" sekmesindeki filtre formu:
```html
<form method="get" class="row g-2 align-items-end">
    <input type="hidden" name="tab" value="acik" />
    <input type="hidden" name="analizTarihiStr" value="@tarihStrTabs" />
```
- Form `action` belirtmediği için mevcut URL'e gider
- Tarihsel modda mevcut URL = `/HesapKontrol/QueryDate?tarih=2026-04-24`
- Form `tarih` parametresi göndermiyor, `analizTarihiStr` gönderiyor
- Controller `QueryDate(string tarih, ...)` → `tarih=null` → `BadRequest`
- Kullanıcı "Geçersiz tarih formatı" hatası görüyordu

## Karar

Filtre formu `isHistoricalTabs` değişkenine göre koşullu render ediliyor:

```razor
@if (isHistoricalTabs)
{
    <form method="get" action="@Url.Action("QueryDate", "HesapKontrol")">
        <input type="hidden" name="tarih" value="@tarihStrTabs" />
        <input type="hidden" name="tab" value="acik" />
        ...
    </form>
}
else
{
    <form method="get">
        <input type="hidden" name="tab" value="acik" />
        <input type="hidden" name="analizTarihiStr" value="@tarihStrTabs" />
        ...
    </form>
}
```

### Neden Tek Form + Hidden Input Yerine Koşullu Form?
- `Index` ve `QueryDate` farklı parametreler bekliyor (`analizTarihiStr` vs `tarih`)
- Tek form'a her iki parametreyi de koymak gereksiz veri gönderir ve
  her iki action'da da unexpected parameter uyarısı yaratır
- Koşullu form, her action'a sadece kendi beklediği parametreleri gönderir (SSOT)

## Değerlendirilen Alternatifler

### A) QueryDate Action'ına `analizTarihiStr` Fallback Eklemek
```csharp
var tarihStr = tarih ?? analizTarihiStr;
```
- **Neden reddedildi:** Controller'ın API kontratını bozmak. İki farklı
  parametre aynı şeyi ifade ediyorsa → naming karışıklığı.

### B) JavaScript ile Form Action Dinamik Değiştirme
- `onsubmit` event'inde `isHistorical` kontrol edip action set etmek
- **Neden reddedildi:** JS bağımlılığı oluşturur. Server-side Razor ile
  zaten çözülebilecek bir sorun için client-side karmaşıklık eklenmemeli.

## Sonuçlar

### Olumlu
- QueryDate sayfasında filtre çalışıyor, "Geçersiz tarih" hatası yok
- Normal modda (Index) davranış tamamen korundu — hiç dokunulmadı
- Sekme geçişleri (Özet→Açık→Takipte→Geçmiş) tarihsel bağlamı koruyor

### Risk
- Düşük. Sadece `isHistoricalTabs == true` branch'ı etkileniyor.
  Normal mod (bugünün tarihi) tamamen dokunulmadı.
