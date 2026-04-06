using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using KasaManager.Application.Abstractions;
using KasaManager.Application.Services;
using KasaManager.Infrastructure.Excel;
using Xunit;
using Xunit.Abstractions;

namespace KasaManager.Tests.AdHoc;

public class ExcelInspectorTests
{
    private readonly ITestOutputHelper _out;

    public ExcelInspectorTests(ITestOutputHelper output)
    {
        _out = output;
    }

    [Fact]
    public void Inspect_MasrafVeReddiyat()
    {
        var path = @"D:\KasaManagerYeni\KasaYonetim\src\KasaManager.Web\wwwroot\Data\Raporlar\MasrafveReddiyat.xlsx";
        _out.WriteLine($"DOSYA: {path}");
        
        var reader = new ExcelDataReaderTableReader();
        var opt = ImportProfiles.GetTrueSourceOptions(KasaManager.Domain.Reports.ImportFileKind.MasrafVeReddiyat);
        
        var res = reader.ReadTable(path, opt);
        if (!res.Ok)
        {
            _out.WriteLine($"HATA: {res.Error}");
            return;
        }

        var table = res.Value!;
        _out.WriteLine($"OKUNAN SATIR SAYISI: {table.Rows.Count}");
        
        var tipCol = table.Columns.FirstOrDefault(c => c.Equals("tip", StringComparison.OrdinalIgnoreCase) || c.Equals("tur", StringComparison.OrdinalIgnoreCase) || c.Equals("tür", StringComparison.OrdinalIgnoreCase));
        
        _out.WriteLine($"TIP KOLONU BULUNDU MU: {tipCol}");
        
        if (tipCol != null)
        {
            var tips = table.Rows
                .Where(r => r != null && r.ContainsKey(tipCol))
                .Select(r => r[tipCol]?.Trim())
                .Where(x => !string.IsNullOrEmpty(x))
                .ToList();
                
            var distinctTips = tips.GroupBy(x => x).Select(g => new { Name = g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).Take(20);
            
            _out.WriteLine("================= İLK 20 DISTINCT TIP DEĞERİ =================");
            foreach(var t in distinctTips)
            {
                _out.WriteLine($"- {t.Name} (Adet: {t.Count})");
            }
        }
    }

    [Fact]
    public void Inspect_BankaTahsilat()
    {
        var path = @"D:\KasaManagerYeni\KasaYonetim\src\KasaManager.Web\wwwroot\Data\Raporlar\BankaTahsilat.xlsx";
        _out.WriteLine($"DOSYA: {path}");
        
        var reader = new ExcelDataReaderTableReader();
        var opt = ImportProfiles.GetTrueSourceOptions(KasaManager.Domain.Reports.ImportFileKind.BankaTahsilat);
        
        var res = reader.ReadTable(path, opt);
        if (!res.Ok)
        {
            _out.WriteLine($"HATA: {res.Error}");
            return;
        }

        var table = res.Value!;
        _out.WriteLine($"OKUNAN SATIR SAYISI: {table.Rows.Count}");
        
        _out.WriteLine("================= EŞLEŞEN KOLON İSİMLERİ =================");
        foreach(var col in table.Columns)
        {
            _out.WriteLine($"- CANONICAL: {col}");
        }

        var bakiyeCol = table.ColumnMetas.FirstOrDefault(c => c.CanonicalName.Contains("bakiye") || c.OriginalHeader.Contains("Bakiye"));
        _out.WriteLine($"BAKİYE KOLONU METADATA: {bakiyeCol?.CanonicalName} (Orjinal: {bakiyeCol?.OriginalHeader})");
        
        var lastRows = table.Rows.Skip(Math.Max(0, table.Rows.Count - 5)).ToList();
        _out.WriteLine("================= SON 5 SATIR VERİSİ =================");
        foreach(var r in lastRows)
        {
            _out.WriteLine(string.Join(" | ", r.Select(kvp => $"{kvp.Key}={kvp.Value}")));
        }
    }
}
