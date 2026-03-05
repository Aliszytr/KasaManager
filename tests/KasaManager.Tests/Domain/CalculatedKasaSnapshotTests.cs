using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.Snapshots;
using Xunit;

namespace KasaManager.Tests.Domain;

/// <summary>
/// R17: CalculatedKasaSnapshot entity unit testleri.
/// </summary>
public class CalculatedKasaSnapshotTests
{
    [Fact]
    public void SetInputs_And_GetInputs_Works()
    {
        // Arrange
        var snapshot = new CalculatedKasaSnapshot
        {
            RaporTarihi = DateOnly.FromDateTime(DateTime.Today),
            KasaTuru = KasaRaporTuru.Aksam
        };
        var inputs = new Dictionary<string, decimal>
        {
            ["normal_tahsilat"] = 1000.50m,
            ["normal_harc"] = 500.25m
        };

        // Act
        snapshot.SetInputs(inputs);
        var result = snapshot.GetInputs();

        // Assert
        // R19: MissingFieldHandler ensures all fields are present, so Count > 2
        Assert.True(result.Count >= 2);
        Assert.Equal(1000.50m, result["normal_tahsilat"]);
        Assert.Equal(500.25m, result["normal_harc"]);
    }

    [Fact]
    public void SetOutputs_And_GetOutputs_Works()
    {
        // Arrange
        var snapshot = new CalculatedKasaSnapshot
        {
            RaporTarihi = DateOnly.FromDateTime(DateTime.Today),
            KasaTuru = KasaRaporTuru.Sabah
        };
        var outputs = new Dictionary<string, decimal>
        {
            ["toplam_tahsilat"] = 5000m,
            ["genel_kasa"] = 4500m
        };

        // Act
        snapshot.SetOutputs(outputs);
        var result = snapshot.GetOutputs();

        // Assert
        // R19: MissingFieldHandler ensures all fields are present
        Assert.True(result.Count >= 2);
        Assert.Equal(5000m, result["toplam_tahsilat"]);
        Assert.Equal(4500m, result["genel_kasa"]);
    }

    [Fact]
    public void GetInputs_With_Null_Json_Returns_All_Defaults()
    {
        // Arrange
        var snapshot = new CalculatedKasaSnapshot
        {
            InputsJson = null!
        };

        // Act
        var result = snapshot.GetInputs();

        // Assert
        // R19: Should return full list of default values (count > 0)
        Assert.NotEmpty(result);
        Assert.Contains("normal_tahsilat", result.Keys); // Sanity check for a known field
    }

    [Fact]
    public void GetOutputs_With_Invalid_Json_Returns_All_Defaults()
    {
        // Arrange
        var snapshot = new CalculatedKasaSnapshot
        {
            OutputsJson = "invalid json {"
        };

        // Act
        var result = snapshot.GetOutputs();

        // Assert
        // R19: Should fallback to defaults on error
        Assert.NotEmpty(result);
    }

    [Fact]
    public void New_Snapshot_Has_Default_Values()
    {
        // Act
        var snapshot = new CalculatedKasaSnapshot();

        // Assert
        Assert.NotEqual(Guid.Empty, snapshot.Id);
        Assert.Equal(1, snapshot.Version);
        Assert.True(snapshot.IsActive);
        Assert.False(snapshot.IsDeleted);
    }

    [Fact]
    public void GetDisplaySummary_Returns_NonEmpty_String()
    {
        // Arrange
        var snapshot = new CalculatedKasaSnapshot
        {
            RaporTarihi = DateOnly.FromDateTime(DateTime.Today),
            KasaTuru = KasaRaporTuru.Genel,
            Version = 2
        };
        snapshot.SetOutputs(new Dictionary<string, decimal>
        {
            ["toplam"] = 10000m,
            ["net"] = 9500m
        });

        // Act
        var summary = snapshot.GetDisplaySummary();

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(summary));
        // Summary formatı: "Genel - dd.MM.yyyy vX - Key: Value"
        Assert.Contains("Genel", summary);
        Assert.Contains("v2", summary);
    }

    [Fact]
    public void KasaRaporTuru_Ortak_Has_Value_4()
    {
        // Assert
        Assert.Equal(4, (int)KasaRaporTuru.Ortak);
    }

    [Theory]
    [InlineData(KasaRaporTuru.Sabah, 1)]   // Sabah = 1
    [InlineData(KasaRaporTuru.Aksam, 2)]   // Aksam = 2
    [InlineData(KasaRaporTuru.Genel, 3)]   // Genel = 3
    [InlineData(KasaRaporTuru.Ortak, 4)]   // Ortak = 4
    public void KasaRaporTuru_Has_Expected_Values(KasaRaporTuru turu, int expected)
    {
        Assert.Equal(expected, (int)turu);
    }

    [Fact]
    public void Snapshot_With_Notes_Persists()
    {
        // Arrange
        var snapshot = new CalculatedKasaSnapshot
        {
            RaporTarihi = DateOnly.FromDateTime(DateTime.Today),
            KasaTuru = KasaRaporTuru.Aksam,
            Notes = "Test notu"
        };

        // Assert
        Assert.Equal("Test notu", snapshot.Notes);
    }

    [Fact]
    public void Snapshot_Supports_FormulaSet_Info()
    {
        // Arrange
        var setId = Guid.NewGuid();
        var snapshot = new CalculatedKasaSnapshot
        {
            RaporTarihi = DateOnly.FromDateTime(DateTime.Today),
            KasaTuru = KasaRaporTuru.Aksam,
            FormulaSetId = setId,
            FormulaSetName = "Akşam Kasa Default"
        };

        // Assert
        Assert.Equal(setId, snapshot.FormulaSetId);
        Assert.Equal("Akşam Kasa Default", snapshot.FormulaSetName);
    }
}
