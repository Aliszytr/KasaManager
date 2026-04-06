using System;
using System.Collections.Generic;
using KasaManager.Domain.Abstractions;

namespace KasaManager.Application.Abstractions;

public record KasaUstLiveSummary(
    decimal PosTahsilat,
    decimal OnlineTahsilat,
    decimal PostTahsilat,
    decimal Tahsilat,
    decimal Reddiyat,
    decimal PosHarc,
    decimal OnlineHarc,
    decimal PostHarc,
    decimal GelmeyenPost,
    decimal Harc,
    decimal GelirVergisi,
    decimal DamgaVergisi,
    decimal Stopaj);

public interface IIntermediateLiveUstRaporProvider
{
    Result<KasaUstLiveSummary> GetSummary(DateOnly raporTarihi, string uploadFolderAbsolute, List<string> issues);
}
