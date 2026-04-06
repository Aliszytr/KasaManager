namespace KasaManager.Domain.Reports;

public enum BankaMismatchType
{
    None = 0,
    SourceDateMissing = 1,
    SourceDateOlderThanRequested = 2,
    SourceDateNewerThanRequested = 3,
    FileMissing = 4,
    PathResolveFailed = 5,
    NoEligibleRowFound = 6
}
