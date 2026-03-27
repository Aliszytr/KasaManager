#nullable enable
using System.ComponentModel.DataAnnotations;

namespace KasaManager.Application.Processing.Models
{
    public class TahsilatHesapKontrolSonuclariNesnesi : BaseEntity
    {
        [DataType(DataType.Date)]
        public DateTime IslemTarihi { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal IslemTutari { get; set; }
        public string? SonucListe { get; set; }
        public string? MIli { get; set; }
        public string? Mahkemesi { get; set; }
        public string? EsasNo { get; set; }
        public string? VergiIdare { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal Toplam { get; set; }
        public int? Adet { get; set; }
        public string? Baslik { get; set; }
        public string? ResultList { get; set; }

    }
}
