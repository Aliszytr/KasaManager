#nullable enable
using System.ComponentModel.DataAnnotations;

namespace KasaManager.Application.Processing.Models
{
    public class MahkemeEsasMiktarNesnesi : BaseEntity
    {

        public DateTime Tarih { get; set; }
        public string? Ili { get; set; }
        public string? Mahkemesi { get; set; }
        public string? VergiIdareDurumu { get; set; }
        public string? EsasNo { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal? Miktar { get; set; }



    }
}
