#nullable enable
using System.ComponentModel.DataAnnotations;

namespace KasaManager.Application.Processing.Models
{
    public class UYAPOnlineReddiyatNesnesi : BaseEntity
    {

        public string? BirimAdi { get; set; }
        public string? MIli { get; set; }
        public string? Mahkemesi { get; set; }
        public string? EsasNo { get; set; }
        public string? VergiIdare { get; set; }
        public DateTime TahsilatTarihi { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal Miktar { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal GelirVergisi { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal DamgaVergisi { get; set; }


    }
}
