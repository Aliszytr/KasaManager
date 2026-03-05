-- =============================================
-- Banka Hesap Kontrol Modülü — Tablo Oluşturma
-- Tarih: 2026-02-12
-- Veritabanı: SQL Server
-- =============================================

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'HesapKontrolKayitlari')
BEGIN
    CREATE TABLE [HesapKontrolKayitlari] (
        [Id]                        UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        
        -- Ne Zaman
        [AnalizTarihi]              DATE NOT NULL,
        [OlusturmaTarihi]           DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        
        -- Hangi Hesap
        [HesapTuru]                 INT NOT NULL,       -- 0=Tahsilat, 1=Harc, 2=Stopaj
        
        -- Ne Tespit Edildi
        [Yon]                       INT NOT NULL,       -- 0=Eksik, 1=Fazla
        [Tutar]                     DECIMAL(18,2) NOT NULL,
        [Aciklama]                  NVARCHAR(1000) NULL,
        [DosyaNo]                   NVARCHAR(100) NULL,
        [BirimAdi]                  NVARCHAR(500) NULL,
        
        -- Sınıflandırma
        [Sinif]                     INT NOT NULL DEFAULT 2,   -- 0=Beklenen, 1=Askida, 2=Bilinmeyen
        [TespitEdilenTip]           NVARCHAR(200) NULL,
        
        -- Karşılaştırma Bağlantısı
        [KarsilastirmaSatirIndex]   INT NULL,
        [KarsilastirmaTuru]         NVARCHAR(100) NULL,
        
        -- Yaşam Döngüsü
        [Durum]                     INT NOT NULL DEFAULT 0,   -- 0=Acik, 1=Cozuldu, 2=Onaylandi, 3=Iptal
        [CozulmeTarihi]             DATE NULL,
        [CozulmeKaynakId]           UNIQUEIDENTIFIER NULL,
        
        -- Kullanıcı Etkileşimi
        [KullaniciOnay]             BIT NOT NULL DEFAULT 0,
        [OnaylayanKullanici]        NVARCHAR(200) NULL,
        [OnayTarihi]                DATETIME2 NULL,
        [Notlar]                    NVARCHAR(2000) NULL,
        
        -- Audit
        [CreatedBy]                 NVARCHAR(200) NULL
    );

    PRINT 'Tablo HesapKontrolKayitlari oluşturuldu.';
END
ELSE
BEGIN
    PRINT 'Tablo HesapKontrolKayitlari zaten mevcut.';
END
GO

-- Sorgulama performansı için indexler
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_HesapKontrol_Tarih_Hesap_Durum')
BEGIN
    CREATE INDEX [IX_HesapKontrol_Tarih_Hesap_Durum]
        ON [HesapKontrolKayitlari] ([AnalizTarihi], [HesapTuru], [Durum]);
    PRINT 'Index IX_HesapKontrol_Tarih_Hesap_Durum oluşturuldu.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_HesapKontrol_Durum')
BEGIN
    CREATE INDEX [IX_HesapKontrol_Durum]
        ON [HesapKontrolKayitlari] ([Durum]);
    PRINT 'Index IX_HesapKontrol_Durum oluşturuldu.';
END
GO
