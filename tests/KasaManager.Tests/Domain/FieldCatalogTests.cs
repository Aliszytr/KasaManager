using KasaManager.Domain.FormulaEngine;
using Xunit;

namespace KasaManager.Tests.Domain;

/// <summary>
/// R17: FieldCatalog unit testleri.
/// </summary>
public class FieldCatalogTests
{
    [Fact]
    public void All_Returns_AllFields()
    {
        // Act
        var all = FieldCatalog.All;

        // Assert
        Assert.NotEmpty(all);
        Assert.True(all.Count >= 60, $"En az 60 alan olmalı, bulunan: {all.Count}");
    }

    [Fact]
    public void GetGroupedByCategory_Returns_NonEmptyCategories()
    {
        // Act
        var groups = FieldCatalog.GetGroupedByCategory();

        // Assert
        Assert.NotEmpty(groups);
        
        foreach (var group in groups)
        {
            Assert.False(string.IsNullOrWhiteSpace(group.Key), "Kategori adı boş olmamalı");
            Assert.NotEmpty(group.ToList());
        }
    }

    [Fact]
    public void Get_Returns_CorrectEntry()
    {
        // Act
        var entry = FieldCatalog.Get("normal_tahsilat");

        // Assert
        Assert.NotNull(entry);
        Assert.Equal("Normal Tahsilat", entry.DisplayName);
        Assert.Equal("Tahsilat", entry.Category);
    }

    [Fact]
    public void Get_Returns_Null_ForInvalidKey()
    {
        // Act
        var entry = FieldCatalog.Get("nonexistent_key_12345");

        // Assert
        Assert.Null(entry);
    }

    [Fact]
    public void GetDefaultsFor_Aksam_Returns_ExpectedFields()
    {
        // Act
        var defaults = FieldCatalog.GetDefaultsFor("Aksam").ToList();

        // Assert
        Assert.NotEmpty(defaults);
        Assert.All(defaults, f => Assert.Contains("Aksam", f.DefaultVisibleIn));
    }

    [Fact]
    public void GetDefaultsFor_Genel_Returns_ExpectedFields()
    {
        // Act
        var defaults = FieldCatalog.GetDefaultsFor("Genel").ToList();

        // Assert
        Assert.NotEmpty(defaults);
    }

    [Fact]
    public void AllEntries_Have_NonEmptyKey()
    {
        // Act
        var all = FieldCatalog.All;

        // Assert
        Assert.All(all, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Key), "Key boş olmamalı");
        });
    }

    [Fact]
    public void AllEntries_Have_NonEmptyDisplayName()
    {
        // Act
        var all = FieldCatalog.All;

        // Assert
        Assert.All(all, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.DisplayName), $"DisplayName boş olmamalı: {entry.Key}");
        });
    }

    [Fact]
    public void AllEntries_Have_Category()
    {
        // Act
        var all = FieldCatalog.All;

        // Assert
        Assert.All(all, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Category), $"Category boş olmamalı: {entry.Key}");
        });
    }

    [Fact]
    public void GetGroupedByCategory_Contains_CoreCategories()
    {
        // Arrange
        var expectedCategories = new[] { "Tahsilat", "Harç", "Reddiyat", "Banka", "Kasa" };

        // Act
        var groups = FieldCatalog.GetGroupedByCategory();
        var categoryNames = groups.Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Assert
        foreach (var cat in expectedCategories)
        {
            Assert.True(categoryNames.Contains(cat), $"'{cat}' kategorisi bulunamadı");
        }
    }

    [Fact]
    public void Keys_Are_Unique()
    {
        // Act
        var all = FieldCatalog.All;
        var keys = all.Select(e => e.Key).ToList();
        var uniqueKeys = keys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // Assert
        Assert.Equal(uniqueKeys.Count, keys.Count);
    }

    [Fact]
    public void GetByCategory_Returns_CorrectItems()
    {
        // Act
        var tahsilatItems = FieldCatalog.GetByCategory("Tahsilat").ToList();

        // Assert
        Assert.NotEmpty(tahsilatItems);
        Assert.All(tahsilatItems, item => Assert.Equal("Tahsilat", item.Category));
    }

    [Fact]
    public void GetCategories_Returns_ExpectedCategories()
    {
        // Act
        var categories = FieldCatalog.GetCategories().ToList();

        // Assert
        Assert.NotEmpty(categories);
        Assert.Contains("Tahsilat", categories);
        Assert.Contains("Banka", categories);
        Assert.Contains("Kasa", categories);
    }
    
    [Fact]
    public void GetGroupedBySource_Returns_ThreeGroups()
    {
        // Act
        var groups = FieldCatalog.GetGroupedBySource().ToList();

        // Assert - Excel, UserInput, Calculated olmak üzere 3 grup olmalı
        Assert.Equal(3, groups.Count);
        
        // Her grubun alanları olmalı  
        Assert.All(groups, group => Assert.NotEmpty(group.ToList()));
        
        // Grup key'leri doğru olmalı
        var sourceTypes = groups.Select(g => g.Key).ToList();
        Assert.Contains(FieldSource.Excel, sourceTypes);
        Assert.Contains(FieldSource.UserInput, sourceTypes);
        Assert.Contains(FieldSource.Calculated, sourceTypes);
    }
    
    [Fact]
    public void GetGroupedBySource_CalculatedFields_HaveIsReadOnly()
    {
        // Arrange
        var calculatedGroup = FieldCatalog.GetGroupedBySource()
            .FirstOrDefault(g => g.Key == FieldSource.Calculated);
        
        // Act & Assert
        Assert.NotNull(calculatedGroup);
        // Hesaplanan alanların çoğu IsReadOnly=true olmalı
        var readOnlyCount = calculatedGroup.Count(f => f.IsReadOnly);
        Assert.True(readOnlyCount > 0, "Hesaplanan alanların en az birinde IsReadOnly=true olmalı");
    }
}

