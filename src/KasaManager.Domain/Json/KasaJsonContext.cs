#nullable enable
using System.Collections.Generic;
using System.Text.Json.Serialization;
using KasaManager.Domain.FormulaEngine;

namespace KasaManager.Domain.Json;

/// <summary>
/// R19: JSON Source Generator - Compile-time JSON serialization.
/// 
/// System.Text.Json Source Generator, runtime reflection yerine compile-time
/// code generation kullanarak JSON serialization/deserialization performansını
/// %30-50 oranında artırır.
/// 
/// Kullanım:
///   JsonSerializer.Serialize(data, KasaJsonContext.Default.DictionaryStringDecimal);
///   JsonSerializer.Deserialize(json, KasaJsonContext.Default.DictionaryStringDecimal);
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Dictionary<string, decimal>))]
[JsonSerializable(typeof(Dictionary<string, decimal?>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, string?>))]
[JsonSerializable(typeof(Dictionary<string, object?>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(List<decimal>))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(decimal[]))]
[JsonSerializable(typeof(FieldCatalogEntry))]
[JsonSerializable(typeof(List<FieldCatalogEntry>))]
public partial class KasaJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Indented (güzel formatlanmış) JSON için ayrı context.
/// Debug ve logging amaçlı kullanılır.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Dictionary<string, decimal>))]
[JsonSerializable(typeof(Dictionary<string, string?>))]
[JsonSerializable(typeof(Dictionary<string, object?>))]
public partial class KasaJsonContextIndented : JsonSerializerContext
{
}
