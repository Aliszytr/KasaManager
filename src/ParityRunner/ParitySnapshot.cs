using System;
using System.Collections.Generic;

namespace ParityRunner;

public record ParitySnapshot
{
    public DateOnly Tarih { get; init; }
    public string HesapTuru { get; init; } = string.Empty; // "Sabah" veya "Aksam"
    public Dictionary<string, decimal> Sonuclar { get; init; } = new();
}
