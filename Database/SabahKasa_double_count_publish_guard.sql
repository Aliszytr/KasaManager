-- Sabah Kasa Double-Count Publish Guard
-- Bu sorgu 0 satır dönmelidir. Eğer 0'dan fazla satır dönerse publish İPTAL edilmeli ve data patch uygulanmalıdır.

SELECT FormulaLineId, TargetKey, FormulaExpression
FROM FormulaLines
WHERE TargetKey = 'genel_kasa'
  AND FormulaExpression LIKE '%gune_ait_eksik_fazla_tahsilat%'
  AND FormulaExpression LIKE '%takip_kasa_etkisi_tahsilat%';
