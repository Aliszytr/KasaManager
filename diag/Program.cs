using Microsoft.Data.SqlClient;

var connStr = "Server=localhost;Database=KasaManager;Trusted_Connection=True;TrustServerCertificate=True";
using var conn = new SqlConnection(connStr);
conn.Open();

// Fix stuck record f22cbd20
using var cmd = conn.CreateCommand();
cmd.CommandText = @"
    UPDATE HesapKontrolKayitlari 
    SET Durum = 1, 
        CozulmeTarihi = '2026-03-04',
        Notlar = Notlar + CHAR(10) + '[07.03.2026 00:47] ✅ Aynı DosyaNo (2025/763) için daha yeni kayıt çözüldü — bu eski kayıt da otomatik kapatıldı. (Manuel düzeltme)'
    WHERE Id = 'f22cbd20-ee28-4fb7-a71d-b92e5f5de9b5' AND Durum = 0";
var rows = cmd.ExecuteNonQuery();
Console.WriteLine($"Updated {rows} row(s) — Stuck record f22cbd20 resolved.");

// Verify
using var cmd2 = conn.CreateCommand();
cmd2.CommandText = @"
    SELECT Durum, HesapTuru, Yon, COUNT(*) as Sayi, SUM(Tutar) as ToplamTutar
    FROM HesapKontrolKayitlari
    WHERE Durum = 0 AND HesapTuru = 1 AND Yon = 0
    GROUP BY Durum, HesapTuru, Yon";
using var r = cmd2.ExecuteReader();
if (!r.HasRows)
    Console.WriteLine("✅ Artık açık Harc Eksik kaydı yok!");
while (r.Read())
    Console.WriteLine($"  Kalan: Durum={r["Durum"]} | HT={r["HesapTuru"]} | Yon={r["Yon"]} | Sayı={r["Sayi"]} | Toplam={r["ToplamTutar"]}");
