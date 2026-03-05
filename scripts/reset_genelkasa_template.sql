-- R21: GenelKasaSablonu Sıfırlama Script'i
-- Bu script eski şablonu siler, böylece uygulama başlatıldığında
-- yeni kapsamlı şablon (13 formül) otomatik oluşturulur.

-- Önce ilgili formül satırlarını sil
DELETE FROM PersistedFormulaLines 
WHERE SetId IN (
    SELECT Id FROM PersistedFormulaSets 
    WHERE Name = 'GenelKasaSablonu'
);

-- Sonra şablonu sil
DELETE FROM PersistedFormulaSets 
WHERE Name = 'GenelKasaSablonu';

-- Kontrol: Silinen kayıtları göster
SELECT @@ROWCOUNT AS 'Silinen Satır Sayısı';

PRINT 'GenelKasaSablonu başarıyla silindi.';
PRINT 'Şimdi uygulamayı başlatın (dotnet run) - yeni şablon otomatik oluşturulacak.';
