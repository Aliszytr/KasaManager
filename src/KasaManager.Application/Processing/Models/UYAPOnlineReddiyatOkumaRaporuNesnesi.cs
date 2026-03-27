#nullable enable
using System.ComponentModel.DataAnnotations;

namespace KasaManager.Application.Processing.Models
{
    public class UYAPOnlineReddiyatOkumaRaporuNesnesi : BaseEntity
    {

        public string? birimAdi { get; set; }
        public string? personelAdi { get; set; }
        public string? dosyaNo { get; set; }
        public string? dosyaTuru { get; set; }
        public string? reddiyatNedeni { get; set; }
        public string? odeyecekKisi { get; set; }
        public DateTime reddiyatTarihi { get; set; }
        public string? makbuzNo { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal odenecekMiktar { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal gelirVergisi { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal damgaVergisi { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal netOdenecek { get; set; }
        public string? evrakiGoster { get; set; }
    }
}
