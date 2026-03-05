using OfficeOpenXml;
using System;
using System.IO;
using System.Text.RegularExpressions;

ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

string raporlarPath = @"d:\KasaManager\KasaManager\km_new\src\KasaManager.Web\wwwroot\Data\Raporlar";

// ─────────────────────────────────────────────────────────────────────────
// 1. OnlineReddiyat.xlsx - Ödenecek Kişi kolonunu analiz et
// ─────────────────────────────────────────────────────────────────────────
Console.WriteLine("=".PadRight(100, '='));
Console.WriteLine("OnlineReddiyat.xlsx - ÖDENECEK KİŞİ ve TUTAR ANALİZİ");
Console.WriteLine("=".PadRight(100, '='));

string reddiyatPath = Path.Combine(raporlarPath, "OnlineReddiyat.xlsx");
var reddiyatData = new List<(string BirimAdi, string DosyaNo, string OdenecekKisi, decimal Tutar, string Tarih)>();

using (var package = new ExcelPackage(new FileInfo(reddiyatPath)))
{
    var ws = package.Workbook.Worksheets[0];
    int rowCount = ws.Dimension?.Rows ?? 0;
    int colCount = ws.Dimension?.Columns ?? 0;
    
    // Kolon indexlerini bul
    int birimIdx = -1, dosyaNoIdx = -1, odenecekKisiIdx = -1, tutarIdx = -1, tarihIdx = -1;
    for (int j = 1; j <= colCount; j++)
    {
        var h = (ws.Cells[1, j].Text ?? "").ToLowerInvariant();
        if (h.Contains("birim ad")) birimIdx = j;
        if (h.Contains("dosya no")) dosyaNoIdx = j;
        if (h.Contains("ödenecek kişi") || h.Contains("odenecek kisi")) odenecekKisiIdx = j;
        if (h.Contains("net ödenecek") || h.Contains("net odenecek")) tutarIdx = j;
        if (h.Contains("reddiyat tar")) tarihIdx = j;
    }
    
    Console.WriteLine($"Birim Adı idx: {birimIdx}, Dosya No idx: {dosyaNoIdx}, Ödenecek Kişi idx: {odenecekKisiIdx}");
    Console.WriteLine($"Net Ödenecek idx: {tutarIdx}, Tarih idx: {tarihIdx}");
    Console.WriteLine($"Toplam satır: {rowCount}\n");
    
    // Verileri oku
    for (int i = 2; i <= rowCount; i++)
    {
        var birim = birimIdx > 0 ? ws.Cells[i, birimIdx].Text : "";
        var dosya = dosyaNoIdx > 0 ? ws.Cells[i, dosyaNoIdx].Text : "";
        var odenecek = odenecekKisiIdx > 0 ? ws.Cells[i, odenecekKisiIdx].Text : "";
        var tutarText = tutarIdx > 0 ? ws.Cells[i, tutarIdx].Text?.Replace(".", "").Replace(",", ".") : "0";
        var tarih = tarihIdx > 0 ? ws.Cells[i, tarihIdx].Text : "";
        decimal.TryParse(tutarText, System.Globalization.NumberStyles.Any, 
            System.Globalization.CultureInfo.InvariantCulture, out var tutar);
        
        if (!string.IsNullOrWhiteSpace(birim) || !string.IsNullOrWhiteSpace(dosya))
            reddiyatData.Add((birim, dosya, odenecek, tutar, tarih));
    }
}

Console.WriteLine($"Toplam OnlineReddiyat kayıt: {reddiyatData.Count}\n");

// Ödenecek Kişi pattern analizi
Console.WriteLine("ÖDENECEK KİŞİ PATTERN ANALİZİ (rastgele 10 örnek):");
var random = new Random(42);
foreach (var item in reddiyatData.OrderBy(x => random.Next()).Take(10))
{
    Console.WriteLine($"  Birim: {item.BirimAdi}");
    Console.WriteLine($"  Dosya: {item.DosyaNo}");
    Console.WriteLine($"  Ödenecek: {item.OdenecekKisi}");
    Console.WriteLine($"  Tutar: {item.Tutar}");
    Console.WriteLine();
}

// ─────────────────────────────────────────────────────────────────────────
// 2. BankaTahsilat.xlsx - Borç kayıtlarını analiz et
// ─────────────────────────────────────────────────────────────────────────
Console.WriteLine("=".PadRight(100, '='));
Console.WriteLine("BankaTahsilat.xlsx - BORÇ KAYITLARI ANALİZİ");
Console.WriteLine("=".PadRight(100, '='));

string bankaPath = Path.Combine(raporlarPath, "BankaTahsilat.xlsx");
var bankaData = new List<(string Aciklama, decimal Tutar, string Tarih)>();

using (var package = new ExcelPackage(new FileInfo(bankaPath)))
{
    var ws = package.Workbook.Worksheets[0];
    int rowCount = ws.Dimension?.Rows ?? 0;
    int colCount = ws.Dimension?.Columns ?? 0;
    
    int tutarIdx = -1, aciklamaIdx = -1, tarihIdx = -1, borcAlacakIdx = -1;
    Console.WriteLine("BankaTahsilat Kolon Başlıkları (Ham):");
    for (int j = 1; j <= colCount; j++)
    {
        var rawHeader = ws.Cells[1, j].Text ?? "";
        Console.WriteLine($"  [{j}] '{rawHeader}'");
        
        var h = rawHeader.ToLowerInvariant().Trim();
        // Boşluk ve unicode karakterleri normalize et
        h = System.Text.RegularExpressions.Regex.Replace(h, @"\s+", " ");
        
        if (h.Contains("işlem tutarı") || h.Contains("islem tutari") || h.Contains("tutar")) tutarIdx = j;
        if (h.Contains("açıklama") || h.Contains("aciklama")) aciklamaIdx = j;
        if (h.Contains("işlem tarihi") || h.Contains("islem tarihi") || h.Contains("tarih")) tarihIdx = j;
        if (h.Contains("borç") || h.Contains("borc") || h.Contains("alacak")) borcAlacakIdx = j;
    }

    
    Console.WriteLine($"Tutar idx: {tutarIdx}, Açıklama idx: {aciklamaIdx}, Borç/Alacak idx: {borcAlacakIdx}\n");
    
    for (int i = 2; i <= rowCount; i++)
    {
        var borcAlacak = borcAlacakIdx > 0 ? ws.Cells[i, borcAlacakIdx].Text.ToLowerInvariant() : "";
        var tutarText = tutarIdx > 0 ? ws.Cells[i, tutarIdx].Text?.Replace(".", "").Replace(",", ".") : "0";
        decimal.TryParse(tutarText, System.Globalization.NumberStyles.Any, 
            System.Globalization.CultureInfo.InvariantCulture, out var tutar);
        
        bool isBorc = borcAlacak.Contains("borç") || borcAlacak.Contains("borc") || borcAlacak == "-" || tutar < 0;
        
        if (isBorc)
        {
            var aciklama = aciklamaIdx > 0 ? ws.Cells[i, aciklamaIdx].Text : "";
            var tarih = tarihIdx > 0 ? ws.Cells[i, tarihIdx].Text : "";
            bankaData.Add((aciklama, Math.Abs(tutar), tarih));
        }
    }
}

Console.WriteLine($"Toplam Borç kayıt: {bankaData.Count}\n");

// Borç kayıtlarını kategorize et
var reddiyatBorclar = bankaData.Where(b => 
    b.Aciklama.Contains("Mahkemesi-") &&
    !b.Aciklama.Contains("POSTA MASRAFI")).ToList();
    
var postaMasraflari = bankaData.Where(b => b.Aciklama.Contains("POSTA MASRAFI")).ToList();
var digerBorclar = bankaData.Except(reddiyatBorclar).Except(postaMasraflari).ToList();

Console.WriteLine($"Reddiyat (Mahkeme transferi): {reddiyatBorclar.Count}");
Console.WriteLine($"Posta Masrafı: {postaMasraflari.Count}");
Console.WriteLine($"Diğer: {digerBorclar.Count}\n");

Console.WriteLine("REDDİYAT (MAHKEME TRANSFERİ) ÖRNEKLERİ:");
foreach (var b in reddiyatBorclar.Take(10))
{
    Console.WriteLine($"  Tutar: {b.Tutar}");
    Console.WriteLine($"  Açıklama: {b.Aciklama}");
    Console.WriteLine();
}

// ─────────────────────────────────────────────────────────────────────────
// 3. EŞLEŞME ANALİZİ - Tutar bazlı potansiyel eşleşmeler
// ─────────────────────────────────────────────────────────────────────────
Console.WriteLine("=".PadRight(100, '='));
Console.WriteLine("TUTAR BAZLI EŞLEŞME ANALİZİ");
Console.WriteLine("=".PadRight(100, '='));

int matchCount = 0;
foreach (var banka in reddiyatBorclar.Take(5))
{
    Console.WriteLine($"\nBanka Borç: {banka.Tutar} TL");
    Console.WriteLine($"Açıklama: {banka.Aciklama.Substring(0, Math.Min(150, banka.Aciklama.Length))}...");
    
    // Açıklamadan mahkeme ve esas no parse et
    var mahkemeMatch = Regex.Match(banka.Aciklama, @"(Ankara\s+\d+\.\s*(İdare|Vergi|Asliye|Ceza)\s*Mahkemesi)-(\d{4}/\d+)");
    if (mahkemeMatch.Success)
    {
        Console.WriteLine($"  -> Parse: Gönderen={mahkemeMatch.Groups[1].Value}, Esas={mahkemeMatch.Groups[3].Value}");
    }
    
    // Aynı tutara sahip OnlineReddiyat kayıtlarını bul
    var candidates = reddiyatData.Where(r => r.Tutar == banka.Tutar).ToList();
    Console.WriteLine($"  -> Aynı tutarlı OnlineReddiyat: {candidates.Count} kayıt");
    
    if (candidates.Count > 0 && candidates.Count <= 5)
    {
        foreach (var c in candidates)
        {
            Console.WriteLine($"     * {c.BirimAdi} - {c.DosyaNo}");
        }
    }
}

Console.WriteLine("\n\n--- ANALİZ TAMAMLANDI ---");

