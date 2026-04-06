using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using KasaManager.Infrastructure.Persistence;
using KasaManager.Infrastructure.Services;
using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Reports;

namespace ProofGenerator
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var options = new DbContextOptionsBuilder<KasaManagerDbContext>()
                .UseSqlServer("Server=localhost;Database=KasaManager;Trusted_Connection=True;TrustServerCertificate=True")
                .Options;

            using var db = new KasaManagerDbContext(options);
            var service = new FactNormalizationService(db, NullLogger<FactNormalizationService>.Instance);

            var date = new DateOnly(2026, 4, 30);
            
            // Dummy Table
            var table = new ImportedTable
            {
                SourceFileName = "BankaTahsilat_Fake.xlsx",
                Kind = ImportFileKind.BankaTahsilat,
                Rows = new List<Dictionary<string, string?>>
                {
                    new Dictionary<string, string?> { { "islem_tarihi", "30.04.2026" }, { "islem_adi", "Gelen Havale" }, { "tutar", "15000.50" }, { "aciklama", "Kira Bedeli" } },
                    new Dictionary<string, string?> { { "islem_tarihi", "30.04.2026" }, { "islem_adi", "Aidat" }, { "tutar", "500" }, { "aciklama", "Nisan Aidat" } }
                }
            };
            
            Console.WriteLine("--- RUN 1: INSERT ---");
            await service.NormalizeAndSaveShadowFactsAsync(table, date, "C:\\TestPath\\BankaTahsilat_Fake.xlsx");
            await DumpDb(db, date);

            Console.WriteLine("--- RUN 2: IDEMPOTENCY TEST (SAME FILE) ---");
            // Due to same file name and we don't have file on disk, ComputeHash will fallback to same value if we create a file.
            System.IO.File.WriteAllText("BankaTahsilat_Fake.xlsx", "test");
            await service.NormalizeAndSaveShadowFactsAsync(table, date, "BankaTahsilat_Fake.xlsx");
            await DumpDb(db, date);
            
            Console.WriteLine("--- RUN 3: IDEMPOTENCY TEST (CHANGED FILE) ---");
            System.IO.File.WriteAllText("BankaTahsilat_Fake.xlsx", "changed_test");
            
            table.Rows.Add(new Dictionary<string, string?> { { "islem_tarihi", "30.04.2026" }, { "islem_adi", "Yeni Satir" }, { "tutar", "90" }, { "aciklama", "Guncellenmis excel" } });
            await service.NormalizeAndSaveShadowFactsAsync(table, date, "BankaTahsilat_Fake.xlsx");
            await DumpDb(db, date);
        }
        
        static async Task DumpDb(KasaManagerDbContext db, DateOnly date)
        {
            var batches = await db.ImportBatches.Where(x => x.TargetDate == date).ToListAsync();
            var facts = await db.DailyFacts.Where(x => x.ForDate == date).ToListAsync();
            
            Console.WriteLine($"[DB STATE] Batches: {batches.Count}, Facts: {facts.Count}");
            foreach (var b in batches) {
                Console.WriteLine($"  Batch: {b.Id} | Hash: {b.FileHash}");
            }
            foreach (var f in facts) {
                Console.WriteLine($"  Fact: {f.CanonicalKey} | Raw: {f.RawValue} | Num: {f.NumericValue}");
            }
            Console.WriteLine("");
        }
    }
}
