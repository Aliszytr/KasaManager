using KasaManager.Application.Orchestration.Dtos;
using KasaManager.Web.Models;

namespace KasaManager.Web.Helpers;

public static class KasaPreviewMapper
{
    public static KasaPreviewDto ToDto(this KasaPreviewViewModel vm)
    {
        return new KasaPreviewDto
        {
            // Primitives & State
            SelectedDate = vm.SelectedDate,
            KasaType = vm.KasaType ?? "",
            IsAdminMode = vm.IsAdminMode,
            AksamMesaiSonuModu = vm.AksamMesaiSonuModu,
            HasResults = vm.HasResults,
            IsDataLoaded = vm.IsDataLoaded,
            // P4.3: HasSnapshot removed

            // Genel Kasa Metadata
            GenelKasaStartDate = vm.GenelKasaStartDate,
            GenelKasaEndDate = vm.GenelKasaEndDate,
            GenelKasaStartDateSource = vm.GenelKasaStartDateSource,
            GenelKasaEndDateSource = vm.GenelKasaEndDateSource,
            DesignerStartDate = vm.DesignerStartDate,
            DesignerEndDate = vm.DesignerEndDate,
            InputCatalogScopeLabel = vm.InputCatalogScopeLabel,

            // Defaults
            VergiKasaBakiyeToplam = vm.VergiKasaBakiyeToplam,
            DefaultBozukPara = vm.DefaultBozukPara,
            DefaultNakitPara = vm.DefaultNakitPara,
            DefaultGenelKasaDevredenSeed = vm.DefaultGenelKasaDevredenSeed,
            DefaultGenelKasaBaslangicTarihiSeed = vm.DefaultGenelKasaBaslangicTarihiSeed,
            DefaultKaydenTahsilat = vm.DefaultKaydenTahsilat,
            DefaultDundenDevredenKasaNakit = vm.DefaultDundenDevredenKasaNakit,

            // User Inputs
            VergidenGelen = vm.VergidenGelen,
            GelmeyenD = vm.GelmeyenD,
            KasadaKalacakHedef = vm.KasadaKalacakHedef,
            KaydenTahsilat = vm.KaydenTahsilat,
            KaydenHarc = vm.KaydenHarc,
            BankadanCekilen = vm.BankadanCekilen,
            CesitliNedenlerleBankadanCikamayanTahsilat = vm.CesitliNedenlerleBankadanCikamayanTahsilat,
            BankayaGonderilmisDeger = vm.BankayaGonderilmisDeger,
            BankayaYatirilacakHarciDegistir = vm.BankayaYatirilacakHarciDegistir,
            BankayaYatirilacakTahsilatiDegistir = vm.BankayaYatirilacakTahsilatiDegistir,
            GuneAitEksikFazlaTahsilat = vm.GuneAitEksikFazlaTahsilat,
            GuneAitEksikFazlaHarc = vm.GuneAitEksikFazlaHarc,
            DundenEksikFazlaTahsilat = vm.DundenEksikFazlaTahsilat,
            DundenEksikFazlaHarc = vm.DundenEksikFazlaHarc,
            DundenEksikFazlaGelenTahsilat = vm.DundenEksikFazlaGelenTahsilat,
            DundenEksikFazlaGelenHarc = vm.DundenEksikFazlaGelenHarc,
            KasayiYapan = vm.KasayiYapan,
            Aciklama = vm.Aciklama,
            BozukPara = vm.BozukPara,
            NakitPara = vm.NakitPara,
            VergideBirikenKasa = vm.VergideBirikenKasa,

            // Formula Set
            DbFormulaSetId = vm.DbFormulaSetId,
            DbFormulaSetName = vm.DbFormulaSetName,
            DbScopeType = vm.DbScopeType,
            DbInfoMessage = vm.DbInfoMessage,
            SelectedFormulaSetId = vm.SelectedFormulaSetId,
            FormulaRun = vm.FormulaRun,
            Drafts = vm.Drafts,

            // Lists (shallow copy — acceptable since lists are recreated on each cycle)
            VeznedarOptions = vm.VeznedarOptions ?? new(),
            VergiKasaVeznedarlar = vm.VergiKasaVeznedarlar ?? new(),
            SelectedInputKeys = vm.SelectedInputKeys ?? new(),
            TargetKeySuggestions = vm.TargetKeySuggestions ?? new(),
            Warnings = vm.Warnings ?? new(),
            Errors = vm.Errors ?? new(),
            AvailableFormulaSets = vm.AvailableFormulaSets ?? new(),
            PoolEntries = vm.PoolEntries ?? new(),
            ParityDiffs = (vm.ParityDiffs ?? new()).Select(x => new KasaManager.Application.Orchestration.Dtos.ParityDiffItem
            {
                Scope = x.Scope,
                CanonicalKey = x.CanonicalKey,
                LegacyKey = x.LegacyKey,
                EngineKey = x.EngineKey,
                LegacyValue = x.LegacyValue,
                EngineValue = x.EngineValue,
                Delta = x.Delta,
                Note = x.Note,
                Status = MapParityStatusToDto(x.Status)
            }).ToList(),

            // Complex list types: Input Catalog
            InputCatalog = (vm.InputCatalog ?? new()).Select(x => new KasaManager.Application.Orchestration.Dtos.KasaInputCatalogEntry
            {
                Key = x.Key,
                IsFromUnifiedPool = x.IsFromUnifiedPool,
                IsVirtual = x.IsVirtual,
                ValueText = x.ValueText,
                Hint = x.Hint
            }).ToList(),

            // Complex list types: Mappings
            Mappings = (vm.Mappings ?? new()).Select(x => new KasaManager.Application.Orchestration.Dtos.KasaPreviewMappingRow
            {
                RowId = x.RowId,
                TargetKey = x.TargetKey,
                Mode = x.Mode,
                SourceKey = x.SourceKey,
                Expression = x.Expression,
                IsHidden = x.IsHidden
            }).ToList(),

            // Complex list types: DB Formula Sets
            DbFormulaSets = (vm.DbFormulaSets ?? new()).Select(x => new KasaManager.Application.Orchestration.Dtos.FormulaSetListItem
            {
                Id = x.Id,
                Name = x.Name,
                ScopeType = x.ScopeType,
                IsActive = x.IsActive,
                UpdatedAtUtc = x.UpdatedAtUtc
            }).ToList(),
        };
    }

    public static void UpdateFromDto(this KasaPreviewViewModel vm, KasaPreviewDto dto)
    {
        // Primitives & Simple Objects
        vm.SelectedDate = dto.SelectedDate;
        vm.KasaType = dto.KasaType ?? "";
        vm.IsAdminMode = dto.IsAdminMode;
        vm.AksamMesaiSonuModu = dto.AksamMesaiSonuModu;
        vm.HasResults = dto.HasResults;
        vm.GenelKasaStartDate = dto.GenelKasaStartDate;
        vm.GenelKasaEndDate = dto.GenelKasaEndDate;
        vm.GenelKasaStartDateSource = dto.GenelKasaStartDateSource;
        vm.GenelKasaEndDateSource = dto.GenelKasaEndDateSource;
        vm.DesignerStartDate = dto.DesignerStartDate;
        vm.DesignerEndDate = dto.DesignerEndDate;
        vm.InputCatalogScopeLabel = dto.InputCatalogScopeLabel;
        vm.IsDataLoaded = dto.IsDataLoaded;
        // P4.3: HasSnapshot removed
        vm.VergiKasaBakiyeToplam = dto.VergiKasaBakiyeToplam;
        vm.DefaultBozukPara = dto.DefaultBozukPara;
        vm.DefaultNakitPara = dto.DefaultNakitPara;
        vm.DefaultGenelKasaDevredenSeed = dto.DefaultGenelKasaDevredenSeed;
        vm.DefaultGenelKasaBaslangicTarihiSeed = dto.DefaultGenelKasaBaslangicTarihiSeed;
        vm.DefaultKaydenTahsilat = dto.DefaultKaydenTahsilat;
        vm.DefaultDundenDevredenKasaNakit = dto.DefaultDundenDevredenKasaNakit;
        vm.VergidenGelen = dto.VergidenGelen;
        vm.GelmeyenD = dto.GelmeyenD;
        vm.KasadaKalacakHedef = dto.KasadaKalacakHedef;
        vm.KaydenTahsilat = dto.KaydenTahsilat;
        vm.KaydenHarc = dto.KaydenHarc;
        vm.BankadanCekilen = dto.BankadanCekilen;
        vm.CesitliNedenlerleBankadanCikamayanTahsilat = dto.CesitliNedenlerleBankadanCikamayanTahsilat;
        vm.BankayaGonderilmisDeger = dto.BankayaGonderilmisDeger;
        vm.BankayaYatirilacakHarciDegistir = dto.BankayaYatirilacakHarciDegistir;
        vm.BankayaYatirilacakTahsilatiDegistir = dto.BankayaYatirilacakTahsilatiDegistir;
        vm.IptalEdilenCikisTutar = dto.IptalEdilenCikisTutar; // Commit 5
        vm.IptalEdilenVirmanTutar = dto.IptalEdilenVirmanTutar; // Commit 5.1
        vm.GuneAitEksikFazlaTahsilat = dto.GuneAitEksikFazlaTahsilat;
        vm.GuneAitEksikFazlaHarc = dto.GuneAitEksikFazlaHarc;
        vm.DundenEksikFazlaTahsilat = dto.DundenEksikFazlaTahsilat;
        vm.DundenEksikFazlaHarc = dto.DundenEksikFazlaHarc;
        vm.DundenEksikFazlaGelenTahsilat = dto.DundenEksikFazlaGelenTahsilat;
        vm.DundenEksikFazlaGelenHarc = dto.DundenEksikFazlaGelenHarc;
        vm.KasayiYapan = dto.KasayiYapan;
        vm.Aciklama = dto.Aciklama;
        vm.BozukPara = dto.BozukPara;
        vm.NakitPara = dto.NakitPara;
        vm.VergideBirikenKasa = dto.VergideBirikenKasa;
        vm.DbFormulaSetId = dto.DbFormulaSetId;
        vm.DbFormulaSetName = dto.DbFormulaSetName;
        vm.DbScopeType = dto.DbScopeType ?? "Custom";
        vm.DbInfoMessage = dto.DbInfoMessage;
        vm.SelectedFormulaSetId = dto.SelectedFormulaSetId;
        vm.FormulaRun = dto.FormulaRun;
        vm.Drafts = dto.Drafts;

        // Lists - Simple
        vm.VeznedarOptions = dto.VeznedarOptions ?? new();
        vm.VergiKasaVeznedarlar = dto.VergiKasaVeznedarlar ?? new();
        vm.SelectedInputKeys = dto.SelectedInputKeys ?? new();
        vm.TargetKeySuggestions = dto.TargetKeySuggestions ?? new();
        vm.Warnings = dto.Warnings ?? new();
        vm.Errors = dto.Errors ?? new();
        vm.AvailableFormulaSets = dto.AvailableFormulaSets ?? new();
        vm.PoolEntries = dto.PoolEntries ?? new(); // Shared type

        // Complex List Mapping (DTO -> ViewModel)
        
        // InputCatalog
        vm.InputCatalog = (dto.InputCatalog ?? new()).Select(x => new KasaManager.Web.Models.KasaInputCatalogEntry
        {
            Key = x.Key,
            IsFromUnifiedPool = x.IsFromUnifiedPool,
            IsVirtual = x.IsVirtual,
            ValueText = x.ValueText,
            Hint = x.Hint
        }).ToList();

        // Mappings
        vm.Mappings = (dto.Mappings ?? new()).Select(x => new KasaManager.Web.Models.KasaPreviewMappingRow
        {
            RowId = x.RowId ?? Guid.NewGuid().ToString("N"),
            TargetKey = x.TargetKey,
            Mode = x.Mode ?? "Formula",
            SourceKey = x.SourceKey,
            Expression = x.Expression,
            IsHidden = x.IsHidden
        }).ToList();

        // DbFormulaSets
        vm.DbFormulaSets = (dto.DbFormulaSets ?? new()).Select(x => new KasaManager.Web.Models.FormulaSetListItem
        {
            Id = x.Id ?? string.Empty,
            Name = x.Name ?? string.Empty,
            ScopeType = x.ScopeType ?? "Custom",
            IsActive = x.IsActive,
            UpdatedAtUtc = x.UpdatedAtUtc ?? string.Empty
        }).ToList();

         // ParityDiffs
        vm.ParityDiffs = (dto.ParityDiffs ?? new()).Select(x => new KasaManager.Web.Models.ParityDiffItem
        {
             Scope = x.Scope ?? string.Empty,
             CanonicalKey = x.CanonicalKey ?? string.Empty,
             LegacyKey = x.LegacyKey,
             EngineKey = x.EngineKey,
             LegacyValue = x.LegacyValue,
             EngineValue = x.EngineValue,
             Delta = x.Delta,
             Note = x.Note,
             Status = MapParityStatus(x.Status)
        }).ToList();
    }

    private static KasaManager.Web.Models.ParityDiffStatus MapParityStatus(KasaManager.Application.Orchestration.Dtos.ParityDiffStatus s)
    {
        return s switch
        {
            KasaManager.Application.Orchestration.Dtos.ParityDiffStatus.Same => KasaManager.Web.Models.ParityDiffStatus.Same,
            KasaManager.Application.Orchestration.Dtos.ParityDiffStatus.Different => KasaManager.Web.Models.ParityDiffStatus.Different,
            KasaManager.Application.Orchestration.Dtos.ParityDiffStatus.MissingInEngine => KasaManager.Web.Models.ParityDiffStatus.MissingInEngine,
            KasaManager.Application.Orchestration.Dtos.ParityDiffStatus.MissingInLegacy => KasaManager.Web.Models.ParityDiffStatus.MissingInLegacy,
            _ => KasaManager.Web.Models.ParityDiffStatus.NotComparable
        };
    }

    private static KasaManager.Application.Orchestration.Dtos.ParityDiffStatus MapParityStatusToDto(KasaManager.Web.Models.ParityDiffStatus s)
    {
        return s switch
        {
            KasaManager.Web.Models.ParityDiffStatus.Same => KasaManager.Application.Orchestration.Dtos.ParityDiffStatus.Same,
            KasaManager.Web.Models.ParityDiffStatus.Different => KasaManager.Application.Orchestration.Dtos.ParityDiffStatus.Different,
            KasaManager.Web.Models.ParityDiffStatus.MissingInEngine => KasaManager.Application.Orchestration.Dtos.ParityDiffStatus.MissingInEngine,
            KasaManager.Web.Models.ParityDiffStatus.MissingInLegacy => KasaManager.Application.Orchestration.Dtos.ParityDiffStatus.MissingInLegacy,
            _ => KasaManager.Application.Orchestration.Dtos.ParityDiffStatus.NotComparable
        };
    }
}
