-- ============================================================
-- Kasa Raporu CRUD Sistemi — Yeni Kolonlar
-- CalculatedKasaSnapshots tablosuna Name, Description, KasaRaporDataJson ekleniyor
-- Tarih: 2026-02-16
-- SQL Server uyumlu (IF NOT EXISTS guard)
-- ============================================================

-- Kullanıcı tarafından verilen rapor adı
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CalculatedKasaSnapshots') AND name = 'Name')
    ALTER TABLE CalculatedKasaSnapshots ADD Name NVARCHAR(200) NULL;

-- Kısa açıklama / rapor notu
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CalculatedKasaSnapshots') AND name = 'Description')
    ALTER TABLE CalculatedKasaSnapshots ADD Description NVARCHAR(500) NULL;

-- KasaRaporData DTO'sunun tam JSON'ı (ekrandaki TÜM veriler)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CalculatedKasaSnapshots') AND name = 'KasaRaporDataJson')
    ALTER TABLE CalculatedKasaSnapshots ADD KasaRaporDataJson NVARCHAR(MAX) NULL;
