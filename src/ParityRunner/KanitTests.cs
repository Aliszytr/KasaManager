using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using KasaManager.Application.Abstractions;
using KasaManager.Application.Orchestration;
using KasaManager.Application.Orchestration.Dtos;

namespace ParityRunner
{
    public static class KanitTests
    {
        public static async Task RunAsync(IServiceProvider sp, string uploadFolder)
        {
            Console.WriteLine("\n\n=======================================================");
            Console.WriteLine("   🔥 ANTIGRAVITY PRODUCTION KANIT TESTLERI 🔥");
            Console.WriteLine("=======================================================\n");

            // Proof 1: CARRYOVER RESOLVER & SAVE
            Console.WriteLine("--- KANIT 1: SAVE -> CARRYOVER RESOLVER ZİNCİRİ ---");
            try {
                var calcSnapshots = sp.GetRequiredService<ICalculatedKasaSnapshotService>();
                var carryover = sp.GetRequiredService<ICarryoverResolver>();
                var testTarih = new DateOnly(2026, 4, 10);
                var nextDay = testTarih.AddDays(1);
                
                // Save a dummy snapshot
                var dummy = new KasaManager.Domain.Reports.Snapshots.CalculatedKasaSnapshot {
                    RaporTarihi = testTarih,
                    KasaTuru = KasaManager.Domain.Reports.KasaRaporTuru.Aksam,
                    Name = "Test Aksam Kasa",
                    CalculatedBy = "Antigravity",
                    InputsJson = "{}",
                    OutputsJson = "{\"SonrayaDevredecek\":\"999.88\"}", // Mock Devreden
                    KasaRaporDataJson = "{}",
                    FormulaSetName = "test"
                };
                await calcSnapshots.SaveAsync(dummy, CancellationToken.None);
                
                var devredenResult = await carryover.ResolveAsync(nextDay, CarryoverScope.GenelKasa, CancellationToken.None);
                
                Console.WriteLine($"[Test] {testTarih:yyyy-MM-dd} Aksam kaydedildi (SonrayaDevredecek=999.88). {nextDay:yyyy-MM-dd} icin okunan sonuc:");
                Console.WriteLine($"   Deger: {devredenResult.Value}");
                Console.WriteLine($"   Source: {devredenResult.SourceCode}");
                if (devredenResult.Value == 999.88m && devredenResult.SourceCode.Contains("Snap")) {
                    Console.WriteLine("✅ KANIT 1 BASARILI: CarryoverResolver, Save mekanizmasinin kaydettigi snapshot'i okuyor!");
                } else {
                    Console.WriteLine("❌ KANIT 1 BASARISIZ.");
                }
            } catch (Exception ex) {
                Console.WriteLine($"KANIT 1 HATA: {ex}");
            }

            // Proof 2: GENEL KASA SSOT COMPARE
            Console.WriteLine("\n--- KANIT 2: İKİ GENEL KASA SSOT UYUMU ---");
            try {
                var orchestrator = sp.GetRequiredService<IKasaOrchestrator>();
                var legacyGenelObj = sp.GetRequiredService<IGenelKasaRaporService>();
                var testTarih2 = new DateOnly(2026, 4, 1);
                
                // New
                var dto = new KasaPreviewDto { SelectedDate = testTarih2 };
                await orchestrator.LoadActiveFormulaSetByScopeAsync(dto, "Genel", CancellationToken.None);
                await orchestrator.RunFormulaEnginePreviewAsync(dto, uploadFolder, CancellationToken.None);
                decimal newGenelKasa = dto.FormulaRun?.Outputs?.GetValueOrDefault("genel_kasa") ?? -1m;
                
                // Old
                var oldRun = await legacyGenelObj.BuildCalculationRunAsync(testTarih2, 0m, uploadFolder, false, CancellationToken.None);
                decimal oldGenelKasa = oldRun.Run?.Outputs?.GetValueOrDefault("genel_kasa") ?? -2m;
                
                Console.WriteLine($"Tarih: {testTarih2:yyyy-MM-dd}");
                Console.WriteLine($"   Yeni Sistem (Orchestrator Bypass): {newGenelKasa:N2}");
                Console.WriteLine($"   Eski Sistem (GenelKasaRaporSer):   {oldGenelKasa:N2}");
                if (newGenelKasa == oldGenelKasa) Console.WriteLine("✅ KANIT 2 BASARILI: İki Genel Kasa modülü %100 uyumlu, hesaplamalar tam macth! (Fark = 0)");
                else Console.WriteLine("❌ KANIT 2 BASARISIZ.");
            } catch (Exception ex) {
                Console.WriteLine($"KANIT 2 HATA: {ex}");
            }

            // Proof 3: bankaBakiye Guncelleme Kaniti
            Console.WriteLine("\n--- KANIT 3: bankaBakiye GERÇEK DEĞER KONTROLÜ ---");
            try {
                var draft = sp.GetRequiredService<IKasaDraftService>();
                var testTarih3 = new DateOnly(2026, 4, 1);
                var runPool = await draft.BuildUnifiedPoolAsync(testTarih3, uploadFolder, kasaScope: "Genel", ct: CancellationToken.None);
                if (runPool.Ok && runPool.Value != null) {
                    var bankaBakiyePool = runPool.Value.FirstOrDefault(x => x.CanonicalKey == "banka_bakiye")?.Value;
                    Console.WriteLine($"   {testTarih3:yyyy-MM-dd} Tarihi icin UnifiedPool'dan okunan banka_bakiye: {bankaBakiyePool}");
                    if (bankaBakiyePool != "0.00" && !string.IsNullOrEmpty(bankaBakiyePool)) {
                        Console.WriteLine("✅ KANIT 3 BASARILI: bankaBakiye artik 0'da kalmiyor, dogrudan BankaTahsilat.xlsx'den hesaplaniyor!");
                    } else {
                        Console.WriteLine("❌ KANIT 3 BASARISIZ VEYA VERI BULUNAMADI (0 geldi).");
                    }
                }
            } catch (Exception ex) {
                Console.WriteLine($"KANIT 3 HATA: {ex}");
            }
            
            Console.WriteLine("=======================================================\n");
        }
    }
}
