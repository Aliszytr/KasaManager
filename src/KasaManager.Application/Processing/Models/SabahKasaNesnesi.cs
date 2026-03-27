#nullable enable
using System.ComponentModel.DataAnnotations;

namespace KasaManager.Application.Processing.Models
{
    public class SabahKasaNesnesi : BaseEntity
    {
        [DataType(DataType.Date)]
        public DateTime IslemTarihiTahsilatSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal DundenDevredenBankaTahsilatSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal YarinaDeverecekBankaTahsilatSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal BankaGirenTahsilatSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal BankaCikanTahsilatSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal BankaCekilenTahsilatSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal DundenDevredenBankaHarcSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal YarinaDeverecekBankaHarcSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal BankaGirenHarcSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal BankaCikanHarcSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal BankaCekilenHarcSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal GelmeyenTahsilatSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal GelmeyenHarcSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal DundenEksikFazlaTahsilatSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal GuneAitEksikFazlaTahsilatSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal DundenEksikFazlaGelenTahsilatSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal DundenEksikFazlaHarcSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal GuneAitEksikFazlaHarcSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal DundenEksikFazlaGelenHarcSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal NormalTahsilatSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal OnlineTahsilatSabahK { get; set; }

        public decimal PostTahsilat { get; set; }
        public decimal GelmeyenPost { get; set; }

        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal OnlineStopajSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal OnlineReddiyatSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal KaydenHarcSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal NormalHarcSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal OnlineHarcSabahK { get; set; }
        public decimal PostHarc { get; set; }
        public decimal OnlineMasraf { get; set; }

        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal NormalStopajSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal NormalReddiyatSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal KaydenTahsilatSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal GenelKasaArtiEksiSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal UyapBakiyeSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal DundenDevredenKasaSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal GenelKasaSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal BozukParaHaricKasaSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal VergiKasaSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal VergiGelenKasaSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal CNBankadanCikamayanTahsilatSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal CNBankadanCikamayanHarcSabahK { get; set; }
        public string? KasayiYapanSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal BankayaYatirilacakNakitSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal BankayaYatirilacakStopajSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal BankayaYatirilacakHarcSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal GelenEFTIadeSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal BankayaYatirilacakHarciDegistirSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal BankayaYatirilacakTahsilatiDegistirSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal BToplamYATANSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal StopajKontrolSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal ToplamStopajSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal ToplamReddiyatSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal OncekiGunDevirKasaSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal DuneAitGelenEksikFazlaTahsilatSabahK { get; set; }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal DuneAitGelenEksikFazlaHarcSabahK { get; set; }


        // ---------------------------------------------------------------------
        // R14F (Sabah Kasa): İsim standardizasyonu / parity
        //
        // UI ve formül önizleme tarafında daha okunabilir isimlerle alanları
        // kullanabilmek için, mevcut "...SabahK" alanlarını alias property
        // olarak dışarıya açıyoruz.
        //
        // Not: DB Safe yaklaşımı korunur. Bu sınıf şu anda migration'a bağlı
        // bir EF schema değişikliği üretmez (çalışan yapı bozulmasın).
        // ---------------------------------------------------------------------

        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal DundenEksikYadaFazlaTahsilat
        {
            get => DundenEksikFazlaTahsilatSabahK;
            set => DundenEksikFazlaTahsilatSabahK = value;
        }

        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal GuneAitEksikYadaFazlaTahsilat
        {
            get => GuneAitEksikFazlaTahsilatSabahK;
            set => GuneAitEksikFazlaTahsilatSabahK = value;
        }

        /// <summary>
        /// "Önceki güne ait" olarak UI'de gösterilecek alan.
        /// Mevcut isimlendirmede "Dünden Eksik/Fazla Gelen" ile eşlenmiştir.
        /// </summary>
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal OncekiGuneAitEksikYadaFazlaTahsilat
        {
            get => DundenEksikFazlaGelenTahsilatSabahK;
            set => DundenEksikFazlaGelenTahsilatSabahK = value;
        }

        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal DundenEksikYadaFazlaHarc
        {
            get => DundenEksikFazlaHarcSabahK;
            set => DundenEksikFazlaHarcSabahK = value;
        }

        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal GuneAitEksikYadaFazlaHarc
        {
            get => GuneAitEksikFazlaHarcSabahK;
            set => GuneAitEksikFazlaHarcSabahK = value;
        }

        /// <summary>
        /// "Önceki güne ait" olarak UI'de gösterilecek Harç versiyonu.
        /// Mevcut isimlendirmede "Dünden Eksik/Fazla Gelen" ile eşlenmiştir.
        /// </summary>
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal OncekiGuneAitEksikYadaFazlaHarc
        {
            get => DundenEksikFazlaGelenHarcSabahK;
            set => DundenEksikFazlaGelenHarcSabahK = value;
        }
        [DisplayFormat(DataFormatString = "{0:#,##0.00}", ApplyFormatInEditMode = true)]
        public decimal BozukPara { get; set; }
        public string? AciklamaSabahK { get; set; }

    }
}
