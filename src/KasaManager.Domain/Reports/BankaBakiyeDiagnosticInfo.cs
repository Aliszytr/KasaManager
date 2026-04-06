#nullable enable
using System;

namespace KasaManager.Domain.Reports;

public class BankaBakiyeDiagnosticInfo
{
    public string? PathResolvedPath { get; set; }
    public bool FileExists { get; set; }
    public int MatchedRowCount { get; set; }
    public DateOnly? LastAvailableBalanceDate { get; set; }
    public DateOnly RequestedEndDate { get; set; }
    public DateOnly? SelectedBalanceDate { get; set; }
}
