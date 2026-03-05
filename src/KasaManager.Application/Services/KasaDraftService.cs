using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Constants;
using KasaManager.Domain.FormulaEngine;
using KasaManager.Domain.Reports;

namespace KasaManager.Application.Services;

/// <summary>
/// R8: Kasa Draft üretimi (DB'ye yazılmaz).
/// Bu servis, doğrulama amaçlı "geçici kasa ekranı"na veri sağlar.
/// </summary>
public sealed partial class KasaDraftService : IKasaDraftService
{
    private readonly IKasaRaporSnapshotService _snapshots;
    private readonly IImportOrchestrator _import;
    private readonly IKasaGlobalDefaultsService _globalDefaults;

    public KasaDraftService(
        IKasaRaporSnapshotService snapshots,
        IImportOrchestrator import,
        IKasaGlobalDefaultsService globalDefaults)
    {
        _snapshots = snapshots;
        _import = import;
        _globalDefaults = globalDefaults;
    }

    public async Task<Result<KasaDraftBundle>> BuildAsync(
        DateOnly raporTarihi,
        string uploadFolderAbsolute,
        KasaDraftFinalizeInputs? finalizeInputs = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(uploadFolderAbsolute))
            return Result<KasaDraftBundle>.Fail("Upload klasörü bulunamadı.");

        var issues = new List<string>();

        // R7 Snapshot kaynağı: KasaÜstRapor snapshot'ı (Genel)
        var genSnap = await _snapshots.GetAsync(raporTarihi, KasaRaporTuru.Genel, ct);
        if (genSnap == null)
        {
            return Result<KasaDraftBundle>.Fail($"{raporTarihi:dd.MM.yyyy} tarihli Genel snapshot bulunamadı. Önce KasaÜstRapor ekranından kaydedin.");
        }

        var selectedVeznedarlar = genSnap.Rows
            .Where(r => r.IsSelected)
            .Select(r => r.Veznedar)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (selectedVeznedarlar.Count == 0)
            issues.Add("KasaÜstRapor snapshot'ında seçili veznedar bulunamadı (selectedRows boş olabilir).");

        finalizeInputs ??= new KasaDraftFinalizeInputs();

        // Banka bakiye kuralı (KİLİTLİ): kaynak = BankaTahsilat, son satır "islem_sonrasi_bakiye"
        var bankaBakiye = TryReadBankaBakiye(uploadFolderAbsolute, issues, out var bankaRawJson);

        // ===== R8 (Akşam) - Legacy benzeri mapping =====
        // KasaÜstRapor TOPLAMLAR satırı (snapshot'tan): Pos/Online/Toplam Tahsilat + Harç + Reddiyat + Stopaj/Vergiler
        var ust = ReadKasaUstRaporSummary(genSnap, issues);

        // OnlineReddiyat: Online Reddiyat + Online Stopaj (Gelir Ver + Damga Ver)
        var online = ReadOnlineReddiyatTotals(raporTarihi, uploadFolderAbsolute, issues, out var onlineRawJson);

        // BankaTahsilat: Devreden/Yarina + giren/cikan (gün filtresi)
        var bankaGun = ReadBankaTahsilatGun(raporTarihi, uploadFolderAbsolute, issues, out var bankaGunRawJson);
        var bankaExtra = ReadBankaTahsilatExtraInflowAgg(raporTarihi, uploadFolderAbsolute, issues, out var bankaExtraRawJson);
        var bankaHarcGun = ReadBankaHarcGun(raporTarihi, uploadFolderAbsolute, issues, out var bankaHarcGunRawJson);
        var onlineHarc = ReadOnlineTotal(raporTarihi, uploadFolderAbsolute, "onlineHarc.xlsx", ImportFileKind.OnlineHarcama, issues, out var onlineHarcRawJson);
        var onlineMasraf = ReadOnlineTotal(raporTarihi, uploadFolderAbsolute, "onlineMasraf.xlsx", ImportFileKind.OnlineMasraf, issues, out var onlineMasrafRawJson);
        var masrafReddiyat = ReadMasrafReddiyatAggUnified(raporTarihi, uploadFolderAbsolute, issues, out var masrafReddiyatRawJson);

        // Dünden devreden kasa (şimdilik: önceki gün Akşam snapshot sonucu varsa oradan)
        var devredenKasa = await DetermineDevredenKasaAsync(raporTarihi, issues, ct);

        // === R13: Akşam/Sabah kasa için kullanıcı girişleri (Legacy parity) ===
        // Bu alanlar eski sistemde kırmızı çerçeveli kullanıcı girişleri.
        // Şimdilik Preview ekranından gelir; ileride Finalize/DB yazımına taşınacak.
        var bankayaYatirilacakHarciDegistir = finalizeInputs.BankayaYatirilacakHarciDegistir ?? 0m;
        var bankayaYatirilacakTahsilatiDegistirManual = finalizeInputs.BankayaYatirilacakTahsilatiDegistir ?? 0m;

        var kaydenTahsilat = finalizeInputs.KaydenTahsilat ?? 0m;
        var kaydenHarc = finalizeInputs.KaydenHarc ?? 0m;

        var bankadanCekilen = finalizeInputs.BankadanCekilen ?? 0m;
        var cesitliNedenlerleBankadanCikamayanTahsilat = finalizeInputs.CesitliNedenlerleBankadanCikamayanTahsilat ?? 0m;
        var bankayaGonderilmisDeger = finalizeInputs.BankayaGonderilmisDeger ?? 0m;

        // R13: Kasada kalacak hedef girildiyse, Yt.Tahs.Değiştir için otomatik öneri üret.
        // Amaç: "GenelKasa (bozuk dahil)" hedefe otursun; kullanıcı üstüne manuel +/- ekleyebilir.
        decimal bankayaYatirilacakTahsilatiDegistirAuto = 0m;
        if (finalizeInputs.KasadaKalacakHedef is decimal targetKasadaKalacak)
        {
            // NormalStopaj = ToplamStopaj - OnlineStopaj (negatifse 0)
            var normalStopajForAuto = ust.Stopaj - (online.OnlineStopaj);
            if (normalStopajForAuto < 0m) normalStopajForAuto = 0m;

            // Clamp'li BankayaYatirilacakNakit devreye girmediği varsayımıyla (legacy formülün lineer kısmı)
            // GenelKasa = DevredenKasa + BankadanCekilen + NormalStopaj + VergidenGelen + CNBCikamayan - KaydenTahsilat - (Yt.Tahs.Degistir)
            var baseForTarget =
                devredenKasa
                + bankadanCekilen
                + normalStopajForAuto
                + (finalizeInputs.VergidenGelen ?? 0m)
                + cesitliNedenlerleBankadanCikamayanTahsilat
                - kaydenTahsilat;

            bankayaYatirilacakTahsilatiDegistirAuto = baseForTarget - targetKasadaKalacak;
            issues.Add($"R13 AUTO: KasadaKalacakHedef={targetKasadaKalacak:N2} girildi. Yt.Tahs.Değiştir önerisi (auto)={bankayaYatirilacakTahsilatiDegistirAuto:N2}, manuel={bankayaYatirilacakTahsilatiDegistirManual:N2}.");
        }

        var bankayaYatirilacakTahsilatiDegistir = bankayaYatirilacakTahsilatiDegistirAuto + bankayaYatirilacakTahsilatiDegistirManual;
        var vergiKasa = finalizeInputs.VergiKasaBakiyeToplam ?? 0m;
        var vergiGelenKasa = finalizeInputs.VergidenGelen ?? 0m;

        // R15B: Normal Tahsilat (FİZİKİ Tahsilat) — KİLİTLİ KURAL
        // Excel'de "NormalTahsilat" ayrı bir sütun olarak gelmez; KasaÜstRapor.xlsx içindeki "Tahsilat" sütunu zaten fiziki/normal tahsilattır.
        // NOT: Daha önce burada yanlışlıkla "Tahsilat - VergiKasa" yapılmıştı. Bu ham katmanda hesaplamadır ve parity'yi bozar.
        // Bu yüzden NormalTahsilat ham olarak direkt ust.Tahsilat alınır (negatif beklenmez).
        var normalTahsilatRaw = ust.Tahsilat;

        // Guard: NormalTahsilat negatif olmamalı; olursa veri/kaynak tutarsızlığıdır.
        // Negatif çıkarsa 0'a clamp edip uyarı üretelim (ileride UnifiedPool GuardRules'a taşınacak).
        var negativeKasaGuardActive = normalTahsilatRaw < 0m;
        if (negativeKasaGuardActive)
        {
            issues.Add($"NEGATIVE KASA GUARD: NormalTahsilat negatif geldi. Tahsilat={ust.Tahsilat:N2}. Kaynağı/veznedar seçimlerini kontrol edin.");
        }

        var normalTahsilat = negativeKasaGuardActive ? 0m : normalTahsilatRaw;

        var bozukPara = finalizeInputs.BozukPara ?? 0m;

#pragma warning disable CS0618 // Intentional: Legacy method kept for parity checking
var aksamCalc = CalculateAksamLegacy(
    devredenKasa: devredenKasa,
    isSabah: false,
    bankaTahsilatGun: bankaGun,
    bankaHarcGun: bankaHarcGun,
    ust: ust,
    online: online,
    bankayaYatirilacakHarciDegistir: bankayaYatirilacakHarciDegistir,
    bankayaYatirilacakTahsilatiDegistir: bankayaYatirilacakTahsilatiDegistir,
    kaydenTahsilat: kaydenTahsilat,
    kaydenHarc: kaydenHarc,
    vergiKasa: vergiKasa,
    vergiGelenKasa: vergiGelenKasa,
    bankadanCekilen: bankadanCekilen,
    cesitliNedenlerleBankadanCikamayanTahsilat: cesitliNedenlerleBankadanCikamayanTahsilat,
    bankayaGonderilmisDeger: bankayaGonderilmisDeger,
    bozukPara: bozukPara);
#pragma warning restore CS0618


        // R9.3: SabahKasa (parity modu)
        // Kullanıcı isteği: SabahKasa alanları AkşamKasa ile aynı kaynaklardan (KasaÜstRapor + yardımcı 5 Excel)
        // doldurulmalı. Bu fazda Sabah için ayrı bir formül ayrışımı yok; Akşam legacy hesap motoru kullanılır.
        #pragma warning disable CS0618 // LEGACY parity/debug only – intentionally used
        var sabahCalc = CalculateAksamLegacy(
            devredenKasa: devredenKasa,
            isSabah: true,
            bankaTahsilatGun: bankaGun,
            bankaHarcGun: bankaHarcGun,
            ust: ust,
            online: online,
            bankayaYatirilacakHarciDegistir: bankayaYatirilacakHarciDegistir,
            bankayaYatirilacakTahsilatiDegistir: bankayaYatirilacakTahsilatiDegistir,
            kaydenTahsilat: kaydenTahsilat,
            kaydenHarc: kaydenHarc,
            vergiKasa: vergiKasa,
            vergiGelenKasa: vergiGelenKasa,
            bankadanCekilen: bankadanCekilen,
            cesitliNedenlerleBankadanCikamayanTahsilat: cesitliNedenlerleBankadanCikamayanTahsilat,
            bankayaGonderilmisDeger: bankayaGonderilmisDeger,
            bozukPara: bozukPara);
        #pragma warning restore CS0618

        var efChain = await ComputeEksikFazlaChainAsync(raporTarihi, uploadFolderAbsolute, ct);

        // ✅ R10.8 FIX PACK:
        // Genel sekmesi artık KasaÜstRapor (günlük) değil, R10 "True Source" mantığı ile dolar:
        // Kaynak: MasrafveReddiyat.xlsx + BankaTahsilat.xlsx (wwwroot/Data/Raporlar)
        // Formül: GenelKasaRaporController ile birebir.
        var genelDraft = await BuildGenelKasaR10DraftAsync(raporTarihi, finalizeInputs, uploadFolderAbsolute, issues, ct);
        var genel = new KasaDraftResult
        {
            Title = "Genel Kasa (R10 – True Source)",
            RawJson = genelDraft.rawJson,
            Fields = genelDraft.fields
        };

        // Sabah/Akşam draft'ları: bu fazda hesap motoru yeni kurulacağı için,
        // en azından doğrulama ekranı boş kalmasın diye temel alanları gösteriyoruz.
        // Sonraki adımda gerçek hesaplar bu alanların üstüne yazılacak.
        var sabah = new KasaDraftResult
        {
            Title = "Sabah Kasa (Draft)",
            RawJson = BuildRawJson(genSnap, bankaRawJson, onlineRawJson, bankaGunRawJson, bankaExtraRawJson, bankaHarcGunRawJson, onlineHarcRawJson, onlineMasrafRawJson, masrafReddiyatRawJson),
#pragma warning disable CS0618 // Intentional: Legacy method for parity
            InlineFormulas = BuildSabahInlineFormulas(devredenKasa, ust, online, vergiKasa, kaydenTahsilat, kaydenHarc, bankayaYatirilacakHarciDegistir, bankayaYatirilacakTahsilatiDegistirAuto, bankayaYatirilacakTahsilatiDegistirManual, bankayaYatirilacakTahsilatiDegistir, (finalizeInputs.KasadaKalacakHedef ?? 0m), cesitliNedenlerleBankadanCikamayanTahsilat, bankadanCekilen, vergiGelenKasa, bankayaGonderilmisDeger, bozukPara, sabahCalc),
#pragma warning restore CS0618
            Fields = BuildSabahFields(
                raporTarihi, selectedVeznedarlar, genSnap, bankaBakiye,
                bankaGun, bankaExtra, bankaHarcGun, ust, online,
                onlineHarc, onlineMasraf, masrafReddiyat,
                devredenKasa, vergiKasa, vergiGelenKasa,
                kaydenTahsilat, kaydenHarc, bankadanCekilen,
                bankayaYatirilacakHarciDegistir,
                bankayaYatirilacakTahsilatiDegistirAuto,
                bankayaYatirilacakTahsilatiDegistirManual,
                bankayaYatirilacakTahsilatiDegistir,
                cesitliNedenlerleBankadanCikamayanTahsilat,
                bankayaGonderilmisDeger, bozukPara,
                (finalizeInputs.KasadaKalacakHedef ?? 0m),
                negativeKasaGuardActive, sabahCalc, efChain, finalizeInputs)
        };

        var aksam = new KasaDraftResult
        {
            Title = "Akşam Kasa (Draft)",
            RawJson = BuildRawJson(genSnap, bankaRawJson, onlineRawJson, bankaGunRawJson, bankaExtraRawJson, bankaHarcGunRawJson, onlineHarcRawJson, onlineMasrafRawJson, masrafReddiyatRawJson),
            #pragma warning disable CS0618 // LEGACY parity/debug only – intentionally used
            InlineFormulas = BuildAksamInlineFormulas(devredenKasa, ust, online, vergiKasa, kaydenTahsilat, kaydenHarc, bankayaYatirilacakHarciDegistir, bankayaYatirilacakTahsilatiDegistirAuto, bankayaYatirilacakTahsilatiDegistirManual, bankayaYatirilacakTahsilatiDegistir, (finalizeInputs.KasadaKalacakHedef ?? 0m), cesitliNedenlerleBankadanCikamayanTahsilat, bankadanCekilen, vergiGelenKasa, bankayaGonderilmisDeger, bozukPara, aksamCalc),
            #pragma warning restore CS0618

            Fields = BuildAksamFields(
                raporTarihi, selectedVeznedarlar, genSnap, bankaBakiye,
                bankaGun, bankaExtra, bankaHarcGun, ust, online,
                onlineHarc, onlineMasraf, masrafReddiyat,
                devredenKasa, normalTahsilat, vergiKasa, vergiGelenKasa,
                kaydenTahsilat, kaydenHarc, bankadanCekilen,
                bankayaYatirilacakHarciDegistir,
                bankayaYatirilacakTahsilatiDegistirAuto,
                bankayaYatirilacakTahsilatiDegistirManual,
                bankayaYatirilacakTahsilatiDegistir,
                cesitliNedenlerleBankadanCikamayanTahsilat,
                bankayaGonderilmisDeger, bozukPara,
                (finalizeInputs.KasadaKalacakHedef ?? 0m),
                negativeKasaGuardActive, aksamCalc, finalizeInputs)
        };

        var bundle = new KasaDraftBundle
        {
            RaporTarihi = raporTarihi,
            Genel = genel,
            Sabah = sabah,
            Aksam = aksam,
            Issues = issues
        };

        return Result<KasaDraftBundle>.Success(bundle);
    }


    public async Task<Result<KasaDraftResult>> BuildGenelKasaTrueSourceV2Async(
        DateOnly raporTarihi,
        string uploadFolderAbsolute,
        KasaDraftFinalizeInputs? finalizeInputs = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(uploadFolderAbsolute))
            return Result<KasaDraftResult>.Fail("Upload klasörü bulunamadı.");

        var issues = new List<string>();

        var draft = await BuildGenelKasaR10DraftAsync(
            raporTarihi,
            finalizeInputs,
            uploadFolderAbsolute,
            issues,
            ct);

        var result = new KasaDraftResult
        {
            Title = "Genel Kasa (True Source v2)",
            RawJson = draft.rawJson,
            Fields = draft.fields
        };

        // Bu metod sadece Genel kartını döner, ama sorunlar UI'de görünür olsun diye RawJson içine zaten gömülü.
        // İstenirse controller tarafında Issues listesi de gösterilebilir.
        return Result<KasaDraftResult>.Success(result);
    }

    public async Task<Result<GenelKasaR10EngineInputBundle>> BuildGenelKasaR10EngineInputsAsync(
        DateOnly? selectedBitisTarihi,
        decimal? gelmeyenD,
        string uploadFolderAbsolute,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(uploadFolderAbsolute))
            return Result<GenelKasaR10EngineInputBundle>.Fail("Upload klasörü bulunamadı.");

        var issues = new List<string>();

        // 1) EndDate (Bitiş Tarihi): UI'den gelirse onu kullan, gelmezse MasrafveReddiyat.xlsx içinden max tarih.
        var endDate = selectedBitisTarihi ?? await ResolveMaxDateFromMasrafveReddiyatAsync(uploadFolderAbsolute, issues, ct);
        if (endDate == null)
            return Result<GenelKasaR10EngineInputBundle>.Fail("MasrafveReddiyat.xlsx içinde tarih bulunamadı.");

        // 2) RangeStart + Devreden: en son GenelKasa snapshot'ına göre belirlenir (R12.5 prensibi).
        var (rangeStart, devredenSonTarihi, devreden) = await ResolveGenelKasaRangeAndDevredenAsync(endDate.Value, ct);

        // 3) True Source hesapları: Masraf/Reddiyat toplamları + Banka bakiye
        var (toplamTahsilat, toplamReddiyat, kaydenTahsilat, masrafRawJson, masrafOk) = await ImportMasrafveReddiyatAggAsync(uploadFolderAbsolute, rangeStart, endDate.Value, issues, ct);
        var (bankaBakiye, bankaRawJson, bankaOk) = await ImportBankaBakiyeAsync(uploadFolderAbsolute, rangeStart, endDate.Value, issues, ct);

        // 4) Defaults (Ayarlar)
        var defaults = await _globalDefaults.GetOrCreateAsync(ct);
        var eksikFazla = defaults.DefaultKasaEksikFazla ?? 0m;
        var kasaNakit = (defaults.DefaultNakitPara ?? 0m) + (defaults.DefaultBozukPara ?? 0m);

        // 5) UnifiedPool (UI + Engine için tek kaynak inputlar)
        //    - Raw: excel'den türeyenler
        //    - Override: settings / UI inputları
        var pool = new List<UnifiedPoolEntry>
        {
            CreateRawEntry(KasaCanonicalKeys.ToplamTahsilat, toplamTahsilat, "MasrafveReddiyat", "MasrafveReddiyat.xlsx", $"{rangeStart:dd.MM.yyyy}-{endDate:dd.MM.yyyy} toplam", includeInCalculations: masrafOk, notes: masrafOk ? null : "MISSING_SOURCE: MasrafveReddiyat.xlsx okunamadı; hesaplamaya dahil edilmedi."),
            CreateRawEntry(KasaCanonicalKeys.ToplamReddiyat, toplamReddiyat, "MasrafveReddiyat", "MasrafveReddiyat.xlsx", $"{rangeStart:dd.MM.yyyy}-{endDate:dd.MM.yyyy} toplam", includeInCalculations: masrafOk, notes: masrafOk ? null : "MISSING_SOURCE: MasrafveReddiyat.xlsx okunamadı; hesaplamaya dahil edilmedi."),
            CreateRawEntry(KasaCanonicalKeys.KaydenTahsilat, kaydenTahsilat, "MasrafveReddiyat", "MasrafveReddiyat.xlsx", $"{rangeStart:dd.MM.yyyy}-{endDate:dd.MM.yyyy} toplam", includeInCalculations: masrafOk, notes: masrafOk ? null : "MISSING_SOURCE: MasrafveReddiyat.xlsx okunamadı; hesaplamaya dahil edilmedi."),
            CreateRawEntry(KasaCanonicalKeys.BankaBakiye, bankaBakiye, "BankaTahsilat", "BankaTahsilat.xlsx", $"{rangeStart:dd.MM.yyyy}-{endDate:dd.MM.yyyy} bakiye", includeInCalculations: bankaOk, notes: bankaOk ? null : "MISSING_SOURCE: BankaTahsilat.xlsx okunamadı; hesaplamaya dahil edilmedi."),

            CreateOverrideEntry(KasaCanonicalKeys.Devreden, devreden, "GenelKasa snapshot/seed devreden"),
            CreateOverrideEntry(KasaCanonicalKeys.GelmeyenD, gelmeyenD ?? 0m, "UI manuel"),
            CreateOverrideEntry(KasaCanonicalKeys.EksikFazla, eksikFazla, "Ayarlar: DefaultKasaEksikFazla"),
            CreateOverrideEntry(KasaCanonicalKeys.KasaNakit, kasaNakit, "Ayarlar: DefaultNakitPara+DefaultBozukPara"),
        };

        // Debug JSON (min)
        var rawJson = JsonSerializer.Serialize(new
        {
            RangeStart = rangeStart,
            EndDate = endDate,
            Masraf = new { toplamTahsilat, toplamReddiyat, kaydenTahsilat, masrafRawJson, masrafOk },
            Banka = new { bankaBakiye, bankaRawJson, bankaOk },
            BankaBakiye = bankaBakiye,
            Devreden = devreden,
            GelmeyenD = gelmeyenD ?? 0m,
            EksikFazla = eksikFazla,
            KasaNakit = kasaNakit,
            Issues = issues
        }, new JsonSerializerOptions { WriteIndented = true });

        var bundle = new GenelKasaR10EngineInputBundle
        {
            BaslangicTarihi = rangeStart,
            BitisTarihi = endDate.Value,
            DevredenSonTarihi = devredenSonTarihi,
            PoolEntries = pool,
            RawJson = rawJson,
            Issues = issues
        };

        return Result<GenelKasaR10EngineInputBundle>.Success(bundle);
    }
}
