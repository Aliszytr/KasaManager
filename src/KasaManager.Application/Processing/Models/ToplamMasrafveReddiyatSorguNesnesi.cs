#nullable enable
using System.ComponentModel.DataAnnotations;

namespace KasaManager.Application.Processing.Models
{
    public class ToplamMasrafveReddiyatSorguNesnesi : BaseEntity
    {
        [DataType(DataType.Date)]
        public DateTime TahsilatTarihi { get; set; }
        [DataType(DataType.Date)]
        public DateTime RaporTarihi { get; set; }
        [DataType(DataType.Date)]
        public DateTime BaslangicTarihi { get; set; }
        [DataType(DataType.Date)]
        public DateTime BitisTarihi { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal TahsilatReddiyatFark { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal OncedenDevreden { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal SonrayaDevreden { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal BankaBakiye { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal KaydenTahsilat { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal KasaNakitMiktar { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal ToplamReddiyat { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal ToplamTahsilat { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal GenelKasaBakiyesi { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal Gelmeyen { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal EksikYadaFazla { get; set; }


    }
}
