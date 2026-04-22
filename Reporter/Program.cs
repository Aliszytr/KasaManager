using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using KasaManager.Infrastructure.Persistence;
using KasaManager.Domain.Calculation.Data;
using KasaManager.Domain.Reports;

class Program {
    static void Main(string[] args) {
        var options = new DbContextOptionsBuilder<KasaManagerDbContext>()
            .UseSqlServer("Server=.;Database=KasaManager;Trusted_Connection=True;TrustServerCertificate=True;")
            .Options;
        
        using var db = new KasaManagerDbContext(options);
        
        var date = new DateOnly(2026, 4, 7);
        var res = db.DailyCalculationResults.Where(x => x.ForDate == date).ToList();
        
        foreach (var r in res) {
            Console.WriteLine($"--- {r.KasaTuru} (v{r.CalculatedVersion}) ---");
            Console.WriteLine(r.ResultsJson);
            Console.WriteLine();
        }

        var hesap = db.HesapKontrolKayitlari.Where(x => x.AnalizTarihi == date).ToList();
        Console.WriteLine("--- HESAP KONTROL KAYITLARI ---");
        foreach(var hk in hesap) {
            Console.WriteLine($"{hk.HesapTuru} | Durum: {hk.Durum} | Yon: {hk.Yon} | Tutar: {hk.Tutar} | Not: {hk.Aciklama} | Sinif: {hk.Sinif}");
        }
    }
}
