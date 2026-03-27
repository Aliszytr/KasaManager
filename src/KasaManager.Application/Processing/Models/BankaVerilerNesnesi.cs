#nullable enable
using System.ComponentModel.DataAnnotations;

namespace KasaManager.Application.Processing.Models
{
    public class BankaVerilerNesnesi : BaseEntity
    {
        [DataType(DataType.Date)]
        public DateTime IslemTarihi { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal IslemTutari { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal IslemSonrasiBakiye { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal DundenDevreden { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal YarinaDevrecek { get; set; }
        public string? IslemAdi { get; set; }
        public string? Aciklama { get; set; }
        public string? MIli { get; set; }
        public string? Mahkemesi { get; set; }
        public string? EsasNo { get; set; }
        public string? BorcAlacak { get; set; }
        public string? VergiIdare { get; set; }

    }
}
