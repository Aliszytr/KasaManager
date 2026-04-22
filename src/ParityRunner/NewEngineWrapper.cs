using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KasaManager.Application.Abstractions;
using KasaManager.Application.Orchestration;
using Microsoft.Extensions.DependencyInjection;

namespace ParityRunner;

public class NewEngineWrapper
{
    private readonly IServiceProvider _sp;
    
    public NewEngineWrapper(IServiceProvider sp)
    {
        _sp = sp;
    }
    
    public async Task<Dictionary<string, decimal>> ComputeAsync(
        DateOnly tarih,
        string hesapTuru,
        string uploadFolder,
        CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<IKasaOrchestrator>();
        
        var dto = new KasaManager.Application.Orchestration.Dtos.KasaPreviewDto
        {
            SelectedDate = tarih,
            KasaType = hesapTuru,
            DbScopeType = hesapTuru.Equals("Aksam", StringComparison.OrdinalIgnoreCase) ? "AksamKasa" : "SabahKasa"
        };
        
        await orchestrator.LoadActiveFormulaSetByScopeAsync(dto, dto.DbScopeType, ct);
        await orchestrator.RunFormulaEnginePreviewAsync(dto, uploadFolder, ct);
        
        var dict = new Dictionary<string, decimal>();
        
        // 1) Gercek FormulaEngine Sonuclarini ekle
        if (dto.FormulaRun?.Outputs != null)
        {
             foreach (var kv in dto.FormulaRun.Outputs)
             {
                  dict[kv.Key] = kv.Value;
             }
        }
        
        // 2) Karsilastirma icin Legacy Expected degerlerini draft Fields uzerinden ekle
        var draftBundle = dto.Drafts ?? new KasaManager.Application.Abstractions.KasaDraftBundle();
        var draftResult = hesapTuru.Equals("Aksam", StringComparison.OrdinalIgnoreCase) ? draftBundle.Aksam : draftBundle.Sabah;
        
        if (draftResult?.Fields != null)
        {
             foreach (var kv in draftResult.Fields)
             {
                  if (kv.Key.StartsWith("LEGACY_") || kv.Key == "vergi_kasa_bakiye_toplam" || kv.Key == "vergi_bina_kasa")
                  {
                      if (decimal.TryParse(kv.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d))
                      {
                           dict[kv.Key] = d;
                      }
                  }
             }
        }
        
        return dict;
    }
    
    private Dictionary<string, decimal> ExtractValues(KasaManager.Application.Abstractions.KasaDraftResult result)
    {
        var dict = new Dictionary<string, decimal>();

        decimal Read(string key)
        {
            var valueStr = result?.Fields?.GetValueOrDefault(key) ?? "0";
            if (decimal.TryParse(valueStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                 return parsed;
            return 0m;
        }
        
        dict["GenelKasa"] = Read("genel_kasa");
        dict["DevredenKasa"] = Read("dunden_devreden_kasa_nakit");
        dict["BankayaYatirilacakNakit"] = Read("bankaya_yatirilacak_tahsilat");
        dict["BankayaYatirilacakHarc"] = Read("bankaya_yatirilacak_harc");
        dict["BankaGoturulecekNakit"] = Read("bankaya_yatirilacak_toplam");
        dict["BozukParaHaricKasa"] = Read("bozuk_para_haric_kasa");
        dict["NormalTahsilat"] = Read("toplam_tahsilat");
        dict["NormalHarc"] = Read("toplam_harc");
        dict["NormalReddiyat"] = Read("normal_reddiyat");
        dict["NormalStopaj"] = Read("normal_stopaj");
        dict["OnlineReddiyat"] = Read("online_reddiyat");
        dict["OnlineTahsilat"] = Read("online_tahsilat");
        dict["KasadaKalacakHedef"] = Read("kasada_kalacak_hedef");
        dict["EksikFazla_OncekiGun"] = Read("eksikfazla_devreden_toplam");
        dict["EksikFazla_DundenDevreden"] = Read("eksikfazla_oncekigun_aciktahsilat"); 
        dict["EksikFazla_GuneAit"] = Read("eksikfazla_guneait_tahsilat"); 
        dict["VergiKasa_SelectionTotal"] = Read("vergi_kasa");
        
        if (result?.Fields != null)
        {
             foreach (var kvp in result.Fields)
             {
                  if (decimal.TryParse(kvp.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d))
                  {
                      dict[kvp.Key] = d;
                  }
             }
        }
        
        return dict;
    }
}
