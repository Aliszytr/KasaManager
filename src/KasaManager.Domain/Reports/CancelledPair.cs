#nullable enable
using System;

namespace KasaManager.Domain.Reports;

/// <summary>
/// İptal edilen bir banka işlemi ile orijinal işleminin eşleşme çifti.
/// MarkCancelledRecords algoritması tarafından oluşturulur.
/// İki tarih arasındaki süre hesaplanarak okunabilir <see cref="Aciklama"/> üretilir.
/// </summary>
public sealed record CancelledPair(
    /// <summary>Orijinal (Borç) işlemin banka kayıt listesindeki satır indexi</summary>
    int OrijinalRowIndex,
    /// <summary>İptal (Alacak) işlemin banka kayıt listesindeki satır indexi</summary>
    int IptalRowIndex,
    /// <summary>İptal edilen tutar (mutlak değer)</summary>
    decimal Tutar,
    /// <summary>Orijinal işlemin tarihi/saati</summary>
    DateTime OrijinalTarih,
    /// <summary>İptal işleminin tarihi/saati</summary>
    DateTime IptalTarihi,
    /// <summary>
    /// Okunabilir açıklama metni.
    /// Örn: "18.940,88 ₺ virman yapıldı, 4 dk sonra iptal edildi"
    /// </summary>
    string Aciklama,
    /// <summary>
    /// Orijinal işlemin tespit edilen türü (ör: "VIRMAN", "TAHSILAT").
    /// </summary>
    string Tur = ""
);
