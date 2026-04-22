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

// Pool: BuildUnifiedPoolAsync, TryReadBankaBakiye
public sealed partial class KasaDraftService
{
    public async Task<Result<IReadOnlyList<UnifiedPoolEntry>>> BuildUnifiedPoolAsync(
        DateOnly raporTarihi,
        string uploadFolderAbsolute,
        KasaDraftFinalizeInputs? finalizeInputs = null,
        DateOnly? rangeStart = null,
        DateOnly? rangeEnd = null,
        bool fullExcelTotals = false,
        string? kasaScope = null,
        bool mesaiSonuModu = false,
        bool skipSlimPoolFilter = false,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(uploadFolderAbsolute))
            return Result<IReadOnlyList<UnifiedPoolEntry>>.Fail("Upload klasörü bulunamadı.");

        var issues = new List<string>();

        // P4.4: Snapshot dependency completely removed.
        // We now rely exclusively on Live Data for the Unified Pool.

        var liveProvider = new IntermediateLiveUstRaporProvider(_import);
        var liveSummaryRes = liveProvider.GetSummary(raporTarihi, uploadFolderAbsolute, issues);
        if (!liveSummaryRes.Ok || liveSummaryRes.Value == null)
            return Result<IReadOnlyList<UnifiedPoolEntry>>.Fail($"{raporTarihi:dd.MM.yyyy} tarihli canlı (live) KasaÜstRapor okunamadı. {liveSummaryRes.Error}");

        finalizeInputs ??= new KasaDraftFinalizeInputs();

        // Global defaults (Ayarlar) – eski sistemden gelen değerler bozulmadan devam etmeli.
        // Bu değerleri UnifiedPool'a da "Override" olarak ekliyoruz ki Formula Authoring tarafında checkbox ile seçilip kullanılabilsin.
        var defaults = await _globalDefaults.GetOrCreateAsync(ct);
        var defaultDundenDevredenKasaNakit = defaults.DefaultDundenDevredenKasaNakit ?? 0m;
        var defaultKasaEksikFazla = defaults.DefaultKasaEksikFazla ?? 0m;

        // R5.1 Contract-First: Legacy Akşam/Sabah parity için devreden kasa değeri engine inputs içinde bulunmalı.
        // Eğer KasaScope "Genel" ise GenelKasa devreden SSOT değerini kullanırız.
        decimal devredenKasa = 0m;
        if (kasaScope != null && (kasaScope.Equals("Genel", StringComparison.OrdinalIgnoreCase) || kasaScope.Equals("GenelKasa", StringComparison.OrdinalIgnoreCase)))
        {
            // BUG FIX: Genel kasa dönemsel bir raporlama ise, devredeni ararken Bitiş Tarihine (raporTarihi) göre değil, 
            // Başlangıç Tarihine (rangeStart) göre geriye doğru aramak gerekir!
            var targetDate = rangeStart ?? raporTarihi;
            var ssot = await DetermineGenelKasaDevredenSsotAsync(targetDate, ct);
            devredenKasa = ssot.Devreden;
        }
        else
        {
            var scopeForCarryover = kasaScope?.StartsWith("Sabah", StringComparison.OrdinalIgnoreCase) == true 
                ? CarryoverScope.SabahKasaNakit 
                : CarryoverScope.AksamKasaNakit;
            devredenKasa = await DetermineDevredenKasaAsync(raporTarihi, scopeForCarryover, issues, ct);
        }

        // Raw kaynaklar
        var hasRange = rangeStart.HasValue && rangeEnd.HasValue;
        var useFullExcel = !hasRange && fullExcelTotals;

        var l = liveSummaryRes.Value;
        var ust = new KasaUstSummary(l.PosTahsilat, l.OnlineTahsilat, l.PostTahsilat, l.Tahsilat, l.Reddiyat, l.PosHarc, l.OnlineHarc, l.PostHarc, l.GelmeyenPost, l.Harc, l.GelirVergisi, l.DamgaVergisi, l.Stopaj);
        var online = ReadOnlineReddiyatTotals(raporTarihi, uploadFolderAbsolute, issues, out _, rangeStart: rangeStart, rangeEnd: rangeEnd, fullExcelTotals: useFullExcel);
        
        var theRangeStart = rangeStart ?? raporTarihi;
        var theRangeEnd = rangeEnd ?? raporTarihi;
        
        // ImportBankaBakiyeAsync: Sadece Genel Kasa scope'ta çağrılır.
        // Sabah/Akşam'da bu metot eski publish'te yoktu — BankaBakiye direkt TryReadBankaBakiye ile okunuyordu.
        decimal bankaBakiye = 0m;
        BankaMismatchType bankaMismatch = BankaMismatchType.None;
        BankaBakiyeDiagnosticInfo bankaDiag = new();
        var isGenel = kasaScope != null && (kasaScope.Equals("Genel", StringComparison.OrdinalIgnoreCase) || kasaScope.Equals("GenelKasa", StringComparison.OrdinalIgnoreCase));
        if (isGenel)
        {
            (bankaBakiye, _, bankaMismatch, bankaDiag) = await ImportBankaBakiyeAsync(uploadFolderAbsolute, theRangeStart, theRangeEnd, issues, ct);
        }

        var bankaTahsilatGun = ReadBankaTahsilatGun(raporTarihi, uploadFolderAbsolute, issues, out var bankaTahsilatGunRawJson, rangeStart: rangeStart, rangeEnd: rangeEnd, fullExcelTotals: useFullExcel);
        var bankaExtraInflow = ReadBankaTahsilatExtraInflowAgg(raporTarihi, uploadFolderAbsolute, issues, out var bankaExtraRawJson, rangeStart: rangeStart, rangeEnd: rangeEnd, fullExcelTotals: useFullExcel);
        var bankaHarcGun = ReadBankaHarcGun(raporTarihi, uploadFolderAbsolute, issues, out var bankaHarcGunRawJson, rangeStart: rangeStart, rangeEnd: rangeEnd, fullExcelTotals: useFullExcel);

        // Mesai Sonu modunda bu 3 dosya atlanır (OnlineHarc, OnlineMasraf, MasrafveReddiyat)
        decimal onlineHarcamaTotal;
        string? onlineHarcRawJson;
        decimal onlineMasrafTotal;
        string? onlineMasrafRawJson;
        string? masrafReddiyatRawJson;
        MasrafReddiyatAgg masrafReddiyatAgg;

        if (mesaiSonuModu)
        {
            onlineHarcamaTotal = 0m;
            onlineHarcRawJson = null;
            onlineMasrafTotal = 0m;
            onlineMasrafRawJson = null;
            masrafReddiyatRawJson = null;
            masrafReddiyatAgg = new MasrafReddiyatAgg(0m, 0m, 0m, "{}");
            issues.Add("ℹ️ Mesai Sonu modu: OnlineHarc, OnlineMasraf ve MasrafveReddiyat dosyaları atlandı.");
        }
        else
        {
            onlineHarcamaTotal = ReadOnlineTotal(raporTarihi, uploadFolderAbsolute, "onlineHarc.xlsx", ImportFileKind.OnlineHarcama, issues, out onlineHarcRawJson, rangeStart: rangeStart, rangeEnd: rangeEnd, fullExcelTotals: useFullExcel);
            onlineMasrafTotal = ReadOnlineTotal(raporTarihi, uploadFolderAbsolute, "onlineMasraf.xlsx", ImportFileKind.OnlineMasraf, issues, out onlineMasrafRawJson, rangeStart: rangeStart, rangeEnd: rangeEnd, fullExcelTotals: useFullExcel);

            // MasrafveReddiyat: Unified Reader
            masrafReddiyatAgg = ReadMasrafReddiyatAggUnified(
                raporTarihi, 
                uploadFolderAbsolute, 
                issues, 
                out masrafReddiyatRawJson, 
                rangeStart: rangeStart, 
                rangeEnd: rangeEnd, 
                fullExcelTotals: useFullExcel);
        }
        // R15B parity kilitleri
        // R10.9 Doğruluk Yaması: Genel Kasa, KasaUstRapor (tek günlük) tahsilatı yerine, MasrafveReddiyat (tarih aralığı) üzerinden hesaplanan kümülatif sonucu kullanmalıdır.
        var toplamTahsilat = isGenel ? masrafReddiyatAgg.Masraf : ust.Tahsilat;
        var toplamReddiyat = isGenel ? masrafReddiyatAgg.Reddiyat : ust.Reddiyat;
        var onlineReddiyat = online.OnlineReddiyat;

        // NormalReddiyat = ToplamReddiyat - OnlineReddiyat (negatif beklenmez)
        var normalReddiyat = toplamReddiyat - onlineReddiyat;
        if (normalReddiyat < 0m)
        {
            issues.Add($"R15B UYARI: NormalReddiyat negatif çıktı (ToplamReddiyat={toplamReddiyat:N2} - OnlineReddiyat={onlineReddiyat:N2} = {normalReddiyat:N2}). Bu beklenmez; kaynak dosyaları kontrol edin.");
        }

        // NormalTahsilat = ToplamTahsilat (ham)
        var normalTahsilat = toplamTahsilat;

        // Override (kullanıcı) alanları: 0 ise hesaplamaya dahil edilmeyecek (flag)
        decimal ov(string? _, decimal? v) => v ?? 0m;
        // CanonicalKey tekilliği: UI'de mükerrer alan istemiyoruz.
        // Aynı CanonicalKey birden fazla kez eklenirse öncelik Raw > Derived > Override olacak şekilde "upsert" ederiz.
        var entriesByKey = new Dictionary<string, UnifiedPoolEntry>(StringComparer.OrdinalIgnoreCase);

        static int Priority(UnifiedPoolValueType t) => t == UnifiedPoolValueType.Raw ? 3 : t == UnifiedPoolValueType.Derived ? 2 : 1;

        void Upsert(UnifiedPoolEntry e)
        {
            if (string.IsNullOrWhiteSpace(e.CanonicalKey))
                return;

            if (!entriesByKey.TryGetValue(e.CanonicalKey, out var existing))
            {
                entriesByKey[e.CanonicalKey] = e;
                return;
            }

            // Daha yüksek öncelik varsa replace et.
            if (Priority(e.Type) > Priority(existing.Type))
            {
                entriesByKey[e.CanonicalKey] = e;
                return;
            }

            // Öncelik eşitse: ilk geleni koru (deterministik).
        }

        void AddRaw(string key, decimal value, string sourceName, string? sourceFile = null, string? details = null, string? notes = null)
        {
            Upsert(new UnifiedPoolEntry
            {
                CanonicalKey = key,
                Value = value.ToString("N2", CultureInfo.InvariantCulture),
                Type = UnifiedPoolValueType.Raw,
                IncludeInCalculations = true,
                SourceName = sourceName,
                SourceFile = sourceFile,
                SourceDetails = details,
                Notes = notes
            });
        }

        void AddOverride(string key, decimal value, string? notes = null)
        {
            Upsert(new UnifiedPoolEntry
            {
                CanonicalKey = key,
                Value = value.ToString("N2", CultureInfo.InvariantCulture),
                Type = UnifiedPoolValueType.Override,
                // Kullanıcı girişleri (UserInput) boş/0 olsa bile formül motoruna "mevcut" olarak girer.
                // Böylece "Missing variable" hatası oluşmaz; 0 değeri formülde doğal olarak etkisiz kalır.
                IncludeInCalculations = true,
                SourceName = "UserInput",
                SourceFile = null,
                SourceDetails = "KasaPreview manuel giriş",
                Notes = notes
            });
        }

        void AddDerived(string key, decimal value, string? notes = null)
        {
            Upsert(new UnifiedPoolEntry
            {
                CanonicalKey = key,
                Value = value.ToString("N2", CultureInfo.InvariantCulture),
                Type = UnifiedPoolValueType.Derived,
                IncludeInCalculations = true,
                SourceName = "DerivedFormula",
                SourceFile = null,
                SourceDetails = "R15B parity",
                Notes = notes
            });
        }

        // === Ayarlar'dan gelen override'lar (eski sistem parity) ===
        // Not: Kullanıcı bu değerleri Ayarlar ekranından yönetmeye devam edecek.
        // Burada sadece Formula Authoring için görünür yapıyoruz.
        var isGenelScope = isGenel; // isGenel yukarıda hesaplandı

        if (isGenelScope)
        {
            // GENEL KASA: SSOT mantığını kullan (devredenKasa = DetermineGenelKasaDevredenSsotAsync sonucu)
            var isSettingsOverrideActive = defaults.DefaultDundenDevredenKasaNakit.HasValue
                                          && defaults.DefaultDundenDevredenKasaNakit.Value != 0m;
            AddOverride("dunden_devreden_kasa_nakit", devredenKasa,
                isSettingsOverrideActive
                    ? $"Ayarlar override: {defaults.DefaultDundenDevredenKasaNakit!.Value:N2}"
                    : "Önceki Akşam snapshot fallback.");
            AddOverride(KasaCanonicalKeys.Devreden, devredenKasa,
                isSettingsOverrideActive
                    ? $"Ayarlar override: {defaults.DefaultDundenDevredenKasaNakit!.Value:N2}"
                    : "Önceki snapshot fallback.");
            AddOverride("genel_kasa_devreden_seed", devredenKasa,
                isSettingsOverrideActive
                    ? $"Ayarlar override: {defaults.DefaultGenelKasaDevredenSeed!.Value:N2}"
                    : "Önceki snapshot fallback (SSOT).");

            AddOverride("kasa_eksik_fazla", defaultKasaEksikFazla, "Ayarlar: Önceki günden devreden (+/-) bakiye / eksik-fazla (eski sistem)." );
            AddOverride(KasaCanonicalKeys.EksikFazla, defaultKasaEksikFazla, "Ayarlar: DefaultKasaEksikFazla");
            var kasaNakit = (defaults.DefaultNakitPara ?? 0m) + (defaults.DefaultBozukPara ?? 0m);
            AddOverride(KasaCanonicalKeys.KasaNakit, kasaNakit, "Ayarlar: DefaultNakitPara+DefaultBozukPara");
            AddOverride(KasaCanonicalKeys.GelmeyenD, finalizeInputs.GelmeyenD ?? 0m, "UI manuel");
        }
        else
        {
            // SABAH / AKŞAM KASA: Eski (legacy) davranışı koru — ayarlardan gelen veya DB'den çözülen değerleri kullan
            var overridesActive = defaultDundenDevredenKasaNakit > 0m;
            AddOverride("dunden_devreden_kasa_nakit", devredenKasa, overridesActive ? "Ayarlar override: Dünden devreden kasa nakit." : "Önceki hesaplama snapshot (Fallback)." );
            AddOverride("kasa_eksik_fazla", defaultKasaEksikFazla, "Ayarlar: Önceki günden devreden (+/-) bakiye / eksik-fazla (eski sistem)." );
        }

        // Minimal RAW set (Excel parity için kritik)
        AddRaw("toplam_tahsilat", toplamTahsilat, "KasaUstRaporSnapshot", "KasaUstRapor.xlsx", "TOPLAMLAR/Tahsilat");
        AddRaw("toplam_reddiyat", toplamReddiyat, "KasaUstRaporSnapshot", "KasaUstRapor.xlsx", "TOPLAMLAR/Reddiyat");
        AddRaw("online_reddiyat", onlineReddiyat, "OnlineReddiyat", "OnlineReddiyat.xlsx", "Net Ödenecek Miktar toplamı");

        // KasaUstRapor TOPLAMLAR kırılımları (HAM)
        AddRaw("pos_tahsilat", ust.PosTahsilat, "KasaUstRaporSnapshot", "KasaUstRapor.xlsx", "TOPLAMLAR/POS Tahsilat");
        AddRaw("online_tahsilat", ust.OnlineTahsilat, "KasaUstRaporSnapshot", "KasaUstRapor.xlsx", "TOPLAMLAR/Online Tahsilat");
        AddRaw("post_tahsilat", ust.PostTahsilat, "KasaUstRaporSnapshot", "KasaUstRapor.xlsx", "TOPLAMLAR/Post Tahsilat");

        AddRaw("toplam_harc", ust.Harc, "KasaUstRaporSnapshot", "KasaUstRapor.xlsx", "TOPLAMLAR/Harç (Normal/Toplam Harç)");
        AddRaw("pos_harc", ust.PosHarc, "KasaUstRaporSnapshot", "KasaUstRapor.xlsx", "TOPLAMLAR/POS Harç");
        AddRaw("online_harc", ust.OnlineHarc, "KasaUstRaporSnapshot", "KasaUstRapor.xlsx", "TOPLAMLAR/Online Harç");
        AddRaw("post_harc", ust.PostHarc, "KasaUstRaporSnapshot", "KasaUstRapor.xlsx", "TOPLAMLAR/Post Harç");
        AddRaw("gelmeyen_post", ust.GelmeyenPost, "KasaUstRaporSnapshot", "KasaUstRapor.xlsx", "TOPLAMLAR/Gelmeyen Post");

        // Gelir/Damga vergisi (HAM) – KasaUstRapor snapshot'ı
        AddRaw("gelir_vergisi", ust.GelirVergisi, "KasaUstRaporSnapshot", "KasaUstRapor.xlsx", "TOPLAMLAR/Gelir Vergisi");
        AddRaw("damga_vergisi", ust.DamgaVergisi, "KasaUstRaporSnapshot", "KasaUstRapor.xlsx", "TOPLAMLAR/Damga Vergisi");

        // Stopaj (HAM) – KasaUstRapor + OnlineReddiyat
        AddRaw("toplam_stopaj", ust.Stopaj, "KasaUstRaporSnapshot", "KasaUstRapor.xlsx", "TOPLAMLAR/Stopaj (toplam)");
        AddRaw("online_stopaj", online.OnlineStopaj, "OnlineReddiyat", "OnlineReddiyat.xlsx", "Gelir+Damga Vergisi toplamı (online)");

        // Banka (HAM) - Doğrudan Net Bakiye — SADECE Genel Kasa scope'ta
        if (isGenelScope)
        {
            AddRaw(KasaCanonicalKeys.BankaBakiye, bankaBakiye, "BankaTahsilat", "BankaTahsilat.xlsx", 
                $"{theRangeStart:dd.MM.yyyy}-{theRangeEnd:dd.MM.yyyy} bakiye hesaplaması", 
                notes: bankaDiag.FileExists 
                    ? (bankaMismatch != BankaMismatchType.None ? $"UYARI: Banka uyuşmazlığı tespit edildi ({bankaMismatch})" : null) 
                    : "MISSING_SOURCE: BankaTahsilat.xlsx okunamadı");
        }

        // Banka günlük giriş/çıkış (HAM)
        AddRaw(KasaCanonicalKeys.BankaGirenTahsilat, bankaTahsilatGun.Giren, "BankaTahsilatGun", "BankaTahsilat.xlsx", "Günlük banka giriş toplamı");
        AddRaw(KasaCanonicalKeys.BankaCikanTahsilat, bankaTahsilatGun.Cikan, "BankaTahsilatGun", "BankaTahsilat.xlsx", "Günlük banka çıkış toplamı");

        // BankaTahsilat.xlsx ek girişler (HAM) – yeni alanlar
        AddRaw(KasaCanonicalKeys.EftOtomatikIade, bankaExtraInflow.EftOtomatikIade, "BankaTahsilatExtra", "BankaTahsilat.xlsx", "İşlem Adı: Gelen EFT Otomatik Yatan (toplam)");
        AddRaw(KasaCanonicalKeys.GelenHavale, bankaExtraInflow.GelenHavale, "BankaTahsilatExtra", "BankaTahsilat.xlsx", "İşlem Adı: Gelen Havale (toplam)");
        AddRaw(KasaCanonicalKeys.IadeKelimesiGiris, bankaExtraInflow.IadeKelimesiGiris, "BankaTahsilatExtra", "BankaTahsilat.xlsx", "Açıklama/İşlem adında 'iade' geçen girişler (toplam)");
        AddRaw("islem_disi_yansiyan", bankaExtraInflow.IslemDisiToplam, "BankaTahsilatExtra", "BankaTahsilat.xlsx", "İşlem dışı yansıyan / özel filtreli girişler (toplam)");

        // Banka devreden/yarına devredecek (HAM) – referans değerler
        AddRaw(KasaCanonicalKeys.DundenDevredenBankaTahsilat, bankaTahsilatGun.Devreden, "BankaTahsilatGun", "BankaTahsilat.xlsx", "Önceki günden devreden banka bakiyesi (tahsilat)");
        AddRaw(KasaCanonicalKeys.YarinaDeverecekBankaTahsilat, bankaTahsilatGun.Yarina, "BankaTahsilatGun", "BankaTahsilat.xlsx", "Yarın devredecek banka bakiyesi (tahsilat)");

        AddRaw(KasaCanonicalKeys.BankaGirenHarc, bankaHarcGun.Giren, "BankaHarcGun", "BankaHarc.xlsx", "Günlük banka harç giriş toplamı");
        AddRaw(KasaCanonicalKeys.BankaCikanHarc, bankaHarcGun.Cikan, "BankaHarcGun", "BankaHarc.xlsx", "Günlük banka harç çıkış toplamı");

        AddRaw(KasaCanonicalKeys.DundenDevredenBankaHarc, bankaHarcGun.Devreden, "BankaHarcGun", "BankaHarc.xlsx", "Önceki günden devreden banka bakiyesi (harç)");
        AddRaw(KasaCanonicalKeys.YarinaDeverecekBankaHarc, bankaHarcGun.Yarina, "BankaHarcGun", "BankaHarc.xlsx", "Yarın devredecek banka bakiyesi (harç)");

        // Bankaya Yatırılacak Doğrulama — Mevduata Para Yatırma / Virman çıkışları (HAM)
        var tahsilatOutflow = ReadBankaOutflowByType(raporTarihi, uploadFolderAbsolute, "BankaTahsilat.xlsx", ImportFileKind.BankaTahsilat, issues, out _, rangeStart: rangeStart, rangeEnd: rangeEnd, fullExcelTotals: useFullExcel);
        var harcOutflow = ReadBankaOutflowByType(raporTarihi, uploadFolderAbsolute, "BankaHarc.xlsx", ImportFileKind.BankaHarcama, issues, out _, rangeStart: rangeStart, rangeEnd: rangeEnd, fullExcelTotals: useFullExcel);
        AddRaw("banka_mevduat_tahsilat", tahsilatOutflow.MevduatYatirma, "BankaTahsilatOutflow", "BankaTahsilat.xlsx", "İşlem Adı: Mevduata Para Yatırma (borç/çıkan)");
        AddRaw("banka_virman_tahsilat", tahsilatOutflow.Virman, "BankaTahsilatOutflow", "BankaTahsilat.xlsx", "İşlem Adı: Virman (borç/çıkan — stopaj)");
        AddRaw("banka_mevduat_harc", harcOutflow.MevduatYatirma, "BankaHarcOutflow", "BankaHarc.xlsx", "İşlem Adı: Mevduata Para Yatırma (borç/çıkan)");

        // Online masraf/harcama dosyaları (HAM)
        AddRaw("online_harcama", onlineHarcamaTotal, "OnlineHarcama", "onlineHarc.xlsx", "Gün toplamı (miktar)");
        AddRaw("online_masraf", onlineMasrafTotal, "OnlineMasraf", "onlineMasraf.xlsx", "Gün toplamı (miktar)");

        // Masraf & Reddiyat (True Source v2) (HAM)
        AddRaw("masraf", masrafReddiyatAgg.Masraf, "MasrafveReddiyat", "MasrafveReddiyat.xlsx", "Tip=Masraf toplamı");
        AddRaw("masraf_reddiyat", masrafReddiyatAgg.Reddiyat, "MasrafveReddiyat", "MasrafveReddiyat.xlsx", "Tip=Reddiyat toplamı");
        AddRaw("masraf_diger", masrafReddiyatAgg.Diger, "MasrafveReddiyat", "MasrafveReddiyat.xlsx", "Tip=Diğer toplamı");

        // Parity derived (şimdilik sadece 2 alan)
        AddDerived("normal_tahsilat", normalTahsilat, "KİLİTLİ: NormalTahsilat = ToplamTahsilat (ham)." );
        AddDerived("normal_reddiyat", normalReddiyat, "KİLİTLİ: NormalReddiyat = ToplamReddiyat - OnlineReddiyat." );

        // Parity derived (Stopaj yardımcı kilidi)
        var normalStopaj = ust.Stopaj - online.OnlineStopaj;
        if (normalStopaj < 0m)
        {
            issues.Add($"R15B UYARI: NormalStopaj negatif çıktı (ToplamStopaj={ust.Stopaj:N2} - OnlineStopaj={online.OnlineStopaj:N2} = {normalStopaj:N2}). 0'a clamp edildi; kaynak dosyaları kontrol edin.");
            normalStopaj = 0m;
        }
        // ORPHAN: AddDerived("normal_stopaj", normalStopaj, "KİLİTLİ: NormalStopaj = max(0, ToplamStopaj - OnlineStopaj).");

        // Override alanlar (genişletilebilir)

        // Contract-First: Kasada kalacak hedef (0 ise etkisiz). "Yatırılacak Tahsilat" auto düzeltmesi bu hedefe göre türetilir.
        // ORPHAN: AddOverride("kasada_kalacak_hedef", ov(null, finalizeInputs.KasadaKalacakHedef), "0 ise etkisiz (Kasada Kalacak Hedef)." );
        AddOverride("bankaya_yatirilacak_harci_degistir", ov(null, finalizeInputs.BankayaYatirilacakHarciDegistir), "+/- düzeltme (0 ise etkisiz)." );
        AddOverride("bankaya_yatirilacak_tahsilati_degistir", ov(null, finalizeInputs.BankayaYatirilacakTahsilatiDegistir), "+/- düzeltme (0 ise etkisiz)." );
        var defaultKayden = isGenel ? masrafReddiyatAgg.Diger : 0m;
        AddOverride("kayden_tahsilat", finalizeInputs.KaydenTahsilat ?? defaultKayden, isGenel ? "MasrafveReddiyat 'Kayden' (Diğer) toplamı." : "0 ise etkisiz.");
        AddOverride("kayden_harc", ov(null, finalizeInputs.KaydenHarc), "0 ise etkisiz." );
        // ORPHAN: AddOverride("bankadan_cekilen", ov(null, finalizeInputs.BankadanCekilen), "0 ise etkisiz." );
        AddOverride("cesitli_nedenlerle_bankadan_cikamayan_tahsilat", ov(null, finalizeInputs.CesitliNedenlerleBankadanCikamayanTahsilat), "0 ise etkisiz." );
        // ORPHAN: AddOverride("bankaya_gonderilmis_deger", ov(null, finalizeInputs.BankayaGonderilmisDeger), "0 ise etkisiz." );
        AddOverride("vergiden_gelen", ov(null, finalizeInputs.VergidenGelen), "0 ise etkisiz." );
        AddOverride("bozuk_para", ov(null, finalizeInputs.BozukPara), "0 ise etkisiz." );
        AddOverride("nakit_para", ov(null, finalizeInputs.NakitPara), "0 ise etkisiz." );
        AddOverride("vergi_kasa_bakiye_toplam", ov(null, finalizeInputs.VergiKasaBakiyeToplam), "Snapshot seçimine göre gelir (0 ise etkisiz)." );

        // Eksik/Fazla kullanıcı girişleri (Sabah Kasa)
        AddOverride("gune_ait_eksik_fazla_tahsilat", ov(null, finalizeInputs.GuneAitEksikFazlaTahsilat), "Kullanıcı girişi (0 ise etkisiz).");
        AddOverride("gune_ait_eksik_fazla_harc", ov(null, finalizeInputs.GuneAitEksikFazlaHarc), "Kullanıcı girişi (0 ise etkisiz).");
        AddOverride("dunden_eksik_fazla_tahsilat", ov(null, finalizeInputs.DundenEksikFazlaTahsilat), "Kullanıcı girişi (0 ise etkisiz).");
        AddOverride("dunden_eksik_fazla_harc", ov(null, finalizeInputs.DundenEksikFazlaHarc), "Kullanıcı girişi (0 ise etkisiz).");
        AddOverride("dunden_eksik_fazla_gelen_tahsilat", ov(null, finalizeInputs.DundenEksikFazlaGelenTahsilat), "Kullanıcı girişi (0 ise etkisiz).");
        AddOverride("dunden_eksik_fazla_gelen_harc", ov(null, finalizeInputs.DundenEksikFazlaGelenHarc), "Kullanıcı girişi (0 ise etkisiz).");

        // Contract-First (Akşam/Sabah parity):
        // - devreden_kasa: DetermineDevredenKasaAsync çıktısı (snapshot/ayar)
        // - vergi_kasa / vergi_bina_kasa: legacy rapordaki iki farklı başlık aynı kaynaktan (vergi_kasa_bakiye_toplam) beslenir
        // - yt_tahsilat_degistir_*: "Kasada Kalacak Hedef" kuralı için auto + manuel + effective
        // ORPHAN: AddDerived("devreden_kasa", devredenKasa, "KİLİTLİ: DevredenKasa = önceki Akşam GenelKasa (snapshot) veya ayar fallback.");

        var vergiKasaBakiyeToplam = ov(null, finalizeInputs.VergiKasaBakiyeToplam);
        if (vergiKasaBakiyeToplam != 0m)
        {
            AddDerived("vergi_kasa", vergiKasaBakiyeToplam, "Alias: vergi_kasa_bakiye_toplam");
            AddDerived("vergi_bina_kasa", vergiKasaBakiyeToplam, "Alias: vergi_kasa_bakiye_toplam");
        }

        // Auto tahsilat düzeltmesi: (Devreden + BankadanÇekilen + NormalStopaj + VergidenGelen + BankadanÇıkamayan - KaydenTahsilat) - KasadaKalacakHedef
        var kasadaKalacakHedef = ov(null, finalizeInputs.KasadaKalacakHedef);
        decimal ytAuto;
        if (kasadaKalacakHedef > 0m)
        {
            var bankadanCekilen = ov(null, finalizeInputs.BankadanCekilen);
            var vergidenGelen = ov(null, finalizeInputs.VergidenGelen);
            var bankadanCikamayan = ov(null, finalizeInputs.CesitliNedenlerleBankadanCikamayanTahsilat);
            var kaydenTahsilat = ov(null, finalizeInputs.KaydenTahsilat);

            var autoBase = devredenKasa + bankadanCekilen + normalStopaj + vergidenGelen + bankadanCikamayan - kaydenTahsilat;
            ytAuto = autoBase - kasadaKalacakHedef;
        }
        else
        {
            ytAuto = 0m;
        }
        AddDerived("yt_tahsilat_degistir_auto", ytAuto, kasadaKalacakHedef > 0m ? "KİLİTLİ: KasadaKalacakHedef kuralı (auto)." : "KasadaKalacakHedef=0 → auto etkisiz.");

        var ytManual = ov(null, finalizeInputs.BankayaYatirilacakTahsilatiDegistir);
        AddDerived("yt_tahsilat_degistir_manuel", ytManual, "Manuel tahsilat düzeltmesi (UI).");

        AddDerived("bankaya_yatirilacak_tahsilati_degistir_effective", ytAuto + ytManual, "Auto + Manuel (legacy parity).");

        // Notlar/uyarılar: UI'de görünmesi için özel satır olarak değil, controller ViewModel'e taşıyacağız.
        // Burada sadece consistency için issue listesi var.
        if (issues.Count > 0)
        {
            // Debug: Son kullanıcıya hatayı göstermek için.
            var masrafErr = issues.FirstOrDefault(x => x.Contains("MasrafveReddiyat"));
            if (!string.IsNullOrWhiteSpace(masrafErr))
            {
               // 0.00 değeri ile ekliyoruz ki sayısal alanda hata vermesin, notunda hatayı görsün.
               AddRaw("debug_masraf_error", 0m, "SİSTEM MESAJI", null, "Bu alan hata teşhisi içindir.", masrafErr);
            }
        }
        
        // Deterministik sıra: Raw -> Derived -> Override, sonra CanonicalKey
        var entries = entriesByKey.Values
            .OrderByDescending(e => Priority(e.Type))
            .ThenBy(e => e.CanonicalKey, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // R25: SlimPool - Kasa scope filtresi
        // Excel ham verileri HER ZAMAN dahil + seçilen kasanın alanları
        if (!skipSlimPoolFilter && !string.IsNullOrWhiteSpace(kasaScope))
        {
            var requiredKeys = FieldCatalog.GetRequiredKeysFor(kasaScope);
            entries = entries
                .Where(e => requiredKeys.Contains(e.CanonicalKey) || e.CanonicalKey.StartsWith("debug_"))
                .ToList();
        }

        return Result<IReadOnlyList<UnifiedPoolEntry>>.Success(entries);
    }


    private decimal? TryReadBankaBakiye(string uploadFolderAbsolute, List<string> issues, out string? rawJson)
    {
        rawJson = null;

        try
        {
            var full = Path.Combine(uploadFolderAbsolute, "BankaTahsilat.xlsx");
            if (!File.Exists(full))
            {
                issues.Add("BankaTahsilat.xlsx bulunamadı (wwwroot/Data/Raporlar içinde olmalı). Banka bakiye hesaplanamadı.");
                return null;
            }

            var imported = _import.Import(full, ImportFileKind.BankaTahsilat);
            if (!imported.Ok || imported.Value == null)
            {
                issues.Add($"BankaTahsilat import başarısız: {imported.Error}");
                return null;
            }

            var table = imported.Value;
            if (table.Rows.Count == 0)
            {
                issues.Add("BankaTahsilat boş (satır yok). Banka bakiye hesaplanamadı.");
                return null;
            }

            // son satır (tamamen boş satırlar varsa atla)
            Dictionary<string, string?>? last = null;
            for (int i = table.Rows.Count - 1; i >= 0; i--)
            {
                var r = table.Rows[i];
                if (r.Values.Any(v => !string.IsNullOrWhiteSpace(v)))
                {
                    last = r;
                    break;
                }
            }

            if (last == null)
            {
                issues.Add("BankaTahsilat içinde dolu satır bulunamadı. Banka bakiye hesaplanamadı.");
                return null;
            }

            rawJson = JsonSerializer.Serialize(last);

            var s = TryGet(last, "islem_sonrasi_bakiye");
            if (!Draft.Helpers.DecimalParsingHelper.TryParseFromJson(s, out var bakiye))
            {
                issues.Add("BankaTahsilat: 'islem_sonrasi_bakiye' kolonu okunamadı/parse edilemedi. Banka bakiye hesaplanamadı.");
                return null;
            }

            return bakiye;
        }
        catch (Exception ex)
        {
            issues.Add($"BankaTahsilat okuma hatası: {ex.Message}");
            return null;
        }
    }
}
