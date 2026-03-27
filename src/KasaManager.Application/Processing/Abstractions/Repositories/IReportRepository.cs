namespace KasaManager.Application.Processing.Abstractions.Repositories;

public interface IReportRepository<T> where T : class
{
    /// <summary>Mevcut dataset'i tamamen değiştirir (Preview/Import sonrası set edilir).</summary>
    void ReplaceAll(IReadOnlyList<T> items);

    /// <summary>Dataset'in anlık snapshot'ı (read-only) döner.</summary>
    IReadOnlyList<T> GetAll();

    /// <summary>Dataset'i temizler.</summary>
    void Clear();
}
