#r "nuget: EPPlus, 6.2.10"
using OfficeOpenXml;
using System;
using System.IO;

ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

var onlineReddiyatPath = @"d:\KasaManager\KasaManager\km_new\src\KasaManager.Web\wwwroot\Data\Raporlar\OnlineReddiyat.xlsx";
var bankaTahsilatPath = @"d:\KasaManager\KasaManager\km_new\src\KasaManager.Web\wwwroot\Data\Raporlar\BankaTahsilat.xlsx";

Console.WriteLine("=== OnlineReddiyat.xlsx Analizi ===");
using (var package = new ExcelPackage(new FileInfo(onlineReddiyatPath)))
{
    var ws = package.Workbook.Worksheets[0];
    var rowCount = ws.Dimension?.Rows ?? 0;
    var colCount = ws.Dimension?.Columns ?? 0;
    
    Console.WriteLine($"Toplam satır: {rowCount}, Toplam sütun: {colCount}");
    Console.WriteLine("\nİlk 30 satır:");
    
    for (int i = 1; i <= Math.Min(30, rowCount); i++)
    {
        var row = "";
        for (int j = 1; j <= Math.Min(10, colCount); j++)
        {
            var val = ws.Cells[i, j].Text ?? "";
            if (!string.IsNullOrEmpty(val))
                row += $"{val} | ";
        }
        Console.WriteLine($"Satır {i}: {row}");
    }
}

Console.WriteLine("\n=== BankaTahsilat.xlsx Analizi (Borç kayıtları) ===");
using (var package = new ExcelPackage(new FileInfo(bankaTahsilatPath)))
{
    var ws = package.Workbook.Worksheets[0];
    var rowCount = ws.Dimension?.Rows ?? 0;
    var colCount = ws.Dimension?.Columns ?? 0;
    
    Console.WriteLine($"Toplam satır: {rowCount}, Toplam sütun: {colCount}");
    Console.WriteLine("\nİlk 30 satır:");
    
    for (int i = 1; i <= Math.Min(30, rowCount); i++)
    {
        var row = "";
        for (int j = 1; j <= Math.Min(10, colCount); j++)
        {
            var val = ws.Cells[i, j].Text ?? "";
            if (!string.IsNullOrEmpty(val))
                row += $"{val} | ";
        }
        Console.WriteLine($"Satır {i}: {row}");
    }
}
