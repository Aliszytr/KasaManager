using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using KasaManager.Infrastructure.Persistence;

var services = new ServiceCollection();
services.AddDbContext<KasaManagerDbContext>(options =>
    options.UseSqlServer("Server=localhost;Database=KasaManager;Trusted_Connection=True;TrustServerCertificate=True"));

var sp = services.BuildServiceProvider();
var db = sp.GetRequiredService<KasaManagerDbContext>();

var latestDcr = db.DailyCalculationResults.Where(r => r.ForDate == new DateOnly(2026, 4, 7) && r.KasaTuru == "Aksam").OrderByDescending(r => r.Id).FirstOrDefault();
if (latestDcr != null) {
    Console.WriteLine($"Test 4 DCR Kontrol:");
    Console.WriteLine($"Id: {latestDcr.Id}, ForDate: {latestDcr.ForDate}, KasaTuru: {latestDcr.KasaTuru}");
    Console.WriteLine($"sonraki_kasaya_devredecek var mi?: {latestDcr.ResultsJson.Contains("\\\"sonraki_kasaya_devredecek\\\"")}");
}

var latestSnap = db.CalculatedKasaSnapshots.Where(r => r.RaporTarihi == new DateOnly(2026, 4, 7) && r.KasaTuru == KasaManager.Domain.Enums.KasaRaporTuru.Aksam).OrderByDescending(r => r.Id).FirstOrDefault();
if (latestSnap != null) {
    Console.WriteLine($"Test 4 Snapshot Kontrol:");
    Console.WriteLine($"Id: {latestSnap.Id}, ForDate: {latestSnap.RaporTarihi}, KasaTuru: {latestSnap.KasaTuru}");
    Console.WriteLine($"sonraki_kasaya_devredecek var mi?: {latestSnap.OutputsJson.Contains("\\\"sonraki_kasaya_devredecek\\\"")}");
    Console.WriteLine($"deger: " + latestSnap.OutputsJson.Substring(latestSnap.OutputsJson.IndexOf("\\\"sonraki_kasaya_devredecek\\\"\"), 50));
}

