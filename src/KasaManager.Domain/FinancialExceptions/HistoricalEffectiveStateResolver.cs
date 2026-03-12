#nullable enable
namespace KasaManager.Domain.FinancialExceptions;

/// <summary>
/// Faz 3.1: Target-date aware effective state resolver.
/// Hedef tarihte bir istisnanın gerçek durumunu belirler.
/// ARCHITECTURE LOCK: FormulaEngine değiştirmez, runtime projection üretir.
/// </summary>
public static class HistoricalEffectiveStateResolver
{
    /// <summary>
    /// Bir istisnanın bugünkü durumuna göre runtime-effective olup olmadığını belirler.
    /// Mevcut BekleyenTutarCalculator.IsRuntimeEffective ile aynı — backward compat.
    /// </summary>
    public static bool IsRuntimeEffectiveNow(FinansalIstisna ex) =>
        BekleyenTutarCalculator.IsRuntimeEffective(ex);

    /// <summary>
    /// Hedef tarihte bir istisnanın runtime-effective olup olmadığını
    /// history event kayıtlarını kullanarak belirler.
    /// 
    /// Yöntem: History eventlerini kronolojik sırada yürüterek
    /// hedef tarihteki KararDurumu ve Durum'u reconstruct eder.
    /// </summary>
    public static HistoricalState ResolveStateAt(
        FinansalIstisna ex,
        DateOnly targetDate,
        IReadOnlyList<FinansalIstisnaHistory> history)
    {
        var targetEnd = targetDate.ToDateTime(new TimeOnly(23, 59, 59), DateTimeKind.Utc);

        // History boşsa → istisna create tarihi hedef tarih öncesiyse, ilk durum
        if (history.Count == 0)
        {
            // Kayıt oluşturma zamanı hedef tarihten sonra → var değildi
            if (ex.OlusturmaTarihiUtc > targetEnd)
                return HistoricalState.NotExisted;

            // History yoksa mevcut durumu kullan (legacy uyum)
            return new HistoricalState(
                Existed: true,
                Tur: ex.Tur,
                KararDurumu: ex.KararDurumu,
                Durum: ex.Durum,
                BeklenenTutar: ex.BeklenenTutar,
                GerceklesenTutar: ex.GerceklesenTutar,
                SistemeGirilenTutar: ex.SistemeGirilenTutar,
                Explanation: "History kaydı yok — mevcut durum kullanıldı");
        }

        // Created event: kayıt oluşturulma zamanını belirle
        var createdEvent = history.FirstOrDefault(h =>
            h.EventType == IstisnaHistoryEventType.Created ||
            h.EventType == IstisnaHistoryEventType.CreateFromDevredilmis);

        var createTime = createdEvent?.EventTarihiUtc ?? ex.OlusturmaTarihiUtc;

        // Hedef tarih kayıt oluşturulmadan önce
        if (createTime > targetEnd)
            return HistoricalState.NotExisted;

        // Hedef tarihe kadar olan eventleri kronolojik sırala
        var relevantEvents = history
            .Where(h => h.EventTarihiUtc <= targetEnd)
            .OrderBy(h => h.EventTarihiUtc)
            .ToList();

        if (relevantEvents.Count == 0)
        {
            // Kayıt vardı ama history event yok — ilk durum
            return new HistoricalState(
                Existed: true,
                Tur: ex.Tur,
                KararDurumu: KararDurumu.IncelemeBekliyor,
                Durum: IstisnaDurumu.Acik,
                BeklenenTutar: ex.BeklenenTutar,
                GerceklesenTutar: 0m,
                SistemeGirilenTutar: 0m,
                Explanation: "İlk oluşturulma durumu");
        }

        // Son event'e kadar yürüterek durumu reconstruct et
        KararDurumu karar = KararDurumu.IncelemeBekliyor;
        IstisnaDurumu durum = IstisnaDurumu.Acik;
        decimal beklenen = ex.BeklenenTutar;
        decimal gerceklesen = 0m;
        decimal sisteme = 0m;
        string explanation = "";

        foreach (var evt in relevantEvents)
        {
            if (evt.NewKararDurumu.HasValue) karar = evt.NewKararDurumu.Value;
            if (evt.NewDurum.HasValue) durum = evt.NewDurum.Value;
            if (evt.NewBeklenenTutar.HasValue) beklenen = evt.NewBeklenenTutar.Value;
            if (evt.NewGerceklesenTutar.HasValue) gerceklesen = evt.NewGerceklesenTutar.Value;
            if (evt.NewSistemeGirilenTutar.HasValue) sisteme = evt.NewSistemeGirilenTutar.Value;

            explanation = evt.EventType switch
            {
                IstisnaHistoryEventType.Created => "Oluşturuldu",
                IstisnaHistoryEventType.KararOnaylandi => "Onaylandı",
                IstisnaHistoryEventType.KararReddedildi => "Reddedildi",
                IstisnaHistoryEventType.KismiCozuldu => $"Kısmen çözüldü (Sisteme: {sisteme:N2} ₺)",
                IstisnaHistoryEventType.Cozuldu => "Çözüldü",
                IstisnaHistoryEventType.ErtesiGuneDevredildi => "Ertesi güne devredildi",
                IstisnaHistoryEventType.IptalEdildi => "İptal edildi",
                IstisnaHistoryEventType.TutarGuncellendi => "Tutar güncellendi",
                IstisnaHistoryEventType.CreateFromDevredilmis => "Devredilmişten oluşturuldu",
                _ => evt.Aciklama ?? "Değişiklik"
            };
        }

        return new HistoricalState(
            Existed: true,
            Tur: ex.Tur,
            KararDurumu: karar,
            Durum: durum,
            BeklenenTutar: beklenen,
            GerceklesenTutar: gerceklesen,
            SistemeGirilenTutar: sisteme,
            Explanation: explanation);
    }

    /// <summary>
    /// Historical state'in runtime-effective olup olmadığını belirler.
    /// Aynı IsRuntimeEffective kuralı: Onaylandi + (Acik | KismiCozuldu).
    /// </summary>
    public static bool IsEffective(HistoricalState state) =>
        state.Existed
        && state.KararDurumu == KararDurumu.Onaylandi
        && state.Durum is IstisnaDurumu.Acik or IstisnaDurumu.KismiCozuldu;
}

/// <summary>
/// Bir istisnanın belirli bir tarihteki reconstruct edilmiş durumu.
/// </summary>
public sealed record HistoricalState(
    bool Existed,
    IstisnaTuru Tur,
    KararDurumu KararDurumu,
    IstisnaDurumu Durum,
    decimal BeklenenTutar,
    decimal GerceklesenTutar,
    decimal SistemeGirilenTutar,
    string Explanation)
{
    /// <summary>Hedef tarihte kayıt henüz mevcut değildi.</summary>
    public static HistoricalState NotExisted => new(
        Existed: false,
        Tur: default,
        KararDurumu: KararDurumu.IncelemeBekliyor,
        Durum: IstisnaDurumu.Acik,
        BeklenenTutar: 0, GerceklesenTutar: 0, SistemeGirilenTutar: 0,
        Explanation: "Bu tarihte kayıt henüz oluşturulmamıştı");

    /// <summary>
    /// Tür-spesifik bekleyen tutar hesabı.
    /// ARCHITECTURE LOCK: BekleyenTutarCalculator ile runtime/historical parity sağlar.
    /// </summary>
    public decimal EffectiveBekleyenTutar =>
        BekleyenTutarCalculator.Hesapla(Tur, BeklenenTutar, GerceklesenTutar, SistemeGirilenTutar);
}
