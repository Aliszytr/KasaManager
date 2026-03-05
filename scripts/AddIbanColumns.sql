-- ═══════════════════════════════════════════════════════════════
-- KasaGlobalDefaultsSettings: IBAN Sütunları Ekleme
-- Tarih: 2026-02-13
-- Açıklama: Bankaya yatırılacak hesaplar için IBAN numaraları
-- Idempotent: Sütun yoksa ekler, varsa atlar.
-- ═══════════════════════════════════════════════════════════════

IF COL_LENGTH('KasaGlobalDefaultsSettings', 'HesapAdiStopaj') IS NULL
    ALTER TABLE KasaGlobalDefaultsSettings ADD HesapAdiStopaj NVARCHAR(200) NULL;
GO

IF COL_LENGTH('KasaGlobalDefaultsSettings', 'IbanStopaj') IS NULL
    ALTER TABLE KasaGlobalDefaultsSettings ADD IbanStopaj NVARCHAR(34) NULL;
GO

IF COL_LENGTH('KasaGlobalDefaultsSettings', 'HesapAdiMasraf') IS NULL
    ALTER TABLE KasaGlobalDefaultsSettings ADD HesapAdiMasraf NVARCHAR(200) NULL;
GO

IF COL_LENGTH('KasaGlobalDefaultsSettings', 'IbanMasraf') IS NULL
    ALTER TABLE KasaGlobalDefaultsSettings ADD IbanMasraf NVARCHAR(34) NULL;
GO

IF COL_LENGTH('KasaGlobalDefaultsSettings', 'HesapAdiHarc') IS NULL
    ALTER TABLE KasaGlobalDefaultsSettings ADD HesapAdiHarc NVARCHAR(200) NULL;
GO

IF COL_LENGTH('KasaGlobalDefaultsSettings', 'IbanHarc') IS NULL
    ALTER TABLE KasaGlobalDefaultsSettings ADD IbanHarc NVARCHAR(34) NULL;
GO

IF COL_LENGTH('KasaGlobalDefaultsSettings', 'IbanPostaPulu') IS NULL
    ALTER TABLE KasaGlobalDefaultsSettings ADD IbanPostaPulu NVARCHAR(34) NULL;
GO

PRINT '✅ IBAN sütunları eklendi (veya zaten mevcuttu).';
GO
