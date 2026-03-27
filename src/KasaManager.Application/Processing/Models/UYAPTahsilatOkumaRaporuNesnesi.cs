#nullable enable
using System.ComponentModel.DataAnnotations;

namespace KasaManager.Application.Processing.Models
{
    public class UYAPTahsilatOkumaRaporuNesnesi : BaseEntity
    {

        public string? BirimAdi { get; set; }
        public string? PersonelAdi { get; set; }
        public string? DosyaNo { get; set; }
        public string? DosyaTuru { get; set; }
        public string? TahsilatTuru { get; set; }
        public DateTime TahsilatTarihi { get; set; }
        public string? OdeyenKisi { get; set; }
        public string? BankaDurumu { get; set; }
        public string? MakbuzNo { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal YatirilanMiktar { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal HesaplanabilirMiktar { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal OdenebilirMiktar { get; set; }
        public string? EvrakiGoster { get; set; }
    }
}
