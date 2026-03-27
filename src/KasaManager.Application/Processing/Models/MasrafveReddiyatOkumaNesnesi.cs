#nullable enable
using System.ComponentModel.DataAnnotations;

namespace KasaManager.Application.Processing.Models
{
    public class MasrafveReddiyatOkumaNesnesi : BaseEntity
    {

        // Non-nullable strings: default to empty to avoid CS8618 warnings.
        public string Tip { get; set; } = string.Empty;
        public string BirimAdi { get; set; } = string.Empty;
        public string PersonelAdi { get; set; } = string.Empty;
        public string DosyaNo { get; set; } = string.Empty;
        public string DosyaTuru { get; set; } = string.Empty;
        public string TahsilatReddiyatTuru { get; set; } = string.Empty;
        [DataType(DataType.Date)]
        public DateTime TahsilatTarihi { get; set; }
        public string IlgiliKisi { get; set; } = string.Empty;
        public string MakbuzNo { get; set; } = string.Empty;
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal Miktar { get; set; }
        [DataType(DataType.Date)]
        public DateTime BaslangicTarihi { get; set; }
        [DataType(DataType.Date)]
        public DateTime BitisTarihi { get; set; }
        public string? EvrakiGoster { get; set; }


    }
}
